using Google.OrTools.Sat;
using Scheduler.Application.Solving;
using Scheduler.Domain.Constraints;
using Scheduler.Domain.Model;
using Scheduler.Domain.Scheduling;

namespace Scheduler.Solver;

/// <summary>
/// 把一份 <see cref="SolveRequest"/> 建成 CP-SAT 模型。這是 ADR-0002 說的「兩份實作」的另一份——
/// 每個原語一個方法，逐條解讀與 <c>ViolationChecker</c> 同一份 <see cref="ConstraintSettings"/>；
/// 範圍（身分、區域類型、日類）用同一組 <see cref="ConstraintScope"/> 方法判，這裡沒有任何身分代碼的特例。
///
/// 與檢查器刻意不同的只有一處：覆蓋（ExactCount）建成極高權重的軟項而不是硬約束（ARCHITECTURE §4.5），
/// 登記過多時回「空缺最少」的解而不是 INFEASIBLE。
///
/// 決策變數 <c>x[s,a,d]</c>：人員 s 在日期 d 值區域 a。只替在職人員建；資格不符（Eligible 啟用時）與
/// 不可排班日（Forbidden 啟用時）的組合直接不建變數。
/// </summary>
internal sealed class ScheduleModel
{
    /// <summary>軟約束權重是小數（使用者設定 × 乘數），CP-SAT 要整數係數：全部乘這個再四捨五入。</summary>
    private const long WeightScale = 100;

    /// <summary>每一格空缺的罰分，必須壓過整個軟目標的總和（軟目標上限約 1e5 × WeightScale）。</summary>
    private const long VacancyPenalty = 1_000_000_000L;

    /// <summary>度量累計值的 IntVar 邊界。額度上限與點數都有 1000 的上界，一個月不可能超過這個數。</summary>
    private const long MetricBound = 100_000;

    private readonly SolveRequest _request;
    private readonly SchedulingContext _ctx;
    private readonly ConstraintSettings _constraints;
    private readonly DateOnly[] _days;
    private readonly Staff[] _staff;
    private readonly Dictionary<(string StaffId, string AreaId, DateOnly Date), BoolVar> _x = new();
    private readonly Dictionary<(string StaffId, DateOnly Date), List<BoolVar>> _byStaffDay = new();
    private readonly Dictionary<(string AreaId, DateOnly Date), List<BoolVar>> _byCell = new();
    private readonly Dictionary<(string StaffId, DateOnly Date), BoolVar> _saturdayPair = new();
    private readonly List<LinearExpr> _objective = new();

    public ScheduleModel(SolveRequest request)
    {
        _request = request;
        _ctx = request.Context;
        _constraints = request.Constraints;
        _days = _ctx.Month.Days().ToArray();
        _staff = _ctx.Staff.Where(s => s.Status == StaffStatus.Active).ToArray();
        Model = new CpModel();

        CreateVariables();
        StructuralInvariants();

        foreach (var c in _constraints.Active)
        {
            switch (c.Primitive)
            {
                case Primitive.ExactCount: ExactCount(c); break;
                case Primitive.Eligible or Primitive.Forbidden: break; // 已在建變數時處理
                case Primitive.Budget: Budget(c); break;
                case Primitive.MinGap: MinGap(c); break;
                case Primitive.MaxConsecutive: MaxConsecutive(c); break;
                case Primitive.Preference: Preference(c); break;
                case Primitive.Fairness: Fairness(c); break;
                case Primitive.Consistency: Consistency(c); break;
                default: throw new ArgumentOutOfRangeException(nameof(request), c.Primitive, "未知的原語");
            }
        }

        Diversity();
        Hints();
        Objective = _objective.Count == 0 ? LinearExpr.Constant(0) : LinearExpr.Sum(_objective);
        Model.Minimize(Objective);
    }

    public CpModel Model { get; }

    /// <summary>目標式本體。<see cref="CpSatSolver"/> 對解直接求值取目標值，不用 <c>CpSolver.ObjectiveValue</c>（理由見那裡）。</summary>
    public LinearExpr Objective { get; }

    public int VariableCount => _x.Count;

