# Instances use PostgreSQL

Instances use PostgreSQL instead of SQL Server. This supersedes [ADR 0011](0011-instances-keep-azure-sql-database.md). The SQL Server test container loads the developer machine too much, because several agents run tests at the same time. On 2026-11-07 the container held 299 leftover test databases. While no test ran, it used about 70% of one core and 1.75 GB of memory. Idle, PostgreSQL 17 uses 21 MB, and SQL Server 2022 uses 1.14 GB (measured on 2026-11-05 in ADR 0011).

## Considered Options

- **Keep SQL Server and only fix the test cleanup:** it costs each company less in Azure, because SQL Database Basic costs about EUR 5 per month. But the local engine stays heavy. Time to first query is 5.5 s against 4.0 s, and `CREATE DATABASE` takes 300–360 ms against 120–170 ms.
- **PostgreSQL in tests only:** this breaks ADR 0005. Only the production engine runs the same migrations, queries and indexes as production.

## Consequences

- Each company pays more. Azure Database for PostgreSQL Flexible Server (Burstable B1ms) costs about EUR 12–15 per month, instead of about EUR 5. The total rises from about EUR 9 to about EUR 16–19 per month. This weakens the low monthly cost of ADR 0003.
- `InitialCreate` is regenerated for PostgreSQL. Databases of release 0.1.0 use SQL Server and cannot upgrade. Operators move them once with `scripts/migrate-sqlserver-to-postgres.cs` (see the Operator guide).
- Email (of a Person and of a Login code), Client name and Project name compare case-insensitively through a nondeterministic ICU collation. This replaces the default collation of SQL Server.
- The Test Instance needs an Azure Database for PostgreSQL Flexible Server. It has no rename, so the reset deletes the database and creates it again.
- All test runs on one machine share one reused PostgreSQL 17 test container, with no memory cap. Each test drops its database when it ends.
