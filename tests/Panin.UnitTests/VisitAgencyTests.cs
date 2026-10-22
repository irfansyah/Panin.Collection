using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;
using Panin.Application.Abstractions;
using Panin.Infrastructure.Collection;
using Panin.Infrastructure.Export;

namespace Panin.UnitTests;

public class VisitAgencyBatchTests
{
    [Fact]
    public void Batch_is_one_transaction_with_a_lock_and_three_procedures_per_chunk()
    {
        var (sql, args) = VisitAgencyService.BuildBatch("T1", "A1", Guid.Empty, "'',U1", ["'',111,222", "'',333"]);

        Assert.Contains("SET XACT_ABORT ON", sql, StringComparison.Ordinal);
        Assert.Contains("sp_getapplock", sql, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(sql, "COMMIT TRAN"));
        Assert.Equal(2, Regex.Matches(sql, "EXEC SP_GENERATE_EXCEL_VISIT @1").Count);
        Assert.Equal(2, Regex.Matches(sql, "EXEC SP_GENERATE_EXCEL_VISIT2").Count);
        Assert.Equal(2, Regex.Matches(sql, "EXEC SP_GENERATE_EXCEL_VISIT4").Count);

        // kelompok user hanya ikut potongan pertama
        Assert.Contains("EXEC SP_GENERATE_EXCEL_VISIT @1, @4, @6, @2, @3;", sql, StringComparison.Ordinal);
        Assert.Contains("EXEC SP_GENERATE_EXCEL_VISIT @1, @5, @7, @2, @3;", sql, StringComparison.Ordinal);
        Assert.Equal(new object?[] { "T1", "A1", Guid.Empty, "'',U1", "''", "'',111,222", "'',333" }, args);
    }

    private static DataTable T(string col, params string[] values)
    {
        var t = new DataTable();
        t.Columns.Add(col);
        foreach (var v in values)
        {
            t.Rows.Add(v);
        }

        return t;
    }

    private static DataTable Keyed(params string[] accounts)
    {
        var t = new DataTable();
        t.Columns.Add("ACC_NO");
        t.Columns.Add("MONTH_DUE");
        foreach (var a in accounts)
        {
            t.Rows.Add(a, "1");
        }

        return t;
    }

    [Fact]
    public void Merge_keeps_each_account_once_across_chunks()
    {
        var set = new DataSet();
        for (var chunk = 0; chunk < 2; chunk++)
        {
            var second = chunk == 0 ? "A2" : "A3";
            set.Tables.Add(T("ACC", "A1", second));   // 0 data visit (kolom pertama = rekening)
            set.Tables.Add(T("x", "G2"));             // 1
            set.Tables.Add(T("P_ID", "01"));          // 2
            set.Tables.Add(T("REL_ID", "00000"));     // 3
            set.Tables.Add(T("RESULT_ID", "R1"));     // 4
            set.Tables.Add(T("ACTION_ID", "5"));      // 5
            set.Tables.Add(T("x", "A1"));             // 6
            set.Tables.Add(Keyed("A1", second));      // 7 surat
            set.Tables.Add(T("x", "A16"));            // 8
            set.Tables.Add(Keyed("A1", second));      // 9 rincian
            set.Tables.Add(T("x", "A1"));             // 10
            set.Tables.Add(Keyed("A1", second));      // 11 slip
        }

        foreach (var list in new[] { 2, 3, 4, 5 })
        {
            foreach (var t in new[] { set.Tables[list], set.Tables[12 + list] })
            {
                t.Columns.Add("DESC");
                t.Rows[0][1] = "deskripsi";
            }
        }

        var data = VisitAgencyService.Merge(set, 2);

        Assert.Equal(["A1", "A2", "A3"], data.Visit.Rows.Cast<DataRow>().Select(r => r[0]?.ToString()));
        Assert.Equal(3, data.Letter.Rows.Count);
        Assert.Equal(3, data.LetterDetail.Rows.Count);
        Assert.Equal(3, data.Slip.Rows.Count);
        Assert.Equal("01 - deskripsi", data.Lists[0][0]);
        Assert.Equal("5 - deskripsi", data.Lists[3][0]);
    }
}

