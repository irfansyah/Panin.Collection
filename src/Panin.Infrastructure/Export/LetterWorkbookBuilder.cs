using System.Globalization;
using System.Text.RegularExpressions;
using NPOI.HSSF.UserModel;
using NPOI.SS.UserModel;
using NPOI.SS.Util;

namespace Panin.Infrastructure.Export;

/// <summary>Jenis surat dan nama sheet template-nya (<c>RFSURAT.SU_TYPE</c>, <c>SU_SHEET</c>).</summary>
public sealed record LetterSheet(string Type, string SheetName);

/// <summary>
/// Mengisi template surat (<c>surat.xls</c>) tanpa Excel/Office terpasang (pengganti <c>DMS.Interface.iExcel.generate_excel_report</c>).
/// Aturan template yang dipertahankan: <b>nama field ada di cell comment</b> sel template (beberapa field dipisah koma); sel bertulisan
/// dengan penanda <c>@1</c>, <c>@2</c>, ... diganti sesuai urutan field; sel kosong diisi nilai field (angka tetap angka sehingga format sel
/// berlaku). Isi sheet satu surat diulang untuk setiap baris data, ditumpuk ke bawah dengan pemisah halaman. Sheet yang tidak dipakai dibuang
/// dari hasil; comment dihapus; sheet dan workbook dilindungi password bila diberikan.
/// </summary>
public static partial class LetterWorkbookBuilder
{
    private static readonly CultureInfo Id = CultureInfo.GetCultureInfo("id-ID");

    [GeneratedRegex(@"@(\d+)")]
    private static partial Regex Placeholder();

    private sealed record CellSnap(int Column, ICellStyle Style, CellType Type, string? Text, double Number, bool Boolean, string? Formula, string[] Fields);

    private sealed record RowSnap(int Index, float Height, List<CellSnap> Cells);

    public static byte[] Build(
        Stream template,
        IReadOnlyList<LetterSheet> sheets,
        IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>> rowsByType,
        string? password)
    {
        var wb = new HSSFWorkbook(template);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var letter in sheets)
        {
            if (!rowsByType.TryGetValue(letter.Type, out var rows) || rows.Count == 0)
            {
                continue;
            }

            var sheet = wb.GetSheet(letter.SheetName)
                ?? throw new InvalidOperationException($"Template tidak memiliki sheet \"{letter.SheetName}\" untuk jenis surat {letter.Type}.");
            Fill(sheet, rows);
            used.Add(sheet.SheetName);
        }

        if (used.Count == 0)
        {
            throw new InvalidOperationException("Tidak ada data surat untuk ditulis ke template.");
        }

        for (var i = wb.NumberOfSheets - 1; i >= 0; i--)
        {
            if (!used.Contains(wb.GetSheetName(i)))
            {
                wb.RemoveSheetAt(i);
            }
        }

        wb.SetActiveSheet(0);
        wb.SetSelectedTab(0);
        if (!string.IsNullOrEmpty(password))
        {
            for (var i = 0; i < wb.NumberOfSheets; i++)
            {
                wb.GetSheetAt(i).ProtectSheet(password);
            }

            wb.WriteProtectWorkbook(password, "panin");
        }

