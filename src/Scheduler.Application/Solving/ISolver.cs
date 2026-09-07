using Scheduler.Domain.Constraints;
using Scheduler.Domain.Model;
using Scheduler.Domain.Scheduling;

namespace Scheduler.Application.Solving;

/// <summary>
/// 求解器一次呼叫的輸入（ARCHITECTURE §4.8）。Solver 不知道「變體」：迴圈、乘數表、多樣性的來源都在
/// <see cref="SolverJobService"/>，這裡收到的已經是「這一份要用什麼權重、要避開哪幾份」。
/// </summary>
/// <param name="Context">要排的月份與所有輸入。<see cref="SchedulingContext.Duties"/> 只當提示，不是固定值。</param>
/// <param name="Constraints">
/// 規則的身分與參數，Solver 讀它決定建哪些約束、哪些啟用。權重<b>不</b>從這裡讀——
/// <see cref="ConstraintDefinition.Weight"/> 上限 100，乘上 1.5 就裝不下，所以乘好的權重另外放在 <see cref="EffectiveWeights"/>。
/// </param>
/// <param name="EffectiveWeights">軟約束代碼 → 乘好的實際權重（使用者設定 × 變體乘數）。缺的代碼視為 0。</param>
/// <param name="AvoidSolutions">前幾份變體的值班清單，這一份要與每一份至少差 <see cref="MinDifferentCells"/> 格。</param>
/// <param name="MinDifferentCells">多樣性約束的門檻；大於格子總數時以格子總數計。</param>
/// <param name="TimeLimit">這一份的時間上限。</param>
public sealed record SolveRequest(
    SchedulingContext Context,
    ConstraintSettings Constraints,
    IReadOnlyDictionary<string, double> EffectiveWeights,
    IReadOnlyList<IReadOnlyList<Duty>> AvoidSolutions,
    int MinDifferentCells,
    TimeSpan TimeLimit);

/// <summary>求解中途的收斂資訊。沒有百分比（§4.7）。</summary>
public sealed record SolveProgress(double ElapsedSec, int SolutionCount, double? BestObjective, double? BestBound);

public enum SolveStatus
{
    /// <summary>已證明最佳。</summary>
    Optimal,

    /// <summary>時間到，有可行解但未證明最佳。</summary>
    Feasible,

    /// <summary>已證明無解（覆蓋是軟項，通常只有多樣性約束會造成這個）。</summary>
    Infeasible,

    /// <summary>時間到，連一個可行解都沒找到。</summary>
    Unknown,

    /// <summary>被中止。找到過解的話 <see cref="SolveResult.Duties"/> 仍是最後一個解。</summary>
    Cancelled,
}

/// <summary>
/// 求解結果。只回值班清單與搜尋資訊；變體的指標由 Application 拿 Domain 重算（§4.8），
/// 目標值只供搜尋與進度顯示，不對外當指標。
/// </summary>
public sealed record SolveResult(
    SolveStatus Status,
    IReadOnlyList<Duty> Duties,
    int SolutionCount,
    double? Objective,
    double? Bound,
    int VariableCount);

/// <summary>
/// Application 對求解器的唯一依賴。實作在 <c>Scheduler.Solver</c>（OR-Tools CP-SAT），
/// 這一層與 Domain 不得引用任何 OR-Tools 型別（硬性規則 1）。
/// </summary>
public interface ISolver
{
    /// <summary>
    /// 求一份。CPU 密集且會阻塞，實作要自己丟到背景執行緒。
    /// <paramref name="onProgress"/> 可能從求解器的執行緒被呼叫，呼叫端要自己顧執行緒安全。
    /// </summary>
    Task<SolveResult> SolveAsync(SolveRequest request, Action<SolveProgress>? onProgress, CancellationToken cancellationToken);
}
