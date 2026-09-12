# Brand assets

The mark and wordmark are Mike's (2026-09-12), handed over as SVG. Everything the site serves is
derived from those two files.

| File | What | Source |
|---|---|---|
| `wwwroot/img/veops-icon.svg` | The mark: amber rounded square, radio waves over a transceiver | as delivered |
| `wwwroot/img/veops-logo.svg` | Mark + "VE Ops" wordmark on one line | as delivered, **not used by the site** — see below |
| `wwwroot/favicon.ico` | 16/32/48 px, PNG-in-ICO | rasterized from the mark |
| `wwwroot/img/apple-touch-icon.png` | 180 px, iOS home-screen bookmark | rasterized from the mark |
| `wwwroot/img/icon-192.png`, `icon-512.png` | Android / PWA icons, listed in `site.webmanifest` | rasterized from the mark |
| `wwwroot/site.webmanifest` | Name, icons, theme colour (`#2A2E31`, the chassis) | hand-written |

Both layouts link the ICO (legacy), the SVG (`type="image/svg+xml"`, what modern browsers pick),
the touch icon and the manifest. The brand in the chassis is the SVG at 22px beside the text
"VE Ops" — HTML text, not the wordmark file.

## Why the wordmark file is not on the site

`veops-logo.svg` sets its text in `<text font-family="JetBrains Mono …">`. Loaded as an `<img>`,
an SVG cannot reach the page's web fonts, so the wordmark would render in whatever monospace the
device has — different on every machine, and never the one the site uses. The brand is therefore
the mark plus real HTML text, which *does* use the site's fonts. The file is kept as the reference
for print/other uses; if it is ever needed as an image, convert the text to outlines first.

The two PNG renderings that came with the SVGs (with a white drop-shadowed frame) are not used:
the SVG is the same drawing with no frame, which is what a favicon and a nav-bar mark need.

## Regenerating the rasters

No image library is installed on the dev machine; the PNGs were made with headless Chrome, which
renders the SVG exactly as the site will:

```
chrome --headless=new --disable-gpu --hide-scrollbars --default-background-color=00000000 \
       --window-size=512,512 --screenshot=icon-512.png file:///…/frame.html
```

where `frame.html` is a page whose only content is `<img src="icon.svg">` stretched to
`100vw × 100vh`. Repeat for 16, 32, 48, 180 and 192. The ICO is the 16/32/48 PNGs wrapped in an
ICO directory (a ~15-line Python script; PNG-in-ICO is valid since Vista and every browser reads
it). `temp/` at the repo root is git-ignored and is where files like this are dropped by hand.
