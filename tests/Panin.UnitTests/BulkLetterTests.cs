using NPOI.HSSF.UserModel;
using Panin.Application.Letters;
using Panin.Infrastructure.Export;
using Panin.Infrastructure.Letters;

namespace Panin.UnitTests;

public class BulkLetterTests
{
    private static string TemplatePath()
    {
        // Template dari aplikasi lama disalin ke output Panin.Web; cari juga langsung di sumber agar tes tidak bergantung urutan build.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        foreach (var candidate in new[] { Path.Combine(AppContext.BaseDirectory, "Templates", "surat.xls") })
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        while (dir is not null)
        {
            var p = Path.Combine(dir.FullName, "src", "Panin.Web", "Templates", "surat.xls");
            if (File.Exists(p))
            {
                return p;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException("surat.xls tidak ditemukan");
    }

    private static IReadOnlyDictionary<string, object?> Letter(string no, string name, double tunggakan) => new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
    {
        ["SU_NO"] = no, ["SU_TYPE"] = "PA-PER-01", ["SU_CITY"] = "Jakarta", ["SU_DATE"] = "31 Oktober 2023", ["CUST_NAME"] = name,
        ["CU_ADDR_CORRESP1"] = "JL. UJI 1", ["ACC_NO"] = "123456", ["PT_DESC"] = "KPR", ["POS_KWJB_PER"] = "Oktober 2023",
        ["BULAN_TUNGGAKAN_1"] = "Maret 2025", ["TUNGGAKAN_1"] = tunggakan, ["DENDA_1"] = 112584.71, ["AO_HP"] = "0812", ["AO_EXT"] = "123",
        ["HUTANG_POKOK_2"] = "27.732.840,64", ["DAY_OF_MATURITY_DATE"] = 31, ["MATURITY_DUE_DATE"] = "29 November 2023",
    };

    private static readonly LetterSheet[] Sheets =
    [
        new("PA-PER-01", "Surat Peringatan I - PA"),
        new("PRK-PEM-01", "Surat Pemberitahuan I - PRK"),
    ];

    [Fact]
    public void Each_letter_fills_its_own_block_with_placeholders_numbers_and_page_breaks()
    {
        using var template = File.OpenRead(TemplatePath());
        var rows = new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>>
        {
            ["PA-PER-01"] = [Letter("0001/MPE/EXT/25", "BUDI SANTOSO", 2226778.43), Letter("0002/MPE/EXT/25", "SITI AMINAH", 100.5)],
        };

        var bytes = LetterWorkbookBuilder.Build(template, Sheets, rows, password: null);

        using var wb = new HSSFWorkbook(new MemoryStream(bytes));
        Assert.Equal(1, wb.NumberOfSheets); // sheet yang tidak dipakai dibuang
        var sheet = wb.GetSheet("Surat Peringatan I - PA");
        Assert.NotNull(sheet);

        // tinggi satu blok = jumlah baris template; blok ke-2 dimulai di bawahnya
        var height = 48; // template "Surat Peringatan I - PA": baris 0..47
        Assert.Equal("No : 0001/MPE/EXT/25", sheet.GetRow(2).GetCell(0).StringCellValue);
        Assert.Equal("BUDI SANTOSO", sheet.GetRow(5).GetCell(0).StringCellValue);
        Assert.Equal("  Jakarta, 31 Oktober 2023", sheet.GetRow(0).GetCell(0).StringCellValue);
        Assert.Equal(2226778.43, sheet.GetRow(20).GetCell(6).NumericCellValue, 2);
        Assert.Equal("No : 0002/MPE/EXT/25", sheet.GetRow(height + 2).GetCell(0).StringCellValue);
        Assert.Equal("SITI AMINAH", sheet.GetRow(height + 5).GetCell(0).StringCellValue);
        Assert.Equal(100.5, sheet.GetRow(height + 20).GetCell(6).NumericCellValue, 2);
        Assert.Contains("KPR", sheet.GetRow(19).GetCell(0).StringCellValue, StringComparison.Ordinal);
        Assert.Equal(2, sheet.RowBreaks.Length);
        Assert.Equal(height - 1, sheet.RowBreaks[0]);
    }

    [Fact]
    public void Template_comments_do_not_leak_into_the_output()
    {
        using var template = File.OpenRead(TemplatePath());
        var rows = new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>> { ["PA-PER-01"] = [Letter("1", "A", 1)] };
        var bytes = LetterWorkbookBuilder.Build(template, Sheets, rows, null);

        using var wb = new HSSFWorkbook(new MemoryStream(bytes));
        var sheet = wb.GetSheetAt(0);
        var comments = 0;
        for (var r = sheet.FirstRowNum; r <= sheet.LastRowNum; r++)
        {
            comments += sheet.GetRow(r)?.Cells.Count(c => c.CellComment is not null) ?? 0;
        }

        Assert.Equal(0, comments);
    }

    [Fact]
    public void Sheets_are_protected_when_a_password_is_given()
    {
        using var template = File.OpenRead(TemplatePath());
        var rows = new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>> { ["PA-PER-01"] = [Letter("1", "A", 1)] };
        var bytes = LetterWorkbookBuilder.Build(template, Sheets, rows, "rahasia");

        using var wb = new HSSFWorkbook(new MemoryStream(bytes));
        Assert.True(wb.GetSheetAt(0).Protect);
    }

    [Fact]
    public void Missing_sheet_or_no_data_is_an_error()
    {
        using var template = File.OpenRead(TemplatePath());
        var rows = new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>> { ["XX-01"] = [Letter("1", "A", 1)] };
        Assert.Throws<InvalidOperationException>(() => LetterWorkbookBuilder.Build(template, [new LetterSheet("XX-01", "Tidak Ada")], rows, null));

        using var template2 = File.OpenRead(TemplatePath());
        Assert.Throws<InvalidOperationException>(() => LetterWorkbookBuilder.Build(template2, Sheets, new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>>(), null));
    }

    [Theory]
    [InlineData(@"..\Upload\Surat\BULKJURALDO20261021154502.xls", "BULKJURALDO20261021154502.xls")]
    [InlineData("surat.xls", "surat.xls")]
    [InlineData(@"..\Upload\Template\surat.xls", "surat.xls")]
    public void Stored_paths_reduce_to_a_plain_file_name(string stored, string expected)
        => Assert.Equal(expected, BulkLetterService.SafeFileName(stored));

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("..\\..\\windows\\system.ini%00.xls")]
    [InlineData("a b.xls")]
    [InlineData("..")]
    [InlineData(".hidden")]
    public void Unsafe_file_names_are_rejected(string? stored)
        => Assert.Null(BulkLetterService.SafeFileName(stored));

    [Fact]
    public void Letter_result_requires_a_result_and_respects_column_limits()
    {
        Assert.Empty(LetterResultRules.Validate(new LetterResultEntry { LetterNo = "1", ResultCode = "R0024" }));
        var errors = LetterResultRules.Validate(new LetterResultEntry { ReceivedBy = new string('x', 21), ReceivedAt = DateTime.Today.AddDays(1) });
        Assert.Equal(4, errors.Count); // nomor, hasil, Receive By >20, tanggal masa depan
    }
}

public class ExceptionLetterSqlTests
{
    [Fact]
    public void Updating_an_existing_exception_touches_only_that_account()
    {
        // SP_ADD_LETTER_EXCEPTION lama menjalankan UPDATE tanpa WHERE (menimpa pelaku/waktu semua baris).
        var update = Panin.Infrastructure.Letters.ExceptionLetterService.AddSql;
        Assert.Contains("UPDATE EXCEPTION_LETTER SET ACT_BY = @2, ACT_DATETIME = GETDATE() WHERE ACC_NO = @1", update, StringComparison.Ordinal);
        Assert.Contains("BEGIN TRAN", update, StringComparison.Ordinal);
    }
}
