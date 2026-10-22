namespace Panin.Application.Auth;

/// <summary>Keputusan login murni (tanpa I/O), port dari <c>ValidateLogin</c> di LOSLOGIN.</summary>
public static class LoginPolicy
{
    /// <param name="FalsePassword">True bila SU_FALSEPWDCOUNT harus naik (password salah, bukan kosong).</param>
    /// <param name="RecordActivity">False bila tidak ada baris user atau password kosong.</param>
    public sealed record Decision(LoginResult Result, bool FalsePassword, bool RecordActivity);

    public static Decision Decide(LoginRow? row, string password, bool otherSessionActive)
    {
        if (row is null)
        {
            return new Decision(LoginResult.NotFound, false, false);
        }

        if (!LegacyPasswordHasher.Matches(password, row.PasswordHash))
        {
            return string.IsNullOrEmpty(password)
                ? new Decision(LoginResult.PasswordEmpty, false, false)
                : new Decision(LoginResult.PasswordInvalid, true, true);
        }

        var defaultHash = string.IsNullOrWhiteSpace(row.DefaultPasswordCheck)
            ? null
            : LegacyPasswordHasher.Hash(row.DefaultPasswordCheck.Trim());

        if (defaultHash is not null && string.Equals(defaultHash, row.PasswordHash.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return new Decision(LoginResult.PasswordDefault, false, true);
        }

        // Satu sesi aktif per user: dari DB (SU_LOGON, host lain < 30 menit) atau dari sesi yang sedang aktif di host ini.
        if (row.Logon == "1" || otherSessionActive)
        {
            return new Decision(LoginResult.HasLogon, false, false);
        }

        if (row.PasswordExpired == "1")
        {
            return new Decision(LoginResult.PasswordExpired, false, true);
        }

        return new Decision(LoginResult.Success, false, true);
    }

    public static string Message(LoginResult result, string userId) => result switch
    {
        LoginResult.NotFound => string.IsNullOrEmpty(userId) ? string.Empty : "Invalid UserID/Password!",
        LoginResult.HasLogon => "User is currently logged in!",
        LoginResult.Locked or LoginResult.JustLocked => "User ID is Locked, Please contact your System Administrator!",
        LoginResult.PasswordEmpty => "Please type in your password...",
        LoginResult.PasswordInvalid => "Invalid UserID/Password",
        LoginResult.Unknown => "Server Error : Unknown Error",
        _ => string.Empty,
    };
}
