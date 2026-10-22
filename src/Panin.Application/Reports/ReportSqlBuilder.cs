using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Panin.Application.Reports;

/// <summary>
/// Menyusun query report daftar. Nama view dan kolom hanya lolos bila berisi huruf/angka/spasi/titik/garis (tanpa tanda kurung siku,
/// kutip, titik koma), lalu selalu dikutip <c>[...]</c>; nilai filter selalu parameter posisional <c>@1..@n</c>.
/// Semantik filter mengikuti <c>ReportSys.getfiltersql</c> lama, kecuali rentang bulan–tahun (lihat <see cref="Where"/>).
/// </summary>
public static partial class ReportSqlBuilder
{
    public static bool IsSafeIdentifier(string name) => name.Length is > 0 and <= 128 && SafeName().IsMatch(name);

    public static string Quote(string name) => "[" + name + "]";

    /// <summary>SELECT TOP (<paramref name="top"/>) dengan alias kolom <c>c0..cn</c> (judul diambil dari definisi, bukan dari SQL).</summary>
    public static string Select(ReportDefinition report, int top)
    {
        var sb = new StringBuilder("SELECT TOP (").Append(top.ToString(CultureInfo.InvariantCulture)).Append(") ");
        for (var i = 0; i < report.Columns.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(", ");
            }

            sb.Append(Quote(report.View)).Append('.').Append(Quote(report.Columns[i].Field)).Append(" AS c").Append(i.ToString(CultureInfo.InvariantCulture));
        }

