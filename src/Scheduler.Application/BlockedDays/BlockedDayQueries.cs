using Scheduler.Application.Scheduling;
using Scheduler.Domain.Defaults;
using Scheduler.Domain.Model;

namespace Scheduler.Application.BlockedDays;

/// <summary><c>GET /blocked-days/{ym}</c>。</summary>
public sealed record BlockedDayRegistration(
    YearMonth YearMonth,
    int MonthlyCap,
    IReadOnlyList<BlockedDay> Entries,
    IReadOnlyList<BlockedDayByStaff> ByStaff,
    IReadOnlyList<BlockedDayByDate> ByDate);

public sealed record BlockedDayByStaff(string StaffId, int Count, int Remaining);

public sealed record BlockedDayByDate(DateOnly Date, int Count);

/// <summary><c>GET /blocked-days/{ym}/feasibility</c>。</summary>
public sealed record FeasibilityReport(
    bool Feasible,
    IReadOnlyList<FeasibilityByDate> ByDate,
    IReadOnlyList<FeasibilityTier> BySupply,
    IReadOnlyList<FeasibilityTier> BaselineBySupply,
    IReadOnlyList<string> Warnings);

public sealed record FeasibilityByDate(DateOnly Date, IReadOnlyList<FeasibilityShortage> Shortages);

public sealed record FeasibilityShortage(string AreaTypeCode, int Required, int AvailableStaff);

public sealed record FeasibilityTier(IReadOnlyList<string> AreaTypeCodes, int DemandPoints, int SupplyPoints, int Headroom);

/// <summary>
/// 不可排班日的讀取路徑。兩個查詢都不要求值班表存在（ADR-0001：登記發生在值班表之前）。
/// </summary>
public sealed class BlockedDayQueries
{
    private readonly SchedulingContextLoader _loader;

    public BlockedDayQueries(SchedulingContextLoader loader)
    {
        _loader = loader;
    }

    public async Task<BlockedDayRegistration> GetRegistrationAsync(YearMonth month, CancellationToken cancellationToken = default)
    {
        var ctx = (await _loader.LoadAsync(month, cancellationToken)).Context;
        var entries = ctx.BlockedDays.OrderBy(b => b.Date).ThenBy(b => b.StaffId, StringComparer.Ordinal).ToArray();
        var countByStaff = entries.GroupBy(b => b.StaffId).ToDictionary(g => g.Key, g => g.Count());
        const int cap = DefaultPointRules.BlockedDayMonthlyCap;

        var byStaff = ctx.Staff
            .Where(s => s.Status == StaffStatus.Active)
            .OrderBy(s => s.EmployeeNo, StringComparer.Ordinal)
            .Select(s =>
            {
                var count = countByStaff.GetValueOrDefault(s.Id);
                return new BlockedDayByStaff(s.Id, count, Math.Max(0, cap - count));
            })
            .ToArray();

        var byDate = entries
            .GroupBy(b => b.Date)
            .OrderBy(g => g.Key)
            .Select(g => new BlockedDayByDate(g.Key, g.Count()))
            .ToArray();

        return new BlockedDayRegistration(month, cap, entries, byStaff, byDate);
    }

