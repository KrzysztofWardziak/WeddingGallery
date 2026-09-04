# CI/CD design

## Problem

Deployment is entirely manual: log in to the server, `git pull`, `docker compose up -d
--build`. Nothing verifies the code before it lands, nothing records which commit is
actually running, and undoing a bad deploy means reversing commits by hand under pressure.

## The constraint that shapes everything

The server sits behind the carrier's DS-Lite, which is the entire reason the Cloudflare
tunnel exists. There is no reachable inbound port, so **GitHub-hosted runners cannot
connect to it**. Every push-style pattern is unavailable: SSH actions, rsync, a remote
Docker context. Asking for the server's IP address would not help, because there is no
address that answers.

Delivery therefore has to be inverted. The server reaches out and converges on a state it
fetches for itself; nothing is ever pushed to it.

A self-hosted GitHub Actions runner would solve the connectivity neatly, since it dials
out. It is rejected because **the repository is public**: a pull request from any fork
could execute arbitrary code on a home server. GitHub documents this hazard, and no amount
of care makes it appropriate here.

## Approach

Three pieces that know almost nothing about each other.

1. **`ci.yml`** verifies every push and pull request. It never touches the server and
   needs no secrets.
2. **`deploy.yml`** is a manual button. Its only effect is to move a git tag named
   `production` to a chosen commit. It pushes nothing, because it cannot.
3. **The agent** is a systemd timer on the server that compares `production` against what
   is checked out and, when they differ, rebuilds and restarts.

The tag is the whole protocol. The button writes it, the server reads it.

The server checks out the tagged **commit**, not just an artefact, because
`docker-compose.yml` changes along with the code - the `uploads-tmp` volume arrived with
chunked upload. Configuration and code have to move together or a deploy is half-applied.

## Decisions

| Decision | Choice | Why |
| --- | --- | --- |
| Trigger | Manual button, not push-to-master | The wedding is irreversible. Nine merges landed on master in a single day, several carrying UI nobody had looked at yet; automatic deployment would have shipped all of them unseen. |
| Pointer | A movable `production` tag | Rollback becomes the same action with an older commit, rather than a separate emergency procedure. |
| Build location | On the server | Chosen deliberately; keeps the deploy self-contained and avoids a registry. |
| Build before switching | `docker compose build` first, `up -d` only on success | `build` leaves running containers alone. Without the split, a failing build would strand the server on a new checkout with the gallery down. |
| Rollback | Manual, via the same button | A half-working automatic rollback needs a definition of "healthy" that this app does not have, and would be worse than none. |
| Poll interval | 2 minutes | Fast enough to feel immediate, slow enough to be invisible. |

## CI workflow

Three jobs, run on every push and pull request:

- **backend** - `dotnet build`, `dotnet test`
- **frontend** - `npm ci`, `ng build`
- **images** - `docker compose build`

The third job exists precisely because the server compiles for itself. A broken
`Dockerfile` passes both other jobs and only fails on production, **after the tag has
already moved**. Building the images in CI moves that failure to before the button.

That job needs a wrinkle: `docker-compose.yml` interpolates `${DOMAIN:?...}` and friends,
so Compose refuses to read the file at all without them. CI supplies a throwaway env file
with placeholder values. They are not secrets and grant nothing; they exist only to satisfy
interpolation.

## Deploy workflow

`workflow_dispatch` with a single input, `ref`, defaulting to `master`. It resolves that
ref to a commit, force-moves the `production` tag there and pushes the tag. Nothing else.

It runs with `contents: write` on the built-in `GITHUB_TOKEN`. **No secret has to be
created, stored or rotated anywhere**, which is the quiet benefit of a pull-based design.

The run log records who pressed the button and which commit it selected, which is the only
audit trail of what was deployed and when.

## The agent

Three files ship in the repository under `deploy/`:

- `wedding-deploy.sh` - the logic
- `wedding-deploy.service` - a `Type=oneshot` unit invoking it
- `wedding-deploy.timer` - fires it every two minutes

The unit name is what `journalctl -u wedding-deploy` refers to throughout this document.
The agent operates on the existing clone at `/srv/wedding/app`.

**The script is copied to `/usr/local/bin/wedding-deploy` at install time rather than run
out of the working tree.** Running it in place would have the agent executing a file that
its own `git checkout` is replacing underneath it. Nothing good is guaranteed to come of
that, and the cost of avoiding it is one `install` command that the runbook already has to
document for the unit files.

### State, and why `HEAD` is not enough

Progress is tracked in two files outside the repository, where a checkout cannot reach
them:

- `/srv/wedding/deployed-commit` - the commit whose deploy **finished successfully**
- `/srv/wedding/failed-commit` - the commit whose deploy failed

Comparing the tag against `HEAD` would look sufficient and is not. The checkout happens
before the build, so a failing build leaves `HEAD` already at the new commit while the old
containers keep serving. On the next tick the agent would see tag equal to `HEAD`, conclude
it had nothing to do, and never retry - the repository and the running application silently
disagreeing, with nothing reporting it.

Each run:

