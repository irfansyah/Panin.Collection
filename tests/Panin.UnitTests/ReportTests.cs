using System.Data;
using Microsoft.Extensions.Configuration;
using Panin.Application.Reports;
using Panin.Infrastructure.Reports;
using Panin.Web.Shell;

namespace Panin.UnitTests;

public class ReportParserTests
{
    private static readonly DateOnly Today = new(2026, 10, 22);

    private static ReportParseResult Parse(string columns, string? filters = null, string? cond = null, string? asm = null, string? rows = null, string? data = null)
        => ReportDefinitionParser.Parse("P1", "Judul", columns, rows, data, filters, cond, asm, Today);

    [Fact]
    public void Entries_split_on_comma_but_double_comma_is_literal()
    {
        var entries = ReportDefinitionParser.Entries("@[V|A]:Satu,\n @[V|B]:Dua:l:SELECT X,, Y FROM T WHERE Z IN('1',,'2'), ").ToList();
        Assert.Equal(2, entries.Count);
        Assert.Equal("@[V|B]:Dua:l:SELECT X, Y FROM T WHERE Z IN('1','2')", entries[1]);
    }

    [Fact]
    public void List_report_is_parsed_with_captions_summary_and_filter_positions()
    {
        var r = Parse(
            "@[VW_X|ACC_NO]:Nomor Rekening, @[VW_X|OUTSTANDING]:Outstanding:SUM, @[VW_X|NAMA]",
            "@[VW_X|TGL]:Tanggal:dr, @[VW_X|BR]:KCU:l:SELECT BRANCH_CODE,, BRANCH_NAME FROM VW_RFKCU, @[VW_X|KCP]:KCP:l:SELECT BRANCH_CODE,, BRANCH_NAME FROM VW_RFKCP WHERE BR_CCOBRANCH=@3, @[VW_X|ACC_NO]:Account");

        Assert.Null(r.UnavailableReason);
        var d = r.Definition!;
        Assert.Equal("VW_X", d.View);
        Assert.Equal(["Nomor Rekening", "Outstanding", "NAMA"], d.Columns.Select(c => c.Caption));
        Assert.Equal(ReportSummary.Sum, d.Columns[1].Summary);
        Assert.Equal([1, 3, 4, 5], d.Filters.Select(f => f.Seq));
        Assert.Equal(5, d.ParamCount);
        Assert.Equal([3], d.Filters[2].OptionDependsOn);
        Assert.Equal(ReportFilterType.Text, d.Filters[3].Type);
    }

    [Theory]
    [InlineData("dr", ReportFilterType.DateRange)]
    [InlineData("d", ReportFilterType.Date)]
    [InlineData("df", ReportFilterType.DateFrom)]
    [InlineData("dt", ReportFilterType.DateTo)]
    [InlineData("l", ReportFilterType.List)]
    [InlineData("i", ReportFilterType.Number)]
    [InlineData("ir", ReportFilterType.NumberRange)]
    [InlineData("m", ReportFilterType.MonthYear)]
    [InlineData("mr", ReportFilterType.MonthYearRange)]
    [InlineData("", ReportFilterType.Text)]
    [InlineData("zzz", ReportFilterType.Text)]
    public void Filter_types_follow_the_old_codes(string code, ReportFilterType expected) => Assert.Equal(expected, ReportDefinitionParser.ParseType(code));

    [Theory]
    [InlineData("drid", 2026, 10, 22, 2026, 10, 22)]
    [InlineData("drid-1", 2026, 10, 21, 2026, 10, 21)]
    [InlineData("drim", 2026, 10, 1, 2026, 10, 31)]
    [InlineData("driy", 2026, 1, 1, 2026, 12, 31)]
    public void Date_defaults_follow_the_init_markers(string code, int fy, int fm, int fd, int ty, int tm, int td)
    {
        var d = Parse("@[V|A]:A", $"@[V|T]:Tanggal:{code}").Definition!.Filters[0];
        Assert.Equal(new DateOnly(fy, fm, fd), d.DefaultFrom);
        Assert.Equal(new DateOnly(ty, tm, td), d.DefaultTo);
    }

