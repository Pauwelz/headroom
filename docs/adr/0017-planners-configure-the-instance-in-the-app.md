# Planners configure the Instance in the app

Planners set the company name, the email server (SMTP) and the two telemetry privacy switches in Settings. Headroom stores them in the database. The environment holds only what the app needs before it can read the database, or what belongs to the Operator: the connection string, `BaseUri`, forwarded headers, logging and the telemetry exporters. A company can then change its email server without a redeploy. A Planner acts as the administrator of the Instance.

## Considered Options

- **Everything stays in environment variables:** each change of the email server needs the Operator and a redeploy.
- **The telemetry exporters move too:** the OpenTelemetry SDK registers exporters at startup, so each change needs a restart. Headroom would read the database before the migrations run, and telemetry would fail when the database fails. A Planner could also send Person ids and IP addresses to any endpoint, against [ADR 0012](0012-telemetry-stays-in-the-company-subscription.md).
- **A setup secret for the first-run screen:** it stops strangers from setting SMTP fields before setup, but it adds an environment variable again.

## Consequences

- The first-run screen asks for the email server before it sends the Login code. The code email tests the settings. The step is skipped when `Smtp__Host` is set, and in Development.
- Before setup, anyone who reaches the first-run screen can make the server connect to any host and port. We accept this, as with the screen itself: it is open only until setup ends. The error shows no reply from the server.
- The SMTP password is encrypted with the Data Protection key ring and never shown again. The key ring is in the same database, so this does not protect a full database dump.
- Headroom saves new SMTP settings only after a test email to the Planner succeeds. To recover from broken settings, the Operator clears them in the database.
- The database value wins. `Smtp__*` and `Telemetry__*` stay as a fallback for existing Instances, and a later release removes them.
- This supersedes in part [ADR 0016](0016-telemetry-holds-the-person-id.md): a Planner switches the Person id and the client IP address off in Settings.
