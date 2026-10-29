# VE attendance audit (#576)

Session Detail's **Check attendance** button compares who was on the Zoom meeting with the session's
VE roster, so a Session Manager can confirm the VEs listed actually sat the session before filing
with the VEC. It is a read-only aid: nothing is blocked, recorded or stored.

## How it behaves

- **After the meeting only.** Zoom's participant endpoints answer 404 (`3001`, "Meeting does not
  exist") until the host **ends** the meeting. The page says so when it sees that, and never shows
  it as an empty attendance list. The workflow is: end the Zoom meeting, press the button, then file.
- **Names only, nothing stored.** The names are fetched, compared and rendered for one page view. No
  durations, no emails, no table. There is therefore nothing for `PiiPurgeJob` to purge.
- **Call-sign hint, not a verdict.** A roster VE counts as present when their call sign appears in a
  Zoom display name, ignoring case and **not touching another letter or digit** (`W0MIK` is not
  found inside `KW0MIKE`). A VE with no call sign never matches. The page shows three lists and the
  Session Manager judges them by eye: on both, on the roster only, on Zoom only. Rejoins repeat in
  Zoom's report (one name six times in the real test), so names are de-duplicated ignoring case and
  runs of spaces.
- **Who can press it.** The day-of gate (`CanRunDayOfActions`), the same as *Refresh candidates*.
  It is read-only, so this is about who is working the session, not about protecting data.

Code: `ZoomAttendanceMatcher` (pure matching), `VeAttendanceAuditService` (loads the session, calls
Zoom, returns a status), `ZoomClient.GetPastParticipantsAsync`, and the `CheckAttendance` handler on
`Pages/SessionManager/Detail`.

## Why not the live participant list

Tested against a real Zoom **Pro** account on 2026-10-28:

| Endpoint | Pro plan | While the meeting is open | After it ends |
|---|---|---|---|
| `GET /metrics/meetings/{id}/participants` (Dashboard, `type=live`) | **Refused**: "only available for ZMP and Business or higher accounts that have enabled the Dashboard feature" | n/a | n/a |
| `GET /past_meetings/{id}/participants` | Works | 404 `3001` | 200 |
| `GET /report/meetings/{id}/participants` | Works | 404 `3001` | 200 |

The live list would let a Session Manager audit while everyone is still on the call, which is how
sessions actually end. It needs a Business plan, or Zoom webhooks (`meeting.participant_joined`/
`left`) feeding an app-side list; whether a Pro account receives those events was **not confirmed**.
Neither is built. If the end-the-meeting-first step proves unworkable in practice, those are the two
ways forward.

`past_meetings` was chosen over `report` because it needs a single scope and answers the same way.
Zoom's own help text says reports can take up to 30 minutes to be complete after a meeting ends; in
the one test run the list was available immediately. **Lag is not measured beyond that.**

## Zoom app scope (per team, once)

Add **`meeting:read:list_past_participants:admin`** to the team's Server-to-Server OAuth app (its
**Scopes** tab), save, and make sure the app is still active. Zoom's own error names a missing scope
exactly (`Invalid access token, does not contain scopes:[...]`), which is the quickest way to find the
name if Zoom renames it again. Without it the button reports that Zoom did not return the list. A
team without a Zoom connection, or a session with no Zoom meeting, gets a plain message instead.

## Things worth knowing

- A reused meeting id (a personal meeting room) returns the **most recent** instance. Sessions here
  each get a fresh scheduled meeting, so this does not affect them.
- The Zoom display name is whatever the person typed. VEs who join as "iPhone" or leave their call
  sign out show as missing **and** as an extra. That is expected: the three lists are for a person to
  read.
- Probe used for the test: a throwaway script that prompted for the three credentials and printed
  only HTTP status and display names. It is not in the repo.