    /// <summary>從求解結果讀回值班清單。</summary>
    public IReadOnlyList<Duty> Extract(CpSolver solver) =>
        _x.Where(kv => solver.BooleanValue(kv.Value))
            .Select(kv => new Duty(kv.Key.AreaId, kv.Key.Date, kv.Key.StaffId))
            .OrderBy(d => d.Date).ThenBy(d => d.AreaId, StringComparer.Ordinal)
            .ToArray();

    // ---- 變數 ----

    private void CreateVariables()
    {
        var eligible = _constraints.Active.Where(c => c.Primitive == Primitive.Eligible).ToArray();
        var forbidden = _constraints.Active.Where(c => c.Primitive == Primitive.Forbidden).ToArray();

        foreach (var staff in _staff)
        {
            var rank = _ctx.RankOf(staff.RankCode);
            foreach (var area in _ctx.Areas)
            {
                foreach (var date in _days)
                {
                    var day = _ctx.Calendar[date];
                    // 檢查器對 Eligible / Forbidden 也是先看範圍再看資料本體，這裡一樣：範圍外的組合不擋
                    if (eligible.Any(c => Applies(c, rank, area, day)) && !_ctx.Eligibility.IsEligible(rank.Code, area.AreaTypeCode))
                    {
                        continue;
                    }

                    if (forbidden.Any(c => Applies(c, rank, area, day)) && _ctx.IsBlocked(staff.Id, date))
                    {
                        continue;
                    }

                    var v = Model.NewBoolVar($"x[{staff.Id},{area.Id},{date:yyyyMMdd}]");
                    _x[(staff.Id, area.Id, date)] = v;
                    Add(_byStaffDay, (staff.Id, date), v);
                    Add(_byCell, (area.Id, date), v);
                }
            }
        }
    }

    /// <summary>
    /// 不在 ConstraintSettings 裡、不能停用的結構不變式：同一人同一天最多一格、同一格最多 requiredPerDay 人。
    /// 寫入端用 STAFF_ALREADY_ON_DUTY 擋的就是前者；<c>SchedulingContext.EnsureConsistent</c> 對兩者都會擲出。
    /// </summary>
    private void StructuralInvariants()
    {
        foreach (var vars in _byStaffDay.Values.Where(v => v.Count > 1))
        {
            Model.AddAtMostOne(vars);
        }

        foreach (var area in _ctx.Areas)
        {
            foreach (var date in _days)
            {
                if (_byCell.TryGetValue((area.Id, date), out var vars) && vars.Count > area.RequiredPerDay)
                {
                    Model.Add(LinearExpr.Sum(vars) <= area.RequiredPerDay);
                }
            }
        }
    }

    // ---- 逐格的規則 ----

    /// <summary>覆蓋：軟項，每缺一人罰 <see cref="VacancyPenalty"/>（§4.5）。</summary>
    private void ExactCount(ConstraintDefinition c)
    {
        foreach (var area in _ctx.Areas.Where(a => c.Scope.AppliesToAreaType(a.AreaTypeCode)))
        {
            foreach (var date in _days.Where(d => c.Scope.AppliesToDay(_ctx.Calendar[d])))
            {
                var filled = _byCell.TryGetValue((area.Id, date), out var vars) ? LinearExpr.Sum(vars) : LinearExpr.Constant(0);
                _objective.Add((area.RequiredPerDay - filled) * VacancyPenalty);
            }
        }
    }

    private void Preference(ConstraintDefinition c)
    {
        var direction = c.Params.Direction ?? throw new InvalidOperationException($"{c.Code}：Preference 原語必須指定 params.direction");
        var weight = Coefficient(c);
        if (weight == 0)
        {
            return;
        }

        foreach (var (key, v) in _x)
        {
            var rank = _ctx.RankOfStaff(key.StaffId);
            if (!c.Scope.AppliesToRank(rank.Code))
            {
                continue;
            }

            var area = _ctx.AreaOf(key.AreaId);
            var matchesTarget = c.Scope.AppliesToAreaType(area.AreaTypeCode) && c.Scope.AppliesToDay(_ctx.Calendar[key.Date]);
            var penalized = direction == PreferenceDirection.Prefer ? !matchesTarget : matchesTarget;
            if (penalized)
            {
                _objective.Add(v * weight);
            }
        }
    }

