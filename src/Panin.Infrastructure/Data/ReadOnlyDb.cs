using System.Data;
using System.Text.RegularExpressions;
using Panin.Application.Abstractions;

namespace Panin.Infrastructure.Data;

/// <summary>
/// Pengaman untuk DB bersama (mis. DB dev lama): hanya SELECT dan prosedur yang diizinkan secara eksplisit
/// (SP baca-saja) yang boleh lewat. Operasi tulis ditolak sebelum sampai ke server.
/// </summary>
public sealed partial class ReadOnlyDb(IDb inner, IReadOnlySet<string> allowedProcedures, string name) : IDb
{
    public Task<int> ExecuteAsync(string sql, object?[]? args = null, DbOptions? options = null, CancellationToken ct = default)
        => throw Blocked(sql);

    public Task<T?> ExecuteScalarAsync<T>(string sql, object?[]? args = null, DbOptions? options = null, CancellationToken ct = default)
        => Check(sql) ? inner.ExecuteScalarAsync<T>(sql, args, options, ct) : throw Blocked(sql);

    public Task<IReadOnlyList<T>> QueryAsync<T>(string sql, object?[]? args = null, DbOptions? options = null, CancellationToken ct = default)
        => Check(sql) ? inner.QueryAsync<T>(sql, args, options, ct) : throw Blocked(sql);

    public Task<T?> QueryFirstOrDefaultAsync<T>(string sql, object?[]? args = null, DbOptions? options = null, CancellationToken ct = default)
        => Check(sql) ? inner.QueryFirstOrDefaultAsync<T>(sql, args, options, ct) : throw Blocked(sql);

    public Task<DataTable> QueryTableAsync(string sql, object?[]? args = null, DbOptions? options = null, CancellationToken ct = default)
        => Check(sql) ? inner.QueryTableAsync(sql, args, options, ct) : throw Blocked(sql);

    public Task<DataSet> QueryDataSetAsync(string sql, object?[]? args = null, DbOptions? options = null, CancellationToken ct = default)
        => Check(sql) ? inner.QueryDataSetAsync(sql, args, options, ct) : throw Blocked(sql);

    public Task<DataRow?> QueryFirstOrDefaultAsync(string sql, object?[]? args = null, DbOptions? options = null, CancellationToken ct = default)
        => Check(sql) ? inner.QueryFirstOrDefaultAsync(sql, args, options, ct) : throw Blocked(sql);

    public ValueTask DisposeAsync() => inner.DisposeAsync();

    /// <summary>True bila pernyataan aman untuk DB baca-saja.</summary>
    public bool Check(string sql)
    {
        var s = sql.Trim();
        var exec = ExecRegex().Match(s);
        if (exec.Success)
        {
            return allowedProcedures.Contains(exec.Groups[1].Value);
        }

        return (s.StartsWith("select", StringComparison.OrdinalIgnoreCase) || s.StartsWith("with", StringComparison.OrdinalIgnoreCase))
            && !ForbiddenRegex().IsMatch(s);
    }

    private InvalidOperationException Blocked(string sql)
        => new($"Operasi ditolak: DB '{name}' dibuka baca-saja dan pernyataan ini tidak diizinkan: {(sql.Length > 80 ? sql[..80] : sql)}");

    [GeneratedRegex(@"^\s*exec(?:ute)?\s+(?:\[?dbo\]?\.)?\[?([A-Za-z0-9_]+)\]?", RegexOptions.IgnoreCase)]
    private static partial Regex ExecRegex();

    [GeneratedRegex(@"\b(insert|update|delete|merge|drop|alter|create|truncate|exec|execute|grant|revoke|into|backup|restore|kill|shutdown)\b|;", RegexOptions.IgnoreCase)]
    private static partial Regex ForbiddenRegex();
}
