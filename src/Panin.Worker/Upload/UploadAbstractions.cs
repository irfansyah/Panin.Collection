namespace Panin.Worker.Upload;

public interface IUploadFiles
{
    /// <summary>Nama berkas di folder (tanpa path). Folder yang tidak ada = kosong.</summary>
    IReadOnlyCollection<string> Names(string path);

    /// <summary>Memindahkan berkas ke folder tujuan dengan nama baru; membuat folder bila perlu.</summary>
    void Move(string path, string name, string destinationDirectory, string newName);
}

public interface IUploadDatabase
{
    /// <summary>Sudah ada catatan SUCCESS di <c>UPLOADRESULT</c> pada hari ini? (juga mencakup unggahan yang dikerjakan PANINSVC lama)</summary>
    Task<bool> HasSuccessOnAsync(DateTime day, CancellationToken ct);

    Task LogSuccessAsync(string sender, DateTime time, CancellationToken ct);

    Task LogFailAsync(string sender, DateTime time, string message, CancellationToken ct);

    /// <summary>
    /// Menjalankan perintah pra lalu pasca secara berurutan pada satu koneksi, dengan kunci agar tidak ada dua proses unggah sekaligus.
    /// Berhenti pada galat pertama.
    /// </summary>
    /// <exception cref="UploadAlreadyRunningException">Proses unggah lain sedang berjalan.</exception>
    Task RunAsync(IReadOnlyList<string> pre, IReadOnlyList<string> post, string path, int timeoutSeconds, CancellationToken ct);
}

public sealed class UploadAlreadyRunningException() : Exception("Proses unggah lain sedang berjalan");