0. `/srv/wedding/HOLD` exists - log that deploys are held and exit 0, before the fetch and
   before any state is read. The event-day freeze: during the reception a bad deploy is
   unrecoverable, and the timer otherwise converges every two minutes including after a
   reboot. A file survives a reboot where `systemctl stop` on an enabled timer does not.
1. `git fetch --tags --force --prune`
2. No `production` tag yet - exit quietly. This is the state before the first deploy and
   must not look like a failure.
3. Tag matches `deployed-commit` - exit quietly. The common case.
4. Tag matches `failed-commit` - exit quietly, saying so. **One attempt per tag move.** A
   broken commit must not rebuild every two minutes forever, filling the journal and
   loading the machine. Retrying means moving the tag again, or deleting that file; both
   are in the runbook.
5. Otherwise: check out the commit detached, `docker compose config -q`, `docker compose
   build`, then `docker compose up -d`. On success record `deployed-commit`, clear
   `failed-commit`, and `docker image prune -f` (guarded - a prune failure is housekeeping,
   not a failed deploy); on failure record `failed-commit` and exit non-zero so the unit
   shows as failed.

`config -q` runs before `build` because a `docker-compose.yml` broken in a way `build` does
not notice would otherwise only surface at `up -d`, which can leave the gallery down. Like a
build failure it changes nothing that is currently serving, so it is recorded the same way.

**A failing `up -d` is retried, but only three times.** It is not recorded as
`failed-commit` immediately, because its causes (port contention, a slow volume, an OOM
during recreation) are often transient and the stack may now be half-started. But each
retry can stop and recreate containers, so an unstartable commit would cycle the gallery
every two minutes forever. Consecutive failures for the same target are counted in
`/srv/wedding/up-failures`; the third records `failed-commit` and stops exactly as a build
failure would. The counter resets on success and whenever the target changes.

The checkout uses `--force`, which discards local edits to **tracked** files. Untracked and
ignored files are untouched, so the server's `.env` is safe. Without it, one stray local
edit would wedge every future deploy.

**Overlapping runs cannot happen.** A build can outlast the two-minute interval, but
systemd will not start a unit that is still active, so the timer simply skips.

**There is deliberately no `git clean`.** The server's `.env` lives in that directory.
It is gitignored, so checking out a different commit leaves it alone - but a stray clean
step would delete it, and the safest way to guarantee that never happens is for no such
step to exist.

The local Compose overlay cannot leak into production: Compose auto-loads only
`docker-compose.override.yml`, and ours is named `docker-compose.local.yml`, which requires
an explicit `-f`.

## What this does not give

- **No deployment status in GitHub.** The button turns green when the tag moves, whatever
  the server does afterwards. The truth lives in `journalctl -u wedding-deploy`. Reporting
  back would need a return path from a machine with no inbound route - a separate problem.
- **Up to two minutes** between pressing the button and the deploy starting.
- **The agent is installed by hand, once.** The script and both unit files are copied out
  of the repository at install time, so changing any of them needs the install step run
  again on the server. This is the price of not executing a script that a checkout is
  rewriting underneath it.
- **A failed deploy stops and waits for a human.** It does not retry on its own, and the
  only place that says so is the journal.
- **No health check.** A deploy that builds and starts but serves errors looks identical to
  a good one.
- **No database rollback.** `Program.cs` runs `db.Database.Migrate()` on every start and EF
  Core never downgrades a schema, so deploying an older SHA rolls back the code and leaves
  the database where the newer code left it. Rollback is therefore safe only between
  commits with no migration between them; crossing a migration backwards is not supported
  and needs a restore from a `pg_dump` (`AddMediaType` and `AddEventDate` are exactly that
  shape). The precaution is a dump taken *before* any deploy that carries a migration -
  the runbook's rollback section says so and points at its backup section for the command.

## Testing

- The workflows are verified by pushing the branch and reading the runs; a workflow that
  has never executed is not evidence of anything.
- The agent script is exercised locally against a scratch clone with a synthetic
  `production` tag and a stub `docker` on `PATH` that records its invocations and can be
  made to fail on demand. Cases: no tag at all, tag already deployed, tag moved forward, a
  failing build leaving the previous containers untouched, and the same failed commit
  skipped on the following run.
- **Not verifiable here:** the real server. Installing the timer and the first convergence
  are manual steps, written into the runbook.

## Risks

- Anyone with write access to the repository can move the tag, and therefore deploy. That
  is the same authority as pushing to master, so it adds no new exposure - but it is now a
  single click.
- A deploy whose `docker-compose.yml` is broken in a way neither `config -q` nor `build`
  catches will still be applied; only `up -d` will fail, and after three attempts the agent
  stops and the gallery stays down until someone looks.
- The agent runs as root. It must be able to drive Docker, and any user that can do so
  effectively holds root on the host anyway, so a dedicated service user in the `docker`
  group would buy nothing. Two consequences: git refuses the operator-owned clone as
  "dubious ownership" until the install step adds a global `safe.directory`, and after the
  first run the clone contains root-owned git objects, so the manual fallback has to be run
  as root too. Both are in the runbook.
- A hung build would block every future deploy, because systemd will not start a unit that
  is still active. `TimeoutStartSec=2700s` (45 minutes) bounds it: comfortably more than a
  cold .NET plus Angular build on this machine, but not forever.
