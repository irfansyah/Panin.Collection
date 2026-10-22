using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace Panin.Web.Infrastructure;

/// <summary>
/// Tiket sementara (10 menit) yang dikeluarkan setelah password benar tetapi masih default/kedaluwarsa,
/// sebagai bukti identitas untuk halaman ganti password.
/// </summary>
public sealed record PasswordTicket(string UserId, string Mode)
{
    private const string Purpose = "Panin.PasswordTicket";

    public static string Create(IDataProtectionProvider provider, string userId, string mode)
        => provider.CreateProtector(Purpose).ToTimeLimitedDataProtector().Protect($"{userId}|{mode}", TimeSpan.FromMinutes(10));

    public static PasswordTicket? TryRead(IDataProtectionProvider provider, string token)
    {
        try
        {
            var text = provider.CreateProtector(Purpose).ToTimeLimitedDataProtector().Unprotect(token);
            var parts = text.Split('|', 2);
            return parts.Length == 2 ? new PasswordTicket(parts[0], parts[1]) : null;
        }
        catch (CryptographicException)
        {
            return null;
        }
    }
}
