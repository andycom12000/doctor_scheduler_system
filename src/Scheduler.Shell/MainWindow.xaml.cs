using System.Windows;

namespace Scheduler.Shell;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // TODO: WebView2 宿主。docs/ARCHITECTURE.md §4 已定義完整規格：
        //   §4.1 CoreWebView2Environment.CreateAsync —— browserExecutableFolder 指向隨附的
        //        webview2/，userDataFolder 指向 data/wv2data（不可省略，否則寫入 %LOCALAPPDATA%）
        //   §4.2 SetVirtualHostNameToFolderMapping("app.local", wwwroot)
        //        + AddWebResourceRequestedFilter("https://app.local/api/*")
        //   §4.3 非 /api 且無副檔名的 Document 請求一律回 index.html（SPA deep link）
        //   §4.4 PostWebMessageAsJson 推送求解進度
        //   §4.5 Debug 建置開啟 DevTools，Release 關閉
        //
        // 啟動時另須檢查 data/ 可寫，不可寫則明確報錯而非崩潰（驗收清單第 4 項）。
    }
}
