using Scheduler.Application.Persistence;
using Scheduler.Application.Scheduling;
using Scheduler.Application.Solving;

namespace Scheduler.Api.Solving;

/// <summary>
/// <see cref="ISolverScopeFactory"/> 的 DI 版：背景的求解工作每次落盤開一個 DI scope，
/// 拿到 scoped 的 repository 與 unit of work，用完就丟。組裝細節住在 Api，Application 看不到 DI 容器。
/// </summary>
internal sealed class ServiceProviderSolverScopeFactory : ISolverScopeFactory
{
    private readonly IServiceScopeFactory _scopes;

    public ServiceProviderSolverScopeFactory(IServiceScopeFactory scopes)
    {
        _scopes = scopes;
    }

    public ISolverScope Create() => new Scope(_scopes.CreateScope());

    private sealed class Scope : ISolverScope
    {
        private readonly IServiceScope _scope;

        public Scope(IServiceScope scope)
        {
            _scope = scope;
        }

        public SchedulingContextLoader Loader => _scope.ServiceProvider.GetRequiredService<SchedulingContextLoader>();

        public ISolverJobRepository Jobs => _scope.ServiceProvider.GetRequiredService<ISolverJobRepository>();

        public IUnitOfWork UnitOfWork => _scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        public void Dispose() => _scope.Dispose();
    }
}