    // ---- 逐人的累計／序列規則 ----

    private void Budget(ConstraintDefinition c)
    {
        var metric = c.Metric ?? throw new InvalidOperationException($"{c.Code}：Budget 原語必須指定 metric");
        foreach (var staff in StaffInScope(c))
        {
            var rank = _ctx.RankOf(staff.RankCode);
            var cap = c.Params.Cap ?? (metric == Metric.QuotaPoint ? _ctx.QuotaCapOf(rank) : null);
            if (cap is null)
            {
                continue;
            }

            var total = MetricTotal(metric, staff, c.Scope);
            if (total is not null)
            {
                Model.Add(total <= cap.Value);
            }
        }
    }

    /// <summary>值休休：任兩次值班相隔至少 days 天 ⇔ 相距小於 days 的任兩天不能都值。上月尾巴是常數。</summary>
    private void MinGap(ConstraintDefinition c)
    {
        var gap = c.Params.Days ?? throw new InvalidOperationException($"{c.Code}：MinGap 原語必須指定 params.days");
        foreach (var staff in StaffInScope(c))
        {
            for (var i = 0; i < _days.Length; i++)
            {
                if (!_byStaffDay.TryGetValue((staff.Id, _days[i]), out var today))
                {
                    continue;
                }

                for (var k = 1; k < gap && i + k < _days.Length; k++)
                {
                    if (_byStaffDay.TryGetValue((staff.Id, _days[i + k]), out var later))
                    {
                        Model.Add(LinearExpr.Sum(today) + LinearExpr.Sum(later) <= 1);
                    }
                }
            }

            foreach (var previous in _ctx.PreviousMonthDutiesOf(staff.Id).Select(d => d.Date).Distinct())
            {
                foreach (var date in _days.Where(d => d.DayNumber - previous.DayNumber < gap))
                {
                    if (_byStaffDay.TryGetValue((staff.Id, date), out var vars))
                    {
                        Model.Add(LinearExpr.Sum(vars) == 0);
                    }
                }
            }
        }
    }

    /// <summary>連續上限：任何 limit+1 天的視窗內值班天數 ≤ limit。視窗滑過上月尾巴時那幾天是常數。</summary>
    private void MaxConsecutive(ConstraintDefinition c)
    {
        var limit = c.Params.Days ?? throw new InvalidOperationException($"{c.Code}：MaxConsecutive 原語必須指定 params.days");
        foreach (var staff in StaffInScope(c))
        {
            var previous = _ctx.PreviousMonthDutiesOf(staff.Id).Select(d => d.Date).ToHashSet();
            var first = _ctx.Month.FirstDay;
            for (var start = first.AddDays(-limit); start <= _ctx.Month.LastDay.AddDays(-limit); start = start.AddDays(1))
            {
                var terms = new List<LinearExpr>();
                var constant = 0;
                for (var k = 0; k <= limit; k++)
                {
                    var date = start.AddDays(k);
                    if (date < first)
                    {
                        constant += previous.Contains(date) ? 1 : 0;
                    }
                    else if (_byStaffDay.TryGetValue((staff.Id, date), out var vars))
                    {
                        terms.AddRange(vars);
                    }
                }

                if (terms.Count > 0 && terms.Count + constant > limit)
                {
                    Model.Add(LinearExpr.Sum(terms) <= limit - constant);
                }
            }
        }
    }

    /// <summary>公平：組內 max − min。quota_point 比剩餘額度（上限 − 已排 − 月結轉）；算不出度量的人（NP）不進比較。</summary>
    private void Fairness(ConstraintDefinition c)
    {
        var metric = c.Metric ?? throw new InvalidOperationException($"{c.Code}：Fairness 原語必須指定 metric");
        var weight = Coefficient(c);
        if (weight == 0)
        {
            return;
        }

        foreach (var group in StaffInScope(c).GroupBy(s => _ctx.RankOf(s.RankCode).GroupCode))
        {
            var values = new List<LinearExpr>();
            foreach (var staff in group)
            {
                var value = metric == Metric.QuotaPoint ? QuotaRemaining(staff, c.Scope) : MetricTotal(metric, staff, c.Scope);
                if (value is null)
                {
                    continue;
                }

                var holder = Model.NewIntVar(-MetricBound, MetricBound, $"{metric}[{staff.Id}]");
                Model.Add(holder == value);
                values.Add(holder);
            }

            if (values.Count < 2)
            {
                continue;
            }

            var max = Model.NewIntVar(-MetricBound, MetricBound, $"max[{group.Key}]");
            var min = Model.NewIntVar(-MetricBound, MetricBound, $"min[{group.Key}]");
            Model.AddMaxEquality(max, values);
            Model.AddMinEquality(min, values);
            _objective.Add((max - min) * weight);
        }
    }

