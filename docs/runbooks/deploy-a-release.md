# Runbook — Deploy a release

**When:** shipping any merged change to the production box.
**Who:** anyone with push access to `main` and the ability to tag.
**Why it works this way:** [`docs/deployment.md`](../deployment.md).

---

## One-time: the first deploy after the VeOps rename (2026-09-06)

⚠️ **Do this before tagging the first release built from the renamed code, or that deploy will
leave both services dead.**

The 2026-09-06 rename changed the *assembly* names — the published entry points are now
`VeOps.Worker.dll` and `VeOps.Web.dll`. Every server-side name stayed as it was on purpose
(`/opt/vesessionmanager`, `/var/lib/vesessionmanager/vesessionmanager.db`,
`/var/lib/vesessionmanager-keys`, the `vesessionmanager` service account, both unit names, the
sudoers allowlist and the two backup scripts), so nothing else needs touching — but the two units
still name the old DLL in `ExecStart`, and `rsync --delete` removes it.

The failure is loud rather than subtle: `rsync` succeeds, then `systemctl start` fails with
`Could not execute because the specified command or file was not found`, and the workflow's
"confirm Worker is active" gate stops the run before Web is started.

```bash
sudo sed -i 's|VeSessionManager\.Worker\.dll|VeOps.Worker.dll|'   /etc/systemd/system/vesessionmanager-worker.service
sudo sed -i 's|VeSessionManager\.Web\.dll|VeOps.Web.dll|'   /etc/systemd/system/vesessionmanager-web.service
sudo systemctl daemon-reload
grep ExecStart /etc/systemd/system/vesessionmanager-{worker,web}.service
```

Edit the units *before* the deploy runs. The old DLL is still in place until `rsync` replaces the
tree, so a unit pointing at the new name will not start until the new build lands — plan on the
short window between the edit and the tag, or edit them while the services are already stopped
mid-deploy.

If you deploy first and only then notice, the recovery is the same two `sed` commands plus
`sudo systemctl start vesessionmanager-worker vesessionmanager-web`; no data is at risk, since the
key-ring and database snapshots were taken before the sync.

Delete this section once it has been done.

---

## Preconditions

- The change is merged to `main` and `ci.yml` is green. A push to `main` is rejected by branch
  protection; everything lands via PR.
- Nothing else is mid-deploy (the workflow stops both services, so two runs would fight).

## Steps

1. **Tag and push.** An ordinary commit to `main` does *not* deploy — only a version tag does.

   ```bash
   git tag v0.4.0
   git push --tags
   ```

2. **Watch the Actions run.** `deploy.yml` does, in this order:
   1. joins the tailnet (the box is only reachable over Tailscale),
   2. snapshots the **key ring** (`vesessionmanager-backup-keyring`) — before the stop,
   3. stops **Web**, then **Worker**,
   4. snapshots the **database** (`vesessionmanager-backup-db`) — after the stop, so `.backup`
      runs against a quiet file,
   5. `rsync --delete`s `publish/worker/` and `publish/web/`,
   6. starts **Worker**, confirms it is active, *then* starts **Web**
      (both call `Database.Migrate()`; starting together races on the same SQLite file),
   7. polls the Web health check until it returns a real HTTP status.

3. **Verify the key ring loaded.** This is the line that proves credentials are still readable:

   ```bash
   sudo journalctl -u vesessionmanager-worker -n 30 --no-pager | grep -i "key ring"
   # Data Protection key ring verified — N team(s), all stored credentials readable
   ```

4. **Verify the app answers.** Load the site, sign in, and confirm one ingestion poll succeeds
   (Admin → Job Run History shows a recent successful `SessionIngestionJob` run).

## If it fails

| Symptom | Cause | Go to |
|---|---|---|
| `sudo: a password is required` on a `systemctl` step | sudoers is one exact rule per unit and matches the **whole** command line | [`docs/deployment.md`](../deployment.md) — do not widen to `systemctl *` |
| Web unit reports failed on a **brand-new** box | no administrator exists yet; the Web app refuses to start | [`stand-up-a-new-server.md`](stand-up-a-new-server.md) |
| Startup crash naming teams and columns | key ring missing or wrong | [`key-ring-problems.md`](key-ring-problems.md) — **stop, do not re-enter credentials** |
| Health check never goes healthy | app starts but 500s, or `AllowedHosts` does not include the serving hostname (every request 400s) | [`docs/deployment.md`](../deployment.md), then [`roll-back-a-release.md`](roll-back-a-release.md) |
| Deploy is fine but a job is silent afterwards | | [`worker-not-processing.md`](worker-not-processing.md) |

## Notes worth not forgetting

- The pre-deploy snapshots are **rollback snapshots, not backups** — same disk as the thing they
  protect, newest 5 kept. Off-box backup is the separate BackupScripts job.
- The `ops/` helpers on the box are **gitignored**; a change to one is a hand-copied server-side
  edit that no PR carries. The copies in this working tree are the source.
- `appsettings.Production.json` needs no server-side editing — it carries no secrets. Every
  integration credential is per-`Team` in the database.
