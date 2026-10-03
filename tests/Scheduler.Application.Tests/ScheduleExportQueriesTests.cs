using Scheduler.Application.Errors;
using Scheduler.Application.Schedules;
using Scheduler.Domain.Defaults;
using Scheduler.Domain.Model;

namespace Scheduler.Application.Tests;

/// <summary>匯出表格的內容規則：格式無關，只驗「哪列哪欄放什麼字」。</summary>
public class ScheduleExportQueriesTests
{
    private static readonly YearMonth Sep = new(2026, 9);

    private static InMemoryStore Store() => new InMemoryStore()
        .WithStaff("s-r2", DefaultRanks.R2)
        .WithStaff("s-pgy1", DefaultRanks.PGY1)
        .WithStaff("s-gone", DefaultRanks.R2, StaffStatus.Inactive)
        .WithStaff("s-idle", DefaultRanks.R2, StaffStatus.Inactive)
        .WithHoliday(new DateOnly(2026, 9, 28), "教師節")
        .WithDraft(Sep)
        .WithDuty("area-a", new DateOnly(2026, 9, 1), "s-pgy1")
        .WithDuty("area-chief", new DateOnly(2026, 9, 1), "s-r2")
        .WithDuty("area-a", new DateOnly(2026, 9, 28), "s-gone");

    [Fact]
    public async Task 沒有值班表_NOT_FOUND()
    {
        var q = new ScheduleExportQueries(new InMemoryStore().WithStaff("s1", DefaultRanks.R2).Loader);
        var ex = await Assert.ThrowsAsync<SchedulerException>(() => q.BuildAsync(Sep, ExportLayout.AreaByDay));
        Assert.Equal(ErrorCode.NotFound, ex.Code);
    }

    [Fact]
    public async Task 同人同日兩區_X1_丟_DoubleBookingPresent_兩種版面都擋()
    {
        var store = Store().WithDuty("area-b", new DateOnly(2026, 9, 1), "s-pgy1"); // s-pgy1 9/1 同時在 A、B

        foreach (var layout in new[] { ExportLayout.AreaByDay, ExportLayout.DayByStaff })
        {
            var ex = await Assert.ThrowsAsync<SchedulerException>(() => new ScheduleExportQueries(store.Loader).BuildAsync(Sep, layout));
            Assert.Equal(ErrorCode.DoubleBookingPresent, ex.Code);
            Assert.Equal(1, ex.Details!["doubleBookingCount"]);
        }
    }

    [Fact]
    public async Task 區域乘日_列是設定順序的區域_欄是本月每一天_格子是人名()
    {
        var table = await new ScheduleExportQueries(Store().Loader).BuildAsync(Sep, ExportLayout.AreaByDay);

        Assert.Equal("區域", table.CornerLabel);
        Assert.Equal(30, table.Columns.Count);
        Assert.Equal(DefaultAreas.Areas.Select(a => a.Name), table.Rows.Select(r => r.Header));
        Assert.All(table.Rows, r => Assert.Equal(30, r.Cells.Count));

        // 9/1 是週二
        Assert.Equal("9/1 (二)", table.Columns[0].Header);
        Assert.False(table.Columns[0].IsHoliday);
        // 9/5 是週六：假日但不是國定假日，不寫「假日」
        Assert.Equal("9/5 (六)", table.Columns[4].Header);
        Assert.True(table.Columns[4].IsHoliday);
        // 9/28 教師節：印名稱
        Assert.Equal("9/28 (一) 教師節", table.Columns[27].Header);
        Assert.True(table.Columns[27].IsHoliday);

        var areaA = table.Rows.Single(r => r.Header == "A");
        Assert.Equal("人員 s-pgy1", areaA.Cells[0]);
        Assert.Equal(string.Empty, areaA.Cells[1]);
        Assert.Equal("人員 s-gone（停用）", areaA.Cells[27]);
        Assert.Equal("人員 s-r2", table.Rows.Single(r => r.Header == "總值").Cells[0]);
    }

    [Fact]
    public async Task 三十一天的月份_欄數跟著月份_補班日印補班()
    {
        var oct = new YearMonth(2026, 10);
        var store = new InMemoryStore().WithStaff("s-r2", DefaultRanks.R2).WithDraft(oct);
        var makeUp = new DateOnly(2026, 10, 3); // 週六，憑空造成補班日
        store.CalendarExceptions[makeUp] = new Application.Persistence.CalendarException(
            new CalendarDay(makeUp, IsHoliday: false, IsPublicHoliday: false, IsMakeUpWorkday: true), Overridden: false);

        var table = await new ScheduleExportQueries(store.Loader).BuildAsync(oct, ExportLayout.AreaByDay);

        Assert.Equal(31, table.Columns.Count);
        Assert.All(table.Rows, r => Assert.Equal(31, r.Cells.Count));
        Assert.Equal("10/3 (六) 補班", table.Columns[2].Header);
        Assert.False(table.Columns[2].IsHoliday);
        Assert.Equal("10/31 (六)", table.Columns[30].Header);
    }

    [Fact]
    public async Task 日乘人員_欄依身分再依員編_停用者只有本月有值班才出現且排最後_格子是區域代碼()
    {
        var table = await new ScheduleExportQueries(Store().Loader).BuildAsync(Sep, ExportLayout.DayByStaff);

        Assert.Equal("日期", table.CornerLabel);
        // 出廠身分順序 PGY1 在 R2 前；s-idle 停用且沒值班，不出現
        Assert.Equal(new[] { "人員 s-pgy1", "人員 s-r2", "人員 s-gone（停用）" }, table.Columns.Select(c => c.Header));
        Assert.All(table.Columns, c => Assert.False(c.IsHoliday));

        Assert.Equal(30, table.Rows.Count);
        Assert.Equal("9/1 (二)", table.Rows[0].Header);
        Assert.Equal(new[] { "A", "CHIEF", "" }, table.Rows[0].Cells);
        Assert.True(table.Rows[4].IsHoliday);
        Assert.Equal("9/28 (一) 教師節", table.Rows[27].Header);
        Assert.Equal(new[] { "", "", "A" }, table.Rows[27].Cells);
    }
}
