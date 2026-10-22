using Panin.Application.Abstractions;
using Panin.Application.Collection;
using Panin.Application.Modules;

namespace Panin.Infrastructure.Collection;

/// <summary>
/// Pencarian debitur di DB DATA modul 60. Hasilnya sama dengan <c>SP_SEARCH_DEBITUR</c> (divalidasi terhadap DB dev:
/// himpunan rekening dan nilai kolom identik) tetapi tanpa <c>LEFT JOIN TREATMENT_HISTORY</c> yang tidak ikut dipilih
/// oleh SP (hanya memperlambat dan dapat menggandakan baris), dengan <c>TOP</c> agar tidak mengembalikan ratusan ribu baris.
/// </summary>
public sealed class DebiturSearchService(IModuleDbFactory dbs) : IDebiturSearchService
{
    /// <summary>Pencarian berat dibatasi bersamaan agar 700 user tidak menghantam DB sekaligus.</summary>
    private static readonly SemaphoreSlim Gate = new(8, 8);

    public const string Sql = """
        SELECT TOP (@4) D.ACC_NO AS AccountNo, D.[CIF NO.] AS CustomerRef, D.[Customer Name] AS CustomerName,
               D.Facility, D.Outstanding, D.DPD AS Dpd, D.Kolektibility AS Collectibility, D.AKSI_TERAKHIR AS LastAction,
               ISNULL(D.TENOR - D.SISA_SETORAN, 0) AS InstallmentNo, D.[NEXT ACTION] AS Recommendation,
               D.USER_YG_MENANGANI AS Officer, AM.STATUS AS AccountStatus
        FROM (SELECT TOP (@4) * FROM VW_SEARCH_DEBITUR_LEFT
              WHERE (@1 IS NULL OR ACC_NO LIKE @1 + '%')
                AND (@2 IS NULL OR [CIF NO.] LIKE @2 + '%')
                AND (@3 IS NULL OR [Customer Name] LIKE '%' + @3 + '%')
              ORDER BY ACC_NO) D
        LEFT JOIN ACCOUNT_MASTER AM ON AM.ACC_NO = D.ACC_NO
        ORDER BY D.ACC_NO
        """;

    public async Task<DebiturSearchResult> SearchAsync(DebiturSearchCriteria criteria, CancellationToken ct = default)
    {
        var c = criteria.Normalized();
        if (DebiturSearchRules.Validate(c) is { } error)
        {
            throw new ArgumentException(error, nameof(criteria));
        }

        var db = await dbs.GetDataDbAsync("60", ct).ConfigureAwait(false);
        await Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Minta satu baris lebih banyak dari batas untuk mengetahui apakah hasil terpotong.
            var rows = await db.QueryAsync<DebiturRow>(
                Sql, [c.AccountNo, c.CustomerRef, c.Name, DebiturSearchRules.MaxRows + 1], new DbOptions(TimeoutSeconds: 90), ct).ConfigureAwait(false);
            var truncated = rows.Count > DebiturSearchRules.MaxRows;
            return new DebiturSearchResult(truncated ? [.. rows.Take(DebiturSearchRules.MaxRows)] : rows, truncated, DebiturSearchRules.MaxRows);
        }
        finally
        {
            Gate.Release();
        }
    }
}
