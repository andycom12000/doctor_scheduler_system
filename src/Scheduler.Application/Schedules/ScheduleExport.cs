namespace Scheduler.Application.Schedules;

/// <summary>契約 <c>exportSchedule</c> 的 <c>layout</c> 查詢參數，跟隨前端目前的檢視分頁。</summary>
public enum ExportLayout
{
    /// <summary>列 = 區域、欄 = 日，格子是人名。</summary>
    AreaByDay,

    /// <summary>列 = 日、欄 = 人員，格子是區域代碼。</summary>
    DayByStaff,
}

/// <summary>
/// 匯出用的格式無關表格。Application 只決定「哪一列哪一欄放什麼字」，
/// 轉成 xlsx 位元組是 Api 包回應的事（ARCHITECTURE §6.4），所以這裡沒有任何試算表型別。
/// </summary>
/// <param name="SheetName">工作表名稱，也是檔名的一部分。</param>
/// <param name="CornerLabel">左上角那一格（第一欄的標題）。</param>
/// <param name="Columns">第一列的欄標題，不含左上角。</param>
/// <param name="Rows">每一列：列標題與對應每個欄的格子；格子數等於 <see cref="Columns"/> 數。</param>
public sealed record ExportTable(
    string SheetName,
    string CornerLabel,
    IReadOnlyList<ExportColumn> Columns,
    IReadOnlyList<ExportRow> Rows);

/// <summary>欄標題與它是不是假日（含國定假日），讓輸出端可以上底色。</summary>
/// <param name="Header">顯示文字。</param>
/// <param name="IsHoliday">假日（週六、週日或國定假日）。只有欄是日期的版面會是 true。</param>
public sealed record ExportColumn(string Header, bool IsHoliday = false);

/// <summary>一列：列標題、是不是假日（列是日期的版面才會 true）、每欄的格子文字（空字串代表沒排）。</summary>
public sealed record ExportRow(string Header, bool IsHoliday, IReadOnlyList<string> Cells);
