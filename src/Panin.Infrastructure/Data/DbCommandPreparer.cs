using System.Globalization;
using Microsoft.Data.SqlClient;

namespace Panin.Infrastructure.Data;

/// <summary>
/// Menerjemahkan konvensi <c>DMS.Tools.DbConnection</c> (parameter posisional <c>@1..@n</c>)
/// menjadi <see cref="SqlParameter"/> sungguhan. Perilaku yang dipertahankan dari aplikasi lama:
/// <list type="bullet">
/// <item><c>exec nama_sp</c> tanpa tanda '@' otomatis diberi <c>@1, @2, ...</c> sesuai jumlah argumen.</item>
/// <item>String kosong/spasi menjadi NULL bila <c>BlankToNull</c> aktif.</item>
/// <item>DateTime 1900-01-01 menjadi NULL (sentinel "tidak ada tanggal" milik kontrol lama).</item>
/// <item>null menjadi DBNull.</item>
/// </list>
/// </summary>
public static class DbCommandPreparer
{
    private static readonly DateTime LegacyNullDate = new(1900, 1, 1);

    public static string PrepareSql(string sql, int argCount)
    {
        ArgumentNullException.ThrowIfNull(sql);
        var trimmed = sql.Trim();
        if (argCount > 0
            && trimmed.StartsWith("exec", StringComparison.OrdinalIgnoreCase)
            && !trimmed.Contains('@', StringComparison.Ordinal))
        {
            var names = Enumerable.Range(1, argCount).Select(i => "@" + i.ToString(CultureInfo.InvariantCulture));
            return trimmed + " " + string.Join(", ", names);
        }

        return trimmed;
    }

    public static object NormalizeValue(object? value, bool blankToNull)
    {
        switch (value)
        {
            case null:
                return DBNull.Value;
            case string s when blankToNull && string.IsNullOrWhiteSpace(s):
                return DBNull.Value;
            case DateTime d when d == LegacyNullDate:
                return DBNull.Value;
            default:
                return value;
        }
    }

    public static Dapper.DynamicParameters BuildDapperParameters(object?[]? args, bool blankToNull)
    {
        var result = new Dapper.DynamicParameters();
        if (args is null)
        {
            return result;
        }

        for (var i = 0; i < args.Length; i++)
        {
            var value = NormalizeValue(args[i], blankToNull);
            result.Add((i + 1).ToString(CultureInfo.InvariantCulture), value is DBNull ? null : value);
        }

        return result;
    }

    public static SqlParameter[] BuildParameters(object?[]? args, bool blankToNull)
    {
        if (args is null or { Length: 0 })
        {
            return [];
        }

        var result = new SqlParameter[args.Length];
        for (var i = 0; i < args.Length; i++)
        {
            result[i] = new SqlParameter("@" + (i + 1).ToString(CultureInfo.InvariantCulture), NormalizeValue(args[i], blankToNull));
        }

        return result;
    }
}
