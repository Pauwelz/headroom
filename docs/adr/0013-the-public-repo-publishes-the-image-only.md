# The public repo publishes the image only

The public repo publishes one public image package, `ghcr.io/pauwelz/headroom`, and a host-neutral configuration contract (the Operator guide). It publishes no deploy template. Each Operator chooses the host: Azure Container Apps, AKS, a VNET with a VM, or anything else that runs one Linux container. The maintainer's Azure templates live in his private repo. This repo only rolls images onto the maintainer's Test Instance.

The image has these tags:

- `X.Y.Z` and `X.Y.Z-rc.N` for releases, and `latest` for a final release. `latest` is a pointer for people: a deploy always pins a version or a digest.
- `dev` and `<version>-dev.<run>` for each push to `main`. The Test Instance runs `dev`.
- `pr-<number>` and `<version>-pr.<number>.<run>` for each PR of this repo.

The package is public, so the host needs no pull secret. The app may scale to zero, and an expiring pull token would break the next cold start.

This supersedes these ADRs:

- ADR 0003 in full: a company no longer forks the repo or builds into its own registry with our Bicep. The public image replaces it. It still holds that we do not host Instances for companies.
- ADR 0007 in full: the Operator deploys with any tool. This repo has no Operator deploy workflow.
- ADR 0010 in part: the Test build of `main` plus the PRs with the label `test-batch` is gone. Each PR builds its own image, and the maintainer may deploy it to the Test Instance before merge. Each merge to `main` deploys `dev` there. The reset of the database and the seed stay.

ADR 0001 and ADR 0004 name Azure Container Apps Express as the host. That is the maintainer's host. Their decisions hold for any host that scales to zero.

## Consequences

- The csproj has no release version. `Directory.Build.props` holds the dev default `0.0.0-dev`, and a build for a tag sets the real one.
- A release is a CHANGELOG heading plus a tag on `main`. The `release` workflow publishes the image and a GitHub Release.
- Anyone can pull the image. It holds no secret or company data.
- Test images share the package with releases. The cleanup workflow deletes only untagged versions and PR or dev builds. It never deletes a release.
- An Operator who wants Bicep, Terraform or a pipeline writes their own, from the configuration contract.
