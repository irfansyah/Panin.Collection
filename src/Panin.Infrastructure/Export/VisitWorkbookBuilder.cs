using System.Data;
using System.Globalization;
using NPOI.HSSF.UserModel;
using NPOI.SS.UserModel;
using NPOI.SS.Util;

namespace Panin.Infrastructure.Export;

/// <summary>
/// Data untuk berkas Excel agen visit (<c>Visit.xls</c>). Dihasilkan <c>SP_GENERATE_EXCEL_VISIT</c>, <c>_VISIT2</c>, dan <c>_VISIT4</c>.
/// </summary>
/// <param name="Visit">Baris lembar "Visit"; kolom dibaca menurut <b>posisi</b> (A, B, C, ...) seperti pencocokan <c>iExcel</c> lama.</param>
/// <param name="Lists">Empat daftar pilihan "kode - deskripsi": tujuan kunjungan (G), hubungan (J), janji debitur (K), rekomendasi (M).</param>
/// <param name="Letter">Baris <c>VW_ACCOUNT_SURAT2</c>, satu halaman "Surat Visit" per baris; kolom dibaca menurut nama (comment sel template).</param>
/// <param name="LetterDetail">Rincian tunggakan per rekening, kunci <paramref name="DetailKey"/>; menjadi baris berulang pada area <paramref name="DetailRow"/>.</param>
/// <param name="Slip">Baris slip setoran, satu halaman "Slip Bukti Setoran" per baris.</param>
public sealed record VisitWorkbookData(
    DataTable Visit,
    IReadOnlyList<IReadOnlyList<string>> Lists,
    DataTable Letter,
    DataTable LetterDetail,
    DataTable Slip,
    int DetailRow = 15,
    string DetailKey = "ACC_NO");

/// <summary>
/// Mengisi templat <c>Visit.xls</c> tanpa Excel/Office (pengganti rangkaian <c>iExcel.generate_excel</c> + <c>generate_excel_report</c> di
/// <c>WorklistMonitoring.BTN_VISITAGENCY_Click</c>). Yang dipertahankan dari layar lama (dicocokkan dengan contoh keluaran lama):
/// lembar "Visit" berisi satu baris per rekening dengan gaya sel templat (kolom A–E dan AG terkunci, kolom lain terbuka untuk diisi agen),
/// validasi isian, dan empat daftar pilihan di kolom AX–BA yang dipasang sebagai dropdown pada kolom G, J, K, M;
/// lembar "Surat Visit" satu halaman per rekening dengan tabel rincian tunggakan yang dibuka sesuai jumlah baris;
/// lembar "Slip Bukti Setoran" satu halaman per rekening; semua lembar dilindungi sandi.
/// <para>
/// <b>Perbaikan atas keluaran lama:</b> (1) validasi diterapkan ke setiap baris data, bukan hanya baris 2; (2) rentang dropdown tepat pada isi daftar
/// (lama menyertakan satu sel kosong di atas); (3) daftar pendukung templat (kolom B–E baris 25–29 dan daftar kabupaten kolom U baris 50–377) dipindah ke
/// kolom BC–BH agar tidak tertimpa data ketika rekening lebih dari 23 (lama menimpa daftar itu).
/// </para>
/// </summary>
public static class VisitWorkbookBuilder
{
    private const int FirstDataRow = 1;
    private const int ListColumn = 49; // AX
    private const int HelperOffset = 54 - 1; // blok B..E -> BC..BF
    private const int CityColumn = 59; // BH

    public static byte[] Build(Stream template, VisitWorkbookData data, string? password)
    {
        var wb = new HSSFWorkbook(template);
        BuildVisitSheet(wb, data);
        FillLetterPages(wb.GetSheet("Surat Visit") ?? throw new InvalidOperationException("Templat tidak memiliki sheet \"Surat Visit\"."), data);
        var slip = wb.GetSheet("Slip Bukti Setoran") ?? throw new InvalidOperationException("Templat tidak memiliki sheet \"Slip Bukti Setoran\".");
        LetterWorkbookBuilder.Fill(slip, Rows(data.Slip));
        slip.SetColumnBreak(23);

        wb.SetActiveSheet(0);
        wb.SetSelectedTab(0);
        if (!string.IsNullOrEmpty(password))
        {
            for (var i = 0; i < wb.NumberOfSheets; i++)
            {
                wb.GetSheetAt(i).ProtectSheet(password);
            }
        }

        using var output = new MemoryStream();
        wb.Write(output, leaveOpen: true);
        return output.ToArray();
    }

