using Microsoft.Extensions.DependencyInjection;
using Scheduler.Application.Calendars.Sync;
using Scheduler.Application.Persistence;
using Scheduler.Domain.Model;
using Scheduler.Persistence.Seed;

namespace Scheduler.Persistence.Tests;

/// <summary>行事曆自動更新的狀態存取，以及它與種子「逐日補缺」的相容（#112）。</summary>
public class CalendarSyncStateTests
{
    private static readonly DateOnly BuiltInHoliday = new(2027, 2, 5); // 內建的除夕

    [Fact]
    public async Task 狀態往返一圈_UTC時間戳不變()
    {
        await using var db = await SqliteDatabase.CreateAsync();
        using var scope = db.Scope();
        var state = scope.ServiceProvider.GetRequiredService<ICalendarSyncStateRepository>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        Assert.Equal(CalendarSyncState.Empty, await state.GetAsync());

        var at = new DateTimeOffset(2027, 10, 6, 1, 2, 3, TimeSpan.Zero);
        await state.SaveAsync(new CalendarSyncState(at, new[] { new SyncedYear(2028, "mirror", at) }, "2029 年更新失敗"));
        await uow.CommitAsync();
        // 再存一次走更新分支
        await state.SaveAsync(new CalendarSyncState(at, new[] { new SyncedYear(2028, "official", at) }, null));
        await uow.CommitAsync();

        using var scope2 = db.Scope();
        var read = await scope2.ServiceProvider.GetRequiredService<ICalendarSyncStateRepository>().GetAsync();
        Assert.Equal(at, read.LastSuccessAt);
        Assert.Equal(new SyncedYear(2028, "official", at), Assert.Single(read.Years));
        Assert.Null(read.LastError);
    }

    [Fact]
    public async Task 種子補缺_已同步過的年份不再補內建值_其他年份照補()
    {
        await using var db = await SqliteDatabase.CreateAsync();
        using var scope = db.Scope();
        var calendar = scope.ServiceProvider.GetRequiredService<ICalendarRepository>();
        var state = scope.ServiceProvider.GetRequiredService<ICalendarSyncStateRepository>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        // 自動更新判定 2027 年的內建除夕不是假日（刪列），並記下 2027 已同步
        await calendar.RemoveAsync(BuiltInHoliday);
        await calendar.RemoveAsync(new DateOnly(2026, 1, 1)); // 2026 沒同步過，應被補回
        await state.SaveAsync(new CalendarSyncState(DateTimeOffset.UtcNow, new[] { new SyncedYear(2027, "official", DateTimeOffset.UtcNow) }, null));
        await uow.CommitAsync();

        // 下次啟動：種子逐日補缺
        await SchedulerDatabase.InitializeAsync(db.Services, seedReferenceRoster: false);

        using var scope2 = db.Scope();
        var after = scope2.ServiceProvider.GetRequiredService<ICalendarRepository>();
        Assert.Null(await after.FindAsync(BuiltInHoliday));
        Assert.NotNull(await after.FindAsync(new DateOnly(2026, 1, 1)));
    }

    [Fact]
    public async Task 種子補缺_沒有同步紀錄時行為不變()
    {
        await using var db = await SqliteDatabase.CreateAsync();
        using var scope = db.Scope();
        var calendar = scope.ServiceProvider.GetRequiredService<ICalendarRepository>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await calendar.RemoveAsync(BuiltInHoliday);
        await uow.CommitAsync();

        await SchedulerDatabase.InitializeAsync(db.Services, seedReferenceRoster: false);

        using var scope2 = db.Scope();
        Assert.NotNull(await scope2.ServiceProvider.GetRequiredService<ICalendarRepository>().FindAsync(BuiltInHoliday));
        Assert.Contains(BuiltInCalendar.Days, d => d.Date == BuiltInHoliday);
    }
}
