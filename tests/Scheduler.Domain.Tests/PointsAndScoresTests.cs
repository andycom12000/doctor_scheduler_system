using Scheduler.Domain.Constraints;
using Scheduler.Domain.Defaults;
using Scheduler.Domain.Model;
using Scheduler.Domain.Scheduling;
using Scheduler.Domain.Validation;

namespace Scheduler.Domain.Tests;

/// <summary>兩套點數與組層級分數。數字全部對照 docs/constraint-defaults.md 的表。</summary>
public class PointsAndScoresTests
{
    // ---- 額度點數 ----

    [Fact]
    public void 額度點數_平日1假日2_補班日視為平日()
    {
        var builder = new ContextBuilder().WithStaff("r1-1", DefaultRanks.R1)
            .WithDuty("r1-1", 1, "area-a")   // 週二
            .WithDuty("r1-1", 5, "area-a")   // 週六
            .WithDuty("r1-1", 12, "area-a"); // 週六 → 覆寫成補班日
        builder.WithCalendar(cal => cal.With(new CalendarDay(builder.Day(12), IsHoliday: false, IsPublicHoliday: false, IsMakeUpWorkday: true)));
        var metrics = new MetricEvaluator(builder.Build());

        Assert.Equal(1 + 2 + 1, metrics.QuotaPoints("r1-1"));
    }

    [Fact]
    public void 剩餘額度等於上限減已排減月結轉()
    {
        var ctx = new ContextBuilder().WithStaff("r2-1", DefaultRanks.R2)
            .WithDuty("r2-1", 1, "area-icu").WithDuty("r2-1", 5, "area-icu") // 1 + 2 = 3
            .WithCarryOver("r2-1", 2)
            .Build();

        Assert.Equal(8 - 3 - 2, new MetricEvaluator(ctx).QuotaRemaining("r2-1"));
    }

    [Fact]
    public void NP的剩餘額度與公平性點數皆為null()
    {
        var ctx = new ContextBuilder().WithStaff("np-1", DefaultRanks.NP).WithDuty("np-1", 1).Build();
        var metrics = new MetricEvaluator(ctx);

        Assert.Null(metrics.QuotaRemaining("np-1"));
        Assert.Null(metrics.FairnessPoints("np-1"));
    }

    // ---- 公平性點數 ----

    [Theory]
    [InlineData(1, 1, 2)]   // 週二 → 週三：平日/平日  A=1 B=2
    [InlineData(4, 2, 1)]   // 週五 → 週六：平日/假日  A=2 B=1
    [InlineData(5, 3, 2)]   // 週六 → 週日：假日/假日  A=3 B=2
    [InlineData(6, 2, 3)]   // 週日 → 週一：假日/平日  A=2 B=3
    public void 公平性點數查表_四種組合(int day, int typeA, int typeB)
    {
        var a = new ContextBuilder().WithStaff("s", DefaultRanks.R2).WithDuty("s", day, "area-icu").Build();
        var b = new ContextBuilder().WithStaff("s", DefaultRanks.R4).WithDuty("s", day, "area-icu").Build();

        Assert.Equal(typeA, new MetricEvaluator(a).FairnessPoints("s"));
        Assert.Equal(typeB, new MetricEvaluator(b).FairnessPoints("s"));
    }

    [Fact]
    public void 連值兩個週六_中間沒有國定假日_第一個週六加1()
    {
        // 9/5、9/12 都是週六，Type A 假日/假日 = 3；第一個週六加 bonus 1 → 3 + 1 + 3 = 7
        var ctx = new ContextBuilder().WithStaff("s", DefaultRanks.R2)
            .WithDuty("s", 5, "area-icu").WithDuty("s", 12, "area-icu")
            .Build();

        Assert.Equal(7, new MetricEvaluator(ctx).FairnessPoints("s"));
    }

