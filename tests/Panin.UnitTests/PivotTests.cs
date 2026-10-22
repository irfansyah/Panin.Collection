using System.Data;
using Panin.Application.Reports;
using Panin.Application.Reports.Pivot;

namespace Panin.UnitTests;

public class PivotBuilderTests
{
    private static PivotPartial P(params double[] values)
    {
        var p = new PivotPartial();
        foreach (var v in values)
        {
            p.Add(v);
        }

        return p;
    }

    private static PivotRecord R(string region, string product, int month, params double[] values)
        => new([region, product], [month], [P(values)]);

    private static readonly PivotRecord[] Data =
    [
        R("A", "x", 1, 10),
        R("A", "x", 2, 20),
        R("A", "y", 1, 5),
        R("B", "x", 1, 7),
        R("B", "y", 2, 3, 9),
    ];

    private static PivotLayout Layout(PivotAggregate aggregate = PivotAggregate.Sum, bool rowTotals = true, bool grandMeasure = true) => new(
        [new PivotField("Region", "Region"), new PivotField("Product", "Produk")],
        [new PivotField("Month", "Bulan", PivotInterval.Month)],
        [new PivotMeasure("Amt", "Jumlah", aggregate, ShowGrandTotal: grandMeasure)],
        RowTotals: rowTotals);

    private static double? Cell(PivotResult r, string[] rowPath, PivotLineKind rowKind, string[] colPath, PivotLineKind colKind)
    {
        var i = r.Rows.ToList().FindIndex(l => l.Kind == rowKind && l.Path.SequenceEqual(rowPath));
        var j = r.Columns.ToList().FindIndex(l => l.Kind == colKind && l.Path.SequenceEqual(colPath));
        Assert.True(i >= 0, $"baris {string.Join('/', rowPath)} tidak ada");
        Assert.True(j >= 0, $"kolom {string.Join('/', colPath)} tidak ada");
        return r.Cells[i][j];
    }

    [Fact]
    public void Lines_are_nested_with_subtotals_and_grand_totals()
    {
        var r = PivotBuilder.Build(Layout(), Data);

        Assert.Equal(
            ["A/x", "A/y", "A (sub)", "B/x", "B/y", "B (sub)", "(total)"],
            r.Rows.Select(l => l.Kind switch { PivotLineKind.Leaf => string.Join('/', l.Path), PivotLineKind.Subtotal => l.Path[0] + " (sub)", _ => "(total)" }));
        Assert.Equal(["Januari", "Februari", "(total)"], r.Columns.Select(l => l.Kind == PivotLineKind.GrandTotal ? "(total)" : l.Path[0]));
    }

    [Fact]
    public void Sums_are_exact_at_every_level()
    {
        var r = PivotBuilder.Build(Layout(), Data);

        Assert.Equal(10, Cell(r, ["A", "x"], PivotLineKind.Leaf, ["Januari"], PivotLineKind.Leaf));
        Assert.Equal(30, Cell(r, ["A", "x"], PivotLineKind.Leaf, [], PivotLineKind.GrandTotal));
        Assert.Equal(15, Cell(r, ["A"], PivotLineKind.Subtotal, ["Januari"], PivotLineKind.Leaf));
        Assert.Equal(35, Cell(r, ["A"], PivotLineKind.Subtotal, [], PivotLineKind.GrandTotal));
        Assert.Equal(22, Cell(r, [], PivotLineKind.GrandTotal, ["Januari"], PivotLineKind.Leaf));
        Assert.Equal(54, Cell(r, [], PivotLineKind.GrandTotal, [], PivotLineKind.GrandTotal));
        Assert.Null(Cell(r, ["B", "x"], PivotLineKind.Leaf, ["Februari"], PivotLineKind.Leaf));
    }

