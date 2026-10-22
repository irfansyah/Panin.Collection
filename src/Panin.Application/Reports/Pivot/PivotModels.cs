namespace Panin.Application.Reports.Pivot;

/// <summary>Pengelompokan nilai tanggal pada field baris/kolom (<c>year</c>, <c>month</c>, <c>day</c>, <c>date</c> di definisi lama).</summary>
public enum PivotInterval
{
    None,
    Year,
    Month,
    Day,
    Date,
}

public enum PivotAggregate
{
    Sum,
    Count,
    Average,
    Min,
    Max,
    PercentOfColumn,
    PercentOfRow,
}

/// <param name="Key">Nama kolom pada sumber data (alias hasil query atau nama kolom SP).</param>
/// <param name="Format">Pola .NET untuk nilai field bertipe tanggal/angka (mis. <c>dd MMM yyyy</c>); kosong = bawaan.</param>
/// <param name="SortByMeasure">Urutkan anggota field ini menurut nilai ukuran tersebut (urutan naik), bukan menurut nilainya sendiri. Dipakai kolom bantu <c>URUTAN</c> pada report kustom.</param>
public sealed record PivotField(string Key, string Caption, PivotInterval Interval = PivotInterval.None, string? SortByMeasure = null, bool ShowTotals = true, string? Format = null);

/// <param name="Visible">False = ukuran hanya untuk mengurutkan, tidak ditampilkan.</param>
/// <param name="ShowGrandTotal">False = baris dan kolom total keseluruhan tidak dihitung untuk ukuran ini.</param>
/// <param name="Format">Pola angka .NET untuk tampilan (mis. <c>N0</c>, <c>N2</c>); kosong = otomatis.</param>
public sealed record PivotMeasure(string Key, string Caption, PivotAggregate Aggregate = PivotAggregate.Sum, bool Visible = true, bool ShowGrandTotal = true, string? Format = null);

/// <summary>
/// Susunan pivot. <c>RowTotals</c>/<c>ColumnTotals</c> = baris/kolom subtotal untuk field luar; <c>RowGrandTotal</c> = baris total di bawah;
/// <c>ColumnGrandTotal</c> = kolom total di kanan (istilah mengikuti DevExpress yang dipakai layar lama).
/// </summary>
public sealed record PivotLayout(
    IReadOnlyList<PivotField> Rows,
    IReadOnlyList<PivotField> Columns,
    IReadOnlyList<PivotMeasure> Measures,
    bool RowTotals = true,
    bool RowGrandTotal = true,
    bool ColumnTotals = true,
    bool ColumnGrandTotal = true,
    bool TotalsForSingleValues = true);

/// <summary>Agregat parsial satu ukuran pada satu kelompok halus; dapat digabung tanpa kehilangan ketepatan (rata-rata = jumlah / banyaknya).</summary>
public struct PivotPartial
{
    public double Sum;
    public long Count;
    public double Min;
    public double Max;

    public void Add(double value)
    {
        Sum += value;
        Min = Count == 0 ? value : Math.Min(Min, value);
        Max = Count == 0 ? value : Math.Max(Max, value);
        Count++;
    }

    public void Merge(in PivotPartial other)
    {
        if (other.Count == 0)
        {
            return;
        }

        Min = Count == 0 ? other.Min : Math.Min(Min, other.Min);
        Max = Count == 0 ? other.Max : Math.Max(Max, other.Max);
        Sum += other.Sum;
        Count += other.Count;
    }
}

/// <summary>Satu kelompok halus: nilai field baris, nilai field kolom, dan agregat parsial tiap ukuran.</summary>
public sealed record PivotRecord(object?[] Rows, object?[] Columns, PivotPartial[] Measures);

public enum PivotLineKind
{
    Leaf,
    Subtotal,
    GrandTotal,
}

/// <param name="Path">Nilai field dari terluar sampai level baris ini (panjang kurang dari jumlah field untuk subtotal; kosong untuk total keseluruhan).</param>
public sealed record PivotRowLine(IReadOnlyList<string> Path, PivotLineKind Kind);

/// <param name="MeasureIndex">Indeks ukuran (di antara ukuran yang tampil) yang ditampilkan kolom ini.</param>
public sealed record PivotColumnLine(IReadOnlyList<string> Path, PivotLineKind Kind, int MeasureIndex);

public sealed class PivotResult
{
    public required IReadOnlyList<string> RowCaptions { get; init; }

    public required IReadOnlyList<string> ColumnCaptions { get; init; }

    public required IReadOnlyList<PivotMeasure> VisibleMeasures { get; init; }

    public required IReadOnlyList<PivotRowLine> Rows { get; init; }

    public required IReadOnlyList<PivotColumnLine> Columns { get; init; }

    /// <summary>Nilai sel [baris][kolom]; <c>null</c> = tidak ada data.</summary>
    public required double?[][] Cells { get; init; }

    /// <summary>True bila jumlah sel melampaui batas dan hasil dipotong.</summary>
    public bool Truncated { get; init; }

    public string Describe(double? value, int measureIndex)
    {
        if (value is not { } v)
        {
            return string.Empty;
        }

        var m = VisibleMeasures[measureIndex];
        var format = m.Format ?? (m.Aggregate is PivotAggregate.PercentOfColumn or PivotAggregate.PercentOfRow ? "N2"
            : m.Aggregate == PivotAggregate.Count ? "N0"
            : v == Math.Floor(v) && Math.Abs(v) < 1e15 ? "N0" : "N2");
        var text = v.ToString(format, Culture);
        return m.Aggregate is PivotAggregate.PercentOfColumn or PivotAggregate.PercentOfRow ? text + "%" : text;
    }

    public static System.Globalization.CultureInfo Culture { get; } = System.Globalization.CultureInfo.GetCultureInfo("id-ID");
}
