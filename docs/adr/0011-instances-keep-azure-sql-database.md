# Instances keep Azure SQL Database

We checked whether PostgreSQL would be lighter on resources than SQL Server, because several agents run the tests at the same time on one machine. Instances keep Azure SQL Database. PostgreSQL is much lighter locally, but in Azure it costs each company more every month, and it saves little CI time. We fix the local load with one shared SQL Server test container instead.

## Considered Options

- **PostgreSQL in production and in tests:** measured on 2026-11-05, an idle PostgreSQL 17 container uses 21 MB RAM, and SQL Server 2022 uses 1.14 GB. Time to first query is 4.0 s against 5.5 s, and `CREATE DATABASE` takes 120–170 ms against 300–360 ms. But Azure Database for PostgreSQL Flexible Server (Burstable B1ms) costs about EUR 12–15 per month, and SQL Database Basic costs about EUR 5. That gives back the low cost from ADR 0003. A switch also needs new migrations and a data move for existing Instances.
- **PostgreSQL in tests only:** this breaks ADR 0005. Only real SQL Server runs the same migrations, queries and indexes as production.

## Consequences

- All test runs on one machine share one reused SQL Server container, capped at 2048 MB. It keeps running between test runs.
- CI time comes from the tests themselves on a 2 vCPU runner, not from the database engine or the image pull. Faster CI needs faster tests.

Superseded by [ADR 0014](0014-instances-use-postgresql.md).
