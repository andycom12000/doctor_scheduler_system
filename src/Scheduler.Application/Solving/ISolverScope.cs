using Scheduler.Application.Persistence;
using Scheduler.Application.Scheduling;

namespace Scheduler.Application.Solving;

/// <summary>
/// 背景工作用的一個工作單元：<see cref="SolverJobService"/> 是 singleton，repository 是 scoped，
/// 每次落盤都要自己開一個 scope、用完就丟。Application 零套件相依，所以不直接拿 DI 的
/// <c>IServiceScopeFactory</c>，由組裝端（Api）實作這個介面；測試用記憶體內的假 store 實作。
/// </summary>
public interface ISolverScope : IDisposable
{
    SchedulingContextLoader Loader { get; }

    ISolverJobRepository Jobs { get; }

    IUnitOfWork UnitOfWork { get; }
}

public interface ISolverScopeFactory
{
    ISolverScope Create();
}
