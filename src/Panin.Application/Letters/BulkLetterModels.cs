using Panin.Application.Collection;

namespace Panin.Application.Letters;

/// <summary>Ringkasan surat yang siap digenerate (<c>SP_LIST_BULK_GENERATE</c>).</summary>
public sealed record PendingLetterGroup(string? ProductType, string? AppType, string? LetterType, int Count);

/// <summary>Surat yang sudah dicetak tetapi hasilnya belum diinput (<c>SP_LIST_SURAT_UNCONFIRMED</c>).</summary>
public sealed record UnconfirmedLetter(string BulkName, string AccountNo, string LetterNo, DateTime? LetterDate, string? LetterType, string? CustomerName);

public sealed record BulkRow(Guid BulkId, string Name, DateTime Date, int Total, bool Finished, string? FileName);

public sealed record BulkGenerateResult(bool Success, string Message, Guid? BulkId = null, int LetterCount = 0)
{
    public static BulkGenerateResult Fail(string message) => new(false, message);
}

public sealed record BulkFile(Stream Content, string FileName);

/// <summary>Hasil surat (<c>SP_SAVE_SURAT_RESULT</c>): hasil, tanggal terima, hubungan dan nama penerima, catatan.</summary>
public sealed class LetterResultEntry
{
    public string LetterNo { get; set; } = string.Empty;

    public string? ResultCode { get; set; }

    public DateTime? ReceivedAt { get; set; }

    public string? ReceiverRelationCode { get; set; }

    public string? ReceivedBy { get; set; }

    public string? Notes { get; set; }
}

public static class LetterResultRules
{
    /// <summary>Kolom <c>SURAT_HISTORY.SU_RECEIVEBY</c> hanya <c>varchar(20)</c>; layar lama mengizinkan 100 karakter dan data terpotong/gagal.</summary>
    public const int MaxReceivedBy = 20;

    public const int MaxNotes = 2000;

    public static IReadOnlyList<string> Validate(LetterResultEntry e)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(e.LetterNo))
        {
            errors.Add("Nomor surat wajib diisi");
        }

        if (string.IsNullOrWhiteSpace(e.ResultCode))
        {
            errors.Add("Hasil Surat wajib diisi");
        }

        if (e.ReceivedBy is { Length: > MaxReceivedBy })
        {
            errors.Add($"Receive By maksimal {MaxReceivedBy} karakter");
        }

        if (e.Notes is { Length: > MaxNotes })
        {
            errors.Add($"Catatan maksimal {MaxNotes} karakter");
        }

        if (e.ReceivedAt is { } d && d.Date > DateTime.Today)
        {
            errors.Add("Tanggal terima tidak boleh di masa depan");
        }

        return errors;
    }
}

/// <summary>Pengaturan berkas surat (bagian <c>Letters</c> di konfigurasi).</summary>
public sealed class BulkLetterOptions
{
    public const string SectionName = "Letters";

    /// <summary>Folder template Excel (<c>surat.xls</c>). Relatif terhadap folder aplikasi bila tidak absolut.</summary>
    public string TemplateDirectory { get; set; } = "Templates";

    /// <summary>Folder hasil bulk. Di produksi isi path absolut di luar folder aplikasi agar tidak terhapus saat deploy.</summary>
    public string OutputDirectory { get; set; } = "App_Data/surat";

    /// <summary>Password proteksi sheet/workbook hasil (sama dengan layar lama: "rahasia"). Kosongkan untuk tanpa proteksi.</summary>
    public string? ProtectionPassword { get; set; } = "rahasia";
}

public interface IBulkLetterService
{
    Task<IReadOnlyList<PendingLetterGroup>> GetPendingAsync(string userId, CancellationToken ct = default);

    Task<IReadOnlyList<UnconfirmedLetter>> GetUnconfirmedAsync(string userId, CancellationToken ct = default);

    Task<IReadOnlyList<BulkRow>> GetBulksAsync(string userId, CancellationToken ct = default);

    /// <summary>Membuat nomor surat untuk surat yang siap (maks. 100 per proses, aturan SP), menyusun Excel, dan menandai selesai.</summary>
    Task<BulkGenerateResult> GenerateAsync(string userId, CancellationToken ct = default);

    /// <summary>Menyusun ulang berkas Excel sebuah bulk milik user (nomor surat tidak berubah).</summary>
    Task<BulkGenerateResult> RegenerateAsync(string userId, Guid bulkId, CancellationToken ct = default);

    /// <summary>Berkas hasil bulk milik user, atau <c>null</c> bila bukan miliknya atau berkasnya tidak ada di server ini.</summary>
    Task<BulkFile?> OpenFileAsync(string userId, Guid bulkId, CancellationToken ct = default);

    Task<IReadOnlyList<LookupItem>> GetResultOptionsAsync(CancellationToken ct = default);

    Task<IReadOnlyList<LookupItem>> GetReceiverRelationsAsync(CancellationToken ct = default);

    Task<LetterSaveResult> SaveResultAsync(string userId, LetterResultEntry entry, CancellationToken ct = default);
}
