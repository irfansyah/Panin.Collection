namespace Panin.Application.Modules;

/// <summary>Modul yang dikenal host (bagian kode), dipasangkan dengan baris <c>RFMODULE</c>.</summary>
/// <param name="Id">ModuleID di DB (60, 70, 99).</param>
/// <param name="HomeRoute">Rute halaman awal modul di Blazor.</param>
public sealed record ModuleDefinition(string Id, string Title, string HomeRoute);

/// <summary>Modul yang boleh diakses sebuah group (gabungan DB + definisi host).</summary>
public sealed record AccessibleModule(string Id, string Name, string HomeRoute);

/// <summary>Baris <c>RFMODULE</c> yang relevan untuk koneksi DATA.</summary>
public sealed record ModuleRecord(string Id, string Name, string? DbServer, string? DbName, string? DbUser, string? DbPassword, bool Active);

public sealed record MenuNode(string Id, string Text, string? Url, IReadOnlyList<MenuNode> Children)
{
    public bool IsLeaf => Children.Count == 0;
}
