using System.Data;
using Panin.Application.Collection;
using Panin.Infrastructure.Collection;

namespace Panin.UnitTests;

public class ActivityTests
{
    private static readonly DateTime D = new(2026, 10, 1);

    [Fact]
    public void Valid_range_has_no_errors() => Assert.Empty(ActivityRules.Validate(D, D.AddDays(ActivityRules.MaxDays)));

    [Fact]
    public void Missing_dates_are_reported()
        => Assert.Equal(2, ActivityRules.Validate(null, null).Count);

    [Fact]
    public void Reversed_range_is_rejected()
        => Assert.Single(ActivityRules.Validate(D, D.AddDays(-1)));

    [Fact]
    public void Range_over_31_days_is_rejected()
        => Assert.Contains(ActivityRules.Validate(D, D.AddDays(ActivityRules.MaxDays + 1)), e => e.Contains("31", StringComparison.Ordinal));

    [Fact]
    public void Map_drops_html_link_columns_and_formats_values()
    {
        var t = new DataTable();
        t.Columns.Add("Nama Debitur");
        t.Columns.Add("Tgl", typeof(DateTime));
        t.Columns.Add("Catatan");
        t.Columns.Add("Lihat");
        t.Rows.Add("BUDI", new DateTime(2026, 10, 5), "a &lt; b", "<a href=#>Lihat</a>");
        t.Rows.Add(DBNull.Value, new DateTime(2026, 10, 5, 14, 30, 0), DBNull.Value, "x");

        var r = ActivityMonitoringService.Map(t);

        Assert.Equal(["Nama Debitur", "Tgl", "Catatan"], r.Columns);
        Assert.Equal("BUDI", r.Rows[0][0]);
        Assert.Equal("05-Okt-2026", r.Rows[0][1]);
        Assert.Equal("a < b", r.Rows[0][2]);
        Assert.Null(r.Rows[1][0]);
        Assert.Equal("05-Okt-2026 14.30", r.Rows[1][1]);
        Assert.False(r.Truncated);
    }

    [Fact]
    public void Map_truncates_large_results()
    {
        var t = new DataTable();
        t.Columns.Add("X");
        for (var i = 0; i < ActivityRules.MaxRows + 5; i++)
        {
            t.Rows.Add(i.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        var r = ActivityMonitoringService.Map(t);
        Assert.Equal(ActivityRules.MaxRows, r.Rows.Count);
        Assert.True(r.Truncated);
    }
}
