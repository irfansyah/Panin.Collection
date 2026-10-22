namespace Panin.Application.Collection;

/// <summary>
/// Bagian "Hasil" yang dipakai bersama form Call dan Visit: perubahan telepon, alamat, kondisi jaminan, dan status surat
/// (23 kolom di <c>SAVE_CALL_HISTORY</c>/<c>SAVE_VISIT_HISTORY</c>). Pilihan pertama setiap deskripsi berarti "tanpa perubahan"
/// dan dikirim NULL; teks baru hanya dikirim bila deskripsi yang dipilih memang meminta teks, seperti kolom yang dibuka layar lama.
/// </summary>
public sealed class ContactUpdate
{
    public const string Unchanged = "Tetap";

    public static IReadOnlyList<string> PhoneChoices { get; } = [Unchanged, "Berubah menjadi", "Tidak ada", "Lainnya"];

    public static IReadOnlyList<string> HousePhoneChoices { get; } = [Unchanged, "Berubah menjadi", "Tidak ada", "lainnya"];

    public static IReadOnlyList<string> CorrespondenceChoices { get; } = [Unchanged, "Pindah ke"];

    public static IReadOnlyList<string> OfficeAddressChoices { get; } = [Unchanged, "Pindah ke"];

    public static IReadOnlyList<string> HouseAddressChoices { get; } = [Unchanged, "Lainnya"];

    public static IReadOnlyList<string> CollateralStatusChoices { get; } = ["", "Ditempati", "Kosong", "Dikontrakkan", "Lainnya"];

    public static IReadOnlyList<string> CollateralConditionChoices { get; } = ["", "Terawat", "Tidak terawat", "Lainnya"];

    public string? LetterStatus { get; set; }

    public string HousePhone { get; set; } = Unchanged;

    public string? HousePhoneNew { get; set; }

    public string OfficePhone { get; set; } = Unchanged;

    public string? OfficePhoneNew { get; set; }

    public string Mobile { get; set; } = Unchanged;

    public string? MobileNew { get; set; }

    public string CorrespondenceAddress { get; set; } = Unchanged;

    public string? CorrespondenceNew { get; set; }

    public string? CorrespondenceNew2 { get; set; }

    public string? CorrespondenceCity { get; set; }

    public string? CorrespondenceZip { get; set; }

    public string HouseAddress { get; set; } = Unchanged;

    public string? HouseNew { get; set; }

    public string? HouseNew2 { get; set; }

    public string? HouseCity { get; set; }

    public string? HouseZip { get; set; }

    public string OfficeAddress { get; set; } = Unchanged;

    public string? OfficeNew { get; set; }

    public string? CollateralStatus { get; set; }

    public string? CollateralStatusNew { get; set; }

    public string? CollateralCondition { get; set; }

    public string? CollateralConditionNew { get; set; }

    // Teks baru hanya berlaku bila deskripsinya memintanya (aturan enableText layar lama).
    public static bool NeedsPhoneText(string? desc) => string.Equals(desc, "Berubah menjadi", StringComparison.OrdinalIgnoreCase);

    public static bool NeedsAddressText(string? desc) => !string.IsNullOrWhiteSpace(desc) && !string.Equals(desc.Trim(), Unchanged, StringComparison.Ordinal);

