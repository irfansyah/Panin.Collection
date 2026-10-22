using System.Net;
using System.Security.Claims;
using Panin.Application.Auth;

namespace Panin.Web.Infrastructure;

/// <summary>Identitas sesi yang disimpan di cookie autentikasi.</summary>
public sealed record SessionUser(string UserId, string GroupId, string FullName, string GroupName, string SessionId, string Host);

public static class SessionUserExtensions
{
    public const string GroupClaim = "panin:group";
    public const string GroupNameClaim = "panin:groupname";
    public const string SessionClaim = "panin:sid";
    public const string HostClaim = "panin:host";

    public static ClaimsPrincipal ToPrincipal(this AuthenticatedUser user, string sessionId, string host)
    {
        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, user.UserId),
                new Claim(ClaimTypes.Name, user.FullName),
                new Claim(GroupClaim, user.GroupId),
                new Claim(GroupNameClaim, user.GroupName),
                new Claim(SessionClaim, sessionId),
                new Claim(HostClaim, host),
            ],
            "Cookies");
        return new ClaimsPrincipal(identity);
    }

    public static SessionUser? ToSessionUser(this ClaimsPrincipal? principal)
    {
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        string? Get(string type) => principal.FindFirst(type)?.Value;
        var userId = Get(ClaimTypes.NameIdentifier);
        var group = Get(GroupClaim);
        var sid = Get(SessionClaim);
        if (userId is null || group is null || sid is null)
        {
            return null;
        }

        return new SessionUser(userId, group, Get(ClaimTypes.Name) ?? userId, Get(GroupNameClaim) ?? group, sid, Get(HostClaim) ?? string.Empty);
    }

    /// <summary>
    /// Alamat klien untuk kolom host di DB LOGIN (<c>varchar(16)</c> di SP lama): IPv4 apa adanya,
    /// loopback IPv6 dipetakan ke 127.0.0.1, IPv6 lain dipotong.
    /// </summary>
    public static string ClientHost(this HttpContext http)
    {
        var ip = http.Connection.RemoteIpAddress;
        if (ip is null)
        {
            return "unknown";
        }

        if (IPAddress.IsLoopback(ip))
        {
            return "127.0.0.1";
        }

        if (ip.IsIPv4MappedToIPv6)
        {
            ip = ip.MapToIPv4();
        }

        var text = ip.ToString();
        return text.Length <= 16 ? text : text[..16];
    }
}
