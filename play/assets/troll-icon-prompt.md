# Stylized Troll icon — 2026-09-22

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
