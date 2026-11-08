using Headroom.Client.Api;
using Headroom.Client.TimeOff;

namespace Headroom.Tests.Schedule;

public class TimeOffCalendarTests
{
    private static ScheduleResponse Data(params SchedulePerson[] people) =>
        new("Schedule", new(2026, 1, 1), new(2026, 12, 31), [], people);

    private static SchedulePerson Person(string name, params ScheduleTimeOff[] off) =>
        new("Person", Guid.NewGuid(), name, "active", [], [], off);

    private static ScheduleTimeOff Off(DateOnly start, string startPart, DateOnly end, string endPart) =>
        new(Guid.NewGuid(), start, startPart, end, endPart);

    private static CalendarCell Cell(IReadOnlyList<IReadOnlyList<CalendarCell>> weeks, DateOnly date) =>
        weeks.SelectMany(w => w).Single(c => c.Date == date);

    [Fact]
    public void RangeOfAMonthThatStartsOnAMondayEndsOnTheSundayAfterTheLastDay()
    {
        // June 2026 starts on a Monday and ends on a Tuesday.
        var (from, to) = TimeOffCalendar.Range(new(2026, 6, 1));

        Assert.Equal(new DateOnly(2026, 6, 1), from);
        Assert.Equal(new DateOnly(2026, 7, 5), to);
    }

    [Fact]
    public void RangeOfAMonthThatStartsOnASundayStartsOnTheMondayBefore()
    {
        // November 2026 starts on a Sunday and ends on a Monday.
        var (from, to) = TimeOffCalendar.Range(new(2026, 11, 15));

        Assert.Equal(new DateOnly(2026, 10, 26), from);
        Assert.Equal(new DateOnly(2026, 12, 6), to);
    }

    [Fact]
    public void WeeksHaveSevenCellsAndMarkDaysOutsideTheMonthAndWeekends()
    {
        var weeks = TimeOffCalendar.Weeks(new(2026, 11, 1), Data());

        Assert.Equal(6, weeks.Count);
        Assert.All(weeks, w => Assert.Equal(7, w.Count));
        Assert.Equal(new DateOnly(2026, 10, 26), weeks[0][0].Date);
        Assert.False(Cell(weeks, new(2026, 10, 31)).InMonth);
        Assert.True(Cell(weeks, new(2026, 11, 1)).InMonth);
        Assert.True(Cell(weeks, new(2026, 11, 30)).InMonth);
        Assert.False(Cell(weeks, new(2026, 12, 1)).InMonth);
        Assert.True(Cell(weeks, new(2026, 11, 7)).Weekend);
        Assert.True(Cell(weeks, new(2026, 11, 8)).Weekend);
        Assert.False(Cell(weeks, new(2026, 11, 9)).Weekend);
    }

    [Fact]
    public void TimeOffOverAWeekendListsThePersonOnSaturdayAndSunday()
    {
        var data = Data(Person("Ann", Off(new(2026, 11, 6), "morning", new(2026, 11, 9), "afternoon")));

        var weeks = TimeOffCalendar.Weeks(new(2026, 11, 1), data);

        Assert.Equal(["Ann"], Cell(weeks, new(2026, 11, 7)).Entries.Select(e => e.Name));
        Assert.Equal(["Ann"], Cell(weeks, new(2026, 11, 8)).Entries.Select(e => e.Name));
        Assert.Empty(Cell(weeks, new(2026, 11, 10)).Entries);
    }

    [Fact]
    public void DaysOutsideTheMonthStillListTheirTimeOff()
    {
        var data = Data(Person("Ann", Off(new(2026, 10, 30), "morning", new(2026, 10, 30), "afternoon")));

        var cell = Cell(TimeOffCalendar.Weeks(new(2026, 11, 1), data), new(2026, 10, 30));

        Assert.False(cell.InMonth);
        Assert.Equal(["Ann"], cell.Entries.Select(e => e.Name));
    }

    [Fact]
    public void TheFirstDayMayStartInTheAfternoonAndTheLastDayMayEndInTheMorning()
    {
        var data = Data(Person("Ann", Off(new(2026, 11, 3), "afternoon", new(2026, 11, 5), "morning")));

        var weeks = TimeOffCalendar.Weeks(new(2026, 11, 1), data);

        Assert.Equal("afternoon", Cell(weeks, new(2026, 11, 3)).Entries.Single().Part);
        Assert.Null(Cell(weeks, new(2026, 11, 4)).Entries.Single().Part);
        Assert.Equal("morning", Cell(weeks, new(2026, 11, 5)).Entries.Single().Part);
    }

    [Theory]
    [InlineData("morning", "morning", "morning")]
    [InlineData("afternoon", "afternoon", "afternoon")]
    [InlineData("morning", "afternoon", null)]
    public void AOneDayTimeOffShowsItsPart(string startPart, string endPart, string? expected)
    {
        var data = Data(Person("Ann", Off(new(2026, 11, 3), startPart, new(2026, 11, 3), endPart)));

        var entry = Cell(TimeOffCalendar.Weeks(new(2026, 11, 1), data), new(2026, 11, 3)).Entries.Single();

        Assert.Equal(expected, entry.Part);
    }

    [Fact]
    public void ManyPeopleOnOneDayAllAppearSortedByName()
    {
        var day = new DateOnly(2026, 11, 3);
        var data = Data(
            Person("Carl", Off(day, "morning", day, "afternoon")),
            Person("Ann", Off(day, "morning", day, "afternoon")),
            Person("Bob", Off(day, "morning", day, "afternoon")));

        var cell = Cell(TimeOffCalendar.Weeks(new(2026, 11, 1), data), day);

        Assert.Equal(["Ann", "Bob", "Carl"], cell.Entries.Select(e => e.Name));
    }
}
