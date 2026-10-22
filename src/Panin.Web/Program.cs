using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.AspNetCore.HttpOverrides;
using Panin.Application.Auth;
using Panin.Application.Modules;
using Panin.Application.Sessions;
using Panin.Infrastructure;
using Panin.Web.Components;
using Panin.Web.Infrastructure;
using Panin.Web.Shell;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddPaninInfrastructure(builder.Configuration);
builder.Services.AddSingleton<IEnumerable<ModuleDefinition>>(ModuleCatalog.Definitions);

builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<AuthenticationStateProvider, SessionRevalidatingAuthenticationStateProvider>();
builder.Services.AddSingleton<CircuitTracker>();
builder.Services.AddSingleton<CircuitHandler>(sp => sp.GetRequiredService<CircuitTracker>());
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
app.MapHealthChecks("/healthz").AllowAnonymous();

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

/// <summary>Dibutuhkan oleh WebApplicationFactory pada uji integrasi.</summary>
public partial class Program;
