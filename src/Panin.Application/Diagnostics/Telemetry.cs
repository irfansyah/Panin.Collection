using System.Diagnostics;

namespace Panin.Application.Diagnostics;

/// <summary>
/// Sumber <see cref="Activity"/> aplikasi. Setiap request HTTP, setiap interaksi di sirkuit Blazor (klik, input, panggilan JS),
/// dan setiap pekerjaan latar berjalan di dalam satu Activity; <c>Activity.TraceId</c>-nya ikut di semua baris log
/// (lihat <c>docs/LOGGING.md</c>), sehingga satu kejadian bisa ditelusuri dari layar sampai query DB.
/// </summary>
public static class Telemetry
{
    public const string SourceName = "Panin";

    public static ActivitySource Source { get; } = new(SourceName);

    /// <summary>Memulai Activity bernama dengan konteks pengguna/sirkuit saat ini (untuk pekerjaan latar dan timer).</summary>
    public static Activity? Begin(string name, ActivityKind kind = ActivityKind.Internal)
        => Source.StartActivity(name, kind);
}

/// <summary>
/// Identitas yang menyertai satu alur kerja: sirkuit, pengguna, sesi. Disimpan di <see cref="AsyncLocal{T}"/> sehingga
/// mengalir ke semua <c>await</c> di bawahnya (termasuk akses DB) tanpa dioper manual.
/// </summary>
public sealed record CorrelationInfo(string? CircuitId, string? UserId, string? GroupId, string? SessionId);

public static class CorrelationContext
{
    private static readonly AsyncLocal<CorrelationInfo?> Holder = new();

    public static CorrelationInfo? Current => Holder.Value;

    /// <summary>Menetapkan konteks untuk alur saat ini; kembalikan hasilnya ke <c>using</c> agar konteks lama dipulihkan.</summary>
    public static IDisposable Set(CorrelationInfo? info)
    {
        var previous = Holder.Value;
        Holder.Value = info;
        return new Restore(previous);
    }

    private sealed class Restore(CorrelationInfo? previous) : IDisposable
    {
        public void Dispose() => Holder.Value = previous;
    }
}

/// <summary>
/// Kode referensi singkat yang ditampilkan ke pengguna saat terjadi galat. Sama dengan awal <c>TraceId</c> di log,
/// jadi pengguna cukup menyebutkan kode itu dan petugas mencarinya di log (<c>grep &lt;kode&gt;</c>).
/// </summary>
public static class ErrorReference
{
    public const int Length = 8;

    /// <summary>Kode untuk alur saat ini; bila tidak ada Activity (mis. di luar request) dibuat acak.</summary>
    public static string Current
    {
        get
        {
            var id = Activity.Current?.TraceId.ToString();
            return string.IsNullOrEmpty(id) || id == "00000000000000000000000000000000"
                ? Guid.NewGuid().ToString("N")[..Length]
                : id[..Length];
        }
    }

    /// <summary>Potongan teks untuk ditempel di pesan galat, mis. <c>(Kode: 4BF92F35)</c>.</summary>
    public static string Tag => $"(Kode: {Current.ToUpperInvariant()})";
}
