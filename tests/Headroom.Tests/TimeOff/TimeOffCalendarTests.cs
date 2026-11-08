using Headroom.Client.Api;
using Headroom.Client.TimeOff;

namespace Headroom.Tests.TimeOff;

public class TimeOffCalendarTests
{
    private static ScheduleResponse Data(params SchedulePerson[] people) =>
        new("Schedule", new(2026, 1, 1), new(2026, 12, 31), [], people);

    private static SchedulePerson Person(string name, params ScheduleTimeOff[] off) =>
        new("Person", Guid.NewGuid(), name, "active", [], [], off);

    private static ScheduleResponse WithDaysOff(ScheduleResponse data, params ScheduleDayOff[] daysOff) =>
        data with { DaysOff = daysOff };

    private static ScheduleTimeOff Off(DateOnly start, string startPart, DateOnly end, string endPart) =>
        new(Guid.NewGuid(), start, startPart, end, endPart);

    private static CalendarCell Cell(IReadOnlyList<CalendarWeek> weeks, DateOnly date) =>
        weeks.SelectMany(w => w.Cells).Single(c => c.Date == date);

    private static IReadOnlyList<CalendarBar> Bars(IReadOnlyList<CalendarWeek> weeks, int week) => weeks[week].Bars;

    [Fact]
    public void RangeOfAMonthThatStartsOnAMondayEndsOnTheSundayAfterTheLastDay()
    {
        // June 2026 starts on a Monday and ends on a Tuesday.
        var (from, to) = TimeOffCalendar.VisibleRange(new(2026, 6, 1));

        Assert.Equal(new DateOnly(2026, 6, 1), from);
        Assert.Equal(new DateOnly(2026, 7, 5), to);
    }

    [Fact]
    public void RangeOfAMonthThatStartsOnASundayStartsOnTheMondayBefore()
    {
        // November 2026 starts on a Sunday and ends on a Monday.
        var (from, to) = TimeOffCalendar.VisibleRange(new(2026, 11, 15));

        Assert.Equal(new DateOnly(2026, 10, 26), from);
        Assert.Equal(new DateOnly(2026, 12, 6), to);
    }

    [Fact]
    public void WeeksHaveSevenCellsAndMarkDaysOutsideTheMonthAndWeekends()
    {
        var weeks = TimeOffCalendar.Weeks(new(2026, 11, 1), Data());

        Assert.Equal(6, weeks.Count);
        Assert.All(weeks, w => Assert.Equal(7, w.Cells.Count));
        Assert.Equal(new DateOnly(2026, 10, 26), weeks[0].Cells[0].Date);
        Assert.False(Cell(weeks, new(2026, 10, 31)).InMonth);
        Assert.True(Cell(weeks, new(2026, 11, 1)).InMonth);
        Assert.True(Cell(weeks, new(2026, 11, 30)).InMonth);
        Assert.False(Cell(weeks, new(2026, 12, 1)).InMonth);
        Assert.True(Cell(weeks, new(2026, 11, 7)).Weekend);
        Assert.True(Cell(weeks, new(2026, 11, 8)).Weekend);
        Assert.False(Cell(weeks, new(2026, 11, 9)).Weekend);
    }

    [Fact]
    public void ABarWithinOneWeekHasItsColumnsAndNoFlags()
    {
        // Tue 3 to Thu 5 November 2026 is in week 1 (Mon 2 Nov).
        var data = Data(Person("Ann", Off(new(2026, 11, 3), "morning", new(2026, 11, 5), "afternoon")));

        var weeks = TimeOffCalendar.Weeks(new(2026, 11, 1), data);

        Assert.Empty(Bars(weeks, 0));
        Assert.Equal([new CalendarBar("Ann", 1, 3, false, false, false, false, 0)], Bars(weeks, 1));
    }

    [Fact]
    public void ABarAcrossSundayIsSplitInTwoWithContinueFlags()
    {
        // Fri 6 to Tue 10 November 2026.
        var data = Data(Person("Ann", Off(new(2026, 11, 6), "morning", new(2026, 11, 10), "afternoon")));

        var weeks = TimeOffCalendar.Weeks(new(2026, 11, 1), data);

        Assert.Equal([new CalendarBar("Ann", 4, 6, false, false, false, true, 0)], Bars(weeks, 1));
        Assert.Equal([new CalendarBar("Ann", 0, 1, false, false, true, false, 0)], Bars(weeks, 2));
    }

