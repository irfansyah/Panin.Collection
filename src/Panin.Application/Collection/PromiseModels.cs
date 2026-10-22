namespace Panin.Application.Collection;

/// <summary>Satu janji debitur (hasil call atau visit) pada layar "Janji Debitur" (<c>Search_by_ptp.aspx</c> lama).</summary>
public sealed class PromiseRow
{
    /// <summary>Call atau Visit.</summary>
    public string Activity { get; set; } = string.Empty;

    public DateTime? PromiseDate { get; set; }

    public string? PromiseTime { get; set; }

    public string? Result { get; set; }

    public string AccountNo { get; set; } = string.Empty;

    public string? CustomerName { get; set; }

    public string? Facility { get; set; }

    public int? Dpd { get; set; }

    public string? Collectibility { get; set; }

    public string? LastAction { get; set; }

    public string? Officer { get; set; }
}

public sealed record PromiseSearchResult(IReadOnlyList<PromiseRow> Rows, bool Truncated, int MaxRows, IReadOnlyList<string> Errors)
{
    public bool Success => Errors.Count == 0;

    public static PromiseSearchResult Fail(string message) => new([], false, PromiseRules.MaxRows, [message]);
}

public static class PromiseRules
{
    public const int MaxRows = 5000;

    /// <summary>Rentang tanggal terlama sekali cari; SP menggabungkan riwayat call dan visit seluruh bawahan.</summary>
    public const int MaxRangeDays = 366;

    public static string? Validate(DateTime from, DateTime to)
    {
        if (from > to)
        {
            return "Tanggal awal tidak boleh setelah tanggal akhir.";
        }

        return (to.Date - from.Date).TotalDays > MaxRangeDays ? $"Rentang tanggal maksimal {MaxRangeDays} hari." : null;
    }
}

public interface IPromiseService
{
    /// <summary>Jenis hasil yang dapat dicari (<c>VW_GET_RESULT</c>, tanpa duplikat).</summary>
    Task<IReadOnlyList<LookupItem>> GetResultsAsync(CancellationToken ct = default);

    /// <summary>
    /// Janji milik <paramref name="userId"/> dan seluruh bawahannya (<c>SP_PTP_SEARCH</c>, tanpa mode "lihat semua").
    /// <paramref name="userId"/> harus berasal dari sesi, bukan dari masukan pengguna.
    /// </summary>
    Task<PromiseSearchResult> SearchAsync(string userId, string? resultId, DateTime from, DateTime to, CancellationToken ct = default);
}

/// <summary>Satu berita yang diterbitkan; <see cref="Html"/> sudah dibersihkan dari skrip dan atribut berbahaya.</summary>
public sealed record NewsItem(string Id, string Subject, string Html, DateTime? Date);

public interface INewsService
{
    /// <summary>Berita yang diterbitkan (<c>NEWS_PUBLISH=1</c>), terbaru dahulu.</summary>
    Task<IReadOnlyList<NewsItem>> ListPublishedAsync(CancellationToken ct = default);
}
