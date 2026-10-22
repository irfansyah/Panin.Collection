using System.Data;
using System.Globalization;
using System.Text;

namespace Panin.Application.Reports.Pivot;

/// <summary>Jenis kolom sumber yang menentukan cara agregasi: tanggal boleh dikelompokkan, angka boleh dijumlah.</summary>
public sealed record PivotColumnTypes(IReadOnlyList<bool> RowIsDate, IReadOnlyList<bool> ColumnIsDate, IReadOnlyList<bool> MeasureIsNumeric);

/// <summary>
/// Pivot dari view: agregasi dilakukan di SQL (<c>GROUP BY</c> semua field baris/kolom), bukan dengan membaca seluruh baris mentah seperti
/// pivot lama. Hasilnya kelompok halus yang kemudian disusun <see cref="PivotBuilder"/>.
/// </summary>
public static class PivotQuery
{
    /// <summary>Batas kelompok halus yang dibaca; di atasnya hasil dipotong dan ditandai.</summary>
    public const int MaxGroups = 200_000;

    /// <summary><c>SELECT TOP 0</c> kolom-kolom pivot untuk mengetahui tipenya.</summary>
    public static string ProbeSql(ReportDefinition report)
    {
        var layout = report.Pivot!;
        var sb = new StringBuilder("SELECT TOP 0 ");
        var first = true;
        foreach (var f in layout.Rows.Concat(layout.Columns).Concat(layout.Measures.Select(m => new PivotField(m.Key, m.Caption))))
        {
            sb.Append(first ? string.Empty : ", ").Append(ReportSqlBuilder.Quote(report.View)).Append('.').Append(ReportSqlBuilder.Quote(f.Key));
            first = false;
        }

        return sb.Append(" FROM ").Append(ReportSqlBuilder.Quote(report.View)).ToString();
    }

    public static PivotColumnTypes TypesFromProbe(ReportDefinition report, DataTable probe)
    {
        var layout = report.Pivot!;
        var index = 0;
        bool[] Take(int count, Func<Type, bool> test)
        {
            var result = new bool[count];
            for (var i = 0; i < count; i++)
            {
                result[i] = test(probe.Columns[index++].DataType);
            }

            return result;
        }

        return new PivotColumnTypes(
            Take(layout.Rows.Count, IsDate),
            Take(layout.Columns.Count, IsDate),
            Take(layout.Measures.Count, IsNumeric));
    }

    public static string Select(ReportDefinition report, PivotColumnTypes types, int maxGroups = MaxGroups)
    {
        var layout = report.Pivot!;
        var view = ReportSqlBuilder.Quote(report.View);
        var dims = new List<string>();
        for (var i = 0; i < layout.Rows.Count; i++)
        {
            dims.Add(Dim(view, layout.Rows[i], types.RowIsDate[i]));
        }

        for (var i = 0; i < layout.Columns.Count; i++)
        {
            dims.Add(Dim(view, layout.Columns[i], types.ColumnIsDate[i]));
        }

        var sb = new StringBuilder("SELECT TOP (").Append((maxGroups + 1).ToString(CultureInfo.InvariantCulture)).Append(") ");
        sb.Append(string.Join(", ", dims.Select((d, i) => $"{d} AS d{i.ToString(CultureInfo.InvariantCulture)}")));
        for (var i = 0; i < layout.Measures.Count; i++)
        {
            var col = view + "." + ReportSqlBuilder.Quote(layout.Measures[i].Key);
            var n = i.ToString(CultureInfo.InvariantCulture);
            sb.Append(dims.Count > 0 || i > 0 ? ", " : string.Empty);
            if (types.MeasureIsNumeric[i])
            {
                sb.Append($"SUM(CAST({col} AS float)) AS s{n}, COUNT({col}) AS n{n}, MIN(CAST({col} AS float)) AS mn{n}, MAX(CAST({col} AS float)) AS mx{n}");
            }
            else
            {
                sb.Append($"CAST(0 AS float) AS s{n}, COUNT({col}) AS n{n}, CAST(0 AS float) AS mn{n}, CAST(0 AS float) AS mx{n}");
            }
        }

        sb.Append(" FROM ").Append(view).Append(" WHERE 1=1").Append(ReportSqlBuilder.Where(report));
        if (dims.Count > 0)
        {
            sb.Append(" GROUP BY ").Append(string.Join(", ", dims));
        }

        return sb.ToString();
    }

