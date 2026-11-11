# Company login uses OpenID Connect, without Provisioning

Companies want People to log in with their own identity provider, and they want a person who leaves the company to lose access. Headroom adds an optional Company login through generic OpenID Connect. It works only for a Person who is already in the Instance. A Company login session lasts 12 hours, so a person disabled in the provider loses access within a working day. Offboarding stays the end date, which a Planner sets. [ADR 0008](0008-headroom-is-its-own-oauth-server-with-pre-registered-connected-apps.md) rejected an external identity provider because every Instance would depend on it. That reason still holds, so Company login stays optional.

## Considered Options

- **SCIM Provisioning, with Accounts split from People:** the provider creates, changes and blocks Accounts, and removes access in about 40 minutes. It needs an Account concept beside the Person, a second Entra app, a public write endpoint, rules for who owns which Account, and a custom attribute mapping. Too much for the problem. See `.out-of-scope/identity-provisioning.md` and `docs/research/identity-and-provisioning.md`.
- **Accounts created at the first login, roles from the token, an hourly refresh check:** less than SCIM, but it still needs the Account split, stored refresh tokens and rules for providers that do not check the account on a refresh.
- **An identity proxy in front of the app (Container Apps authentication, oauth2-proxy, Keycloak):** Headroom still needs its own People, Planner right, tokens and sessions. Container Apps authentication moves a Planner setting to the Operator.
- **Entra only, through Microsoft Graph:** other companies cannot use it, against [ADR 0003](0003-each-company-builds-and-hosts-its-own-instance.md).

## Consequences

- One Company login per Instance. A Planner sets it in Settings or on the first-run screen. Headroom saves it only after a test login that matches the Planner who saves. The client secret is encrypted with the Data Protection key ring, as the SMTP password is ([ADR 0017](0017-planners-configure-the-instance-in-the-app.md)).
- Two kinds. **Microsoft Entra ID** needs a single-tenant issuer, matches the first login on `preferred_username`, then on `tid` + `oid`, and never trusts the `email` claim. **Other OpenID Connect** matches the first login on `email` when `email_verified` is true, then on `iss` + `sub`.
- The Person's email must be the email the provider returns (for Entra, the UPN). Headroom has no flow to link another email.
- A Planner can make Company login the only way to log in for People with a company email domain. Their Passkeys are then deleted. The Operator recovers in the database.
- A person disabled in the provider keeps an open session for up to 12 hours, and keeps Access tokens and Connections until a Planner sets the end date.
- The Planner right stays in Headroom. The provider does not create or change People.
