using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Panin.Application.Memos;
using Panin.Infrastructure.Memos;
using Panin.Web.Shell;

namespace Panin.UnitTests;

public class MemoRulesTests
{
    [Theory]
    [InlineData("skpm", MemoKind.Skpm)]
    [InlineData("hapus-buku", MemoKind.HapusBuku)]
    [InlineData("HAPUS-TAGIH", MemoKind.HapusTagih)]
    [InlineData("restrukturisasi", MemoKind.Restrukturisasi)]
    [InlineData("ayda", MemoKind.Ayda)]
    public void Slugs_map_to_kinds_and_back(string slug, MemoKind expected)
    {
        Assert.True(MemoRules.TryParseSlug(slug, out var kind));
        Assert.Equal(expected, kind);
        Assert.Equal(slug.ToLowerInvariant(), MemoRules.Slug(kind));
    }

    [Theory]
    [InlineData("")]
    [InlineData("../etc")]
    [InlineData("memo")]
    public void Unknown_slugs_are_rejected(string slug) => Assert.False(MemoRules.TryParseSlug(slug, out _));

    [Theory]
    [InlineData("100136000331", true)]
    [InlineData("A1b2", true)]
    [InlineData("", false)]
    [InlineData("1 OR 1=1", false)]
    [InlineData("12'; DROP TABLE X;--", false)]
    [InlineData("../../x", false)]
    public void Account_numbers_are_restricted(string account, bool ok) => Assert.Equal(ok, MemoRules.IsValidAccount(account));

    [Fact]
    public void Each_memo_has_its_own_menu_id()
        => Assert.Equal(["202010", "202020", "202030", "202040", "202050"], Enum.GetValues<MemoKind>().Select(MemoRules.MenuId));
}

public class MemoRendererTests
{
    [Fact]
    public void Tokens_are_replaced_encoded_and_unknown_ones_become_empty()
    {
        var html = MemoRenderer.Render("<p>{{lblNama}} / {{lblKosong}} / {{tidakAda}}</p>", new Dictionary<string, string?>
        {
            ["lblNama"] = "<b>BUDI & ANI</b>",
            ["lblKosong"] = null,
        });

        Assert.Equal("<p>&lt;b&gt;BUDI &amp; ANI&lt;/b&gt; /  / </p>", html);
    }

    [Fact]
    public void Plain_text_is_untouched() => Assert.Equal("a { b } c {{", MemoRenderer.Render("a { b } c {{", new Dictionary<string, string?>()));
}

public class MemoTemplateTests
{
    private static string TemplateDir
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Panin.Web", "Templates", "Memo")))
            {
                dir = dir.Parent;
            }

            return Path.Combine(dir!.FullName, "src", "Panin.Web", "Templates", "Memo");
        }
    }

    [Fact]
    public void Every_mapped_token_exists_in_its_template_and_nothing_server_side_is_left()
    {
        foreach (var def in MemoCatalog.All)
        {
            var text = File.ReadAllText(Path.Combine(TemplateDir, def.Template));
            foreach (var token in def.Map.Keys)
            {
                Assert.Contains("{{" + token + "}}", text, StringComparison.Ordinal);
            }

            Assert.DoesNotContain("runat=", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("<asp:", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("<%", text, StringComparison.Ordinal);
            Assert.DoesNotContain("<script", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Templates_keep_the_old_structure()
    {
        var restruct = File.ReadAllText(Path.Combine(TemplateDir, "restrukturisasi.html"));
        Assert.Contains("Penurunan Suku Bunga", restruct, StringComparison.Ordinal);
        Assert.Equal(6, Regex.Matches(restruct, "<input type=\"checkbox\"").Count);

        Assert.Contains("Memorandum Permohonan Hapus Buku", File.ReadAllText(Path.Combine(TemplateDir, "hapus-buku.html")), StringComparison.Ordinal);
    }
}

public class MemoMenuTests
{
    [Theory]
    [InlineData("~/Controls/CariSKPM.aspx", "skpm")]
    [InlineData("~/Controls/CariHapusBuku.aspx", "hapus-buku")]
    [InlineData("~/Controls/CariAYDA.aspx", "ayda")]
    public void Memo_menus_resolve_to_the_search_page_with_the_kind(string url, string kind)
    {
        var c = new ModulePageCatalog(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Menu:LegacyUrlMap:~/controls/cariskpm.aspx"] = "/collection/memo?Kind=skpm",
            ["Menu:LegacyUrlMap:~/controls/carihapusbuku.aspx"] = "/collection/memo?Kind=hapus-buku",
            ["Menu:LegacyUrlMap:~/controls/cariayda.aspx"] = "/collection/memo?Kind=ayda",
        }).Build());

        var page = c.Resolve("60", url);

        Assert.NotNull(page.PageType);
        Assert.Equal(kind, page.Parameters!["Kind"]);
    }
}
