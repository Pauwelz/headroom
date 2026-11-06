# Test builds are checked before merge on a Test Instance that resets

A change could reach the Test Instance only after it was merged to `main` and released as an rc, with a release PR and a tag. With several agents opening PRs at the same time, that put untested changes on `main` and cost one release per check. Now a Test build of `main` plus the open PRs with the label `test-batch` is deployed to the one Test Instance, and checked before those PRs are merged. A Test build has no release PR, tag or `CHANGELOG.md` section. Its version is the `<Version>` on `main` plus `-test.<run number>`. Each Test build resets the Test Instance database and seeds it with made-up data. This reverses the earlier rule "Do not use `DevSeed` in Azure".

## Considered Options

- **Test after merge:** every merge deploys `main` to the Test Instance. It needs less machinery, but a bad agent PR lands on `main` and must be reverted.
- **One preview Instance per PR:** each needs its own database, a bootstrap and cleanup. PRs that conflict with each other pass alone and fail together.
- **Keep the database between Test builds:** a PR with a migration that is left out or changed later leaves a schema that matches no code.

## Consequences

- Data entered by hand on the Test Instance is lost on the next deploy.
- The seed runs only when Bicep sets the seed flag, which it does only for `environmentName` `test`, and only on an empty database. Production never gets the flag.
- The seeded Planner logs in with the mailbox in the `test` Environment variable `SEED_PLANNER_EMAIL`.
- A Test build never tests an upgrade from the production version with data. The rc step stays for that: reset, deploy the production version, then deploy the rc over it.
- "Checked before merge" is a convention, not a required status check. A docs-only PR needs no Test build.

Superseded in part by [ADR 0013](0013-the-public-repo-publishes-the-image-only.md).
