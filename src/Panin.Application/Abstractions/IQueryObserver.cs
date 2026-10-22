namespace Panin.Application.Abstractions;

/// <summary>
/// Menerima hasil ukur tiap panggilan DB. Dipakai untuk statistik per query dan peringatan query lambat
/// (lihat <c>docs/PERFORMANCE.md</c>): aplikasi baru harus lebih cepat dari yang lama, jadi query yang
/// lambat harus terlihat, bukan baru ketahuan saat user mengeluh.
/// </summary>
public interface IQueryObserver
{
    /// <param name="database">Nama DB logis (mis. "login", "data:60").</param>
    /// <param name="sql">Teks SQL (tanpa nilai parameter).</param>
    /// <param name="rows">Jumlah baris hasil pertama bila diketahui.</param>
    /// <param name="exception">Galat yang menyebabkan <paramref name="failed"/>; dicatat bersama teks SQL agar tidak hilang.</param>
    void Observe(string database, string sql, TimeSpan elapsed, int? rows, bool failed, Exception? exception = null);
}
