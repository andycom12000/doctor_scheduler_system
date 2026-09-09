using System.Globalization;
using Google.OrTools.Sat;
using Scheduler.Application.Solving;

namespace Scheduler.Solver;

/// <summary>
/// <see cref="ISolver"/> 的 OR-Tools CP-SAT 實作。一次解一份；迴圈、變體、乘數都在 Application 的 <c>SolverJobService</c>。
/// 求解阻塞且吃滿核心，丟到執行緒池跑；中止用 <see cref="CpSolver.StopSearch"/>，找到過的最後一個解仍會回。
/// </summary>
public sealed class CpSatSolver : ISolver
{
    /// <summary>CI 的 runner 只有兩核，別超額訂閱。</summary>
    private static readonly int Workers = Math.Clamp(Environment.ProcessorCount, 1, 8);

    public Task<SolveResult> SolveAsync(SolveRequest request, Action<SolveProgress>? onProgress, CancellationToken cancellationToken) =>
        Task.Run(() => Solve(request, onProgress, cancellationToken), CancellationToken.None);

    private static SolveResult Solve(SolveRequest request, Action<SolveProgress>? onProgress, CancellationToken cancellationToken)
    {
        var model = new ScheduleModel(request);
        if (cancellationToken.IsCancellationRequested)
        {
            return new SolveResult(SolveStatus.Cancelled, Array.Empty<Domain.Model.Duty>(), 0, null, null, model.VariableCount);
        }

        var solver = new CpSolver
        {
            StringParameters = string.Create(CultureInfo.InvariantCulture,
                $"max_time_in_seconds:{request.TimeLimit.TotalSeconds:0.###}, num_workers:{Workers}"),
        };
        var callback = new ProgressCallback(model, onProgress, cancellationToken);

        CpSolverStatus status;
        using (cancellationToken.Register(() => solver.StopSearch()))
        {
            // StopSearch 在 Solve 尚未進入搜尋前可能是 no-op：註冊之後再看一次，把這段空窗縮到最小；
            // 剩下的由 callback 在每個解補檢查（找到第一個解之後一定停得下來）
            if (cancellationToken.IsCancellationRequested)
            {
                return new SolveResult(SolveStatus.Cancelled, Array.Empty<Domain.Model.Duty>(), 0, null, null, model.VariableCount);
            }

            status = solver.Solve(model.Model, callback);
        }

        var cancelled = cancellationToken.IsCancellationRequested;
        return status switch
        {
            CpSolverStatus.Optimal => Result(cancelled ? SolveStatus.Cancelled : SolveStatus.Optimal),
            CpSolverStatus.Feasible => Result(cancelled ? SolveStatus.Cancelled : SolveStatus.Feasible),
            CpSolverStatus.Infeasible => new SolveResult(SolveStatus.Infeasible, Array.Empty<Domain.Model.Duty>(), 0, null, null, model.VariableCount),
            CpSolverStatus.Unknown => new SolveResult(cancelled ? SolveStatus.Cancelled : SolveStatus.Unknown, Array.Empty<Domain.Model.Duty>(), 0, null, null, model.VariableCount),
            _ => throw new InvalidOperationException($"CP-SAT 拒絕模型：{status}。{solver.ResponseStats()}"),
        };

        // 目標值用回傳的解直接對目標式求值，不用 solver.ObjectiveValue：時限內停下的 Feasible 解，ObjectiveValue
        // 可能比這個解的真實目標值高（實測差額是 Fairness 項的整數倍——presolve 把 AddMaxEquality 之類只出現在目標式的
        // 輔助變數放鬆，搜尋中的值帶鬆弛，回傳的解經 postsolve 後才是緊的）。這樣 Objective 才與 Extract 出來的班表、
        // Domain 重算的分數對得上（tests/Scheduler.Solver.Tests/ObjectiveConsistencyTests）。
        SolveResult Result(SolveStatus s) => new(
            s, model.Extract(solver), callback.SolutionCount, (double)solver.Value(model.Objective), solver.BestObjectiveBound, model.VariableCount);
    }

    /// <summary>每找到一個解回報一次。從求解器的執行緒被叫，回呼裡的例外不能漏到 native 端。</summary>
    private sealed class ProgressCallback : CpSolverSolutionCallback
    {
        private readonly ScheduleModel _model;
        private readonly Action<SolveProgress>? _onProgress;
        private readonly CancellationToken _cancellationToken;

        public ProgressCallback(ScheduleModel model, Action<SolveProgress>? onProgress, CancellationToken cancellationToken)
        {
            _model = model;
            _onProgress = onProgress;
            _cancellationToken = cancellationToken;
        }

        public int SolutionCount { get; private set; }

        public override void OnSolutionCallback()
        {
            SolutionCount++;
            if (_cancellationToken.IsCancellationRequested)
            {
                StopSearch();
            }

            try
            {
                // 與最終結果同一個算法（見 Result），進度看到的數字才不會比結果高
                _onProgress?.Invoke(new SolveProgress(WallTime(), SolutionCount, (double)Value(_model.Objective), BestObjectiveBound()));
            }
            catch
            {
                // 進度只是通知，回呼端壞掉不該中斷求解
            }
        }
    }
}
