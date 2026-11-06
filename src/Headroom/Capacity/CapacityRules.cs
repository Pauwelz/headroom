namespace Headroom.Capacity;

/// <summary>Contract hours for Monday to Sunday. Valid from <paramref name="Start"/> until the next Contract.</summary>
public sealed record ContractTerm(DateOnly Start, IReadOnlyDictionary<DayOfWeek, decimal> Hours);

public enum DayPart { Morning, Afternoon }

/// <summary>Everything that sets the hours of one Person: Contracts, public holidays, Company days off and Time off.</summary>
public sealed record PersonRecords(
    IReadOnlyList<ContractTerm> Contracts, IPublicHolidays Holidays, IReadOnlySet<DateOnly> CompanyDaysOff, IReadOnlyList<TimeOffTerm> TimeOff);

/// <summary>
/// One Person on one day. Capacity = ContractHours - PublicHolidayHours - CompanyDayOffHours - TimeOffHours.
/// A half day of Time off halves Capacity. A full day off sets Capacity to 0.
/// </summary>
public sealed record PersonDay(
    decimal ContractHours, decimal PublicHolidayHours, decimal CompanyDayOffHours, decimal TimeOffHours, decimal Capacity, bool IsFullDayOff)
{
    /// <summary>Hours of one Allocation on this day. A half day of Time off keeps the full hours.</summary>
    public decimal AllocatedHours(int percentage) => IsFullDayOff ? 0 : percentage * ContractHours / 100m;
}

/// <summary>Time off. Start "Afternoon" means the Person works the morning of the first day.
/// End "Morning" means the Person works the afternoon of the last day. Both days are inclusive.</summary>
public sealed record TimeOffTerm(DateOnly StartDate, DayPart StartPart, DateOnly EndDate, DayPart EndPart);

/// <summary>Capacity and Allocation-hours rules. Plain inputs, no database.</summary>
public static class CapacityRules
{
    /// <summary>Contract hours of the Contract valid on the day. 0 before the first Contract.</summary>
    public static decimal ContractHours(IEnumerable<ContractTerm> contracts, DateOnly day)
    {
        var valid = contracts.Where(c => c.Start <= day).MaxBy(c => c.Start);
        return valid is not null && valid.Hours.TryGetValue(day.DayOfWeek, out var hours) ? hours : 0;
    }

    /// <summary>Hours and Capacity of one Person on one day. Both the Schedule and the Utilisation report use it.</summary>
    public static PersonDay Day(DateOnly day, PersonRecords records)
    {
        var contract = ContractHours(records.Contracts, day);
        var removed = RemovedFraction(day, records.TimeOff);
        var holiday = records.Holidays.IsPublicHoliday(day);
        var company = records.CompanyDaysOff.Contains(day);
        var fullDayOff = holiday || company || removed >= 1m;
        var capacity = fullDayOff ? 0 : contract * (1 - removed);
        // A day with more than one reason is one day off: holiday first, then Company day, then Time off.
        var holidayHours = holiday ? contract : 0;
        var companyHours = !holiday && company ? contract : 0;
        return new PersonDay(contract, holidayHours, companyHours, contract - capacity - holidayHours - companyHours, capacity, fullDayOff);
    }

    /// <summary>Over-allocation: all Allocations of a Person on a day add up to more than Capacity.</summary>
    public static bool IsOverAllocated(decimal allocatedHours, decimal capacity) => allocatedHours > capacity;

    /// <summary>Group Utilisation: sum of hours divided by sum of Capacity. Null when the Capacity is 0.</summary>
    public static decimal? Utilisation(decimal totalHours, decimal totalCapacity) =>
        totalCapacity == 0 ? null : totalHours / totalCapacity;

    // Time off of one Person does not overlap. The clamp keeps a day from counting twice.
    private static decimal RemovedFraction(DateOnly day, IEnumerable<TimeOffTerm> timeOff) =>
        Math.Min(1m, timeOff.Sum(t => RemovedFraction(t, day)));

    private static decimal RemovedFraction(TimeOffTerm t, DateOnly day)
    {
        if (day < t.StartDate || day > t.EndDate) return 0;
        var morningOff = !(day == t.StartDate && t.StartPart == DayPart.Afternoon);
        var afternoonOff = !(day == t.EndDate && t.EndPart == DayPart.Morning);
        return (morningOff ? 0.5m : 0) + (afternoonOff ? 0.5m : 0);
    }
}
