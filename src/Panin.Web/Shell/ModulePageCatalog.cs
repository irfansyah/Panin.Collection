using System.Reflection;
using Microsoft.Extensions.Configuration;

namespace Panin.Web.Shell;

public sealed record ResolvedPage(Type? PageType, string? Route, bool IsLegacy);

/// <summary>
/// Memetakan <c>MENUURL</c> ke komponen halaman. Urutan: rute Blazor langsung, lalu peta URL lama
/// (<c>Menu:LegacyUrlMap</c> di konfigurasi), lalu "belum dimigrasi".
/// </summary>
public sealed class ModulePageCatalog
{
    private readonly Dictionary<string, (Type Type, string ModuleId)> _routes;
    private readonly Dictionary<string, string> _legacyMap;

    public ModulePageCatalog(IConfiguration configuration)
    {
        _routes = typeof(ModulePageCatalog).Assembly.GetTypes()
            .Select(t => (Type: t, Attr: t.GetCustomAttribute<ModulePageAttribute>()))
            .Where(x => x.Attr is not null)
            .ToDictionary(x => Normalize(x.Attr!.Route), x => (x.Type, x.Attr!.ModuleId), StringComparer.OrdinalIgnoreCase);

        _legacyMap = configuration.GetSection("Menu:LegacyUrlMap").GetChildren()
            .Where(c => !string.IsNullOrWhiteSpace(c.Value))
            .ToDictionary(c => Normalize(c.Key.Replace("__", "/", StringComparison.Ordinal)), c => Normalize(c.Value!), StringComparer.OrdinalIgnoreCase);
    }

    public int PageCount => _routes.Count;

    public ResolvedPage Resolve(string moduleId, string? menuUrl)
    {
        if (string.IsNullOrWhiteSpace(menuUrl))
        {
            return new ResolvedPage(null, null, false);
        }

        var key = Normalize(menuUrl);
        if (_legacyMap.TryGetValue(key, out var mapped))
        {
            key = mapped;
        }

        if (_routes.TryGetValue(key, out var page) && page.ModuleId == moduleId)
        {
            return new ResolvedPage(page.Type, key, false);
        }

        // Rute Blazor yang tidak dikenal atau URL WebForms lama (~/..., *.aspx) = belum dimigrasi.
        return new ResolvedPage(null, menuUrl, true);
    }

    public Type? Home(string moduleId, string homeRoute)
        => _routes.TryGetValue(Normalize(homeRoute), out var page) && page.ModuleId == moduleId ? page.Type : null;

    private static string Normalize(string url) => url.Trim().TrimEnd('/').ToLowerInvariant() switch
    {
        "" => "/",
        var s => s,
    };
}