    [Fact]
    public void Average_is_computed_from_all_underlying_values_not_from_averages()
    {
        // B/y Feb = {3, 9}; B/x Jan = {7} -> rata-rata B = (3 + 9 + 7) / 3, bukan (6 + 7) / 2
        var r = PivotBuilder.Build(Layout(PivotAggregate.Average), Data);
        Assert.Equal(19d / 3, Cell(r, ["B"], PivotLineKind.Subtotal, [], PivotLineKind.GrandTotal)!.Value, 9);
        Assert.Equal(6, Cell(r, ["B", "y"], PivotLineKind.Leaf, ["Februari"], PivotLineKind.Leaf));
    }

    [Fact]
    public void Count_min_max_use_partials()
    {
        Assert.Equal(6, Cell(PivotBuilder.Build(Layout(PivotAggregate.Count), Data), [], PivotLineKind.GrandTotal, [], PivotLineKind.GrandTotal));
        Assert.Equal(3, Cell(PivotBuilder.Build(Layout(PivotAggregate.Min), Data), [], PivotLineKind.GrandTotal, [], PivotLineKind.GrandTotal));
        Assert.Equal(20, Cell(PivotBuilder.Build(Layout(PivotAggregate.Max), Data), [], PivotLineKind.GrandTotal, [], PivotLineKind.GrandTotal));
    }

    [Fact]
    public void Percent_of_column_uses_the_column_grand_total()
    {
        var r = PivotBuilder.Build(Layout(PivotAggregate.PercentOfColumn), Data);
        // Januari: total 22, A/x = 10
        Assert.Equal(10d / 22 * 100, Cell(r, ["A", "x"], PivotLineKind.Leaf, ["Januari"], PivotLineKind.Leaf)!.Value, 9);
        Assert.Equal(100, Cell(r, [], PivotLineKind.GrandTotal, ["Januari"], PivotLineKind.Leaf)!.Value, 9);
    }

    [Fact]
    public void Percent_of_row_uses_the_row_grand_total()
    {
        var r = PivotBuilder.Build(Layout(PivotAggregate.PercentOfRow), Data);
        Assert.Equal(10d / 30 * 100, Cell(r, ["A", "x"], PivotLineKind.Leaf, ["Januari"], PivotLineKind.Leaf)!.Value, 9);
    }

    [Fact]
    public void Row_totals_off_hides_subtotals_but_keeps_grand_total()
    {
        var r = PivotBuilder.Build(Layout(rowTotals: false), Data);
        Assert.DoesNotContain(r.Rows, l => l.Kind == PivotLineKind.Subtotal);
        Assert.Contains(r.Rows, l => l.Kind == PivotLineKind.GrandTotal);
    }

    [Fact]
    public void Measure_without_grand_total_has_no_grand_column_and_blank_grand_row()
    {
        var r = PivotBuilder.Build(Layout(grandMeasure: false), Data);
        Assert.DoesNotContain(r.Columns, l => l.Kind == PivotLineKind.GrandTotal);
        Assert.Null(Cell(r, [], PivotLineKind.GrandTotal, ["Januari"], PivotLineKind.Leaf));
        Assert.Equal(15, Cell(r, ["A"], PivotLineKind.Subtotal, ["Januari"], PivotLineKind.Leaf));
    }

    [Fact]
    public void Each_visible_measure_gets_its_own_column_and_hidden_ones_are_dropped()
    {
        var layout = new PivotLayout(
            [new PivotField("R", "R")],
            [new PivotField("C", "C")],
            [new PivotMeasure("A", "Unit"), new PivotMeasure("B", "Urutan", Visible: false), new PivotMeasure("C", "Nilai")]);
        var rec = new PivotRecord(["r"], ["c"], [P(1), P(9), P(2)]);

        var r = PivotBuilder.Build(layout, [rec]);

        Assert.Equal(["Unit", "Nilai"], r.VisibleMeasures.Select(m => m.Caption));
        Assert.Equal(4, r.Columns.Count);
        Assert.Equal(2, Cell(r, ["r"], PivotLineKind.Leaf, ["c"], PivotLineKind.Leaf) is double ? 2 : 0);
        Assert.Equal([1d, 2d, 1d, 2d], r.Cells[0].Select(v => v!.Value));
    }

