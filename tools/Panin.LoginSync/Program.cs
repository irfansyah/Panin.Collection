// Sinkronisasi satu arah DB LOGIN lama -> DB LOGIN baru untuk masa uji paralel (lihat docs/PARALEL.md).
//
// Sumber (lama) HANYA dibaca (SELECT, ApplicationIntent=ReadOnly); tidak ada satu pun perintah tulis dikirim ke sumber.
// Tujuan (baru) diubah dalam SATU transaksi: gagal di tengah = tidak ada perubahan. Default adalah dry-run (transaksi di-rollback
// setelah menghitung); --apply untuk benar-benar menyimpan.
//
// Yang disalin : SCALLGROUP, SCALLUSER (termasuk password kecuali --skip-passwords), GRPMENUX, TEMP_DOWNLINE; baris SCALLUSERFLAG
//                yang belum ada dibuat (flag login/revoke aplikasi baru tidak ditimpa).
// Tidak disalin: RFMENUX (MENUURL aplikasi baru berbeda), RFMODULE, SCALLUSERFLAG yang sudah ada, SCALLUSERLOG, AUDITTRAIL*, ESAPPTOKEN.
using System.Globalization;
using System.Text;
using Microsoft.Data.SqlClient;

return await Run(args);

static async Task<int> Run(string[] args)
{
    var opt = Options.Parse(args);
    if (opt.Help || opt.Source is null || opt.Target is null)
    {
        Console.WriteLine("""
            Panin.LoginSync - salin satu arah DB LOGIN lama ke DB LOGIN baru (default dry-run)

              --source <connection string>   DB LOGIN lama (hanya dibaca)     atau env PANIN_SYNC_SOURCE
              --target <connection string>   DB LOGIN baru (diubah)           atau env PANIN_SYNC_TARGET
              --apply                        simpan perubahan (tanpa ini: hitung lalu batalkan)
              --skip-passwords               jangan menimpa SU_PWD/SU_PWDEXPDATE user yang sudah ada
              --max-delete-percent <n>       batas penghapusan per tabel (default 5; minimal 10 baris)
              --allow-mass-delete            izinkan penghapusan melebihi batas

            Kode keluar: 0 sukses, 1 galat, 2 argumen salah, 3 dibatalkan oleh pengaman penghapusan.
            """);
        return opt.Help ? 0 : 2;
    }

    var source = new SqlConnectionStringBuilder(opt.Source) { ApplicationIntent = ApplicationIntent.ReadOnly };
    var target = new SqlConnectionStringBuilder(opt.Target);
    if (string.Equals(source.DataSource, target.DataSource, StringComparison.OrdinalIgnoreCase)
        && string.Equals(source.InitialCatalog, target.InitialCatalog, StringComparison.OrdinalIgnoreCase))
    {
        Console.Error.WriteLine("Sumber dan tujuan sama persis; dibatalkan.");
        return 2;
    }

    Console.WriteLine($"Sumber (hanya baca): {source.DataSource} / {source.InitialCatalog}");
    Console.WriteLine($"Tujuan             : {target.DataSource} / {target.InitialCatalog}");
    Console.WriteLine(opt.Apply ? "Mode               : APPLY (perubahan disimpan)" : "Mode               : DRY-RUN (dibatalkan di akhir)");

    await using var src = new SqlConnection(source.ConnectionString);
    await using var dst = new SqlConnection(target.ConnectionString);
    await src.OpenAsync();
    await dst.OpenAsync();

    await using var tx = (SqlTransaction)await dst.BeginTransactionAsync();
    try
    {
        // Trigger INS/UPD/DEL_USER mengasumsikan satu baris per perintah; pada salinan massal hasilnya salah.
        // TEMP_DOWNLINE disalin sendiri dari sumber, jadi trigger dimatikan selama sinkronisasi (dalam transaksi yang sama).
        await Exec(dst, tx, "ALTER TABLE SCALLUSER DISABLE TRIGGER ALL");

        var summary = new List<(string Table, int Inserted, int Updated, int Deleted)>();
        foreach (var plan in SyncPlan.Tables)
        {
            var result = await SyncTable(src, dst, tx, plan, opt);
            summary.Add((plan.Table, result.Inserted, result.Updated, result.Deleted));
            if (result.Aborted)
            {
                await tx.RollbackAsync();
                Console.Error.WriteLine($"DIBATALKAN: {plan.Table} akan menghapus {result.Deleted} baris, melebihi batas ({result.Limit}). " +
                                        "Periksa sumber (mungkin salah DB/koneksi). Gunakan --allow-mass-delete bila memang benar.");
                return 3;
            }
        }

        var flags = await ExecCount(dst, tx,
            "INSERT INTO SCALLUSERFLAG (USERID, SU_LOGON, SU_REVOKE, SU_FALSEPWDCOUNT) " +
            "SELECT U.USERID, 0, 0, 0 FROM SCALLUSER U WHERE NOT EXISTS (SELECT 1 FROM SCALLUSERFLAG F WHERE F.USERID = U.USERID)");
        summary.Add(("SCALLUSERFLAG (baris baru)", flags, 0, 0));

        await Exec(dst, tx, "ALTER TABLE SCALLUSER ENABLE TRIGGER ALL");

        Console.WriteLine();
        Console.WriteLine($"{"Tabel",-28} {"Tambah",8} {"Ubah",8} {"Hapus",8}");
        foreach (var (table, i, u, d) in summary)
        {
            Console.WriteLine($"{table,-28} {i,8} {u,8} {d,8}");
        }

        if (opt.Apply)
        {
            await tx.CommitAsync();
            Console.WriteLine("\nSelesai: perubahan DISIMPAN.");
        }
        else
        {
            await tx.RollbackAsync();
            Console.WriteLine("\nDry-run selesai: tidak ada yang diubah. Jalankan lagi dengan --apply untuk menyimpan.");
        }

        return 0;
    }
    catch (Exception ex)
    {
        try
        {
            await tx.RollbackAsync();
        }
        catch (InvalidOperationException)
        {
            // transaksi sudah berakhir
        }

        Console.Error.WriteLine($"GALAT, semua perubahan dibatalkan: {ex.Message}");
        return 1;
    }
}

