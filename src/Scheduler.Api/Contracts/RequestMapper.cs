using System.Text.Json.Nodes;
using Scheduler.Api.Http;
using Scheduler.Application.Calendars;
using Scheduler.Application.Errors;
using Scheduler.Application.People;
using Scheduler.Application.Schedules;
using Scheduler.Application.Settings;
using Scheduler.Domain.Constraints;
using Scheduler.Domain.Model;

namespace Scheduler.Api.Contracts;

/// <summary>
/// 契約 DTO → Application／Domain 的輸入。純形狀轉換加「欄位有沒有給、列舉認不認得」的檢查，
/// 全部是 422 <c>INVALID_REQUEST</c>；業務判斷（存在性、被引用）在 Application。
/// </summary>
internal static class RequestMapper
{
    // -- 值班表 -------------------------------------------------------------

    public static (CellRef Cell, string? StaffId) ToCommand(this SetDutyRequestDto dto) =>
        (new CellRef(Required(dto.AreaId, "areaId"), Parse.Date(Required(dto.Date, "date"))), NullIfBlank(dto.StaffId));

    public static (CellRef A, CellRef B) ToCommand(this SwapDutiesRequestDto dto) =>
        (ToCell(dto.A, "a"), ToCell(dto.B, "b"));

    private static CellRef ToCell(CellRefDto? dto, string name) =>
        dto is null
            ? throw Missing(name)
            : new CellRef(Required(dto.AreaId, $"{name}.areaId"), Parse.Date(Required(dto.Date, $"{name}.date")));

    // -- 人員 ---------------------------------------------------------------

    public static StaffWrite ToCommand(this StaffWriteDto dto) =>
        new(Required(dto.EmployeeNo, "employeeNo"), Required(dto.Name, "name"), Required(dto.RankCode, "rankCode"));

    public static StaffStatus ToStatus(this StaffStatusRequestDto dto) =>
        ContractNames.ToStaffStatus(Required(dto.Status, "status"))
            ?? throw Invalid($"status 必須是 active 或 inactive，收到 {dto.Status}");

    // -- 行事曆 -------------------------------------------------------------

    /// <summary>四個欄位都可省略；<c>holidayName</c> 要分「沒送」與「送 null」，所以從 JSON 物件讀。</summary>
    public static CalendarDayPatch ToCalendarPatch(this JsonObject body)
    {
        // 契約沒有 additionalProperties: false；前端把 GET 回來的 CalendarDay 整個 PATCH 回來是合理用法，多的欄位忽略
        var provided = body.ContainsKey("holidayName");
        return new CalendarDayPatch(
            Bool(body, "isHoliday"),
            Bool(body, "isPublicHoliday"),
            Bool(body, "isMakeUpWorkday"),
            provided,
            provided ? String(body, "holidayName") : null);
    }

    private static bool? Bool(JsonObject body, string key)
    {
        if (!body.TryGetPropertyValue(key, out var node) || node is null)
        {
            return null;
        }

        return node is JsonValue v && v.TryGetValue<bool>(out var b) ? b : throw Invalid($"{key} 必須是布林值");
    }

    private static string? String(JsonObject body, string key)
    {
        var node = body[key];
        if (node is null)
        {
            return null;
        }

        return node is JsonValue v && v.TryGetValue<string>(out var s) ? s : throw Invalid($"{key} 必須是字串或 null");
    }

    // -- 設定 ---------------------------------------------------------------

    public static AreaSettings ToDomain(this AreaSettingsDto dto) =>
        new(
            RequiredList(dto.AreaTypes, "areaTypes").Select(t => new AreaType(Required(t.Code, "areaTypes[].code"), Required(t.Name, "areaTypes[].name"))).ToArray(),
            RequiredList(dto.Areas, "areas").Select(a => new Area(
                Required(a.Id, "areas[].id"),
                Required(a.Code, "areas[].code"),
                Required(a.Name, "areas[].name"),
                Required(a.AreaTypeCode, "areas[].areaTypeCode"),
                Required(a.RequiredPerDay, "areas[].requiredPerDay"))).ToArray());

    public static RankSettings ToDomain(this RankSettingsDto dto) =>
        new(
            RequiredList(dto.Groups, "groups").Select(g => new RankGroup(Required(g.Code, "groups[].code"), Required(g.Name, "groups[].name"))).ToArray(),
            RequiredList(dto.Ranks, "ranks").Select(r => new Rank(
                Required(r.Code, "ranks[].code"),
                Required(r.Name, "ranks[].name"),
                Required(r.GroupCode, "ranks[].groupCode"),
                r.QuotaCap,
                r.PointType is null ? null : ContractNames.ToPointType(r.PointType) ?? throw Invalid($"pointType 必須是 A、B 或 null，收到 {r.PointType}"))).ToArray());

    public static EligibilityMatrix ToDomain(this EligibilityMatrixDto dto)
    {
        var matrix = dto.Matrix ?? throw Missing("matrix");
        foreach (var (rankCode, row) in matrix)
        {
            if (row is null)
            {
                throw Invalid($"matrix.{rankCode} 必須是物件");
            }
        }

        return new EligibilityMatrix(matrix);
    }

