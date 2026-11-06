namespace Headroom.Data;

// Base of every domain record.
public abstract class Record
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    // A Person id, or null for the system.
    public Guid? LastChangedBy { get; set; }

    // Set by HeadroomDbContext on every save.
    public DateTimeOffset DateModified { get; set; }
}
