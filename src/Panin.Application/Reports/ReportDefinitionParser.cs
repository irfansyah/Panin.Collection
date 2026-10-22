using System.Globalization;
using System.Text.RegularExpressions;
using Panin.Application.Reports.Pivot;

namespace Panin.Application.Reports;

/// <summary>Hasil membaca satu baris <c>PIVOTLIST</c>: definisi yang bisa dijalankan, atau alasan belum tersedia.</summary>
public sealed record ReportParseResult(ReportDefinition? Definition, ReportKind Kind, string? UnavailableReason);

/// <summary>
/// Membaca definisi report daftar dari kolom <c>PIVOTLIST</c>. Format entri (dipisah koma; <c>,,</c> = koma literal):
/// <c>@[VIEW|KOLOM]:Judul[:ringkasan]</c> untuk kolom dan <c>@[VIEW|KOLOM]:Judul[:tipe[:SQL pilihan]]</c> untuk filter.
/// Hanya report dengan satu view sumber, tanpa kondisi kustom, dan tanpa SQL pilihan berbahaya yang dinyatakan tersedia.
/// </summary>
public static partial class ReportDefinitionParser
{
    public static ReportParseResult Parse(string id, string title, string? columnList, string? rowList, string? dataList, string? filterList, string? cond, string? assemblyObj, DateOnly today, string? setting = null, ICustomPivotCatalog? custom = null)
    {
        CustomPivotSpec? spec = null;
        CustomListSpec? listSpec = null;
        if (!string.IsNullOrWhiteSpace(assemblyObj))
        {
            spec = custom?.Find(assemblyObj.Trim());
            listSpec = spec is null ? CustomLists.Find(assemblyObj) : null;
            if (spec is null && listSpec is null)
            {
                return new(null, ReportKind.Custom, "Laporan kustom ini belum dimigrasi");
            }
        }

        var isPivot = spec is not null || (listSpec is null && !string.IsNullOrWhiteSpace(rowList) && !string.IsNullOrWhiteSpace(dataList));
        string? view = null;
        PivotLayout? pivot = spec?.Layout;
        var columns = listSpec is null ? new List<ReportColumn>() : [.. listSpec.Columns];
        if (isPivot && spec is null)
        {
            var parsedPivot = ParsePivot(columnList, rowList!, dataList!, setting, ref view, out var pivotError);
            if (parsedPivot is null)
            {
                return new(null, ReportKind.Pivot, pivotError);
            }

            pivot = parsedPivot;
        }

        foreach (var entry in isPivot || listSpec is not null ? Enumerable.Empty<string>() : Entries(columnList))
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

        if (spec is null && listSpec is null && (view is null || (!isPivot && columns.Count == 0)))
        {
            return Unavailable("Tidak ada kolom");
        }

        var filters = new List<ReportFilter>();
        var notes = new List<string>();
        var seq = 1;
        foreach (var entry in Entries(filterList))
        {
            if (!TryField(entry, out var v, out var f, out var rest, out var error))
            {
                return Unavailable(error);
            }

            var parts = rest.Split(':', 3);
            var caption = parts.Length > 0 && parts[0].Trim().Length > 0 ? parts[0].Trim() : f;
            if (view is not null && spec is null && listSpec is null && !string.Equals(view, v, StringComparison.OrdinalIgnoreCase))
            {
                // Layar lama menggabungkan view lain lewat tabel relasi FRAMEWORK_FOREIGNKEY (kosong di salinan lokal); tanpa relasi itu
                // hasilnya perkalian silang. Filter dari view lain tidak diterapkan dan dicatat agar terlihat di layar.
                notes.Add($"Filter {caption} (dari {v}) tidak diterapkan: relasi antar-view tidak terdefinisi");
                continue;
            }

            if (!SameView(ref view, v))
            {
                return Unavailable("Filter berasal dari view yang berbeda dari kolom");
            }

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

        ReportLatestRow? latest = null;
        if (!string.IsNullOrWhiteSpace(cond))
        {
            latest = ParseLatest(cond, view!);
            if (latest is null)
            {
                return Unavailable("Memakai kondisi kustom (PV_COND) yang belum dikenali");
            }
        }

        var definition = new ReportDefinition(id, title, view ?? spec?.Procedure ?? listSpec!.Procedure, columns, filters, pivot, spec, latest, notes.Count == 0 ? null : notes, listSpec);
        if (listSpec is not null && definition.ParamCount != listSpec.ParamCount)
        {
            return Unavailable($"SP {listSpec.Procedure} menerima {listSpec.ParamCount} parameter, filter report menghasilkan {definition.ParamCount}");
        }

        if (latest is not null && latest.Match.Any(m => m.Param < 1 || m.Param > definition.ParamCount))
        {
            return Unavailable("PV_COND merujuk parameter filter yang tidak ada");
        }

        if (spec is not null && definition.ParamCount != spec.ParamCount)
        {
            return Unavailable($"SP {spec.Procedure} menerima {spec.ParamCount} parameter, filter report menghasilkan {definition.ParamCount}");
        }

        foreach (var filter in filters)
        {
            if (filter.OptionDependsOnInvalid(definition.ParamCount))
            {
                return Unavailable($"Filter {filter.Caption} merujuk parameter yang tidak ada");
            }
        }

        return new(definition, isPivot ? ReportKind.Pivot : ReportKind.List, null);

        ReportParseResult Unavailable(string reason) => new(null, isPivot ? ReportKind.Pivot : ReportKind.List, reason);
    }

    /// <summary>Mengenali pola PV_COND "baris terbaru per kunci"; selain itu <c>null</c>.</summary>
    public static ReportLatestRow? ParseLatest(string cond, string view)
    {
        var m = LatestPattern().Match(cond);
        if (!m.Success)
        {
            return null;
        }

        string D(string g) => m.Groups[g].Value;
        bool Same(params string[] names) => names.All(n => string.Equals(D(n), D(names[0]), StringComparison.OrdinalIgnoreCase));
        if (!Same("d", "d2", "d3") || !Same("k", "k2") || !Same("v", "v2") || !string.Equals(D("v"), view, StringComparison.OrdinalIgnoreCase)
            || !ReportSqlBuilder.IsSafeIdentifier(D("d")) || !ReportSqlBuilder.IsSafeIdentifier(D("k")))
        {
            return null;
        }

        var match = new List<ReportLatestMatch>();
        foreach (var part in AndSplit().Split(D("conds")))
        {
            var c = MatchCondition().Match(part.Trim());
            if (!c.Success || !ReportSqlBuilder.IsSafeIdentifier(c.Groups[1].Value))
            {
                return null;
            }

            match.Add(new ReportLatestMatch(c.Groups[1].Value, int.Parse(c.Groups[2].Value, CultureInfo.InvariantCulture)));
        }

        return new ReportLatestRow(D("d"), D("k"), match);
    }

    [GeneratedRegex(@"^\s*AND\s+\[(?<d>[^\]]+)\]\s+IN\s*\(\s*SELECT\s+MAX\(\[(?<d2>[^\]]+)\]\)\s+FROM\s+(?<v>\w+)\s+A\s+WHERE\s+A\.(?<k>\w+)\s*=\s*\[(?<v2>\w+)\]\.\[(?<k2>\w+)\]\s+AND\s*\((?<conds>[^()]*)\)\s*\)\s+OR\s+\[(?<d3>[^\]]+)\]\s+IS\s+NULL\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LatestPattern();

    [GeneratedRegex(@"\s+AND\s+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AndSplit();

    [GeneratedRegex(@"^A\.(\w+)\s*=\s*@(\d+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MatchCondition();

    // Susunan pivot dinamis: PV_COLUMNLIST = field kolom, PV_ROWLIST = field baris, PV_DATALIST = ukuran.
    // Atribut field: Judul:interval (baris/kolom: year|month|day|date); ukuran: Judul:agregat:tampilkanTotal (agregat: sum|avg|count|min|max|col%|row%).
    private static PivotLayout? ParsePivot(string? columnList, string rowList, string dataList, string? setting, ref string? view, out string error)
    {
        error = string.Empty;
        var rows = new List<PivotField>();
        var cols = new List<PivotField>();
        var measures = new List<PivotMeasure>();
        foreach (var (list, target) in new[] { (rowList, rows), (columnList, cols) })
        {
            foreach (var entry in Entries(list))
            {
                if (!TryField(entry, out var v, out var f, out var rest, out error))
                {
                    return null;
                }

                if (!SameView(ref view, v))
                {
                    error = "Field pivot berasal dari lebih dari satu view";
                    return null;
                }

                var parts = rest.Split(':', 3);
                var caption = parts.Length > 0 && parts[0].Trim().Length > 0 ? parts[0].Trim() : f;
                target.Add(new PivotField(f, caption, parts.Length > 1 ? ParseInterval(parts[1]) : PivotInterval.None));
            }
        }

        foreach (var entry in Entries(dataList))
        {
            if (!TryField(entry, out var v, out var f, out var rest, out error))
            {
                return null;
            }

            if (!SameView(ref view, v))
            {
                error = "Field pivot berasal dari lebih dari satu view";
                return null;
            }

            var parts = rest.Split(':', 4);
            var caption = parts.Length > 0 && parts[0].Trim().Length > 0 ? parts[0].Trim() : f;
            var aggregate = parts.Length > 1 ? ParseAggregate(parts[1]) : PivotAggregate.Sum;
            var grandTotal = parts.Length <= 2 || !string.Equals(parts[2].Trim(), "false", StringComparison.OrdinalIgnoreCase);
            measures.Add(new PivotMeasure(f, caption, aggregate, ShowGrandTotal: grandTotal));
        }

        if (measures.Count == 0 || (rows.Count == 0 && cols.Count == 0))
        {
            error = "Pivot tanpa ukuran atau tanpa field baris/kolom";
            return null;
        }

        // PV_SETTING "a:b": a = tampilkan subtotal baris (ShowRowTotals), b = boleh seret (diabaikan).
        var showTotals = true;
        var settingParts = (setting ?? string.Empty).Split(':');
        if (settingParts.Length >= 2 && bool.TryParse(settingParts[0], out var parsed))
        {
            showTotals = parsed;
        }

        return new PivotLayout(rows, cols, measures, RowTotals: showTotals);
    }

    private static PivotInterval ParseInterval(string text) => text.Trim().ToLowerInvariant() switch
    {
        "year" => PivotInterval.Year,
        "month" => PivotInterval.Month,
        "day" => PivotInterval.Day,
        "date" => PivotInterval.Date,
        _ => PivotInterval.None,
    };

    private static PivotAggregate ParseAggregate(string text) => text.Trim().ToLowerInvariant() switch
    {
        "avg" => PivotAggregate.Average,
        "count" => PivotAggregate.Count,
        "min" => PivotAggregate.Min,
        "max" => PivotAggregate.Max,
        "col%" => PivotAggregate.PercentOfColumn,
        "row%" => PivotAggregate.PercentOfRow,
        _ => PivotAggregate.Sum,
    };

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
