# Each company builds and hosts its own Instance

Each company runs its Instance in its own Azure tenant and subscription, and pays for it. Its Operator forks the repo and runs one CI workflow in the fork. The workflow builds the image into the company's own Azure Container Registry and deploys it with Bicep. We do not host Instances for companies, and we do not distribute a built image. We rejected two alternatives. Hosting all Instances in our subscription makes us responsible for the data, backups and costs of other companies. Distributing the image from our registry with a pull token per company makes us a dependency for every Instance.

## Consequences

- A company pays about EUR 16–19 per month: Azure Database for PostgreSQL Flexible Server (Burstable B1ms) plus its own ACR Basic. Before [ADR 0014](0014-instances-use-postgresql.md) it was about EUR 9 with SQL Database Basic.
- Restore, data export and deletion are the company's responsibility. The Operator guide explains them.
- Companies upgrade when they choose and can skip versions. Every older version must migrate directly to the newest, and every migration must be backward compatible with the previous release.
- Nothing provider-specific may be hard-wired where a company could choose otherwise. Login email goes through plain SMTP, not a provider API.
- The CI workflow runs on GitHub Actions only. Superseded by [ADR 0007](0007-operators-deploy-with-github-actions-only.md): it no longer stays portable to other CI systems.

Superseded by [ADR 0013](0013-the-public-repo-publishes-the-image-only.md).
