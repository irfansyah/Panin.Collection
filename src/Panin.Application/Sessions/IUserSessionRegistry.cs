namespace Panin.Application.Sessions;

public enum SessionStartResult
{
    Started,

    /// <summary>User sudah punya sesi aktif lain (tab/perangkat lain).</summary>
    BlockedByActiveSession,
}

public sealed record ExpiredSession(string UserId, string GroupId, string SessionId, string Host);

/// <summary>
/// Satu sesi aktif per user (aturan <c>SU_LOGON</c> lama). Berada di memori satu instance server:
/// murah untuk 700+ user dan dicek tanpa menyentuh DB.
/// </summary>
public interface IUserSessionRegistry
{
    int ActiveCount { get; }

    /// <summary>Apakah user punya sesi aktif lain (selain <paramref name="sessionId"/>)?</summary>
    bool HasActiveSession(string userId, string? exceptSessionId = null);

    SessionStartResult TryStart(string userId, string groupId, string sessionId, string host);

    /// <summary>
    /// Apakah <paramref name="sessionId"/> masih sesi yang sah? Bila user tidak punya entri
    /// (mis. server direstart) sesi dipulihkan, bukan ditolak.
    /// </summary>
    bool Validate(string userId, string groupId, string sessionId, string host);

    void Touch(string userId, string sessionId);

    /// <returns>True bila sesi itu memang yang dilepas.</returns>
    bool Release(string userId, string sessionId);

    /// <summary>Melepas sesi user di server ini apa pun idnya (admin membuka kunci login). Sirkuit yang masih terbuka akan ditolak pada validasi berikutnya.</summary>
    bool ReleaseAll(string userId);

    /// <summary>Melepas sesi yang tidak ada detak dalam <paramref name="maxAge"/>.</summary>
    IReadOnlyList<ExpiredSession> ReapExpired(TimeSpan maxAge);
}
