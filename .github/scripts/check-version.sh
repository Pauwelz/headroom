#!/usr/bin/env bash
# Usage: check-version.sh <version>
# Prints the version without a leading "v". Refuses a bad version with exit 1.
set -euo pipefail
version="${1#v}"

# Official SemVer regex from semver.org, without the build metadata group:
# container image tags cannot contain "+".
semver='^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-((?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*)(?:\.(?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*))*))?$'

printf '%s\n' "$version" | grep -Pq "$semver" \
  || { echo "Version '$version' must look like 1.2.3 or 1.2.3-rc.1 (no build metadata)." >&2; exit 1; }
echo "$version"
