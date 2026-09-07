namespace Scheduler.Shell;

/// <summary>
/// WPF 應用程式進入點。Shell 只引用 Scheduler.Api（硬性規則 2），且關掉了傳遞引用，
/// 所以 Scheduler.Application 命名空間在這裡看不到，不會與 System.Windows.Application 撞名；
/// 基底型別仍完整限定，免得日後有人把傳遞引用打開時默默撞回去。
/// </summary>
public partial class App : System.Windows.Application
{
}
