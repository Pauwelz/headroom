namespace Headroom.Data;

// A Person works a percentage of contract hours on a Project. EndDate is inclusive.
public class Allocation : Record
{
    public Guid PersonId { get; set; }
    public Guid ProjectId { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public int Percentage { get; set; }
}
