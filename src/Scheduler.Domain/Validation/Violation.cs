using System.Security.Cryptography;
using System.Text;
using Scheduler.Domain.Constraints;

namespace Scheduler.Domain.Validation;

/// <summary>
/// 一份既有值班表上不滿足某條約束的具體位置。讀取時衍生，不儲存。
/// 公平性與延續性是組層級／人層級的分數，指不到格子，不算違規。
/// </summary>
/// <param name="Id">確定性雜湊（code + 排序後的 cellKeys）。同一個違規每次讀都同 id，前端才能 diff 與去重。</param>
/// <param name="CellKeys">已排序。可能是 <c>area:…</c> 或 <c>staff:…</c>。</param>
public sealed record Violation(string Id, string Code, Severity Severity, IReadOnlyList<string> CellKeys, string Message)
{
    public static Violation Create(ConstraintDefinition constraint, IEnumerable<string> cellKeys, string message)
    {
        var sorted = cellKeys.Distinct(StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal).ToArray();
        return new Violation(ComputeId(constraint.Code, sorted), constraint.Code, constraint.Severity, sorted, message);
    }

    /// <summary>SHA-256(code | key1,key2,…) 前 8 bytes 的小寫 hex。夠短、夠穩定、夠不撞。</summary>
    public static string ComputeId(string code, IReadOnlyList<string> sortedCellKeys)
    {
        var input = code + "|" + string.Join(",", sortedCellKeys);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash.AsSpan(0, 8)).ToLowerInvariant();
    }
}

/// <summary><c>POST /schedules/{ym}/validate</c> 的回應本體。</summary>
public sealed record ValidationResult(IReadOnlyList<Violation> Violations)
{
    public int HardCount => Violations.Count(v => v.Severity == Severity.Hard);

    public int SoftCount => Violations.Count(v => v.Severity == Severity.Soft);

    /// <summary>無硬約束違規時為 true。</summary>
    public bool Ok => HardCount == 0;
}
