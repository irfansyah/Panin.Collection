using Panin.Application.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Panin.Application.Abstractions;
using Panin.Application.Collection;
using Panin.Application.Modules;
using Panin.Infrastructure.Data;

namespace Panin.Infrastructure.Collection;

/// <summary>
/// Layar Distribusi Debitur (menu <c>worklistmonitoring</c>, <c>WorklistMonitoring.ascx</c>).
/// <list type="bullet">
/// <item><c>SP_WORKLIST_MONITORING_TREE</c> ditulis ulang tanpa subquery berkorelasi dan tanpa HTML: di DB dev ukuran produksi
/// 0,77 s menjadi 0,03 s (user dengan 111 bawahan) dengan angka identik.</item>
/// <item><c>SP_WORKLIST_MONITORING_DEBITUR</c> membaca seluruh baris (19.344 untuk user teratas, penuh tautan HTML); di sini dibatasi
/// 2.000 baris terbaru plus jumlah total, tanpa HTML.</item>
/// <item><c>SP_REASIGN</c> tetap dipakai (bisnis di DB), tetapi dipanggil dalam transaksi, dengan otorisasi di server dan daftar
/// rekening dipecah agar tidak terpotong di batas <c>varchar(4000)</c>.</item>
/// </list>
/// </summary>
public sealed partial class WorklistService(IModuleDbFactory dbs, SingleFlightCache cache, ILogger<WorklistService> logger) : IWorklistService
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(60);

    /// <summary>Daftar debitur user teratas berat diurutkan; batasi bersamaan agar 700 user tidak menghantam DB sekaligus.</summary>
    private static readonly SemaphoreSlim Gate = new(6, 6);

    public const string TeamSql = """
        SELECT U.USERID AS UserId, LTRIM(RTRIM(U.FULL_NAME)) AS Name, G.GROUP_NAME AS [Group], RB.BRANCH_NAME AS Branch,
               ISNULL(S.Cnt, 0) AS DebitorCount
        FROM SCUSER U
        JOIN RFGROUP G ON G.GROUP_ID = U.[GROUP]
        LEFT JOIN RFBRANCH RB ON U.BRANCH_CODE = RB.BRANCH_CODE
        LEFT JOIN (
            SELECT X.USERID, COUNT(LM.ACC_NO) AS Cnt
            FROM (SELECT USERID, USERID AS OWNER_ID FROM SCUSER WHERE UPLINER = @1
                  UNION
                  SELECT USERID, DOWNLINE FROM TEMP_DOWNLINE WHERE USERID IN (SELECT USERID FROM SCUSER WHERE UPLINER = @1)) X
            JOIN LOAN_MASTER LM ON LM.USERID = X.OWNER_ID AND LM.CONFIRMED = '1'
            GROUP BY X.USERID) S ON S.USERID = U.USERID
        WHERE U.UPLINER = @1 AND U.ACTIVE = '1'
        ORDER BY Name
        """;

    public const string DebitorSql = """
        SELECT TOP (@2) AM.ACC_NO AS AccountNo, AM.CU_REF AS CustomerRef,
               LTRIM(RTRIM(ISNULL(CUST.CU_FIRSTNAME + ' ', '') + ISNULL(CUST.CU_MIDDLENAME + ' ', '') + ISNULL(CUST.CU_LASTNAME, ''))) AS Name,
               P.PRODUCTNAME AS Facility, LM.USER_ASSIGN_DATE AS AssignedOn,
               DATEDIFF(day, LM.USER_ASSIGN_DATE, GETDATE()) AS DaysHandled, LD.DPD AS Dpd,
               CASE ISNULL(NULLIF(LM.TREATMENT, ''), '00010') WHEN '00010' THEN 'Not Yet' WHEN '00017' THEN 'Done' ELSE 'On Process' END AS Action,
               RP.PARAM_DISPLAY AS Recommendation, RS.NAMA_STATUS AS AccountStatus,
               CASE WHEN ISNULL(LM.MANUALMOVE, '0') = '1' THEN CASE WHEN LM.DATE_MOVE_TO IS NULL THEN 'Permanent' ELSE 'Temporary' END ELSE 'No' END AS Reassign
        FROM ACCOUNT_MASTER AM
        INNER JOIN LOAN_DETAIL LD ON AM.ACC_NO = LD.ACC_NO
        INNER JOIN LOAN_MASTER LM ON AM.ACC_NO = LM.ACC_NO
        LEFT JOIN CUSTOMER CUST ON AM.CU_REF = CUST.CU_REF
        LEFT JOIN VW_RFPRODUCT P ON P.PRODUCTID = LM.LOAN_TYPE
        LEFT JOIN RFPARAMETER RP ON LM.NEXT_ACTION = RP.PARAM_ID
        LEFT JOIN RFSTATUS_REK RS ON AM.STATUS = RS.KODE_STATUS
        WHERE LM.USERID = @1
        ORDER BY LM.USER_ASSIGN_DATE DESC, AM.ACC_NO
        """;

    public const string DebitorCountSql = """
        SELECT COUNT(*) FROM ACCOUNT_MASTER AM
        INNER JOIN LOAN_DETAIL LD ON AM.ACC_NO = LD.ACC_NO
        INNER JOIN LOAN_MASTER LM ON AM.ACC_NO = LM.ACC_NO
        WHERE LM.USERID = @1
        """;

    /// <summary>Tujuan pindah: user aktif yang adalah diri sendiri atau bawahan (aturan layar lama).</summary>
    public const string TargetSql = """
        SELECT FULL_NAME FROM SCUSER
        WHERE USERID = @2 AND ACTIVE = '1'
          AND (USERID = @1 OR USERID IN (SELECT DOWNLINE FROM TEMP_DOWNLINE WHERE USERID = @1))
        """;

    /// <summary>Jumlah rekening di daftar yang BUKAN milik diri sendiri atau bawahan (harus 0).</summary>
    public const string ForeignAccountsSql = """
        SELECT COUNT(*) FROM LOAN_MASTER LM
        WHERE LM.ACC_NO IN (SELECT DATA FROM dbo.FNC_SPLIT_TO_TBL(@2, ','))
          AND NOT (ISNULL(LM.USERID, '') = @1 OR LM.USERID IN (SELECT DOWNLINE FROM TEMP_DOWNLINE WHERE USERID = @1))
        """;

    /// <summary>Pemilik rekening saat ini; daftar mereka berubah setelah pemindahan.</summary>
    public const string OwnersSql = "SELECT DISTINCT USERID FROM LOAN_MASTER WHERE ACC_NO IN (SELECT DATA FROM dbo.FNC_SPLIT_TO_TBL(@1, ',')) AND USERID IS NOT NULL";

    public async Task<bool> CanViewAsync(string viewerUserId, string targetUserId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(viewerUserId) || string.IsNullOrWhiteSpace(targetUserId))
        {
            return false;
        }

        var db = await Db(ct).ConfigureAwait(false);
        return await db.ExecuteScalarAsync<int>(AccountMonitoringService.CanViewSql, [viewerUserId.Trim(), targetUserId.Trim()], ct: ct).ConfigureAwait(false) == 1;
    }

    public async Task<IReadOnlyList<WorklistTeamMember>> GetTeamAsync(string userId, CancellationToken ct = default)
        => await cache.GetOrCreateAsync(TeamKey(userId), CacheFor, async () =>
        {
            var db = await Db().ConfigureAwait(false);
            return (IReadOnlyList<WorklistTeamMember>?)await db.QueryAsync<WorklistTeamMember>(TeamSql, [userId], new DbOptions(TimeoutSeconds: 60)).ConfigureAwait(false);
        }).ConfigureAwait(false) ?? [];

    public async Task<WorklistDebitorResult> GetDebitorsAsync(string userId, CancellationToken ct = default)
        => await cache.GetOrCreateAsync(DebitorKey(userId), CacheFor, async () =>
        {
            await Gate.WaitAsync().ConfigureAwait(false);
            try
            {
                var db = await Db().ConfigureAwait(false);
                var options = new DbOptions(TimeoutSeconds: 120);
                var rows = db.QueryAsync<WorklistDebitorRow>(DebitorSql, [userId, WorklistRules.MaxDebitors], options);
                var total = db.ExecuteScalarAsync<int>(DebitorCountSql, [userId], options);
                await Task.WhenAll(rows, total).ConfigureAwait(false);
                return (WorklistDebitorResult?)new WorklistDebitorResult(rows.Result, total.Result, WorklistRules.MaxDebitors);
            }
            finally
            {
                Gate.Release();
            }
        }).ConfigureAwait(false) ?? new WorklistDebitorResult([], 0, WorklistRules.MaxDebitors);

    public async Task<ReassignResult> ReassignAsync(string actorUserId, ReassignRequest request, CancellationToken ct = default)
    {
        var errors = WorklistRules.Validate(request, DateTime.Today);
        if (errors.Count > 0)
        {
            return new ReassignResult(false, errors);
        }

        var db = await Db(ct).ConfigureAwait(false);
        var target = await db.QueryFirstOrDefaultAsync(TargetSql, [actorUserId, request.TargetUserId.Trim()], ct: ct).ConfigureAwait(false);
        if (target is null)
        {
            return ReassignResult.Fail($"User ID: {request.TargetUserId} tidak ditemukan, tidak aktif, atau bukan bawahan Anda");
        }

        foreach (var group in request.GroupUserIds)
        {
            if (!await CanViewAsync(actorUserId, group, ct).ConfigureAwait(false))
            {
                LogDenied(logger, actorUserId, group);
                return ReassignResult.Fail("Ada user terpilih yang bukan bawahan Anda.");
            }
        }

        var chunks = request.AccountNos.Distinct(StringComparer.OrdinalIgnoreCase).Chunk(WorklistRules.AccountsPerCall).Select(c => "''," + string.Join(',', c)).ToList();
        var owners = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var chunk in chunks)
        {
            if (await db.ExecuteScalarAsync<int>(ForeignAccountsSql, [actorUserId, chunk], ct: ct).ConfigureAwait(false) > 0)
            {
                LogDenied(logger, actorUserId, "rekening");
                return ReassignResult.Fail("Ada rekening terpilih yang bukan milik Anda atau bawahan Anda.");
            }

            var table = await db.QueryTableAsync(OwnersSql, [chunk], ct: ct).ConfigureAwait(false);
            owners.UnionWith(table.Rows.Cast<System.Data.DataRow>().Select(r => Convert.ToString(r[0], CultureInfo.InvariantCulture)!.Trim()));
        }

        try
        {
            var (sql, args) = BuildReassign(actorUserId, request, chunks);
            await db.ExecuteAsync(sql, args, new DbOptions(TimeoutSeconds: 300), ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFailed(logger, ex, actorUserId, request.TargetUserId);
            return ReassignResult.Fail($"Pemindahan gagal. Tidak ada perubahan yang tercatat; coba lagi atau hubungi administrator. {ErrorReference.Tag}");
        }

        // Daftar tim/debitur milik pemindah dan penerima sudah berubah.
        foreach (var user in new[] { actorUserId, request.TargetUserId.Trim() }.Concat(request.GroupUserIds).Concat(owners))
        {
            cache.Remove(TeamKey(user));
            cache.Remove(DebitorKey(user));
        }

        LogMoved(logger, actorUserId, request.TargetUserId, request.AccountNos.Count, request.GroupUserIds.Count);
        return new ReassignResult(true, [], Convert.ToString(target[0])?.Trim());
    }

    /// <summary>
    /// Satu transaksi berisi satu <c>SP_REASIGN</c> per kelompok rekening. Kelompok user hanya ikut panggilan pertama.
    /// Format daftar ("''," di depan) mengikuti layar lama.
    /// </summary>
    public static (string Sql, object?[] Args) BuildReassign(string actorUserId, ReassignRequest r, IReadOnlyList<string> accountChunks)
    {
        var group = "''" + string.Concat(r.GroupUserIds.Select(g => "," + g.Trim()));
        var args = new List<object?> { r.TargetUserId.Trim(), group, actorUserId, r.Permanent ? null : r.Until?.Date, "''" };
        var calls = accountChunks.Count == 0 ? ["''"] : accountChunks;
        var sql = new System.Text.StringBuilder("SET XACT_ABORT ON; BEGIN TRAN; ");
        for (var i = 0; i < calls.Count; i++)
        {
            args.Add(calls[i]);
            sql.Append(CultureInfo.InvariantCulture, $"exec SP_REASIGN @1, {(i == 0 ? "@2" : "@5")}, @{args.Count}, @3, NULL, @4; ");
        }

        sql.Append("COMMIT TRAN");
        return (sql.ToString(), [.. args]);
    }

    private static readonly SemaphoreSlim RedistributionGate = new(1, 1);

    public const string RedistributionLock = "PANIN_WORKLIST_REDISTRIBUTE";

    // Lingkup rekening yang diperiksa sebelum/sesudah SP untuk menghitung yang berpindah. Template juga menyentuh rekening tanpa pemilik atau
    // berpemilik user yang sudah tidak ada/aktif (SP melepas lalu menugaskan ulang), jadi ikut dihitung.
    private const string TemplateScope = """
        USERID IN (SELECT DOWNLINE FROM TEMP_DOWNLINE WHERE USERID = @1 UNION SELECT @1)
        OR USERID IS NULL
        OR USERID NOT IN (SELECT USERID FROM SCUSER)
        OR USERID IN (SELECT USERID FROM SCUSER WHERE ISNULL(ACTIVE, 0) = 0)
        """;

    private const string IndexScope = "USERID IN (SELECT USERID FROM SCUSER WHERE UPLINER = @1 UNION SELECT @1)";

    /// <summary>
    /// SP lama tidak memakai transaksi dan mengubah ribuan baris bertahap; di sini dibungkus satu transaksi (gagal di tengah = batal semua),
    /// dikunci agar tidak dua kali sekaligus, dan menghitung rekening yang berpindah. Parameter: @1 user, @2 pratinjau (1/0).
    /// </summary>
    public static string BuildRedistribute(RedistributionKind kind)
    {
        var (procedure, scope) = kind == RedistributionKind.Template
            ? ("SP_WORKLIST_DISTRIBUTION_TEMPLATE", TemplateScope)
            : ("SP_WORKLIST_DISTRIBUTION_INDEX", IndexScope);
        return $"""
            SET XACT_ABORT ON;
            DECLARE @r int, @moved int;
            BEGIN TRAN;
            EXEC @r = sp_getapplock @Resource = N'{RedistributionLock}', @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 0;
            IF @r < 0
            BEGIN
                ROLLBACK TRAN;
                SELECT -1;
                RETURN;
            END
            SELECT ACC_NO, USERID INTO #before FROM LOAN_MASTER WHERE {scope};
            EXEC {procedure} @1;
            SELECT @moved = COUNT(*) FROM LOAN_MASTER L JOIN #before B ON B.ACC_NO = L.ACC_NO WHERE ISNULL(L.USERID, '') <> ISNULL(B.USERID, '');
            IF @2 = 1 ROLLBACK TRAN ELSE COMMIT TRAN;
            SELECT @moved;
            """;
    }

    public async Task<RedistributionResult> RedistributeAsync(string actorUserId, RedistributionKind kind, string targetUserId, bool dryRun, CancellationToken ct = default)
    {
        var user = kind == RedistributionKind.Template ? actorUserId : targetUserId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(actorUserId) || string.IsNullOrWhiteSpace(user) || !await CanViewAsync(actorUserId, user, ct).ConfigureAwait(false))
        {
            LogDenied(logger, actorUserId, user);
            return RedistributionResult.Fail("Anda tidak berwenang melakukan redistribusi untuk user tersebut.");
        }

        if (!await RedistributionGate.WaitAsync(0, ct).ConfigureAwait(false))
        {
            return RedistributionResult.Fail("Redistribusi lain sedang berjalan. Coba lagi beberapa saat.");
        }

        try
        {
            var db = await Db(ct).ConfigureAwait(false);
            int moved;
            try
            {
                moved = await db.ExecuteScalarAsync<int>(BuildRedistribute(kind), [user, dryRun ? 1 : 0], new DbOptions(TimeoutSeconds: 900), ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogRedistributionFailed(logger, ex, actorUserId, kind.ToString(), user);
                return RedistributionResult.Fail($"Redistribusi gagal. Tidak ada perubahan yang tercatat; coba lagi atau hubungi administrator. {ErrorReference.Tag}");
            }

            if (moved < 0)
            {
                return RedistributionResult.Fail("Redistribusi lain sedang berjalan di server. Coba lagi beberapa saat.");
            }

            if (!dryRun)
            {
                var affected = await db.QueryTableAsync("SELECT USERID FROM SCUSER WHERE USERID = @1 OR USERID IN (SELECT DOWNLINE FROM TEMP_DOWNLINE WHERE USERID = @1)", [user], ct: ct).ConfigureAwait(false);
                foreach (var id in affected.Rows.Cast<System.Data.DataRow>().Select(r => Convert.ToString(r[0], CultureInfo.InvariantCulture)!.Trim()))
                {
                    cache.Remove(TeamKey(id));
                    cache.Remove(DebitorKey(id));
                }
            }

            LogRedistributed(logger, actorUserId, kind.ToString(), user, moved, dryRun);
            return new RedistributionResult(true, moved, dryRun, []);
        }
        finally
        {
            RedistributionGate.Release();
        }
    }

    private static string TeamKey(string userId) => $"wl-team:{userId}";

    private static string DebitorKey(string userId) => $"wl-deb:{userId}";

    private Task<IDb> Db(CancellationToken ct = default) => dbs.GetDataDbAsync("60", ct);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Pemindahan debitur ditolak (di luar bawahan): user {UserId}, objek {Subject}")]
    private static partial void LogDenied(ILogger logger, string userId, string subject);

    [LoggerMessage(Level = LogLevel.Error, Message = "Pemindahan debitur gagal: user {UserId} ke {Target}")]
    private static partial void LogFailed(ILogger logger, Exception ex, string userId, string target);

    [LoggerMessage(Level = LogLevel.Error, Message = "Redistribusi gagal: oleh {UserId}, {Kind} untuk {Target}")]
    private static partial void LogRedistributionFailed(ILogger logger, Exception ex, string userId, string kind, string target);

    [LoggerMessage(Level = LogLevel.Information, Message = "Redistribusi {Kind} oleh {UserId} untuk {Target}: {Moved} rekening berpindah (pratinjau={DryRun})")]
    private static partial void LogRedistributed(ILogger logger, string userId, string kind, string target, int moved, bool dryRun);

    [LoggerMessage(Level = LogLevel.Information, Message = "Debitur dipindahkan: oleh {UserId} ke {Target}, {Accounts} rekening, {Groups} user")]
    private static partial void LogMoved(ILogger logger, string userId, string target, int accounts, int groups);
}
