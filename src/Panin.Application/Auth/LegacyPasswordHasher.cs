using System.Security.Cryptography;
using System.Text;

namespace Panin.Application.Auth;

/// <summary>
/// Hash password yang kompatibel dengan kolom <c>SU_PWD</c> lama:
/// <c>FormsAuthentication.HashPasswordForStoringInConfigFile(password, "sha1")</c>
/// = SHA-1 dari byte UTF-8, heksadesimal huruf besar. Hanya untuk verifikasi login selama DB belum berubah.
/// </summary>
public static class LegacyPasswordHasher
{
#pragma warning disable CA5350 // SHA-1 dibutuhkan untuk kompatibilitas data lama
    public static string Hash(string password)
        => Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(password)));
#pragma warning restore CA5350

    public static bool Matches(string password, string storedHash)
        => string.Equals(Hash(password), storedHash?.Trim(), StringComparison.OrdinalIgnoreCase);
}
