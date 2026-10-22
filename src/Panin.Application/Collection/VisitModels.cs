namespace Panin.Application.Collection;

/// <summary>Pilihan dropdown form "Visitation" (tabel/SP referensi yang sama dengan layar lama).</summary>
public sealed class VisitFormOptions
{
    public IReadOnlyList<LookupItem> Officers { get; init; } = [];

    public IReadOnlyList<LookupItem> Purposes { get; init; } = [];

    public IReadOnlyList<LookupItem> Relationships { get; init; } = [];

    public IReadOnlyList<LookupItem> Results { get; init; } = [];

    public IReadOnlyList<LookupItem> NextActions { get; init; } = [];
}

public sealed class VisitEntry
{
    public string AccountNo { get; set; } = string.Empty;

    public string? OfficerCode { get; set; }

    public string? PurposeCode { get; set; }

    public string? PersonVisited { get; set; }

    public string? RelationshipCode { get; set; }

    public string? CustomerAddress { get; set; }

    public DateTime? VisitAt { get; set; }

    public string? ResultCode { get; set; }

    public DateTime? ResultAt { get; set; }

    public string? NextActionCode { get; set; }

    public DateTime? NextActionAt { get; set; }

    public string? Notes { get; set; }

    public ContactUpdate Update { get; } = new();
}

public sealed record VisitHistoryRow(
    DateTime Date,
    string? UserId,
    string? OfficerCode,
    string? PersonVisited,
    string? ResultCode,
    DateTime? ResultAt,
    string? NextActionCode,
    DateTime? NextActionAt,
    string? Notes);

public static class VisitRules
{
    public const int MaxNotesLength = 2000;

    public static IReadOnlyList<string> Validate(VisitEntry e)
    {
        var errors = new List<string>();
        Require(errors, e.AccountNo, "Nomor rekening");
        Require(errors, e.OfficerCode, "Petugas kunjungan");
        Require(errors, e.PurposeCode, "Tujuan");
        Require(errors, e.PersonVisited, "Orang yang ditemui");
        Require(errors, e.RelationshipCode, "Hubungan");
        Require(errors, e.Notes, "Catatan");

        if (e.Notes is { Length: > MaxNotesLength })
        {
            errors.Add($"Catatan maksimal {MaxNotesLength} karakter");
        }

        if (e.PersonVisited is { Length: > 50 })
        {
            errors.Add("Orang yang ditemui maksimal 50 karakter");
        }

        if (e.CustomerAddress is { Length: > 50 })
        {
            errors.Add("Alamat yang dikunjungi maksimal 50 karakter");
        }

        if (e.NextActionAt is not null && string.IsNullOrWhiteSpace(e.NextActionCode))
        {
            errors.Add("Pilih Rekomendasi untuk tanggal rekomendasi yang diisi");
        }

        errors.AddRange(e.Update.Validate());
        return errors;
    }

    private static void Require(List<string> errors, string? value, string label)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"{label} wajib diisi");
        }
    }
}

public interface IVisitService
{
    Task<VisitFormOptions> GetOptionsAsync(string userId, CancellationToken ct = default);

    Task<bool> CanActAsync(string userId, string accountNo, CancellationToken ct = default);

    /// <summary>Validasi, otorisasi (pemilik/atasan), lalu simpan dalam satu transaksi.</summary>
    Task<CallSaveResult> SaveAsync(string userId, VisitEntry entry, CancellationToken ct = default);

    Task<IReadOnlyList<VisitHistoryRow>> GetHistoryAsync(string accountNo, int take = 30, CancellationToken ct = default);
}
