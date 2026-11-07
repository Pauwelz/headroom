# Contributing

Thank you for your interest. Read the [Code of Conduct](CODE_OF_CONDUCT.md) first.
Terms follow [GLOSSARY.md](GLOSSARY.md).

## Build and test

1. Install the .NET SDK that the project uses.
2. Run `dotnet build`.
3. Run `dotnet test`. The database tests need Docker, because they start a real PostgreSQL.

See [docs/testing.md](docs/testing.md). Run `scripts/preview.sh` to try the app with demo data.

## Pull requests

1. Open or pick an issue. Ask first before you start a large change.
2. Branch from `main`. Make small commits.
3. Open a PR. Wait for the checks to pass.
4. Write the PR title as a [Conventional Commit](https://www.conventionalcommits.org/), for example `fix: keep the end date`. The maintainer squash-merges, and the title becomes the commit message.
5. Sign your commits. The repo requires signed commits.

## What each change needs

- A line under `## [Unreleased]` in [CHANGELOG.md](CHANGELOG.md) for each user-visible change. CI checks this. A change that is not user-visible gets the label `no-changelog`.
- An update of the [user guide](docs/guides/user-guide/README.md) with each UI or behaviour change.
- A migration that follows [docs/migrations.md](docs/migrations.md), if the database changes.

## Questions and security

- Ask questions in [Discussions](https://github.com/Pauwelz/headroom/discussions).
- Report a vulnerability privately. Do not open a public issue. See [SECURITY.md](SECURITY.md).

The maintainers release by [docs/releasing.md](docs/releasing.md).
