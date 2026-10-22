using System.Text.RegularExpressions;

namespace Panin.Application.Reports;

/// <summary>
/// SQL pilihan filter (<c>SELECT kode, teks FROM ... [WHERE ... =@n]</c>) tersimpan sebagai teks di <c>PIVOTLIST.PV_FILTERLIST</c>.
/// Layar lama menjalankannya apa adanya; di sini hanya SELECT sederhana yang dijalankan. Yang lain ditolak, dan report-nya ditandai belum tersedia.
/// </summary>
public static partial class ReportOptionSql
{
    /// <returns>Pesan alasan penolakan, atau <c>null</c> bila aman.</returns>
    public static string? Validate(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return "SQL pilihan kosong";
        }

        var text = sql.Trim();
        if (!SelectStart().IsMatch(text))
        {
            return "SQL pilihan harus diawali SELECT";
        }

        if (text.Contains(';', StringComparison.Ordinal) || text.Contains("--", StringComparison.Ordinal)
            || text.Contains("/*", StringComparison.Ordinal) || text.Contains("*/", StringComparison.Ordinal))
        {
            return "SQL pilihan memuat pemisah atau komentar";
        }

        if (!FromClause().IsMatch(text))
        {
            return "SQL pilihan harus memiliki FROM";
        }

        if (Forbidden().Match(text) is { Success: true } m)
        {
            return $"SQL pilihan memuat kata terlarang {m.Value}";
        }

        return null;
    }

    /// <summary>Posisi parameter (<c>@n</c>) yang dirujuk SQL, tanpa duplikat, urut naik.</summary>
    public static IReadOnlyList<int> References(string sql)
        => [.. ParamRef().Matches(sql).Select(m => int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)).Distinct().Order()];

    [GeneratedRegex(@"^SELECT\s", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SelectStart();

    [GeneratedRegex(@"\sFROM\s", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FromClause();

    [GeneratedRegex(@"\b(EXEC|EXECUTE|INSERT|UPDATE|DELETE|DROP|ALTER|CREATE|TRUNCATE|MERGE|INTO|GRANT|REVOKE|DENY|OPENROWSET|OPENQUERY|OPENDATASOURCE|WAITFOR|BULK|SHUTDOWN|DBCC|USE|XP_\w*|SP_\w*|SYS\.\w+|MASTER|INFORMATION_SCHEMA)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Forbidden();

    [GeneratedRegex(@"@(\d+)")]
    private static partial Regex ParamRef();
}
