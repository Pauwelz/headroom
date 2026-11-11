namespace Headroom.Data;

public class Person : Record
{
    public string Name { get; set; } = "";

    // Unique without case.
    public string? Email { get; set; }

    public bool IsPlanner { get; set; }

    // Last working day, inclusive.
    public DateOnly? EndDate { get; set; }

    // Unused since Delete replaced Erase. A later release drops the column.
    public int? ErasedNumber { get; set; }

    // Login stops the day after the end date.
    public bool CanLogIn(DateOnly today) => EndDate is null || EndDate >= today;
}
