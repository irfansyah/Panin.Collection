using Panin.Application.Diagnostics;
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
/// Pencatatan kunjungan ("Visitation"). Bisnis penyimpanan tetap di <c>SAVE_VISIT_HISTORY</c> (dipakai bersama aplikasi lama):
/// mencatat <c>VISIT_HISTORY</c>, memindahkan kepemilikan rekening bila ada rekomendasi, dan menulis perubahan kontak. Seperti
/// <c>SAVE_CALL_HISTORY</c> SP itu tanpa transaksi, jadi dipanggil di dalam <c>BEGIN TRAN</c> dengan <c>XACT_ABORT ON</c>.
/// </summary>
public sealed partial class VisitService(IModuleDbFactory dbs, SingleFlightCache cache, ICallService calls, ILogger<VisitService> logger) : IVisitService
{
    private const string Function = "Visitation";
    private static readonly TimeSpan ReferenceCache = TimeSpan.FromMinutes(10);

    private const string SaveSql =
        "SET XACT_ABORT ON; BEGIN TRAN; " +
        "exec SAVE_VISIT_HISTORY @1, @2, @3, @4, @5, @6, @7, @8, @9, @10, @11, @12, @13, @14, @15, @16, @17, @18, @19, @20, " +
        "@21, @22, @23, @24, @25, @26, @27, @28, @29, @30, @31, @32, @33, @34, @35, @36, @37; " +
        "COMMIT TRAN";

    public const string HistorySql = """
        SELECT TOP (@2) [DATE] AS [Date], USERID AS UserId, COLLECTOR_OFFR AS OfficerCode, PERSON_VISIT AS PersonVisited,
               RESULT AS ResultCode, RESULT_DATE AS ResultAt, NEXTACTION AS NextActionCode, NEXTACTION_DATE AS NextActionAt,
               CONVERT(nvarchar(max), NOTES) AS Notes
        FROM VISIT_HISTORY WHERE ACC_NO = @1 ORDER BY [DATE] DESC
        """;

    public async Task<VisitFormOptions> GetOptionsAsync(string userId, CancellationToken ct = default)
    {
        var shared = await cache.GetOrCreateAsync("visit-options", ReferenceCache, async () =>
        {
            var db = await Db().ConfigureAwait(false);
            return new VisitFormOptions
            {
                Purposes = Items(await db.QueryTableAsync("select P_ID, P_DESC from RFPURPOSE where ACTIVE = 1 order by P_DESC").ConfigureAwait(false)),
                Relationships = Items(await db.QueryTableAsync("exec GET_RELATIONSHIP_TYPE").ConfigureAwait(false)),
                Results = Items(await db.QueryTableAsync("exec SP_GET_RESULT @1", [Function]).ConfigureAwait(false)),
                NextActions = Items(await db.QueryTableAsync("exec SP_GET_ACTION @1", [Function]).ConfigureAwait(false)),
            };
        }).ConfigureAwait(false) ?? new VisitFormOptions();

        // Petugas lapangan = bawahan langsung user, berbeda per user.
        var officers = await cache.GetOrCreateAsync($"visit-officers:{userId}", ReferenceCache, async () =>
        {
            var db = await Db().ConfigureAwait(false);
            return (IReadOnlyList<LookupItem>?)Items(await db.QueryTableAsync("exec SP_GET_FIELD_OFFICER @1", [userId]).ConfigureAwait(false));
        }).ConfigureAwait(false) ?? [];

        return new VisitFormOptions
        {
            Officers = officers,
            Purposes = shared.Purposes,
            Relationships = shared.Relationships,
            Results = shared.Results,
            NextActions = shared.NextActions,
        };
    }

    public Task<bool> CanActAsync(string userId, string accountNo, CancellationToken ct = default) => calls.CanActAsync(userId, accountNo, ct);

