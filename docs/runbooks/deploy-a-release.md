# Runbook — Deploy a release

**When:** shipping any merged change to the production box.
**Who:** anyone with push access to `main` and the ability to tag.
**Why it works this way:** [`docs/deployment.md`](../deployment.md), "Automated Deploy" and
"Releases and rollback".

---

## Preconditions

- The change is merged to `main` and `ci.yml` is green. A push to `main` is rejected by branch
  protection; everything lands via PR.
- If the PR changed anything under `ops/`, that change is **not on the box yet** — copy `ops/` over
  and re-run `sudo bash setup-server.sh` first. The deploy key can only write under `releases/`.
- Two tags pushed close together queue rather than fight (`concurrency: deploy-production`), but
  the second still deploys — make sure it is the one you meant.

## Steps

1. **Tag and push.** An ordinary commit to `main` does *not* deploy — only a version tag does.

   Tags are `YYYY.MM.PATCH` — first release of the month is `.0`, then count up. Check
   `git tag --list '2*' | sort -V | tail -1` for the last one.

   ```bash
   git tag -a 2026.09.1 -m "What changed"
   git push --tags
   ```

2. **Watch the Actions run.** `deploy.yml` does, in this order:
   1. builds and tests; publishes both hosts with the tag as their version,
   2. joins the tailnet (the box is only reachable over Tailscale), pinned host key,
   3. `rsync`s both hosts into `/opt/vesessionmanager/releases/<tag>/` — **beside** the running
      release; nothing is down yet,
   4. hands off to `ops/deploy-release <tag>` on the box, which: snapshots the **key ring**, stops
      **Web** then **Worker**, snapshots the **database** (`.backup` against a quiet file), flips
      `current` to the new release, starts **Worker** and confirms it is active, starts **Web**,
      and polls the health check with the real `Host:` header — up to 90 s,
   5. **on any failure there, flips `current` back and restarts the previous release**, then
      fails the run — see "If it fails",
   6. on success, prunes to the newest five releases and publishes the GitHub release.

3. **Verify the key ring loaded.** This is the line that proves credentials are still readable:

   ```bash
   sudo journalctl -u vesessionmanager-worker --since "30 min ago" --no-pager | grep -i "key ring"
   # Data Protection key ring verified — N team(s) plus system settings, all stored credentials readable
   ```

4. **Verify the app answers.** Load the site, sign in, check the footer shows the new tag, and
   confirm one ingestion poll succeeds (Admin → Job Run History shows a recent successful
   `SessionIngestionJob` run).

What healthy looks like in the Actions log, from `deploy-release`:

```
==> current: /opt/vesessionmanager/releases/2026.09.0  ->  new: /opt/vesessionmanager/releases/2026.09.1
==> Snapshots (key ring first; database after the stop so the file is quiet)
==> Switching to 2026.09.1
web healthy — HTTP 200
==> Deployed 2026.09.1
```

## If it fails

The run ending in `ROLLING BACK to …` followed by `rolled back to …; deploy of <tag> FAILED` means
the box is **serving the previous release again** and nothing is on fire. Read the journal lines
printed just above it for the cause, fix forward, tag again.

| Symptom | Cause | Go to |
|---|---|---|
| `rolled back to …; deploy of <tag> FAILED` | new release did not start or did not pass health | fix forward; **if the release carried a migration**, read [`roll-back-a-release.md`](roll-back-a-release.md) — code is rolled back, the schema is not |
| `rollback did not come up either — service is DOWN` | the previous release will not start either — usually a migration the old binary cannot run against | [`roll-back-a-release.md`](roll-back-a-release.md), step B, now |
| `refused: 'deploy …'` / `refused dest:` from the box | the SSH key on the box is not this app's forced-command key, or the destination is not `releases/<tag>/` | `SSH_PRIVATE_KEY` must be the key `setup-server.sh` generated; re-run it if unsure |
| `DEPLOY_HOST_KEY secret is not set` | the pinned host key was never added | on the box: `ssh-keyscan -t ed25519 localhost 2>/dev/null \| cut -d' ' -f2-`; store the output |
| `sudo: a password is required` in `deploy-release` | sudoers is one exact rule per unit and matches the **whole** command line — the rule set on the box is older than the script | re-run `setup-server.sh`; do not widen to `systemctl *` |
| Web unit reports failed on a **brand-new** box | no administrator exists yet; the Web app refuses to start | [`stand-up-a-new-server.md`](stand-up-a-new-server.md) |
| Startup crash naming teams and columns | key ring missing or wrong | [`key-ring-problems.md`](key-ring-problems.md) — **stop, do not re-enter credentials** |
| `web unhealthy — last HTTP 400` | `AllowedHosts` in `appsettings.Production.json` does not include the serving hostname | fix the setting, tag again |
| Deploy is fine but a job is silent afterwards | | [`worker-not-processing.md`](worker-not-processing.md) |

## Notes worth not forgetting

- The pre-deploy snapshots are **rollback snapshots, not backups** — same disk as the thing they
  protect, newest 5 kept. Off-box backup is the separate BackupScripts job.
- A release directory is only ever *added* by a deploy; `prune` removes the oldest beyond five and
  never the one `current` points at. Any of the five can be put back with `deploy <tag>` through the
  deploy key, or by a person on the box — see [`roll-back-a-release.md`](roll-back-a-release.md).
- `appsettings.Production.json` needs no server-side editing — it carries no secrets. Every
  integration credential is per-`Team` in the database.
