using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Panin.Application.Diagnostics;
using Panin.Application.Auth;

namespace Panin.Infrastructure.Auth;

/// <summary>
/// Antrean pencatatan logout ke DB. <c>SP_LOGUSER</c> lama memindai <c>SCALLUSERLOG</c> (±1 juta baris, tanpa indeks
/// selain PK) sehingga logout serentak bisa memakan detik; pengguna tidak boleh menunggunya. Pencatatan dilakukan
/// di latar dengan konkurensi terbatas, sedangkan sesi di memori dilepas seketika.
/// </summary>
public sealed partial class LogoutWriter(IServiceProvider services, ILogger<LogoutWriter> logger) : BackgroundService, ILogoutRecorder
{
    private const int Workers = 4;

    private readonly Channel<LogoutWork> _queue = Channel.CreateUnbounded<LogoutWork>(new UnboundedChannelOptions { SingleReader = false });

    public int Pending => _queue.Reader.Count;

    public void Enqueue(string userId, string groupId, string host) => _queue.Writer.TryWrite(new LogoutWork(userId, groupId, host));

    private sealed record LogoutWork(string UserId, string GroupId, string Host);

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
        => Task.WhenAll(Enumerable.Range(0, Workers).Select(_ => Worker(stoppingToken)));

    private async Task Worker(CancellationToken ct)
    {
        try
        {
            await foreach (var w in _queue.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                using var activity = Telemetry.Begin("logout.write");
                using var scope = CorrelationContext.Set(new CorrelationInfo(null, w.UserId, w.GroupId, null));
                try
                {
                    await services.GetRequiredService<IAuthService>().RecordLogoutAsync(w.UserId, w.GroupId, w.Host, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    LogFailed(logger, ex, w.UserId);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // aplikasi berhenti; sisa antrean dilepas.
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Pencatatan logout ke DB gagal untuk {UserId}; sesi di memori sudah dilepas, catatan SCALLUSERLOG tidak tersimpan")]
    private static partial void LogFailed(ILogger logger, Exception ex, string userId);
}
