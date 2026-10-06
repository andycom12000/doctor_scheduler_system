using Microsoft.EntityFrameworkCore;
using Scheduler.Application.Persistence;
using Scheduler.Domain.Model;
using Scheduler.Persistence.Entities;

namespace Scheduler.Persistence.Repositories;

internal sealed class CalendarRepository : ICalendarRepository
{
    private readonly SchedulerDbContext _db;

    public CalendarRepository(SchedulerDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<CalendarException>> GetExceptionsAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default) =>
        await _db.CalendarDays.AsNoTracking()
            .Where(d => d.Date >= from && d.Date <= to)
            .OrderBy(d => d.Date)
            .Select(d => ToDomain(d))
            .ToListAsync(cancellationToken);

    public async Task<CalendarException?> FindAsync(DateOnly date, CancellationToken cancellationToken = default)
    {
        var entity = await _db.CalendarDays.AsNoTracking().SingleOrDefaultAsync(d => d.Date == date, cancellationToken);
        return entity is null ? null : ToDomain(entity);
    }

    public async Task UpsertAsync(CalendarException exception, CancellationToken cancellationToken = default)
    {
        var day = exception.Day;
        var entity = await _db.CalendarDays.FindAsync(new object[] { day.Date }, cancellationToken);
        if (entity is null)
        {
            entity = new CalendarDayEntity { Date = day.Date };
            _db.CalendarDays.Add(entity);
        }

        entity.IsHoliday = day.IsHoliday;
        entity.IsPublicHoliday = day.IsPublicHoliday;
        entity.IsMakeUpWorkday = day.IsMakeUpWorkday;
        entity.HolidayName = day.HolidayName;
        entity.Overridden = exception.Overridden;
    }

    public async Task<bool> UpsertIfNotOverriddenAsync(CalendarException exception, CancellationToken cancellationToken = default)
    {
        // 讀取不走追蹤快取：要看資料庫當下的值，不是 scope 內較早載入的
        var current = await _db.CalendarDays.AsNoTracking().SingleOrDefaultAsync(d => d.Date == exception.Day.Date, cancellationToken);
        if (current is { Overridden: true })
        {
            return false;
        }

        await UpsertAsync(exception, cancellationToken);
        return true;
    }

    public async Task<bool> RemoveIfNotOverriddenAsync(DateOnly date, CancellationToken cancellationToken = default)
    {
        var current = await _db.CalendarDays.AsNoTracking().SingleOrDefaultAsync(d => d.Date == date, cancellationToken);
        if (current is null || current.Overridden)
        {
            return false;
        }

        await RemoveAsync(date, cancellationToken);
        return true;
    }

    public async Task RemoveAsync(DateOnly date, CancellationToken cancellationToken = default)
    {
        var entity = await _db.CalendarDays.FindAsync(new object[] { date }, cancellationToken);
        if (entity is not null)
        {
            _db.CalendarDays.Remove(entity);
        }
    }

    private static CalendarException ToDomain(CalendarDayEntity d) =>
        new(new CalendarDay(d.Date, d.IsHoliday, d.IsPublicHoliday, d.IsMakeUpWorkday, d.HolidayName), d.Overridden);
}
