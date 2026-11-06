namespace Headroom.Data;

// Technical table: one row per auth cookie.
public class Session
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public byte[] Ticket { get; set; } = [];
    public DateTimeOffset ExpiresAt { get; set; }
}
