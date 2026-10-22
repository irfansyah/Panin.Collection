using System.Data;
using Microsoft.Extensions.Logging;
using Panin.Application.Abstractions;
using Panin.Application.Auth;
using Panin.Application.Sessions;
using Panin.Infrastructure.Data;

namespace Panin.Infrastructure.Auth;

/// <summary>Port dari alur login LOSLOGIN/LOSCOLLECTION, memakai stored procedure DB LOGIN yang sama.</summary>
public sealed partial class AuthService(ILoginDb db, IUserSessionRegistry sessions, ILogoutRecorder logoutRecorder, ILogger<AuthService> logger) : IAuthService
{
    public async Task<LoginOutcome> LoginAsync(string userId, string password, string host, CancellationToken ct = default)
    {
        userId = (userId ?? string.Empty).Trim();
        password ??= string.Empty;
        try
        {
            var table = await db.QueryFirstOrDefaultAsync("exec SU_USERLOGINGIN", [userId, host], ct: ct).ConfigureAwait(false);
            var row = table is null ? null : ToLoginRow(table);

            var decision = LoginPolicy.Decide(row, password, sessions.HasActiveSession(row?.UserId ?? userId));
            var result = decision.Result;

            if (row is not null && decision.RecordActivity)
            {
                await DeadlockRetry.RunAsync(
                    () => db.ExecuteAsync(
                        "exec SU_ALLUSERACTIVITY @1, @2, @3, @4, '1', @5, null, @6",
                        [row.UserId, row.GroupId, decision.FalsePassword ? "1" : "0", row.Revoke, host, row.Logon],
                        ct: ct),
                    ct: ct).ConfigureAwait(false);
            }

            // Cek revoke (terkunci) setelah aktivitas dicatat; SU_ALLUSERACTIVITY dapat mengunci user.
            var revoked = await db.QueryFirstOrDefaultAsync(
                "select SU_REVOKE from scalluserflag where USERID = @1 and SU_REVOKE = '1'", [userId], ct: ct).ConfigureAwait(false);
            if (revoked is not null)
            {
                result = row is not null && row.Revoke == "0" ? LoginResult.JustLocked : LoginResult.Locked;
            }

            if (row is null || result is not (LoginResult.Success or LoginResult.PasswordDefault or LoginResult.PasswordExpired))
            {
                return new LoginOutcome(result);
            }

            var user = await GetUserAsync(row.UserId, ct).ConfigureAwait(false);
            if (user is null)
            {
                return new LoginOutcome(LoginResult.NotFound);
            }

            if (result != LoginResult.Success)
            {
                return new LoginOutcome(result, user);
            }

            var sessionId = Guid.NewGuid().ToString("N");
            if (sessions.TryStart(user.UserId, user.GroupId, sessionId, host) == SessionStartResult.BlockedByActiveSession)
            {
                return new LoginOutcome(LoginResult.HasLogon);
            }

            await DeadlockRetry.RunAsync(() => db.ExecuteAsync("exec SU_LOGINSTARTED @1, @2", [user.UserId, host], ct: ct), ct: ct).ConfigureAwait(false);
            await DeadlockRetry.RunAsync(() => db.ExecuteAsync("exec SP_LOGUSER @1, '1', @2", [user.UserId, host], ct: ct), ct: ct).ConfigureAwait(false);
            return new LoginOutcome(LoginResult.Success, user, sessionId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogLoginFailure(logger, ex);
            return new LoginOutcome(LoginResult.Unknown);
        }
    }

    public async Task LogoutAsync(string userId, string groupId, string sessionId, string host, CancellationToken ct = default)
    {
        // Sesi dilepas seketika; pencatatan ke DB masuk antrean latar (lihat LogoutWriter).
        sessions.Release(userId, sessionId);
        logoutRecorder.Enqueue(userId, groupId, host);
        await Task.CompletedTask;
    }

    public async Task RecordLogoutAsync(string userId, string groupId, string host, CancellationToken ct = default)
    {
        try
        {
            await DeadlockRetry.RunAsync(() => db.ExecuteAsync("exec SU_ALLUSERACTIVITY @1, @2, '0', '0', '0', @3", [userId, groupId, host], ct: ct), ct: ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogLogoutFailure(logger, ex, userId);
        }
    }

    public async Task<bool> HeartbeatAsync(string userId, string sessionId, string host, CancellationToken ct = default)
    {
        sessions.Touch(userId, sessionId);
        var affected = await DeadlockRetry.RunAsync(() => db.ExecuteScalarAsync<int>("exec SU_MONITORACTIVITY @1, @2", [userId, host], ct: ct), ct: ct).ConfigureAwait(false);
        return affected > 0;
    }

    public async Task<PasswordChangeOutcome> ChangePasswordAsync(
        string userId, string? oldPassword, string newPassword, string verifyPassword, CancellationToken ct = default)
    {
        if (newPassword.Trim() != verifyPassword.Trim())
        {
            return new PasswordChangeOutcome(PasswordChangeResult.Mismatch, "Password mismatch!");
        }

        if (oldPassword is not null)
        {
            var stored = await db.ExecuteScalarAsync<string>("select SU_PWD from SCALLUSER where USERID = @1", [userId], ct: ct).ConfigureAwait(false);
            if (stored is null || !LegacyPasswordHasher.Matches(oldPassword.Trim(), stored))
            {
                return new PasswordChangeOutcome(PasswordChangeResult.OldPasswordInvalid, "Old Password invalid!");
            }
        }

        var hash = LegacyPasswordHasher.Hash(newPassword);
        var violation = await db.ExecuteScalarAsync<string>("select dbo.isPwdValid(@1, @2, @3)", [userId, newPassword, hash], ct: ct).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(violation))
        {
            return new PasswordChangeOutcome(PasswordChangeResult.PolicyViolation, violation);
        }

        await db.ExecuteAsync("exec SU_SCALLUSERPASSWORD @1, @2", [userId, hash], ct: ct).ConfigureAwait(false);
        return new PasswordChangeOutcome(PasswordChangeResult.Changed);
    }

    public async Task<AuthenticatedUser?> GetUserAsync(string userId, CancellationToken ct = default)
    {
        var r = await db.QueryFirstOrDefaultAsync(
            "select USERID, GROUPID, SU_FULLNAME, SG_GRPNAME from VW_SESSIONLOS where USERID = @1", [userId], ct: ct).ConfigureAwait(false);
        return r is null ? null : new AuthenticatedUser(S(r, "USERID"), S(r, "GROUPID"), S(r, "SU_FULLNAME"), S(r, "SG_GRPNAME"));
    }

    private static LoginRow ToLoginRow(DataRow r) => new(
        S(r, "USERID"), S(r, "GROUPID"), S(r, "SU_PWD"), S(r, "SU_REVOKE"), S(r, "SU_PWDEXPIRED"), S(r, "CHECKDEFPWD"), S(r, "SU_LOGON"));

    private static string S(DataRow r, string column)
        => r.Table.Columns.Contains(column) && r[column] is not DBNull ? Convert.ToString(r[column], System.Globalization.CultureInfo.InvariantCulture)!.Trim() : string.Empty;

    [LoggerMessage(Level = LogLevel.Error, Message = "Login gagal karena kesalahan server")]
    private static partial void LogLoginFailure(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Gagal mencatat logout untuk {UserId}")]
    private static partial void LogLogoutFailure(ILogger logger, Exception ex, string userId);
}
