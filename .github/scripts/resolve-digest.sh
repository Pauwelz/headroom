#!/usr/bin/env bash
# Usage: resolve-digest.sh <repository> <tag>   for example: resolve-digest.sh pauwelz/headroom 1.2.3
# Prints the digest (sha256:...) of ghcr.io/<repository>:<tag>. It signs in nowhere: the package must be public.
set -euo pipefail
repository="${1:?repository is required}"
tag="${2:?tag is required}"
fail() { echo "Cannot resolve ghcr.io/$repository:$tag. The image does not exist, or the package is not public." >&2; exit 1; }
token=$(curl -fsSL "https://ghcr.io/token?scope=repository:$repository:pull" | jq -er .token) || fail
accept="application/vnd.oci.image.index.v1+json,application/vnd.docker.distribution.manifest.list.v2+json,application/vnd.oci.image.manifest.v1+json,application/vnd.docker.distribution.manifest.v2+json"
digest=$(curl -fsSI -H "Authorization: Bearer $token" -H "Accept: $accept" "https://ghcr.io/v2/$repository/manifests/$tag" \
  | tr -d '\r' | awk 'tolower($1) == "docker-content-digest:" { print $2 }') || fail
[[ "$digest" == sha256:* ]] || fail
echo "$digest"
