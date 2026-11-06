namespace Headroom.Client.Api;

// Access tokens (GLOSSARY). The secret shows once, in the create response.
// Expiry is one of the AccessTokenExpiry choices. A null ExpiresAt means the token never expires.
public record AccessTokenRequest(string Name, string Expiry = AccessTokenExpiry.DefaultChoice, bool CanChange = false);

public record AccessTokenResponse(string Type, Guid Id, string Name, string Owner, DateTimeOffset DateCreated, DateTimeOffset? ExpiresAt, bool CanChange);

public record NewAccessTokenResponse(string Type, Guid Id, string Name, string Owner, DateTimeOffset DateCreated, DateTimeOffset? ExpiresAt, bool CanChange, string Secret);
