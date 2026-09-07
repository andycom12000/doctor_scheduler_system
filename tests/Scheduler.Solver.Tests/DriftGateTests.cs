using Scheduler.Application.Solving;
using Scheduler.Domain.Constraints;
using Scheduler.Domain.Defaults;
using Scheduler.Domain.Model;
using Scheduler.Domain.Tests;
using Scheduler.Domain.Validation;

namespace Scheduler.Solver.Tests;

/// <summary>
/// ADR-0002 的自動化守門（ARCHITECTURE §4.8）：Solver 的任一輸出丟給 <see cref="ViolationChecker"/>，
/// 硬違規必須為零，唯一允許的例外是覆蓋（空缺）。每個 Solver PR 必過。
/// </summary>
public sealed class DriftGateTests
{
    [Fact]
    public async Task 參考名單_預設約束_排得滿且沒有任何硬違規()
    {
        var ctx = new ContextBuilder().WithReferenceRoster().Build();

        var result = await SolverFixture.SolveAsync(ctx, limit: TimeSpan.FromSeconds(6));

        Assert.Contains(result.Status, new[] { SolveStatus.Optimal, SolveStatus.Feasible });
        var check = SolverFixture.AssertOnlyCoverageViolations(ctx, result.Duties);
        Assert.Equal(0, check.HardCount);
        Assert.Equal(0, SolverFixture.Vacancies(ctx, result.Duties));
        Assert.Equal(ctx.Areas.Count * ctx.Month.DayCount, result.Duties.Count);
        Assert.True(result.SolutionCount > 0);
        Assert.NotNull(result.Objective);
    }

    [Fact]
    public async Task 只有停用者與不合資格者_不會被排_空缺留著()
    {
        // R1 只能值病房；停用的 R4 不能用。ICU 與總值必定空缺，但不會硬塞不合資格的人
        var ctx = new ContextBuilder()
            .WithStaff("R1-1", DefaultRanks.R1)
            .WithInactiveStaff("R4-1", DefaultRanks.R4)
            .Build();

        var result = await SolverFixture.SolveAsync(ctx);

        SolverFixture.AssertOnlyCoverageViolations(ctx, result.Duties);
        Assert.DoesNotContain(result.Duties, d => d.StaffId == "R4-1");
        Assert.All(result.Duties, d => Assert.Equal(DefaultAreas.Ward, ctx.AreaOf(d).AreaTypeCode));
        Assert.True(SolverFixture.Vacancies(ctx, result.Duties) >= 2 * ctx.Month.DayCount, "ICU 與總值整月都該空著");
    }

