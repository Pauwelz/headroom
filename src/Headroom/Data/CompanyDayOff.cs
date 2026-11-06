namespace Headroom.Data;

// A date on which nobody works. One per date.
public class CompanyDayOff : Record
{
    public DateOnly Date { get; set; }
    public string Name { get; set; } = "";
}
