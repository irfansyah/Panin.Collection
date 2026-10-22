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
}
