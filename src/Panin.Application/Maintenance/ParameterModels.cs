using System.Globalization;
using System.Text.RegularExpressions;
using Panin.Application.Collection;

namespace Panin.Application.Maintenance;

/// <summary>Satu entri daftar parameter (<c>PARAMETERSYSTEM</c>): judul dan tautan lama, mis. <c>ParamMaker.aspx?TBLNM=RFPURPOSE&amp;Moduleid=60</c>.</summary>
public sealed record ParameterSetItem(string Title, string Link)
{
    /// <summary>Nama berkas tujuan pada tautan lama (huruf kecil), mis. <c>parammaker.aspx</c>.</summary>
    public string File => Link.Split('?')[0].Split('/')[^1].ToLowerInvariant();

    public string? Table => Query("TBLNM");

    public string? ModuleId => Query("Moduleid");

    private string? Query(string key)
    {
        var at = Link.IndexOf('?', StringComparison.Ordinal);
        if (at < 0)
        {
            return null;
        }

        foreach (var part in Link[(at + 1)..].Split('&'))
        {
            var kv = part.Split('=', 2);
            if (kv.Length == 2 && kv[0].Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                return kv[1];
            }
        }

        return null;
    }
}

public enum ParameterKind
{
    Text,
    Integer,
    Decimal,
    Boolean,
    DateTime,

    /// <summary>Tipe kolom yang belum didukung editor generik (mis. uniqueidentifier, binary); kolom ini tidak bisa diubah.</summary>
    Unsupported,
}

/// <summary>Satu kolom tabel parameter: metadata dari <c>PARAMETERSYSTEMFIELD</c> digabung dengan tipe sebenarnya dari <c>sys.columns</c>.</summary>
public sealed record ParameterField(
    string Name,
    string Label,
    bool IsKey,
    bool IsMandatory,
    string? LookupSql,
    ParameterKind Kind,
    int MaxLength,
    bool IsNullable,
    bool IsIdentity);

public sealed record ParameterTable(string ModuleId, string SetId, string Table, string Title, IReadOnlyList<ParameterField> Fields, string? ReadOnlyReason)
{
    public bool CanEdit => ReadOnlyReason is null;

    public IEnumerable<ParameterField> Keys => Fields.Where(f => f.IsKey);

    public ParameterField? Field(string name) => Fields.FirstOrDefault(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
}

public sealed record ParameterRow(IReadOnlyDictionary<string, string?> Values)
{
    public string? Get(string field) => Values.GetValueOrDefault(field);
}

public sealed record ParameterRows(IReadOnlyList<ParameterRow> Rows, int Total, int MaxRows)
{
    public bool Truncated => Total > Rows.Count;
}

public sealed record PendingParameterRow(Guid TemporaryId, string Status, string? CreatedBy, IReadOnlyDictionary<string, string?> Values, string? Changes)
{
    public string? Get(string field) => Values.GetValueOrDefault(field);
}

/// <summary>Isian form: nilai berupa teks (bit "1"/"0", tanggal "yyyy-MM-dd HH:mm:ss", angka invariant).</summary>
public sealed class ParameterDraft
{
    public Dictionary<string, string?> Values { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool IsEdit { get; set; }
}

/// <summary>Pelaku beserta group-nya; hak Maker/Approver diperiksa lagi di service, bukan hanya di halaman.</summary>
public sealed record ParameterActor(string UserId, string GroupId);

public static partial class ParameterRules
{
    public const int MaxRows = 2000;

    /// <summary>
    /// Tabel yang tidak boleh diubah lewat editor generik walau tercantum di metadata: mengubahnya sama dengan mengubah
    /// siapa yang berwenang atau apa yang bisa diubah. Ubah lewat jalur khusus (User/Group) atau oleh administrator DB.
    /// </summary>
    private static readonly HashSet<string> Blocked = new(StringComparer.OrdinalIgnoreCase)
    {
        "PARAMETERSYSTEM", "PARAMETERSYSTEMFIELD", "PARAMETERSYSTEM_TEMPORARY", "PARAMETERSYSTEM_TEMPORARYDETAIL",
        "AUDITTRAIL", "AUDITTRAIL_DETAIL", "SCALLUSER", "SCALLGROUP", "SCALLUSERFLAG", "SCUSER", "SCGROUP", "GRPMENUX", "RFMENUX",
        "RFMODULE", "LOGINPARAM", "ESAPPTOKEN", "TEMP_DOWNLINE",
    };

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_]{0,63}$")]
    private static partial Regex IdentifierPattern();

