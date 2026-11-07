namespace Headroom.Client.Api;

// Passkeys (GLOSSARY). The WebAuthn options and credentials travel as raw JSON, not as these types.
public record PasskeyResponse(string Type, Guid Id, string Name, DateTimeOffset DateCreated, DateTimeOffset? LastUsedAt);
