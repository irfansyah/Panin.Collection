using Panin.Application.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Panin.Application.Abstractions;
using Panin.Application.Collection;
using Panin.Application.Maintenance;
using Panin.Application.Modules;
using Panin.Application.Sessions;
using Panin.Infrastructure.Data;

namespace Panin.Infrastructure.Maintenance;

public sealed class MaintenanceOptions
{
    public const string SectionName = "Maintenance";

    /// <summary>Maker dan approver harus orang berbeda (prinsip empat mata). Layar lama tidak memeriksanya.</summary>
    public bool RequireDifferentApprover { get; set; } = true;
}

/// <summary>
/// User &amp; Group Parameter — User (Maker dan Approval). Mesin maker-checker lama (<c>PARAMETERSYSTEM_TEMPORARY</c> +
/// <c>...DETAIL</c>) dipertahankan agar berjalan berdampingan dengan aplikasi lama; yang berubah:
/// semua nilai dikirim sebagai parameter (lama: disusun sebagai teks SQL), kolom ditulis dari daftar tetap (bukan dinamis),
/// <c>SU_PWD</c> tidak pernah dibaca atau dikirim ke browser, setiap persetujuan satu transaksi (tulis <c>SCALLUSER</c> + <c>SP_AUDITTRAIL</c>),
/// maker dan approver harus berbeda orang, dan hak maker/approver diperiksa di server.
/// Bisnis tetap di DB: trigger <c>INS/UPD/DEL_USER</c> menjaga <c>TEMP_DOWNLINE</c>; <c>SP_AUDITTRAIL</c> memberi password awal
/// untuk user baru, memindahkan rekening user yang dihapus ke upliner-nya, lalu menghapus ajuan.
/// </summary>
public sealed partial class UserMaintenanceService(
    IModuleDbFactory dbs,
    IMenuService menus,
    IUserSessionRegistry sessions,
    IOptions<MaintenanceOptions> options,
    ILogger<UserMaintenanceService> logger) : IUserMaintenanceService
{
    private const string ModuleId = "99";
    private const string Table = "SCALLUSER";
    private const string MakerMenuId = "201010";
    private const string ApproverMenuId = "201020";

    private static readonly string[] Fields = ["USERID", "GROUPID", "SU_BRANCH_CODE", "SU_FULLNAME", "SU_HPNUM", "SU_EMAIL", "SU_UPLINER", "SU_ACTIVE"];

    private const string UserSelect = """
        SELECT X.USERID AS UserId, X.GROUPID AS GroupId, G.SG_GRPNAME AS GroupName, X.SU_FULLNAME AS FullName, X.SU_HPNUM AS Phone,
               X.SU_EMAIL AS Email, X.SU_UPLINER AS Upliner, X.SU_BRANCH_CODE AS BranchCode, X.SU_ACTIVE AS Active,
               CAST(CASE WHEN CONVERT(varchar(5), ISNULL(F.SU_LOGON, 0)) = '1' THEN 1 ELSE 0 END AS bit) AS LoggedOn,
               CAST(CASE WHEN CONVERT(varchar(5), ISNULL(F.SU_REVOKE, 0)) = '1' THEN 1 ELSE 0 END AS bit) AS Revoked
        FROM SCALLUSER X
        LEFT JOIN SCALLUSERFLAG F ON F.USERID = X.USERID
        LEFT JOIN SCALLGROUP G ON G.GROUPID = X.GROUPID
        """;

    public const string PendingSelect = """
        SELECT P.TEMPORARYID AS TemporaryId, P.STATUS AS Status, P.CREATEDBY AS CreatedBy, P.USERID AS UserId, P.GROUPID AS GroupId,
               P.SG_GRPNAME AS GroupName, P.SU_FULLNAME AS FullName, P.SU_HPNUM AS Phone, P.SU_UPLINER AS Upliner,
               P.SU_BRANCH_CODE AS BranchCode, CASE WHEN P.SU_ACTIVE = '1' THEN CAST(1 AS bit) WHEN P.SU_ACTIVE = '0' THEN CAST(0 AS bit) END AS Active
        FROM VW_SCALLUSERPENDING P
        ORDER BY P.USERID
        """;

    private Task<IDb> Db(CancellationToken ct = default) => dbs.GetDataDbAsync(ModuleId, ct);

    public Task<bool> HasRoleAsync(string groupId, MaintenanceRole role, CancellationToken ct = default)
        => ParameterEngine.HasRoleAsync(menus, groupId, role, ct);

    public async Task<IReadOnlyList<UserRow>> GetUsersAsync(CancellationToken ct = default)
    {
        var db = await Db(ct).ConfigureAwait(false);
        return await db.QueryAsync<UserRow>(UserSelect + " ORDER BY X.USERID", ct: ct).ConfigureAwait(false);
    }

    public async Task<UserRow?> GetUserAsync(string userId, CancellationToken ct = default)
    {
        var db = await Db(ct).ConfigureAwait(false);
        return await db.QueryFirstOrDefaultAsync<UserRow>(UserSelect + " WHERE X.USERID = @1", [userId.Trim()], ct: ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<PendingUserRow>> GetPendingAsync(CancellationToken ct = default)
    {
        var db = await Db(ct).ConfigureAwait(false);
        var rows = await db.QueryAsync<PendingUserRow>(PendingSelect, ct: ct).ConfigureAwait(false);
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

    public async Task<IReadOnlyList<LookupItem>> GetGroupsAsync(CancellationToken ct = default)
    {
        var db = await Db(ct).ConfigureAwait(false);
        var table = await db.QueryTableAsync("SELECT GROUPID, SG_GRPNAME FROM SCALLGROUP ORDER BY SG_GRPNAME", ct: ct).ConfigureAwait(false);
        return [.. table.Rows.Cast<System.Data.DataRow>().Select(r => new LookupItem(Cell(r[0]), Cell(r[1])))];
    }

    public async Task<IReadOnlyList<LookupItem>> GetBranchesAsync(CancellationToken ct = default)
    {
        var db = await Db(ct).ConfigureAwait(false);
        var table = await db.QueryTableAsync("SELECT BRANCH_CODE, BRANCH_NAME FROM RFBRANCH ORDER BY BRANCH_CODE", ct: ct).ConfigureAwait(false);
        return [.. table.Rows.Cast<System.Data.DataRow>().Select(r => new LookupItem(Cell(r[0]), Cell(r[1])))];
    }

    public async Task<MaintenanceResult> SavePendingAsync(string makerUserId, UserDraft draft, CancellationToken ct = default)
    {
        var errors = UserRules.Validate(draft);
        if (errors.Count > 0)
        {
            return new MaintenanceResult(false, errors);
        }

        var userId = draft.UserId.Trim();
        var db = await Db(ct).ConfigureAwait(false);

        var existing = await db.QueryFirstOrDefaultAsync("SELECT USERID, GROUPID, SU_BRANCH_CODE, SU_FULLNAME, SU_HPNUM, SU_EMAIL, SU_UPLINER, SU_ACTIVE FROM SCALLUSER WHERE USERID = @1", [userId], ct: ct).ConfigureAwait(false);
        if (!draft.IsEdit && existing is not null)
        {
            return MaintenanceResult.Fail("User ID sudah ada");
        }

        if (draft.IsEdit && existing is null)
        {
            return MaintenanceResult.Fail("User tidak ditemukan");
        }

        var upliner = Blank(draft.Upliner);
        if (upliner is not null)
        {
            if (await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM SCALLUSER WHERE USERID = @1", [upliner], ct: ct).ConfigureAwait(false) == 0)
            {
                return MaintenanceResult.Fail("Upliner tidak ditemukan");
            }

            if (await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM TEMP_DOWNLINE WHERE USERID = @1 AND DOWNLINE = @2", [userId, upliner], ct: ct).ConfigureAwait(false) > 0)
            {
                return MaintenanceResult.Fail("Recursive Upliner !");
            }
        }

        var after = new Dictionary<string, string?>
        {
            ["USERID"] = userId,
            ["GROUPID"] = Blank(draft.GroupId),
            ["SU_BRANCH_CODE"] = Blank(draft.BranchCode),
            ["SU_FULLNAME"] = Blank(draft.FullName),
            ["SU_HPNUM"] = Blank(draft.Phone),
            ["SU_EMAIL"] = Blank(draft.Email),
            ["SU_UPLINER"] = upliner,
            ["SU_ACTIVE"] = draft.Active ? "1" : "0",
        };
        var before = existing is null ? null : Fields.ToDictionary(f => f, f => ToText(existing[f]));
        try
        {
            await WritePendingAsync(db, userId, existing is null ? "Insert" : "Update", makerUserId, after, before, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFailed(logger, ex, "simpan ajuan", makerUserId, userId);
            return MaintenanceResult.Fail($"Ajuan gagal disimpan. Coba lagi atau hubungi administrator. {ErrorReference.Tag}");
        }

        LogPending(logger, makerUserId, existing is null ? "Insert" : "Update", userId);
        return MaintenanceResult.Ok;
    }

    public async Task<MaintenanceResult> DeleteToPendingAsync(string makerUserId, string userId, CancellationToken ct = default)
    {
        userId = userId.Trim();
        if (string.Equals(userId, makerUserId, StringComparison.OrdinalIgnoreCase))
        {
            return MaintenanceResult.Fail("Anda tidak dapat menghapus akun Anda sendiri");
        }

        var db = await Db(ct).ConfigureAwait(false);
        var existing = await db.QueryFirstOrDefaultAsync("SELECT USERID, GROUPID, SU_BRANCH_CODE, SU_FULLNAME, SU_HPNUM, SU_EMAIL, SU_UPLINER, SU_ACTIVE FROM SCALLUSER WHERE USERID = @1", [userId], ct: ct).ConfigureAwait(false);
        if (existing is null)
        {
            return MaintenanceResult.Fail("User tidak ditemukan");
        }

        if (await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM SCALLUSER WHERE SU_UPLINER = @1", [userId], ct: ct).ConfigureAwait(false) > 0)
        {
            return MaintenanceResult.Fail("User masih punya bawahan; pindahkan bawahannya terlebih dahulu");
        }

        var values = Fields.ToDictionary(f => f, f => ToText(existing[f]));
        try
        {
            await WritePendingAsync(db, userId, "Delete", makerUserId, values, values, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFailed(logger, ex, "ajukan hapus", makerUserId, userId);
            return MaintenanceResult.Fail($"Ajuan gagal disimpan. Coba lagi atau hubungi administrator. {ErrorReference.Tag}");
        }

        LogPending(logger, makerUserId, "Delete", userId);
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

        await DeletePendingAsync(db, temporaryId, ct).ConfigureAwait(false);
        return MaintenanceResult.Ok;
    }

    public async Task<MaintenanceResult> RejectAsync(string approverUserId, IReadOnlyList<Guid> temporaryIds, CancellationToken ct = default)
    {
        var db = await Db(ct).ConfigureAwait(false);
        foreach (var id in temporaryIds.Distinct())
        {
            await DeletePendingAsync(db, id, ct).ConfigureAwait(false);
            LogDecision(logger, approverUserId, "tolak", id);
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
                LogFailed(logger, ex, "setujui", approverUserId, id.ToString());
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

        if (!string.Equals(Cell(header["TABLENAME"]), Table, StringComparison.OrdinalIgnoreCase))
        {
            return "Ajuan bukan untuk data user";
        }

        var status = Cell(header["STATUS"]);
        var maker = Cell(header["CREATEDBY"]);
        if (options.Value.RequireDifferentApprover && string.Equals(maker, approver, StringComparison.OrdinalIgnoreCase))
        {
            return "Ajuan Anda sendiri tidak dapat Anda setujui (maker dan approver harus berbeda)";
        }

        var details = await db.QueryTableAsync("SELECT FIELDNAME, FIELDVALUE FROM PARAMETERSYSTEM_TEMPORARYDETAIL WHERE TEMPORARYID = @1", [id], ct: ct).ConfigureAwait(false);
        var v = details.Rows.Cast<System.Data.DataRow>().ToDictionary(r => Cell(r[0]).ToUpperInvariant(), r => r[1] is DBNull ? null : Convert.ToString(r[1], CultureInfo.InvariantCulture));
        var userId = v.GetValueOrDefault("USERID");
        if (string.IsNullOrWhiteSpace(userId))
        {
            return "Ajuan rusak (User ID kosong)";
        }

        if (status is "Insert" or "Update")
        {
            var upliner = Blank(v.GetValueOrDefault("SU_UPLINER"));
            if (upliner is not null
                && (string.Equals(upliner, userId, StringComparison.OrdinalIgnoreCase)
                    || await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM TEMP_DOWNLINE WHERE USERID = @1 AND DOWNLINE = @2", [userId, upliner], ct: ct).ConfigureAwait(false) > 0))
            {
                return $"Recursive Upliner in {userId} !";
            }

            // Satu transaksi: tulis SCALLUSER lalu SP_AUDITTRAIL (audit, password awal user baru, hapus ajuan).
            const string Sql = """
                SET XACT_ABORT ON; BEGIN TRAN;
                UPDATE SCALLUSER SET GROUPID = @2, SU_BRANCH_CODE = @3, SU_FULLNAME = @4, SU_HPNUM = @5, SU_EMAIL = @6, SU_UPLINER = @7, SU_ACTIVE = @8 WHERE USERID = @1;
                IF @@ROWCOUNT = 0
                    INSERT INTO SCALLUSER (USERID, GROUPID, SU_BRANCH_CODE, SU_FULLNAME, SU_HPNUM, SU_EMAIL, SU_UPLINER, SU_ACTIVE) VALUES (@1, @2, @3, @4, @5, @6, @7, @8);
                EXEC SP_AUDITTRAIL @9, @10;
                COMMIT TRAN
                """;
            await db.ExecuteAsync(Sql,
                [userId, Blank(v.GetValueOrDefault("GROUPID")), Blank(v.GetValueOrDefault("SU_BRANCH_CODE")), Blank(v.GetValueOrDefault("SU_FULLNAME")),
                 Blank(v.GetValueOrDefault("SU_HPNUM")), Blank(v.GetValueOrDefault("SU_EMAIL")), upliner, v.GetValueOrDefault("SU_ACTIVE") == "1", id, approver],
                new DbOptions(TimeoutSeconds: 120), ct).ConfigureAwait(false);
            return null;
        }

        if (status == "Delete")
        {
            const string Sql = """
                SET XACT_ABORT ON; BEGIN TRAN;
                IF (SELECT COUNT(*) FROM SCALLUSER WHERE USERID = @1) <> 1 RAISERROR ('User tidak ditemukan', 16, 1);
                DELETE SCALLUSER WHERE USERID = @1;
                EXEC SP_AUDITTRAIL @2, @3;
                COMMIT TRAN
                """;
            await db.ExecuteAsync(Sql, [userId, id, approver], new DbOptions(TimeoutSeconds: 120), ct).ConfigureAwait(false);
            return null;
        }

        return $"Status ajuan tidak dikenal: {status}";
    }

    public async Task<MaintenanceResult> UnlockLoginAsync(string actorUserId, string userId, CancellationToken ct = default)
    {
        var db = await Db(ct).ConfigureAwait(false);
        await db.ExecuteAsync("UPDATE SCALLUSERFLAG SET SU_LOGON = 0 WHERE USERID = @1", [userId.Trim()], ct: ct).ConfigureAwait(false);
        sessions.ReleaseAll(userId.Trim());
        LogAction(logger, actorUserId, "buka kunci login", userId);
        return MaintenanceResult.Ok;
    }

    public async Task<MaintenanceResult> ClearRevokeAsync(string actorUserId, string userId, CancellationToken ct = default)
    {
        var db = await Db(ct).ConfigureAwait(false);
        await db.ExecuteAsync("UPDATE SCALLUSERFLAG SET SU_REVOKE = 0 WHERE USERID = @1", [userId.Trim()], ct: ct).ConfigureAwait(false);
        LogAction(logger, actorUserId, "cabut revoke", userId);
        return MaintenanceResult.Ok;
    }

    public async Task<MaintenanceResult> ResetPasswordAsync(string actorUserId, string userId, CancellationToken ct = default)
    {
        var db = await Db(ct).ConfigureAwait(false);
        var rows = await db.ExecuteAsync("UPDATE SCALLUSER SET SU_PWD = L.DEF_PWD FROM SCALLUSER JOIN LOGINPARAM L ON 1 = 1 WHERE USERID = @1", [userId.Trim()], ct: ct).ConfigureAwait(false);
        if (rows == 0)
        {
            return MaintenanceResult.Fail("User tidak ditemukan");
        }

        LogAction(logger, actorUserId, "reset password", userId);
        return MaintenanceResult.Ok;
    }

    private static Task WritePendingAsync(IDb db, string userId, string status, string maker, IReadOnlyDictionary<string, string?> after, IReadOnlyDictionary<string, string?>? before, CancellationToken ct)
        => ParameterEngine.WritePendingAsync(db, Table, [new("USERID", userId)], status, maker, Fields, after, before, ct);

    private static Task DeletePendingAsync(IDb db, Guid id, CancellationToken ct) => ParameterEngine.DeletePendingAsync(db, id, ct);

    private static readonly Dictionary<string, string> Labels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["GROUPID"] = "Group", ["SU_BRANCH_CODE"] = "Cabang", ["SU_FULLNAME"] = "Nama", ["SU_HPNUM"] = "No. HP",
        ["SU_EMAIL"] = "Email", ["SU_UPLINER"] = "Upliner", ["SU_ACTIVE"] = "Aktif",
    };

    private static string DescribeChanges(IEnumerable<System.Data.DataRow> details)
        => string.Join("; ", details
            .Where(r => Labels.ContainsKey(Cell(r[1])) && Cell(r[2]) != Cell(r[3]))
            .Select(r => $"{Labels[Cell(r[1])]}: {Show(Cell(r[3]), Cell(r[1]))} → {Show(Cell(r[2]), Cell(r[1]))}"));

    private static string Show(string value, string field)
        => field.Equals("SU_ACTIVE", StringComparison.OrdinalIgnoreCase) ? (value == "1" ? "Ya" : "Tidak") : value.Length == 0 ? "(kosong)" : value;

    private static string? Blank(string? value) => ParameterEngine.Blank(value);

    private static string Cell(object? value) => ParameterEngine.Cell(value);

    private static string? ToText(object? value) => ParameterEngine.ToText(value);

    [LoggerMessage(Level = LogLevel.Information, Message = "Maintenance user: {Maker} mengajukan {Status} untuk {UserId}")]
    private static partial void LogPending(ILogger logger, string maker, string status, string userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Maintenance user: {Approver} {Decision} ajuan {TemporaryId}")]
    private static partial void LogDecision(ILogger logger, string approver, string decision, Guid temporaryId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Maintenance user: {Actor} {Action} untuk {UserId}")]
    private static partial void LogAction(ILogger logger, string actor, string action, string userId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Maintenance user gagal ({Action}): {Actor}, objek {Subject}")]
    private static partial void LogFailed(ILogger logger, Exception ex, string action, string actor, string subject);
}
