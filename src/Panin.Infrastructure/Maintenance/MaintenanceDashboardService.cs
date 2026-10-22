using Panin.Application.Abstractions;
using Panin.Application.Maintenance;
using Panin.Application.Modules;

namespace Panin.Infrastructure.Maintenance;

/// <summary>
/// Data dashboard modul Maintenance (hanya membaca): antrian <c>PARAMETERSYSTEM_TEMPORARY</c>, hitungan user (<c>SCALLUSER</c> + flag),
/// riwayat disetujui (<c>AUDITTRAIL</c>), dan log login (<c>SCALLUSERLOG</c>). Semua query ringan (agregat berindeks tanggal), tanpa SP.
/// </summary>
public sealed class MaintenanceDashboardService(IModuleDbFactory dbs) : IMaintenanceDashboardService
{
    private const string ModuleId = "99";

    private sealed record DayTable(DateTime Day, string? TableName, int N);

    private sealed record DayCount(DateTime Day, int N);

    private Task<IDb> Db(CancellationToken ct) => dbs.GetDataDbAsync(ModuleId, ct);

    public async Task<PendingSummary> GetPendingAsync(string userId, int take, CancellationToken ct = default)
    {
        var db = await Db(ct).ConfigureAwait(false);
        var total = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM PARAMETERSYSTEM_TEMPORARY", ct: ct).ConfigureAwait(false);
        var mine = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM PARAMETERSYSTEM_TEMPORARY WHERE CreatedBy = @1", [userId], ct: ct).ConfigureAwait(false);
        var oldest = await db.QueryAsync<PendingChange>(
            $"SELECT TOP ({Math.Clamp(take, 1, 50)}) TemporaryID AS TemporaryId, TableName, Status, CreatedBy, CreatedDate FROM PARAMETERSYSTEM_TEMPORARY ORDER BY CreatedDate",
            ct: ct).ConfigureAwait(false);
        return new PendingSummary(total, mine, oldest);
    }

    public async Task<UserCounts> GetUserCountsAsync(CancellationToken ct = default)
    {
        var db = await Db(ct).ConfigureAwait(false);
        var rows = await db.QueryAsync<UserCounts>(
            """
            SELECT COUNT(*) AS Total,
                   ISNULL(SUM(CASE WHEN CONVERT(varchar(5), X.SU_ACTIVE) = '1' THEN 1 ELSE 0 END), 0) AS Active,
                   ISNULL(SUM(CASE WHEN CONVERT(varchar(5), ISNULL(F.SU_REVOKE, 0)) = '1' THEN 1 ELSE 0 END), 0) AS Revoked,
                   ISNULL(SUM(CASE WHEN CONVERT(varchar(5), ISNULL(F.SU_LOGON, 0)) = '1' THEN 1 ELSE 0 END), 0) AS LoggedOn
            FROM SCALLUSER X LEFT JOIN SCALLUSERFLAG F ON F.USERID = X.USERID
            """,
            ct: ct).ConfigureAwait(false);
        return rows.Count > 0 ? rows[0] : new UserCounts(0, 0, 0, 0);
    }

    public async Task<IReadOnlyList<DailyChanges>> GetChangesAsync(DateOnly today, int days, CancellationToken ct = default)
    {
        var from = DashboardDays.Range(today, days)[0].ToDateTime(TimeOnly.MinValue);
        var db = await Db(ct).ConfigureAwait(false);
        var rows = await db.QueryAsync<DayTable>(
            "SELECT CONVERT(date, ApprovedDate) AS Day, TableName, COUNT(*) AS N FROM AUDITTRAIL WHERE ApprovedDate >= @1 GROUP BY CONVERT(date, ApprovedDate), TableName",
            [from], ct: ct).ConfigureAwait(false);
        return DashboardDays.FillChanges(today, days, rows.Select(r => (DateOnly.FromDateTime(r.Day), r.TableName, r.N)));
    }

    public async Task<IReadOnlyList<AppliedChange>> GetRecentAsync(int take, CancellationToken ct = default)
    {
        var db = await Db(ct).ConfigureAwait(false);
        return await db.QueryAsync<AppliedChange>(
            $"SELECT TOP ({Math.Clamp(take, 1, 50)}) TableName, Status, CreatedBy, ApprovedBy, ApprovedDate FROM AUDITTRAIL ORDER BY ApprovedDate DESC",
            ct: ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<DailyLogins>> GetLoginsAsync(DateOnly today, int days, CancellationToken ct = default)
    {
        var from = DashboardDays.Range(today, days)[0].ToDateTime(TimeOnly.MinValue);
        var db = await Db(ct).ConfigureAwait(false);
        var rows = await db.QueryAsync<DayCount>(
            "SELECT CONVERT(date, _DATE) AS Day, COUNT(*) AS N FROM SCALLUSERLOG WHERE _DATE >= @1 GROUP BY CONVERT(date, _DATE)",
            [from], ct: ct).ConfigureAwait(false);
        return DashboardDays.FillLogins(today, days, rows.Select(r => (DateOnly.FromDateTime(r.Day), r.N)));
    }
}
