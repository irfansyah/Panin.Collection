using Panin.Application.Modules;

namespace Panin.Web.Shell;

/// <summary>State satu tab modul. Tetap hidup selama sirkuit, sehingga pindah tab tidak menghilangkan halaman.</summary>
public sealed class ModuleWorkspace(AccessibleModule module)
{
    public AccessibleModule Module { get; } = module;

    public IReadOnlyList<MenuNode>? Menu { get; set; }

    public Type? PageType { get; set; }

    public IDictionary<string, object?> PageParameters { get; } = new Dictionary<string, object?>();

    public string? ActiveMenuId { get; set; }

    public HashSet<string> Expanded { get; } = new(StringComparer.Ordinal);
}

/// <summary>State shell per sirkuit: modul yang pernah dibuka dan modul aktif.</summary>
public sealed class WorkspaceState
{
    private readonly Dictionary<string, ModuleWorkspace> _opened = new(StringComparer.Ordinal);

    public event Action? Changed;

    public string? ActiveModuleId { get; private set; }

    public IReadOnlyCollection<ModuleWorkspace> Opened => _opened.Values;

    public ModuleWorkspace? Active => ActiveModuleId is not null && _opened.TryGetValue(ActiveModuleId, out var w) ? w : null;

    /// <returns>Workspace baru bila modul belum pernah dibuka, selain itu null.</returns>
    public ModuleWorkspace? Activate(AccessibleModule module)
    {
        ModuleWorkspace? created = null;
        if (!_opened.ContainsKey(module.Id))
        {
            created = new ModuleWorkspace(module);
            _opened[module.Id] = created;
        }

        ActiveModuleId = module.Id;
        Changed?.Invoke();
        return created;
    }

    public void Notify() => Changed?.Invoke();
}
