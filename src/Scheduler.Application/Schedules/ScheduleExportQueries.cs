using System.Globalization;
using Scheduler.Application.Errors;
using Scheduler.Application.Scheduling;
using Scheduler.Domain.Model;
using Scheduler.Domain.Scheduling;

namespace Scheduler.Application.Schedules;

/// <summary>
/// <c>GET /schedules/{ym}/export</c> 的內容：把某月值班表攤成 <see cref="ExportTable"/>。
/// 從 <see cref="SchedulingContextLoader"/> 組 context（人員含停用，值班表上引用到的離職者才找得到名字；
/// 行事曆在 context 裡，國定假日的名稱直接印在日標題上），值班表不存在時 404，與 <c>getSchedule</c> 一致。
/// </summary>
public sealed class ScheduleExportQueries
{
    private const string InactiveSuffix = "（停用）";
    private static readonly string[] WeekdayNames = ["日", "一", "二", "三", "四", "五", "六"];

    private readonly SchedulingContextLoader _loader;

    public ScheduleExportQueries(SchedulingContextLoader loader)
    {
        _loader = loader;
    }

    /// <summary>建議的下載檔名，不含副檔名：<c>duty-2026-09</c>。</summary>
    public static string FileStem(YearMonth month) => $"duty-{month}";

    public async Task<ExportTable> BuildAsync(YearMonth month, ExportLayout layout, CancellationToken cancellationToken = default)
    {
        var loaded = await _loader.LoadAsync(month, cancellationToken);
        if (!loaded.ScheduleExists)
        {
            throw SchedulerException.ScheduleNotFound(month);
        }

        var ctx = loaded.Context;
        return layout switch
        {
            ExportLayout.AreaByDay => AreaByDay(ctx),
            ExportLayout.DayByStaff => DayByStaff(ctx),
            _ => throw new SchedulerException(ErrorCode.InvalidRequest, $"不支援的版面：{layout}"),
        };
    }

    /// <summary>列 = 區域（設定的順序）、欄 = 日、格子 = 人名。目前每格只會有一人（同格是結構不變式），「、」相連是為 <c>RequiredPerDay &gt; 1</c> 預留。</summary>
    private static ExportTable AreaByDay(SchedulingContext ctx)
    {
        var days = ctx.Month.Days().Select(d => ctx.Calendar[d]).ToArray();
        var columns = days.Select(d => new ExportColumn(DayHeader(d), d.IsHoliday)).ToArray();

        var byCell = ctx.Duties.ToLookup(d => (d.AreaId, d.Date));
        var rows = ctx.Areas
            .Select(area => new ExportRow(
                area.Name,
                IsHoliday: false,
                days.Select(day => Join(byCell[(area.Id, day.Date)].Select(d => StaffLabel(ctx.StaffOf(d.StaffId))))).ToArray()))
            .ToArray();

        return new ExportTable($"{ctx.Month} 區域×日", "區域", columns, rows);
    }

    /// <summary>列 = 日、欄 = 人員、格子 = 區域代碼。人員依身分順序再依員工編號；停用者只在本月有值班時才出現、排在最後。</summary>
    private static ExportTable DayByStaff(SchedulingContext ctx)
    {
        var staffWithDuty = ctx.Duties.Select(d => d.StaffId).ToHashSet(StringComparer.Ordinal);
        var rankOrder = ctx.Ranks.Select((r, i) => (r.Code, Index: i)).ToDictionary(x => x.Code, x => x.Index, StringComparer.Ordinal);

        var staff = ctx.Staff
            .Where(s => s.Status == StaffStatus.Active || staffWithDuty.Contains(s.Id))
            .OrderBy(s => s.Status == StaffStatus.Active ? 0 : 1)
            .ThenBy(s => rankOrder.TryGetValue(s.RankCode, out var i) ? i : int.MaxValue)
            .ThenBy(s => s.EmployeeNo, StringComparer.Ordinal)
            .ToArray();
        var columns = staff.Select(s => new ExportColumn(StaffLabel(s))).ToArray();

        var byCell = ctx.Duties.ToLookup(d => (d.StaffId, d.Date));
        var rows = ctx.Month.Days()
            .Select(date => ctx.Calendar[date])
            .Select(day => new ExportRow(
                DayHeader(day),
                day.IsHoliday,
                staff.Select(s => Join(byCell[(s.Id, day.Date)].Select(d => ctx.AreaOf(d.AreaId).Code))).ToArray()))
            .ToArray();

        return new ExportTable($"{ctx.Month} 日×人員", "日期", columns, rows);
    }

    /// <summary><c>9/28 (一) 教師節</c>：國定假日印名稱，一般週末只靠星期辨識，不寫「假日」二字（CONTEXT.md）。</summary>
    private static string DayHeader(CalendarDay day)
    {
        var text = string.Create(CultureInfo.InvariantCulture, $"{day.Date.Month}/{day.Date.Day} ({WeekdayNames[(int)day.Weekday]})");
        if (day.IsPublicHoliday && !string.IsNullOrWhiteSpace(day.HolidayName))
        {
            text += " " + day.HolidayName;
        }
        else if (day.IsMakeUpWorkday)
        {
            text += " 補班";
        }

        return text;
    }

    private static string StaffLabel(Staff staff) =>
        staff.Status == StaffStatus.Active ? staff.Name : staff.Name + InactiveSuffix;

    private static string Join(IEnumerable<string> parts) => string.Join("、", parts.Order(StringComparer.Ordinal));
}
