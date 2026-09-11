# Responsive / mobile-first UI

Until 2026-08-05 the admin UI was desktop-only **by construction**: `wwwroot/css/app.css` was 307
lines of hand-rolled design system containing **zero media queries**. The viewport meta tag was
present in both layouts, so a phone rendered the page at device width and everything simply
overflowed — a 56px fixed-height chassis holding brand + five nav items + three dropdowns, and up to
sixteen table columns, all on a 390px screen.

This pass makes the whole site work on a phone. It is a CSS/JS change plus one markup class per
table; no page model, service, or query was touched.

## The breakpoint, and why the base layer is the mobile one

There is one breakpoint, **768px**, written as `max-width: 767.98px` / `min-width: 768px` so the two
can never both match at a fractional viewport width.

`app.css` is now genuinely mobile-first: everything above the "Responsive layer" banner near the
bottom of the file **is the phone layout**, and it is what a device gets with no media query
evaluated at all. Two layers sit on top:

1. **Card tables** (`max-width: 767.98px`) — a treatment that only exists below the breakpoint.
2. **Desktop layer** (`min-width: 768px`) — restores the original single-row chassis and the roomier
   desktop spacing and type scale.

The desktop layer is a deliberate *restoration*, not a redesign: the design that was already live
and in daily use is unchanged from 768px up. If a desktop rule looks like it is only there to undo a
mobile rule, that is exactly what it is.

There is no `--bp-md` custom property, despite the comments using that name. Custom properties
cannot be used in a media query condition, so a token would be a decoration the queries themselves
could not reference.

## The chassis nav

Below the breakpoint the header bar carries only the brand and a `☰` toggle. The nav links **and**
the `.who` cluster (user menu, Help, theme toggle) are one collapsed panel that
`header.chassis.nav-open` reveals; `app.js` toggles the class, keeps `aria-expanded` in step, and
closes the panel on Escape or a tap outside the header.

Two things inside the panel are easy to get wrong:

- `.nav-group` / `.user-menu` / `.help-menu` must be **`flex-direction: column`** on mobile. Once
  their `.menu` becomes `position: static` it is a real sibling of its trigger in that flex
  container, and the default row direction lays the menu out *beside* the button instead of under
  it. This shipped broken in the first draft and was caught in the harness.
- The chassis dropdowns become inline accordions rather than floating menus. A `position: absolute`
  dropdown inside a stacked panel overlays the links beneath it.

### Only viewport-positioned menus close on scroll

A row menu inside `.table-scroll` is lifted to `position: fixed` so it can escape the wrapper's
clipping, which means it no longer travels with its row — so any scroll has to close it.

**That must not apply to the chassis nav's menus.** They are `position: static` inside the collapsed
mobile panel and scroll with the page perfectly well. Closing every open menu on scroll made the
Settings menu unusable on a phone: it shut on the smallest drag, before anyone could reach an item
(reported 2026-08-06). The scroll handler now closes only menus carrying the inline
`position: fixed` that marks a lifted one.

> **Resize needed the same treatment, for a reason that is easy to miss.** On mobile, scrolling hides
> and shows the browser's URL bar, and that fires `resize` with a changed *height*. So a handler that
> closed menus on any resize reproduced the identical symptom — and would have survived fixing the
> scroll handler alone. Resize now acts only when the **width** changes, which is orientation or a
> real window resize, never the URL bar.

## Tables: two treatments that stack

Every data table in the app — all fifteen, Session Manager and Admin alike — carries **both**:

| Treatment | What it does | When it applies |
|---|---|---|
| `class="cards"` on the `<table>` | Each row restacks into a labelled card | Below 768px |
| `.table-scroll` wrapper `<div>` | Table scrolls sideways inside the wrapper instead of forcing the page to | Any width where the table is wider than its container |

These are not alternatives, and the reason is a band that is easy to miss. Cards only exist below
768px, so between 768px and roughly 1100px — a tablet, a split-screen window, a small laptop — a
16-column table is a real table again with nothing catching its overflow. The first version of this
work shipped with the SM tables unwrapped and had exactly that bug. The wrapper covers that band;
cards cover below it.

