# Glossary

Amateur radio and FCC terms first, then words this app invented.

## The exam world

| Term | Means |
|---|---|
| **VE** | Volunteer Examiner. An accredited licensee who administers exams. |
| **VE team** | The group of VEs who run sessions together. In VE Ops, a **Team** — it owns its own credentials, settings and roster. |
| **VEC** | Volunteer Examiner Coordinator. The FCC-recognised organisation that accredits VEs and files results — ARRL, W5YI and others. A **shared** record in VE Ops: one "ARRL" for the whole deployment, not one per team. |
| **Session** | One sitting at which exams are administered. |
| **Element** | One exam: Technician (2), General (3), Extra (4). |
| **CSCE** | Certificate of Successful Completion of Examination — proof a candidate passed an element. |
| **Retest** | Taking another element at the same session, usually after passing the one below it. Its own fee, its own payment. |

## The FCC side

| Term | Means |
|---|---|
| **FRN** | FCC Registration Number. Public, not private — it is how the FCC identifies a person. |
| **CORES** | The FCC's system where a candidate pays the FCC's own application fee. |
| **ULS** | Universal Licensing System. The FCC's licensing database, and where a grant shows up. |
| **Grant** | The FCC issuing the license. What everyone is waiting for after a pass. |
| **The ten-day rule** | The FCC's application fee must be paid within ten days or the application expires. This is the *only* ten-day rule in this domain. |
| **Upgrade** | An existing licensee moving up a class. ⚠️ The FCC's **grant date does not change** on an upgrade — only the effective date does. Anything checking "did this exam produce a result" against grant date is right for a new licensee and permanently wrong for an upgrade. |

## Systems VE Ops talks to

| Term | Means |
|---|---|
| **ExamTools** | Where sessions and candidates are created. **The source of truth for people** — VE Ops reads it and never writes back. |
| **Square** | Collects the team's exam fee. |
| **Zoom / Discord** | The meeting and the event for a remote session. Both optional. |

## Words this app made up

| Term | Means |
|---|---|
| **Trigger** | A moment worth telling somebody about — "a candidate registered", "the session starts tomorrow", "the FCC fee is outstanding". |
| **Message rule** | Trigger + audience + channel + timing + template. The unit a Team Admin configures. |
| **Suppressed** | A message that was deliberately not sent and never will be. Not "queued" — turning the switch back off releases no backlog. |
| **Unmatched payment** | Money arrived that does not correspond to a known candidate. Usually a different email address. |
| **Applicant Status** | The list of everyone who has passed and is still waiting on the FCC. |
| **Historical import** | Backfilling old, already-finished sessions. Imported sessions are flagged so the app knows not to email their candidates or chase their fees. |
| **Refresh candidates** | Pull ExamTools right now. Also creates payment links and sends confirmations for anyone new — not a passive reload. |
| **Job** | Background work that runs on a schedule. Everything automatic in this app is a job comparing stored state against a feed, not a reaction to a button. |
| **Tag** | A label on a VE. Grants no access to anything. Where Discord sync is on, mapped tags follow Discord roles. |

## Where these words get used

- [Roles and permissions](Roles-and-Permissions.md) — who can do each of the things above
- [Session Manager](Guide-Session-Manager.md) — most of this vocabulary in the order you meet it
- [What a candidate sees](Guide-Candidate.md) — the same session from the other side

Something here unclear, or a word missing? [File an issue](https://github.com/MikeWills/VeOps/issues)
— a term that needed looking up and wasn't here is worth knowing about.
