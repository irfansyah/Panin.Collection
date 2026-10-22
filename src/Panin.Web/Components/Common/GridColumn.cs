using Microsoft.AspNetCore.Components;
using Panin.Application.Export;

namespace Panin.Web.Components.Common;

/// <summary>Definisi satu kolom <see cref="DataGrid{TItem}"/>.</summary>
public sealed class GridColumn<TItem>
{
    public required string Header { get; init; }

    /// <summary>Nilai mentah sel; dipakai untuk urut, filter, dan ekspor.</summary>
    public required Func<TItem, object?> Value { get; init; }

    /// <summary>Teks tampilan; bila null dipakai <see cref="Value"/>.ToString().</summary>
    public Func<TItem, string?>? Display { get; init; }

    /// <summary>Tampilan khusus (mis. tombol/tautan); tidak ikut filter/ekspor.</summary>
    public RenderFragment<TItem>? Template { get; init; }

    public bool Numeric { get; init; }

    public bool Sortable { get; init; } = true;

    public ExportFormat ExportFormat { get; init; } = ExportFormat.Text;

    /// <summary>Kolom hanya tampilan (tombol aksi): tidak ikut urut, filter, dan ekspor.</summary>
    public bool ActionOnly { get; init; }

    public string Text(TItem item) => Display?.Invoke(item) ?? Convert.ToString(Value(item), System.Globalization.CultureInfo.CurrentCulture) ?? string.Empty;
}
