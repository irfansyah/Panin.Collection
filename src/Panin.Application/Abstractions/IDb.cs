using System.Data;

namespace Panin.Application.Abstractions;

/// <summary>
/// Pengganti <c>DMS.Tools.DbConnection</c> dari aplikasi lama.
/// Query memakai parameter posisional <c>@1, @2, ...</c> persis seperti kode lama,
/// tetapi nilainya selalu dikirim sebagai parameter SQL sungguhan (bukan disisipkan ke teks SQL).
/// </summary>
public interface IDb : IAsyncDisposable
{
    /// <summary>Menjalankan perintah non-query; mengembalikan jumlah baris terpengaruh.</summary>
    Task<int> ExecuteAsync(string sql, object?[]? args = null, DbOptions? options = null, CancellationToken ct = default);

    /// <summary>Menjalankan query dan mengembalikan hasil pertama sebagai <see cref="DataTable"/>.</summary>
    Task<DataTable> QueryTableAsync(string sql, object?[]? args = null, DbOptions? options = null, CancellationToken ct = default);

    /// <summary>Menjalankan query/SP yang mengembalikan beberapa result set.</summary>
    Task<DataSet> QueryDataSetAsync(string sql, object?[]? args = null, DbOptions? options = null, CancellationToken ct = default);

    /// <summary>Baris pertama dari hasil query, atau <c>null</c> bila kosong.</summary>
    Task<DataRow?> QueryFirstOrDefaultAsync(string sql, object?[]? args = null, DbOptions? options = null, CancellationToken ct = default);
}

/// <param name="TimeoutSeconds">0 = default driver. Aplikasi lama memakai dbTimeOut=600 dan dbBigTimeOut=3600.</param>
/// <param name="BlankToNull">Lama: string kosong/spasi dikirim sebagai NULL (default true).</param>
public sealed record DbOptions(int TimeoutSeconds = 0, bool BlankToNull = true);
