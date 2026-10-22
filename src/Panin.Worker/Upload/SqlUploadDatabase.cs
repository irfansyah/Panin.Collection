using System.Data;
using Microsoft.Data.SqlClient;

namespace Panin.Worker.Upload;

/// <summary>Akses DB DATA untuk unggah harian (<c>UPLOADRESULT</c>, <c>USPV_UPLOADSUCCESS/FAIL</c>, dan perintah pra/pasca).</summary>
public sealed class SqlUploadDatabase(string connectionString) : IUploadDatabase
{
    private const string LockName = "PANIN_UPLOAD";

    public async Task<bool> HasSuccessOnAsync(DateTime day, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand("SELECT COUNT(*) FROM UPLOADRESULT WHERE STATUS = 'SUCCESS' AND UPLOADTIME >= @a AND UPLOADTIME < @b", conn);
        cmd.Parameters.AddWithValue("@a", day.Date);
        cmd.Parameters.AddWithValue("@b", day.Date.AddDays(1));
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture) > 0;
    }

    public async Task LogSuccessAsync(string sender, DateTime time, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand("exec USPV_UPLOADSUCCESS @sender, @time", conn);
        cmd.Parameters.AddWithValue("@sender", sender);
        cmd.Parameters.Add("@time", SqlDbType.DateTime).Value = time;
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task LogFailAsync(string sender, DateTime time, string message, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand("exec USPV_UPLOADFAIL @sender, @time, @msg", conn);
        cmd.Parameters.AddWithValue("@sender", sender);
        cmd.Parameters.Add("@time", SqlDbType.DateTime).Value = time;
        cmd.Parameters.AddWithValue("@msg", message);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task RunAsync(IReadOnlyList<string> pre, IReadOnlyList<string> post, string path, int timeoutSeconds, CancellationToken ct)
    {
        await using var conn = await OpenAsync(ct).ConfigureAwait(false);

        // Kunci tingkat sesi pada koneksi yang sama dengan eksekusi: lepas otomatis bila koneksi/proses mati.
        await using (var lockCmd = new SqlCommand("DECLARE @r int; EXEC @r = sp_getapplock @Resource = @name, @LockMode = 'Exclusive', @LockOwner = 'Session', @LockTimeout = 0; SELECT @r;", conn))
        {
            lockCmd.Parameters.AddWithValue("@name", LockName);
            if (Convert.ToInt32(await lockCmd.ExecuteScalarAsync(ct).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture) < 0)
            {
                throw new UploadAlreadyRunningException();
            }
        }

        foreach (var statement in pre.Concat(post).Where(s => !string.IsNullOrWhiteSpace(s)))
        {
            await using var cmd = new SqlCommand(statement, conn) { CommandTimeout = timeoutSeconds };
            if (statement.Contains("@1", StringComparison.Ordinal))
            {
                cmd.Parameters.AddWithValue("@1", path);
            }

            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    private async Task<SqlConnection> OpenAsync(CancellationToken ct)
    {
        var conn = new SqlConnection(connectionString);
        try
        {
            await conn.OpenAsync(ct).ConfigureAwait(false);
            return conn;
        }
        catch
        {
            await conn.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}