    /// <summary>
    /// 可行性預警，兩層都是必要條件（契約說明）：
    /// <list type="bullet">
    /// <item><c>byDate</c>：逐日逐區域類型，扣掉當天登記的人之後，有資格的在職人數 ≥ 需求</item>
    /// <item><c>bySupply</c>：資格是巢狀的，所以照「有資格的身分數」由窄到寬累計區域類型，
    /// 每一層拿累計需求點數對上「有資格值其中任一種、且有額度上限的人」的供給
    /// （每人 <c>min(額度上限, 未登記日的額度點數總和)</c>）。
    /// 層次從資格矩陣推出來，不寫死 CHIEF／ICU／WARD；沒有額度上限的身分（NP）不算供給，
    /// 用「上限是 null」判斷，不看身分代碼</item>
    /// </list>
    /// <c>baselineBySupply</c> 是同一個月份、同一份名冊與行事曆、但「沒有任何不可排班日登記」的 <c>bySupply</c>，
    /// 給前端判斷登記讓供給比基準少多少（#98）。基準由後端算，名冊組成與國定假日都會影響它。
    /// </summary>
    public async Task<FeasibilityReport> GetFeasibilityAsync(YearMonth month, CancellationToken cancellationToken = default)
    {
        var loaded = await _loader.LoadAsync(month, cancellationToken);
        var ctx = loaded.Context;
        var active = ctx.Staff.Where(s => s.Status == StaffStatus.Active).ToArray();
        var blockedByStaff = ctx.BlockedDays.ToLookup(b => b.StaffId, b => b.Date);
        var requiredByType = ctx.Areas
            .GroupBy(a => a.AreaTypeCode)
            .ToDictionary(g => g.Key, g => g.Sum(a => a.RequiredPerDay));

        var byDate = new List<FeasibilityByDate>();
        foreach (var date in month.Days())
        {
            var shortages = new List<FeasibilityShortage>();
            foreach (var type in loaded.AreaTypes)
            {
                var required = requiredByType.GetValueOrDefault(type.Code);
                var available = active.Count(s =>
                    ctx.Eligibility.IsEligible(s.RankCode, type.Code) && !blockedByStaff[s.Id].Contains(date));
                if (available < required)
                {
                    shortages.Add(new FeasibilityShortage(type.Code, required, available));
                }
            }

            byDate.Add(new FeasibilityByDate(date, shortages));
        }

        var bySupply = SupplyTiers(month, loaded, ctx, requiredByType, active, blockedByStaff);
        var baselineBySupply = SupplyTiers(month, loaded, ctx, requiredByType, active, Enumerable.Empty<BlockedDay>().ToLookup(b => b.StaffId, b => b.Date));

        var feasible = byDate.All(d => d.Shortages.Count == 0) && bySupply.All(t => t.Headroom >= 0);
        return new FeasibilityReport(feasible, byDate, bySupply, baselineBySupply, loaded.Warnings);
    }

    private static List<FeasibilityTier> SupplyTiers(
        YearMonth month,
        LoadedContext loaded,
        Domain.Scheduling.SchedulingContext ctx,
        IReadOnlyDictionary<string, int> requiredByType,
        IReadOnlyList<Staff> active,
        ILookup<string, DateOnly> blockedByStaff)
    {
        var bySupply = new List<FeasibilityTier>();
        var tier = new List<string>();
        foreach (var type in TiersOf(loaded, ctx))
        {
            tier.Add(type);
            var demand = month.Days().Sum(d => tier.Sum(t => requiredByType.GetValueOrDefault(t)) * ctx.PointRules.Quota.ValueOf(ctx.Calendar[d]));
            var supply = 0;
            foreach (var s in active)
            {
                var cap = ctx.QuotaCapOf(ctx.RankOf(s.RankCode));
                if (cap is null || !tier.Any(t => ctx.Eligibility.IsEligible(s.RankCode, t)))
                {
                    continue;
                }

                // 登記日不消耗額度：一個人能供給的是「上限」與「沒登記的日子加起來的點數」取小。
                // 從上限裡扣登記日的點數是單位不對的減法，正常登記量就會整片報無解。
                var blocked = blockedByStaff[s.Id].ToHashSet();
                var availablePoints = month.Days().Where(d => !blocked.Contains(d)).Sum(d => ctx.PointRules.Quota.ValueOf(ctx.Calendar[d]));
                supply += Math.Min(cap.Value, availablePoints);
            }

            bySupply.Add(new FeasibilityTier(tier.ToArray(), demand, supply, supply - demand));
        }

        return bySupply;
    }

    /// <summary>區域類型由資格最窄（能值的身分最少）到最寬排，同寬時照設定順序。</summary>
    internal static IReadOnlyList<string> TiersOf(LoadedContext loaded, Domain.Scheduling.SchedulingContext ctx) =>
        loaded.AreaTypes
            .Select((t, index) => (t.Code, Width: ctx.Ranks.Count(r => ctx.Eligibility.IsEligible(r.Code, t.Code)), index))
            .OrderBy(x => x.Width).ThenBy(x => x.index)
            .Select(x => x.Code)
            .ToArray();
}
