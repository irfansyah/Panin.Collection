namespace Panin.Application.Reports;

/// <summary>
/// Report kustom berbentuk daftar dari satu SP (bukan pivot): kelas XtraReport berpita di aplikasi lama. Kolom dan judulnya disalin dari
/// kepala tabel kelas itu. Hanya sedikit yang ada, jadi ditulis tangan di sini, bukan diekstrak.
/// </summary>
/// <param name="DataTable">Indeks result set yang berisi baris daftar.</param>
/// <param name="SubtitleTemplate">Keterangan di atas laporan dari result set pertama: <c>{KOLOM}</c> diganti nilainya.</param>
public sealed record CustomListSpec(string Class, string Procedure, int ParamCount, int TimeoutSeconds, int DataTable, string? SubtitleTemplate, IReadOnlyList<ReportColumn> Columns)
{
    public string CallSql => "EXEC " + Procedure + (ParamCount == 0 ? string.Empty : " " + string.Join(", ", Enumerable.Range(1, ParamCount).Select(i => "@" + i.ToString(System.Globalization.CultureInfo.InvariantCulture))));
}

public static class CustomLists
{
    private static readonly IReadOnlyList<ReportColumn> Productivity =
    [
        new("FULL_NAME", "Collector Name", ReportSummary.None),
        new("CALL_DATE", "Date", ReportSummary.None),
        new("NUMBER_OF_ACCOUNT", "ACC#", ReportSummary.Sum),
        new("OUTGOING_CALL", "OC#", ReportSummary.Sum),
        new("CONTACTED", "C#", ReportSummary.Sum),
        new("PTP", "PTP#", ReportSummary.Sum),
        new("PTPAMOUNT", "PTP Amount", ReportSummary.Sum),
        new("PK", "PK#", ReportSummary.Sum),
        new("PKAMOUNT", "PK Amount", ReportSummary.Sum),
        new("BP", "BP#", ReportSummary.Sum),
        new("BPAMOUNT", "BP Amount", ReportSummary.Sum),
    ];

    private static readonly Dictionary<string, CustomListSpec> Specs = new(StringComparer.OrdinalIgnoreCase)
    {
        // Call Productivity: kelas dan SP ada di aplikasi lama.
        ["LOSCOLLECTION.Report.Template.CallProductivity"] = new("LOSCOLLECTION.Report.Template.CallProductivity", "USP_VIEWREPORTCOLLPROD", 4, 600, 1, "Supervisor: {UPLINER}   Kolektor: {COLLECTOR}", Productivity),

        // Varian Recovery: kelas CallProductivityRecovery tidak ada di sumber aplikasi lama yang dibaca (report itu tidak bisa dibuka di sana),
        // tetapi SP-nya ada dan mengembalikan result set yang sama bentuknya; dipakai dengan tata letak yang sama.
        ["LOSCOLLECTION.Report.Template.CallProductivityRecovery"] = new("LOSCOLLECTION.Report.Template.CallProductivityRecovery", "USP_VIEWREPORTCOLLPROD_RECOVERY", 4, 600, 1, "Supervisor: {UPLINER}   Kolektor: {COLLECTOR}", Productivity),
    };

    public static CustomListSpec? Find(string assemblyObj) => Specs.TryGetValue(assemblyObj.Trim(), out var spec) ? spec : null;

    public static string? Subtitle(CustomListSpec spec, System.Data.DataTable header)
    {
        if (spec.SubtitleTemplate is not { } template || header.Rows.Count == 0)
        {
            return null;
        }

        var text = template;
        foreach (System.Data.DataColumn c in header.Columns)
        {
            var value = Convert.ToString(header.Rows[0][c], System.Globalization.CultureInfo.InvariantCulture)?.Trim() ?? string.Empty;
            text = text.Replace("{" + c.ColumnName + "}", value, StringComparison.OrdinalIgnoreCase);
        }

        return text.Contains('{', StringComparison.Ordinal) ? null : text;
    }
}
