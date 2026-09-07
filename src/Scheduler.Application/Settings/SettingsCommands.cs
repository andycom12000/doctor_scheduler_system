using Scheduler.Application.Errors;
using Scheduler.Application.Persistence;
using Scheduler.Domain.Constraints;
using Scheduler.Domain.Model;

namespace Scheduler.Application.Settings;

/// <summary>
/// 五份設定文件與逐月覆寫的「整份取代」。兩件事在這裡：
/// <list type="bullet">
/// <item>「被引用時不可刪」（契約的 409：<c>AREA_IN_USE</c>、<c>AREA_TYPE_IN_USE</c>、<c>RANK_IN_USE</c>）</item>
/// <item>讀取路徑對設定形狀的假設（出廠 seed 天生滿足的）在這裡守住，否則一次 PUT 就能讓之後每個 GET 都 500：
/// 主鍵重複、指到不存在的類型／組、Fairness／Budget 沒給 metric、MinGap／MaxConsecutive 沒給天數、
/// 公平性查表缺列、範圍給空陣列（Persistence 拒收，正規化成「不限」）。這些是 422</item>
/// </list>
/// </summary>
public sealed class SettingsCommands
{
    private readonly ISettingsRepository _settings;
    private readonly IScheduleRepository _schedules;
    private readonly IStaffRepository _staff;
    private readonly IUnitOfWork _unitOfWork;

    public SettingsCommands(ISettingsRepository settings, IScheduleRepository schedules, IStaffRepository staff, IUnitOfWork unitOfWork)
    {
        _settings = settings;
        _schedules = schedules;
        _staff = staff;
        _unitOfWork = unitOfWork;
    }

    public async Task<AreaSettings> ReplaceAreasAsync(AreaSettings settings, CancellationToken cancellationToken = default)
    {
        EnsureUnique(settings.AreaTypes.Select(t => t.Code), "區域類型代碼");
        EnsureUnique(settings.Areas.Select(a => a.Id), "區域 id");
        EnsureUnique(settings.Areas.Select(a => a.Code), "區域代碼");
        var typeCodes = settings.AreaTypes.Select(t => t.Code).ToHashSet(StringComparer.Ordinal);
        var current = await _settings.GetAreasAsync(cancellationToken);

        // 先問「被刪的類型是否還有區域指著」（409），再問「指到的類型存不存在」（422），
        // 否則「刪掉類型但區域沒改」這種最常見的錯會被當成格式錯誤。
        foreach (var removedType in current.AreaTypes.Where(t => !typeCodes.Contains(t.Code)))
        {
            if (settings.Areas.Any(a => a.AreaTypeCode == removedType.Code))
            {
                throw new SchedulerException(ErrorCode.AreaTypeInUse, $"區域類型 {removedType.Name} 仍被區域引用，不可刪除",
                    new Dictionary<string, object?> { ["areaTypeCode"] = removedType.Code });
            }
        }

        foreach (var area in settings.Areas)
        {
            if (!typeCodes.Contains(area.AreaTypeCode))
            {
                throw Invalid($"區域 {area.Code} 指到不存在的區域類型 {area.AreaTypeCode}");
            }

            if (area.RequiredPerDay < 1)
            {
                throw Invalid($"區域 {area.Code} 的每日需求人數必須至少 1");
            }
        }

        var keptAreaIds = settings.Areas.Select(a => a.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var removed in current.Areas.Where(a => !keptAreaIds.Contains(a.Id)))
        {
            if (await _schedules.AnyDutyForAreaAsync(removed.Id, cancellationToken))
            {
                throw new SchedulerException(ErrorCode.AreaInUse, $"區域 {removed.Name} 仍被值班表引用，不可刪除",
                    new Dictionary<string, object?> { ["areaId"] = removed.Id });
            }
        }

        await _settings.ReplaceAreasAsync(settings, cancellationToken);
        await _unitOfWork.CommitAsync(cancellationToken);
        return await _settings.GetAreasAsync(cancellationToken);
    }

