using System.Data;
using System.Globalization;
using System.Net;
using Microsoft.Extensions.Logging;
using Panin.Application.Abstractions;
using Panin.Application.Collection;
using Panin.Application.Modules;
using Panin.Infrastructure.Data;

namespace Panin.Infrastructure.Collection;

/// <summary>
/// Pencatatan call ("Place A Call"). Logika bisnis penyimpanan tetap di <c>SAVE_CALL_HISTORY</c> (dipakai bersama aplikasi
/// lama selama masa paralel): ia mencatat <c>CALL_HISTORY</c> beserta foto kondisi rekening, memindahkan kepemilikan rekening
/// bila ada rekomendasi, dan menulis NEXT_ACTION_HISTORY, MEMO, LOAN_HANDLING_HISTORY. SP itu tidak memakai transaksi, sehingga
/// kegagalan di tengah meninggalkan data setengah jadi; di sini ia dipanggil di dalam <c>BEGIN TRAN</c> dengan
/// <c>XACT_ABORT ON</c> agar semua perubahan berhasil atau tidak sama sekali.
/// </summary>
public sealed partial class CallService(IModuleDbFactory dbs, SingleFlightCache cache, ILogger<CallService> logger) : ICallService
{
    private const string Function = "Call";
    private static readonly TimeSpan ReferenceCache = TimeSpan.FromMinutes(10);

    /// <summary>Urutan 40 parameter <c>SAVE_CALL_HISTORY</c>; yang tidak diisi layar ini dikirim NULL (= tidak ada perubahan).</summary>
    private const string SaveSql =
        "SET XACT_ABORT ON; BEGIN TRAN; " +
        "exec SAVE_CALL_HISTORY @1, @2, @3, @4, @5, @6, @7, @8, @9, @10, @11, @12, @13, @14, @15, @16, @17, @18, @19, @20, " +
        "@21, @22, @23, @24, @25, @26, @27, @28, @29, @30, @31, @32, @33, @34, @35, @36, @37, @38, @39, @40; " +
        "COMMIT TRAN";

    public const string CanActSql = """
        SELECT CASE WHEN EXISTS (
            SELECT 1 FROM LOAN_MASTER LM
            WHERE LM.ACC_NO = @2
              AND (LM.USERID = @1 OR LM.USERID IN (SELECT DOWNLINE FROM TEMP_DOWNLINE WHERE USERID = @1))
        ) THEN 1 ELSE 0 END
        """;

    public const string HistorySql = """
        SELECT TOP (@2) [DATE] AS [Date], [TIME] AS [Time], USERID AS UserId, RESULT AS ResultCode, RESULT_DATE AS PromiseAt,
               NEXT_ACTION AS NextActionCode, DATE_ACTION AS NextActionAt, REASON_NON_PAYMENT AS ReasonCode,
               CONTACT_NO AS ContactNo, SPOKE_TO AS SpokeTo, CONVERT(nvarchar(max), NOTES) AS Notes
        FROM CALL_HISTORY WHERE ACC_NO = @1 ORDER BY [DATE] DESC
        """;

