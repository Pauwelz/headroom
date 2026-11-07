namespace Headroom.Data;

// A WebAuthn credential that lets one Person log in (GLOSSARY: Passkey). Only the public key is stored.
public class Passkey : Record
{
    public Guid PersonId { get; set; }
    public byte[] CredentialId { get; set; } = [];
    public byte[] PublicKey { get; set; } = [];
    // PostgreSQL has no unsigned integer. The value is a uint.
    public long SignCount { get; set; }
    // The WebAuthn user handle: the same for every Passkey of one Person.
    public byte[] UserHandle { get; set; } = [];
    // Names of AuthenticatorTransport values, for example "internal".
    public string[] Transports { get; set; } = [];
    public string Name { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }
}
