using System.Text.RegularExpressions;

namespace Panin.Application.Memos;

/// <summary>Lima memo/surat cetak di menu "Laporan Recovery" (modul 60, menu 202010–202050).</summary>
public enum MemoKind
{
    Skpm,
    HapusBuku,
    HapusTagih,
    Restrukturisasi,
    Ayda,
}

public sealed record MemoSearchRow(string AccountNo, string? DebtorName);

/// <param name="Html">Dokumen HTML lengkap (templat lama dengan nilai terisi).</param>
/// <param name="FileName">Nama berkas unduhan Word, sama dengan layar lama (mis. <c>ReportSKPM_123.doc</c>).</param>
public sealed record MemoDocument(string Html, string FileName);

public static partial class MemoRules
{
    public static string Slug(MemoKind kind) => kind switch
    {
        MemoKind.Skpm => "skpm",
        MemoKind.HapusBuku => "hapus-buku",
        MemoKind.HapusTagih => "hapus-tagih",
        MemoKind.Restrukturisasi => "restrukturisasi",
        _ => "ayda",
    };

    public static bool TryParseSlug(string? slug, out MemoKind kind)
    {
        foreach (var k in Enum.GetValues<MemoKind>())
        {
            if (string.Equals(Slug(k), slug, StringComparison.OrdinalIgnoreCase))
            {
                kind = k;
                return true;
            }
        }

        kind = default;
        return false;
    }

    public static string Title(MemoKind kind) => kind switch
    {
        MemoKind.Skpm => "Print Surat Kuasa Penarikan Agunan Mobil",
        MemoKind.HapusBuku => "Print Memo Pengajuan Hapus Buku",
        MemoKind.HapusTagih => "Print Memo Pengajuan Hapus Tagih",
        MemoKind.Restrukturisasi => "Print Memo Pengajuan Restrukturisasi",
        _ => "Print Memo Pengajuan Penyelesaian AYDA",
    };

    /// <summary>Id menu (<c>RFMENUX.MENUID</c>, modul 60) yang harus dimiliki group untuk mencetak memo ini.</summary>
    public static string MenuId(MemoKind kind) => kind switch
    {
        MemoKind.Skpm => "202010",
        MemoKind.HapusBuku => "202020",
        MemoKind.HapusTagih => "202030",
        MemoKind.Restrukturisasi => "202040",
        _ => "202050",
    };

    /// <summary>Nomor rekening: huruf/angka saja dan tidak lebih dari 30 karakter.</summary>
    public static bool IsValidAccount(string? accountNo) => !string.IsNullOrWhiteSpace(accountNo) && AccountPattern().IsMatch(accountNo.Trim());

    [GeneratedRegex(@"^[A-Za-z0-9]{1,30}$")]
    private static partial Regex AccountPattern();
}

public interface IMemoService
{
    /// <summary>Cari rekening menurut nomor persis (<c>VW_SEARCH_*</c>), seperti halaman Cari lama.</summary>
    Task<IReadOnlyList<MemoSearchRow>> SearchAsync(MemoKind kind, string accountNo, CancellationToken ct = default);

    /// <summary>Dokumen memo untuk rekening; <c>null</c> bila rekening tidak ditemukan di view laporan.</summary>
    Task<MemoDocument?> BuildAsync(MemoKind kind, string accountNo, CancellationToken ct = default);
}
