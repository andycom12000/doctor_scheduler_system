using Scheduler.Domain.Model;

namespace Scheduler.Domain.Defaults;

/// <summary>區域類型與區域的出廠值。抄自 <c>docs/constraint-defaults.md</c>「原語讀取的其他聚合根」。</summary>
public static class DefaultAreas
{
    public const string Ward = "WARD";
    public const string Icu = "ICU";
    public const string Chief = "CHIEF";

    public static IReadOnlyList<AreaType> AreaTypes { get; } = new[]
    {
        new AreaType(Ward, "一般病房"),
        new AreaType(Icu, "加護病房"),
        new AreaType(Chief, "總值"),
    };

    public static IReadOnlyList<Area> Areas { get; } = new[]
    {
        new Area("area-a", "A", "A", Ward),
        new Area("area-b", "B", "B", Ward),
        new Area("area-c", "C", "C", Ward),
        new Area("area-icu", "ICU", "ICU", Icu),
        new Area("area-chief", "CHIEF", "總值", Chief),
    };
}

/// <summary>身分、身分組與資格矩陣的出廠值。抄自 <c>docs/constraint-defaults.md</c>。</summary>
public static class DefaultRanks
{
    public const string PGY1 = "PGY1";
    public const string PGY2 = "PGY2";
    public const string R1 = "R1";
    public const string R2 = "R2";
    public const string R3 = "R3";
    public const string R4 = "R4";
    public const string R5 = "R5";
    public const string R6 = "R6";
    public const string PTR = "PTR";
    public const string NP = "NP";

    public const string Junior = "JUNIOR";
    public const string Mid = "MID";
    public const string Senior = "SENIOR";
    public const string NpGroup = "NP";

    public static IReadOnlyList<RankGroup> Groups { get; } = new[]
    {
        new RankGroup(Junior, "低年級"),
        new RankGroup(Mid, "中階"),
        new RankGroup(Senior, "資深"),
        new RankGroup(NpGroup, "NP"),
    };

    public static IReadOnlyList<Rank> Ranks { get; } = new[]
    {
        new Rank(PGY1, "PGY1", Junior, QuotaCap: 10, PointType.A),
        new Rank(PGY2, "PGY2", Junior, QuotaCap: 9, PointType.A),
        new Rank(R1, "R1", Junior, QuotaCap: 9, PointType.A),
        new Rank(R2, "R2", Mid, QuotaCap: 8, PointType.A),
        new Rank(R3, "R3", Mid, QuotaCap: 7, PointType.A),
        new Rank(R4, "R4", Senior, QuotaCap: 6, PointType.B),
        new Rank(R5, "R5", Senior, QuotaCap: 5, PointType.B),
        // R6 的 5 是預設值，當月實際值由 MonthlyOverride.QuotaCapByRank["R6"] 覆寫
        new Rank(R6, "R6", Senior, QuotaCap: 5, PointType.B),
        new Rank(PTR, "打工R", Junior, QuotaCap: 6, PointType.A),
        new Rank(NP, "NP", NpGroup, QuotaCap: null, PointType: null),
    };

    public static EligibilityMatrix Eligibility { get; } = new(
        new Dictionary<string, IReadOnlyDictionary<string, bool>>
        {
            [PGY1] = Row(ward: true, icu: false, chief: false),
            [PGY2] = Row(ward: true, icu: false, chief: false),
            [R1] = Row(ward: true, icu: false, chief: false),
            [R2] = Row(ward: true, icu: true, chief: false),
            [R3] = Row(ward: true, icu: true, chief: false),
            [R4] = Row(ward: false, icu: true, chief: true),
            [R5] = Row(ward: false, icu: true, chief: true),
            [R6] = Row(ward: false, icu: true, chief: true),
            [PTR] = Row(ward: true, icu: false, chief: false),
            [NP] = Row(ward: true, icu: false, chief: false),
        });

    /// <summary>
    /// 參考人數組成（估算用，不是真實名單）：33 位醫師 + 1 位 NP。
    /// mock 與測試 fixture 用這一組，才能重現 ARCHITECTURE §9.1 的供需數字。
    /// </summary>
    public static IReadOnlyDictionary<string, int> ReferenceHeadcount { get; } = new Dictionary<string, int>
    {
        [R4] = 4, [R5] = 5, [R6] = 4,
        [R2] = 3, [R3] = 4,
        [PGY1] = 2, [PGY2] = 4, [R1] = 4, [PTR] = 3,
        [NP] = 1,
    };

    private static IReadOnlyDictionary<string, bool> Row(bool ward, bool icu, bool chief) =>
        new Dictionary<string, bool>
        {
            [DefaultAreas.Ward] = ward,
            [DefaultAreas.Icu] = icu,
            [DefaultAreas.Chief] = chief,
        };
}

/// <summary>點數規則與不可排班日上限的出廠值。抄自 <c>docs/constraint-defaults.md</c>。</summary>
public static class DefaultPointRules
{
    /// <summary>每人每月不可排班日上限。登記期驗證規則，求解器看不到（ADR-0001）。</summary>
    public const int BlockedDayMonthlyCap = 16;

    public static PointRules Rules { get; } = new(
        Quota: new QuotaPointRule(Weekday: 1, Holiday: 2),
        Fairness: new FairnessPointRule(
            Tables: new Dictionary<PointType, IReadOnlyList<FairnessTableEntry>>
            {
                [PointType.A] = new[]
                {
                    new FairnessTableEntry(DayKind.Holiday, DayKind.Holiday, 3),
                    new FairnessTableEntry(DayKind.Holiday, DayKind.Weekday, 2),
                    new FairnessTableEntry(DayKind.Weekday, DayKind.Holiday, 2),
                    new FairnessTableEntry(DayKind.Weekday, DayKind.Weekday, 1),
                },
                [PointType.B] = new[]
                {
                    new FairnessTableEntry(DayKind.Holiday, DayKind.Holiday, 2),
                    new FairnessTableEntry(DayKind.Holiday, DayKind.Weekday, 3),
                    new FairnessTableEntry(DayKind.Weekday, DayKind.Holiday, 1),
                    new FairnessTableEntry(DayKind.Weekday, DayKind.Weekday, 2),
                },
            },
            ConsecutiveSaturdayBonus: new ConsecutiveSaturdayBonus(Points: 1, WindowDays: 10)));
}
