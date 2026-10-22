using System.Diagnostics;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.AspNetCore.HttpOverrides;
using Panin.Application.Auth;
using Panin.Application.Diagnostics;
using Panin.Application.Letters;
using Panin.Application.Memos;
using Panin.Application.Modules;
using Panin.Application.Sessions;
using Panin.Infrastructure;
using Panin.Infrastructure.Data;
using Panin.Web.Components;
using Panin.Web.Infrastructure;
using Panin.Web.Infrastructure.Logging;
using Panin.Web.Shell;
using Serilog;

// Logger awal: galat saat start (konfigurasi, koneksi) pun harus tercatat sebelum logger utama siap.
Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateBootstrapLogger();
AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Fatal(e.ExceptionObject as Exception, "Galat tak tertangani di luar request (proses {Terminating})", e.IsTerminating ? "berhenti" : "lanjut");
TaskScheduler.UnobservedTaskException += (_, e) =>
{
    Log.Error(e.Exception, "Task gagal tanpa ada yang menunggu hasilnya");
    e.SetObserved();
};

// Tanpa pendengar, Activity (TraceId) tidak dibuat; dengan ini setiap request, interaksi sirkuit, dan pekerjaan latar punya TraceId.
ActivitySource.AddActivityListener(new ActivityListener
{
    ShouldListenTo = source => source.Name is Telemetry.SourceName or "Microsoft.AspNetCore",
    Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
});

try
{
var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, logger) => logger
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.With(new CorrelationEnricher(services.GetService<IHttpContextAccessor>()))
    .Enrich.WithProperty("Application", "Panin.Collection")
    .Enrich.WithProperty("Machine", Environment.MachineName));
builder.Services.AddHttpContextAccessor();
builder.Services.AddClientLogRateLimit();

builder.Services.AddPaninInfrastructure(builder.Configuration);
builder.Services.AddSingleton<IEnumerable<ModuleDefinition>>(ModuleCatalog.Definitions);

builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<AuthenticationStateProvider, SessionRevalidatingAuthenticationStateProvider>();
builder.Services.AddSingleton<CircuitTracker>();
builder.Services.AddSingleton<CircuitHandler>(sp => sp.GetRequiredService<CircuitTracker>());
builder.Services.AddScoped<CircuitCorrelationHandler>();
builder.Services.AddScoped<CircuitHandler>(sp => sp.GetRequiredService<CircuitCorrelationHandler>());
builder.Services.AddScoped<ICircuitContext>(sp => sp.GetRequiredService<CircuitCorrelationHandler>());
builder.Services.AddScoped<WorkspaceState>();
builder.Services.AddSingleton<ModulePageCatalog>();
builder.Services.AddDataProtection();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "panin.auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/login";
        options.ExpireTimeSpan = TimeSpan.FromHours(12);
        options.SlidingExpiration = false;
        options.Events.OnValidatePrincipal = async context =>
        {
            var user = context.Principal?.ToSessionUser();
            var registry = context.HttpContext.RequestServices.GetRequiredService<IUserSessionRegistry>();
            if (user is null || !registry.Validate(user.UserId, user.GroupId, user.SessionId, user.Host))
            {
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            }
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddHealthChecks();

builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

var app = builder.Build();

app.UseForwardedHeaders();
app.UseTraceIdHeader();
app.UseSerilogRequestLogging(o =>
{
    // Satu baris per request; aset statis dan health check tidak dicatat agar log tetap terbaca.
    o.GetLevel = (http, _, ex) => ex is not null || http.Response.StatusCode >= 500 ? Serilog.Events.LogEventLevel.Error
        : http.Response.StatusCode >= 400 ? Serilog.Events.LogEventLevel.Warning
        : http.Request.Path.StartsWithSegments("/healthz") || http.Request.Path.StartsWithSegments("/_framework") || http.Request.Path.StartsWithSegments("/_blazor") || http.Request.Path.Value!.Contains('.', StringComparison.Ordinal)
            ? Serilog.Events.LogEventLevel.Verbose : Serilog.Events.LogEventLevel.Information;
});
app.UseRateLimiter();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();

// Gambar berita lama (Image/News/*) berada di server aplikasi lama; salin foldernya lalu isi News:ImageDirectory agar tampil di halaman News.
if (app.Configuration["News:ImageDirectory"] is { Length: > 0 } newsImages && Directory.Exists(newsImages))
{
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(Path.GetFullPath(newsImages)),
        RequestPath = "/Image/News",
    });
}
app.MapHealthChecks("/healthz").AllowAnonymous();
app.MapClientLog();

