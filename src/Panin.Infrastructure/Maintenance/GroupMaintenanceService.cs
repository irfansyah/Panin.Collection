using Panin.Application.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Panin.Application.Abstractions;
using Panin.Application.Maintenance;
using Panin.Application.Modules;
using Panin.Infrastructure.Data;
using Panin.Infrastructure.Modules;

namespace Panin.Infrastructure.Maintenance;

/// <summary>
/// User &amp; Group Parameter — Group (Maker dan Approval) serta akses menu per group. Memakai mesin ajuan bersama
/// (<see cref="ParameterEngine"/>); persetujuan menulis <c>SCALLGROUP</c> dan <c>SP_AUDITTRAIL</c> dalam satu transaksi.
/// Akses menu (<c>GRPMENUX</c>) di layar lama disimpan langsung oleh maker tanpa approval; di sini tetap langsung, tetapi atomik,
/// hanya untuk Maker, dengan induk menu otomatis ikut, menolak mencabut hak Maintenance milik group sendiri, dan tercatat di log.
/// </summary>
public sealed partial class GroupMaintenanceService(
    IModuleDbFactory dbs,
    IMenuService menus,
    SingleFlightCache cache,
    IOptions<MaintenanceOptions> options,
    ILogger<GroupMaintenanceService> logger) : IGroupMaintenanceService
{
    private const string Table = "SCALLGROUP";
    private static readonly string[] Fields = ["GROUPID", "SG_GRPNAME", "SG_ACTIVE"];

    private Task<IDb> Db(CancellationToken ct = default) => dbs.GetDataDbAsync(ParameterEngine.ModuleId, ct);

    public Task<bool> HasRoleAsync(string groupId, MaintenanceRole role, CancellationToken ct = default)
        => ParameterEngine.HasRoleAsync(menus, groupId, role, ct);

    public async Task<IReadOnlyList<GroupRow>> GetGroupsAsync(CancellationToken ct = default)
    {
        var db = await Db(ct).ConfigureAwait(false);
        return await db.QueryAsync<GroupRow>(
            """
            SELECT G.GROUPID AS GroupId, G.SG_GRPNAME AS Name, CAST(CASE WHEN G.SG_ACTIVE = '1' THEN 1 ELSE 0 END AS bit) AS Active,
                   (SELECT COUNT(*) FROM SCALLUSER U WHERE U.GROUPID = G.GROUPID) AS UserCount
            FROM SCALLGROUP G ORDER BY G.GROUPID
            """, ct: ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<PendingGroupRow>> GetPendingAsync(CancellationToken ct = default)
    {
        var db = await Db(ct).ConfigureAwait(false);
        var rows = await db.QueryAsync<PendingGroupRow>(
            """
            SELECT P.TEMPORARYID AS TemporaryId, P.STATUS AS Status, P.CREATEDBY AS CreatedBy, P.GROUPID AS GroupId, P.SG_GRPNAME AS Name,
                   CASE WHEN P.SG_ACTIVE = '1' THEN CAST(1 AS bit) WHEN P.SG_ACTIVE = '0' THEN CAST(0 AS bit) END AS Active
            FROM VW_SCALLGROUPPENDING P ORDER BY P.GROUPID
            """, ct: ct).ConfigureAwait(false);
        if (rows.Count == 0)
        {
            return rows;
        }

        var details = await db.QueryTableAsync(
            "SELECT TemporaryID, FieldName, FieldValue, FieldValueBefore FROM PARAMETERSYSTEM_TEMPORARYDETAIL WHERE TemporaryID IN (SELECT TemporaryID FROM PARAMETERSYSTEM_TEMPORARY WHERE TableName = @1)",
            [Table], ct: ct).ConfigureAwait(false);
        var byId = details.Rows.Cast<System.Data.DataRow>().GroupBy(r => (Guid)r[0]).ToDictionary(g => g.Key, g => g.ToList());
        return [.. rows.Select(r => r.Status == "Update" && byId.TryGetValue(r.TemporaryId, out var d) ? r with { Changes = DescribeChanges(d) } : r)];
    }

    public async Task<MaintenanceResult> SavePendingAsync(string makerUserId, GroupDraft draft, CancellationToken ct = default)
    {
        var errors = GroupRules.Validate(draft);
        if (errors.Count > 0)
        {
            return new MaintenanceResult(false, errors);
        }

        var groupId = draft.GroupId.Trim();
        var db = await Db(ct).ConfigureAwait(false);
        var existing = await db.QueryFirstOrDefaultAsync("SELECT GROUPID, SG_GRPNAME, SG_ACTIVE FROM SCALLGROUP WHERE GROUPID = @1", [groupId], ct: ct).ConfigureAwait(false);
        if (!draft.IsEdit && existing is not null)
        {
            return MaintenanceResult.Fail("Group ID sudah ada");
        }

        if (draft.IsEdit && existing is null)
        {
            return MaintenanceResult.Fail("Group tidak ditemukan");
        }

        var after = new Dictionary<string, string?>
        {
            ["GROUPID"] = groupId,
            ["SG_GRPNAME"] = ParameterEngine.Blank(draft.Name),
            ["SG_ACTIVE"] = draft.Active ? "1" : "0",
        };
        var before = existing is null ? null : Fields.ToDictionary(f => f, f => ParameterEngine.ToText(existing[f]));
        try
        {
            await ParameterEngine.WritePendingAsync(db, Table, [new("GROUPID", groupId)], existing is null ? "Insert" : "Update", makerUserId, Fields, after, before, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFailed(logger, ex, "simpan ajuan group", makerUserId, groupId);
            return MaintenanceResult.Fail($"Ajuan gagal disimpan. Coba lagi atau hubungi administrator. {ErrorReference.Tag}");
        }

        LogPending(logger, makerUserId, existing is null ? "Insert" : "Update", groupId);
        return MaintenanceResult.Ok;
    }

    public async Task<MaintenanceResult> DeleteToPendingAsync(string makerUserId, string makerGroupId, string groupId, CancellationToken ct = default)
    {
        groupId = groupId.Trim();
        if (string.Equals(groupId, makerGroupId, StringComparison.OrdinalIgnoreCase))
        {
            return MaintenanceResult.Fail("Anda tidak dapat menghapus group Anda sendiri");
        }

        var db = await Db(ct).ConfigureAwait(false);
        var existing = await db.QueryFirstOrDefaultAsync("SELECT GROUPID, SG_GRPNAME, SG_ACTIVE FROM SCALLGROUP WHERE GROUPID = @1", [groupId], ct: ct).ConfigureAwait(false);
        if (existing is null)
        {
            return MaintenanceResult.Fail("Group tidak ditemukan");
        }

        var users = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM SCALLUSER WHERE GROUPID = @1", [groupId], ct: ct).ConfigureAwait(false);
        if (users > 0)
        {
            return MaintenanceResult.Fail($"Group masih dipakai {users} user; pindahkan user-nya terlebih dahulu");
        }

        var values = Fields.ToDictionary(f => f, f => ParameterEngine.ToText(existing[f]));
        try
        {
            await ParameterEngine.WritePendingAsync(db, Table, [new("GROUPID", groupId)], "Delete", makerUserId, Fields, values, values, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFailed(logger, ex, "ajukan hapus group", makerUserId, groupId);
            return MaintenanceResult.Fail($"Ajuan gagal disimpan. Coba lagi atau hubungi administrator. {ErrorReference.Tag}");
        }

        LogPending(logger, makerUserId, "Delete", groupId);
        return MaintenanceResult.Ok;
    }

    public async Task<MaintenanceResult> CancelPendingAsync(string makerUserId, Guid temporaryId, CancellationToken ct = default)
    {
        var db = await Db(ct).ConfigureAwait(false);
        var owner = await db.ExecuteScalarAsync<string>("SELECT CREATEDBY FROM PARAMETERSYSTEM_TEMPORARY WHERE TEMPORARYID = @1 AND TABLENAME = @2", [temporaryId, Table], ct: ct).ConfigureAwait(false);
        if (owner is null)
        {
            return MaintenanceResult.Fail("Ajuan tidak ditemukan");
        }

        if (!string.Equals(owner, makerUserId, StringComparison.OrdinalIgnoreCase))
        {
            return MaintenanceResult.Fail("Hanya pembuat ajuan yang dapat membatalkannya");
        }

        await ParameterEngine.DeletePendingAsync(db, temporaryId, ct).ConfigureAwait(false);
        return MaintenanceResult.Ok;
    }

    public async Task<MaintenanceResult> RejectAsync(string approverUserId, IReadOnlyList<Guid> temporaryIds, CancellationToken ct = default)
    {
        var db = await Db(ct).ConfigureAwait(false);
        foreach (var id in temporaryIds.Distinct())
        {
            // Hanya ajuan group yang boleh ditolak dari layar ini.
            var table = await db.ExecuteScalarAsync<string>("SELECT TABLENAME FROM PARAMETERSYSTEM_TEMPORARY WHERE TEMPORARYID = @1", [id], ct: ct).ConfigureAwait(false);
            if (string.Equals(table, Table, StringComparison.OrdinalIgnoreCase))
            {
                await ParameterEngine.DeletePendingAsync(db, id, ct).ConfigureAwait(false);
                LogDecision(logger, approverUserId, "tolak", id);
            }
        }

        return MaintenanceResult.Ok;
    }

    public async Task<MaintenanceResult> ApproveAsync(string approverUserId, IReadOnlyList<Guid> temporaryIds, CancellationToken ct = default)
    {
        var db = await Db(ct).ConfigureAwait(false);
        var errors = new List<string>();
        foreach (var id in temporaryIds.Distinct())
        {
            try
            {
                var error = await ApproveOneAsync(db, approverUserId, id, ct).ConfigureAwait(false);
                if (error is not null)
                {
                    errors.Add(error);
                }
                else
                {
                    LogDecision(logger, approverUserId, "setuju", id);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFailed(logger, ex, "setujui group", approverUserId, id.ToString());
                errors.Add($"Ajuan {id} gagal diproses; tidak ada perubahan yang tercatat untuk ajuan itu.");
            }
        }

        return errors.Count == 0 ? MaintenanceResult.Ok : new MaintenanceResult(false, errors);
    }

    private async Task<string?> ApproveOneAsync(IDb db, string approver, Guid id, CancellationToken ct)
    {
        var header = await db.QueryFirstOrDefaultAsync("SELECT STATUS, CREATEDBY, TABLENAME FROM PARAMETERSYSTEM_TEMPORARY WHERE TEMPORARYID = @1", [id], ct: ct).ConfigureAwait(false);
        if (header is null)
        {
            return "Ajuan sudah tidak ada (mungkin sudah diproses)";
        }

        if (!string.Equals(ParameterEngine.Cell(header["TABLENAME"]), Table, StringComparison.OrdinalIgnoreCase))
        {
            return "Ajuan bukan untuk data group";
        }

        var status = ParameterEngine.Cell(header["STATUS"]);
        var maker = ParameterEngine.Cell(header["CREATEDBY"]);
        if (options.Value.RequireDifferentApprover && string.Equals(maker, approver, StringComparison.OrdinalIgnoreCase))
        {
            return "Ajuan Anda sendiri tidak dapat Anda setujui (maker dan approver harus berbeda)";
        }

        var details = await db.QueryTableAsync("SELECT FIELDNAME, FIELDVALUE FROM PARAMETERSYSTEM_TEMPORARYDETAIL WHERE TEMPORARYID = @1", [id], ct: ct).ConfigureAwait(false);
        var v = details.Rows.Cast<System.Data.DataRow>().ToDictionary(r => ParameterEngine.Cell(r[0]).ToUpperInvariant(), r => r[1] is DBNull ? null : Convert.ToString(r[1], CultureInfo.InvariantCulture));
        var groupId = v.GetValueOrDefault("GROUPID");
        if (string.IsNullOrWhiteSpace(groupId))
        {
            return "Ajuan rusak (Group ID kosong)";
        }

        if (status is "Insert" or "Update")
        {
            const string Sql = """
                SET XACT_ABORT ON; BEGIN TRAN;
                UPDATE SCALLGROUP SET SG_GRPNAME = @2, SG_ACTIVE = @3 WHERE GROUPID = @1;
                IF @@ROWCOUNT = 0 INSERT INTO SCALLGROUP (GROUPID, SG_GRPNAME, SG_ACTIVE) VALUES (@1, @2, @3);
                EXEC SP_AUDITTRAIL @4, @5;
                COMMIT TRAN
                """;
            await db.ExecuteAsync(Sql, [groupId, ParameterEngine.Blank(v.GetValueOrDefault("SG_GRPNAME")), v.GetValueOrDefault("SG_ACTIVE") == "1" ? "1" : "0", id, approver],
                new DbOptions(TimeoutSeconds: 120), ct).ConfigureAwait(false);
            return null;
        }

        if (status == "Delete")
        {
            var users = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM SCALLUSER WHERE GROUPID = @1", [groupId], ct: ct).ConfigureAwait(false);
            if (users > 0)
            {
                return $"Group {groupId} masih dipakai {users} user; ajuan hapus tidak dapat disetujui";
            }

            const string Sql = """
                SET XACT_ABORT ON; BEGIN TRAN;
                IF (SELECT COUNT(*) FROM SCALLGROUP WHERE GROUPID = @1) <> 1 RAISERROR ('Group tidak ditemukan', 16, 1);
                DELETE GRPMENUX WHERE GROUPID = @1;
                DELETE SCALLGROUP WHERE GROUPID = @1;
                EXEC SP_AUDITTRAIL @2, @3;
                COMMIT TRAN
                """;
            await db.ExecuteAsync(Sql, [groupId, id, approver], new DbOptions(TimeoutSeconds: 120), ct).ConfigureAwait(false);
            InvalidateGroup(groupId);
            return null;
        }

        return $"Status ajuan tidak dikenal: {status}";
    }

    public async Task<IReadOnlyList<ModuleItem>> GetModulesAsync(CancellationToken ct = default)
    {
        var db = await Db(ct).ConfigureAwait(false);
        var table = await db.QueryTableAsync("SELECT MODULEID, MODULENAME FROM RFMODULE WHERE ACTIVE = '1' ORDER BY MODULEID", ct: ct).ConfigureAwait(false);
        return [.. table.Rows.Cast<System.Data.DataRow>().Select(r => new ModuleItem(ParameterEngine.Cell(r[0]), ParameterEngine.Cell(r[1])))];
    }

    public async Task<IReadOnlyList<MenuAccessItem>> GetMenuAccessAsync(string groupId, string moduleId, CancellationToken ct = default)
    {
        var db = await Db(ct).ConfigureAwait(false);
        var table = await db.QueryTableAsync(
            """
            SELECT M.MENUID, M.MENUDESC, M.MENUPARENT, ISNULL(M.MENUPOSITION, 0) AS POS,
                   CAST(CASE WHEN GM.MENUID IS NULL THEN 0 ELSE 1 END AS bit) AS ALLOWED
            FROM RFMENUX M
            LEFT JOIN GRPMENUX GM ON GM.GROUPID = @1 AND GM.TYPEID = M.TYPEID AND GM.MENUID = M.MENUID
            WHERE M.TYPEID = @2
            ORDER BY M.MENUPARENT, M.MENUPOSITION, M.MENUID
            """, [groupId.Trim(), moduleId.Trim()], ct: ct).ConfigureAwait(false);
        return [.. table.Rows.Cast<System.Data.DataRow>().Select(r => new MenuAccessItem(
            ParameterEngine.Cell(r[0]), ParameterEngine.Cell(r[1]), ParameterEngine.Blank(ParameterEngine.Cell(r[2])),
            Convert.ToInt32(r[3], CultureInfo.InvariantCulture), (bool)r[4]))];
    }

    public async Task<MaintenanceResult> SaveMenuAccessAsync(string makerUserId, string makerGroupId, string groupId, string moduleId, IReadOnlyList<string> menuIds, CancellationToken ct = default)
    {
        if (!await HasRoleAsync(makerGroupId, MaintenanceRole.Maker, ct).ConfigureAwait(false))
        {
            return MaintenanceResult.Fail("Group Anda tidak punya hak Maker");
        }

        groupId = groupId.Trim();
        moduleId = moduleId.Trim();
        var db = await Db(ct).ConfigureAwait(false);
        if (await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM SCALLGROUP WHERE GROUPID = @1", [groupId], ct: ct).ConfigureAwait(false) == 0)
        {
            return MaintenanceResult.Fail("Group tidak ditemukan");
        }

        var all = await GetMenuAccessAsync(groupId, moduleId, ct).ConfigureAwait(false);
        if (all.Count == 0)
        {
            return MaintenanceResult.Fail("Modul tidak ditemukan atau belum punya menu");
        }

        var wanted = GroupRules.WithAncestors(menuIds, all);

        // Jangan sampai admin mengunci dirinya: hak Maker/Approver Maintenance milik group sendiri tidak boleh dicabut dari sini.
        if (moduleId == ParameterEngine.ModuleId && string.Equals(groupId, makerGroupId, StringComparison.OrdinalIgnoreCase))
        {
            var lost = all.Where(m => m.MenuId is "201010" or "201020").Where(m => m.Allowed && !wanted.Contains(m.MenuId)).ToList();
            if (lost.Count > 0)
            {
                return MaintenanceResult.Fail("Hak Maker/Approval Maintenance milik group Anda sendiri tidak boleh dicabut dari sini");
            }
        }

        var args = new List<object?> { groupId, moduleId };
        var sql = new StringBuilder("SET XACT_ABORT ON; BEGIN TRAN; DELETE GRPMENUX WHERE GROUPID = @1 AND TYPEID = @2; ");
        foreach (var menu in wanted)
        {
            args.Add(menu);
            sql.Append(CultureInfo.InvariantCulture, $"INSERT INTO GRPMENUX (GROUPID, TYPEID, MENUID) VALUES (@1, @2, @{args.Count}); ");
        }

        sql.Append("COMMIT TRAN");
        try
        {
            await db.ExecuteAsync(sql.ToString(), [.. args], new DbOptions(TimeoutSeconds: 60), ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFailed(logger, ex, "simpan akses menu", makerUserId, $"{groupId}/{moduleId}");
            return MaintenanceResult.Fail($"Akses menu gagal disimpan. Tidak ada perubahan yang tercatat; coba lagi atau hubungi administrator. {ErrorReference.Tag}");
        }

        InvalidateGroup(groupId);
        LogMenu(logger, makerUserId, groupId, moduleId, wanted.Count);
        return MaintenanceResult.Ok;
    }

    private void InvalidateGroup(string groupId)
    {
        cache.Remove(ModuleRegistry.GroupCacheKey(groupId));
        foreach (var module in new[] { "60", "70", "99" })
        {
            cache.Remove(MenuService.CacheKey(module, groupId));
        }
    }

    private static readonly Dictionary<string, string> Labels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SG_GRPNAME"] = "Nama", ["SG_ACTIVE"] = "Aktif",
    };

    private static string DescribeChanges(IEnumerable<System.Data.DataRow> details)
        => string.Join("; ", details
            .Where(r => Labels.ContainsKey(ParameterEngine.Cell(r[1])) && ParameterEngine.Cell(r[2]) != ParameterEngine.Cell(r[3]))
            .Select(r => $"{Labels[ParameterEngine.Cell(r[1])]}: {Show(ParameterEngine.Cell(r[3]), ParameterEngine.Cell(r[1]))} → {Show(ParameterEngine.Cell(r[2]), ParameterEngine.Cell(r[1]))}"));

    private static string Show(string value, string field)
        => field.Equals("SG_ACTIVE", StringComparison.OrdinalIgnoreCase) ? (value == "1" ? "Ya" : "Tidak") : value.Length == 0 ? "(kosong)" : value;

    [LoggerMessage(Level = LogLevel.Information, Message = "Maintenance group: {Maker} mengajukan {Status} untuk {GroupId}")]
    private static partial void LogPending(ILogger logger, string maker, string status, string groupId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Maintenance group: {Approver} {Decision} ajuan {TemporaryId}")]
    private static partial void LogDecision(ILogger logger, string approver, string decision, Guid temporaryId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Maintenance group: {Maker} menyimpan akses menu group {GroupId} modul {ModuleId} ({Count} menu)")]
    private static partial void LogMenu(ILogger logger, string maker, string groupId, string moduleId, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Maintenance group gagal ({Action}): {Actor}, objek {Subject}")]
    private static partial void LogFailed(ILogger logger, Exception ex, string action, string actor, string subject);
}
