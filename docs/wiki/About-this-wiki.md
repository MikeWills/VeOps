# About this wiki

## This wiki is generated — do not edit it here

Every page you can see lives in
[`docs/wiki/`](https://github.com/MikeWills/VeOps/tree/main/docs/wiki) in the repository. That
folder is the master copy. A push to `main` that touches it republishes the whole wiki, overwriting
whatever is here.

**An edit made in the GitHub wiki editor will be silently destroyed by the next merge.** To change a
page, open a pull request against `docs/wiki/`. If that is more ceremony than you want,
[file an issue](https://github.com/MikeWills/VeOps/issues) saying what is wrong and somebody will
carry it.

## What belongs here, and what does not

| This wiki | `/docs` in the repository |
|---|---|
| How to *use* the app | How the app *works* |
| Read by a Session Manager on a Tuesday night | Read by whoever is changing the code |
| Roles, screens, tasks, vocabulary | Design decisions, API shapes, gotchas |
| | Runbooks — read with a terminal open |

The rule of thumb: **if it stops being true when the code changes, it goes next to the code.** A
page here that restates a design decision will drift out of date on its own, so it links to that
decision instead of repeating it.

## How the pages are organised

- **One page per role**, and each is deliberately short. It says what the role can do, in the order
  they will do it, and links onward.
- **One page per task**, shared across every role allowed to do it. There is no per-role copy of the
  same instructions — five parallel manuals is five things to keep in step, and they will not stay
  in step.
- **One page per fact.** Permissions are stated in
  [Roles and permissions](Roles-and-Permissions.md) and nowhere else; vocabulary is in the
  [Glossary](Glossary.md) and nowhere else. Everything else links to them.

Task pages get written when somebody actually needs one — usually the second time the same question
gets asked. [Tasks](Tasks.md) tracks which exist.

## File conventions

- **Flat folder, no subdirectories** apart from `images/`. The wiki has no folders; a page's name
  is its filename.
- **Hyphens become spaces** in the page title. `Guide-Team-Admin.md` shows up as *Guide Team Admin*.
- **`README.md` is the landing page.** It is published as `Home`, and it is also what github.com
  renders when somebody browses `docs/wiki/` — one file, right in both places.
- **Link to a sibling page with `.md`**: `[Team Admin](Guide-Team-Admin.md)`. The link works while
  browsing the repository, and the build strips the extension for the wiki.
- **Link into the rest of `/docs` with `../`**: `[deploy](../runbooks/deploy-a-release.md)`. The
  wiki is a different repository and cannot follow that, so the build turns it into an absolute URL.
- **Screenshots go in `images/`** and are written `![alt text](images/name.png)`. The build copies
  them into the wiki and repoints the link at the wiki's own raw host. See
  [`images/README.md`](https://github.com/MikeWills/VeOps/blob/main/docs/wiki/images/README.md) —
  it has the one rule that matters, which is never to screenshot real candidate data.
- **The sidebar and footer are generated**, not files here. Their running order lives in
  `scripts/build_wiki.py`; a new page appears in the sidebar without touching it, just at the
  bottom until it is given a place in the order.
- Anything a reader should not see does not go in this folder.

## The publishing mechanism

`.github/workflows/publish-wiki.yml` runs `scripts/build_wiki.py` on any push to `main` that
touches `docs/wiki/`, plus on demand from the Actions tab. The script empties the wiki checkout and
rewrites it, so a page deleted here disappears from the wiki rather than lingering with nothing
pointing at it.

It is the same mechanism as the sibling Course Ops repository, on purpose — two projects that
publish a wiki this way should not have two mechanisms to learn.

## Where to go next

- [Start here](README.md) — the landing page, if you arrived at this one from search
- [Tasks](Tasks.md) — which how-to pages exist and which are still to be written
- [File an issue](https://github.com/MikeWills/VeOps/issues) — the low-ceremony way to report
  something wrong on any page here
