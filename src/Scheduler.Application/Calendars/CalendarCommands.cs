using Scheduler.Application.Errors;
using Scheduler.Application.Persistence;
using Scheduler.Domain.Model;

namespace Scheduler.Application.Calendars;

/// <summary>
/// 契約 <c>CalendarDayOverride</c>：每個欄位都可省略，省略即維持原值。
/// <see cref="HolidayName"/> 要分「沒送」與「送 null 清掉」，所以多一個旗標。
/// </summary>
public sealed record CalendarDayPatch(
    bool? IsHoliday = null,
    bool? IsPublicHoliday = null,
    bool? IsMakeUpWorkday = null,
    bool HolidayNameProvided = false,
    string? HolidayName = null);

/// <summary>行事曆的寫入路徑：覆寫單日的假日屬性（補班日修正與臨時放假）。</summary>
public sealed class CalendarCommands
{
    private readonly ICalendarRepository _calendar;
    private readonly ISettingsRepository _settings;
    private readonly IUnitOfWork _unitOfWork;

    public CalendarCommands(ICalendarRepository calendar, ISettingsRepository settings, IUnitOfWork unitOfWork)
    {
        _calendar = calendar;
        _settings = settings;
        _unitOfWork = unitOfWork;
    }

    /// <summary>
    /// 以目前的那一天（例外日或純週末推算）為底套上 patch，存成使用者覆寫。
    /// 補班日視為平日：<c>isMakeUpWorkday</c> 設為 true 時 <c>isHoliday</c> 跟著變 false，除非同一個 patch 明講。
    /// </summary>
    public async Task<CalendarDayView> OverrideDayAsync(DateOnly date, CalendarDayPatch patch, CancellationToken cancellationToken = default)
    {
        var current = (await _calendar.FindAsync(date, cancellationToken))?.Day ?? CalendarDay.Plain(date);

        var isMakeUpWorkday = patch.IsMakeUpWorkday ?? current.IsMakeUpWorkday;
        var isHoliday = patch.IsHoliday ?? (patch.IsMakeUpWorkday == true ? false : current.IsHoliday);
        var isPublicHoliday = patch.IsPublicHoliday ?? current.IsPublicHoliday;
        if (isMakeUpWorkday && isHoliday)
        {
            throw new SchedulerException(ErrorCode.InvalidRequest, "補班日視為平日，不能同時是假日");
        }

        if (isPublicHoliday && !isHoliday)
        {
            throw new SchedulerException(ErrorCode.InvalidRequest, "國定假日必定是假日");
        }

        var day = new CalendarDay(
            date,
            isHoliday,
            isPublicHoliday,
            isMakeUpWorkday,
            patch.HolidayNameProvided ? NullIfBlank(patch.HolidayName) : current.HolidayName);

        await _calendar.UpsertAsync(new CalendarException(day, Overridden: true), cancellationToken);
        await _unitOfWork.CommitAsync(cancellationToken);

        var quota = (await _settings.GetPointRulesAsync(cancellationToken)).Quota;
        return new CalendarDayView(day, quota.ValueOf(day), Overridden: true);
    }

    private static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
