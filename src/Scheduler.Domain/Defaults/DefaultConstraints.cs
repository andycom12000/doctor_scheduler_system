using Scheduler.Domain.Constraints;
using Scheduler.Domain.Model;

namespace Scheduler.Domain.Defaults;

/// <summary>
/// 7 硬 / 7 軟約束的出廠值。**唯一抄寫來源是 <c>docs/constraint-defaults.md</c>**——
/// 這裡的每一個代碼、數字、範圍都必須與那份文件逐字相同，不得另發明。
/// seed、MSW mock、Solver 測試 fixture 都從那份文件（或這裡）抄。
/// </summary>
public static class DefaultConstraints
{
    public const string H1AreaCoverage = "H1_AREA_COVERAGE";
    public const string H2Eligibility = "H2_ELIGIBILITY";
    public const string H3QuotaCap = "H3_QUOTA_CAP";
    public const string H4MinGap = "H4_MIN_GAP";
    public const string H5BlockedDay = "H5_BLOCKED_DAY";
    public const string H6NpMonthlyDays = "H6_NP_MONTHLY_DAYS";
    public const string H7NpMaxConsecutive = "H7_NP_MAX_CONSECUTIVE";

    public const string S1QuotaFairness = "S1_QUOTA_FAIRNESS";
    public const string S2AreaConsistency = "S2_AREA_CONSISTENCY";
    public const string S3R2R3PreferIcu = "S3_R2R3_PREFER_ICU";
    public const string S4R4R6PreferChief = "S4_R4R6_PREFER_CHIEF";
    public const string S5NpLastResort = "S5_NP_LAST_RESORT";
    public const string S6NpAvoidHoliday = "S6_NP_AVOID_HOLIDAY";
    public const string S7FairnessPoint = "S7_FAIRNESS_POINT";

    public static ConstraintSettings Settings { get; } = new(
        Hard: new[]
        {
            ConstraintDefinition.Hard(H1AreaCoverage, "每日每區恰好 1 人", Primitive.ExactCount),
            ConstraintDefinition.Hard(H2Eligibility, "身分資格", Primitive.Eligible),
            ConstraintDefinition.Hard(H3QuotaCap, "額度點數上限", Primitive.Budget,
                scope: ConstraintScope.Exempt(DefaultRanks.NP), metric: Metric.QuotaPoint),
            ConstraintDefinition.Hard(H4MinGap, "值休休值", Primitive.MinGap,
                scope: ConstraintScope.Exempt(DefaultRanks.NP), @params: new ConstraintParams(Days: 3)),
            ConstraintDefinition.Hard(H5BlockedDay, "不可排班日", Primitive.Forbidden),
            ConstraintDefinition.Hard(H6NpMonthlyDays, "NP 每月天數上限", Primitive.Budget,
                scope: ConstraintScope.ForRanks(DefaultRanks.NP), metric: Metric.DutyDay, @params: new ConstraintParams(Cap: 20)),
            ConstraintDefinition.Hard(H7NpMaxConsecutive, "NP 最多連六", Primitive.MaxConsecutive,
                scope: ConstraintScope.ForRanks(DefaultRanks.NP), @params: new ConstraintParams(Days: 6)),
        },
        Soft: new[]
        {
            ConstraintDefinition.Soft(S1QuotaFairness, "額度點數組內公平", Primitive.Fairness, weight: 100,
                scope: ConstraintScope.Exempt(DefaultRanks.NP), metric: Metric.QuotaPoint),
            ConstraintDefinition.Soft(S2AreaConsistency, "同區延續", Primitive.Consistency, weight: 40,
                scope: ConstraintScope.Exempt(DefaultRanks.NP)),
            ConstraintDefinition.Soft(S3R2R3PreferIcu, "R2/R3 優先 ICU", Primitive.Preference, weight: 50,
                scope: ConstraintScope.ForRanks(DefaultRanks.R2, DefaultRanks.R3).InAreaTypes(DefaultAreas.Icu),
                @params: new ConstraintParams(Direction: PreferenceDirection.Prefer)),
            ConstraintDefinition.Soft(S4R4R6PreferChief, "R4~R6 優先總值", Primitive.Preference, weight: 50,
                scope: ConstraintScope.ForRanks(DefaultRanks.R4, DefaultRanks.R5, DefaultRanks.R6).InAreaTypes(DefaultAreas.Chief),
                @params: new ConstraintParams(Direction: PreferenceDirection.Prefer)),
            ConstraintDefinition.Soft(S5NpLastResort, "NP 盡量不用", Primitive.Preference, weight: 60,
                scope: ConstraintScope.ForRanks(DefaultRanks.NP),
                @params: new ConstraintParams(Direction: PreferenceDirection.Avoid)),
            ConstraintDefinition.Soft(S6NpAvoidHoliday, "NP 避開假日", Primitive.Preference, weight: 30,
                scope: ConstraintScope.ForRanks(DefaultRanks.NP).OnDayKinds(DayKind.Holiday),
                @params: new ConstraintParams(Direction: PreferenceDirection.Avoid)),
            // 實驗性規則，預設停用（權重 0）
            ConstraintDefinition.Soft(S7FairnessPoint, "公平性點數組內公平", Primitive.Fairness, weight: 0,
                scope: ConstraintScope.Exempt(DefaultRanks.NP), metric: Metric.FairnessPoint),
        });
}