    private static void BuildVisitSheet(HSSFWorkbook wb, VisitWorkbookData data)
    {
        var src = (HSSFSheet)(wb.GetSheet("Visit") ?? throw new InvalidOperationException("Templat tidak memiliki sheet \"Visit\"."));
        var dst = (HSSFSheet)wb.CreateSheet("Visit_baru");
        var count = data.Visit.Rows.Count;
        var lastRow = FirstDataRow + count - 1;

        dst.DefaultColumnWidth = src.DefaultColumnWidth;
        dst.DefaultRowHeightInPoints = src.DefaultRowHeightInPoints;
        for (var c = 0; c <= 70; c++)
        {
            if (src.GetColumnWidth(c) != src.DefaultColumnWidth * 256)
            {
                dst.SetColumnWidth(c, src.GetColumnWidth(c));
            }

            if (src.IsColumnHidden(c))
            {
                dst.SetColumnHidden(c, true);
            }
        }

        // Baris judul: nilai, gaya, dan sel gabung apa adanya.
        CopyRow(src.GetRow(0), dst.CreateRow(0));
        for (var i = 0; i < src.NumMergedRegions; i++)
        {
            var m = src.GetMergedRegion(i);
            if (m.FirstRow == 0 && m.LastRow == 0)
            {
                dst.AddMergedRegion(m);
            }
        }

        // Baris data: gaya baris 2 templat untuk setiap rekening, nilai menurut posisi kolom.
        var templateRow = src.GetRow(FirstDataRow);
        for (var i = 0; i < count; i++)
        {
            var row = dst.CreateRow(FirstDataRow + i);
            if (templateRow is not null)
            {
                row.HeightInPoints = templateRow.HeightInPoints;
                foreach (var tc in templateRow.Cells)
                {
                    var cell = row.CreateCell(tc.ColumnIndex);
                    cell.CellStyle = tc.CellStyle;
                }
            }

            var values = data.Visit.Rows[i].ItemArray;
            for (var c = 0; c < values.Length; c++)
            {
                SetValue(row.GetCell(c) ?? row.CreateCell(c), values[c]);
            }
        }

        // Daftar pendukung templat dipindah ke kolom yang tidak bisa tertimpa data.
        for (var r = 24; r <= 28; r++)
        {
            for (var c = 1; c <= 4; c++)
            {
                var text = src.GetRow(r)?.GetCell(c)?.ToString();
                if (!string.IsNullOrEmpty(text))
                {
                    (dst.GetRow(r) ?? dst.CreateRow(r)).CreateCell(c + HelperOffset).SetCellValue(text);
                }
            }
        }

        for (var r = 49; r <= 376; r++)
        {
            var text = src.GetRow(r)?.GetCell(20)?.ToString();
            if (!string.IsNullOrEmpty(text))
            {
                (dst.GetRow(r) ?? dst.CreateRow(r)).CreateCell(CityColumn).SetCellValue(text);
            }
        }

        // Empat daftar pilihan di AX..BA, mulai baris 2.
        for (var l = 0; l < data.Lists.Count && l < 4; l++)
        {
            for (var i = 0; i < data.Lists[l].Count; i++)
            {
                (dst.GetRow(FirstDataRow + i) ?? dst.CreateRow(FirstDataRow + i)).CreateCell(ListColumn + l).SetCellValue(data.Lists[l][i]);
            }
        }

        if (count > 0)
        {
            CloneValidations(src, dst, lastRow);
            string[] targets = ["G", "J", "K", "M"];
            for (var l = 0; l < data.Lists.Count && l < 4; l++)
            {
                if (data.Lists[l].Count == 0)
                {
                    continue;
                }

                var col = CellReference.ConvertColStringToIndex(targets[l]);
                var listCol = CellReference.ConvertNumToColString(ListColumn + l);
                var constraint = DVConstraint.CreateFormulaListConstraint($"${listCol}$2:${listCol}${1 + data.Lists[l].Count}");
                dst.AddValidationData(new HSSFDataValidation(new CellRangeAddressList(FirstDataRow, lastRow, col, col), constraint));
            }
        }

        var index = wb.GetSheetIndex(src);
        wb.RemoveSheetAt(index);
        wb.SetSheetName(wb.GetSheetIndex(dst), "Visit");
        wb.SetSheetOrder("Visit", index);
    }

