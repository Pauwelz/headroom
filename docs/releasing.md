# Releasing

This repo builds and publishes Headroom. It deploys only the Test Instance of the maintainer.
Operators host production themselves (see the [Hosting requirements](guides/operator-guide.md)). The maintainer's own production deploy lives in a private repo.

The cycle:

1. A PR builds the image `headroom:pr-<number>`.
2. You may deploy that image to the Test Instance and check it. Then you merge the PR.
3. The merge to `main` builds `headroom:dev` and deploys it to the Test Instance.
4. A release PR changes the CHANGELOG. You tag `main`.
5. The tag publishes `headroom:X.Y.Z`, `headroom:latest` and a GitHub Release.
6. You check the upgrade on the Test Instance.
7. The private repo rolls `headroom:<version>` to production, by digest.

The decisions are in ADR 0010 and [ADR 0013](adr/0013-the-public-repo-publishes-the-image-only.md).

## Images

All images are in one public package: `ghcr.io/<owner>/headroom`.

| Tags | Built by | Meaning |
| --- | --- | --- |
| `X.Y.Z`, `X.Y.Z-rc.N` | the workflow `release`, for each tag | A release. Never moves |
| `latest` | the workflow `release`, for a final version only | A pointer for people. A deploy never uses it |
| `dev`, `<version>-dev.<run>` | the workflow `dev`, for each push to `main` | `dev` moves to the newest build. The other tag never moves |
| `pr-<number>`, `<version>-pr.<number>.<run>` | the workflow `pr-image`, for each PR of this repo | `pr-<number>` moves to the newest build. The other tag never moves |

`<version>` is the latest tag on `main`, without `v` (`0.0.0` without a tag).
Forks get no image: their token is read-only.
The workflow `cleanup-test-images` runs every week. It deletes untagged versions and old PR and dev builds.
It never deletes a release, `dev`, `latest`, the image of an open PR, or the 10 newest dev builds.

## Check a PR on the Test Instance

The workflow `pr-image` runs next to the tests. A PR that changes only docs also builds an image, but needs no check.

1. Open the PR, or push to it. Wait until the checks and the `image` job are green. Deploy a PR only after its checks are green.
   The job summary shows the image and its version, for example `0.1.0-pr.12.3`.
2. Start the `deploy-test` workflow by hand from `main`. Select `pr` as `image` and enter the PR number.
3. Check the Test Instance by hand. Log in as the Planner in `SEED_PLANNER_EMAIL`. The Help page shows the version of the build.
4. Merge the PR if it passed.

`deploy-test` builds nothing. It resolves the tag to a digest, recreates the database empty, and starts a new revision with the image. The app migrates and seeds the database at start.
Each deploy deletes all data on the Test Instance. Do not enter data that you want to keep.
"Check before merge" is a rule for you to follow. GitHub does not enforce it.

## The dev image

Every push to `main` starts the workflow `dev`. It builds `headroom:dev` and deploys it to the Test Instance.
The database is reset and seeded.
The deploy uses the version tag of that build, not the moving tag `dev`, so a later push cannot change what a run deploys.
Start `deploy-test` with `dev` as `image` to deploy the newest `dev` again.

## Release

1. Open a PR that changes `CHANGELOG.md` only.
   Rename `## [Unreleased]` to `## [X.Y.Z] - YYYY-MM-DD`. Add a new empty `## [Unreleased]` above it.
   The workflow `test` does not run for this PR. The workflow `ci` does.
2. Merge the PR (squash).
3. Tag the merge commit on `main` and push the tag:
   `git tag -a vX.Y.Z -m vX.Y.Z && git push origin vX.Y.Z`.
   Use `vX.Y.Z-rc.N` for a release candidate. It becomes a pre-release on GitHub.
   An rc needs no CHANGELOG section: its notes are the `[Unreleased]` section.

The `release` workflow starts on the tag. It:

1. checks that the commit of the tag is on `main`.
   It checks that the tag is `vX.Y.Z` or `vX.Y.Z-rc.N`. Other pre-release shapes are refused.
   For a final version, it also checks that `CHANGELOG.md` has a line that starts with `## [X.Y.Z]`;
2. builds the image and pushes it as `X.Y.Z`, and for a final version also as `latest`;
3. attests the build provenance of the image. The attestation stays in the attestation store of GitHub, not in the registry;
4. creates the GitHub Release with the CHANGELOG section as notes. It has no assets.

Anyone can check an image: `gh attestation verify oci://ghcr.io/pauwelz/headroom:X.Y.Z --owner Pauwelz`.

The csproj has no release version. `Directory.Build.props` holds the dev default `0.0.0-dev`.
A build for a tag sets the real version from the tag.
A tag that fails a check creates nothing. Delete the tag, fix the cause and tag again.

## Check the upgrade on the Test Instance

Do this before you deploy the release to production.

1. Start `deploy-test`. Select `version` as `image` and enter the previous release.
   Leave `keep-data` off. The database is reset and seeded.
2. Open the Test Instance and log in. This starts the app, and the app migrates and seeds the database.
3. Start `deploy-test` again. Enter the new version and turn `keep-data` on. The app upgrades the data.
4. Check the Test Instance by hand: log in, open the Schedule and check the data.

This deploys the image that the release published. It builds nothing.
If the check finds a fault, fix it and release `X.Y.Z-rc.N+1`.

## Production

The private repo of the maintainer rolls `headroom:<version>` to production, by digest.
Other Operators do the same on their own host (see the [Hosting requirements](guides/operator-guide.md)).
The workflows of this repo cannot deploy production. An rc is for the Test Instance only.

The order is a rule for you to follow. No workflow enforces that a version ran on the Test Instance first.

## Reset and seed of the Test Instance

`deploy-test` deletes the Test Instance database before each deploy, unless `keep-data` is on.
The reset step checks the variable `ENVIRONMENT_NAME` (it must be `test`) and the database name.

The app seeds an empty database with demo data when the setting `Seed:PlannerEmail` is set.
Only the Test Instance sets the setting. Production never sets it.
The seeded Planner uses the address in `Seed__PlannerEmail`, so login codes reach a real mailbox.
The other seeded People use addresses at `example.test`.

Rules for migrations are in `docs/migrations.md`.

## One-time setup of the Test Instance

The Test Instance is a Container App and a SQL database in an Azure subscription of the maintainer.
The private repo creates them and sets the app settings (see the configuration table in the Hosting requirements),
including `Seed__PlannerEmail`. This repo only rolls images onto them.

1. In the repository, create the Environment `test`. Select **Selected branches and tags** and add the rule `main` only. Add no required reviewers.
   The workflows `dev` and `deploy-test` run from `main`.
2. Create a federated credential for the Environment `test` on an identity that may update the Container App and recreate the database.
   The subject ends in `:environment:test`. Read the exact value from the error of the first failed sign-in.
3. Set these variables in the `test` Environment: `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`,
   `AZURE_RESOURCE_GROUP`, `CONTAINER_APP`, `SQL_SERVER`, `SQL_DATABASE` and `ENVIRONMENT_NAME` = `test`.
   The name in `SQL_DATABASE` must contain `-test-`. The reset step refuses other names.
4. After the first image is pushed, set the visibility of the GHCR package `headroom` to public.
   `deploy-test` signs in nowhere, so it needs a public package.
   Give this repo the role Admin on the package (Manage Actions access), so `cleanup-test-images` can delete versions.
5. Run `deploy-test` with `dev`. Then run the upgrade check above.
