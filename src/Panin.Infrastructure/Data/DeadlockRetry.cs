using Microsoft.Data.SqlClient;

namespace Panin.Infrastructure.Data;

/// <summary>
/// Mengulang operasi tulis yang menjadi korban deadlock (SQL error 1205). SP login/logout lama memindai tabel
/// log besar tanpa indeks sehingga deadlock muncul saat banyak user masuk/keluar bersamaan.
/// </summary>
public static class DeadlockRetry
{
    private const int DeadlockVictim = 1205;

    public static async Task<T> RunAsync<T>(Func<Task<T>> action, int attempts = 5, CancellationToken ct = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await action().ConfigureAwait(false);
            }
            catch (SqlException ex) when (ex.Number == DeadlockVictim && attempt < attempts)
            {
                // Jeda acak bertambah agar korban deadlock tidak bertabrakan lagi.
                await Task.Delay(TimeSpan.FromMilliseconds((100 * attempt) + Random.Shared.Next(0, 100)), ct).ConfigureAwait(false);
            }
        }
    }
}
