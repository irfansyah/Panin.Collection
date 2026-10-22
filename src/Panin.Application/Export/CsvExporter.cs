using System.Globalization;
using System.Text;

namespace Panin.Application.Export;

/// <summary>
/// Ekspor tabel ke CSV. Pemisah titik koma dan angka berkoma desimal (budaya id-ID) supaya terbuka benar di Excel Indonesia,
/// seperti <c>WriteCsv</c> aplikasi lama. UTF-8 dengan BOM agar karakter dikenali Excel.
/// </summary>
public static class CsvExporter
{
    public const string ContentType = "text/csv";

    private const char Separator = ';';

    private static readonly System.Buffers.SearchValues<char> SpecialChars = System.Buffers.SearchValues.Create(";\"\r\n");

    private static readonly CultureInfo Id = CultureInfo.GetCultureInfo("id-ID");

    public static byte[] Export(ExportTable table)
    {
        ArgumentNullException.ThrowIfNull(table);

        var sb = new StringBuilder();
        AppendLine(sb, table.Columns.Select(c => Escape(c.Header)));
        foreach (var row in table.Rows)
        {
            AppendLine(sb, table.Columns.Select((c, i) => Escape(Format(i < row.Count ? row[i] : null, c.Format))));
        }

        var body = Encoding.UTF8.GetBytes(sb.ToString());
        return [.. Encoding.UTF8.GetPreamble(), .. body];
    }

    internal static string Format(object? value, ExportFormat format) => value switch
    {
        null => string.Empty,
        DateTime d => d.TimeOfDay == TimeSpan.Zero ? d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : d.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        decimal m => m.ToString("0.##########", Id),
        double or float => Convert.ToDecimal(value, CultureInfo.InvariantCulture).ToString("0.##########", Id),
        IFormattable f when format != ExportFormat.Text => f.ToString(null, Id),
        _ => Convert.ToString(value, Id) ?? string.Empty,
    };

    private static void AppendLine(StringBuilder sb, IEnumerable<string> cells) => sb.Append(string.Join(Separator, cells)).Append("\r\n");

    private static string Escape(string text)
    {
        // Sel yang diawali = + @ dapat dibaca Excel sebagai rumus; diberi apostrof di depannya.
        if (text.Length > 0 && text[0] is '=' or '+' or '@' or '\t' or '\r')
        {
            text = "'" + text;
        }

        return text.AsSpan().IndexOfAny(SpecialChars) >= 0
            ? "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""
            : text;
    }
}
