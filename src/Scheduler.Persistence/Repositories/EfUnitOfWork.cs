using Microsoft.EntityFrameworkCore;
using Scheduler.Application.Persistence;

namespace Scheduler.Persistence.Repositories;

/// <summary>一個 DbContext 就是一個工作單元；<see cref="CommitAsync"/> 就是 <c>SaveChanges</c>，EF 自己包交易。</summary>
internal sealed class EfUnitOfWork : IUnitOfWork
{
    private readonly SchedulerDbContext _db;

    public EfUnitOfWork(SchedulerDbContext db)
    {
        _db = db;
    }

    public Task CommitAsync(CancellationToken cancellationToken = default) => _db.SaveChangesAsync(cancellationToken);
}

/// <summary>
/// 「整份取代」的共用做法：主鍵相同的列就地更新，多出來的刪，缺的新增。
/// 不用 delete-all + insert-all，因為同一批 SaveChanges 裡刪掉再插回同一個主鍵，
/// 對有外鍵指過來的表（area_type ← area、rank_group ← rank）會在中途撞外鍵。
/// 另外，同一批裡 Remove 一個實例再 Add 同主鍵的另一個實例，change tracker 也會直接拒絕。
/// </summary>
internal static class DbSetSync
{
    public static void Sync<TEntity, TKey>(
        DbSet<TEntity> set,
        IEnumerable<TEntity> existing,
        IEnumerable<TEntity> desired,
        Func<TEntity, TKey> key,
        Action<TEntity, TEntity> copyInto)
        where TEntity : class
        where TKey : notnull
    {
        var current = existing.ToDictionary(key);
        var seen = new HashSet<TKey>();

        foreach (var wanted in desired)
        {
            var k = key(wanted);
            if (!seen.Add(k))
            {
                throw new InvalidOperationException($"整份取代的資料裡主鍵重複：{k}");
            }

            if (current.TryGetValue(k, out var tracked))
            {
                copyInto(wanted, tracked);
            }
            else
            {
                set.Add(wanted);
            }
        }

        foreach (var (k, tracked) in current)
        {
            if (!seen.Contains(k))
            {
                set.Remove(tracked);
            }
        }
    }
}
