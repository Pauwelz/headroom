# Issue tracker: GitHub

Issues and specs for this repo live as GitHub issues in `Pauwelz/headroom`. Use the `gh` CLI for all operations.

## Conventions

- **Create an issue**: `gh issue create --title "<title>" --body "..."`. Use `--body-file <path>` for multi-line bodies.
- **Read an issue**: `gh issue view <number>`. Add `--comments` to include comments.
- **Search issues**: `gh issue list --search "<query>"`.
- **Comment on an issue**: `gh issue comment <number> --body "..."`.
- **Edit labels**: `gh issue edit <number> --add-label "<label>" --remove-label "<label>"`.
- **Edit the milestone**: `gh issue edit <number> --milestone "<title>"`.
- **Close**: `gh issue close <number>`.
- **Merge a PR**: `gh pr merge <number> --squash`. The repo does not allow merge commits.
- **Dependencies**: `gh api` with the REST endpoints below.

Run `gh issue <command> --help` for exact flags. `gh` infers the repo from the git remote `origin`. Use `-R Pauwelz/headroom` to be explicit.

## Pull requests as a triage surface

**PRs as a request surface: yes.** _(Set to `no` to keep external PRs out of `/triage`; `/triage` reads this flag.)_

PRs run through the same labels and states as issues, using the `gh pr` equivalents:

- **Read a PR**: `gh pr view <number>`, then `gh pr view <number> --comments` (off a TTY, `--comments` prints only the comments, not the body), and `gh pr diff <number>` for the diff.
- **List external PRs for triage**: `gh api --paginate 'repos/{owner}/{repo}/pulls?state=open' --jq '.[] | select(.author_association | IN("OWNER","MEMBER","COLLABORATOR") | not) | {number, title, author: .user.login, author_association, labels: [.labels[].name]}'`.
- **Comment / label / close**: `gh pr comment`, `gh pr edit --add-label`/`--remove-label`, `gh pr close`.

GitHub shares one number space across issues and PRs, so a bare `#42` may be either: resolve with `gh pr view 42` and fall back to `gh issue view 42`.

## When a skill says "publish to the issue tracker"

Create a GitHub issue with `gh issue create`.

## When a skill says "fetch the relevant ticket"

Run `gh issue view <number> --comments`.

## Dependencies

GitHub has native issue dependencies ("blocked by" and "blocking"). The REST endpoints take the issue number in the path:

- List blockers: `gh api repos/{owner}/{repo}/issues/<n>/dependencies/blocked_by`
- Add a blocker: `gh api -X POST repos/{owner}/{repo}/issues/<n>/dependencies/blocked_by -F issue_id=<blocker id>`
- Remove a blocker: `gh api -X DELETE repos/{owner}/{repo}/issues/<n>/dependencies/blocked_by/<blocker id>`
- List what an issue blocks: `gh api repos/{owner}/{repo}/issues/<n>/dependencies/blocking`

`{owner}` and `{repo}` expand from the current repo. `issue_id` is the numeric database id, not the issue number. Get it with `gh api repos/{owner}/{repo}/issues/<blocker> --jq .id`. Use `-F` (not `-f`) so the id is sent as an integer.

Not confirmed: the repo has no issues yet, so no call ran against a real issue. The paths, `issue_id` body field and DELETE form come from the GitHub REST docs as recalled, not from a live call. Check them with a read-only call on the first issue.

## Wayfinding operations

GitHub has native dependencies and milestones. Sub-issues exist too, but the map uses a label and a milestone instead.

- **Map**: an issue with the label `wayfinder:map`, plus a milestone with the same title as the map.
- **Child ticket**: an issue in the map's milestone. Its body starts with `Map: #<map>`.
- **Unblocked**: every issue in the `blocked_by` list is closed.
- **Frontier**: open issues in the milestone that have no assignee and no open blockers.

### Command reference

| Step | Command |
| --- | --- |
| Create the type labels (once per repo) | `gh label create wayfinder:<type> --color <rrggbb> --description "<description>"` for `map`, `research`, `prototype`, `grilling`, `task` |
| List labels | `gh label list` |
| Create the map milestone | `gh api repos/{owner}/{repo}/milestones -f title="<map title>" -f description="<text>"` |
| Show milestone progress | `gh api repos/{owner}/{repo}/milestones --jq '.[] \| select(.title=="<map title>") \| {open_issues, closed_issues}'` |
| Create the map or a ticket | `gh issue create --title "<title>" --body-file <file> --label wayfinder:<type> --milestone "<map title>"` |
| Put a ticket in the map | `gh issue edit <n> --milestone "<map title>"` |
| Add the type label | `gh issue edit <n> --add-label wayfinder:<type>` |
| Wire blocking | `gh api -X POST repos/{owner}/{repo}/issues/<blocked>/dependencies/blocked_by -F issue_id=<blocker id>` |
| Check blocking | `gh api repos/{owner}/{repo}/issues/<blocked>/dependencies/blocked_by --jq '.[] \| "#\(.number) \(.state)"'` |
| List the map's tickets | `gh issue list --milestone "<map title>" --state all` (add `--label wayfinder:<type>` to filter) |
| Check the claim | `gh issue view <n> --json assignees --jq '.assignees[].login'` |
| Claim a ticket | `gh issue edit <n> --add-assignee @me` |
| Read a ticket | `gh issue view <n> --comments` |
| Record the resolution | `gh issue comment <n> --body-file <file>`, then `gh issue close <n>` |
| Write the map body | `gh issue edit <map> --body-file <file>` |
| Read the raw map body | `gh issue view <map> --json body --jq .body` |

`gh issue view` returns the raw Markdown body with `--json body`. No API workaround is needed.

### Frontier query

```bash
MAP="<map title>"
for n in $(gh issue list --milestone "$MAP" --state open --json number,assignees \
    --jq '.[] | select(.assignees | length == 0) | .number'); do
  open=$(gh api "repos/{owner}/{repo}/issues/$n/dependencies/blocked_by" --jq '[.[] | select(.state=="open")] | length')
  [ "$open" = 0 ] && echo "#$n"
done
```
