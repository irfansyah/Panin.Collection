namespace Panin.Application.Status;

/// <summary>Cabang/area/kota user (<c>VW_AUTH_USERAREA</c> di DB DATA modul 60).</summary>
public sealed record UserArea(string BranchCode, string AreaId, string CityId);

/// <summary>Kesegaran data (<c>MAX(AS_OF_DATE)</c> di <c>ACCOUNT_MASTER</c>).</summary>
public sealed record DataFreshness(DateTime LastUpdate, int DaysBehind)
{
    public bool IsStale => DaysBehind > 0;
}

/// <param name="Cells">Nilai sel per kolom (HTML dari SP lama sudah dibersihkan).</param>
/// <param name="AccountNo">Nomor akun, bila kolom pertama berupa tautan lama.</param>
/// <param name="CustomerRef">Referensi nasabah dari tautan lama.</param>
public sealed record WarningRow(IReadOnlyDictionary<string, string> Cells, string? AccountNo, string? CustomerRef);

/// <summary>Hasil <c>SP_VW_WARNINGCONTENT</c>: judul + daftar baris peringatan.</summary>
public sealed record WarningSet(string Title, IReadOnlyList<string> Columns, IReadOnlyList<WarningRow> Rows)
{
    public static readonly WarningSet Empty = new(string.Empty, [], []);

    public bool HasRows => Rows.Count > 0;
}

public interface IUserAreaService
{
    /// <summary>Area user, atau null bila belum ada data / DB DATA belum dikonfigurasi.</summary>
    Task<UserArea?> GetAsync(string userId, CancellationToken ct = default);
}

public interface IDataStatusService
{
    /// <summary>Kesegaran data (di-cache dan dibagi semua user), atau null bila tidak dapat dibaca.</summary>
    Task<DataFreshness?> GetFreshnessAsync(CancellationToken ct = default);
}

public interface IWarningService
{
    Task<WarningSet> GetAsync(string userId, CancellationToken ct = default);
}
