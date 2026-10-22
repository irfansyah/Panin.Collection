using System.Data;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Panin.Application.Abstractions;
using Panin.Application.Collection;
using Panin.Application.Diagnostics;
using Panin.Application.Letters;
using Panin.Application.Modules;
using Panin.Infrastructure.Data;
using Panin.Infrastructure.Export;

namespace Panin.Infrastructure.Letters;

/// <summary>
/// Bulk Surat (<c>Facilities/BulkSurat.aspx</c>). Bisnis tetap di SP lama (<c>SP_GENERATE_BULK</c> membuat nomor surat per cabang,
/// <c>SP_BULK_FINISH</c>, <c>SP_SAVE_SURAT_RESULT</c>); yang diganti adalah pembuatan Excel (NPOI, tanpa Office) dan penjagaannya:
/// pembangkitan nomor berjalan dalam satu transaksi dengan kunci aplikasi (SP lama membaca dan menaikkan urutan nomor cabang tanpa
/// kunci, sehingga dua proses bersamaan bisa menghasilkan nomor kembar), berkas hanya bisa diunduh pemiliknya, dan hasil surat hanya
/// bisa diinput untuk surat dari bulk milik sendiri.
/// </summary>
public sealed partial class BulkLetterService(
    IModuleDbFactory dbs,
    SingleFlightCache cache,
    IOptions<BulkLetterOptions> options,
    ILogger<BulkLetterService> logger) : IBulkLetterService
{
    /// <summary>Pembangkitan nomor surat dibatasi satu per satu di server ini; SP lama sudah membatasi 100 surat per proses.</summary>
    private static readonly SemaphoreSlim GenerateGate = new(1, 1);
    private static readonly TimeSpan ReferenceCache = TimeSpan.FromMinutes(10);

    /// <summary>Kunci aplikasi di DB agar tidak ada dua pembangkitan nomor surat sekaligus (lintas sirkuit dan lintas instance aplikasi baru).</summary>
    public const string GenerateSql = """
        SET XACT_ABORT ON; BEGIN TRAN;
        DECLARE @lock int;
        EXEC @lock = sp_getapplock @Resource = N'PANIN_BULK_SURAT', @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 120000;
        IF @lock < 0 RAISERROR ('Proses bulk surat lain sedang berjalan; coba lagi sebentar lagi', 16, 1);
        EXEC SP_GENERATE_BULK @1;
        COMMIT TRAN
        """;

    public const string BulksSql = """
        SELECT TOP (100) B.BULKID AS BulkId, B.BULKFILE AS BulkFile, B.BULKDATE AS BulkDate, ISNULL(B.BULKFINISH, 0) AS Finished,
               (SELECT COUNT(*) FROM SURAT_HISTORY S WHERE S.BULKID = B.BULKID) AS Total
        FROM BULK_SURAT B WHERE B.BULKBY = @1 ORDER BY B.BULKDATE DESC
        """;

    private Task<IDb> Db(CancellationToken ct = default) => dbs.GetDataDbAsync("60", ct);

    private static string PendingKey(string userId) => $"bulk-pending:{userId}";

    public async Task<IReadOnlyList<PendingLetterGroup>> GetPendingAsync(string userId, CancellationToken ct = default)
        => await cache.GetOrCreateAsync(PendingKey(userId), TimeSpan.FromSeconds(30), async () =>
        {
            var db = await Db().ConfigureAwait(false);
            var table = await db.QueryTableAsync("exec SP_LIST_BULK_GENERATE @1", [userId], new DbOptions(TimeoutSeconds: 120)).ConfigureAwait(false);
            return (IReadOnlyList<PendingLetterGroup>?)[.. table.Rows.Cast<DataRow>().Select(r => new PendingLetterGroup(
                Text(r, 0), Text(r, 1), Text(r, 2), Convert.ToInt32(r[3], CultureInfo.InvariantCulture)))];
        }).ConfigureAwait(false) ?? [];

    public async Task<IReadOnlyList<UnconfirmedLetter>> GetUnconfirmedAsync(string userId, CancellationToken ct = default)
    {
        var db = await Db(ct).ConfigureAwait(false);
        var table = await db.QueryTableAsync("exec SP_LIST_SURAT_UNCONFIRMED @1", [userId], new DbOptions(TimeoutSeconds: 120), ct).ConfigureAwait(false);
        return [.. table.Rows.Cast<DataRow>().Take(2000).Select(r => new UnconfirmedLetter(
            Text(r, 0) ?? string.Empty, Text(r, 1) ?? string.Empty, Text(r, 2) ?? string.Empty,
            r[3] as DateTime?, Text(r, 4), Text(r, 5)))];
    }

    public async Task<IReadOnlyList<BulkRow>> GetBulksAsync(string userId, CancellationToken ct = default)
    {
        var db = await Db(ct).ConfigureAwait(false);
        var table = await db.QueryTableAsync(BulksSql, [userId], ct: ct).ConfigureAwait(false);
        return [.. table.Rows.Cast<DataRow>().Select(r =>
        {
            var name = SafeFileName(Text(r, 1));
            return new BulkRow((Guid)r[0], name is null ? string.Empty : Path.GetFileNameWithoutExtension(name), (DateTime)r[2],
                Convert.ToInt32(r[4], CultureInfo.InvariantCulture), Convert.ToBoolean(r[3], CultureInfo.InvariantCulture),
                name is not null && File.Exists(PathFor(name)) ? name : null);
        })];
    }

    public async Task<BulkGenerateResult> GenerateAsync(string userId, CancellationToken ct = default)
    {
        await GenerateGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var db = await Db(ct).ConfigureAwait(false);
            DataSet set;
            try
            {
                set = await db.QueryDataSetAsync(GenerateSql, [userId], new DbOptions(TimeoutSeconds: 600), ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFailed(logger, ex, "buat nomor", userId);
                return BulkGenerateResult.Fail($"Pembuatan bulk surat gagal; tidak ada nomor yang tercatat. Coba lagi atau hubungi administrator. {ErrorReference.Tag}");
            }

            cache.Remove(PendingKey(userId));
            if (set.Tables.Count < 3 || set.Tables[0].Rows.Count == 0)
            {
                return BulkGenerateResult.Fail("Tidak ada surat yang perlu digenerate.");
            }

            return await RenderAndFinishAsync(db, userId, set, ct).ConfigureAwait(false);
        }
        finally
        {
            GenerateGate.Release();
        }
    }

    public async Task<BulkGenerateResult> RegenerateAsync(string userId, Guid bulkId, CancellationToken ct = default)
    {
        var db = await Db(ct).ConfigureAwait(false);
        if (!await OwnsBulkAsync(db, userId, bulkId, ct).ConfigureAwait(false))
        {
            return BulkGenerateResult.Fail("Bulk tidak ditemukan.");
        }

        DataSet set;
        try
        {
            set = await db.QueryDataSetAsync("exec SP_REGENERATE_BULK @1", [bulkId], new DbOptions(TimeoutSeconds: 600), ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFailed(logger, ex, "regenerate", userId);
            return BulkGenerateResult.Fail($"Regenerate gagal. Coba lagi atau hubungi administrator. {ErrorReference.Tag}");
        }

        return set.Tables.Count < 3 || set.Tables[0].Rows.Count == 0
            ? BulkGenerateResult.Fail("Data bulk tidak ditemukan.")
            : await RenderAndFinishAsync(db, userId, set, ct).ConfigureAwait(false);
    }

    private async Task<BulkGenerateResult> RenderAndFinishAsync(IDb db, string userId, DataSet set, CancellationToken ct)
    {
        var head = set.Tables[0].Rows[0];
        var bulkId = (Guid)head[0];
        var fileName = SafeFileName(Text(head, 2));
        var templateName = SafeFileName(Text(head, 1));
        if (fileName is null || templateName is null)
        {
            return BulkGenerateResult.Fail($"Nama berkas bulk tidak valid. Hubungi administrator. {ErrorReference.Tag}");
        }

        try
        {
            var sheets = set.Tables[1].Rows.Cast<DataRow>().Select(r => new LetterSheet(Text(r, "SU_TYPE") ?? string.Empty, Text(r, "SU_SHEET") ?? string.Empty)).ToList();
            var detail = set.Tables[2];
            var rowsByType = detail.Rows.Cast<DataRow>()
                .GroupBy(r => Text(r, "SU_TYPE") ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    g => g.Key,
                    g => (IReadOnlyList<IReadOnlyDictionary<string, object?>>)[.. g.Select(r => (IReadOnlyDictionary<string, object?>)detail.Columns.Cast<DataColumn>()
                        .ToDictionary(c => c.ColumnName, c => r[c] is DBNull ? null : r[c], StringComparer.OrdinalIgnoreCase))],
                    StringComparer.OrdinalIgnoreCase);

            var templatePath = Path.Combine(Resolve(options.Value.TemplateDirectory), templateName);
            if (!File.Exists(templatePath))
            {
                return BulkGenerateResult.Fail($"Template {templateName} tidak ditemukan di server. Nomor surat sudah tercatat; setelah template dipasang gunakan Regenerate. {ErrorReference.Tag}");
            }

            byte[] bytes;
            await using (var template = File.OpenRead(templatePath))
            {
                bytes = LetterWorkbookBuilder.Build(template, sheets, rowsByType, options.Value.ProtectionPassword);
            }

            Directory.CreateDirectory(Resolve(options.Value.OutputDirectory));
            var target = PathFor(fileName);
            var temp = target + ".tmp";
            await File.WriteAllBytesAsync(temp, bytes, ct).ConfigureAwait(false);
            File.Move(temp, target, overwrite: true);

            await db.ExecuteAsync("exec SP_BULK_FINISH @1", [bulkId], ct: ct).ConfigureAwait(false);
            LogGenerated(logger, userId, bulkId, detail.Rows.Count, fileName);
            return new BulkGenerateResult(true, $"Bulk surat dibuat: {detail.Rows.Count} surat.", bulkId, detail.Rows.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFailed(logger, ex, "susun Excel", userId);
            return BulkGenerateResult.Fail($"Nomor surat sudah tercatat tetapi berkas Excel gagal dibuat. Gunakan Regenerate pada bulk ini. {ErrorReference.Tag}");
        }
    }

    public async Task<BulkFile?> OpenFileAsync(string userId, Guid bulkId, CancellationToken ct = default)
    {
        var db = await Db(ct).ConfigureAwait(false);
        var file = await db.ExecuteScalarAsync<string>("SELECT BULKFILE FROM BULK_SURAT WHERE BULKID = @1 AND BULKBY = @2", [bulkId, userId], ct: ct).ConfigureAwait(false);
        var name = SafeFileName(file);
        if (name is null || !File.Exists(PathFor(name)))
        {
            return null;
        }

        return new BulkFile(new FileStream(PathFor(name), FileMode.Open, FileAccess.Read, FileShare.Read), name);
    }

    public async Task<IReadOnlyList<LookupItem>> GetResultOptionsAsync(CancellationToken ct = default)
        => await Reference("bulk-result", "exec SP_GET_RESULT @1", ["Letter"]).ConfigureAwait(false);

    public async Task<IReadOnlyList<LookupItem>> GetReceiverRelationsAsync(CancellationToken ct = default)
        => await Reference("bulk-relation", "exec GET_RELATIONSHIP_TYPE", null).ConfigureAwait(false);

    private async Task<IReadOnlyList<LookupItem>> Reference(string key, string sql, object?[]? args)
        => await cache.GetOrCreateAsync(key, ReferenceCache, async () =>
        {
            var db = await Db().ConfigureAwait(false);
            var table = await db.QueryTableAsync(sql, args).ConfigureAwait(false);
            return (IReadOnlyList<LookupItem>?)[.. table.Rows.Cast<DataRow>().Select(r => new LookupItem(Text(r, 0) ?? string.Empty, Text(r, 1) ?? string.Empty))];
        }).ConfigureAwait(false) ?? [];

    public async Task<LetterSaveResult> SaveResultAsync(string userId, LetterResultEntry entry, CancellationToken ct = default)
    {
        var errors = LetterResultRules.Validate(entry);
        if (errors.Count > 0)
        {
            return new LetterSaveResult(false, errors);
        }

        var db = await Db(ct).ConfigureAwait(false);
        var letterNo = entry.LetterNo.Trim();
        // Hanya surat dari bulk milik user yang belum dikonfirmasi (sama dengan daftar di layar lama; SP lama tidak memeriksa apa pun).
        var allowed = await db.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM SURAT_HISTORY SH JOIN BULK_SURAT B ON B.BULKID = SH.BULKID WHERE SH.SU_NO = @1 AND B.BULKBY = @2 AND ISNULL(SH.SU_STATUS, '0') <> '1' AND B.BULKFINISH IS NOT NULL",
            [letterNo, userId], ct: ct).ConfigureAwait(false);
        if (allowed != 1)
        {
            return LetterSaveResult.Fail("Surat tidak ditemukan, bukan dari bulk Anda, atau hasilnya sudah diinput.");
        }

        try
        {
            await db.ExecuteAsync(
                "SET XACT_ABORT ON; BEGIN TRAN; exec SP_SAVE_SURAT_RESULT @1, @2, @3, @4, @5, @6, @7; COMMIT TRAN",
                [letterNo, entry.ResultCode, entry.ReceivedAt?.ToString("yyyyMMdd", CultureInfo.InvariantCulture), entry.ReceiverRelationCode,
                 entry.ReceivedBy?.Trim(), entry.Notes, userId],
                new DbOptions(TimeoutSeconds: 60), ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFailed(logger, ex, "simpan hasil", userId);
            return LetterSaveResult.Fail($"Hasil surat gagal disimpan. Tidak ada perubahan; coba lagi atau hubungi administrator. {ErrorReference.Tag}");
        }

        LogResult(logger, userId, letterNo, entry.ResultCode!);
        return new LetterSaveResult(true, []);
    }

    private static async Task<bool> OwnsBulkAsync(IDb db, string userId, Guid bulkId, CancellationToken ct)
        => await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM BULK_SURAT WHERE BULKID = @1 AND BULKBY = @2", [bulkId, userId], ct: ct).ConfigureAwait(false) == 1;

    private string PathFor(string fileName) => Path.Combine(Resolve(options.Value.OutputDirectory), fileName);

    private static string Resolve(string path) => Path.IsPathRooted(path) ? path : Path.Combine(AppContext.BaseDirectory, path);

    /// <summary>Nama berkas dari kolom bergaya lama (<c>..\Upload\Surat\BULKxxx.xls</c>); hanya nama berkas aman yang dipakai (tanpa path).</summary>
    public static string? SafeFileName(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored))
        {
            return null;
        }

        var name = stored.Replace('\\', '/').Split('/')[^1].Trim();
        return name.Length is > 0 and <= 150 && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.') && !name.StartsWith('.') && !name.Contains("..", StringComparison.Ordinal)
            ? name
            : null;
    }

    private static string? Text(DataRow r, int index) => r[index] is DBNull ? null : Convert.ToString(r[index], CultureInfo.InvariantCulture)?.Trim();

    private static string? Text(DataRow r, string column) => r.Table.Columns.Contains(column) && r[column] is not DBNull ? Convert.ToString(r[column], CultureInfo.InvariantCulture)?.Trim() : null;

    [LoggerMessage(Level = LogLevel.Information, Message = "Bulk surat dibuat: user {UserId}, bulk {BulkId}, {Count} surat, berkas {File}")]
    private static partial void LogGenerated(ILogger logger, string userId, Guid bulkId, int count, string file);

    [LoggerMessage(Level = LogLevel.Information, Message = "Hasil surat disimpan: user {UserId}, surat {LetterNo}, hasil {Result}")]
    private static partial void LogResult(ILogger logger, string userId, string letterNo, string result);

    [LoggerMessage(Level = LogLevel.Error, Message = "Bulk surat gagal ({Step}): user {UserId}")]
    private static partial void LogFailed(ILogger logger, Exception ex, string step, string userId);
}
