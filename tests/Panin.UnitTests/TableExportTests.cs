using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Panin.Application.Export;

namespace Panin.UnitTests;

public class TableExportTests
{
    private static ExportTable Sample(int rows = 3) => new(
        "Monitoring (Debitur)",
        [new ExportColumn("Nama"), new ExportColumn("Outstanding", ExportFormat.Amount), new ExportColumn("DPD", ExportFormat.Count), new ExportColumn("Tgl", ExportFormat.Date)],
        [.. Enumerable.Range(0, rows).Select(i => (IReadOnlyList<object?>)[$"Nasabah {i} (é) \\ {{x}}", 1234567.5m + i, 30 + i, new DateTime(2026, 10, 23)])]);

    private sealed class NoExcel : IExcelExporter
    {
        public byte[] Export(ExportTable table) => [1, 2, 3];
    }

    [Theory]
    [InlineData(ExportKind.Xlsx, "xlsx")]
    [InlineData(ExportKind.Csv, "csv")]
    [InlineData(ExportKind.Pdf, "pdf")]
    [InlineData(ExportKind.Rtf, "rtf")]
    [InlineData(ExportKind.Txt, "txt")]
    public void CreateDispatchesByKind(ExportKind kind, string extension)
    {
        var file = TableExport.Create(Sample(), kind, new NoExcel());
        Assert.Equal(extension, file.Extension);
        Assert.NotEmpty(file.Content);
        Assert.False(string.IsNullOrEmpty(file.ContentType));
    }

    [Fact]
    public void TextIsTabSeparatedWithIndonesianNumbers()
    {
        var text = Encoding.UTF8.GetString(TextTableWriter.Write(Sample(1)), 3, TextTableWriter.Write(Sample(1)).Length - 3);
        var lines = text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("Nama\tOutstanding\tDPD\tTgl", lines[0]);
        Assert.Equal("Nasabah 0 (é) \\ {x}\t1.234.567,50\t30\t23-Okt-2026", lines[1].Replace("Okt", "Okt"));
    }

    [Fact]
    public void RtfIsBalancedEscapedAndRepeatsHeader()
    {
        var rtf = Encoding.ASCII.GetString(RtfTableWriter.Write(Sample(4)));
        Assert.StartsWith(@"{\rtf1", rtf, StringComparison.Ordinal);
        Assert.EndsWith("}", rtf, StringComparison.Ordinal);
        Assert.Contains(@"\trhdr", rtf, StringComparison.Ordinal);
        Assert.Equal(5, Regex.Matches(rtf, Regex.Escape(@"\row")).Count); // judul + 4 baris
        Assert.Contains(@"\u233?", rtf, StringComparison.Ordinal);          // é
        Assert.Contains(@"\\", rtf, StringComparison.Ordinal);
        Assert.Contains(@"\{x\}", rtf, StringComparison.Ordinal);
        // kurung kurawal tanpa escape harus seimbang
        var stripped = rtf.Replace(@"\\", string.Empty, StringComparison.Ordinal).Replace(@"\{", string.Empty, StringComparison.Ordinal).Replace(@"\}", string.Empty, StringComparison.Ordinal);
        Assert.Equal(stripped.Count(c => c == '{'), stripped.Count(c => c == '}'));
    }

    [Fact]
    public void PdfHasValidStructureAndText()
    {
        var pdf = PdfTableWriter.Write(Sample(5));
        var raw = Encoding.Latin1.GetString(pdf);
        Assert.StartsWith("%PDF-1.4", raw, StringComparison.Ordinal);
        Assert.EndsWith("%%EOF\n", raw, StringComparison.Ordinal);

        // setiap offset xref menunjuk tepat ke "N 0 obj"
        var xrefAt = int.Parse(Regex.Match(raw, @"startxref\n(\d+)").Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        Assert.StartsWith("xref", raw[xrefAt..], StringComparison.Ordinal);
        var entries = Regex.Matches(raw[xrefAt..], @"(\d{10}) 00000 n ");
        Assert.Equal(6, entries.Count); // katalog, halaman-halaman, 2 font, 1 halaman + isinya
        for (var i = 0; i < entries.Count; i++)
        {
            var offset = int.Parse(entries[i].Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            Assert.StartsWith($"{i + 1} 0 obj", raw[offset..], StringComparison.Ordinal);
        }

        Assert.Single(Regex.Matches(raw, @"/Type /Page "));
        var content = Inflate(pdf, raw);
        Assert.Contains("(Monitoring \\(Debitur\\)) Tj", content, StringComparison.Ordinal);
        Assert.Contains("1.234.567,50", content, StringComparison.Ordinal);
        Assert.Contains("(Halaman 1 dari 1) Tj", content, StringComparison.Ordinal);
        Assert.Contains("\\\\", content, StringComparison.Ordinal); // backslash di-escape
    }

    [Fact]
    public void PdfPaginatesAndRepeatsHeaderOnEveryPage()
    {
        var pdf = PdfTableWriter.Write(Sample(120));
        var raw = Encoding.Latin1.GetString(pdf);
        var pages = Regex.Matches(raw, @"/Type /Page ").Count;
        Assert.InRange(pages, 3, 6);
        var headerCount = Regex.Matches(Inflate(pdf, raw), @"\(Outstanding\) Tj").Count;
        Assert.Equal(pages, headerCount);
    }

    [Fact]
    public void PdfTruncatesVeryLargeTables()
    {
        var table = new ExportTable("T", [new ExportColumn("A")], [.. Enumerable.Range(0, PdfTableWriter.MaxRows + 5).Select(i => (IReadOnlyList<object?>)[i.ToString(System.Globalization.CultureInfo.InvariantCulture)])]);
        var pdf = PdfTableWriter.Write(table);
        Assert.Contains("dipotong pada", Inflate(pdf, Encoding.Latin1.GetString(pdf)), StringComparison.Ordinal);
    }

    [Fact]
    public void PdfHandlesEmptyTableAndLongText()
    {
        var empty = PdfTableWriter.Write(new ExportTable("Kosong", [new ExportColumn("A")], []));
        Assert.Contains("Tidak ada data", Inflate(empty, Encoding.Latin1.GetString(empty)), StringComparison.Ordinal);

        var longText = new string('W', 500);
        var wide = PdfTableWriter.Write(new ExportTable("T", [new ExportColumn("A"), new ExportColumn("B")], [[longText, "x"]]));
        var content = Inflate(wide, Encoding.Latin1.GetString(wide));
        Assert.Contains("...", content, StringComparison.Ordinal);
        Assert.DoesNotContain(longText, content, StringComparison.Ordinal);
    }

    /// <summary>Gabungkan isi semua aliran terkompresi (ZLib) menjadi satu teks.</summary>
    private static string Inflate(byte[] pdf, string raw)
    {
        var sb = new StringBuilder();
        foreach (Match m in Regex.Matches(raw, @"/Length (\d+) /Filter /FlateDecode >>\nstream\n"))
        {
            var start = m.Index + m.Length;
            var length = int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            using var input = new MemoryStream(pdf, start, length);
            using var z = new ZLibStream(input, CompressionMode.Decompress);
            using var reader = new StreamReader(z, Encoding.Latin1);
            sb.Append(reader.ReadToEnd());
        }

        return sb.ToString();
    }
}
