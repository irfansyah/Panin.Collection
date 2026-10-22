using System.Data;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Panin.Application.Abstractions;
using Panin.Application.Collection;
using Panin.Application.Modules;
using Panin.Infrastructure.Data;

namespace Panin.Infrastructure.Collection;

/// <summary>
/// Permintaan surat manual ("Letter"). Bisnisnya tetap di <c>SP_SAVE_SURAT_MANUAL</c>, yang menulis <c>SURAT_MANUAL</c>, memindahkan
/// rekening kembali ke pemilik semula bila rekomendasinya Surat, dan mengubah treatment; tanpa transaksi dan tanpa otorisasi di SP
/// lama. Di sini dipanggil dalam <c>BEGIN TRAN</c> dengan <c>XACT_ABORT ON</c>, hanya oleh pemilik/atasan, dan hanya untuk jenis
/// surat yang memang tersedia bagi rekening itu (layar lama mempercayai nilai dropdown dari browser).
/// </summary>
public sealed partial class LetterService(IModuleDbFactory dbs, ICallService calls, ILogger<LetterService> logger) : ILetterService
{
    private const string SaveSql = "SET XACT_ABORT ON; BEGIN TRAN; exec SP_SAVE_SURAT_MANUAL @1, @2, @3, @4; COMMIT TRAN";

    public const string PendingSql = "SELECT CASE WHEN EXISTS (SELECT 1 FROM SURAT_MANUAL WHERE ACC_NO = @1 AND SUMAN_TYPE = @2 AND SUMAN_NO IS NULL) THEN 1 ELSE 0 END";

    public const string HistorySql = """
        SELECT TOP (@2) M.SUMAN_TYPE AS TypeCode, S.SU_DESC AS TypeName, M.SUMAN_BY AS RequestedBy, M.SUMAN_DATE AS RequestedAt,
               M.SUMAN_NO AS LetterNo, M.SUMAN_MEMO AS Notes
        FROM SURAT_MANUAL M
        LEFT JOIN RFSURAT S ON S.SU_TYPE = M.SUMAN_TYPE
        WHERE M.ACC_NO = @1
        ORDER BY M.SUMAN_DATE DESC
        """;

    public async Task<IReadOnlyList<LookupItem>> GetTypesAsync(string accountNo, CancellationToken ct = default)
    {
        var db = await Db(ct).ConfigureAwait(false);
        var table = await db.QueryTableAsync("exec SP_SURAT_TYPE @1", [accountNo], ct: ct).ConfigureAwait(false);
        return [.. table.Rows.Cast<DataRow>().Select(r => new LookupItem(Cell(r, 0), Cell(r, 1)))];
    }

    public Task<bool> CanActAsync(string userId, string accountNo, CancellationToken ct = default) => calls.CanActAsync(userId, accountNo, ct);

    public async Task<LetterSaveResult> SaveAsync(string userId, LetterEntry entry, CancellationToken ct = default)
    {
        var errors = LetterRules.Validate(entry);
        if (errors.Count > 0)
        {
            return new LetterSaveResult(false, errors);
        }

        var account = entry.AccountNo.Trim();
        if (!await CanActAsync(userId, account, ct).ConfigureAwait(false))
        {
            LogDenied(logger, userId, account);
            return LetterSaveResult.Fail("Anda tidak berwenang mencatat aktivitas pada rekening ini.");
        }

        var types = await GetTypesAsync(account, ct).ConfigureAwait(false);
        if (types.All(t => t.Code != entry.TypeCode))
        {
            return LetterSaveResult.Fail("Jenis surat tidak tersedia untuk rekening ini.");
        }

        var db = await Db(ct).ConfigureAwait(false);
        try
        {
            var pending = await db.ExecuteScalarAsync<int>(PendingSql, [account, entry.TypeCode], ct: ct).ConfigureAwait(false) == 1;
            await db.ExecuteAsync(SaveSql, [account, entry.TypeCode, entry.Notes, userId], new DbOptions(TimeoutSeconds: 120), ct).ConfigureAwait(false);
            LogSaved(logger, userId, account, entry.TypeCode!);
            return new LetterSaveResult(true, [], pending);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogSaveFailed(logger, ex, userId, account);
            return LetterSaveResult.Fail("Data gagal disimpan. Tidak ada perubahan yang tercatat; coba lagi atau hubungi administrator.");
        }
    }

    public async Task<IReadOnlyList<LetterHistoryRow>> GetHistoryAsync(string accountNo, int take = 30, CancellationToken ct = default)
    {
        var db = await Db(ct).ConfigureAwait(false);
        return await db.QueryAsync<LetterHistoryRow>(HistorySql, [accountNo, Math.Clamp(take, 1, 200)], ct: ct).ConfigureAwait(false);
    }

    private Task<IDb> Db(CancellationToken ct = default) => dbs.GetDataDbAsync("60", ct);

    private static string Cell(DataRow r, int index)
        => r[index] is DBNull ? string.Empty : Convert.ToString(r[index], CultureInfo.InvariantCulture)!.Trim();

    [LoggerMessage(Level = LogLevel.Information, Message = "Surat manual tersimpan: user {UserId}, rekening {AccountNo}, jenis {Type}")]
    private static partial void LogSaved(ILogger logger, string userId, string accountNo, string type);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Surat manual ditolak (bukan pemilik/atasan): user {UserId}, rekening {AccountNo}")]
    private static partial void LogDenied(ILogger logger, string userId, string accountNo);

    [LoggerMessage(Level = LogLevel.Error, Message = "Surat manual gagal disimpan: user {UserId}, rekening {AccountNo}")]
    private static partial void LogSaveFailed(ILogger logger, Exception ex, string userId, string accountNo);
}
