using Scheduler.Domain.Constraints;
using Scheduler.Domain.Defaults;
using Scheduler.Domain.Validation;

namespace Scheduler.Domain.Tests;

/// <summary>
/// NP 的專屬規則（H4 豁免、H6 每月天數、H7 最多連六、S5 盡量不用、S6 避開假日）。
/// NP 只有 1 人且是後備人力，真實資料下極少觸發，所以這組測試不能依賴真實名單——
/// 全部用 ContextBuilder 憑空造出剛好觸發的情境。
/// </summary>
public class NpRulesTests
{
    private const string Np = "np-1";
    private const string Pgy = "pgy1-1";

    private static readonly ConstraintSettings Defaults = DefaultConstraints.Settings;

    private static ContextBuilder Fixture() => new ContextBuilder()
        .WithStaff(Np, DefaultRanks.NP, "NP 小美")
        .WithStaff(Pgy, DefaultRanks.PGY1, "PGY 小明");

    // ---- H4 值休休值：NP 豁免 ----

    [Fact]
    public void H4_NP連兩天值班不算違規_同樣情境的PGY1會()
    {
        var ctx = Fixture()
            .WithDuty(Np, 1, "area-a").WithDuty(Np, 2, "area-a")
            .WithDuty(Pgy, 1, "area-b").WithDuty(Pgy, 2, "area-b")
            .Build();

        var result = new ViolationChecker(ctx).Check(Defaults);
        var h4 = result.Violations.Where(v => v.Code == DefaultConstraints.H4MinGap).ToArray();

        var only = Assert.Single(h4);
        Assert.Equal(new[] { $"staff:{Pgy}:2026-09-01", $"staff:{Pgy}:2026-09-02" }, only.CellKeys);
        Assert.DoesNotContain(h4, v => v.CellKeys.Any(k => k.Contains(Np, StringComparison.Ordinal)));
    }

    [Fact]
    public void H4_間隔剛好三天不違規_兩天違規()
    {
        // 值 1、休 2、休 3、值 4 → 差 3 天，合法；值 1、值 3 → 差 2 天，違規
        var ok = Fixture().WithDuty(Pgy, 1).WithDuty(Pgy, 4).Build();
        var bad = Fixture().WithDuty(Pgy, 1).WithDuty(Pgy, 3).Build();

        Assert.Empty(H4(ok));
        Assert.Single(H4(bad));

        static IEnumerable<Violation> H4(Scheduling.SchedulingContext ctx) =>
            new ViolationChecker(ctx).Check(Defaults).Violations.Where(v => v.Code == DefaultConstraints.H4MinGap);
    }

    [Fact]
    public void H4_跨月_上月30日值班本月1日再值_只標本月那格()
    {
        var ctx = Fixture()
            .WithPreviousMonthDuty(Pgy, 30)
            .WithDuty(Pgy, 1)
            .Build();

        var v = Assert.Single(new ViolationChecker(ctx).Check(Defaults).Violations, v => v.Code == DefaultConstraints.H4MinGap);
        Assert.Equal(new[] { $"staff:{Pgy}:2026-09-01" }, v.CellKeys);
    }

    // ---- H3 額度上限：NP 沒有上限 ----

    [Fact]
    public void H3_NP的額度上限為null_不論值幾天都不違規()
    {
        var builder = Fixture();
        for (var d = 1; d <= 20; d++)
        {
            builder.WithDuty(Np, d);
        }

        var result = new ViolationChecker(builder.Build()).Check(Defaults);

        Assert.DoesNotContain(result.Violations, v => v.Code == DefaultConstraints.H3QuotaCap);
    }

    // ---- H6 NP 每月最多 20 天 ----

