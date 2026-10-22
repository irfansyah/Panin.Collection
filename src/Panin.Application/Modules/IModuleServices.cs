using Panin.Application.Abstractions;

namespace Panin.Application.Modules;

public interface IModuleRegistry
{
    /// <summary>Definisi modul yang diimplementasi host ini.</summary>
    IReadOnlyList<ModuleDefinition> Definitions { get; }

    /// <summary>Modul yang boleh diakses <paramref name="groupId"/>: ada di DB, aktif, dan sudah ada di host.</summary>
    Task<IReadOnlyList<AccessibleModule>> GetAccessibleAsync(string groupId, CancellationToken ct = default);

    Task<ModuleRecord?> GetRecordAsync(string moduleId, CancellationToken ct = default);
}

public interface IModuleDbFactory
{
    /// <summary>
    /// <see cref="IDb"/> untuk DB DATA modul. Prioritas: <c>ConnectionStrings:Data:{moduleId}</c>
    /// (secrets), lalu kolom <c>DB_*</c> di <c>RFMODULE</c> (paritas dengan sistem lama).
    /// </summary>
    Task<IDb> GetDataDbAsync(string moduleId, CancellationToken ct = default);
}

public interface IMenuService
{
    /// <summary>Menu modul untuk group (<c>RFMENUX</c> x <c>GRPMENUX</c>), sudah berbentuk pohon dan di-cache.</summary>
    Task<IReadOnlyList<MenuNode>> GetMenuAsync(string moduleId, string groupId, CancellationToken ct = default);
}
