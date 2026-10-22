namespace Panin.Application.Collection;

/// <summary>Anggota tim di bawah seorang supervisor (layar "User-user dibawah pengawasan ...").</summary>
public sealed class MonitoringTeamMember
{
    public string UserId { get; set; } = string.Empty;

    public string? Name { get; set; }

    public string? Group { get; set; }

    public int DebitorCount { get; set; }

    public double? Outstanding { get; set; }

    public double Target { get; set; }

    public double Achievement { get; set; }

    public string? Branch { get; set; }
}

public sealed class MonitoringDebitorRow
{
    public string AccountNo { get; set; } = string.Empty;

    public string? CustomerRef { get; set; }

    public string? CustomerName { get; set; }

    public string? City { get; set; }

    public string? Facility { get; set; }

    public double? Principal { get; set; }

    public int? Dpd { get; set; }

    public string? AccountStatus { get; set; }

    public string? LastAction { get; set; }

    public int? InstallmentNo { get; set; }

    public double? ShortPayment { get; set; }

    public string? Recommendation { get; set; }

    public DateTime? AssignedOn { get; set; }

    /// <summary>Status call/visit hari ini, bila ada.</summary>
    public string? TodayStatus { get; set; }

    /// <summary>Rekening yang punya surat pengecualian (tanda * di layar lama).</summary>
    public bool HasLetterException { get; set; }
}

public sealed record MonitoringDebitorResult(IReadOnlyList<MonitoringDebitorRow> Rows, bool Truncated, int MaxRows);

public static class MonitoringRules
{
    /// <summary>Supervisor teratas dapat mengelola belasan ribu debitur; yang ditampilkan dibatasi (lihat docs/PERFORMANCE.md).</summary>
    public const int MaxDebitors = 2000;
}

public interface IAccountMonitoringService
{
    /// <summary>
    /// Boleh melihat data <paramref name="targetUserId"/>? Diri sendiri atau bawahan (<c>TEMP_DOWNLINE</c> / langsung).
    /// Layar lama mempercayai field tersembunyi dari browser; di sini selalu diperiksa di server.
    /// </summary>
    Task<bool> CanViewAsync(string viewerUserId, string targetUserId, CancellationToken ct = default);

    Task<IReadOnlyList<MonitoringTeamMember>> GetTeamAsync(string userId, CancellationToken ct = default);

    /// <summary>Target dan pencapaian bulan ini (<c>SP_GET_TARGET_ACHIEVMENT</c>).</summary>
    Task<DetailRecord?> GetTargetAsync(string userId, CancellationToken ct = default);

    /// <summary>Total outstanding (<c>SP_GET_OS</c>).</summary>
    Task<string?> GetOutstandingAsync(string userId, CancellationToken ct = default);

    /// <param name="touched">False = Pending, true = Touched (sudah ditindaklanjuti).</param>
    Task<MonitoringDebitorResult> GetDebitorsAsync(string userId, bool touched, CancellationToken ct = default);
}
