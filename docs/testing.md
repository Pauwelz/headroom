# Testing

Run all tests before you push:

```sh
dotnet test Headroom.slnx
```

Database tests need a local Docker daemon (ADR 0005).

## The shared SQL Server container

All test runs on one machine share one SQL Server container, also across worktrees and agents (ADR 0009).
The first run starts it. Later runs connect to it and do not wait for a start.
Each test class creates its own database, so runs do not see each other's data.
The container uses at most 2048 MB of memory.

The container keeps running after the tests, until you stop it or reboot.
To stop and remove it:

```sh
docker ps -aq --filter label=headroom.test-sql | xargs -r docker rm -f
```

The next test run starts a new container.
