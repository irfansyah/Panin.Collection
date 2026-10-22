namespace Panin.Application.Collection;

/// <summary>Kriteria "Inquiry Status Debitur" (menu 4010). Boleh diisi satu atau lebih; kosong = diabaikan.</summary>
public sealed record DebiturSearchCriteria(string? AccountNo, string? CustomerRef, string? Name)
{
    public DebiturSearchCriteria Normalized() => new(Clean(AccountNo), Clean(CustomerRef), Clean(Name));

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

public static class DebiturSearchRules
{
    /// <summary>
    /// Batas baris hasil. <c>SP_SEARCH_DEBITUR</c> lama tidak membatasi: pencarian nama pendek bisa mengembalikan
    /// ratusan ribu baris (±1 menit) dan memenuhi memori server bila dilayani 700 user.
    /// </summary>
    public const int MaxRows = 500;

    /// <summary>Pencarian nama memakai LIKE '%...%' (memindai seluruh data), jadi butuh minimal sekian huruf.</summary>
    public const int MinNameLength = 3;

    /// <returns>Pesan kesalahan, atau null bila kriteria valid.</returns>
    public static string? Validate(DebiturSearchCriteria criteria)
    {
        var c = criteria.Normalized();
        if (c.AccountNo is null && c.CustomerRef is null && c.Name is null)
        {
            return "Pilih salah satu kriteria pencarian";
        }

        if (c.Name is { Length: < MinNameLength } && c.AccountNo is null && c.CustomerRef is null)
        {
            return $"Nama debitur minimal {MinNameLength} huruf";
        }

        return null;
    }
}

public sealed class DebiturRow
{
    public string AccountNo { get; set; } = string.Empty;

    public string? CustomerRef { get; set; }

    public string? CustomerName { get; set; }

    public string? Facility { get; set; }

    public double? Outstanding { get; set; }

    public int? Dpd { get; set; }

    public string? Collectibility { get; set; }

    public string? LastAction { get; set; }

    public int InstallmentNo { get; set; }

    public string? Recommendation { get; set; }

    public string? Officer { get; set; }

    /// <summary>Status rekening di <c>ACCOUNT_MASTER</c>; 023/024 = rekening recovery.</summary>
    public string? AccountStatus { get; set; }

    public bool IsRecovery => AccountStatus is "023" or "024";
}

public sealed record DebiturSearchResult(IReadOnlyList<DebiturRow> Rows, bool Truncated, int MaxRows)
{
    public static DebiturSearchResult Empty { get; } = new([], false, DebiturSearchRules.MaxRows);
}

public interface IDebiturSearchService
{
    /// <exception cref="ArgumentException">Kriteria tidak valid (lihat <see cref="DebiturSearchRules.Validate"/>).</exception>
    Task<DebiturSearchResult> SearchAsync(DebiturSearchCriteria criteria, CancellationToken ct = default);
}
