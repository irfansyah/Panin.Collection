using System.Globalization;

namespace Panin.Worker.Upload;

public enum CycleKind
{
    /// <summary>Tidak ada pekerjaan (mode Off, di luar jendela, menunggu selang, sudah sukses hari ini, atau dihentikan setelah gagal).</summary>
    Idle,

    /// <summary>Berkas wajib belum lengkap; menunggu.</summary>
    Waiting,

    /// <summary>Mode Observe: berkas lengkap, tetapi tidak dijalankan.</summary>
    Observed,

    Succeeded,

    Failed,
}

public sealed record CycleOutcome(CycleKind Kind, string Message)
{
    public static CycleOutcome Idle(string reason) => new(CycleKind.Idle, reason);
}

/// <summary>
/// Satu langkah penjadwal unggah harian. Logika <c>losDataUploader.mainProcess</c> lama yang dipertahankan: jendela waktu harian,
/// pemeriksaan tiap selang, berkas harian <c>&lt;awalan&gt;ddMM.TXT</c>, perintah pra/pasca dengan path, catatan hasil lewat
/// <c>USPV_UPLOADSUCCESS/FAIL</c>, dan pemindahan berkas ke folder Done/Fail dengan cap waktu.
/// Yang diperbaiki: tidak ada status "selesai" di memori (restart tidak memicu unggah ulang; cukup lihat <c>UPLOADRESULT</c>),
/// berkas yang belum lengkap tidak dipindah ke Fail dan tidak dicatat gagal tiap menit (hanya sekali di kesempatan terakhir),
/// dan setelah gagal tidak diulang otomatis pada hari yang sama.
/// </summary>
public sealed class UploadCycle(UploadOptions options, IUploadFiles files, IUploadDatabase db)
{
    private DateTime? _lastAttempt;
    private DateOnly? _failLoggedFor;
    private DateOnly? _stoppedFor;

    public async Task<CycleOutcome> StepAsync(DateTime now, CancellationToken ct)
    {
        if (options.Mode == UploadMode.Off)
        {
            return CycleOutcome.Idle("Mode Off");
        }

        var today = DateOnly.FromDateTime(now);
        var time = TimeOnly.FromDateTime(now);
        if (time < options.Start || time > options.Finish)
        {
            return CycleOutcome.Idle("Di luar jendela waktu");
        }

        if (_stoppedFor == today)
        {
            return CycleOutcome.Idle("Dihentikan setelah gagal hari ini; perbaiki lalu letakkan berkas kembali dan restart worker");
        }

        var interval = TimeSpan.FromMinutes(options.IntervalMinutes);
        if (_lastAttempt is { } last && now - last < interval)
        {
            return CycleOutcome.Idle("Menunggu selang berikutnya");
        }

        _lastAttempt = now;
        if (await db.HasSuccessOnAsync(now.Date, ct).ConfigureAwait(false))
        {
            return CycleOutcome.Idle("Unggah hari ini sudah sukses");
        }

        var dated = Suffix(now);
        var present = files.Names(options.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = options.RequiredFiles.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim() + dated).Where(n => !present.Contains(n)).ToList();
        if (missing.Count > 0)
        {
            // Kesempatan terakhir: pemeriksaan berikutnya sudah di luar jendela. Catat gagal sekali agar tampak di UPLOADRESULT.
            var lastChance = time.ToTimeSpan() + interval > options.Finish.ToTimeSpan();
            var message = $"All or some required files({string.Join(';', missing)}) does not exists";
            if (lastChance && _failLoggedFor != today && options.Mode == UploadMode.Run)
            {
                _failLoggedFor = today;
                await db.LogFailAsync(options.Sender, now, message, ct).ConfigureAwait(false);
                return new CycleOutcome(CycleKind.Failed, message);
            }

            return new CycleOutcome(CycleKind.Waiting, message);
        }

        var all = options.RequiredFiles.Concat(options.OptionalFiles).Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim() + dated).Where(present.Contains).ToList();
        if (options.Mode == UploadMode.Observe)
        {
            return new CycleOutcome(CycleKind.Observed, $"Berkas lengkap ({all.Count}); mode Observe tidak menjalankan unggah");
        }

        var path = WithSeparator(options.Path);
        try
        {
            await db.RunAsync(options.PreStatements, options.PostStatements, path, options.CommandTimeoutSeconds, ct).ConfigureAwait(false);
        }
        catch (UploadAlreadyRunningException)
        {
            _lastAttempt = null; // coba lagi pada putaran berikutnya
            return CycleOutcome.Idle("Proses unggah lain sedang berjalan");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var message = Trim(ex.Message);
            await db.LogFailAsync(options.Sender, now, message, ct).ConfigureAwait(false);
            Move(all, now, options.FailPath);
            _stoppedFor = today;
            return new CycleOutcome(CycleKind.Failed, message);
        }

        await db.LogSuccessAsync(options.Sender, now, ct).ConfigureAwait(false);
        Move(all, now, options.DonePath);
        return new CycleOutcome(CycleKind.Succeeded, $"Unggah selesai ({all.Count} berkas)");
    }

    /// <summary>Akhiran nama harian: <c>ddMM.TXT</c>.</summary>
    public static string Suffix(DateTime now) => now.ToString("ddMM", CultureInfo.InvariantCulture) + ".TXT";

    /// <summary>Path diakhiri pemisah dengan gaya yang sama dengan isinya (SP menyambung nama berkas langsung di belakang path).</summary>
    public static string WithSeparator(string path)
    {
        if (path.EndsWith('\\') || path.EndsWith('/'))
        {
            return path;
        }

        return path + (path.Contains('\\', StringComparison.Ordinal) ? '\\' : '/');
    }

    private void Move(IEnumerable<string> names, DateTime now, string destination)
    {
        if (string.IsNullOrWhiteSpace(destination))
        {
            return;
        }

        var stamp = now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        foreach (var name in names)
        {
            var dot = name.LastIndexOf('.');
            files.Move(options.Path, name, destination, dot > 0 ? $"{name[..dot]}-{stamp}{name[dot..]}" : $"{name}-{stamp}");
        }
    }

    // Galat SQL lama menyertakan "Last Query ..." di belakang pesan; dibuang seperti di PANINSVC, dan dibatasi 6000 karakter (kolom SP).
    private static string Trim(string message)
    {
        var at = message.IndexOf("Last Query", StringComparison.Ordinal);
        var text = at > 0 ? message[..at] : message;
        return text.Length > 6000 ? text[..6000] : text;
    }
}
