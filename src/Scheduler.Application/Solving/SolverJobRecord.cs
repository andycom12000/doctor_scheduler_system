using Scheduler.Domain.Model;

namespace Scheduler.Application.Solving;

/// <summary>契約的 <c>SolverJobStatus</c>。</summary>
public enum SolverJobStatus
{
    Queued,
    Running,
    Succeeded,
    Failed,
    Cancelled,
}

/// <summary>模型規模，契約 <c>SolverJob.scale</c>。</summary>
public sealed record SolverScale(int Staff, int Areas, int Days, int Variables);

/// <summary>建模時的約束數，契約 <c>SolverJob.constraintCount</c>。</summary>
public sealed record ConstraintCount(int Hard, int Soft);

/// <summary>
/// 一次求解工作的持久化紀錄。只在狀態轉換時寫入（ARCHITECTURE §4.8）；
/// 逐秒的進度不進資料庫，只留「最後一份完成的變體」的搜尋統計（<c>Last*</c> 三欄），
/// 工作結束、從資料庫讀回時 <c>progress</c> 才不會退回 0／null（#22）。
/// </summary>
public sealed record SolverJobRecord(
    string JobId,
    YearMonth YearMonth,
    SolverJobStatus Status,
    int VariantCount,
    int TimeLimitSecPerVariant,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    double? ElapsedSec,
    string? FailureReason,
    IReadOnlyList<string> Warnings,
    SolverScale? Scale,
    ConstraintCount? ConstraintCount,
    int? LastSolutionCount = null,
    double? LastBestObjective = null,
    double? LastBestBound = null);

/// <summary>契約 <c>Variant.metrics</c>。全部由 Domain 重算後存下，是使用者當時看到的數字。</summary>
public sealed record VariantMetrics(
    int Vacancies,
    double QuotaFairness,
    double AreaConsistency,
    double RankPreference,
    double? FairnessPoint);

/// <summary>一份變體。<see cref="WeightProfile"/> 是乘數，鍵為軟約束代碼。</summary>
public sealed record VariantRecord(
    string JobId,
    string Id,
    string Label,
    IReadOnlyDictionary<string, double> WeightProfile,
    VariantMetrics Metrics,
    int HardViolationCount,
    double SoftScore,
    IReadOnlyList<Duty> Duties);
