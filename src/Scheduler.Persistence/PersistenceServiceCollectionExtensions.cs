using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Scheduler.Application.Persistence;
using Scheduler.Persistence.Repositories;

namespace Scheduler.Persistence;

/// <summary>Persistence 的 DI 註冊。Api 與測試都從這裡進，不各自 new DbContext。</summary>
public static class PersistenceServiceCollectionExtensions
{
    /// <summary>
    /// 註冊指向 <paramref name="databasePath"/> 的 SQLite 資料庫與全部 repository。
    /// 正式路徑用 <see cref="SchedulerDatabase.DefaultPath"/>（程式旁的 <c>data/scheduler.db</c>）。
    /// </summary>
    public static IServiceCollection AddSchedulerPersistence(this IServiceCollection services, string databasePath)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            // 外鍵預設就開；明寫是為了不依賴 provider 的預設值
            ForeignKeys = true,
        }.ToString();

        services.AddDbContext<SchedulerDbContext>(options => options.UseSqlite(connectionString));
        return services.AddSchedulerRepositories();
    }

    /// <summary>
    /// 用外部提供的連線註冊。測試用：SQLite in-memory 資料庫的壽命等於連線壽命，
    /// 連線必須由呼叫端開著、整個測試共用一條。
    /// </summary>
    public static IServiceCollection AddSchedulerPersistence(this IServiceCollection services, SqliteConnection connection)
    {
        services.AddDbContext<SchedulerDbContext>(options => options.UseSqlite(connection));
        return services.AddSchedulerRepositories();
    }

    private static IServiceCollection AddSchedulerRepositories(this IServiceCollection services)
    {
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<IScheduleRepository, ScheduleRepository>();
        services.AddScoped<IBlockedDayRepository, BlockedDayRepository>();
        services.AddScoped<IStaffRepository, StaffRepository>();
        services.AddScoped<ISettingsRepository, SettingsRepository>();
        services.AddScoped<ICalendarRepository, CalendarRepository>();
        services.AddScoped<ISolverJobRepository, SolverJobRepository>();
        return services;
    }
}
