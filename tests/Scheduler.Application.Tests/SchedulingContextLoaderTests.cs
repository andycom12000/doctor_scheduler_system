using Scheduler.Application.Scheduling;
using Scheduler.Domain.Constraints;
using Scheduler.Domain.Defaults;
using Scheduler.Domain.Model;

namespace Scheduler.Application.Tests;

public class SchedulingContextLoaderTests
{
    private static readonly YearMonth Sep = new(2026, 9);
    private static readonly YearMonth Oct = new(2026, 10);

    [Fact]
    public async Task 草稿月份即時讀上月發布時結算的月結轉()
    {
        var store = new InMemoryStore()
            .WithStaff("s1", DefaultRanks.R2)
            .WithPublished(Sep, new CarryOverEntry("s1", 2))
            .WithDraft(Oct);
        store.CarryOverApplied[Oct] = new() { new CarryOverEntry("s1", 99) }; // 草稿不該讀這份

        var loaded = await store.Loader.LoadAsync(Oct);

        Assert.Equal(2, loaded.Context.CarryOverOf("s1"));
        Assert.Empty(loaded.Warnings);
    }

    [Fact]
    public async Task 已發布月份讀凍結的那份_上月再改也不動()
    {
        var store = new InMemoryStore()
            .WithStaff("s1", DefaultRanks.R2)
            .WithPublished(Sep, new CarryOverEntry("s1", 5)) // 9 月重新發布後的新值
            .WithPublished(Oct);
        store.CarryOverApplied[Oct] = new() { new CarryOverEntry("s1", 2) }; // 10 月第一次發布時凍結的

        var loaded = await store.Loader.LoadAsync(Oct);

        Assert.Equal(2, loaded.Context.CarryOverOf("s1"));
    }

    [Fact]
    public async Task 上月未發布_月結轉為空_只提醒不擋()
    {
        var store = new InMemoryStore()
            .WithStaff("s1", DefaultRanks.R2)
            .WithDraft(Sep)
            .WithDuty("area-a", new DateOnly(2026, 9, 30), "s1")
            .WithDraft(Oct);

        var loaded = await store.Loader.LoadAsync(Oct);

        Assert.Equal(0, loaded.Context.CarryOverOf("s1"));
        Assert.Contains(SchedulingContextLoader.PreviousMonthNotPublishedWarning, loaded.Warnings);
        Assert.Contains(SchedulingContextLoader.PreviousMonthIsDraftWarning, loaded.Warnings);
        Assert.Single(loaded.Context.PreviousMonthDuties); // 草稿的尾巴照樣算
    }

    [Fact]
    public async Task 上月尾巴撈的天數來自約束設定_不是寫死的()
    {
        var store = new InMemoryStore().WithStaff("s1", DefaultRanks.R2).WithDraft(Oct);
        store.Constraints = DefaultConstraints.Settings.With(
            DefaultConstraints.H4MinGap, c => c with { Params = new ConstraintParams(Days: 8) }); // 蓋過 NP 連六的 6

        await store.Loader.LoadAsync(Oct);

        var range = Assert.Single(store.DutyRangeQueries);
        Assert.Equal((new DateOnly(2026, 9, 23), new DateOnly(2026, 9, 30)), range);
    }

    [Fact]
    public async Task 預設值的尾巴是六天_由_NP_連六決定()
    {
        Assert.Equal(6, SchedulingContextLoader.TailDaysOf(DefaultConstraints.Settings));

        var store = new InMemoryStore().WithStaff("s1", DefaultRanks.R2).WithDraft(Oct);
        await store.Loader.LoadAsync(Oct);
        Assert.Equal(new DateOnly(2026, 9, 25), store.DutyRangeQueries.Single().From);
    }

    [Fact]
    public async Task 十二月的行事曆跨到隔年_國定假日也讀得到()
    {
        var dec = new YearMonth(2026, 12);
        var store = new InMemoryStore()
            .WithStaff("s1", DefaultRanks.R2)
            .WithDraft(dec)
            .WithHoliday(new DateOnly(2027, 1, 1), "元旦");

        var loaded = await store.Loader.LoadAsync(dec);

        var newYear = loaded.Context.Calendar[new DateOnly(2027, 1, 1)];
        Assert.True(newYear.IsPublicHoliday);
        Assert.Equal("元旦", newYear.HolidayName);
        // 連值週六加分往後看 windowDays 天
        Assert.True(loaded.Context.Calendar.Covers(new DateOnly(2027, 1, 10)));
        Assert.True(loaded.Context.Calendar.Covers(new DateOnly(2026, 11, 25)));
    }

    [Fact]
    public async Task 一月的上月尾巴往回跨年()
    {
        var jan = new YearMonth(2027, 1);
        var dec = new YearMonth(2026, 12);
        var store = new InMemoryStore()
            .WithStaff("s1", DefaultRanks.R2)
            .WithPublished(dec, new CarryOverEntry("s1", 1))
            .WithDuty("area-a", new DateOnly(2026, 12, 31), "s1")
            .WithDraft(jan);

        var loaded = await store.Loader.LoadAsync(jan);

        Assert.Equal((new DateOnly(2026, 12, 26), new DateOnly(2026, 12, 31)), store.DutyRangeQueries.Single());
        Assert.Single(loaded.Context.PreviousMonthDuties);
        Assert.Equal(1, loaded.Context.CarryOverOf("s1"));
        Assert.True(loaded.Context.Calendar.Covers(new DateOnly(2026, 12, 26)));
    }

    [Fact]
    public async Task 已停用的人員仍載入_否則他留下的值班會讓_context_組不起來()
    {
        var store = new InMemoryStore()
            .WithStaff("gone", DefaultRanks.R2, StaffStatus.Inactive)
            .WithDraft(Sep)
            .WithDuty("area-a", new DateOnly(2026, 9, 3), "gone");

        var loaded = await store.Loader.LoadAsync(Sep);

        Assert.Single(loaded.Context.Duties);
        Assert.Equal(DefaultRanks.R2, loaded.Context.RankOfStaff("gone").Code);
    }

    [Fact]
    public async Task 該月沒有值班表也組得出_context_值班為空()
    {
        var store = new InMemoryStore().WithStaff("s1", DefaultRanks.R2).WithBlockedDay("s1", new DateOnly(2026, 10, 5));

        var loaded = await store.Loader.LoadAsync(Oct);

        Assert.False(loaded.ScheduleExists);
        Assert.Empty(loaded.Context.Duties);
        Assert.True(loaded.Context.IsBlocked("s1", new DateOnly(2026, 10, 5)));
    }

    [Fact]
    public async Task 逐月覆寫進到_context()
    {
        var store = new InMemoryStore().WithStaff("s1", DefaultRanks.R6).WithDraft(Oct);
        store.Overrides[Oct] = new MonthlyOverride(Oct, new Dictionary<string, int> { [DefaultRanks.R6] = 4 });

        var loaded = await store.Loader.LoadAsync(Oct);

        Assert.Equal(4, loaded.Context.QuotaCapOf(loaded.Context.RankOf(DefaultRanks.R6)));
    }
}
