using Panin.Infrastructure.Data;

namespace Panin.UnitTests;

/// <summary>
/// Uji terhadap SQL Server sungguhan. Jalan hanya bila <c>PANIN_TEST_SQL</c> berisi connection string DB uji (mis. Docker lokal);
/// tanpa itu tes ini lolos tanpa melakukan apa-apa. Hanya SELECT, tidak mengubah data.
/// </summary>
public class SqlDbIntegrationTests
{
    private static SqlDb? Connect()
    {
        var cs = Environment.GetEnvironmentVariable("PANIN_TEST_SQL");
        return string.IsNullOrWhiteSpace(cs) ? null : new SqlDb(cs, "test");
    }

    [Fact]
    public async Task Multiple_result_sets_are_all_returned()
    {
        if (Connect() is not { } db)
        {
            return;
        }

        var set = await db.QueryDataSetAsync("select 1 as a; select 2 as b, 3 as c; select 4 as d");

        Assert.Equal(3, set.Tables.Count);
        Assert.Equal(1, Convert.ToInt32(set.Tables[0].Rows[0]["a"], System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(2, set.Tables[1].Columns.Count);
        Assert.Equal(4, Convert.ToInt32(set.Tables[2].Rows[0]["d"], System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task A_single_result_set_still_works()
    {
        if (Connect() is not { } db)
        {
            return;
        }

        var set = await db.QueryDataSetAsync("select 1 as a");
        Assert.Single(set.Tables);
    }

    [Fact]
    public async Task Statements_without_a_result_set_yield_an_empty_table()
    {
        if (Connect() is not { } db)
        {
            return;
        }

        var set = await db.QueryDataSetAsync("declare @x int = 1;");
        Assert.True(set.Tables.Count <= 1);
        Assert.Equal(0, set.Tables.Count == 0 ? 0 : set.Tables[0].Rows.Count);
    }

    [Fact]
    public async Task Recovery_procedure_output_maps_like_the_collection_one()
    {
        if (Connect() is not { } db)
        {
            return;
        }

        // Pemilik rekening NPL terbanyak pada salinan lokal; bila tidak ada data, tes tidak berarti apa-apa.
        var owner = await db.ExecuteScalarAsync<string?>(
            "SELECT TOP 1 LM.USERID FROM LOAN_MASTER LM JOIN ACCOUNT_MASTER AM ON AM.ACC_NO = LM.ACC_NO JOIN LOAN_DETAIL LD ON LD.ACC_NO = LM.ACC_NO WHERE LM.CONFIRMED = '1' AND (AM.STATUS IN ('006','023','024') OR LD.DPD > 90) GROUP BY LM.USERID ORDER BY COUNT(*) DESC", []);
        if (owner is null)
        {
            return;
        }

        var table = await db.QueryTableAsync("exec SP_ACCOUNT_MONITORING_RECOVERY @1, @2", [owner, false]);
        var result = Panin.Infrastructure.Collection.AccountMonitoringService.Map(table);

        Assert.NotEmpty(result.Rows);
        Assert.All(result.Rows, r =>
        {
            Assert.False(string.IsNullOrEmpty(r.AccountNo));
            Assert.False(string.IsNullOrEmpty(r.CustomerName));
            Assert.False(string.IsNullOrEmpty(r.CustomerRef));
        });
    }

    [Fact]
    public async Task Every_supported_list_report_and_its_filter_options_run_against_the_real_views()
    {
        if (Connect() is not { } db)
        {
            return;
        }

        var table = await db.QueryTableAsync("SELECT PV_ID, PV_DESC, PV_COLUMNLIST, PV_ROWLIST, PV_DATALIST, PV_FILTERLIST, PV_COND, AssemblyObj FROM VW_REPORTSET", []);
        var failures = new List<string>();
        var supported = 0;
        var customRun = 0;
        var customRows = 0;
        foreach (System.Data.DataRow r in table.Rows)
        {
            string? T(string c) => r[c] is DBNull ? null : Convert.ToString(r[c], System.Globalization.CultureInfo.InvariantCulture);
            var parsed = Panin.Application.Reports.ReportDefinitionParser.Parse(T("PV_ID")!, T("PV_DESC") ?? string.Empty, T("PV_COLUMNLIST"), T("PV_ROWLIST"), T("PV_DATALIST"), T("PV_FILTERLIST"), T("PV_COND"), T("AssemblyObj"), DateOnly.FromDateTime(DateTime.Today), null, new Panin.Infrastructure.Reports.Custom.CustomPivotCatalog());
            if (parsed.Definition is not { } def)
            {
                continue;
            }

            supported++;
            try
            {
                var args = new object?[def.ParamCount];
                if (def.Custom is { Procedure: "SP_REPORT_WAS_IS_ANALYSIS" })
                {
                    // Terukur 193 detik untuk rentang satu hari di salinan lokal (docs/QUERY-REGISTRY.md); tidak dijalankan di tes.
                    customRun++;
                }
                else if (def.CustomList is { } listSpec)
                {
                    var set = await db.QueryDataSetAsync(listSpec.CallSql, args);
                    Panin.Infrastructure.Reports.ReportService.MapProcedure(listSpec, set, 10);
                    customRun++;
                }
                else if (def.Custom is { } spec)
                {
                    var set = await db.QueryDataSetAsync(spec.CallSql, args);
                    var data = set.Tables[spec.DataTable];
                    var built = Panin.Application.Reports.Pivot.PivotBuilder.Build(def.Pivot!, Panin.Application.Reports.Pivot.PivotQuery.FromRows(def.Pivot!, data));
                    customRun++;
                    customRows += data.Rows.Count;
                    _ = built;
                }
                else if (def.Pivot is not null)
                {
                    var probe = await db.QueryTableAsync(Panin.Application.Reports.Pivot.PivotQuery.ProbeSql(def), []);
                    var types = Panin.Application.Reports.Pivot.PivotQuery.TypesFromProbe(def, probe);
                    var grouped = await db.QueryTableAsync(Panin.Application.Reports.Pivot.PivotQuery.Select(def, types, 50), args);
                    var (records, _) = Panin.Application.Reports.Pivot.PivotQuery.Read(def.Pivot, grouped, 50);
                    Panin.Application.Reports.Pivot.PivotBuilder.Build(def.Pivot, records);
                }
                else
                {
                    await db.QueryTableAsync(Panin.Application.Reports.ReportSqlBuilder.Select(def, 3), args);
                }

                foreach (var f in def.Filters.Where(f => f.OptionsSql is not null))
                {
                    await db.QueryTableAsync(f.OptionsSql!, args);
                }
            }
            catch (Exception ex)
            {
                failures.Add($"{def.Id} {def.Title}: {ex.Message}");
            }
        }

        Assert.True(supported > 0, "tidak ada report daftar yang didukung");
        Assert.True(failures.Count == 0, $"{failures.Count} dari {supported} report gagal ({customRun} kustom jalan, {customRows} baris):{Environment.NewLine}{string.Join(Environment.NewLine, failures)}");
        Assert.True(customRun >= 40, $"hanya {customRun} report kustom yang didukung");
    }

    [Fact]
    public async Task Every_memo_builds_from_the_real_views_with_the_debtor_name_filled_in()
    {
        if (Connect() is not { } db)
        {
            return;
        }

        foreach (var def in Panin.Infrastructure.Memos.MemoCatalog.All)
        {
            var row = await db.QueryFirstOrDefaultAsync($"SELECT TOP 1 ACC_NO, NAMA_DEBITUR FROM {def.ReportView} WHERE NAMA_DEBITUR IS NOT NULL");
            if (row is null)
            {
                continue;
            }

            var search = await db.QueryTableAsync($"SELECT [NO ACCOUNT], [NAMA DEBITUR] FROM {def.SearchView} WHERE [NO ACCOUNT] = @1", [Convert.ToString(row["ACC_NO"], System.Globalization.CultureInfo.InvariantCulture)!.Trim()]);
            Assert.NotEmpty(search.Rows);

            var columns = string.Join(", ", def.Map.Values.Distinct());
            Assert.NotNull(await db.QueryFirstOrDefaultAsync($"SELECT {columns} FROM {def.ReportView} WHERE ACC_NO = @1", [row["ACC_NO"]]));
        }
    }
}
