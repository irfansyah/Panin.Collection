using System.Data;
using Panin.Application.Collection;
using Panin.Infrastructure.Collection;
using Panin.Infrastructure.Data;

namespace Panin.UnitTests;

public class ReadOnlyGuardAllowsApplicationQueriesTests
{
    // Pengaman baca-saja menolak apa pun di luar SELECT dan SP yang diizinkan. Uji ini memastikan query aplikasi sendiri
    // tidak ikut terblokir (kesalahan seperti itu baru muncul saat halaman dibuka).
    private static readonly string[] Procedures =
    [
        "SP_VW_WARNINGCONTENT", "USP_VWDELINQHIST", "SP_JOB_INFO", "SP_SPOUSE_INFO", "SP_SPOUSE_JOB", "SP_GET_CONTACT_INFO",
        "SP_ACCOUNT_MONITORING_DEBITUR", "SP_GET_TARGET_ACHIEVMENT", "SP_GET_OS",
    ];

    private static readonly ReadOnlyDb Db = new(null!, new HashSet<string>(Procedures, StringComparer.OrdinalIgnoreCase), "t");

    [Theory]
    [InlineData(AccountMonitoringService.TeamSql)]
    [InlineData(AccountMonitoringService.CanViewSql)]
    [InlineData(DebiturSearchService.Sql)]
    public void Application_selects_pass_the_guard(string sql) => Assert.True(Db.Check(sql));

    [Fact]
    public void Account_detail_query_passes_the_guard() => Assert.True(Db.Check(DebiturDetailService.AccountSql));

    [Theory]
    [InlineData("exec SP_ACCOUNT_MONITORING_DEBITUR @1, @2")]
    [InlineData("exec SP_GET_TARGET_ACHIEVMENT @1, @2")]
    [InlineData("exec SP_GET_OS @1")]
    public void Allowlisted_monitoring_procedures_pass(string sql) => Assert.True(Db.Check(sql));
}

public class MonitoringMapTests
{
    private static DataTable Table(params object?[][] rows)
    {
        var t = new DataTable();
        foreach (var c in new[] { "FLAG", "Detail", "Nomor Rekening", "Nama Nasabah", "Kota", "Fasilitas", "Outstanding Pokok", "DPD", "Status Rekening", "Status Akhir", "Angsuran Ke", "Kurang Setor", "Rekomendasi", "Status" })
        {
            t.Columns.Add(c);
        }

        t.Columns.Add("Tgl Penugasan", typeof(DateTime));
        foreach (var r in rows)
        {
            t.Rows.Add(r);
        }

        return t;
    }

    [Fact]
    public void Maps_columns_strips_html_and_extracts_customer_reference()
    {
        var link = "<a href=# onclick=\"PopupPage('../Popup/Detail_nasabah.aspx?ACC=123&CUST=C77',null,600);\">Lihat</a>";
        var recommend = "<a href=# onclick=\"x\">Telepon &amp; kunjungi</a>";
        var result = AccountMonitoringService.Map(Table(["*", link, "123", " BUDI ", "JAKARTA", "KPR", "1500000.5", "35", "AKSEP", "Janji", "12", "-250", recommend, "Called", new DateTime(2025, 5, 7)]));

        var row = Assert.Single(result.Rows);
        Assert.Equal("123", row.AccountNo);
        Assert.Equal("C77", row.CustomerRef);
        Assert.Equal("BUDI", row.CustomerName);
        Assert.Equal(1500000.5, row.Principal);
        Assert.Equal(35, row.Dpd);
        Assert.Equal("Telepon & kunjungi", row.Recommendation);
        Assert.True(row.HasLetterException);
        Assert.Equal(new DateTime(2025, 5, 7), row.AssignedOn);
        Assert.False(result.Truncated);
    }

    [Fact]
    public void Blank_recommendation_placeholder_becomes_null()
    {
        var result = AccountMonitoringService.Map(Table(["", "", "1", "A", "", "", "0", "0", "", "", "0", "0", "&nbsp", "", null]));
        Assert.Null(Assert.Single(result.Rows).Recommendation);
    }

    [Fact]
    public void Result_is_capped_and_marked_truncated()
    {
        var rows = Enumerable.Range(0, MonitoringRules.MaxDebitors + 5).Select(i => (object?[])["", "", i.ToString(System.Globalization.CultureInfo.InvariantCulture), "N", "", "", "0", "0", "", "", "0", "0", "", "", null]).ToArray();
        var result = AccountMonitoringService.Map(Table(rows));
        Assert.Equal(MonitoringRules.MaxDebitors, result.Rows.Count);
        Assert.True(result.Truncated);
    }
}
