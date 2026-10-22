using System.Text.RegularExpressions;
using Panin.Application.Collection;

namespace Panin.Application.Maintenance;

public enum MaintenanceRole
{
    /// <summary>Menu "Maker" (RFMENUX 201010): mengajukan perubahan user.</summary>
    Maker,

    /// <summary>Menu "Approval" (RFMENUX 201020): menyetujui atau menolak.</summary>
    Approver,
}

public sealed record UserRow(
    string UserId,
    string? GroupId,
    string? GroupName,
    string? FullName,
    string? Phone,
    string? Email,
    string? Upliner,
    string? BranchCode,
    bool? Active,
    bool LoggedOn,
    bool Revoked);

public sealed record PendingUserRow(
    Guid TemporaryId,
    string Status,
    string? CreatedBy,
    string? UserId,
    string? GroupId,
    string? GroupName,
    string? FullName,
    string? Phone,
    string? Upliner,
    string? BranchCode,
    bool? Active)
{
    /// <summary>Ringkasan perubahan untuk approver, mis. "Group: A → B" (hanya untuk ajuan Update).</summary>
    public string? Changes { get; init; }
}

/// <summary>Isian form user; kolom <c>SU_PWD</c> tidak pernah ikut (user baru diberi password awal oleh <c>SP_AUDITTRAIL</c> saat disetujui).</summary>
public sealed class UserDraft
{
    public string UserId { get; set; } = string.Empty;

    public string? GroupId { get; set; }

    public string? BranchCode { get; set; }

    public string? FullName { get; set; }

    public string? Phone { get; set; }

    public string? Email { get; set; }

    public string? Upliner { get; set; }

    public bool Active { get; set; } = true;

    /// <summary>True bila mengubah user yang sudah ada (ID tidak boleh diubah).</summary>
    public bool IsEdit { get; set; }
}

public sealed record MaintenanceResult(bool Success, IReadOnlyList<string> Errors)
{
    public static MaintenanceResult Ok { get; } = new(true, []);

    public static MaintenanceResult Fail(params string[] errors) => new(false, errors);
}

public static partial class UserRules
{
    public const int MaxUserId = 20;

    [GeneratedRegex("^[A-Za-z0-9._-]+$")]
    private static partial Regex IdPattern();

    /// <summary>Validasi yang tidak membutuhkan DB; batas panjang mengikuti kolom <c>SCALLUSER</c>.</summary>
    public static IReadOnlyList<string> Validate(UserDraft d)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(d.UserId))
        {
            errors.Add("User ID wajib diisi");
        }
        else if (d.UserId.Trim().Length > MaxUserId || !IdPattern().IsMatch(d.UserId.Trim()))
        {
            errors.Add($"User ID hanya huruf, angka, titik, garis bawah, atau strip, maksimal {MaxUserId} karakter");
        }

        if (string.IsNullOrWhiteSpace(d.GroupId))
        {
            errors.Add("Group wajib diisi");
        }

        if (string.IsNullOrWhiteSpace(d.FullName))
        {
            errors.Add("Nama lengkap wajib diisi");
        }

        Max(errors, d.FullName, 300, "Nama lengkap");
        Max(errors, d.Phone, 15, "No. HP");
        Max(errors, d.Email, 50, "Email");
        Max(errors, d.Upliner, 20, "Upliner");
        Max(errors, d.BranchCode, 20, "Cabang");

        if (!string.IsNullOrWhiteSpace(d.Email) && !d.Email.Contains('@', StringComparison.Ordinal))
        {
            errors.Add("Email tidak valid");
        }

        if (!string.IsNullOrWhiteSpace(d.Upliner) && string.Equals(d.Upliner.Trim(), d.UserId?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("Recursive Upliner !");
        }

        return errors;
    }

    private static void Max(List<string> errors, string? value, int max, string label)
    {
        if (value is not null && value.Trim().Length > max)
        {
            errors.Add($"{label} maksimal {max} karakter");
        }
    }
}

public interface IUserMaintenanceService
{
    /// <summary>Apakah group ini punya menu maker/approval Maintenance (hak yang sama dengan menu di layar lama)?</summary>
    Task<bool> HasRoleAsync(string groupId, MaintenanceRole role, CancellationToken ct = default);

    Task<IReadOnlyList<UserRow>> GetUsersAsync(CancellationToken ct = default);

    Task<UserRow?> GetUserAsync(string userId, CancellationToken ct = default);

    Task<IReadOnlyList<PendingUserRow>> GetPendingAsync(CancellationToken ct = default);

    Task<IReadOnlyList<LookupItem>> GetGroupsAsync(CancellationToken ct = default);

    Task<IReadOnlyList<LookupItem>> GetBranchesAsync(CancellationToken ct = default);

    /// <summary>Simpan ajuan tambah/ubah user ke daftar pending (belum berlaku sampai disetujui).</summary>
    Task<MaintenanceResult> SavePendingAsync(string makerUserId, UserDraft draft, CancellationToken ct = default);

    /// <summary>Ajukan penghapusan user.</summary>
    Task<MaintenanceResult> DeleteToPendingAsync(string makerUserId, string userId, CancellationToken ct = default);

    /// <summary>Batalkan ajuan sendiri.</summary>
    Task<MaintenanceResult> CancelPendingAsync(string makerUserId, Guid temporaryId, CancellationToken ct = default);

    Task<MaintenanceResult> ApproveAsync(string approverUserId, IReadOnlyList<Guid> temporaryIds, CancellationToken ct = default);

    Task<MaintenanceResult> RejectAsync(string approverUserId, IReadOnlyList<Guid> temporaryIds, CancellationToken ct = default);

    /// <summary>Buka kunci "sedang login" (<c>SU_LOGON = 0</c>) dan lepas sesi di server ini.</summary>
    Task<MaintenanceResult> UnlockLoginAsync(string actorUserId, string userId, CancellationToken ct = default);

    /// <summary>Cabut status revoke akibat salah password berulang (<c>SU_REVOKE = 0</c>).</summary>
    Task<MaintenanceResult> ClearRevokeAsync(string actorUserId, string userId, CancellationToken ct = default);

    /// <summary>Kembalikan password ke password awal (<c>LOGINPARAM.DEF_PWD</c>).</summary>
    Task<MaintenanceResult> ResetPasswordAsync(string actorUserId, string userId, CancellationToken ct = default);
}
