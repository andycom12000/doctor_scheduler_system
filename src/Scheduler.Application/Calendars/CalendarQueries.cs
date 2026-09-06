using Scheduler.Application.Persistence;
using Scheduler.Domain.Model;

namespace Scheduler.Application.Calendars;

/// <summary><c>GET /calendars/{year}</c>。</summary>
public sealed record CalendarYear(int Year, IReadOnlyList<CalendarDayView> Days);

public sealed record CalendarDayView(CalendarDay Day, int QuotaPointValue, bool Overridden);

public sealed class CalendarQueries
{
    private readonly ICalendarRepository _calendar;
    private readonly ISettingsRepository _settings;

    public CalendarQueries(ICalendarRepository calendar, ISettingsRepository settings)
    {
        _calendar = calendar;
        _settings = settings;
    }

    /// <summary>整年 365／366 天全列，任何年份都回得來：沒有例外日的年份就是純週末。</summary>
    public async Task<CalendarYear> GetYearAsync(int year, CancellationToken cancellationToken = default)
    {
        var from = new DateOnly(year, 1, 1);
        var to = new DateOnly(year, 12, 31);
        var exceptions = (await _calendar.GetExceptionsAsync(from, to, cancellationToken)).ToDictionary(e => e.Day.Date);
        var quota = (await _settings.GetPointRulesAsync(cancellationToken)).Quota;

        var days = new List<CalendarDayView>(366);
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            var (day, overridden) = exceptions.TryGetValue(d, out var e) ? (e.Day, e.Overridden) : (CalendarDay.Plain(d), false);
            days.Add(new CalendarDayView(day, quota.ValueOf(day), overridden));
        }

        return new CalendarYear(year, days);
    }
}
