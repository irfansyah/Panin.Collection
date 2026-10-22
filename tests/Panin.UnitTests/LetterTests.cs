using Panin.Application.Collection;

namespace Panin.UnitTests;

public class LetterRulesTests
{
    [Fact]
    public void Complete_entry_is_valid() => Assert.Empty(LetterRules.Validate(new LetterEntry { AccountNo = "1", TypeCode = "PA-PEM-01", Notes = "n" }));

    [Fact]
    public void Type_and_notes_are_required()
    {
        var errors = LetterRules.Validate(new LetterEntry { AccountNo = "1" });
        Assert.Contains(errors, e => e.StartsWith("Jenis Surat", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.StartsWith("Catatan", StringComparison.Ordinal));
    }

    [Fact]
    public void Long_notes_are_rejected()
        => Assert.Single(LetterRules.Validate(new LetterEntry { AccountNo = "1", TypeCode = "X", Notes = new string('a', LetterRules.MaxNotesLength + 1) }));
}
