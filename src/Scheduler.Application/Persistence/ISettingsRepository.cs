using Scheduler.Application.Settings;
using Scheduler.Domain.Constraints;
using Scheduler.Domain.Model;

namespace Scheduler.Application.Persistence;

/// <summary>
/// 五份設定文件與逐月覆寫。每份都是「整份讀、整份取代」，對應契約的 GET／PUT。
/// 「被引用時不可刪」的檢查（AREA_IN_USE 等）在 Application，這裡只負責存取。
/// </summary>
public interface ISettingsRepository
{
    Task<AreaSettings> GetAreasAsync(CancellationToken cancellationToken = default);

    Task ReplaceAreasAsync(AreaSettings settings, CancellationToken cancellationToken = default);

    Task<RankSettings> GetRanksAsync(CancellationToken cancellationToken = default);

    Task ReplaceRanksAsync(RankSettings settings, CancellationToken cancellationToken = default);

    Task<EligibilityMatrix> GetEligibilityAsync(CancellationToken cancellationToken = default);

    Task ReplaceEligibilityAsync(EligibilityMatrix matrix, CancellationToken cancellationToken = default);

    Task<PointRules> GetPointRulesAsync(CancellationToken cancellationToken = default);

    Task ReplacePointRulesAsync(PointRules rules, CancellationToken cancellationToken = default);

    Task<ConstraintSettings> GetConstraintsAsync(CancellationToken cancellationToken = default);

    Task ReplaceConstraintsAsync(ConstraintSettings settings, CancellationToken cancellationToken = default);

    /// <summary>某月的逐月覆寫。沒設過時回 <see cref="MonthlyOverride.Empty"/>。</summary>
    Task<MonthlyOverride> GetMonthlyOverrideAsync(YearMonth yearMonth, CancellationToken cancellationToken = default);

    Task ReplaceMonthlyOverrideAsync(MonthlyOverride monthlyOverride, CancellationToken cancellationToken = default);
}
