using Scheduler.Application.Errors;
using Scheduler.Application.Schedules;
using Scheduler.Domain.Constraints;
using Scheduler.Domain.Defaults;
using Scheduler.Domain.Model;

namespace Scheduler.Application.Tests;

public class ScheduleQueriesTests
{
    private static readonly YearMonth Sep = new(2026, 9);
    private static readonly YearMonth Oct = new(2026, 10);

    private static ScheduleQueries QueriesOf(InMemoryStore store) => new(store, store.Loader);

    [Fact]
    public async Task 該月沒有值班表_讀取類全部_NOT_FOUND()
    {
        var store = new InMemoryStore().WithStaff("s1", DefaultRanks.R2);
        var q = QueriesOf(store);
        var d1 = new DateOnly(2026, 10, 1);

        foreach (var call in new Func<Task>[]
        {
            () => q.GetAsync(Oct),
            () => q.ValidateAsync(Oct),
            () => q.ListViolationsAsync(Oct, null, null),
            () => q.GetPointBoardAsync(Oct),
            () => q.GetDayDetailAsync(Oct, d1),
            () => q.ListVacanciesAsync(Oct),
            () => q.ListCandidatesAsync(Oct, "area-a", d1),
        })
        {
            var ex = await Assert.ThrowsAsync<SchedulerException>(call);
            Assert.Equal(ErrorCode.NotFound, ex.Code);
        }

        Assert.Empty(await q.ListAsync());
    }

    [Fact]
    public async Task 值班表本體_含_cellKey_在職人數_與天數()
    {
        var store = new InMemoryStore()
            .WithStaff("s1", DefaultRanks.R2)
            .WithStaff("s2", DefaultRanks.R3, StaffStatus.Inactive)
            .WithDraft(Sep)
            .WithDuty("area-icu", new DateOnly(2026, 9, 14), "s1");

        var view = await QueriesOf(store).GetAsync(Sep);

        Assert.Equal(30, view.DayCount);
        Assert.Equal(1, view.StaffCount);
        Assert.Equal(ScheduleStatus.Draft, view.Status);
        var duty = Assert.Single(view.Duties);
        Assert.Equal("area:area-icu:2026-09-14", duty.CellKey);
        Assert.Equal(5, view.Areas.Count);
    }

    [Fact]
    public async Task 清單附硬違規數_依月份排序()
    {
        var store = new InMemoryStore()
            .WithStaff("s1", DefaultRanks.R2)
            .WithDraft(Oct)
            .WithDraft(Sep)
            .WithDuty("area-a", new DateOnly(2026, 9, 1), "s1")
            .WithDuty("area-a", new DateOnly(2026, 9, 2), "s1"); // 值休休

        var list = await QueriesOf(store).ListAsync();

        Assert.Equal(new[] { Sep, Oct }, list.Select(s => s.YearMonth));
        Assert.True(list[0].HardViolationCount > 0);
        Assert.True(list[1].HardViolationCount > 0); // 10 月每格都空，覆蓋違規
    }

    [Fact]
    public async Task 驗證看得到跨月違規_歸屬後一個月()
    {
        var store = new InMemoryStore()
            .WithStaff("s1", DefaultRanks.R2)
            .WithPublished(Sep)
            .WithDuty("area-a", new DateOnly(2026, 9, 30), "s1")
            .WithDraft(Oct)
            .WithDuty("area-a", new DateOnly(2026, 10, 1), "s1");

        var result = await QueriesOf(store).ValidateAsync(Oct);

        var gap = Assert.Single(result.Violations, v => v.Code == DefaultConstraints.H4MinGap);
        Assert.Equal(new[] { "staff:s1:2026-10-01" }, gap.CellKeys); // 9/30 那格不在本月，不標
        Assert.False(result.Ok);
    }

    [Fact]
    public async Task 違規可依嚴重度與日期篩()
    {
        var store = new InMemoryStore()
            .WithStaff("s1", DefaultRanks.R2)
            .WithDraft(Sep)
            .WithDuty("area-a", new DateOnly(2026, 9, 1), "s1")
            .WithDuty("area-a", new DateOnly(2026, 9, 2), "s1");
        var q = QueriesOf(store);

        var hardOnly = await q.ListViolationsAsync(Sep, Severity.Hard, null);
        Assert.All(hardOnly, v => Assert.Equal(Severity.Hard, v.Severity));

        var sep2 = await q.ListViolationsAsync(Sep, null, new DateOnly(2026, 9, 2));
        Assert.NotEmpty(sep2);
        Assert.All(sep2, v => Assert.Contains(v.CellKeys, k => k.EndsWith(":2026-09-02")));
        Assert.DoesNotContain(sep2, v => v.Code == DefaultConstraints.H1AreaCoverage && v.CellKeys[0].EndsWith(":2026-09-03"));
    }