    public async Task<RankSettings> ReplaceRanksAsync(RankSettings settings, CancellationToken cancellationToken = default)
    {
        EnsureUnique(settings.Groups.Select(g => g.Code), "身分組代碼");
        EnsureUnique(settings.Ranks.Select(r => r.Code), "身分代碼");
        var groupCodes = settings.Groups.Select(g => g.Code).ToHashSet(StringComparer.Ordinal);
        var current = await _settings.GetRanksAsync(cancellationToken);

        foreach (var removedGroup in current.Groups.Where(g => !groupCodes.Contains(g.Code)))
        {
            if (settings.Ranks.Any(r => r.GroupCode == removedGroup.Code))
            {
                throw new SchedulerException(ErrorCode.RankInUse, $"身分組 {removedGroup.Name} 仍被身分引用，不可刪除",
                    new Dictionary<string, object?> { ["groupCode"] = removedGroup.Code });
            }
        }

        foreach (var rank in settings.Ranks)
        {
            if (!groupCodes.Contains(rank.GroupCode))
            {
                throw Invalid($"身分 {rank.Code} 指到不存在的身分組 {rank.GroupCode}");
            }

            if (rank.QuotaCap is < 0)
            {
                throw Invalid($"身分 {rank.Code} 的額度上限不得為負");
            }
        }

        var keptRankCodes = settings.Ranks.Select(r => r.Code).ToHashSet(StringComparer.Ordinal);
        var eligibility = await _settings.GetEligibilityAsync(cancellationToken);
        var constraints = await _settings.GetConstraintsAsync(cancellationToken);
        foreach (var removed in current.Ranks.Where(r => !keptRankCodes.Contains(r.Code)))
        {
            var usedBy = await _staff.AnyWithRankAsync(removed.Code, cancellationToken) ? "人員"
                : eligibility.Matrix.ContainsKey(removed.Code) ? "資格矩陣"
                : constraints.All.Any(c => c.Scope.RankCodes?.Contains(removed.Code) == true || c.Scope.ExemptRankCodes?.Contains(removed.Code) == true) ? "約束範圍"
                : null;
            if (usedBy is not null)
            {
                throw new SchedulerException(ErrorCode.RankInUse, $"身分 {removed.Name} 仍被{usedBy}引用，不可刪除",
                    new Dictionary<string, object?> { ["rankCode"] = removed.Code, ["usedBy"] = usedBy });
            }
        }

        await _settings.ReplaceRanksAsync(settings, cancellationToken);
        await _unitOfWork.CommitAsync(cancellationToken);
        return await _settings.GetRanksAsync(cancellationToken);
    }

    public async Task<EligibilityMatrix> ReplaceEligibilityAsync(EligibilityMatrix matrix, CancellationToken cancellationToken = default)
    {
        await _settings.ReplaceEligibilityAsync(matrix, cancellationToken);
        await _unitOfWork.CommitAsync(cancellationToken);
        return await _settings.GetEligibilityAsync(cancellationToken);
    }

    /// <summary>查表要能回答每一種「當日／隔日」組合（<see cref="FairnessPointRule.Lookup"/> 查不到會擲例外），每個點數類型都要有表。</summary>
    public async Task<PointRules> ReplacePointRulesAsync(PointRules rules, CancellationToken cancellationToken = default)
    {
        if (rules.Quota.Weekday < 0 || rules.Quota.Holiday < 0)
        {
            throw Invalid("額度點數不得為負");
        }

        if (rules.Fairness.ConsecutiveSaturdayBonus.WindowDays < 1)
        {
            throw Invalid("連值週六加分的視窗天數必須至少 1");
        }

        var kinds = new[] { DayKind.Weekday, DayKind.Holiday };
        foreach (var pointType in Enum.GetValues<PointType>())
        {
            if (!rules.Fairness.Tables.TryGetValue(pointType, out var table))
            {
                throw Invalid($"公平性點數缺少 Type {pointType} 的查表");
            }

            foreach (var today in kinds)
            {
                foreach (var tomorrow in kinds)
                {
                    var matches = table.Count(e => e.Today == today && e.Tomorrow == tomorrow);
                    if (matches != 1)
                    {
                        throw Invalid($"公平性點數 Type {pointType} 的查表 {today}/{tomorrow} 必須恰好一列，目前 {matches} 列");
                    }
                }
            }
        }

        await _settings.ReplacePointRulesAsync(rules, cancellationToken);
        await _unitOfWork.CommitAsync(cancellationToken);
        return await _settings.GetPointRulesAsync(cancellationToken);
    }

