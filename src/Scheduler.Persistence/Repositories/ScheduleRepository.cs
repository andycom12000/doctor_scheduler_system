using Microsoft.EntityFrameworkCore;
using Scheduler.Application.Persistence;
using Scheduler.Application.Schedules;
using Scheduler.Domain.Model;
using Scheduler.Persistence.Entities;
using Scheduler.Persistence.Mapping;

namespace Scheduler.Persistence.Repositories;

internal sealed class ScheduleRepository : IScheduleRepository
{
    private readonly SchedulerDbContext _db;

    public ScheduleRepository(SchedulerDbContext db)
    {
        _db = db;
    }

    public async Task<ScheduleHeader?> FindAsync(YearMonth yearMonth, CancellationToken cancellationToken = default)
    {
        var entity = await _db.Schedules.AsNoTracking()
            .SingleOrDefaultAsync(s => s.Year == yearMonth.Year && s.Month == yearMonth.Month, cancellationToken);
        return entity is null ? null : ToHeader(entity);
    }

    public async Task<IReadOnlyList<ScheduleHeader>> ListAsync(CancellationToken cancellationToken = default) =>
        await _db.Schedules.AsNoTracking()
            .OrderBy(s => s.Year).ThenBy(s => s.Month)
            .Select(s => ToHeader(s))
            .ToListAsync(cancellationToken);

    public async Task UpsertAsync(ScheduleHeader header, CancellationToken cancellationToken = default)
    {
        var entity = await _db.Schedules.FindAsync(new object[] { header.YearMonth.Year, header.YearMonth.Month }, cancellationToken);
        if (entity is null)
        {
            entity = new ScheduleEntity { Year = header.YearMonth.Year, Month = header.YearMonth.Month };
            _db.Schedules.Add(entity);
        }

        entity.Status = EnumNames.Of(header.Status);
        entity.Revision = header.Revision;
        entity.PublishedAt = header.PublishedAt;
    }