        return sb.Append(" FROM ").Append(Quote(report.View)).Append(" WHERE 1=1").Append(Where(report)).ToString();
    }

    /// <summary>
    /// Klausa filter; setiap filter kosong (NULL) diabaikan. Selisih dari layar lama: rentang bulan–tahun dibandingkan sebagai
    /// (tahun, bulan) utuh — layar lama membandingkan bulan dan tahun terpisah sehingga rentang yang melewati pergantian tahun salah.
    /// </summary>
    public static string Where(ReportDefinition report)
    {
        var sb = new StringBuilder();
        foreach (var f in report.Filters)
        {
            var col = Quote(report.View) + "." + Quote(f.Field);
            var a = "@" + f.Seq.ToString(CultureInfo.InvariantCulture);
            var b = "@" + (f.Seq + 1).ToString(CultureInfo.InvariantCulture);
            var c = "@" + (f.Seq + 2).ToString(CultureInfo.InvariantCulture);
            var d = "@" + (f.Seq + 3).ToString(CultureInfo.InvariantCulture);
            switch (f.Type)
            {
                case ReportFilterType.DateRange:
                    sb.Append($" AND ({a} IS NULL OR {col} >= {a}) AND ({b} IS NULL OR {col} < DATEADD(DD, 1, CONVERT(DATETIME, {b})))");
                    break;
                case ReportFilterType.DateFrom:
                    sb.Append($" AND ({a} IS NULL OR {col} >= {a})");
                    break;
                case ReportFilterType.DateTo:
                    sb.Append($" AND ({a} IS NULL OR {col} < DATEADD(DD, 1, CONVERT(DATETIME, {a})))");
                    break;
                case ReportFilterType.Date:
                    sb.Append($" AND ({a} IS NULL OR ({col} >= {a} AND {col} < DATEADD(DD, 1, CONVERT(DATETIME, {a}))))");
                    break;
                case ReportFilterType.List or ReportFilterType.Number:
                    sb.Append($" AND ({a} IS NULL OR {col} = {a})");
                    break;
                case ReportFilterType.NumberRange:
                    sb.Append($" AND ({a} IS NULL OR {col} >= {a}) AND ({b} IS NULL OR {col} <= {b})");
                    break;
                case ReportFilterType.MonthYear:
                    sb.Append($" AND ({a} IS NULL OR MONTH({col}) = {a}) AND ({b} IS NULL OR YEAR({col}) = {b})");
                    break;
                case ReportFilterType.MonthYearRange:
                    // a/b = bulan/tahun awal, c/d = bulan/tahun akhir
                    sb.Append($" AND ({a} IS NULL OR {b} IS NULL OR YEAR({col}) * 12 + MONTH({col}) >= {b} * 12 + {a})");
                    sb.Append($" AND ({c} IS NULL OR {d} IS NULL OR YEAR({col}) * 12 + MONTH({col}) <= {d} * 12 + {c})");
                    break;
                default:
                    sb.Append($" AND ({a} IS NULL OR {col} LIKE '%' + {a} + '%')");
                    break;
            }
        }

        if (report.Latest is { } latest)
        {
            var view = Quote(report.View);
            var date = Quote(latest.DateColumn);
            var key = Quote(latest.KeyColumn);
            var match = string.Concat(latest.Match.Select(m => $" AND (@{m.Param.ToString(CultureInfo.InvariantCulture)} IS NULL OR A.{Quote(m.Column)} = @{m.Param.ToString(CultureInfo.InvariantCulture)})"));
            sb.Append($" AND ({view}.{date} IS NULL OR {view}.{date} = (SELECT MAX(A.{date}) FROM {view} A WHERE A.{key} = {view}.{key}{match}))");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Mengubah nilai mentah dari layar (teks/tanggal) menjadi argumen bertipe untuk <see cref="Select"/>.
    /// Teks kosong dan <c>(none)</c> menjadi NULL; angka dan bulan/tahun yang tidak valid menjadi pesan galat.
    /// </summary>
    public static (object?[] Args, IReadOnlyList<string> Errors) Coerce(ReportDefinition report, object?[] raw)
    {
        var args = new object?[report.ParamCount];
        var errors = new List<string>();
        foreach (var f in report.Filters)
        {
            for (var k = 0; k < f.ParamCount; k++)
            {
                var index = f.Seq - 1 + k;
                var value = index < raw.Length ? raw[index] : null;
                args[index] = Convert(f, k, value, errors);
            }
        }

        return (args, errors);
    }

    private static object? Convert(ReportFilter f, int k, object? value, List<string> errors)
    {
        if (value is string s)
        {
            s = s.Trim();
            if (s.Length == 0 || s == "(none)")
            {
                return null;
            }

            value = s;
        }

        if (value is null)
        {
            return null;
        }

        switch (f.Type)
        {
            case ReportFilterType.Number or ReportFilterType.NumberRange:
                if (decimal.TryParse(System.Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Number, CultureInfo.InvariantCulture, out var number))
                {
                    return number;
                }

                errors.Add($"{f.Caption}: harus berupa angka");
                return null;
            case ReportFilterType.MonthYear or ReportFilterType.MonthYearRange:
                if (int.TryParse(System.Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
                    && (k % 2 == 0 ? n is >= 1 and <= 12 : n is >= 1900 and <= 2999))
                {
                    return n;
                }

                errors.Add(k % 2 == 0 ? $"{f.Caption}: bulan harus 1-12" : $"{f.Caption}: tahun harus 4 digit");
                return null;
            case ReportFilterType.Date or ReportFilterType.DateFrom or ReportFilterType.DateTo or ReportFilterType.DateRange:
                return value switch
                {
                    DateTime dt => dt.Date,
                    DateOnly d => d.ToDateTime(TimeOnly.MinValue),
                    _ => DateTime.TryParse(System.Convert.ToString(value, CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) ? parsed.Date : AddError(errors, f),
                };
            default:
                return System.Convert.ToString(value, CultureInfo.InvariantCulture);
        }
    }

    private static object? AddError(List<string> errors, ReportFilter f)
    {
        errors.Add($"{f.Caption}: tanggal tidak valid");
        return null;
    }

    [GeneratedRegex(@"^[\p{L}\p{N}_ .\-#/]+$")]
    private static partial Regex SafeName();
}
