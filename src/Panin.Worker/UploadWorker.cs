using Panin.Application.Diagnostics;
using Panin.Worker.Upload;

namespace Panin.Worker;

/// <summary>Memanggil <see cref="UploadCycle"/> tiap <c>PollSeconds</c>. Tidak pernah berhenti karena galat; setiap putaran adalah satu Activity (TraceId).</summary>
public sealed class UploadWorker(UploadOptions options, UploadCycle cycle, ILogger<UploadWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Worker unggah start: mode={Mode} jendela={Start}-{Finish} selang={Interval}m path={Path}",
            options.Mode, options.WindowStart, options.WindowFinish, options.IntervalMinutes, options.Path);
        if (options.Mode == UploadMode.Off)
        {
            logger.LogWarning("Upload:Mode=Off; worker tidak mengerjakan apa pun");
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(1, options.PollSeconds)));
        var lastMessage = string.Empty;
        do
        {
            using var activity = Telemetry.Begin("upload.cycle");
            try
            {
                var outcome = await cycle.StepAsync(DateTime.Now, stoppingToken);
                var message = $"{outcome.Kind}: {outcome.Message}";
                var level = outcome.Kind switch
                {
                    CycleKind.Failed => LogLevel.Error,
                    CycleKind.Succeeded => LogLevel.Information,
                    _ => message == lastMessage ? LogLevel.Debug : LogLevel.Information,
                };
                logger.Log(level, "Unggah harian {Kind}: {Message}", outcome.Kind, outcome.Message);
                lastMessage = message;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Putaran unggah gagal tak terduga; akan dicoba lagi pada putaran berikutnya {Ref}", ErrorReference.Tag);
            }
        }
        while (await WaitAsync(timer, stoppingToken));
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try
        {
            return await timer.WaitForNextTickAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
