using System.Globalization;
using System.Text;
using Panin.Application.Abstractions;
using Panin.Application.Maintenance;
using Panin.Application.Modules;

namespace Panin.Infrastructure.Maintenance;

/// <summary>
/// Mesin maker-checker bersama (tabel <c>PARAMETERSYSTEM_TEMPORARY</c> + <c>...DETAIL</c>, sama dengan aplikasi lama) untuk
/// User, Group, dan parameter lain. Semua nilai dikirim sebagai parameter SQL; daftar kolom selalu dari kode, bukan dari browser.
/// </summary>
internal static class ParameterEngine
{
    public const string ModuleId = "99";
    private const string MakerMenuId = "201010";
    private const string ApproverMenuId = "201020";

    public static async Task<bool> HasRoleAsync(IMenuService menus, string groupId, MaintenanceRole role, CancellationToken ct)
    {
        var wanted = role == MaintenanceRole.Maker ? MakerMenuId : ApproverMenuId;
        var tree = await menus.GetMenuAsync(ModuleId, groupId, ct).ConfigureAwait(false);
        return Contains(tree, wanted);

        static bool Contains(IReadOnlyList<MenuNode> nodes, string id)
            => nodes.Any(n => n.Id == id || Contains(n.Children, id));
    }

    /// <summary>
    /// Mengganti ajuan yang sudah ada untuk kunci yang sama (satu ajuan per objek), atau membuat baru, dalam satu transaksi.
    /// Nilai "sebelum" disimpan untuk pembanding di layar approval.
    /// </summary>
    public static async Task WritePendingAsync(
        IDb db, string table, string keyField, string keyValue, string status, string maker, IReadOnlyList<string> fields,
        IReadOnlyDictionary<string, string?> after, IReadOnlyDictionary<string, string?>? before, CancellationToken ct)
    {
        var existingId = await db.ExecuteScalarAsync<Guid?>(
            "SELECT TOP 1 T.TEMPORARYID FROM PARAMETERSYSTEM_TEMPORARY T JOIN PARAMETERSYSTEM_TEMPORARYDETAIL D ON D.TEMPORARYID = T.TEMPORARYID AND D.FIELDNAME = @3 AND D.FIELDVALUE = @1 WHERE T.TABLENAME = @2",
            [keyValue, table, keyField], ct: ct).ConfigureAwait(false);
        var id = existingId ?? Guid.NewGuid();

        var args = new List<object?> { id, table, status, maker };
        var sql = new StringBuilder("SET XACT_ABORT ON; BEGIN TRAN; ");
        sql.Append("DELETE PARAMETERSYSTEM_TEMPORARYDETAIL WHERE TEMPORARYID = @1; DELETE PARAMETERSYSTEM_TEMPORARY WHERE TEMPORARYID = @1; ");
        sql.Append("INSERT INTO PARAMETERSYSTEM_TEMPORARY (TemporaryID, TableName, Status, CreatedDate, CreatedBy) VALUES (@1, @2, @3, GETDATE(), @4); ");
        foreach (var field in fields)
        {
            args.Add(field);
            args.Add(after.GetValueOrDefault(field));
            args.Add(before?.GetValueOrDefault(field));
            var n = args.Count;
            sql.Append(CultureInfo.InvariantCulture, $"INSERT INTO PARAMETERSYSTEM_TEMPORARYDETAIL (TemporaryID, FieldName, FieldValue, FieldValueBefore) VALUES (@1, @{n - 2}, @{n - 1}, @{n}); ");
        }

        sql.Append("COMMIT TRAN");
        await db.ExecuteAsync(sql.ToString(), [.. args], new DbOptions(TimeoutSeconds: 60), ct).ConfigureAwait(false);
    }

    public static Task DeletePendingAsync(IDb db, Guid id, CancellationToken ct)
        => db.ExecuteAsync("SET XACT_ABORT ON; BEGIN TRAN; DELETE PARAMETERSYSTEM_TEMPORARYDETAIL WHERE TEMPORARYID = @1; DELETE PARAMETERSYSTEM_TEMPORARY WHERE TEMPORARYID = @1; COMMIT TRAN", [id], ct: ct);

    public static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static string Cell(object? value) => value is null or DBNull ? string.Empty : Convert.ToString(value, CultureInfo.InvariantCulture)!.Trim();

    // bit dari DB -> "1"/"0" (sama dengan yang disimpan layar lama); lainnya teks apa adanya.
    public static string? ToText(object? value) => value switch
    {
        null or DBNull => null,
        bool b => b ? "1" : "0",
        _ => Blank(Convert.ToString(value, CultureInfo.InvariantCulture)),
    };
}
