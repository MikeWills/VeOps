# Deployment

## Overview

The app is deployed to Ubuntu Linux as **two** systemd services sharing one SQLite database,
behind an Apache reverse proxy with SSL via Let's Encrypt (Web only — the Worker has no public
endpoint).

| Item | Detail |
|---|---|
| Server | Same Ubuntu box as `NcsScheduler`, reachable only over Tailscale |
| App path | `/opt/vesessionmanager/releases/<tag>/{worker,web}/`, with `/opt/vesessionmanager/current` → the release the units run (since 2026-09-13, see "Releases and rollback" below) |
| Database path | `/var/lib/vesessionmanager/vesessionmanager.db` — **deliberately outside** the app path (see below) |
| Data Protection key ring | `/var/lib/vesessionmanager-keys/` — **a separate directory from the database, deliberately** (moved 2026-08-10, see below) |
| Logs | `/var/lib/vesessionmanager/logs/{web,worker}-<date>.log` — absolute, outside the release tree, so pruning a release never takes its history |
| Service account | `vesessionmanager` (dedicated, distinct from NcsScheduler's `www-data` on the same box) |
| Worker service | `vesessionmanager-worker.service` — background jobs, no listening port |
| Web service | `vesessionmanager-web.service` — `ASPNETCORE_URLS=http://localhost:5100` |
| Reverse proxy | Apache with `ProxyPreserveHost On`, Web only |
| SSL | Let's Encrypt |
| Public domain(s) | `ve.wx0mik.radio` (decided 2026-07-22) — a second domain may be added later for a second team; see "Apache Virtual Host" below |

**Why the DB lives outside the app path:** unlike NcsScheduler (whose SQLite file sits inside
`/opt/ncsscheduler/`, the same tree its deploy `rsync --delete`s, protected only by an `--exclude`
flag on every run), VeOps's `appsettings.Production.json` already points the connection
string at `/var/lib/vesessionmanager/vesessionmanager.db` — physically outside
`/opt/vesessionmanager/releases/` entirely. An `rsync --delete` against the app folders can
never touch it, exclude flags or not. `/var/lib/` is also the conventionally-correct FHS location
for a service's variable data, vs. `/opt/` for its binaries.

**Data Protection key ring (2026-07-30, see `docs/credential-encryption.md`):** `Team`'s per-team
credential columns (ExamTools/Zoom/Square/SMTP secrets) are encrypted at rest via ASP.NET Core's
Data Protection API. Both `vesessionmanager-worker` and `vesessionmanager-web` must point at the
exact same key-ring path *and* register the same application name (`"VeOps"`, hardcoded
identically in both `Program.cs` files) — if these ever drift, one process's writes silently become
unreadable by the other. **This key ring needs the same backup discipline as the DB file** — if
it's ever lost while the DB survives, every encrypted credential becomes permanently unrecoverable
and every team has to re-enter Zoom/Square/SMTP/ExamTools credentials from scratch.

**But do not literally bundle the key ring into the same backup artifact as the DB.** The key file
itself is stored unencrypted on disk (Linux has no DPAPI-style at-rest protection for it, unlike
Windows), so the encryption only actually protects the credentials in a scenario where the key ring
and the DB end up in different hands. If both ever ship together in one archive and that archive
leaks, whoever has it can decrypt everything as trivially as if the columns were never encrypted —
back the key ring up somewhere separate from (or with tighter access than) wherever the DB backup
goes. See `docs/credential-encryption.md` for the full reasoning.

### The key ring moved out of the database directory (2026-08-10)

It used to live at `/var/lib/vesessionmanager/dataprotection-keys/` — *inside* the same directory as
`vesessionmanager.db`. That satisfied "outside the app path, so `rsync --delete` can never touch it"
but not the point of the paragraph above: one `tar` of `/var/lib/vesessionmanager/`, one disk image,
one careless backup, and the ciphertext and the key that opens it travel together. Encrypting the
columns bought nothing in that scenario.

It is now **`/var/lib/vesessionmanager-keys/`**, a sibling directory with its own ownership and mode
`0700`.

#### Migrating an existing deployment — order matters

⚠️ **Copy the keys before deploying the config change.** If the new path is empty when a service
starts, Data Protection generates a *fresh* key, and `EncryptedStringConverter`'s legacy-plaintext
fallback means nothing throws — every credential just reads back as an opaque blob and every
integration fails for reasons that point anywhere but here.

`DataProtectionKeyRingGuard` now refuses to start the host in exactly that state, which converts a
silent, hard-to-diagnose outage into a startup crash naming the affected teams and columns. **Do not
treat that guard as permission to skip the copy** — it is the backstop, not the procedure.

```bash
# 1. On the server, BEFORE deploying the config change:
sudo mkdir -p /var/lib/vesessionmanager-keys
sudo cp /var/lib/vesessionmanager/dataprotection-keys/*.xml /var/lib/vesessionmanager-keys/
sudo chown -R vesessionmanager:vesessionmanager /var/lib/vesessionmanager-keys
sudo chmod 700 /var/lib/vesessionmanager-keys

# 2. Confirm the keys actually arrived — an empty directory is the failure mode:
sudo ls -l /var/lib/vesessionmanager-keys/

# 3. Only now deploy (tag a release). Both services pick up the new path together.

# 4. Verify: the startup log should read
#    "Data Protection key ring verified — N team(s) plus system settings, all stored credentials readable".
sudo journalctl -u vesessionmanager-worker --since "30 min ago" --no-pager | grep -i "key ring"

# 5. Confirm in the UI that a team's credentials still work (Team Settings shows them set, and the
#    next ingestion poll succeeds), then remove the old copy:
sudo rm -rf /var/lib/vesessionmanager/dataprotection-keys
```

**If step 4 shows the guard throwing instead:** stop, and put the original key ring back. Do **not**
"fix" it by re-entering credentials in Team Settings — that overwrites the originals with values
encrypted under the new key and makes the old ones unrecoverable for good.

#### Backups

Back up `/var/lib/vesessionmanager-keys/` **separately from, and not alongside**, the database
backup — that separation is now the whole point of the directory being separate. Losing it while the
database survives makes every stored credential permanently unrecoverable.

**Every deploy already takes a snapshot of both**, in `deploy.yml`, *after* it stops the services:

```bash
sudo /usr/local/sbin/vesessionmanager-backup-db        # -> /var/lib/vesessionmanager/vesessionmanager.db.bak-<stamp>
sudo /usr/local/sbin/vesessionmanager-backup-keyring   # -> /var/lib/vesessionmanager-keys/.bak-<stamp>/
```

Four things here are deliberate and worth not "tidying up":

- **Argument-free helpers on the box, not `sudo rsync` from the runner.** A sudoers rule naming
  `/usr/bin/rsync *` is root-equivalent — it can write any file anywhere — which quietly voided every
  narrow rule beside it (#254). The helpers take no arguments at all, and refuse to run if given any.
- **They live in `ops/`, committed since 2026-09-13, but are copied to the box by hand.** A PR
  can change one, and that change does nothing until `ops/setup-server.sh` is re-run on the box —
  the workflow's forced-command key can write only under `releases/`, deliberately.
- **The database snapshot uses `sqlite3 .backup`, not a file copy** (#343), and runs
  `PRAGMA integrity_check` on the result, deleting it if it fails. It produces one self-contained
  file: no `-wal`/`-shm` sidecars to restore alongside it. A file copy was correct only while nothing
  was writing, and got that guarantee from the service stop above — see `BACKUP.md` for the silent
  data loss (#255) that came of not having it.
- **The two snapshots go to different directories.** Putting the key ring's copy next to the database
  would rebuild the exact problem that separating them solved — one archive of one directory carrying
  both the ciphertext and the key.

On a brand-new box neither path exists yet — the database is not created until a service first runs
`Database.Migrate()` — and each helper treats that as normal rather than failing. An unconditional
copy made the very first deploy to any server fail here (found live 2026-08-04), and guarding from
the runner side was never an option: `sudo test` is not allowlisted, and an unprivileged `test -f`
cannot read `/var/lib/vesessionmanager` at all, so it would report "missing" forever once the
database existed. Inside a helper, running as root, it is just an `if`.

⚠️ **These are rollback snapshots, not backups.** Both sit on the same disk as the thing they
protect, so they survive a bad deploy and nothing else. The off-box backup that matters if the box is
lost is a separate job, built 2026-08-14 (#256): database and key ring to separate Wasabi buckets
under separate keys, the key ring GPG-encrypted on top, both halves restore-tested.

Each helper keeps the **newest 5** snapshots and deletes the rest, pruning only after a new one has
been written and verified — so a failed snapshot can never also discard a good one. Only
`.bak-<14 digits>` names are eligible, which means a hand-made `…bak-before-migration` is left alone
by whoever named it that to keep it. Retention is a `RETAIN` constant at the top of each helper, and
the two are deliberately equal: the snapshots are taken as a pair and restored as a pair, so
different retentions would leave a stamp with one half present and the other already gone.

**Why `appsettings.Production.json` needs no manual server-side editing:** every real integration
credential (ExamTools/Zoom/Discord/Square/SMTP) lives per-`Team` in the database, hand-edited there
directly — never in appsettings (see `CLAUDE.md`'s "Optional-integration pattern" note). Both
`appsettings.Production.json` files already committed to this repo carry no secrets, so they're
synced automatically on every deploy like any other file — nothing to hand-maintain on the server,
unlike NcsScheduler where that file is deliberately excluded from sync because it *does* carry
secrets there.

---

## Both hosts migrate at startup, and they serialize themselves (#443)

`VeOps.Web` and `VeOps.Worker` each call `Database.Migrate()` when they start.
They take an exclusive lock file beside the database first (`<db>.migration-lock`), so whichever gets
there first migrates and the other waits, then finds nothing to do.

**This used to be enforced only by `deploy.yml` starting Worker, confirming it active, then Web** —
workflow sequencing, not a guarantee. It did nothing for a reboot (systemd starts both units
together, with no `After=` between them), nothing for a server attached to no pipeline, and nothing
for a self-hoster using the manual publish-and-copy install below. Both units are `Restart=always`,
so a box-wide hiccup could bring them back simultaneously too.

The lock sits beside the **database**, not in the app directory: the service account writes the data
directory and deliberately cannot write `/opt/vesessionmanager`, and a lock inside a tree that
`rsync --delete` replaces mid-deploy would be worse than no lock. It is opened `DeleteOnClose`, so a
killed process does not leave one behind.

⚠️ **A host that waits says so** — `Waiting for the other host to finish applying database migrations
before starting.` — and gives up after two minutes with an error naming the lock file. If you ever
see that with neither service running, the file is safe to delete.

## Build and Publish (manual, if ever needed outside CI)

```bash
dotnet publish src/VeOps.Worker/VeOps.Worker.csproj -c Release -o publish/worker
dotnet publish src/VeOps.Web/VeOps.Web.csproj -c Release -o publish/web
```

Then copy each folder to its own directory on the server (e.g. via `scp`/`rsync`).

---

## Automated Deploy (GitHub Actions)

`.github/workflows/deploy.yml` deploys automatically whenever a **version tag is pushed**
(`YYYY.MM.PATCH`, e.g. `2026.09.0` — see "Triggering a deploy") — never on every commit to `main`
(that's `ci.yml`'s job: build + test only, on push/PR against `main`). The runner joins the
Tailscale network (the server has no public SSH access) as an ephemeral node, publishes both hosts,
and then does two things over SSH:

1. **`rsync`s each publish output into `/opt/vesessionmanager/releases/<tag>/{worker,web}/`** —
   *beside* the running release, not over it. The services are untouched; there is no downtime yet.
2. **Runs `deploy <tag>` on the box**, which the deploy key's forced command turns into
   `ops/deploy-release <tag>`: snapshot the key ring, stop Web then Worker, snapshot the database
   (`.backup` against a quiet file), flip `/opt/vesessionmanager/current` to the new release, start
   Worker and confirm it is active, start Web, and health-check `http://localhost:5100/` with the
   `Host:` header read from that release's own `AllowedHosts`. **If any of that fails, it flips
   `current` back to the previous release, restarts it, and exits non-zero** — the box is never left
   on a broken build. Then `prune` keeps the newest five releases, and a GitHub release is published
   with the zipped binaries.

The downtime window is stop → symlink → start, a few seconds. Until 2026-09-13 it was
stop → rsync → start, and there was no rollback at all: the previous build was overwritten the
moment the sync ran, so "roll back" meant tagging and redeploying an older commit.

> **Running this yourself?** Everything server-specific is a repository secret (the table under
> "One-time setup") or a variable at the top of `ops/setup-server.sh` and `ops/deploy-release`
> (`APP_SLUG`, `WEB_PORT`/`PORT`). Set the secrets, run the bootstrap script on your server, push a
> tag.
>
> Two notes for anyone who is not the original maintainer: the Tailscale step exists because *this*
> server has no public SSH — delete it and its two secrets if yours is reachable directly — and the
> references below to reusing NcsScheduler's OAuth client are about a sibling project on the same
> box. Create your own instead; nothing depends on it being shared.

### Releases and rollback

```
/opt/vesessionmanager/
├── current -> /opt/vesessionmanager/releases/2026.09.1     what the systemd units run
├── releases/
│   ├── 2026.09.0/{web,worker}/
│   └── 2026.09.1/{web,worker}/                              newest 5 kept
└── ops/deploy-release, ops/ssh-deploy-command               root-owned, run by deploy
```

- Both units' `WorkingDirectory` and `ExecStart` go through `current`; systemd resolves the link at
  each start, so a deploy and a rollback are the same operation pointed different ways.
- `releases/` is owned `deploy:vesessionmanager` with setgid directories: the workflow writes as
  `deploy` with no sudo, the service reads through the group. `ops/` is root-owned so a stolen deploy
  key can upload a build but cannot rewrite the script that judges it.
- **Automatic rollback covers code, not data.** A release that ran a migration and then failed
  health leaves the *previous* binary running against the *newer* schema. The pre-deploy `.bak-`
  snapshot pair is the way back for the data — [`runbooks/roll-back-a-release.md`](runbooks/roll-back-a-release.md).
- A rollback by hand is `deploy <older-tag>` for any tag still under `releases/` — which is what
  makes keeping five worth the disk. Older than that: tag the last good commit and push.
- Serilog's log path is absolute (`/var/lib/vesessionmanager/logs/`) for the same reason the
  database is: a relative `logs/` would live inside one release directory and be pruned with it.

### One-time setup

**`ops/setup-server.sh` does all of the server side**, idempotently, on a fresh box *or* on one
still on the pre-2026-09 in-place layout (it detects `/opt/vesessionmanager/web` with no `current`
link, moves the build under `releases/`, and restarts the units on the new paths — a few seconds
down). Copy the `ops/` directory to the box and run it as root:

```bash
scp -r ops deploy@<box>:/tmp/veops-ops
ssh deploy@<box> sudo bash /tmp/veops-ops/setup-server.sh        # add the running tag as $1 to name the moved release
```

What it creates, and why each piece is shaped the way it is:

1. **The `vesessionmanager` service account** — system account, no shell, no home. Not `www-data`
   (NcsScheduler's account on the same box), so the two apps stay isolated.
2. **Directories.** `releases/` + `ops/` under `/opt`, the database and `logs/` under
   `/var/lib/vesessionmanager`, and the key ring at `/var/lib/vesessionmanager-keys` at **0700** —
   a *sibling* of the database directory, never a child, so no single archive carries both halves
   (see "The key ring moved out of the database directory" above).
3. **The two backup helpers** at `/usr/local/sbin/vesessionmanager-backup-{db,keyring}` — root-owned,
   argument-free, hardcoded paths. The privileged parts of a deploy live here rather than in
   `sudo rsync`, whose sudoers rule was root-equivalent (#254).
4. **`/etc/sudoers.d/vesessionmanager-deploy`** — exact commands, no wildcards: one
   `systemctl stop|start|restart` per unit, the two helpers with `""` (no arguments), the two
   `journalctl` lines exactly as `deploy-release` runs them. Written to a temp file, `visudo -c`'d,
   then installed at `0440` — sudo silently ignores a file at any other mode, and a malformed one
   can lock you out of sudo entirely.

   > **sudo matches the whole command line.** `systemctl stop vesessionmanager-web
   > vesessionmanager-worker` matches *neither* single-unit rule and is rejected as
   > `sudo: a password is required`, which reads like a broken SSH key. `deploy-release` issues one
   > call per unit; keep it that way rather than widening the rules.
5. **The systemd units**, pointing through `current`. Enabled, not started: on a fresh box there is
   no release yet, and on an upgraded one the script restarts them itself.
6. **This app's deploy key.** A fresh ed25519 keypair, `~deploy/.ssh/vesessionmanager-ci`, installed
   in `authorized_keys` with `command="/opt/vesessionmanager/ops/ssh-deploy-command"` and
   no-pty/no-forwarding. That forced command accepts exactly three things — an rsync whose
   destination is `releases/<tag>/{web,worker}/`, `deploy <tag>`, and `prune` — so a leaked repo
   secret cannot open a shell, read `/var/lib`, or touch NcsScheduler's grants on the same account.
   The script prints where the private half is; copy it into the secret below and `shred -u` it.

**Tailscale — nothing new to create.** This is the same box as `NcsScheduler`, so reuse its OAuth
client (tagged `tag:ci`, already permitted in the tailnet ACLs). GitHub secrets are per-repo, so
the two values still have to be added here.

**GitHub repo secrets** (Settings → Secrets and variables → Actions)

| Secret | Value |
|---|---|
| `TS_OAUTH_CLIENT_ID` | same value as NcsScheduler's repo secret |
| `TS_OAUTH_SECRET` | same value as NcsScheduler's repo secret |
| `SSH_PRIVATE_KEY` | **this app's** key — the private half `setup-server.sh` generated (not the shared `deploy` key NcsScheduler uses) |
| `DEPLOY_HOST` | server's Tailscale hostname, e.g. `myserver.tailXXXX.ts.net` |
| `DEPLOY_HOST_KEY` | the server's host key, `ssh-keyscan -t ed25519 <host> \| cut -d' ' -f2-` (`ssh-ed25519 AAAA…`); the workflow pairs it with `DEPLOY_HOST` itself, so the name in the secret does not matter, and it **refuses to run without it** rather than falling back to trust-on-first-use |
| `DEPLOY_USER` | `deploy` |

### Triggering a deploy

Merge into `main` as usual (this only builds/tests via `ci.yml`), then:

```bash
git tag -a 2026.09.0 -m "Release notes go here"
git push --tags
```

That tag push is what triggers `deploy.yml` — never an ordinary commit to `main`.

Tags are **calendar versions, `YYYY.MM.PATCH`**: four-digit year, zero-padded month, and a patch
number that starts at 0 for the first release of a month and counts up — `2026.09.0`, `2026.09.1`,
then `2026.10.0`. No `v` prefix. The workflow's tag filter and `AppVersion` (the footer) both
recognise a release by the four-digit year, so an old-style `v0.42.1` tag no longer deploys. Tags
before 2026-09-12 are semver (`v0.1.0` … `v0.42.1`) and stay as they are. The annotated tag message
is the release notes — `deploy.yml` publishes a GitHub release from it.

---

## First sign-in on a fresh deployment (2026-08-01)

A Production database starts with **no account anyone can sign into**. `DevAuthSeeder` runs only in
Development, and every route that could create a user is itself `[Authorize]`d — so without the
command below, nobody can sign in, and therefore nobody can create the account that would let them.

(The Worker's `DevDataSeeder` does create a `System` user with `Role = SystemAdmin`, but it has no
password and exists purely to own audit-trail foreign keys. It is not a way in — which is also why
the "is anyone able to sign in?" check is written against `PasswordHash != null` rather than against
the role or a row count.)

```bash
dotnet /opt/vesessionmanager/current/web/VeOps.Web.dll --create-admin --email you@example.org --name "Your Name" [--callsign WX0MIK]
```

Applies migrations first, so it works on a box where the services have never started. Prints a
generated password once to stdout, then exits without starting the web host. Refuses if that email
already exists.

To supply the password instead — scripted provisioning, or a password you have already chosen — set
`VSM_ADMIN_PASSWORD` in the environment. Never pass it as an argument: arguments are visible in shell
history and to anyone who can run `ps`.

**Nothing is seeded automatically.** An earlier design created a setup account with credentials
published in the README; that was reverted (2026-08-01) because it meant a documented username and
password worked on every deployment from first start until setup was finished.

**The Web app refuses to start until an administrator exists.** Rather than serving a login page
where every credential is rejected — which looks like a forgotten password or broken auth rather than
unfinished setup — it logs `Critical` and exits non-zero:

```
[CRT] Refusing to start: no account on this deployment can sign in. Create the first administrator with: ...
```

### Run `--create-admin` *before* starting the Web service on a new box

Because the unit is `Restart=always` / `RestartSec=10`, a Web service started before the administrator
exists will **restart-loop every ten seconds**, logging that line each time, until you create one. It
self-heals the moment you do — the next restart comes up normally — but on a fresh server the tidy
order is:

```bash
# after the files are in place, before starting vesessionmanager-web
dotnet /opt/vesessionmanager/current/web/VeOps.Web.dll --create-admin --email you@example.org --name "Your Name"
sudo systemctl start vesessionmanager-web
```

`--create-admin` applies migrations itself, so it is safe to run before either service has ever
started. **The Worker is unaffected** and will happily poll ExamTools with no user accounts at all —
only the Web app has a login surface to protect.

Expect the first deploy to a brand-new server to report the Web unit as failed if you skip this: that
is the safeguard working, not a broken build.

## systemd Services

`ops/setup-server.sh` writes both units; what follows is what it writes. Both paths go through the
`current` symlink — see "Releases and rollback" above.

`/etc/systemd/system/vesessionmanager-worker.service`:

```ini
[Unit]
Description=VE Ops Worker
After=network.target

[Service]
WorkingDirectory=/opt/vesessionmanager/current/worker
ExecStart=/usr/bin/dotnet /opt/vesessionmanager/current/worker/VeOps.Worker.dll
Restart=always
RestartSec=10
User=vesessionmanager
# The Worker is a plain generic Host, not ASP.NET Core -- it reads DOTNET_ENVIRONMENT, not
# ASPNETCORE_ENVIRONMENT (see CLAUDE.md's "Worker Service reads DOTNET_ENVIRONMENT" gotcha). Its
# own default when unset is Production anyway, but set it explicitly so it's never in question.
Environment=DOTNET_ENVIRONMENT=Production

[Install]
WantedBy=multi-user.target
```

`/etc/systemd/system/vesessionmanager-web.service`:

```ini
[Unit]
Description=VE Ops Web
After=network.target

[Service]
WorkingDirectory=/opt/vesessionmanager/current/web
ExecStart=/usr/bin/dotnet /opt/vesessionmanager/current/web/VeOps.Web.dll
Restart=always
RestartSec=10
User=vesessionmanager
Environment=ASPNETCORE_ENVIRONMENT=Production
# Without this, Kestrel falls back to its own default rather than the port Apache/the deploy
# health check expect -- set it explicitly so the real listening port is never in question. Must
# match PORT in ops/deploy-release.
Environment=ASPNETCORE_URLS=http://localhost:5100

[Install]
WantedBy=multi-user.target
```

Neither unit needs a `ConnectionStrings__DefaultConnection` override — both
`appsettings.Production.json` files already commit that value (`/var/lib/vesessionmanager/vesessionmanager.db`)
directly, since it carries no secret (unlike NcsScheduler, which sets its connection string via
systemd `Environment=` because its own deployment model treats it as more sensitive).

```bash
sudo systemctl daemon-reload
sudo systemctl enable vesessionmanager-worker vesessionmanager-web
sudo systemctl start vesessionmanager-worker
sudo systemctl start vesessionmanager-web
sudo systemctl status vesessionmanager-worker vesessionmanager-web
```

---

## Apache Virtual Host

The public domain for the Web admin backend is **`ve.wx0mik.radio`** (decided 2026-07-22). This is
independent of the deploy pipeline above, which only ever talks to `localhost:5100`.

```apache
<VirtualHost *:443>
    ServerName ve.wx0mik.radio

    ProxyPreserveHost On
    ProxyPass / http://localhost:5100/
    ProxyPassReverse / http://localhost:5100/

    SSLEngine on
    # ... Let's Encrypt cert paths
</VirtualHost>
```

**A second domain for a second team is still an open possibility, not yet needed.** This app is
multi-tenant behind one deployment — a second `Team` row (see `docs/multi-team.md`) is served by
this same Worker/Web pair, not a separate deploy — so a second domain here would be purely cosmetic
branding for that team's own users, not a functional requirement. If/when a second team wants their
own domain, add a second `<VirtualHost>` block with a different `ServerName`, pointing at the same
`localhost:5100` — no code or deploy change needed either way, and nothing here blocks on that
decision.

Enable required modules if not already active:

```bash
sudo a2enmod proxy proxy_http
sudo systemctl reload apache2
```
