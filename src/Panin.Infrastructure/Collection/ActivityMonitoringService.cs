using System.Data;
using System.Globalization;
using System.Net;
using Microsoft.Extensions.Logging;
using Panin.Application.Abstractions;
using Panin.Application.Collection;
using Panin.Application.Modules;
using Panin.Infrastructure.Data;

namespace Panin.Infrastructure.Collection;

/// <summary>
/// Layar Monitoring Aktivitas Collection (<c>Search_activity_collector.aspx</c>). Hanya membaca.
/// <c>SP_ACTIVITY_SEARCH</c> dan <c>SP_GET_DOWNLINE</c> dipakai apa adanya (bisnisnya ada di SP; mis. grup monitoring melihat
/// bawahan <c>KSN</c>), tetapi dengan penjagaan yang tidak ada di layar lama: collector yang dipilih harus ada di daftar
/// <c>SP_GET_DOWNLINE</c> milik pemantau, rentang tanggal dibatasi 31 hari, hasil dibatasi 5.000 baris, dan pencarian berat dibatasi bersamaan.
/// Kolom tautan HTML ("Lihat" ke memo) dibuang; memo belum dimigrasi.
/// </summary>
public sealed partial class ActivityMonitoringService(IModuleDbFactory dbs, SingleFlightCache cache, ILogger<ActivityMonitoringService> logger) : IActivityMonitoringService
{
    private static readonly SemaphoreSlim Gate = new(6, 6);

    public async Task<IReadOnlyList<LookupItem>> GetCollectorsAsync(string viewerUserId, CancellationToken ct = default)
        => await cache.GetOrCreateAsync($"act-collectors:{viewerUserId}", TimeSpan.FromSeconds(60), async () =>
        {
            var db = await dbs.GetDataDbAsync("60").ConfigureAwait(false);
            var table = await db.QueryTableAsync("exec SP_GET_DOWNLINE @1", [viewerUserId]).ConfigureAwait(false);
            return (IReadOnlyList<LookupItem>?)[.. table.Rows.Cast<DataRow>().Select(r => new LookupItem(Cell(r, 0), Cell(r, 1)))];
        }).ConfigureAwait(false) ?? [];

    public async Task<ActivitySearchResult> SearchAsync(string viewerUserId, string collectorUserId, ActivityKind kind, DateTime? fromDate, DateTime? toDate, CancellationToken ct = default)
    {
        var errors = ActivityRules.Validate(fromDate, toDate);
        if (string.IsNullOrWhiteSpace(collectorUserId))
        {
            errors = [.. errors, "Nama wajib diisi"];
        }

        if (errors.Count > 0)
        {
            return new ActivitySearchResult(false, null, errors);
        }

        var allowed = await GetCollectorsAsync(viewerUserId, ct).ConfigureAwait(false);
        if (allowed.All(c => !string.Equals(c.Code, collectorUserId.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            LogDenied(logger, viewerUserId, collectorUserId);
            return ActivitySearchResult.Fail("Anda tidak berwenang melihat aktivitas user tersebut.");
        }

        await Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var db = await dbs.GetDataDbAsync("60", ct).ConfigureAwait(false);
            var table = await db.QueryTableAsync("exec SP_ACTIVITY_SEARCH @1, @2, @3, @4",
                [((int)kind).ToString(CultureInfo.InvariantCulture), fromDate!.Value.Date, toDate!.Value.Date, collectorUserId.Trim()],
                new DbOptions(TimeoutSeconds: 120), ct).ConfigureAwait(false);
            return new ActivitySearchResult(true, Map(table), []);
        }
        finally
        {
            Gate.Release();
        }
    }

    public static ActivityResult Map(DataTable table)
    {
        // Kolom tautan memo ("Lihat"/"viewletter") berisi HTML; tidak dipakai.
        var keep = table.Columns.Cast<DataColumn>()
            .Where(c => !c.ColumnName.Equals("Lihat", StringComparison.OrdinalIgnoreCase) && !c.ColumnName.Equals("viewletter", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var rows = table.Rows.Cast<DataRow>().Take(ActivityRules.MaxRows)
            .Select(r => (IReadOnlyList<string?>)[.. keep.Select(c => Format(r[c]))])
            .ToList();
        return new ActivityResult([.. keep.Select(c => c.ColumnName)], rows, table.Rows.Count > ActivityRules.MaxRows, ActivityRules.MaxRows);
    }

    private static string? Format(object value) => value switch
    {
        DBNull => null,
        DateTime d => d.TimeOfDay == TimeSpan.Zero ? d.ToString("dd-MMM-yyyy", CultureInfo.GetCultureInfo("id-ID")) : d.ToString("dd-MMM-yyyy HH:mm", CultureInfo.GetCultureInfo("id-ID")),
        _ => WebUtility.HtmlDecode(Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim() ?? string.Empty),
    };

    private static string Cell(DataRow r, int index)
        => r[index] is DBNull ? string.Empty : Convert.ToString(r[index], CultureInfo.InvariantCulture)!.Trim();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Pencarian aktivitas ditolak (di luar daftar): pemantau {ViewerId}, collector {CollectorId}")]
    private static partial void LogDenied(ILogger logger, string viewerId, string collectorId);
}
