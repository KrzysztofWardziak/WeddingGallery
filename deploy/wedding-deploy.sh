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

cd "$APP_DIR" || { echo "No app directory at $APP_DIR"; exit 1; }

git fetch --tags --force --prune origin || { echo "Fetch failed"; exit 1; }

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

# --force discards local edits to tracked files, which would otherwise wedge every future
# deploy. Untracked and ignored files are untouched, so the server's .env is safe. There is
# deliberately no `git clean` anywhere in this script.
if ! git checkout --detach --force "$target"; then
  echo "Checkout failed"
  mkdir -p "$STATE_DIR" && echo "$target" > "$FAILED_FILE"
  exit 1
fi

# build leaves the running containers alone, so a failure here changes nothing that is
# currently serving. Only once it succeeds do we restart anything.
if ! docker compose build; then
  echo "Build failed; leaving the running containers alone."
  mkdir -p "$STATE_DIR" && echo "$target" > "$FAILED_FILE"
  exit 1
fi

if ! docker compose up -d; then
  echo "Start failed."
  mkdir -p "$STATE_DIR" && echo "$target" > "$FAILED_FILE"
  exit 1
fi

mkdir -p "$STATE_DIR"
echo "$target" > "$DEPLOYED_FILE"
rm -f "$FAILED_FILE"
echo "Deployed ${target}."
