using Microsoft.Extensions.Configuration;
using Panin.Application.Collection;
using Panin.Application.Modules;
using Panin.Web.Shell;

namespace Panin.UnitTests;

public class DetailRecordTests
{
    private static DetailRecord Record(params (string Key, object? Value)[] items)
        => new(items.ToDictionary(i => i.Key, i => i.Value, StringComparer.OrdinalIgnoreCase));

    [Fact]
    public void Text_trims_values_and_formats_dates()
    {
        var r = Record(("NAMA", "  BUDI  "), ("TGL", new DateTime(2025, 5, 7)), ("KOSONG", null));
        Assert.Equal("BUDI", r.Text("nama"));
        Assert.Equal("07-May-2025", r.Text("TGL"));
        Assert.Null(r.Text("KOSONG"));
        Assert.Null(r.Text("TIDAK_ADA"));
    }

    [Fact]
    public void Account_columns_have_no_duplicates_and_cover_every_displayed_field()
    {
        var columns = DebiturDetailFields.AccountColumns.ToList();
        Assert.Equal(columns.Count, columns.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        foreach (var f in DebiturDetailFields.Customer.Concat(DebiturDetailFields.Credit).Concat(DebiturDetailFields.Arrears).Concat(DebiturDetailFields.Other))
        {
            Assert.Contains(f.Column, columns);
        }
    }

    [Fact]
    public void Account_query_selects_named_columns_instead_of_star()
    {
        // Kolom dipilih satu per satu (lihat docs/PERFORMANCE.md); SELECT * pada view 74 kolom lebih lambat.
        var sql = "select " + string.Join(", ", DebiturDetailFields.AccountColumns.Select(c => $"[{c}]"));
        Assert.DoesNotContain("*", sql, StringComparison.Ordinal);
        Assert.Contains("[CIF NO.]", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Delinquency_period_is_year_dash_two_digit_month()
        => Assert.Equal("2025-04", new DelinquencyMonth(2025, 4, "1", 0).Period);
}

public class ModuleNavigatorTests
{
    private sealed class PageA;

    private sealed class PageB;

    private sealed class PageC;

    private static ModuleWorkspace Workspace()
    {
        var ws = new ModuleWorkspace(new AccessibleModule("60", "Collection", "/collection"));
        ws.SetRoot(typeof(PageA), new Dictionary<string, object?> { ["x"] = 1 });
        return ws;
    }

    [Fact]
    public void Open_keeps_previous_page_alive_and_back_returns_to_the_same_entry()
    {
        var ws = Workspace();
        var original = ws.Current;
        var changes = 0;
        var nav = new ModuleNavigator(ws, () => changes++);

        nav.Open<PageB>(new Dictionary<string, object?> { ["AccountNo"] = "123" });

        Assert.Equal(typeof(PageB), ws.PageType);
        Assert.Equal("123", ws.Current!.Parameters["AccountNo"]);
        Assert.True(nav.CanGoBack);
        Assert.Contains(original, ws.Pages);

        nav.Back();

        Assert.Same(original, ws.Current);
        Assert.Equal(1, ws.Current!.Parameters["x"]);
        Assert.False(nav.CanGoBack);
        Assert.Equal(2, changes);
    }

    [Fact]
    public void Back_without_history_does_nothing()
    {
        var ws = Workspace();
        var nav = new ModuleNavigator(ws, () => throw new InvalidOperationException("tidak boleh berubah"));
        nav.Back();
        Assert.Equal(typeof(PageA), ws.PageType);
    }

    [Fact]
    public void Depth_is_bounded_and_oldest_page_is_dropped()
    {
        var ws = Workspace();
        var nav = new ModuleNavigator(ws, () => { });
        for (var i = 0; i < ModuleWorkspace.MaxDepth + 3; i++)
        {
            nav.Open<PageB>();
        }

        Assert.Equal(ModuleWorkspace.MaxDepth, ws.Pages.Count);
        Assert.Equal(typeof(PageB), ws.Pages[0].PageType);
    }

    [Fact]
    public void SetRoot_clears_the_stack()
    {
        var ws = Workspace();
        new ModuleNavigator(ws, () => { }).Open<PageB>();
        ws.SetRoot(typeof(PageC));
        Assert.Single(ws.Pages);
        Assert.Equal(typeof(PageC), ws.PageType);
    }
}

public class ModulePageCatalogTests
{
    private static ModulePageCatalog Catalog(params (string Key, string Value)[] legacy)
        => new(new ConfigurationBuilder().AddInMemoryCollection(legacy.ToDictionary(l => $"Menu:LegacyUrlMap:{l.Key}", l => (string?)l.Value)).Build());

    [Fact]
    public void Legacy_menu_url_resolves_to_blazor_page_for_the_same_module()
    {
        var c = Catalog(("~/controls/search_debitur.aspx", "/collection/inquiry/debitur"));
        var page = c.Resolve("60", "~/Controls/Search_Debitur.aspx");
        Assert.NotNull(page.PageType);
        Assert.Equal("/collection/inquiry/debitur", page.Route);
        Assert.False(page.IsLegacy);
    }

    [Fact]
    public void Page_of_another_module_is_not_resolved()
        => Assert.True(Catalog().Resolve("99", "/collection/inquiry/debitur").IsLegacy);

    [Fact]
    public void Unknown_url_is_reported_as_not_migrated()
    {
        var page = Catalog().Resolve("60", "~/Main/MainFacility.aspx?FUNC=news");
        Assert.Null(page.PageType);
        Assert.True(page.IsLegacy);
    }

    [Fact]
    public void Direct_blazor_route_in_menuurl_works_without_a_map()
        => Assert.NotNull(Catalog().Resolve("60", "/collection/inquiry/debitur/").PageType);
}