> **`min-width` must be scoped `:not(.cards)`.** The wrapper's `min-width: 680px` floor is what makes
> a table scroll rather than compress its columns into slivers — but in card mode the table is
> block-level, and a 680px floor pushes it straight off the side of a phone, reintroducing the exact
> horizontal page scroll this all exists to remove.

Admin tables were initially left on scroll-only, on the theory that configuration screens are rarely
opened on a phone. That turned out to be wrong in practice — the real use case is fixing something
quickly while away from a computer — and they moved to cards on 2026-08-05. Because the labels are
generated rather than hand-written, that change was one word per table.

### Labels come from the `<th>` at runtime

`app.js`'s `labelCardTable` stamps each `<td>` with `data-label` taken from its own column's `<th>`,
and the CSS renders it via `content: attr(data-label)`. Hand-writing `data-label` on every cell would
have meant hundreds of attributes to add and to keep in step with the headers forever after.

Two cases are marked rather than labelled:

- `.is-unlabelled` — the column has no header text (the View / `⋮` action columns). The cell drops
  the empty label gutter and uses `display: inline-flex`, so consecutive action cells sit together
  on one final row instead of each taking a row.
- `.is-blank` — the cell rendered nothing. A desktop table needs the empty cell to keep its grid
  aligned; a card row reading `FRN —` with no value is noise. Emptiness is tested on text content
  **and** the absence of `a, button, input, select, svg, img`, or a cell holding only an icon button
  would be judged empty and hidden.

### The grid trap inside a card cell

A card cell is `display: grid` with a label column and a value column. Grid auto-placement sends the
*second* child of a cell back to **column 1, directly under the label** — and a two-line cell like
the Sessions list's title + sub-line is exactly that. `table.cards td > *` therefore pins every
element child to `grid-column: 2`. The same rule carries `justify-self: start`, without which every
status chip stretches into a full-width bar, since a grid item fills its column by default.

Text-only cells still auto-place correctly into column 2 and need no help.

#### ...but a cell holding *both* text and an element splits across two lines (2026-09-09)

The two rules above interact badly in one specific shape. A cell written as a bare value followed by
a trailing affordance —

```html
<td class="mono">
    @row.CallSign
    <a href="@row.LicenseUrl"><i class="bi bi-arrow-up-right"></i></a>
</td>
```

— has *two* grid items: the anonymous text run, and the anchor. The text auto-places into column 2
first; `td > *` then pins the anchor to column 2 as well, and grid puts it on the **next row**. The
result is the ULS link stranded on its own line underneath the call sign it annotates. Reported from
a phone against Applicant Status; the same shape was in four cells — Applicant Status' FRN and call
sign, VE Directory's duplicate-call-sign warning, and Renewal Monitor's "days left" pill.

The fix is not a CSS override. Stacking the second child is *correct* for the case the rule was
written for, and cannot be told apart from this one by the CSS, because the difference is semantic:

| Shape | Belongs |
|---|---|
| A value and something that annotates **that value** — call sign + ULS link, expiry + days pill | one line |
| A separate fact about the row — a sub-line under a session title, a "Muted" chip under a team name | its own line |

So the markup says which it is. Wrapping the pair in **`<span class="cell-inline">`** makes it a
single grid item whose contents flow inline:

```css
table.cards td > .cell-inline { display: inline-flex; align-items: baseline; flex-wrap: wrap; gap: 6px; }
```

Above the breakpoint the span is inert, so there is no desktop risk and nothing to undo in the
desktop layer.

**Why it survived this long:** Chrome enforces a ~500px minimum window width, so no amount of
resizing on a dev machine reproduces it. It shows up on a real phone, or in the sized iframe of the
harness below — which is how the fix was verified, with an unwrapped control cell in the same table
to prove the harness could still see the bug (`textTop=154 affTop=184` before, overlapping after).

### Sorting

`thead` is hidden in card mode, so **client-side sorting is unavailable on a phone** for the tables
that use it. The sort still applies if one was remembered from a desktop visit (it is stored in
`localStorage` and reorders rows, which cards inherit). This is a known, accepted limitation rather
than an oversight — surfacing a sort control per card was not worth the complexity for screens whose
result sets are already filtered and paged.

