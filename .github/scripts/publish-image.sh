#!/usr/bin/env bash
# Usage: publish-image.sh <version> <tag>...   for example: publish-image.sh 1.2.3 1.2.3 latest
# Builds the image with the .NET SDK, pushes it to ghcr.io/<owner>/headroom with the tags, and prints its digest.
# The environment needs SDK_CONTAINER_REGISTRY_UNAME and SDK_CONTAINER_REGISTRY_PWORD (GHCR sign-in).
set -euo pipefail
version="${1:?version is required}"
shift
[ $# -gt 0 ] || { echo "At least one tag is required." >&2; exit 1; }
repository="${GITHUB_REPOSITORY_OWNER,,}/headroom"
tags=$(IFS=';'; echo "$*")
dotnet publish src/Headroom --configuration Release /t:PublishContainer \
  -p:ContainerRegistry=ghcr.io -p:ContainerRepository="$repository" \
  -p:ContainerImageTags="\"$tags\"" -p:Version="$version" >&2
echo "$SDK_CONTAINER_REGISTRY_PWORD" | docker login ghcr.io --username "$SDK_CONTAINER_REGISTRY_UNAME" --password-stdin >&2
docker buildx imagetools inspect "ghcr.io/$repository:$1" --format '{{.Manifest.Digest}}'
