using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Panin.Application.Abstractions;
using Panin.Application.Auth;
using Panin.Application.Collection;
using Panin.Application.Export;
using Panin.Application.Modules;
using Panin.Application.Sessions;
using Panin.Application.Status;
using Panin.Infrastructure.Auth;
using Panin.Infrastructure.Collection;
using Panin.Infrastructure.Data;
using Panin.Infrastructure.Export;
using Panin.Infrastructure.Modules;
using Panin.Infrastructure.Sessions;
using Panin.Infrastructure.Status;

namespace Panin.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddPaninInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var loginConnection = configuration.GetConnectionString("Login")
            ?? throw new InvalidOperationException("ConnectionStrings:Login belum diisi (gunakan user-secrets).");

        services.Configure<SessionOptions>(configuration.GetSection(SessionOptions.SectionName));
        services.AddMemoryCache();
        services.AddSingleton(TimeProvider.System);

        services.AddSingleton<ILoginDb>(_ => new LoginDb(loginConnection));
        services.AddSingleton<IUserSessionRegistry, UserSessionRegistry>();
        services.AddSingleton<IAuthService, AuthService>();
        services.AddSingleton<IModuleRegistry, ModuleRegistry>();
        services.AddSingleton<IModuleDbFactory, ModuleDbFactory>();
        services.AddSingleton<IMenuService, MenuService>();
        services.AddSingleton<IUserAreaService, UserAreaService>();
        services.AddSingleton<IDataStatusService, DataStatusService>();
        services.AddSingleton<IWarningService, WarningService>();
        services.AddSingleton<IDebiturSearchService, DebiturSearchService>();
        services.AddSingleton<IExcelExporter, NpoiExcelExporter>();
        services.AddSingleton<LogoutWriter>();
        services.AddSingleton<ILogoutRecorder>(sp => sp.GetRequiredService<LogoutWriter>());
        services.AddHostedService(sp => sp.GetRequiredService<LogoutWriter>());
        services.AddHostedService<SessionReaper>();
        return services;
    }
}
