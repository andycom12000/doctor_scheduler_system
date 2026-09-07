using Scheduler.Domain.Constraints;

namespace Scheduler.Application.Solving;

/// <summary>一個具名立場：id、標籤、說明與軟約束的權重乘數。未列出的代碼乘數為 1。</summary>
public sealed record VariantProfile(string Id, string Label, string Description, IReadOnlyDictionary<string, double> Multipliers);

/// <summary>
/// 三個具名立場（ADR-0003），乘數表抄自 <c>docs/constraint-defaults.md</c>「變體的權重乘數」。
/// 只有三個立場，所以 <c>variantCount</c> 上限 3；要讓排班者自訂立場時把乘數表搬進請求即可。
/// </summary>
public static class VariantProfiles
{
    public const int MaxVariantCount = 3;

    /// <summary>多樣性約束：第 N 份與前面每一份至少差這麼多格（約 10%）。實作細節，不進契約。</summary>
    public const int MinDifferentCells = 15;

    public static readonly IReadOnlyList<VariantProfile> All = new[]
    {
        new VariantProfile("v-a", "重視公平", "拉高額度點數組內公平的權重，壓低同區延續",
            new Dictionary<string, double> { ["S1_QUOTA_FAIRNESS"] = 1.5, ["S2_AREA_CONSISTENCY"] = 0.5 }),
        new VariantProfile("v-b", "重視延續性", "拉高同區延續的權重，壓低額度點數組內公平",
            new Dictionary<string, double> { ["S1_QUOTA_FAIRNESS"] = 0.5, ["S2_AREA_CONSISTENCY"] = 1.5 }),
        new VariantProfile("v-c", "平衡", "全部權重乘數為 1，即使用者在設定裡填的原始權重",
            new Dictionary<string, double>()),
    };

    /// <summary>
    /// 實際權重 = 使用者設定的權重 × 乘數。使用者設 0（停用）的乘任何數仍是 0，變體不會偷偷把它打開；
    /// 硬約束不在這張表裡。
    /// </summary>
    public static IReadOnlyDictionary<string, double> EffectiveWeights(ConstraintSettings settings, VariantProfile profile) =>
        settings.Soft.ToDictionary(
            c => c.Code,
            c => c.Weight * profile.Multipliers.GetValueOrDefault(c.Code, 1.0),
            StringComparer.Ordinal);
}
