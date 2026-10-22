using System.Data;
using Microsoft.Data.SqlClient;
using Panin.Application.Abstractions;

namespace Panin.Infrastructure.Data;

public sealed class SqlDb(string connectionString) : IDb
{
    private readonly string _connectionString = connectionString;

    public async Task<int> ExecuteAsync(string sql, object?[]? args = null, DbOptions? options = null, CancellationToken ct = default)
    {
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = CreateCommand(conn, sql, args, options);
        return await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<DataTable> QueryTableAsync(string sql, object?[]? args = null, DbOptions? options = null, CancellationToken ct = default)
    {
        var set = await QueryDataSetAsync(sql, args, options, ct).ConfigureAwait(false);
        return set.Tables.Count > 0 ? set.Tables[0] : new DataTable();
    }

    public async Task<DataSet> QueryDataSetAsync(string sql, object?[]? args = null, DbOptions? options = null, CancellationToken ct = default)
    {
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = CreateCommand(conn, sql, args, options);
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var set = new DataSet();
        do
        {
            var table = new DataTable();
            table.Load(reader);
            set.Tables.Add(table);
        }
        while (!reader.IsClosed && await reader.NextResultAsync(ct).ConfigureAwait(false));

        return set;
    }

    public async Task<DataRow?> QueryFirstOrDefaultAsync(string sql, object?[]? args = null, DbOptions? options = null, CancellationToken ct = default)
    {
        var table = await QueryTableAsync(sql, args, options, ct).ConfigureAwait(false);
        return table.Rows.Count > 0 ? table.Rows[0] : null;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static SqlCommand CreateCommand(SqlConnection conn, string sql, object?[]? args, DbOptions? options)
    {
        options ??= new DbOptions();
        var cmd = conn.CreateCommand();
        cmd.CommandText = DbCommandPreparer.PrepareSql(sql, args?.Length ?? 0);
        cmd.Parameters.AddRange(DbCommandPreparer.BuildParameters(args, options.BlankToNull));
        if (options.TimeoutSeconds > 0)
        {
            cmd.CommandTimeout = options.TimeoutSeconds;
        }

        return cmd;
    }
}
