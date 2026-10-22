using Panin.Application.Collection;

namespace Panin.Application.Letters;

/// <summary>Hasil pencarian rekening beserta statusnya di daftar pengecualian surat.</summary>
public sealed record ExceptionSearchRow(
    string AccountNo,
    string? CustomerName,
    string? Facility,
    double? Outstanding,
    int? Dpd,
    string? Collectibility,
    string? LastAction,
    int? InstallmentNo,
    bool IsException);

public sealed record ExceptionLetterEntry(string AccountNo, string? CustomerName, string? SetBy, DateTime? SetAt);

public sealed record ExceptionSearchResult(bool Success, IReadOnlyList<ExceptionSearchRow> Rows, bool Truncated, int MaxRows, IReadOnlyList<string> Errors)
{
    public static ExceptionSearchResult Fail(params string[] errors) => new(false, [], false, 0, errors);
}

public interface IExceptionLetterService
{
    /// <summary>Menu "Account Exception Letter" (RFMENUX 103020) ada di menu group ini?</summary>
    Task<bool> HasAccessAsync(string groupId, CancellationToken ct = default);

    /// <summary>Cari rekening (nomor atau nama; minimal 3 karakter, hasil dibatasi) beserta statusnya.</summary>
    Task<ExceptionSearchResult> SearchAsync(string groupId, string? accountNo, string? name, CancellationToken ct = default);

    /// <summary>Rekening yang sedang dikecualikan dari pembuatan surat.</summary>
    Task<IReadOnlyList<ExceptionLetterEntry>> ListAsync(string groupId, CancellationToken ct = default);

    /// <summary>Kecualikan rekening dari pembuatan surat otomatis (Bulk Surat melewatinya).</summary>
    Task<LetterSaveResult> SetExceptionAsync(string userId, string groupId, string accountNo, CancellationToken ct = default);

    /// <summary>Hapus pengecualian (rekening kembali ikut pembuatan surat).</summary>
    Task<LetterSaveResult> SetAllowAsync(string userId, string groupId, string accountNo, CancellationToken ct = default);
}
