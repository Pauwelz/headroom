namespace Headroom.Data;

// A named secret that acts as its Person (GLOSSARY: Access token). Only the SHA-256 hash is stored.
public class AccessToken : Record
{
    public Guid PersonId { get; set; }
    public string Name { get; set; } = "";
    // The token may change data (GLOSSARY: Can change). Its reach comes from the Person on each request.
    public bool CanChange { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public byte[] TokenHash { get; set; } = [];
}
