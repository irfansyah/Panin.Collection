namespace Panin.Application.Export;

public enum ExportFormat
{
    Text,
    Count,
    Amount,
    Date,
}

public sealed record ExportColumn(string Header, ExportFormat Format = ExportFormat.Text);

public sealed record ExportTable(string SheetName, IReadOnlyList<ExportColumn> Columns, IReadOnlyList<IReadOnlyList<object?>> Rows);

/// <summary>Ekspor tabel ke Excel (.xlsx) tanpa Office terpasang di server (NPOI).</summary>
public interface IExcelExporter
{
    byte[] Export(ExportTable table);

    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
}
