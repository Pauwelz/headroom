# Operators deploy with GitHub Actions only

ADR 0003 kept the deploy workflow portable to other CI systems. We cannot test that portability, so it would break without notice. The deploy workflow now runs on GitHub Actions only and can use GitHub features, such as Environments for a Test Instance. GitHub is the default host for a fork. An Operator who hosts the fork elsewhere, for example on Gitea or Azure DevOps, writes their own deploy pipeline from the Bicep templates.

This supersedes the CI portability part of ADR 0003. The rest of ADR 0003 stays valid.

Superseded by [ADR 0013](0013-the-public-repo-publishes-the-image-only.md).
