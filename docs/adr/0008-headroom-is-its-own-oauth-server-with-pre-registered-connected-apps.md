# Headroom is its own OAuth server, with pre-registered Connected apps

Claude Team and Enterprise store one connector key for the whole organisation, so an Access token makes every member act as one Person (ADR 0006). To let each Person connect as themselves, Headroom becomes its own OAuth 2.1 authorization server with OpenIddict. A Planner registers each Connected app in Settings. A Person connects it once with the web login and a consent screen. `/mcp` and `/api` then accept tokens from that Connection, each only for its own URL.

## Considered Options

- **External identity provider (Entra ID, Auth0):** less security code, but every Instance then depends on a provider. ADR 0003 rules this out.
- **Write the authorization server ourselves:** no dependency, but too much security code to get right.
- **Client ID Metadata Documents (CIMD):** the method the MCP spec prefers, and no setup in Claude. OpenIddict does not support it, so we must fetch client URLs safely ourselves. Copilot Studio does not support it. We can add it later.
- **Dynamic Client Registration (DCR):** no setup in Claude or Copilot Studio, but anyone can register a client, and the MCP draft spec deprecates it.

## Consequences

- An Owner enters the client ID and secret of a Connected app once in the connector settings. Each member then connects as themselves.
- Access tokens stay, for scripts and clients without OAuth. A Person can use both.
- A token for `/mcp` is refused on `/api`, and the reverse.
- OAuth keys and tokens live in SQL, not in memory, so the Instance can still scale to zero. A token refresh can hit a cold start, and Claude waits only 10 seconds.
- Authorization codes, access tokens and refresh tokens use the Data Protection key ring in SQL. OpenIddict 7.7.2 also requires a signing key and an encryption key, so Headroom registers ephemeral ones. Nothing uses them: Headroom refuses `openid` and issues no JWT tokens. If Headroom ever issues a JWT token, replace them with persistent keys first.
