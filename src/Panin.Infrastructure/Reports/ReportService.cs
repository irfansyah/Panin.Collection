using System.Data;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Panin.Application.Abstractions;
using Panin.Application.Modules;
using Panin.Application.Reports;
using Panin.Infrastructure.Data;

namespace Panin.Infrastructure.Reports;

/// <summary>
/// Report daftar dinamis (R1): membaca definisi dari <c>PIVOTLIST</c>/<c>VW_REPORTSET</c> di DB modul dan menjalankannya sebagai SELECT
/// satu view dengan parameter (lihat <c>docs/REPORTS.md</c>). Hanya membaca. Report pivot, kustom, atau yang memakai kondisi kustom
/// ditandai belum tersedia dengan alasannya, tidak disembunyikan.
/// </summary>
public sealed class ReportService(IModuleDbFactory dbs, SingleFlightCache cache, ILogger<ReportService> logger) : IReportService
{
    private static readonly TimeSpan DefinitionCache = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan OptionCache = TimeSpan.FromMinutes(2);
    private const int MaxOptions = 2000;

    /// <summary>Query report bisa berat; dibatasi bersamaan agar tidak menghabiskan koneksi DB.</summary>
    private static readonly SemaphoreSlim Gate = new(6, 6);

    public const string SetSql = """
        SELECT PV_ID, PV_DESC, PV_COLUMNLIST, PV_ROWLIST, PV_DATALIST, PV_FILTERLIST, PV_COND, AssemblyObj
        FROM VW_REPORTSET WHERE SETID = @1 ORDER BY PV_DESC
        """;

    // Hanya report yang memang tercantum di sebuah set menu yang dapat dibuka lewat id.
    public const string OneSql = """
        SELECT TOP 1 PV_ID, PV_DESC, PV_COLUMNLIST, PV_ROWLIST, PV_DATALIST, PV_FILTERLIST, PV_COND, AssemblyObj
        FROM VW_REPORTSET WHERE PV_ID = @1
        """;

    public async Task<IReadOnlyList<ReportListItem>> ListAsync(string moduleId, string setId, CancellationToken ct = default)
        => await cache.GetOrCreateAsync($"rpt-set:{moduleId}:{setId.ToUpperInvariant()}", DefinitionCache, async () =>
        {
            var db = await dbs.GetDataDbAsync(moduleId).ConfigureAwait(false);
            var table = await db.QueryTableAsync(SetSql, [setId.Trim()], new DbOptions(TimeoutSeconds: 60)).ConfigureAwait(false);
            var today = DateOnly.FromDateTime(DateTime.Today);
            return (IReadOnlyList<ReportListItem>?)[.. table.Rows.Cast<DataRow>().Select(r =>
            {
                var parsed = Parse(r, today);
                return new ReportListItem(Text(r, "PV_ID") ?? string.Empty, Text(r, "PV_DESC") ?? string.Empty, parsed.Kind, parsed.UnavailableReason);
            })];
        }).ConfigureAwait(false) ?? [];

    public async Task<ReportDefinition?> GetAsync(string moduleId, string reportId, CancellationToken ct = default)
        => await cache.GetOrCreateAsync($"rpt-def:{moduleId}:{reportId}", DefinitionCache, async () =>
        {
            var db = await dbs.GetDataDbAsync(moduleId).ConfigureAwait(false);
            var row = await db.QueryFirstOrDefaultAsync(OneSql, [reportId.Trim()]).ConfigureAwait(false);
            return row is null ? null : Parse(row, DateOnly.FromDateTime(DateTime.Today)).Definition;
        }).ConfigureAwait(false);

