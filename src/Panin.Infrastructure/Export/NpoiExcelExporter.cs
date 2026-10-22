using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using Panin.Application.Export;

namespace Panin.Infrastructure.Export;

public sealed class NpoiExcelExporter : IExcelExporter
{
    private const int MaxSheetNameLength = 31;
    private const int MaxColumnWidthChars = 60;

    public byte[] Export(ExportTable table)
    {
        using var workbook = new XSSFWorkbook();
        var sheet = workbook.CreateSheet(SafeSheetName(table.SheetName));

        var headerStyle = workbook.CreateCellStyle();
        var headerFont = workbook.CreateFont();
        headerFont.IsBold = true;
        headerStyle.SetFont(headerFont);
        headerStyle.FillForegroundColor = IndexedColors.Grey25Percent.Index;
        headerStyle.FillPattern = FillPattern.SolidForeground;

        var format = workbook.CreateDataFormat();
        var styles = new Dictionary<ExportFormat, ICellStyle>
        {
            [ExportFormat.Count] = Style(workbook, format.GetFormat("#,##0")),
            [ExportFormat.Amount] = Style(workbook, format.GetFormat("#,##0.00")),
            [ExportFormat.Date] = Style(workbook, format.GetFormat("dd-mmm-yyyy")),
        };

        var header = sheet.CreateRow(0);
        for (var c = 0; c < table.Columns.Count; c++)
        {
            var cell = header.CreateCell(c);
            cell.SetCellValue(table.Columns[c].Header);
            cell.CellStyle = headerStyle;
        }

        for (var r = 0; r < table.Rows.Count; r++)
        {
            var row = sheet.CreateRow(r + 1);
            var values = table.Rows[r];
            for (var c = 0; c < table.Columns.Count && c < values.Count; c++)
            {
                Write(row.CreateCell(c), values[c], table.Columns[c].Format, styles);
            }
        }

        // Lebar kolom dihitung dari panjang isi. AutoSizeColumn NPOI memakai SkiaSharp (pustaka native + font),
        // yang tidak boleh menjadi syarat server.
        for (var c = 0; c < table.Columns.Count; c++)
        {
            var longest = table.Columns[c].Header.Length;
            foreach (var values in table.Rows)
            {
                if (c < values.Count && values[c] is { } v)
                {
                    longest = Math.Max(longest, v is DateTime ? 11 : Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture)?.Length ?? 0);
                }
            }

            sheet.SetColumnWidth(c, Math.Min(MaxColumnWidthChars, longest + 2) * 256);
        }

        sheet.CreateFreezePane(0, 1);
        using var stream = new MemoryStream();
        workbook.Write(stream, leaveOpen: true);
        return stream.ToArray();
    }

    private static ICellStyle Style(XSSFWorkbook workbook, short dataFormat)
    {
        var style = workbook.CreateCellStyle();
        style.DataFormat = dataFormat;
        return style;
    }

    private static void Write(ICell cell, object? value, ExportFormat format, Dictionary<ExportFormat, ICellStyle> styles)
    {
        switch (value)
        {
            case null:
                return;
            case DateTime d when format == ExportFormat.Date || format == ExportFormat.Text:
                cell.SetCellValue(d);
                cell.CellStyle = styles[ExportFormat.Date];
                return;
            case int or long or short or byte or double or float or decimal:
                cell.SetCellValue(Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture));
                if (styles.TryGetValue(format, out var numeric))
                {
                    cell.CellStyle = numeric;
                }

                return;
            default:
                cell.SetCellValue(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture));
                return;
        }
    }

    private static string SafeSheetName(string name)
    {
        var cleaned = new string([.. (string.IsNullOrWhiteSpace(name) ? "Data" : name).Where(ch => !"[]:*?/\\".Contains(ch, StringComparison.Ordinal))]);
        return cleaned.Length > MaxSheetNameLength ? cleaned[..MaxSheetNameLength] : cleaned;
    }
}
