using System.Diagnostics;
using Panin.Application.Diagnostics;
using Panin.Worker;
using Panin.Worker.Upload;
using Serilog;
using Serilog.Core;
using Serilog.Events;

Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateBootstrapLogger();
try
{
    var builder = Host.CreateApplicationBuilder(args);
    builder.Services.AddWindowsService(o => o.ServiceName = "Panin.Worker");
    builder.Services.AddSerilog((services, cfg) => cfg
        .ReadFrom.Configuration(builder.Configuration)
        .Enrich.With<ActivityEnricher>()
        .Enrich.WithProperty("Application", "Panin.Worker")
        .Enrich.WithProperty("Machine", Environment.MachineName));

    ActivitySource.AddActivityListener(new ActivityListener
    {
        ShouldListenTo = s => s.Name == Telemetry.SourceName,
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    });

    var options = builder.Configuration.GetSection(UploadOptions.SectionName).Get<UploadOptions>() ?? new UploadOptions();
    var errors = options.Validate();
    if (errors.Count > 0)
    {
        throw new InvalidOperationException("Konfigurasi Upload tidak valid: " + string.Join("; ", errors));
    }

    var connection = builder.Configuration.GetSection("ConnectionStrings:Data")["60"] ?? string.Empty;
    if (options.Mode != UploadMode.Off && string.IsNullOrWhiteSpace(connection))
    {
        throw new InvalidOperationException("ConnectionStrings:Data:60 (DB DATA modul 60) wajib diisi");
    }

    builder.Services.AddSingleton(options);
    builder.Services.AddSingleton<IUploadFiles, PhysicalUploadFiles>();
    builder.Services.AddSingleton<IUploadDatabase>(_ => new SqlUploadDatabase(connection));
    builder.Services.AddSingleton<UploadCycle>();
    builder.Services.AddHostedService<UploadWorker>();

    builder.Build().Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "Worker berhenti karena galat");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}

return 0;

internal sealed class ActivityEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory factory)
    {
        if (Activity.Current is { } a && a.TraceId != default)
        {
            var trace = a.TraceId.ToString();
            logEvent.AddPropertyIfAbsent(factory.CreateProperty("TraceId", trace));
            logEvent.AddPropertyIfAbsent(factory.CreateProperty("Ref", trace[..ErrorReference.Length].ToUpperInvariant()));
        }
    }
}
