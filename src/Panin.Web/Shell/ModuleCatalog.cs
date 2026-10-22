using Panin.Application.Modules;

namespace Panin.Web.Shell;

/// <summary>Modul yang diimplementasikan host ini (ModuleID mengikuti <c>RFMODULE</c>).</summary>
public static class ModuleCatalog
{
    public static readonly IReadOnlyList<ModuleDefinition> Definitions =
    [
        new("60", "Collection Management", "/collection"),
        new("70", "MIS Report", "/mis"),
        new("99", "Maintenance", "/maintenance"),
    ];
}
