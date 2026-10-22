namespace Panin.Application.Auth;

public interface IAuthService
{
    /// <summary>Memvalidasi login (port <c>ValidateLogin</c>) dan, bila berhasil, memulai sesi tunggal.</summary>
    Task<LoginOutcome> LoginAsync(string userId, string password, string host, CancellationToken ct = default);

    /// <summary>Detak sesi: menyegarkan registry dan <c>SU_MONITORACTIVITY</c>. False bila baris aktivitas user tidak ada.</summary>
    Task<bool> HeartbeatAsync(string userId, string sessionId, string host, CancellationToken ct = default);

    /// <summary>Mencatat logout di DB tanpa menyentuh registry (dipakai saat sesi dilepas karena tidak ada detak).</summary>
    Task RecordLogoutAsync(string userId, string groupId, string host, CancellationToken ct = default);

    /// <summary>Mencatat logout (<c>SU_ALLUSERACTIVITY</c>) dan melepas sesi.</summary>
    Task LogoutAsync(string userId, string groupId, string sessionId, string host, CancellationToken ct = default);

    /// <summary>
    /// Ganti password. <paramref name="oldPassword"/> null berarti password awal (tanpa verifikasi password lama;
    /// pemanggil sudah membuktikan identitas lewat tiket login).
    /// </summary>
    Task<PasswordChangeOutcome> ChangePasswordAsync(string userId, string? oldPassword, string newPassword, string verifyPassword, CancellationToken ct = default);

    /// <summary>Data pengguna untuk header/footer (<c>VW_SESSIONLOS</c>).</summary>
    Task<AuthenticatedUser?> GetUserAsync(string userId, CancellationToken ct = default);
}

/// <summary>Antrean pencatatan logout ke DB di latar belakang.</summary>
public interface ILogoutRecorder
{
    void Enqueue(string userId, string groupId, string host);
}
