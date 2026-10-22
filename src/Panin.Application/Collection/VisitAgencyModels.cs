namespace Panin.Application.Collection;

public sealed class VisitAgencyOptions
{
    public const string SectionName = "VisitAgency";

    /// <summary>Templat Excel agen visit (relatif terhadap folder aplikasi bila tidak absolut).</summary>
    public string TemplatePath { get; set; } = "Templates/Visit.xls";

    /// <summary>Sandi proteksi lembar pada berkas hasil; sama dengan layar lama ("PASS"). Kosongkan untuk tanpa proteksi.</summary>
    public string? ProtectionPassword { get; set; } = "PASS";

    /// <summary>Jumlah debitur maksimum per berkas: lembar "Surat Visit" memakai 77 baris per debitur dan satu lembar .xls berisi paling banyak 65.536 baris.</summary>
    public int MaxAccounts { get; set; } = 500;
}

/// <param name="Content">Isi berkas .xls; <c>null</c> bila gagal.</param>
/// <param name="Warnings">Berkas sudah dibuat tetapi ada langkah sesudahnya yang gagal (mis. pemindahan debitur).</param>
public sealed record VisitExcelResult(bool Success, string? FileName, byte[]? Content, int Accounts, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings)
{
    public static VisitExcelResult Fail(string message) => new(false, null, null, 0, [message], []);
}

public interface IVisitAgencyService
{
    /// <summary>Group pengguna berhak (treatment <c>00002</c>) memakai Generate Excel agen visit?</summary>
    Task<bool> IsAllowedAsync(string userId, CancellationToken ct = default);

    /// <summary>
    /// Membuat berkas Excel agen visit untuk pilihan debitur (nomor slip dibuat di DB), lalu memindahkan debitur itu ke <c>TargetUserId</c> secara permanen,
    /// seperti tombol "Generate Excel" di layar lama. Otorisasi dan validasi pilihan sama dengan pemindahan biasa.
    /// </summary>
    Task<VisitExcelResult> GenerateAsync(string actorUserId, ReassignRequest request, CancellationToken ct = default);
}
