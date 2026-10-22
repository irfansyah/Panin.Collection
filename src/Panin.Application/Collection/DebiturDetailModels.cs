namespace Panin.Application.Collection;

/// <summary>Satu baris data dari view/SP (kolom -> nilai). DBNull menjadi null.</summary>
public sealed class DetailRecord(IReadOnlyDictionary<string, object?> values)
{
    public IReadOnlyDictionary<string, object?> Values { get; } = values;

    public string? Text(string column)
        => Values.TryGetValue(column, out var v) && v is not null
            ? v is DateTime d ? d.ToString("dd-MMM-yyyy", System.Globalization.CultureInfo.InvariantCulture) : Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture)?.Trim()
            : null;
}

public sealed record DetailField(string Label, string Column);

public sealed record DelinquencyMonth(int Year, int Month, string Code, int? Dpd)
{
    public string Period => $"{Year}-{Month:00}";
}

public interface IDebiturDetailService
{
    /// <summary>Data kredit dan tunggakan (<c>VW_ACCOUNT_SURAT</c>), atau null bila rekening tidak ada.</summary>
    Task<DetailRecord?> GetAccountAsync(string accountNo, CancellationToken ct = default);

    /// <summary>Rekening produk revolving (PRK, <c>APP_TYPE = 20</c>): beberapa bidang (cerukan, bunga) menjadi sorotan.</summary>
    Task<bool> IsRevolvingAsync(string accountNo, CancellationToken ct = default);

    Task<IReadOnlyList<DelinquencyMonth>> GetDelinquencyAsync(string accountNo, CancellationToken ct = default);

    Task<DetailRecord?> GetPersonalAsync(string accountNo, CancellationToken ct = default);

    Task<DetailRecord?> GetJobAsync(string accountNo, CancellationToken ct = default);

    Task<DetailRecord?> GetSpouseAsync(string accountNo, CancellationToken ct = default);

    Task<DetailRecord?> GetSpouseJobAsync(string accountNo, CancellationToken ct = default);

    Task<IReadOnlyList<DetailRecord>> GetContactsAsync(string customerRef, CancellationToken ct = default);
}

/// <summary>
/// Definisi bidang layar "Monitoring Collection" dan info debitur, mengikuti label layar lama (TopTitle, personalinfo,
/// personaljob, spouseinfo, spousejob). Nilai <c>VW_ACCOUNT_SURAT</c> sudah berupa teks terformat dari view.
/// </summary>
public static class DebiturDetailFields
{
    public static readonly IReadOnlyList<DetailField> Customer =
    [
        new("No CIF", "CIF NO."),
        new("No Rekening", "ACC_NO"),
        new("Nama Nasabah", "CUSTOMER NAME"),
        new("Cabang", "BRANCH_NAME"),
        new("Fasilitas", "FASILITAS"),
        new("Status Rekening", "STATUS_REK_DESC"),
        new("User yang Menangani", "USER_YG_MENANGANI"),
        new("Kode AO", "KODE_AO"),
        new("Nama Officer", "OFFICER_NAMA"),
        new("Source Code", "SOURCE_CODE"),
        new("Sektor Ekonomi", "ECO_SECTOR_DESCRIPTION"),
    ];

    public static readonly IReadOnlyList<DetailField> Credit =
    [
        new("Maksimum Kredit", "MAKSIMUM_KREDIT"),
        new("No PK", "PK_NO"),
        new("Tanggal PK", "PK_DATE"),
        new("Tenor", "TENOR"),
        new("Tanggal Jatuh Tempo", "TGL_JATUH_TEMPO"),
        new("Jatuh Tempo Asli", "MATURITY_DATE_ORIGINAL"),
        new("Suku Bunga", "SUKU_BUNGA"),
        new("Kolektibilitas", "KOLEKTIBILITAS"),
        new("Kredit Ke", "Kredit_ke"),
        new("EWS", "EWS"),
    ];

