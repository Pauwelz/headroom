# Access tokens belong to a Person and act with its rights

An Access token belonged to the Instance and could only read, so an AI assistant could not ask for "my" schedule or change a plan. Each Access token now belongs to one Person and has the rights of that Person, checked at each call. It is read only unless its Person allows changes. We build these personal tokens before OAuth, because a static bearer token already works in Copilot Studio and Claude, and an OAuth authorization server is a much larger security surface.

## Considered Options

- **Keep Instance-wide tokens and add write rights:** a change has no Person, so `LastChangedBy` and Events cannot say who made it, and "my" has no meaning.
- **Keep both kinds of token:** two terms and two rule sets for one idea.
- **OAuth 2.1 first:** each user signs in from the client, so a shared client acts per user. It needs an authorization server with PKCE, consent, refresh tokens and dynamic client registration. We keep it for a later issue.
- **Copy the rights into the token when it is created:** no lookup per call, but a token keeps rights after its Person loses the Planner role or leaves.

## Consequences

- A client that stores one key for all its users (Claude Team or Enterprise, and possibly Copilot Studio) acts as the Person who owns the key. Per-user calls in those clients need OAuth.
- An Access token cannot manage Access tokens. Otherwise a leaked token could create new tokens and keep access after it is revoked.
- Each call loads the Person, which adds one database lookup.

## Update

Since #32, any Person can set Can change on an Access token or Connection.
The token gives the rights of its Person at request time.
A token of a Person who is not a Planner can change only the Time off of that Person.
