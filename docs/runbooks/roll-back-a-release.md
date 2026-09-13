# Runbook — Roll back a release

**When:** a deployed release is broken and the fix is not minutes away — or the deploy already
rolled itself back and you need to decide about the data.
**Who:** whoever can SSH to the box; step C also needs the ability to tag.
**Why it works this way:** [`docs/deployment.md`](../deployment.md), "Releases and rollback".

---

## First: did the deploy already roll back?

Since 2026-09-13 a deploy that fails its health check **flips `current` back to the previous
release and restarts it by itself**. If the Actions log ends in `rolled back to …; deploy of <tag>
FAILED`, the code is already back. What is *not* back is the data — go straight to "Decide: did
the bad release change the schema?" below.

If the release deployed cleanly and turned out to be broken later, roll the code back yourself
(step A), then decide about the data.

## Decide: did the bad release change the schema?

```bash
git diff --stat <last-good-tag>..<bad-tag> -- src/VeOps.Core/Migrations/
```

- **No migration** → code-only. Step A (if not already done automatically) is enough.
- **A migration ran** → the older binary is now running against a **newer schema**. It may start and
  behave wrongly rather than fail loudly. Do step B as well.

## Step A — flip `current` back (any of the last five releases)

On the box, as the deploy user. Every deploy keeps the previous four releases beside the current one,
so this is a symlink flip and a restart, not a rebuild:

```bash
ls -1 /opt/vesessionmanager/releases/                 # what is still here
readlink -f /opt/vesessionmanager/current             # what is running
sudo -u deploy /opt/vesessionmanager/ops/deploy-release 2026.09.0    # the last good tag
```

`deploy-release` does exactly what a tag deploy does from that point — snapshots the key ring and
database, stops Web then Worker, flips `current`, starts Worker then Web, health-checks — and if
*that* fails it flips back again. The snapshot it takes first is the state you would want if the
rollback itself goes wrong.

Check the footer shows the old tag and the `key ring verified` journal line appears.

## Step B — restore the database (only if a migration ran)

The pre-deploy snapshots live beside the originals, newest 5 kept, named `.bak-<14 digits>`:

```bash
sudo ls -lt /var/lib/vesessionmanager/vesessionmanager.db.bak-*
sudo ls -ltd /var/lib/vesessionmanager-keys/.bak-*
```

Pick the stamp taken **immediately before the bad deploy** — the newest one is from your step A
rollback, not from before the bad release. The two halves are taken as a pair and retained equally
— restore the pair, not one half.

1. Stop both services (Web first).
2. Restore the **key ring** first, then the database — a database restored against a missing or
   wrong key ring makes the app refuse to start, which is correct but confusing mid-incident.
3. Start the **Worker**, confirm the `key ring verified` line, then start Web.
4. Confirm in Team Settings that a team's credentials still read as configured, and watch one
   ingestion poll succeed — that exercises a decrypted ExamTools password end to end.

Full detail, including the off-box case: [`restore-from-backup.md`](restore-from-backup.md).

⚠️ **Everything written since that snapshot is lost** — ingested sessions, payments recorded by
webhook, audit entries. If the bad release ran for hours, weigh a forward fix against the data loss
before restoring.

## Step C — if the good release is no longer under `releases/`

Older than five deploys ago, or a fresh box: tag the last good commit under the next patch number
and push. Force-moving an existing tag is not the way.

```bash
git tag -a 2026.09.2 -m "Rollback to 2026.09.0 -- <why>" <last-good-commit>
git push --tags
```

Then follow [`deploy-a-release.md`](deploy-a-release.md) from step 2.

## What a rollback cannot undo

- **Emails already sent.** No unsend.
- **Zoom/Discord events already created.** They stay; the next poll's query-before-create matching
  should find them rather than duplicating, but confirm.
- **Square payment links already issued**, and any payment taken against one.
- **An ARRL filing already POSTed.** ARRL has no unsend and cannot dedupe — see
  [`arrl-filing-unconfirmed.md`](arrl-filing-unconfirmed.md).

## After

Write down which tag was rolled back and why, in the PR or issue that carried the bad change —
CLAUDE.md's Definition of Done expects rollback decisions to be recorded somewhere findable.