    [Fact]
    public async Task 點數看板_依身分組分區_只列在職_附凍結或即時的月結轉()
    {
        var store = new InMemoryStore()
            .WithStaff("r2", DefaultRanks.R2)
            .WithStaff("r5", DefaultRanks.R5)
            .WithStaff("np", DefaultRanks.NP)
            .WithStaff("gone", DefaultRanks.R2, StaffStatus.Inactive)
            .WithPublished(Sep, new CarryOverEntry("r2", 3))
            .WithDraft(Oct)
            .WithDuty("area-a", new DateOnly(2026, 10, 3), "r2") // 週六
            .WithDuty("area-a", new DateOnly(2026, 10, 7), "r2");

        var board = await QueriesOf(store).GetPointBoardAsync(Oct);

        var allRows = board.Groups.SelectMany(g => g.Rows).ToArray();
        Assert.Equal(new[] { "r2", "r5", "np" }.OrderBy(x => x), allRows.Select(r => r.StaffId).OrderBy(x => x));
        var r2 = allRows.Single(r => r.StaffId == "r2");
        Assert.Equal(3, r2.QuotaPoints); // 假日 2 + 平日 1
        Assert.Equal(2, r2.Duties);
        Assert.Equal(1, r2.HolidayDuties);
        Assert.Equal(3, r2.CarryOverApplied);
        Assert.Equal(r2.QuotaCap - 3, r2.QuotaRemaining);
        var np = allRows.Single(r => r.StaffId == "np");
        Assert.Null(np.QuotaCap);
        Assert.Null(np.QuotaRemaining);
        Assert.Null(np.FairnessPoints);
        Assert.Equal(DefaultRanks.Groups.Select(g => g.Code).Where(c => board.Groups.Any(g => g.GroupCode == c)), board.Groups.Select(g => g.GroupCode));
    }

    [Fact]
    public async Task 單日明細_每區一列_空的格子_staff_為_null()
    {
        var store = new InMemoryStore()
            .WithStaff("s1", DefaultRanks.R2)
            .WithDraft(Sep)
            .WithHoliday(new DateOnly(2026, 9, 28), "教師節")
            .WithDuty("area-icu", new DateOnly(2026, 9, 28), "s1");
        var q = QueriesOf(store);

        var detail = await q.GetDayDetailAsync(Sep, new DateOnly(2026, 9, 28));

        Assert.True(detail.IsHoliday);
        Assert.True(detail.IsPublicHoliday);
        Assert.Equal(2, detail.QuotaPointValue);
        Assert.Equal(5, detail.Areas.Count);
        var icu = detail.Areas.Single(a => a.AreaId == "area-icu");
        Assert.True(icu.Filled);
        Assert.Equal(2, icu.Staff!.MonthQuotaPoints);
        Assert.Equal(1, icu.Staff.MonthHolidayDuties);
        Assert.All(detail.Areas.Where(a => a.AreaId != "area-icu"), a => Assert.Null(a.Staff));

        var ex = await Assert.ThrowsAsync<SchedulerException>(() => q.GetDayDetailAsync(Sep, new DateOnly(2026, 10, 1)));
        Assert.Equal(ErrorCode.NotFound, ex.Code);
    }

    [Fact]
    public async Task 空缺_只列有空的日期()
    {
        var store = new InMemoryStore()
            .WithStaff("s1", DefaultRanks.R2)
            .WithStaff("s2", DefaultRanks.R2)
            .WithStaff("s3", DefaultRanks.R2)
            .WithStaff("s4", DefaultRanks.R2)
            .WithStaff("s5", DefaultRanks.R5)
            .WithDraft(Sep);
        var d1 = new DateOnly(2026, 9, 1);
        foreach (var (area, staff) in new[] { ("area-a", "s1"), ("area-b", "s2"), ("area-c", "s3"), ("area-icu", "s4"), ("area-chief", "s5") })
        {
            store.WithDuty(area, d1, staff);
        }

        var report = await QueriesOf(store).ListVacanciesAsync(Sep);

        Assert.Equal(29 * 5, report.Total);
        Assert.DoesNotContain(report.ByDate, v => v.Date == d1);
        Assert.Equal(5, report.ByDate.First().Count);
    }