        using var output = new MemoryStream();
        wb.Write(output, leaveOpen: true);
        return output.ToArray();
    }

    private static void Fill(ISheet sheet, IReadOnlyList<IReadOnlyDictionary<string, object?>> rows)
    {
        var (snapshot, height, merged) = Snapshot(sheet);
        Clear(sheet);

        for (var n = 0; n < rows.Count; n++)
        {
            var offset = n * height;
            foreach (var r in snapshot)
            {
                var row = sheet.CreateRow(offset + r.Index);
                row.Height = (short)(r.Height * 20);
                foreach (var c in r.Cells)
                {
                    var cell = row.CreateCell(c.Column);
                    cell.CellStyle = c.Style;
                    WriteCell(cell, c, rows[n]);
                }
            }

            foreach (var m in merged)
            {
                sheet.AddMergedRegion(new CellRangeAddress(m.FirstRow + offset, m.LastRow + offset, m.FirstColumn, m.LastColumn));
            }

            sheet.SetRowBreak(offset + height - 1);
        }
    }

    private static (List<RowSnap> Rows, int Height, List<CellRangeAddress> Merged) Snapshot(ISheet sheet)
    {
        var rows = new List<RowSnap>();
        for (var r = sheet.FirstRowNum; r <= sheet.LastRowNum; r++)
        {
            var row = sheet.GetRow(r);
            if (row is null)
            {
                continue;
            }

            var cells = new List<CellSnap>();
            foreach (var c in row.Cells)
            {
                var fields = c.CellComment?.String.String
                    .Split([',', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];
                cells.Add(c.CellType switch
                {
                    CellType.String => new CellSnap(c.ColumnIndex, c.CellStyle, CellType.String, c.StringCellValue, 0, false, null, fields),
                    CellType.Numeric => new CellSnap(c.ColumnIndex, c.CellStyle, CellType.Numeric, null, c.NumericCellValue, false, null, fields),
                    CellType.Boolean => new CellSnap(c.ColumnIndex, c.CellStyle, CellType.Boolean, null, 0, c.BooleanCellValue, null, fields),
                    CellType.Formula => new CellSnap(c.ColumnIndex, c.CellStyle, CellType.Formula, null, 0, false, c.CellFormula, fields),
                    _ => new CellSnap(c.ColumnIndex, c.CellStyle, CellType.Blank, null, 0, false, null, fields),
                });
            }

            rows.Add(new RowSnap(r, row.HeightInPoints, cells));
        }

        var merged = Enumerable.Range(0, sheet.NumMergedRegions).Select(sheet.GetMergedRegion).ToList();
        return (rows, Math.Max(1, sheet.LastRowNum + 1), merged);
    }

    private static void Clear(ISheet sheet)
    {
        for (var i = sheet.NumMergedRegions - 1; i >= 0; i--)
        {
            sheet.RemoveMergedRegion(i);
        }

        for (var r = sheet.LastRowNum; r >= sheet.FirstRowNum; r--)
        {
            var row = sheet.GetRow(r);
            if (row is null)
            {
                continue;
            }

            foreach (var c in row.Cells)
            {
                c.RemoveCellComment(); // comment template tidak boleh terbawa ke hasil
            }

            sheet.RemoveRow(row);
        }

        foreach (var brk in sheet.RowBreaks)
        {
            sheet.RemoveRowBreak(brk);
        }
    }

    private static void WriteCell(ICell cell, CellSnap snap, IReadOnlyDictionary<string, object?> data)
    {
        if (snap.Fields.Length == 0)
        {
            switch (snap.Type)
            {
                case CellType.String: cell.SetCellValue(snap.Text); break;
                case CellType.Numeric: cell.SetCellValue(snap.Number); break;
                case CellType.Boolean: cell.SetCellValue(snap.Boolean); break;
                case CellType.Formula: cell.SetCellFormula(snap.Formula); break;
                default: cell.SetCellType(CellType.Blank); break;
            }

            return;
        }

        var values = snap.Fields.Select(f => data.TryGetValue(f, out var v) && v is not DBNull ? v : null).ToArray();
        if (snap.Type == CellType.String && snap.Text is { } text && Placeholder().IsMatch(text))
        {
            cell.SetCellValue(Placeholder().Replace(text, m =>
            {
                var index = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) - 1;
                return index >= 0 && index < values.Length ? AsText(values[index]) : m.Value;
            }));
            return;
        }

        // Sel berkomentar tanpa penanda: nilai field pertama menggantikan isi sel.
        switch (values[0])
        {
            case null:
                cell.SetCellType(CellType.Blank);
                break;
            case double or float or decimal or int or long or short:
                cell.SetCellValue(Convert.ToDouble(values[0], CultureInfo.InvariantCulture));
                break;
            default:
                cell.SetCellValue(AsText(values[0]));
                break;
        }
    }

    private static string AsText(object? value) => value switch
    {
        null => string.Empty,
        string s => s,
        DateTime d => d.ToString("dd MMMM yyyy", Id),
        double or float or decimal => Convert.ToDecimal(value, CultureInfo.InvariantCulture).ToString("N2", Id),
        IFormattable f => f.ToString(null, Id),
        _ => Convert.ToString(value, Id) ?? string.Empty,
    };
}
