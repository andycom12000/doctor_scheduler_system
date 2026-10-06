namespace Scheduler.Shell;

/// <summary>
/// 起始視窗尺寸夾進工作區（單位都是 DIP）。1366×768 的筆電工作區只有約 720 高，
/// 預設 1280×800 會讓 CenterScreen 把標題列推出螢幕上緣。純計算、沒有 WPF 型別，好測。
/// </summary>
public static class WindowSizing
{
    /// <summary>
    /// 回傳夾過的 (寬, 高)：不超過工作區，但不低於 <paramref name="minWidth"/>／<paramref name="minHeight"/>
    /// （工作區比最小值還小時以最小值為準，使用者仍可自行拖動）。
    /// </summary>
    public static (double Width, double Height) Clamp(
        double width, double height,
        double workAreaWidth, double workAreaHeight,
        double minWidth, double minHeight)
    {
        var w = Math.Max(minWidth, Math.Min(width, workAreaWidth));
        var h = Math.Max(minHeight, Math.Min(height, workAreaHeight));
        return (w, h);
    }
}
