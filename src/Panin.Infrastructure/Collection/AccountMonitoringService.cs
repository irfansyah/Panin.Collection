using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;
using Panin.Application.Abstractions;
using Panin.Application.Collection;
using Panin.Application.Modules;
using Panin.Infrastructure.Data;

namespace Panin.Infrastructure.Collection;

/// <summary>
/// Layar Monitoring Debitur (menu <c>accmon</c>, <c>AccountMonitoring.ascx</c>) dan Monitoring Recovery/NPL (menu <c>recmon</c>,
/// <c>RecoveryMonitoring.ascx</c>). Keduanya identik kecuali SP daftar debitur; kolom keluarannya sama. Hanya membaca.
/// <list type="bullet">
/// <item><c>SP_ACCOUNT_MONITORING_TREE</c> ditulis ulang: tidak ada hubungan antara subquery berkorelasi per anggota tim
/// dan gabungan sekali jalan; terukur 7,3 s menjadi 0,18 s untuk 39 anggota dengan angka identik.</item>
/// <item><c>SP_ACCOUNT_MONITORING_DEBITUR</c> dipakai apa adanya (logikanya panjang, memakai tabel sementara), tetapi hasilnya
/// dibatasi dan di-cache singkat: pengguna teratas mengembalikan ±13 ribu baris.</item>
/// </list>
/// </summary>
public sealed partial class AccountMonitoringService(IModuleDbFactory dbs, SingleFlightCache cache) : IAccountMonitoringService
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(60);

    /// <summary>Layar ini berat bagi DB; dibatasi bersamaan agar 700 user tidak menghantamnya sekaligus.</summary>
    private static readonly SemaphoreSlim Gate = new(6, 6);

    public const string TeamSql = """
        SELECT U.USERID AS UserId, LTRIM(RTRIM(U.FULL_NAME)) AS Name, G.GROUP_NAME AS [Group],
               ISNULL(S.Cnt, 0) AS DebitorCount, S.OS AS Outstanding,
               ISNULL(TA.TARGET_AMOUNT, 0) + ISNULL(TA.ACHIEVMENT, 0) AS Target, ISNULL(TA.ACHIEVMENT, 0) AS Achievement,
               RB.BRANCH_NAME AS Branch
        FROM SCUSER U
        JOIN RFGROUP G ON G.GROUP_ID = U.[GROUP]
        LEFT JOIN RFBRANCH RB ON U.BRANCH_CODE = RB.BRANCH_CODE
        LEFT JOIN TARGET_ACHIEVMENT TA ON TA.USERID = U.USERID AND TA._YEAR = YEAR(GETDATE()) AND TA._MONTH = MONTH(GETDATE())
        LEFT JOIN (
            SELECT X.USERID, COUNT(LM.ACC_NO) AS Cnt, SUM(CASE WHEN LD.ACC_NO IS NOT NULL THEN ISNULL(LD.OUTSTANDING, 0) END) AS OS
            FROM (SELECT USERID, USERID AS OWNER_ID FROM SCUSER WHERE UPLINER = @1
                  UNION
                  SELECT USERID, DOWNLINE FROM TEMP_DOWNLINE WHERE USERID IN (SELECT USERID FROM SCUSER WHERE UPLINER = @1)) X
            JOIN LOAN_MASTER LM ON LM.USERID = X.OWNER_ID AND LM.CONFIRMED = '1'
            LEFT JOIN LOAN_DETAIL LD ON LD.ACC_NO = LM.ACC_NO
            GROUP BY X.USERID) S ON S.USERID = U.USERID
        WHERE U.UPLINER = @1 AND U.ACTIVE = '1'
        ORDER BY Name
        """;

    public const string CanViewSql = """
        SELECT CASE WHEN @1 = @2
                      OR EXISTS (SELECT 1 FROM TEMP_DOWNLINE WHERE USERID = @1 AND DOWNLINE = @2)
                      OR EXISTS (SELECT 1 FROM SCUSER WHERE USERID = @2 AND UPLINER = @1) THEN 1 ELSE 0 END
        """;

    public async Task<bool> CanViewAsync(string viewerUserId, string targetUserId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(viewerUserId) || string.IsNullOrWhiteSpace(targetUserId))
        {
            return false;
        }

        var db = await dbs.GetDataDbAsync("60", ct).ConfigureAwait(false);
        return await db.ExecuteScalarAsync<int>(CanViewSql, [viewerUserId.Trim(), targetUserId.Trim()], ct: ct).ConfigureAwait(false) == 1;
    }

    public async Task<IReadOnlyList<MonitoringTeamMember>> GetTeamAsync(string userId, CancellationToken ct = default)
        => await cache.GetOrCreateAsync($"mon-team:{userId}", CacheFor, async () =>
        {
            var db = await dbs.GetDataDbAsync("60").ConfigureAwait(false);
            return (IReadOnlyList<MonitoringTeamMember>?)await db.QueryAsync<MonitoringTeamMember>(TeamSql, [userId], new DbOptions(TimeoutSeconds: 60)).ConfigureAwait(false);
        }).ConfigureAwait(false) ?? [];

    public async Task<DetailRecord?> GetTargetAsync(string userId, CancellationToken ct = default)
        => await cache.GetOrCreateAsync($"mon-target:{userId}", CacheFor, async () =>
        {
            var db = await dbs.GetDataDbAsync("60").ConfigureAwait(false);
            var row = await db.QueryFirstOrDefaultAsync("exec SP_GET_TARGET_ACHIEVMENT @1, @2", [userId, DateTime.Today]).ConfigureAwait(false);
            return ToRecord(row);
        }).ConfigureAwait(false);

    public async Task<string?> GetOutstandingAsync(string userId, CancellationToken ct = default)
        => await cache.GetOrCreateAsync($"mon-os:{userId}", CacheFor, async () =>
        {
            var db = await dbs.GetDataDbAsync("60").ConfigureAwait(false);
            var row = await db.QueryFirstOrDefaultAsync("exec SP_GET_OS @1", [userId]).ConfigureAwait(false);
            return row is null || !row.Table.Columns.Contains("OUTSTANDING_MF") || row["OUTSTANDING_MF"] is DBNull
                ? null
                : Convert.ToString(row["OUTSTANDING_MF"], CultureInfo.InvariantCulture)?.Trim();
        }).ConfigureAwait(false);

    public static string DebitorProcedure(MonitoringKind kind) => kind == MonitoringKind.Recovery ? "SP_ACCOUNT_MONITORING_RECOVERY" : "SP_ACCOUNT_MONITORING_DEBITUR";

    public async Task<MonitoringDebitorResult> GetDebitorsAsync(string userId, bool touched, MonitoringKind kind = MonitoringKind.Collection, CancellationToken ct = default)
        => await cache.GetOrCreateAsync($"mon-debitor:{kind}:{userId}:{touched}", CacheFor, async () =>
        {
            await Gate.WaitAsync().ConfigureAwait(false);
            try
            {
                var db = await dbs.GetDataDbAsync("60").ConfigureAwait(false);
                var table = await db.QueryTableAsync($"exec {DebitorProcedure(kind)} @1, @2", [userId, touched], new DbOptions(TimeoutSeconds: 120)).ConfigureAwait(false);
                return (MonitoringDebitorResult?)Map(table);
            }
            finally
            {
                Gate.Release();
            }
        }).ConfigureAwait(false) ?? new MonitoringDebitorResult([], false, MonitoringRules.MaxDebitors);

    public static MonitoringDebitorResult Map(DataTable table)
    {
        var truncated = table.Rows.Count > MonitoringRules.MaxDebitors;
        var rows = table.Rows.Cast<DataRow>().Take(MonitoringRules.MaxDebitors).Select(r => new MonitoringDebitorRow
        {
            AccountNo = Text(r, "Nomor Rekening") ?? string.Empty,
            CustomerRef = CustomerRef(Text(r, "Detail")) ?? CustomerRef(Text(r, "Monitoring")),
            CustomerName = Text(r, "Nama Nasabah"),
            City = Text(r, "Kota"),
            Facility = Text(r, "Fasilitas"),
            Principal = Number(r, "Outstanding Pokok"),
            Dpd = (int?)Number(r, "DPD"),
            AccountStatus = Text(r, "Status Rekening"),
            LastAction = Text(r, "Status Akhir"),
            InstallmentNo = (int?)Number(r, "Angsuran Ke"),
            ShortPayment = Number(r, "Kurang Setor"),
            Recommendation = Strip(Text(r, "Rekomendasi")),
            AssignedOn = r.Table.Columns.Contains("Tgl Penugasan") && r["Tgl Penugasan"] is DateTime d ? d : null,
            TodayStatus = Text(r, "Status"),
            HasLetterException = Text(r, "FLAG") == "*",
        }).ToList();
        return new MonitoringDebitorResult(rows, truncated, MonitoringRules.MaxDebitors);
    }

    private static string? Text(DataRow r, string column)
        => r.Table.Columns.Contains(column) && r[column] is not DBNull ? Convert.ToString(r[column], CultureInfo.InvariantCulture)?.Trim() : null;

    private static double? Number(DataRow r, string column)
        => r.Table.Columns.Contains(column) && r[column] is not DBNull
            && double.TryParse(Convert.ToString(r[column], CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;

    private static string? CustomerRef(string? html)
        => html is null ? null : CustRegex().Match(html) is { Success: true } m ? m.Groups[1].Value : null;

    private static string? Strip(string? html)
        => html is null ? null : WebUtility(HtmlTag().Replace(html, string.Empty)).Trim() is { Length: > 0 } s && s != "&nbsp" ? s : null;

    private static string WebUtility(string s) => System.Net.WebUtility.HtmlDecode(s);

    private static DetailRecord? ToRecord(DataRow? row)
    {
        if (row is null)
        {
            return null;
        }

        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (DataColumn c in row.Table.Columns)
        {
            values[c.ColumnName] = row[c] is DBNull ? null : row[c];
        }

        return new DetailRecord(values);
    }

    [GeneratedRegex(@"CUST=([^'&""]+)", RegexOptions.IgnoreCase)]
    private static partial Regex CustRegex();

    [GeneratedRegex("<[^>]*>")]
    private static partial Regex HtmlTag();
}
