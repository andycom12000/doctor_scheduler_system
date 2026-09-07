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
        var callback = new ProgressCallback(onProgress);

        CpSolverStatus status;
        using (cancellationToken.Register(() => solver.StopSearch()))
        {
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

        SolveResult Result(SolveStatus s) => new(
            s, model.Extract(solver), callback.SolutionCount, solver.ObjectiveValue, solver.BestObjectiveBound, model.VariableCount);
    }

    /// <summary>每找到一個解回報一次。從求解器的執行緒被叫，回呼裡的例外不能漏到 native 端。</summary>
    private sealed class ProgressCallback : CpSolverSolutionCallback
    {
        private readonly Action<SolveProgress>? _onProgress;

        public ProgressCallback(Action<SolveProgress>? onProgress)
        {
            _onProgress = onProgress;
        }

        public int SolutionCount { get; private set; }

        public override void OnSolutionCallback()
        {
            SolutionCount++;
            try
            {
                _onProgress?.Invoke(new SolveProgress(WallTime(), SolutionCount, ObjectiveValue(), BestObjectiveBound()));
            }
            catch
            {
                // 進度只是通知，回呼端壞掉不該中斷求解
            }
        }
    }
}
