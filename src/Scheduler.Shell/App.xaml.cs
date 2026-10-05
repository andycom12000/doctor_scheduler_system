using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace Scheduler.Shell;

/// <summary>
/// WPF 應用程式進入點。Shell 只引用 Scheduler.Api（硬性規則 2），且關掉了傳遞引用，
/// 所以 Scheduler.Application 命名空間在這裡看不到，不會與 System.Windows.Application 撞名；
/// 基底型別仍完整限定，免得日後有人把傳遞引用打開時默默撞回去。
/// 另外在這裡掛最後一道例外防線（#101），紀錄寫在程式旁的 data/shell-error.log。
/// </summary>
public partial class App : System.Windows.Application
{
    /// <summary>MainWindow 啟動流程跑完（頁面已導向）後設為 true；在這之前的 UI 例外一律視為啟動失敗。</summary>
    internal static volatile bool StartupCompleted;

    public App()
    {
        // 先掛處理器再做其他事；路徑自己算，不碰 MainWindow 型別（避免提早觸發它的 static 初始化）
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        ShellSafety.LogPath = Path.Combine(AppContext.BaseDirectory, "data", ShellSafety.LogFileName);
    }

    /// <summary>
    /// UI 執行緒的未處理例外。啟動完成後：記錄並標為已處理，程式繼續跑（單機工具、每個用例一次 commit、
    /// 資料都已落盤，閃退只會讓使用者丟掉畫面脈絡）。啟動完成前或沒有任何視窗時：繼續跑只會留下看不見的
    /// 殘留程序，所以告知紀錄檔位置後結束。
    /// </summary>
    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        ShellSafety.Report("Dispatcher", e.Exception);
        e.Handled = true;

        var app = Current;
        if (StartupCompleted && app?.MainWindow is not null && app.Windows.Count > 0) return;

        ShellSafety.Try(() => MessageBox.Show(
            $"程式發生錯誤無法繼續：{e.Exception.Message}\n\n詳細紀錄：{ShellSafety.LogPath}",
            "醫院排班系統", MessageBoxButton.OK, MessageBoxImage.Error), "Dispatcher.MessageBox");
        ShellSafety.Try(() => app?.Shutdown(1), "Dispatcher.Shutdown");
    }

    /// <summary>非 UI 執行緒的例外擋不住（runtime 一定終止），只能留一行紀錄。</summary>
    private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex) ShellSafety.Report(e.IsTerminating ? "AppDomain(terminating)" : "AppDomain", ex);
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        ShellSafety.Report("UnobservedTask", e.Exception);
        e.SetObserved();
    }
}
