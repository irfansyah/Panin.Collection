namespace Panin.Web.Shell;

/// <summary>
/// Kamus nama menu → ikon (nama ikon di <c>Icon.razor</c>). Menu datang dari DB lama (<c>RFMENUX</c>) yang tidak boleh diubah,
/// jadi ikon dipilih di sisi aplikasi dari teks menu; yang tidak dikenal memakai ikon dokumen.
/// </summary>
public static class MenuIcons
{
    public const string Default = "document";

    // Urutan penting: aturan yang lebih spesifik lebih dulu ("monitoring aktifitas" sebelum "monitoring").
    private static readonly (string Contains, string Icon)[] Rules =
    [
        ("dashboard", "home"),
        ("distribusi", "arrows-right-left"),
        ("flow rate", "arrows-right-left"),
        ("monitoring aktifitas", "pulse"),
        ("monitoring aktivitas", "pulse"),
        ("aktifitas", "pulse"),
        ("monitoring", "eye"),
        ("bulk surat", "inbox"),
        ("surat", "inbox"),
        ("inquiry", "search"),
        ("search", "search"),
        ("cari", "search"),
        ("add news", "clipboard-list"),
        ("news", "book-open"),
        ("berita", "book-open"),
        ("password", "key"),
        ("daily tracking prk", "building"),
        ("prk", "building"),
        ("daily tracking", "trending-up"),
        ("leading edge", "chart"),
        ("lag delinquency", "clock"),
        ("kpm", "clipboard-list"),
        ("was is", "scale"),
        ("report", "document"),
        ("laporan", "document"),
        ("approval", "check-circle"),
        ("maker", "clipboard-list"),
        ("user", "users"),
        ("group", "users"),
        ("process", "rules"),
        ("parameter", "cog"),
        ("general", "cog"),
        ("facilit", "cog"),
        ("administrasi", "cog"),
        ("maintenance", "cog"),
        ("collection", "users"),
    ];

    public static string For(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Default;
        }

        foreach (var (contains, icon) in Rules)
        {
            if (text.Contains(contains, StringComparison.OrdinalIgnoreCase))
            {
                return icon;
            }
        }

        return Default;
    }
}