    public static bool NeedsOtherText(string? desc) => string.Equals(desc?.Trim(), "Lainnya", StringComparison.Ordinal);

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        Text(errors, NeedsPhoneText(HousePhone), HousePhoneNew, "Telp Rumah", 50);
        Text(errors, NeedsPhoneText(OfficePhone), OfficePhoneNew, "Telp Kantor", 50);
        Text(errors, NeedsPhoneText(Mobile), MobileNew, "Telp Selular", 20);
        Text(errors, NeedsAddressText(CorrespondenceAddress), CorrespondenceNew, "Alamat Korespondensi", 40);
        Text(errors, false, CorrespondenceNew2, "Alamat Korespondensi (lanjutan)", 40);
        Text(errors, false, CorrespondenceZip, "Kode Pos Korespondensi", 20);
        Text(errors, NeedsAddressText(HouseAddress), HouseNew, "Alamat Rumah", 40);
        Text(errors, false, HouseNew2, "Alamat Rumah (lanjutan)", 40);
        Text(errors, false, HouseZip, "Kode Pos Rumah", 20);
        Text(errors, NeedsAddressText(OfficeAddress), OfficeNew, "Alamat Kantor", 120);
        Text(errors, NeedsOtherText(CollateralStatus), CollateralStatusNew, "Status Jaminan", 100);
        Text(errors, NeedsOtherText(CollateralCondition), CollateralConditionNew, "Kondisi Jaminan", 100);
        return errors;
    }

    /// <summary>
    /// Mengisi slot parameter yang dipakai bersama kedua SP: 12 (surat), 13–28 (deskripsi/teks baru), 30–36 (alamat lanjutan,
    /// kota, kode pos). <paramref name="letterCondition"/> adalah isi slot 30 (<c>@LETTER_COND</c>), yang diisi berbeda oleh Call dan Visit.
    /// </summary>
    public void Fill(object?[] a, string? letterCondition)
    {
        a[12] = Blank(LetterStatus);
        Pair(a, 13, HousePhone, NeedsPhoneText(HousePhone) ? HousePhoneNew : null);
        Pair(a, 15, OfficePhone, NeedsPhoneText(OfficePhone) ? OfficePhoneNew : null);
        Pair(a, 17, Mobile, NeedsPhoneText(Mobile) ? MobileNew : null);
        Pair(a, 19, CorrespondenceAddress, NeedsAddressText(CorrespondenceAddress) ? CorrespondenceNew : null);
        Pair(a, 21, HouseAddress, NeedsAddressText(HouseAddress) ? HouseNew : null);
        Pair(a, 23, OfficeAddress, NeedsAddressText(OfficeAddress) ? OfficeNew : null);
        Pair(a, 25, CollateralStatus, NeedsOtherText(CollateralStatus) ? CollateralStatusNew : null);
        Pair(a, 27, CollateralCondition, NeedsOtherText(CollateralCondition) ? CollateralConditionNew : null);

        var corresp = NeedsAddressText(CorrespondenceAddress);
        var house = NeedsAddressText(HouseAddress);
        a[30] = Blank(letterCondition);
        a[31] = corresp ? Blank(CorrespondenceNew2) : null;
        a[32] = corresp ? Blank(CorrespondenceCity) : null;
        a[33] = corresp ? Blank(CorrespondenceZip) : null;
        a[34] = house ? Blank(HouseNew2) : null;
        a[35] = house ? Blank(HouseCity) : null;
        a[36] = house ? Blank(HouseZip) : null;
    }

    // Deskripsi pertama ("Tetap"/kosong) = tanpa perubahan = NULL.
    private static void Pair(object?[] a, int index, string? desc, string? text)
    {
        var first = string.IsNullOrWhiteSpace(desc) || desc.Trim() == Unchanged;
        a[index] = first ? null : desc!.Trim();
        a[index + 1] = first ? null : Blank(text);
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void Text(List<string> errors, bool required, string? value, string label, int max)
    {
        if (required && string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"{label} baru wajib diisi");
        }

        if (value is { Length: > 0 } && value.Trim().Length > max)
        {
            errors.Add($"{label} maksimal {max} karakter");
        }
    }
}

/// <summary>Referensi untuk bagian "Hasil": status surat (<c>SP_GET_RESULT 'Letter'</c>) dan kota (<c>SP_GET_CITY</c>), dicache bersama.</summary>
public interface IContactReferenceService
{
    Task<IReadOnlyList<LookupItem>> GetLetterStatusesAsync(CancellationToken ct = default);

    Task<IReadOnlyList<LookupItem>> GetCitiesAsync(CancellationToken ct = default);
}
