using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Panin.Infrastructure.Data;

namespace Panin.UnitTests;

public class QueryStatsTests
{
    private static QueryStats Create(double slowMs = 1000)
        => new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Diagnostics:SlowQueryMs"] = slowMs.ToString(System.Globalization.CultureInfo.InvariantCulture) }).Build(),
            NullLogger<QueryStats>.Instance);

    [Fact]
    public void Aggregates_by_normalized_sql()
    {
        var stats = Create();
        stats.Observe("login", "select  *\n from   T where A = @1", TimeSpan.FromMilliseconds(10), 3, failed: false);
        stats.Observe("login", "select * from T where A = @1", TimeSpan.FromMilliseconds(30), 5, failed: false);

        var s = Assert.Single(stats.Top());
        Assert.Equal(2, s.Count);
        Assert.Equal(40, s.TotalMs, precision: 3);
        Assert.Equal(30, s.MaxMs, precision: 3);
        Assert.Equal(8, s.Rows);
        Assert.Equal(20, s.AverageMs, precision: 3);
    }

    [Fact]
    public void Top_orders_by_total_time_not_by_single_slowest_call()
    {
        var stats = Create();
        stats.Observe("d", "frequent", TimeSpan.FromMilliseconds(100), null, failed: false);
        stats.Observe("d", "frequent", TimeSpan.FromMilliseconds(100), null, failed: false);
        stats.Observe("d", "frequent", TimeSpan.FromMilliseconds(100), null, failed: false);
        stats.Observe("d", "rare", TimeSpan.FromMilliseconds(250), null, failed: false);

        Assert.Equal(["frequent", "rare"], stats.Top().Select(s => s.Sql));
    }

    [Fact]
    public void Counts_slow_and_failed_calls()
    {
        var stats = Create(slowMs: 50);
        stats.Observe("d", "q", TimeSpan.FromMilliseconds(10), null, failed: false);
        stats.Observe("d", "q", TimeSpan.FromMilliseconds(80), null, failed: false);
        stats.Observe("d", "q", TimeSpan.FromMilliseconds(5), null, failed: true);

        var s = Assert.Single(stats.Top());
        Assert.Equal(1, s.Slow);
        Assert.Equal(1, s.Failed);
    }

    [Fact]
    public void Same_sql_in_different_databases_is_tracked_separately()
    {
        var stats = Create();
        stats.Observe("login", "select 1", TimeSpan.FromMilliseconds(1), null, failed: false);
        stats.Observe("data:60", "select 1", TimeSpan.FromMilliseconds(1), null, failed: false);
        Assert.Equal(2, stats.Top().Count);
    }
}
