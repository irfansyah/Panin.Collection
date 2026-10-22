namespace Panin.Worker;

public enum UploadMode
{
    /// <summary>Tidak melakukan apa-apa (default; aman selama aplikasi lama/PANINSVC masih berjalan).</summary>
    Off,

    /// <summary>Hanya memeriksa dan mencatat berkas yang ada; tidak menjalankan SP dan tidak memindahkan berkas.</summary>
    Observe,

    /// <summary>Menjalankan unggah harian. Jangan aktifkan selagi PANINSVC lama masih berjalan: data akan terunggah dua kali.</summary>
    Run,
}

/// <summary>
/// Pengaturan unggah data harian (pengganti <c>PANINSVC</c> / <c>losDataUploader</c>). Nama dan makna sama dengan <c>App.config</c> lama
/// (<c>uploadTimeStart</c>, <c>uploadFiles</c>, <c>uploadPostSP</c>, dst.); salin nilai dari konfigurasi PANINSVC di server produksi.
/// </summary>
public sealed class UploadOptions
{
    public const string SectionName = "Upload";

    public UploadMode Mode { get; set; } = UploadMode.Off;

    /// <summary>Awal dan akhir jendela harian (HH:mm). Di luar jendela tidak ada yang dijalankan.</summary>
    public string WindowStart { get; set; } = "05:00";

    public string WindowFinish { get; set; } = "17:00";

    /// <summary>Selang antar pemeriksaan di dalam jendela (menit). Sama dengan <c>uploadTimeIntervalInMinutes</c>.</summary>
    public int IntervalMinutes { get; set; } = 5;

    /// <summary>Selang bangun worker (detik); sama dengan <c>ThreadSleepTimeInMilliSeconds</c>.</summary>
    public int PollSeconds { get; set; } = 30;

    /// <summary>
    /// Folder tempat berkas diletakkan. <b>Harus dapat dibaca SQL Server</b> dengan teks yang sama (SP membaca berkas lewat path ini),
    /// biasanya UNC <c>\server\share\</c>. Path diakhiri pemisah karena SP menyambung nama berkas langsung di belakangnya.
    /// </summary>
    public string Path { get; set; } = string.Empty;

    public string DonePath { get; set; } = string.Empty;

    public string FailPath { get; set; } = string.Empty;

    // Catatan: nilai bawaan daftar (RequiredFiles/OptionalFiles/PostStatements) ada di appsettings.json, bukan di sini:
    // pengikat konfigurasi .NET MENAMBAHKAN elemen ke array bawaan sehingga nilai bawaan tidak bisa ditimpa, hanya ditambah.

    /// <summary>Awalan berkas wajib; nama harian = awalan + <c>ddMM</c> + <c>.TXT</c> (mis. <c>CUS2210.TXT</c>).</summary>
    public string[] RequiredFiles { get; set; } = [];

    /// <summary>Berkas yang ikut dipindahkan bila ada, tetapi tidak ditunggu (mis. <c>MIS</c>: PANINSVC lama menunggunya padahal SP tidak membacanya).</summary>
    public string[] OptionalFiles { get; set; } = [];

    /// <summary>Perintah SQL sebelum/sesudah berkas siap; <c>@1</c> diisi path folder. Sama dengan <c>uploadPreSP</c>/<c>uploadPostSP</c>.</summary>
    public string[] PreStatements { get; set; } = [];

    public string[] PostStatements { get; set; } = [];

    /// <summary>Batas waktu satu perintah SQL (detik). PANINSVC lama memakai 100000.</summary>
    public int CommandTimeoutSeconds { get; set; } = 7200;

    /// <summary>Nama pengirim pada catatan <c>UPLOADRESULT</c>.</summary>
    public string Sender { get; set; } = "DataUploader";

    public TimeOnly Start => TimeOnly.ParseExact(WindowStart, "HH:mm", System.Globalization.CultureInfo.InvariantCulture);

    public TimeOnly Finish => TimeOnly.ParseExact(WindowFinish, "HH:mm", System.Globalization.CultureInfo.InvariantCulture);

    /// <returns>Daftar masalah konfigurasi (kosong = valid). Mode Off tidak divalidasi.</returns>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (Mode == UploadMode.Off)
        {
            return errors;
        }

        if (string.IsNullOrWhiteSpace(Path))
        {
            errors.Add("Upload:Path wajib diisi");
        }

        if (!TimeOnly.TryParseExact(WindowStart, "HH:mm", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var start)
            || !TimeOnly.TryParseExact(WindowFinish, "HH:mm", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var finish))
        {
            errors.Add("Upload:WindowStart/WindowFinish harus berformat HH:mm");
        }
        else if (finish <= start)
        {
            errors.Add("Upload:WindowFinish harus setelah WindowStart");
        }

        if (IntervalMinutes < 1)
        {
            errors.Add("Upload:IntervalMinutes minimal 1");
        }

        if (RequiredFiles.Length == 0 && Mode == UploadMode.Run)
        {
            errors.Add("Upload:RequiredFiles tidak boleh kosong pada mode Run");
        }

        if (Mode == UploadMode.Run && string.IsNullOrWhiteSpace(DonePath))
        {
            errors.Add("Upload:DonePath wajib pada mode Run (berkas harus dipindah setelah selesai agar tidak terunggah ulang)");
        }

        return errors;
    }
}
