using System.Data;
using Panin.Infrastructure.Data;
using Panin.Infrastructure.Status;

namespace Panin.UnitTests;

public class ReadOnlyDbTests
{
    private static readonly ReadOnlyDb Db = new(null!, new HashSet<string>(["SP_VW_WARNINGCONTENT"], StringComparer.OrdinalIgnoreCase), "t");

    [Theory]
    [InlineData("select * from ACCOUNT_MASTER where ACC_NO = @1")]
    [InlineData("  SELECT max(AS_OF_DATE) from ACCOUNT_MASTER")]
    [InlineData("with x as (select 1 a) select a from x")]
    [InlineData("exec SP_VW_WARNINGCONTENT @1")]
    [InlineData("EXEC dbo.sp_vw_warningcontent @1")]
    public void Allows_reads_and_allowlisted_procedures(string sql) => Assert.True(Db.Check(sql));

    [Theory]
    [InlineData("update ACCOUNT_MASTER set X = 1")]
    [InlineData("delete from CALL_HISTORY")]
    [InlineData("insert into T values (1)")]
    [InlineData("select * into COPY from ACCOUNT_MASTER")]
    [InlineData("select 1; drop table T")]
    [InlineData("exec SP_SAVE_SURAT_TEGURAN @1")]
    [InlineData("exec sp_executesql N'delete from T'")]
    [InlineData("truncate table T")]
    public void Blocks_writes_and_unlisted_procedures(string sql) => Assert.False(Db.Check(sql));

    [Fact]
    public async Task Execute_is_always_blocked()
        => await Assert.ThrowsAsync<InvalidOperationException>(() => Db.ExecuteAsync("select 1"));
}

public class WarningParseTests
{
    private static DataSet Set(params object[][] rows)
    {
        var title = new DataTable();
        title.Columns.Add("TITLE");
        title.Rows.Add("Daftar Janji");
        var data = new DataTable();
        data.Columns.Add("Account Number");
        data.Columns.Add("Customer Name");
        foreach (var r in rows)
        {
            data.Rows.Add(r);
        }

        var ds = new DataSet();
        ds.Tables.Add(title);
        ds.Tables.Add(data);
        return ds;
    }

    [Fact]
    public void Empty_result_gives_empty_set() => Assert.False(WarningService.Parse(Set()).HasRows);

    [Fact]
    public void Strips_html_and_extracts_account_and_customer_from_legacy_link()
    {
        var link = "<a href=# onclick=\"PopupPage('../Popup/main_collection.aspx?ACC=1234567&CUST=C99',null,600);\">1234567</a>";
        var set = WarningService.Parse(Set([link, "BUDI"]));

        Assert.Equal("Daftar Janji", set.Title);
        var row = Assert.Single(set.Rows);
        Assert.Equal("1234567", row.Cells["Account Number"]);
        Assert.Equal("BUDI", row.Cells["Customer Name"]);
        Assert.Equal("1234567", row.AccountNo);
        Assert.Equal("C99", row.CustomerRef);
    }
}
