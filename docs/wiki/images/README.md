# Screenshots

Referenced from the pages in this folder as `![alt text](images/name.png)`. That renders while
browsing `docs/wiki/` on github.com; `scripts/build_wiki.py` copies the files into the wiki and
repoints each link at the wiki's own raw host, so one form works in both places.

This file is not published — the build skips it.

## The one rule

**Never capture a screenshot from a team holding real data.** This repository and its wiki are
public, and git history keeps an image after it is deleted from the working tree. Use the **WX0MIK**
test team, whose data is fabricated. HRCC and MARC are real people: real names, addresses, emails
and FRNs.

That applies to more than the obvious screens. A team picker, a nav badge count, a browser tab title
and an autocomplete dropdown have all leaked a name in somebody's documentation.

## Conventions

- **Light theme**, 1280px wide, PNG.
- **Lowercase-hyphenated names** that say which page they belong to:
  `session-manager-detail.png`, not `screenshot-3.png`.
- **Alt text on every image, always.** It is what a reader gets when the image is missing, when it
  has gone stale, and when they are using a screen reader. Describe what the picture shows, not
  that it is a picture.
- **Crop to the thing being explained.** A full-page screenshot of a busy admin screen dates faster
  and reads worse than the panel the sentence is about.

## Before adding one, ask whether prose would do

Screenshots rot faster than words and rot *silently* — nothing fails a build when a button gets
renamed. Worth one when the layout itself is the answer (where a control hides, what a filled-in
form looks like). Not worth one when a sentence already says it, because then there are two copies
of the same claim to keep in step, and they will not stay in step.

## How these were captured

Recorded so the next pass does not re-derive it. First done 2026-09-10.

1. **Run the app locally** (`dotnet run --project src/VeOps.Web`) and have Mike sign in — every
   Session Manager and Admin page is `[Authorize]`d and Claude does not enter passwords. The auth
   cookie then carries, so one sign-in covers a whole pass.
2. **Sign in as the role the guide is about.** A Session Manager guide showing a Team Admin's
   sidebar is quietly wrong. `sessionmanager@`, `teamlead@` and `teamadmin@` were each scoped to
   **WX0MIK only** for the duration, so no account used for capture could display MARC or HRCC even
   by accident. Put them back afterwards.
3. **Seed a realistic session first.** WX0MIK's own data is titled "fdsa fsadasd" with no FRNs, no
   results and nothing filed — unusable. A fictional session (plausible names, FRNs, payments,
   results, an ARRL filing) makes every shot possible, including the Quickstart's verify state.
   Back up the dev database first, and restore it after.
4. **Strip three dev-only banners from the DOM before capturing** — `TEST MODE`, `INGESTION STALLED`
   (the Worker is not running locally) and the two-factor nag (specific to the seeded account).
   ⚠️ Remove the *element*, never the setting: turning test mode off would let real email escape.
5. **Park the mouse pointer** off the content first. A stray cursor in a documentation screenshot
   reads as sloppiness.
6. **Reveal hidden state client-side, without saving.** FCC Status hides its sub-switches until the
   master is on; ticking the checkbox in the DOM and *not* submitting shows the real page with
   nothing persisted. Confirm afterwards against the database.

Doing this pass found two live layout bugs that no test could see. It is worth repeating
occasionally even when no new images are wanted — see `docs/responsive-ui.md`.
