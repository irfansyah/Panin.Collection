using System.Globalization;

namespace Panin.Application.Reports.Pivot;

/// <summary>
/// Menyusun tabel pivot dari kelompok halus (<see cref="PivotRecord"/>). Agregat dihitung dari agregat parsial, sehingga subtotal, total,
/// rata-rata, dan persentase tepat di semua level tanpa mengulang data mentah. Penggantian atas pivot DevExpress yang dipakai layar lama
/// (<c>ASPxPivotGrid</c> dan <c>XRPivotGrid</c>): field baris/kolom bertingkat, beberapa ukuran, subtotal per level, total keseluruhan,
/// dan pengurutan menurut ukuran bantu.
/// </summary>
public static class PivotBuilder
{
    private static readonly CultureInfo Id = CultureInfo.GetCultureInfo("id-ID");
    private const char Sep = '\u001f';

    public static PivotResult Build(PivotLayout layout, IEnumerable<PivotRecord> records, int maxCells = 400_000)
    {
        var r = layout.Rows.Count;
        var c = layout.Columns.Count;
        var m = layout.Measures.Count;
        var rowRoot = new Node(null, string.Empty, string.Empty, 0);
        var colRoot = new Node(null, string.Empty, string.Empty, 0);
        var acc = new Dictionary<(string Row, string Col), PivotPartial[]>();

        foreach (var rec in records)
        {
            var rowKeys = Prefixes(layout.Rows, rec.Rows, rowRoot, out var rowTexts);
            var colKeys = Prefixes(layout.Columns, rec.Columns, colRoot, out var colTexts);
            _ = rowTexts;
            _ = colTexts;
            foreach (var rk in rowKeys)
            {
                foreach (var ck in colKeys)
                {
                    if (!acc.TryGetValue((rk, ck), out var cell))
                    {
                        cell = new PivotPartial[m];
                        acc[(rk, ck)] = cell;
                    }

                    for (var i = 0; i < m; i++)
                    {
                        cell[i].Merge(rec.Measures[i]);
                    }
                }
            }
        }

        var measureIndex = layout.Measures.Select((x, i) => (x, i)).Where(t => t.x.Visible).Select(t => t.i).ToArray();
        SortChildren(rowRoot, layout.Rows, layout.Measures, acc, byRow: true);
        SortChildren(colRoot, layout.Columns, layout.Measures, acc, byRow: false);

        var rowLines = new List<(PivotRowLine Line, string Key)>();
        EmitRows(rowRoot, layout, rowLines, r);
        if (r > 0 && layout.RowGrandTotal)
        {
            rowLines.Add((new PivotRowLine([], PivotLineKind.GrandTotal), string.Empty));
        }
        else if (r == 0)
        {
            rowLines.Add((new PivotRowLine([], PivotLineKind.Leaf), string.Empty));
        }

        var colLines = new List<(PivotColumnLine Line, string Key)>();
        EmitColumns(colRoot, layout, colLines, c, measureIndex);
        if (c > 0 && layout.ColumnGrandTotal)
        {
            foreach (var mi in Enumerable.Range(0, measureIndex.Length).Where(k => layout.Measures[measureIndex[k]].ShowGrandTotal))
            {
                colLines.Add((new PivotColumnLine([], PivotLineKind.GrandTotal, mi), string.Empty));
            }
        }
        else if (c == 0)
        {
            for (var mi = 0; mi < measureIndex.Length; mi++)
            {
                colLines.Add((new PivotColumnLine([], PivotLineKind.Leaf, mi), string.Empty));
            }
        }

        var truncated = false;
        if (colLines.Count > 0 && (long)rowLines.Count * colLines.Count > maxCells)
        {
            rowLines = [.. rowLines.Take(Math.Max(1, maxCells / colLines.Count))];
            truncated = true;
        }

        var cells = new double?[rowLines.Count][];
        for (var i = 0; i < rowLines.Count; i++)
        {
            cells[i] = new double?[colLines.Count];
            for (var j = 0; j < colLines.Count; j++)
            {
                cells[i][j] = Value(layout, acc, measureIndex, rowLines[i], colLines[j]);
            }
        }

        return new PivotResult
        {
            RowCaptions = [.. layout.Rows.Select(f => f.Caption)],
            ColumnCaptions = [.. layout.Columns.Select(f => f.Caption)],
            VisibleMeasures = [.. measureIndex.Select(i => layout.Measures[i])],
            Rows = [.. rowLines.Select(x => x.Line)],
            Columns = [.. colLines.Select(x => x.Line)],
            Cells = cells,
            Truncated = truncated,
        };
    }

