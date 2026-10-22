using System.Reflection;
using System.Text.Json;
using Panin.Application.Reports.Pivot;

namespace Panin.Infrastructure.Reports.Custom;

/// <summary>
/// Katalog report pivot kustom dari <c>pivot-layouts.json</c> (resource tertanam), hasil ekstraksi tata letak kelas <c>XtraReport PV_*</c> lama
/// oleh <c>tools/ReportExtract/extract_pivot_layouts.py</c>. Kelas yang tidak sesuai pola ditandai <c>irregular</c> dan tidak masuk katalog.
/// </summary>
public sealed class CustomPivotCatalog : ICustomPivotCatalog
{
    private readonly Dictionary<string, CustomPivotSpec> _specs;

    public CustomPivotCatalog()
    {
        using var stream = typeof(CustomPivotCatalog).Assembly.GetManifestResourceStream("Panin.Infrastructure.Reports.Custom.pivot-layouts.json")
            ?? throw new InvalidOperationException("Resource pivot-layouts.json tidak ditemukan");
        _specs = Parse(JsonDocument.Parse(stream).RootElement);
    }

    public CustomPivotCatalog(JsonElement root) => _specs = Parse(root);

    public int Count => _specs.Count;

    public IReadOnlyCollection<string> Classes => _specs.Keys;

    public CustomPivotSpec? Find(string assemblyObj) => _specs.TryGetValue(assemblyObj, out var spec) ? spec : null;

    public static Dictionary<string, CustomPivotSpec> Parse(JsonElement root)
    {
        var result = new Dictionary<string, CustomPivotSpec>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in root.EnumerateObject().Select(p => p.Value))
        {
            if (entry.TryGetProperty("irregular", out _) || !entry.TryGetProperty("sp", out var sp))
            {
                continue;
            }

            var fields = entry.GetProperty("fields").EnumerateArray().ToList();
            List<JsonElement> Area(string area) => [.. fields.Where(f => f.GetProperty("area").GetString() == area).OrderBy(f => f.TryGetProperty("index", out var i) ? i.GetInt32() : int.MaxValue)];

            PivotField Field(JsonElement f) => new(
                f.GetProperty("field").GetString()!,
                Text(f, "caption") ?? f.GetProperty("field").GetString()!,
                SortByMeasure: Text(f, "sortBy"),
                Format: Text(f, "valueFormat"));

            PivotMeasure Measure(JsonElement f) => new(
                f.GetProperty("field").GetString()!,
                Text(f, "caption") ?? f.GetProperty("field").GetString()!,
                Visible: !f.TryGetProperty("visible", out var v) || v.GetBoolean(),
                ShowGrandTotal: !f.TryGetProperty("grandTotal", out var g) || g.GetBoolean(),
                Format: Text(f, "cellFormat"));

            var options = entry.GetProperty("options");
            bool Option(string name) => !options.TryGetProperty(name, out var o) || o.GetBoolean();

            var layout = new PivotLayout(
                [.. Area("row").Select(Field)],
                [.. Area("column").Select(Field)],
                [.. Area("data").Select(Measure)],
                RowTotals: Option("rowTotals"),
                RowGrandTotal: Option("rowGrandTotals"),
                ColumnTotals: Option("columnTotals"),
                ColumnGrandTotal: Option("columnGrandTotals"));

            var spec = new CustomPivotSpec(
                entry.GetProperty("class").GetString()!,
                sp.GetString()!,
                entry.GetProperty("params").GetInt32(),
                entry.GetProperty("timeoutSeconds").GetInt32(),
                entry.GetProperty("dataTable").GetInt32(),
                Text(entry, "periodColumn"),
                layout);
            result[spec.Class] = spec;
        }

        return result;
    }

    private static string? Text(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