## The session header lined up (2026-09-10)

Reported from the session page: *"The alignment is all over the place."* Nine metadata items, nine
different left edges, and the second row landing wherever the first happened to wrap.

The cause was in the **desktop** layer, not the mobile one. The base layer is a two-column grid, but
`min-width: 768px` replaced it with `display: flex; flex-wrap: wrap`, which sizes every item to its
own content — so no item shares a left edge with the item above it, by construction. It is the same
class of bug as a table rendered with tabs.

Three changes, each fixing a distinct kind of raggedness:

| Symptom | Fix |
|---|---|
| Items at nine different left edges | `grid-template-columns: repeat(auto-fit, minmax(230px, 1fr))` |
| A figure beside a button riding lower than the figure next to it | `.meta-item .v` gets `min-height: 30px` and centres its content |
| One cell twice the width of its neighbours | "Edit" became an `.icon-action` pencil |

**230px is not arbitrary** — it is the widest value any of these carries (`$15.00 exam · $7.00
retained`, and a figure followed by a chip and a button). At the 1160px shell that settles on four
columns with nothing wrapped; below ~1000px it drops to three rather than squeezing values onto a
second line. `auto-fit` rather than a fixed count for exactly that reason.

**30px is `.icon-action`'s own height** (14px glyph + 7px padding either side + border). Change one
and the other has to move, which is why both say so.

`.meta-grid` is shared with the ARRL filing page, so that became a tidy 4×2 at the same time —
checked deliberately rather than discovered later.

### Verifying it

The harness below, at the real 1160px shell width rather than the browser's full width — the first
pass measured at 1905px and made every option look better than it was. Assertions were on **distinct
left edges** (nine before, four after) and on the spread of value centres within a row (0–1px after).

⚠️ **One measurement trap, worth knowing before reusing it.** Wrapping was being detected with
`el.getClientRects().length > 1`, which stopped working the moment `.v` became a flex container: a
flex box has one client rect however its children wrap, so the check reported "no wrapping" while the
phone screenshot plainly showed the pencil on its own line. A layout assertion can be invalidated by
the very change it is meant to verify — look at the render as well.

At 390px the header is unaffected by any of this (the card layer is a separate rule): two columns, no
horizontal page scroll, nothing clipped, and the pencil is a 34×31px touch target.

## iOS zoom — why controls are 16px on mobile

iOS Safari auto-zooms the viewport when a control with `font-size` below 16px receives focus, and it
never zooms back out. Every focusable control is therefore 16px on mobile and drops to the design's
12–14px at the breakpoint.

This is why the repeated `style="… font-size:12px …"` attributes on the Sessions and VE Roster
date-range inputs, and the Unmatched Payments candidate `<select>`, were replaced with a
`.menu-input` class: **an inline `font-size` cannot be overridden by a media query**, so those
controls were unreachable while the style stayed inline. The rest of the site's ~139 inline `style=""`
attributes are `max-width`, which is mobile-safe, and were left alone (they are also load-bearing for
the CSP `style-src` allowance — see `docs/security-hardening-2026-08-03.md`).

Touch targets were raised to roughly 44px on mobile: nav links, kebabs, menu items, buttons and
filter pills all gained padding, reverting to the tighter desktop values above the breakpoint.

## Verifying responsive changes (the framing gotcha)

**The app cannot be loaded in an iframe.** The 2026-08-03 hardening pass sends `X-Frame-Options:
DENY` and CSP `frame-ancestors 'none'`, so the obvious approach — framing `localhost:5158` at 390px
to evaluate media queries — fails silently with a broken-image placeholder. Do not weaken those
headers to test layout.

Chrome also enforces a minimum window width of roughly 500px, so `resize_window` cannot produce a
true phone viewport either.

What works, and what was used here: a **self-contained harness** — an HTML file with the real
`app.css` and `app.js` inlined and representative markup copied from `_AppLayout.cshtml` and the page
being checked, served over a throwaway local HTTP server and loaded into iframes sized 390px and
1180px. Media queries evaluate against the iframe's own viewport, so this gives a true mobile render
of the actually-shipped CSS and JS, with no login required. The harness can also assert rather than
just look — the admin-table check read back `documentElement.scrollWidth === clientWidth` (page never
scrolls sideways) and `wrapper.scrollWidth > wrapper.clientWidth` (the table does).

