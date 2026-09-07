using Scheduler.Application.Errors;
using Scheduler.Application.Solving;
using Scheduler.Domain.Defaults;
using Scheduler.Domain.Model;

namespace Scheduler.Application.Tests;

/// <summary>
/// 迴圈與狀態機（ARCHITECTURE §4.8），用假的 <see cref="ISolver"/>：這裡驗的是乘數、多樣性輸入、單一 slot、
/// 中止保留已完成的變體、指標由 Domain 重算，不是 CP-SAT。
/// </summary>
public class SolverJobServiceTests
{
    private static readonly YearMonth Oct = new(2026, 10);

    private static DateOnly D(int day) => new(2026, 10, day);

    private static (SolverJobService Service, InMemoryStore Store, FakeSolver Solver) Setup(Func<SolveRequest, CancellationToken, Task<SolveResult>>? handler = null)
    {
        var store = new InMemoryStore().WithStaff("s1", DefaultRanks.R2).WithStaff("s2", DefaultRanks.R2).WithStaff("gone", DefaultRanks.R1, StaffStatus.Inactive);
        var solver = new FakeSolver(handler ?? ((r, _) => Task.FromResult(OneDuty(r))));
        return (new SolverJobService(store, solver, TimeProvider.System), store, solver);
    }

    /// <summary>把 s1 排進 10/1 的 ICU，其他全空：合法但空缺一堆。</summary>
    private static SolveResult OneDuty(SolveRequest r) =>
        new(SolveStatus.Optimal, new[] { new Duty("area-icu", D(1), "s1") }, 3, 12.0, 12.0, r.Context.Staff.Count * 5);

    private static async Task<SolverJobView> WaitForTerminalAsync(SolverJobService service, string jobId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            var view = await service.GetAsync(jobId);
            if (view.Record.Status is SolverJobStatus.Succeeded or SolverJobStatus.Failed or SolverJobStatus.Cancelled)
            {
                return view;
            }

            await Task.Delay(20);
        }

