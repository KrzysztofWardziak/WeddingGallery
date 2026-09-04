#!/usr/bin/env bash
# Exercises deploy/wedding-deploy.sh against a scratch repository.
set -uo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
AGENT="$SCRIPT_DIR/wedding-deploy.sh"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

failures=0
check() {
  if [ "$2" = "$3" ]; then echo "OK    $1"; else
    echo "FAIL  $1"; echo "        expected: $3"; echo "        actual:   $2"
    failures=$((failures + 1))
  fi
}

export GIT_AUTHOR_NAME=test GIT_AUTHOR_EMAIL=test@test
export GIT_COMMITTER_NAME=test GIT_COMMITTER_EMAIL=test@test

git init -q --bare "$WORK/origin.git"
git clone -q "$WORK/origin.git" "$WORK/app" 2>/dev/null
cd "$WORK/app"
echo one > file.txt && git add . && git commit -qm one
first="$(git rev-parse HEAD)"
echo two > file.txt && git commit -qam two
second="$(git rev-parse HEAD)"
git push -q origin HEAD:master

# Untracked file (stands in for the server's real .env), created once and never committed.
# `checkout --force` must never touch it.
echo "SECRET=keepme" > .env

# Stub docker: records every invocation, and fails when FAIL_ON matches the subcommand.
mkdir -p "$WORK/bin"
cat > "$WORK/bin/docker" <<'STUB'
#!/usr/bin/env bash
echo "$*" >> "$DOCKER_LOG"
if [ -n "${FAIL_ON:-}" ] && [ "${2:-}" = "$FAIL_ON" ]; then exit 1; fi
exit 0
STUB
chmod +x "$WORK/bin/docker"
export PATH="$WORK/bin:$PATH"

STATE="$WORK/state"
mkdir -p "$STATE"
run_agent() {
  DOCKER_LOG="$WORK/docker.log" APP_DIR="$WORK/app" STATE_DIR="$STATE" \
    DEPLOY_TAG=production bash "$AGENT" > "$WORK/out.txt" 2>&1
  echo $?
}
tag_at() { git -C "$WORK/app" tag -f production "$1" -m x 2>/dev/null || git -C "$WORK/app" tag -f production "$1"; git -C "$WORK/app" push -q --force origin refs/tags/production; }

# 1. No tag at all: quiet success, nothing built.
: > "$WORK/docker.log"
check "no tag exits 0" "$(run_agent)" "0"
check "no tag builds nothing" "$(wc -l < "$WORK/docker.log" | tr -d ' ')" "0"

# 2. Tag moved: checkout, build, up, state recorded.
tag_at "$first"
: > "$WORK/docker.log"
check "deploy exits 0" "$(run_agent)" "0"
check "deploy built" "$(grep -c 'compose build' "$WORK/docker.log")" "1"
check "deploy started" "$(grep -c 'compose up -d' "$WORK/docker.log")" "1"
check "deploy validated the compose file" "$(grep -c 'compose config -q' "$WORK/docker.log")" "1"
check "config precedes build" \
  "$(grep -n 'compose config\|compose build' "$WORK/docker.log" | head -1 | grep -c config)" "1"
check "deploy prunes dangling images" "$(grep -c 'image prune -f' "$WORK/docker.log")" "1"
check "build precedes up" \
  "$(grep -n 'compose build\|compose up' "$WORK/docker.log" | head -1 | grep -c build)" "1"
check "records deployed commit" "$(cat "$STATE/deployed-commit")" "$first"
check "checked out the commit" "$(git -C "$WORK/app" rev-parse HEAD)" "$first"
check "untracked .env survives checkout --force" "$(cat "$WORK/app/.env" 2>/dev/null)" "SECRET=keepme"

# 3. Same tag again: nothing happens.
: > "$WORK/docker.log"
check "already deployed exits 0" "$(run_agent)" "0"
check "already deployed does nothing" "$(wc -l < "$WORK/docker.log" | tr -d ' ')" "0"

# 4. Build fails: non-zero, failure recorded, containers left alone.
tag_at "$second"
: > "$WORK/docker.log"
FAIL_ON=build
export FAIL_ON
check "failed build exits non-zero" "$(run_agent)" "1"
check "failed build never starts containers" "$(grep -c 'compose up' "$WORK/docker.log")" "0"
check "records failed commit" "$(cat "$STATE/failed-commit")" "$second"
check "deployed commit unchanged" "$(cat "$STATE/deployed-commit")" "$first"

