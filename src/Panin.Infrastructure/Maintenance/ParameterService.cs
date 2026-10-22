using System.Data;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Panin.Application.Abstractions;
using Panin.Application.Collection;
using Panin.Application.Maintenance;
using Panin.Application.Modules;
using Panin.Infrastructure.Data;

namespace Panin.Infrastructure.Maintenance;

/// <summary>
/// Editor parameter generik berbasis metadata (<c>ParamSet/ParamMaker/ParamApprv.aspx</c> lama): General, Process, dan Group Treatment.
/// Daftar tabel dari <c>PARAMETERSYSTEM</c>, kolom dari <c>PARAMETERSYSTEMFIELD</c> digabung tipe asli dari <c>sys.columns</c>.
/// Dibanding layar lama: nama tabel/kolom hanya dipakai bila ada di metadata <b>dan</b> di skema DB (lalu diberi tanda kurung siku),
/// nilai selalu parameter bertipe (bukan teks SQL), SQL dropdown (<c>FIELDREFF</c>) hanya dijalankan bila berupa SELECT sederhana,
/// tabel keamanan/metadata tidak bisa diedit, tabel tanpa kunci hanya-baca, dan persetujuan satu transaksi dengan <c>SP_AUDITTRAIL</c>.
/// </summary>
public sealed partial class ParameterService(
    IModuleDbFactory dbs,
    IMenuService menus,
    SingleFlightCache cache,
    IOptions<MaintenanceOptions> options,
    ILogger<ParameterService> logger) : IParameterService
{
    private static readonly TimeSpan MetaCache = TimeSpan.FromMinutes(5);

    /// <summary>Tempat daftar parameter sebuah set: USER ada di DB modul 99 (login), GEN/PROC di DB modul 60 (data).</summary>
    private static string ListModule(string setId) => setId.Equals("USER", StringComparison.OrdinalIgnoreCase) ? "99" : "60";

    public Task<bool> HasRoleAsync(string groupId, MaintenanceRole role, string setId, CancellationToken ct = default)
        => ParameterEngine.HasRoleAsync(menus, groupId, role, ct, setId);

    public async Task<IReadOnlyList<ParameterSetItem>> GetItemsAsync(string setId, bool maker, CancellationToken ct = default)
    {
        var db = await dbs.GetDataDbAsync(ListModule(setId), ct).ConfigureAwait(false);
        var column = maker ? "ISMAKER" : "ISAPPRV";
        var link = maker ? "PARAMLINKMAKER" : "PARAMLINKAPPRV";
        var table = await db.QueryTableAsync($"SELECT PARAMDESC, {link} FROM PARAMETERSYSTEM WHERE SETID = @1 AND {column} = 1 ORDER BY PARAMDESC", [setId], ct: ct).ConfigureAwait(false);
        return [.. table.Rows.Cast<DataRow>().Select(r => new ParameterSetItem(ParameterEngine.Cell(r[0]), ParameterEngine.Cell(r[1])))];
    }

    public async Task<ParameterTable?> GetTableAsync(string setId, string moduleId, string table, CancellationToken ct = default)
    {
        if (!ParameterRules.IsValidIdentifier(table) || ParameterRules.IsBlocked(table) || moduleId is not ("60" or "99"))
        {
            return null;
        }

        return await cache.GetOrCreateAsync($"param-meta:{setId}:{moduleId}:{table}", MetaCache, async () =>
        {
            // 1. Tabel harus terdaftar di set ini (daftar yang sama dengan menu); judulnya diambil dari sana.
            var listDb = await dbs.GetDataDbAsync(ListModule(setId)).ConfigureAwait(false);
            var title = await listDb.ExecuteScalarAsync<string>(
                "SELECT TOP 1 PARAMDESC FROM PARAMETERSYSTEM WHERE SETID = @1 AND (ISMAKER = 1 OR ISAPPRV = 1) AND CHARINDEX('TBLNM=' + @2 + '&', UPPER(PARAMLINKMAKER)) > 0",
                [setId, table.ToUpperInvariant()]).ConfigureAwait(false);
            if (title is null)
            {
                return null;
            }

            // 2. Metadata kolom (DB sasaran) digabung skema asli tabel.
            var db = await dbs.GetDataDbAsync(moduleId).ConfigureAwait(false);
            var meta = await db.QueryTableAsync(
                "SELECT FIELDNM, FIELDDESC, FIELDREFF, FIELDKEY, FIELDMAN FROM PARAMETERSYSTEMFIELD WHERE TABLENM = @1 ORDER BY FIELDPOS", [table]).ConfigureAwait(false);
            var schema = await db.QueryTableAsync(
                "SELECT c.name, t.name AS type_name, c.max_length, c.is_nullable, c.is_identity FROM sys.columns c JOIN sys.types t ON t.user_type_id = c.user_type_id WHERE c.object_id = OBJECT_ID(@1)",
                [$"[dbo].[{table}]"]).ConfigureAwait(false);
            var columns = schema.Rows.Cast<DataRow>().ToDictionary(r => ParameterEngine.Cell(r[0]), r => r, StringComparer.OrdinalIgnoreCase);
            if (columns.Count == 0)
            {
                return null;
            }

            var fields = new List<ParameterField>();
            foreach (var m in meta.Rows.Cast<DataRow>())
            {
                var name = ParameterEngine.Cell(m[0]);
                if (!ParameterRules.IsValidIdentifier(name) || !columns.TryGetValue(name, out var col))
                {
                    continue; // metadata kedaluwarsa: kolom sudah tidak ada
                }

                var kind = ParameterRules.KindOf(ParameterEngine.Cell(col[1]));
                var maxLen = Convert.ToInt32(col[2], CultureInfo.InvariantCulture);
                var type = ParameterEngine.Cell(col[1]).ToLowerInvariant();
                if (type.StartsWith('n') && kind == ParameterKind.Text)
                {
                    maxLen /= 2; // nchar/nvarchar: max_length dalam byte
                }

                var lookup = ParameterEngine.Blank(ParameterEngine.Cell(m[2]));
                fields.Add(new ParameterField(
                    name,
                    ParameterEngine.Blank(ParameterEngine.Cell(m[1])) ?? name,
                    m[3] is not DBNull && Convert.ToBoolean(m[3], CultureInfo.InvariantCulture),
                    m[4] is not DBNull && Convert.ToBoolean(m[4], CultureInfo.InvariantCulture),
                    ParameterRules.IsSafeLookup(lookup) ? lookup : null,
                    kind,
                    maxLen < 0 ? 0 : maxLen,
                    (bool)col[3],
                    (bool)col[4]));
            }

            var reason = ReadOnlyReason(fields);
            return new ParameterTable(moduleId, setId, table, title, fields, reason);
        }).ConfigureAwait(false);
    }

    // Tanpa kunci yang jelas, "ubah" tidak bisa menunjuk satu baris; kunci identity/tipe tak didukung tidak bisa diisi dari form.
    private static string? ReadOnlyReason(List<ParameterField> fields)
    {
        if (fields.Count == 0)
        {
            return "Metadata kolom tabel ini kosong; tabel hanya dapat dilihat.";
        }

        var keys = fields.Where(f => f.IsKey).ToList();
        if (keys.Count == 0)
        {
            return "Tabel ini belum punya kolom kunci di metadata sehingga tidak dapat diubah dari sini (hanya dilihat).";
        }

        if (keys.Any(k => k.Kind == ParameterKind.Unsupported || k.IsIdentity))
        {
            return "Kunci tabel ini bertipe otomatis/tidak didukung sehingga tidak dapat diubah dari sini (hanya dilihat).";
        }

        return null;
    }

    public async Task<IReadOnlyList<LookupItem>> GetLookupAsync(ParameterTable table, string fieldName, CancellationToken ct = default)
    {
        var field = table.Field(fieldName);
        if (field?.LookupSql is not { } sql)
        {
            return [];
        }

        return await cache.GetOrCreateAsync($"param-lookup:{table.ModuleId}:{table.Table}:{field.Name}", MetaCache, async () =>
        {
            var db = await dbs.GetDataDbAsync(table.ModuleId).ConfigureAwait(false);
            var result = await db.QueryTableAsync(sql, options: new DbOptions(TimeoutSeconds: 30)).ConfigureAwait(false);
            return (IReadOnlyList<LookupItem>?)[.. result.Rows.Cast<DataRow>().Take(ParameterRules.MaxRows)
                .Select(r => new LookupItem(ParameterEngine.Cell(r[0]), result.Columns.Count > 1 ? ParameterEngine.Cell(r[1]) : ParameterEngine.Cell(r[0])))];
        }).ConfigureAwait(false) ?? [];
    }

    public async Task<ParameterRows> GetRowsAsync(ParameterTable table, CancellationToken ct = default)
    {
        var db = await dbs.GetDataDbAsync(table.ModuleId, ct).ConfigureAwait(false);
        var cols = string.Join(", ", table.Fields.Select(f => Q(f.Name)));
        var order = string.Join(", ", (table.CanEdit ? table.Keys : table.Fields.Take(1)).Select(f => Q(f.Name)));
        var options = new DbOptions(TimeoutSeconds: 60);
        var data = db.QueryTableAsync($"SELECT TOP ({ParameterRules.MaxRows}) {cols} FROM {Q(table.Table)} ORDER BY {order}", options: options, ct: ct);
        var total = db.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {Q(table.Table)}", options: options, ct: ct);
        await Task.WhenAll(data, total).ConfigureAwait(false);
        var rows = data.Result.Rows.Cast<DataRow>()
            .Select(r => new ParameterRow(table.Fields.ToDictionary(f => f.Name, f => ParameterRules.ToText(f.Kind, r[f.Name]), StringComparer.OrdinalIgnoreCase)))
            .ToList();
        return new ParameterRows(rows, total.Result, ParameterRules.MaxRows);
    }

    public async Task<IReadOnlyList<PendingParameterRow>> GetPendingAsync(ParameterTable table, CancellationToken ct = default)
    {
        var db = await dbs.GetDataDbAsync(table.ModuleId, ct).ConfigureAwait(false);
        var data = await db.QueryTableAsync(
            "SELECT T.TemporaryID, T.Status, T.CreatedBy, T.CreatedDate, D.FieldName, D.FieldValue, D.FieldValueBefore FROM PARAMETERSYSTEM_TEMPORARY T " +
            "JOIN PARAMETERSYSTEM_TEMPORARYDETAIL D ON D.TemporaryID = T.TemporaryID WHERE T.TableName = @1 ORDER BY T.CreatedDate, T.TemporaryID", [table.Table], ct: ct).ConfigureAwait(false);
        var known = table.Fields.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = new List<PendingParameterRow>();
        foreach (var g in data.Rows.Cast<DataRow>().GroupBy(r => (Guid)r[0]))
        {
            var first = g.First();
            var status = ParameterEngine.Cell(first[1]);
            var values = g.Where(r => known.Contains(ParameterEngine.Cell(r[4])))
                .ToDictionary(r => ParameterEngine.Cell(r[4]), r => r[5] is DBNull ? null : Convert.ToString(r[5], CultureInfo.InvariantCulture), StringComparer.OrdinalIgnoreCase);
            var changes = status == "Update"
                ? string.Join("; ", g.Where(r => known.Contains(ParameterEngine.Cell(r[4])) && ParameterEngine.Cell(r[5]) != ParameterEngine.Cell(r[6]))
                    .Select(r => $"{table.Field(ParameterEngine.Cell(r[4]))!.Label}: {Show(r[6])} → {Show(r[5])}"))
                : null;
            result.Add(new PendingParameterRow(g.Key, status, ParameterEngine.Cell(first[2]), values, changes));
        }

        return result;

        static string Show(object? v) => v is null or DBNull || ParameterEngine.Cell(v).Length == 0 ? "(kosong)" : ParameterEngine.Cell(v);
    }

    public async Task<MaintenanceResult> SavePendingAsync(ParameterActor actor, ParameterTable table, ParameterDraft draft, CancellationToken ct = default)
    {
        if (!await HasRoleAsync(actor.GroupId, MaintenanceRole.Maker, table.SetId, ct).ConfigureAwait(false))
        {
            return MaintenanceResult.Fail("Group Anda tidak punya hak Maker untuk parameter ini");
        }

        var errors = ParameterRules.Validate(table, draft.Values);
        if (errors.Count > 0)
        {
            return new MaintenanceResult(false, errors);
        }

        var editable = table.Fields.Where(f => f.Kind != ParameterKind.Unsupported).ToList();
        var after = editable.ToDictionary(f => f.Name, f => Normalize(f, draft.Values.GetValueOrDefault(f.Name)), StringComparer.OrdinalIgnoreCase);
        var keys = table.Keys.Select(k => new KeyValuePair<string, string?>(k.Name, after[k.Name])).ToList();

        var db = await dbs.GetDataDbAsync(table.ModuleId, ct).ConfigureAwait(false);
        var existing = await ReadRowAsync(db, table, keys, ct).ConfigureAwait(false);
        if (!draft.IsEdit && existing is not null)
        {
            return MaintenanceResult.Fail("Data dengan kunci tersebut sudah ada");
        }

        if (draft.IsEdit && existing is null)
        {
            return MaintenanceResult.Fail("Data tidak ditemukan");
        }

        var before = existing?.Values.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
        try
        {
            await ParameterEngine.WritePendingAsync(db, table.Table, keys, existing is null ? "Insert" : "Update", actor.UserId,
                [.. editable.Select(f => f.Name)], after, before, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFailed(logger, ex, "simpan ajuan", actor.UserId, table.Table);
            return MaintenanceResult.Fail($"Ajuan gagal disimpan. Coba lagi atau hubungi administrator. {Panin.Application.Diagnostics.ErrorReference.Tag}");
        }

        LogPending(logger, actor.UserId, existing is null ? "Insert" : "Update", table.Table);
        return MaintenanceResult.Ok;
    }

    public async Task<MaintenanceResult> DeleteToPendingAsync(ParameterActor actor, ParameterTable table, IReadOnlyDictionary<string, string?> keyValues, CancellationToken ct = default)
    {
        if (!await HasRoleAsync(actor.GroupId, MaintenanceRole.Maker, table.SetId, ct).ConfigureAwait(false))
        {
            return MaintenanceResult.Fail("Group Anda tidak punya hak Maker untuk parameter ini");
        }

        if (!table.CanEdit)
        {
            return MaintenanceResult.Fail(table.ReadOnlyReason!);
        }

        var keys = table.Keys.Select(k => new KeyValuePair<string, string?>(k.Name, ParameterEngine.Blank(keyValues.GetValueOrDefault(k.Name)))).ToList();
        if (keys.Any(k => k.Value is null))
        {
            return MaintenanceResult.Fail("Kunci tidak lengkap");
        }

        var db = await dbs.GetDataDbAsync(table.ModuleId, ct).ConfigureAwait(false);
        var existing = await ReadRowAsync(db, table, keys, ct).ConfigureAwait(false);
        if (existing is null)
        {
            return MaintenanceResult.Fail("Data tidak ditemukan");
        }

        var values = existing.Values;
        try
        {
            await ParameterEngine.WritePendingAsync(db, table.Table, keys, "Delete", actor.UserId,
                [.. table.Fields.Where(f => f.Kind != ParameterKind.Unsupported).Select(f => f.Name)], values, values, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFailed(logger, ex, "ajukan hapus", actor.UserId, table.Table);
            return MaintenanceResult.Fail($"Ajuan gagal disimpan. Coba lagi atau hubungi administrator. {Panin.Application.Diagnostics.ErrorReference.Tag}");
        }

        LogPending(logger, actor.UserId, "Delete", table.Table);
        return MaintenanceResult.Ok;
    }

    public async Task<MaintenanceResult> CancelPendingAsync(ParameterActor actor, ParameterTable table, Guid temporaryId, CancellationToken ct = default)
    {
        var db = await dbs.GetDataDbAsync(table.ModuleId, ct).ConfigureAwait(false);
        var owner = await db.ExecuteScalarAsync<string>("SELECT CREATEDBY FROM PARAMETERSYSTEM_TEMPORARY WHERE TEMPORARYID = @1 AND TABLENAME = @2", [temporaryId, table.Table], ct: ct).ConfigureAwait(false);
        if (owner is null)
        {
            return MaintenanceResult.Fail("Ajuan tidak ditemukan");
        }

        if (!string.Equals(owner, actor.UserId, StringComparison.OrdinalIgnoreCase))
        {
            return MaintenanceResult.Fail("Hanya pembuat ajuan yang dapat membatalkannya");
        }

        await ParameterEngine.DeletePendingAsync(db, temporaryId, ct).ConfigureAwait(false);
        return MaintenanceResult.Ok;
    }

    public async Task<MaintenanceResult> RejectAsync(ParameterActor actor, ParameterTable table, IReadOnlyList<Guid> temporaryIds, CancellationToken ct = default)
    {
        if (!await HasRoleAsync(actor.GroupId, MaintenanceRole.Approver, table.SetId, ct).ConfigureAwait(false))
        {
            return MaintenanceResult.Fail("Group Anda tidak punya hak Approval untuk parameter ini");
        }

        var db = await dbs.GetDataDbAsync(table.ModuleId, ct).ConfigureAwait(false);
        foreach (var id in temporaryIds.Distinct())
        {
            var t = await db.ExecuteScalarAsync<string>("SELECT TABLENAME FROM PARAMETERSYSTEM_TEMPORARY WHERE TEMPORARYID = @1", [id], ct: ct).ConfigureAwait(false);
            if (string.Equals(t, table.Table, StringComparison.OrdinalIgnoreCase))
            {
                await ParameterEngine.DeletePendingAsync(db, id, ct).ConfigureAwait(false);
                LogDecision(logger, actor.UserId, "tolak", id, table.Table);
            }
        }

        return MaintenanceResult.Ok;
    }

    public async Task<MaintenanceResult> ApproveAsync(ParameterActor actor, ParameterTable table, IReadOnlyList<Guid> temporaryIds, CancellationToken ct = default)
    {
        if (!await HasRoleAsync(actor.GroupId, MaintenanceRole.Approver, table.SetId, ct).ConfigureAwait(false))
        {
            return MaintenanceResult.Fail("Group Anda tidak punya hak Approval untuk parameter ini");
        }

        var db = await dbs.GetDataDbAsync(table.ModuleId, ct).ConfigureAwait(false);
        var errors = new List<string>();
        foreach (var id in temporaryIds.Distinct())
        {
            try
            {
                var error = await ApproveOneAsync(db, actor, table, id, ct).ConfigureAwait(false);
                if (error is not null)
                {
                    errors.Add(error);
                }
                else
                {
                    LogDecision(logger, actor.UserId, "setuju", id, table.Table);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFailed(logger, ex, "setujui", actor.UserId, $"{table.Table}/{id}");
                errors.Add($"Ajuan {id} gagal diproses; tidak ada perubahan yang tercatat untuk ajuan itu. {Panin.Application.Diagnostics.ErrorReference.Tag}");
            }
        }

        return errors.Count == 0 ? MaintenanceResult.Ok : new MaintenanceResult(false, errors);
    }

    private async Task<string?> ApproveOneAsync(IDb db, ParameterActor actor, ParameterTable table, Guid id, CancellationToken ct)
    {
        var header = await db.QueryFirstOrDefaultAsync("SELECT STATUS, CREATEDBY, TABLENAME FROM PARAMETERSYSTEM_TEMPORARY WHERE TEMPORARYID = @1", [id], ct: ct).ConfigureAwait(false);
        if (header is null)
        {
            return "Ajuan sudah tidak ada (mungkin sudah diproses)";
        }

        if (!string.Equals(ParameterEngine.Cell(header["TABLENAME"]), table.Table, StringComparison.OrdinalIgnoreCase))
        {
            return "Ajuan bukan untuk tabel ini";
        }

        if (!table.CanEdit)
        {
            return table.ReadOnlyReason;
        }

        var status = ParameterEngine.Cell(header["STATUS"]);
        if (options.Value.RequireDifferentApprover && string.Equals(ParameterEngine.Cell(header["CREATEDBY"]), actor.UserId, StringComparison.OrdinalIgnoreCase))
        {
            return "Ajuan Anda sendiri tidak dapat Anda setujui (maker dan approver harus berbeda)";
        }

        var details = await db.QueryTableAsync("SELECT FIELDNAME, FIELDVALUE FROM PARAMETERSYSTEM_TEMPORARYDETAIL WHERE TEMPORARYID = @1", [id], ct: ct).ConfigureAwait(false);
        var text = details.Rows.Cast<DataRow>().ToDictionary(r => ParameterEngine.Cell(r[0]), r => r[1] is DBNull ? null : Convert.ToString(r[1], CultureInfo.InvariantCulture), StringComparer.OrdinalIgnoreCase);

        // Konversi nilai ke tipe kolom; ajuan yang tidak bisa dikonversi ditolak sebelum menyentuh data.
        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in table.Fields.Where(f => f.Kind != ParameterKind.Unsupported))
        {
            if (!text.TryGetValue(f.Name, out var raw))
            {
                continue;
            }

            if (!ParameterRules.TryConvert(f, raw, out var typed))
            {
                return $"Ajuan rusak: nilai {f.Label} tidak valid";
            }

            values[f.Name] = typed;
        }

        var keyFields = table.Keys.ToList();
        if (keyFields.Any(k => !values.TryGetValue(k.Name, out var v) || v is null))
        {
            return "Ajuan rusak: kunci kosong";
        }

        var (sql, args) = status switch
        {
            "Insert" or "Update" => BuildUpsert(table, keyFields, values, id, actor.UserId),
            "Delete" => BuildDelete(table, keyFields, values, id, actor.UserId),
            _ => (null, []),
        };
        if (sql is null)
        {
            return $"Status ajuan tidak dikenal: {status}";
        }

        await db.ExecuteAsync(sql, args, new DbOptions(TimeoutSeconds: 120), ct).ConfigureAwait(false);
        return null;
    }

    /// <summary>UPDATE lalu INSERT bila tidak ada baris (sama dengan <c>staticFramework.save</c> lama), plus audit, satu transaksi.</summary>
    public static (string Sql, object?[] Args) BuildUpsert(ParameterTable table, IReadOnlyList<ParameterField> keys, IReadOnlyDictionary<string, object?> values, Guid id, string approver)
    {
        var args = new List<object?>();
        string P(object? v) { args.Add(v); return "@" + args.Count; }

        var keyNames = keys.Select(k => k.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var where = string.Join(" AND ", keys.Select(k => $"{Q(k.Name)} = {P(values[k.Name])}"));
        var others = table.Fields.Where(f => f.Kind != ParameterKind.Unsupported && !keyNames.Contains(f.Name) && values.ContainsKey(f.Name)).ToList();

        var sql = new StringBuilder("SET XACT_ABORT ON; BEGIN TRAN; ");
        if (others.Count > 0)
        {
            var set = string.Join(", ", others.Select(f => $"{Q(f.Name)} = {P(values[f.Name])}"));
            sql.Append(CultureInfo.InvariantCulture, $"UPDATE {Q(table.Table)} SET {set} WHERE {where}; IF @@ROWCOUNT = 0 ");
        }
        else
        {
            sql.Append(CultureInfo.InvariantCulture, $"IF NOT EXISTS (SELECT 1 FROM {Q(table.Table)} WHERE {where}) ");
        }

        var insertFields = keys.Concat(others).ToList();
        var cols = string.Join(", ", insertFields.Select(f => Q(f.Name)));
        var vals = string.Join(", ", insertFields.Select(f => P(values[f.Name])));
        sql.Append(CultureInfo.InvariantCulture, $"INSERT INTO {Q(table.Table)} ({cols}) VALUES ({vals}); ");
        sql.Append(CultureInfo.InvariantCulture, $"EXEC SP_AUDITTRAIL {P(id)}, {P(approver)}; COMMIT TRAN");
        return (sql.ToString(), [.. args]);
    }

    /// <summary>Hapus = <c>ACTIVE = 0</c> bila tabel punya kolom ACTIVE (sama dengan layar lama), selain itu <c>DELETE</c> satu baris.</summary>
    public static (string Sql, object?[] Args) BuildDelete(ParameterTable table, IReadOnlyList<ParameterField> keys, IReadOnlyDictionary<string, object?> values, Guid id, string approver)
    {
        var args = new List<object?>();
        string P(object? v) { args.Add(v); return "@" + args.Count; }

        var where = string.Join(" AND ", keys.Select(k => $"{Q(k.Name)} = {P(values[k.Name])}"));
        var sql = new StringBuilder("SET XACT_ABORT ON; BEGIN TRAN; ");
        sql.Append(CultureInfo.InvariantCulture, $"IF (SELECT COUNT(*) FROM {Q(table.Table)} WHERE {where}) <> 1 RAISERROR ('Data tidak ditemukan atau tidak unik', 16, 1); ");
        var active = table.Field("ACTIVE");
        if (active is not null && active.Kind != ParameterKind.Unsupported)
        {
            object off = active.Kind switch { ParameterKind.Boolean => false, ParameterKind.Integer => 0L, _ => "0" };
            sql.Append(CultureInfo.InvariantCulture, $"UPDATE {Q(table.Table)} SET {Q(active.Name)} = {P(off)} WHERE {where}; ");
        }
        else
        {
            sql.Append(CultureInfo.InvariantCulture, $"DELETE {Q(table.Table)} WHERE {where}; ");
        }

        sql.Append(CultureInfo.InvariantCulture, $"EXEC SP_AUDITTRAIL {P(id)}, {P(approver)}; COMMIT TRAN");
        // Parameter posisional boleh dirujuk berkali-kali, jadi kondisi WHERE yang sama dipakai untuk hitung dan hapus/ubah.
        return (sql.ToString(), [.. args]);
    }

    private static async Task<ParameterRow?> ReadRowAsync(IDb db, ParameterTable table, IReadOnlyList<KeyValuePair<string, string?>> keys, CancellationToken ct)
    {
        var args = new List<object?>();
        var where = new List<string>();
        foreach (var (name, text) in keys)
        {
            var field = table.Field(name)!;
            ParameterRules.TryConvert(field, text, out var typed);
            args.Add(typed);
            where.Add($"{Q(name)} = @{args.Count}");
        }

        var cols = string.Join(", ", table.Fields.Where(f => f.Kind != ParameterKind.Unsupported).Select(f => Q(f.Name)));
        var row = await db.QueryFirstOrDefaultAsync($"SELECT TOP 1 {cols} FROM {Q(table.Table)} WHERE {string.Join(" AND ", where)}", [.. args], ct: ct).ConfigureAwait(false);
        if (row is null)
        {
            return null;
        }

        return new ParameterRow(table.Fields.Where(f => f.Kind != ParameterKind.Unsupported)
            .ToDictionary(f => f.Name, f => ParameterRules.ToText(f.Kind, row[f.Name]), StringComparer.OrdinalIgnoreCase));
    }

    private static string? Normalize(ParameterField field, string? text)
    {
        if (field.Kind == ParameterKind.Boolean)
        {
            return text is "1" or "True" or "true" ? "1" : "0";
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        ParameterRules.TryConvert(field, text, out var typed);
        return typed is null ? text.Trim() : ParameterRules.ToText(field.Kind, typed);
    }

    /// <summary>Nama sudah divalidasi terhadap skema; kurung siku ditambahkan dan <c>]</c> digandakan sebagai pengaman tambahan.</summary>
    public static string Q(string name) => "[" + name.Replace("]", "]]", StringComparison.Ordinal) + "]";

    [LoggerMessage(Level = LogLevel.Information, Message = "Maintenance parameter: {Maker} mengajukan {Status} pada {Table}")]
    private static partial void LogPending(ILogger logger, string maker, string status, string table);

    [LoggerMessage(Level = LogLevel.Information, Message = "Maintenance parameter: {Approver} {Decision} ajuan {TemporaryId} pada {Table}")]
    private static partial void LogDecision(ILogger logger, string approver, string decision, Guid temporaryId, string table);

    [LoggerMessage(Level = LogLevel.Error, Message = "Maintenance parameter gagal ({Action}): {Actor}, objek {Subject}")]
    private static partial void LogFailed(ILogger logger, Exception ex, string action, string actor, string subject);
}
