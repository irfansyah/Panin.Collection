using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Panin.Application.Abstractions;
using Panin.Application.Auth;
using Panin.Application.Collection;
using Panin.Application.Export;
using Panin.Application.Letters;
using Panin.Application.Maintenance;
using Panin.Application.Modules;
using Panin.Application.Reports;
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
        services.AddSingleton<SingleFlightCache>();
        services.AddSingleton(TimeProvider.System);

        services.AddSingleton<QueryStats>();
        services.AddSingleton<IQueryObserver>(sp => sp.GetRequiredService<QueryStats>());
        services.AddSingleton<ILoginDb>(sp => new LoginDb(loginConnection, sp.GetRequiredService<IQueryObserver>()));
        services.AddSingleton<IUserSessionRegistry, UserSessionRegistry>();
        services.AddSingleton<IAuthService, AuthService>();
        services.AddSingleton<IModuleRegistry, ModuleRegistry>();
        services.AddSingleton<IModuleDbFactory, ModuleDbFactory>();
        services.AddSingleton<IMenuService, MenuService>();
        services.AddSingleton<IUserAreaService, UserAreaService>();
        services.AddSingleton<IDataStatusService, DataStatusService>();
        services.AddSingleton<IWarningService, WarningService>();
        services.AddSingleton<IDebiturSearchService, DebiturSearchService>();
        services.AddSingleton<IDebiturDetailService, DebiturDetailService>();
        services.AddSingleton<IAccountMonitoringService, AccountMonitoringService>();
        services.AddSingleton<ICallService, CallService>();
        services.AddSingleton<IVisitService, VisitService>();
        services.AddSingleton<IWorklistService, WorklistService>();
        services.AddSingleton<ILetterService, LetterService>();
        services.AddSingleton<IActivityMonitoringService, ActivityMonitoringService>();
        services.AddSingleton<Panin.Application.Reports.Pivot.ICustomPivotCatalog, Panin.Infrastructure.Reports.Custom.CustomPivotCatalog>();
        services.AddSingleton<IReportService, Panin.Infrastructure.Reports.ReportService>();
        services.Configure<Maintenance.MaintenanceOptions>(configuration.GetSection(Maintenance.MaintenanceOptions.SectionName));
        services.Configure<ParallelRunOptions>(configuration.GetSection(ParallelRunOptions.SectionName));
        services.Configure<BulkLetterOptions>(configuration.GetSection(BulkLetterOptions.SectionName));
        services.AddSingleton<IBulkLetterService, Letters.BulkLetterService>();
        services.AddSingleton<IExceptionLetterService, Letters.ExceptionLetterService>();
        services.AddSingleton<IUserMaintenanceService, Maintenance.UserMaintenanceService>();
        services.AddSingleton<IGroupMaintenanceService, Maintenance.GroupMaintenanceService>();
        services.AddSingleton<IParameterService, Maintenance.ParameterService>();
        services.AddSingleton<IContactReferenceService, ContactReferenceService>();
        services.AddSingleton<ITreatmentMenuService, TreatmentMenuService>();
        services.AddSingleton<IExcelExporter, NpoiExcelExporter>();
        services.AddSingleton<LogoutWriter>();
        services.AddSingleton<ILogoutRecorder>(sp => sp.GetRequiredService<LogoutWriter>());
        services.AddHostedService(sp => sp.GetRequiredService<LogoutWriter>());
        services.AddHostedService<SessionReaper>();
        return services;
    }
}
