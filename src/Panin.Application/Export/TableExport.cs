using System.Globalization;
using System.Text;

namespace Panin.Application.Export;

/// <summary>Jenis berkas ekspor tabel (aplikasi lama: xls, pdf, rtf, csv, txt).</summary>
public enum ExportKind
{
    Xlsx,
    Csv,
    Pdf,
    Rtf,
    Txt,
}

public sealed record ExportFile(byte[] Content, string Extension, string ContentType);

/// <summary>Menulis <see cref="ExportTable"/> ke jenis berkas yang dipilih; Excel diserahkan ke <see cref="IExcelExporter"/>.</summary>
public static class TableExport
{
    public static ExportFile Create(ExportTable table, ExportKind kind, IExcelExporter excel)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(excel);

        return kind switch
        {
            ExportKind.Xlsx => new ExportFile(excel.Export(table), "xlsx", IExcelExporter.ContentType),
            ExportKind.Csv => new ExportFile(CsvExporter.Export(table), "csv", CsvExporter.ContentType),
            ExportKind.Pdf => new ExportFile(PdfTableWriter.Write(table), "pdf", "application/pdf"),
            ExportKind.Rtf => new ExportFile(RtfTableWriter.Write(table), "rtf", "application/rtf"),
            ExportKind.Txt => new ExportFile(TextTableWriter.Write(table), "txt", "text/plain"),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    public static string Label(ExportKind kind) => kind switch
    {
        ExportKind.Xlsx => "Excel (.xlsx)",
        ExportKind.Csv => "CSV",
        ExportKind.Pdf => "PDF",
        ExportKind.Rtf => "RTF (Word)",
        ExportKind.Txt => "Teks (.txt)",
        _ => kind.ToString(),
    };
}

/// <summary>Teks tampilan sel (angka berpemisah ribuan id-ID, tanggal dd-MMM-yyyy) untuk berkas yang dibaca manusia: PDF, RTF, TXT.</summary>
internal static class ExportDisplay
{
    private static readonly CultureInfo Id = CultureInfo.GetCultureInfo("id-ID");

    public static string Text(object? value, ExportFormat format) => value switch
    {
        null => string.Empty,
        DateTime d => d.TimeOfDay == TimeSpan.Zero ? d.ToString("dd-MMM-yyyy", Id) : d.ToString("dd-MMM-yyyy HH:mm", Id),
        decimal or double or float when format == ExportFormat.Count => Convert.ToDecimal(value, CultureInfo.InvariantCulture).ToString("N0", Id),
        decimal or double or float => Convert.ToDecimal(value, CultureInfo.InvariantCulture).ToString("N2", Id),
        int or long or short or byte when format == ExportFormat.Amount => Convert.ToDecimal(value, CultureInfo.InvariantCulture).ToString("N2", Id),
        int or long or short or byte when format == ExportFormat.Count => Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString("N0", Id),
        _ => Convert.ToString(value, Id) ?? string.Empty,
    };

    public static bool IsNumeric(ExportFormat format) => format is ExportFormat.Count or ExportFormat.Amount;

    public static string[][] Rows(ExportTable table) =>
        [.. table.Rows.Select(r => (string[])[.. table.Columns.Select((c, i) => Text(i < r.Count ? r[i] : null, c.Format))])];
}

/// <summary>Teks bertab (setara ekspor TXT pivot lama), UTF-8 dengan BOM.</summary>
public static class TextTableWriter
{
    public static byte[] Write(ExportTable table)
    {
        var sb = new StringBuilder();
        sb.Append(string.Join('\t', table.Columns.Select(c => Clean(c.Header)))).Append("\r\n");
        foreach (var row in ExportDisplay.Rows(table))
        {
            sb.Append(string.Join('\t', row.Select(Clean))).Append("\r\n");
        }

        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(sb.ToString())];
    }

    private static string Clean(string text) => text.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
}

/// <summary>Tabel RTF landscape A4 yang dapat dibuka di Word dan WordPad; baris judul diulang di tiap halaman.</summary>
public static class RtfTableWriter
{
    private const int PageWidthTwips = 16838;
    private const int MarginTwips = 600;

    public static byte[] Write(ExportTable table)
    {
        var rows = ExportDisplay.Rows(table);
        var usable = PageWidthTwips - (2 * MarginTwips);
        var widths = ColumnWidths(table, rows, usable);

        var sb = new StringBuilder();
        sb.Append(@"{\rtf1\ansi\ansicpg1252\deff0{\fonttbl{\f0\fswiss Arial;}}{\colortbl;\red224\green224\blue224;}\landscape\paperw16838\paperh11906");
        sb.Append(@"\margl").Append(MarginTwips).Append(@"\margr").Append(MarginTwips).Append(@"\margt600\margb600\fs16");
        sb.Append(@"\pard\b\fs22 ").Append(Escape(table.SheetName)).Append(@"\b0\fs16\par\par").Append("\r\n");

        Row(sb, widths, table.Columns.Select(c => c.Header).ToList(), table.Columns.Select(_ => false).ToList(), header: true);
        foreach (var row in rows)
        {
            Row(sb, widths, row, table.Columns.Select(c => ExportDisplay.IsNumeric(c.Format)).ToList(), header: false);
        }

        sb.Append('}');
        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    private static void Row(StringBuilder sb, int[] widths, IReadOnlyList<string> cells, IReadOnlyList<bool> numeric, bool header)
    {
        sb.Append(@"\trowd\trgaph70");
        if (header)
        {
            sb.Append(@"\trhdr");
        }

        var x = MarginTwips;
        for (var i = 0; i < widths.Length; i++)
        {
            x += widths[i];
            sb.Append(@"\clbrdrb\brdrs\brdrw5");
            if (header)
            {
                sb.Append(@"\clcbpat1");
            }

            sb.Append(@"\cellx").Append(x);
        }

        for (var i = 0; i < widths.Length; i++)
        {
            sb.Append(@"\pard\intbl").Append(numeric[i] ? @"\qr " : @"\ql ");
            sb.Append(header ? @"\b " : string.Empty).Append(Escape(i < cells.Count ? cells[i] : string.Empty)).Append(header ? @"\b0" : string.Empty).Append(@"\cell");
        }

        sb.Append(@"\row").Append("\r\n");
    }

    private static int[] ColumnWidths(ExportTable table, string[][] rows, int usable)
    {
        var n = table.Columns.Count;
        var chars = new double[n];
        for (var c = 0; c < n; c++)
        {
            var longest = table.Columns[c].Header.Length;
            foreach (var r in rows.Take(500))
            {
                longest = Math.Max(longest, r[c].Length);
            }

            chars[c] = Math.Clamp(longest, 4, 40);
        }

        var total = chars.Sum();
        return [.. chars.Select(w => (int)(w / total * usable))];
    }

    internal static string Escape(string text)
    {
        var sb = new StringBuilder(text.Length + 8);
        foreach (var ch in text)
        {
            switch (ch)
            {
                case '\\':
                    sb.Append(@"\\");
                    break;
                case '{':
                    sb.Append(@"\{");
                    break;
                case '}':
                    sb.Append(@"\}");
                    break;
                case '\r':
                    break;
                case '\n':
                    sb.Append(@"\line ");
                    break;
                case < ' ':
                    sb.Append(' ');
                    break;
                case > '~':
                    sb.Append(@"\u").Append((short)ch).Append('?');
                    break;
                default:
                    sb.Append(ch);
                    break;
            }
        }

        return sb.ToString();
    }
}
