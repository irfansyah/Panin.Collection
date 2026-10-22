using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Panin.Application.Auth;
using Panin.Application.Maintenance;
using Panin.Infrastructure.Auth;
using Panin.Infrastructure.Maintenance;

namespace Panin.UnitTests;

public class ParallelRunTests
{
    private static IOptions<ParallelRunOptions> On(string? url = null) => Options.Create(new ParallelRunOptions { Enabled = true, OldAppUrl = url });

    [Fact]
    public void Messages_point_to_the_old_app_when_a_url_is_configured()
    {
        var o = new ParallelRunOptions { Enabled = true, OldAppUrl = "http://lama/login" };
        Assert.Contains("http://lama/login", o.UserGroupMessage, StringComparison.Ordinal);
        Assert.Contains("http://lama/login", o.PasswordMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("Aplikasi lama:", new ParallelRunOptions().PasswordMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Parallel_mode_is_off_by_default() => Assert.False(new ParallelRunOptions().Enabled);

    [Fact]
    public async Task Password_change_is_refused_while_running_in_parallel()
    {
        // Tanpa DB: penolakan terjadi sebelum ada akses ke DB.
        var auth = new AuthService(null!, null!, null!, On("http://lama"), NullLogger<AuthService>.Instance);
        var outcome = await auth.ChangePasswordAsync("U1", "lama", "baru123", "baru123");
        Assert.Equal(PasswordChangeResult.ManagedElsewhere, outcome.Result);
        Assert.Contains("aplikasi lama", outcome.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task User_and_group_writes_are_refused_while_running_in_parallel()
    {
        var users = new UserMaintenanceService(null!, null!, null!, Options.Create(new MaintenanceOptions()), On(), NullLogger<UserMaintenanceService>.Instance);
        var groups = new GroupMaintenanceService(null!, null!, null!, Options.Create(new MaintenanceOptions()), On(), NullLogger<GroupMaintenanceService>.Instance);
        var draft = new UserDraft { UserId = "U1", GroupId = "G", FullName = "N" };

        foreach (var result in new[]
        {
            await users.SavePendingAsync("M", draft),
            await users.DeleteToPendingAsync("M", "U2"),
            await users.ApproveAsync("A", [Guid.NewGuid()]),
            await users.ResetPasswordAsync("M", "U2"),
            await groups.SavePendingAsync("M", new GroupDraft { GroupId = "G", Name = "N" }),
            await groups.DeleteToPendingAsync("M", "MG", "G2"),
            await groups.ApproveAsync("A", [Guid.NewGuid()]),
            await groups.SaveMenuAccessAsync("M", "MG", "G2", "60", ["10"]),
        })
        {
            Assert.False(result.Success);
            Assert.Contains("aplikasi lama", Assert.Single(result.Errors), StringComparison.Ordinal);
        }
    }
}