    [Fact]
    public void 連值兩個週六_窗口內有國定假日就不加分_一般週日不算()
    {
        // 窗口 10 天含當日：9/5 ~ 9/14。9/9 設為國定假日 → 沒有 bonus。
        var builder = new ContextBuilder().WithStaff("s", DefaultRanks.R2)
            .WithDuty("s", 5, "area-icu").WithDuty("s", 12, "area-icu");
        builder.WithCalendar(cal => cal.With(new CalendarDay(builder.Day(9), IsHoliday: true, IsPublicHoliday: true, HolidayName: "測試假")));

        Assert.Equal(3 + 3, new MetricEvaluator(builder.Build()).FairnessPoints("s"));
    }

    [Fact]
    public void 連值兩個週六_國定假日落在窗口外仍加分()
    {
        // 9/15 是窗口（9/5 ~ 9/14）外的第一天
        var builder = new ContextBuilder().WithStaff("s", DefaultRanks.R2)
            .WithDuty("s", 5, "area-icu").WithDuty("s", 12, "area-icu");
        builder.WithCalendar(cal => cal.With(new CalendarDay(builder.Day(15), IsHoliday: true, IsPublicHoliday: true)));

        Assert.Equal(7, new MetricEvaluator(builder.Build()).FairnessPoints("s"));
    }

    // ---- Fairness 分數與月結轉 ----

    [Fact]
    public void 額度公平性_組內剩餘額度的max減min_不跨組_NP不計()
    {
        // MID：R2 上限 8 值 1 點 → 剩 7；R3 上限 7 值 0 → 剩 7；差 0
        // JUNIOR：PGY1 上限 10 值 0 → 剩 10；R1 上限 9 值 4 點 → 剩 5；差 5
        var ctx = new ContextBuilder()
            .WithStaff("r2", DefaultRanks.R2).WithStaff("r3", DefaultRanks.R3)
            .WithStaff("pgy1", DefaultRanks.PGY1).WithStaff("r1", DefaultRanks.R1)
            .WithStaff("np", DefaultRanks.NP)
            .WithDuty("r2", 1, "area-icu")
            .WithDuties("r1", "area-a", 5, 12) // 兩個週六 = 4 點
            .WithDuties("np", "area-b", 1, 2, 3)
            .Build();

        var scores = new ScheduleScores(ctx);
        var s1 = DefaultConstraints.Settings[DefaultConstraints.S1QuotaFairness];
        var byGroup = scores.FairnessByGroup(s1.Scope, Metric.QuotaPoint);

        Assert.Equal(0, byGroup[DefaultRanks.Mid]);
        Assert.Equal(5, byGroup[DefaultRanks.Junior]);
        Assert.False(byGroup.ContainsKey(DefaultRanks.NpGroup));
        Assert.Equal(5, scores.Fairness(s1));
    }

    [Fact]
    public void 月結轉_組內最大剩餘減本人剩餘_剩最多的人為0()
    {
        var ctx = new ContextBuilder()
            .WithStaff("pgy1", DefaultRanks.PGY1).WithStaff("r1", DefaultRanks.R1)
            .WithDuties("r1", "area-a", 5, 12)
            .Build();

        var carry = new ScheduleScores(ctx).SettleCarryOver(DefaultConstraints.Settings[DefaultConstraints.S1QuotaFairness].Scope)
            .ToDictionary(c => c.StaffId, c => c.Points);

        Assert.Equal(0, carry["pgy1"]);
        Assert.Equal(5, carry["r1"]);
    }

    // ---- Consistency ----

    [Fact]
    public void 延續性_值班數減最常值的那一區()
    {
        var ctx = new ContextBuilder().WithStaff("s", DefaultRanks.R1)
            .WithDuties("s", "area-a", 1, 4, 7)
            .WithDuties("s", "area-b", 10)
            .WithDuties("s", "area-c", 13)
            .Build();

        Assert.Equal(5 - 3, new ScheduleScores(ctx).ConsistencyOf("s"));
    }
}
