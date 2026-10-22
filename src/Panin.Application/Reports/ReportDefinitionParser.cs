using System.Globalization;

namespace Panin.Application.Reports;

/// <summary>Hasil membaca satu baris <c>PIVOTLIST</c>: definisi yang bisa dijalankan, atau alasan belum tersedia.</summary>
public sealed record ReportParseResult(ReportDefinition? Definition, ReportKind Kind, string? UnavailableReason);

/// <summary>
/// Membaca definisi report daftar dari kolom <c>PIVOTLIST</c>. Format entri (dipisah koma; <c>,,</c> = koma literal):
/// <c>@[VIEW|KOLOM]:Judul[:ringkasan]</c> untuk kolom dan <c>@[VIEW|KOLOM]:Judul[:tipe[:SQL pilihan]]</c> untuk filter.
/// Hanya report dengan satu view sumber, tanpa kondisi kustom, dan tanpa SQL pilihan berbahaya yang dinyatakan tersedia.
/// </summary>
public static class ReportDefinitionParser
{
    public static ReportParseResult Parse(string id, string title, string? columnList, string? rowList, string? dataList, string? filterList, string? cond, string? assemblyObj, DateOnly today)
    {
        if (!string.IsNullOrWhiteSpace(assemblyObj))
        {
            return new(null, ReportKind.Custom, "Laporan kustom (pivot dari SP) belum dimigrasi");
        }

        if (!string.IsNullOrWhiteSpace(rowList) && !string.IsNullOrWhiteSpace(dataList))
        {
            return new(null, ReportKind.Pivot, "Laporan pivot belum dimigrasi");
        }

        if (!string.IsNullOrWhiteSpace(cond))
        {
            return new(null, ReportKind.List, "Memakai kondisi kustom (PV_COND) yang belum didukung");
        }

        var columns = new List<ReportColumn>();
        string? view = null;
        foreach (var entry in Entries(columnList))
        {
            if (!TryField(entry, out var v, out var f, out var rest, out var error))
            {
                return Unavailable(error);
            }

            if (!SameView(ref view, v))
            {
                return Unavailable("Kolom berasal dari lebih dari satu view");
            }

            var parts = rest.Split(':', 3);
            var caption = parts.Length > 0 && parts[0].Trim().Length > 0 ? parts[0].Trim() : f;
            columns.Add(new ReportColumn(f, caption, parts.Length > 1 ? ParseSummary(parts[1]) : ReportSummary.None));
        }

        if (columns.Count == 0 || view is null)
        {
            return Unavailable("Tidak ada kolom");
        }

        var filters = new List<ReportFilter>();
        var seq = 1;
        foreach (var entry in Entries(filterList))
        {
            if (!TryField(entry, out var v, out var f, out var rest, out var error))
            {
                return Unavailable(error);
            }

            if (!SameView(ref view, v))
            {
                return Unavailable("Filter berasal dari view yang berbeda dari kolom");
            }

            var parts = rest.Split(':', 3);
            var caption = parts.Length > 0 && parts[0].Trim().Length > 0 ? parts[0].Trim() : f;
            var typeText = parts.Length > 1 ? parts[1].Trim() : string.Empty;
            var type = ParseType(typeText);
            string? optionsSql = null;
            if (type == ReportFilterType.List)
            {
                optionsSql = parts.Length > 2 ? parts[2].Trim() : null;
                if (ReportOptionSql.Validate(optionsSql ?? string.Empty) is { } reason)
                {
                    return Unavailable($"Filter {caption}: {reason}");
                }
            }

            var (from, to) = Defaults(typeText, type, today);
            var filter = new ReportFilter(seq, f, caption, type, optionsSql, from, to);
            filters.Add(filter);
            seq += filter.ParamCount;
        }

        var definition = new ReportDefinition(id, title, view, columns, filters);
        foreach (var filter in filters)
        {
            if (filter.OptionDependsOnInvalid(definition.ParamCount))
            {
                return Unavailable($"Filter {filter.Caption} merujuk parameter yang tidak ada");
            }
        }

        return new(definition, ReportKind.List, null);

        static ReportParseResult Unavailable(string reason) => new(null, ReportKind.List, reason);
    }

