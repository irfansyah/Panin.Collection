// Menjalankan semua report menu Reporting terhadap sebuah DB DATA, mengukur waktunya, dan memeriksa konsistensi hasilnya
// dengan perhitungan kedua yang independen. Dirancang untuk dijalankan pada salinan lokal, staging, atau produksi (hanya baca).
// Lihat docs/REPORTS.md ("Alat pemeriksa report").
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Panin.Application.Abstractions;
using Panin.Application.Modules;
using Panin.Application.Reports;
using Panin.Application.Reports.Pivot;
using Panin.Infrastructure.Data;
using Panin.Infrastructure.Reports;
using Panin.Infrastructure.Reports.Custom;

return await Run(args);

static async Task<int> Run(string[] args)
{
    var opt = Options.Parse(args);
    if (opt.Help || string.IsNullOrWhiteSpace(opt.Data))
    {
        Console.WriteLine("""
            Panin.ReportCheck - jalankan semua report, ukur waktu, dan periksa konsistensi hasil (hanya baca)

              --data <connection string>   DB DATA modul (atau env PANIN_CHECK_DATA, atau user-secrets ConnectionStrings:Data:<modul>)
              --module <id>                modul (default 60)
              --days <n>                   rentang tanggal bawaan untuk filter tanggal tanpa nilai awal (default 7 hari terakhir)
              --only <id,id,...>           hanya report ini (mis. PIVOT65,PIVOT86)
              --skip <id,id,...>           lewati report ini
              --max-raw <n>                batas baris mentah untuk pemeriksaan pivot independen (default 300000)
              --timeout <detik>            batas waktu per query (default 300)
              --json <berkas>              tulis hasil lengkap sebagai JSON

            Status: OK, DIFF (hasil kedua berbeda), ERROR, SKIP (pemeriksaan dilewati, alasan di kolom detail),
                    N/A (report belum tersedia di aplikasi baru). Kode keluar 0 bila tidak ada DIFF/ERROR, 1 bila ada, 2 argumen salah.
            Aman: koneksi ApplicationIntent=ReadOnly, hanya SELECT dan SP report yang definisinya diperiksa tidak menulis.
            """);
        return opt.Help ? 0 : 2;
    }

    var builder = new SqlConnectionStringBuilder(opt.Data) { ApplicationIntent = ApplicationIntent.ReadOnly };
    var catalog = new CustomPivotCatalog();
    var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var cls in catalog.Classes)
    {
        allowed.Add(catalog.Find(cls)!.Procedure);
    }

    foreach (var name in new[] { "LOSCOLLECTION.Report.Template.CallProductivity", "LOSCOLLECTION.Report.Template.CallProductivityRecovery" })
    {
        allowed.Add(CustomLists.Find(name)!.Procedure);
    }

    var timeout = new DbOptions(TimeoutSeconds: opt.Timeout);
    var inner = new SqlDb(builder.ConnectionString, "check");
    var db = new ReadOnlyDb(inner, allowed, "check");
    Console.WriteLine($"Server  : {builder.DataSource} / {builder.InitialCatalog} (hanya baca)");
    Console.WriteLine($"Modul   : {opt.Module}    rentang tanggal bawaan: {opt.Days} hari terakhir    batas data mentah: {opt.MaxRaw:N0}");

    // Pengaman SP: tolak SP yang definisinya tampak menulis ke tabel permanen.
    var unsafeProcedures = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    foreach (var proc in allowed)
    {
        var definition = await db.ExecuteScalarAsync<string?>("SELECT OBJECT_DEFINITION(OBJECT_ID(@1))", [proc]);
        if (definition is null)
        {
            unsafeProcedures[proc] = "SP tidak ditemukan di DB ini";
        }
        else if (WriteFinder.Find(definition) is { } finding)
        {
            unsafeProcedures[proc] = $"definisi tampak menulis: {finding}";
        }
    }

    var service = new ReportService(new FixedDbFactory(db), new SingleFlightCache(new MemoryCache(new MemoryCacheOptions())), catalog, NullLogger<ReportService>.Instance);
    var sets = await db.QueryTableAsync("SELECT DISTINCT SETID FROM VW_REPORTSET ORDER BY SETID", [], timeout);
    var results = new List<CheckResult>();
    var only = opt.Only.Count == 0 ? null : opt.Only.ToHashSet(StringComparer.OrdinalIgnoreCase);

    foreach (var setId in sets.Rows.Cast<DataRow>().Select(r => Convert.ToString(r[0], CultureInfo.InvariantCulture)!.Trim()))
    {
        foreach (var item in await service.ListAsync(opt.Module, setId))
        {
            if ((only is not null && !only.Contains(item.Id)) || opt.Skip.Contains(item.Id, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!item.Available)
            {
                results.Add(new(item.Id, item.Title, setId, item.Kind.ToString(), "N/A", item.UnavailableReason ?? string.Empty, 0, 0));
                continue;
            }

            var def = await service.GetAsync(opt.Module, item.Id);
            if (def is null)
            {
                results.Add(new(item.Id, item.Title, setId, item.Kind.ToString(), "ERROR", "definisi tidak terbaca", 0, 0));
                continue;
            }

            var proc = def.Custom?.Procedure ?? def.CustomList?.Procedure;
            if (proc is not null && unsafeProcedures.TryGetValue(proc, out var why))
            {
                results.Add(new(item.Id, item.Title, setId, item.Kind.ToString(), "SKIP", $"{proc}: {why}", 0, 0));
                continue;
            }

            var result = await Check(service, db, opt, def, setId, item.Kind, catalog, timeout);
            results.Add(result);
            Console.WriteLine($"{result.Status,-5} {result.Id,-10} {result.Ms,8:N0} ms {result.Rows,9:N0}  {Trunc(result.Title, 44),-44} {Trunc(result.Detail, 90)}");
        }
    }

    foreach (var r in results.Where(r => r.Status == "N/A"))
    {
        Console.WriteLine($"N/A   {r.Id,-10}                      {Trunc(r.Title, 44),-44} {Trunc(r.Detail, 90)}");
    }

    var summary = results.GroupBy(r => r.Status).OrderBy(g => g.Key).Select(g => $"{g.Key}={g.Count()}");
    Console.WriteLine($"\nRingkasan: {string.Join("  ", summary)}  (total {results.Count})");
    var slow = results.Where(r => r.Ms >= 5000).OrderByDescending(r => r.Ms).Take(10).ToList();
    if (slow.Count > 0)
    {
        Console.WriteLine("Paling lambat (>= 5 dtk): " + string.Join("; ", slow.Select(r => $"{r.Id} {r.Ms / 1000.0:N1} dtk")));
    }

    if (!string.IsNullOrWhiteSpace(opt.Json))
    {
        await File.WriteAllTextAsync(opt.Json, JsonSerializer.Serialize(new { at = DateTime.Now, server = builder.DataSource, database = builder.InitialCatalog, results }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"JSON    : {opt.Json}");
    }

    return results.Any(r => r.Status is "DIFF" or "ERROR") ? 1 : 0;
}

static async Task<CheckResult> Check(ReportService service, IDb db, Options opt, ReportDefinition def, string setId, ReportKind kind, CustomPivotCatalog catalog, DbOptions timeout)
{
    var values = DefaultValues(def, opt.Days);
    var sw = Stopwatch.StartNew();
    try
    {
        if (def.IsPivot)
        {
            var run = await service.RunPivotAsync(opt.Module, def, values);
            sw.Stop();
            var cells = run.Table.Rows.Count * run.Table.Columns.Count;
            var (status, detail) = def.Custom is not null
                ? await VerifyCustom(db, def, run, values, sw.ElapsedMilliseconds, timeout)
                : await VerifyDynamic(db, def, run, values, opt.MaxRaw, timeout);
            return new(def.Id, def.Title, setId, kind.ToString(), status, $"{run.Table.Rows.Count}x{run.Table.Columns.Count} sel{(run.GroupsTruncated ? ", TERPOTONG" : string.Empty)}; {detail}", sw.ElapsedMilliseconds, run.Table.Rows.Count);
        }

        var result = await service.RunAsync(opt.Module, def, values, forExport: false);
        sw.Stop();
        if (def.CustomList is not null || result.Truncated)
        {
            return new(def.Id, def.Title, setId, kind.ToString(), "OK", result.Truncated ? $"terpotong di {result.MaxRows:N0} baris" : "dari SP", sw.ElapsedMilliseconds, result.Rows.Count);
        }

        var count = await db.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {ReportSqlBuilder.Quote(def.View)} WHERE 1=1{ReportSqlBuilder.Where(def)}", ReportSqlBuilder.Coerce(def, values).Args, timeout);
        return count == result.Rows.Count
            ? new(def.Id, def.Title, setId, kind.ToString(), "OK", $"jumlah baris cocok dengan COUNT(*) ({count:N0})", sw.ElapsedMilliseconds, result.Rows.Count)
            : new(def.Id, def.Title, setId, kind.ToString(), "DIFF", $"baris {result.Rows.Count:N0} <> COUNT(*) {count:N0}", sw.ElapsedMilliseconds, result.Rows.Count);
    }
    catch (Exception ex)
    {
        return new(def.Id, def.Title, setId, kind.ToString(), "ERROR", ex.Message.Replace('\n', ' '), sw.ElapsedMilliseconds, 0);
    }
}

// Pivot dinamis: ambil baris mentah, kelompokkan di memori (jalur berbeda dari GROUP BY di SQL), lalu bandingkan sel demi sel.
static async Task<(string, string)> VerifyDynamic(IDb db, ReportDefinition def, PivotRunResult run, object?[] values, int maxRaw, DbOptions timeout)
{
    var layout = def.Pivot!;
    var view = ReportSqlBuilder.Quote(def.View);
    var cols = new List<string>();
    var rows = layout.Rows.Select((f, i) => f with { Key = "r" + i }).ToList();
    var columns = layout.Columns.Select((f, i) => f with { Key = "c" + i }).ToList();
    var measures = layout.Measures.Select((m, i) => m with { Key = "m" + i }).ToList();
    for (var i = 0; i < rows.Count; i++)
    {
        cols.Add($"{view}.{ReportSqlBuilder.Quote(layout.Rows[i].Key)} AS r{i}");
    }

    for (var i = 0; i < columns.Count; i++)
    {
        cols.Add($"{view}.{ReportSqlBuilder.Quote(layout.Columns[i].Key)} AS c{i}");
    }

    for (var i = 0; i < measures.Count; i++)
    {
        cols.Add($"{view}.{ReportSqlBuilder.Quote(layout.Measures[i].Key)} AS m{i}");
    }

    var raw = await db.QueryTableAsync($"SELECT TOP ({maxRaw + 1}) {string.Join(", ", cols)} FROM {view} WHERE 1=1{ReportSqlBuilder.Where(def)}", ReportSqlBuilder.Coerce(def, values).Args, timeout);
    if (raw.Rows.Count > maxRaw)
    {
        return ("SKIP", $"data mentah > {maxRaw:N0} baris; pemeriksaan independen dilewati");
    }

    var independent = layout with { Rows = rows, Columns = columns, Measures = measures };
    var expected = PivotBuilder.Build(independent, PivotQuery.FromRows(independent, raw));
    var diff = PivotCompare.Compare(expected, run.Table);
    return diff is null ? ("OK", $"sama dengan perhitungan independen dari {raw.Rows.Count:N0} baris mentah") : ("DIFF", diff);
}

// Pivot dari SP: hasil SP dijumlahkan langsung dan dibandingkan dengan total pivot.
static async Task<(string, string)> VerifyCustom(IDb db, ReportDefinition def, PivotRunResult run, object?[] values, long firstRunMs, DbOptions timeout)
{
    if (firstRunMs > 30_000)
    {
        return ("SKIP", "SP > 30 dtk; pemeriksaan independen (menjalankan SP lagi) dilewati");
    }

    var spec = def.Custom!;
    var set = await db.QueryDataSetAsync(spec.CallSql, ReportSqlBuilder.Coerce(def, values).Args, new DbOptions(TimeoutSeconds: spec.TimeoutSeconds));
    var table = set.Tables[spec.DataTable];
    var layout = spec.Layout;
    var result = run.Table;
    var visible = layout.Measures.Where(m => m.Visible).ToList();
    var problems = new List<string>();
    for (var mi = 0; mi < visible.Count; mi++)
    {
        var m = visible[mi];
        if (m.Aggregate != PivotAggregate.Sum || !table.Columns.Contains(m.Key))
        {
            continue;
        }

        var expected = table.Rows.Cast<DataRow>().Where(r => r[m.Key] is not DBNull).Sum(r => PivotCompare.ToDouble(r[m.Key]));

        // jumlah semua sel daun x daun untuk ukuran ini (tidak bergantung pada pengaturan total)
        double actual = 0;
        var any = false;
        for (var i = 0; i < result.Rows.Count; i++)
        {
            if (result.Rows[i].Kind != PivotLineKind.Leaf)
            {
                continue;
            }

            for (var j = 0; j < result.Columns.Count; j++)
            {
                if (result.Columns[j].Kind == PivotLineKind.Leaf && result.Columns[j].MeasureIndex == mi && result.Cells[i][j] is { } v)
                {
                    actual += v;
                    any = true;
                }
            }
        }

        if (any && !PivotCompare.Near(expected, actual))
        {
            problems.Add($"{m.Caption}: SP {expected:N2} <> pivot {actual:N2}");
        }
    }

    return problems.Count == 0 ? ("OK", $"jumlah ukuran = jumlah baris hasil SP ({table.Rows.Count:N0} baris)") : ("DIFF", string.Join("; ", problems));
}

static object?[] DefaultValues(ReportDefinition def, int days)
{
    var values = new object?[def.ParamCount];
    var today = DateTime.Today;
    foreach (var f in def.Filters)
    {
        var i = f.Seq - 1;
        string D(DateTime d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        switch (f.Type)
        {
            case ReportFilterType.DateRange:
                values[i] = D(f.DefaultFrom?.ToDateTime(TimeOnly.MinValue) ?? today.AddDays(-days));
                values[i + 1] = D(f.DefaultTo?.ToDateTime(TimeOnly.MinValue) ?? today);
                break;
            case ReportFilterType.DateFrom:
                values[i] = D(f.DefaultFrom?.ToDateTime(TimeOnly.MinValue) ?? today.AddDays(-days));
                break;
            case ReportFilterType.DateTo or ReportFilterType.Date:
                values[i] = D(f.DefaultTo?.ToDateTime(TimeOnly.MinValue) ?? today);
                break;
            case ReportFilterType.MonthYear:
                values[i] = today.Month.ToString(CultureInfo.InvariantCulture);
                values[i + 1] = today.Year.ToString(CultureInfo.InvariantCulture);
                break;
            case ReportFilterType.MonthYearRange:
                var from = today.AddMonths(-3);
                values[i] = from.Month.ToString(CultureInfo.InvariantCulture);
                values[i + 1] = from.Year.ToString(CultureInfo.InvariantCulture);
                values[i + 2] = today.Month.ToString(CultureInfo.InvariantCulture);
                values[i + 3] = today.Year.ToString(CultureInfo.InvariantCulture);
                break;
        }
    }

    return values;
}

static string Trunc(string s, int n) => s.Length <= n ? s : s[..(n - 1)] + "…";

internal sealed record CheckResult(string Id, string Title, string Set, string Kind, string Status, string Detail, long Ms, long Rows);

internal sealed class FixedDbFactory(IDb db) : IModuleDbFactory
{
    public Task<IDb> GetDataDbAsync(string moduleId, CancellationToken ct = default) => Task.FromResult(db);
}

internal static class PivotCompare
{
    public static double ToDouble(object value) => Convert.ToDouble(value, CultureInfo.InvariantCulture);

    public static bool Near(double a, double b) => Math.Abs(a - b) <= 1e-6 * Math.Max(1, Math.Max(Math.Abs(a), Math.Abs(b)));

    /// <returns>Perbedaan pertama, atau <c>null</c> bila sama.</returns>
    public static string? Compare(PivotResult expected, PivotResult actual)
    {
        if (expected.Rows.Count != actual.Rows.Count || expected.Columns.Count != actual.Columns.Count)
        {
            return $"bentuk berbeda: {expected.Rows.Count}x{expected.Columns.Count} (independen) vs {actual.Rows.Count}x{actual.Columns.Count}";
        }

        for (var i = 0; i < expected.Rows.Count; i++)
        {
            if (!expected.Rows[i].Path.SequenceEqual(actual.Rows[i].Path) || expected.Rows[i].Kind != actual.Rows[i].Kind)
            {
                return $"baris {i}: '{string.Join('/', expected.Rows[i].Path)}' vs '{string.Join('/', actual.Rows[i].Path)}'";
            }
        }

        for (var j = 0; j < expected.Columns.Count; j++)
        {
            if (!expected.Columns[j].Path.SequenceEqual(actual.Columns[j].Path) || expected.Columns[j].Kind != actual.Columns[j].Kind)
            {
                return $"kolom {j}: '{string.Join('/', expected.Columns[j].Path)}' vs '{string.Join('/', actual.Columns[j].Path)}'";
            }
        }

        for (var i = 0; i < expected.Rows.Count; i++)
        {
            for (var j = 0; j < expected.Columns.Count; j++)
            {
                var a = expected.Cells[i][j];
                var b = actual.Cells[i][j];
                if (a.HasValue != b.HasValue || (a.HasValue && !Near(a.Value, b!.Value)))
                {
                    return $"sel [{string.Join('/', expected.Rows[i].Path)} | {string.Join('/', expected.Columns[j].Path)}]: {a?.ToString("N4", CultureInfo.InvariantCulture) ?? "kosong"} vs {b?.ToString("N4", CultureInfo.InvariantCulture) ?? "kosong"}";
                }
            }
        }

        return null;
    }
}

internal static class WriteFinder
{
    /// <summary>Tanda bahwa SP menulis ke objek permanen (tabel sementara <c>#x</c> dan variabel tabel <c>@x</c> diperbolehkan).</summary>
    public static string? Find(string definition)
    {
        var text = System.Text.RegularExpressions.Regex.Replace(definition, @"--[^\r\n]*|/\*.*?\*/", " ", System.Text.RegularExpressions.RegexOptions.Singleline);
        var write = System.Text.RegularExpressions.Regex.Match(text, @"\b(?:INSERT\s+(?:INTO\s+)?|UPDATE\s+|DELETE\s+(?:FROM\s+)?|TRUNCATE\s+TABLE\s+|MERGE\s+(?:INTO\s+)?|DROP\s+TABLE\s+|ALTER\s+TABLE\s+)(?<t>[^\s(,;]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        while (write.Success)
        {
            var target = write.Groups["t"].Value;
            if (!target.StartsWith('#') && !target.StartsWith('@'))
            {
                return write.Value.Trim();
            }

            write = write.NextMatch();
        }

        return null;
    }
}

internal sealed class Options
{
    public string? Data { get; private set; }

    public string Module { get; private set; } = "60";

    public int Days { get; private set; } = 7;

    public int MaxRaw { get; private set; } = 300_000;

    public int Timeout { get; private set; } = 300;

    public string? Json { get; private set; }

    public bool Help { get; private set; }

    public List<string> Only { get; } = [];

    public List<string> Skip { get; } = [];

    public static Options Parse(string[] args)
    {
        var o = new Options();
        for (var i = 0; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i] : string.Empty;
            switch (args[i])
            {
                case "--data": o.Data = Next(); break;
                case "--module": o.Module = Next(); break;
                case "--days": o.Days = int.TryParse(Next(), out var d) && d > 0 ? d : 7; break;
                case "--max-raw": o.MaxRaw = int.TryParse(Next(), out var m) && m > 0 ? m : 300_000; break;
                case "--timeout": o.Timeout = int.TryParse(Next(), out var t) && t > 0 ? t : 300; break;
                case "--json": o.Json = Next(); break;
                case "--only": o.Only.AddRange(Next().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)); break;
                case "--skip": o.Skip.AddRange(Next().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)); break;
                case "-h" or "--help": o.Help = true; break;
            }
        }

        o.Data ??= Environment.GetEnvironmentVariable("PANIN_CHECK_DATA");
        if (string.IsNullOrWhiteSpace(o.Data))
        {
            var cfg = new ConfigurationBuilder().AddUserSecrets<Options>(optional: true).Build();
            o.Data = cfg[$"ConnectionStrings:Data:{o.Module}"];
        }

        return o;
    }
}
