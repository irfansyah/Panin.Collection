using System.Data;
using Panin.Application.Abstractions;
using Panin.Application.Modules;
using Panin.Infrastructure.Data;

namespace Panin.Infrastructure.Modules;

public sealed class ModuleRegistry(ILoginDb db, IEnumerable<ModuleDefinition> definitions, SingleFlightCache cache) : IModuleRegistry
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(5);
    private readonly ModuleDefinition[] _definitions = [.. definitions];

    public IReadOnlyList<ModuleDefinition> Definitions => _definitions;

    public async Task<IReadOnlyList<AccessibleModule>> GetAccessibleAsync(string groupId, CancellationToken ct = default)
    {
        var rows = await cache.GetOrCreateAsync($"modules:group:{groupId}", CacheFor, async () =>
        {
            var table = await db.QueryTableAsync(
                "select MODULEID, MODULENAME from VW_GRPACCESSMODULE where GROUPID = @1 order by MODULEID", [groupId]).ConfigureAwait(false);
            return table.Rows.Cast<DataRow>().Select(r => (Id: S(r, "MODULEID"), Name: S(r, "MODULENAME"))).ToArray();
        }).ConfigureAwait(false) ?? [];

        var result = new List<AccessibleModule>();
        foreach (var (id, name) in rows)
        {
            var def = _definitions.FirstOrDefault(d => d.Id == id);
            if (def is not null)
            {
                result.Add(new AccessibleModule(id, string.IsNullOrWhiteSpace(name) ? def.Title : name, def.HomeRoute));
            }
        }

        return result;
    }

    public async Task<ModuleRecord?> GetRecordAsync(string moduleId, CancellationToken ct = default)
    {
        return await cache.GetOrCreateAsync($"modules:record:{moduleId}", CacheFor, async () =>
        {
            var r = await db.QueryFirstOrDefaultAsync(
                "select MODULEID, MODULENAME, DB_IP, DB_NAMA, DB_LOGINID, DB_LOGINPWD, ACTIVE from RFMODULE where MODULEID = @1", [moduleId]).ConfigureAwait(false);
            return r is null
                ? null
                : new ModuleRecord(S(r, "MODULEID"), S(r, "MODULENAME"), N(r, "DB_IP"), N(r, "DB_NAMA"), N(r, "DB_LOGINID"), N(r, "DB_LOGINPWD"), S(r, "ACTIVE") is "1" or "True");
        }).ConfigureAwait(false);
    }

    private static string S(DataRow r, string c) => r[c] is DBNull ? string.Empty : Convert.ToString(r[c], System.Globalization.CultureInfo.InvariantCulture)!.Trim();

    private static string? N(DataRow r, string c) => S(r, c) is { Length: > 0 } s ? s : null;
}
