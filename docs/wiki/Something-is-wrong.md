# Something is wrong

Find your symptom below. Each one links to the procedure that fixes it, and says who can do it —
several need a Team Admin or System Admin rather than the person who noticed.

Detailed procedures live [with the code](https://github.com/MikeWills/VeOps/tree/main/docs/runbooks)
rather than here, because they're read with a terminal open. This page is the way in.

## A candidate is affected

| Symptom | Who fixes it | Where |
|---|---|---|
| "I paid" but the app says **Unpaid** | Session Manager | [Square payment not recorded](../runbooks/square-payment-not-recorded.md) |
| A candidate got no email, or one with a blank link | Session Manager | [Candidate did not get email](../runbooks/candidate-did-not-get-email.md) |
| Money arrived that matches nobody | Session Manager | **Unmatched Payments** in the app — usually they paid from a different address |
| Passed weeks ago, still no call sign | Nobody, usually | **Applicant Status**. The FCC is slow; the app is already chasing the fee |

## A session is affected

| Symptom | Who fixes it | Where |
|---|---|---|
| A VEC filing came back **Unknown** | Session Manager | [ARRL filing unconfirmed](../runbooks/arrl-filing-unconfirmed.md) — ⚠️ do **not** submit again first |
| The filing page says ExamTools hasn't closed the session | Session Manager | Close it in ExamTools. Marking it completed here is not the same thing |
| A walk-in isn't showing up | Session Manager | Register them in ExamTools, then press **Refresh candidates** |
| No Zoom link or Discord event | Team Admin | Those integrations are optional — check whether the team has them configured |

## Everything is affected

| Symptom | Who fixes it | Where |
|---|---|---|
| Sessions stopped appearing, emails stopped going out | Team Admin → System Admin | [Worker not processing](../runbooks/worker-not-processing.md). Check **Job History** first — if it's gone quiet, that's the sign |
| Every integration suddenly fails to authenticate | System Admin | [Key ring problems](../runbooks/key-ring-problems.md) — ⚠️ **never** fix this by re-entering credentials |
| The site is down or a release broke something | System Admin | [Roll back a release](../runbooks/roll-back-a-release.md) |
| The FCC itself is stalled, and reminders are nagging people | Team Admin | **FCC Status** in the app — suppress the reminders, then tell candidates directly |

## Two warnings worth reading before you need them

**Never press "Submit to VEC" twice after an `Unknown`.** The VEC cannot de-duplicate and there is no
unsend. A missing receipt is not proof that nothing was filed.

**Never re-enter credentials to fix an authentication failure.** If the cause is the key ring, doing
that encrypts them under the wrong key and destroys the originals permanently.

## Still stuck

[File an issue](https://github.com/MikeWills/VeOps/issues) with what you saw and what you expected.
If it's a security problem, use
[SECURITY.md](https://github.com/MikeWills/VeOps/blob/main/SECURITY.md) instead — not an issue.

## Next steps

- [Session Manager](Guide-Session-Manager.md) — how the normal path is meant to go
- [Roles and permissions](Roles-and-Permissions.md) — whether you can fix it yourself
- [All runbooks](../runbooks/README.md) — the full operational index, including deployment
