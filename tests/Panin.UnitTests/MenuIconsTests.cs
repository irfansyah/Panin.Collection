using Panin.Web.Shell;

namespace Panin.UnitTests;

public class MenuIconsTests
{
    [Theory]
    [InlineData("Distribusi Debitur", "arrows-right-left")]
    [InlineData("Monitoring Debitur", "eye")]
    [InlineData("Monitoring Aktifitas Collection", "pulse")]
    [InlineData("Bulk Surat", "inbox")]
    [InlineData("Bulk Surat (New)", "inbox")]
    [InlineData("Inquiry Status Debitur", "search")]
    [InlineData("Add News", "clipboard-list")]
    [InlineData("News View", "book-open")]
    [InlineData("Change Password", "key")]
    [InlineData("Daily Tracking PRK", "building")]
    [InlineData("Daily Tracking", "trending-up")]
    [InlineData("Was Is Reprot", "scale")]
    [InlineData("Report Khusus KPM", "clipboard-list")]
    [InlineData("User & Group Parameter", "users")]
    [InlineData("Approval", "check-circle")]
    [InlineData("Maker", "clipboard-list")]
    [InlineData("Collection Parameter", "cog")]
    public void MapsKnownMenuNames(string text, string icon) => Assert.Equal(icon, MenuIcons.For(text));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Sesuatu yang tidak dikenal")]
    public void UnknownOrEmptyUsesDefault(string? text) => Assert.Equal(MenuIcons.Default, MenuIcons.For(text));

    [Fact]
    public void EveryMappedIconExistsInTheIconComponent()
    {
        var source = File.ReadAllText(Path.Combine(FindRoot(), "src", "Panin.Web", "Components", "Shell", "Icon.razor"));
        foreach (var name in new[] { "home", "arrows-right-left", "eye", "pulse", "inbox", "search", "clipboard-list", "book-open", "key", "building", "trending-up", "chart", "clock", "scale", "document", "check-circle", "users", "rules", "cog", "bell", "logout", "queue" })
        {
            Assert.Contains($"[\"{name}\"]", source, StringComparison.Ordinal);
        }
    }

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "PaninCollection.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Akar repo tidak ditemukan.");
    }
}
