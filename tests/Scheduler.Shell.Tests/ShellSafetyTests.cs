namespace Scheduler.Shell.Tests;

/// <summary>例外防線的純邏輯（#101）：吞例外、記一行、記錄本身失敗也不外洩。ShellSafety.LogPath 是全域的，整個 class 序列執行。</summary>
[Collection("ShellSafety")]
public class ShellSafetyTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "shell-safety-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        ShellSafety.LogPath = null;
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [Fact]
    public void FormatLine_單行且帶時間來源與內層例外()
    {
        var ex = new InvalidOperationException("外層\n換行", new IOException("內層"));
        var line = ShellSafety.FormatLine(new DateTimeOffset(2026, 10, 6, 1, 2, 3, TimeSpan.FromHours(8)), "Src", ex);

        Assert.DoesNotContain('\n', line);
        Assert.StartsWith("2026-10-05T17:02:03.0000000Z [Src] System.InvalidOperationException: 外層 換行", line);
        Assert.Contains("<- System.IO.IOException: 內層", line);
    }

    [Fact]
    public void Try_丟例外回false不外洩並寫入紀錄檔()
    {
        ShellSafety.LogPath = Path.Combine(_dir, "data", ShellSafety.LogFileName);

        var ok = ShellSafety.Try(() => throw new InvalidOperationException("boom"), "Test");

        Assert.False(ok);
        var text = File.ReadAllText(ShellSafety.LogPath);
        Assert.Contains("[Test]", text);
        Assert.Contains("boom", text);
    }

    [Fact]
    public void Try_成功回true且不寫紀錄()
    {
        ShellSafety.LogPath = Path.Combine(_dir, ShellSafety.LogFileName);
        Assert.True(ShellSafety.Try(() => { }, "Test"));
        Assert.False(File.Exists(ShellSafety.LogPath));
    }

    [Fact]
    public async Task TryAsync_await之後才丟的例外也被吞()
    {
        ShellSafety.LogPath = Path.Combine(_dir, ShellSafety.LogFileName);

        var ok = await ShellSafety.TryAsync(async () =>
        {
            await Task.Yield();
            throw new InvalidOperationException("late");
        }, "Async");

        Assert.False(ok);
        Assert.Contains("late", File.ReadAllText(ShellSafety.LogPath));
    }

    [Fact]
    public async Task TryAsync_預期內的取消不記錄_非預期的取消要記()
    {
        ShellSafety.LogPath = Path.Combine(_dir, ShellSafety.LogFileName);

        await ShellSafety.TryAsync(() => throw new OperationCanceledException(), "Cancel", () => true);
        Assert.False(File.Exists(ShellSafety.LogPath));

        await ShellSafety.TryAsync(() => throw new OperationCanceledException(), "Cancel", () => false);
        Assert.Contains("[Cancel]", File.ReadAllText(ShellSafety.LogPath));
    }

    [Fact]
    public void Report_紀錄路徑不可寫時不丟例外()
    {
        // 路徑的上層是一個檔案，CreateDirectory／AppendAllText 必然失敗
        Directory.CreateDirectory(_dir);
        var blocker = Path.Combine(_dir, "file");
        File.WriteAllText(blocker, "x");
        ShellSafety.LogPath = Path.Combine(blocker, "sub", ShellSafety.LogFileName);

        var ex = Record.Exception(() => ShellSafety.Report("Test", new InvalidOperationException("x")));

        Assert.Null(ex);
    }

    [Fact]
    public void Report_未設路徑時不丟例外()
    {
        ShellSafety.LogPath = null;
        Assert.Null(Record.Exception(() => ShellSafety.Report("Test", new InvalidOperationException("x"))));
    }
}
