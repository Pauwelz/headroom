namespace Headroom.Data;

public class InstanceSettings : Record
{
    // Set on first run.
    public string? CompanyName { get; set; }

    // The last erased number given out. The next erase takes one more.
    public int ErasedCount { get; set; }
}
