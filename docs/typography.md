# Typography

**Inter** for text, **JetBrains Mono** for the chrome and the data — both self-hosted, both variable
fonts. Swapped in on 2026-09-11 from IBM Plex Sans / IBM Plex Mono, which had been the pairing since
the design handoff; Mike: *"I'm not a fan."* Chosen from a side-by-side of four pairings rendered on
the same Sessions list (Inter/JetBrains Mono, Source Sans 3/Source Code Pro, Public Sans/Plex Mono,
and the original) — Inter for the tall x-height that holds up at the 12–14px this app sets nearly
everything in, JetBrains Mono because it reads as a *typeface* rather than a terminal at those sizes.

## Where each one goes

**Mono is for data only** (since the 2026-09-12 front-panel pass, `docs/front-panel-design.md`).
The day before, "mono everywhere" had been offered as a separate toggle beside the face choice and
kept; it lasted one day, because the swap of faces alone changed nothing anyone could see — the
mono chrome was what defined the look.

- **Inter** — everything that is a word: body, headings, labels, table headers, nav, filters,
  chips, pagination, crumbs, footer, form controls.
- **JetBrains Mono** — everything that is a value: `.mono`, `.count`, `.stat-value`, identifiers,
  call signs, FRNs, amounts, times; plus the message editor's HTML view and the 2FA key/URI.

`app.css` names the families directly rather than through a custom property — the only `--mono`
in the file is a fallback on the two copy-control rules. Seven mono sites remain; a future change
of face is a `sed`.

## How they are loaded

`wwwroot/lib/fonts/` holds four `woff2` files (each face split into the same `latin` and
`latin-ext` subsets Google Fonts serves, so a page pulls only what it uses — 48 KB + 31 KB for a
plain-ASCII page), `fonts.css` with the `@font-face` rules, and the two SIL OFL license texts.
Both layouts link `fonts.css` before `app.css`, same as `bootstrap-icons.css`.

Self-hosted for the same reason the icons are: the CSP's `font-src` is `'self'`. The Google Fonts
allowances (`style-src … https://fonts.googleapis.com`, `font-src … https://fonts.gstatic.com`) were
removed from `Program.cs` in the same change, so a page load now makes **no third-party request** —
which also means no font-loading dependency on an outside host, and nothing for a candidate's
browser to leak to Google on the public pages.

The files were fetched from Google Fonts' own CDN (the variable-font builds it serves to a modern
browser, v20 of Inter and v24 of JetBrains Mono) rather than from each project's GitHub release,
because the CDN's subsetting is already done and matches the `unicode-range` blocks in `fonts.css`.
To bump a version: request `https://fonts.googleapis.com/css2?family=Inter:wght@400..700&family=JetBrains+Mono:wght@400..600`
with a current-Chrome `User-Agent`, take the `latin` and `latin-ext` URLs, and replace the four
files — the `@font-face` rules only change if the `unicode-range` blocks do.

## Weights

`fonts.css` declares `font-weight: 400 700` (Inter) and `400 600` (JetBrains Mono) — the ranges
`app.css` actually uses. A weight outside those ranges renders synthetically bolded/lightened by
the browser rather than from the font, which looks wrong rather than failing, so extend the range
(and re-fetch with a wider `wght@` axis) before using one.
