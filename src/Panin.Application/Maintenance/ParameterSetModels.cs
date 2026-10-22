namespace Panin.Application.Maintenance;

/// <summary>Satu entri daftar parameter (<c>PARAMETERSYSTEM</c>), mis. "User", "Group", "Group Treatment".</summary>
public sealed record ParameterSetItem(string Title, string Link);

public interface IParameterSetService
{
    /// <summary>Daftar parameter untuk menu Maker atau Approval di set tertentu (mis. <c>USER</c>).</summary>
    Task<IReadOnlyList<ParameterSetItem>> GetItemsAsync(string setId, bool maker, CancellationToken ct = default);
}