One escaping trap: if the harness is injected via `srcdoc` or a JS string, its own `</script>` ends
the host page's script block early. Write the harness to its own file and use `src=` instead.

The harness can be pointed at any page shape. The admin pass used a Users-table harness rendered at
390px **and 900px**, asserting `pageOverflowsHorizontally === false` at both while
`wrapper.scrollWidth > wrapper.clientWidth` only at 900px — which is precisely the tablet-band
regression described under "Tables" above.

### ⚠️ The harness must reproduce the CONTAINING BLOCK, not just the component

This is the trap that actually caught someone, so it goes above the general caveat below.

#541 turned the session header's `.meta-grid` from a wrapped flex row into an `auto-fit` grid. The
harness rendered `.meta-grid` inside a plain `<div>`, proved four columns, and shipped. On the real
page `.meta-grid` lives inside `.session-panel`, which is a **flex row** — and while the grid was a
wrapped flex row its max-content width was enormous, so its parent filled the panel *by accident*.
As a grid its max-content fell to ~396px and the entire header silently collapsed to one column at
full desktop width. It ran in production from `v0.39.1` until somebody looked at the page.

A component's layout is a function of its parent as much as itself. **Copy the real ancestor chain
into the harness** — the panel, its `display`, its `gap`, its `flex-wrap` — or the harness proves
only that the component works in isolation, which was never in doubt.

The same applies to measurement, not just rendering. A wrapping check written as
`el.getClientRects().length > 1` stopped working the moment `.v` became a flex container: a flex box
has one client rect however its children wrap, so it reported "no wrapping" while a phone screenshot
plainly showed otherwise. **A layout assertion can be invalidated by the very change it verifies.**

### What is *not* verified

Every Session Manager and Admin page is `[Authorize]`d, and Claude does not enter the dev password
(see CLAUDE.md). The harness proves the CSS and JS mechanisms against real markup; it does not prove
each authenticated page's own content renders well with real data. The remaining check is a
logged-in pass over Session Detail, Applicant Status, Candidate Detail and Team Settings at phone
width.

**Two live bugs were found doing exactly that pass on 2026-09-10** — the collapsed header above, and
the ARRL filing page rendering with the signed-out layout because it never set
`Layout = "_AppLayout"`. Neither was catchable by the harness (both were bugs in the page's context)
nor by `PageSmokeTests` (a page with the wrong layout still returns 200 and contains its expected
strings). A periodic logged-in look at the real pages is the only thing that covers this class, and
it is worth doing even when no screenshots are needed.

## Anchor-buttons were never styled as buttons (2026-08-16)

Reported as "the email button is smaller compared to the refresh candidates button". Measured in a
browser rather than eyeballed, and the difference was bigger than the report: an
`<a class="btn-secondary btn-sm">` came out **weight 400, no border, no border-radius,
`display: inline`**, beside an identically-classed `<button>` that had all four.

The cause is the same specificity trap this file and CLAUDE.md already record twice. The base rule is
`.vesm button, .vesm .btn { … }`, and **every anchor-button in the app is written
`class="btn-secondary btn-sm"` with no `.btn`** — 24 of them. They matched no base rule at all, so
they inherited plain link styling and picked up only `.btn-sm`'s font-size and padding.

Fixed by naming the variants in the base rule: `.vesm button, .vesm .btn, .vesm .btn-primary,
.vesm .btn-secondary, .vesm .btn-danger`. The `border-color` rules for secondary and danger must stay
**after** it — they are now equal specificity, and the base rule's `border` shorthand would otherwise
leave every secondary button borderless, which is the exact regression the comment above them
describes.

**Found while measuring, not fixed:** `.btn-sm` does nothing to a `<button>`. `.vesm button` is
(0,1,1) and `.btn-sm` is (0,1,0), so a small button and a full-size one measure identically —
`13px | 600 | 9px 16px` for both. Every "small" button in the app is full size and always has been.
Fixing it would resize buttons on every screen, which is a visual change nobody asked for, so it is
recorded here rather than done quietly.
