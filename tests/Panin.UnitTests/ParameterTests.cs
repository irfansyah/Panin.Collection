using Panin.Application.Maintenance;
using Panin.Infrastructure.Maintenance;

namespace Panin.UnitTests;

public class ParameterRulesTests
{
    private static ParameterField F(string name, ParameterKind kind = ParameterKind.Text, bool key = false, bool man = false, int max = 0, string? lookup = null)
        => new(name, name, key, man, lookup, kind, max, true, false);

    private static ParameterTable T(params ParameterField[] fields)
        => new("60", "GEN", "RFPURPOSE", "Tujuan", fields, null);

    [Theory]
    [InlineData("select city_id,city_name from city", true)]
    [InlineData("SELECT regional_id, regional_name FROM RFREGIONAL", true)]
    [InlineData("select su_branch_code, su_branch_code from rfsubranch where active=1 order by su_branch_code", true)]
    [InlineData("select a,b from c; drop table c", false)]
    [InlineData("select a,b from c where x=1 -- komentar", false)]
    [InlineData("exec sp_who", false)]
    [InlineData("select a,b from c where x in (select 1); delete c", false)]
    [InlineData("select a, b from c where 1=1 union select name, 1 from sys.xp_cmdshell", false)]
    [InlineData("update c set a=1", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Lookup_sql_must_be_a_plain_select(string? sql, bool expected)
        => Assert.Equal(expected, ParameterRules.IsSafeLookup(sql));

    [Theory]
    [InlineData("RFPURPOSE", true)]
    [InlineData("RF_PURPOSE2", true)]
    [InlineData("rf purpose", false)]
    [InlineData("x];drop table y;--", false)]
    [InlineData("1abc", false)]
    [InlineData("", false)]
    public void Identifiers_are_strict(string name, bool expected)
        => Assert.Equal(expected, ParameterRules.IsValidIdentifier(name));

    [Theory]
    [InlineData("SCALLUSER")]
    [InlineData("scallgroup")]
    [InlineData("PARAMETERSYSTEM")]
    [InlineData("PARAMETERSYSTEMFIELD")]
    [InlineData("GRPMENUX")]
    [InlineData("AUDITTRAIL")]
    public void Security_and_metadata_tables_are_blocked(string table)
        => Assert.True(ParameterRules.IsBlocked(table));

    [Fact]
    public void Ordinary_parameter_tables_are_not_blocked() => Assert.False(ParameterRules.IsBlocked("RFPURPOSE"));

    [Theory]
    [InlineData("varchar", ParameterKind.Text)]
    [InlineData("nvarchar", ParameterKind.Text)]
    [InlineData("int", ParameterKind.Integer)]
    [InlineData("decimal", ParameterKind.Decimal)]
    [InlineData("bit", ParameterKind.Boolean)]
    [InlineData("datetime", ParameterKind.DateTime)]
    [InlineData("uniqueidentifier", ParameterKind.Unsupported)]
    [InlineData("varbinary", ParameterKind.Unsupported)]
    public void Sql_types_map_to_kinds(string type, ParameterKind kind) => Assert.Equal(kind, ParameterRules.KindOf(type));

    [Fact]
    public void Conversion_uses_invariant_culture_and_the_column_type()
    {
        Assert.True(ParameterRules.TryConvert(F("n", ParameterKind.Integer), "42", out var i));
        Assert.Equal(42L, i);
        Assert.True(ParameterRules.TryConvert(F("d", ParameterKind.Decimal), "1234.50", out var d));
        Assert.Equal(1234.50m, d);
        Assert.True(ParameterRules.TryConvert(F("b", ParameterKind.Boolean), "1", out var b));
        Assert.Equal(true, b);
        Assert.True(ParameterRules.TryConvert(F("t", ParameterKind.DateTime), "2026-10-22 13:05:00", out var t));
        Assert.Equal(new DateTime(2026, 10, 22, 13, 5, 0), t);
        Assert.True(ParameterRules.TryConvert(F("t", ParameterKind.DateTime), "2026/10/22", out _));
        Assert.False(ParameterRules.TryConvert(F("n", ParameterKind.Integer), "4x2", out _));
        Assert.False(ParameterRules.TryConvert(F("d", ParameterKind.Decimal), "1,5,5", out _));
        Assert.False(ParameterRules.TryConvert(F("t", ParameterKind.DateTime), "kemarin", out _));
    }

    [Fact]
    public void Empty_text_converts_to_null() => Assert.True(ParameterRules.TryConvert(F("n", ParameterKind.Integer), "  ", out var v) && v is null);

    [Fact]
    public void Keys_and_mandatory_fields_are_required()
    {
        var table = T(F("P_ID", key: true, max: 5), F("P_DESC", man: true, max: 10), F("NOTE"));
        var errors = ParameterRules.Validate(table, new Dictionary<string, string?>());
        Assert.Equal(2, errors.Count);
        Assert.Empty(ParameterRules.Validate(table, new Dictionary<string, string?> { ["P_ID"] = "01", ["P_DESC"] = "x" }));
    }

    [Fact]
    public void Length_and_format_are_validated()
    {
        var table = T(F("P_ID", key: true, max: 2), F("QTY", ParameterKind.Integer));
        var errors = ParameterRules.Validate(table, new Dictionary<string, string?> { ["P_ID"] = "123", ["QTY"] = "abc" });
        Assert.Equal(2, errors.Count);
    }

    [Fact]
    public void Boolean_fields_are_never_required_to_be_filled()
    {
        var table = T(F("P_ID", key: true), F("ACTIVE", ParameterKind.Boolean, man: true));
        Assert.Empty(ParameterRules.Validate(table, new Dictionary<string, string?> { ["P_ID"] = "1" }));
    }

    [Fact]
    public void Read_only_tables_reject_all_changes()
    {
        var table = new ParameterTable("60", "GEN", "RFSEX", "Jenis Kelamin", [F("SEXID")], "Tanpa kunci");
        Assert.Equal(["Tanpa kunci"], ParameterRules.Validate(table, new Dictionary<string, string?>()));
    }

    [Fact]
    public void Text_formats_match_what_the_old_screens_stored()
    {
        Assert.Equal("1", ParameterRules.ToText(ParameterKind.Boolean, true));
        Assert.Equal("0", ParameterRules.ToText(ParameterKind.Boolean, false));
        Assert.Equal("2026-10-22 13:05:00", ParameterRules.ToText(ParameterKind.DateTime, new DateTime(2026, 10, 22, 13, 5, 0)));
        Assert.Equal("1234.5", ParameterRules.ToText(ParameterKind.Decimal, 1234.5m));
        Assert.Null(ParameterRules.ToText(ParameterKind.Text, DBNull.Value));
    }

    [Fact]
    public void Set_item_links_are_parsed()
    {
        var item = new ParameterSetItem("Tujuan", "ParamMaker.aspx?TBLNM=RFPURPOSE&Moduleid=60");
        Assert.Equal("parammaker.aspx", item.File);
        Assert.Equal("RFPURPOSE", item.Table);
        Assert.Equal("60", item.ModuleId);
        var group = new ParameterSetItem("Group", "~/Parameter/GroupMaker.aspx?TBLNM=SCALLGROUP&moduleid=99");
        Assert.Equal("groupmaker.aspx", group.File);
        Assert.Equal("99", group.ModuleId);
        Assert.Null(new ParameterSetItem("x", "Halaman.aspx").Table);
    }
}

public class ParameterSqlTests
{
    private static ParameterField F(string name, ParameterKind kind = ParameterKind.Text, bool key = false)
        => new(name, name, key, false, null, kind, 0, true, false);

    private static readonly ParameterField Id = F("P_ID", key: true);
    private static readonly ParameterField Desc = F("P_DESC");
    private static readonly ParameterField Active = F("ACTIVE", ParameterKind.Boolean);

    [Fact]
    public void Upsert_updates_then_inserts_and_audits_in_one_transaction_with_typed_parameters()
    {
        var table = new ParameterTable("60", "GEN", "RFPURPOSE", "Tujuan", [Id, Desc, Active], null);
        var values = new Dictionary<string, object?> { ["P_ID"] = "07", ["P_DESC"] = "Uji'; DROP TABLE x;--", ["ACTIVE"] = true };
        var tempId = Guid.NewGuid();

        var (sql, args) = ParameterService.BuildUpsert(table, [Id], values, tempId, "APPROVER");

        Assert.StartsWith("SET XACT_ABORT ON; BEGIN TRAN; UPDATE [RFPURPOSE] SET [P_DESC] = @2, [ACTIVE] = @3 WHERE [P_ID] = @1;", sql, StringComparison.Ordinal);
        Assert.Contains("IF @@ROWCOUNT = 0 INSERT INTO [RFPURPOSE] ([P_ID], [P_DESC], [ACTIVE]) VALUES (", sql, StringComparison.Ordinal);
        Assert.EndsWith("COMMIT TRAN", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DROP", sql, StringComparison.Ordinal);
        Assert.Contains("Uji'; DROP TABLE x;--", args);
        Assert.Contains(tempId, args);
        Assert.Contains("APPROVER", args);
        Assert.Contains(true, args);
    }

    [Fact]
    public void Upsert_of_a_key_only_table_inserts_only_when_missing()
    {
        var a = F("GROUPID", key: true);
        var b = F("TREATMENT", key: true);
        var table = new ParameterTable("60", "USER", "GRPACCESSTREATMENT", "Group Treatment", [a, b], null);
        var (sql, _) = ParameterService.BuildUpsert(table, [a, b], new Dictionary<string, object?> { ["GROUPID"] = "G", ["TREATMENT"] = "00001" }, Guid.NewGuid(), "X");
        Assert.DoesNotContain("UPDATE", sql, StringComparison.Ordinal);
        Assert.Contains("IF NOT EXISTS (SELECT 1 FROM [GRPACCESSTREATMENT] WHERE [GROUPID] = @1 AND [TREATMENT] = @2) INSERT INTO", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Delete_deactivates_when_the_table_has_an_active_column()
    {
        var table = new ParameterTable("60", "GEN", "RFPURPOSE", "Tujuan", [Id, Desc, Active], null);
        var (sql, args) = ParameterService.BuildDelete(table, [Id], new Dictionary<string, object?> { ["P_ID"] = "07" }, Guid.NewGuid(), "X");
        Assert.Contains("UPDATE [RFPURPOSE] SET [ACTIVE] = @2 WHERE [P_ID] = @1", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DELETE [RFPURPOSE]", sql, StringComparison.Ordinal);
        Assert.Contains(false, args);
    }

    [Fact]
    public void Delete_removes_the_row_when_there_is_no_active_column()
    {
        var table = new ParameterTable("60", "GEN", "RFSEX", "Jenis Kelamin", [Id, Desc], null);
        var (sql, _) = ParameterService.BuildDelete(table, [Id], new Dictionary<string, object?> { ["P_ID"] = "07" }, Guid.NewGuid(), "X");
        Assert.Contains("DELETE [RFSEX] WHERE [P_ID] = @1", sql, StringComparison.Ordinal);
        Assert.Contains("RAISERROR", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Identifiers_are_bracket_quoted_and_closing_brackets_doubled()
        => Assert.Equal("[A]]B]", ParameterService.Q("A]B"));
}
