using System.Globalization;

namespace Panin.Web.Components.Common;

/// <summary>Ringkasan kolom pada baris total dan baris kelompok.</summary>
public enum GridSummary
{
    None,
    Sum,
    Count,
    Average,
    Min,
    Max,
}

/// <summary>Satu baris tampilan: baris data atau judul kelompok.</summary>
public sealed record GridRow<TItem>(TItem? Item, int Level, string? GroupCaption, string? GroupValue, string? GroupPath, IReadOnlyList<TItem>? GroupItems, bool Collapsed)
{
    public bool IsGroup => GroupPath is not null;
}

/// <summary>Logika murni <see cref="DataGrid{TItem}"/> (filter kolom, ringkasan, kelompok) supaya dapat diuji tanpa komponen.</summary>
public static class GridEngine
{
    private static readonly CultureInfo Id = CultureInfo.GetCultureInfo("id-ID");

    /// <summary>
    /// Cocokkan filter kolom. Kolom angka mendukung operator (<c>&gt;100</c>, <c>&lt;=5</c>, <c>=0</c>, <c>&lt;&gt;3</c>); awalan <c>=</c> pada kolom teks berarti sama persis;
    /// selain itu "mengandung" tanpa membedakan huruf besar kecil.
    /// </summary>
    public static bool MatchesColumnFilter(string text, object? value, bool numeric, string filter)
    {
        filter = filter.Trim();
        if (filter.Length == 0)
        {
            return true;
        }

        if (numeric && TryOperator(filter, out var op, out var operand) && TryDecimal(operand, out var wanted))
        {
            if (!TryDecimal(value, out var actual))
            {
                return false;
            }

            return op switch
            {
                ">=" => actual >= wanted,
                "<=" => actual <= wanted,
                "<>" => actual != wanted,
                ">" => actual > wanted,
                "<" => actual < wanted,
                _ => actual == wanted,
            };
        }

        if (filter[0] == '=' && filter.Length > 1)
        {
            return string.Equals(text, filter[1..].Trim(), StringComparison.OrdinalIgnoreCase);
        }

        return text.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryOperator(string filter, out string op, out string operand)
    {
        foreach (var candidate in new[] { ">=", "<=", "<>", ">", "<", "=" })
        {
            if (filter.StartsWith(candidate, StringComparison.Ordinal))
            {
                op = candidate;
                operand = filter[candidate.Length..].Trim();
                return true;
            }
        }

        op = string.Empty;
        operand = string.Empty;
        return false;
    }

    private static bool TryDecimal(object? value, out decimal result)
    {
        switch (value)
        {
            case null:
                result = 0;
                return false;
            case decimal d:
                result = d;
                return true;
            case string s:
                return decimal.TryParse(s, NumberStyles.Number, Id, out result) || decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out result);
            case IConvertible c when value is not DateTime and not bool:
                try
                {
                    result = c.ToDecimal(CultureInfo.InvariantCulture);
                    return true;
                }
                catch (Exception ex) when (ex is FormatException or OverflowException or InvalidCastException)
                {
                    break;
                }
        }

        result = 0;
        return false;
    }

    /// <summary>Hitung ringkasan; nilai kosong atau bukan angka dilewati. Null bila tidak ada yang dapat dihitung.</summary>
    public static decimal? Summarize(GridSummary summary, IEnumerable<object?> values)
    {
        if (summary == GridSummary.None)
        {
            return null;
        }

        if (summary == GridSummary.Count)
        {
            return values.Count(v => v is not null);
        }

        var numbers = new List<decimal>();
        foreach (var v in values)
        {
            if (TryDecimal(v, out var n))
            {
                numbers.Add(n);
            }
        }

        if (numbers.Count == 0)
        {
            return null;
        }

        return summary switch
        {
            GridSummary.Sum => numbers.Sum(),
            GridSummary.Average => numbers.Average(),
            GridSummary.Min => numbers.Min(),
            GridSummary.Max => numbers.Max(),
            _ => null,
        };
    }

    public static string SummaryLabel(GridSummary summary) => summary switch
    {
        GridSummary.Sum => "Total",
        GridSummary.Count => "Jumlah",
        GridSummary.Average => "Rata-rata",
        GridSummary.Min => "Min",
        GridSummary.Max => "Maks",
        _ => string.Empty,
    };

    public static string FormatSummary(decimal? value, bool integer) => value is null
        ? string.Empty
        : value.Value.ToString(integer ? "N0" : "N2", Id);

    /// <summary>
    /// Susun baris tampilan. <paramref name="sorted"/> sudah terurut menurut kolom kelompok lebih dulu. Tanpa kolom kelompok hasilnya baris data saja.
    /// Kelompok yang ada di <paramref name="collapsed"/> (jalur kunci) tidak menampilkan isinya.
    /// </summary>
    public static IReadOnlyList<GridRow<TItem>> Flatten<TItem>(
        IReadOnlyList<TItem> sorted,
        IReadOnlyList<(string Header, Func<TItem, string> Key)> groups,
        ISet<string> collapsed)
    {
        if (groups.Count == 0)
        {
            return [.. sorted.Select(i => new GridRow<TItem>(i, 0, null, null, null, null, false))];
        }

        var rows = new List<GridRow<TItem>>();
        Walk(sorted, 0, string.Empty);
        return rows;

        void Walk(IReadOnlyList<TItem> items, int level, string parentPath)
        {
            if (level >= groups.Count)
            {
                rows.AddRange(items.Select(i => new GridRow<TItem>(i, level, null, null, null, null, false)));
                return;
            }

            var (header, key) = groups[level];
            foreach (var bucket in items.GroupBy(key, StringComparer.Ordinal))
            {
                var path = parentPath + "\u001f" + bucket.Key;
                var isCollapsed = collapsed.Contains(path);
                var list = bucket.ToList();
                rows.Add(new GridRow<TItem>(default, level, header, bucket.Key, path, list, isCollapsed));
                if (!isCollapsed)
                {
                    Walk(list, level + 1, path);
                }
            }
        }
    }
}
