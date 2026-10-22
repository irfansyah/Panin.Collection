using System.Data;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Panin.Application.Sessions;
using Panin.Infrastructure.Modules;
using Panin.Infrastructure.Sessions;

namespace Panin.UnitTests;

public class UserSessionRegistryTests
{
    private readonly FakeTimeProvider _time = new();

    private UserSessionRegistry Create() => new(Options.Create(new SessionOptions { AbandonedAfterSeconds = 180 }), _time);

    [Fact]
    public void Second_login_is_blocked_while_first_session_is_alive()
    {
        var r = Create();
        Assert.Equal(SessionStartResult.Started, r.TryStart("U1", "G", "s1", "h"));
        Assert.Equal(SessionStartResult.BlockedByActiveSession, r.TryStart("U1", "G", "s2", "h"));
        Assert.True(r.HasActiveSession("U1"));
        Assert.False(r.HasActiveSession("U1", exceptSessionId: "s1"));
    }

    [Fact]
    public void User_ids_are_case_insensitive()
    {
        var r = Create();
        r.TryStart("abc", "G", "s1", "h");
        Assert.Equal(SessionStartResult.BlockedByActiveSession, r.TryStart("ABC", "G", "s2", "h"));
    }

    [Fact]
    public void Session_without_heartbeat_can_be_replaced()
    {
        var r = Create();
        r.TryStart("U1", "G", "s1", "h");
        _time.Advance(TimeSpan.FromSeconds(181));
        Assert.Equal(SessionStartResult.Started, r.TryStart("U1", "G", "s2", "h"));
        Assert.False(r.Validate("U1", "G", "s1", "h"));
        Assert.True(r.Validate("U1", "G", "s2", "h"));
    }

    [Fact]
    public void Heartbeat_keeps_session_alive()
    {
        var r = Create();
        r.TryStart("U1", "G", "s1", "h");
        _time.Advance(TimeSpan.FromSeconds(120));
        r.Touch("U1", "s1");
        _time.Advance(TimeSpan.FromSeconds(120));
        Assert.Equal(SessionStartResult.BlockedByActiveSession, r.TryStart("U1", "G", "s2", "h"));
    }

    [Fact]
    public void Validate_restores_session_after_restart()
    {
        var r = Create();
        Assert.True(r.Validate("U1", "G", "cookie-session", "h"));
        Assert.True(r.HasActiveSession("U1"));
        Assert.False(r.Validate("U1", "G", "other", "h"));
    }

    [Fact]
    public void Release_only_removes_matching_session()
    {
        var r = Create();
        r.TryStart("U1", "G", "s1", "h");
        Assert.False(r.Release("U1", "other"));
        Assert.True(r.Release("U1", "s1"));
        Assert.Equal(0, r.ActiveCount);
    }

    [Fact]
    public void Reap_removes_only_abandoned_sessions()
    {
        var r = Create();
        r.TryStart("OLD", "G", "s1", "h");
        _time.Advance(TimeSpan.FromSeconds(200));
        r.TryStart("NEW", "G", "s2", "h");

        var expired = r.ReapExpired(TimeSpan.FromSeconds(180));

        Assert.Equal(["OLD"], expired.Select(e => e.UserId));
        Assert.Equal(1, r.ActiveCount);
    }
}

public class MenuTreeTests
{
    private static DataTable Table(params (string id, string text, string? url, string? parent, int pos)[] rows)
    {
        var t = new DataTable();
        t.Columns.Add("MENUID");
        t.Columns.Add("MENUDESC");
        t.Columns.Add("MENUURL");
        t.Columns.Add("MENUPARENT");
        t.Columns.Add("MENUPOSITION", typeof(int));
        foreach (var r in rows)
        {
            t.Rows.Add(r.id, r.text, (object?)r.url ?? DBNull.Value, (object?)r.parent ?? DBNull.Value, r.pos);
        }

        return t;
    }

    [Fact]
    public void Builds_hierarchy_sorted_by_position()
    {
        var tree = MenuService.BuildTree(Table(
            ("20", "Reporting", null, null, 2),
            ("10", "Collection", null, null, 1),
            ("1020", "Bulk Surat", "/collection/bulk", "10", 2),
            ("1010", "Distribusi", "~/Main/MainFacility.aspx", "10", 1)));

        Assert.Equal(["Collection", "Reporting"], tree.Select(n => n.Text));
        Assert.Equal(["Distribusi", "Bulk Surat"], tree[0].Children.Select(n => n.Text));
        Assert.True(tree[1].IsLeaf);
        Assert.Equal("/collection/bulk", tree[0].Children[1].Url);
    }

    [Fact]
    public void Item_whose_parent_is_not_granted_becomes_a_root()
    {
        var tree = MenuService.BuildTree(Table(("1010", "Orphan", "/x", "10", 1)));
        Assert.Single(tree);
        Assert.Equal("Orphan", tree[0].Text);
    }

    [Fact]
    public void Blank_url_becomes_null()
    {
        var tree = MenuService.BuildTree(Table(("10", "Group", "", null, 1)));
        Assert.Null(tree[0].Url);
    }
}
