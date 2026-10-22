using Panin.Application.Maintenance;

namespace Panin.UnitTests;

public class GroupRulesTests
{
    [Fact]
    public void Complete_draft_is_valid() => Assert.Empty(GroupRules.Validate(new GroupDraft { GroupId = "ZZ.GRP-1", Name = "Grup Uji" }));

    [Fact]
    public void Id_and_name_are_required()
    {
        var errors = GroupRules.Validate(new GroupDraft());
        Assert.Contains(errors, e => e.StartsWith("Group ID", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.StartsWith("Nama", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("a b")]
    [InlineData("x';--")]
    [InlineData("123456789012345678901")]
    public void Unsafe_or_long_ids_are_rejected(string id)
        => Assert.Single(GroupRules.Validate(new GroupDraft { GroupId = id, Name = "N" }));

    [Fact]
    public void Long_names_are_rejected()
        => Assert.Single(GroupRules.Validate(new GroupDraft { GroupId = "G", Name = new string('n', GroupRules.MaxName + 1) }));

    private static readonly MenuAccessItem[] Menus =
    [
        new("10", "Collection", null, 1, false),
        new("1010", "Distribusi", "10", 1, false),
        new("101010", "Detail", "1010", 1, false),
        new("20", "Inquiry", null, 2, false),
        new("2010", "Status", "20", 1, false),
    ];

    [Fact]
    public void Selecting_a_child_brings_all_its_ancestors()
    {
        var result = GroupRules.WithAncestors(["101010"], Menus);
        Assert.Equal(["10", "1010", "101010"], result.OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public void Unknown_menu_ids_are_dropped()
    {
        var result = GroupRules.WithAncestors(["999", "2010"], Menus);
        Assert.Equal(["20", "2010"], result.OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public void Empty_selection_removes_module_access()
        => Assert.Empty(GroupRules.WithAncestors([], Menus));

    [Fact]
    public void Cycles_do_not_hang()
    {
        MenuAccessItem[] cyclic = [new("1", "A", "2", 1, false), new("2", "B", "1", 1, false)];
        var result = GroupRules.WithAncestors(["1"], cyclic);
        Assert.Equal(2, result.Count);
    }
}
