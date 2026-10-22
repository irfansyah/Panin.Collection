namespace Panin.Application.Collection;

public sealed record LookupItem(string Code, string Text);

/// <summary>Pilihan dropdown form "Place A Call" (semua berasal dari tabel/SP referensi yang sama dengan layar lama).</summary>
public sealed class CallFormOptions
{
    public IReadOnlyList<LookupItem> Purposes { get; init; } = [];

    public IReadOnlyList<LookupItem> Relationships { get; init; } = [];

    public IReadOnlyList<LookupItem> PaymentTypes { get; init; } = [];

    public IReadOnlyList<LookupItem> Reasons { get; init; } = [];

    public IReadOnlyList<LookupItem> NextActions { get; init; } = [];

    /// <summary>Nomor yang dapat dihubungi untuk rekening ini (<c>SP_GET_CONTACT ... 'COMB'</c>).</summary>
    public IReadOnlyList<LookupItem> Contacts { get; init; } = [];

    /// <summary>Orang yang akan menyetor (<c>SP_GET_CONTACT ... 'NAMA'</c>).</summary>
    public IReadOnlyList<LookupItem> PtpPersons { get; init; } = [];
}

public sealed class CallEntry
{
    public string AccountNo { get; set; } = string.Empty;

    public string? PurposeCode { get; set; }

    public string? ContactNo { get; set; }

    public string? SpokeTo { get; set; }

    public string? RelationshipCode { get; set; }

    public string? ResultCode { get; set; }

    public DateTime? PromiseAt { get; set; }

    public string? PaymentTypeCode { get; set; }

    public string? PtpPersonId { get; set; }

    public decimal? PtpAmount { get; set; }

    public string? ReasonCode { get; set; }

    public string? NextActionCode { get; set; }

    public DateTime? NextActionAt { get; set; }

    public string? Notes { get; set; }

    /// <summary>Bagian "Hasil Call": perubahan telepon/alamat/jaminan dan status surat.</summary>
    public ContactUpdate Update { get; } = new();
}

public sealed record CallHistoryRow(
    DateTime Date,
    string? Time,
    string? UserId,
    string? ResultCode,
    DateTime? PromiseAt,
    string? NextActionCode,
    DateTime? NextActionAt,
    string? ReasonCode,
    string? ContactNo,
    string? SpokeTo,
    string? Notes);

public sealed record CallSaveResult(bool Success, IReadOnlyList<string> Errors)
{
    public static CallSaveResult Ok { get; } = new(true, []);

    public static CallSaveResult Fail(params string[] errors) => new(false, errors);
}

public static class CallRules
{
    /// <summary>Hasil "Janji bayar": tanggal janji, tipe pembayaran, dan jumlah menjadi wajib.</summary>
    public const string PromiseResultCode = "R0020";

    /// <summary>Hubungan "Yang Bersangkutan" (debitur sendiri).</summary>
    public const string DebtorRelationship = "00000";

    public const int MaxNotesLength = 2000;

    /// <summary>Validasi yang tidak membutuhkan DB (mengikuti kolom <c>mandatory</c> dan JavaScript layar lama).</summary>
    public static IReadOnlyList<string> Validate(CallEntry e)
    {
        var errors = new List<string>();
        Require(errors, e.AccountNo, "Nomor rekening");
        Require(errors, e.PurposeCode, "Tujuan");
        Require(errors, e.ContactNo, "No. yang dihubungi");
        Require(errors, e.RelationshipCode, "Hubungan");
        Require(errors, e.ResultCode, "Janji Debitur");
        Require(errors, e.ReasonCode, "Alasan tidak bayar");
        Require(errors, e.Notes, "Catatan");

        if (e.Notes is { Length: > MaxNotesLength })
        {
            errors.Add($"Catatan maksimal {MaxNotesLength} karakter");
        }

        if (e.ResultCode == PromiseResultCode)
        {
            if (e.PromiseAt is null)
            {
                errors.Add("Tanggal Janji wajib diisi");
            }

            Require(errors, e.PaymentTypeCode, "Tipe Pembayaran");
            if (e.PtpAmount is null or <= 0)
            {
                errors.Add("Jumlah yang Akan Setor wajib diisi dan lebih dari nol");
            }
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

public interface ICallService
{
    Task<CallFormOptions> GetOptionsAsync(string accountNo, CancellationToken ct = default);

    /// <summary>Daftar hasil call; bergantung pada hubungan (<c>SP_PTP_CHANGELIST</c>: "Info Restrukturisasi" hanya untuk debitur sendiri).</summary>
    Task<IReadOnlyList<LookupItem>> GetResultsAsync(string? relationshipCode, CancellationToken ct = default);

    /// <summary>Boleh mencatat aktivitas pada rekening ini? Pemilik rekening atau atasannya (<c>TEMP_DOWNLINE</c>).</summary>
    Task<bool> CanActAsync(string userId, string accountNo, CancellationToken ct = default);

    /// <summary>Validasi penuh (aturan lokal + batas hari janji di DB), otorisasi, lalu simpan dalam satu transaksi.</summary>
    Task<CallSaveResult> SaveAsync(string userId, CallEntry entry, CancellationToken ct = default);

    Task<IReadOnlyList<CallHistoryRow>> GetHistoryAsync(string accountNo, int take = 30, CancellationToken ct = default);
}

public sealed record TreatmentOption(string Title, string Function);

public interface ITreatmentMenuService
{
    /// <summary>Tindakan yang boleh dilakukan user (Call, Letter, Visitation, ...) menurut <c>SP_GET_TREATMENT_MENU</c>.</summary>
    Task<IReadOnlyList<TreatmentOption>> GetAsync(string userId, CancellationToken ct = default);
}
