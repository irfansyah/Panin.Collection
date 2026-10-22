using System.Text.RegularExpressions;

namespace Panin.Application.Maintenance;

public sealed record GroupRow(string GroupId, string? Name, bool Active, int UserCount);

public sealed record PendingGroupRow(Guid TemporaryId, string Status, string? CreatedBy, string? GroupId, string? Name, bool? Active)
{
    public string? Changes { get; init; }
}

public sealed class GroupDraft
{
    public string GroupId { get; set; } = string.Empty;

    public string? Name { get; set; }

    public bool Active { get; set; } = true;

    /// <summary>True bila mengubah group yang sudah ada (ID tidak boleh diubah).</summary>
    public bool IsEdit { get; set; }
}

public sealed record ModuleItem(string ModuleId, string Name);

/// <summary>Satu menu modul beserta apakah group boleh mengaksesnya (<c>GRPMENUX</c>).</summary>
public sealed record MenuAccessItem(string MenuId, string Text, string? ParentId, int Position, bool Allowed);

public static partial class GroupRules
{
    public const int MaxGroupId = 20;
    public const int MaxName = 150;

    [GeneratedRegex("^[A-Za-z0-9._-]+$")]
    private static partial Regex IdPattern();

    public static IReadOnlyList<string> Validate(GroupDraft d)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(d.GroupId))
        {
            errors.Add("Group ID wajib diisi");
        }
        else if (d.GroupId.Trim().Length > MaxGroupId || !IdPattern().IsMatch(d.GroupId.Trim()))
        {
            errors.Add($"Group ID hanya huruf, angka, titik, garis bawah, atau strip, maksimal {MaxGroupId} karakter");
        }

        if (string.IsNullOrWhiteSpace(d.Name))
        {
            errors.Add("Nama group wajib diisi");
        }
        else if (d.Name.Trim().Length > MaxName)
        {
            errors.Add($"Nama group maksimal {MaxName} karakter");
        }

        return errors;
    }

    /// <summary>
    /// Menu yang akan disimpan: pilihan user ditambah seluruh induknya (layar lama menambahkan induk yang belum ada),
    /// hanya dari menu yang memang ada di modul itu.
    /// </summary>
    public static IReadOnlySet<string> WithAncestors(IEnumerable<string> selected, IReadOnlyList<MenuAccessItem> allMenus)
    {
        var byId = allMenus.ToDictionary(m => m.MenuId, StringComparer.Ordinal);
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in selected)
        {
            var current = id;
            while (current is not null && byId.TryGetValue(current, out var menu) && result.Add(menu.MenuId))
            {
                current = menu.ParentId;
            }
        }

        return result;
    }
}

public interface IGroupMaintenanceService
{
    Task<bool> HasRoleAsync(string groupId, MaintenanceRole role, CancellationToken ct = default);

    Task<IReadOnlyList<GroupRow>> GetGroupsAsync(CancellationToken ct = default);

    Task<IReadOnlyList<PendingGroupRow>> GetPendingAsync(CancellationToken ct = default);

    Task<MaintenanceResult> SavePendingAsync(string makerUserId, GroupDraft draft, CancellationToken ct = default);

    Task<MaintenanceResult> DeleteToPendingAsync(string makerUserId, string makerGroupId, string groupId, CancellationToken ct = default);

    Task<MaintenanceResult> CancelPendingAsync(string makerUserId, Guid temporaryId, CancellationToken ct = default);

    Task<MaintenanceResult> ApproveAsync(string approverUserId, IReadOnlyList<Guid> temporaryIds, CancellationToken ct = default);

    Task<MaintenanceResult> RejectAsync(string approverUserId, IReadOnlyList<Guid> temporaryIds, CancellationToken ct = default);

    Task<IReadOnlyList<ModuleItem>> GetModulesAsync(CancellationToken ct = default);

    /// <summary>Semua menu modul dengan tanda akses untuk group ini.</summary>
    Task<IReadOnlyList<MenuAccessItem>> GetMenuAccessAsync(string groupId, string moduleId, CancellationToken ct = default);

    /// <summary>
    /// Mengganti akses menu group untuk satu modul, atomik. Layar lama menyimpannya langsung (tanpa approval); perilaku itu
    /// dipertahankan, tetapi hanya untuk Maker dan tercatat di log. Mencabut semua menu modul berarti group kehilangan akses ke modul itu.
    /// </summary>
    Task<MaintenanceResult> SaveMenuAccessAsync(string makerUserId, string makerGroupId, string groupId, string moduleId, IReadOnlyList<string> menuIds, CancellationToken ct = default);
}
