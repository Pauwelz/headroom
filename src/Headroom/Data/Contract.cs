namespace Headroom.Data;

// Hours per weekday, valid from StartDate until the next Contract of the Person starts.
public class Contract : Record
{
    public Guid PersonId { get; set; }
    public DateOnly StartDate { get; set; }
    public decimal HoursMonday { get; set; }
    public decimal HoursTuesday { get; set; }
    public decimal HoursWednesday { get; set; }
    public decimal HoursThursday { get; set; }
    public decimal HoursFriday { get; set; }
    public decimal HoursSaturday { get; set; }
    public decimal HoursSunday { get; set; }
}
