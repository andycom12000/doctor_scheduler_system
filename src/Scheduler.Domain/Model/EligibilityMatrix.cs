namespace Scheduler.Domain.Model;

/// <summary>
/// 資格矩陣：身分代碼 → (區域類型代碼 → 可否值班)。直接給定，沒有推導規則。
/// 查不到的組合視為不可值——新增身分或區域類型後忘了填矩陣，症狀是「排不進去」而不是「亂排」。
/// </summary>
public sealed class EligibilityMatrix
{
    private readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, bool>> _matrix;

    public EligibilityMatrix(IReadOnlyDictionary<string, IReadOnlyDictionary<string, bool>> matrix)
    {
        _matrix = matrix;
    }

    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, bool>> Matrix => _matrix;

    public bool IsEligible(string rankCode, string areaTypeCode) =>
        _matrix.TryGetValue(rankCode, out var row) && row.TryGetValue(areaTypeCode, out var ok) && ok;

    /// <summary>某身分可值的區域類型。<c>Staff.eligibleAreaTypes</c> 由此推導，不另外儲存。</summary>
    public IReadOnlyList<string> EligibleAreaTypes(string rankCode) =>
        _matrix.TryGetValue(rankCode, out var row)
            ? row.Where(kv => kv.Value).Select(kv => kv.Key).ToArray()
            : Array.Empty<string>();
}