    [Fact]
    public void Members_can_be_sorted_by_a_helper_measure()
    {
        var layout = new PivotLayout(
            [new PivotField("Dpd", "DPD", SortByMeasure: "Urutan")],
            [],
            [new PivotMeasure("Amt", "Jumlah"), new PivotMeasure("Urutan", "Urutan", Visible: false)]);
        PivotRecord Rec(string dpd, double amt, double order) => new([dpd], [], [P(amt), P(order)]);

        var r = PivotBuilder.Build(layout, [Rec("> 90", 1, 3), Rec("1-30", 2, 1), Rec("31-90", 3, 2)]);

        Assert.Equal(["1-30", "31-90", "> 90"], r.Rows.Where(l => l.Kind == PivotLineKind.Leaf).Select(l => l.Path[0]));
    }

    [Fact]
    public void Default_order_is_by_value_not_by_text_and_blanks_come_first()
    {
        var layout = new PivotLayout([new PivotField("N", "N")], [], [new PivotMeasure("A", "A")]);
        PivotRecord Rec(object? n) => new([n], [], [P(1)]);

        var r = PivotBuilder.Build(layout, [Rec(10), Rec(2), Rec(null), Rec(1)]);

        Assert.Equal(["(kosong)", "1", "2", "10"], r.Rows.Where(l => l.Kind == PivotLineKind.Leaf).Select(l => l.Path[0]));
    }

    [Fact]
    public void No_row_fields_gives_a_single_line_and_no_column_fields_gives_measure_columns()
    {
        var layout = new PivotLayout([], [], [new PivotMeasure("A", "Jumlah")]);
        var r = PivotBuilder.Build(layout, [new PivotRecord([], [], [P(2, 3)])]);

        Assert.Single(r.Rows);
        Assert.Single(r.Columns);
        Assert.Equal(5, r.Cells[0][0]);
    }

    [Fact]
    public void Output_is_cut_when_it_exceeds_the_cell_limit()
    {
        var layout = new PivotLayout([new PivotField("R", "R")], [new PivotField("C", "C")], [new PivotMeasure("A", "A")]);
        var records = Enumerable.Range(0, 50).Select(i => new PivotRecord([i], [i % 5], [P(1)]));

        var r = PivotBuilder.Build(layout, records, maxCells: 100);

        Assert.True(r.Truncated);
        Assert.True(r.Rows.Count * r.Columns.Count <= 100);
    }

    [Theory]
    [InlineData(1234567.0, "N0", "1.234.567")]
    [InlineData(12.5, "N2", "12,50")]
    public void Describe_uses_indonesian_number_format(double value, string format, string expected)
    {
        var r = PivotBuilder.Build(new PivotLayout([], [], [new PivotMeasure("A", "A", Format: format)]), [new PivotRecord([], [], [P(value)])]);
        Assert.Equal(expected, r.Describe(r.Cells[0][0], 0));
    }
}

public class PivotQueryTests
{
    private static ReportDefinition Def() => new(
        "P", "T", "VW_FR", [],
        [new ReportFilter(1, "AS_OF_DATE", "Tanggal", ReportFilterType.DateRange, null, null, null)],
        new PivotLayout(
            [new PivotField("BRANCH_NAME", "Cabang"), new PivotField("_MONTH", "Bulan", PivotInterval.Month)],
            [new PivotField("_DATE", "Tanggal")],
            [new PivotMeasure("UNIT", "Unit"), new PivotMeasure("NOTE", "Catatan")]));

    private static readonly PivotColumnTypes Types = new([false, true], [false], [true, false]);

