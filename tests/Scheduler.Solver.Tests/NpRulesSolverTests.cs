using Scheduler.Domain.Defaults;
using Scheduler.Domain.Model;
using Scheduler.Domain.Tests;

namespace Scheduler.Solver.Tests;

/// <summary>
/// NP 的四條專屬規則在求解器端。真實資料下 NP 幾乎不會被排到，這裡把 NP 逼成唯一人選，
/// 四條規則才會真的被建模碰到：H6 每月 20 天、H7 最多連六、H4 豁免（可以連值）、S5/S6 只是軟項。
/// Domain 端的對應測試在 <c>Scheduler.Domain.Tests/NpRulesTests</c>。
/// </summary>
public sealed class NpRulesSolverTests
{
    [Fact]
    public async Task NP_獨撐病房_排滿_20_天_連續不超過六天_而且可以連值()
    {
        var b = new ContextBuilder().WithStaff("NP-1", DefaultRanks.NP);
        var ctx = b.Build();

        var result = await SolverFixture.SolveAsync(ctx, limit: TimeSpan.FromSeconds(4));

        SolverFixture.AssertOnlyCoverageViolations(ctx, result.Duties);
        Assert.Equal(20, result.Duties.Count);
        Assert.All(result.Duties, d => Assert.Equal(DefaultAreas.Ward, ctx.AreaOf(d).AreaTypeCode));

        var dates = result.Duties.Select(d => d.Date).OrderBy(d => d).ToArray();
        Assert.True(LongestRun(dates) <= 6, $"最長連值 {LongestRun(dates)} 天");
        // 30 天排 20 天、每段最多 6 天，必然有連續兩天：H4 對 NP 豁免確實生效
        Assert.True(LongestRun(dates) >= 2);
    }

    [Fact]
    public async Task NP_上月已連六_月初第一天不能再值()
    {
        var b = new ContextBuilder().WithStaff("NP-1", DefaultRanks.NP);
        foreach (var day in new[] { 26, 27, 28, 29, 30, 31 })
        {
            b.WithPreviousMonthDuty("NP-1", day);
        }

        var ctx = b.Build();
        var result = await SolverFixture.SolveAsync(ctx);

        SolverFixture.AssertOnlyCoverageViolations(ctx, result.Duties);
        Assert.DoesNotContain(result.Duties, d => d.Date == b.Day(1));
        Assert.Equal(20, result.Duties.Count);
    }

    [Fact]
    public async Task NP_避開假日_有平日可選時不排週末()
    {
        // 30 天裡平日 22 天，20 天的額度光平日就夠：S6（避開假日）會讓解全部落在平日
        var b = new ContextBuilder().WithStaff("NP-1", DefaultRanks.NP);
        var ctx = b.Build();

        var result = await SolverFixture.SolveAsync(ctx, limit: TimeSpan.FromSeconds(4));

        SolverFixture.AssertOnlyCoverageViolations(ctx, result.Duties);
        Assert.Equal(20, result.Duties.Count);
        Assert.All(result.Duties, d => Assert.False(ctx.DayOf(d).IsHoliday, $"{d.Date} 是假日"));
    }

    [Fact]
    public async Task NP_是後備_有別人可用時不會被排()
    {
        // 病房三區、一位 NP 加三位 R1：R1 有值休休所以三個人撐不滿三區，缺口才輪到 NP
        var b = new ContextBuilder()
            .WithStaff("NP-1", DefaultRanks.NP)
            .WithStaff("R1-1", DefaultRanks.R1)
            .WithStaff("R1-2", DefaultRanks.R1)
            .WithStaff("R1-3", DefaultRanks.R1)
            .WithStaff("PGY2-1", DefaultRanks.PGY2)
            .WithStaff("PGY2-2", DefaultRanks.PGY2)
            .WithStaff("PGY2-3", DefaultRanks.PGY2)
            .WithStaff("PGY2-4", DefaultRanks.PGY2)
            .WithStaff("PGY1-1", DefaultRanks.PGY1)
            .WithStaff("PGY1-2", DefaultRanks.PGY1);
        var ctx = b.Build();

        var result = await SolverFixture.SolveAsync(ctx, limit: TimeSpan.FromSeconds(4));

        SolverFixture.AssertOnlyCoverageViolations(ctx, result.Duties);
        // 9 位醫師的額度合計 27 + 36 + 20 = 83 點，病房三區整月需求約 90 × 1.3 點：NP 一定會被用到，但只該補缺口
        var npDuties = result.Duties.Count(d => d.StaffId == "NP-1");
        var doctorDuties = result.Duties.Count(d => d.StaffId != "NP-1");
        Assert.True(doctorDuties > npDuties * 2, $"醫師 {doctorDuties} 格、NP {npDuties} 格：NP 不該是主力");
    }

    private static int LongestRun(DateOnly[] sorted)
    {
        var best = 0;
        var run = 0;
        for (var i = 0; i < sorted.Length; i++)
        {
            run = i > 0 && sorted[i].DayNumber == sorted[i - 1].DayNumber + 1 ? run + 1 : 1;
            best = Math.Max(best, run);
        }

        return best;
    }
}
