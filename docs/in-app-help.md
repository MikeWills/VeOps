# In-app help (`/Help`)

The user manual in [`docs/wiki`](wiki/README.md) is readable inside the app at `/Help`, from the
**Help → Documentation** menu, as well as on the GitHub wiki. Same files, two readers — there is
still exactly one master copy, and an edit to a page in `docs/wiki/` reaches both on the next
merge (the wiki through `publish-wiki.yml`, the app through the next tagged release).

## How it works

- `VeOps.Web.csproj` copies `docs/wiki/**` into the build output as content under `Help/`
  (`images/README.md` excluded — it is authoring guidance, not a page). The files ship inside the
  release like `appsettings` does; nothing reads the repository at runtime.
- `HelpPages` (Web) reads `Help/<name>.md` from `AppContext.BaseDirectory`, renders it with
  [Markdig](https://github.com/xoofx/markdig) (pipe tables, GitHub-style heading ids, autolinks,
  **raw HTML disabled**), and rewrites links the way `scripts/build_wiki.py` does for the wiki:
  sibling `Page.md` → `/Help/Page` (`README.md` → `/Help`), `../x.md` → the absolute GitHub URL for
  the rest of `/docs`, `images/x.png` → `/Help/images/x.png`. Anchors and absolute URLs pass
  through untouched.
- Two Razor Pages, both `[AllowAnonymous]` because the wiki is public and the candidate guide is
  for people who cannot sign in: `Pages/Help/Index.cshtml` (`/Help/{name?}`) and
  `Pages/Help/Image.cshtml` (`/Help/images/{file}`). A signed-in reader gets `_AppLayout`, a
  signed-out one `_PublicLayout` — same page either side of the login.
- Names are validated, not sanitised: a page name must match `^[A-Za-z0-9-]+$` and an image
  `^[a-z0-9-]+\.png$`, and anything else is a 404 before a path is ever built. The smoke crawl
  supplies `Glossary` / `session-detail.png` for the two route parameters.

## Things to keep in step

- **The sidebar order, titles and group headings live in two places**: `ORDER`/`TITLES`/`HEADINGS`
  in `scripts/build_wiki.py` and `Order`/`Titles`/`Headings` in `HelpPages.cs`. Both append an
  unlisted page alphabetically at the end rather than hiding it, so forgetting is visible, not
  silent.
- The route value is `name`, not `page`: `page` is the key Razor Pages itself uses to select the
  page, and a template that reuses it never matches — the first version 404'd on every request
  for exactly this reason.
- `_PublicLayout`'s `<main>` is 760px wide by default; `/Help` asks for 1160px through
  `ViewData["MainMaxWidth"]` because of the sidebar.

## Tests

`HelpPagesTests` (Web): the landing page renders signed out; sibling links point at `/Help/...`
and no relative `.md` survives; `../` links point at GitHub and images at the image route; an
image serves as `image/png`; unknown and traversing names 404; the Help menu links to `/Help`;
and **every page in `docs/wiki` renders with no dead sibling link** — a dead link used to be found
by a reader.
