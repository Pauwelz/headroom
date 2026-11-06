namespace Headroom.Data;

public enum ProjectStatus { Tentative, Confirmed, Archived }

// Name is unique within the Client, without case. A Project has no dates of its own.
public class Project : Record
{
    public Guid ClientId { get; set; }
    public string Name { get; set; } = "";
    public ProjectStatus Status { get; set; }

    // The API value of the status. Not stored.
    public string StatusName => Status.ToString().ToLowerInvariant();
}
