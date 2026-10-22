using System.Globalization;

namespace Panin.Web.Components.Dashboard;

public sealed record ChartSeries(string Name, string Color);

/// <summary>Satu kolom grafik; <see cref="Values"/> mengikuti urutan <see cref="ChartSeries"/> (yang pertama di dasar kolom).</summary>
public sealed record ChartColumn(string Label, IReadOnlyList<double> Values, string? Tooltip = null, bool Highlight = false)
{
    public double Total => Values.Sum();
}

public sealed record BarItem(string Label, double Value, string? Hint = null, string Tone = "info");

public sealed record LinePoint(string Label, double Value);

/// <summary>Hitungan murni untuk grafik (agar dapat diuji tanpa komponen).</summary>
public static class ChartMath
{
    /// <summary>Tinggi kolom dalam persen dari nilai terbesar; kolom kosong tetap tampak sebagai garis tipis (1%), kolom berisi minimal 4%.</summary>
    public static double ColumnPercent(double total, double max)
    {
        if (max <= 0 || total <= 0)
        {
            return 1;
        }

        return Math.Clamp(total * 100 / max, 4, 100);
    }

    /// <summary>Porsi tiap seri dalam satu kolom (persen dari total kolom); jumlahnya 100 bila ada isi.</summary>
    public static IReadOnlyList<double> StackPercents(IReadOnlyList<double> values)
    {
        var total = values.Sum();
        return total <= 0 ? [.. values.Select(_ => 0d)] : [.. values.Select(v => v * 100 / total)];
    }

    public static double Percent(double part, double whole) => whole <= 0 ? 0 : Math.Clamp(part * 100 / whole, 0, 100);

    public static string Css(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>Warna nada kartu/batang.</summary>
    public static string ToneColor(string tone) => tone switch
    {
        "ok" => "#059669",
        "warn" => "#d97706",
        "bad" => "#e11d48",
        _ => "#2f6fdc",
    };

    /// <summary>Titik poligon garis: x merata, y dibalik (0 di bawah). Mengembalikan pasangan koordinat piksel.</summary>
    public static IReadOnlyList<(double X, double Y)> LineCoordinates(IReadOnlyList<double> values, double width, double height, double padding)
    {
        if (values.Count == 0)
        {
            return [];
        }

        var min = values.Min();
        var max = values.Max();
        var span = max - min;
        if (span <= 0)
        {
            span = 1;
            min -= .5;
        }

        double X(int i) => values.Count == 1 ? width / 2 : padding + (i * (width - (2 * padding)) / (values.Count - 1));
        double Y(double v) => height - padding - ((v - min) * (height - (2 * padding)) / span);
        return [.. values.Select((v, i) => (X(i), Y(v)))];
    }
}
