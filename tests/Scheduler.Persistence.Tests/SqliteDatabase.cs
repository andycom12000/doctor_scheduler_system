using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Scheduler.Persistence;

namespace Scheduler.Persistence.Tests;

/// <summary>
/// 一個測試一顆 SQLite in-memory 資料庫。in-memory 資料庫的壽命等於連線壽命，
/// 所以連線在這裡開著、整個測試共用一條，Dispose 時資料庫跟著消失。
/// 啟動流程走正式的 <see cref="SchedulerDatabase.InitializeAsync"/>：migration、WAL、seed 都跟正式版一樣。
/// </summary>
public sealed class SqliteDatabase : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _services;

    private SqliteDatabase(SqliteConnection connection, ServiceProvider services)
    {
        _connection = connection;
        _services = services;
    }

    public IServiceProvider Services => _services;

    public static async Task<SqliteDatabase> CreateAsync(bool initialize = true, bool seedReferenceRoster = true)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var services = new ServiceCollection()
            .AddSchedulerPersistence(connection)
            .BuildServiceProvider();

        var db = new SqliteDatabase(connection, services);
        if (initialize)
        {
            await SchedulerDatabase.InitializeAsync(services, seedReferenceRoster);
        }

        return db;
    }

    /// <summary>新的 DI scope，即新的 DbContext。要驗證「真的寫進去了」時開第二個 scope 讀。</summary>
    public IServiceScope Scope() => _services.CreateScope();

    public async ValueTask DisposeAsync()
    {
        await _services.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
