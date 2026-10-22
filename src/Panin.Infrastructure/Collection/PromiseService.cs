using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;
using Ganss.Xss;
using Panin.Application.Abstractions;
using Panin.Application.Collection;
using Panin.Application.Modules;
using Panin.Infrastructure.Data;

namespace Panin.Infrastructure.Collection;

/// <summary>
/// Janji Debitur (<c>Search_by_ptp.aspx</c> lama, tautan di layar Monitoring) dan Berita (<c>NewsView.ascx</c>). Hanya membaca.
/// <c>SP_PTP_SEARCH</c> dipakai apa adanya (terbatas ke pengguna dan bawahannya oleh SP), dengan <c>@VIEWALL</c> selalu 0.
/// </summary>
public sealed partial class PromiseService(IModuleDbFactory dbs, SingleFlightCache cache) : IPromiseService
{
    private static readonly TimeSpan ResultsCache = TimeSpan.FromMinutes(10);

    public const string ResultsSql = "SELECT DISTINCT RESULT_ID, RESULT_DESC FROM VW_GET_RESULT ORDER BY RESULT_DESC";

    public async Task<IReadOnlyList<LookupItem>> GetResultsAsync(CancellationToken ct = default)
        => await cache.GetOrCreateAsync("ptp-results", ResultsCache, async () =>
        {
            var db = await dbs.GetDataDbAsync("60").ConfigureAwait(false);
            var table = await db.QueryTableAsync(ResultsSql, [], new DbOptions(TimeoutSeconds: 60)).ConfigureAwait(false);
            return (IReadOnlyList<LookupItem>?)[.. table.Rows.Cast<DataRow>().Select(r => new LookupItem(Text(r, "RESULT_ID") ?? string.Empty, Text(r, "RESULT_DESC") ?? string.Empty))];
        }).ConfigureAwait(false) ?? [];

    public async Task<PromiseSearchResult> SearchAsync(string userId, string? resultId, DateTime from, DateTime to, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return PromiseSearchResult.Fail("Sesi tidak valid.");
        }

        if (PromiseRules.Validate(from, to) is { } error)
        {
            return PromiseSearchResult.Fail(error);
        }

        var db = await dbs.GetDataDbAsync("60", ct).ConfigureAwait(false);
        var table = await db.QueryTableAsync(
            "exec SP_PTP_SEARCH @1, @2, @3, @4, @5",
            [string.IsNullOrWhiteSpace(resultId) ? null : resultId.Trim(), from.Date, to.Date, userId.Trim(), false],
            new DbOptions(TimeoutSeconds: 120),
            ct).ConfigureAwait(false);
        return Map(table);
    }

    public static PromiseSearchResult Map(DataTable table)
    {
        var truncated = table.Rows.Count > PromiseRules.MaxRows;
        var rows = table.Rows.Cast<DataRow>().Take(PromiseRules.MaxRows).Select(r => new PromiseRow
        {
            Activity = Text(r, "Aktivitas") ?? string.Empty,
            PromiseDate = r.Table.Columns.Contains("Tgl Janji") && r["Tgl Janji"] is DateTime d ? d : null,
            PromiseTime = Text(r, "Waktu Janji") is { } t && t != "00:00:00" ? t : null,
            Result = Text(r, "Janji Debitur"),
            AccountNo = Text(r, "No Rek") ?? string.Empty,
            CustomerName = Text(r, "Nama"),
            Facility = Text(r, "Produk"),
            Dpd = int.TryParse(Text(r, "DPD"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var dpd) ? dpd : null,
            Collectibility = Text(r, "Kol"),
            LastAction = Text(r, "Aktivitas Terakhir") is { } la && !string.Equals(la, "NULL", StringComparison.OrdinalIgnoreCase) ? la : null,
            Officer = Text(r, "Officer"),
        }).ToList();
        return new PromiseSearchResult(rows, truncated, PromiseRules.MaxRows, []);
    }

    private static string? Text(DataRow r, string column)
        => r.Table.Columns.Contains(column) && r[column] is not DBNull ? Convert.ToString(r[column], CultureInfo.InvariantCulture)?.Trim() : null;
}

public sealed partial class NewsService(IModuleDbFactory dbs, SingleFlightCache cache) : INewsService
{
    private static readonly TimeSpan NewsCache = TimeSpan.FromMinutes(2);
    private const int MaxItems = 200;

    public const string Sql = "SELECT TOP (200) NEWSID, NEWS_SUBJECT, NEWS_CONTENT, NEWS_DATE FROM VW_NEWS WHERE NEWS_PUBLISH = 1 ORDER BY NEWS_DATE DESC";

    // Berita ditulis admin lewat editor HTML; tetap diperlakukan tidak tepercaya karena tampil di browser semua pengguna.
    private static readonly HtmlSanitizer Sanitizer = CreateSanitizer();

    public async Task<IReadOnlyList<NewsItem>> ListPublishedAsync(CancellationToken ct = default)
        => await cache.GetOrCreateAsync("news-published", NewsCache, async () =>
        {
            var db = await dbs.GetDataDbAsync("60").ConfigureAwait(false);
            var table = await db.QueryTableAsync(Sql, [], new DbOptions(TimeoutSeconds: 60)).ConfigureAwait(false);
            return (IReadOnlyList<NewsItem>?)[.. table.Rows.Cast<DataRow>().Take(MaxItems).Select(Map)];
        }).ConfigureAwait(false) ?? [];

    public static NewsItem Map(DataRow r)
    {
        var subject = r["NEWS_SUBJECT"] is DBNull ? string.Empty : Convert.ToString(r["NEWS_SUBJECT"], CultureInfo.InvariantCulture)!.Trim();
        var html = r["NEWS_CONTENT"] is DBNull ? string.Empty : Convert.ToString(r["NEWS_CONTENT"], CultureInfo.InvariantCulture)!;
        return new NewsItem(
            Convert.ToString(r["NEWSID"], CultureInfo.InvariantCulture) ?? string.Empty,
            string.IsNullOrEmpty(subject) ? "(tanpa judul)" : subject,
            Clean(html),
            r["NEWS_DATE"] is DateTime d ? d : null);
    }

    /// <summary>Membersihkan HTML berita dan mengarahkan gambar lama (<c>../Image/News/x.jpg</c>, <c>~/Image/News/x.jpg</c>) ke <c>/Image/News/</c>.</summary>
    public static string Clean(string html)
        => string.IsNullOrWhiteSpace(html) ? string.Empty : Sanitizer.Sanitize(OldImagePath().Replace(html, "/Image/News/"));

    private static HtmlSanitizer CreateSanitizer()
    {
        var s = new HtmlSanitizer();
        s.AllowedSchemes.Clear();
        s.AllowedSchemes.Add("http");
        s.AllowedSchemes.Add("https");
        s.AllowedSchemes.Add("mailto");
        s.AllowedTags.Remove("form");
        return s;
    }

    [GeneratedRegex(@"(?:\.\./|~/|/)?Image/News/", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OldImagePath();
}
