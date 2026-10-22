using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;

namespace Panin.Infrastructure.Data;

/// <summary>
/// Cache memori dengan perlindungan "cache stampede": bila banyak pemanggil meminta kunci yang sama saat belum
/// ada isinya (mis. 700 user login bersamaan), hanya satu yang menjalankan query; yang lain menunggu hasil yang sama.
/// <see cref="IMemoryCache"/> saja tidak melakukannya, sehingga ratusan query identik menghantam DB sekaligus
/// (terukur: 175 query kesegaran data bersamaan, rata-rata 6,6 detik, padahal satu kali jalan 66 ms).
/// </summary>
public sealed class SingleFlightCache(IMemoryCache cache)
{
    private readonly ConcurrentDictionary<string, Lazy<Task<object?>>> _inflight = new();

    /// <param name="factory">Dijalankan paling banyak sekali per kunci pada satu waktu; tidak menerima token pembatalan pemanggil,
    /// karena hasilnya dipakai bersama dan tidak boleh gagal hanya karena satu pemanggil membatalkan.</param>
    public async Task<T?> GetOrCreateAsync<T>(string key, TimeSpan ttl, Func<Task<T?>> factory)
    {
        if (cache.TryGetValue(key, out var hit))
        {
            return (T?)hit;
        }

        var flight = _inflight.GetOrAdd(key, k => new Lazy<Task<object?>>(() => RunAsync(k, ttl, factory)));
        return (T?)await flight.Value.ConfigureAwait(false);
    }

    /// <summary>Membuang isi cache (mis. setelah data berubah oleh aksi user sendiri).</summary>
    public void Remove(string key) => cache.Remove(key);

    private async Task<object?> RunAsync<T>(string key, TimeSpan ttl, Func<Task<T?>> factory)
    {
        try
        {
            var value = await factory().ConfigureAwait(false);
            cache.Set(key, value, ttl);
            return value;
        }
        finally
        {
            _inflight.TryRemove(key, out _);
        }
    }
}
