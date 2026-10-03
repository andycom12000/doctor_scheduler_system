using Scheduler.Domain.Model;

namespace Scheduler.Domain.Scheduling;

/// <summary>
/// 檢查一份值班表（或替它求解）所需的全部輸入，收攏在一個物件裡。
/// 領域的違規檢查器與 Solver 的建模器讀的是同一個 context，
/// 原語「讀別的聚合根」（資格矩陣、不可排班日、上限、月結轉）全部從這裡讀。
/// </summary>
/// <param name="PreviousMonthDuties">
/// 上個月月尾的值班，MinGap / MaxConsecutive 跨月時當固定輸入。只需要最後幾天，多給無妨。
/// </param>
/// <param name="CarryOver">上月發布時結算的月結轉，Fairness(quota_point) 的起始偏移。</param>
public sealed record SchedulingContext(
    YearMonth Month,
    Calendar Calendar,
    IReadOnlyList<Area> Areas,
    IReadOnlyList<Staff> Staff,
    IReadOnlyList<Rank> Ranks,
    EligibilityMatrix Eligibility,
    PointRules PointRules,
    MonthlyOverride Override,
    IReadOnlyList<Duty> Duties,
    IReadOnlyList<Duty> PreviousMonthDuties,
    IReadOnlyList<BlockedDay> BlockedDays,
    IReadOnlyList<CarryOverEntry> CarryOver)
{
    private Dictionary<string, Area>? _areasById;
    private Dictionary<string, Staff>? _staffById;
    private Dictionary<string, Rank>? _ranksByCode;
    private ILookup<string, Duty>? _dutiesByStaff;
    private ILookup<string, Duty>? _previousByStaff;
    private HashSet<(string StaffId, DateOnly Date)>? _blocked;
    private Dictionary<string, int>? _carryOverByStaff;

    public Area AreaOf(string areaId) =>
        (_areasById ??= Areas.ToDictionary(a => a.Id)).TryGetValue(areaId, out var a)
            ? a
            : throw new KeyNotFoundException($"沒有 id 為 {areaId} 的區域");

    public Staff StaffOf(string staffId) =>
        (_staffById ??= Staff.ToDictionary(s => s.Id)).TryGetValue(staffId, out var s)
            ? s
            : throw new KeyNotFoundException($"沒有 id 為 {staffId} 的人員");

    public Rank RankOf(string rankCode) =>
        (_ranksByCode ??= Ranks.ToDictionary(r => r.Code)).TryGetValue(rankCode, out var r)
            ? r
            : throw new KeyNotFoundException($"沒有代碼為 {rankCode} 的身分");

    public Rank RankOfStaff(string staffId) => RankOf(StaffOf(staffId).RankCode);

    public Area AreaOf(Duty duty) => AreaOf(duty.AreaId);

    public CalendarDay DayOf(Duty duty) => Calendar[duty.Date];

    /// <summary>本月某人的值班。</summary>
    public IEnumerable<Duty> DutiesOf(string staffId) =>
        (_dutiesByStaff ??= Duties.ToLookup(d => d.StaffId))[staffId];

    /// <summary>上月月尾某人的值班（固定輸入）。</summary>
    public IEnumerable<Duty> PreviousMonthDutiesOf(string staffId) =>
        (_previousByStaff ??= PreviousMonthDuties.ToLookup(d => d.StaffId))[staffId];

    public bool IsBlocked(string staffId, DateOnly date) =>
        (_blocked ??= BlockedDays.Select(b => (b.StaffId, b.Date)).ToHashSet()).Contains((staffId, date));

    public int CarryOverOf(string staffId) =>
        (_carryOverByStaff ??= CarryOver.ToDictionary(c => c.StaffId, c => c.Points)).GetValueOrDefault(staffId);

    /// <summary>某身分當月的額度上限：先看逐月覆寫，再看身分預設。NP 為 null。</summary>
    public int? QuotaCapOf(Rank rank) =>
        Override.QuotaCapByRank.TryGetValue(rank.Code, out var overridden) ? overridden : rank.QuotaCap;

    /// <summary>
    /// 確認 context 自洽：行事曆涵蓋整個月與次月第一天（公平性點數查「隔日」需要）、
    /// 值班都落在本月、引用的區域／人員／身分都存在。壞資料在這裡失敗，比在檢查器深處失敗好找。
    /// </summary>
    public void EnsureConsistent()
    {
        foreach (var date in Month.Days().Append(Month.Next.FirstDay))
        {
            if (!Calendar.Covers(date))
            {
                throw new InvalidOperationException($"行事曆未涵蓋 {date:yyyy-MM-dd}");
            }
        }

        foreach (var duty in Duties)
        {
            if (!Month.Contains(duty.Date))
            {
                throw new InvalidOperationException($"值班 {duty.AreaId}/{duty.Date:yyyy-MM-dd} 不在 {Month} 內");
            }

            _ = AreaOf(duty);
            _ = RankOfStaff(duty.StaffId);
        }

        foreach (var duty in PreviousMonthDuties)
        {
            if (duty.Date >= Month.FirstDay)
            {
                throw new InvalidOperationException($"上月值班 {duty.Date:yyyy-MM-dd} 不在 {Month} 之前");
            }
        }

        var duplicated = Duties.GroupBy(d => (d.AreaId, d.Date)).FirstOrDefault(g => g.Count() > 1);
        if (duplicated is not null)
        {
            throw new InvalidOperationException($"格子 {duplicated.Key.AreaId}/{duplicated.Key.Date:yyyy-MM-dd} 有多筆值班");
        }

        // 同一人同一天排在多區「不」在這裡擲出：它是 ViolationChecker 的結構規則 X1（硬違規），
        // 寫入端不擋、發布與匯出時才擋（前端另擋列印，#68）。若在這裡擲出，存了這種資料後每次讀取都會 500。
    }
}
