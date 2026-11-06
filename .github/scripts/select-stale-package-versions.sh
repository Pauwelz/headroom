#!/usr/bin/env bash
# Usage: select-stale-package-versions.sh [open PR number]...   (the versions JSON array of the GitHub packages API on stdin)
# Prints the ids of the package versions to delete. It never selects a release.
# Delete a version only if it is untagged, or if all its tags are tags of a PR build or a dev build.
# Keep a version with the tag dev or latest, a version with the tag pr-<n> of an open PR,
# and the 10 newest versions that have a -dev. tag.
set -euo pipefail
open_prs=$(printf '%s\n' "$@" | jq -R 'select(length > 0) | tonumber' | jq -s .)
jq -r --argjson open "$open_prs" '
  def tags: .metadata.container.tags // [];
  def disposable: all(tags[]; test("^pr-[0-9]+$|-pr\\.[0-9]+\\.[0-9]+$|-dev\\.[0-9]+$"));
  def protected: any(tags[]; . == "dev" or . == "latest" or ((capture("^pr-(?<n>[0-9]+)$").n // null | if . == null then false else (tonumber | IN($open[])) end)));
  def is_dev: any(tags[]; test("-dev\\.[0-9]+$"));
  [.[] | select(disposable and (protected | not))] as $candidates
  | ([$candidates[] | select(is_dev)] | sort_by(.created_at) | reverse | .[10:] | map(.id)) as $old_dev
  | $candidates[] | select((is_dev | not) or (.id | IN($old_dev[]))) | .id' 