    // Hanya "SELECT kolom[, kolom] FROM tabel [WHERE ...] [ORDER BY ...]" tanpa titik koma, komentar, atau perintah lain.
    [GeneratedRegex(@"^\s*SELECT\s+[\w\[\]\., ]+\s+FROM\s+[\w\[\]\.]+(\s+WHERE\s+[^;]+?)?(\s+ORDER\s+BY\s+[\w\[\]\., ]+)?\s*$", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex LookupPattern();

    [GeneratedRegex(@"--|/\*|\b(EXEC|EXECUTE|INSERT|UPDATE|DELETE|DROP|ALTER|CREATE|TRUNCATE|MERGE|GRANT|REVOKE|SHUTDOWN|OPENROWSET|OPENQUERY|XP_\w+|SP_\w+)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ForbiddenPattern();

    public static bool IsValidIdentifier(string? name) => name is not null && IdentifierPattern().IsMatch(name);

    public static bool IsBlocked(string table) => Blocked.Contains(table);

    /// <summary>Teks SQL di <c>FIELDREFF</c> dijalankan aplikasi untuk mengisi dropdown; hanya SELECT sederhana yang diizinkan.</summary>
    public static bool IsSafeLookup(string? sql) => !string.IsNullOrWhiteSpace(sql) && LookupPattern().IsMatch(sql) && !ForbiddenPattern().IsMatch(sql);

    public static ParameterKind KindOf(string sqlType) => sqlType.ToLowerInvariant() switch
    {
        "char" or "varchar" or "nchar" or "nvarchar" or "text" or "ntext" => ParameterKind.Text,
        "tinyint" or "smallint" or "int" or "bigint" => ParameterKind.Integer,
        "decimal" or "numeric" or "money" or "smallmoney" or "float" or "real" => ParameterKind.Decimal,
        "bit" => ParameterKind.Boolean,
        "date" or "datetime" or "smalldatetime" or "datetime2" => ParameterKind.DateTime,
        _ => ParameterKind.Unsupported,
    };

    /// <summary>Mengubah teks isian menjadi nilai bertipe untuk parameter SQL; <c>false</c> bila format tidak sah.</summary>
    public static bool TryConvert(ParameterField field, string? text, out object? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        var t = text.Trim();
        switch (field.Kind)
        {
            case ParameterKind.Text:
                value = t;
                return true;
            case ParameterKind.Integer:
                if (long.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l))
                {
                    value = l;
                    return true;
                }

                return false;
            case ParameterKind.Decimal:
                if (decimal.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                {
                    value = d;
                    return true;
                }

                return false;
            case ParameterKind.Boolean:
                if (t is "1" or "0")
                {
                    value = t == "1";
                    return true;
                }

                if (bool.TryParse(t, out var b))
                {
                    value = b;
                    return true;
                }

                return false;
            case ParameterKind.DateTime:
                if (DateTime.TryParse(t, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                {
                    value = dt;
                    return true;
                }

                return false;
            default:
                return false;
        }
    }

    public static IReadOnlyList<string> Validate(ParameterTable table, IReadOnlyDictionary<string, string?> values)
    {
        var errors = new List<string>();
        if (!table.CanEdit)
        {
            errors.Add(table.ReadOnlyReason!);
            return errors;
        }

        foreach (var f in table.Fields.Where(f => f.Kind != ParameterKind.Unsupported))
        {
            var text = values.GetValueOrDefault(f.Name);
            var empty = string.IsNullOrWhiteSpace(text);
            // Kolom bit selalu punya nilai (tidak dicentang = 0), jadi tidak pernah "kosong".
            if (empty && f.Kind != ParameterKind.Boolean && (f.IsKey || f.IsMandatory))
            {
                errors.Add($"{f.Label} wajib diisi");
                continue;
            }

            if (empty)
            {
                continue;
            }

            if (!TryConvert(f, text, out _))
            {
                errors.Add($"{f.Label} tidak valid");
            }
            else if (f.Kind == ParameterKind.Text && f.MaxLength > 0 && text!.Trim().Length > f.MaxLength)
            {
                errors.Add($"{f.Label} maksimal {f.MaxLength} karakter");
            }
        }

        return errors;
    }

    /// <summary>Teks seragam yang disimpan di <c>PARAMETERSYSTEM_TEMPORARYDETAIL</c> (sama dengan yang dihasilkan layar lama).</summary>
    public static string? ToText(ParameterKind kind, object? value) => value switch
    {
        null or DBNull => null,
        bool b => b ? "1" : "0",
        DateTime dt => dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        decimal m => m.ToString(CultureInfo.InvariantCulture),
        double db => db.ToString(CultureInfo.InvariantCulture),
        float f => f.ToString(CultureInfo.InvariantCulture),
        IFormattable fm => fm.ToString(null, CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim(),
    };
}

public interface IParameterService
{
    /// <summary>Tabel yang bisa dikelola di set ini (daftar di <c>PARAMETERSYSTEM</c> DB yang menampung set tersebut).</summary>
    Task<IReadOnlyList<ParameterSetItem>> GetItemsAsync(string setId, bool maker, CancellationToken ct = default);

    Task<bool> HasRoleAsync(string groupId, MaintenanceRole role, string setId, CancellationToken ct = default);

    /// <summary>Metadata tabel (kolom, kunci, lookup); <c>null</c> bila tabel tidak terdaftar atau tidak boleh dikelola.</summary>
    Task<ParameterTable?> GetTableAsync(string setId, string moduleId, string table, CancellationToken ct = default);

    Task<IReadOnlyList<LookupItem>> GetLookupAsync(ParameterTable table, string fieldName, CancellationToken ct = default);

    Task<ParameterRows> GetRowsAsync(ParameterTable table, CancellationToken ct = default);

    Task<IReadOnlyList<PendingParameterRow>> GetPendingAsync(ParameterTable table, CancellationToken ct = default);

    Task<MaintenanceResult> SavePendingAsync(ParameterActor actor, ParameterTable table, ParameterDraft draft, CancellationToken ct = default);

    Task<MaintenanceResult> DeleteToPendingAsync(ParameterActor actor, ParameterTable table, IReadOnlyDictionary<string, string?> keyValues, CancellationToken ct = default);

    Task<MaintenanceResult> CancelPendingAsync(ParameterActor actor, ParameterTable table, Guid temporaryId, CancellationToken ct = default);

    Task<MaintenanceResult> ApproveAsync(ParameterActor actor, ParameterTable table, IReadOnlyList<Guid> temporaryIds, CancellationToken ct = default);

    Task<MaintenanceResult> RejectAsync(ParameterActor actor, ParameterTable table, IReadOnlyList<Guid> temporaryIds, CancellationToken ct = default);
}
