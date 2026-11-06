# Database migrations

## Expand and contract

Every migration must work with the previous release. An Operator can then roll back the app without a database restore.

- Expand first. Add a column, table or index in release N. Make a new column nullable or give it a default.
- Do not remove or rename anything that release N-1 reads or writes.
- Contract later. Remove the old column or table in release N+1, after no supported release uses it.
- Move data in the migration that adds the new shape. Do not rely on app code to fill it.
- Do not edit a migration after it ships. Add a new one.

## Migration test

CI runs `MigrationUpgradeTests` on every PR. It starts SQL Server with Testcontainers.
It migrates an empty database to the oldest supported version, then to the newest.

`OldestSupportedMigration` in that test names the last migration of the oldest supported release.
Before the first release, it is the first migration.
When a release drops support for an older version, update the constant in the release PR.
Any older version upgrades directly to the newest. Only a major version may need an intermediate step.
