using Panin.Application.Auth;

namespace Panin.UnitTests;

public class LegacyPasswordHasherTests
{
    [Fact]
    public void Hash_is_uppercase_hex_sha1_of_utf8()
        => Assert.Equal("A9993E364706816ABA3E25717850C26C9CD0D89D", LegacyPasswordHasher.Hash("abc"));

    [Theory]
    [InlineData("a9993e364706816aba3e25717850c26c9cd0d89d")]
    [InlineData(" A9993E364706816ABA3E25717850C26C9CD0D89D ")]
    public void Matches_ignores_case_and_padding_of_stored_hash(string stored)
        => Assert.True(LegacyPasswordHasher.Matches("abc", stored));

    [Fact]
    public void Matches_rejects_other_password()
        => Assert.False(LegacyPasswordHasher.Matches("abd", "A9993E364706816ABA3E25717850C26C9CD0D89D"));
}

public class LoginPolicyTests
{
    private static LoginRow Row(
        string password = "secret", string revoke = "0", string expired = "0", string logon = "0", string defaultPwd = "default123")
        => new("U1", "G1", LegacyPasswordHasher.Hash(password), revoke, expired, defaultPwd, logon);

    [Fact]
    public void Unknown_user_is_not_found_and_records_nothing()
    {
        var d = LoginPolicy.Decide(null, "x", false);
        Assert.Equal(LoginResult.NotFound, d.Result);
        Assert.False(d.RecordActivity);
    }

    [Fact]
    public void Correct_password_succeeds()
    {
        var d = LoginPolicy.Decide(Row(), "secret", false);
        Assert.Equal(LoginResult.Success, d.Result);
        Assert.False(d.FalsePassword);
        Assert.True(d.RecordActivity);
    }

    [Fact]
    public void Wrong_password_counts_as_false_attempt()
    {
        var d = LoginPolicy.Decide(Row(), "nope", false);
        Assert.Equal(LoginResult.PasswordInvalid, d.Result);
        Assert.True(d.FalsePassword);
        Assert.True(d.RecordActivity);
    }

    [Fact]
    public void Empty_password_is_not_counted_as_false_attempt()
    {
        var d = LoginPolicy.Decide(Row(), string.Empty, false);
        Assert.Equal(LoginResult.PasswordEmpty, d.Result);
        Assert.False(d.FalsePassword);
        Assert.False(d.RecordActivity);
    }

    [Fact]
    public void Default_password_forces_change_before_anything_else()
    {
        var d = LoginPolicy.Decide(Row(password: "default123", logon: "1", expired: "1"), "default123", true);
        Assert.Equal(LoginResult.PasswordDefault, d.Result);
    }

    [Theory]
    [InlineData("1", false)]
    [InlineData("0", true)]
    public void Existing_session_blocks_login(string dbLogon, bool localSession)
    {
        var d = LoginPolicy.Decide(Row(logon: dbLogon), "secret", localSession);
        Assert.Equal(LoginResult.HasLogon, d.Result);
        Assert.False(d.RecordActivity);
    }

    [Fact]
    public void Expired_password_requires_change()
        => Assert.Equal(LoginResult.PasswordExpired, LoginPolicy.Decide(Row(expired: "1"), "secret", false).Result);

    [Fact]
    public void Blank_default_password_setting_does_not_flag_default()
        => Assert.Equal(LoginResult.Success, LoginPolicy.Decide(Row(defaultPwd: ""), "secret", false).Result);

    [Theory]
    [InlineData(LoginResult.HasLogon, "User is currently logged in!")]
    [InlineData(LoginResult.Locked, "User ID is Locked, Please contact your System Administrator!")]
    [InlineData(LoginResult.PasswordInvalid, "Invalid UserID/Password")]
    public void Messages_match_legacy_text(LoginResult result, string expected)
        => Assert.Equal(expected, LoginPolicy.Message(result, "u"));
}
