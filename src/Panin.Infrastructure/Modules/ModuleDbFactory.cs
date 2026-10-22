using System.Collections.Concurrent;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Panin.Application.Abstractions;
using Panin.Application.Modules;
using Panin.Infrastructure.Data;

namespace Panin.Infrastructure.Modules;

public sealed class ModuleDbFactory(IConfiguration configuration, IModuleRegistry registry) : IModuleDbFactory
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

        return _cache.GetOrAdd(moduleId, _ => new SqlDb(connectionString));
    }
}
