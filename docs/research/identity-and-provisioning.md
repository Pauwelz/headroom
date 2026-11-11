# Identity and Provisioning: how comparable software does it

> Outcome (2026-11-11): the maintainer chose the minimal design. Company login stays for existing People with a 12-hour session. The Account split and SCIM are out of scope. See ADR 0018 and `.out-of-scope/identity-provisioning.md`. The decision numbers below refer to the earlier design.

Research date: 2026-11-11. Scope: ADR 0018, 0019, 0020 and issues #84 to #90.
Every claim links to the vendor document, the spec or the source file that owns it.
A sentence that starts with "Judgment:" is our conclusion, not a fact from a source.
Subagents read the vendor pages. The last section lists which quotes were checked again against the live page.

## Summary

| # | Decision | Verdict | Reason |
|---|---|---|---|
| 1 | Account split from Person | Keep | Float and Runn keep access apart from the scheduled person. Productive merges them and loses future bookings. |
| 2a | Entra login key is `tid` + `oid` | Keep | Microsoft names `oid` + `tid` as the key to use. |
| 2b | First login matches on `preferred_username` | Risk to note | Microsoft says never to use it for access decisions. The single-tenant rule limits the risk. |
| 2c | No "link your login" flow | Keep | Float, Runn and Linear also require the email to match. |
| 2d | "`externalId` is the `oid`" (#87) | Change | The Entra example mapping sends `mailNickname`. Without a custom mapping, Company login refuses provisioned Accounts. |
| 3 | "Company login only", recovery by SQL | Keep, risk to note | Self-hosted apps recover on the server. SaaS products exempt an owner instead. |
| 4a | SCIM Users only, Planner from app role | Keep | Entra supports it. Group assignment needs P1 or P2. |
| 4b | Strict ownership of every Account | Change (small) | Nobody can block an Account without a Person. Entra never deletes an unassigned user. |
| 4c | A push can remove the last Planner | Risk to note | Notion, Slack and Tailscale protect one owner. |
| 5 | Block at the next request, 30-day cookie | Keep, close one gap | Matches Tailscale, GitHub and Notion. Without Provisioning, the directory cannot end a session. |
| 6 | Provisioning never touches a Person | Keep | No tool sets an end date from a provider. Entra sends one signal for four events. |
| 7 | Auto-link on a unique name | Risk to note | No precedent found. Others link on email. |
| 8 | Delete removes history | Risk to note | Each tool with a documented answer keeps history in reports. |
| 9 | Overall design | Adjust | Keep Accounts and Company login. Park SCIM. Limit the session age for Company login. |

## Alternative overall designs

Our design: local Accounts, optional OpenID Connect login, optional SCIM with strict ownership.

### (a) An identity proxy or broker in front of the app

- Azure Container Apps built-in authentication runs "as a sidecar container on each replica". It supports [Entra][aca-auth] and any [custom OpenID Connect provider][aca-oidc].
- It has two modes. "Require authentication" rejects all unauthenticated traffic. "Allow unauthenticated access" leaves authorization to the application code ([Container Apps][aca-auth]).
- It passes the identity in headers such as `X-MS-CLIENT-PRINCIPAL-ID` ([Container Apps][aca-auth]). It knows nothing about a Planner right or a blocked Account.
- oauth2-proxy is "a reverse proxy" that validates accounts "by email, domain or group" ([oauth2-proxy][o2p]). Its default session lives in a client-side cookie ([session storage][o2p-session]).
- Keycloak persists its data in a database ([Keycloak][kc-db]). Its sizing guide gives 1250 MB of base memory for each pod ([Keycloak][kc-size]).
- Authentik: not researched.

Judgment:
- Code and security surface: the proxy removes only the OpenID Connect handler, which ASP.NET Core already supplies. Headroom still needs Accounts, the Planner right, Access tokens and Connections.
- Login codes, Passkeys, `/api`, `/mcp` and the OAuth server need anonymous access. So the proxy must run in "allow unauthenticated" mode, and Headroom keeps its own session logic.
- Fit with ADR 0003: Container Apps authentication is an Azure feature that the Operator sets. It moves a Planner setting back to the Operator, against ADR 0017.
- A broker makes every Instance depend on a second service with its own database. ADR 0008 rejected that. It also breaks the cost target and scale to zero.
- Deprovisioning speed: no gain. The proxy session is separate from the directory.
- Verdict: reject.

### (b) ASP.NET Core Identity with external logins

- Microsoft documents external providers as a feature of Identity ([ASP.NET Core][ext-login]).
- The same page warns that a user without a local password is "completely dependent on the external login provider" ([ASP.NET Core][ext-login]).
- ADR 0015 already records that Headroom has no `UserManager` and no Identity user store.

Judgment: Identity adds a password-shaped user store and its own tables. It solves account linking, which we decided not to build. Issue #85 already uses the platform OpenID Connect handler. Verdict: reject.

### (c) OpenID Connect login only, Accounts made just in time from token claims, no SCIM

- Entra puts a `roles` claim in the ID token: "The set of roles that were assigned to the user who is logging in" ([ID token claims][id-claims]). App roles reach ID tokens when the app signs in a user ([app roles][app-roles]).
- With "Assignment required" set to Yes, only assigned users get a token ([Entra][restrict]).
- Productive creates the account on the first login: "Productive will automatically create one the first time they log in" ([Productive][prod-sso]). Toggl has the same option ([Toggl][toggl-sso]).
- The weak point is the open session. Grafana states it: without a refresh token, "the session remains valid even if the user loses access at the IdP" ([Grafana][graf-auth]).
- Grafana closes it with the refresh token: when access is revoked, "the refresh will fail and the session will be invalidated" ([Grafana][graf-auth]).
- Entra refresh tokens last 90 days by default. The server can revoke them "because of a change in credentials, user action, or admin action" ([refresh tokens][refresh]).
- On a refresh, Entra "reevaluates its authorization policies. If the user is still authorized", it issues new tokens ([revoke access][revoke]).
- An Entra access token lasts 60 to 90 minutes by default ([token lifetimes][token-life]).

Judgment:
- Code and security surface: much smaller than SCIM. No public write endpoint, no filter parser, no PATCH variants, no Provisioning token. New: one encrypted refresh token for each Account, and a timed re-check.
- Fit with ADR 0003: standard OpenID Connect, so it works for every provider. It needs no second Entra app (see "SCIM" below).
- Scale to zero: Entra calls a SCIM endpoint every 40 minutes ([SCIM tutorial][scim-tut]). Each call wakes the Instance. A re-check on use does not. Not measured.
- Deprovisioning speed: at most one access-token lifetime, so 60 to 90 minutes for Entra. The exact result for a disabled user needs a test in a real tenant (#89).
- Access tokens and Connections can run the same re-check with the stored refresh token of their Account.
- Planner effort: an Account appears only after its first login. This is acceptable, because a Person needs no Account to be scheduled.
- It loses name and email updates between logins. It cannot remove an Account, only refuse it.
- Verdict: the best alternative for directory-driven access. It is a candidate until the tenant test passes.

### (d) What comparable planning tools do

- Float has login through the provider and nothing more: "User provisioning is not currently supported" ([Float, Entra][float-entra]).
- Float requires the Account first: "they must already have account access to Float" ([Float, Entra][float-entra]).
- Runn requires an invitation "with an email address matching in your Identity Provider" ([Runn][runn-saml]).
- Only Productive documents SCIM, on its highest plan ([Productive][prod-scim]). Forecast and Harvest pages blocked our fetch.

Judgment: Float ships what #84, #85 and #86 describe, and stops there. A Planner removes access by hand.

### Comparison (all cells are judgments from the facts above)

| Design | Code and security surface | Fit with ADR 0003 | Deprovisioning speed | Operator and Planner effort |
|---|---|---|---|---|
| Ours, with SCIM | Largest: a public write endpoint, PATCH variants, an Instance secret | Good: standards only | About 40 minutes, also for tokens | High: two Entra apps, mappings, app role |
| Ours, without SCIM (as Float) | Small: the platform login handler | Good | By hand, or the end date of the Person | Low: one Entra app |
| (a) Proxy or broker | No smaller: Headroom keeps Accounts and sessions | Poor: an Azure-only setting, or a second service | No gain | High for the Operator. The Planner loses the setting. |
| (b) ASP.NET Core Identity | Larger: a second user store and a migration | Good | No gain | Same as ours |
| (c) Login with claims and re-check | Medium: one stored refresh token for each Account | Good | 60 to 90 minutes, to be tested | Low: one Entra app, app role in the token |
| (d) Login only, as Float | Same as ours without SCIM | Good | By hand | Low |

### Recommendation: adjust

1. Keep the Account split (#84), Company login (#85) and "Company login only" (#86). No alternative removes this code, and (a) and (b) add a dependency or a second store.
2. Do not build SCIM (#87) now. Float runs without provisioning, and SCIM is the largest part of the design.
3. Limit the session age for Company login sessions. This needs no unverified fact and bounds the gap in section 5.
4. When a company wants the directory to end access, test the refresh re-check of design (c) in a real tenant first.
5. Build SCIM only when a company needs Accounts before the first login. Then apply the changes in "Recommended changes".

## 1. Login account versus scheduled person

- Float separates them. "Adding people doesn't automatically grant them access to Float. Access is optional" ([Float, People][float-people]).
- Float schedules people without access, and they still use a paid seat ([Float, People][float-people]).
- Float can remove access "while keeping the person on your Schedule" ([Float, remove access][float-remove]).
- Float also has placeholders as a resource type ([Float, People][float-people]).
- Runn keeps a contract end date on the person, apart from login ([Runn, archive][runn-archive]).
- Productive deactivates a user and then removes the work: "Future bookings are deleted" ([Productive, offboarding][prod-off]). Judgment: Productive joins access and scheduling.
- Resource Guru, Toggl Plan, Harvest, Forecast, Replicon: not found.

Verdict: keep. ADR 0018 says that Float keeps access and the end date apart. The sources confirm it.

## 2. Identity matching

Specifications and Microsoft:
- OpenID Connect Core: `sub` and `iss` together "are the only Claims that an RP can rely upon as a stable identifier" ([OIDC Core 5.7][oidc]).
- `email_verified` true means that the provider "took affirmative steps" to check control of the address ([OIDC Core 5.1][oidc]).
- Entra: `oid` is "the immutable identifier" and is the same for every application ([ID token claims][id-claims]).
- Entra: `sub` is "a pairwise identifier and is unique to an application ID" ([ID token claims][id-claims]).
- Microsoft: "use the immutable claim values tid and oid as a combined key" ([claims validation][claims-val]).
- Entra: `email` "isn't guaranteed to be correct and is mutable". "Never use it for authorization" ([ID token claims][id-claims]).
- Entra: `preferred_username` "is mutable". It "can't be used to make authorization decisions" ([ID token claims][id-claims]).
- Microsoft: "Never use claims like email, preferred_username or unique_name". They "can be controllable by tenant administrators or sometimes users" ([claims validation][claims-val]).
- The guest value of `preferred_username`: not found. A guest UPN is stored as `foo_hometenant.com#EXT#@resourcetenant.com` ([optional claims][opt-claims]).

Other products:
- Float matches "primary email first, then fallback to NameID" ([Float, SSO][float-sso]). Runn and Linear match on email ([Runn][runn-saml], [Linear][linear-saml]).
- Slack wants an identifier that "will not change for the user over time" ([Slack][slack-saml]).
- GitHub managed users lose the link to their history after an email change at the provider ([GitHub][gh-emu]).
- Grafana matches on the provider's unique id. Email lookup is off by default: `oauth_allow_insecure_email_lookup = false` ([Grafana defaults.ini][graf-ini]).
- GitLab uses `sub` as the uid ([GitLab OIDC][gl-oidc]). It calls linking "on email match alone" the less secure way ([GitLab OmniAuth][gl-omni]).
- Linking flows exist at GitLab (user-driven) and Slack (bind by email) ([GitLab OmniAuth][gl-omni], [Slack][slack-dup]). Float, Runn and Linear document none.

Verdicts:
- `tid` + `oid` for Entra, `iss` + `sub` for other providers: keep.
- Other providers, first match on `email` with `email_verified`: keep. It is the strongest first match that the spec offers.
- Entra first match on `preferred_username`: risk to note. It goes against the Microsoft warning. Judgment: the risk is small, because the issuer is one tenant, the match happens once, and only a tenant administrator sets the UPN. The guest case is unknown. Record this reasoning in ADR 0019.
- No linking flow: keep. A Planner can set the Account email to the UPN, as the note on #85 says.

## 3. "Company login only" and recovery

- The switch is common. Float: "SSO becomes the only way to sign in, and other login options are removed" ([Float, SSO][float-sso]). Runn and Toggl have it too ([Runn][runn-saml], [Toggl][toggl-sso]).
- Float has "Require SSO for specific domains - a flexible, mixed enforcement option", as #86 does ([Float, SSO][float-sso]).
- Runn warns: test first, "or you will no longer have access to your account" ([Runn][runn-saml]). Its recovery path: not found.
- SaaS products keep one way in. Slack owners "can bypass SSO authentication" ([Slack][slack-sso]). Notion owners "can still sign in with an email and password" ([Notion][notion-saml]).
- 1Password owners "can't unlock 1Password with SSO", to prevent lockout ([1Password][op-sso]). Linear admins "may be able to log in through other methods" ([Linear][linear-saml]).
- Toggl has a list of exempt emails ([Toggl][toggl-sso]). GitHub has a setup user and recovery codes ([GitHub][gh-recovery]).
- Self-hosted apps recover on the server. Grafana: `grafana cli admin reset-admin-password` ([Grafana CLI][graf-cli]). GitLab: a rake task or the Rails console ([GitLab][gl-reset]).
- GitLab and Nextcloud also keep a bypass URL: `?auto_sign_in=false` and `?direct=1` ([GitLab OmniAuth][gl-omni], [Nextcloud user_oidc][nc-oidc]).

Verdict: keep. Recovery by the Operator in the database is normal for a self-hosted app. Others ship a command, not raw SQL.
Risk to note: no SaaS product in this list locks every owner out. "No Planner is exempt" is stricter than all of them.
Judgment, no source: the switch could refuse Passkeys and keep them. That is less code, and turning the switch off would restore them.

## 4. SCIM

Users only, and the Planner right:
- Microsoft: "You are not required to support both users and groups" ([SCIM tutorial][scim-tut]).
- RFC 7643 defines `roles` with "No vocabulary or syntax" ([RFC 7643, 4.1.2][rfc7643]).
- Entra sends app roles in `roles` only through a mapping expression such as `AssertiveAppRoleAssignmentsComplex` ([attribute mappings][scim-map]). A direct mapping is wrong.
- That expression sends a PATCH `replace`, so a removed role also goes away in the application ([attribute mappings][scim-map]).
- Role expressions do not work with the scope "Sync All users and groups" ([attribute mappings][scim-map]).
- Microsoft: "Historically, users assigned to this role aren't provisioned". The role is "default access" ([Entra troubleshooting][scim-skip]).
- Others: Notion uses a `role` attribute. Linear uses role groups. Tailscale refuses roles from group sync ([Notion][notion-scim], [Linear][linear-scim], [Tailscale][ts-scim]).

Licence:
- "Group-based assignment requires Microsoft Entra ID P1 or P2 edition" ([Entra, assignment][assign]).
- The licensing table marks "Automated user provisioning to SaaS apps" for Free and "Automated group provisioning" from P1 ([Entra licensing][lic]). It does not name non-gallery apps.

Entra facts that change #87:
- The tutorial's example mapping sends `mailNickname` as `externalId` for users. It lists `objectId` only for groups ([SCIM tutorial][scim-tut]).
- The tutorial's sample query matches "the mailNickname attribute value", and says that the attribute "is configurable" ([SCIM tutorial][scim-tut]).
- Judgment from #85 and #87: #87 stores `externalId` as the Company login identifier. With `mailNickname` there, a login with `tid` + `oid` finds another identifier and is refused.
- An OpenID Connect app registration cannot provision: "create a second non-gallery app for provisioning" ([known issues][scim-known]). Company login and Provisioning are then two Entra apps.
- "The process runs every 40 minutes" ([SCIM tutorial][scim-tut]). The interval "is currently not configurable" ([known issues][scim-known]). The first cycle takes longer.
- Without a flag, Entra disables a user with `"value": "False"` as a string. With `aadOptscim062020` it sends a `replace` without a `path` ([SCIM compliance][scim-compat]).
- Entra emits `op` as "Add", "Replace" and "Remove" ([SCIM tutorial][scim-tut]). Issue #87 handles all three quirks.
- Long-lived bearer token: "Supported for existing apps and non-gallery apps. Not supported for new apps" ([SCIM tutorial][scim-tut]). Judgment: "new apps" means new gallery apps.
- Entra also offers the OAuth client credentials grant and Workload Identity Federation for non-gallery apps ([SCIM tutorial][scim-tut]).

`active` and DELETE:
- RFC 7643: the meaning of `active` "is determined by the service provider" ([RFC 7643, 4.1.1][rfc7643]). After a DELETE, the service must return 404 ([RFC 7644, 3.6][rfc7644]).
- Entra sets `active` to false in four cases: unassigned, out of scope, soft-deleted, and `AccountEnabled` false ([attribute mappings][scim-map]).
- Entra sends DELETE only for a hard delete, 30 days after the soft delete ([how provisioning works][scim-how]).
- After an unassignment, "a delete request isn't sent when the user is soft-deleted" ([how provisioning works][scim-how]). An unassigned Account stays blocked for ever.
- Microsoft: "always support both soft-deletes and hard-deletes" ([how provisioning works][scim-how]).
- Others suspend and rarely delete. GitHub: "a deprovisioned account is never deleted" ([GitHub][gh-deprov]). Notion: "cannot be deleted through SCIM" ([Notion][notion-scim]).
- 1Password lets an administrator delete an account "after they've been deprovisioned" ([1Password][op-prov]).

Ownership:
- Strict: Linear "Admins will not be able to manage users", with "a manual override to allow user suspension" ([Linear][linear-scim]).
- Per account: Atlassian locks only an account that "is synced from an identity provider" ([Atlassian][atl-managed]). Tailscale suspends other users by hand ([Tailscale][ts-offboard]).
- Grafana can prevent "non-SCIM provisioned users from signing in" ([Grafana SCIM][graf-scim]).
- Protected owner: Notion keeps the owner that made the token. Slack keeps the Primary Owner. Tailscale needs a new Owner first ([Notion][notion-scim], [Slack][slack-deact], [Tailscale][ts-remove]).
- SCIM off: Linear "All SCIM restrictions will stop being enforced" ([Linear][linear-scim]). GitHub suspends all managed accounts ([GitHub][gh-disable]).

Verdicts:
- Users only, Planner from an app role, block on `active`, DELETE removes: keep.
- Strict ownership: change (small). Headroom freezes a never-pushed Account and lets it keep working.
- An Account with a Person is safe: a Planner sets the end date, and #84 then refuses the login. An Account without a Person cannot be blocked by anyone.
- None of the products we read freezes an account and leaves it active.
- An unassigned Account stays for ever, with its email, and no Planner can delete it. This weakens "the provider decides" in ADR 0020.
- Last Planner removed by a push: risk to note. Recovery by SQL is acceptable for a self-hosted app.
- Stopping Provisioning returns control to Planners: keep. Linear does the same.

## 5. Deprovisioning effects on sessions, tokens and grants

- Tailscale: "Their API access tokens and auth keys stop working", usually "within seconds" ([Tailscale][ts-remove]).
- GitHub: "personal access tokens, SSH keys, and app authorizations can no longer be used" ([GitHub][gh-offboard]).
- Notion: "The user is logged out of all active sessions" ([Notion][notion-scim]). Slack: "signed out of your workspace(s) on all devices" ([Slack][slack-deact]).
- Counter-example, Atlassian: a SCIM deactivation "does not automatically remove" access, and API tokens "remain usable" ([Atlassian][atl-tokens]).
- Tailscale OAuth clients keep working after their creator has left ([Tailscale][ts-oauth]).
- ASP.NET Core: "Validating the cookie on every request mitigates the risk of revoked users accessing the app" ([ASP.NET Core][cookie]).
- The same page warns of "a large performance penalty" for large apps ([ASP.NET Core][cookie]). Headroom already reads the Person on every request (#84).
- Session length: Mattermost SSO sessions have "Default is 720 hours" ([Mattermost][mm-session]).
- Microsoft: "the app might never send the user back to Microsoft Entra ID as long as the session token is valid" ([revoke access][revoke]).
- Microsoft: "Microsoft Entra ID can't directly revoke a session token issued by an application" ([revoke access][revoke]).

Verdict: keep. The check on every request puts Headroom in the stricter group. A 30-day cookie with that check is normal.
Gap: with Company login and no Provisioning, nothing tells Headroom that the directory disabled a user. The sliding cookie and the Access tokens keep working.
Grafana documents the same gap and closes it with a refresh token ([Grafana][graf-auth]). Until a Planner sets the end date, the Account stays open.

## 6. End dates, archiving and future Allocations

- No tool was found where a provider sets an end date. Float has no provisioning at all ([Float, Entra][float-entra]).
- Float: "Archiving a person does not update their start and end date" ([Float, archive][float-archive]). Archiving removes login access.
- Runn refuses the step: "you'll need to ensure they don't have any future assignments". It wants a contract end date first ([Runn, archive][runn-archive]).
- Resource Guru deletes future bookings on archive: they "will be permanently deleted" ([Resource Guru][rg-archive]).
- Productive deletes on deactivation: "Deleted bookings cannot be restored" ([Productive, offboarding][prod-off]).
- Productive deactivates through SCIM when the user leaves the Okta app ([Productive][prod-scim]). Inference from the two pages: a provider change can delete future bookings there.
- Entra gives one `active: false` for four different events (section 4). A suspension and a resignation look the same.

Verdict: keep. "Mark the Person, a Planner sets the end date" is the safe choice. Productive shows the irreversible alternative.
Auto-link on a unique name: no precedent found. Atlassian links an existing account on email ([Atlassian][atl-prov]). Risk to note: a wrong link gives an Account the Time off of another Person.

## 7. Delete versus anonymise

- Float archive: "All their historical data remains retained for reporting purposes" ([Float, archive][float-archive]). Float delete is "permanent and irreversible". Its effect on reports: not found.
- Resource Guru: "Historical data relating to deleted resources will still appear in your reports" ([Resource Guru][rg-archive]).
- Runn archive: "will retain all their data" ([Runn, archive][runn-archive]).
- GitHub keeps the commits, issues and comments of a removed user ([GitHub][gh-offboard]). Slack keeps messages and files ([Slack][slack-deact]).
- Atlassian deactivation "won't delete personal data" ([Atlassian][atl-deact]).
- An anonymise or erase feature in a planning tool: not found.

Verdict: risk to note. Each tool with a documented answer keeps history after a person leaves. Headroom changes past Utilisation, and it does so on its own after 12 months.
The comparison with GitHub in ADR 0020 holds for one point: the provider controls the account. GitHub never deletes it and keeps the history ([GitHub][gh-deprov]).

## 8. Entra specifics

| Check | Answer | Source and page date |
|---|---|---|
| Cycle | "The process runs every 40 minutes". The interval is fixed. | [SCIM tutorial][scim-tut], 2026-09-15. [Known issues][scim-known], 2026-11-09. |
| Disable, unassign, out of scope, soft delete | All four send `active` false. | [Attribute mappings][scim-map], 2026-08-06. |
| Hard delete | DELETE, 30 days after the soft delete. Never after an unassignment. | [How provisioning works][scim-how], 2025-03-04. |
| Guest `preferred_username` | Not found. | Test in #89. |
| Licence, group assignment | "requires Microsoft Entra ID P1 or P2 edition". | [Assignment][assign], 2026-04-01. |
| Licence, user provisioning | Listed for Free. Non-gallery apps are not named. | [Licensing][lic], 2026-06-18. |
| Long-lived bearer token | "Supported for existing apps and non-gallery apps. Not supported for new apps." | [SCIM tutorial][scim-tut], 2026-09-15. |
| Token field left blank | The Entra-issued token "is primarily available for testing purposes". | [SCIM tutorial][scim-tut], 2026-09-15. |

## Recommended changes, ranked by impact

1. Reorder the plan: ship #84, #85 and #86, and park #87. Float runs without provisioning.
2. Close the session gap of Company login without Provisioning. Give Company login sessions an absolute age. Test the refresh re-check of design (c) in #89.
3. If SCIM stays, correct #87 and ADR 0019: `externalId` is the `oid` only with a custom mapping from `objectId`. Without it, Company login refuses provisioned Accounts. Put the mapping in the user guide.
4. If SCIM stays, let a Planner delete a Login-blocked Account. 1Password allows this. Entra sends no DELETE after an unassignment.
5. If SCIM stays, let a Planner block an Account that has no Person and no provider identifier. Atlassian locks only synced accounts.
6. Add to the user guide and #89: Provisioning needs a second, non-gallery Entra app. Where the `Planner` app role must live is not verified.
7. Replace "Not verified" in ADR 0019: group assignment needs P1 or P2. User provisioning is listed for Free, non-gallery not named.
8. Record in ADR 0019 why the first match on `preferred_username` is acceptable, against the Microsoft warning.
9. Say in ADR 0020 that comparable tools keep history, and that the 12-month Delete changes past reports without a prompt.

## Not found or unverified

- The value of `preferred_username` for an Entra guest. Issue #89 already plans this check.
- Whether an app role assigned through a group reaches the SCIM `roles` attribute.
- Whether "Automated user provisioning to SaaS apps" on the Free licence covers non-gallery apps.
- The exact refresh result after `AccountEnabled` is false. Microsoft says only that Entra "reevaluates its authorization policies". Test it in #89.
- Whether a `Planner` app role on the non-gallery app is visible to the provisioning mapping. Likely, not verified.
- The Microsoft guide on moving away from the `email` claim now redirects to a hub page. The claims reference was used.
- Harvest, Forecast: help pages returned HTTP 403. Replicon, Toggl Plan (people), Resource Guru (login): no usable page.
- Slack SCIM API pages returned HTTP 403. Slack role mapping: not found.
- Browser session effects of deprovisioning at GitHub, Atlassian, Linear and Tailscale: not found.
- GitLab self-managed: tokens of a blocked user, and DELETE through SCIM: not found.
- Mattermost and Outline: identity key, linking and behaviour after a provider disable: not found.
- Grafana: the claim used for the Entra user id, and the default session length: not found.
- Authentik was not researched. No measurement of SCIM calls against scale to zero.
- Checked again against the live page: all Microsoft, RFC and OpenID quotes, and the quotes from Float, Resource Guru, Productive (offboarding), Linear, GitHub (deprovisioning, offboarding), Notion (SCIM), 1Password (owners), Grafana (sessions), Atlassian (synced accounts) and Mattermost.
- Not checked again, taken from the subagent reports: Runn, Toggl, Slack, Tailscale, GitLab, Nextcloud, Productive (login, SCIM), the other Atlassian, GitHub, Notion and 1Password pages.

[aca-auth]: https://learn.microsoft.com/en-us/azure/container-apps/authentication
[aca-oidc]: https://learn.microsoft.com/en-us/azure/container-apps/authentication-openid
[o2p]: https://oauth2-proxy.github.io/oauth2-proxy/
[o2p-session]: https://oauth2-proxy.github.io/oauth2-proxy/configuration/session_storage
[kc-db]: https://www.keycloak.org/server/db
[kc-size]: https://www.keycloak.org/high-availability/single-cluster/concepts-memory-and-cpu-sizing
[ext-login]: https://learn.microsoft.com/en-us/aspnet/core/security/authentication/social/?view=aspnetcore-10.0
[cookie]: https://learn.microsoft.com/en-us/aspnet/core/security/authentication/cookie?view=aspnetcore-10.0
[id-claims]: https://learn.microsoft.com/en-us/entra/identity-platform/id-token-claims-reference
[claims-val]: https://learn.microsoft.com/en-us/entra/identity-platform/claims-validation
[opt-claims]: https://learn.microsoft.com/en-us/entra/identity-platform/optional-claims-reference
[app-roles]: https://learn.microsoft.com/en-us/entra/identity-platform/howto-add-app-roles-in-apps
[restrict]: https://learn.microsoft.com/en-us/entra/identity-platform/howto-restrict-your-app-to-a-set-of-users
[refresh]: https://learn.microsoft.com/en-us/entra/identity-platform/refresh-tokens
[revoke]: https://learn.microsoft.com/en-us/entra/identity/users/users-revoke-access
[token-life]: https://learn.microsoft.com/en-us/entra/identity-platform/configurable-token-lifetimes
[scim-tut]: https://learn.microsoft.com/en-us/entra/identity/app-provisioning/use-scim-to-provision-users-and-groups
[scim-how]: https://learn.microsoft.com/en-us/entra/identity/app-provisioning/how-provisioning-works
[scim-map]: https://learn.microsoft.com/en-us/entra/identity/app-provisioning/customize-application-attributes
[scim-known]: https://learn.microsoft.com/en-us/entra/identity/app-provisioning/known-issues
[scim-compat]: https://learn.microsoft.com/en-us/entra/identity/app-provisioning/application-provisioning-config-problem-scim-compatibility
[scim-skip]: https://learn.microsoft.com/en-us/entra/identity/app-provisioning/application-provisioning-config-problem-no-users-provisioned
[assign]: https://learn.microsoft.com/en-us/entra/identity/enterprise-apps/assign-user-or-group-access-portal
[lic]: https://learn.microsoft.com/en-us/entra/fundamentals/licensing
[oidc]: https://openid.net/specs/openid-connect-core-1_0.html
[rfc7643]: https://www.rfc-editor.org/rfc/rfc7643
[rfc7644]: https://www.rfc-editor.org/rfc/rfc7644
[float-people]: https://support.float.com/en/articles/5228805-people
[float-remove]: https://support.float.com/en/articles/6556534-remove-account-access
[float-archive]: https://support.float.com/en/articles/28916-archive-a-person
[float-sso]: https://support.float.com/en/articles/2360130-single-sign-on-2fa-and-google-sign-in
[float-entra]: https://support.float.com/en/articles/3941805-microsoft-entra-id-formerly-azure-ad
[runn-saml]: https://help.runn.io/en/articles/4633734-setting-up-saml-sso
[runn-archive]: https://help.runn.io/en/articles/11573444-how-do-i-archive-or-unarchive-a-person
[rg-archive]: https://help.resourceguruapp.com/en/articles/1954498
[prod-sso]: https://help.productive.io/en/articles/4362684-enabling-sso-using-okta
[prod-scim]: https://help.productive.io/en/articles/15432512-sync-users-between-okta-and-productive-with-scim
[prod-off]: https://help.productive.io/en/articles/12397436-offboarding-users
[toggl-sso]: https://support.toggl.com/how-to-set-up-single-sign-on
[gh-emu]: https://docs.github.com/en/enterprise-cloud@latest/admin/identity-and-access-management/using-enterprise-managed-users-for-iam/about-enterprise-managed-users
[gh-deprov]: https://docs.github.com/en/enterprise-cloud@latest/admin/managing-iam/provisioning-user-accounts-with-scim/deprovisioning-and-reinstating-users
[gh-offboard]: https://docs.github.com/en/enterprise-cloud@latest/admin/concepts/identity-and-access-management/user-offboarding
[gh-recovery]: https://docs.github.com/en/enterprise-cloud@latest/admin/identity-and-access-management/managing-recovery-codes-for-your-enterprise/accessing-your-enterprise-account-if-your-identity-provider-is-unavailable
[gh-disable]: https://docs.github.com/en/enterprise-cloud@latest/admin/managing-iam/configuring-authentication-for-enterprise-managed-users/disabling-authentication-and-provisioning-for-enterprise-managed-users
[slack-saml]: https://slack.com/help/articles/205168057-Custom-SAML-single-sign-on
[slack-sso]: https://slack.com/help/articles/203772216-Set-up-SAML-single-sign-on-for-Slack
[slack-dup]: https://slack.com/help/articles/360002069588-Fix-duplicate-accounts
[slack-deact]: https://slack.com/help/articles/204475027-Deactivate-a-members-account
[atl-prov]: https://support.atlassian.com/provisioning-users/docs/understand-user-provisioning/
[atl-managed]: https://support.atlassian.com/user-management/docs/make-changes-to-a-managed-user-account/
[atl-deact]: https://support.atlassian.com/user-management/docs/deactivate-a-managed-account/
[atl-tokens]: https://support.atlassian.com/jira/kb/atlassian-api-token-key-status-of-api-tokens-keys-when-a-user-who-generated-the-token-key-has-left-the-organization/
[notion-saml]: https://www.notion.com/help/saml-sso-configuration
[notion-scim]: https://www.notion.com/help/provision-users-and-groups-with-scim
[linear-saml]: https://linear.app/docs/saml-and-access-control
[linear-scim]: https://linear.app/docs/scim
[op-sso]: https://support.1password.com/sso/
[op-prov]: https://support.1password.com/provisioning-best-practices/
[ts-scim]: https://tailscale.com/docs/integrations/identity/entra/entra-id-scim
[ts-offboard]: https://tailscale.com/docs/features/sharing/how-to/offboard
[ts-remove]: https://tailscale.com/docs/features/sharing/how-to/remove-team-members
[ts-oauth]: https://tailscale.com/kb/1215/oauth-clients
[graf-auth]: https://grafana.com/docs/grafana/latest/setup-grafana/configure-security/configure-authentication/
[graf-ini]: https://github.com/grafana/grafana/blob/main/conf/defaults.ini
[graf-cli]: https://grafana.com/docs/grafana/latest/administration/cli/
[graf-scim]: https://grafana.com/docs/grafana/latest/setup-grafana/configure-security/configure-scim-provisioning/
[gl-oidc]: https://docs.gitlab.com/ee/administration/auth/oidc.html
[gl-omni]: https://docs.gitlab.com/ee/integration/omniauth.html
[gl-reset]: https://docs.gitlab.com/ee/security/reset_user_password.html
[mm-session]: https://docs.mattermost.com/administration-guide/configure/environment-configuration-settings.html
[nc-oidc]: https://github.com/nextcloud/user_oidc