    [Fact]
    public async Task 登記爆量_那一天全空缺_而不是整份無解()
    {
        var b = new ContextBuilder().WithReferenceRoster();
        var ctx0 = b.Build();
        foreach (var s in ctx0.Staff)
        {
            b.WithBlockedDay(s.Id, 15);
        }

        var ctx = b.Build();
        var result = await SolverFixture.SolveAsync(ctx);

        Assert.NotEqual(SolveStatus.Infeasible, result.Status);
        var check = SolverFixture.AssertOnlyCoverageViolations(ctx, result.Duties);
        Assert.DoesNotContain(result.Duties, d => d.Date == b.Day(15));
        Assert.Equal(ctx.Areas.Count, check.Violations.Count(v => v.Code == "H1_AREA_COVERAGE" && v.CellKeys[0].EndsWith("2026-09-15", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task 跨月尾巴_值休休把月初擋掉()
    {
        // 8/31 值過班，H4（間隔 ≥ 3 天）→ 9/1、9/2 不能排；只有他一個人，病房 A 的 9/1、9/2 只能空著
        var b = new ContextBuilder().WithStaff("R1-1", DefaultRanks.R1).WithPreviousMonthDuty("R1-1", 31);
        var ctx = b.Build();

        var result = await SolverFixture.SolveAsync(ctx);

        SolverFixture.AssertOnlyCoverageViolations(ctx, result.Duties);
        Assert.DoesNotContain(result.Duties, d => d.Date == b.Day(1) || d.Date == b.Day(2));
        Assert.NotEmpty(result.Duties);
    }

    [Fact]
    public async Task 關掉資格約束_才會把不合資格的人排進去()
    {
        // 覆蓋只算 ICU 與總值：關掉 H2 之後，R1 唯一能減少空缺的方法就是去值他本來沒資格的區
        var ctx = new ContextBuilder().WithStaff("R1-1", DefaultRanks.R1).Build();
        var relaxed = DefaultConstraints.Settings
            .With("H2_ELIGIBILITY", c => c with { Enabled = false })
            .With("H1_AREA_COVERAGE", c => c with { Scope = ConstraintScope.All.InAreaTypes(DefaultAreas.Icu, DefaultAreas.Chief) });

        var result = await SolverFixture.SolveAsync(ctx, relaxed);

        // 對「關掉 H2」的設定檢查：沒有硬違規；對出廠設定檢查：會有 H2 違規——建模確實讀了定義而不是寫死矩陣
        SolverFixture.AssertOnlyCoverageViolations(ctx, result.Duties, relaxed);
        Assert.Contains(result.Duties, d => ctx.AreaOf(d).AreaTypeCode != DefaultAreas.Ward);
        var strict = new ViolationChecker(ctx.WithDuties(result.Duties)).Check(DefaultConstraints.Settings);
        Assert.Contains(strict.Violations, v => v.Code == "H2_ELIGIBILITY");
    }

    [Fact]
    public async Task 多樣性_第二份與第一份至少差指定格數()
    {
        var ctx = new ContextBuilder().WithReferenceRoster().Build();
        var first = await SolverFixture.SolveAsync(ctx);
        Assert.NotEmpty(first.Duties);

        var second = await SolverFixture.SolveAsync(ctx, avoid: new[] { first.Duties }, minDifferent: 15);

        SolverFixture.AssertOnlyCoverageViolations(ctx, second.Duties);
        var a = first.Duties.ToDictionary(d => (d.AreaId, d.Date), d => d.StaffId);
        var bb = second.Duties.ToDictionary(d => (d.AreaId, d.Date), d => d.StaffId);
        var differing = ctx.Areas.Sum(area => ctx.Month.Days().Count(date =>
            a.GetValueOrDefault((area.Id, date)) != bb.GetValueOrDefault((area.Id, date))));
        Assert.True(differing >= 15, $"只差了 {differing} 格");
    }

    [Fact]
    public async Task 多樣性_做不到時回無解_由呼叫端決定要不要放棄()
    {
        // 只有一個人、只有病房 A 可排；能變的格子少於 15 格時要求差 15 格是做不到的
        var ctx = new ContextBuilder().WithStaff("R1-1", DefaultRanks.R1).Build();
        var first = await SolverFixture.SolveAsync(ctx);
        var pinned = DefaultConstraints.Settings;

        var second = await SolverFixture.SolveAsync(ctx, pinned, avoid: new[] { first.Duties }, minDifferent: 3 * ctx.Areas.Count * ctx.Month.DayCount);

        Assert.Equal(SolveStatus.Infeasible, second.Status);
        Assert.Empty(second.Duties);
    }

    [Fact]
    public async Task 中止_很快就回來_狀態是_Cancelled()
    {
        var ctx = new ContextBuilder().WithReferenceRoster().Build();
        using var cts = new CancellationTokenSource();
        var task = SolverFixture.SolveAsync(ctx, limit: TimeSpan.FromSeconds(60), cancellationToken: cts.Token);

        await Task.Delay(500);
        cts.Cancel();
        var finished = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(15)));

        Assert.Same(task, finished);
        var result = await task;
        Assert.Equal(SolveStatus.Cancelled, result.Status);
        if (result.Duties.Count > 0)
        {
            SolverFixture.AssertOnlyCoverageViolations(ctx, result.Duties);
        }
    }

    [Fact]
    public async Task 額度上限_月結轉偏移不影響硬上限_只影響公平()
    {
        // 一個 R1（上限 9 點）獨撐病房 A：整月 30 天平日 1、假日 2 遠超上限，只能排到 9 點
        var b = new ContextBuilder().WithStaff("R1-1", DefaultRanks.R1).WithCarryOver("R1-1", 3);
        var ctx = b.Build();

        var result = await SolverFixture.SolveAsync(ctx);

        SolverFixture.AssertOnlyCoverageViolations(ctx, result.Duties);
        var points = result.Duties.Sum(d => ctx.PointRules.Quota.ValueOf(ctx.DayOf(d)));
        Assert.True(points <= 9, $"排了 {points} 點");
        Assert.True(points >= 8, $"只排了 {points} 點，額度沒用滿");
    }

    [Fact]
    public async Task 公平性點數啟用_連值週六加分的建模不漂移()
    {
        // S7 出廠停用；打開後才會建唯一決策相依的項（週六 ∧ 下週六）。這裡守它的 reification 沒建錯
        var ctx = new ContextBuilder().WithReferenceRoster().Build();
        var settings = DefaultConstraints.Settings.With("S7_FAIRNESS_POINT", c => c with { Weight = 50 });

        var result = await SolverFixture.SolveAsync(ctx, settings, limit: TimeSpan.FromSeconds(6));

        Assert.Contains(result.Status, new[] { SolveStatus.Optimal, SolveStatus.Feasible });
        var check = SolverFixture.AssertOnlyCoverageViolations(ctx, result.Duties, settings);
        Assert.Equal(0, check.HardCount);
        var scores = new ScheduleScores(ctx.WithDuties(result.Duties));
        Assert.True(scores.FairnessByGroup(ConstraintScope.All, Metric.FairnessPoint).Values.Sum() < 40);
    }

    [Fact]
    public async Task 額度公平_同組剩餘額度差距被壓小()
    {
        // 同組 4 位 R1 撐病房 A（30 格、每人上限 9 點、需求約 39 點）：S1 讓四人的剩餘額度差距不超過 2
        var b = new ContextBuilder();
        for (var i = 1; i <= 4; i++)
        {
            b.WithStaff($"R1-{i}", DefaultRanks.R1);
        }

        var ctx = b.Build();
        var result = await SolverFixture.SolveAsync(ctx, limit: TimeSpan.FromSeconds(4));

        SolverFixture.AssertOnlyCoverageViolations(ctx, result.Duties);
        var spread = new ScheduleScores(ctx.WithDuties(result.Duties)).FairnessByGroup(ConstraintScope.All, Metric.QuotaPoint);
        Assert.True(spread[DefaultRanks.Junior] <= 2, $"低年級組剩餘額度差距 {spread[DefaultRanks.Junior]}");
    }
}
