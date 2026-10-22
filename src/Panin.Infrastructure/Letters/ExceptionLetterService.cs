using System.Globalization;
using Microsoft.Extensions.Logging;
using Panin.Application.Abstractions;
using Panin.Application.Collection;
using Panin.Application.Diagnostics;
using Panin.Application.Letters;
using Panin.Application.Modules;
using Panin.Infrastructure.Data;

namespace Panin.Infrastructure.Letters;

/// <summary>
/// Account Exception Letter (<c>Controls/LetterException.ascx</c>): rekening di <c>EXCEPTION_LETTER</c> dilewati pembuatan surat otomatis.
/// Dibanding layar lama: hak menu 103020 diperiksa di server; pencarian wajib punya kriteria dan dibatasi (SP lama tanpa kriteria
/// mengembalikan seluruh debitur); penambahan tidak memakai <c>SP_ADD_LETTER_EXCEPTION</c> karena SP itu, bila rekening sudah ada,
/// menjalankan <c>UPDATE</c> <b>tanpa WHERE</b> dan menimpa pelaku/waktu di semua baris pengecualian; rekening harus ada di <c>LOAN_MASTER</c>.
/// </summary>
public sealed partial class ExceptionLetterService(
    IModuleDbFactory dbs,
    IMenuService menus,
    IDebiturSearchService search,
    ILogger<ExceptionLetterService> logger) : IExceptionLetterService
{
    private const string MenuId = "103020";
    private const int ListLimit = 500;

    public const string AddSql = """
        SET XACT_ABORT ON; BEGIN TRAN;
        IF EXISTS (SELECT 1 FROM EXCEPTION_LETTER WHERE ACC_NO = @1)
            UPDATE EXCEPTION_LETTER SET ACT_BY = @2, ACT_DATETIME = GETDATE() WHERE ACC_NO = @1;
        ELSE
            INSERT INTO EXCEPTION_LETTER (EL_ID, ACC_NO, ACT_BY, ACT_DATETIME) VALUES (NEWID(), @1, @2, GETDATE());
        COMMIT TRAN
        """;

    public const string ListSql = """
        SELECT TOP (@1) E.ACC_NO AS AccountNo,
               LTRIM(RTRIM(ISNULL(C.CU_FIRSTNAME + ' ', '') + ISNULL(C.CU_MIDDLENAME + ' ', '') + ISNULL(C.CU_LASTNAME, ''))) AS CustomerName,
               E.ACT_BY AS SetBy, E.ACT_DATETIME AS SetAt
        FROM EXCEPTION_LETTER E
        LEFT JOIN ACCOUNT_MASTER AM ON AM.ACC_NO = E.ACC_NO
        LEFT JOIN CUSTOMER C ON C.CU_REF = AM.CU_REF
        ORDER BY E.ACT_DATETIME DESC
        """;

    public async Task<bool> HasAccessAsync(string groupId, CancellationToken ct = default)
    {
        var tree = await menus.GetMenuAsync("60", groupId, ct).ConfigureAwait(false);
        return Contains(tree, MenuId);

        static bool Contains(IReadOnlyList<MenuNode> nodes, string id) => nodes.Any(n => n.Id == id || Contains(n.Children, id));
    }

    public async Task<ExceptionSearchResult> SearchAsync(string groupId, string? accountNo, string? name, CancellationToken ct = default)
    {
        if (!await HasAccessAsync(groupId, ct).ConfigureAwait(false))
        {
            return ExceptionSearchResult.Fail("Group Anda tidak punya hak untuk menu ini.");
        }

        var criteria = new DebiturSearchCriteria(accountNo, null, name);
        if (DebiturSearchRules.Validate(criteria.Normalized()) is { } error)
        {
            return ExceptionSearchResult.Fail(error);
        }

        var found = await search.SearchAsync(criteria, ct).ConfigureAwait(false);
        var db = await dbs.GetDataDbAsync("60", ct).ConfigureAwait(false);
        var excepted = (await db.QueryTableAsync("SELECT ACC_NO FROM EXCEPTION_LETTER", ct: ct).ConfigureAwait(false))
            .Rows.Cast<System.Data.DataRow>().Select(r => Convert.ToString(r[0], CultureInfo.InvariantCulture)!.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var rows = found.Rows.Select(r => new ExceptionSearchRow(
            r.AccountNo, r.CustomerName, r.Facility, r.Outstanding, r.Dpd, r.Collectibility, r.LastAction, r.InstallmentNo, excepted.Contains(r.AccountNo))).ToList();
        return new ExceptionSearchResult(true, rows, found.Truncated, found.MaxRows, []);
    }

    public async Task<IReadOnlyList<ExceptionLetterEntry>> ListAsync(string groupId, CancellationToken ct = default)
    {
        if (!await HasAccessAsync(groupId, ct).ConfigureAwait(false))
        {
            return [];
        }

        var db = await dbs.GetDataDbAsync("60", ct).ConfigureAwait(false);
        return await db.QueryAsync<ExceptionLetterEntry>(ListSql, [ListLimit], ct: ct).ConfigureAwait(false);
    }

    public async Task<LetterSaveResult> SetExceptionAsync(string userId, string groupId, string accountNo, CancellationToken ct = default)
    {
        if (!await HasAccessAsync(groupId, ct).ConfigureAwait(false))
        {
            return LetterSaveResult.Fail("Group Anda tidak punya hak untuk menu ini.");
        }

        accountNo = accountNo.Trim();
        var db = await dbs.GetDataDbAsync("60", ct).ConfigureAwait(false);
        if (await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM LOAN_MASTER WHERE ACC_NO = @1", [accountNo], ct: ct).ConfigureAwait(false) == 0)
        {
            return LetterSaveResult.Fail("Rekening tidak ditemukan.");
        }

        try
        {
            await db.ExecuteAsync(AddSql, [accountNo, userId], ct: ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFailed(logger, ex, "kecualikan", userId, accountNo);
            return LetterSaveResult.Fail($"Gagal menyimpan. Coba lagi atau hubungi administrator. {ErrorReference.Tag}");
        }

        LogChanged(logger, userId, "dikecualikan", accountNo);
        return new LetterSaveResult(true, []);
    }

    public async Task<LetterSaveResult> SetAllowAsync(string userId, string groupId, string accountNo, CancellationToken ct = default)
    {
        if (!await HasAccessAsync(groupId, ct).ConfigureAwait(false))
        {
            return LetterSaveResult.Fail("Group Anda tidak punya hak untuk menu ini.");
        }

        accountNo = accountNo.Trim();
        var db = await dbs.GetDataDbAsync("60", ct).ConfigureAwait(false);
        try
        {
            // Satu rekening saja; SP_DELETE_LETTER_EXCEPTION lama ekuivalen tetapi tidak mencatat siapa pelakunya.
            await db.ExecuteAsync("DELETE FROM EXCEPTION_LETTER WHERE ACC_NO = @1", [accountNo], ct: ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFailed(logger, ex, "izinkan", userId, accountNo);
            return LetterSaveResult.Fail($"Gagal menyimpan. Coba lagi atau hubungi administrator. {ErrorReference.Tag}");
        }

        LogChanged(logger, userId, "diizinkan kembali", accountNo);
        return new LetterSaveResult(true, []);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Pengecualian surat: {UserId} — rekening {AccountNo} {Change}")]
    private static partial void LogChanged(ILogger logger, string userId, string change, string accountNo);

    [LoggerMessage(Level = LogLevel.Error, Message = "Pengecualian surat gagal ({Step}): {UserId}, rekening {AccountNo}")]
    private static partial void LogFailed(ILogger logger, Exception ex, string step, string userId, string accountNo);
}