    /// <summary>Teks tampilan nilai field menurut interval (nama bulan untuk <see cref="PivotInterval.Month"/>).</summary>
    public static string Text(object? value, PivotInterval interval, string? format = null)
    {
        if (value is null or DBNull)
        {
            return "(kosong)";
        }

        if (!string.IsNullOrEmpty(format) && interval == PivotInterval.None && value is IFormattable formattable)
        {
            return formattable.ToString(format, Id);
        }

        return value switch
        {
            DateTime d when interval == PivotInterval.Month => d.ToString("MMMM", Id),
            DateTime d when interval == PivotInterval.Year => d.Year.ToString(CultureInfo.InvariantCulture),
            DateTime d => d.TimeOfDay == TimeSpan.Zero ? d.ToString("dd-MMM-yyyy", Id) : d.ToString("dd-MMM-yyyy HH:mm", Id),
            int month when interval == PivotInterval.Month && month is >= 1 and <= 12 => Id.DateTimeFormat.GetMonthName(month),
            string s => s.Trim(),
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
        };
    }

    private static string[] Prefixes(IReadOnlyList<PivotField> fields, object?[] values, Node root, out string[] texts)
    {
        var keys = new string[fields.Count + 1];
        texts = new string[fields.Count];
        keys[0] = string.Empty;
        var node = root;
        for (var i = 0; i < fields.Count; i++)
        {
            var value = i < values.Length ? values[i] : null;
            var text = Text(value, fields[i].Interval, fields[i].Format);
            texts[i] = text;
            keys[i + 1] = keys[i] + Sep + text;
            node = node.Child(text, keys[i + 1], value);
        }

        return keys;
    }

    private static void SortChildren(Node node, IReadOnlyList<PivotField> fields, IReadOnlyList<PivotMeasure> measures, Dictionary<(string Row, string Col), PivotPartial[]> acc, bool byRow)
    {
        if (node.Children.Count == 0)
        {
            return;
        }

        var field = fields[node.Level];
        var sortMeasure = field.SortByMeasure is null ? -1 : IndexOf(measures, field.SortByMeasure);
        if (sortMeasure >= 0)
        {
            node.Children.Sort((a, b) => Order(a).CompareTo(Order(b)));
        }
        else
        {
            node.Children.Sort((a, b) => Compare(a.Value, b.Value));
        }

        foreach (var child in node.Children)
        {
            SortChildren(child, fields, measures, acc, byRow);
        }

        double Order(Node n)
        {
            var key = byRow ? (n.Key, string.Empty) : (string.Empty, n.Key);
            return acc.TryGetValue(key, out var p) && p[sortMeasure].Count > 0 ? p[sortMeasure].Sum : double.MaxValue;
        }
    }