    public async Task<CallSaveResult> SaveAsync(string userId, VisitEntry entry, CancellationToken ct = default)
    {
        var errors = VisitRules.Validate(entry);
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
        if (entry.ResultCode is not null && entry.ResultAt is { } resultAt)
        {
            var exceeded = await db.QueryFirstOrDefaultAsync("exec SP_TREATMENTRESULT_MAXDAY @1, @2", [entry.ResultCode, resultAt], ct: ct).ConfigureAwait(false);
            if (exceeded is not null)
            {
                var name = (await GetOptionsAsync(userId, ct).ConfigureAwait(false)).Results.FirstOrDefault(r => r.Code == entry.ResultCode)?.Text ?? entry.ResultCode;
                return CallSaveResult.Fail($"Tanggal Janji Debitur {name} tidak boleh lebih dari {Convert.ToString(exceeded[0], CultureInfo.InvariantCulture)} hari");
            }
        }

        try
        {
            var letters = await new ContactReferenceLookup(dbs, cache).LetterStatusesAsync(ct).ConfigureAwait(false);
            var letterText = letters.FirstOrDefault(l => l.Code == entry.Update.LetterStatus)?.Text;
            await db.ExecuteAsync(SaveSql, BuildArguments(userId, entry, letterText), new DbOptions(TimeoutSeconds: 120), ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogSaveFailed(logger, ex, userId, entry.AccountNo);
            return CallSaveResult.Fail($"Data gagal disimpan. Tidak ada perubahan yang tercatat; coba lagi atau hubungi administrator. {ErrorReference.Tag}");
        }

        LogSaved(logger, userId, entry.AccountNo);
        return CallSaveResult.Ok;
    }

    public async Task<IReadOnlyList<VisitHistoryRow>> GetHistoryAsync(string accountNo, int take = 30, CancellationToken ct = default)
    {
        var db = await Db(ct).ConfigureAwait(false);
        return await db.QueryAsync<VisitHistoryRow>(HistorySql, [accountNo, Math.Clamp(take, 1, 200)], ct: ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Mengisi 37 parameter. Mengikuti layar lama demi paritas: catatan, alamat, dan identitas di-HTML-encode; tanggal kunjungan
    /// berupa tanggal saja, tanggal hasil/rekomendasi dengan jam; status surat (@LETTER_COND) dikirim sebagai teksnya.
    /// </summary>
    public static object?[] BuildArguments(string userId, VisitEntry e, string? letterText)
    {
        var a = new object?[37];
        a[0] = userId;
        a[1] = e.AccountNo.Trim();
        a[2] = e.OfficerCode;
        a[3] = e.RelationshipCode;
        a[4] = e.Notes is null ? null : WebUtility.HtmlEncode(e.Notes);
        a[5] = e.VisitAt?.Date;
        a[6] = string.IsNullOrWhiteSpace(e.CustomerAddress) ? null : WebUtility.HtmlEncode(e.CustomerAddress.Trim());
        a[7] = e.ResultCode;
        a[8] = e.PurposeCode;
        a[9] = e.ResultAt;
        a[10] = e.NextActionCode;
        a[11] = e.NextActionAt;
        a[29] = e.PersonVisited?.Trim();
        e.Update.Fill(a, letterText);
        return a;
    }

    private Task<IDb> Db(CancellationToken ct = default) => dbs.GetDataDbAsync("60", ct);

    private static List<LookupItem> Items(DataTable table)
        => [.. table.Rows.Cast<DataRow>().Where(_ => table.Columns.Count >= 2).Select(r => new LookupItem(Cell(r, 0), Cell(r, 1)))];

    private static string Cell(DataRow r, int index)
        => r[index] is DBNull ? string.Empty : Convert.ToString(r[index], CultureInfo.InvariantCulture)!.Trim();

    [LoggerMessage(Level = LogLevel.Information, Message = "Kunjungan tersimpan: user {UserId}, rekening {AccountNo}")]
    private static partial void LogSaved(ILogger logger, string userId, string accountNo);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Kunjungan ditolak (bukan pemilik/atasan): user {UserId}, rekening {AccountNo}")]
    private static partial void LogDenied(ILogger logger, string userId, string accountNo);

    [LoggerMessage(Level = LogLevel.Error, Message = "Kunjungan gagal disimpan: user {UserId}, rekening {AccountNo}")]
    private static partial void LogSaveFailed(ILogger logger, Exception ex, string userId, string accountNo);
}

/// <summary>Referensi bagian "Hasil" (status surat dan kota), dicache bersama untuk semua user.</summary>
public sealed class ContactReferenceService(IModuleDbFactory dbs, SingleFlightCache cache) : IContactReferenceService
{
    public Task<IReadOnlyList<LookupItem>> GetLetterStatusesAsync(CancellationToken ct = default) => new ContactReferenceLookup(dbs, cache).LetterStatusesAsync(ct);

    public Task<IReadOnlyList<LookupItem>> GetCitiesAsync(CancellationToken ct = default) => new ContactReferenceLookup(dbs, cache).CitiesAsync(ct);
}

internal readonly struct ContactReferenceLookup(IModuleDbFactory dbs, SingleFlightCache cache)
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(30);

    public async Task<IReadOnlyList<LookupItem>> LetterStatusesAsync(CancellationToken ct)
    {
        var self = this;
        return await cache.GetOrCreateAsync("contact-letter-status", Ttl, () => self.LoadAsync("exec SP_GET_RESULT @1", ["Letter"])).ConfigureAwait(false) ?? [];
    }

    public async Task<IReadOnlyList<LookupItem>> CitiesAsync(CancellationToken ct)
    {
        var self = this;
        return await cache.GetOrCreateAsync("contact-cities", Ttl, () => self.LoadAsync("exec SP_GET_CITY", null)).ConfigureAwait(false) ?? [];
    }

    private async Task<IReadOnlyList<LookupItem>?> LoadAsync(string sql, object?[]? args)
    {
        var db = await dbs.GetDataDbAsync("60").ConfigureAwait(false);
        var table = await db.QueryTableAsync(sql, args).ConfigureAwait(false);
        return [.. table.Rows.Cast<DataRow>().Where(_ => table.Columns.Count >= 2).Select(r => new LookupItem(
            r[0] is DBNull ? string.Empty : Convert.ToString(r[0], CultureInfo.InvariantCulture)!.Trim(),
            r[1] is DBNull ? string.Empty : Convert.ToString(r[1], CultureInfo.InvariantCulture)!.Trim()))];
    }
}
