using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Scheduler.Persistence.Repositories;
using Scheduler.Persistence.Seed;

namespace Scheduler.Persistence;

/// <summary>
/// 資料庫檔案的位置與啟動流程。所有執行期狀態都在程式旁的 <c>data/</c>，
/// 不得碰 %APPDATA% / %LOCALAPPDATA% / 登錄檔——portable 的硬性要求，也是驗收項目。
/// </summary>
public static class SchedulerDatabase
{
    public const string DataDirectoryName = "data";
    public const string FileName = "scheduler.db";

    /// <summary>程式旁的 <c>data/scheduler.db</c>。以 <see cref="AppContext.BaseDirectory"/> 為準，不看目前工作目錄。</summary>
    public static string DefaultPath => Path.Combine(AppContext.BaseDirectory, DataDirectoryName, FileName);

    /// <summary>
    /// 啟動時做的事，依序：建 <c>data/</c>（SQLite 只會建檔、不會建目錄）→ 套用 migration →
    /// 開 WAL → 設定表空的話從出廠值 seed → 把上次沒跑完的求解工作標成失敗。
    /// 每一步都可重複執行，第二次啟動不會重做。
    /// </summary>
    /// <param name="seedReferenceRoster">
    /// false 時完全不種參考名單 34 人；行事曆例外日、約束等其他出廠設定不受影響（#37）。
    /// </param>
    /// <param name="rosterFilePath">
    /// 發佈包的名冊檔路徑（#82）。有給時：檔案存在、從沒匯入過、人員表空的才匯入，並寫「已匯入」標記。
    /// 與 <paramref name="seedReferenceRoster"/> 不可同時使用。
    /// </param>
    public static async Task InitializeAsync(
        IServiceProvider services, bool seedReferenceRoster = true, CancellationToken cancellationToken = default,
        string? rosterFilePath = null)
    {
        if (seedReferenceRoster && rosterFilePath is not null)
        {
            throw new ArgumentException("參考名單與名冊檔只能擇一", nameof(rosterFilePath));
        }

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SchedulerDbContext>();

        var dataSource = new SqliteConnectionStringBuilder(db.Database.GetConnectionString()).DataSource;
        if (!string.Equals(dataSource, ":memory:", StringComparison.OrdinalIgnoreCase))
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(dataSource));
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }

        await db.Database.MigrateAsync(cancellationToken);

        // WAL 是資料庫檔案的持久屬性，設一次就留著；對 in-memory 資料庫沒有意義但也無害。
        await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode = 'wal';", cancellationToken);

        await DefaultDataSeeder.SeedIfEmptyAsync(db, seedReferenceRoster, cancellationToken);

        if (rosterFilePath is not null)
        {
            // 身分表要先 seed 好才能驗身分代碼，所以排在 DefaultDataSeeder 之後
            var logger = scope.ServiceProvider.GetService<ILoggerFactory>()?.CreateLogger(typeof(RosterImporter).FullName!);
            var now = scope.ServiceProvider.GetService<TimeProvider>()?.GetUtcNow() ?? DateTimeOffset.UtcNow;
            // 發佈版沒有 console，只有 ILogger 看不到；失敗時另外附加一行到 data/roster-import.log
            var logPath = string.Equals(dataSource, ":memory:", StringComparison.OrdinalIgnoreCase)
                ? null
                : Path.Combine(Path.GetDirectoryName(Path.GetFullPath(dataSource))!, "roster-import.log");
            void Warn(string message)
            {
                logger?.LogWarning("{Message}", message);
                if (logPath is not null)
                {
                    try
                    {
                        File.AppendAllText(logPath, $"{now:O} {message}{Environment.NewLine}");
                    }
                    catch (IOException)
                    {
                        // 紀錄寫不進去不能讓程式起不來
                    }
                    catch (UnauthorizedAccessException)
                    {
                    }
                }
            }

            await RosterImporter.ImportIfFirstRunAsync(db, rosterFilePath, Warn, now, cancellationToken);
        }

        await new SolverJobRepository(db).FailUnfinishedAsync("程式重啟中斷", DateTimeOffset.UtcNow, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// 載入自檢：確認 e_sqlite3 native 程式庫載得起來、資料庫連得上。
    /// RID-specific 的 self-contained publish 會把 e_sqlite3.dll 放在 exe 旁，這條路一旦被
    /// trim / single-file / 錯誤的 RID 打斷，第一個症狀就在這裡。回傳 SQLite 程式庫版本。
    /// </summary>
    public static async Task<string> ProbeAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SchedulerDbContext>();
        if (!await db.Database.CanConnectAsync(cancellationToken))
        {
            throw new InvalidOperationException("SQLite 資料庫連不上");
        }

        return await db.Database.SqlQueryRaw<string>("select sqlite_version() as Value").SingleAsync(cancellationToken);
    }
}
