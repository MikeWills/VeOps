# Roles and permissions

There are four signed-in roles, plus two audiences who never sign in to the main app at all.

Each role is a **superset of the one below it, within its own scope**. A Team Admin can do
everything a Session Manager can do, but only for their own team. A System Admin can do everything,
everywhere.

| Role | Scope | In one sentence |
|---|---|---|
| **System Admin** | The whole deployment | Runs the server: creates teams, edits shared reference data, sees every team's everything. |
| **Team Admin** | Their own team(s) | Owns the team's configuration — credentials, users, message rules, the VE roster. |
| **Session Manager** | Their own team(s) | Runs sessions end to end: candidates, payments, email, filing to the VEC. |
| **Team Lead** | Their own team(s) | Read-only, with two deliberate exceptions (below). |

Not roles, but people the app serves:

- **Volunteer Examiners** reach their own record through a separate emailed sign-in link. They do
  not have an account in the table above unless somebody also gave them one. See
  [Volunteer Examiner](Guide-Volunteer-Examiner.md).
- **Candidates** never sign in. Everything they touch is a public page or an email. See
  [What a candidate sees](Guide-Candidate.md).

## What a role belongs to

A Team Admin, Session Manager or Team Lead belongs to **one or more teams**, and sees only those
teams' sessions. A user attached to no team correctly sees nothing at all — not everything. A
System Admin is attached to no team and sees all of them, with a team picker to narrow the view.

A Team Lead also has an **assigned manager** recorded on their account. That is informational only:
it records who the lead reports to and grants no access whatsoever.

## Team Lead's two exceptions

Team Lead is read-only everywhere except:

1. **The Renewal Monitor** — open to every signed-in user, on the reasoning that checking whether a
   club member's license is lapsing is not a privileged act.
2. **The two day-of session actions** — *Refresh candidates* and *Create retest payment* on a
   session's page. These are a key part of actually running a session, and the Session Manager may
   not be available to do them.

Worth knowing about *Refresh candidates*: it is not a pure read. It also mints payment links and
sends registration confirmations to anyone newly registered — which is exactly the point when a
walk-in appears.

## Screen by screen

Read this as "the minimum role needed to open the page." Everything is additionally scoped to the
teams the user belongs to.

### Sessions and candidates

| Screen | Minimum role |
|---|---|
| Sessions list, session detail, candidate detail | Team Lead |
| Applicant Status | Team Lead |
| Submit to VEC | Team Lead |
| Renewal Monitor | Any signed-in user |
| Email a candidate / bulk-email pending applicants | Session Manager |
| Unmatched Payments | Session Manager |
| Invite VEs to a session | Session Manager |

### VEs and reports

| Screen | Minimum role |
|---|---|
| VE Directory, VE detail, VE Tags, Import VEs, Discord Tags, merge VEs, email VEs | Team Admin |
| Stats, VE Session Counts, Auditioning Report, Transactions Report | Team Admin |

### Administration

| Screen | Minimum role |
|---|---|
| Teams, Team Settings, Team Maintenance | Team Admin |
| Users | Team Admin |
| Message Rules | Team Admin |
| FCC Status | Team Admin |
| Audit Log, Job History, Job Schedule, Reconciliation | Team Admin |
| VECs | System Admin |
| Fee Configurations | System Admin |
| System Settings | System Admin |

A Team Admin opening a shared screen sees only their own team's rows; a System Admin sees all of
them.

## Where this comes from

This page is the user-facing statement of the rules. The rules themselves live in
`RoleGroups`, `SessionAccessScope` and `AdminAccessScope` in the code, and the reasoning behind them
is in [`docs/admin-auth.md`](../admin-auth.md).
**If this page and the app disagree, the app is right and this page is a bug** — please
[file it](https://github.com/MikeWills/VeOps/issues).
