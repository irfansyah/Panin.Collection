// Menyalin skema + data dari DB lama ke SQL Server lokal (Docker) untuk pengembangan.
//
// Aman by design:
//  - Sumber (ConnectionStrings:LoginSource) hanya dibaca: SELECT saja, ApplicationIntent=ReadOnly.
//  - Target wajib server lokal (localhost / 127.0.0.1 / (localdb)); selain itu program berhenti.
//  - Target didrop dan dibuat ulang setiap kali dijalankan.
//
// Pakai:  dotnet run --project tools/Panin.DbClone
using System.Data;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

var cfg = new ConfigurationBuilder().AddUserSecrets<Marker>().Build();
var source = new SqlConnectionStringBuilder(cfg["ConnectionStrings:LoginSource"] ?? throw new("ConnectionStrings:LoginSource belum diisi"))
{
    ApplicationIntent = ApplicationIntent.ReadOnly,
};
var target = new SqlConnectionStringBuilder(cfg["ConnectionStrings:Login"] ?? throw new("ConnectionStrings:Login belum diisi"));

if (!IsLocal(target.DataSource))
{
    Console.Error.WriteLine($"Ditolak: target '{target.DataSource}' bukan server lokal.");
    return 1;
}

if (string.Equals(source.DataSource, target.DataSource, StringComparison.OrdinalIgnoreCase))
{
    Console.Error.WriteLine("Ditolak: sumber dan target menunjuk server yang sama.");
    return 1;
}

Console.WriteLine($"Sumber : {source.DataSource} / {source.InitialCatalog} (read-only)");
Console.WriteLine($"Target : {target.DataSource} / {target.InitialCatalog}");

if (args.Length > 0 && args[0].Equals("data", StringComparison.OrdinalIgnoreCase))
{
    if (args.Contains("--objects-only"))
    {
        await RedoDataObjects();
        return 0;
    }

    var picked = args.Length > 2 && args[1] == "--users"
        ? args[2].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        : await PickSampleUsers();
    await CloneDataSample(picked);
    Console.WriteLine("Selesai.");
    return 0;
}

// 1) DB DATA: hanya objek referensi yang dipakai view/SP di DB LOGIN (lintas database).
//    View di DB LOGIN seperti RFBRANCH membaca LOSPANINCOLLECTION.dbo.RFBRANCH.
var dataDb = "LOSPANINCOLLECTION";
var refTables = new[] { "AREA", "RFAPPTYPE", "RFBRANCH", "RFDPD", "RFPRODUCT", "RFPRODUCTGROUP", "RFPRODUCTTYPE", "RFREASONDETAIL", "RFREASONMASTER", "RFREGIONAL", "RFMIS_PERC", "RFMIS_TOTAL" };
var refViews = new[] { "VW_RFBRANCH", "VW_RFKCU", "VW_RFMIS" };
await CloneDatabase(dataDb, dataDb, refTables, refViews);

// 2) DB LOGIN lengkap (dibuat setelah DB DATA karena view-nya membaca DB DATA)
await CloneDatabase(source.InitialCatalog, target.InitialCatalog, tables: null, objects: null);


// 3) Netralkan kredensial DB di salinan lokal agar aplikasi tidak pernah tersambung ke server sumber
//    lewat fallback RFMODULE. Koneksi DATA lokal harus datang dari user-secrets (ConnectionStrings:Data:<id>).
{
    var local = new SqlConnectionStringBuilder(target.ConnectionString);
    await using var d = new SqlConnection(local.ConnectionString);
    await d.OpenAsync();
    foreach (var table in new[] { "RFMODULE", "RFMODULE_BACKUP_20250318" })
    {
        await Exec(d, $"IF OBJECT_ID('dbo.{table}') IS NOT NULL UPDATE dbo.{table} SET DB_LOGINPWD = '', DB_LOGINID = ''");
    }

    Console.WriteLine("\nRFMODULE: DB_LOGINID/DB_LOGINPWD dikosongkan di salinan lokal.");
}
Console.WriteLine("Selesai.");
return 0;

static bool IsLocal(string dataSource)
{
    var host = dataSource.Split(',')[0].Trim().ToLowerInvariant();
    return host is "localhost" or "127.0.0.1" or "." or "(local)" || host.StartsWith("(localdb)", StringComparison.Ordinal);
}

