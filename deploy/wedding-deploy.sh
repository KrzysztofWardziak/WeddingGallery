#!/usr/bin/env bash
# Converges the checked-out application onto whatever commit the `production` tag names.
#
# Delivery is pull-based because the server sits behind DS-Lite and nothing can reach it
# from outside. This script is the only thing that decides what runs.
set -uo pipefail

APP_DIR="${APP_DIR:-/srv/wedding/app}"
STATE_DIR="${STATE_DIR:-/srv/wedding}"
DEPLOY_TAG="${DEPLOY_TAG:-production}"

DEPLOYED_FILE="$STATE_DIR/deployed-commit"
FAILED_FILE="$STATE_DIR/failed-commit"
UPFAIL_FILE="$STATE_DIR/up-failures"
HOLD_FILE="$STATE_DIR/HOLD"

# How many consecutive `up -d` failures on the same target are tolerated before the commit
# is treated as broken. Each retry can stop and recreate containers, so an unstartable
# commit would otherwise cycle the gallery every two minutes forever.
UP_FAILURE_LIMIT=3

cd "$APP_DIR" || { echo "No app directory at $APP_DIR"; exit 1; }

# Deploy freeze. During the event a bad deploy is unrecoverable, so the operator can drop
# a sentinel file and the agent stops deciding anything at all - before the fetch, before
# any state is read or written.
if [ -e "$HOLD_FILE" ]; then
  echo "Deploys are held by ${HOLD_FILE}; doing nothing. Remove it to resume."
  exit 0
fi

if ! git fetch --tags --force --prune origin; then
  echo "Fetch failed"
  # The agent runs as root while the clone is owned by the operator, so the usual cause is
  # git refusing to touch a repository it considers foreign.
  echo "Hint: if the message above says 'detected dubious ownership', run:" >&2
  echo "      sudo git config --system --add safe.directory ${APP_DIR}" >&2
  exit 1
fi

if ! git rev-parse --verify --quiet "refs/tags/${DEPLOY_TAG}^{commit}" > /dev/null; then
  echo "No ${DEPLOY_TAG} tag yet; nothing to deploy."
  exit 0
fi

target="$(git rev-parse "refs/tags/${DEPLOY_TAG}^{commit}")"
deployed="$(cat "$DEPLOYED_FILE" 2>/dev/null || true)"
failed="$(cat "$FAILED_FILE" 2>/dev/null || true)"

if [ "$target" = "$deployed" ]; then
  exit 0
fi

# One attempt per tag move. A broken commit must not rebuild every two minutes forever.
# Move the tag again, or delete this file, to retry.
if [ "$target" = "$failed" ]; then
  echo "${target} already failed; skipping. Delete ${FAILED_FILE} to retry."
  exit 0
fi

echo "Deploying ${target} (was ${deployed:-unknown})"

# Consecutive `up -d` failures, counted per target. A different target starts over.
upfail_commit=""
upfail_count=0
if [ -r "$UPFAIL_FILE" ]; then
  read -r upfail_commit upfail_count < "$UPFAIL_FILE" 2>/dev/null || true
  case "$upfail_count" in ''|*[!0-9]*) upfail_count=0 ;; esac
fi
if [ "$upfail_commit" != "$target" ]; then
  upfail_count=0
  rm -f "$UPFAIL_FILE"
fi

# Records a commit as failed so the "one attempt per tag move" rule above actually holds.
# A silent failure to write this file would look identical to success and the broken
# commit would be rebuilt every tick forever, so the write itself is checked here.
record_failed() {
  if ! { mkdir -p "$STATE_DIR" && echo "$1" > "$FAILED_FILE"; }; then
    echo "Could not record failed commit to ${FAILED_FILE}; it may be retried next tick." >&2
  fi
}

# --force discards local edits to tracked files, which would otherwise wedge every future
# deploy. Untracked and ignored files are untouched, so the server's .env is safe. There is
# deliberately no `git clean` anywhere in this script.
if ! git checkout --detach --force "$target"; then
  echo "Checkout failed"
  record_failed "$target"
  exit 1
fi

# A docker-compose.yml that is broken in a way `build` does not notice would only be
# discovered by `up -d`, which can leave the gallery down. Validating it first costs
# nothing and, like a build failure, changes nothing that is currently serving.
if ! docker compose config -q; then
  echo "Compose file is invalid; leaving the running containers alone."
  record_failed "$target"
  exit 1
fi

# build leaves the running containers alone, so a failure here changes nothing that is
# currently serving. Only once it succeeds do we restart anything.
if ! docker compose build; then
  echo "Build failed; leaving the running containers alone."
  record_failed "$target"
  exit 1
fi

# up failing is different from build failing: build failing means the code is broken and
# retrying cannot help, but up failing (port contention, a slow volume, an OOM during
# recreation) can be transient and the stack may now be half-started or down. We do NOT
# record this commit as failed-commit, so the next tick retries the same target instead of
# waiting for a human to delete the failure marker.
# ... but it is not unbounded: each retry can stop and recreate containers, so a commit
# that can never start would flap the gallery forever. After UP_FAILURE_LIMIT consecutive
# failures on the same target we stop exactly as a build failure would.
if ! docker compose up -d; then
  upfail_count=$((upfail_count + 1))
  if ! { mkdir -p "$STATE_DIR" && echo "$target $upfail_count" > "$UPFAIL_FILE"; }; then
    echo "Could not record the up-failure count to ${UPFAIL_FILE}." >&2
  fi
  if [ "$upfail_count" -ge "$UP_FAILURE_LIMIT" ]; then
    echo "Start failed ${upfail_count} times in a row; giving up on ${target}."
    record_failed "$target"
  else
    echo "Start failed (${upfail_count}/${UP_FAILURE_LIMIT}); will retry next tick."
  fi
  exit 1
fi

if ! { mkdir -p "$STATE_DIR" && echo "$target" > "$DEPLOYED_FILE"; }; then
  echo "Deploy succeeded but could not record deployed commit to ${DEPLOYED_FILE}." >&2
  exit 1
fi
rm -f "$FAILED_FILE" "$UPFAIL_FILE"
echo "Deployed ${target}."

# Build layers accumulate on the same filesystem that holds Postgres and the guest
# uploads, so reclaim the dangling ones. The deploy has already succeeded at this point:
# a failure to prune is a housekeeping problem, never a failed deploy.
if ! docker image prune -f; then
  echo "Image prune failed; deploy stands. Check disk space on /srv/wedding." >&2
fi