# 5. Same failed commit next tick: skipped, not retried.
: > "$WORK/docker.log"
check "known failure exits 0" "$(run_agent)" "0"
check "known failure does not rebuild" "$(wc -l < "$WORK/docker.log" | tr -d ' ')" "0"
unset FAIL_ON

# 6. `up -d` fails: non-zero, but this is transient (port contention, OOM, a slow volume),
# so unlike a build failure it must NOT be recorded as failed-commit, and the next tick
# must retry the same target rather than skipping it.
echo three > file.txt && git -C "$WORK/app" commit -qam three
third="$(git -C "$WORK/app" rev-parse HEAD)"
git -C "$WORK/app" push -q origin HEAD:master
tag_at "$third"
: > "$WORK/docker.log"
FAIL_ON=up
export FAIL_ON
check "failed up exits non-zero" "$(run_agent)" "1"
check "failed up still builds" "$(grep -c 'compose build' "$WORK/docker.log")" "1"
check "failed up attempts start" "$(grep -c 'compose up -d' "$WORK/docker.log")" "1"
check "failed up does not record failed commit" "$(cat "$STATE/failed-commit" 2>/dev/null)" "$second"
check "failed up leaves deployed commit unchanged" "$(cat "$STATE/deployed-commit")" "$first"

# 7. Next tick after a failed `up`: retried, not skipped.
: > "$WORK/docker.log"
check "up retried exits non-zero" "$(run_agent)" "1"
check "up retried invokes docker again" "$(grep -c 'compose up -d' "$WORK/docker.log")" "1"
check "second up failure still not recorded" "$(cat "$STATE/failed-commit" 2>/dev/null)" "$second"
check "up failures counted" "$(cat "$STATE/up-failures")" "$third 2"

# 8. Deploys held: the sentinel file stops everything before any state is read or written.
# Slotted in mid-retry precisely to prove it touches nothing on its way out.
: > "$WORK/docker.log"
touch "$STATE/HOLD"
check "held deploy exits 0" "$(run_agent)" "0"
check "held deploy invokes no docker" "$(wc -l < "$WORK/docker.log" | tr -d ' ')" "0"
check "held deploy says so" "$(grep -c 'held' "$WORK/out.txt")" "1"
check "held deploy leaves deployed commit" "$(cat "$STATE/deployed-commit")" "$first"
check "held deploy leaves failed commit" "$(cat "$STATE/failed-commit")" "$second"
check "held deploy leaves the up-failure count" "$(cat "$STATE/up-failures")" "$third 2"
rm -f "$STATE/HOLD"

# 9. Third consecutive `up -d` failure on the same target: bounded. Retrying forever would
# stop and recreate the containers every two minutes, so now it is recorded as failed.
: > "$WORK/docker.log"
check "third up failure exits non-zero" "$(run_agent)" "1"
check "third up failure records failed commit" "$(cat "$STATE/failed-commit")" "$third"
check "third up failure leaves deployed commit unchanged" "$(cat "$STATE/deployed-commit")" "$first"
unset FAIL_ON

# 10. `docker compose config` fails: treated exactly like a build failure.
echo four > file.txt && git -C "$WORK/app" commit -qam four
fourth="$(git -C "$WORK/app" rev-parse HEAD)"
git -C "$WORK/app" push -q origin HEAD:master
tag_at "$fourth"
: > "$WORK/docker.log"
FAIL_ON=config
export FAIL_ON
check "invalid compose exits non-zero" "$(run_agent)" "1"
check "invalid compose never builds" "$(grep -c 'compose build' "$WORK/docker.log")" "0"
check "invalid compose never starts containers" "$(grep -c 'compose up' "$WORK/docker.log")" "0"
check "invalid compose records failed commit" "$(cat "$STATE/failed-commit")" "$fourth"
check "invalid compose leaves deployed commit unchanged" "$(cat "$STATE/deployed-commit")" "$first"
unset FAIL_ON

echo
[ "$failures" -eq 0 ] && echo "ALL PASS" || echo "$failures FAILURES"
exit $((failures == 0 ? 0 : 1))
