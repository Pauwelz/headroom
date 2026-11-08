# Changelog

All notable changes to this project are in this file.
The format follows [Keep a Changelog 1.1.0](https://keepachangelog.com/en/1.1.0/).
This project follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.3.0] - 2026-11-08

### Added

- MCP: the new tools `list_company_days_off`, `add_company_day_off`, `change_company_day_off` and `delete_company_day_off` let a Planner manage Company days off.
- MCP: the new tool `list_people` gives a Planner every Person with their Contracts and ids. It does not need Can change.
- MCP: the new tools `add_person`, `change_person`, `add_contract`, `change_contract` and `delete_contract` let a Planner manage People and Contracts.
- MCP: the new tools `get_end_date_impact`, `set_end_date` and `remove_end_date` let a Planner set and remove the end date of a Person. The agent shows the impact first.
- Log in with a Passkey: select **Log in with a Passkey** on the login page. The Login code keeps working.
- Save and remove Passkeys on the new Account page.
- After a Login code login, Headroom offers to save a Passkey.
- MCP: the new tools `list_clients`, `create_client`, `rename_client`, `create_project` and `change_project` let a Planner manage Clients and Projects.
- MCP: the new tool `split_allocation` lets a Planner split an Allocation on a date. It needs **Can change**.
- Each Person can add, change and delete their own Time off in the Schedule.
- The new **Time off** page shows a month calendar of who is off on each day. It also shows public holidays and Company days off. Every Person can see it. Change Time off in the Schedule.

### Changed

- The Account page replaces the Access tokens page. It holds your Passkeys, Access tokens and Connections. The `/access-tokens` address is gone.
- **Split here** now checks the same rules as the other Allocation changes. It refuses an Allocation that covers a day after the end date of the Person.

## [0.2.0] - 2026-11-07

### Added

- Each GitHub Release carries the build provenance of its image as `headroom-X.Y.Z.sigstore.json`.

### Changed

- **Breaking:** Headroom now uses PostgreSQL instead of SQL Server. Databases of release 0.1.0 do not upgrade. Copy them once with the script in [Move from SQL Server](docs/guides/operator-guide.md#move-from-sql-server).
- The privacy notice no longer promises a backup period. It says that the database host of the company sets it.

### Security

- Erase personal details now blanks the name and email in stored Webhook Events.
- Headroom deletes delivered and given-up Events 30 days after it creates them.
- The OpenAPI document is served in Development only. Other environments return 404.
- The consent page encodes hidden-field names. It forwards only known OAuth parameters.
- The login, first-run, consent and help pages send a Content-Security-Policy that blocks script and framing.

## [0.1.0] - 2026-11-06

### Added

- First public release.