    // Validasi templat hanya ada di baris 2; diperluas ke seluruh baris data. Rujukan daftar pendukung diarahkan ke lokasi barunya.
    private static void CloneValidations(HSSFSheet src, HSSFSheet dst, int lastRow)
    {
        var moved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["$C$26:$C$29"] = "$BD$26:$BD$29",
            ["$D$26:$D$28"] = "$BE$26:$BE$28",
            ["$E$26:$E$28"] = "$BF$26:$BF$28",
            ["$U$50:$U$377"] = "$BH$50:$BH$377",
        };

        foreach (var dv in src.GetDataValidations())
        {
            var c = dv.ValidationConstraint;
            var type = c.GetValidationType();
            IDataValidationConstraint constraint;
            if (type == ValidationType.LIST)
            {
                constraint = DVConstraint.CreateFormulaListConstraint(moved.TryGetValue(c.Formula1 ?? string.Empty, out var target) ? target : c.Formula1);
            }
            else if (type == ValidationType.DATE)
            {
                constraint = DVConstraint.CreateDateConstraint(c.Operator, AsFormula(c.Formula1), AsFormula(c.Formula2), null);
            }
            else if (type is ValidationType.INTEGER or ValidationType.DECIMAL or ValidationType.TEXT_LENGTH)
            {
                constraint = DVConstraint.CreateNumericConstraint(type, c.Operator, c.Formula1, c.Formula2);
            }
            else
            {
                continue; // validasi "apa saja" (hanya pesan input) tidak berpengaruh
            }

            var regions = new CellRangeAddressList();
            foreach (var r in dv.Regions.CellRangeAddresses)
            {
                regions.AddCellRangeAddress(new CellRangeAddress(FirstDataRow, lastRow, r.FirstColumn, r.LastColumn));
            }

            var copy = new HSSFDataValidation(regions, constraint)
            {
                EmptyCellAllowed = dv.EmptyCellAllowed,
                SuppressDropDownArrow = dv.SuppressDropDownArrow,
                ShowErrorBox = dv.ShowErrorBox,
                ShowPromptBox = dv.ShowPromptBox,
                ErrorStyle = dv.ErrorStyle,
            };
            if (!string.IsNullOrEmpty(dv.ErrorBoxTitle) || !string.IsNullOrEmpty(dv.ErrorBoxText))
            {
                copy.CreateErrorBox(dv.ErrorBoxTitle, dv.ErrorBoxText);
            }

            if (!string.IsNullOrEmpty(dv.PromptBoxTitle) || !string.IsNullOrEmpty(dv.PromptBoxText))
            {
                copy.CreatePromptBox(dv.PromptBoxTitle, dv.PromptBoxText);
            }

            dst.AddValidationData(copy);
        }
    }

    // NPOI membaca ungkapan tanggal tanpa awalan '=' sebagai teks tanggal (YYYY/MM/DD); rumus seperti NOW() atau serial 1 harus diberi '='.
    private static string? AsFormula(string? expression)
        => string.IsNullOrEmpty(expression) || expression.StartsWith('=') ? expression : "=" + expression;

    private static void CopyRow(IRow? from, IRow to)
    {
        if (from is null)
        {
            return;
        }

        to.HeightInPoints = from.HeightInPoints;
        foreach (var c in from.Cells)
        {
            var cell = to.CreateCell(c.ColumnIndex);
            cell.CellStyle = c.CellStyle;
            if (c.CellType == CellType.String)
            {
                cell.SetCellValue(c.StringCellValue);
            }
            else if (c.CellType == CellType.Numeric)
            {
                cell.SetCellValue(c.NumericCellValue);
            }
        }
    }

    private static void SetValue(ICell cell, object? value)
    {
        switch (value)
        {
            case null or DBNull:
                cell.SetCellType(CellType.Blank);
                break;
            case DateTime d:
                cell.SetCellValue(d);
                break;
            case double or float or decimal or int or long or short:
                cell.SetCellValue(Convert.ToDouble(value, CultureInfo.InvariantCulture));
                break;
            default:
                cell.SetCellValue(Convert.ToString(value, CultureInfo.InvariantCulture)?.TrimEnd());
                break;
        }
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows(DataTable table)
        => [.. table.Rows.Cast<DataRow>().Select(r => (IReadOnlyDictionary<string, object?>)ToDictionary(r))];

    private static Dictionary<string, object?> ToDictionary(DataRow row)
    {
        var d = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (DataColumn c in row.Table.Columns)
        {
            d[c.ColumnName] = row[c] is DBNull ? null : row[c];
        }

        return d;
    }

    // Halaman "Surat Visit": satu salinan templat per rekening; baris area rincian (templat baris 16) digandakan sesuai jumlah rincian rekening itu,
    // baris sesudahnya bergeser, dan setiap halaman diakhiri pemisah halaman.
    private static void FillLetterPages(ISheet sheet, VisitWorkbookData data)
    {
        var (snapshot, height, merged) = LetterWorkbookBuilder.Snapshot(sheet);
        LetterWorkbookBuilder.Clear(sheet);

        var details = data.LetterDetail.Rows.Cast<DataRow>()
            .GroupBy(r => Convert.ToString(r[data.DetailKey], CultureInfo.InvariantCulture)?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(ToDictionary).ToList(), StringComparer.OrdinalIgnoreCase);

        var area = data.DetailRow;
        var offset = 0;
        foreach (DataRow letterRow in data.Letter.Rows)
        {
            var main = ToDictionary(letterRow);
            var key = Convert.ToString(letterRow[data.DetailKey], CultureInfo.InvariantCulture)?.Trim() ?? string.Empty;
            var rows = details.TryGetValue(key, out var found) && found.Count > 0 ? found : [[]];
            var extra = rows.Count - 1;

            foreach (var r in snapshot)
            {
                if (r.Index == area)
                {
                    for (var k = 0; k < rows.Count; k++)
                    {
                        WriteRow(sheet, offset + area + k, r, rows[k]);
                    }
                }
                else
                {
                    WriteRow(sheet, offset + (r.Index > area ? r.Index + extra : r.Index), r, main);
                }
            }

            foreach (var m in merged)
            {
                if (m.FirstRow == area && m.LastRow == area)
                {
                    for (var k = 0; k < rows.Count; k++)
                    {
                        sheet.AddMergedRegion(new CellRangeAddress(offset + area + k, offset + area + k, m.FirstColumn, m.LastColumn));
                    }
                }
                else if (m.FirstRow > area)
                {
                    sheet.AddMergedRegion(new CellRangeAddress(m.FirstRow + extra + offset, m.LastRow + extra + offset, m.FirstColumn, m.LastColumn));
                }
                else
                {
                    sheet.AddMergedRegion(new CellRangeAddress(m.FirstRow + offset, m.LastRow + (m.LastRow >= area ? extra : 0) + offset, m.FirstColumn, m.LastColumn));
                }
            }

            var page = height + extra;
            sheet.SetRowBreak(offset + page - 1);
            offset += page;
        }

        sheet.SetColumnBreak(8);
    }

    private static void WriteRow(ISheet sheet, int index, LetterWorkbookBuilder.RowSnap snap, IReadOnlyDictionary<string, object?> values)
    {
        var row = sheet.CreateRow(index);
        row.Height = (short)(snap.Height * 20);
        foreach (var c in snap.Cells)
        {
            var cell = row.CreateCell(c.Column);
            cell.CellStyle = c.Style;
            LetterWorkbookBuilder.WriteCell(cell, c, values);
        }
    }
}
