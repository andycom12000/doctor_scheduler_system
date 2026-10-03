using Scheduler.Domain.Constraints;
using Scheduler.Domain.Model;
using Scheduler.Domain.Scheduling;

namespace Scheduler.Domain.Validation;

/// <summary>
/// 領域的違規檢查器：逐條解讀 <see cref="ConstraintSettings"/>，對一份既有值班表產出 <see cref="Violation"/>。
/// 這是 ADR-0002 說的「兩份實作之一」——另一份是 Solver 的 CP-SAT 建模器，讀同一份定義。
///
/// 每個原語一個方法，方法裡只有該原語的語義，沒有任何身分代碼的特例：
/// NP 的四條規則全靠 <see cref="ConstraintScope"/> 命中或豁免。
///
/// cellKey 的慣例：逐格的規則（覆蓋、資格、不可排班日、偏好）指向 <c>area:</c> 格；
/// 逐人的序列／累計規則（額度、值休休、連續）指向 <c>staff:</c> 格。
/// Fairness 與 Consistency 是分數，不產生違規（見 <see cref="ScheduleScores"/>）。
/// 另有一條結構規則 X1（同人同日兩區），不是原語、不能停用，見 <see cref="StructuralRules"/>。
/// </summary>
public sealed class ViolationChecker
{
    private readonly SchedulingContext _ctx;
    private readonly MetricEvaluator _metrics;

    public ViolationChecker(SchedulingContext ctx)
    {
        _ctx = ctx;
        _metrics = new MetricEvaluator(ctx);
    }

    public ValidationResult Check(ConstraintSettings settings)
    {
        _ctx.EnsureConsistent();
        var violations = settings.Active
            .SelectMany(Evaluate)
            .Concat(StaffDoubleBooked())
            .OrderBy(v => v.Severity)
            .ThenBy(v => v.Code, StringComparer.Ordinal)
            .ThenBy(v => v.CellKeys[0], StringComparer.Ordinal)
            .ToArray();
        return new ValidationResult(violations);
    }

    public IEnumerable<Violation> Evaluate(ConstraintDefinition c) => c.Primitive switch
    {
        Primitive.ExactCount => ExactCount(c),
        Primitive.Eligible => Eligible(c),
        Primitive.Budget => Budget(c),
        Primitive.MinGap => MinGap(c),
        Primitive.MaxConsecutive => MaxConsecutive(c),
        Primitive.Forbidden => Forbidden(c),
        Primitive.Preference => Preference(c),
        // 組層級／人層級的分數，指不到格子，不算違規
        Primitive.Fairness or Primitive.Consistency => Enumerable.Empty<Violation>(),
        _ => throw new ArgumentOutOfRangeException(nameof(c), c.Primitive, "未知的原語"),
    };

    // ---- 結構規則（不是原語，不看 ConstraintSettings） ----

    /// <summary>
    /// X1：同一人同一天排在兩個以上區域。序列原語以日期為單位、先 Distinct，同日兩格會合併，任何身分都抓不到，所以獨立成結構規則。
    /// 一個（人，日）一筆違規，cellKeys 是他當天所在的每一個 <c>area:</c> 格，前端兩格都能標示。
    /// 寫入端不擋（排班者多步調整的中間狀態，#68），發布與匯出時才擋（前端另擋列印）。
    /// </summary>
    private IEnumerable<Violation> StaffDoubleBooked()
    {
        foreach (var group in _ctx.Duties.GroupBy(d => (d.StaffId, d.Date)).Where(g => g.Count() > 1))
        {
            var staff = _ctx.StaffOf(group.Key.StaffId);
            var areas = string.Join("、", group.Select(d => _ctx.AreaOf(d).Name).Order(StringComparer.Ordinal));
            yield return Violation.CreateStructural(
                StructuralRules.StaffDoubleBooked,
                Severity.Hard,
                group.Select(d => CellKey.Area(d)),
                $"{staff.Name} {group.Key.Date:M/d} 同時排在 {areas}");
        }
    }

    // ---- 逐格的規則 ----

