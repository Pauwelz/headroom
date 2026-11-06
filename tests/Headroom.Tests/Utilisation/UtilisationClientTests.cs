using Headroom.Client.Api;
using Headroom.Client.Utilisation;

namespace Headroom.Tests.Utilisation;

public class UtilisationClientTests
{
    private static UtilisationCell Cell(decimal? utilisation, decimal tentative = 0, decimal capacity = 40) =>
        new(capacity, 0, 0, 0, capacity, 0, tentative, 0, 0, utilisation, []);

    [Theory]
    [InlineData(null, "none")]
    [InlineData(0.74, "low")]
    [InlineData(0.75, "ideal")]
    [InlineData(0.90, "ideal")]
    [InlineData(0.91, "high")]
    [InlineData(1.00, "high")]
    [InlineData(1.01, "over")]
    public void Bands_are_fixed(double? utilisation, string band) =>
        Assert.Equal(band, UtilisationBand.Of(Cell(utilisation is { } u ? (decimal)u : null)));

    [Fact]
    public void No_Capacity_shows_a_dash_and_Tentative_shows_its_share()
    {
        Assert.Equal("—", UtilisationBand.Percent(null));
        Assert.Equal("83%", UtilisationBand.Percent(50m / 60m));
        Assert.Equal("incl. 25% T", UtilisationBand.TentativeNote(Cell(0.5m, tentative: 10)));
        Assert.Null(UtilisationBand.TentativeNote(Cell(0.5m)));
    }

    [Fact]
    public void Free_is_Capacity_minus_all_allocated_hours_and_negative_when_over_allocated()
    {
        var cell = Cell(1.1m, capacity: 40) with { Confirmed = 30, Tentative = 10, Archived = 4 };

        Assert.Equal(44m, UtilisationBand.Allocated(cell));
        Assert.Equal(-4m, UtilisationBand.Free(cell));
    }

    [Fact]
    public void The_range_is_the_current_quarter_plus_the_next_and_pages_by_one_quarter()
    {
        var range = UtilisationRange.Default(new(2026, 11, 15));

        Assert.Equal(new DateOnly(2026, 10, 1), range.From);
        Assert.Equal(new DateOnly(2027, 3, 31), range.To);
        Assert.Equal("Q4 2026 + Q1 2027", range.Label);
        Assert.Equal(new DateOnly(2026, 7, 1), range.Page(-1).From);
        Assert.Equal(new DateOnly(2027, 1, 1), range.Page(1).From);
    }
}
