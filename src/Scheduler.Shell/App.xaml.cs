namespace Scheduler.Shell;

/// <summary>
/// WPF 應用程式進入點。
///
/// 注意：本組件同時引用 Scheduler.Application 命名空間，會與 System.Windows.Application
/// 型別撞名，因此基底型別必須完整限定。整個 Scheduler.Shell 都適用這條。
/// </summary>
public partial class App : System.Windows.Application
{
}
