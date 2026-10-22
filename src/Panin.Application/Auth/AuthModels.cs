namespace Panin.Application.Auth;

/// <summary>Hasil upaya login. Pesan tampilan mengikuti sistem lama (<c>getLogonMsg</c>).</summary>
public enum LoginResult
{
    NotFound,
    Success,
    HasLogon,
    Locked,
    JustLocked,
    PasswordExpired,
    PasswordEmpty,
    PasswordInvalid,
    PasswordDefault,
    Unknown,
}

public sealed record AuthenticatedUser(string UserId, string GroupId, string FullName, string GroupName);

/// <param name="User">Terisi bila login berhasil atau perlu ganti password.</param>
/// <param name="SessionId">Terisi hanya bila <see cref="LoginResult.Success"/>.</param>
public sealed record LoginOutcome(LoginResult Result, AuthenticatedUser? User = null, string? SessionId = null)
{
    public bool Succeeded => Result == LoginResult.Success;

    public bool MustChangePassword => Result is LoginResult.PasswordDefault or LoginResult.PasswordExpired;
}

/// <summary>Baris hasil <c>SU_USERLOGINGIN</c>.</summary>
public sealed record LoginRow(
    string UserId,
    string GroupId,
    string PasswordHash,
    string Revoke,
    string PasswordExpired,
    string DefaultPasswordCheck,
    string Logon);

public enum PasswordChangeResult
{
    Changed,
    Mismatch,
    OldPasswordInvalid,
    PolicyViolation,
    Failed,

    /// <summary>Masa paralel: password hanya diubah di aplikasi lama (lihat <c>ParallelRun</c>).</summary>
    ManagedElsewhere,
}

public sealed record PasswordChangeOutcome(PasswordChangeResult Result, string? Message = null);
