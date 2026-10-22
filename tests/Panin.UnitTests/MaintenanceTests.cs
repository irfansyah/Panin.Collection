using Microsoft.Extensions.Options;
using Panin.Application.Maintenance;
using Panin.Application.Sessions;
using Panin.Infrastructure.Sessions;

namespace Panin.UnitTests;

public class UserRulesTests
{
    private static UserDraft Valid() => new() { UserId = "NEW.USER-1", GroupId = "FESPV", FullName = "Budi" };

    [Fact]
    public void Complete_draft_is_valid() => Assert.Empty(UserRules.Validate(Valid()));

    [Fact]
    public void Required_fields_are_reported()
    {
        var errors = UserRules.Validate(new UserDraft());
        Assert.Contains(errors, e => e.StartsWith("User ID", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.StartsWith("Group", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.StartsWith("Nama", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("a b")]
    [InlineData("x';--")]
    [InlineData("123456789012345678901")]
    public void Unsafe_or_too_long_user_ids_are_rejected(string id)
    {
        var d = Valid();
        d.UserId = id;
        Assert.Single(UserRules.Validate(d));
    }

    [Fact]
    public void Own_upliner_is_recursive()
    {
        var d = Valid();
        d.Upliner = "new.user-1";
        Assert.Contains(UserRules.Validate(d), e => e.Contains("Recursive", StringComparison.Ordinal));
    }

    [Fact]
    public void Column_lengths_follow_the_table()
    {
        var d = Valid();
        d.Phone = new string('1', 16);
        d.Email = new string('a', 51) + "@x";
        Assert.Equal(2, UserRules.Validate(d).Count);
    }

    [Fact]
    public void Email_needs_an_at_sign()
    {
        var d = Valid();
        d.Email = "bukan-email";
        Assert.Single(UserRules.Validate(d));
    }
}

public class SessionReleaseAllTests
{
    [Fact]
    public void ReleaseAll_frees_the_user_even_with_an_unknown_session_id()
    {
        var registry = new UserSessionRegistry(Options.Create(new SessionOptions { AbandonedAfterSeconds = 180 }), TimeProvider.System);
        Assert.Equal(SessionStartResult.Started, registry.TryStart("U1", "G", "s1", "h"));
        Assert.Equal(SessionStartResult.BlockedByActiveSession, registry.TryStart("U1", "G", "s2", "h"));

        Assert.True(registry.ReleaseAll("U1"));
        Assert.False(registry.ReleaseAll("U1"));
        Assert.Equal(SessionStartResult.Started, registry.TryStart("U1", "G", "s2", "h"));
    }
}