    /// <summary>同區延續：每人「值班數 − 最常值的那一區的值班數」。</summary>
    private void Consistency(ConstraintDefinition c)
    {
        var weight = Coefficient(c);
        if (weight == 0)
        {
            return;
        }

        foreach (var staff in StaffInScope(c))
        {
            var perArea = _ctx.Areas
                .Select(a => _days.Where(d => _x.ContainsKey((staff.Id, a.Id, d))).Select(d => (LinearExpr)_x[(staff.Id, a.Id, d)]).ToArray())
                .Where(vars => vars.Length > 0)
                .Select(vars => LinearExpr.Sum(vars))
                .ToArray();
            if (perArea.Length < 2)
            {
                continue;
            }

            var most = Model.NewIntVar(0, _days.Length, $"most[{staff.Id}]");
            Model.AddMaxEquality(most, perArea);
            _objective.Add((LinearExpr.Sum(perArea) - most) * weight);
        }
    }

    // ---- 多樣性與提示 ----

    /// <summary>與前幾份每一份至少差 MinDifferentCells 格：一格「不同」指排的人不同，空↔有人也算。</summary>
    private void Diversity()
    {
        var cellCount = _ctx.Areas.Count * _days.Length;
        var required = Math.Min(_request.MinDifferentCells, cellCount);
        if (required <= 0)
        {
            return;
        }

        foreach (var solution in _request.AvoidSolutions)
        {
            var assigned = solution.ToDictionary(d => (d.AreaId, d.Date), d => d.StaffId);
            var differs = new List<LinearExpr>();
            foreach (var area in _ctx.Areas)
            {
                foreach (var date in _days)
                {
                    if (assigned.TryGetValue((area.Id, date), out var staffId))
                    {
                        differs.Add(_x.TryGetValue((staffId, area.Id, date), out var v) ? 1 - v : LinearExpr.Constant(1));
                    }
                    else if (_byCell.TryGetValue((area.Id, date), out var vars))
                    {
                        differs.Add(LinearExpr.Sum(vars));
                    }
                }
            }

            Model.Add(LinearExpr.Sum(differs) >= required);
        }
    }

    /// <summary>既有草稿當提示，讓搜尋從排班者已經排好的樣子出發。只是提示，不是固定值。</summary>
    private void Hints()
    {
        foreach (var duty in _ctx.Duties)
        {
            if (_x.TryGetValue((duty.StaffId, duty.AreaId, duty.Date), out var v))
            {
                Model.AddHint(v, 1);
            }
        }
    }

    // ---- 度量 ----

    /// <summary>剩餘額度 = 上限 − Σ 額度點數·x − 月結轉偏移；上限 null（NP）回 null。</summary>
    private LinearExpr? QuotaRemaining(Staff staff, ConstraintScope scope)
    {
        var cap = _ctx.QuotaCapOf(_ctx.RankOf(staff.RankCode));
        if (cap is null)
        {
            return null;
        }

        var spent = MetricTotal(Metric.QuotaPoint, staff, scope) ?? LinearExpr.Constant(0);
        return cap.Value - spent - _ctx.CarryOverOf(staff.Id);
    }