    private IEnumerable<Violation> ExactCount(ConstraintDefinition c)
    {
        var byCell = _ctx.Duties.ToLookup(d => (d.AreaId, d.Date));
        foreach (var area in _ctx.Areas.Where(a => c.Scope.AppliesToAreaType(a.AreaTypeCode)))
        {
            foreach (var date in _ctx.Month.Days().Where(d => c.Scope.AppliesToDay(_ctx.Calendar[d])))
            {
                var actual = byCell[(area.Id, date)].Count();
                if (actual == area.RequiredPerDay)
                {
                    continue;
                }

                var message = actual == 0
                    ? $"{area.Name} {date:M/d} 無人值班"
                    : $"{area.Name} {date:M/d} 需 {area.RequiredPerDay} 人，實排 {actual} 人";
                yield return Violation.Create(c, new[] { CellKey.Area(area.Id, date) }, message);
            }
        }
    }

    private IEnumerable<Violation> Eligible(ConstraintDefinition c)
    {
        foreach (var duty in InScope(c))
        {
            var staff = _ctx.StaffOf(duty.StaffId);
            var area = _ctx.AreaOf(duty);
            if (!_ctx.Eligibility.IsEligible(staff.RankCode, area.AreaTypeCode))
            {
                yield return Violation.Create(c, new[] { CellKey.Area(duty) },
                    $"{staff.Name}（{staff.RankCode}）沒有值 {area.Name}（{area.AreaTypeCode}）的資格");
            }
        }
    }

    private IEnumerable<Violation> Forbidden(ConstraintDefinition c)
    {
        foreach (var duty in InScope(c))
        {
            if (_ctx.IsBlocked(duty.StaffId, duty.Date))
            {
                var staff = _ctx.StaffOf(duty.StaffId);
                yield return Violation.Create(c, new[] { CellKey.Area(duty) },
                    $"{staff.Name} 已登記 {duty.Date:M/d} 為不可排班日");
            }
        }
    }

    private IEnumerable<Violation> Preference(ConstraintDefinition c)
    {
        var direction = c.Params.Direction
            ?? throw new InvalidOperationException($"{c.Code}：Preference 原語必須指定 params.direction");

        foreach (var duty in _ctx.Duties.Where(d => c.Scope.AppliesToRank(_ctx.RankOfStaff(d.StaffId).Code)))
        {
            var area = _ctx.AreaOf(duty);
            var matchesTarget = c.Scope.AppliesToAreaType(area.AreaTypeCode) && c.Scope.AppliesToDay(_ctx.DayOf(duty));
            var violated = direction == PreferenceDirection.Prefer ? !matchesTarget : matchesTarget;
            if (!violated)
            {
                continue;
            }

            var staff = _ctx.StaffOf(duty.StaffId);
            var message = direction == PreferenceDirection.Prefer
                ? $"{staff.Name}（{staff.RankCode}）優先值 {DescribeTarget(c.Scope)}，此格為 {area.Name} {duty.Date:M/d}"
                : $"{staff.Name}（{staff.RankCode}）應盡量避開{DescribeTarget(c.Scope)}值班：{area.Name} {duty.Date:M/d}";
            yield return Violation.Create(c, new[] { CellKey.Area(duty) }, message);
        }
    }

    // ---- 逐人的累計／序列規則 ----

    private IEnumerable<Violation> Budget(ConstraintDefinition c)
    {
        var metric = c.Metric ?? throw new InvalidOperationException($"{c.Code}：Budget 原語必須指定 metric");

        foreach (var staff in StaffInScope(c))
        {
            var rank = _ctx.RankOf(staff.RankCode);
            // params.cap 明說就用它；沒說時只有額度點數有地方可讀（身分上限 + 當月覆寫）。
            // 讀出 null（NP）代表不計，不是 0。
            var cap = c.Params.Cap ?? (metric == Metric.QuotaPoint ? _ctx.QuotaCapOf(rank) : null);
            if (cap is null)
            {
                continue;
            }

            var duties = InScope(c, _ctx.DutiesOf(staff.Id)).OrderBy(d => d.Date).ToArray();
            var running = 0;
            var excess = new List<string>();
            foreach (var duty in duties)
            {
                running += _metrics.ValueOf(metric, duty) ?? 0;
                if (running > cap)
                {
                    excess.Add(CellKey.Staff(duty));
                }
            }

            if (excess.Count > 0)
            {
                yield return Violation.Create(c, excess,
                    $"{staff.Name}（{staff.RankCode}）本月{MetricName(metric)} {running} 超過上限 {cap}");
            }
        }
    }

