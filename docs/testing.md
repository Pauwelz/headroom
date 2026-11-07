# Testing

Run all tests before you push:

```sh
dotnet test
```

Database tests need a local Docker daemon (ADR 0005).

## The shared PostgreSQL container

All test runs on one machine share one PostgreSQL 17 container (`postgres:17`), also across worktrees and agents (ADR 0009).
The first run starts it. Later runs connect to it and do not wait for a start.
Each test creates its own database and drops it when it ends, so runs do not see each other's data.
A run that is killed can leave databases behind. The next run drops test databases older than 2 hours.

The container keeps running after the tests, until you stop it or reboot.
To stop and remove it:

```sh
docker ps -aq --filter label=headroom.test-sql | xargs -r docker rm -f
```

The next test run starts a new container.
