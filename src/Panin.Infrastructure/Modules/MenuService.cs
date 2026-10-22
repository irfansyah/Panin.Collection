using System.Data;
using Panin.Application.Abstractions;
using Panin.Application.Modules;
using Panin.Infrastructure.Data;

namespace Panin.Infrastructure.Modules;

public sealed class MenuService(ILoginDb db, SingleFlightCache cache) : IMenuService
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(5);

    public async Task<IReadOnlyList<MenuNode>> GetMenuAsync(string moduleId, string groupId, CancellationToken ct = default)
    {
        return await cache.GetOrCreateAsync($"menu:{moduleId}:{groupId}", CacheFor, async () =>
        {
            var table = await db.QueryTableAsync(
                "select A.MENUID, A.MENUDESC, A.MENUURL, A.MENUPARENT, A.MENUPOSITION " +
                "from RFMENUX A join GRPMENUX B on A.TYPEID = B.TYPEID and A.MENUID = B.MENUID " +
                "where B.TYPEID = @1 and B.GROUPID = @2 order by A.MENUPARENT, A.MENUPOSITION", [moduleId, groupId]).ConfigureAwait(false);
            return (IReadOnlyList<MenuNode>)BuildTree(table);
        }).ConfigureAwait(false) ?? [];
    }

    public static List<MenuNode> BuildTree(DataTable table)
    {
        var rows = table.Rows.Cast<DataRow>()
            .Select(r => (Id: S(r, "MENUID"), Text: S(r, "MENUDESC"), Url: S(r, "MENUURL"), Parent: S(r, "MENUPARENT"),
                Pos: r["MENUPOSITION"] is DBNull ? 0 : Convert.ToInt32(r["MENUPOSITION"], System.Globalization.CultureInfo.InvariantCulture)))
            .ToList();
        var ids = rows.Select(r => r.Id).ToHashSet(StringComparer.Ordinal);

        List<MenuNode> Children(string? parent) => rows
            .Where(r => parent is null
                ? r.Parent.Length == 0 || !ids.Contains(r.Parent)
                : r.Parent == parent)
            .OrderBy(r => r.Pos)
            .ThenBy(r => r.Id, StringComparer.Ordinal)
            .Select(r => new MenuNode(r.Id, r.Text, r.Url.Length == 0 ? null : r.Url, Children(r.Id)))
            .ToList();

        return Children(null);
    }

    private static string S(DataRow r, string c) => r[c] is DBNull ? string.Empty : Convert.ToString(r[c], System.Globalization.CultureInfo.InvariantCulture)!.Trim();
}
