# Play graphics

- `app-icon-512x512.png` is the square, opaque stylized-troll store icon.
  Its transparent source is `src/Chummer.Android/Resources/AppIcon/appiconfg.png`
  (relative to the repository root), generated with the built-in image tool.
  The launcher uses that same foreground over a solid dark-green background,
  scaled to preserve horns and tusks inside adaptive masks. Play applies its own
  mask to the store PNG. Prompt and export commands: `troll-icon-prompt.md`.
- `feature-graphic-1024x500.png` is the upload-ready Play feature graphic.
- `feature-graphic-source.png` is the uncropped generated source retained for
  traceability.
- `screenshots/phone-01-home.png` through `phone-05-campaign.png` are real
  1080×2400 API 36 emulator captures of the native UI covering home, build,
  the direct new-runner workflow, table play, and campaign navigation.
- `screenshots/tablet-01-home.png` through `tablet-04-native-tools.png` are
  real 1440×2560 (9:16) API 36 emulator captures of the native UI covering
  home, build, the new-runner flow, and native tools. Mockups and generated
  interface screenshots are forbidden.

The feature graphic was generated with the built-in image-generation tool from
an original, no-text Chummer product-art prompt. Final SHA-256:
`cc741a76cbffc5690fa30918835c5f4d8cfd5a29541989f667e4b08f78da5288`.

The graphic dimensions follow the current official Play asset contract:
https://support.google.com/googleplay/android-developer/answer/9866151.

These are source assets for the next build. Updating them does not change an
already-installed app or constitute a Play listing upload.