    public async Task<CallFormOptions> GetOptionsAsync(string accountNo, CancellationToken ct = default)
    {
        var shared = await cache.GetOrCreateAsync("call-options", ReferenceCache, async () =>
        {
            var db = await Db().ConfigureAwait(false);
            var purposes = Items(await db.QueryTableAsync("select P_ID, P_DESC from RFPURPOSE where ACTIVE = 1 order by P_DESC").ConfigureAwait(false));
            var relationships = Items(await db.QueryTableAsync("exec GET_RELATIONSHIP_TYPE").ConfigureAwait(false));
            var payments = Items(await db.QueryTableAsync("exec GET_PAYMENT_TYPE").ConfigureAwait(false));
            var reasons = Items(await db.QueryTableAsync("exec SP_GET_REASON @1", [Function]).ConfigureAwait(false));
            var actions = Items(await db.QueryTableAsync("exec SP_GET_ACTION @1", [Function]).ConfigureAwait(false));
            return new CallFormOptions { Purposes = purposes, Relationships = relationships, PaymentTypes = payments, Reasons = reasons, NextActions = actions };
        }).ConfigureAwait(false) ?? new CallFormOptions();

        var accountDb = await Db(ct).ConfigureAwait(false);
        var contacts = Items(await accountDb.QueryTableAsync("exec SP_GET_CONTACT @1, @2", [accountNo, "COMB"], ct: ct).ConfigureAwait(false));
        var persons = Items(await accountDb.QueryTableAsync("exec SP_GET_CONTACT @1, @2", [accountNo, "NAMA"], ct: ct).ConfigureAwait(false));
        return new CallFormOptions
        {
            Purposes = shared.Purposes,
            Relationships = shared.Relationships,
            PaymentTypes = shared.PaymentTypes,
            Reasons = shared.Reasons,
            NextActions = shared.NextActions,
            Contacts = contacts,
            PtpPersons = persons,
        };
    }

    public async Task<IReadOnlyList<LookupItem>> GetResultsAsync(string? relationshipCode, CancellationToken ct = default)
    {
        var relationship = string.IsNullOrWhiteSpace(relationshipCode) ? CallRules.DebtorRelationship : relationshipCode.Trim();
        return await cache.GetOrCreateAsync($"call-results:{relationship}", ReferenceCache, async () =>
        {
            var db = await Db().ConfigureAwait(false);
            return (IReadOnlyList<LookupItem>?)Items(await db.QueryTableAsync("exec SP_PTP_CHANGELIST @1, @2", [Function, relationship]).ConfigureAwait(false));
        }).ConfigureAwait(false) ?? [];
    }

    public async Task<bool> CanActAsync(string userId, string accountNo, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(accountNo))
        {
            return false;
        }

