using Panin.Application.Maintenance;
using Panin.Application.Modules;

namespace Panin.Infrastructure.Maintenance;

public sealed class ParameterSetService(IModuleDbFactory dbs) : IParameterSetService
{
    public async Task<IReadOnlyList<ParameterSetItem>> GetItemsAsync(string setId, bool maker, CancellationToken ct = default)
    {
        var db = await dbs.GetDataDbAsync(ParameterEngine.ModuleId, ct).ConfigureAwait(false);
        // Kolom ISMAKER/ISAPPRV memilih entri untuk menu Maker atau Approval; tautan lama dipakai hanya untuk menentukan halaman tujuan.
        var column = maker ? "ISMAKER" : "ISAPPRV";
        var link = maker ? "PARAMLINKMAKER" : "PARAMLINKAPPRV";
        var table = await db.QueryTableAsync($"SELECT PARAMDESC, {link} FROM PARAMETERSYSTEM WHERE SETID = @1 AND {column} = 1 ORDER BY PARAMDESC", [setId], ct: ct).ConfigureAwait(false);
        return [.. table.Rows.Cast<System.Data.DataRow>().Select(r => new ParameterSetItem(ParameterEngine.Cell(r[0]), ParameterEngine.Cell(r[1])))];
    }
}
