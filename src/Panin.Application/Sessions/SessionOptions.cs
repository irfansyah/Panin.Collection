namespace Panin.Application.Sessions;

/// <summary>Konfigurasi sesi (bagian <c>Session</c> di appsettings).</summary>
public sealed class SessionOptions
{
    public const string SectionName = "Session";

    /// <summary>Interval detak klien ke server (menyegarkan sesi dan <c>SU_MONITORACTIVITY</c>).</summary>
    public int HeartbeatSeconds { get; set; } = 60;

    /// <summary>Tanpa detak selama ini, sesi dianggap mati dan dilepas (memungkinkan login ulang).</summary>
    public int AbandonedAfterSeconds { get; set; } = 180;

    /// <summary>Peringatan idle (tanpa gerakan user) setelah sekian menit; di sistem lama 20 menit.</summary>
    public int IdleWarningMinutes { get; set; } = 20;

    /// <summary>Logout otomatis setelah sekian menit idle sejak peringatan muncul.</summary>
    public int IdleLogoutAfterWarningMinutes { get; set; } = 5;
}
