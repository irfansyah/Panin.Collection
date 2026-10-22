using System.Data;
using Panin.Application.Abstractions;
using Panin.Application.Collection;
using Panin.Application.Modules;

namespace Panin.Infrastructure.Collection;

/// <summary>
/// Data layar "Monitoring Collection" dan info debitur dari DB DATA modul 60. Hanya membaca. Kolom view dipilih satu per satu
/// (bukan <c>SELECT *</c>): kolom yang dipakai saja membuat <c>VW_ACCOUNT_SURAT</c> ±170 ms, dibanding ±276 ms untuk semua 74 kolom.
/// </summary>
public sealed class DebiturDetailService(IModuleDbFactory dbs) : IDebiturDetailService
{
    public static readonly string AccountSql =
        "select " + string.Join(", ", DebiturDetailFields.AccountColumns.Select(c => $"[{c}]")) + " from VW_ACCOUNT_SURAT where ACC_NO = @1";

    public async Task<DetailRecord?> GetAccountAsync(string accountNo, CancellationToken ct = default)
    {
        var db = await Db(ct).ConfigureAwait(false);
        return ToRecord(await db.QueryFirstOrDefaultAsync(AccountSql, [accountNo], ct: ct).ConfigureAwait(false));
    }

    public async Task<bool> IsRevolvingAsync(string accountNo, CancellationToken ct = default)
    {
        var db = await Db(ct).ConfigureAwait(false);
        var type = await db.ExecuteScalarAsync<string>(
            "select P.APP_TYPE from LOAN_MASTER LM left join RFPRODUCT P on P.PRODUCTID = LM.LOAN_TYPE where LM.ACC_NO = @1", [accountNo], ct: ct).ConfigureAwait(false);
        return type?.Trim() == "20";
    }

    public async Task<IReadOnlyList<DelinquencyMonth>> GetDelinquencyAsync(string accountNo, CancellationToken ct = default)
    {
        var db = await Db(ct).ConfigureAwait(false);
        var table = await db.QueryTableAsync("exec USP_VWDELINQHIST @1", [accountNo], ct: ct).ConfigureAwait(false);
        return [.. table.Rows.Cast<DataRow>()
            .Select(r => new DelinquencyMonth(Int(r, "DH_YEAR") ?? 0, Int(r, "DH_MONTH") ?? 0, Str(r, "DH_CODE"), Int(r, "DH_DPD")))
            .OrderBy(m => m.Year).ThenBy(m => m.Month)];
    }

    public async Task<DetailRecord?> GetPersonalAsync(string accountNo, CancellationToken ct = default)
        => ToRecord(await (await Db(ct).ConfigureAwait(false)).QueryFirstOrDefaultAsync(
            "select * from VW_CUSTOMER_PERSONALINFO where ACC_NO = @1", [accountNo], ct: ct).ConfigureAwait(false));

    public Task<DetailRecord?> GetJobAsync(string accountNo, CancellationToken ct = default) => Proc("exec SP_JOB_INFO @1", accountNo, ct);

    public Task<DetailRecord?> GetSpouseAsync(string accountNo, CancellationToken ct = default) => Proc("exec SP_SPOUSE_INFO @1", accountNo, ct);

    public Task<DetailRecord?> GetSpouseJobAsync(string accountNo, CancellationToken ct = default) => Proc("exec SP_SPOUSE_JOB @1", accountNo, ct);

    public async Task<IReadOnlyList<DetailRecord>> GetContactsAsync(string customerRef, CancellationToken ct = default)
    {
        var table = await (await Db(ct).ConfigureAwait(false)).QueryTableAsync("exec SP_GET_CONTACT_INFO @1", [customerRef], ct: ct).ConfigureAwait(false);
        return [.. table.Rows.Cast<DataRow>().Select(r => ToRecord(r)!)];
    }

    private async Task<DetailRecord?> Proc(string sql, string accountNo, CancellationToken ct)
        => ToRecord(await (await Db(ct).ConfigureAwait(false)).QueryFirstOrDefaultAsync(sql, [accountNo], ct: ct).ConfigureAwait(false));

    private Task<IDb> Db(CancellationToken ct) => dbs.GetDataDbAsync("60", ct);

    private static DetailRecord? ToRecord(DataRow? row)
    {
        if (row is null)
        {
            return null;
        }

        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (DataColumn c in row.Table.Columns)
        {
            values[c.ColumnName] = row[c] is DBNull ? null : row[c];
        }

        return new DetailRecord(values);
    }

    private static string Str(DataRow r, string c) => r[c] is DBNull ? string.Empty : Convert.ToString(r[c], System.Globalization.CultureInfo.InvariantCulture)!.Trim();

    private static int? Int(DataRow r, string c)
        => r[c] is DBNull ? null : int.TryParse(Convert.ToString(r[c], System.Globalization.CultureInfo.InvariantCulture), out var v) ? v : null;
}
