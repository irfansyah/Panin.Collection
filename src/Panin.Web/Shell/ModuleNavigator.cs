namespace Panin.Web.Shell;

/// <summary>Navigasi antarhalaman di dalam satu tab modul (tanpa mengganti URL browser, state tab modul tetap).</summary>
public interface IModuleNavigator
{
    bool CanGoBack { get; }

    /// <summary>Membuka halaman di tab modul ini dan menyimpan halaman sekarang untuk tombol Kembali.</summary>
    void Open(Type page, IDictionary<string, object?>? parameters = null);

    void Back();
}

public static class ModuleNavigatorExtensions
{
    public static void Open<TPage>(this IModuleNavigator navigator, IDictionary<string, object?>? parameters = null)
        => navigator.Open(typeof(TPage), parameters);
}

public sealed class ModuleNavigator(ModuleWorkspace workspace, Action changed) : IModuleNavigator
{
    public bool CanGoBack => workspace.Pages.Count > 1;

    public void Open(Type page, IDictionary<string, object?>? parameters = null)
    {
        workspace.Pages.Add(new PageEntry(page, parameters ?? new Dictionary<string, object?>()));
        if (workspace.Pages.Count > ModuleWorkspace.MaxDepth)
        {
            workspace.Pages.RemoveAt(0);
        }

        changed();
    }

    public void Back()
    {
        if (workspace.Pages.Count > 1)
        {
            workspace.Pages.RemoveAt(workspace.Pages.Count - 1);
            changed();
        }
    }
}
