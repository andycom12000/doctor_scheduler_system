using ClosedXML.Excel;
using Scheduler.Application.Schedules;

namespace Scheduler.Api.Export;

/// <summary>
/// 把 Application 的 <see cref="ExportTable"/> 寫成 xlsx 位元組（ARCHITECTURE §6.4）。
/// 內容全由 Application 決定，這裡只管格式：標題列凍結、假日欄／列上底色、欄寬依內容。
/// 全程在記憶體裡，portable 環境沒有暫存檔可以放。
/// </summary>
internal static class XlsxRenderer
{
    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private static readonly XLColor HolidayFill = XLColor.FromArgb(0xFF, 0xF2, 0xF2);

    public static byte[] Render(ExportTable table)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add(SafeSheetName(table.SheetName));

        sheet.Cell(1, 1).Value = table.CornerLabel;
        for (var c = 0; c < table.Columns.Count; c++)
        {
            var cell = sheet.Cell(1, c + 2);
            cell.Value = table.Columns[c].Header;
            if (table.Columns[c].IsHoliday)
            {
                cell.Style.Fill.BackgroundColor = HolidayFill;
            }
        }

        for (var r = 0; r < table.Rows.Count; r++)
        {
            var row = table.Rows[r];
            sheet.Cell(r + 2, 1).Value = row.Header;
            for (var c = 0; c < row.Cells.Count; c++)
            {
                sheet.Cell(r + 2, c + 2).Value = row.Cells[c];
            }

            if (row.IsHoliday)
            {
                sheet.Row(r + 2).Style.Fill.BackgroundColor = HolidayFill;
            }
        }

        var used = sheet.Range(1, 1, table.Rows.Count + 1, table.Columns.Count + 1);
        used.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        used.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        used.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        sheet.Row(1).Style.Font.Bold = true;
        sheet.Column(1).Style.Font.Bold = true;
        sheet.SheetView.FreezeRows(1);
        sheet.SheetView.FreezeColumns(1);
        sheet.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    /// <summary>Excel 的工作表名稱上限 31 字、不得含 <c>: \ / ? * [ ]</c>。</summary>
    private static string SafeSheetName(string name)
    {
        var cleaned = new string(name.Select(ch => ch is ':' or '\\' or '/' or '?' or '*' or '[' or ']' ? '-' : ch).ToArray());
        return cleaned.Length <= 31 ? cleaned : cleaned[..31];
    }
}
