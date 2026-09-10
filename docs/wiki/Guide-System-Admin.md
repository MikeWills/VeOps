# System Admin

You run the deployment. Everything a [Team Admin](Guide-Team-Admin.md) can do, you can do for every
team — read that guide first. This page is what only you can do, plus what you are on the hook for
when it breaks.

## Only yours

Four screens are yours alone. Three of them hold things that are shared across every team on the
server, which is why no single team's admin can edit them.

| Screen | What it is |
|---|---|
| **Teams** | Create a team. Everything else about a team is the Team Admin's. |
| **VECs** | Shared reference data — the real-world coordinating organisations. One row per VEC across the whole deployment, not one per team. |
| **Fee Configurations** | The fee structure. |
| **System Settings** | Deployment-wide settings, including the site-wide banner. |

You also grant the Team Admin role, and you are the only role that sees every team's Audit Log, Job
History and Reconciliation merged together.

## The team picker

You belong to no team, so by default you see all of them. The picker at the top of a page narrows
the view, and your choice follows you across every screen that filters by team. Some screens —
anything that has to send mail over a team's own credentials — refuse to work until you pick one.

## What you are on the hook for

This is server work, not app work. It lives in the repository, not here.

| Situation | Procedure |
|---|---|
| Shipping a release | [deploy-a-release.md](../runbooks/deploy-a-release.md) |
| A release is broken | [roll-back-a-release.md](../runbooks/roll-back-a-release.md) |
| Fresh box, or somebody self-hosting | [stand-up-a-new-server.md](../runbooks/stand-up-a-new-server.md) |
| Box lost, database corrupt, restore test | [restore-from-backup.md](../runbooks/restore-from-backup.md) |
| Backfilling old closed sessions | [run-a-historical-import.md](../runbooks/run-a-historical-import.md) |

Those are procedures you set out to do. When something has instead gone wrong — integrations failing
to authenticate, background jobs gone quiet — start from
**[Something is wrong](Something-is-wrong.md)**, which is arranged by symptom.

The full runbook index, and the four warnings worth reading before you need any of them, is
[docs/runbooks](../runbooks/README.md).

## The three that will actually bite you

1. **Never re-enter credentials to fix a key-ring problem.** It encrypts them under the wrong key
   and destroys the originals permanently. A wrong key ring looks exactly like un-migrated data —
   nothing throws, nothing logs.
2. **The pre-deploy snapshots are rollback points, not backups.** Same disk, newest five. The backup
   that survives losing the box is the separate off-box job.
3. **A code rollback does not undo a schema migration.** Restore the key ring first, then the
   database — a database restored against the wrong key ring refuses to start.

## Related

- [Team Admin](Guide-Team-Admin.md)
- [Roles and permissions](Roles-and-Permissions.md)
- [`docs/deployment.md`](../deployment.md) and
  [`docs/configuration.md`](../configuration.md)