    public static PointRules ToDomain(this PointRulesDto dto)
    {
        var quota = dto.Quota ?? throw Missing("quota");
        var fairness = dto.Fairness ?? throw Missing("fairness");
        var bonus = fairness.ConsecutiveSaturdayBonus ?? throw Missing("fairness.consecutiveSaturdayBonus");
        var tables = new Dictionary<PointType, IReadOnlyList<FairnessTableEntry>>();
        foreach (var (key, entries) in fairness.Tables ?? throw Missing("fairness.tables"))
        {
            var pointType = ContractNames.ToPointType(key) ?? throw Invalid($"fairness.tables 的鍵必須是 A 或 B，收到 {key}");
            tables[pointType] = RequiredList(entries, $"fairness.tables.{key}")
                .Select(e => new FairnessTableEntry(ToTableKind(e.Today, "today"), ToTableKind(e.Tomorrow, "tomorrow"), Required(e.Points, $"fairness.tables.{key}[].points")))
                .ToArray();
        }

        return new PointRules(
            new QuotaPointRule(Required(quota.Weekday, "quota.weekday"), Required(quota.Holiday, "quota.holiday")),
            new FairnessPointRule(
                tables,
                new ConsecutiveSaturdayBonus(
                    Required(bonus.Points, "fairness.consecutiveSaturdayBonus.points"),
                    Required(bonus.WindowDays, "fairness.consecutiveSaturdayBonus.windowDays"))));
    }

    /// <summary>查表的列只會是 weekday 或 holiday（契約 enum），publicHoliday 不是查表的維度。</summary>
    private static DayKind ToTableKind(string? s, string name) =>
        ContractNames.ToDayKind(Required(s, name)) is (DayKind.Weekday or DayKind.Holiday) and var kind
            ? kind
            : throw Invalid($"{name} 必須是 weekday 或 holiday，收到 {s}");

    public static ConstraintSettings ToDomain(this ConstraintSettingsDto dto) =>
        new(
            RequiredList(dto.Hard, "hard").Select(c => ToDefinition(c.Code, c.Name, c.Primitive, Severity.Hard, Required(c.Enabled, $"{c.Code}.enabled"), 0, c.Scope, c.Metric, c.Params)).ToArray(),
            // 軟約束的 Enabled 是「權重 > 0」的衍生值，那是領域規則，由 SettingsCommands 定；這裡先給 true 佔位
            RequiredList(dto.Soft, "soft").Select(c => ToDefinition(c.Code, c.Name, c.Primitive, Severity.Soft, true, Required(c.Weight, $"{c.Code}.weight"), c.Scope, c.Metric, c.Params)).ToArray());

    private static ConstraintDefinition ToDefinition(
        string? code, string? name, string? primitive, Severity severity, bool enabled, int weight,
        ConstraintScopeDto? scope, string? metric, ConstraintParamsDto? @params)
    {
        var codeValue = Required(code, "code");
        return new ConstraintDefinition(
            codeValue,
            Required(name, $"{codeValue}.name"),
            ContractNames.ToPrimitive(Required(primitive, $"{codeValue}.primitive")) ?? throw Invalid($"{codeValue}.primitive 不認得：{primitive}"),
            severity,
            enabled,
            weight,
            scope is null ? ConstraintScope.All : new ConstraintScope(
                Set(scope.RankCodes),
                Set(scope.ExemptRankCodes),
                Set(scope.AreaTypeCodes),
                scope.DayKinds?.Select(k => ContractNames.ToDayKind(k) ?? throw Invalid($"{codeValue}.scope.dayKinds 不認得：{k}")).ToHashSet()),
            metric is null ? null : ContractNames.ToMetric(metric) ?? throw Invalid($"{codeValue}.metric 不認得：{metric}"),
            @params is null ? ConstraintParams.None : new ConstraintParams(
                @params.Days,
                @params.Cap,
                @params.Direction is null ? null : ContractNames.ToPreferenceDirection(@params.Direction) ?? throw Invalid($"{codeValue}.params.direction 不認得：{@params.Direction}")));
    }

    private static IReadOnlySet<string>? Set(IReadOnlyList<string>? list) => list?.ToHashSet(StringComparer.Ordinal);

    public static IReadOnlyDictionary<string, int> ToQuotaCapByRank(this MonthlyOverrideDto dto) =>
        dto.QuotaCapByRank ?? new Dictionary<string, int>();

    // ---- helpers ----

    private static string Required(string? value, string name) =>
        string.IsNullOrWhiteSpace(value) ? throw Missing(name) : value;

    private static T Required<T>(T? value, string name) where T : struct =>
        value ?? throw Missing(name);

    private static IReadOnlyList<T> RequiredList<T>(IReadOnlyList<T>? list, string name)
    {
        if (list is null)
        {
            throw Missing(name);
        }

        if (list.Any(x => x is null))
        {
            throw Invalid($"{name} 裡有 null 元素");
        }

        return list;
    }

    private static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    private static SchedulerException Missing(string name) => Invalid($"缺少必要欄位 {name}");

    private static SchedulerException Invalid(string message) => new(ErrorCode.InvalidRequest, message);
}
