using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Panin.Application.Auth;
using Panin.Application.Sessions;

namespace Panin.Infrastructure.Sessions;

/// <summary>
/// Melepas sesi yang tidak ada detak (tab ditutup, jaringan putus) dan menurunkan flag login di DB
/// supaya user tidak terkunci seperti pada <c>SU_LOGON</c> yang menggantung.
/// </summary>
public sealed partial class SessionReaper(
    IUserSessionRegistry registry,
    IAuthService auth,
    IOptions<SessionOptions> options,
    ILogger<SessionReaper> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var abandoned = TimeSpan.FromSeconds(options.Value.AbandonedAfterSeconds);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            foreach (var s in registry.ReapExpired(abandoned))
            {
                LogReaped(logger, s.UserId);
                await auth.RecordLogoutAsync(s.UserId, s.GroupId, s.Host, stoppingToken).ConfigureAwait(false);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Sesi {UserId} dilepas karena tidak ada detak")]
    private static partial void LogReaped(ILogger logger, string userId);
}