static async Task<TableResult> SyncTable(SqlConnection src, SqlConnection dst, SqlTransaction tx, TablePlan plan, Options opt)
{
    var srcCols = await Columns(src, null, plan.Table);
    var dstCols = await Columns(dst, tx, plan.Table);
    if (srcCols.Count == 0 || dstCols.Count == 0)
    {
        throw new InvalidOperationException($"Tabel {plan.Table} tidak ada di sumber atau tujuan.");
    }

    var cols = dstCols.Where(c => !c.Computed && srcCols.Any(s => s.Name.Equals(c.Name, StringComparison.OrdinalIgnoreCase))).ToList();
    if (cols.Any(c => c.Identity))
    {
        throw new InvalidOperationException($"Tabel {plan.Table} punya kolom identity; belum didukung.");
    }

    foreach (var key in plan.Keys)
    {
        if (!cols.Any(c => c.Name.Equals(key, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Kolom kunci {plan.Table}.{key} tidak ada di salah satu sisi.");
        }
    }

    var names = cols.Select(c => c.Name).ToList();
    var temp = "#src_" + plan.Table;
    await Exec(dst, tx, $"SELECT TOP 0 {List(names)} INTO [{temp}] FROM [{plan.Table}]");

    // Baca sumber (SELECT saja) dan isi tabel sementara di tujuan.
    await using (var read = new SqlCommand($"SELECT {List(names)} FROM [{plan.Table}]", src) { CommandTimeout = 0 })
    await using (var reader = await read.ExecuteReaderAsync())
    {
        using var bulk = new SqlBulkCopy(dst, SqlBulkCopyOptions.Default, tx) { DestinationTableName = $"[{temp}]", BulkCopyTimeout = 0, BatchSize = 5000 };
        foreach (var n in names)
        {
            bulk.ColumnMappings.Add(n, n);
        }

        await bulk.WriteToServerAsync(reader);
    }

    var on = string.Join(" AND ", plan.Keys.Select(k => $"t.[{k}] = s.[{k}]"));
    var targetCount = (int)(await Scalar(dst, tx, $"SELECT COUNT(*) FROM [{plan.Table}]") ?? 0);
    var toDelete = (int)(await Scalar(dst, tx, $"SELECT COUNT(*) FROM [{plan.Table}] t WHERE NOT EXISTS (SELECT 1 FROM [{temp}] s WHERE {on})") ?? 0);
    var limit = Math.Max(10, (int)Math.Ceiling(targetCount * opt.MaxDeletePercent / 100.0));
    if (toDelete > limit && !opt.AllowMassDelete)
    {
        return new TableResult(0, 0, toDelete, true, limit);
    }

    var skip = opt.SkipPasswords && plan.Table.Equals("SCALLUSER", StringComparison.OrdinalIgnoreCase)
        ? new HashSet<string>(["SU_PWD", "SU_PWDEXPDATE"], StringComparer.OrdinalIgnoreCase)
        : [];
    var updatable = names.Where(n => !plan.Keys.Contains(n, StringComparer.OrdinalIgnoreCase) && !skip.Contains(n)).ToList();

    var merge = new StringBuilder();
    merge.Append("DECLARE @a TABLE (act nvarchar(10));\n");
    merge.Append(CultureInfo.InvariantCulture, $"MERGE [{plan.Table}] AS t USING [{temp}] AS s ON {on}\n");
    if (updatable.Count > 0)
    {
        merge.Append(CultureInfo.InvariantCulture, $"WHEN MATCHED AND EXISTS (SELECT {string.Join(", ", updatable.Select(n => $"s.[{n}]"))} EXCEPT SELECT {string.Join(", ", updatable.Select(n => $"t.[{n}]"))}) THEN UPDATE SET {string.Join(", ", updatable.Select(n => $"t.[{n}] = s.[{n}]"))}\n");
    }

    merge.Append(CultureInfo.InvariantCulture, $"WHEN NOT MATCHED BY TARGET THEN INSERT ({List(names)}) VALUES ({string.Join(", ", names.Select(n => $"s.[{n}]"))})\n");
    merge.Append("WHEN NOT MATCHED BY SOURCE THEN DELETE\n");
    merge.Append("OUTPUT $action INTO @a;\n");
    merge.Append("SELECT act, COUNT(*) FROM @a GROUP BY act;");

    int ins = 0, upd = 0, del = 0;
    await using (var cmd = new SqlCommand(merge.ToString(), dst, tx) { CommandTimeout = 0 })
    await using (var r = await cmd.ExecuteReaderAsync())
    {
        while (await r.ReadAsync())
        {
            var n = r.GetInt32(1);
            switch (r.GetString(0))
            {
                case "INSERT": ins = n; break;
                case "UPDATE": upd = n; break;
                case "DELETE": del = n; break;
            }
        }
    }

    await Exec(dst, tx, $"DROP TABLE [{temp}]");
    return new TableResult(ins, upd, del, false, limit);
}

static async Task<List<Col>> Columns(SqlConnection c, SqlTransaction? tx, string table)
{
    var list = new List<Col>();
    await using var cmd = new SqlCommand("SELECT name, is_computed, is_identity FROM sys.columns WHERE object_id = OBJECT_ID(@t) ORDER BY column_id", c, tx);
    cmd.Parameters.AddWithValue("@t", $"[dbo].[{table}]");
    await using var r = await cmd.ExecuteReaderAsync();
    while (await r.ReadAsync())
    {
        list.Add(new Col(r.GetString(0), r.GetBoolean(1), r.GetBoolean(2)));
    }

    return list;
}

static string List(IEnumerable<string> names) => string.Join(", ", names.Select(n => $"[{n}]"));

static async Task Exec(SqlConnection c, SqlTransaction tx, string sql)
{
    await using var cmd = new SqlCommand(sql, c, tx) { CommandTimeout = 0 };
    await cmd.ExecuteNonQueryAsync();
}

static async Task<int> ExecCount(SqlConnection c, SqlTransaction tx, string sql)
{
    await using var cmd = new SqlCommand(sql, c, tx) { CommandTimeout = 0 };
    return await cmd.ExecuteNonQueryAsync();
}

static async Task<object?> Scalar(SqlConnection c, SqlTransaction tx, string sql)
{
    await using var cmd = new SqlCommand(sql, c, tx) { CommandTimeout = 0 };
    return await cmd.ExecuteScalarAsync();
}

internal sealed record Col(string Name, bool Computed, bool Identity);

internal sealed record TablePlan(string Table, string[] Keys);

internal sealed record TableResult(int Inserted, int Updated, int Deleted, bool Aborted, int Limit);

internal sealed record Options(string? Source, string? Target, bool Apply, bool SkipPasswords, double MaxDeletePercent, bool AllowMassDelete, bool Help)
{
    public static Options Parse(string[] args)
    {
        string? source = Environment.GetEnvironmentVariable("PANIN_SYNC_SOURCE"), target = Environment.GetEnvironmentVariable("PANIN_SYNC_TARGET");
        bool apply = false, skip = false, mass = false, help = false;
        var percent = 5d;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--source": source = args[++i]; break;
                case "--target": target = args[++i]; break;
                case "--apply": apply = true; break;
                case "--skip-passwords": skip = true; break;
                case "--allow-mass-delete": mass = true; break;
                case "--max-delete-percent": percent = double.Parse(args[++i], CultureInfo.InvariantCulture); break;
                case "-h" or "--help": help = true; break;
                default: Console.Error.WriteLine($"Argumen tidak dikenal: {args[i]}"); help = true; break;
            }
        }

        return new Options(source, target, apply, skip, percent, mass, help);
    }
}

/// <summary>Urutan penting: group dan user dulu, lalu akses menu dan hierarki.</summary>
internal static class SyncPlan
{
    public static readonly TablePlan[] Tables =
    [
        new("SCALLGROUP", ["GROUPID"]),
        new("SCALLUSER", ["USERID"]),
        new("GRPMENUX", ["GROUPID", "TYPEID", "MENUID"]),
        new("TEMP_DOWNLINE", ["USERID", "DOWNLINE"]),
    ];
}