    private static int IndexOf(IReadOnlyList<PivotMeasure> measures, string key)
    {
        for (var i = 0; i < measures.Count; i++)
        {
            if (string.Equals(measures[i].Key, key, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private static void EmitRows(Node parent, PivotLayout layout, List<(PivotRowLine, string)> lines, int depth)
    {
        foreach (var child in parent.Children)
        {
            var path = child.Path();
            if (child.Level == depth)
            {
                lines.Add((new PivotRowLine(path, PivotLineKind.Leaf), child.Key));
                continue;
            }

            EmitRows(child, layout, lines, depth);
            if (layout.RowTotals && layout.Rows[child.Level - 1].ShowTotals && (child.Children.Count > 1 || layout.TotalsForSingleValues))
            {
                lines.Add((new PivotRowLine(path, PivotLineKind.Subtotal), child.Key));
            }
        }
    }

    private static void EmitColumns(Node parent, PivotLayout layout, List<(PivotColumnLine, string)> lines, int depth, int[] visible)
    {
        foreach (var child in parent.Children)
        {
            var path = child.Path();
            if (child.Level == depth)
            {
                for (var mi = 0; mi < visible.Length; mi++)
                {
                    lines.Add((new PivotColumnLine(path, PivotLineKind.Leaf, mi), child.Key));
                }

                continue;
            }

            EmitColumns(child, layout, lines, depth, visible);
            if (layout.ColumnTotals && layout.Columns[child.Level - 1].ShowTotals && (child.Children.Count > 1 || layout.TotalsForSingleValues))
            {
                for (var mi = 0; mi < visible.Length; mi++)
                {
                    lines.Add((new PivotColumnLine(path, PivotLineKind.Subtotal, mi), child.Key));
                }
            }
        }
    }

    private static double? Value(PivotLayout layout, Dictionary<(string Row, string Col), PivotPartial[]> acc, int[] visible, (PivotRowLine Line, string Key) row, (PivotColumnLine Line, string Key) col)
    {
        var mIndex = visible[col.Line.MeasureIndex];
        var measure = layout.Measures[mIndex];
        if ((row.Line.Kind == PivotLineKind.GrandTotal || col.Line.Kind == PivotLineKind.GrandTotal) && !measure.ShowGrandTotal)
        {
            return null;
        }

        if (!acc.TryGetValue((row.Key, col.Key), out var cell))
        {
            return null;
        }

        var p = cell[mIndex];
        if (p.Count == 0)
        {
            return null;
        }

        switch (measure.Aggregate)
        {
            case PivotAggregate.Sum:
                return p.Sum;
            case PivotAggregate.Count:
                return p.Count;
            case PivotAggregate.Average:
                return p.Sum / p.Count;
            case PivotAggregate.Min:
                return p.Min;
            case PivotAggregate.Max:
                return p.Max;
            case PivotAggregate.PercentOfColumn:
                return Percent(p.Sum, acc.TryGetValue((string.Empty, col.Key), out var colTotal) ? colTotal[mIndex].Sum : 0);
            default:
                return Percent(p.Sum, acc.TryGetValue((row.Key, string.Empty), out var rowTotal) ? rowTotal[mIndex].Sum : 0);
        }

        static double? Percent(double part, double whole) => whole == 0 ? null : part / whole * 100;
    }

    private static int Compare(object? a, object? b)
    {
        if (a is null or DBNull)
        {
            return b is null or DBNull ? 0 : -1;
        }

        if (b is null or DBNull)
        {
            return 1;
        }

        if (a.GetType() == b.GetType() && a is IComparable ca && a is not string)
        {
            return ca.CompareTo(b);
        }

        if (IsNumber(a) && IsNumber(b))
        {
            return Convert.ToDecimal(a, CultureInfo.InvariantCulture).CompareTo(Convert.ToDecimal(b, CultureInfo.InvariantCulture));
        }

        return string.Compare(Convert.ToString(a, CultureInfo.InvariantCulture), Convert.ToString(b, CultureInfo.InvariantCulture), StringComparison.InvariantCultureIgnoreCase);
    }

    private static bool IsNumber(object o) => o is sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal;

    private sealed class Node(Node? parent, string text, string key, int level)
    {
        private readonly Dictionary<string, Node> _map = [];

        public Node? Parent { get; } = parent;

        public string Text { get; } = text;

        public string Key { get; } = key;

        public int Level { get; } = level;

        public object? Value { get; private set; }

        public List<Node> Children { get; } = [];

        public Node Child(string childText, string childKey, object? value)
        {
            if (!_map.TryGetValue(childKey, out var node))
            {
                node = new Node(this, childText, childKey, Level + 1) { Value = value };
                _map[childKey] = node;
                Children.Add(node);
            }

            return node;
        }

        public string[] Path()
        {
            var parts = new List<string>();
            for (var n = this; n.Parent is not null; n = n.Parent)
            {
                parts.Add(n.Text);
            }

            parts.Reverse();
            return [.. parts];
        }
    }
}
