using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace Panin.Application.Export;

/// <summary>
/// Penulis PDF tabel tanpa pustaka luar dan tanpa font terpasang di server: huruf standar PDF (Helvetica) dengan sandi WinAnsi.
/// Landscape A4, judul di tiap halaman, baris selang-seling, kolom angka rata kanan, teks panjang dipotong dengan "...".
/// </summary>
public static class PdfTableWriter
{
    /// <summary>Batas baris per berkas (sisanya dipotong dan dicatat di halaman terakhir) supaya ukuran berkas wajar.</summary>
    public const int MaxRows = 20_000;

    private const double PageWidth = 841.89;
    private const double PageHeight = 595.28;
    private const double Margin = 30;
    private const double Pad = 3;
    private const double MaxColumnWidth = 230;

    // Lebar huruf Helvetica dan Helvetica-Bold (per 1000 em) untuk kode 32..126.
    private static readonly short[] Regular =
    [
        278, 278, 355, 556, 556, 889, 667, 191, 333, 333, 389, 584, 278, 333, 278, 278,
        556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 278, 278, 584, 584, 584, 556,
        1015, 667, 667, 722, 722, 667, 611, 778, 722, 278, 500, 667, 556, 833, 722, 778,
        667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 278, 278, 278, 469, 556,
        333, 556, 556, 500, 556, 556, 278, 556, 556, 222, 222, 500, 222, 833, 556, 556,
        556, 556, 333, 500, 278, 556, 500, 722, 500, 500, 500, 334, 260, 334, 584,
    ];

    private static readonly short[] Bold =
    [
        278, 333, 474, 556, 556, 889, 722, 238, 333, 333, 389, 584, 278, 333, 278, 278,
        556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 333, 333, 584, 584, 584, 611,
        975, 722, 722, 722, 722, 667, 611, 778, 722, 278, 556, 722, 611, 833, 722, 778,
        667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 333, 278, 333, 584, 556,
        333, 556, 611, 556, 611, 556, 333, 611, 611, 278, 278, 556, 278, 889, 611, 611,
        611, 611, 389, 556, 333, 611, 556, 778, 556, 556, 500, 389, 280, 389, 584,
    ];

    private static readonly Encoding WinAnsi = CodePagesEncodingProvider.Instance.GetEncoding(1252)!;

