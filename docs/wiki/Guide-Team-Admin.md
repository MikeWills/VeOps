# Team Admin

You own a team's configuration. Everything a [Session Manager](Guide-Session-Manager.md) can do,
you can do — read that guide first, because running sessions is still most of the work. This page
covers the part that is only yours.

## The team's credentials

**Team Settings** holds every integration this team uses. All of them are stored encrypted, and all
of them are per-team: two teams on one server can be on entirely different Square accounts, Zoom
accounts and mail servers.

| Integration | Required? | What breaks without it |
|---|---|---|
| **ExamTools** | Yes | Everything. Nothing else has anything to work on. |
| **Zoom** | No | No meeting is created; the session still runs. |
| **Discord** | No | No event, no channel posts. |
| **Square** | No | No payment links; candidates cannot pay through the app. |
| **Email (SMTP)** | No | No candidate email of any kind goes out. |

Every optional integration fails *quietly*. If Square is not configured, payment links are
not created and the app logs one line saying so — and the moment you add the credentials, the next
pass creates every link that was skipped. There is no separate backfill step to remember.

⚠️ **One thing to never do:** if integrations suddenly all fail to authenticate, do **not** fix it
by re-entering the credentials. That is a key-ring problem, and re-entering them destroys the
originals permanently. Follow
[key-ring-problems.md](../runbooks/key-ring-problems.md)
and get a System Admin.

## Who is on the team

**Users** is where you create accounts, set a role, and set which team(s) somebody belongs to.
Role and team membership are separate actions — granting somebody Session Manager does not put them
on a team, and a user on no team sees nothing.

You can grant Session Manager and Team Lead within your own team. Only a System Admin grants Team
Admin.

## What the app says to people

**Message Rules** is the whole outbound messaging system: which trigger, who receives it, on which
channel, how long before or after, and from which template. A rule can send email, post to Discord,
or both.

Two behaviours that surprise people:

- **A new or re-enabled rule does not send a backlog.** It starts from the moment you enabled it.
  A person's inbox is not a resource that has to exist, so the app deliberately does not catch up.
- **Suppressed is final.** When a message is held back — a muted team, or the FCC-issue switches
  below — it is marked suppressed, not queued. Turning the switch back off never releases a flood.

**FCC Status** is the manual switch for when the FCC itself is broken. When their processing stalls,
flip the master switch (and the per-population sub-switches) to stop the app nagging candidates
about something no one can fix. Then use the bulk-email screen off **Applicant Status** to tell them
what is going on. That screen needs one specific team picked, because the message goes out over
that team's own mail credentials.

## Watching the machinery

| Screen | Read it when |
|---|---|
| **Job History** | Did the overnight work actually run, and what did it do |
| **Job Schedule** | When each job is due next |
| **Reconciliation** | Payments and candidates that do not line up |
| **Audit Log** | Who changed what, and when |
| **Team Maintenance** | Team-level housekeeping |

**Job History going quiet is the signal worth watching** — it means the background work stopped, and
everything else stops with it. That one is probably a System Admin's problem; see
[Something is wrong](Something-is-wrong.md).

## The VE roster

**VE Directory**, **VE Tags**, **Import VEs**, **Discord Tags**, merging duplicates, and emailing
the roster are all yours.

Two things to know. The **session roster is display-only** — it is reconciled from ExamTools on
every pass, so an edit here would be reverted precisely when it mattered. And **Discord tag sync**
is one-directional: mapped tags follow Discord roles, so a tag removed by hand comes back on the
next run. The place to make a tag change stick is Discord.

## Related

- [Session Manager](Guide-Session-Manager.md) — the day-to-day work
- [Roles and permissions](Roles-and-Permissions.md)
- [System Admin](Guide-System-Admin.md) — what to escalate, and to whom
