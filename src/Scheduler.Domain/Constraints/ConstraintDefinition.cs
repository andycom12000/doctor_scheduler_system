using Scheduler.Domain.Model;

namespace Scheduler.Domain.Constraints;

/// <summary>
/// 九個約束原語。constraints.md 的每一條規則都對應到其中之一（ADR-0002）。
/// 未來出現表達不了的規則，要先擴充這個集合，而不是在檢查器裡偷加特例。
/// </summary>
public enum Primitive
{
    /// <summary>每日每區恰好 n 人。檢查器視為硬違規；求解器建成極高權重軟項。</summary>
    ExactCount,

    /// <summary>身分 × 區域類型 資格。讀資格矩陣。</summary>
    Eligible,

    /// <summary>度量上限（額度點數上限、NP 每月天數）。</summary>
    Budget,

    /// <summary>兩次值班的最小間隔（值休休）。跨月。</summary>
    MinGap,

    /// <summary>連續天數上限。跨月。</summary>
    MaxConsecutive,

    /// <summary>不可排班日。讀登記。</summary>
    Forbidden,

    /// <summary>身分集合對「區域類型 × 日類」的偏好，<see cref="ConstraintParams.Direction"/> 決定 prefer / avoid。</summary>
    Preference,

    /// <summary>身分組內某度量的公平性（max − min）。組層級分數，不產生逐格違規。</summary>
    Fairness,

    /// <summary>同一人盡量值同一區（離開主區的次數）。分數，不產生逐格違規。</summary>
    Consistency,
}

/// <summary>
/// 度量。<see cref="Primitive.Budget"/> 與 <see cref="Primitive.Fairness"/> 對它參數化，
/// 因此兩套點數與天數上限共用同一份程式碼。
/// </summary>
public enum Metric
{
    QuotaPoint,
    FairnessPoint,
    DutyDay,
}

public enum Severity
{
    Hard,
    Soft,
}

public enum PreferenceDirection
{
    Prefer,
    Avoid,
}

/// <summary>
/// 原語的參數。契約上是 <c>additionalProperties</c>，領域裡收成封閉集合——
/// 九個原語需要的參數就這幾個，多一個參數就是多一個原語的語義，該走 ADR 而不是塞字典。
/// </summary>
/// <param name="Days">MinGap：最小間隔天數；MaxConsecutive：連續上限。</param>
/// <param name="Cap">Budget：上限。省略時 quota_point 讀 <see cref="Rank.QuotaCap"/> 與當月覆寫。</param>
/// <param name="Direction">Preference：prefer 或 avoid。</param>
public sealed record ConstraintParams(int? Days = null, int? Cap = null, PreferenceDirection? Direction = null)
{
    public static readonly ConstraintParams None = new();
}

/// <summary>
/// 約束的適用範圍，三個維度：身分、區域類型、日類。
/// NP 的四條特例與「NP 避開假日」全部透過範圍表達，不在程式裡寫 if。
/// 省略的維度代表不限。
/// </summary>
public sealed record ConstraintScope(
    IReadOnlySet<string>? RankCodes = null,
    IReadOnlySet<string>? ExemptRankCodes = null,
    IReadOnlySet<string>? AreaTypeCodes = null,
    IReadOnlySet<DayKind>? DayKinds = null)
{
    public static readonly ConstraintScope All = new();

    public static ConstraintScope ForRanks(params string[] rankCodes) => new(RankCodes: rankCodes.ToHashSet());

    public static ConstraintScope Exempt(params string[] rankCodes) => new(ExemptRankCodes: rankCodes.ToHashSet());

    public ConstraintScope InAreaTypes(params string[] areaTypeCodes) => this with { AreaTypeCodes = areaTypeCodes.ToHashSet() };

    public ConstraintScope OnDayKinds(params DayKind[] dayKinds) => this with { DayKinds = dayKinds.ToHashSet() };

    public bool AppliesToRank(string rankCode) =>
        (RankCodes is null || RankCodes.Contains(rankCode)) &&
        (ExemptRankCodes is null || !ExemptRankCodes.Contains(rankCode));

    public bool AppliesToAreaType(string areaTypeCode) =>
        AreaTypeCodes is null || AreaTypeCodes.Contains(areaTypeCode);

    /// <summary>日類維度：列了多個日類時任一命中即適用。</summary>
    public bool AppliesToDay(CalendarDay day) =>
        DayKinds is null || DayKinds.Any(day.Is);
}

/// <summary>
/// 一條約束：規則的身分與參數。硬約束用 <see cref="Enabled"/> 開關，
/// 軟約束用 <see cref="Weight"/>（0–100，0 即停用）。
/// 領域的檢查器與求解器的建模器各自解讀這同一份定義。
/// </summary>
public sealed record ConstraintDefinition(
    string Code,
    string Name,
    Primitive Primitive,
    Severity Severity,
    bool Enabled,
    int Weight,
    ConstraintScope Scope,
    Metric? Metric,
    ConstraintParams Params)
{
    public const int MaxWeight = 100;

    public static ConstraintDefinition Hard(
        string code, string name, Primitive primitive,
        ConstraintScope? scope = null, Metric? metric = null, ConstraintParams? @params = null, bool enabled = true) =>
        new(code, name, primitive, Severity.Hard, enabled, Weight: 0, scope ?? ConstraintScope.All, metric, @params ?? ConstraintParams.None);

    public static ConstraintDefinition Soft(
        string code, string name, Primitive primitive, int weight,
        ConstraintScope? scope = null, Metric? metric = null, ConstraintParams? @params = null)
    {
        if (weight is < 0 or > MaxWeight)
        {
            throw new ArgumentOutOfRangeException(nameof(weight), weight, $"軟約束權重必須在 0–{MaxWeight}");
        }

        return new(code, name, primitive, Severity.Soft, Enabled: weight > 0, weight, scope ?? ConstraintScope.All, metric, @params ?? ConstraintParams.None);
    }

    /// <summary>硬約束看 Enabled，軟約束看 Weight &gt; 0。</summary>
    public bool IsActive => Severity == Severity.Hard ? Enabled : Weight > 0;
}

/// <summary>
/// <c>GET · PUT /api/settings/constraints</c> 讀寫的就是這份文件本身。
/// 預設內容在 <c>docs/constraint-defaults.md</c>，程式裡的複本是 <see cref="Defaults.DefaultConstraints"/>。
/// </summary>
public sealed record ConstraintSettings(IReadOnlyList<ConstraintDefinition> Hard, IReadOnlyList<ConstraintDefinition> Soft)
{
    public IEnumerable<ConstraintDefinition> All => Hard.Concat(Soft);

    public IEnumerable<ConstraintDefinition> Active => All.Where(c => c.IsActive);

    public ConstraintDefinition this[string code] =>
        All.FirstOrDefault(c => c.Code == code) ?? throw new KeyNotFoundException($"沒有代碼為 {code} 的約束");

    /// <summary>回傳改過某一條之後的新設定。測試裡用來關掉或調權重。</summary>
    public ConstraintSettings With(string code, Func<ConstraintDefinition, ConstraintDefinition> change)
    {
        ConstraintDefinition Apply(ConstraintDefinition c) => c.Code == code ? change(c) : c;
        return new ConstraintSettings(Hard.Select(Apply).ToArray(), Soft.Select(Apply).ToArray());
    }
}