    public static readonly IReadOnlyList<DetailField> Arrears =
    [
        new("DPD", "DPD"),
        new("Saldo Akhir", "SALDO_AKHIR"),
        new("Outstanding", "OUTSTANDING"),
        new("Tunggakan Pokok", "TUNGGAKAN_POKOK"),
        new("Tunggakan Bunga", "TUNGGAKAN_BUNGA"),
        new("Tunggakan Denda", "TUNGGAKAN_DENDA"),
        new("Total Tunggakan", "TOTAL_TUNGGAKAN"),
        new("Angsuran per Bulan", "ANGSURAN_PERBULAN"),
        new("Total Kewajiban", "TOTAL_KEWAJIBAN"),
        new("Kurang Setor", "KURANG_SETOR"),
        new("Tanggal Berubah NPL", "TGL_BERUBAH_NPL"),
        new("Tanggal Pembayaran Terakhir", "TGL_TRANSAKSI_TERAKHIR"),
    ];

    public static readonly IReadOnlyList<DetailField> Other =
    [
        new("Rekening Afiliasi", "REKENING_AFILIASI"),
        new("Saldo Afiliasi", "SALDO_AFILIASI"),
        new("Saldo Minimum (Bunga s/d Hari Ini)", "SALDO_MINIMUM"),
        new("Cerukan", "CERUKAN"),
        new("Total Cerukan s/d Hari Ini", "TOT_CRKN_SMP_HR_INI"),
        new("Faktor Bunga Harian", "DAILY_INT_FACTOR"),
        new("Dana Ditahan", "HOLD_AMOUNT"),
        new("Charge Off (Jumlah)", "CHARGE_OFF_AMOUNT"),
        new("Charge Off (Tanggal)", "CHARGE_OFF_DATE"),
        new("User Sebelumnya", "BEFORE_USER"),
        new("Dipindah Oleh", "MOVER_USER"),
        new("Tanggal Update Data", "TGL_UPDATE"),
    ];

    public static readonly IReadOnlyList<DetailField> Personal =
    [
        new("Nama Depan", "CU_FIRSTNAME"),
        new("Nama Tengah", "CU_MIDDLENAME"),
        new("Nama Belakang", "CU_LASTNAME"),
        new("No KTP", "CP_IDNUM"),
        new("Alamat KTP", "CU_ADDR1"),
        new("Alamat KTP (2)", "CU_ADDR2"),
        new("Alamat KTP (3)", "CU_ADDR3"),
        new("Kodepos", "CU_ZIP_CODE"),
        new("Kota", "CU_CITY"),
        new("Telepon", "CU_PHNNUM"),
        new("HP", "CP_HPNUM"),
        new("E-Mail", "CU_EMAIL"),
        new("Jenis Kelamin", "SEXDESC"),
        new("Tempat Lahir", "CP_POB"),
        new("Tanggal Lahir", "CP_DOB"),
        new("Kebangsaan", "NA_DESC"),
        new("Status Perkawinan", "MARITALDESC"),
        new("Nama Perusahaan", "CP_COMPANYNAME"),
        new("Jenis Pekerjaan", "OCC_DESC"),
        new("Telepon Kantor", "CP_OFFNUM"),
        new("Income", "CP_INCOMETYPE_DESC"),
    ];

