namespace Panin.Application.Reports;

public enum ReportKind
{
    /// <summary>Daftar dinamis: kolom dan filter dari <c>PIVOTLIST</c>, satu view sumber.</summary>
    List,

    /// <summary>Pivot dinamis (baris/kolom/nilai dari <c>PIVOTLIST</c>). Belum dimigrasi (R2).</summary>
    Pivot,

    /// <summary>Laporan kelas <c>XtraReport</c> dari satu SP (<c>AssemblyObj</c>). Belum dimigrasi (R3).</summary>
    Custom,
}

public enum ReportFilterType
{
    Text,
    Number,
    NumberRange,
    List,
    Date,
    DateFrom,
    DateTo,
    DateRange,
    MonthYear,
    MonthYearRange,
}

public enum ReportSummary
{
    None,
    Count,
    Sum,
    Average,
    Min,
    Max,
}

/// <summary>Satu entri di halaman set report (menu Reporting).</summary>
public sealed record ReportListItem(string Id, string Title, ReportKind Kind, string? UnavailableReason)
{
    public bool Available => UnavailableReason is null;
}

public sealed record ReportColumn(string Field, string Caption, ReportSummary Summary);

/// <param name="Seq">Posisi parameter pertama filter ini (1-based). Filter rentang memakai dua posisi atau lebih; teks <c>@n</c> di SQL pilihan merujuk posisi ini.</param>
/// <param name="OptionsSql">Hanya untuk <see cref="ReportFilterType.List"/>: <c>SELECT kode, teks FROM ...</c> yang sudah tervalidasi aman (lihat <see cref="ReportOptionSql"/>).</param>
/// <param name="DefaultFrom">Nilai awal tanggal (filter tanggal dengan penanda <c>id</c>/<c>im</c>/<c>iy</c>).</param>
public sealed record ReportFilter(
    int Seq,
    string Field,
    string Caption,
    ReportFilterType Type,
    string? OptionsSql,
    DateOnly? DefaultFrom,
    DateOnly? DefaultTo)
{
    /// <summary>Jumlah parameter yang dipakai filter ini.</summary>
    public int ParamCount => Type switch
    {
        ReportFilterType.NumberRange or ReportFilterType.DateRange or ReportFilterType.MonthYear => 2,
        ReportFilterType.MonthYearRange => 4,
        _ => 1,
    };

    /// <summary>Posisi parameter yang dirujuk SQL pilihan (<c>@n</c>) atau kosong.</summary>
    public IReadOnlyList<int> OptionDependsOn => OptionsSql is null ? [] : ReportOptionSql.References(OptionsSql);

    /// <summary>True bila SQL pilihan merujuk posisi di luar 1..<paramref name="paramCount"/>, atau ke filter ini sendiri atau sesudahnya.</summary>
    public bool OptionDependsOnInvalid(int paramCount) => OptionDependsOn.Any(n => n < 1 || n > paramCount || n >= Seq);
}

public sealed record ReportDefinition(
    string Id,
    string Title,
    string View,
    IReadOnlyList<ReportColumn> Columns,
    IReadOnlyList<ReportFilter> Filters)
{
    public int ParamCount => Filters.Sum(f => f.ParamCount);
}

public sealed record ReportOption(string Value, string Text);

public enum ReportCellKind
{
    Text,
    Integer,
    Decimal,
    Date,
}

public sealed record ReportResultColumn(string Caption, ReportCellKind Kind, ReportSummary Summary);

public sealed record ReportResult(IReadOnlyList<ReportResultColumn> Columns, IReadOnlyList<object?[]> Rows, bool Truncated, int MaxRows);

public static class ReportLimits
{
    /// <summary>Baris yang ditampilkan di layar; sisanya lewat ekspor.</summary>
    public const int PreviewRows = 5000;

    /// <summary>Batas ekspor Excel (satu sheet .xlsx dapat memuat jauh lebih banyak, tetapi memori server dan ukuran berkas dijaga).</summary>
    public const int ExportRows = 100_000;
}

public interface IReportService
{
    /// <summary>Report dalam satu set menu (<c>GEN</c>, <c>DT</c>, ...), urut judul.</summary>
    Task<IReadOnlyList<ReportListItem>> ListAsync(string moduleId, string setId, CancellationToken ct = default);

    /// <summary>Definisi report daftar; <c>null</c> bila tidak ada atau belum didukung.</summary>
    Task<ReportDefinition?> GetAsync(string moduleId, string reportId, CancellationToken ct = default);

    /// <summary>Pilihan filter daftar; <paramref name="values"/> memuat nilai filter lain (induk) pada posisi parameternya.</summary>
    Task<IReadOnlyList<ReportOption>> GetOptionsAsync(string moduleId, ReportDefinition report, ReportFilter filter, object?[] values, CancellationToken ct = default);

    /// <param name="forExport">True memakai batas ekspor, bukan batas tampilan.</param>
    Task<ReportResult> RunAsync(string moduleId, ReportDefinition report, object?[] values, bool forExport, CancellationToken ct = default);
}
