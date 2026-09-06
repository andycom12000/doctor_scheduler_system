using Scheduler.Application.BlockedDays;
using Scheduler.Application.Calendars;
using Scheduler.Application.People;
using Scheduler.Application.Persistence;
using Scheduler.Application.Scheduling;
using Scheduler.Domain.Defaults;
using Scheduler.Domain.Model;

namespace Scheduler.Application.Tests;

public class BlockedDayQueriesTests
{
    private static readonly YearMonth Oct = new(2026, 10);

    [Fact]
    public async Task 登記表_沒有值班表也回得來_在職每人一列()
    {
        var store = new InMemoryStore()
            .WithStaff("a", DefaultRanks.R2)
            .WithStaff("b", DefaultRanks.R3)
            .WithStaff("gone", DefaultRanks.R3, StaffStatus.Inactive)
            .WithBlockedDay("a", new DateOnly(2026, 10, 5))
            .WithBlockedDay("a", new DateOnly(2026, 10, 6))
            .WithBlockedDay("b", new DateOnly(2026, 10, 5))
            .WithBlockedDay("a", new DateOnly(2026, 11, 1)); // 別的月份

        var reg = await new BlockedDayQueries(store.Loader).GetRegistrationAsync(Oct);

        Assert.Equal(DefaultPointRules.BlockedDayMonthlyCap, reg.MonthlyCap);
        Assert.Equal(3, reg.Entries.Count);
        Assert.Equal(new[] { "a", "b" }, reg.ByStaff.Select(s => s.StaffId));
        Assert.Equal((2, 14), (reg.ByStaff[0].Count, reg.ByStaff[0].Remaining));
        Assert.Equal(new[] { (new DateOnly(2026, 10, 5), 2), (new DateOnly(2026, 10, 6), 1) }, reg.ByDate.Select(d => (d.Date, d.Count)));
    }

    [Fact]
    public async Task 可行性_層次由資格矩陣推出_出廠值是總值_ICU_病房()
    {
        var store = new InMemoryStore().WithStaff("a", DefaultRanks.R2);
        var loaded = await store.Loader.LoadAsync(Oct);

        var tiers = BlockedDayQueries.TiersOf(loaded, loaded.Context);

        Assert.Equal(new[] { DefaultAreas.Chief, DefaultAreas.Icu, DefaultAreas.Ward }, tiers);
    }

    [Fact]
    public async Task 可行性_逐日缺口與巢狀供需()
    {
        var store = new InMemoryStore()
            .WithStaff("r5", DefaultRanks.R5)
            .WithStaff("r2", DefaultRanks.R2)
            .WithStaff("np", DefaultRanks.NP)
            .WithBlockedDay("r5", new DateOnly(2026, 10, 5));

        var report = await new BlockedDayQueries(store.Loader).GetFeasibilityAsync(Oct);

        Assert.False(report.Feasible);
        var oct5 = report.ByDate.Single(d => d.Date == new DateOnly(2026, 10, 5));
        var chief = oct5.Shortages.Single(s => s.AreaTypeCode == DefaultAreas.Chief);
        Assert.Equal((1, 0), (chief.Required, chief.AvailableStaff));

        Assert.Equal(3, report.BySupply.Count);
        var top = report.BySupply[0];
        Assert.Equal(new[] { DefaultAreas.Chief }, top.AreaTypeCodes);
        var r5Cap = DefaultRanks.Ranks.Single(r => r.Code == DefaultRanks.R5).QuotaCap!.Value;
        Assert.Equal(r5Cap - 1, top.SupplyPoints); // 10/5 是平日，扣 1 點
        var octDemand = Oct.Days().Sum(d => DefaultPointRules.Rules.Quota.ValueOf(CalendarDay.Plain(d)));
        Assert.Equal(octDemand, top.DemandPoints);
        Assert.Equal(top.SupplyPoints - top.DemandPoints, top.Headroom);

        // 最寬那層：NP 沒有額度上限，不算供給
        var all = report.BySupply[2];
        var r2Cap = DefaultRanks.Ranks.Single(r => r.Code == DefaultRanks.R2).QuotaCap!.Value;
        Assert.Equal(r5Cap - 1 + r2Cap, all.SupplyPoints);
        Assert.Contains(SchedulingContextLoader.PreviousMonthNotPublishedWarning, report.Warnings);
    }
}

public class StaffQueriesTests
{
    [Fact]
    public async Task 人員清單_資格由矩陣推導_計數不受篩選影響()
    {
        var store = new InMemoryStore()
            .WithStaff("a", DefaultRanks.R2)
            .WithStaff("b", DefaultRanks.R5, StaffStatus.Inactive);
        var q = new StaffQueries(store, store);

        var all = await q.ListAsync(null);
        Assert.Equal(2, all.Items.Count);
        Assert.Equal((1, 1), (all.Counts.Active, all.Counts.Inactive));
        Assert.Equal(DefaultRanks.Eligibility.EligibleAreaTypes(DefaultRanks.R2), all.Items.Single(s => s.Id == "a").EligibleAreaTypes);

        var inactive = await q.ListAsync(StaffStatus.Inactive);
        Assert.Equal("b", Assert.Single(inactive.Items).Id);
        Assert.Equal((1, 1), (inactive.Counts.Active, inactive.Counts.Inactive));
    }
}

public class CalendarQueriesTests
{
    [Fact]
    public async Task 整年逐日_例外日蓋上去_覆寫旗標帶出來()
    {
        var store = new InMemoryStore().WithHoliday(new DateOnly(2026, 9, 28), "教師節");
        store.CalendarExceptions[new DateOnly(2026, 8, 12)] = new CalendarException(
            new CalendarDay(new DateOnly(2026, 8, 12), IsHoliday: true, IsPublicHoliday: true, HolidayName: "颱風假"), Overridden: true);

        var year = await new CalendarQueries(store, store).GetYearAsync(2026);

        Assert.Equal(365, year.Days.Count);
        var teachers = year.Days.Single(d => d.Day.Date == new DateOnly(2026, 9, 28));
        Assert.Equal((true, true, 2, false), (teachers.Day.IsHoliday, teachers.Day.IsPublicHoliday, teachers.QuotaPointValue, teachers.Overridden));
        var typhoon = year.Days.Single(d => d.Day.Date == new DateOnly(2026, 8, 12));
        Assert.True(typhoon.Overridden);
        var plainSat = year.Days.Single(d => d.Day.Date == new DateOnly(2026, 9, 5));
        Assert.Equal((true, false, 2), (plainSat.Day.IsHoliday, plainSat.Day.IsPublicHoliday, plainSat.QuotaPointValue));

        Assert.Equal(366, (await new CalendarQueries(store, store).GetYearAsync(2028)).Days.Count);
    }
}
