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

    [Fact]
    public async Task 結束後從資料庫讀回_進度仍是最後一份變體的搜尋統計_不退回零()
    {
        // 假求解器的進度回呼只報 1 個解、目標 20；最終結果是 3 個解、目標 12、bound 12：以結果為準
        var (service, _, _) = Setup();

        var created = await service.CreateAsync(Oct, 2, 1);
        var done = await WaitForTerminalAsync(service, created.Record.JobId);
        // 等背景工作把 slot 放掉（能再建一個工作就代表放掉了）：之後的 GetAsync 一定走資料庫紀錄，不再是記憶體裡的活工作
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (true)
        {
            try
            {
                await service.CreateAsync(Oct, 1, 1);
                break;
            }
            catch (SchedulerException ex) when (ex.Code == ErrorCode.SolverBusy && DateTime.UtcNow < deadline)
            {
                await Task.Delay(20);
            }
        }

        var reread = await service.GetAsync(created.Record.JobId);
        foreach (var view in new[] { done, reread })
        {
            Assert.Equal(SolverJobStatus.Succeeded, view.Progress.Status);
            Assert.Equal(2, view.Progress.VariantIndex);
            Assert.Equal(3, view.Progress.SolutionCount);
            Assert.Equal(12.0, view.Progress.BestObjective);
            Assert.Equal(12.0, view.Progress.BestBound);
            Assert.Equal(0.0, view.Progress.Gap);
        }
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
        // 最後一份變體的搜尋統計跟著紀錄落盤（#22）
        Assert.Equal((3, 3, 12.0, 12.0), (persisted.LastVariantIndex, persisted.LastSolutionCount, persisted.LastBestObjective, persisted.LastBestBound));
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
        var done = await WaitForTerminalAsync(service, created.Record.JobId);

        var variant = Assert.Single(store.Variants);
        Assert.True(done.Record.ElapsedSec >= 0);
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
    public async Task 看得到終態就能立刻建下一個工作_不需重試()
    {
        // 競態守門：終態對外可見（GetAsync）的那一刻 slot 必須已經空出來。
        // 把終態落盤卡住，製造「終態已算出、落盤未完」的窗口：此時若 GetAsync 已回終態，就必須能建下一個工作
        var (service, store, _) = Setup();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        // scope 呼叫順序：撈 context(1)、新增紀錄(2)、轉 running(3)、存變體(4)、終態落盤(5)
        var gated = new GatedScopeFactory(store, entered, release, gateAtCall: 5);
        service = new SolverJobService(gated, new FakeSolver((r, _) => Task.FromResult(OneDuty(r))), TimeProvider.System);

        var created = await service.CreateAsync(Oct, 1, 1);
        Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));

        var view = await service.GetAsync(created.Record.JobId);
        // 新順序下此時一定還沒到終態（終態要等落盤後才與放 slot 一起對外可見）；
        // 舊順序在這裡已是 succeeded 但 slot 仍佔著，此斷言會失敗
        Assert.Equal(SolverJobStatus.Running, view.Record.Status);
        release.Set();

        await WaitForTerminalAsync(service, created.Record.JobId);
        var next = await service.CreateAsync(Oct, 1, 1);
        await WaitForTerminalAsync(service, next.Record.JobId);
    }

    [Fact]
    public async Task SOLVER_BUSY_在佔到_slot_但還沒登記的空窗也帶_jobId()
    {
        var (service, store, _) = Setup();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var gated = new GatedScopeFactory(store, entered, release);
        service = new SolverJobService(gated, new FakeSolver((r, _) => Task.FromResult(OneDuty(r))), TimeProvider.System);

        // 第一個工作卡在撈資料（slot 已佔、尚未進 _live）
        var firstTask = Task.Run(() => service.CreateAsync(Oct, 1, 1));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));

        var ex = await Assert.ThrowsAsync<SchedulerException>(() => service.CreateAsync(Oct, 1, 1));
        Assert.Equal(ErrorCode.SolverBusy, ex.Code);
        var busyJobId = Assert.IsType<string>(ex.Details!["jobId"]);

        release.Set();
        var first = await firstTask;
        Assert.Equal(first.Record.JobId, busyJobId);
        await WaitForTerminalAsync(service, first.Record.JobId);
    }

    /// <summary>第 <paramref name="gateAtCall"/> 次 <see cref="Create"/> 卡住直到放行，其餘直接轉給記憶體 store。</summary>
    private sealed class GatedScopeFactory(ISolverScopeFactory inner, ManualResetEventSlim entered, ManualResetEventSlim release, int gateAtCall = 1) : ISolverScopeFactory
    {
        private int _calls;

        public ISolverScope Create()
        {
            if (Interlocked.Increment(ref _calls) == gateAtCall)
            {
                entered.Set();
                release.Wait(TimeSpan.FromSeconds(10));
            }

            return inner.Create();
        }
    }

    [Fact]
    public async Task 中止遇上工作剛好結束_不丟_ObjectDisposedException_回終態()
    {
        // 空窗：CancelAsync 已從 _live 拿到工作、尚未 Cancel；此時讓工作跑完並 Dispose CancellationTokenSource。
        // 假求解器卡在閘門上（不看 ct，放行後照常成功），保證 CancelAsync 查表時工作還在 _live；
        // 縫的回呼先開閘門、再等 RunTask 完成（RunAsync 的 finally 已 Dispose），才放行 Cancel，確定性地踩進去
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var (service, store, _) = Setup(async (r, _) =>
        {
            await release.Task;
            return OneDuty(r);
        });
        var created = await service.CreateAsync(Oct, 1, 1);
        var hit = false;
        service.AfterLiveLookup = runTask =>
        {
            hit = true;
            release.SetResult();
            Assert.True(runTask!.Wait(TimeSpan.FromSeconds(10)), "工作沒有在時限內收尾");
        };

        var view = await service.CancelAsync(created.Record.JobId);

        Assert.Equal(SolverJobStatus.Succeeded, view.Record.Status);
        Assert.Equal(SolverJobStatus.Succeeded, store.Jobs[created.Record.JobId].Status);
        Assert.True(hit, "空窗沒踩到：CancelAsync 查表時工作已不在 _live");
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
    public async Task 指標依約束自己的範圍算_S2_豁免的_NP_不進同區延續()
    {
        // NP 在 A、B 兩區混值：S2 豁免 NP，areaConsistency 不該把他算進去（目標函數也沒有他的項）
        var (service, store, _) = Setup((r, _) => Task.FromResult(new SolveResult(
            SolveStatus.Feasible, new[] { new Duty("area-a", D(1), "np"), new Duty("area-b", D(5), "np") }, 1, 0, 0, 10)));
        store.WithStaff("np", DefaultRanks.NP);

        var created = await service.CreateAsync(Oct, 1, 1);
        await WaitForTerminalAsync(service, created.Record.JobId);

        Assert.Equal(0, Assert.Single(store.Variants).Metrics.AreaConsistency);
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
    public async Task 全域訂閱_不分工作_每筆帶_jobId_含終態_不會自己結束()
    {
        var gate = new TaskCompletionSource();
        var (service, _, _) = Setup(async (r, ct) =>
        {
            await gate.Task.WaitAsync(ct);
            return OneDuty(r);
        });

        using var stop = new CancellationTokenSource();
        var events = new List<SolverProgressSnapshot>();
        var reading = Task.Run(async () =>
        {
            await foreach (var e in service.SubscribeAllAsync(stop.Token))
            {
                events.Add(e);
            }
        });

        var created = await service.CreateAsync(Oct, 1, 1);
        await Task.Delay(50);
        gate.SetResult();

        // 工作結束後序列還活著：只有呼叫端的 token 收得掉
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline && !events.Any(e => e.Status == SolverJobStatus.Succeeded))
        {
            await Task.Delay(20);
        }

        Assert.False(reading.IsCompleted);
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reading.WaitAsync(TimeSpan.FromSeconds(10)));

        Assert.NotEmpty(events);
        Assert.All(events, e => Assert.Equal(created.Record.JobId, e.JobId));
        Assert.Equal(SolverJobStatus.Succeeded, events[^1].Status);
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
