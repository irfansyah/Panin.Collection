namespace Panin.Application.Collection;

/// <summary>Anggota tim pada layar Distribusi Debitur ("User-user dibawah pengawasan ...").</summary>
public sealed class WorklistTeamMember
{
    public string UserId { get; set; } = string.Empty;

    public string? Name { get; set; }

    public string? Group { get; set; }

    public string? Branch { get; set; }

    /// <summary>Debitur yang sedang dikerjakan user ini beserta seluruh bawahannya.</summary>
    public int DebitorCount { get; set; }
}

public sealed class WorklistDebitorRow
{
    public string AccountNo { get; set; } = string.Empty;

    public string? CustomerRef { get; set; }

    public string? Name { get; set; }

    public string? Facility { get; set; }

    public DateTime? AssignedOn { get; set; }

    public int? DaysHandled { get; set; }

    public int? Dpd { get; set; }

    /// <summary>Status tindakan: Not Yet, On Process, atau Done.</summary>
    public string? Action { get; set; }

    public string? Recommendation { get; set; }

    public string? AccountStatus { get; set; }

    /// <summary>No, Permanent, atau Temporary (rekening yang dipindah manual).</summary>
    public string? Reassign { get; set; }
}

public sealed record WorklistDebitorResult(IReadOnlyList<WorklistDebitorRow> Rows, int Total, int MaxRows)
{
    public bool Truncated => Total > Rows.Count;
}

public sealed class ReassignRequest
{
    public string TargetUserId { get; set; } = string.Empty;

    /// <summary>User yang dipilih di tabel tim: seluruh debitur user itu beserta bawahannya ikut pindah (perilaku layar lama).</summary>
    public IReadOnlyList<string> GroupUserIds { get; set; } = [];

    public IReadOnlyList<string> AccountNos { get; set; } = [];

    public bool Permanent { get; set; } = true;

    /// <summary>Tanggal berakhir untuk pemindahan sementara.</summary>
    public DateTime? Until { get; set; }
}

public sealed record ReassignResult(bool Success, IReadOnlyList<string> Errors, string? TargetName = null)
{
    public static ReassignResult Fail(params string[] errors) => new(false, errors);
}

public static class WorklistRules
{
    public const int MaxDebitors = 2000;

    /// <summary>
    /// <c>SP_REASIGN</c> menerima daftar rekening sebagai <c>varchar(4000)</c>; daftar yang lebih panjang terpotong diam-diam di
    /// layar lama. Di sini daftar dipecah per kelompok sebesar ini, dalam satu transaksi.
    /// </summary>
    public const int AccountsPerCall = 250;

    public static IReadOnlyList<string> Validate(ReassignRequest r, DateTime today)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(r.TargetUserId))
        {
            errors.Add("User tujuan wajib diisi");
        }

        if (r.GroupUserIds.Count == 0 && r.AccountNos.Count == 0)
        {
            errors.Add("Pilih minimal satu user atau debitur yang akan dipindahkan");
        }

        if (r.AccountNos.Count > MaxDebitors)
        {
            errors.Add($"Maksimal {MaxDebitors} debitur sekali pindah");
        }

        if (r.AccountNos.Concat(r.GroupUserIds).Any(x => !IsSafeToken(x)))
        {
            errors.Add("Pilihan mengandung karakter yang tidak valid");
        }

        if (!r.Permanent && (r.Until is null || r.Until.Value.Date < today.Date))
        {
            errors.Add("Jika tidak permanent, tanggal sampai tidak boleh sebelum tanggal sekarang");
        }

        return errors;
    }

    // Nomor rekening dan user ID hanya huruf/angka/garis bawah/titik; daftar dikirim ke SP sebagai teks dipisah koma.
    private static bool IsSafeToken(string value)
        => value.Length is > 0 and <= 40 && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '.' or '-');
}

public interface IWorklistService
{
    /// <summary>Diri sendiri atau bawahan (langsung atau tidak langsung)?</summary>
    Task<bool> CanViewAsync(string viewerUserId, string targetUserId, CancellationToken ct = default);

    Task<IReadOnlyList<WorklistTeamMember>> GetTeamAsync(string userId, CancellationToken ct = default);

    Task<WorklistDebitorResult> GetDebitorsAsync(string userId, CancellationToken ct = default);

    /// <summary>Validasi, otorisasi (user tujuan, user terpilih, dan setiap rekening harus diri sendiri atau bawahan), lalu pindah dalam satu transaksi.</summary>
    Task<ReassignResult> ReassignAsync(string actorUserId, ReassignRequest request, CancellationToken ct = default);
}