public class VisitAgencyIntegrationTests
{
    private static SqlDbForTests? Connect()
    {
        var cs = Environment.GetEnvironmentVariable("PANIN_TEST_SQL");
        return string.IsNullOrWhiteSpace(cs) ? null : new SqlDbForTests(new Panin.Infrastructure.Data.SqlDb(cs, "test"));
    }

    private sealed class SqlDbForTests(Panin.Infrastructure.Data.SqlDb db)
    {
        public Panin.Infrastructure.Data.SqlDb Db { get; } = db;
    }

    [Fact]
    public async Task Excel_is_built_from_the_real_stored_procedures_and_the_database_is_restored()
    {
        if (Connect() is not { } holder)
        {
            return;
        }

        var db = holder.Db;
        var accounts = (await db.QueryTableAsync("SELECT TOP 3 LM.ACC_NO FROM LOAN_MASTER LM JOIN ACCOUNT_MASTER AM ON AM.ACC_NO = LM.ACC_NO WHERE LM.USERID IS NOT NULL ORDER BY LM.ACC_NO", []))
            .Rows.Cast<DataRow>().Select(r => Convert.ToString(r[0], CultureInfo.InvariantCulture)!.Trim()).ToList();
        if (accounts.Count == 0)
        {
            return;
        }

        var actId = Guid.NewGuid();
        await db.ExecuteAsync("IF OBJECT_ID('ZZ_RNH') IS NOT NULL DROP TABLE ZZ_RNH; IF OBJECT_ID('ZZ_INI') IS NOT NULL DROP TABLE ZZ_INI; SELECT * INTO ZZ_RNH FROM RUNNING_NUMBER_HISTORY; SELECT IN_LAST_YEAR, IN_LAST_NUMBER INTO ZZ_INI FROM INITIAL WHERE IN_SEQ = 1", []);
        try
        {
            var chunk = "''," + string.Join(',', accounts);
            var (sql, args) = VisitAgencyService.BuildBatch("LT0003", "LT0003", actId, "''", [chunk]);
            var set = await db.QueryDataSetAsync(sql, args, new DbOptions(TimeoutSeconds: 300));

            Assert.Equal(12, set.Tables.Count);
            var data = VisitAgencyService.Merge(set, 1);
            Assert.Equal(accounts.Count, data.Visit.Rows.Count);
            Assert.Equal(accounts.Count, data.Letter.Rows.Count);
            Assert.Equal(accounts.Count, data.Slip.Rows.Count);
            Assert.Equal(4, data.Lists.Count);
            Assert.All(data.Lists, l => Assert.NotEmpty(l));

            var template = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Panin.Web", "Templates", "Visit.xls"));
            await using var stream = File.OpenRead(template);
            var bytes = VisitWorkbookBuilder.Build(stream, data, "PASS");
            var wb = new NPOI.HSSF.UserModel.HSSFWorkbook(new MemoryStream(bytes));
            Assert.Equal(accounts[0], wb.GetSheet("Visit").GetRow(1).GetCell(0).ToString()?.Trim());
            Assert.False(string.IsNullOrWhiteSpace(wb.GetSheet("Visit").GetRow(1).GetCell(32)?.ToString()));
            Assert.Equal(accounts.Count, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM SLIP_HISTORY WHERE ACT_ID = @1", [actId]));
        }
        finally
        {
            await db.ExecuteAsync("DELETE FROM SLIP_HISTORY WHERE ACT_ID = @1; DELETE FROM RUNNING_NUMBER_HISTORY; INSERT RUNNING_NUMBER_HISTORY SELECT * FROM ZZ_RNH; UPDATE I SET IN_LAST_YEAR = Z.IN_LAST_YEAR, IN_LAST_NUMBER = Z.IN_LAST_NUMBER FROM INITIAL I CROSS JOIN ZZ_INI Z WHERE I.IN_SEQ = 1; DROP TABLE ZZ_RNH; DROP TABLE ZZ_INI", [actId]);
        }
    }
}
