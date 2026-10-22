using System.Diagnostics;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Panin.Application.Diagnostics;

namespace Panin.Web.Infrastructure.Logging;

/// <summary>Identitas sirkuit saat ini, untuk komponen yang menjalankan pekerjaan di luar event (timer, mis. detak sesi).</summary>
public interface ICircuitContext
{
    CorrelationInfo Info { get; }
}

/// <summary>
/// Satu per sirkuit. Membungkus <b>setiap</b> aktivitas masuk ke sirkuit (klik, input, panggilan balik JS, render) dalam sebuah
/// <see cref="Activity"/> baru berikut <see cref="CorrelationContext"/>. Karena itu semua log dan query DB yang dipicu interaksi itu
/// memiliki <c>TraceId</c>, <c>CircuitId</c>, dan <c>UserId</c> yang sama, dan pesan galat ke pengguna bisa memuat kode yang sama.
/// </summary>
public sealed partial class CircuitCorrelationHandler(AuthenticationStateProvider auth, ILogger<CircuitCorrelationHandler> logger) : CircuitHandler, ICircuitContext
{
    private string? _circuitId;
    private CorrelationInfo _info = new(null, null, null, null);
    private DateTimeOffset _opened;

    public CorrelationInfo Info => _info;

    public override Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        _circuitId = circuit.Id;
        _opened = DateTimeOffset.UtcNow;
        _info = _info with { CircuitId = _circuitId };
        using var activity = Telemetry.Begin("circuit.open");
        using var scope = CorrelationContext.Set(_info);
        LogOpened(logger, circuit.Id);
        return Task.CompletedTask;
    }

    public override Task OnCircuitClosedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.Begin("circuit.close");
        using var scope = CorrelationContext.Set(_info);
        LogClosed(logger, circuit.Id, (long)(DateTimeOffset.UtcNow - _opened).TotalSeconds);
        return Task.CompletedTask;
    }

    public override Func<CircuitInboundActivityContext, Task> CreateInboundActivityHandler(Func<CircuitInboundActivityContext, Task> next)
        => async context =>
        {
            await EnsureUserAsync().ConfigureAwait(false);
            using var activity = Telemetry.Begin("circuit.inbound");
            using var scope = CorrelationContext.Set(_info);
            await next(context).ConfigureAwait(false);
        };

    // Pengguna dibaca sekali per sirkuit (sesi tidak berganti pengguna di tengah jalan).
    private async Task EnsureUserAsync()
    {
        if (_info.UserId is not null)
        {
            return;
        }

        try
        {
            var user = (await auth.GetAuthenticationStateAsync().ConfigureAwait(false)).User.ToSessionUser();
            if (user is not null)
            {
                _info = _info with { UserId = user.UserId, GroupId = user.GroupId, SessionId = user.SessionId };
            }
        }
        catch (InvalidOperationException)
        {
            // status autentikasi belum siap pada aktivitas sangat awal; dicoba lagi pada aktivitas berikutnya.
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Sirkuit dibuka {Circuit}")]
    private static partial void LogOpened(ILogger logger, string circuit);

    [LoggerMessage(Level = LogLevel.Information, Message = "Sirkuit ditutup {Circuit} setelah {Seconds} detik")]
    private static partial void LogClosed(ILogger logger, string circuit, long seconds);
}
