using Microsoft.AspNetCore.Components.Server.Circuits;

namespace Panin.Web.Infrastructure;

/// <summary>Menghitung sirkuit Blazor aktif (untuk /_diag dan uji beban).</summary>
public sealed class CircuitTracker : CircuitHandler
{
    private int _active;
    private long _opened;

    public int Active => Volatile.Read(ref _active);

    public long OpenedTotal => Interlocked.Read(ref _opened);

    public override Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _active);
        Interlocked.Increment(ref _opened);
        return Task.CompletedTask;
    }

    public override Task OnCircuitClosedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        Interlocked.Decrement(ref _active);
        return Task.CompletedTask;
    }
}
