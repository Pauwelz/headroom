#!/usr/bin/env bash
# Runs select-stale-package-versions.sh on sample JSON. Fails if the selection is wrong.
cd "$(dirname "$0")" || exit 1
fail=0
# v <id> <created day> <tag>...   prints one version object
v() { local id=$1 day=$2; shift 2; printf '{"id":%s,"created_at":"2026-01-%sT00:00:00Z","metadata":{"container":{"tags":%s}}}' "$id" "$day" "$(printf '%s\n' "$@" | jq -R 'select(length > 0)' | jq -sc .)"; }
# expect <description> <expected ids, space separated> <open PRs> <versions...>
expect() {
  local what=$1 want=$2 open=$3; shift 3
  local json got
  json="[$(IFS=,; echo "$*")]"
  # shellcheck disable=SC2086
  got=$(printf '%s' "$json" | ./select-stale-package-versions.sh $open | sort -n | xargs)
  [ "$got" = "$want" ] || { echo "FAIL: $what: want '$want', got '$got'" >&2; fail=1; }
}
expect "a release is never selected" "" "" "$(v 1 01 1.2.3)" "$(v 2 02 1.2.3-rc.1)" "$(v 3 03 1.2.3 latest)"
expect "untagged and PR builds go" "1 2 3" "" "$(v 1 01)" "$(v 2 02 pr-5 0.1.0-pr.5.7)" "$(v 3 03 0.1.0-pr.6.2)"
expect "dev and latest stay" "" "" "$(v 1 01 dev 0.1.0-dev.9)" "$(v 2 02 latest)"
expect "pr tag of an open PR stays" "3" "5 7" "$(v 1 01 pr-5 0.1.0-pr.5.7)" "$(v 2 02 pr-7)" "$(v 3 03 pr-8)"
expect "a mixed version stays" "" "" "$(v 1 01 0.1.0-pr.5.7 1.0.0)"
dev=""; ids=""
for i in $(seq 1 12); do dev="$dev|$(v "$i" "$(printf '%02d' "$i")" "0.1.0-dev.$i")"; done
IFS='|' read -r -a devs <<< "${dev#|}"
expect "the 10 newest dev builds stay" "1 2" "" "${devs[@]}"
[ "$fail" -eq 0 ] && echo "All selection checks passed."
exit "$fail"