    /// <summary>Memecah daftar entri: koma memisahkan, <c>,,</c> adalah koma literal di dalam entri.</summary>
    public static IEnumerable<string> Entries(string? list)
    {
        if (string.IsNullOrWhiteSpace(list))
        {
            yield break;
        }

        const string Sentinel = "\u0001";
        foreach (var raw in list.Replace(",,", Sentinel, StringComparison.Ordinal).Split(','))
        {
            var entry = raw.Replace(Sentinel, ",", StringComparison.Ordinal).Trim();
            if (entry.Length > 0)
            {
                yield return entry;
            }
        }
    }

    public static ReportFilterType ParseType(string type)
    {
        var t = type.Trim().ToLowerInvariant();
        if (t.StartsWith('d'))
        {
            return t.Length > 1
                ? t[1] switch
                {
                    'r' => ReportFilterType.DateRange,
                    'f' => ReportFilterType.DateFrom,
                    't' => ReportFilterType.DateTo,
                    _ => ReportFilterType.Date,
                }
                : ReportFilterType.Date;
        }

        return t switch
        {
            "l" => ReportFilterType.List,
            "i" => ReportFilterType.Number,
            "ir" => ReportFilterType.NumberRange,
            "m" => ReportFilterType.MonthYear,
            "mr" => ReportFilterType.MonthYearRange,
            _ => ReportFilterType.Text,
        };
    }

    private static ReportSummary ParseSummary(string text)
    {
        var s = text.ToLowerInvariant();
        return s.Contains("sum", StringComparison.Ordinal) ? ReportSummary.Sum
            : s.Contains("avg", StringComparison.Ordinal) ? ReportSummary.Average
            : s.Contains("min", StringComparison.Ordinal) ? ReportSummary.Min
            : s.Contains("max", StringComparison.Ordinal) ? ReportSummary.Max
            : s.Contains("count", StringComparison.Ordinal) ? ReportSummary.Count
            : ReportSummary.None;
    }

    // Nilai awal tanggal: penanda id<N> (hari ini +N), im (bulan ini), iy (tahun ini), seperti layar lama.
    private static (DateOnly? From, DateOnly? To) Defaults(string typeText, ReportFilterType type, DateOnly today)
    {
        if (type is not (ReportFilterType.Date or ReportFilterType.DateFrom or ReportFilterType.DateTo or ReportFilterType.DateRange))
        {
            return (null, null);
        }

        var t = typeText.ToLowerInvariant();
        var marker = t.IndexOf("id", 1, StringComparison.Ordinal) is var a and > 0 ? a
            : t.IndexOf("im", 1, StringComparison.Ordinal) is var b and > 0 ? b
            : t.IndexOf("iy", 1, StringComparison.Ordinal);
        if (marker < 0)
        {
            return (null, null);
        }

        var tail = t[marker..];
        if (tail.StartsWith("id", StringComparison.Ordinal))
        {
            var days = double.TryParse(tail[2..], NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0;
            var day = today.AddDays((int)days);
            return (day, day);
        }

        if (tail.StartsWith("im", StringComparison.Ordinal))
        {
            var first = new DateOnly(today.Year, today.Month, 1);
            return (first, first.AddMonths(1).AddDays(-1));
        }

        return (new DateOnly(today.Year, 1, 1), new DateOnly(today.Year, 12, 31));
    }

    private static bool SameView(ref string? view, string candidate)
    {
        if (view is null)
        {
            view = candidate;
            return true;
        }

        return string.Equals(view, candidate, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryField(string entry, out string view, out string field, out string rest, out string error)
    {
        (view, field, rest, error) = (string.Empty, string.Empty, string.Empty, string.Empty);
        var end = entry.IndexOf(']', StringComparison.Ordinal);
        if (!entry.StartsWith("@[", StringComparison.Ordinal) || end < 0)
        {
            error = $"Entri tidak dikenal: {Shorten(entry)}";
            return false;
        }

        var id = entry[2..end];
        var bar = id.LastIndexOf('|');
        if (bar <= 0 || bar == id.Length - 1)
        {
            error = $"Field tanpa view: {Shorten(entry)}";
            return false;
        }

        view = id[..bar].Trim();
        field = id[(bar + 1)..].Trim();
        if (!ReportSqlBuilder.IsSafeIdentifier(view) || !ReportSqlBuilder.IsSafeIdentifier(field))
        {
            error = $"Nama view atau kolom tidak valid: {Shorten(id)}";
            return false;
        }

        rest = entry[(end + 1)..].TrimStart(':');
        return true;
    }

    private static string Shorten(string s) => s.Length <= 60 ? s : s[..60] + "...";
}