        var db = await Db(ct).ConfigureAwait(false);
        return await db.ExecuteScalarAsync<int>(CanActSql, [userId.Trim(), accountNo.Trim()], ct: ct).ConfigureAwait(false) == 1;
    }

    public async Task<CallSaveResult> SaveAsync(string userId, CallEntry entry, CancellationToken ct = default)
    {
        var errors = CallRules.Validate(entry);
        if (errors.Count > 0)
        {
            return new CallSaveResult(false, errors);
        }

        if (!await CanActAsync(userId, entry.AccountNo, ct).ConfigureAwait(false))
        {
            LogDenied(logger, userId, entry.AccountNo);
            return CallSaveResult.Fail("Anda tidak berwenang mencatat aktivitas pada rekening ini.");
        }

        var db = await Db(ct).ConfigureAwait(false);
        if (entry.ResultCode == CallRules.PromiseResultCode && entry.PromiseAt is { } promiseAt)
        {
            var exceeded = await db.QueryFirstOrDefaultAsync("exec SP_TREATMENTRESULT_MAXDAY @1, @2", [entry.ResultCode, promiseAt], ct: ct).ConfigureAwait(false);
            if (exceeded is not null)
            {
                var results = await GetResultsAsync(entry.RelationshipCode, ct).ConfigureAwait(false);
                var name = results.FirstOrDefault(r => r.Code == entry.ResultCode)?.Text ?? entry.ResultCode;
                return CallSaveResult.Fail($"Tanggal Janji Debitur {name} tidak boleh lebih dari {Convert.ToString(exceeded[0], CultureInfo.InvariantCulture)} hari");
            }
        }

        try
        {
            await db.ExecuteAsync(SaveSql, BuildArguments(userId, entry, DateTime.Now), new DbOptions(TimeoutSeconds: 120), ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogSaveFailed(logger, ex, userId, entry.AccountNo);
            return CallSaveResult.Fail("Data gagal disimpan. Tidak ada perubahan yang tercatat; coba lagi atau hubungi administrator.");
        }

        LogSaved(logger, userId, entry.AccountNo, entry.ResultCode!);
        return CallSaveResult.Ok;
    }

    public async Task<IReadOnlyList<CallHistoryRow>> GetHistoryAsync(string accountNo, int take = 30, CancellationToken ct = default)
    {
        var db = await Db(ct).ConfigureAwait(false);
        return await db.QueryAsync<CallHistoryRow>(HistorySql, [accountNo, Math.Clamp(take, 1, 200)], ct: ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Mengisi 40 parameter. Perlakuan teks mengikuti layar lama demi paritas selama masa paralel: SPOKE_TO di-HTML-encode
    /// (tampilan lama menuliskannya apa adanya) dan tanda petik tunggal pada catatan diganti '`'.
    /// </summary>
    public static object?[] BuildArguments(string userId, CallEntry e, DateTime now)
    {
        var a = new object?[40];
        a[0] = e.AccountNo.Trim();
        a[1] = userId;
        a[2] = now.ToString("HH:mm", CultureInfo.InvariantCulture);
        a[3] = e.ResultCode;
        a[4] = e.ReasonCode;
        a[5] = e.NextActionAt;
        a[6] = e.NextActionCode;
        a[7] = e.ContactNo;
        a[8] = e.RelationshipCode;
        a[9] = string.IsNullOrWhiteSpace(e.SpokeTo) ? null : WebUtility.HtmlEncode(e.SpokeTo.Trim());
        a[10] = e.PurposeCode;
        a[11] = e.PromiseAt;
        e.Update.Fill(a, e.Update.LetterStatus);
        a[29] = e.Notes?.Replace("'", "`", StringComparison.Ordinal);
        a[37] = e.PtpAmount?.ToString(CultureInfo.InvariantCulture);
        a[38] = e.PaymentTypeCode;
        a[39] = e.PtpPersonId;
        return a;
    }

    private Task<IDb> Db(CancellationToken ct = default) => dbs.GetDataDbAsync("60", ct);

    private static List<LookupItem> Items(DataTable table)
        => [.. table.Rows.Cast<DataRow>().Where(r => table.Columns.Count >= 2).Select(r => new LookupItem(Cell(r, 0), Cell(r, 1)))];

    private static string Cell(DataRow r, int index)
        => r[index] is DBNull ? string.Empty : Convert.ToString(r[index], CultureInfo.InvariantCulture)!.Trim();

    [LoggerMessage(Level = LogLevel.Information, Message = "Call tersimpan: user {UserId}, rekening {AccountNo}, hasil {ResultCode}")]
    private static partial void LogSaved(ILogger logger, string userId, string accountNo, string resultCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Call ditolak (bukan pemilik/atasan): user {UserId}, rekening {AccountNo}")]
    private static partial void LogDenied(ILogger logger, string userId, string accountNo);

    [LoggerMessage(Level = LogLevel.Error, Message = "Call gagal disimpan: user {UserId}, rekening {AccountNo}")]
    private static partial void LogSaveFailed(ILogger logger, Exception ex, string userId, string accountNo);
}

public sealed class TreatmentMenuService(IModuleDbFactory dbs, SingleFlightCache cache) : ITreatmentMenuService
{
    public async Task<IReadOnlyList<TreatmentOption>> GetAsync(string userId, CancellationToken ct = default)
        => await cache.GetOrCreateAsync($"treatments:{userId}", TimeSpan.FromMinutes(10), async () =>
        {
            var db = await dbs.GetDataDbAsync("60").ConfigureAwait(false);
            var table = await db.QueryTableAsync("exec SP_GET_TREATMENT_MENU @1", [userId]).ConfigureAwait(false);
            return (IReadOnlyList<TreatmentOption>?)[.. table.Rows.Cast<DataRow>().Select(r => new TreatmentOption(
                Convert.ToString(r["TREATMENT_DESC"], CultureInfo.InvariantCulture)?.Trim() ?? string.Empty,
                Convert.ToString(r["TREATMENT_FUNCTION"], CultureInfo.InvariantCulture)?.Trim() ?? string.Empty))];
        }).ConfigureAwait(false) ?? [];
}