    public async Task<IReadOnlyList<Duty>> GetDutiesAsync(YearMonth yearMonth, CancellationToken cancellationToken = default) =>
        await _db.Duties.AsNoTracking()
            .Where(d => d.Year == yearMonth.Year && d.Month == yearMonth.Month)
            .OrderBy(d => d.Date).ThenBy(d => d.AreaId)
            .Select(d => new Duty(d.AreaId, d.Date, d.StaffId))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Duty>> GetDutiesInRangeAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default) =>
        await _db.Duties.AsNoTracking()
            .Where(d => d.Date >= from && d.Date <= to)
            .OrderBy(d => d.Date).ThenBy(d => d.AreaId)
            .Select(d => new Duty(d.AreaId, d.Date, d.StaffId))
            .ToListAsync(cancellationToken);

    public Task<bool> AnyDutyForStaffAsync(string staffId, CancellationToken cancellationToken = default) =>
        _db.Duties.AnyAsync(d => d.StaffId == staffId, cancellationToken);

    public Task<bool> AnyDutyForAreaAsync(string areaId, CancellationToken cancellationToken = default) =>
        _db.Duties.AnyAsync(d => d.AreaId == areaId, cancellationToken);

    public async Task SetDutyAsync(YearMonth yearMonth, string areaId, DateOnly date, string? staffId, CancellationToken cancellationToken = default)
    {
        EnsureInMonth(yearMonth, date);
        var entity = await _db.Duties.FindAsync(new object[] { yearMonth.Year, yearMonth.Month, areaId, date }, cancellationToken);
        if (staffId is null)
        {
            if (entity is not null)
            {
                _db.Duties.Remove(entity);
            }

            return;
        }

        if (entity is null)
        {
            _db.Duties.Add(new DutyEntity
            {
                Year = yearMonth.Year,
                Month = yearMonth.Month,
                AreaId = areaId,
                Date = date,
                StaffId = staffId,
            });
        }
        else
        {
            entity.StaffId = staffId;
        }
    }

    /// <summary>
    /// duty 的主鍵含 (year, month)，同一個 (area, date) 掛在兩個月份標頭下是兩列合法資料，
    /// 但會讓跨月區間查詢同一格出現兩個人。這裡守住：日期必須落在該月。
    /// </summary>
    private static void EnsureInMonth(YearMonth yearMonth, DateOnly date)
    {
        if (!yearMonth.Contains(date))
        {
            throw new ArgumentOutOfRangeException(nameof(date), $"{date:yyyy-MM-dd} 不在 {yearMonth} 裡。");
        }
    }

    public async Task ReplaceDutiesAsync(YearMonth yearMonth, IReadOnlyList<Duty> duties, CancellationToken cancellationToken = default)
    {
        var existing = await _db.Duties
            .Where(d => d.Year == yearMonth.Year && d.Month == yearMonth.Month)
            .ToListAsync(cancellationToken);

        foreach (var duty in duties)
        {
            EnsureInMonth(yearMonth, duty.Date);
        }

        DbSetSync.Sync(
            _db.Duties,
            existing,
            duties.Select(d => new DutyEntity
            {
                Year = yearMonth.Year,
                Month = yearMonth.Month,
                AreaId = d.AreaId,
                Date = d.Date,
                StaffId = d.StaffId,
            }),
            d => (d.AreaId, d.Date),
            (from, into) => into.StaffId = from.StaffId);
    }

    public async Task<IReadOnlyList<CarryOverEntry>> GetCarryOverAsync(YearMonth yearMonth, CancellationToken cancellationToken = default) =>
        await _db.CarryOvers.AsNoTracking()
            .Where(c => c.Year == yearMonth.Year && c.Month == yearMonth.Month)
            .OrderBy(c => c.StaffId)
            .Select(c => new CarryOverEntry(c.StaffId, c.Points))
            .ToListAsync(cancellationToken);

    public async Task ReplaceCarryOverAsync(YearMonth yearMonth, IReadOnlyList<CarryOverEntry> entries, CancellationToken cancellationToken = default)
    {
        var existing = await _db.CarryOvers
            .Where(c => c.Year == yearMonth.Year && c.Month == yearMonth.Month)
            .ToListAsync(cancellationToken);

        DbSetSync.Sync(
            _db.CarryOvers,
            existing,
            entries.Select(e => new CarryOverEntity { Year = yearMonth.Year, Month = yearMonth.Month, StaffId = e.StaffId, Points = e.Points }),
            c => c.StaffId,
            (from, into) => into.Points = from.Points);
    }

    public async Task<IReadOnlyList<CarryOverEntry>> GetCarryOverAppliedAsync(YearMonth yearMonth, CancellationToken cancellationToken = default) =>
        await _db.CarryOversApplied.AsNoTracking()
            .Where(c => c.Year == yearMonth.Year && c.Month == yearMonth.Month)
            .OrderBy(c => c.StaffId)
            .Select(c => new CarryOverEntry(c.StaffId, c.Points))
            .ToListAsync(cancellationToken);

    public async Task ReplaceCarryOverAppliedAsync(YearMonth yearMonth, IReadOnlyList<CarryOverEntry> entries, CancellationToken cancellationToken = default)
    {
        var existing = await _db.CarryOversApplied
            .Where(c => c.Year == yearMonth.Year && c.Month == yearMonth.Month)
            .ToListAsync(cancellationToken);

        DbSetSync.Sync(
            _db.CarryOversApplied,
            existing,
            entries.Select(e => new CarryOverAppliedEntity { Year = yearMonth.Year, Month = yearMonth.Month, StaffId = e.StaffId, Points = e.Points }),
            c => c.StaffId,
            (from, into) => into.Points = from.Points);
    }

    private static ScheduleHeader ToHeader(ScheduleEntity s) =>
        new(new YearMonth(s.Year, s.Month), EnumNames.ToScheduleStatus(s.Status), s.Revision, s.PublishedAt);
}
