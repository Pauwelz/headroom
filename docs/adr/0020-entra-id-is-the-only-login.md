# Entra ID is the only login

Instances run on Azure, and companies that use Headroom use Microsoft Entra ID. We make Entra ID the only way to log in, and we require it. The Login code, Passkeys, the generic OpenID Connect kind, "Company login only" and the Email server are removed. Email existed only for Login codes, so MailKit and the SMTP settings go too. This reverses the reason in [ADR 0008](0008-headroom-is-its-own-oauth-server-with-pre-registered-connected-apps.md) and [ADR 0018](0018-company-login-uses-openid-connect-without-provisioning.md): an Instance now depends on one identity provider. That is acceptable, because the Instance already runs on Azure. The browser keeps a cookie session. Headroom does not use MSAL in the browser.

## Considered Options

- **Keep the Login code and Passkeys as a fallback:** it keeps the Email server, a recovery path that bypasses the company's account policy, and two more login surfaces to secure. Offboarding in Entra ID would not stop a Login code.
- **Keep generic OpenID Connect beside Entra ID:** other providers work, but each Instance needs settings in the database, a test login, and rules for two identity kinds. Companies on Azure do not need it.
- **A client secret in the environment only:** simple, but the Operator must rotate the secret. A federated credential on the managed identity needs no secret. A secret stays possible for local development and the mock provider.
- **MSAL in the browser:** it moves the login into the WebAssembly app and makes the cookie session and the server rules harder to keep.

## Consequences

- The Operator sets `EntraId__TenantId` and `EntraId__ClientId`. Both are required. The app does not start without them. `EntraId__ManagedIdentityClientId` is optional: empty means the system-assigned managed identity. `EntraId__ClientSecret` is optional: when set, Headroom uses it and not the managed identity. `EntraId__Authority` is optional and only a local provider needs it.
- Without a client secret, the code exchange sends a client assertion. Headroom gets it from the managed identity, with the scope `api://AzureADTokenExchange/.default`. The app registration has a federated credential for that identity.
- The identity rule stays: match on `tid` and `oid`, then on `preferred_username` without case. Headroom never trusts the `email` claim. Headroom refuses a `tid` that differs from `EntraId__TenantId`.
- A Planner still creates People. The Person's email must be the `preferred_username` of the account in Entra ID.
- On an empty Instance, the first person of the tenant who logs in becomes the first Planner. Headroom takes the name from `name` and the email from `preferred_username`. The company name stays empty until the Planner sets it in Settings. A database lock makes sure that only one login creates the first Planner.
- Every session lasts 12 hours and does not slide. A person who is disabled in Entra ID loses access within 12 hours. Access tokens and Connections stay until a Planner sets the end date.
- Recovery: when nobody can log in, the Operator fixes the data in the database. There is no other way in.
- The Login code table, the Passkey table and the SMTP columns stay unused for one release, by the expand and contract rule in `docs/migrations.md`. A later release drops them. The migration deletes all open sessions, so every Person logs in again with Entra ID.
- Access tokens and OpenIddict stay for now. They do not depend on the login. A follow-up decides whether `/mcp` accepts Entra ID bearer tokens. Claude connectors against Entra ID are unproven.
- This supersedes [ADR 0015](0015-passkeys-sit-beside-the-login-code-with-fido2netlib.md) and [ADR 0018](0018-company-login-uses-openid-connect-without-provisioning.md). It replaces the Email server part of [ADR 0017](0017-planners-configure-the-instance-in-the-app.md).
