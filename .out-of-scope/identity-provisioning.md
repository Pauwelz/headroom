# Identity provisioning and Accounts apart from People

Headroom does not let an identity provider create, change or block who can log in. It has no SCIM endpoint, no Account concept beside the Person, and no Planner right from provider roles or groups. Company login ([ADR 0018](../docs/adr/0018-company-login-uses-openid-connect-without-provisioning.md)) works only for People that a Planner added.

## Why this is out of scope

We designed it in full (an Account split, SCIM Users, a Provisioning token, Login blocked, strict ownership) and then dropped it. The research in `docs/research/identity-and-provisioning.md` showed:

- SCIM needs a second, non-gallery Entra app and a custom `externalId` mapping, or a provisioned Account fails Company login.
- Entra never sends a DELETE after an unassignment, so strict ownership leaves blocked Accounts that nobody can remove.
- Float, a close comparable, has no provisioning either.

The problem it solves, fast removal of access, is mostly covered by the 12-hour Company login session and the end date. A Planner adds People by hand anyway, because each Person needs a Contract.

## When to look again

When a company needs People or the Planner right to come from its directory before the first login, or needs access removed within minutes. Start from the closed issues below: they hold the full design and its open points.

## Prior requests

- #84: "Split the Account from the Person"
- #87: "The identity provider manages Accounts through Provisioning"
- #90: "An Entra group gives the Planner right through SCIM Groups"
