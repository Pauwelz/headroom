using Headroom.Capacity;

namespace Headroom.Data;

// Time off of one Person. Start "Afternoon" and end "Morning" make the first and last day half days.
public class TimeOff : Record
{
    public Guid PersonId { get; set; }
    public DateOnly StartDate { get; set; }
    public DayPart StartPart { get; set; }
    public DateOnly EndDate { get; set; }
    public DayPart EndPart { get; set; }

    public TimeOffTerm Term => new(StartDate, StartPart, EndDate, EndPart);
}
