using System.IO;
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
    public App()
    {
        ShellSafety.LogPath = Path.Combine(Shell.MainWindow.DataDirectory, ShellSafety.LogFileName);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    /// <summary>
    /// UI 執行緒的未處理例外：記錄後標為已處理，程式繼續跑。這是單機排班工具，每個用例一次 commit、
    /// 資料都已落盤，閃退只會讓使用者丟掉畫面上的操作脈絡，繼續跑的風險較小。
    /// </summary>
    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        ShellSafety.Report("Dispatcher", e.Exception);
        e.Handled = true;
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
