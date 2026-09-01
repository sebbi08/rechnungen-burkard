#!/usr/bin/env bash
# Start the Release workflow from here instead of the "Run workflow" button in
# the GitHub UI.
#
#   scripts/release.sh                release the checked-out branch
#   scripts/release.sh --ref main     release another branch
#   scripts/release.sh --yes          no confirmation prompt
#   scripts/release.sh --no-watch     dispatch and return instead of following the run
#
# The workflow releases what GitHub has, not what is in the working tree: it
# checks out origin/<ref>, derives the version from the commits since the last
# tag, pushes the changelog commit back to that branch and cuts the tag. So the
# preview below is taken from a throwaway worktree at origin/<ref>, and a
# branch with nothing releasable on it stops the script here rather than in
# next-version.sh a minute into a run that would build nothing.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "${REPO_ROOT}"

WORKFLOW="release.yml"

usage() {
  cat <<'EOF'
usage: scripts/release.sh [--ref <branch>] [--yes] [--no-watch]

  --ref <branch>  branch to release (default: the checked-out branch)
  -y, --yes       start the workflow without asking
  --no-watch      dispatch and return instead of following the run
EOF
}

ref=""
assume_yes=0
watch=1

while [ $# -gt 0 ]; do
  case "$1" in
    --ref)
      ref="${2:-}"
      [ -n "$ref" ] || { echo "--ref needs a branch name" >&2; exit 2; }
      shift 2
      ;;
    --ref=*) ref="${1#--ref=}"; shift ;;
    -y | --yes) assume_yes=1; shift ;;
    --no-watch) watch=0; shift ;;
    -h | --help) usage; exit 0 ;;
    *) echo "unknown argument: $1" >&2; usage >&2; exit 2 ;;
  esac
done

command -v gh > /dev/null 2>&1 || {
  echo "gh is not installed -- see https://cli.github.com" >&2
  exit 1
}
gh auth status > /dev/null 2>&1 || {
  echo "gh is not logged in -- run: gh auth login" >&2
  exit 1
}

if [ -z "$ref" ]; then
  ref=$(git symbolic-ref --quiet --short HEAD) || {
    echo "HEAD is detached -- name the branch to release with --ref" >&2
    exit 1
  }
fi

echo "fetching origin ..."
git fetch --quiet --tags origin

git rev-parse --verify --quiet "refs/remotes/origin/${ref}" > /dev/null || {
  echo "origin/${ref} does not exist -- push the branch first" >&2
  exit 1
}

remote_sha=$(git rev-parse "origin/${ref}")
local_sha=$(git rev-parse --verify --quiet "refs/heads/${ref}" || true)
if [ -n "$local_sha" ] && [ "$local_sha" != "$remote_sha" ]; then
  ahead=$(git rev-list --count "origin/${ref}..refs/heads/${ref}")
  behind=$(git rev-list --count "refs/heads/${ref}..origin/${ref}")
  echo "note: local ${ref} is ${ahead} ahead and ${behind} behind origin/${ref};"
  echo "      the release is cut from origin/${ref}."
fi

# The version and the changelog come out of the scripts as they exist on the
# branch being released, run against that branch's history -- the same thing
# the workflow will do. A detached worktree is the cheap way to get both.
preview=$(mktemp -d)
cleanup() {
  git worktree remove --force "$preview" > /dev/null 2>&1 || rm -rf "$preview"
}
trap cleanup EXIT
git worktree add --quiet --detach "$preview" "origin/${ref}"

if ! version=$(cd "$preview" && scripts/next-version.sh 2>&1); then
  echo "${version}" >&2
  echo "nothing to release on origin/${ref}" >&2
  exit 1
fi
entry=$(cd "$preview" && scripts/changelog-entry.sh "$version")

# The release workflow builds and pushes the image without running the tests
# again, so the checks CI left on the commit are all there is to go on.
checks="could not be read"
if counts=$(gh api "repos/{owner}/{repo}/commits/${remote_sha}/check-runs" \
  --jq '"\(.check_runs | length) \([.check_runs[] | select(.status != "completed")] | length) \([.check_runs[] | select(.conclusion == "failure" or .conclusion == "cancelled" or .conclusion == "timed_out")] | length)"' 2> /dev/null); then
  read -r total running red <<< "$counts"
  if [ "$total" -eq 0 ]; then
    checks="no checks ran on this commit"
  elif [ "$red" -gt 0 ]; then
    checks="${red} of ${total} FAILED"
  elif [ "$running" -gt 0 ]; then
    checks="${running} of ${total} still running"
  else
    checks="all ${total} green"
  fi
fi

printf '\n%s\n\n' "$entry"
printf 'release   v%s\n' "$version"
printf 'from      origin/%s (%s)\n' "$ref" "$(git rev-parse --short "$remote_sha")"
printf 'checks    %s\n' "$checks"
printf 'image     ghcr.io/%s:v%s and :latest\n' \
  "$(gh repo view --json nameWithOwner --jq .nameWithOwner)" "$version"

if [ "$assume_yes" -eq 0 ]; then
  if [ ! -t 0 ]; then
    echo "not a terminal -- pass --yes to release without the prompt" >&2
    exit 1
  fi
  printf '\nStart the Release workflow? [y/N] '
  read -r reply
  case "$reply" in
    y | Y | yes | Yes) ;;
    *) echo "nothing started"; exit 1 ;;
  esac
fi

# Which run is ours: the newest one before the dispatch, then the first one
# that is newer. GitHub takes a moment to register it.
before=$(gh run list --workflow "$WORKFLOW" --branch "$ref" --limit 1 \
  --json databaseId --jq '.[0].databaseId // 0')

gh workflow run "$WORKFLOW" --ref "$ref" || {
  echo "could not start the workflow -- a 403 here usually means the gh token" >&2
  echo "lacks the workflow scope: gh auth refresh -s workflow" >&2
  exit 1
}

run_id="$before"
for _ in $(seq 30); do
  sleep 2
  run_id=$(gh run list --workflow "$WORKFLOW" --branch "$ref" --limit 1 \
    --json databaseId --jq '.[0].databaseId // 0')
  [ "$run_id" != "$before" ] && break
done

if [ "$run_id" = "$before" ]; then
  echo "started, but the run has not shown up yet -- gh run list --workflow ${WORKFLOW}"
  exit 0
fi

url=$(gh run view "$run_id" --json url --jq .url)
echo "started: ${url}"
[ "$watch" -eq 1 ] || exit 0

gh run watch "$run_id" --exit-status || {
  echo "the release run failed -- ${url}" >&2
  exit 1
}

echo
gh release view "v${version}" --json tagName,url --jq '"released \(.tagName) -- \(.url)"' \
  2> /dev/null || echo "released v${version}"
