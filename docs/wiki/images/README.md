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
