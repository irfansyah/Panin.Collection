using Panin.Application.Export;

namespace Panin.Application.Reports.Pivot;

public sealed record PivotHeaderCell(string Text, int ColSpan, int RowSpan, bool Total);

public sealed record PivotRowCell(string Text, int RowSpan, int ColSpan, bool Total);

/// <summary>
/// Bentuk tampilan <see cref="PivotResult"/> sebagai tabel HTML bertingkat (sel gabung) dan sebagai ekspor Excel datar.
/// Dipisah dari komponen Razor agar bisa diuji.
/// </summary>
public static class PivotLayoutView
{
    /// <summary>Jumlah kolom penampung judul baris (minimal satu).</summary>
    public static int RowHeaderWidth(PivotResult r) => Math.Max(1, r.RowCaptions.Count);

    public static bool HasMeasureRow(PivotResult r) => r.VisibleMeasures.Count > 1 || r.ColumnCaptions.Count == 0;

    /// <summary>Baris-baris judul kolom: satu baris per field kolom, ditambah satu baris nama ukuran bila ukuran lebih dari satu.</summary>
    public static IReadOnlyList<IReadOnlyList<PivotHeaderCell>> ColumnHeaders(PivotResult r)
    {
        var levels = r.ColumnCaptions.Count;
        var rows = new List<IReadOnlyList<PivotHeaderCell>>();
        for (var l = 0; l < levels; l++)
        {
            var cells = new List<PivotHeaderCell>();
            string? lastKey = null;
            foreach (var col in r.Columns)
            {
                var len = col.Path.Count;
                if (col.Kind == PivotLineKind.GrandTotal)
                {
                    if (l == 0)
                    {
                        cells.Add(new PivotHeaderCell("Total", 1, levels, true));
                        lastKey = null;
                    }

                    continue;
                }

                if (col.Kind == PivotLineKind.Subtotal && l > len - 1)
                {
                    // level ini sudah tertutup oleh RowSpan sel "Total ..." di atasnya
                    continue;
                }

                if (col.Kind == PivotLineKind.Subtotal && l == len - 1)
                {
                    cells.Add(new PivotHeaderCell("Total " + col.Path[l], 1, levels - l, true));
                    lastKey = null;
                    continue;
                }

                var key = string.Join('\u001f', col.Path.Take(l + 1));
                if (lastKey == key && cells.Count > 0 && !cells[^1].Total)
                {
                    var last = cells[^1];
                    cells[^1] = last with { ColSpan = last.ColSpan + 1 };
                }
                else
                {
                    cells.Add(new PivotHeaderCell(col.Path[l], 1, 1, false));
                    lastKey = key;
                }
            }

            rows.Add(cells);
        }

        if (HasMeasureRow(r))
        {
            rows.Add([.. r.Columns.Select(c => new PivotHeaderCell(r.VisibleMeasures[c.MeasureIndex].Caption, 1, 1, c.Kind != PivotLineKind.Leaf))]);
        }

        return rows;
    }

    /// <summary>Sel judul baris untuk satu baris isi (kosong bila sel itu sudah tercakup rowspan baris sebelumnya).</summary>
    public static IReadOnlyList<PivotRowCell> RowHeaders(PivotResult r, int index)
    {
        var line = r.Rows[index];
        var width = RowHeaderWidth(r);
        var cells = new List<PivotRowCell>();
        if (r.RowCaptions.Count == 0)
        {
            cells.Add(new PivotRowCell("Total", 1, 1, true));
            return cells;
        }

        for (var level = 0; level < r.RowCaptions.Count; level++)
        {
            if (level < line.Path.Count)
            {
                if (StartsGroup(r, index, level))
                {
                    cells.Add(new PivotRowCell(line.Path[level], GroupSize(r, index, level), 1, false));
                }

                continue;
            }

            if (line.Kind == PivotLineKind.Subtotal)
            {
                cells.Add(new PivotRowCell("Total " + line.Path[^1], 1, width - level, true));
            }
            else if (line.Kind == PivotLineKind.GrandTotal)
            {
                cells.Add(new PivotRowCell("Total", 1, width, true));
            }

            break;
        }

        return cells;
    }

    /// <summary>Ekspor datar: satu baris judul (kolom bertingkat digabung dengan " / ") dan satu baris per baris pivot.</summary>
    public static ExportTable ToExportTable(PivotResult r, string sheetName)
    {
        var width = RowHeaderWidth(r);
        var columns = new List<ExportColumn>();
        for (var i = 0; i < width; i++)
        {
            columns.Add(new ExportColumn(r.RowCaptions.Count == 0 ? string.Empty : r.RowCaptions[i]));
        }

        foreach (var col in r.Columns)
        {
            var measure = r.VisibleMeasures[col.MeasureIndex];
            var parts = new List<string>();
            switch (col.Kind)
            {
                case PivotLineKind.GrandTotal:
                    parts.Add("Total");
                    break;
                case PivotLineKind.Subtotal:
                    parts.AddRange(col.Path.Take(col.Path.Count - 1));
                    parts.Add("Total " + col.Path[^1]);
                    break;
                default:
                    parts.AddRange(col.Path);
                    break;
            }

            if (r.VisibleMeasures.Count > 1 || parts.Count == 0)
            {
                parts.Add(measure.Caption);
            }

            columns.Add(new ExportColumn(string.Join(" / ", parts), measure.Aggregate is PivotAggregate.Count ? ExportFormat.Count : ExportFormat.Amount));
        }

        var rows = new List<IReadOnlyList<object?>>();
        for (var i = 0; i < r.Rows.Count; i++)
        {
            var line = r.Rows[i];
            var row = new List<object?>(new object?[width]);
            switch (line.Kind)
            {
                case PivotLineKind.Leaf:
                    for (var k = 0; k < line.Path.Count; k++)
                    {
                        row[k] = line.Path[k];
                    }

                    if (line.Path.Count == 0)
                    {
                        row[0] = "Total";
                    }

                    break;
                case PivotLineKind.Subtotal:
                    for (var k = 0; k < line.Path.Count; k++)
                    {
                        row[k] = line.Path[k];
                    }

                    if (line.Path.Count < width)
                    {
                        row[line.Path.Count] = "Total " + line.Path[^1];
                    }

                    break;
                default:
                    row[0] = "Total";
                    break;
            }

            row.AddRange(r.Cells[i].Select(v => (object?)v));
            rows.Add(row);
        }

        return new ExportTable(sheetName.Length > 31 ? sheetName[..31] : sheetName, columns, rows);
    }

    private static bool StartsGroup(PivotResult r, int index, int level)
        => index == 0 || !Covers(r.Rows[index - 1], r.Rows[index], level);

    private static int GroupSize(PivotResult r, int index, int level)
    {
        var n = 1;
        while (index + n < r.Rows.Count && Covers(r.Rows[index], r.Rows[index + n], level))
        {
            n++;
        }

        return n;
    }

    // Baris `next` masih satu kelompok dengan `head` pada level tersebut: punya nilai di level itu dan awalan yang sama.
    private static bool Covers(PivotRowLine head, PivotRowLine next, int level)
        => head.Path.Count > level && next.Path.Count > level && head.Path.Take(level + 1).SequenceEqual(next.Path.Take(level + 1));
}
