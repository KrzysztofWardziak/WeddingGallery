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
check "build precedes up" \
  "$(grep -n 'compose build\|compose up' "$WORK/docker.log" | head -1 | grep -c build)" "1"
check "records deployed commit" "$(cat "$STATE/deployed-commit")" "$first"
check "checked out the commit" "$(git -C "$WORK/app" rev-parse HEAD)" "$first"

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

echo
[ "$failures" -eq 0 ] && echo "ALL PASS" || echo "$failures FAILURES"
exit $((failures == 0 ? 0 : 1))
