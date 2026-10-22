using System.Data;
using NPOI.HSSF.UserModel;
using NPOI.SS.UserModel;
using NPOI.SS.Util;
using Panin.Infrastructure.Export;

namespace Panin.UnitTests;

public class VisitWorkbookTests
{
    private static string TemplatePath
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "Panin.Web", "Templates", "Visit.xls")))
            {
                dir = dir.Parent;
            }

            return Path.Combine(dir!.FullName, "src", "Panin.Web", "Templates", "Visit.xls");
        }
    }

    private static VisitWorkbookData Data(int accounts, int details = 12)
    {
        var visit = new DataTable();
        for (var c = 0; c < 33; c++)
        {
            visit.Columns.Add("c" + c, typeof(object));
        }

        var letter = new DataTable();
        foreach (var c in new[] { "CIF NO.", "ACC_NO", "CUSTOMER NAME", "TOTAL_TUNGGAKAN", "BRANCH_NAME", "CU_ADDR_CORRESP", "OUTSTANDING" })
        {
            letter.Columns.Add(c, typeof(object));
        }

        var detail = new DataTable();
        foreach (var c in new[] { "ACC_NO", "MONTH_DUE_2", "PRINCIPAL_DUE", "CHARGES_DUE", "TOT_TUNGGAKAN" })
        {
            detail.Columns.Add(c, typeof(object));
        }

        var slip = new DataTable();
        foreach (var c in new[] { "SLIP_DATE", "SLIP_NUMBER", "COLLECTOR_NAME", "ACC_NO", "CUSTOMER NAME" })
        {
            slip.Columns.Add(c, typeof(object));
        }

        for (var i = 1; i <= accounts; i++)
        {
            var acc = "ACC" + i.ToString("D4", System.Globalization.CultureInfo.InvariantCulture);
            var row = new object?[33];
            row[0] = acc;
            row[1] = "Nama " + i;
            row[2] = "U1 - Petugas";
            row[18] = "Jl. Koresp " + i;
            row[32] = "SLIP" + i;
            visit.Rows.Add(row);
            letter.Rows.Add("CIF" + i, acc, "Nama " + i, 1000d * i, "KCU X", "Jl. Koresp " + i, 5_000_000d);
            for (var m = 1; m <= details; m++)
            {
                detail.Rows.Add(acc, m + " (Jan)", 100d * m, 10d * m, 110d * m);
            }

            slip.Rows.Add("22 Oktober 2026", "SLIP" + i, "U1 - Petugas", acc, "Nama " + i);
        }

        return new VisitWorkbookData(
            visit,
            [["01 - Mengingatkan", "02 - Penagihan"], ["00000 - Yang Bersangkutan", "02 - Suami", "03 - Istri"], ["R0012 - Akan Setor Tgl"], ["00005 - Surat"]],
            letter,
            detail,
            slip);
    }

    private static HSSFWorkbook Build(VisitWorkbookData data, string? password = "PASS")
    {
        using var template = File.OpenRead(TemplatePath);
        var bytes = VisitWorkbookBuilder.Build(template, data, password);
        return new HSSFWorkbook(new MemoryStream(bytes));
    }

    private static string? Text(ISheet sheet, string address)
    {
        var r = new CellReference(address);
        return sheet.GetRow(r.Row)?.GetCell(r.Col)?.ToString();
    }

    private static string RegionsText(IDataValidation dv) => string.Join(",", dv.Regions.CellRangeAddresses.Select(a => a.FormatAsString()));

    [Fact]
    public void Workbook_has_the_three_sheets_in_order_and_all_are_protected()
    {
        var wb = Build(Data(2));

        Assert.Equal(["Visit", "Surat Visit", "Slip Bukti Setoran"], Enumerable.Range(0, wb.NumberOfSheets).Select(wb.GetSheetName));
        Assert.All(Enumerable.Range(0, wb.NumberOfSheets), i => Assert.True(wb.GetSheetAt(i).Protect));
    }

    [Fact]
    public void Without_password_the_sheets_stay_unprotected()
        => Assert.All(Enumerable.Range(0, 3), i => Assert.False(Build(Data(1), password: null).GetSheetAt(i).Protect));

    [Fact]
    public void Visit_sheet_has_one_row_per_account_with_positional_values_and_template_locking()
    {
        var sheet = Build(Data(3)).GetSheet("Visit");

        Assert.Equal("No. Account", Text(sheet, "A1"));
        Assert.Equal(["ACC0001", "ACC0002", "ACC0003"], new[] { "A2", "A3", "A4" }.Select(a => Text(sheet, a)));
        Assert.Equal("Nama 2", Text(sheet, "B3"));
        Assert.Equal("Jl. Koresp 3", Text(sheet, "S4"));
        Assert.Equal("SLIP2", Text(sheet, "AG3"));
        Assert.True(sheet.GetRow(2).GetCell(0).CellStyle.IsLocked);   // kolom A–E diisi sistem
        Assert.False(sheet.GetRow(2).GetCell(5).CellStyle.IsLocked);  // F diisi agen
        Assert.True(sheet.IsColumnHidden(12) && sheet.IsColumnHidden(13) && sheet.IsColumnHidden(32)); // M, N, AG
    }

    [Fact]
    public void Dropdown_lists_are_written_in_AX_to_BA_and_attached_to_every_data_row()
    {
        var sheet = (HSSFSheet)Build(Data(3)).GetSheet("Visit");

        Assert.Equal("01 - Mengingatkan", Text(sheet, "AX2"));
        Assert.Equal("03 - Istri", Text(sheet, "AY4"));
        Assert.Equal("00005 - Surat", Text(sheet, "BA2"));

        var dvs = sheet.GetDataValidations();
        var purpose = Assert.Single(dvs, d => d.ValidationConstraint.Formula1 == "$AX$2:$AX$3");
        Assert.Equal("G2:G4", RegionsText(purpose));
        Assert.Contains(dvs, d => d.ValidationConstraint.Formula1 == "$AY$2:$AY$4" && RegionsText(d) == "J2:J4");
        Assert.Contains(dvs, d => d.ValidationConstraint.Formula1 == "$AZ$2:$AZ$2" && RegionsText(d) == "K2:K4");
        Assert.Contains(dvs, d => d.ValidationConstraint.Formula1 == "$BA$2:$BA$2" && RegionsText(d) == "M2:M4");
    }

    [Fact]
    public void Template_validations_are_extended_to_all_rows_with_their_messages()
    {
        var sheet = (HSSFSheet)Build(Data(3)).GetSheet("Visit");
        var dvs = sheet.GetDataValidations();

        var notes = Assert.Single(dvs, d => RegionsText(d) == "O2:O4");
        Assert.Equal("Field 100  Karakter", notes.ErrorBoxText);
        Assert.Contains(dvs, d => RegionsText(d).Contains("P2:R4", StringComparison.Ordinal));
        Assert.Contains(dvs, d => d.ValidationConstraint.GetValidationType() == ValidationType.DATE && RegionsText(d) == "F2:F4");
    }

    [Fact]
    public void Helper_lists_of_the_template_move_out_of_the_data_area_so_many_accounts_cannot_overwrite_them()
    {
        var sheet = (HSSFSheet)Build(Data(40)).GetSheet("Visit");

        // Baris 26–29 kolom B–E kini data rekening, bukan daftar bantu.
        Assert.Equal("ACC0025", Text(sheet, "A26"));
        Assert.Equal("Nama 25", Text(sheet, "B26"));
        Assert.Equal("Ditempati", Text(sheet, "BD26"));
        Assert.Equal("Sudah Diterima", Text(sheet, "BF26"));
        Assert.Equal("0101 - KAB. TANGERANG", Text(sheet, "BH50")?.Trim());

        var dvs = sheet.GetDataValidations();
        Assert.Contains(dvs, d => d.ValidationConstraint.Formula1 == "$BD$26:$BD$29" && RegionsText(d) == "AB2:AB41");
        Assert.Contains(dvs, d => d.ValidationConstraint.Formula1 == "$BH$50:$BH$377" && RegionsText(d).Contains("U2:U41", StringComparison.Ordinal));
        Assert.DoesNotContain(dvs, d => d.ValidationConstraint.Formula1 is "$C$26:$C$29" or "$U$50:$U$377");
    }

    [Fact]
    public void Letter_pages_stack_with_the_detail_area_opened_to_the_number_of_detail_rows()
    {
        var wb = Build(Data(2));
        var sheet = wb.GetSheet("Surat Visit");

        // Halaman = 66 baris templat + 11 baris tambahan (12 rincian menggantikan 1 baris).
        Assert.Equal("ACC0001", Text(sheet, "D3"));
        Assert.Equal("ACC0002", Text(sheet, "D" + (3 + 77)));
        Assert.Equal(100d, sheet.GetRow(15).GetCell(2).NumericCellValue);
        Assert.Equal(1200d, sheet.GetRow(26).GetCell(2).NumericCellValue);
        Assert.Equal("TOTAL", Text(sheet, "E28"));
        Assert.Equal(1000d, sheet.GetRow(27).GetCell(6).NumericCellValue);
        Assert.Equal(2000d, sheet.GetRow(27 + 77).GetCell(6).NumericCellValue);
        Assert.Equal([76, 153], ((HSSFSheet)sheet).RowBreaks.Order());
        Assert.Equal([8], ((HSSFSheet)sheet).ColumnBreaks);
    }

    [Fact]
    public void Letter_page_without_details_keeps_the_template_height()
    {
        var sheet = Build(Data(1, details: 0)).GetSheet("Surat Visit");

        Assert.Equal("TOTAL", Text(sheet, "E17"));
        Assert.Equal([65], ((HSSFSheet)sheet).RowBreaks);
    }

    [Fact]
    public void Slip_pages_repeat_per_account()
    {
        var sheet = Build(Data(2)).GetSheet("Slip Bukti Setoran");

        Assert.Equal("22 Oktober 2026", Text(sheet, "R2"));
        Assert.Equal("SLIP1", Text(sheet, "R3"));
        Assert.Equal("Nama 1", Text(sheet, "F12"));
        Assert.Equal("ACC0001", Text(sheet, "R12"));
        Assert.Equal("SLIP2", Text(sheet, "R" + (3 + 50)));
        Assert.Equal([23], ((HSSFSheet)sheet).ColumnBreaks);
    }

    [Fact]
    public void Template_comments_do_not_leak_into_the_output()
    {
        var wb = Build(Data(2));
        foreach (var name in new[] { "Surat Visit", "Slip Bukti Setoran" })
        {
            var sheet = wb.GetSheet(name);
            var comments = Enumerable.Range(0, sheet.LastRowNum + 1).SelectMany(r => sheet.GetRow(r)?.Cells ?? []).Count(c => c.CellComment is not null);
            Assert.Equal(0, comments);
        }
    }

    [Fact]
    public void Maximum_supported_batch_fits_in_one_xls_sheet()
    {
        var wb = Build(Data(800));

        Assert.Equal(800 * 77, wb.GetSheet("Surat Visit").LastRowNum + 1);
        Assert.True(wb.GetSheet("Surat Visit").LastRowNum < 65535);
    }
}
