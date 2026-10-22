namespace Panin.Web.Shell;

/// <summary>
/// Menandai komponen sebagai halaman sebuah modul. <see cref="Route"/> adalah nilai yang disimpan di
/// <c>RFMENUX.MENUURL</c> (mis. <c>/collection/worklist</c>). Halaman dirender di area konten shell,
/// bukan lewat router URL, sehingga state tab modul dipertahankan.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class ModulePageAttribute(string moduleId, string route) : Attribute
{
    public string ModuleId { get; } = moduleId;

    public string Route { get; } = route;
}