async Task CloneDatabase(string srcDb, string dstDb, string[]? tables, string[]? objects)
{
    Console.WriteLine($"\n=== {srcDb} -> {dstDb}");
    var src = new SqlConnectionStringBuilder(source.ConnectionString) { InitialCatalog = srcDb };
    var master = new SqlConnectionStringBuilder(target.ConnectionString) { InitialCatalog = "master" };
    var dst = new SqlConnectionStringBuilder(target.ConnectionString) { InitialCatalog = dstDb };

    await using var s = new SqlConnection(src.ConnectionString);
    await s.OpenAsync();

    var collation = (string)(await Scalar(s, "select convert(nvarchar(128), DATABASEPROPERTYEX(db_name(), 'Collation'))"))!;
    await using (var m = new SqlConnection(master.ConnectionString))
    {
        await m.OpenAsync();
        await Exec(m, $"IF DB_ID('{dstDb}') IS NOT NULL BEGIN ALTER DATABASE [{dstDb}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{dstDb}]; END");
        await Exec(m, $"CREATE DATABASE [{dstDb}] COLLATE {collation}");
    }

    await using var d = new SqlConnection(dst.ConnectionString);
    await d.OpenAsync();

    var tableNames = await Strings(s, "select name from sys.tables where is_ms_shipped = 0 order by name");
    if (tables is not null)
    {
        tableNames = tableNames.Where(t => tables.Contains(t, StringComparer.OrdinalIgnoreCase)).ToList();
    }

    // Tabel + data (tanpa FK dulu)
    foreach (var t in tableNames)
    {
        await Exec(d, await ScriptTable(s, t));
        var rows = await CopyData(s, d, t);
        Console.WriteLine($"  tabel {t,-32} {rows,9:n0} baris");
    }

    // Index non-PK dan foreign key (setelah data masuk)
    foreach (var t in tableNames)
    {
        foreach (var ddl in await ScriptIndexes(s, t))
        {
            await Exec(d, ddl);
        }
    }

    foreach (var ddl in await ScriptForeignKeys(s, tableNames))
    {
        await Exec(d, ddl);
    }

    // Function -> View -> Procedure -> Trigger
    var modules = await Modules(s, objects);
    foreach (var type in new[] { "FN", "IF", "TF", "V", "P", "TR" })
    {
        var pending = modules.Where(x => x.Type == type).ToList();
        for (var pass = 0; pass < 8 && pending.Count > 0; pass++)
        {
            var failed = new List<(string Name, string Type, string Sql, string Err)>();
            foreach (var mod in pending)
            {
                try { await Exec(d, mod.Sql); }
                catch (SqlException ex) { failed.Add((mod.Name, mod.Type, mod.Sql, ex.Message)); }
            }

            foreach (var ok in pending.Where(p => failed.All(f => f.Name != p.Name)))
            {
                Console.WriteLine($"  {type,-2} {ok.Name}");
            }

            pending = failed.Select(f => (f.Name, f.Type, f.Sql)).ToList();
            if (pass == 7 || (failed.Count == 0))
            {
                foreach (var f in failed)
                {
                    Console.WriteLine($"  !! GAGAL {f.Type} {f.Name}: {f.Err.Split('\n')[0]}");
                }
            }
        }
    }

    // Verifikasi jumlah baris
    var bad = 0;
    foreach (var t in tableNames)
    {
        var a = (long)(await Scalar(s, $"select count_big(*) from [{t}]"))!;
        var b = (long)(await Scalar(d, $"select count_big(*) from [{t}]"))!;
        if (a != b)
        {
            bad++;
            Console.WriteLine($"  !! BEDA {t}: sumber {a:n0} vs target {b:n0}");
        }
    }

    Console.WriteLine(bad == 0 ? $"  verifikasi OK ({tableNames.Count} tabel)" : $"  verifikasi: {bad} tabel berbeda");
}