    /// <summary>範圍的空集合正規化成 null（不限），再檢查每個原語必要的參數。</summary>
    public async Task<ConstraintSettings> ReplaceConstraintsAsync(ConstraintSettings settings, CancellationToken cancellationToken = default)
    {
        EnsureUnique(settings.All.Select(c => c.Code), "約束代碼");
        var normalized = new ConstraintSettings(
            settings.Hard.Select(c => Normalize(c, Severity.Hard)).ToArray(),
            settings.Soft.Select(c => Normalize(c, Severity.Soft)).ToArray());

        await _settings.ReplaceConstraintsAsync(normalized, cancellationToken);
        await _unitOfWork.CommitAsync(cancellationToken);
        return await _settings.GetConstraintsAsync(cancellationToken);
    }

    /// <summary>路徑上的月份為準，本體的 <c>yearMonth</c> 不看。</summary>
    public async Task<MonthlyOverride> ReplaceMonthlyOverrideAsync(YearMonth month, IReadOnlyDictionary<string, int> quotaCapByRank, CancellationToken cancellationToken = default)
    {
        var ranks = await _settings.GetRanksAsync(cancellationToken);
        foreach (var (rankCode, cap) in quotaCapByRank)
        {
            if (!ranks.Ranks.Any(r => r.Code == rankCode))
            {
                throw Invalid($"找不到身分 {rankCode}");
            }

            if (cap < 0)
            {
                throw Invalid($"身分 {rankCode} 的額度上限不得為負");
            }
        }

        var value = new MonthlyOverride(month, quotaCapByRank);
        await _settings.ReplaceMonthlyOverrideAsync(value, cancellationToken);
        await _unitOfWork.CommitAsync(cancellationToken);
        return await _settings.GetMonthlyOverrideAsync(month, cancellationToken);
    }

    // ---- helpers ----

    private static ConstraintDefinition Normalize(ConstraintDefinition c, Severity severity)
    {
        if (c.Severity != severity)
        {
            throw Invalid($"約束 {c.Code} 的嚴重度與所在清單不符");
        }

        if (severity == Severity.Soft && c.Weight is < 0 or > ConstraintDefinition.MaxWeight)
        {
            throw Invalid($"約束 {c.Code} 的權重必須在 0–{ConstraintDefinition.MaxWeight}");
        }

        switch (c.Primitive)
        {
            case Primitive.Budget or Primitive.Fairness when c.Metric is null:
                throw Invalid($"約束 {c.Code}：{c.Primitive} 原語必須指定 metric");
            case Primitive.MinGap or Primitive.MaxConsecutive when c.Params.Days is null or < 1:
                throw Invalid($"約束 {c.Code}：{c.Primitive} 原語必須指定 params.days 且至少 1");
            case Primitive.Preference when c.Params.Direction is null:
                throw Invalid($"約束 {c.Code}：Preference 原語必須指定 params.direction");
        }

        var scope = new ConstraintScope(
            NullIfEmpty(c.Scope.RankCodes),
            NullIfEmpty(c.Scope.ExemptRankCodes),
            NullIfEmpty(c.Scope.AreaTypeCodes),
            NullIfEmpty(c.Scope.DayKinds));
        return c with { Scope = scope, Enabled = severity == Severity.Hard ? c.Enabled : c.Weight > 0 };
    }

    private static IReadOnlySet<T>? NullIfEmpty<T>(IReadOnlySet<T>? set) => set is { Count: > 0 } ? set : null;

    private static void EnsureUnique(IEnumerable<string> keys, string what)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in keys)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw Invalid($"{what}不得空白");
            }

            if (!seen.Add(key))
            {
                throw Invalid($"{what}重複：{key}");
            }
        }
    }

    private static SchedulerException Invalid(string message) => new(ErrorCode.InvalidRequest, message);
}