    [Fact]
    public void Select_groups_in_sql_applies_date_intervals_only_to_date_columns_and_sums_only_numbers()
    {
        var sql = PivotQuery.Select(Def(), Types, maxGroups: 100);

        Assert.StartsWith("SELECT TOP (101) [VW_FR].[BRANCH_NAME] AS d0, MONTH([VW_FR].[_MONTH]) AS d1, [VW_FR].[_DATE] AS d2, ", sql, StringComparison.Ordinal);
        Assert.Contains("SUM(CAST([VW_FR].[UNIT] AS float)) AS s0, COUNT([VW_FR].[UNIT]) AS n0", sql, StringComparison.Ordinal);
        Assert.Contains("CAST(0 AS float) AS s1, COUNT([VW_FR].[NOTE]) AS n1", sql, StringComparison.Ordinal);
        Assert.Contains("[VW_FR].[AS_OF_DATE] >= @1", sql, StringComparison.Ordinal);
        Assert.EndsWith("GROUP BY [VW_FR].[BRANCH_NAME], MONTH([VW_FR].[_MONTH]), [VW_FR].[_DATE]", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_turns_the_grouped_result_into_records()
    {
        var def = Def();
        var t = new DataTable();
        foreach (var name in new[] { "d0", "d1", "d2" })
        {
            t.Columns.Add(name, typeof(object));
        }

        foreach (var name in new[] { "s0", "n0", "mn0", "mx0", "s1", "n1", "mn1", "mx1" })
        {
            t.Columns.Add(name, typeof(object));
        }

        t.Rows.Add("  Jakarta ", 10, new DateTime(2026, 10, 1), 30d, 3L, 5d, 15d, 0d, 3L, 0d, 0d);
        t.Rows.Add("Bogor", DBNull.Value, new DateTime(2026, 10, 1), DBNull.Value, 0L, DBNull.Value, DBNull.Value, 0d, 0L, 0d, 0d);

        var (records, truncated) = PivotQuery.Read(def.Pivot!, t);

        Assert.False(truncated);
        Assert.Equal("Jakarta", records[0].Rows[0]);
        Assert.Equal(10, records[0].Rows[1]);
        Assert.Equal(30, records[0].Measures[0].Sum);
        Assert.Equal(3, records[0].Measures[0].Count);
        Assert.Equal(5, records[0].Measures[0].Min);
        Assert.Null(records[1].Rows[1]);
        Assert.Equal(0, records[1].Measures[0].Count);
    }

    [Fact]
    public void FromRows_groups_raw_rows_and_applies_intervals()
    {
        var layout = new PivotLayout(
            [new PivotField("KCU", "KCU")],
            [new PivotField("TGL", "Bulan", PivotInterval.Month)],
            [new PivotMeasure("AMT", "Amt")]);
        var t = new DataTable();
        t.Columns.Add("KCU", typeof(string));
        t.Columns.Add("TGL", typeof(DateTime));
        t.Columns.Add("AMT", typeof(decimal));
        t.Rows.Add("A", new DateTime(2026, 1, 5), 10m);
        t.Rows.Add("A", new DateTime(2026, 1, 20), 5m);
        t.Rows.Add("A", new DateTime(2026, 2, 1), 1m);

        var records = PivotQuery.FromRows(layout, t);
        var result = PivotBuilder.Build(layout, records);

        Assert.Equal(2, records.Count);
        Assert.Equal(["Januari", "Februari", "(total)"], result.Columns.Select(c => c.Kind == PivotLineKind.GrandTotal ? "(total)" : c.Path[0]));
        Assert.Equal([15d, 1d, 16d], result.Cells[0].Select(v => v!.Value));
    }

    [Fact]
    public void Probe_reads_types_in_the_order_rows_columns_measures()
    {
        var t = new DataTable();
        t.Columns.Add("a", typeof(string));
        t.Columns.Add("b", typeof(DateTime));
        t.Columns.Add("c", typeof(string));
        t.Columns.Add("d", typeof(decimal));
        t.Columns.Add("e", typeof(string));

        var types = PivotQuery.TypesFromProbe(Def(), t);

        Assert.Equal([false, true], types.RowIsDate);
        Assert.Equal([false], types.ColumnIsDate);
        Assert.Equal([true, false], types.MeasureIsNumeric);
        Assert.Contains("SELECT TOP 0 [VW_FR].[BRANCH_NAME]", PivotQuery.ProbeSql(Def()), StringComparison.Ordinal);
    }
}

public class PivotDefinitionParserTests
{
    private static ReportParseResult Parse(string? rows, string? cols, string? data, string? setting = null, string? cond = null)
        => ReportDefinitionParser.Parse("P", "T", cols, rows, data, "@[VW|TGL]:Tanggal:dr", cond, null, new DateOnly(2026, 10, 22), setting);

    [Fact]
    public void Dynamic_pivot_is_parsed_into_a_layout()
    {
        var r = Parse(
            "@[VW|BRANCH]:Cabang, @[VW|_MONTH]:TAHUN:year, @[VW|_MONTH]:BULAN:Month",
            "@[VW|_DATE]:Tanggal",
            "@[VW|UNIT]:Account::false, @[VW|AMT]:Amount:avg, @[VW|X]:Pct:col%",
            "false:false");

        Assert.Null(r.UnavailableReason);
        Assert.Equal(ReportKind.Pivot, r.Kind);
        var p = r.Definition!.Pivot!;
        Assert.Equal(["Cabang", "TAHUN", "BULAN"], p.Rows.Select(f => f.Caption));
        Assert.Equal([PivotInterval.None, PivotInterval.Year, PivotInterval.Month], p.Rows.Select(f => f.Interval));
        Assert.Equal(["Tanggal"], p.Columns.Select(f => f.Caption));
        Assert.Equal([PivotAggregate.Sum, PivotAggregate.Average, PivotAggregate.PercentOfColumn], p.Measures.Select(m => m.Aggregate));
        Assert.Equal([false, true, true], p.Measures.Select(m => m.ShowGrandTotal));
        Assert.False(p.RowTotals);
        Assert.True(r.Definition.IsPivot);
        Assert.Single(r.Definition.Filters);
    }

    [Fact]
    public void Row_totals_default_to_on_without_setting()
        => Assert.True(Parse("@[VW|A]:A", "@[VW|B]:B", "@[VW|C]:C").Definition!.Pivot!.RowTotals);

    [Fact]
    public void Pivot_with_custom_condition_or_mixed_views_is_not_available()
    {
        Assert.Contains("PV_COND", Parse("@[VW|A]:A", "@[VW|B]:B", "@[VW|C]:C", cond: "AND 1=1").UnavailableReason, StringComparison.Ordinal);
        Assert.NotNull(Parse("@[VW|A]:A", "@[OTHER|B]:B", "@[VW|C]:C").UnavailableReason);
    }

    [Fact]
    public void Pivot_without_columns_field_is_fine_when_rows_exist()
        => Assert.Null(Parse("@[VW|A]:A", null, "@[VW|C]:C").UnavailableReason);
}

public class PivotLayoutViewTests
{
    private static PivotPartial P(double v)
    {
        var p = new PivotPartial();
        p.Add(v);
        return p;
    }

    private static PivotResult Sample(int measures = 1)
    {
        var layout = new PivotLayout(
            [new PivotField("R", "Region"), new PivotField("P", "Produk")],
            [new PivotField("T", "Tahun"), new PivotField("M", "Bulan")],
            [.. Enumerable.Range(0, measures).Select(i => new PivotMeasure("m" + i, "Ukuran" + i))]);
        PivotRecord Rec(string region, string product, int year, int month, double v) => new([region, product], [year, month], [.. Enumerable.Range(0, measures).Select(_ => P(v))]);
        return PivotBuilder.Build(layout, [Rec("A", "x", 2025, 1, 1), Rec("A", "y", 2025, 2, 2), Rec("B", "x", 2026, 1, 4)]);
    }

    [Fact]
    public void Row_headers_merge_groups_and_label_subtotals_in_the_next_column()
    {
        var r = Sample();
        // baris: A/x, A/y, A(sub), B/x, B(sub), (total)
        var perLine = Enumerable.Range(0, r.Rows.Count).Select(i => PivotLayoutView.RowHeaders(r, i)).ToList();

        Assert.Equal(6, perLine.Count);
        Assert.Equal(("A", 3), (perLine[0][0].Text, perLine[0][0].RowSpan));
        Assert.Equal("x", perLine[0][1].Text);
        Assert.Equal(["y"], perLine[1].Select(c => c.Text));
        Assert.Equal(["Total A"], perLine[2].Select(c => c.Text));
        Assert.Equal(1, perLine[2][0].ColSpan);
        Assert.True(perLine[2][0].Total);
        Assert.Equal(("B", 2), (perLine[3][0].Text, perLine[3][0].RowSpan));
        Assert.Equal(["Total"], perLine[5].Select(c => c.Text));
        Assert.Equal(2, perLine[5][0].ColSpan);
    }

    [Fact]
    public void Column_headers_group_by_level_and_total_cells_span_the_levels_below()
    {
        var r = Sample();
        var h = PivotLayoutView.ColumnHeaders(r);

        Assert.Equal(2, h.Count);
        // tahun: 2025 (2 bulan + total) | 2026 (1 bulan + total) | Total
        Assert.Equal(["2025", "Total 2025", "2026", "Total 2026", "Total"], h[0].Select(c => c.Text));
        Assert.Equal(2, h[0][0].ColSpan);
        Assert.Equal(1, h[0][0].RowSpan);
        Assert.Equal(2, h[0][1].RowSpan);
        Assert.Equal(2, h[0][4].RowSpan);
        Assert.Equal(["1", "2", "1"], h[1].Select(c => c.Text));
    }

    [Fact]
    public void Measure_row_is_added_when_there_are_several_measures()
    {
        var r = Sample(measures: 2);
        var h = PivotLayoutView.ColumnHeaders(r);

        Assert.Equal(3, h.Count);
        Assert.Equal(r.Columns.Count, h[2].Count);
        Assert.Equal("Ukuran0", h[2][0].Text);
        Assert.Equal("Ukuran1", h[2][1].Text);
        Assert.Equal(4, h[0][0].ColSpan);
    }

    [Fact]
    public void Export_is_flat_with_combined_headers_and_total_labels()
    {
        var r = Sample();
        var t = PivotLayoutView.ToExportTable(r, "Judul");

        Assert.Equal(["Region", "Produk", "2025 / 1", "2025 / 2", "Total 2025", "2026 / 1", "Total 2026", "Total"], t.Columns.Select(c => c.Header));
        Assert.Equal(r.Rows.Count, t.Rows.Count);
        Assert.Equal(["A", "x"], t.Rows[0].Take(2).Cast<string>());
        Assert.Equal(["A", "Total A"], t.Rows[2].Take(2).Cast<string>());
        Assert.Equal("Total", t.Rows[^1][0]);
        Assert.Equal(1d, t.Rows[0][2]);
    }
}

public class CustomPivotCatalogTests
{
    private static readonly Panin.Infrastructure.Reports.Custom.CustomPivotCatalog Catalog = new();

    [Fact]
    public void Catalog_loads_the_extracted_layouts()
    {
        Assert.True(Catalog.Count >= 50, $"hanya {Catalog.Count} layout");
        Assert.NotNull(Catalog.Find("LOSCOLLECTION.Report.Template.PV_Alasan_NPL"));
        Assert.Null(Catalog.Find("tidak.ada"));
    }

    [Fact]
    public void Daily_tracking_layout_matches_the_old_designer()
    {
        var spec = Catalog.Find("LOSCOLLECTION.Report.Template.PV_DailyTracking_National")!;

        Assert.Equal("EXEC SP_VW_REPORT_DAILY_TRACKING_NATIONAL @1, @2", spec.CallSql);
        Assert.Equal(["PA/NS", "DPD"], spec.Layout.Rows.Select(f => f.Caption));
        Assert.Equal("URUTAN", spec.Layout.Rows[1].SortByMeasure);
        Assert.Equal("dd MMM yyyy", spec.Layout.Columns[0].Format);
        Assert.Equal(["UNIT", "PLAFOND", "OUTSTANDING", "CERUKAN", "URUTAN"], spec.Layout.Measures.Select(m => m.Key));
        Assert.False(spec.Layout.Measures.Single(m => m.Key == "URUTAN").Visible);
        Assert.False(spec.Layout.RowTotals);
        Assert.False(spec.Layout.RowGrandTotal);
        Assert.False(spec.Layout.ColumnTotals);
        Assert.False(spec.Layout.ColumnGrandTotal);
    }

    [Fact]
    public void Dataset_reports_read_the_pivot_table_and_the_period_text()
    {
        var spec = Catalog.Find("LOSCOLLECTION.Report.Template.PV_Kolek_National_PType")!;
        Assert.Equal(1, spec.DataTable);
        Assert.Equal("PERIODE", spec.SubtitleColumn);
    }

    [Fact]
    public void Every_layout_is_complete()
    {
        foreach (var cls in Catalog.Classes)
        {
            var spec = Catalog.Find(cls)!;
            Assert.True(spec.Layout.Measures.Count > 0, $"{cls}: tanpa ukuran");
            Assert.True(spec.Layout.Rows.Count + spec.Layout.Columns.Count > 0, $"{cls}: tanpa field baris/kolom");
            Assert.True(spec.Layout.Measures.Any(m => m.Visible), $"{cls}: semua ukuran tersembunyi");
            foreach (var f in spec.Layout.Rows.Concat(spec.Layout.Columns).Where(f => f.SortByMeasure is not null))
            {
                Assert.Contains(spec.Layout.Measures, m => m.Key == f.SortByMeasure);
            }
        }
    }

    [Fact]
    public void Parser_uses_the_catalog_and_checks_the_parameter_count()
    {
        var ok = ReportDefinitionParser.Parse("P26", "Daily Tracking - National", null, null, null, "@[SP_X|AS_OF_DATE]:Tanggal:dr", null, "LOSCOLLECTION.Report.Template.PV_DailyTracking_National", new DateOnly(2026, 10, 22), null, Catalog);
        Assert.Null(ok.UnavailableReason);
        Assert.Equal(ReportKind.Pivot, ok.Kind);
        Assert.NotNull(ok.Definition!.Custom);
        Assert.Equal(2, ok.Definition.ParamCount);

        var mismatch = ReportDefinitionParser.Parse("P26", "x", null, null, null, "@[SP_X|AS_OF_DATE]:Tanggal:df", null, "LOSCOLLECTION.Report.Template.PV_DailyTracking_National", new DateOnly(2026, 10, 22), null, Catalog);
        Assert.Contains("parameter", mismatch.UnavailableReason, StringComparison.Ordinal);

        var unknown = ReportDefinitionParser.Parse("P1", "x", null, null, null, null, null, "LOSCOLLECTION.Report.Template.Tidak_Ada", new DateOnly(2026, 10, 22), null, Catalog);
        Assert.Equal(ReportKind.Custom, unknown.Kind);
        Assert.NotNull(unknown.UnavailableReason);
    }
}

public class CompanionTableTests
{
    [Fact]
    public void Reason_report_has_a_pivot_table_and_a_companion_legend()
    {
        var spec = new Panin.Infrastructure.Reports.Custom.CustomPivotCatalog().Find("LOSCOLLECTION.Report.Template.PV_Alasan_NPL")!;

        Assert.Equal("EXEC SP_VW_REKAP_REASON_BRANCH @1, @2", spec.CallSql);
        Assert.Equal(1, spec.DataTable);
        Assert.Equal(2, spec.Companion!.Table);
        Assert.Equal(["REASON_ID", "REASON_DESC"], spec.Companion.Columns.Select(c => c.Field));
        Assert.Equal(["Branch(KCU)"], spec.Layout.Rows.Select(f => f.Caption));
        Assert.Equal(["Reason"], spec.Layout.Columns.Select(f => f.Caption));
        Assert.False(spec.Layout.RowTotals);
    }
}
