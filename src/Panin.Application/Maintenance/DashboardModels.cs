namespace Panin.Application.Maintenance;

/// <summary>Jenis perubahan yang melewati maker-checker (dikelompokkan dari nama tabel).</summary>
public enum ChangeKind
{
    User,
    Group,
    Parameter,
}

public static class ChangeKinds
{
    /// <summary><c>SCALLUSER</c> = User, <c>SCALLGROUP</c> = Group, tabel lain = Parameter.</summary>
    public static ChangeKind Classify(string? tableName) => tableName?.Trim().ToUpperInvariant() switch
    {
        "SCALLUSER" => ChangeKind.User,
        "SCALLGROUP" => ChangeKind.Group,
        _ => ChangeKind.Parameter,
    };

    public static string Label(ChangeKind kind) => kind switch
    {
        ChangeKind.User => "User",
        ChangeKind.Group => "Group",
        _ => "Parameter",
    };

    /// <summary>Umur ajuan dalam bahasa sehari-hari: "5 menit", "3 jam", "2 hari".</summary>
    public static string Age(DateTime? created, DateTime now)
    {
        if (created is null)
        {
            return "-";
        }

        var span = now - created.Value;
        if (span < TimeSpan.Zero)
        {
            span = TimeSpan.Zero;
        }

        return span.TotalMinutes < 1 ? "baru saja"
            : span.TotalHours < 1 ? $"{(int)span.TotalMinutes} menit"
            : span.TotalDays < 1 ? $"{(int)span.TotalHours} jam"
            : $"{(int)span.TotalDays} hari";
    }
}

/// <summary>Satu ajuan yang menunggu persetujuan (baris <c>PARAMETERSYSTEM_TEMPORARY</c>).</summary>
public sealed record PendingChange(Guid TemporaryId, string? TableName, string? Status, string? CreatedBy, DateTime? CreatedDate)
{
    public ChangeKind Kind => ChangeKinds.Classify(TableName);
}

public sealed record PendingSummary(int Total, int Mine, IReadOnlyList<PendingChange> Oldest);

public sealed record UserCounts(int Total, int Active, int Revoked, int LoggedOn);

/// <summary>Jumlah perubahan yang sudah disetujui pada satu hari, per jenis.</summary>
public sealed record DailyChanges(DateOnly Day, int Users, int Groups, int Parameters)
{
    public int Total => Users + Groups + Parameters;
}

public sealed record DailyLogins(DateOnly Day, int Logins);

/// <summary>Satu perubahan yang sudah disetujui (baris <c>AUDITTRAIL</c>).</summary>
public sealed record AppliedChange(string? TableName, string? Status, string? CreatedBy, string? ApprovedBy, DateTime? ApprovedDate)
{
    public ChangeKind Kind => ChangeKinds.Classify(TableName);
}

/// <summary>Data dashboard Maintenance. Tiap metode independen supaya satu widget yang gagal tidak merusak yang lain.</summary>
public interface IMaintenanceDashboardService
{
    /// <summary>Antrian persetujuan; <paramref name="userId"/> menentukan hitungan "ajuan saya".</summary>
    Task<PendingSummary> GetPendingAsync(string userId, int take, CancellationToken ct = default);

    Task<UserCounts> GetUserCountsAsync(CancellationToken ct = default);

    /// <summary>Perubahan yang disetujui per hari selama <paramref name="days"/> hari terakhir (hari tanpa data berisi nol), terlama lebih dulu.</summary>
    Task<IReadOnlyList<DailyChanges>> GetChangesAsync(DateOnly today, int days, CancellationToken ct = default);

    Task<IReadOnlyList<AppliedChange>> GetRecentAsync(int take, CancellationToken ct = default);

    /// <summary>Jumlah login per hari selama <paramref name="days"/> hari terakhir (hari tanpa data berisi nol), terlama lebih dulu.</summary>
    Task<IReadOnlyList<DailyLogins>> GetLoginsAsync(DateOnly today, int days, CancellationToken ct = default);
}

/// <summary>Mengisi hari kosong supaya grafik selalu berisi deretan hari yang utuh.</summary>
public static class DashboardDays
{
    public static IReadOnlyList<DateOnly> Range(DateOnly today, int days) =>
        [.. Enumerable.Range(0, Math.Max(1, days)).Select(i => today.AddDays(i - (Math.Max(1, days) - 1)))];

    public static IReadOnlyList<DailyChanges> FillChanges(DateOnly today, int days, IEnumerable<(DateOnly Day, string? TableName, int Count)> rows)
    {
        var list = rows.ToList();
        return [.. Range(today, days).Select(d => new DailyChanges(
            d,
            list.Where(r => r.Day == d && ChangeKinds.Classify(r.TableName) == ChangeKind.User).Sum(r => r.Count),
            list.Where(r => r.Day == d && ChangeKinds.Classify(r.TableName) == ChangeKind.Group).Sum(r => r.Count),
            list.Where(r => r.Day == d && ChangeKinds.Classify(r.TableName) == ChangeKind.Parameter).Sum(r => r.Count)))];
    }

    public static IReadOnlyList<DailyLogins> FillLogins(DateOnly today, int days, IEnumerable<(DateOnly Day, int Count)> rows)
    {
        var list = rows.ToList();
        return [.. Range(today, days).Select(d => new DailyLogins(d, list.Where(r => r.Day == d).Sum(r => r.Count)))];
    }
}