    private IEnumerable<Violation> MinGap(ConstraintDefinition c)
    {
        var gap = c.Params.Days ?? throw new InvalidOperationException($"{c.Code}：MinGap 原語必須指定 params.days");

        foreach (var staff in StaffInScope(c))
        {
            var dates = DutyDatesAcrossMonths(staff.Id);
            for (var i = 1; i < dates.Count; i++)
            {
                var (prev, next) = (dates[i - 1], dates[i]);
                if (next.DayNumber - prev.DayNumber >= gap)
                {
                    continue;
                }

                // 兩天都在上月是上月的問題；至少一天在本月才指得到格子
                var keys = new[] { prev, next }.Where(_ctx.Month.Contains).Select(d => CellKey.Staff(staff.Id, d)).ToArray();
                if (keys.Length == 0)
                {
                    continue;
                }

                yield return Violation.Create(c, keys,
                    $"{staff.Name} {prev:M/d} 與 {next:M/d} 間隔不足 {gap} 天（{c.Name}）");
            }
        }
    }

    private IEnumerable<Violation> MaxConsecutive(ConstraintDefinition c)
    {
        var limit = c.Params.Days ?? throw new InvalidOperationException($"{c.Code}：MaxConsecutive 原語必須指定 params.days");

        foreach (var staff in StaffInScope(c))
        {
            var dates = DutyDatesAcrossMonths(staff.Id);
            var runStart = 0;
            for (var i = 1; i <= dates.Count; i++)
            {
                var runEnds = i == dates.Count || dates[i].DayNumber != dates[i - 1].DayNumber + 1;
                if (!runEnds)
                {
                    continue;
                }

                var run = dates.Skip(runStart).Take(i - runStart).ToArray();
                runStart = i;
                if (run.Length <= limit)
                {
                    continue;
                }

                // 只標超出上限的那幾天，而且只標本月的
                var keys = run.Skip(limit).Where(_ctx.Month.Contains).Select(d => CellKey.Staff(staff.Id, d)).ToArray();
                if (keys.Length == 0)
                {
                    continue;
                }

                yield return Violation.Create(c, keys,
                    $"{staff.Name} 自 {run[0]:M/d} 起連續值班 {run.Length} 天，超過 {limit} 天");
            }
        }
    }

    // ---- 範圍 ----

    /// <summary>本月值班中，身分、區域類型、日類三個維度都命中的那些。</summary>
    private IEnumerable<Duty> InScope(ConstraintDefinition c) => InScope(c, _ctx.Duties);

    private IEnumerable<Duty> InScope(ConstraintDefinition c, IEnumerable<Duty> duties) =>
        duties.Where(d =>
            c.Scope.AppliesToRank(_ctx.RankOfStaff(d.StaffId).Code) &&
            c.Scope.AppliesToAreaType(_ctx.AreaOf(d).AreaTypeCode) &&
            c.Scope.AppliesToDay(_ctx.DayOf(d)));

    private IEnumerable<Staff> StaffInScope(ConstraintDefinition c) =>
        _ctx.Staff.Where(s => c.Scope.AppliesToRank(s.RankCode));

    /// <summary>
    /// 序列規則（MinGap / MaxConsecutive）看的是「這個人哪幾天有值班」，跨月接上上月月尾。
    /// 區域類型與日類對序列沒有意義，這裡只用身分範圍。
    /// </summary>
    private List<DateOnly> DutyDatesAcrossMonths(string staffId) =>
        _ctx.PreviousMonthDutiesOf(staffId).Concat(_ctx.DutiesOf(staffId))
            .Select(d => d.Date)
            .Distinct()
            .OrderBy(d => d)
            .ToList();

    private static string DescribeTarget(ConstraintScope scope)
    {
        var parts = new List<string>();
        if (scope.AreaTypeCodes is not null)
        {
            parts.Add(string.Join("/", scope.AreaTypeCodes.OrderBy(x => x, StringComparer.Ordinal)));
        }

        if (scope.DayKinds is not null)
        {
            parts.Add(string.Join("/", scope.DayKinds.OrderBy(k => k).Select(DayKindName)));
        }

        return parts.Count == 0 ? "任何" : string.Join(" ", parts);
    }

    private static string DayKindName(DayKind kind) => kind switch
    {
        DayKind.Weekday => "平日",
        DayKind.Holiday => "假日",
        DayKind.PublicHoliday => "國定假日",
        _ => kind.ToString(),
    };

    private static string MetricName(Metric metric) => metric switch
    {
        Metric.QuotaPoint => "額度點數",
        Metric.FairnessPoint => "公平性點數",
        Metric.DutyDay => "值班天數",
        _ => metric.ToString(),
    };
}
