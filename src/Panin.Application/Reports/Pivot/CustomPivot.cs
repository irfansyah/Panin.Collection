namespace Panin.Application.Reports.Pivot;

/// <summary>
/// Report pivot kustom (kelas <c>XtraReport PV_*</c> di aplikasi lama): satu SP, lalu pivot dengan tata letak tetap.
/// Tata letak diekstrak dari <c>*.Designer.cs</c> lama oleh <c>tools/ReportExtract</c>.
/// </summary>
/// <param name="Class">Nama kelas lama (nilai <c>PIVOTLIST.AssemblyObj</c>).</param>
/// <param name="DataTable">Indeks result set SP yang menjadi sumber pivot.</param>
/// <param name="SubtitleColumn">Kolom pada result set pertama yang berisi keterangan (periode) di atas laporan.</param>
public sealed record CustomPivotSpec(string Class, string Procedure, int ParamCount, int TimeoutSeconds, int DataTable, string? SubtitleColumn, PivotLayout Layout, CompanionSpec? Companion = null)
{
    public string CallSql => "EXEC " + Procedure + (ParamCount == 0 ? string.Empty : " " + string.Join(", ", Enumerable.Range(1, ParamCount).Select(i => "@" + i.ToString(System.Globalization.CultureInfo.InvariantCulture))));
}

/// <summary>Tabel pendamping di bawah pivot (mis. daftar keterangan alasan): result set ke-<paramref name="Table"/> dengan kolom dan judulnya.</summary>
public sealed record CompanionSpec(int Table, IReadOnlyList<ReportColumn> Columns);

/// <summary>Isi tabel pendamping yang sudah dibaca: judul dan baris teks.</summary>
public sealed record PivotCompanion(IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<string>> Rows);

public interface ICustomPivotCatalog
{
    CustomPivotSpec? Find(string assemblyObj);

    int Count { get; }
}