async Task<string[]> PickSampleUsers()
{
    var src = new SqlConnectionStringBuilder(source.ConnectionString) { InitialCatalog = "LOSPANINCOLLECTION" };
    await using var s = new SqlConnection(src.ConnectionString);
    await s.OpenAsync();
    var users = await Strings(s, "select top 12 USERID from LOAN_MASTER where CONFIRMED = '1' and USERID is not null group by USERID having count(*) between 20 and 300 order by newid()");
    Console.WriteLine("User contoh (otomatis): " + string.Join(", ", users));
    return [.. users];
}

// Salinan lokal DB DATA modul 60: skema lengkap (tabel, view, SP, fungsi, trigger) + data contoh.
// Data contoh: semua tabel kecil (<= SmallTableRows baris); untuk tabel besar yang punya indeks ACC_NO/CU_REF hanya baris milik
// rekening contoh (seek, murah bagi server sumber); tabel besar lain hanya TOP N baris; tabel sementara/unduhan hanya skema.
// Sumber dibuka ReadOnly + READ UNCOMMITTED agar tidak mengganggu DB dev bersama.
async Task<List<(string Name, string Type, string Sql)>> CreatableModules(SqlConnection s, SqlConnection d)
{
    // View di DB DATA membaca DB LOGIN lewat nama tiga bagian (LOSPANINLOGIN_COLLECTION di sumber); di salinan lokal DB LOGIN bernama LOSPANINLOGIN.
    var existing = (await Strings(d, "select name from sys.objects where type in ('FN','IF','TF','V','P','TR')")).ToHashSet(StringComparer.OrdinalIgnoreCase);
    return (await Modules(s, null))
        .Where(m => !existing.Contains(m.Name))
        .Select(m => (m.Name, m.Type, m.Sql.Replace("LOSPANINLOGIN_COLLECTION", "LOSPANINLOGIN", StringComparison.OrdinalIgnoreCase)))
        .ToList();
}

async Task RedoDataObjects()
{
    var src = new SqlConnectionStringBuilder(source.ConnectionString) { InitialCatalog = "LOSPANINCOLLECTION" };
    var dst = new SqlConnectionStringBuilder(target.ConnectionString) { InitialCatalog = "LOSPANINCOLLECTION" };
    await using var s = new SqlConnection(src.ConnectionString);
    await s.OpenAsync();
    await using var d = new SqlConnection(dst.ConnectionString);
    await d.OpenAsync();
    var modules = await CreatableModules(s, d);
    var failed = 0;
    foreach (var type in new[] { "FN", "IF", "TF", "V", "P", "TR" })
    {
        var pending = modules.Where(x => x.Type == type).ToList();
        for (var pass = 0; pass < 8 && pending.Count > 0; pass++)
        {
            var next = new List<(string Name, string Type, string Sql)>();
            foreach (var mod in pending)
            {
                try { await Exec(d, mod.Sql); }
                catch (SqlException) { next.Add(mod); }
            }

            pending = next;
        }

        foreach (var f in pending)
        {
            failed++;
            Console.WriteLine($"  !! GAGAL {f.Type} {f.Name}");
        }
    }

    Console.WriteLine($"  objek dibuat ulang; masih gagal: {failed}");
}