    [Fact]
    public void H6_NP值20天不違規_第21天起標為超額()
    {
        var twenty = Fixture();
        var twentyOne = Fixture();
        for (var d = 1; d <= 20; d++)
        {
            twenty.WithDuty(Np, d);
            twentyOne.WithDuty(Np, d);
        }

        twentyOne.WithDuty(Np, 21);

        Assert.DoesNotContain(new ViolationChecker(twenty.Build()).Check(Defaults).Violations, v => v.Code == DefaultConstraints.H6NpMonthlyDays);

        var v = Assert.Single(new ViolationChecker(twentyOne.Build()).Check(Defaults).Violations, v => v.Code == DefaultConstraints.H6NpMonthlyDays);
        Assert.Equal(Severity.Hard, v.Severity);
        Assert.Equal(new[] { $"staff:{Np}:2026-09-21" }, v.CellKeys);
        Assert.Contains("21", v.Message, StringComparison.Ordinal);
        Assert.Contains("20", v.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void H6_只算NP_PGY1值21天不會撞到這條()
    {
        var builder = Fixture();
        for (var d = 1; d <= 21; d++)
        {
            builder.WithDuty(Pgy, d);
        }

        var result = new ViolationChecker(builder.Build()).Check(Defaults);

        // 他會撞 H3 與 H4，但不會撞 NP 專屬的 H6
        Assert.DoesNotContain(result.Violations, v => v.Code == DefaultConstraints.H6NpMonthlyDays);
        Assert.Contains(result.Violations, v => v.Code == DefaultConstraints.H3QuotaCap);
    }

    // ---- H7 NP 最多連六 ----

    [Fact]
    public void H7_NP連六不違規_連七標第七天()
    {
        var six = Fixture().WithDuties(Np, "area-a", 1, 2, 3, 4, 5, 6).Build();
        var seven = Fixture().WithDuties(Np, "area-a", 1, 2, 3, 4, 5, 6, 7).Build();

        Assert.DoesNotContain(new ViolationChecker(six).Check(Defaults).Violations, v => v.Code == DefaultConstraints.H7NpMaxConsecutive);

        var v = Assert.Single(new ViolationChecker(seven).Check(Defaults).Violations, v => v.Code == DefaultConstraints.H7NpMaxConsecutive);
        Assert.Equal(new[] { $"staff:{Np}:2026-09-07" }, v.CellKeys);
    }

    [Fact]
    public void H7_跨月_上月末三天加本月前四天_只標本月的第七天()
    {
        // 8/29、30、31 + 9/1、2、3、4 = 連 7 天；上限 6，超出的是 9/4
        var ctx = Fixture()
            .WithPreviousMonthDuty(Np, 29).WithPreviousMonthDuty(Np, 30).WithPreviousMonthDuty(Np, 31)
            .WithDuties(Np, "area-a", 1, 2, 3, 4)
            .Build();

        var v = Assert.Single(new ViolationChecker(ctx).Check(Defaults).Violations, v => v.Code == DefaultConstraints.H7NpMaxConsecutive);
        Assert.Equal(new[] { $"staff:{Np}:2026-09-04" }, v.CellKeys);
        Assert.Contains("7 天", v.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void H7_中間斷一天就重新起算()
    {
        var ctx = Fixture().WithDuties(Np, "area-a", 1, 2, 3, 4, 5, 6, 8, 9, 10, 11, 12, 13).Build();

        Assert.DoesNotContain(new ViolationChecker(ctx).Check(Defaults).Violations, v => v.Code == DefaultConstraints.H7NpMaxConsecutive);
    }

    // ---- S5 NP 盡量不用、S6 NP 避開假日 ----

    [Fact]
    public void S5_NP任何一次值班都是軟違規_平日不會多出S6()
    {
        var ctx = Fixture().WithDuty(Np, 1).Build(); // 9/1 週二
        // 其餘 149 格是空的，把 H1 關掉才看得出 S5 本身不影響 ok
        var settings = Defaults.With(DefaultConstraints.H1AreaCoverage, c => c with { Enabled = false });

        var result = new ViolationChecker(ctx).Check(settings);
        var codes = result.Violations.Where(v => v.CellKeys.Contains("area:area-a:2026-09-01")).Select(v => v.Code).ToArray();

        Assert.Contains(DefaultConstraints.S5NpLastResort, codes);
        Assert.DoesNotContain(DefaultConstraints.S6NpAvoidHoliday, codes);
        Assert.True(result.Ok, "S5 是軟約束，不影響 ok");
    }

    [Fact]
    public void S6_NP在週六值班_S5與S6同時成立()
    {
        var ctx = Fixture().WithDuty(Np, 5).Build(); // 9/5 週六

        var result = new ViolationChecker(ctx).Check(Defaults);
        var codes = result.Violations.Where(v => v.CellKeys.Contains("area:area-a:2026-09-05")).Select(v => v.Code).ToArray();

        Assert.Contains(DefaultConstraints.S5NpLastResort, codes);
        Assert.Contains(DefaultConstraints.S6NpAvoidHoliday, codes);
        Assert.All(result.Violations.Where(v => v.Code is DefaultConstraints.S5NpLastResort or DefaultConstraints.S6NpAvoidHoliday),
            v => Assert.Equal(Severity.Soft, v.Severity));
    }

    [Fact]
    public void S6_補班日視為平日_不算避開假日的違規()
    {
        var builder = Fixture().WithDuty(Np, 5);
        builder.WithCalendar(cal => cal.With(new Model.CalendarDay(builder.Day(5), IsHoliday: false, IsPublicHoliday: false, IsMakeUpWorkday: true)));

        var result = new ViolationChecker(builder.Build()).Check(Defaults);

        Assert.DoesNotContain(result.Violations, v => v.Code == DefaultConstraints.S6NpAvoidHoliday);
    }

    [Fact]
    public void S5權重設為0即停用()
    {
        var settings = Defaults.With(DefaultConstraints.S5NpLastResort, c => c with { Weight = 0 });
        var ctx = Fixture().WithDuty(Np, 1).Build();

        var result = new ViolationChecker(ctx).Check(settings);

        Assert.DoesNotContain(result.Violations, v => v.Code == DefaultConstraints.S5NpLastResort);
    }

    // ---- 範圍是資料，不是 if：把 NP 從 H4 的豁免名單拿掉，NP 就會被 H4 抓到 ----

    [Fact]
    public void 豁免名單是資料_拿掉NP後H4就會抓NP()
    {
        var settings = Defaults.With(DefaultConstraints.H4MinGap, c => c with { Scope = ConstraintScope.All });
        var ctx = Fixture().WithDuty(Np, 1).WithDuty(Np, 2).Build();

        var result = new ViolationChecker(ctx).Check(settings);

        Assert.Contains(result.Violations, v => v.Code == DefaultConstraints.H4MinGap && v.CellKeys.Contains($"staff:{Np}:2026-09-01"));
    }
}
