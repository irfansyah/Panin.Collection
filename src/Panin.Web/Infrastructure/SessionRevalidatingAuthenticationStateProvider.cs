using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.Extensions.Options;
using Panin.Application.Sessions;

namespace Panin.Web.Infrastructure;

/// <summary>
/// Memeriksa ulang tiap menit apakah sesi sirkuit masih yang sah di registry
/// (mis. dilepas karena login dari tempat lain atau sesi sudah dibersihkan).
/// Pemeriksaan murni di memori sehingga murah untuk ratusan sirkuit.
/// </summary>
public sealed class SessionRevalidatingAuthenticationStateProvider(
    ILoggerFactory loggerFactory,
    IUserSessionRegistry registry) : RevalidatingServerAuthenticationStateProvider(loggerFactory)
{
    protected override TimeSpan RevalidationInterval => TimeSpan.FromMinutes(1);

    protected override Task<bool> ValidateAuthenticationStateAsync(AuthenticationState authenticationState, CancellationToken cancellationToken)
    {
        var user = authenticationState.User.ToSessionUser();
        return Task.FromResult(user is not null && registry.Validate(user.UserId, user.GroupId, user.SessionId, user.Host));
    }
}
