using NPOI.XSSF.UserModel;
using Panin.Application.Collection;
using Panin.Application.Export;
using Panin.Infrastructure.Export;

namespace Panin.UnitTests;

public class DebiturSearchRulesTests
{
    [Fact]
    public void No_criteria_is_rejected()
        => Assert.NotNull(DebiturSearchRules.Validate(new DebiturSearchCriteria(null, "  ", string.Empty)));

    [Theory]
    [InlineData("A")]
    [InlineData("AB")]
    public void Short_name_alone_is_rejected(string name)
        => Assert.NotNull(DebiturSearchRules.Validate(new DebiturSearchCriteria(null, null, name)));

    [Fact]
    public void Three_letter_name_is_accepted()
        => Assert.Null(DebiturSearchRules.Validate(new DebiturSearchCriteria(null, null, "BUD")));

    [Fact]
    public void Short_name_is_fine_together_with_account_number()
        => Assert.Null(DebiturSearchRules.Validate(new DebiturSearchCriteria("1001", null, "A")));

    [Fact]
    public void Criteria_are_trimmed_and_blank_becomes_null()
    {
        var c = new DebiturSearchCriteria(" 1001 ", "", "  ").Normalized();
        Assert.Equal("1001", c.AccountNo);
        Assert.Null(c.CustomerRef);
        Assert.Null(c.Name);
    }

    [Theory]
    [InlineData("023", true)]
    [InlineData("024", true)]
    [InlineData("010", false)]
    [InlineData(null, false)]
    public void Recovery_accounts_are_status_023_and_024(string? status, bool expected)
        => Assert.Equal(expected, new DebiturRow { AccountStatus = status }.IsRecovery);
}

public class NpoiExcelExporterTests
{
    [Fact]
    public void Writes_headers_values_and_formats()
    {
        var table = new ExportTable(
            "Debitur: [test]/1",
            [new ExportColumn("Nama"), new ExportColumn("Outstanding", ExportFormat.Count), new ExportColumn("Tanggal", ExportFormat.Date)],
            [
                ["Budi", 1234567.0, new DateTime(2025, 5, 7)],
                ["Siti", null, null],
            ]);

        var bytes = new NpoiExcelExporter().Export(table);

        using var wb = new XSSFWorkbook(new MemoryStream(bytes));
        var sheet = wb.GetSheetAt(0);
        Assert.Equal("Debitur test1", sheet.SheetName);
        Assert.Equal("Nama", sheet.GetRow(0).GetCell(0).StringCellValue);
        Assert.Equal("Budi", sheet.GetRow(1).GetCell(0).StringCellValue);
        Assert.Equal(1234567.0, sheet.GetRow(1).GetCell(1).NumericCellValue);
        Assert.Equal(new DateTime(2025, 5, 7), sheet.GetRow(1).GetCell(2).DateCellValue);
        Assert.Equal("Siti", sheet.GetRow(2).GetCell(0).StringCellValue);
        Assert.Equal(NPOI.SS.UserModel.CellType.Blank, sheet.GetRow(2).GetCell(1).CellType);
    }

    [Fact]
    public void Long_sheet_names_are_cut_to_excel_limit()
    {
        var bytes = new NpoiExcelExporter().Export(new ExportTable(new string('x', 80), [new ExportColumn("A")], []));
        using var wb = new XSSFWorkbook(new MemoryStream(bytes));
        Assert.Equal(31, wb.GetSheetAt(0).SheetName.Length);
    }
}
