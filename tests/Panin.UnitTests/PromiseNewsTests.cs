using System.Data;
using Microsoft.Extensions.Configuration;
using Panin.Application.Collection;
using Panin.Infrastructure.Collection;
using Panin.Infrastructure.Data;
using Panin.Web.Shell;

namespace Panin.UnitTests;

public class PromiseTests
{
    [Theory]
    [InlineData("2026-10-01", "2026-10-31", null)]
    [InlineData("2026-10-31", "2026-10-01", "setelah")]
    [InlineData("2025-01-01", "2026-10-01", "maksimal")]
    public void Date_range_is_validated(string from, string to, string? messagePart)
    {
        var error = PromiseRules.Validate(DateTime.Parse(from, System.Globalization.CultureInfo.InvariantCulture), DateTime.Parse(to, System.Globalization.CultureInfo.InvariantCulture));
        if (messagePart is null)
        {
            Assert.Null(error);
        }
        else
        {
            Assert.Contains(messagePart, error, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Procedure_output_is_mapped_without_the_html_history_link()
    {
        var t = new DataTable();
        foreach (var c in new[] { "Aktivitas", "Tgl Janji", "Waktu Janji", "Janji Debitur", "No Rek", "Nama", "Produk", "DPD", "Kol", "Aktivitas Terakhir", "Officer", "Riwayat" })
        {
            t.Columns.Add(c, c == "Tgl Janji" ? typeof(DateTime) : c == "DPD" ? typeof(int) : typeof(string));
        }

        t.Rows.Add("Call", new DateTime(2026, 10, 1), "00:00:00", "Janji bayar", "123", " ANI ", "KPR", 35, "2", "NULL", "BUDI", "<a href=#>Lihat</a>");
        t.Rows.Add("Visit", new DateTime(2026, 10, 2), "14:30:00", "Akan Setor Tgl", "456", "ITA", "KPR", DBNull.Value, "1", "call", "BUDI", "");

        var result = PromiseService.Map(t);

        Assert.True(result.Success);
        Assert.Equal(("Call", "ANI", 35), (result.Rows[0].Activity, result.Rows[0].CustomerName, result.Rows[0].Dpd));
        Assert.Null(result.Rows[0].PromiseTime);
        Assert.Null(result.Rows[0].LastAction);
        Assert.Equal("14:30:00", result.Rows[1].PromiseTime);
        Assert.Null(result.Rows[1].Dpd);
    }

    [Fact]
    public void Procedure_is_allowed_by_the_read_only_guard()
    {
        var db = new ReadOnlyDb(null!, new HashSet<string>(["SP_PTP_SEARCH"], StringComparer.OrdinalIgnoreCase), "t");
        Assert.True(db.Check("exec SP_PTP_SEARCH @1, @2, @3, @4, @5"));
        Assert.True(db.Check(PromiseService.ResultsSql));
        Assert.True(db.Check(NewsService.Sql));
    }
}

public class NewsHtmlTests
{
    [Theory]
    [InlineData("<p>Halo <script>alert(1)</script>dunia</p>", "script")]
    [InlineData("<img src=\"x\" onerror=\"alert(1)\">", "onerror")]
    [InlineData("<a href=\"javascript:alert(1)\">klik</a>", "javascript:")]
    [InlineData("<div onclick=\"x()\">a</div>", "onclick")]
    [InlineData("<iframe src=\"http://evil\"></iframe>", "iframe")]
    [InlineData("<form action=\"http://evil\"><input name=\"p\"></form>", "<form")]
    [InlineData("<style>body{display:none}</style>x", "<style")]
    public void Dangerous_markup_is_removed(string html, string forbidden)
        => Assert.DoesNotContain(forbidden, NewsService.Clean(html), StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void Ordinary_formatting_survives()
    {
        var clean = NewsService.Clean("<p><b>Penting</b> <a href=\"https://panin.example/x\">tautan</a></p><ul><li>satu</li></ul>");
        Assert.Contains("<b>Penting</b>", clean, StringComparison.Ordinal);
        Assert.Contains("href=\"https://panin.example/x\"", clean, StringComparison.Ordinal);
        Assert.Contains("<li>satu</li>", clean, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("<img src=\"../Image/News/a.jpg\">")]
    [InlineData("<img src=\"~/Image/News/a.jpg\">")]
    [InlineData("<img src=\"/Image/News/a.jpg\">")]
    public void Old_image_paths_point_to_the_new_static_folder(string html)
        => Assert.Contains("src=\"/Image/News/a.jpg\"", NewsService.Clean(html), StringComparison.Ordinal);

    [Fact]
    public void Blank_content_gives_blank_html() => Assert.Equal(string.Empty, NewsService.Clean("  "));

    [Fact]
    public void Row_without_subject_gets_a_placeholder()
    {
        var t = new DataTable();
        t.Columns.Add("NEWSID", typeof(string));
        t.Columns.Add("NEWS_SUBJECT", typeof(string));
        t.Columns.Add("NEWS_CONTENT", typeof(string));
        t.Columns.Add("NEWS_DATE", typeof(DateTime));
        t.Rows.Add("1", DBNull.Value, "<p>x</p>", new DateTime(2026, 1, 1));

        var item = NewsService.Map(t.Rows[0]);

        Assert.Equal("(tanpa judul)", item.Subject);
        Assert.Contains("<p>x</p>", item.Html, StringComparison.Ordinal);
    }
}

public class PromiseNewsMenuTests
{
    [Fact]
    public void News_view_menu_resolves_to_the_news_page()
    {
        var c = new ModulePageCatalog(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Menu:LegacyUrlMap:~/main/mainfacility.aspx?func=news&status=view"] = "/collection/news",
        }).Build());

        var page = c.Resolve("60", "~/Main/MainFacility.aspx?FUNC=news&status=view");

        Assert.NotNull(page.PageType);
        Assert.False(page.IsLegacy);
    }
}

public class ChangePasswordMenuTests
{
    [Theory]
    [InlineData("60")]
    [InlineData("70")]
    [InlineData("99")]
    public void Change_password_menu_resolves_in_every_module(string module)
    {
        var c = new ModulePageCatalog(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Menu:LegacyUrlMap:~/changepassword.aspx"] = "/account/password",
        }).Build());

        var page = c.Resolve(module, "~/ChangePassword.aspx");

        Assert.NotNull(page.PageType);
        Assert.False(page.IsLegacy);
    }
}
