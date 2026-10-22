using System.Data;
using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Panin.Application.Abstractions;
using Panin.Application.Memos;
using Panin.Application.Modules;

namespace Panin.Infrastructure.Memos;

public sealed class MemoOptions
{
    public const string SectionName = "Memos";

    /// <summary>Folder templat HTML memo (relatif terhadap folder aplikasi bila tidak absolut).</summary>
    public string TemplateDirectory { get; set; } = "Templates/Memo";
}

/// <summary>
/// Satu jenis memo: view cari, view laporan, templat, awalan nama berkas, dan pemetaan penanda templat (<c>{{lblNama}}</c>) ke kolom view laporan.
/// Disalin dari kode-behind halaman lama (<c>Report/*.aspx.cs</c>); penanda yang tidak dipetakan tetap kosong, seperti label yang tidak diisi layar lama.
/// </summary>
public sealed record MemoDefinition(MemoKind Kind, string SearchView, string ReportView, string Template, string FilePrefix, IReadOnlyDictionary<string, string> Map);

public static class MemoCatalog
{
    private static readonly Dictionary<MemoKind, MemoDefinition> Definitions = new()
    {
        [MemoKind.Skpm] = new(MemoKind.Skpm, "VW_SEARCH_SKPM", "VW_REPORT_SKPAM", "skpm.html", "ReportSKPM_", new Dictionary<string, string>
        {
            ["lblMerk"] = "MERK",
            ["lblNoRangka"] = "NO_RANGKA",
            ["lblNoMesin"] = "NO_MESIN",
            ["lblNoPolisi"] = "NO_POLISI",
            ["lblWarna"] = "WARNA",
            ["lblKCU"] = "NAMA_KCU",
            ["lblNama"] = "NAMA_DEBITUR",
            ["tglSurat"] = "TANGGAL",
        }),
        [MemoKind.HapusBuku] = new(MemoKind.HapusBuku, "VW_SEARCH_HAPUS_BUKU", "VW_REPORT_HAPUS_BUKU", "hapus-buku.html", "ReportHapusBuku_", new Dictionary<string, string>
        {
            ["lblNmdebitur"] = "NAMA_DEBITUR",
            ["TANGGAL"] = "TANGGAL",
        }),
        [MemoKind.HapusTagih] = new(MemoKind.HapusTagih, "VW_SEARCH_HAPUS_TAGIH", "VW_REPORT_HAPUS_TAGIH", "hapus-tagih.html", "ReportHapusTagih_", new Dictionary<string, string>
        {
            ["lblNmdebitur"] = "NAMA_DEBITUR",
            ["TANGGAL"] = "TANGGAL",
        }),
        [MemoKind.Restrukturisasi] = new(MemoKind.Restrukturisasi, "VW_SEARCH_RESTRUKTURISASI", "VW_REPORT_RESTRUKTURISASI", "restrukturisasi.html", "ReportRestrukturisasi_", new Dictionary<string, string>
        {
            ["lblNama"] = "NAMA_DEBITUR",
            ["TANGGAL"] = "TANGGAL",
            ["lblNoKTP"] = "NO_KTP",
        }),
        [MemoKind.Ayda] = new(MemoKind.Ayda, "VW_SEARCH_PENGAJUAN_AYDA", "VW_REPORT_PENGAJUAN_AYDA", "ayda.html", "ReportPengajuanAYDA_", new Dictionary<string, string>
        {
            ["lblName"] = "NAMA_DEBITUR",
            ["TANGGAL"] = "TANGGAL",
        }),
    };

    public static MemoDefinition Get(MemoKind kind) => Definitions[kind];

    public static IEnumerable<MemoDefinition> All => Definitions.Values;
}

public static partial class MemoRenderer
{
    /// <summary>Mengganti setiap <c>{{PENANDA}}</c> dengan nilai yang di-HTML-encode; penanda tanpa nilai menjadi kosong.</summary>
    public static string Render(string template, IReadOnlyDictionary<string, string?> values)
        => Token().Replace(template, m => values.TryGetValue(m.Groups[1].Value, out var v) && v is not null ? WebUtility.HtmlEncode(v) : string.Empty);

    [GeneratedRegex(@"\{\{(\w+)\}\}")]
    private static partial Regex Token();
}

/// <summary>Memo cetak (SKPM, Hapus Buku, Hapus Tagih, Restrukturisasi, AYDA). Hanya membaca.</summary>
public sealed class MemoService(IModuleDbFactory dbs, IOptions<MemoOptions> options) : IMemoService
{
    private readonly Dictionary<MemoKind, string> _templates = [];

    public async Task<IReadOnlyList<MemoSearchRow>> SearchAsync(MemoKind kind, string accountNo, CancellationToken ct = default)
    {
        if (!MemoRules.IsValidAccount(accountNo))
        {
            return [];
        }

        var def = MemoCatalog.Get(kind);
        var db = await dbs.GetDataDbAsync("60", ct).ConfigureAwait(false);
        var table = await db.QueryTableAsync($"SELECT TOP (50) [NO ACCOUNT], [NAMA DEBITUR] FROM {def.SearchView} WHERE [NO ACCOUNT] = @1", [accountNo.Trim()], new DbOptions(TimeoutSeconds: 60), ct).ConfigureAwait(false);
        return [.. table.Rows.Cast<DataRow>().Select(r => new MemoSearchRow(Text(r, "NO ACCOUNT") ?? string.Empty, Text(r, "NAMA DEBITUR")))];
    }

    public async Task<MemoDocument?> BuildAsync(MemoKind kind, string accountNo, CancellationToken ct = default)
    {
        if (!MemoRules.IsValidAccount(accountNo))
        {
            return null;
        }

        var def = MemoCatalog.Get(kind);
        var db = await dbs.GetDataDbAsync("60", ct).ConfigureAwait(false);
        var row = await db.QueryFirstOrDefaultAsync($"SELECT * FROM {def.ReportView} WHERE ACC_NO = @1", [accountNo.Trim()], new DbOptions(TimeoutSeconds: 60), ct).ConfigureAwait(false);
        if (row is null)
        {
            return null;
        }

        var values = def.Map.ToDictionary(kv => kv.Key, kv => Text(row, kv.Value), StringComparer.Ordinal);
        var html = MemoRenderer.Render(await TemplateAsync(def, ct).ConfigureAwait(false), values);
        return new MemoDocument(html, def.FilePrefix + accountNo.Trim() + ".doc");
    }

    private async Task<string> TemplateAsync(MemoDefinition def, CancellationToken ct)
    {
        if (_templates.TryGetValue(def.Kind, out var cached))
        {
            return cached;
        }

        var dir = Path.IsPathRooted(options.Value.TemplateDirectory) ? options.Value.TemplateDirectory : Path.Combine(AppContext.BaseDirectory, options.Value.TemplateDirectory);
        var text = await File.ReadAllTextAsync(Path.Combine(dir, def.Template), ct).ConfigureAwait(false);
        lock (_templates)
        {
            _templates[def.Kind] = text;
        }

        return text;
    }

    private static string? Text(DataRow r, string column)
        => r.Table.Columns.Contains(column) && r[column] is not DBNull ? Convert.ToString(r[column], CultureInfo.InvariantCulture)?.Trim() : null;
}
