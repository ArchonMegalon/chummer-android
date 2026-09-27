# User-supplied Troll icon — 2026-09-24

The user replaced the generated head below with an exact, full-body SVG.
The current launcher foreground is
`src/Chummer.Android/Resources/AppIcon/appiconfg.svg` (208 paths, 14 gradients).
The original artwork was copied unchanged from the user's message, with only a
final newline added. Original SHA-256:
`404d590668e91f9330463a799e8bd34dc9619fdaa4f42a158b5c751f39ba64d0`.
There are no external resources, scripts or embedded raster images.

On 2026-09-27 the user requested a slightly brighter SVG. Only RGB fill and
gradient-stop channels were lifted using `round(255 * (channel / 255)^0.82)`;
geometry, transparency, gradient positions and layer order are unchanged.
This lifts shadows and midtones while retaining the green/teal palette and
near-white highlights. The splash SVG is identical to the launcher foreground.
Current master SHA-256:
`1258f544b17111d1424dc42ea7dacba04dd9515cd4156d98236ee046a63cabb9`.
The 512×512 Play export was regenerated with librsvg/Cairo; launcher-scale
48×48 and 96×96 renders were visually checked. This brightness revision has
not itself been uploaded to Play or verified in an installed app.

MAUI consumes the SVG directly, retaining the full figure and transparent
surroundings. Keep the separate opaque `#102426` background and foreground
scale 0.78, as declared by the current MAUI project. Android owns the
round/squircle mask; this value describes the launcher, not the Play asset.
The Play icon uses the same supplied artwork, rasterized by librsvg/Cairo to
opaque 512×512. A standard librsvg CLI equivalent for regeneration is:

```sh
rsvg-convert --width 512 --height 512 --background-color '#102426' \
  --output play/assets/app-icon-512x512.png \
  src/Chummer.Android/Resources/AppIcon/appiconfg.svg
```

This supersedes the generated PNG as the active icon. Its historical source
and prompt remain below; they do not describe the current launcher asset.
Do not use ImageMagick's internal SVG renderer for this master: it renders
some of the supplied gradients black. MAUI Resizetizer 10.0.20 successfully
generated all five Android density sets and adaptive XML from the exact SVG.
The prior unsigned Preview 29 AAB did not contain this change; that is a
historical build observation, not an instruction to rebuild a consumed release.
On 2026-09-26 the then-current 512x512 PNG was uploaded to the Chummer default
Play listing and its icon-only change was saved and submitted for review.
Preliminary checks were still running; a live store icon or physical installed
icon is not yet proved. See the [store-artwork observation](../evidence/troll-store-icon-review-20260926.md).
Preview 41 remains unchanged; store artwork does not require another AAB.

## Historical generated head — 2026-09-22

Requested by the user: replace the S/hexagon app icon with a stylized troll.
Generated with the built-in image-generation tool, not the API/CLI fallback.
The generated transparent PNG is the launcher foreground master at
`src/Chummer.Android/Resources/AppIcon/appiconfg.png`. It is raster artwork, not
an SVG tracing. The existing dark green, mint and off-white palette is retained.

## Generation prompt

Use case: logo-brand. Asset type: final transparent foreground mark for the Chummer Android launcher and Play app icon. Create ONE original, highly stylized urban-fantasy TROLL HEAD, front-facing, centered, broad powerful square jaw, two short thick outward/upward horns integrated into the silhouette, pointed ears, two unmistakable ivory lower tusks curving upward, heavy brow and alert intelligent eyes. Express tough but approachable confidence, not a monster scream. Crisp flat vector-like geometry, substantial solid shapes and negative space, extremely legible at 48 pixels, refined professional app identity rather than detailed character concept art. Reuse the app's established palette: mint #54d6b3, off-white #f3f7f4 and deep green #102426 for face shadows and facial negative spaces. No other colors. Square 1024x1024 composition with the entire troll head fitting in the central 640x640 area (about 62 percent of canvas), fully inside that safe area including horns, ears and chin. GENERATE ACTUAL TRANSPARENT BACKGROUND with alpha; no square tile, no circle, no border, no shield, no hexagon, no pedestal. The background is transparent everywhere outside the head and inside deliberate cutouts. Head only; no body, no shoulders, no props, no weapons, no scenery. Minimal large planar facial shapes, no fine texture, no hair strands, no gradients, no glow, no 3D rendering, no fine outlines. No lettering, no number, no S monogram, no text, no watermark. Original design, not a copy of an existing mascot. Output a single finished icon foreground, not a design board or multiple variants.

## Mechanical export and launcher composition

The actual returned image is 1254×1254 with alpha, not the requested 1024×1024.
Retain the generated alpha. The foreground is scaled to 0.60 by MAUI to provide
adaptive-mask safe space; do not paint a rounded tile into the foreground.
The background SVG is a full opaque `#102426` square with no S or hexagon.

Store export, run from repository root with ImageMagick:

```sh
convert src/Chummer.Android/Resources/AppIcon/appiconfg.png \
  -resize 512x512 -background '#102426' -alpha remove -alpha off \
  play/assets/app-icon-512x512.png
```

No app-version bump, signing, upload, or installed-icon claim is implied by
this asset change. The old SVGs remain recoverable from Git history.