    /// <summary>Membaca hasil <see cref="Select"/> menjadi kelompok halus.</summary>
    public static (IReadOnlyList<PivotRecord> Records, bool Truncated) Read(PivotLayout layout, DataTable table, int maxGroups = MaxGroups)
    {
        var rows = layout.Rows.Count;
        var cols = layout.Columns.Count;
        var dims = rows + cols;
        var truncated = table.Rows.Count > maxGroups;
        var records = new List<PivotRecord>(Math.Min(table.Rows.Count, maxGroups));
        foreach (var row in table.Rows.Cast<DataRow>().Take(maxGroups))
        {
            var measures = new PivotPartial[layout.Measures.Count];
            for (var i = 0; i < measures.Length; i++)
            {
                var at = dims + (i * 4);
                var count = row[at + 1] is DBNull ? 0 : Convert.ToInt64(row[at + 1], CultureInfo.InvariantCulture);
                if (count > 0)
                {
                    measures[i] = new PivotPartial
                    {
                        Sum = Number(row[at]),
                        Count = count,
                        Min = Number(row[at + 2]),
                        Max = Number(row[at + 3]),
                    };
                }
            }

            records.Add(new PivotRecord(
                [.. Enumerable.Range(0, rows).Select(i => Clean(row[i]))],
                [.. Enumerable.Range(rows, cols).Select(i => Clean(row[i]))],
                measures));
        }

        return (records, truncated);
    }

    /// <summary>Pivot dari data yang sudah ada di memori (hasil SP): mengelompokkan baris mentah ke kelompok halus.</summary>
    public static IReadOnlyList<PivotRecord> FromRows(PivotLayout layout, DataTable table)
    {
        var groups = new Dictionary<string, PivotRecord>();
        foreach (DataRow row in table.Rows)
        {
            var rowValues = layout.Rows.Select(f => Interval(Value(row, f.Key), f.Interval)).ToArray();
            var colValues = layout.Columns.Select(f => Interval(Value(row, f.Key), f.Interval)).ToArray();
            var key = string.Join('\u001e', rowValues.Concat(colValues).Select(v => PivotBuilder.Text(v, PivotInterval.None)));
            if (!groups.TryGetValue(key, out var rec))
            {
                rec = new PivotRecord(rowValues, colValues, new PivotPartial[layout.Measures.Count]);
                groups[key] = rec;
            }

            for (var i = 0; i < layout.Measures.Count; i++)
            {
                var v = Value(row, layout.Measures[i].Key);
                if (v is null)
                {
                    continue;
                }

                rec.Measures[i].Add(v is IConvertible and not string and not DateTime and not bool ? Convert.ToDouble(v, CultureInfo.InvariantCulture)
                    : v is string s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0);
            }
        }

        return [.. groups.Values];
    }

    public static object? Interval(object? value, PivotInterval interval) => value is DateTime d
        ? interval switch
        {
            PivotInterval.Year => d.Year,
            PivotInterval.Month => d.Month,
            PivotInterval.Day => d.Day,
            PivotInterval.Date => d.Date,
            _ => value,
        }
        : value;

    private static string Dim(string view, PivotField f, bool isDate)
    {
        var col = view + "." + ReportSqlBuilder.Quote(f.Key);
        return !isDate ? col : f.Interval switch
        {
            PivotInterval.Year => $"YEAR({col})",
            PivotInterval.Month => $"MONTH({col})",
            PivotInterval.Day => $"DAY({col})",
            PivotInterval.Date => $"CAST({col} AS date)",
            _ => col,
        };
    }

    private static object? Value(DataRow row, string column)
        => row.Table.Columns.Contains(column) && row[column] is not DBNull ? row[column] : null;

    private static object? Clean(object value) => value is DBNull ? null : value is string s ? s.Trim() : value;

    private static double Number(object value) => value is DBNull ? 0 : Convert.ToDouble(value, CultureInfo.InvariantCulture);

    private static bool IsDate(Type t) => t == typeof(DateTime) || t == typeof(DateTimeOffset);

    private static bool IsNumeric(Type t) => t == typeof(decimal) || t == typeof(double) || t == typeof(float) || t == typeof(int) || t == typeof(long) || t == typeof(short) || t == typeof(byte);
}
