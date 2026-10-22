using Panin.Application.Collection;
using Panin.Infrastructure.Collection;

namespace Panin.UnitTests;

public class WorklistTests
{
    private static readonly DateTime Today = new(2026, 10, 22);

    private static ReassignRequest Valid() => new() { TargetUserId = "AO01", AccountNos = ["123", "456"] };

    [Fact]
    public void Valid_request_has_no_errors() => Assert.Empty(WorklistRules.Validate(Valid(), Today));

    [Fact]
    public void Target_and_selection_are_required()
    {
        var errors = WorklistRules.Validate(new ReassignRequest(), Today);
        Assert.Contains(errors, e => e.StartsWith("User tujuan", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.StartsWith("Pilih minimal", StringComparison.Ordinal));
    }

    [Fact]
    public void Temporary_move_needs_a_date_not_in_the_past()
    {
        var r = Valid();
        r.Permanent = false;
        Assert.Single(WorklistRules.Validate(r, Today));
        r.Until = Today.AddDays(-1);
        Assert.Single(WorklistRules.Validate(r, Today));
        r.Until = Today;
        Assert.Empty(WorklistRules.Validate(r, Today));
    }

    [Theory]
    [InlineData("1' OR 1=1 --")]
    [InlineData("12,34")]
    [InlineData("a b")]
    public void Unsafe_tokens_are_rejected(string token)
    {
        var r = Valid();
        r.AccountNos = [token];
        Assert.Contains(WorklistRules.Validate(r, Today), e => e.Contains("tidak valid", StringComparison.Ordinal));
        r.AccountNos = ["1"];
        r.GroupUserIds = [token];
        Assert.Contains(WorklistRules.Validate(r, Today), e => e.Contains("tidak valid", StringComparison.Ordinal));
    }

    [Fact]
    public void Too_many_accounts_are_rejected()
    {
        var r = Valid();
        r.AccountNos = [.. Enumerable.Range(0, WorklistRules.MaxDebitors + 1).Select(i => i.ToString(System.Globalization.CultureInfo.InvariantCulture))];
        Assert.Single(WorklistRules.Validate(r, Today));
    }

    [Fact]
    public void Reassign_batch_is_one_transaction_with_a_call_per_chunk()
    {
        var r = Valid();
        r.GroupUserIds = ["TEAM1", "TEAM2"];
        var (sql, args) = WorklistService.BuildReassign("BOSS", r, ["'',1,2", "'',3"]);

        Assert.StartsWith("SET XACT_ABORT ON; BEGIN TRAN; ", sql, StringComparison.Ordinal);
        Assert.EndsWith("COMMIT TRAN", sql, StringComparison.Ordinal);
        Assert.Equal(2, sql.Split("exec SP_REASIGN", StringSplitOptions.None).Length - 1);
        Assert.Contains("exec SP_REASIGN @1, @2, @6, @3, NULL, @4;", sql, StringComparison.Ordinal);
        Assert.Contains("exec SP_REASIGN @1, @5, @7, @3, NULL, @4;", sql, StringComparison.Ordinal);
        Assert.Equal("AO01", args[0]);
        Assert.Equal("'',TEAM1,TEAM2", args[1]);
        Assert.Equal("BOSS", args[2]);
        Assert.Null(args[3]);
        Assert.Equal("''", args[4]);
        Assert.Equal("'',1,2", args[5]);
        Assert.Equal("'',3", args[6]);
    }

    [Fact]
    public void Temporary_move_passes_the_end_date_and_groups_only_still_run_once()
    {
        var r = new ReassignRequest { TargetUserId = "AO01", GroupUserIds = ["TEAM1"], Permanent = false, Until = new DateTime(2026, 11, 5, 13, 0, 0) };
        var (sql, args) = WorklistService.BuildReassign("BOSS", r, []);
        Assert.Equal(1, sql.Split("exec SP_REASIGN", StringSplitOptions.None).Length - 1);
        Assert.Equal(new DateTime(2026, 11, 5), args[3]);
        Assert.Equal("''", args[5]);
    }
}
