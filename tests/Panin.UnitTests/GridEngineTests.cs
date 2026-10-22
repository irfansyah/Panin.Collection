using System.Text;
using Panin.Application.Export;
using Panin.Web.Components.Common;

namespace Panin.UnitTests;

public class GridEngineTests
{
    [Theory]
    [InlineData("100", ">50", true)]
    [InlineData("100", ">100", false)]
    [InlineData("100", ">=100", true)]
    [InlineData("100", "<=99", false)]
    [InlineData("100", "<>100", false)]
    [InlineData("100", "=100", true)]
    [InlineData("100", "10", true)] // bukan operator: "mengandung"
    [InlineData("100", "", true)]
    public void NumericFilter(string text, string filter, bool expected)
    {
        var value = decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(expected, GridEngine.MatchesColumnFilter(text, value, numeric: true, filter));
    }

    [Fact]
    public void NumericFilterWithNullValueNeverMatchesOperator() =>
        Assert.False(GridEngine.MatchesColumnFilter(string.Empty, null, numeric: true, ">0"));

    [Fact]
    public void NumericFilterAcceptsIndonesianDecimal() =>
        Assert.True(GridEngine.MatchesColumnFilter("1,5", 1.5m, numeric: true, ">=1,5"));

    [Theory]
    [InlineData("Jakarta Selatan", "selatan", true)]
    [InlineData("Jakarta Selatan", "=jakarta", false)]
    [InlineData("Jakarta", "=jakarta", true)]
    [InlineData("Jakarta", "bandung", false)]
    public void TextFilter(string text, string filter, bool expected) =>
        Assert.Equal(expected, GridEngine.MatchesColumnFilter(text, text, numeric: false, filter));

    [Fact]
    public void TextColumnTreatsOperatorLiterally() =>
        Assert.True(GridEngine.MatchesColumnFilter("a>b", "a>b", numeric: false, ">b"));

    [Fact]
    public void Summaries()
    {
        object?[] values = [10m, 20, null, "30", "x"];
        Assert.Equal(60m, GridEngine.Summarize(GridSummary.Sum, values));
        Assert.Equal(20m, GridEngine.Summarize(GridSummary.Average, values));
        Assert.Equal(10m, GridEngine.Summarize(GridSummary.Min, values));
        Assert.Equal(30m, GridEngine.Summarize(GridSummary.Max, values));
        Assert.Equal(4m, GridEngine.Summarize(GridSummary.Count, values));
        Assert.Null(GridEngine.Summarize(GridSummary.Sum, new object?[] { null, "x" }));
        Assert.Null(GridEngine.Summarize(GridSummary.None, values));
    }

    [Fact]
    public void FormatSummaryUsesIndonesianGrouping()
    {
        Assert.Equal("1.234.567", GridEngine.FormatSummary(1234567m, integer: true));
        Assert.Equal("1.234,50", GridEngine.FormatSummary(1234.5m, integer: false));
        Assert.Equal(string.Empty, GridEngine.FormatSummary(null, integer: false));
    }

    private sealed record Row(string Region, string Product, int Amount);

    [Fact]
    public void FlattenBuildsNestedGroups()
    {
        Row[] rows = [new("A", "x", 1), new("A", "y", 2), new("A", "x", 3), new("B", "x", 4)];
        var sorted = rows.OrderBy(r => r.Region).ThenBy(r => r.Product).ToList();
        (string, Func<Row, string>)[] groups = [("Wilayah", r => r.Region), ("Produk", r => r.Product)];

        var flat = GridEngine.Flatten(sorted, groups, new HashSet<string>());

        // A, x(2), y(1), B, x(1): 2 judul tingkat 1 + 3 judul tingkat 2 + 4 baris data
        Assert.Equal(9, flat.Count);
        Assert.Equal(2, flat.Count(r => r is { IsGroup: true, Level: 0 }));
        Assert.Equal(3, flat.Count(r => r is { IsGroup: true, Level: 1 }));
        Assert.Equal(4, flat.Count(r => !r.IsGroup));
        var a = flat[0];
        Assert.Equal("Wilayah", a.GroupCaption);
        Assert.Equal("A", a.GroupValue);
        Assert.Equal(3, a.GroupItems!.Count);
    }

    [Fact]
    public void FlattenHidesCollapsedGroupContents()
    {
        Row[] rows = [new("A", "x", 1), new("A", "y", 2), new("B", "x", 4)];
        (string, Func<Row, string>)[] groups = [("Wilayah", r => r.Region)];

        var flat = GridEngine.Flatten(rows, groups, new HashSet<string> { "\u001fA" });

        Assert.Equal(["A", "B"], flat.Where(r => r.IsGroup).Select(r => r.GroupValue!).ToArray());
        Assert.True(flat[0].Collapsed);
        Assert.Equal(1, flat.Count(r => !r.IsGroup));
    }

    [Fact]
    public void FlattenWithoutGroupsReturnsDataRowsOnly()
    {
        Row[] rows = [new("A", "x", 1), new("B", "y", 2)];
        var flat = GridEngine.Flatten(rows, [], new HashSet<string>());
        Assert.All(flat, r => Assert.False(r.IsGroup));
        Assert.Equal(2, flat.Count);
    }
}

public class CsvExporterTests
{
    private static string Text(byte[] bytes) => Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);

    [Fact]
    public void WritesBomSemicolonsAndIndonesianDecimals()
    {
        var table = new ExportTable(
            "S",
            [new ExportColumn("Nama"), new ExportColumn("Nilai", ExportFormat.Amount), new ExportColumn("Tgl", ExportFormat.Date)],
            [[ "Budi", 1234.5m, new DateTime(2026, 10, 23) ]]);

        var bytes = CsvExporter.Export(table);

        Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);
        Assert.Equal("Nama;Nilai;Tgl\r\nBudi;1234,5;2026-10-23\r\n", Text(bytes));
    }

    [Fact]
    public void QuotesSpecialCharactersAndNeutralisesFormulas()
    {
        var table = new ExportTable(
            "S",
            [new ExportColumn("A"), new ExportColumn("B")],
            [[ "a;b", "say \"hi\"" ], [ "=1+1", "@x" ], [ null, "baris\nbaru" ]]);

        var lines = Text(CsvExporter.Export(table));

        Assert.Contains("\"a;b\";\"say \"\"hi\"\"\"\r\n", lines, StringComparison.Ordinal);
        Assert.Contains("'=1+1;'@x\r\n", lines, StringComparison.Ordinal);
        Assert.Contains(";\"baris\nbaru\"\r\n", lines, StringComparison.Ordinal);
    }
}
