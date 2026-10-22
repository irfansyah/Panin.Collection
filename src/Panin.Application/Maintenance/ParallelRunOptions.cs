namespace Panin.Application.Maintenance;

/// <summary>
/// Pengaturan masa uji paralel dengan aplikasi lama (bagian <c>ParallelRun</c> di konfigurasi).
/// Selama paralel, DB LOGIN aplikasi baru adalah salinan satu arah dari DB LOGIN lama, sedangkan DB DATA dipakai bersama.
/// DB DATA membaca user/group/hierarki dari DB LOGIN <b>lama</b> (view bernama tetap), jadi pemeliharaan user, group, akses menu,
/// dan password harus dilakukan di satu tempat saja: aplikasi lama. Lihat <c>docs/PARALEL.md</c>.
/// </summary>
public sealed class ParallelRunOptions
{
    public const string SectionName = "ParallelRun";

    /// <summary>True selama masa paralel: pemeliharaan user/group dan ganti password di aplikasi ini dinonaktifkan.</summary>
    public bool Enabled { get; set; }

    /// <summary>Alamat aplikasi lama, ditampilkan di pesan agar pengguna tahu ke mana harus pergi (opsional).</summary>
    public string? OldAppUrl { get; set; }

    public string UserGroupMessage => Compose("Selama masa uji paralel, user, group, dan akses menu dikelola di aplikasi lama. Halaman ini hanya untuk dilihat; perubahan tidak dapat disimpan di sini.");

    public string PasswordMessage => Compose("Selama masa uji paralel, password diubah di aplikasi lama. Setelah diubah, tunggu sinkronisasi lalu masuk kembali di sini.");

    private string Compose(string text) => string.IsNullOrWhiteSpace(OldAppUrl) ? text : $"{text} Aplikasi lama: {OldAppUrl}";
}
