using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;

namespace Panin.Web.Infrastructure.Logging;

/// <summary>Satu laporan galat dari browser (JavaScript error, promise yang ditolak, gagal sambung sirkuit).</summary>
public sealed record ClientLogEntry(string? Kind, string? Message, string? Source, int? Line, int? Column, string? Stack, string? Url, string? PageTrace);

public static class ClientLogEndpoint
{
    public const string PolicyName = "client-log";
    private const int MaxText = 2000;
    private const int MaxStack = 6000;

    public static void AddClientLogRateLimit(this IServiceCollection services)
        => services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            // Per alamat IP: galat berulang dari satu browser tidak boleh membanjiri log.
            o.AddPolicy(PolicyName, http => RateLimitPartition.GetFixedWindowLimiter(
                http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        });

    public static IEndpointRouteBuilder MapClientLog(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/client-log", (ClientLogEntry entry, HttpContext http, ILoggerFactory loggers) =>
        {
            var logger = loggers.CreateLogger("ClientLog");
            // Properti terstruktur (bukan templat) dengan panjang dibatasi: isi dari browser tidak dipercaya.
            logger.LogWarning("Galat di browser {Kind}: {Message} @ {Source}:{Line}:{Column} halaman {Url} halaman-trace {PageTrace}\n{Stack}",
                Cut(entry.Kind, 40), Cut(entry.Message, MaxText), Cut(entry.Source, 300), entry.Line, entry.Column,
                Cut(entry.Url, 300), Cut(entry.PageTrace, 64), Cut(entry.Stack, MaxStack));
            return Results.NoContent();
        }).AllowAnonymous().DisableAntiforgery().RequireRateLimiting(PolicyName)
          .WithMetadata(new RequestSizeLimitAttribute(16 * 1024));
        return app;
    }

    private static string? Cut(string? value, int max) => value is null ? null : value.Length <= max ? value : value[..max];

    /// <summary>Menambahkan header <c>X-Trace-Id</c> ke setiap respons agar browser/proksi/log bisa dicocokkan.</summary>
    public static IApplicationBuilder UseTraceIdHeader(this IApplicationBuilder app)
        => app.Use(async (context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                var id = Activity.Current?.TraceId.ToString();
                if (!string.IsNullOrEmpty(id))
                {
                    context.Response.Headers["X-Trace-Id"] = id;
                }

                return Task.CompletedTask;
            });
            await next(context);
        });
}
