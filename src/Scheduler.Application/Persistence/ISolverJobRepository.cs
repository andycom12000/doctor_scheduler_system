using Scheduler.Application.Solving;

namespace Scheduler.Application.Persistence;

/// <summary>
/// 求解紀錄。全部保留、不清理（ARCHITECTURE §5）。只在狀態轉換時寫，
/// 活著的 job 從記憶體讀，結束的從這裡讀（§4.8）。
/// </summary>
public interface ISolverJobRepository
{
    Task<SolverJobRecord?> FindAsync(string jobId, CancellationToken cancellationToken = default);

    /// <summary>所有 job，依建立時間遞減。</summary>
    Task<IReadOnlyList<SolverJobRecord>> ListAsync(CancellationToken cancellationToken = default);

    Task AddAsync(SolverJobRecord job, CancellationToken cancellationToken = default);

    /// <summary>狀態轉換時整筆更新。不存在時擲出例外。</summary>
    Task UpdateAsync(SolverJobRecord job, CancellationToken cancellationToken = default);

    /// <summary>每完成一份變體就寫一份，中止時已完成的變體仍留著。</summary>
    Task AddVariantAsync(VariantRecord variant, CancellationToken cancellationToken = default);

    /// <summary>某 job 的變體，依寫入順序。</summary>
    Task<IReadOnlyList<VariantRecord>> GetVariantsAsync(string jobId, CancellationToken cancellationToken = default);

    Task<VariantRecord?> FindVariantAsync(string jobId, string variantId, CancellationToken cancellationToken = default);

    /// <summary>程式啟動時把仍是 queued／running 的 job 一律改成 failed。回傳改了幾筆。與其他寫入一樣只登記，要 commit 才落盤。</summary>
    Task<int> FailUnfinishedAsync(string reason, DateTimeOffset finishedAt, CancellationToken cancellationToken = default);
}