    [Fact]
    public void AnAfternoonStartAndAMorningEndMakeHalfBars()
    {
        var data = Data(Person("Ann", Off(new(2026, 11, 3), "afternoon", new(2026, 11, 5), "morning")));

        var bar = Bars(TimeOffCalendar.Weeks(new(2026, 11, 1), data), 1).Single();

        Assert.True(bar.StartsHalf);
        Assert.True(bar.EndsHalf);
    }

    [Fact]
    public void ATimeOffThatContinuesDoesNotHalveTheSplitEdge()
    {
        // Starts Friday afternoon, ends Monday morning: halves only at the real start and end.
        var data = Data(Person("Ann", Off(new(2026, 11, 6), "afternoon", new(2026, 11, 9), "morning")));

        var weeks = TimeOffCalendar.Weeks(new(2026, 11, 1), data);

        Assert.Equal([new CalendarBar("Ann", 4, 6, true, false, false, true, 0)], Bars(weeks, 1));
        Assert.Equal([new CalendarBar("Ann", 0, 0, false, true, true, false, 0)], Bars(weeks, 2));
    }

    [Theory]
    [InlineData("morning", "morning", false, true)]
    [InlineData("afternoon", "afternoon", true, false)]
    [InlineData("morning", "afternoon", false, false)]
    public void AOneDayTimeOffFillsTheMatchingPartOfTheCell(string startPart, string endPart, bool startsHalf, bool endsHalf)
    {
        var data = Data(Person("Ann", Off(new(2026, 11, 3), startPart, new(2026, 11, 3), endPart)));

        var bar = Bars(TimeOffCalendar.Weeks(new(2026, 11, 1), data), 1).Single();

        Assert.Equal((1, 1, startsHalf, endsHalf), (bar.FirstColumn, bar.LastColumn, bar.StartsHalf, bar.EndsHalf));
    }

    [Fact]
    public void OverlappingBarsTakeSeparateLanesAndALaneIsReusedAfterAGap()
    {
        var data = Data(
            Person("Ann", Off(new(2026, 11, 2), "morning", new(2026, 11, 4), "afternoon")),
            Person("Bob", Off(new(2026, 11, 3), "morning", new(2026, 11, 3), "afternoon")),
            Person("Carl", Off(new(2026, 11, 5), "morning", new(2026, 11, 5), "afternoon")));

        var bars = Bars(TimeOffCalendar.Weeks(new(2026, 11, 1), data), 1);

        Assert.Equal([("Ann", 0), ("Bob", 1), ("Carl", 0)], bars.Select(b => (b.Name, b.Lane)));
    }

    [Fact]
    public void BarsInTheSameColumnAreSortedByNameIgnoringCase()
    {
        var off = Off(new(2026, 11, 3), "morning", new(2026, 11, 3), "afternoon");
        var data = Data(Person("bob", off), Person("Carl", off), Person("Ann", off));

        var bars = Bars(TimeOffCalendar.Weeks(new(2026, 11, 1), data), 1);

        Assert.Equal([("Ann", 0), ("bob", 1), ("Carl", 2)], bars.Select(b => (b.Name, b.Lane)));
    }

    [Fact]
    public void BarsCoverWeekendsAndDaysOutsideTheMonth()
    {
        var data = Data(Person("Ann", Off(new(2026, 10, 30), "morning", new(2026, 11, 1), "afternoon")));

        var bar = Bars(TimeOffCalendar.Weeks(new(2026, 11, 1), data), 0).Single();

        Assert.Equal((4, 6), (bar.FirstColumn, bar.LastColumn));
    }

    [Fact]
    public void APublicHolidayAndACompanyDayOffGiveTheirTypeAndName()
    {
        var holiday = new DateOnly(2026, 4, 6);
        var company = new DateOnly(2026, 5, 15);
        var data = WithDaysOff(
            Data(Person("Ann", Off(holiday, "morning", company, "afternoon"))),
            new(DayOffTypeName.PublicHoliday, holiday, "Easter Monday"),
            new(DayOffTypeName.CompanyDayOff, company, "Bridge day"));

        var weeks = TimeOffCalendar.Weeks(new(2026, 4, 1), data);
        var holidayCell = Cell(weeks, holiday);
        var companyCell = Cell(TimeOffCalendar.Weeks(new(2026, 5, 1), data), company);

        Assert.Equal(new ScheduleDayOff(DayOffTypeName.PublicHoliday, holiday, "Easter Monday"), holidayCell.DayOff);
        Assert.Equal(new ScheduleDayOff(DayOffTypeName.CompanyDayOff, company, "Bridge day"), companyCell.DayOff);
        Assert.Null(Cell(weeks, new(2026, 4, 7)).DayOff);
    }
}
