using Panin.Application.Maintenance;
using Panin.Web.Components.Dashboard;

namespace Panin.UnitTests;

public class ChartMathTests
{
    [Theory]
    [InlineData(0, 10, 1)]
    [InlineData(5, 0, 1)]
    [InlineData(10, 10, 100)]
    [InlineData(5, 10, 50)]
    [InlineData(0.1, 100, 4)] // kolom berisi tetap terlihat
    public void ColumnPercent(double total, double max, double expected) => Assert.Equal(expected, ChartMath.ColumnPercent(total, max), 6);

    [Fact]
    public void StackPercentsSumToHundred()
    {
        var p = ChartMath.StackPercents([1, 1, 2]);
        Assert.Equal([25d, 25d, 50d], p);
        Assert.Equal(100, p.Sum(), 6);
    }

    [Fact]
    public void StackPercentsOfEmptyColumnAreZero() => Assert.All(ChartMath.StackPercents([0, 0]), v => Assert.Equal(0, v));

    [Theory]
    [InlineData(25, 100, 25)]
    [InlineData(150, 100, 100)]
    [InlineData(-5, 100, 0)]
    [InlineData(5, 0, 0)]
    public void PercentIsClamped(double part, double whole, double expected) => Assert.Equal(expected, ChartMath.Percent(part, whole));

    [Fact]
    public void CssNumbersUseInvariantDecimalPoint() => Assert.Equal("12.35", ChartMath.Css(12.345));

    [Fact]
    public void LineCoordinatesSpanTheAreaAndFlipY()
    {
        var c = ChartMath.LineCoordinates([1, 2, 3], 100, 50, 10);
        Assert.Equal(3, c.Count);
        Assert.Equal(10, c[0].X, 6);
        Assert.Equal(90, c[2].X, 6);
        Assert.True(c[0].Y > c[2].Y); // nilai terbesar di atas (y kecil)
        Assert.Equal(40, c[0].Y, 6);
        Assert.Equal(10, c[2].Y, 6);
    }

    [Fact]
    public void LineCoordinatesHandleFlatAndSingleSeries()
    {
        var flat = ChartMath.LineCoordinates([5, 5, 5], 100, 50, 10);
        Assert.All(flat, p => Assert.Equal(flat[0].Y, p.Y));
        var single = ChartMath.LineCoordinates([7], 100, 50, 10);
        Assert.Equal(50, single[0].X);
        Assert.Empty(ChartMath.LineCoordinates([], 100, 50, 10));
    }
}

public class MaintenanceDashboardModelTests
{
    [Theory]
    [InlineData("SCALLUSER", ChangeKind.User)]
    [InlineData(" scalluser ", ChangeKind.User)]
    [InlineData("SCALLGROUP", ChangeKind.Group)]
    [InlineData("RFPURPOSE", ChangeKind.Parameter)]
    [InlineData(null, ChangeKind.Parameter)]
    public void ClassifiesTables(string? table, ChangeKind expected) => Assert.Equal(expected, ChangeKinds.Classify(table));

    [Fact]
    public void AgeIsHumanReadable()
    {
        var now = new DateTime(2026, 10, 23, 12, 0, 0);
        Assert.Equal("baru saja", ChangeKinds.Age(now.AddSeconds(-20), now));
        Assert.Equal("5 menit", ChangeKinds.Age(now.AddMinutes(-5), now));
        Assert.Equal("3 jam", ChangeKinds.Age(now.AddHours(-3), now));
        Assert.Equal("2 hari", ChangeKinds.Age(now.AddDays(-2).AddHours(-1), now));
        Assert.Equal("baru saja", ChangeKinds.Age(now.AddHours(1), now)); // jam mesin berbeda: tidak negatif
        Assert.Equal("-", ChangeKinds.Age(null, now));
    }

    [Fact]
    public void RangeEndsToday()
    {
        var today = new DateOnly(2026, 10, 23);
        var range = DashboardDays.Range(today, 14);
        Assert.Equal(14, range.Count);
        Assert.Equal(today, range[^1]);
        Assert.Equal(new DateOnly(2026, 10, 10), range[0]);
    }

    [Fact]
    public void FillChangesZeroFillsAndSplitsByKind()
    {
        var today = new DateOnly(2026, 10, 23);
        var filled = DashboardDays.FillChanges(today, 3,
        [
            (today, "SCALLUSER", 2),
            (today, "SCALLGROUP", 1),
            (today, "RFPURPOSE", 4),
            (today, "RFKOLEK", 1),
            (today.AddDays(-2), "SCALLUSER", 7),
        ]);

        Assert.Equal(3, filled.Count);
        Assert.Equal(new DailyChanges(today.AddDays(-2), 7, 0, 0), filled[0]);
        Assert.Equal(0, filled[1].Total);
        Assert.Equal(new DailyChanges(today, 2, 1, 5), filled[2]);
        Assert.Equal(8, filled[2].Total);
    }

    [Fact]
    public void FillLoginsZeroFillsMissingDays()
    {
        var today = new DateOnly(2026, 10, 23);
        var filled = DashboardDays.FillLogins(today, 3, [(today, 10), (today, 5)]);
        Assert.Equal([0, 0, 15], filled.Select(d => d.Logins));
    }
}
