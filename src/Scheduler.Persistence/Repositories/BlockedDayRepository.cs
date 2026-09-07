using Microsoft.EntityFrameworkCore;
using Scheduler.Application.Persistence;
using Scheduler.Domain.Model;
using Scheduler.Persistence.Entities;

namespace Scheduler.Persistence.Repositories;

internal sealed class BlockedDayRepository : IBlockedDayRepository
{
    private readonly SchedulerDbContext _db;

    public BlockedDayRepository(SchedulerDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<BlockedDay>> ListAsync(YearMonth yearMonth, CancellationToken cancellationToken = default)
    {
        var (from, to) = (yearMonth.FirstDay, yearMonth.LastDay);
        return await _db.BlockedDays.AsNoTracking()
            .Where(b => b.Date >= from && b.Date <= to)
            .OrderBy(b => b.Date).ThenBy(b => b.StaffId)
            .Select(b => new BlockedDay(b.StaffId, b.Date))
            .ToListAsync(cancellationToken);
    }

    public Task<bool> ExistsAsync(BlockedDay blockedDay, CancellationToken cancellationToken = default) =>
        _db.BlockedDays.AnyAsync(b => b.StaffId == blockedDay.StaffId && b.Date == blockedDay.Date, cancellationToken);

    public Task<int> CountAsync(string staffId, YearMonth yearMonth, CancellationToken cancellationToken = default)
    {
        var (from, to) = (yearMonth.FirstDay, yearMonth.LastDay);
        return _db.BlockedDays.CountAsync(b => b.StaffId == staffId && b.Date >= from && b.Date <= to, cancellationToken);
    }

    public async Task AddAsync(BlockedDay blockedDay, CancellationToken cancellationToken = default)
    {
        var existing = await _db.BlockedDays.FindAsync(new object[] { blockedDay.StaffId, blockedDay.Date }, cancellationToken);
        if (existing is null)
        {
            _db.BlockedDays.Add(new BlockedDayEntity { StaffId = blockedDay.StaffId, Date = blockedDay.Date });
        }
    }

    public async Task RemoveAsync(BlockedDay blockedDay, CancellationToken cancellationToken = default)
    {
        var existing = await _db.BlockedDays.FindAsync(new object[] { blockedDay.StaffId, blockedDay.Date }, cancellationToken);
        if (existing is not null)
        {
            _db.BlockedDays.Remove(existing);
        }
    }

    public async Task<int> RemoveAllForStaffAsync(string staffId, CancellationToken cancellationToken = default)
    {
        var rows = await _db.BlockedDays.Where(b => b.StaffId == staffId).ToListAsync(cancellationToken);
        _db.BlockedDays.RemoveRange(rows);
        return rows.Count;
    }
}