async Task CloneDataSample(string[] sampleUsers)
{
    const int SmallTableRows = 100_000;
    const int TopRowsForOtherBigTables = 2_000;
    var dstDb = "LOSPANINCOLLECTION";
    var src = new SqlConnectionStringBuilder(source.ConnectionString) { InitialCatalog = dstDb };
    var master = new SqlConnectionStringBuilder(target.ConnectionString) { InitialCatalog = "master" };
    var dst = new SqlConnectionStringBuilder(target.ConnectionString) { InitialCatalog = dstDb };
    var skipData = new System.Text.RegularExpressions.Regex(
        @"(_TEMP\d*$|_TEMP_|_DOWNLOAD|_BCK_|BACKUP|^TBL_TEMP|^UPLOAD_.*TEMP|^tempdbtblcompare|^tempcheck|^dtproperties)",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    Console.WriteLine($"\n=== DB DATA {src.InitialCatalog} -> {dstDb} (skema lengkap + data contoh)");
    await using var s = new SqlConnection(src.ConnectionString);
    await s.OpenAsync();
    await Exec(s, "SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED");

    var collation = (string)(await Scalar(s, "select convert(nvarchar(128), DATABASEPROPERTYEX(db_name(), 'Collation'))"))!;
    await using (var m = new SqlConnection(master.ConnectionString))
    {
        await m.OpenAsync();
        await Exec(m, $"IF DB_ID('{dstDb}') IS NOT NULL BEGIN ALTER DATABASE [{dstDb}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{dstDb}]; END");
        await Exec(m, $"CREATE DATABASE [{dstDb}] COLLATE {collation}");
        await Exec(m, $"ALTER DATABASE [{dstDb}] SET RECOVERY SIMPLE");
    }

    await using var d = new SqlConnection(dst.ConnectionString);
    await d.OpenAsync();

    // Rekening contoh: milik user contoh (LOAN_MASTER.USERID), lalu CIF-nya dari ACCOUNT_MASTER.
    var userList = string.Join(",", sampleUsers.Select(u => "'" + u.Replace("'", "''", StringComparison.Ordinal) + "'"));
    var accounts = await Strings(s, $"select distinct ACC_NO from LOAN_MASTER where USERID in ({userList})");
    var customers = new List<string>();
    foreach (var chunk in accounts.Chunk(500))
    {
        customers.AddRange(await Strings(s, $"select distinct CU_REF from ACCOUNT_MASTER where ACC_NO in ({Literals(chunk)})"));
    }

    customers = [.. customers.Distinct()];
    Console.WriteLine($"  user contoh: {sampleUsers.Length}, rekening: {accounts.Count:n0}, nasabah: {customers.Count:n0}");

    string SampleCte(string column, IReadOnlyList<string> values)
        => "WITH S(V) AS (" + string.Join(" UNION ALL ", values.Chunk(900).Select(c => "SELECT V FROM (VALUES " + string.Join(",", c.Select(v => "('" + v.Replace("'", "''", StringComparison.Ordinal) + "')")) + ") X(V)")) + ")";

    var stats = await Rows(s, @"
select t.name, isnull(sum(ps.row_count), 0),
  (select count(*) from sys.columns c where c.object_id = t.object_id and c.name = 'ACC_NO'),
  (select count(*) from sys.columns c where c.object_id = t.object_id and c.name = 'CU_REF'),
  (select count(*) from sys.indexes i join sys.index_columns ic on ic.object_id = i.object_id and ic.index_id = i.index_id and ic.key_ordinal = 1
     join sys.columns c on c.object_id = ic.object_id and c.column_id = ic.column_id where i.object_id = t.object_id and c.name = 'ACC_NO'),
  (select count(*) from sys.indexes i join sys.index_columns ic on ic.object_id = i.object_id and ic.index_id = i.index_id and ic.key_ordinal = 1
     join sys.columns c on c.object_id = ic.object_id and c.column_id = ic.column_id where i.object_id = t.object_id and c.name = 'CU_REF')
from sys.tables t left join sys.dm_db_partition_stats ps on ps.object_id = t.object_id and ps.index_id in (0, 1)
where t.is_ms_shipped = 0 group by t.name, t.object_id order by t.name");

    var tableNames = stats.Select(r => (string)r[0]!).ToList();
    var total = 0L;
    foreach (var r in stats)
    {
        var table = (string)r[0]!;
        var approx = Convert.ToInt64(r[1], System.Globalization.CultureInfo.InvariantCulture);
        var hasAcc = Convert.ToInt32(r[2], System.Globalization.CultureInfo.InvariantCulture) > 0;
        var hasCu = Convert.ToInt32(r[3], System.Globalization.CultureInfo.InvariantCulture) > 0;
        var accIdx = Convert.ToInt32(r[4], System.Globalization.CultureInfo.InvariantCulture) > 0;
        var cuIdx = Convert.ToInt32(r[5], System.Globalization.CultureInfo.InvariantCulture) > 0;

        await Exec(d, await ScriptTable(s, table));

        string? select;
        string how;
        if (skipData.IsMatch(table) && !table.Equals("TEMP_DOWNLINE", StringComparison.OrdinalIgnoreCase))
        {
            select = null;
            how = "skema saja";
        }
        else if (approx <= SmallTableRows)
        {
            select = $"select * from [dbo].[{table}]";
            how = "penuh";
        }
        else if (hasAcc && accIdx)
        {
            select = $"{SampleCte("ACC_NO", accounts)} select T.* from [dbo].[{table}] T join S on S.V = T.ACC_NO";
            how = "rekening contoh";
        }
        else if (hasCu && cuIdx)
        {
            select = $"{SampleCte("CU_REF", customers)} select T.* from [dbo].[{table}] T join S on S.V = T.CU_REF";
            how = "nasabah contoh";
        }
        else
        {
            select = $"select top ({TopRowsForOtherBigTables}) * from [dbo].[{table}]";
            how = $"TOP {TopRowsForOtherBigTables:n0}";
        }

        long copied = 0;
        if (select is not null)
        {
            copied = await CopyQuery(s, d, table, select);
        }

        total += copied;
        Console.WriteLine($"  tabel {table,-40} {copied,10:n0} baris  ({how}; sumber ±{approx:n0})");
    }

    foreach (var t in tableNames)
    {
        foreach (var ddl in await ScriptIndexes(s, t))
        {
            try { await Exec(d, ddl); }
            catch (SqlException ex) { Console.WriteLine($"  !! indeks {t}: {ex.Message.Split('\n')[0]}"); }
        }
    }

    foreach (var ddl in await ScriptForeignKeys(s, tableNames))
    {
        try { await Exec(d, ddl); }
        catch (SqlException ex) { Console.WriteLine($"  !! FK: {ex.Message.Split('\n')[0]}"); }
    }

    var modules = await CreatableModules(s, d);
    var failedModules = 0;
    foreach (var type in new[] { "FN", "IF", "TF", "V", "P", "TR" })
    {
        var pending = modules.Where(x => x.Type == type).ToList();
        for (var pass = 0; pass < 8 && pending.Count > 0; pass++)
        {
            var failed = new List<(string Name, string Type, string Sql)>();
            foreach (var mod in pending)
            {
                try { await Exec(d, mod.Sql); }
                catch (SqlException) { failed.Add(mod); }
            }

            pending = failed;
        }

        foreach (var f in pending)
        {
            failedModules++;
            Console.WriteLine($"  !! GAGAL {f.Type} {f.Name}");
        }
    }

    Console.WriteLine($"  total {total:n0} baris disalin; objek gagal dibuat: {failedModules}");
}

static string Literals(IEnumerable<string> values)
    => string.Join(",", values.Select(v => "'" + v.Replace("'", "''", StringComparison.Ordinal) + "'"));

static async Task<long> CopyQuery(SqlConnection s, SqlConnection d, string table, string select)
{
    await using var cmd = new SqlCommand(select, s) { CommandTimeout = 0 };
    await using var reader = await cmd.ExecuteReaderAsync();
    using var bulk = new SqlBulkCopy(d, SqlBulkCopyOptions.KeepIdentity | SqlBulkCopyOptions.KeepNulls | SqlBulkCopyOptions.TableLock, null)
    {
        DestinationTableName = $"[dbo].[{table}]",
        BatchSize = 10000,
        BulkCopyTimeout = 0,
        EnableStreaming = true,
    };
    var schema = reader.GetColumnSchema();
    foreach (var c in schema)
    {
        if (string.Equals(c.DataTypeName, "timestamp", StringComparison.OrdinalIgnoreCase))
        {
            continue; // rowversion: tidak dapat diisi
        }

        bulk.ColumnMappings.Add(c.ColumnName!, c.ColumnName!);
    }

    await bulk.WriteToServerAsync(reader);
    return bulk.RowsCopied;
}

static async Task<string> ScriptTable(SqlConnection s, string table)
{
    var sb = new StringBuilder($"CREATE TABLE [dbo].[{table}] (\n");
    await using var cmd = new SqlCommand(@"
select c.name, ty.name as tname, c.max_length, c.precision, c.scale, c.is_nullable, c.is_identity,
       ic.seed_value, ic.increment_value, dc.definition, c.collation_name
from sys.columns c
join sys.types ty on ty.user_type_id = c.user_type_id
left join sys.identity_columns ic on ic.object_id = c.object_id and ic.column_id = c.column_id
left join sys.default_constraints dc on dc.parent_object_id = c.object_id and dc.parent_column_id = c.column_id
where c.object_id = object_id(@t)
order by c.column_id", s);
    cmd.Parameters.AddWithValue("@t", "dbo." + table);
    var cols = new List<string>();
    await using (var r = await cmd.ExecuteReaderAsync())
    {
        while (await r.ReadAsync())
        {
            var name = r.GetString(0);
            var t = r.GetString(1);
            int len = r.GetInt16(2), prec = r.GetByte(3), scale = r.GetByte(4);
            var type = t switch
            {
                "varchar" or "char" or "varbinary" or "binary" => $"{t}({(len == -1 ? "MAX" : len)})",
                "nvarchar" or "nchar" => $"{t}({(len == -1 ? "MAX" : len / 2)})",
                "decimal" or "numeric" => $"{t}({prec},{scale})",
                "datetime2" or "time" or "datetimeoffset" => $"{t}({scale})",
                _ => t,
            };
            var line = $"  [{name}] {type}";
            if (r.GetBoolean(6))
            {
                line += $" IDENTITY({r.GetValue(7)},{r.GetValue(8)})";
            }

            line += r.GetBoolean(5) ? " NULL" : " NOT NULL";
            if (!r.IsDBNull(9))
            {
                line += " DEFAULT " + r.GetString(9);
            }

            cols.Add(line);
        }
    }

    sb.Append(string.Join(",\n", cols));

    var pk = await Rows(s, @"
select i.name, i.type_desc, c.name, ic.is_descending_key
from sys.indexes i
join sys.index_columns ic on ic.object_id = i.object_id and ic.index_id = i.index_id
join sys.columns c on c.object_id = ic.object_id and c.column_id = ic.column_id
where i.object_id = object_id(@t) and i.is_primary_key = 1
order by ic.key_ordinal", ("@t", "dbo." + table));
    if (pk.Count > 0)
    {
        var kind = pk[0][1]!.ToString()!.StartsWith("NONCLUSTERED", StringComparison.Ordinal) ? "NONCLUSTERED" : "CLUSTERED";
        var keyCols = string.Join(", ", pk.Select(p => $"[{p[2]}]{((bool)p[3]! ? " DESC" : "")}"));
        sb.Append($",\n  CONSTRAINT [{pk[0][0]}] PRIMARY KEY {kind} ({keyCols})");
    }

    sb.Append("\n)");
    return sb.ToString();
}

static async Task<List<string>> ScriptIndexes(SqlConnection s, string table)
{
    var rows = await Rows(s, @"
select i.name, i.is_unique, i.type_desc, c.name, ic.is_descending_key, ic.is_included_column, ic.key_ordinal
from sys.indexes i
join sys.index_columns ic on ic.object_id = i.object_id and ic.index_id = i.index_id
join sys.columns c on c.object_id = ic.object_id and c.column_id = ic.column_id
where i.object_id = object_id(@t) and i.is_primary_key = 0 and i.index_id > 0 and i.is_hypothetical = 0 and i.type in (1, 2)
order by i.index_id, ic.is_included_column, ic.key_ordinal", ("@t", "dbo." + table));
    var result = new List<string>();
    foreach (var g in rows.GroupBy(r => (string)r[0]!))
    {
        var first = g.First();
        var keys = string.Join(", ", g.Where(r => !(bool)r[5]!).Select(r => $"[{r[3]}]{((bool)r[4]! ? " DESC" : "")}"));
        var inc = g.Where(r => (bool)r[5]!).Select(r => $"[{r[3]}]").ToList();
        var kind = first[2]!.ToString()!.StartsWith("CLUSTERED", StringComparison.Ordinal) ? "CLUSTERED" : "NONCLUSTERED";
        result.Add($"CREATE {((bool)first[1]! ? "UNIQUE " : "")}{kind} INDEX [{g.Key}] ON [dbo].[{table}] ({keys})" +
                   (inc.Count > 0 ? $" INCLUDE ({string.Join(", ", inc)})" : ""));
    }

    return result;
}

static async Task<List<string>> ScriptForeignKeys(SqlConnection s, List<string> tables)
{
    var rows = await Rows(s, @"
select fk.name, object_name(fk.parent_object_id), pc.name, object_name(fk.referenced_object_id), rc.name, fkc.constraint_column_id
from sys.foreign_keys fk
join sys.foreign_key_columns fkc on fkc.constraint_object_id = fk.object_id
join sys.columns pc on pc.object_id = fkc.parent_object_id and pc.column_id = fkc.parent_column_id
join sys.columns rc on rc.object_id = fkc.referenced_object_id and rc.column_id = fkc.referenced_column_id
order by fk.name, fkc.constraint_column_id");
    var result = new List<string>();
    foreach (var g in rows.GroupBy(r => (string)r[0]!))
    {
        var f = g.First();
        if (!tables.Contains((string)f[1]!, StringComparer.OrdinalIgnoreCase) || !tables.Contains((string)f[3]!, StringComparer.OrdinalIgnoreCase))
        {
            continue;
        }

        result.Add($"ALTER TABLE [dbo].[{f[1]}] WITH NOCHECK ADD CONSTRAINT [{g.Key}] FOREIGN KEY ({string.Join(", ", g.Select(r => $"[{r[2]}]"))}) " +
                   $"REFERENCES [dbo].[{f[3]}] ({string.Join(", ", g.Select(r => $"[{r[4]}]"))})");
    }

    return result;
}

static async Task<long> CopyData(SqlConnection s, SqlConnection d, string table)
{
    await using var cmd = new SqlCommand($"select * from [dbo].[{table}]", s) { CommandTimeout = 0 };
    await using var reader = await cmd.ExecuteReaderAsync();
    using var bulk = new SqlBulkCopy(d, SqlBulkCopyOptions.KeepIdentity | SqlBulkCopyOptions.KeepNulls | SqlBulkCopyOptions.TableLock, null)
    {
        DestinationTableName = $"[dbo].[{table}]",
        BatchSize = 10000,
        BulkCopyTimeout = 0,
        EnableStreaming = true,
    };
    for (var i = 0; i < reader.FieldCount; i++)
    {
        bulk.ColumnMappings.Add(reader.GetName(i), reader.GetName(i));
    }

    await bulk.WriteToServerAsync(reader);
    return bulk.RowsCopied;
}

static async Task<List<(string Name, string Type, string Sql)>> Modules(SqlConnection s, string[]? only)
{
    var rows = await Rows(s, @"
select o.name, rtrim(o.type), m.definition
from sys.sql_modules m join sys.objects o on o.object_id = m.object_id
where o.is_ms_shipped = 0 and o.type in ('FN','IF','TF','V','P','TR') and m.definition is not null
order by o.name");
    return rows
        .Select(r => ((string)r[0]!, (string)r[1]!, (string)r[2]!))
        .Where(x => only is null || only.Contains(x.Item1, StringComparer.OrdinalIgnoreCase))
        .ToList();
}

static async Task<object?> Scalar(SqlConnection c, string sql)
{
    await using var cmd = new SqlCommand(sql, c) { CommandTimeout = 0 };
    return await cmd.ExecuteScalarAsync();
}

static async Task Exec(SqlConnection c, string sql)
{
    await using var cmd = new SqlCommand(sql, c) { CommandTimeout = 0 };
    await cmd.ExecuteNonQueryAsync();
}

static async Task<List<string>> Strings(SqlConnection c, string sql)
{
    var list = new List<string>();
    await using var cmd = new SqlCommand(sql, c);
    await using var r = await cmd.ExecuteReaderAsync();
    while (await r.ReadAsync())
    {
        list.Add(r.GetString(0));
    }

    return list;
}

static async Task<List<object?[]>> Rows(SqlConnection c, string sql, params (string Name, object Value)[] ps)
{
    var list = new List<object?[]>();
    await using var cmd = new SqlCommand(sql, c);
    foreach (var (n, v) in ps)
    {
        cmd.Parameters.AddWithValue(n, v);
    }

    await using var r = await cmd.ExecuteReaderAsync();
    while (await r.ReadAsync())
    {
        var v = new object?[r.FieldCount];
        for (var i = 0; i < v.Length; i++)
        {
            v[i] = r.IsDBNull(i) ? null : r.GetValue(i);
        }

        list.Add(v);
    }

    return list;
}

internal sealed class Marker;
