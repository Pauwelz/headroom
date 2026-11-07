# Database tests run against real PostgreSQL

Each Instance stores its data in a PostgreSQL database (ADR 0014). Tests that need a database run against PostgreSQL in one container, with a new database per test. Only real PostgreSQL runs the same migrations, queries and indexes as production. Tests without a database need no container.

## Considered Options

- **SQLite in memory:** faster and needs no Docker. But it cannot apply the PostgreSQL migrations, so it cannot check that every older version migrates directly to the newest (ADR 0003). Its types, collation and SQL also differ from PostgreSQL, so a test can pass where production fails.
- **EF Core InMemory provider:** it does not support bulk updates and deletes, and it does not enforce unique indexes. Tests would pass where production fails.

## Consequences

- Database tests need a local Docker daemon. The test run forces the local socket, so tests never start containers on a remote host.
- The first database test in a run waits for the container to start.
