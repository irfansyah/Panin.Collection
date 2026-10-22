using Panin.Application.Modules;

namespace Panin.Web.Shell;

/// <summary>Satu halaman di tumpukan tab modul. Dibandingkan dengan identitas objek (dipakai sebagai kunci render).</summary>
public sealed class PageEntry(Type? pageType, IDictionary<string, object?> parameters)
{
    public Type? PageType { get; } = pageType;

    public IDictionary<string, object?> Parameters { get; } = parameters;
}

/// <summary>State satu tab modul. Tetap hidup selama sirkuit, sehingga pindah tab tidak menghilangkan halaman.</summary>
public sealed class ModuleWorkspace(AccessibleModule module)
{
    public AccessibleModule Module { get; } = module;

    public IReadOnlyList<MenuNode>? Menu { get; set; }

    /// <summary>
    /// Tumpukan halaman tab ini. Halaman di bawah teratas tetap hidup (disembunyikan) sehingga Kembali mengembalikan
    /// halaman persis seperti ditinggalkan, mis. hasil pencarian. Kedalaman dibatasi agar memori sirkuit terjaga.
    /// </summary>
    public List<PageEntry> Pages { get; } = [];

    public const int MaxDepth = 4;

    public PageEntry? Current => Pages.Count > 0 ? Pages[^1] : null;

    public Type? PageType => Current?.PageType;

    /// <summary>Mengganti seluruh tumpukan dengan satu halaman (pilihan menu / halaman awal modul).</summary>
    public void SetRoot(Type? page, IDictionary<string, object?>? parameters = null)
    {
        Pages.Clear();
        Pages.Add(new PageEntry(page, parameters ?? new Dictionary<string, object?>()));
    }

    public IModuleNavigator Navigator { get; set; } = null!;

    public string? ActiveMenuId { get; set; }

    public HashSet<string> Expanded { get; } = new(StringComparer.Ordinal);
}

/// <summary>State shell per sirkuit: modul yang pernah dibuka dan modul aktif.</summary>
public sealed class WorkspaceState
{
    private readonly Dictionary<string, ModuleWorkspace> _opened = new(StringComparer.Ordinal);

    public event Action? Changed;

    public string? ActiveModuleId { get; private set; }

    /// <summary>Cabang/area/kota user (dari DB DATA), dipakai modul untuk membatasi data.</summary>
    public Panin.Application.Status.UserArea? Area { get; set; }

    public IReadOnlyCollection<ModuleWorkspace> Opened => _opened.Values;

    public ModuleWorkspace? Active => ActiveModuleId is not null && _opened.TryGetValue(ActiveModuleId, out var w) ? w : null;

    /// <returns>Workspace baru bila modul belum pernah dibuka, selain itu null.</returns>
    public ModuleWorkspace? Activate(AccessibleModule module)
    {
        ModuleWorkspace? created = null;
        if (!_opened.ContainsKey(module.Id))
        {
            created = new ModuleWorkspace(module);
            created.Navigator = new ModuleNavigator(created, Notify);
            _opened[module.Id] = created;
        }

        ActiveModuleId = module.Id;
        Changed?.Invoke();
        return created;
    }

    public void Notify() => Changed?.Invoke();
}
