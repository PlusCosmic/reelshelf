#!/usr/bin/env bash
# Print the monitor's version for the checked-out commit: MAJOR.MINOR from desktop/monitor/Cargo.toml, PATCH = the
# number of commits reachable from HEAD that touch what the app is built from (PATHS below).
#
# The patch counts only those commits, not the whole repository's, so it moves only when the app could have changed,
# and it only ever grows: every workflow computes the same number for the same commit with no stored counter. Bump
# MAJOR.MINOR in desktop/monitor/Cargo.toml by hand for a release people should notice; the patch carries on counting.
#
# Needs full history: in GitHub Actions, check out with fetch-depth: 0.
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/../../.."

# The monitor builds from the whole Cargo workspace (it depends on reelshelf-app, reelshelf-api and reelshelf-core)
# and is released by publish-monitor.yml. Keep this list and the workflow's `paths` the same.
PATHS=(desktop .github/workflows/publish-monitor.yml)

base=$(sed -n 's/^version = "\([0-9][0-9]*\.[0-9][0-9]*\)\..*"$/\1/p' desktop/monitor/Cargo.toml | head -1)
[ -n "$base" ] || { echo "no MAJOR.MINOR version in desktop/monitor/Cargo.toml" >&2; exit 1; }

if [ "$(git rev-parse --is-shallow-repository)" = "true" ]; then
  echo "shallow clone: the commit count would be wrong (fetch full history)" >&2
  exit 1
fi
echo "$base.$(git rev-list --count HEAD -- "${PATHS[@]}")"
