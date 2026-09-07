using Scheduler.Application.Persistence;
using Scheduler.Domain.Model;

namespace Scheduler.Application.Calendars;

/// <summary>
/// 把「只存例外日」的資料庫（ARCHITECTURE §5）還原成一段連續的 <see cref="Calendar"/>：
/// 週六日先算出來，再把國定假日、補班日、使用者覆寫蓋上去。
/// 跨年只是日期區間，沒有特例。
/// </summary>
public sealed class CalendarBuilder
{
    private readonly ICalendarRepository _calendar;

    public CalendarBuilder(ICalendarRepository calendar)
    {
        _calendar = calendar;
    }

    public async Task<Calendar> BuildAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new ArgumentOutOfRangeException(nameof(to), $"{to:yyyy-MM-dd} 早於 {from:yyyy-MM-dd}");
        }

        var exceptions = await _calendar.GetExceptionsAsync(from, to, cancellationToken);
        return Calendar.Plain(from, to).With(exceptions.Select(e => e.Day).ToArray());
    }
}