        throw new TimeoutException("求解工作沒有結束");
    }

    [Fact]
    public async Task 三份變體_序列求解_乘數乘在使用者權重上_前幾份的解丟給下一份()
    {
        var (service, store, solver) = Setup();

        var created = await service.CreateAsync(Oct, 3, 1);
        var done = await WaitForTerminalAsync(service, created.Record.JobId);

        Assert.Equal(SolverJobStatus.Succeeded, done.Record.Status);
        Assert.Equal(3, solver.Requests.Count);
        // S1 出廠 100、S2 出廠 40；v-a 乘 1.5／0.5，v-b 乘 0.5／1.5，v-c 全 1
        Assert.Equal(new[] { 150.0, 50.0, 100.0 }, solver.Requests.Select(r => r.EffectiveWeights["S1_QUOTA_FAIRNESS"]));
        Assert.Equal(new[] { 20.0, 60.0, 40.0 }, solver.Requests.Select(r => r.EffectiveWeights["S2_AREA_CONSISTENCY"]));
        Assert.Equal(new[] { 0, 1, 2 }, solver.Requests.Select(r => r.AvoidSolutions.Count));
        Assert.All(solver.Requests, r => Assert.Equal(TimeSpan.FromSeconds(1), r.TimeLimit));
        Assert.All(solver.Requests, r => Assert.Equal(VariantProfiles.MinDifferentCells, r.MinDifferentCells));

        Assert.Equal(new[] { "v-a", "v-b", "v-c" }, store.Variants.Select(v => v.Id));
        Assert.Equal(new[] { "重視公平", "重視延續性", "平衡" }, store.Variants.Select(v => v.Label));
        Assert.Equal(1.5, store.Variants[0].WeightProfile["S1_QUOTA_FAIRNESS"]);
        Assert.Empty(store.Variants[2].WeightProfile);

        var persisted = store.Jobs[created.Record.JobId];
        Assert.Equal(SolverJobStatus.Succeeded, persisted.Status);
        Assert.NotNull(persisted.StartedAt);
        Assert.NotNull(persisted.FinishedAt);
        Assert.NotNull(persisted.ElapsedSec);
        Assert.Null(persisted.FailureReason);
        // 建立、開始、三份變體、結束：每個狀態轉換一次 commit
        Assert.True(store.Commits >= 6, $"只 commit 了 {store.Commits} 次");
    }

    [Fact]
    public async Task 建立時就有規模與約束數_停用者不算()
    {
        var (service, _, _) = Setup();

        var created = await service.CreateAsync(Oct, 1, 1);

        Assert.Contains(created.Record.Status, new[] { SolverJobStatus.Queued, SolverJobStatus.Running });
        var scale = created.Record.Scale!;
        Assert.Equal(2, scale.Staff);
        Assert.Equal(5, scale.Areas);
        Assert.Equal(31, scale.Days);
        // R2 可值病房三區 + ICU = 4 區 × 31 天 × 2 人
        Assert.Equal(4 * 31 * 2, scale.Variables);
        Assert.Equal(7, created.Record.ConstraintCount!.Hard);
        Assert.Equal(6, created.Record.ConstraintCount.Soft); // S7 出廠停用
        Assert.Contains(Scheduling.SchedulingContextLoader.PreviousMonthNotPublishedWarning, created.Record.Warnings);
        await WaitForTerminalAsync(service, created.Record.JobId);
    }

    [Fact]
    public async Task 使用者設_0_的約束_乘任何數仍是_0()
    {
        var (service, store, solver) = Setup();
        store.Constraints = store.Constraints.With("S1_QUOTA_FAIRNESS", c => c with { Weight = 0, Enabled = false });

        var created = await service.CreateAsync(Oct, 3, 1);
        await WaitForTerminalAsync(service, created.Record.JobId);

        Assert.All(solver.Requests, r => Assert.Equal(0.0, r.EffectiveWeights["S1_QUOTA_FAIRNESS"]));
    }

    [Fact]
    public async Task 變體指標由_Domain_重算_不是抄求解器的目標值()
    {
        // 假求解器把 s1 排 10/1、10/2 連兩天（H4 違規），目標值故意給個沒意義的數
        var (service, store, _) = Setup((r, _) => Task.FromResult(new SolveResult(
            SolveStatus.Feasible, new[] { new Duty("area-icu", D(1), "s1"), new Duty("area-icu", D(2), "s1") }, 1, 999.0, 1.0, 10)));

        var created = await service.CreateAsync(Oct, 1, 1);
        await WaitForTerminalAsync(service, created.Record.JobId);

        var variant = Assert.Single(store.Variants);
        Assert.Equal(5 * 31 - 2, variant.Metrics.Vacancies);
        Assert.True(variant.HardViolationCount >= 5 * 31 - 2 + 1, "覆蓋違規加一條 H4");
        Assert.Equal(0, variant.Metrics.RankPreference); // R2 值 ICU 正合 S3
        Assert.Null(variant.Metrics.FairnessPoint);       // S7 出廠停用
        Assert.Equal(2, variant.Duties.Count);
        // 兩位 R2 同組：s1 剩 8 − 2 = 6（10/1、10/2 平日）、s2 剩 8 → 差 2
        Assert.Equal(2, variant.Metrics.QuotaFairness);
    }

    [Fact]
    public async Task 同時只能一個工作_第二個_SOLVER_BUSY_結束後才能再建()
    {
        var gate = new TaskCompletionSource();
        var (service, _, _) = Setup(async (r, ct) =>
        {
            await gate.Task.WaitAsync(ct);
            return OneDuty(r);
        });

        var first = await service.CreateAsync(Oct, 1, 1);
        var ex = await Assert.ThrowsAsync<SchedulerException>(() => service.CreateAsync(Oct, 1, 1));
        Assert.Equal(ErrorCode.SolverBusy, ex.Code);
        Assert.Equal(first.Record.JobId, ex.Details!["jobId"]);

        gate.SetResult();
        await WaitForTerminalAsync(service, first.Record.JobId);
        var second = await service.CreateAsync(Oct, 1, 1);
        await WaitForTerminalAsync(service, second.Record.JobId);
    }

    [Fact]
    public async Task 中止_已完成的變體留著_之後再中止是冪等的()
    {
        var calls = 0;
        var (service, store, _) = Setup(async (r, ct) =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                return OneDuty(r);
            }

            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return OneDuty(r);
        });

        var created = await service.CreateAsync(Oct, 3, 30);
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (store.Variants.Count < 1 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        var cancelled = await service.CancelAsync(created.Record.JobId);

        Assert.Equal(SolverJobStatus.Cancelled, cancelled.Record.Status);
        Assert.NotNull(cancelled.Record.FinishedAt);
        Assert.Single(store.Variants);
        Assert.Equal(SolverJobStatus.Cancelled, store.Jobs[created.Record.JobId].Status);

        var again = await service.CancelAsync(created.Record.JobId);
        Assert.Equal(SolverJobStatus.Cancelled, again.Record.Status);
        var variants = await service.ListVariantsAsync(created.Record.JobId);
        Assert.Single(variants);
    }

    [Fact]
    public async Task 多樣性做不到_退一步不要求不同_留提醒()
    {
        var (service, _, solver) = Setup((r, _) => Task.FromResult(
            r.AvoidSolutions.Count > 0
                ? new SolveResult(SolveStatus.Infeasible, Array.Empty<Duty>(), 0, null, null, 10)
                : OneDuty(r)));

        var created = await service.CreateAsync(Oct, 2, 1);
        var done = await WaitForTerminalAsync(service, created.Record.JobId);

        Assert.Equal(SolverJobStatus.Succeeded, done.Record.Status);
        Assert.Equal(3, solver.Requests.Count);
        Assert.Empty(solver.Requests[2].AvoidSolutions);
        Assert.Contains(done.Record.Warnings, w => w.Contains("改為不要求與前幾份不同"));
    }

    [Fact]
    public async Task 找不到任何可行解_failed_帶原因()
    {
        var (service, store, _) = Setup((_, _) => Task.FromResult(new SolveResult(SolveStatus.Unknown, Array.Empty<Duty>(), 0, null, null, 10)));

        var created = await service.CreateAsync(Oct, 1, 1);
        var done = await WaitForTerminalAsync(service, created.Record.JobId);

        Assert.Equal(SolverJobStatus.Failed, done.Record.Status);
        Assert.Contains("找不到任何可行解", done.Record.FailureReason);
        Assert.Equal(SolverJobStatus.Failed, store.Jobs[created.Record.JobId].Status);
        Assert.Empty(store.Variants);
    }

    [Fact]
    public async Task 求解器丟例外_failed_不會卡住_slot()
    {
        var (service, _, _) = Setup((_, _) => throw new InvalidOperationException("CP-SAT 拒絕模型"));

        var created = await service.CreateAsync(Oct, 1, 1);
        var done = await WaitForTerminalAsync(service, created.Record.JobId);

        Assert.Equal(SolverJobStatus.Failed, done.Record.Status);
        Assert.Equal("CP-SAT 拒絕模型", done.Record.FailureReason);
        var next = await service.CreateAsync(Oct, 1, 1);
        await WaitForTerminalAsync(service, next.Record.JobId);
    }

    [Fact]
    public async Task 訂閱_先重播快照_進度帶_jobId_終態後序列結束()
    {
        var gate = new TaskCompletionSource();
        var (service, _, _) = Setup(async (r, ct) =>
        {
            await gate.Task.WaitAsync(ct);
            return OneDuty(r);
        });

        var created = await service.CreateAsync(Oct, 1, 1);
        var events = new List<SolverProgressSnapshot>();
        var reading = Task.Run(async () =>
        {
            await foreach (var e in service.SubscribeAsync(created.Record.JobId))
            {
                events.Add(e);
            }
        });

        await Task.Delay(50);
        gate.SetResult();
        await reading.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.NotEmpty(events);
        Assert.All(events, e => Assert.Equal(created.Record.JobId, e.JobId));
        Assert.Equal(SolverJobStatus.Succeeded, events[^1].Status);
        Assert.Equal(1, events[^1].VariantCount);

        // 結束後再訂閱：只有一筆終態
        var after = new List<SolverProgressSnapshot>();
        await foreach (var e in service.SubscribeAsync(created.Record.JobId))
        {
            after.Add(e);
        }

        var only = Assert.Single(after);
        Assert.Equal(SolverJobStatus.Succeeded, only.Status);
    }

    [Fact]
    public async Task 結束的工作從資料庫讀_不存在的_404()
    {
        var (service, _, _) = Setup();
        var created = await service.CreateAsync(Oct, 1, 1);
        await WaitForTerminalAsync(service, created.Record.JobId);

        var view = await service.GetAsync(created.Record.JobId);
        Assert.Equal(SolverJobStatus.Succeeded, view.Progress.Status);
        Assert.Equal(1, view.Progress.VariantIndex);

        foreach (var call in new Func<Task>[]
        {
            () => service.GetAsync("job-nope"),
            () => service.CancelAsync("job-nope"),
            () => service.ListVariantsAsync("job-nope"),
            async () => { await foreach (var _ in service.SubscribeAsync("job-nope")) { } },
        })
        {
            var ex = await Assert.ThrowsAsync<SchedulerException>(call);
            Assert.Equal(ErrorCode.NotFound, ex.Code);
        }
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(4, 1)]
    [InlineData(1, 0)]
    [InlineData(1, 601)]
    public async Task 參數超界_422_不佔_slot(int variantCount, int timeLimit)
    {
        var (service, store, _) = Setup();

        var ex = await Assert.ThrowsAsync<SchedulerException>(() => service.CreateAsync(Oct, variantCount, timeLimit));

        Assert.Equal(ErrorCode.InvalidRequest, ex.Code);
        Assert.Empty(store.Jobs);
        var ok = await service.CreateAsync(Oct, 1, 1);
        await WaitForTerminalAsync(service, ok.Record.JobId);
    }
}

internal sealed class FakeSolver : ISolver
{
    private readonly Func<SolveRequest, CancellationToken, Task<SolveResult>> _handler;

    public FakeSolver(Func<SolveRequest, CancellationToken, Task<SolveResult>> handler)
    {
        _handler = handler;
    }

    public List<SolveRequest> Requests { get; } = new();

    public async Task<SolveResult> SolveAsync(SolveRequest request, Action<SolveProgress>? onProgress, CancellationToken cancellationToken)
    {
        lock (Requests)
        {
            Requests.Add(request);
        }

        onProgress?.Invoke(new SolveProgress(0.1, 1, 20, 10));
        try
        {
            return await _handler(request, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return new SolveResult(SolveStatus.Cancelled, Array.Empty<Duty>(), 0, null, null, 0);
        }
    }
}
