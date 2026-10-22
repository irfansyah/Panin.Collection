using Panin.Infrastructure.Data;

namespace Panin.UnitTests;

public class DbCommandPreparerTests
{
    [Fact]
    public void Exec_without_at_sign_gets_positional_parameters()
        => Assert.Equal("exec ES_SAVETOKEN @1, @2, @3", DbCommandPreparer.PrepareSql("exec ES_SAVETOKEN", 3));

    [Theory]
    [InlineData("EXEC SP_LOGUSER @1, 'x', @2")]
    [InlineData("select * from SCUSER where USERID = @1")]
    public void Statements_with_at_sign_are_left_alone(string sql)
        => Assert.Equal(sql, DbCommandPreparer.PrepareSql(sql, 2));

    [Fact]
    public void Exec_with_no_args_is_left_alone()
        => Assert.Equal("exec SP_X", DbCommandPreparer.PrepareSql("exec SP_X", 0));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_strings_become_null_when_enabled(string value)
        => Assert.Equal(DBNull.Value, DbCommandPreparer.NormalizeValue(value, blankToNull: true));

    [Fact]
    public void Blank_strings_are_kept_when_disabled()
        => Assert.Equal("  ", DbCommandPreparer.NormalizeValue("  ", blankToNull: false));

    [Fact]
    public void Legacy_null_date_becomes_null()
        => Assert.Equal(DBNull.Value, DbCommandPreparer.NormalizeValue(new DateTime(1900, 1, 1), true));

    [Fact]
    public void Other_dates_pass_through()
    {
        var d = new DateTime(2025, 7, 7);
        Assert.Equal(d, DbCommandPreparer.NormalizeValue(d, true));
    }

    [Fact]
    public void Parameters_are_named_by_position_and_nulls_are_dbnull()
    {
        var ps = DbCommandPreparer.BuildParameters(["abc", null, 5], true);
        Assert.Equal(["@1", "@2", "@3"], ps.Select(p => p.ParameterName));
        Assert.Equal(DBNull.Value, ps[1].Value);
        Assert.Equal(5, ps[2].Value);
    }
}

public class DapperParameterTests
{
    [Fact]
    public void Dapper_parameters_are_named_by_position_with_normalized_values()
    {
        var p = DbCommandPreparer.BuildDapperParameters(["x", " ", null], blankToNull: true);
        Assert.Equal(["1", "2", "3"], p.ParameterNames);
        Assert.Equal("x", p.Get<string>("1"));
        Assert.Null(p.Get<string?>("2"));
    }
}