    public static byte[] Write(ExportTable table)
    {
        ArgumentNullException.ThrowIfNull(table);

        var all = ExportDisplay.Rows(table);
        var truncated = all.Length > MaxRows;
        var rows = truncated ? all[..MaxRows] : all;
        var n = table.Columns.Count;
        var numeric = table.Columns.Select(c => ExportDisplay.IsNumeric(c.Format)).ToArray();
        var usable = PageWidth - (2 * Margin);

        var (fontSize, widths) = Layout(table, rows, usable);
        var rowHeight = fontSize + 4;
        var headerHeight = fontSize + 7;
        const double titleBlock = 34;
        const double footerBlock = 22;
        var rowsPerPage = Math.Max(1, (int)((PageHeight - (2 * Margin) - titleBlock - footerBlock - headerHeight) / rowHeight));
        var pageCount = Math.Max(1, (int)Math.Ceiling(rows.Length / (double)rowsPerPage));
        var stamp = DateTime.Now.ToString("dd-MMM-yyyy HH:mm", CultureInfo.GetCultureInfo("id-ID"));

        var pages = new List<byte[]>();
        for (var p = 0; p < pageCount; p++)
        {
            var c = new Content();
            var y = PageHeight - Margin;
            c.Text(Margin, y - 11, table.SheetName, bold: true, 11);
            c.Text(Margin, y - 24, stamp, bold: false, 7);
            y -= titleBlock;

            // Baris judul
            c.Fill(0.88);
            c.Rect(Margin, y - headerHeight, usable, headerHeight);
            c.FillBlack();
            var x = Margin;
            for (var i = 0; i < n; i++)
            {
                var label = Fit(table.Columns[i].Header, widths[i] - (2 * Pad), bold: true, fontSize);
                var tx = numeric[i] ? x + widths[i] - Pad - Measure(label, true, fontSize) : x + Pad;
                c.Text(tx, y - headerHeight + 3.5, label, bold: true, fontSize);
                x += widths[i];
            }

            y -= headerHeight;
            c.Line(Margin, y, Margin + usable, y);

            var first = p * rowsPerPage;
            var last = Math.Min(rows.Length, first + rowsPerPage);
            for (var r = first; r < last; r++)
            {
                if ((r & 1) == 1)
                {
                    c.Fill(0.96);
                    c.Rect(Margin, y - rowHeight, usable, rowHeight);
                    c.FillBlack();
                }

                x = Margin;
                for (var i = 0; i < n; i++)
                {
                    var text = Fit(rows[r][i], widths[i] - (2 * Pad), bold: false, fontSize);
                    if (text.Length > 0)
                    {
                        var tx = numeric[i] ? x + widths[i] - Pad - Measure(text, false, fontSize) : x + Pad;
                        c.Text(tx, y - rowHeight + 3.2, text, bold: false, fontSize);
                    }

                    x += widths[i];
                }

                y -= rowHeight;
            }

            if (n == 0 || rows.Length == 0)
            {
                c.Text(Margin + Pad, y - rowHeight + 3.2, "Tidak ada data.", bold: false, fontSize);
            }

            var note = truncated && p == pageCount - 1 ? $"  |  dipotong pada {MaxRows:N0} baris dari {all.Length:N0}" : string.Empty;
            c.Text(Margin, Margin - 8, $"Halaman {p + 1} dari {pageCount}{note}", bold: false, 7);
            pages.Add(c.ToBytes());
        }

        return Assemble(pages);
    }

    private static (double FontSize, double[] Widths) Layout(ExportTable table, string[][] rows, double usable)
    {
        var n = table.Columns.Count;
        if (n == 0)
        {
            return (8, []);
        }

        // Lebar teks terpanjang (satuan 1/1000 em) per kolom dari judul dan 500 baris pertama.
        var units = new double[n];
        for (var i = 0; i < n; i++)
        {
            var longest = MeasureUnits(table.Columns[i].Header, true);
            foreach (var r in rows.Take(500))
            {
                longest = Math.Max(longest, MeasureUnits(r[i], false));
            }

            units[i] = longest;
        }

        foreach (var size in new[] { 8.0, 7.0, 6.0, 5.0 })
        {
            var natural = units.Select(u => Math.Min(MaxColumnWidth, (u * size / 1000) + (2 * Pad))).ToArray();
            if (natural.Sum() <= usable || size == 5.0)
            {
                var scale = natural.Sum() <= usable ? 1.0 : usable / natural.Sum();
                // Sisa ruang dibagi rata supaya tabel pendek tetap mengisi lebar halaman.
                var extra = natural.Sum() < usable ? (usable - natural.Sum()) / n : 0;
                return (size, [.. natural.Select(w => (w * scale) + extra)]);
            }
        }

        return (5, [.. Enumerable.Repeat(usable / n, n)]);
    }

    private static double MeasureUnits(string text, bool bold)
    {
        var table = bold ? Bold : Regular;
        double sum = 0;
        foreach (var ch in text)
        {
            sum += ch is >= ' ' and <= '~' ? table[ch - ' '] : 556;
        }

        return sum;
    }

    private static double Measure(string text, bool bold, double size) => MeasureUnits(text, bold) * size / 1000;

