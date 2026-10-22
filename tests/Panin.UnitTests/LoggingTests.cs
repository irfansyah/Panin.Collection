using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Panin.Application.Diagnostics;
using Panin.Infrastructure.Data;
using Panin.Web.Infrastructure.Logging;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Panin.UnitTests;

public class LoggingTests
{
    private sealed class Collector : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];

        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }

    private static string Prop(LogEvent e, string name) => e.Properties.TryGetValue(name, out var v) ? v.ToString().Trim('"') : string.Empty;

    private static (Serilog.ILogger Log, Collector Sink) NewLogger()
    {
        var sink = new Collector();
        return (new LoggerConfiguration().Enrich.With(new CorrelationEnricher()).WriteTo.Sink(sink).CreateLogger(), sink);
    }

    private static ActivityListener Listen()
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == Telemetry.SourceName,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    [Fact]
    public void Log_lines_carry_trace_circuit_and_user_from_the_current_flow()
    {
        using var listener = Listen();
        var (log, sink) = NewLogger();

        using (var activity = Telemetry.Begin("test"))
        using (CorrelationContext.Set(new CorrelationInfo("circuit-1", "U1", "G1", "S1")))
        {
            log.Error(new InvalidOperationException("boom"), "gagal");
        }

        var e = Assert.Single(sink.Events);
        Assert.Equal(32, Prop(e, "TraceId").Length);
        Assert.Equal(Prop(e, "TraceId")[..8].ToUpperInvariant(), Prop(e, "Ref"));
        Assert.Equal("circuit-1", Prop(e, "CircuitId"));
        Assert.Equal("U1", Prop(e, "UserId"));
        Assert.Equal("G1", Prop(e, "GroupId"));
        Assert.Equal("S1", Prop(e, "SessionId"));
        Assert.NotNull(e.Exception);
    }

    [Fact]
    public void Context_is_restored_after_the_scope_ends()
    {
        using (CorrelationContext.Set(new CorrelationInfo("c", "u", null, null)))
        {
            Assert.Equal("u", CorrelationContext.Current?.UserId);
        }

        Assert.Null(CorrelationContext.Current);
    }

    [Fact]
    public async Task Context_flows_across_awaits()
    {
        using var scope = CorrelationContext.Set(new CorrelationInfo("c", "u-async", null, null));
        await Task.Delay(5);
        Assert.Equal("u-async", CorrelationContext.Current?.UserId);
    }

    [Fact]
    public void Error_reference_matches_the_trace_id_prefix()
    {
        using var listener = Listen();
        using var activity = Telemetry.Begin("test");
        Assert.NotNull(activity);
        Assert.Equal(activity!.TraceId.ToString()[..8].ToUpperInvariant(), ErrorReference.Current.ToUpperInvariant());
        Assert.Equal($"(Kode: {ErrorReference.Current.ToUpperInvariant()})", ErrorReference.Tag);
    }

    [Fact]
    public void Error_reference_without_activity_is_still_usable()
    {
        Assert.Equal(ErrorReference.Length, ErrorReference.Current.Length);
    }

    private sealed class ListLoggerProvider : ILoggerProvider
    {
        public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = [];

        public Microsoft.Extensions.Logging.ILogger CreateLogger(string categoryName) => new L(Entries);

        public void Dispose()
        {
        }

        private sealed class L(List<(LogLevel, string, Exception?)> entries) : Microsoft.Extensions.Logging.ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
                => entries.Add((logLevel, formatter(state, exception), exception));
        }
    }

    [Fact]
    public void Failed_queries_are_logged_as_errors_with_the_exception_and_without_parameter_values()
    {
        var provider = new ListLoggerProvider();
        using var factory = LoggerFactory.Create(b => b.AddProvider(provider));
        var stats = new QueryStats(new ConfigurationBuilder().Build(), factory.CreateLogger<QueryStats>());

        var ex = new InvalidOperationException("timeout");
        stats.Observe("data:60", "exec SAVE_CALL_HISTORY @1, @2", TimeSpan.FromMilliseconds(30), null, failed: true, ex);

        var entry = Assert.Single(provider.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Same(ex, entry.Exception);
        Assert.Contains("SAVE_CALL_HISTORY", entry.Message, StringComparison.Ordinal);
        Assert.Contains("data:60", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Successful_fast_queries_are_not_logged()
    {
        var provider = new ListLoggerProvider();
        using var factory = LoggerFactory.Create(b => b.AddProvider(provider));
        var stats = new QueryStats(new ConfigurationBuilder().Build(), factory.CreateLogger<QueryStats>());

        stats.Observe("login", "select 1", TimeSpan.FromMilliseconds(3), 1, failed: false);

        Assert.Empty(provider.Entries);
    }
}
