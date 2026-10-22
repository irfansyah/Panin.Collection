using System.Data;
using System.Diagnostics;
using Dapper;
using Microsoft.Data.SqlClient;
using Panin.Application.Abstractions;

namespace Panin.Infrastructure.Data;

/// <summary>
/// Implementasi <see cref="IDb"/> di atas Dapper + Microsoft.Data.SqlClient. Satu koneksi per panggilan (pooling).
/// Setiap panggilan diukur dan dilaporkan ke <see cref="IQueryObserver"/> (statistik dan log query lambat).
/// </summary>
public class SqlDb(string connectionString, string name = "db", IQueryObserver? observer = null) : IDb
{
    private readonly string _connectionString = connectionString;

    public Task<int> ExecuteAsync(string sql, object?[]? args = null, DbOptions? options = null, CancellationToken ct = default)
        => Measure(sql, async () =>
        {
            await using var conn = await OpenAsync(ct).ConfigureAwait(false);
            return await conn.ExecuteAsync(Command(sql, args, options, ct)).ConfigureAwait(false);
        });

    public Task<T?> ExecuteScalarAsync<T>(string sql, object?[]? args = null, DbOptions? options = null, CancellationToken ct = default)
        => Measure(sql, async () =>
        {
            await using var conn = await OpenAsync(ct).ConfigureAwait(false);
            return await conn.ExecuteScalarAsync<T>(Command(sql, args, options, ct)).ConfigureAwait(false);
        });

    public Task<IReadOnlyList<T>> QueryAsync<T>(string sql, object?[]? args = null, DbOptions? options = null, CancellationToken ct = default)
        => Measure(sql, async () =>
        {
            await using var conn = await OpenAsync(ct).ConfigureAwait(false);
            var rows = await conn.QueryAsync<T>(Command(sql, args, options, ct)).ConfigureAwait(false);
            return (IReadOnlyList<T>)rows.AsList();
        }, rows => rows.Count);

    public Task<T?> QueryFirstOrDefaultAsync<T>(string sql, object?[]? args = null, DbOptions? options = null, CancellationToken ct = default)
        => Measure(sql, async () =>
        {
            await using var conn = await OpenAsync(ct).ConfigureAwait(false);
            return await conn.QueryFirstOrDefaultAsync<T>(Command(sql, args, options, ct)).ConfigureAwait(false);
        });

    public async Task<DataTable> QueryTableAsync(string sql, object?[]? args = null, DbOptions? options = null, CancellationToken ct = default)
    {
        var set = await QueryDataSetAsync(sql, args, options, ct).ConfigureAwait(false);
        return set.Tables.Count > 0 ? set.Tables[0] : new DataTable();
    }

    public Task<DataSet> QueryDataSetAsync(string sql, object?[]? args = null, DbOptions? options = null, CancellationToken ct = default)
        => Measure(sql, async () =>
        {
            await using var conn = await OpenAsync(ct).ConfigureAwait(false);
            await using var reader = await conn.ExecuteReaderAsync(Command(sql, args, options, ct)).ConfigureAwait(false);
            var set = new DataSet();
            do
            {
                var table = new DataTable();
                table.Load(reader);
                set.Tables.Add(table);
            }
            while (!reader.IsClosed && await reader.NextResultAsync(ct).ConfigureAwait(false));

            return set;
        }, set => set.Tables.Count > 0 ? set.Tables[0].Rows.Count : 0);

    public async Task<DataRow?> QueryFirstOrDefaultAsync(string sql, object?[]? args = null, DbOptions? options = null, CancellationToken ct = default)
    {
        var table = await QueryTableAsync(sql, args, options, ct).ConfigureAwait(false);
        return table.Rows.Count > 0 ? table.Rows[0] : null;
    }

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }

    private async Task<T> Measure<T>(string sql, Func<Task<T>> action, Func<T, int>? rowCount = null)
    {
        if (observer is null)
        {
            return await action().ConfigureAwait(false);
        }

        var started = Stopwatch.GetTimestamp();
        try
        {
            var result = await action().ConfigureAwait(false);
            observer.Observe(name, sql, Stopwatch.GetElapsedTime(started), rowCount?.Invoke(result), failed: false);
            return result;
        }
        catch (Exception ex)
        {
            observer.Observe(name, sql, Stopwatch.GetElapsedTime(started), null, failed: true, ex);
            throw;
        }
    }

    private async Task<SqlConnection> OpenAsync(CancellationToken ct)
    {
        var conn = new SqlConnection(_connectionString);
        try
        {
            await conn.OpenAsync(ct).ConfigureAwait(false);
            return conn;
        }
        catch
        {
            await conn.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static CommandDefinition Command(string sql, object?[]? args, DbOptions? options, CancellationToken ct)
    {
        options ??= new DbOptions();
        return new CommandDefinition(
            DbCommandPreparer.PrepareSql(sql, args?.Length ?? 0),
            DbCommandPreparer.BuildDapperParameters(args, options.BlankToNull),
            commandTimeout: options.TimeoutSeconds > 0 ? options.TimeoutSeconds : null,
            cancellationToken: ct);
    }
}