    private static string Fit(string text, double maxWidth, bool bold, double size)
    {
        text = text.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
        if (Measure(text, bold, size) <= maxWidth)
        {
            return text;
        }

        const string ellipsis = "...";
        var budget = maxWidth - Measure(ellipsis, bold, size);
        var sb = new StringBuilder();
        double used = 0;
        foreach (var ch in text)
        {
            var w = MeasureUnits(ch.ToString(), bold) * size / 1000;
            if (used + w > budget)
            {
                break;
            }

            sb.Append(ch);
            used += w;
        }

        return sb.Length == 0 ? string.Empty : sb + ellipsis;
    }

    private static byte[] Assemble(List<byte[]> pages)
    {
        // Objek: 1 Catalog, 2 Pages, 3 Helvetica, 4 Helvetica-Bold, lalu pasangan (halaman, isi) per halaman.
        using var ms = new MemoryStream();
        var offsets = new List<long>();

        void Begin(int number)
        {
            offsets.Add(ms.Position);
            Write($"{number} 0 obj\n");
        }

        void Write(string s) => ms.Write(Encoding.ASCII.GetBytes(s));

        Write("%PDF-1.4\n%âãÏÓ\n");
        var kids = string.Join(' ', Enumerable.Range(0, pages.Count).Select(i => $"{5 + (2 * i)} 0 R"));

        Begin(1);
        Write("<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        Begin(2);
        Write($"<< /Type /Pages /Kids [{kids}] /Count {pages.Count} >>\nendobj\n");
        Begin(3);
        Write("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>\nendobj\n");
        Begin(4);
        Write("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>\nendobj\n");

        for (var i = 0; i < pages.Count; i++)
        {
            var pageObj = 5 + (2 * i);
            Begin(pageObj);
            Write(string.Create(CultureInfo.InvariantCulture, $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {PageWidth:0.##} {PageHeight:0.##}] /Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> /Contents {pageObj + 1} 0 R >>\nendobj\n"));

            using var packed = new MemoryStream();
            using (var z = new ZLibStream(packed, CompressionLevel.Optimal, leaveOpen: true))
            {
                z.Write(pages[i]);
            }

            Begin(pageObj + 1);
            Write($"<< /Length {packed.Length} /Filter /FlateDecode >>\nstream\n");
            ms.Write(packed.GetBuffer(), 0, (int)packed.Length);
            Write("\nendstream\nendobj\n");
        }

        var xref = ms.Position;
        Write($"xref\n0 {offsets.Count + 1}\n0000000000 65535 f \n");
        foreach (var o in offsets)
        {
            Write($"{o:D10} 00000 n \n");
        }

        Write($"trailer\n<< /Size {offsets.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return ms.ToArray();
    }

    private sealed class Content
    {
        private readonly MemoryStream _ms = new();

        public void Text(double x, double y, string text, bool bold, double size)
        {
            Raw(string.Create(CultureInfo.InvariantCulture, $"BT /{(bold ? "F2" : "F1")} {size:0.##} Tf {x:0.##} {y:0.##} Td ("));
            foreach (var b in WinAnsi.GetBytes(text))
            {
                if (b is (byte)'(' or (byte)')' or (byte)'\\')
                {
                    _ms.WriteByte((byte)'\\');
                }

                _ms.WriteByte(b is < 32 ? (byte)' ' : b);
            }

            Raw(") Tj ET\n");
        }

        public void Fill(double gray) => Raw(string.Create(CultureInfo.InvariantCulture, $"{gray:0.##} g\n"));

        public void FillBlack() => Raw("0 g\n");

        public void Rect(double x, double y, double w, double h) => Raw(string.Create(CultureInfo.InvariantCulture, $"{x:0.##} {y:0.##} {w:0.##} {h:0.##} re f\n"));

        public void Line(double x1, double y1, double x2, double y2) => Raw(string.Create(CultureInfo.InvariantCulture, $"0.4 w {x1:0.##} {y1:0.##} m {x2:0.##} {y2:0.##} l S\n"));

        public byte[] ToBytes() => _ms.ToArray();

        private void Raw(string s) => _ms.Write(Encoding.ASCII.GetBytes(s));
    }
}
