# Hosting requirements

This guide is for the Operator of a Headroom Instance.
The Operator hosts, upgrades and backs up the Instance for a company.
An Operator is not a role in the app. The Operator does not need to be a Person in the Instance.
Terms follow [GLOSSARY.md](../../GLOSSARY.md). Planners and People read the [user guide](user-guide/README.md).

## License

Headroom uses the MIT license. See [LICENSE](../../LICENSE).
It comes with no warranty. Support is best effort.

## What you need

Headroom is one container image. It does not depend on a cloud. You choose how to host it.

- A container host that runs one Linux container. The app listens on port 8080.
  Put HTTPS termination in front of it, for example a reverse proxy or the ingress of your platform.
- PostgreSQL 17 or later, or Azure Database for PostgreSQL Flexible Server. The app creates and migrates its schema at start.
- An SMTP server. Headroom sends the login codes through it (see [Email](#email)).
- Optional telemetry: any OTLP backend or collector, or Azure Monitor through Application Insights, or both (see [Telemetry](#telemetry)).

### Image

The image is `ghcr.io/pauwelz/headroom:<version>`. It is public: it needs no pull secret.
Pin a version or a digest. `latest` is a pointer for people. Never deploy it.
[`CHANGELOG.md`](../../CHANGELOG.md) lists the released versions.

### Verify an image

Each release image has a build provenance attestation. Check it before you deploy:

```sh
gh attestation verify oci://ghcr.io/pauwelz/headroom:<version> --owner Pauwelz
```

### One replica

Run exactly one replica. Each replica runs the daily erase (`ErasureCheck.cs`).
The erase counter has no concurrency check (`PersonEraser.cs`).
Two replicas that erase at the same time can give two Persons the same "Former Person N".
The app may scale to zero when idle: the first request after that is slow (see ADR 0004).
Sending Webhook Events is safe with more replicas, because a replica claims an Event before it sends.
OAuth tokens and login cookies use the Data Protection key ring in the database, so they work on every replica.

### Configuration

ASP.NET Core reads `__` in an environment variable name as `:`.

| Setting | Value |
| --- | --- |
| `ConnectionStrings__Headroom` | The Npgsql connection string, for example `Host=<server>.postgres.database.azure.com;Database=<database>;Username=<user>;Password=<password>;SSL Mode=Require;Maximum Pool Size=20`. Keep `Maximum Pool Size` below the connection limit of the server: the Burstable B1ms tier allows 50, and Azure uses some of them. Use letters and digits only in the password: a `;` breaks the string |
| `Smtp__Host` | Host name of your SMTP server |
| `Smtp__Port` | Port, for example `587` |
| `Smtp__Username` | SMTP user name |
| `Smtp__Password` | SMTP password. Keep it secret |
| `Smtp__FromAddress` | The sender address. Your SMTP provider must allow it |
| `BaseUri` | The public URL of the Instance, with `https://` and without a path |
| `ASPNETCORE_FORWARDEDHEADERS_ENABLED` | `true`. TLS ends in front of the app, so it must trust `X-Forwarded-Proto` |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | Optional. Sends telemetry to Azure Monitor |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | Optional (also `OTEL_EXPORTER_OTLP_TRACES_ENDPOINT`, `OTEL_EXPORTER_OTLP_METRICS_ENDPOINT` or `OTEL_EXPORTER_OTLP_LOGS_ENDPOINT`). Sends telemetry to an OTLP backend or collector. Headroom reads the other standard `OTEL_EXPORTER_OTLP_*` variables too, such as `OTEL_EXPORTER_OTLP_HEADERS` and `OTEL_EXPORTER_OTLP_PROTOCOL` |
| `Seed__PlannerEmail` | Test Instances only. Never set it in production: it seeds demo data into an empty database |

The Instance uses one URL. A custom domain is your choice, as long as `BaseUri` matches it.
Passkeys are bound to the domain in `BaseUri`. If you change the domain, all Passkeys stop working.
People then log in with a Login code and save a new Passkey. Warn People before you change the domain.

### Health probe

Configure a startup, a liveness and a readiness probe on `/health`.
Give the startup probe up to 5 minutes: the .NET start can wait for a cold database.
The endpoint needs no login. It returns `200 Healthy` when the database answers, and `503 Unhealthy` when it does not.

A failed probe means that the app cannot reach the database.
The readiness probe takes the replica out of traffic. The liveness probe restarts it.
Check that the PostgreSQL server is up and that the app may connect to it. Then read the logs of your host.


### Backups

Back up the database. Backups, point-in-time restore and retention are the job of your database host.
Test a restore before you need one. Changes made after the restore time are lost.
After a restore, the app connects again by itself.

## Upgrade and rollback

1. Read [`CHANGELOG.md`](../../CHANGELOG.md) for the new version.
   Any older version upgrades directly to the newest.
   Only a major version may need an intermediate step. Its release notes say so.
2. Note the time, so that you can restore to it if the upgrade goes wrong.
3. Run the newer image.

The app applies the database migrations when it starts. You do not run them yourself.

To roll back, run the older image. This does not roll back the database schema.
Every migration works with the previous release (expand and contract, see [docs/migrations.md](../migrations.md)).
So the previous release runs on the newer schema.
To go back further than one release, restore the database to a time before the upgrade.

## First run

1. Open the URL from the deploy run, followed by `/first-run`.
   For example `https://<container app>.<environment>.westeurope.azurecontainerapps.io/first-run`.
2. Enter the company name, and the name and email of the first Planner.
3. The server sends a login code to that email. Enter the code.
4. The first Planner exists. The first-run screen closes for good.

There is no setup secret. Complete this screen right after the first start, before anyone else finds the URL.
The company name cannot change after this step.
The first run also creates a default Contract for the Planner: 8 hours, Monday to Friday, from today.

Tell the first Planner to add a second Planner on the People page.
Then one Planner can still work when the other one leaves (see [Recover a Planner](#recover-a-planner)).

If the code email fails, the screen shows "The email could not be sent. Check the SMTP settings."
Fix the SMTP parameters. Deploy again.

## Email

Login codes go out through the SMTP account that you choose.
That provider sees the email addresses and the codes. Check that its terms and location fit your company.

- Headroom uses plain SMTP (MailKit). It uses no provider API.
- The From address is `Smtp__FromAddress`. The From name is "<company name> Headroom".
- The subject is "Your Headroom login code".
- Headroom does not write email addresses or codes to the logs.
- If the provider is down, nobody can log in.

To change the SMTP settings, change the parameters.
Then deploy again with the version that you run now.

## Export

Headroom has no export feature. The Operator makes a dump with `pg_dump`.

1. Allow your IP address to connect to the PostgreSQL server (on Azure: a firewall rule of the server).

2. Export with `pg_dump`:

   ```sh
   PGSSLMODE=require PGPASSWORD='<password>' pg_dump --host <server>.postgres.database.azure.com \
     --username <user> --format custom --file headroom.dump <database>
   ```

3. Remove your IP address from the PostgreSQL server again.

The file holds personal data. Store it with care.

A Planner answers an access request for one Person by hand.
A request for full erasure, including Time off, is also handled by hand.
Headroom erases personal details automatically 12 months after the end date.
Your database host sets how long backups keep data.

## Delete the Instance

Remove the container and the database. [Export](#export) first if you need the data.
Backups of your database host keep the data until their retention ends. Delete them too.

## Recover a Planner

The app keeps at least one Planner who can log in.
But two Planners can both have a future end date.
After those dates, nobody can log in and nobody can change the Instance.
Then the Operator sets the Planner flag and clears the end date in the database.

The columns `IsPlanner` and `EndDate` are in the table `People`.
An erased Person has no email and cannot log in. Choose a Person who is not erased.

1. Allow your IP address to connect to the PostgreSQL server.
2. Connect to the database `<database>` with an account that can write to it.
   Use `psql` or any other client.

   ```sh
   PGSSLMODE=require PGPASSWORD='<password>' psql --host <server>.postgres.database.azure.com --username <user> --dbname <database>
   ```

3. Find the Person:

   ```sql
   SELECT "Id", "Name", "Email", "IsPlanner", "EndDate" FROM "People" WHERE "Email" = 'person@example.com';
   ```

4. Set the flag and clear the end date:

   ```sql
   UPDATE "People"
   SET "IsPlanner" = true, "EndDate" = NULL, "DateModified" = now(), "LastChangedBy" = NULL
   WHERE "Email" = 'person@example.com';
   ```

   `"LastChangedBy" = NULL` marks the change as a system change.
5. Check that one row changed. Remove your IP address again.
6. Ask the Person to log in. The Person can now give the Planner flag to others.

The Person keeps their Contracts. A Person with no Contract from today still logs in, but has no Capacity.

## Move from SQL Server

Release 0.1.0 used SQL Server. Databases of that release cannot upgrade to PostgreSQL.
A one-time script copies the data. It needs the .NET 10 SDK.
The script empties the target tables, except `__EFMigrationsHistory`.
It copies every table in foreign-key order in one transaction, and converts times to UTC.

1. Stop the old app. Scale it to zero.
2. Create the PostgreSQL database.
3. Deploy the new version against the PostgreSQL database. Let it start once, so it creates the schema.
4. Stop the app again.
5. Run the script from the repo root:

   ```sh
   dotnet run scripts/migrate-sqlserver-to-postgres.cs -- "<SQL Server connection string>" "<PostgreSQL connection string>"
   ```

6. Start the app.
7. Log in and check the data. Delete the old SQL database when you are sure.

## Telemetry

Headroom can send traces, metrics and logs with OpenTelemetry. You choose the target:

- Any OTLP backend or collector: set `OTEL_EXPORTER_OTLP_ENDPOINT`. Set `OTEL_EXPORTER_OTLP_HEADERS` and `OTEL_EXPORTER_OTLP_PROTOCOL` if the target needs them.
- Azure Monitor: set `APPLICATIONINSIGHTS_CONNECTION_STRING` to the connection string of an Application Insights resource.
- Both together.

Without either setting, Headroom sends nothing. The data goes only to the targets that you set. Headroom sends it nowhere else.
The filter for personal data (below) runs before every exporter.
Database spans exist with Azure Monitor only.
Each MCP call gets its own span with the tool name, such as `get_schedule`.

The telemetry holds one personal value: the Person id, as `enduser.id` on each request of a logged-in Person.
Azure Monitor shows it as `user_AuthenticatedId`. Only your database links it to a name or an email.
Set the retention of your telemetry with this in mind. It holds no other personal data:
- Requests show the route template, such as `/api/people/{id}`. They show no path with ids and no query string.
- Database calls show no statement text.
- Headroom adds no name or email to spans, logs or log scopes.
- Headroom does not log Login codes or email addresses.

Headroom exports every trace. It does not sample. If the volume grows, add tail-based sampling in an OpenTelemetry Collector.

### Metrics

Headroom exports these metrics under the meter name `Headroom`. They hold no Person id, name or email.
In Application Insights, a metric is a row in `customMetrics` with this name. Each dimension is a key in `customDimensions`.

| Metric | Kind | Dimension and values |
|---|---|---|
| `headroom.logins` | Counter | `result`: `success`, `failed` |
| `headroom.login_code_emails` | Counter | `result`: `sent`, `failed` |
| `headroom.record_changes` | Counter | `type`: `headroom.Allocation.created`, `headroom.Allocation.changed`, `headroom.Allocation.deleted`, the same three for `headroom.TimeOff`, and `headroom.Project.changed` |
| `headroom.requests` | Counter | `surface`: `api`, `mcp` |
| `headroom.webhook_deliveries` | Counter | `result`: `success`, `failed` |
| `headroom.events.backlog` | Gauge | none |

`headroom.events.backlog` is the number of Events that are not delivered and not older than 24 hours.
The request counter counts every call to `/api` and `/mcp`, so it also counts the 401 and 404 answers.
A Webhook delivery counts each attempt, so a retry counts again.

### Share a result in a GitHub issue

Telemetry holds no personal data (see above), but check before you share (ADR 0012).
Copy the result of a query as text, or take a screenshot.
Check that it shows no email address, name or Login code. Remove anything else that identifies a Person or a client. Then paste it in the issue.
Do not share the connection string.