    [Fact]
    public async Task 候選人_阻擋理由來自違規檢查器_不重寫規則()
    {
        var store = new InMemoryStore()
            .WithStaff("fresh", DefaultRanks.R2)
            .WithStaff("tired", DefaultRanks.R2)
            .WithStaff("busy", DefaultRanks.R3)
            .WithStaff("off", DefaultRanks.R3)
            .WithStaff("chief", DefaultRanks.R5) // 沒有 ICU 以外的資格差異：R5 可 ICU
            .WithStaff("np", DefaultRanks.NP)     // 沒 ICU 資格
            .WithStaff("gone", DefaultRanks.R2, StaffStatus.Inactive)
            .WithDraft(Sep)
            .WithDuty("area-a", new DateOnly(2026, 9, 9), "tired")   // 與 9/10 只差 1 天
            .WithDuty("area-b", new DateOnly(2026, 9, 10), "busy")   // 當日已在其他區
            .WithBlockedDay("off", new DateOnly(2026, 9, 10));

        var candidates = await QueriesOf(store).ListCandidatesAsync(Sep, "area-icu", new DateOnly(2026, 9, 10));

        var ids = candidates.Select(c => c.StaffId).ToArray();
        Assert.DoesNotContain("np", ids);
        Assert.DoesNotContain("gone", ids);
        Assert.Contains("chief", ids);

        Assert.Empty(candidates.Single(c => c.StaffId == "fresh").BlockingReasons);
        Assert.Contains("當日已排在其他區域", candidates.Single(c => c.StaffId == "busy").BlockingReasons);
        Assert.NotEmpty(candidates.Single(c => c.StaffId == "off").BlockingReasons);
        Assert.NotEmpty(candidates.Single(c => c.StaffId == "tired").BlockingReasons);

        // 沒有阻擋的排前面
        Assert.Empty(candidates[0].BlockingReasons);
        Assert.True(candidates.TakeWhile(c => c.BlockingReasons.Count == 0).Count() >= 2);
    }

    [Fact]
    public async Task 候選人_本人已超過額度上限_再指派仍算阻擋()
    {
        var store = new InMemoryStore().WithStaff("r6", DefaultRanks.R6).WithDraft(Sep);
        store.Overrides[Sep] = new MonthlyOverride(Sep, new Dictionary<string, int> { [DefaultRanks.R6] = 2 });
        store.WithDuty("area-chief", new DateOnly(2026, 9, 1), "r6")
             .WithDuty("area-chief", new DateOnly(2026, 9, 7), "r6")
             .WithDuty("area-chief", new DateOnly(2026, 9, 14), "r6"); // 已經 3 點，超過 2

        var candidates = await QueriesOf(store).ListCandidatesAsync(Sep, "area-chief", new DateOnly(2026, 9, 21));

        var r6 = Assert.Single(candidates);
        Assert.Contains(r6.BlockingReasons, r => r.Contains("額度"));
        Assert.Equal(2 - 3, r6.QuotaRemaining);
    }

    [Fact]
    public async Task 空缺_需求兩人只排一人算缺一()
    {
        var store = new InMemoryStore().WithStaff("s1", DefaultRanks.R2).WithDraft(Sep);
        store.Areas = new Application.Settings.AreaSettings(
            DefaultAreas.AreaTypes,
            new[] { new Area("area-a", "A", "A", DefaultAreas.Ward, RequiredPerDay: 2) });
        store.WithDuty("area-a", new DateOnly(2026, 9, 1), "s1");

        var report = await QueriesOf(store).ListVacanciesAsync(Sep);

        var sep1 = report.ByDate.Single(v => v.Date == new DateOnly(2026, 9, 1));
        Assert.Equal(1, sep1.Count);
        Assert.Equal(new[] { "area-a" }, sep1.AreaIds);
        Assert.Equal(1 + 29 * 2, report.Total);
    }

    [Fact]
    public async Task 候選人_已填的格子評估的是換人_現任者也在清單裡()
    {
        var store = new InMemoryStore()
            .WithStaff("cur", DefaultRanks.R2)
            .WithStaff("alt", DefaultRanks.R2)
            .WithDraft(Sep)
            .WithDuty("area-a", new DateOnly(2026, 9, 10), "cur");

        var candidates = await QueriesOf(store).ListCandidatesAsync(Sep, "area-a", new DateOnly(2026, 9, 10));

        var cur = candidates.Single(c => c.StaffId == "cur");
        Assert.Empty(cur.BlockingReasons); // 不會被自己擋
        Assert.Equal(1.0, cur.AreaConsistency);
        Assert.Equal(0.0, candidates.Single(c => c.StaffId == "alt").AreaConsistency);
    }

    [Fact]
    public async Task 候選人_軟違規進警示_例如_NP_盡量不用()
    {
        var store = new InMemoryStore()
            .WithStaff("np", DefaultRanks.NP)
            .WithDraft(Sep);

        var candidates = await QueriesOf(store).ListCandidatesAsync(Sep, "area-a", new DateOnly(2026, 9, 10));

        var np = Assert.Single(candidates);
        Assert.Empty(np.BlockingReasons);
        Assert.NotEmpty(np.Warnings);
    }
}
