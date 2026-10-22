namespace Panin.Application.Collection;

public sealed class LetterEntry
{
    public string AccountNo { get; set; } = string.Empty;

    public string? TypeCode { get; set; }

    public string? Notes { get; set; }
}

public sealed record LetterHistoryRow(string? TypeCode, string? TypeName, string? RequestedBy, DateTime? RequestedAt, string? LetterNo, string? Notes);

/// <summary><paramref name="AlreadyPending"/>: jenis surat itu sudah diminta dan belum diterbitkan, sehingga catatan hanya disimpan sebagai memo.</summary>
public sealed record LetterSaveResult(bool Success, IReadOnlyList<string> Errors, bool AlreadyPending = false)
{
    public static LetterSaveResult Fail(params string[] errors) => new(false, errors);
}

public static class LetterRules
{
    public const int MaxNotesLength = 2000;

    public static IReadOnlyList<string> Validate(LetterEntry e)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(e.AccountNo))
        {
            errors.Add("Nomor rekening wajib diisi");
        }

        if (string.IsNullOrWhiteSpace(e.TypeCode))
        {
            errors.Add("Jenis Surat wajib diisi");
        }

        if (string.IsNullOrWhiteSpace(e.Notes))
        {
            errors.Add("Catatan wajib diisi");
        }
        else if (e.Notes.Length > MaxNotesLength)
        {
            errors.Add($"Catatan maksimal {MaxNotesLength} karakter");
        }

        return errors;
    }
}

public interface ILetterService
{
    /// <summary>Jenis surat yang tersedia untuk rekening (menurut produk), <c>SP_SURAT_TYPE</c>.</summary>
    Task<IReadOnlyList<LookupItem>> GetTypesAsync(string accountNo, CancellationToken ct = default);

    Task<bool> CanActAsync(string userId, string accountNo, CancellationToken ct = default);

    /// <summary>Validasi, otorisasi (pemilik/atasan), jenis surat harus tersedia untuk rekening, lalu simpan dalam satu transaksi.</summary>
    Task<LetterSaveResult> SaveAsync(string userId, LetterEntry entry, CancellationToken ct = default);

    Task<IReadOnlyList<LetterHistoryRow>> GetHistoryAsync(string accountNo, int take = 30, CancellationToken ct = default);
}
