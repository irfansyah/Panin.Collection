using System.Collections.Concurrent;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Panin.Application.Abstractions;
using Panin.Application.Modules;
using Panin.Infrastructure.Data;

namespace Panin.Infrastructure.Modules;

public sealed class ModuleDbFactory(IConfiguration configuration, IModuleRegistry registry, IQueryObserver observer) : IModuleDbFactory
{
    private readonly ConcurrentDictionary<string, IDb> _cache = new();

    public async Task<IDb> GetDataDbAsync(string moduleId, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(moduleId, out var cached))
        {
            return cached;
        }

        var configured = configuration[$"ConnectionStrings:Data:{moduleId}"];
        string connectionString;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            connectionString = configured;
        }
        else
        {
            var record = await registry.GetRecordAsync(moduleId, ct).ConfigureAwait(false);
            if (record is null || record.DbServer is null || record.DbName is null || record.DbUser is null)
            {
                throw new InvalidOperationException(
                    $"Koneksi DB DATA modul {moduleId} belum dikonfigurasi. Isi ConnectionStrings:Data:{moduleId} di user-secrets.");
            }

            // Paritas dengan sistem lama (kolom DB_* di RFMODULE). Jalur utama tetap konfigurasi/secrets.
            connectionString = new SqlConnectionStringBuilder
            {
                DataSource = record.DbServer,
                InitialCatalog = record.DbName,
                UserID = record.DbUser,
                Password = record.DbPassword ?? string.Empty,
                TrustServerCertificate = true,
            }.ConnectionString;
        }

        return _cache.GetOrAdd(moduleId, _ => Wrap(moduleId, connectionString));
    }

    private IDb Wrap(string moduleId, string connectionString)
    {
        IDb db = new SqlDb(connectionString, $"data:{moduleId}", observer);

        // Baca-saja bila diminta lewat konfigurasi, atau bila connection string sendiri berniat ReadOnly
        // (dipakai untuk DB dev bersama yang tidak boleh ditulis dari mesin pengembang).
        var setting = configuration[$"DataAccess:ReadOnly:{moduleId}"];
        var readOnly = bool.TryParse(setting, out var forced)
            ? forced
            : new SqlConnectionStringBuilder(connectionString).ApplicationIntent == ApplicationIntent.ReadOnly;
        if (!readOnly)
        {
            return db;
        }

        var allowed = configuration.GetSection("DataAccess:ReadOnlyProcedures").Get<string[]>() ?? ["SP_VW_WARNINGCONTENT"];
        return new ReadOnlyDb(db, new HashSet<string>(allowed, StringComparer.OrdinalIgnoreCase), $"modul {moduleId}");
    }
}
