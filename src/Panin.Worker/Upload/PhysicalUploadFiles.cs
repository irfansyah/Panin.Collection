namespace Panin.Worker.Upload;

public sealed class PhysicalUploadFiles : IUploadFiles
{
    public IReadOnlyCollection<string> Names(string path)
        => Directory.Exists(path) ? [.. Directory.EnumerateFiles(path).Select(f => System.IO.Path.GetFileName(f))] : [];

    public void Move(string path, string name, string destinationDirectory, string newName)
    {
        // Nama berasal dari konfigurasi + tanggal, tetapi tetap dijaga agar tidak keluar dari folder.
        if (System.IO.Path.GetFileName(name) != name || System.IO.Path.GetFileName(newName) != newName)
        {
            throw new InvalidOperationException($"Nama berkas tidak valid: {name}");
        }

        var source = System.IO.Path.Combine(path, ResolveExisting(path, name));
        Directory.CreateDirectory(destinationDirectory);
        File.Move(source, System.IO.Path.Combine(destinationDirectory, newName));
    }

    // Di Linux nama berkas peka huruf besar/kecil; cari yang cocok tanpa memperhatikan huruf.
    private static string ResolveExisting(string path, string name)
        => Directory.EnumerateFiles(path).Select(f => System.IO.Path.GetFileName(f)).FirstOrDefault(f => string.Equals(f, name, StringComparison.OrdinalIgnoreCase)) ?? name;
}
