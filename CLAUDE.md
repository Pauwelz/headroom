## Agent skills

### Issue tracker

Issues live in GitHub Issues on Pauwelz/headroom, through the `gh` CLI. See `docs/agents/issue-tracker.md`.

### Triage labels

Default vocabulary: `needs-triage`, `needs-info`, `ready-for-agent`, `ready-for-human`, `wontfix`. See `docs/agents/triage-labels.md`.

### Domain docs

Single-context: one `GLOSSARY.md` and `docs/adr/` at the repo root. See `docs/agents/domain.md`.

### Tests

Run `dotnet test` locally before you push. Do not wait for CI during prototyping. See `docs/testing.md`.
