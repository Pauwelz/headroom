# Telemetry holds the Person id

Request spans carry the Person id as `enduser.id`. This supersedes the "no Person ids" rule in [ADR 0012](0012-telemetry-stays-in-the-company-subscription.md). Azure Monitor maps `enduser.id` to `user_AuthenticatedId`. The Operator can then count active users and follow the requests of one user while they find a fault. The Person id is a random GUID. Only the Headroom database links it to a name or an email.

Request spans also carry the client IP address as `client.address`. Azure Monitor maps it to `client_IP`. Without it, the column shows the address of the proxy in front of the app, which misleads.

## Considered Options

- **No user id (ADR 0012):** no personal data in telemetry, but the Operator cannot count users or trace a fault reported by one Person.
- **A keyed hash of the Person id:** it hides the id from a reader without database access. It is still pseudonymous personal data under GDPR, and it needs a secret per Instance.
- **The email address:** the Operator reads it directly, but it puts a direct identifier in telemetry.

## Consequences

- The Person id and the client IP address are personal data. Telemetry stays in the company's subscription (ADR 0012), and its retention applies to both.
- An Operator can switch off each value with `Telemetry__EndUserId=false` or `Telemetry__ClientAddress=false`.
- Names, emails, Login codes, paths with ids, query strings and SQL text stay out. The integration test still asserts this.
- Erasure removes the name and email from the database. After that, the Person id in telemetry no longer links to a Person.
- MCP calls export the spans of the MCP SDK, with the tool name. They carry no tool arguments.
