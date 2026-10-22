using System.Reflection;
using Microsoft.Extensions.Configuration;

namespace Panin.Web.Shell;

public sealed record ResolvedPage(Type? PageType, string? Route, bool IsLegacy, IReadOnlyDictionary<string, object?>? Parameters = null);

/// <summary>
/// Memetakan <c>MENUURL</c> ke komponen halaman. Urutan: rute Blazor langsung, lalu peta URL lama
/// (<c>Menu:LegacyUrlMap</c> di konfigurasi), lalu "belum dimigrasi".
/// </summary>
public sealed class ModulePageCatalog
{
    private readonly Dictionary<string, Type> _routes;
    private readonly Dictionary<string, string> _legacyMap;

    public ModulePageCatalog(IConfiguration configuration)
    {
        _routes = typeof(ModulePageCatalog).Assembly.GetTypes()
            .SelectMany(t => t.GetCustomAttributes<ModulePageAttribute>().Select(a => (Type: t, Attr: a)))
            .ToDictionary(x => RouteKey(x.Attr.ModuleId, x.Attr.Route), x => x.Type, StringComparer.OrdinalIgnoreCase);

        _legacyMap = configuration.GetSection("Menu:LegacyUrlMap").GetChildren()
            .Where(c => !string.IsNullOrWhiteSpace(c.Value))
            .ToDictionary(c => Normalize(c.Key.Replace("__", "/", StringComparison.Ordinal)), c => c.Value!.Trim(), StringComparer.OrdinalIgnoreCase);
    }

    public int PageCount => _routes.Count;

    /// <summary>
    /// Urutan: peta URL lama (boleh diberi awalan modul <c>70|~/url</c> untuk membedakan modul), lalu rute Blazor langsung.
    /// Target peta boleh membawa parameter komponen sebagai query string, mis. <c>/reports?SetId=GEN</c>.
    /// </summary>
    public ResolvedPage Resolve(string moduleId, string? menuUrl)
    {
        if (string.IsNullOrWhiteSpace(menuUrl))
        {
            return new ResolvedPage(null, null, false);
        }

        var key = Normalize(menuUrl);
        var target = _legacyMap.TryGetValue(moduleId + "|" + key, out var specific) ? specific
            : _legacyMap.TryGetValue(key, out var mapped) ? mapped
            : key;

        var query = target.IndexOf('?', StringComparison.Ordinal);
        var route = Normalize(query < 0 ? target : target[..query]);
        if (_routes.TryGetValue(RouteKey(moduleId, route), out var page))
        {
            return new ResolvedPage(page, route, false, query < 0 ? null : ParseQuery(target[(query + 1)..]));
        }

        // Rute Blazor yang tidak dikenal atau URL WebForms lama (~/..., *.aspx) = belum dimigrasi.
        return new ResolvedPage(null, menuUrl, true);
    }

    public Type? Home(string moduleId, string homeRoute)
        => _routes.TryGetValue(RouteKey(moduleId, Normalize(homeRoute)), out var page) ? page : null;

    private static string RouteKey(string moduleId, string route) => moduleId + "|" + Normalize(route);

    private static Dictionary<string, object?> ParseQuery(string query)
        => query.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .ToDictionary(p => Uri.UnescapeDataString(p[0]), p => (object?)(p.Length > 1 ? Uri.UnescapeDataString(p[1]) : string.Empty), StringComparer.OrdinalIgnoreCase);

    private static string Normalize(string url) => url.Trim().TrimEnd('/').ToLowerInvariant() switch
    {
        "" => "/",
        var s => s,
    };
}