    /// <summary>
    /// 某人本月在某度量下的累計，只計入範圍（區域類型、日類）內的格子。
    /// 公平性點數的連值週六加分附在「那個週六」上而不是某一格：範圍有區域類型或日類限制時加分不分格計，
    /// 與檢查器逐格計的方式有微小差異；出廠規則裡 fairness_point 只有身分範圍，碰不到這個差異。
    /// </summary>
    private LinearExpr? MetricTotal(Metric metric, Staff staff, ConstraintScope scope)
    {
        var pointType = _ctx.RankOf(staff.RankCode).PointType;
        if (metric == Metric.FairnessPoint && pointType is null)
        {
            return null;
        }

        var terms = new List<LinearExpr>();
        foreach (var area in _ctx.Areas.Where(a => scope.AppliesToAreaType(a.AreaTypeCode)))
        {
            foreach (var date in _days)
            {
                var day = _ctx.Calendar[date];
                if (!scope.AppliesToDay(day) || !_x.TryGetValue((staff.Id, area.Id, date), out var v))
                {
                    continue;
                }

                var value = metric switch
                {
                    Metric.QuotaPoint => _ctx.PointRules.Quota.ValueOf(day),
                    Metric.DutyDay => 1,
                    Metric.FairnessPoint => _ctx.PointRules.Fairness.Lookup(pointType!.Value, day, _ctx.Calendar[date.AddDays(1)]),
                    _ => throw new ArgumentOutOfRangeException(nameof(metric), metric, null),
                };
                terms.Add(v * value);
            }
        }

        if (metric == Metric.FairnessPoint)
        {
            terms.AddRange(SaturdayBonusTerms(staff));
        }

        return terms.Count == 0 ? LinearExpr.Constant(0) : LinearExpr.Sum(terms);
    }

    /// <summary>
    /// 唯一決策相依的點數項（§4.3）：週六值班、往後 windowDays 天內沒有國定假日、且下週六也值班 → +points。
    /// <c>b = y[週六] ∧ y[下週六]</c>，兩個 y 都是「當天有沒有班」的和（≤ 1）。下週六必須在本月，與檢查器只看本月一致。
    /// </summary>
    private IEnumerable<LinearExpr> SaturdayBonusTerms(Staff staff)
    {
        var bonus = _ctx.PointRules.Fairness.ConsecutiveSaturdayBonus;
        if (bonus.Points == 0)
        {
            yield break;
        }

        foreach (var date in _days.Where(d => d.DayOfWeek == DayOfWeek.Saturday && _ctx.Month.Contains(d.AddDays(7))))
        {
            var window = Enumerable.Range(0, bonus.WindowDays).Select(i => date.AddDays(i));
            if (window.Any(d => _ctx.Calendar.Covers(d) && _ctx.Calendar[d].IsPublicHoliday))
            {
                continue;
            }

            if (!_byStaffDay.TryGetValue((staff.Id, date), out var today) || !_byStaffDay.TryGetValue((staff.Id, date.AddDays(7)), out var next))
            {
                continue;
            }

            if (!_saturdayPair.TryGetValue((staff.Id, date), out var pair))
            {
                pair = Model.NewBoolVar($"sat[{staff.Id},{date:yyyyMMdd}]");
                var a = LinearExpr.Sum(today);
                var b = LinearExpr.Sum(next);
                Model.Add(pair <= a);
                Model.Add(pair <= b);
                Model.Add(pair >= a + b - 1);
                _saturdayPair[(staff.Id, date)] = pair;
            }

            yield return pair * bonus.Points;
        }
    }

    // ---- helpers ----

    private IEnumerable<Staff> StaffInScope(ConstraintDefinition c) => _staff.Where(s => c.Scope.AppliesToRank(s.RankCode));

    private static bool Applies(ConstraintDefinition c, Rank rank, Area area, CalendarDay day) =>
        c.Scope.AppliesToRank(rank.Code) && c.Scope.AppliesToAreaType(area.AreaTypeCode) && c.Scope.AppliesToDay(day);

    private long Coefficient(ConstraintDefinition c) =>
        (long)Math.Round(_request.EffectiveWeights.GetValueOrDefault(c.Code, 0) * WeightScale);

    private static void Add<TKey>(Dictionary<TKey, List<BoolVar>> index, TKey key, BoolVar v) where TKey : notnull
    {
        if (!index.TryGetValue(key, out var list))
        {
            index[key] = list = new List<BoolVar>();
        }

        list.Add(v);
    }
}
