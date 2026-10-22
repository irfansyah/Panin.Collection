using System.Diagnostics;
using Panin.Application.Diagnostics;
using Serilog.Core;
using Serilog.Events;

namespace Panin.Web.Infrastructure.Logging;

/// <summary>
/// Menambahkan ke setiap baris log: <c>TraceId</c>/<c>SpanId</c> (dari <see cref="Activity"/>), <c>Ref</c> (kode singkat yang dilihat pengguna),
/// serta <c>CircuitId</c>, <c>UserId</c>, <c>GroupId</c>, <c>SessionId</c> dari <see cref="CorrelationContext"/> atau, untuk request HTTP, dari HttpContext.
/// </summary>
public sealed class CorrelationEnricher(IHttpContextAccessor? http = null) : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory factory)
    {
        var activity = Activity.Current;
        if (activity is not null && activity.TraceId != default)
        {
            var trace = activity.TraceId.ToString();
            logEvent.AddPropertyIfAbsent(factory.CreateProperty("TraceId", trace));
            logEvent.AddPropertyIfAbsent(factory.CreateProperty("SpanId", activity.SpanId.ToString()));
            logEvent.AddPropertyIfAbsent(factory.CreateProperty("Ref", trace[..ErrorReference.Length].ToUpperInvariant()));
        }

        var info = CorrelationContext.Current;
        string? user = info?.UserId, group = info?.GroupId, session = info?.SessionId;
        if (user is null && http?.HttpContext?.User.ToSessionUser() is { } u)
        {
            (user, group, session) = (u.UserId, u.GroupId, u.SessionId);
        }

        Add(logEvent, factory, "CircuitId", info?.CircuitId);
        Add(logEvent, factory, "UserId", user);
        Add(logEvent, factory, "GroupId", group);
        Add(logEvent, factory, "SessionId", session);
    }

    private static void Add(LogEvent logEvent, ILogEventPropertyFactory factory, string name, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            logEvent.AddPropertyIfAbsent(factory.CreateProperty(name, value));
        }
    }
}
