# The VeOps rename (2026-09-06)

The product has been called **VE Ops** since 2026-08-25, but that pass changed display strings only —
the nav brand, page titles, the 2FA issuer, the default email `FromDisplayName`. The code underneath
was still `VeSessionManager` everywhere. This pass finished the job for the code and the repo, and
deliberately stopped short of the server.

## What changed

| Was | Is |
|---|---|
| `namespace VeSessionManager.*` | `namespace VeOps.*` |
| `src/VeSessionManager.{Core,Web,Worker}` | `src/VeOps.{Core,Web,Worker}` |
| `tests/VeSessionManager.*.Tests` | `tests/VeOps.*.Tests` |
| `VeSessionManager.slnx` | `VeOps.slnx` |
| Published `VeSessionManager.{Web,Worker}.dll` | `VeOps.{Web,Worker}.dll` |
| `design_handoff_vesessionmanager_admin_ui/` | `design_handoff_veops_admin_ui/` |
| `github.com/MikeWills/VeSessionManager` | `github.com/MikeWills/VeOps` |
| "VE Session Manager" / "VESESSIONMGR" in prose | "VE Ops" / "VE OPS" |

The working directory on the maintainer's machine is unchanged (`…/HamRadio/VeSessionManager`) — it
is a local path, not a name anything depends on.

## What deliberately did not change, and why

### Every server-side name

The whole `vesessionmanager` slug on the box stayed as it was:

- `/opt/vesessionmanager/{worker,web}/`
- `/var/lib/vesessionmanager/vesessionmanager.db` and `/var/lib/vesessionmanager/vec-archives`
- `/var/lib/vesessionmanager-keys` (the Data Protection key ring)
- the `vesessionmanager` service account and group
- `vesessionmanager-worker.service` / `vesessionmanager-web.service`
- `/etc/sudoers.d/vesessionmanager-deploy` and its exact, no-wildcard command allowlist
- `/usr/local/sbin/vesessionmanager-backup-db` and `…-backup-keyring`
- the local dev DB filenames (`vesessionmanager.db`, `vesessionmanager.test.db`), which match the
  production connection string on purpose

Renaming these is not a search-and-replace. The database and the key ring have to move together and
in order (key ring first — a database restored against a missing key ring refuses to start), the
service account owns the deploy tree through group membership, and the sudoers allowlist matches
*whole command lines*, so a unit rename silently invalidates the deploy account's grants. That is a
migration with a rollback plan, not a side effect of a rename, and there is no benefit to the running
system from doing it. It was explicitly scoped out.

**So: a lowercase `vesessionmanager` in this repo is a live server path, not a missed replacement.**
Treat it as load-bearing. The case-sensitive split is the tell — `VeSessionManager` (PascalCase) is
gone from the codebase entirely apart from the one exception below.

### `SetApplicationName("VeSessionManager")`

Both `src/VeOps.Web/Program.cs` and `src/VeOps.Worker/Program.cs` still pass the old name to
`AddDataProtection()`, and must keep doing so. It is a **key-derivation purpose string, not a display
name**. Change it and every `Team` credential already encrypted under the live key ring becomes
undecryptable.

The reason this is worth a comment in both files and an entry here is that it fails *silently*.
`EncryptedStringConverter`'s read path returns the raw stored value when `Unprotect` throws — that
fallback is what made the original plaintext-to-encrypted migration safe — so nothing throws and
nothing logs. Every integration simply starts authenticating with a base64 blob, and the first
symptom is ExamTools, Zoom, Square and SMTP all failing auth at once for no visible reason.

`DataProtectionKeyRingGuard` would catch the specific case of a credential still looking like
ciphertext after being read through the converter, but do not rely on that as permission to change
the name. Changing it needs a planned re-encryption pass: read every credential under the old name,
write it back under the new one, key ring backed up first.

## The one operational consequence

The assemblies renamed, so the published entry points are `VeOps.Worker.dll` and `VeOps.Web.dll`. The
two systemd units on the live box still name the old DLL in `ExecStart`, and `deploy.yml`'s
`rsync --delete` removes it.

**Done 2026-09-06.** Both units were edited by hand and `daemon-reload`ed before `v0.35.0` was
tagged, and that deploy succeeded — including the "confirm Worker is healthy before starting Web"
gate. The one-time section that carried the steps has been removed from the deploy runbook. A fresh
box needs nothing, since `ops/setup-server.sh` emits the new names.

This one fails loudly, which is the good case: `rsync` succeeds, `systemctl start` reports
`Could not execute because the specified command or file was not found`, and the workflow's "confirm
Worker is active" gate stops the run before Web is touched. The key-ring and database snapshots are
both taken before the sync, so nothing is at risk — the recovery is the same two `sed` commands plus
a `systemctl start`.

`ops/setup-server.sh` (local-only, gitignored) was updated to emit units with the new DLL names, so a
fresh box built from it is correct without any manual step.

## Repo rename fallout

GitHub redirects the old URL for clones, fetches and pushes indefinitely, and redirects web traffic
to issues and PRs, so nothing breaks immediately. Still worth knowing:

- The local remote was repointed at the new URL rather than relying on the redirect.
- Actions secrets, branch protection, labels and issues all survive a rename untouched.
- The CI and license badges in `README.md` point at the new path.
- Anyone else with a clone (or the deploy box, if it ever grows one) needs
  `git remote set-url origin https://github.com/MikeWills/VeOps.git`.

## Verification

Live-verified on production 2026-09-06. `v0.35.0` deployed clean, and the Worker logged
`Data Protection key ring verified — 3 team(s) plus system settings, all stored credentials
readable` at startup. That line is the one that matters here: it proves the deliberately-unrenamed
`SetApplicationName` is still deriving the same keys and every stored credential across all three
teams still decrypts.

`dotnet build` clean with `-warnaserror`, and the full suite green across all three test projects.
Several tests assert on source paths and namespace strings (`FormBindingTests`,
`DocumentationReferenceTests`, `ParentCrumbRoleMirrorTests`, `JobRegistrationTests` and others), so a
half-finished rename would have failed the suite rather than passed quietly.
