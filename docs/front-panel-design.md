# The "front panel" design

The visual pass of 2026-09-12 — what changed in `app.css`, why, and what to keep in mind when
adding a screen. The typeface half of the story is in `docs/typography.md`; this is the layout,
colour and chrome half.

## Why

The original design (Phase 9's Claude Design handoff, see `docs/session-manager-ui.md`) reached
for an *instrument panel* — dark chassis, amber, monospace labels — and delivered it through
generic dashboard chrome: a near-black nav band with pill-highlighted links, every label set as
letterspaced uppercase mono, every table and panel boxed in a rounded card. Swapping the typeface
(Inter for Plex, 2026-09-11) changed nothing anyone could see, which was the point: the look was
carried by the chrome, not the letterforms. Mike: *"I don't really see much difference."*

The direction is the same instrument, less costume. The subject's own materials — the front
panel of a transceiver (brushed grey, one amber-lit readout), the paper logbook — are where the
choices come from.

## The rules

**One accent, spent in one place.** Amber (`--amber`) is the accent, and it appears as the 3px
rule across the top of the chassis (a power-LED strip), the underline on the active nav item, and
the fill of `.btn-primary`. That is all. Green/red/blue are *status* colours and do not count.
Anything new that wants to be noticed gets amber only if it is the one action a screen leads with.

**Graphite, not black.** `--panel` is `#2A2E31` in light mode — warm graphite, visibly not the
paper and visibly not black — and the bar is 49px, not 56. In dark mode the chassis and the paper
are close (`#1A1E21` on `#121517`); the amber rule is what separates them.

**Mono is for data.** `"JetBrains Mono"` appears on: `.mono`, `.count`, `.stat-value`, the
message editor's HTML view, placeholder chips, and the 2FA key/URI. Every other former mono site
— nav, brand, eyebrows, filters, pagination, crumbs, footer, chips, table headers, form labels,
card-mode labels, `.who`, `.session-sub` — is Inter now. When a new value is an identifier, a call
sign, an FRN, an amount, a count or a time, give it `.mono`; when it is a word, don't.

**Sentence case, no tracking.** No `text-transform: uppercase` remains in `app.css`. An `.eyebrow`,
a `thead th`, a `.field label`, a `table.cards td::before` label are all 12.5px/600 in
`--ink-soft`. A label is a small line of text, not a stencil.

**Ruled rows, not cards.** `table` has no background, border or radius. `thead th` is a rule
(`--rule`), not a tinted band (`--thead-bg` is `transparent` and kept only so nothing else has to
change). First and last columns have no side padding, so a table's edges line up with the heading
above it and it reads as part of the page rather than a widget on it. The last row keeps its bottom
rule. Card mode (`table.cards` below 768px) is unchanged apart from its label style — a phone
still gets one card per row, because that is the layout that works there.

**Filters are tabs on a rule.** `.filters` carries a bottom border; `.filter-pill` is an underlined
tab whose count (`.pill-count`) is a quiet second word in `--ink-faint`, not a badge. Everything
else that lives in a filter bar — `.ve-search`, `.filter-select`, `.filter-dropdown`, `.toggle-pill`
— is a plain 5px-radius bordered control lifted 6px so it sits above the rule. The Sessions list
has no tabs (its filters are all dropdowns), so there it reads as a row of controls over a rule,
which is fine.

**Session header is a facts list.** `.session-panel` lost its card: it is the heading, a
two-column `.meta-grid` of label-beside-value pairs (`.meta-item .k` is a fixed 118px column),
and a bottom rule. `.flag-banner` is a left-ruled amber notice, not a bordered box.

**What kept its card.** `.ve-panel`, `footer.roster`, `.ve-add`, `.ve-action-bar`, `.auth-card`,
`.modal`, `.menu` — things that are genuinely separate objects (a form, a sticky bar, a popover, a
sign-in box) or sit *beside* the flow rather than in it. The rule of thumb from the design pass:
border, fill and radius say "separate object"; spend them where that is true.

## What did not change, and why

- **The markup.** This was a CSS pass. The only Razor edits were the brand text (`VE OPS` →
  `VE Ops`, since the brand is no longer letterspaced mono caps) and a `.mono` span around the VE
  call sign on Session Detail's roster chips. *(Later the same day the brand gained Mike's mark —
  `wwwroot/img/veops-icon.svg`, also the source of `favicon.ico`, the Apple touch icon and the
  manifest icons; `docs/brand-assets.md`.)*
- **The responsive layer.** Card tables, the mobile accordion nav and the 768px breakpoint are
  as they were. The mobile panel keeps the *pill* highlight for the active link (a background on
  a stacked list reads better than an underline); the tab underline is desktop-only.
- **The specificity traps.** Every rule recorded in CLAUDE.md's Known Constraints
  (`.vesm button` / `.vesm a` at (0,1,1), the two-class rule) still applies and was respected —
  `.vesm a.btn-primary` restates its colour, `.vesm .nav-toggle` stays two classes deep.

## Verifying a change to this file

Every admin page is `[Authorize]`d, so the way to look at `app.css` without signing in is the
harness recipe in `docs/responsive-ui.md`: the real stylesheet inlined into a page of
representative markup, served from a throwaway local HTTP server, screenshotted in both themes.
**Strip the BOM when inlining.** `app.css` starts with U+FEFF; inside a `<style>` block that
character is an ident token, so the first selector becomes `﻿:root` and matches nothing —
every light-mode token silently disappears while dark mode (its own `[data-theme]` block) looks
fine. Read the file with `utf-8-sig`, not `utf-8`. Cost half an hour on 2026-09-12.
