using System.Data;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Panin.Application.Abstractions;
using Panin.Application.Collection;
using Panin.Application.Diagnostics;
using Panin.Application.Modules;
using Panin.Infrastructure.Export;

namespace Panin.Infrastructure.Collection;

/// <summary>
/// Generate Excel agen visit (tombol di <c>WorklistMonitoring.ascx</c>, hanya untuk group dengan treatment <c>00002</c>).
/// Layar lama memanggil <c>SP_GENERATE_EXCEL_VISIT</c>/<c>_VISIT2</c>/<c>_VISIT4</c> satu per satu tanpa transaksi (nomor slip terpakai walau
/// berkas gagal, dan dua pengguna bersamaan bisa menerima nomor slip yang sama karena SP membaca nomor terakhir lalu menambah 1), lalu memindahkan
/// debitur. Di sini: otorisasi sama dengan pemindahan biasa, semua SP dalam satu transaksi dengan kunci <c>sp_getapplock</c>, daftar dipecah agar muat
/// di parameter SP, pemindahan dilakukan sesudah berkas jadi, dan nomor slip dibatalkan bila berkas gagal dibuat.
/// </summary>
public sealed partial class VisitAgencyService(
    IModuleDbFactory dbs,
    IWorklistService worklist,
    IOptions<VisitAgencyOptions> options,
    ILogger<VisitAgencyService> logger) : IVisitAgencyService
{
    private const string Treatment = "00002";
    private const int TablesPerChunk = 12;
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public const string AllowedSql = "SELECT 1 FROM dbo.GRPACCESSTREATMENT GT JOIN SCUSER U ON U.[GROUP] = GT.GROUPID WHERE U.USERID = @1 AND GT.TREATMENT = @2";

    // Lingkup yang sama dengan SP: user terpilih beserta bawahannya, atau rekening terpilih.
    public const string ScopeSql = """
        SELECT DISTINCT TOP (@3) LM.ACC_NO FROM LOAN_MASTER LM
        WHERE LM.USERID IN (SELECT DOWNLINE FROM TEMP_DOWNLINE WHERE USERID IN (SELECT DATA FROM DBO.FNC_SPLIT_TO_TBL(@1, ',')))
           OR LM.USERID IN (SELECT DATA FROM DBO.FNC_SPLIT_TO_TBL(@1, ','))
           OR LM.ACC_NO IN (SELECT DATA FROM DBO.FNC_SPLIT_TO_TBL(@2, ','))
        """;

    public async Task<bool> IsAllowedAsync(string userId, CancellationToken ct = default)
    {
        var db = await dbs.GetDataDbAsync("60", ct).ConfigureAwait(false);
        return !string.IsNullOrWhiteSpace(userId) && await db.QueryFirstOrDefaultAsync(AllowedSql, [userId.Trim(), Treatment], ct: ct).ConfigureAwait(false) is not null;
    }

    public async Task<VisitExcelResult> GenerateAsync(string actorUserId, ReassignRequest request, CancellationToken ct = default)
    {
        request.Permanent = true;
        if (!await IsAllowedAsync(actorUserId, ct).ConfigureAwait(false))
        {
            LogDenied(logger, actorUserId);
            return VisitExcelResult.Fail("Group Anda tidak berhak membuat Excel agen visit.");
        }

        var check = await worklist.ValidateSelectionAsync(actorUserId, request, ct).ConfigureAwait(false);
        if (!check.Ok)
        {
            return new VisitExcelResult(false, null, null, 0, check.Errors, []);
        }

        var db = await dbs.GetDataDbAsync("60", ct).ConfigureAwait(false);
        var max = options.Value.MaxAccounts;
        var groups = "''" + string.Concat(request.GroupUserIds.Select(g => "," + g.Trim()));
        var chunks = check.AccountChunks.Count == 0 ? ["''"] : check.AccountChunks;

        // Hitung lingkup lebih dulu: berkas terlalu besar atau kosong ditolak sebelum nomor slip dipakai.
        var scope = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < chunks.Count; i++)
        {
            var table = await db.QueryTableAsync(ScopeSql, [i == 0 ? groups : "''", chunks[i], max + 1], new DbOptions(TimeoutSeconds: 120), ct).ConfigureAwait(false);
            scope.UnionWith(table.Rows.Cast<DataRow>().Select(r => Convert.ToString(r[0], CultureInfo.InvariantCulture)!.Trim()));
        }

        if (scope.Count == 0)
        {
            return VisitExcelResult.Fail("Tidak ada debitur pada pilihan Anda.");
        }

        if (scope.Count > max)
        {
            return VisitExcelResult.Fail($"Pilihan memuat lebih dari {max:N0} debitur; satu berkas Excel dibatasi {max:N0}. Pilih lebih sedikit.");
        }

        if (!await Gate.WaitAsync(0, ct).ConfigureAwait(false))
        {
            return VisitExcelResult.Fail("Pembuatan Excel agen visit lain sedang berjalan. Coba lagi beberapa saat.");
        }

        var actId = Guid.NewGuid();
        var committed = false;
        try
        {
            DataSet set;
            try
            {
                var (sql, args) = BuildBatch(request.TargetUserId.Trim(), actorUserId, actId, groups, chunks);
                set = await db.QueryDataSetAsync(sql, args, new DbOptions(TimeoutSeconds: 900), ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFailed(logger, ex, actorUserId, request.TargetUserId);
                return VisitExcelResult.Fail($"Pembuatan Excel gagal. Tidak ada perubahan yang tercatat; coba lagi atau hubungi administrator. {ErrorReference.Tag}");
            }

            if (set.Tables.Count == 1)
            {
                return VisitExcelResult.Fail("Pembuatan Excel lain sedang berjalan di server. Coba lagi beberapa saat.");
            }

            committed = true;
            if (set.Tables.Count != TablesPerChunk * chunks.Count)
            {
                throw new InvalidOperationException($"SP agen visit mengembalikan {set.Tables.Count} result set, diharapkan {TablesPerChunk * chunks.Count}.");
            }

            byte[] content;
            try
            {
                var data = Merge(set, chunks.Count);
                await using var template = File.OpenRead(TemplatePath());
                content = await Task.Run(() => VisitWorkbookBuilder.Build(template, data, options.Value.ProtectionPassword), ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFailed(logger, ex, actorUserId, request.TargetUserId);
                await UndoSlipsAsync(db, actId).ConfigureAwait(false);
                return VisitExcelResult.Fail($"Berkas Excel gagal dibuat; nomor slip dibatalkan. Coba lagi atau hubungi administrator. {ErrorReference.Tag}");
            }

            var warnings = new List<string>();
            var moved = await worklist.ReassignAsync(actorUserId, request, ct).ConfigureAwait(false);
            if (!moved.Success)
            {
                warnings.Add("Berkas dibuat tetapi debitur belum dipindahkan: " + string.Join("; ", moved.Errors));
            }

            LogGenerated(logger, actorUserId, request.TargetUserId, scope.Count, moved.Success);
            return new VisitExcelResult(true, $"Visit_{request.TargetUserId.Trim()}.xls", content, scope.Count, [], warnings);
        }
        catch (Exception ex) when (ex is not OperationCanceledException && committed)
        {
            LogFailed(logger, ex, actorUserId, request.TargetUserId);
            await UndoSlipsAsync(db, actId).ConfigureAwait(false);
            return VisitExcelResult.Fail($"Pembuatan Excel gagal; nomor slip dibatalkan. Hubungi administrator. {ErrorReference.Tag}");
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>
    /// Satu batch: kunci, lalu tiga SP untuk setiap potongan rekening (kelompok user hanya ikut potongan pertama), semua dalam satu transaksi.
    /// Parameter: @1 user tujuan, @2 pemindah, @3 id aksi, @4 kelompok pertama, @5 kelompok kosong, @6.. potongan rekening.
    /// </summary>
    public static (string Sql, object?[] Args) BuildBatch(string targetUserId, string actorUserId, Guid actId, string groups, IReadOnlyList<string> accountChunks)
    {
        var args = new List<object?> { targetUserId, actorUserId, actId, groups, "''" };
        var sql = new StringBuilder("""
            SET XACT_ABORT ON;
            DECLARE @r int;
            BEGIN TRAN;
            EXEC @r = sp_getapplock @Resource = N'PANIN_VISIT_SLIP', @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 30000;
            IF @r < 0
            BEGIN
                ROLLBACK TRAN;
                SELECT -1 AS Locked;
                RETURN;
            END

            """);
        for (var i = 0; i < accountChunks.Count; i++)
        {
            args.Add(accountChunks[i]);
            var g = i == 0 ? "@4" : "@5";
            var a = "@" + args.Count.ToString(CultureInfo.InvariantCulture);
            sql.Append(CultureInfo.InvariantCulture, $"EXEC SP_GENERATE_EXCEL_VISIT @1, {g}, {a}, @2, @3;\n");
            sql.Append(CultureInfo.InvariantCulture, $"EXEC SP_GENERATE_EXCEL_VISIT2 @1, {g}, {a}, @2;\n");
            sql.Append(CultureInfo.InvariantCulture, $"EXEC SP_GENERATE_EXCEL_VISIT4 @1, {g}, {a}, @2, @3;\n");
        }

        sql.Append("COMMIT TRAN;");
        return (sql.ToString(), [.. args]);
    }

    /// <summary>Menggabungkan hasil per potongan: baris yang sama (rekening tercantum lewat kelompok user dan lewat daftar) hanya satu kali.</summary>
    public static VisitWorkbookData Merge(DataSet set, int chunks)
    {
        DataTable Union(int offset, Func<DataRow, string> key)
        {
            var result = set.Tables[offset]!.Clone();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var c = 0; c < chunks; c++)
            {
                foreach (DataRow row in set.Tables[c * TablesPerChunk + offset]!.Rows)
                {
                    if (seen.Add(key(row)))
                    {
                        result.ImportRow(row);
                    }
                }
            }

            return result;
        }

        // Urutan result set per potongan: 0 data visit, 1 sel daftar, 2–5 daftar pilihan, 6 sel area, 7 surat, 8 sel area rincian, 9 rincian, 10 sel slip, 11 slip.
        static string Str(DataRow r, int col) => Convert.ToString(r[col], CultureInfo.InvariantCulture)?.Trim() ?? string.Empty;
        var visit = Union(0, r => Str(r, 0));
        var letter = Union(7, r => Str(r, r.Table.Columns.IndexOf("ACC_NO")));
        var detail = Union(9, r => Str(r, r.Table.Columns.IndexOf("ACC_NO")) + "|" + Str(r, r.Table.Columns.IndexOf("MONTH_DUE")));
        var slip = Union(11, r => Str(r, r.Table.Columns.IndexOf("ACC_NO")));

        IReadOnlyList<string> List(int table) =>
            [.. set.Tables[table]!.Rows.Cast<DataRow>().Select(r => $"{Str(r, 0)} - {Str(r, 1)}")];
        return new VisitWorkbookData(visit, [List(2), List(3), List(4), List(5)], letter, detail, slip);
    }

    private string TemplatePath()
    {
        var path = options.Value.TemplatePath;
        return Path.IsPathRooted(path) ? path : Path.Combine(AppContext.BaseDirectory, path);
    }

    private static async Task UndoSlipsAsync(IDb db, Guid actId)
    {
        try
        {
            await db.ExecuteAsync("DELETE FROM SLIP_HISTORY WHERE ACT_ID = @1", [actId], new DbOptions(TimeoutSeconds: 60)).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // pembatalan nomor slip hanya upaya terbaik; nomor yang tersisa tidak merusak data
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Generate Excel agen visit ditolak (tanpa hak treatment): user {UserId}")]
    private static partial void LogDenied(ILogger logger, string userId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Generate Excel agen visit gagal: user {UserId} untuk {Target}")]
    private static partial void LogFailed(ILogger logger, Exception ex, string userId, string target);

    [LoggerMessage(Level = LogLevel.Information, Message = "Excel agen visit dibuat: oleh {UserId} untuk {Target}, {Accounts} debitur, dipindahkan={Moved}")]
    private static partial void LogGenerated(ILogger logger, string userId, string target, int accounts, bool moved);
}