// Unduh berkas bulk surat: hanya pemilik bulk (di aplikasi lama berkas ini bisa diunduh siapa pun yang tahu URL-nya).
app.MapGet("/api/surat/{bulkId:guid}", async (Guid bulkId, HttpContext http, IBulkLetterService letters) =>
{
    var user = http.User.ToSessionUser();
    var file = user is null ? null : await letters.OpenFileAsync(user.UserId, bulkId, http.RequestAborted);
    return file is null ? Results.NotFound() : Results.File(file.Content, "application/vnd.ms-excel", file.FileName);
}).RequireAuthorization();

// Memo cetak (Laporan Recovery): hanya untuk group yang memiliki menu memo itu; ?doc=1 mengunduh sebagai Word seperti layar lama.
app.MapGet("/api/memo/{slug}/{accountNo}", async (string slug, string accountNo, string? doc, HttpContext http, IMemoService memos, IMenuService menus) =>
{
    var user = http.User.ToSessionUser();
    if (user is null || !MemoRules.TryParseSlug(slug, out var kind) || !MemoRules.IsValidAccount(accountNo))
    {
        return Results.NotFound();
    }

    static bool Has(IEnumerable<MenuNode> nodes, string id) => nodes.Any(n => n.Id == id || Has(n.Children, id));
    if (!Has(await menus.GetMenuAsync("60", user.GroupId, http.RequestAborted), MemoRules.MenuId(kind)))
    {
        return Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    var memo = await memos.BuildAsync(kind, accountNo, http.RequestAborted);
    if (memo is null)
    {
        return Results.NotFound();
    }

    http.Response.Headers.CacheControl = "no-store";
    return doc == "1"
        ? Results.File(System.Text.Encoding.UTF8.GetBytes(memo.Html), "application/msword", memo.FileName)
        : Results.Content(memo.Html, "text/html; charset=utf-8");
}).RequireAuthorization();

app.MapPost("/auth/logout", async (HttpContext http, IAuthService auth, IAntiforgery antiforgery) =>
{
    await antiforgery.ValidateRequestAsync(http);
    var user = http.User.ToSessionUser();
    if (user is not null)
    {
        await auth.LogoutAsync(user.UserId, user.GroupId, user.SessionId, user.Host, http.RequestAborted);
    }

    await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/login");
}).RequireAuthorization();

if (app.Configuration.GetValue("Diagnostics:Enabled", false))
{
    app.MapGet("/_diag/queries", (QueryStats stats, int? top) => Results.Json(stats.Top(top ?? 20)
        .Select(s => new { s.Database, s.Sql, s.Count, s.Failed, s.Slow, totalMs = (long)s.TotalMs, avgMs = (long)s.AverageMs, maxMs = (long)s.MaxMs, s.Rows }))).AllowAnonymous();

    app.MapGet("/_diag", (CircuitTracker circuits, IUserSessionRegistry sessions, bool? gc) =>
    {
        if (gc == true)
        {
            // Hanya untuk pengukuran: paksa GC agar angka memori tidak berisi sampah yang belum dikumpulkan.
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        }

        var info = GC.GetGCMemoryInfo();
        return Results.Json(new
        {
            circuits = circuits.Active,
            circuitsOpenedTotal = circuits.OpenedTotal,
            sessions = sessions.ActiveCount,
            managedMemoryMb = GC.GetTotalMemory(false) / 1024 / 1024,
            workingSetMb = Environment.WorkingSet / 1024 / 1024,
            heapMb = info.HeapSizeBytes / 1024 / 1024,
            gen2Collections = GC.CollectionCount(2),
        });
    }).AllowAnonymous();
}

app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "Aplikasi berhenti karena galat saat start atau saat berjalan");
    throw;
}
finally
{
    Log.CloseAndFlush();
}

/// <summary>Dibutuhkan oleh WebApplicationFactory pada uji integrasi.</summary>
public partial class Program;
