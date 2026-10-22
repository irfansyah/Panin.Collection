using System.Data;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Panin.Application.Modules;
using Panin.Application.Status;

namespace Panin.Infrastructure.Status;

/// <summary>Modul yang DB DATA-nya dipakai layanan status di shell (Collection Management).</summary>
internal static class StatusModule
{
    public const string Id = "60";
}

public sealed partial class UserAreaService(IModuleDbFactory dbs, IMemoryCache cache, ILogger<UserAreaService> logger) : IUserAreaService
{
    public async Task<UserArea?> GetAsync(string userId, CancellationToken ct = default)
    {
        try
        {
            return await cache.GetOrCreateAsync($"area:{userId}", async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30);
                var db = await dbs.GetDataDbAsync(StatusModule.Id, ct).ConfigureAwait(false);
                var r = await db.QueryFirstOrDefaultAsync(
                    "select BRANCH_CODE, AREA_ID, CITY_ID from VW_AUTH_USERAREA where USERID = @1", [userId], ct: ct).ConfigureAwait(false);
                return r is null ? null : new UserArea(Text(r, "BRANCH_CODE"), Text(r, "AREA_ID"), Text(r, "CITY_ID"));
            }).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFailure(logger, ex, userId);
            return null;
        }
    }

    private static string Text(DataRow r, string c) => r[c] is DBNull ? string.Empty : Convert.ToString(r[c], System.Globalization.CultureInfo.InvariantCulture)!.Trim();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Area user {UserId} tidak dapat dibaca dari DB DATA")]
    private static partial void LogFailure(ILogger logger, Exception ex, string userId);
}

public sealed partial class DataStatusService(IModuleDbFactory dbs, IMemoryCache cache, ILogger<DataStatusService> logger) : IDataStatusService
{
    public async Task<DataFreshness?> GetFreshnessAsync(CancellationToken ct = default)
    {
        try
        {
            return await cache.GetOrCreateAsync("data-freshness", async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
                var db = await dbs.GetDataDbAsync(StatusModule.Id, ct).ConfigureAwait(false);
                var r = await db.QueryFirstOrDefaultAsync(
                    "select max(AS_OF_DATE) as LAST_DATE, isnull(datediff(day, max(AS_OF_DATE), getdate()), 0) as DAYS_BEHIND from ACCOUNT_MASTER", ct: ct).ConfigureAwait(false);
                return r is null || r["LAST_DATE"] is DBNull
                    ? null
                    : new DataFreshness(Convert.ToDateTime(r["LAST_DATE"], System.Globalization.CultureInfo.InvariantCulture), Convert.ToInt32(r["DAYS_BEHIND"], System.Globalization.CultureInfo.InvariantCulture));
            }).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFailure(logger, ex);
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Kesegaran data tidak dapat dibaca dari DB DATA")]
    private static partial void LogFailure(ILogger logger, Exception ex);
}

/// <summary>Membungkus <c>SP_VW_WARNINGCONTENT</c> (peringatan janji bayar, dsb.) yang mengembalikan judul dan tabel berisi HTML lama.</summary>
public sealed partial class WarningService(IModuleDbFactory dbs, IMemoryCache cache, ILogger<WarningService> logger) : IWarningService
{
    public async Task<WarningSet> GetAsync(string userId, CancellationToken ct = default)
    {
        try
        {
            return await cache.GetOrCreateAsync($"warning:{userId}", async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
                var db = await dbs.GetDataDbAsync(StatusModule.Id, ct).ConfigureAwait(false);
                var ds = await db.QueryDataSetAsync("exec SP_VW_WARNINGCONTENT @1", [userId], ct: ct).ConfigureAwait(false);
                return Parse(ds);
            }).ConfigureAwait(false) ?? WarningSet.Empty;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFailure(logger, ex, userId);
            return WarningSet.Empty;
        }
    }

    public static WarningSet Parse(DataSet ds)
    {
        if (ds.Tables.Count < 2 || ds.Tables[1].Rows.Count == 0)
        {
            return WarningSet.Empty;
        }

        var title = ds.Tables[0].Rows.Count > 0 ? Convert.ToString(ds.Tables[0].Rows[0][0], System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty : string.Empty;
        var table = ds.Tables[1];
        var columns = table.Columns.Cast<DataColumn>().Select(c => c.ColumnName).ToList();
        var rows = new List<WarningRow>();
        foreach (DataRow r in table.Rows)
        {
            var cells = new Dictionary<string, string>(StringComparer.Ordinal);
            string? acc = null, cust = null;
            foreach (var c in columns)
            {
                var raw = r[c] is DBNull ? string.Empty : Convert.ToString(r[c], System.Globalization.CultureInfo.InvariantCulture)!;
                var link = LegacyLink().Match(raw);
                if (link.Success)
                {
                    acc ??= link.Groups["acc"].Value;
                    cust ??= link.Groups["cust"].Value;
                }

                cells[c] = HtmlTag().Replace(raw, string.Empty).Trim();
            }

            rows.Add(new WarningRow(cells, acc, cust));
        }

        return new WarningSet(title.Trim(), columns, rows);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Peringatan untuk {UserId} tidak dapat dibaca dari DB DATA")]
    private static partial void LogFailure(ILogger logger, Exception ex, string userId);

    [GeneratedRegex(@"ACC=(?<acc>[^&'""]*)&(?:amp;)?CUST=(?<cust>[^'""]*)", RegexOptions.IgnoreCase)]
    private static partial Regex LegacyLink();

    [GeneratedRegex("<[^>]*>")]
    private static partial Regex HtmlTag();
}
