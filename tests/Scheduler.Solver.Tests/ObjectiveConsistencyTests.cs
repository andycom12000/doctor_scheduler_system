using Scheduler.Application.Solving;
using Scheduler.Domain.Constraints;
using Scheduler.Domain.Defaults;
using Scheduler.Domain.Scheduling;
using Scheduler.Domain.Tests;

namespace Scheduler.Solver.Tests;

/// <summary>
/// 目標函數的方向守門（#21）：Solver 最小化的目標值必須等於「1e9 × 空缺數 + 100 × Domain 重算的軟分數」。
/// 硬違規的漂移由 <see cref="DriftGateTests"/> 守；這裡守的是軟項——任何一項符號建反、權重換算不一致、
/// 或 Solver 與 <see cref="ScheduleScores"/> 對同一原語的算法不同，等式就會破。
/// 出廠軟約束的範圍只有身分，所以對應是精確的（度量累計含區域類型／日類範圍時 Solver 與檢查器有微小差異，見 ScheduleModel.MetricTotal）。
/// </summary>
public sealed class ObjectiveConsistencyTests
{
    private const long VacancyPenalty = 1_000_000_000L;
    private const long WeightScale = 100;

    [Fact]
    public async Task 參考名單_預設約束_目標值等於空缺罰分加一百倍軟分數()
    {
        var ctx = new ContextBuilder().WithReferenceRoster().Build();

        await AssertObjectiveMatchesDomainAsync(ctx, DefaultConstraints.Settings, TimeSpan.FromSeconds(6));
    }

    [Fact]
    public async Task 公平性點數啟用_唯一的_reified_項也在同一把尺上()
    {
        var ctx = new ContextBuilder().WithReferenceRoster().Build();
        var settings = DefaultConstraints.Settings.With("S7_FAIRNESS_POINT", c => c with { Weight = 50 });

        await AssertObjectiveMatchesDomainAsync(ctx, settings, TimeSpan.FromSeconds(6));
    }

    [Fact]
    public async Task 人不夠排滿_空缺罰分與軟分數同時存在()
    {
        // 4 位 R1 只撐得起病房：其他區整月空缺，目標值裡 1e9 的那一段不是 0
        var b = new ContextBuilder();
        for (var i = 1; i <= 4; i++)
        {
            b.WithStaff($"R1-{i}", DefaultRanks.R1);
        }

        var ctx = b.Build();
        var result = await AssertObjectiveMatchesDomainAsync(ctx, DefaultConstraints.Settings, TimeSpan.FromSeconds(4));
        Assert.True(SolverFixture.Vacancies(ctx, result.Duties) > 0);
    }

    private static async Task<SolveResult> AssertObjectiveMatchesDomainAsync(SchedulingContext ctx, ConstraintSettings settings, TimeSpan limit)
    {
        var progress = new List<SolveProgress>();
        var result = await SolverFixture.SolveAsync(ctx, settings, limit: limit, onProgress: p => { lock (progress) { progress.Add(p); } });

        Assert.Contains(result.Status, new[] { SolveStatus.Optimal, SolveStatus.Feasible });
        Assert.NotNull(result.Objective);
        Assert.NotNull(result.Bound);

        // 進度回呼每找到一個更好的解就推一次，最後一次的解數要與結果一致；結果不會比最後一次進度差（時限邊緣
        // 可能回一個沒經過回呼的更好的解），bound 只會收緊、不會鬆
        Assert.NotEmpty(progress);
        Assert.True(progress[0].SolutionCount >= 1);
        Assert.Equal(result.SolutionCount, progress[^1].SolutionCount);
        Assert.True(result.Objective <= progress[^1].BestObjective + 1e-6, $"結果目標值 {result.Objective} 比最後一次進度 {progress[^1].BestObjective} 差");
        Assert.True(result.Bound <= result.Objective + 1e-6);

        SolverFixture.AssertOnlyCoverageViolations(ctx, result.Duties, settings);
        var solved = ctx.WithDuties(result.Duties);
        var vacancies = SolverFixture.Vacancies(ctx, result.Duties);
        var softScore = VariantScoring.SoftScore(solved, settings);
        var expected = VacancyPenalty * vacancies + WeightScale * (long)Math.Round(softScore);

        Assert.True(expected == (long)Math.Round(result.Objective!.Value),
            $"Solver 目標值 {result.Objective} ≠ 1e9×{vacancies} + 100×{softScore}（Domain 重算），建模與 Domain 定義漂移；status={result.Status}");
        return result;
    }
}
