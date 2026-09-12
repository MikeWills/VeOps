# Typography

**Inter** for text, **JetBrains Mono** for the chrome and the data — both self-hosted, both variable
fonts. Swapped in on 2026-09-11 from IBM Plex Sans / IBM Plex Mono, which had been the pairing since
the design handoff; Mike: *"I'm not a fan."* Chosen from a side-by-side of four pairings rendered on
the same Sessions list (Inter/JetBrains Mono, Source Sans 3/Source Code Pro, Public Sans/Plex Mono,
and the original) — Inter for the tall x-height that holds up at the 12–14px this app sets nearly
everything in, JetBrains Mono because it reads as a *typeface* rather than a terminal at those sizes.

## Where each one goes

The split is unchanged from the Plex days, and it was offered as a separate choice and declined —
the question "which faces" and the question "how much of the chrome is monospace" were shown
side by side with a toggle, and the answer was "everywhere":

- **Inter** — body, headings, table cells, buttons, form controls, the nav links.
- **JetBrains Mono** — the *chrome*: brand mark, `.eyebrow`, `thead th`, filter pills, pagination,
  crumbs, footer, `.who`, chips, `.meta-item .k` labels; and the *data*: `.mono`, `.count`, IDs,
  call signs, FRNs, amounts.

`app.css` names the families directly (`"Inter", system-ui, sans-serif` /
`"JetBrains Mono", monospace`) at every site rather than through a custom property — the only
`--mono` in the file is a fallback on the two copy-control rules. That is 34 mono sites; a future
change of face is a `sed`, and a future change of *split* (mono for data only) is a rule-by-rule
edit, which is the harder and more interesting one.

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
