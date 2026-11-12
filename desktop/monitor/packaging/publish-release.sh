#!/usr/bin/env bash
# Publish the monitor's disk image to the GitHub Release of a version: create the release with it if it does not
# exist yet, otherwise attach the files it lacks.
#
#   desktop/monitor/packaging/publish-release.sh VERSION FILE...
#
# The release is tagged monitor-vVERSION at the commit built and titled "Reelshelf Monitor VERSION". The monitor is the
# only thing in this repository published to GitHub Releases (the uploader goes to the Microsoft Store from its own
# uploader-v* tags, which nothing here creates or reads), so its newest release is marked Latest. A re-run finds its
# files already attached and publishes nothing.
#
# Needs gh with GH_TOKEN (contents: write), and GITHUB_REPOSITORY, GITHUB_SHA and GITHUB_SERVER_URL as GitHub Actions
# sets them.
set -euo pipefail

if [ "$#" -lt 2 ]; then
  echo "usage: $0 VERSION FILE..." >&2
  exit 2
fi
version=$1
shift
tag="monitor-v$version"
repo=$GITHUB_REPOSITORY

if ! gh release view "$tag" --repo "$repo" > /dev/null 2>&1; then
  short=$(echo "$GITHUB_SHA" | cut -c1-7)
  notes="Reelshelf Monitor ${version}, built from \`main\` at [${short}](${GITHUB_SERVER_URL}/${repo}/commit/${GITHUB_SHA})."
  gh release create "$tag" "$@" \
    --repo "$repo" \
    --target "$GITHUB_SHA" \
    --title "Reelshelf Monitor ${version}" \
    --notes "$notes" \
    --latest
  echo "Created release $tag with $# file(s)."
  exit 0
fi

attached=$(gh release view "$tag" --repo "$repo" --json assets --jq '.assets[].name')
missing=()
for file in "$@"; do
  if grep -qxF -- "$(basename "$file")" <<< "$attached"; then
    echo "Release $tag already has $(basename "$file")."
  else
    missing+=("$file")
  fi
done
if [ "${#missing[@]}" -eq 0 ]; then
  echo "Release $tag already has every file (a re-run); nothing to publish."
  exit 0
fi
gh release upload "$tag" "${missing[@]}" --repo "$repo"
echo "Attached ${#missing[@]} file(s) to release $tag."