    public async Task<IReadOnlyList<ReportOption>> GetOptionsAsync(string moduleId, ReportDefinition report, ReportFilter filter, object?[] values, CancellationToken ct = default)
    {
        if (filter.Type != ReportFilterType.List || filter.OptionsSql is null || ReportOptionSql.Validate(filter.OptionsSql) is not null)
        {
            return [];
        }

        // Hanya nilai induk yang dirujuk SQL pilihan yang masuk kunci cache dan parameter.
        var (args, errors) = ReportSqlBuilder.Coerce(report, values);
        var referenced = filter.OptionDependsOn;
        var key = $"rpt-opt:{moduleId}:{report.Id}:{filter.Seq}:{string.Join('|', referenced.Select(n => System.Convert.ToString(args[n - 1], CultureInfo.InvariantCulture)))}";
        if (errors.Count > 0)
        {
            return [];
        }

        return await cache.GetOrCreateAsync(key, OptionCache, async () =>
        {
            var db = await dbs.GetDataDbAsync(moduleId).ConfigureAwait(false);
            var table = await db.QueryTableAsync(filter.OptionsSql, args, new DbOptions(TimeoutSeconds: 60)).ConfigureAwait(false);
            return (IReadOnlyList<ReportOption>?)[.. table.Rows.Cast<DataRow>().Take(MaxOptions).Select(r =>
            {
                var code = System.Convert.ToString(r[0], CultureInfo.InvariantCulture)?.Trim() ?? string.Empty;
                var text = table.Columns.Count > 1 ? System.Convert.ToString(r[1], CultureInfo.InvariantCulture)?.Trim() : null;
                return new ReportOption(code, string.IsNullOrEmpty(text) ? code : text);
            })];
        }).ConfigureAwait(false) ?? [];
    }

    public async Task<ReportResult> RunAsync(string moduleId, ReportDefinition report, object?[] values, bool forExport, CancellationToken ct = default)
    {
        var (args, errors) = ReportSqlBuilder.Coerce(report, values);
        if (errors.Count > 0)
        {
            throw new ArgumentException(string.Join("; ", errors));
        }

        var max = forExport ? ReportLimits.ExportRows : ReportLimits.PreviewRows;
        var sql = ReportSqlBuilder.Select(report, max + 1);
        await Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var db = await dbs.GetDataDbAsync(moduleId, ct).ConfigureAwait(false);
            var table = await db.QueryTableAsync(sql, args, new DbOptions(TimeoutSeconds: 180), ct).ConfigureAwait(false);
            return Map(report, table, max);
        }
        finally
        {
            Gate.Release();
        }
    }

    public static ReportResult Map(ReportDefinition report, DataTable table, int max)
    {
        var truncated = table.Rows.Count > max;
        var columns = report.Columns.Select((c, i) => new ReportResultColumn(c.Caption, Kind(table.Columns[i].DataType), c.Summary)).ToList();
        var rows = table.Rows.Cast<DataRow>().Take(max).Select(r =>
        {
            var cells = new object?[report.Columns.Count];
            for (var i = 0; i < cells.Length; i++)
            {
                cells[i] = r[i] is DBNull ? null : r[i] is string s ? s.Trim() : r[i];
            }

            return cells;
        }).ToList();
        return new ReportResult(columns, rows, truncated, max);
    }

    private static ReportCellKind Kind(Type type)
        => type == typeof(DateTime) || type == typeof(DateTimeOffset) ? ReportCellKind.Date
            : type == typeof(decimal) || type == typeof(double) || type == typeof(float) ? ReportCellKind.Decimal
            : type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte) ? ReportCellKind.Integer
            : ReportCellKind.Text;

    private ReportParseResult Parse(DataRow r, DateOnly today)
    {
        var id = Text(r, "PV_ID") ?? string.Empty;
        var result = ReportDefinitionParser.Parse(id, Text(r, "PV_DESC") ?? id, Text(r, "PV_COLUMNLIST"), Text(r, "PV_ROWLIST"), Text(r, "PV_DATALIST"),
            Text(r, "PV_FILTERLIST"), Text(r, "PV_COND"), Text(r, "AssemblyObj"), today);
        if (result.Kind == ReportKind.List && result.UnavailableReason is not null)
        {
            logger.LogInformation("Report {ReportId} belum tersedia: {Reason}", id, result.UnavailableReason);
        }

        return result;
    }

    private static string? Text(DataRow r, string column)
        => r.Table.Columns.Contains(column) && r[column] is not DBNull ? System.Convert.ToString(r[column], CultureInfo.InvariantCulture) : null;
}