    [Fact]
    public void Plain_date_range_has_no_default() => Assert.Null(Parse("@[V|A]:A", "@[V|T]:Tanggal:dr").Definition!.Filters[0].DefaultFrom);

    [Theory]
    [InlineData("@[V|A]:A", null, "AND x=1", null, "PV_COND")]
    [InlineData("@[V|A]:A", null, null, "LOSCOLLECTION.Report.Template.PV_X", "kustom")]
    [InlineData("", null, null, null, "kolom")]
    [InlineData("@[V|A]:A, @[W|B]:B", null, null, null, "lebih dari satu view")]
    [InlineData("@[V|A]:A", "@[V|B]:B:l:EXEC xp_cmdshell 'x'", null, null, "SELECT")]
    [InlineData("@[V|A]:A", "@[V|B]:B:l:SELECT a,, b FROM T; DROP TABLE T", null, null, "pemisah")]
    [InlineData("@[V|A]:A", "@[V|B]:B:l:SELECT a,, b FROM T WHERE x=@1", null, null, "tidak ada")]
    [InlineData("@[V|A;B]:A", null, null, null, "tidak valid")]
    [InlineData("A tanpa kurung", null, null, null, "tidak dikenal")]
    public void Unsupported_or_unsafe_definitions_are_reported_not_run(string columns, string? filters, string? cond, string? asm, string reasonPart)
    {
        var r = Parse(columns, filters, cond, asm);
        Assert.Null(r.Definition);
        Assert.Contains(reasonPart, r.UnavailableReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Filters_from_another_view_are_dropped_with_a_visible_note_and_do_not_consume_parameters()
    {
        var r = Parse("@[V|A]:A", "@[V|T]:Tanggal:dr, @[W|B]:Cabang:l:SELECT x,, y FROM T, @[V|N]:Nama");

        Assert.Null(r.UnavailableReason);
        var d = r.Definition!;
        Assert.Equal(["Tanggal", "Nama"], d.Filters.Select(f => f.Caption));
        Assert.Equal([1, 3], d.Filters.Select(f => f.Seq));
        Assert.Contains("Cabang", Assert.Single(d.Notes!), StringComparison.Ordinal);
    }

    private const string Latest = "AND [DATE] IN (SELECT MAX([DATE]) FROM VW_X A WHERE A.NOMER_LOAN = [VW_X].[NOMER_LOAN] AND (A.KCU_CODE=@1 AND A.KODE_STATUS=@2)) OR [DATE] IS NULL";

    [Fact]
    public void Latest_row_per_key_condition_is_recognised()
    {
        var r = ReportDefinitionParser.Parse("P", "T", "@[VW_X|NAMA]:Nama", null, null, "@[VW_X|KCU_CODE]:KCU:l:SELECT a,, b FROM T, @[VW_X|KODE_STATUS]:Status:l:SELECT a,, b FROM T", Latest, null, Today);

        Assert.Null(r.UnavailableReason);
        var latest = r.Definition!.Latest!;
        Assert.Equal(("DATE", "NOMER_LOAN"), (latest.DateColumn, latest.KeyColumn));
        Assert.Equal([("KCU_CODE", 1), ("KODE_STATUS", 2)], latest.Match.Select(m => (m.Column, m.Param)));

        var sql = ReportSqlBuilder.Where(r.Definition);
        Assert.Contains("([VW_X].[DATE] IS NULL OR [VW_X].[DATE] = (SELECT MAX(A.[DATE]) FROM [VW_X] A WHERE A.[NOMER_LOAN] = [VW_X].[NOMER_LOAN] AND (@1 IS NULL OR A.[KCU_CODE] = @1) AND (@2 IS NULL OR A.[KODE_STATUS] = @2)))", sql, StringComparison.Ordinal);
        Assert.Contains("[VW_X].[KCU_CODE] = @1", sql, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("AND [DATE] IN (SELECT MAX([DATE]) FROM VW_Y A WHERE A.K = [VW_X].[K] AND (A.C=@1)) OR [DATE] IS NULL")]
    [InlineData("AND [DATE] IN (SELECT MAX([DATE]) FROM VW_X A WHERE A.K = [VW_X].[K] AND (A.C=@1; DROP TABLE T)) OR [DATE] IS NULL")]
    [InlineData("AND [DATE] IN (SELECT MAX([DATE]) FROM VW_X A WHERE A.K = [VW_X].[K] AND (A.C=@1 OR 1=1)) OR [DATE] IS NULL")]
    [InlineData("AND [DATE] IN (SELECT MAX([DATE]) FROM VW_X A WHERE A.K = [VW_X].[K] AND (A.C=@9)) OR [DATE] IS NULL")]
    [InlineData("AND 1=1")]
    public void Other_conditions_stay_unavailable(string cond)
    {
        var r = ReportDefinitionParser.Parse("P", "T", "@[VW_X|NAMA]:Nama", null, null, "@[VW_X|C]:C:l:SELECT a,, b FROM T", cond, null, Today);
        Assert.Null(r.Definition);
        Assert.Contains("PV_COND", r.UnavailableReason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("SELECT a, b FROM T WHERE c=@2 ORDER BY b", true)]
    [InlineData("select code,  name from VW_RFKCU", true)]
    [InlineData("SELECT KODE_STATUS, NAMA FROM RFSTATUS_REK WHERE KODE_STATUS IN('001','002')", true)]
    [InlineData("SELECT a, b FROM T; SELECT 1", false)]
    [InlineData("SELECT a, b FROM T -- x", false)]
    [InlineData("SELECT a, b FROM T /* x */", false)]
    [InlineData("UPDATE T SET a=1", false)]
    [InlineData("SELECT a INTO #x FROM T", false)]
    [InlineData("SELECT a, b FROM T WHERE a IN (SELECT name FROM sys.objects)", false)]
    [InlineData("SELECT a, b FROM OPENROWSET('x','y','z')", false)]
    [InlineData("SELECT 1", false)]
    [InlineData("", false)]
    public void Option_sql_is_validated(string sql, bool ok) => Assert.Equal(ok, ReportOptionSql.Validate(sql) is null);

    [Fact]
    public void Option_sql_references_are_distinct_and_sorted() => Assert.Equal([1, 3], ReportOptionSql.References("SELECT a, b FROM T WHERE x=@3 AND y=@1 AND z=@3"));
}

public class ReportSqlBuilderTests
{
    private static ReportDefinition Def(params (string Field, ReportFilterType Type)[] filters)
    {
        var seq = 1;
        var list = new List<ReportFilter>();
        foreach (var (field, type) in filters)
        {
            var f = new ReportFilter(seq, field, field, type, null, null, null);
            list.Add(f);
            seq += f.ParamCount;
        }

        return new ReportDefinition("P", "T", "VW_X", [new ReportColumn("ACC NO.", "No", ReportSummary.None), new ReportColumn("AMT", "Jumlah", ReportSummary.Sum)], list);
    }

    [Fact]
    public void Select_quotes_names_aliases_columns_and_limits_rows()
    {
        var sql = ReportSqlBuilder.Select(Def(("TGL", ReportFilterType.DateRange), ("NAMA", ReportFilterType.Text)), 5001);
        Assert.StartsWith("SELECT TOP (5001) [VW_X].[ACC NO.] AS c0, [VW_X].[AMT] AS c1 FROM [VW_X] WHERE 1=1", sql, StringComparison.Ordinal);
        Assert.Contains("@1 IS NULL OR [VW_X].[TGL] >= @1", sql, StringComparison.Ordinal);
        Assert.Contains("[VW_X].[TGL] < DATEADD(DD, 1, CONVERT(DATETIME, @2))", sql, StringComparison.Ordinal);
        Assert.Contains("[VW_X].[NAMA] LIKE '%' + @3 + '%'", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Values_never_appear_in_the_sql_text()
    {
        var def = Def(("NAMA", ReportFilterType.Text));
        var (args, _) = ReportSqlBuilder.Coerce(def, ["x'; DROP TABLE T;--"]);
        var sql = ReportSqlBuilder.Select(def, 10);
        Assert.DoesNotContain("DROP", sql, StringComparison.Ordinal);
        Assert.Equal("x'; DROP TABLE T;--", args[0]);
    }

    [Fact]
    public void Month_year_range_compares_whole_months()
    {
        var sql = ReportSqlBuilder.Where(Def(("TGL", ReportFilterType.MonthYearRange)));
        Assert.Contains("YEAR([VW_X].[TGL]) * 12 + MONTH([VW_X].[TGL]) >= @2 * 12 + @1", sql, StringComparison.Ordinal);
        Assert.Contains("YEAR([VW_X].[TGL]) * 12 + MONTH([VW_X].[TGL]) <= @4 * 12 + @3", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Coerce_converts_blank_and_none_to_null_and_parses_types()
    {
        var def = Def(("A", ReportFilterType.DateRange), ("B", ReportFilterType.NumberRange), ("C", ReportFilterType.MonthYear), ("D", ReportFilterType.List));
        var (args, errors) = ReportSqlBuilder.Coerce(def, ["2026-10-01", "", "1.500,5".Replace(".", string.Empty, StringComparison.Ordinal).Replace(",", ".", StringComparison.Ordinal), "  ", "10", "2026", "(none)"]);

        Assert.Empty(errors);
        Assert.Equal(new DateTime(2026, 10, 1), args[0]);
        Assert.Null(args[1]);
        Assert.Equal(1500.5m, args[2]);
        Assert.Null(args[3]);
        Assert.Equal(10, args[4]);
        Assert.Equal(2026, args[5]);
        Assert.Null(args[6]);
    }

    [Theory]
    [InlineData(ReportFilterType.Number, "abc", "angka")]
    [InlineData(ReportFilterType.Date, "bukan tanggal", "tanggal")]
    public void Coerce_reports_invalid_values(ReportFilterType type, string value, string message)
    {
        var (_, errors) = ReportSqlBuilder.Coerce(Def(("A", type)), [value]);
        Assert.Contains(errors, e => e.Contains(message, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("13", "2026", "bulan")]
    [InlineData("1", "26", "tahun")]
    public void Coerce_validates_month_and_year(string month, string year, string message)
    {
        var (_, errors) = ReportSqlBuilder.Coerce(Def(("A", ReportFilterType.MonthYear)), [month, year]);
        Assert.Contains(errors, e => e.Contains(message, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("ACC_NO", true)]
    [InlineData("CIF NO.", true)]
    [InlineData("Action By", true)]
    [InlineData("A]B", false)]
    [InlineData("A;B", false)]
    [InlineData("A'B", false)]
    [InlineData("", false)]
    public void Identifiers_are_restricted(string name, bool ok) => Assert.Equal(ok, ReportSqlBuilder.IsSafeIdentifier(name));

    [Fact]
    public void Map_trims_text_marks_truncation_and_detects_kinds()
    {
        var def = Def();
        var table = new DataTable();
        table.Columns.Add("c0", typeof(string));
        table.Columns.Add("c1", typeof(decimal));
        table.Rows.Add("  A  ", 1.5m);
        table.Rows.Add("B", DBNull.Value);
        table.Rows.Add("C", 3m);

        var result = ReportService.Map(def, table, 2);

        Assert.True(result.Truncated);
        Assert.Equal(2, result.Rows.Count);
        Assert.Equal("A", result.Rows[0][0]);
        Assert.Null(result.Rows[1][1]);
        Assert.Equal(ReportCellKind.Decimal, result.Columns[1].Kind);
        Assert.Equal(ReportSummary.Sum, result.Columns[1].Summary);
    }
}

public class ReportMenuResolveTests
{
    private static ModulePageCatalog Catalog() => new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Menu:LegacyUrlMap:~/report/reportset.aspx?setid=dt"] = "/reports?SetId=DT",
    }).Build());

    [Theory]
    [InlineData("60")]
    [InlineData("70")]
    public void Report_menu_resolves_in_both_modules_with_the_set_as_parameter(string module)
    {
        var page = Catalog().Resolve(module, "~/Report/ReportSet.aspx?setid=DT");
        Assert.False(page.IsLegacy);
        Assert.Equal("DT", page.Parameters!["SetId"]);
    }

    [Fact]
    public void Report_menu_is_not_available_in_other_modules() => Assert.True(Catalog().Resolve("99", "~/Report/ReportSet.aspx?setid=DT").IsLegacy);
}

public class CustomListTests
{
    private const string Productivity = "LOSCOLLECTION.Report.Template.CallProductivity";
    private const string Filters = "@[usp_ViewReportCollProd|CALL_DATE]:Call Date:dr, @[usp_ViewReportCollProd|Upliner]:Supervisor:l:SELECT USERID,, FULL_NAME FROM SCUSER, @[usp_ViewReportCollProd|USERID]:Kolektor:l:SELECT USERID,, FULL_NAME FROM SCUSER where USERID IN (SELECT DOWNLINE FROM TEMP_DOWNLINE WHERE USERID=@3)";

    [Theory]
    [InlineData(Productivity, "USP_VIEWREPORTCOLLPROD")]
    [InlineData(Productivity + "Recovery", "USP_VIEWREPORTCOLLPROD_RECOVERY")]
    public void Productivity_reports_are_parsed_as_procedure_lists(string cls, string procedure)
    {
        var r = ReportDefinitionParser.Parse("P", "Collector Productivity", null, null, null, Filters, null, cls, new DateOnly(2026, 10, 22));

        Assert.Null(r.UnavailableReason);
        var d = r.Definition!;
        Assert.Equal(procedure, d.CustomList!.Procedure);
        Assert.Equal("EXEC " + procedure + " @1, @2, @3, @4", d.CustomList.CallSql);
        Assert.Equal(4, d.ParamCount);
        Assert.Equal(11, d.Columns.Count);
        Assert.False(d.IsPivot);
    }

    [Fact]
    public void Parameter_count_mismatch_makes_the_report_unavailable()
    {
        var r = ReportDefinitionParser.Parse("P", "x", null, null, null, "@[x|CALL_DATE]:Call Date:df", null, Productivity, new DateOnly(2026, 10, 22));
        Assert.Contains("parameter", r.UnavailableReason, StringComparison.Ordinal);
    }

    [Fact]
    public void Result_set_is_mapped_by_column_name_with_a_subtitle()
    {
        var spec = CustomLists.Find(Productivity)!;
        var header = new DataTable();
        header.Columns.Add("UPLINER");
        header.Columns.Add("COLLECTOR");
        header.Rows.Add("Budi", "Ani");
        var detail = new DataTable();
        foreach (var c in new[] { "USERID", "FULL_NAME", "CALL_DATE", "NUMBER_OF_ACCOUNT", "OUTGOING_CALL", "CONTACTED", "PTP", "PTPAMOUNT", "PK", "PKAMOUNT", "BP", "BPAMOUNT" })
        {
            detail.Columns.Add(c, c == "CALL_DATE" ? typeof(DateTime) : c == "FULL_NAME" || c == "USERID" ? typeof(string) : typeof(double));
        }

        detail.Rows.Add("U1", " ANI ", new DateTime(2026, 1, 5), 3d, 2d, 1d, 0d, 0d, 0d, 0d, 0d, 0d);
        var set = new DataSet();
        set.Tables.Add(header);
        set.Tables.Add(detail);

        var result = ReportService.MapProcedure(spec, set, 100);

        Assert.Equal("Supervisor: Budi   Kolektor: Ani", result.Subtitle);
        Assert.Equal("ANI", result.Rows[0][0]);
        Assert.Equal(ReportCellKind.Date, result.Columns[1].Kind);
        Assert.Equal(ReportSummary.Sum, result.Columns[2].Summary);
    }
}