    public static readonly IReadOnlyList<DetailField> Job =
    [
        new("Tipe Pekerjaan", "DES"),
        new("Kepemilikan Perusahaan", "CP_DESC"),
        new("Nama Perusahaan", "CU_OFNAME"),
        new("Alamat", "CU_OFADDR1"),
        new("Alamat (2)", "CU_OFADDR2"),
        new("Alamat (3)", "CU_OFADDR3"),
        new("Kodepos", "CU_OFPOSCODE"),
        new("Kota", "CITY_NAME"),
        new("Jabatan Pekerjaan", "JT_DESC"),
        new("Jenis Bidang Usaha", "BT_DESC"),
        new("Posisi", "PS_NAME"),
        new("Departemen", "CU_OFDEPT"),
        new("NIP", "CU_NIP"),
        new("Umur Pensiun", "CU_PENSIUN"),
        new("Pendapatan (Neto per Bulan)", "CU_OFINCNETMM"),
        new("Pendapatan Lain", "CU_OFINCOTH"),
        new("Total Pendapatan per Bulan", "CU_OFINCMMTOT"),
        new("Total Expenses", "CU_OFOUTINCTOT"),
        new("Tunjangan Hari Tua (THT)", "CU_THT"),
        new("Tipe Perusahaan", "CU_OFCOMPTYPE"),
        new("Telepon Kantor (Area)", "CU_OFPHAREA"),
        new("Telepon Kantor", "CU_OFPHNNUM"),
        new("Telepon Lainnya", "CU_OFPHNOTH"),
    ];

    public static readonly IReadOnlyList<DetailField> Spouse =
    [
        new("Gelar Sebelum Nama", "CU_SPTITLE"),
        new("Gelar Setelah Nama", "CU_SPTITLEAFT"),
        new("Nama Depan", "CU_SPNMFIRST"),
        new("Nama Tengah", "CU_SPNMMID"),
        new("Nama Belakang", "CU_SPNMLAST"),
        new("Jenis Kelamin", "CU_SPSEX"),
        new("Alamat KTP", "CU_SPKTPADDR1"),
        new("Alamat KTP (2)", "CU_SPKTPADDR2"),
        new("Alamat KTP (3)", "CU_SPKTPADDR3"),
        new("RT", "CU_SPKTPRT"),
        new("RW", "CU_SPKTPRW"),
        new("Kodepos", "CU_SPKTPZIPCODE"),
        new("Kota", "CU_SPKTPCITY"),
        new("Tempat Lahir", "CU_SPPOB"),
        new("Tanggal Lahir", "CU_SPBORNDATE"),
        new("Kebangsaan", "NA_DESC"),
        new("Pendidikan", "EDUCATIONDESC"),
        new("Pendidikan Lain", "CU_SPOTHEDU"),
        new("Email", "CU_SPEMAIL"),
        new("No. Handphone", "CU_SPMOBILE"),
        new("No. KTP", "CU_SPKTPNUM"),
    ];

    public static readonly IReadOnlyList<DetailField> SpouseJob =
    [
        new("Tipe Pekerjaan", "DES"),
        new("Kepemilikan Perusahaan", "CP_DESC"),
        new("Nama Perusahaan", "CU_SPOFNAME"),
        new("Alamat", "CU_SPOFADDR1"),
        new("Alamat (2)", "CU_SPOFADDR2"),
        new("Alamat (3)", "CU_SPOFADDR3"),
        new("Kodepos", "CU_SPOFZIPCODE"),
        new("Kota", "CU_SPOFCITY"),
        new("Jabatan Pekerjaan", "JT_DESC"),
        new("Jenis Bidang Usaha", "BT_DESC"),
        new("Posisi", "PS_NAME"),
        new("Departemen", "CU_SPDEPT"),
        new("NIP", "CU_SPNIP"),
        new("Pendapatan (Neto per Bulan)", "CU_SPOFINCNETMM"),
        new("Total Pendapatan per Bulan", "CU_SPOFINCMMTOT"),
        new("Telepon Kantor", "CU_SPOFPHNNUM"),
        new("Email", "CU_SPOFEMAIL"),
    ];

    /// <summary>Kolom <c>VW_ACCOUNT_SURAT</c> yang dipilih; hanya yang dipakai (view lebar, lihat docs/PERFORMANCE.md).</summary>
    public static IEnumerable<string> AccountColumns => Customer.Concat(Credit).Concat(Arrears).Concat(Other).Select(f => f.Column).Distinct(StringComparer.OrdinalIgnoreCase);
}
