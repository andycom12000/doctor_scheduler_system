using Microsoft.Data.Sqlite;

namespace Scheduler.Persistence;

/// <summary>
/// 啟動時的自我檢查：確認 e_sqlite3 native 程式庫載得起來。
/// RID-specific 的 self-contained publish 會把 e_sqlite3.dll 直接放在 exe 旁，
/// 這條路一旦被 trim / single-file / 錯誤的 RID 打斷，第一個症狀就在這裡。
/// e_sqlite3 靜態連結 CRT，不依賴 msvcp140 / vcruntime140（見 docs/ARCHITECTURE.md §9.2）。
/// </summary>
public static class SqliteRuntimeProbe
{
    /// <summary>回傳 SQLite 程式庫版本字串（例：3.46.1）。載入失敗時擲出例外。</summary>
    public static string GetLibraryVersion()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "select sqlite_version()";
        return (string)command.ExecuteScalar()!;
    }
}
