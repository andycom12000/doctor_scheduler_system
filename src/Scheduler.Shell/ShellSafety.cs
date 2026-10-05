using System.Globalization;
using System.IO;
using System.Text;

namespace Scheduler.Shell;

/// <summary>
/// Shell 的例外防線（#101）。WebView2 的事件、<c>async void</c> 與 fire-and-forget 路徑沒有呼叫端可接例外，
/// 一旦外洩整個 process 就結束；請求被前端中止、視窗關閉中時，COM 呼叫丟例外是常態而不是異常。
/// 這裡只放不依賴 WPF／WebView2 型別的純邏輯，讓 tests/Scheduler.Shell.Tests 連結原始檔就能測。
/// 紀錄檔只寫 <c>data/</c>（portable：不碰 %APPDATA%）。
/// </summary>
internal static class ShellSafety
{
    public const string LogFileName = "shell-error.log";

    /// <summary>紀錄檔完整路徑；null 表示尚未設定（只寫 Trace）。由 <c>App</c> 啟動時指向 data/。</summary>
    public static string? LogPath { get; set; }

    /// <summary>單行紀錄：<c>2026-10-06T01:02:03.0000000Z [來源] 例外型別: 訊息</c>。換行折成空白，一筆一行。</summary>
    public static string FormatLine(DateTimeOffset at, string source, Exception ex)
    {
        var text = $"{ex.GetType().FullName}: {ex.Message}";
        var inner = ex.InnerException;
        for (var depth = 0; inner is not null && depth < 3; depth++, inner = inner.InnerException)
        {
            text += $" <- {inner.GetType().FullName}: {inner.Message}";
        }

        var line = $"{at.UtcDateTime.ToString("o", CultureInfo.InvariantCulture)} [{source}] {text}";
        return line.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ');
    }

    /// <summary>紀錄檔上限；超過就輪替成 <c>.1</c>（只留一份舊檔）。測試可調小。</summary>
    public static long MaxLogBytes { get; set; } = 1024 * 1024;

    /// <summary>視窗關閉中：COM 與已 dispose 的物件丟例外是預期的，不寫紀錄免得關閉時一堆雜訊。</summary>
    public static volatile bool Closing;

    private static readonly object LogLock = new();

    /// <summary>
    /// 記一筆。絕不丟例外：記錄本身失敗（唯讀、磁碟滿）就只剩 Trace。
    /// <paramref name="force"/> 為 true 時不受 <see cref="Closing"/> 影響（process 即將終止的例外一定要留）。
    /// <paramref name="toFile"/> 為 false 時只寫 Trace（次要的連帶失敗，避免一次中止寫好幾行）。
    /// </summary>
    public static void Report(string source, Exception ex, bool toFile = true, bool force = false)
    {
        try
        {
            if (Closing && !force) return;
            var line = FormatLine(DateTimeOffset.UtcNow, source, ex);
            System.Diagnostics.Trace.TraceWarning(line);
            var path = LogPath;
            if (path is null || !toFile) return;
            lock (LogLock)
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                var info = new FileInfo(path);
                if (info.Exists && info.Length > MaxLogBytes)
                {
                    try
                    {
                        File.Move(path, path + ".1", overwrite: true);
                    }
                    catch
                    {
                        // 舊檔被鎖住等：搬不動就照樣 append 到主檔，不能因此丟掉這一筆
                    }
                }

                File.AppendAllText(path, line + Environment.NewLine, new UTF8Encoding(false));
            }
        }
        catch
        {
            // 最後一道防線，沒有更後面可以回報了
        }
    }

    /// <summary>執行動作，吞掉例外並記錄；成功回 true。</summary>
    public static bool Try(Action action, string source, bool toFile = true)
    {
        try
        {
            action();
            return true;
        }
        catch (Exception ex)
        {
            Report(source, ex, toFile);
            return false;
        }
    }

    /// <summary>
    /// 非同步版。<paramref name="expectedCancel"/> 為真時，<see cref="OperationCanceledException"/> 視為正常結束不記錄
    /// （例如視窗關閉中取消進度迴圈）。
    /// </summary>
    public static async Task<bool> TryAsync(Func<Task> action, string source, Func<bool>? expectedCancel = null)
    {
        try
        {
            await action();
            return true;
        }
        catch (OperationCanceledException) when (expectedCancel?.Invoke() == true)
        {
            return false;
        }
        catch (Exception ex)
        {
            Report(source, ex);
            return false;
        }
    }
}
