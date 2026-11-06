#!/usr/bin/env bash
# Runs check-version.sh on a table of examples. Fails if one result is wrong.
cd "$(dirname "$0")" || exit 1
fail=0
# check <version> ok <stripped version>  |  check <version> refuse
check() {
  local out status=0
  out=$(./check-version.sh "$1" 2>/dev/null) || status=$?
  if [ "$2" = ok ] && { [ "$status" -ne 0 ] || [ "$out" != "$3" ]; }; then
    echo "FAIL: '$1' should give '$3', got status $status and '$out'" >&2; fail=1
  elif [ "$2" = refuse ] && [ "$status" -eq 0 ]; then
    echo "FAIL: '$1' should be refused" >&2; fail=1
  fi
}
check 1.2.3 ok 1.2.3
check v1.2.3 ok 1.2.3
check 1.2.3-rc.1 ok 1.2.3-rc.1
check 0.1.0-pr.12.3 ok 0.1.0-pr.12.3
check 0.1.0-dev.57 ok 0.1.0-dev.57
for v in 1.2.3+abc 01.2.3 1.2 1.2.3- 1.2.3-01; do check "$v" refuse; done
[ "$fail" -eq 0 ] && echo "All version checks passed."
exit "$fail"
