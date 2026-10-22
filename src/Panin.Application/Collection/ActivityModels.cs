namespace Panin.Application.Collection;

public enum ActivityKind
{
    All = 0,
    Call = 1,
    Letter = 2,
    Visit = 3,
}

/// <summary>Hasil pencarian aktivitas: bentuk kolomnya berbeda per jenis (mengikuti <c>SP_ACTIVITY_SEARCH</c>), jadi disajikan generik.</summary>
public sealed record ActivityResult(IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<string?>> Rows, bool Truncated, int MaxRows)
{
    public static ActivityResult Empty { get; } = new([], [], false, ActivityRules.MaxRows);
}

/// <summary>Satu baris hasil; sel berurutan sesuai <see cref="ActivityResult.Columns"/>.</summary>
public sealed record ActivityRow(IReadOnlyList<string?> Cells);

public sealed record ActivitySearchResult(bool Success, ActivityResult? Result, IReadOnlyList<string> Errors)
{
    public static ActivitySearchResult Fail(params string[] errors) => new(false, null, errors);
}

public static class ActivityRules
{
    /// <summary>Label layar lama: "Tempo Maksimal 31 hari" (pemeriksaannya dimatikan di SP; di sini ditegakkan).</summary>
    public const int MaxDays = 31;

    public const int MaxRows = 5000;

    public static IReadOnlyList<string> Validate(DateTime? from, DateTime? to)
    {
        var errors = new List<string>();
        if (from is null)
        {
            errors.Add("Tanggal 1 tidak valid");
        }

        if (to is null)
        {
            errors.Add("Tanggal 2 tidak valid");
        }

        if (from is { } f && to is { } t)
        {
            if (t.Date < f.Date)
            {
                errors.Add("Tanggal 2 tidak boleh sebelum Tanggal 1");
            }
            else if ((t.Date - f.Date).TotalDays > MaxDays)
            {
                errors.Add($"Rentang tanggal maksimal {MaxDays} hari");
            }
        }

        return errors;
    }
}

public interface IActivityMonitoringService
{
    /// <summary>User yang boleh dipantau oleh <paramref name="viewerUserId"/> (<c>SP_GET_DOWNLINE</c>).</summary>
    Task<IReadOnlyList<LookupItem>> GetCollectorsAsync(string viewerUserId, CancellationToken ct = default);

    /// <summary>Validasi, pastikan collector yang dipilih termasuk daftar di atas (layar lama mempercayai dropdown), lalu cari.</summary>
    Task<ActivitySearchResult> SearchAsync(string viewerUserId, string collectorUserId, ActivityKind kind, DateTime? from, DateTime? to, CancellationToken ct = default);
}
