using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Panin.Application.Abstractions;

namespace Panin.Infrastructure.Data;

public sealed record QueryStat(string Database, string Sql, long Count, long Failed, long Slow, double TotalMs, double MaxMs, long Rows)
{
    public double AverageMs => Count == 0 ? 0 : TotalMs / Count;
}

/// <summary>
/// Statistik query di memori (per teks SQL ternormalisasi) dan log peringatan untuk query lambat.
/// Ambang lambat: <c>Diagnostics:SlowQueryMs</c> (default 1000 ms).
/// </summary>
public sealed partial class QueryStats(IConfiguration configuration, ILogger<QueryStats> logger) : IQueryObserver
{
    private const int KeyLength = 160;
    private const int MaxTrackedQueries = 2000;

    private readonly ConcurrentDictionary<string, Entry> _entries = new();
    private readonly double _slowMs = configuration.GetValue("Diagnostics:SlowQueryMs", 1000d);

    public void Observe(string database, string sql, TimeSpan elapsed, int? rows, bool failed, Exception? exception = null)
    {
        var ms = elapsed.TotalMilliseconds;
        var key = database + "|" + Normalize(sql);
        if (_entries.Count >= MaxTrackedQueries && !_entries.ContainsKey(key))
        {
            key = database + "|(lainnya)";
        }

        var entry = _entries.GetOrAdd(key, k => new Entry(database, k[(database.Length + 1)..]));
        entry.Add(ms, rows, failed, ms >= _slowMs);

        if (failed)
        {
            // Galat DB selalu dicatat di sini dengan TraceId/UserId alur pemanggil, apa pun yang dilakukan pemanggil sesudahnya.
            LogFailed(logger, exception, database, (long)ms, Truncate(sql), exception is Microsoft.Data.SqlClient.SqlException sqlEx ? sqlEx.Number : null);
        }
        else if (ms >= _slowMs)
        {
            LogSlow(logger, database, (long)ms, rows, entry.Sql);
        }
    }

    /// <summary>Query dengan total waktu terbesar (kandidat optimasi), bukan hanya yang paling lambat sekali jalan.</summary>
    public IReadOnlyList<QueryStat> Top(int count = 20)
        => [.. _entries.Values.Select(e => e.Snapshot()).OrderByDescending(s => s.TotalMs).Take(count)];

    private static string Normalize(string sql)
    {
        var s = Whitespace().Replace(sql.Trim(), " ");
        return s.Length > KeyLength ? s[..KeyLength] : s;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Query gagal di {Database} setelah {ElapsedMs} ms (SQL error {SqlError}): {Sql}")]
    private static partial void LogFailed(ILogger logger, Exception? exception, string database, long elapsedMs, string sql, int? sqlError);

    private static string Truncate(string sql)
    {
        var s = Whitespace().Replace(sql.Trim(), " ");
        return s.Length > 500 ? s[..500] + "…" : s;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Query lambat di {Database}: {ElapsedMs} ms, {Rows} baris: {Sql}")]
    private static partial void LogSlow(ILogger logger, string database, long elapsedMs, int? rows, string sql);

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    private sealed class Entry(string database, string sql)
    {
        private readonly Lock _gate = new();
        private long _count, _failed, _slow, _rows;
        private double _total, _max;

        public string Sql { get; } = sql;

        public void Add(double ms, int? rows, bool failed, bool slow)
        {
            lock (_gate)
            {
                _count++;
                _total += ms;
                _max = Math.Max(_max, ms);
                _rows += rows ?? 0;
                if (failed)
                {
                    _failed++;
                }

                if (slow)
                {
                    _slow++;
                }
            }
        }

        public QueryStat Snapshot()
        {
            lock (_gate)
            {
                return new QueryStat(database, Sql, _count, _failed, _slow, _total, _max, _rows);
            }
        }
    }
}
