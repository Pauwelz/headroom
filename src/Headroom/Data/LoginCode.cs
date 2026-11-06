namespace Headroom.Data;

// Technical table, not a domain record.
public class LoginCode
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Email { get; set; } = "";
    public byte[] CodeHash { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public int Attempts { get; set; }
}
