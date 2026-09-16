# Offline AAR input and consumed-cache layouts

The offline source feed remains exactly the five flat originals in
`eng/android-aar-inputs.lock.json`. Seeding accepts only that source shape and
creates a fresh, private cache with the same flat bytes. It never seeds
extracted state, completion markers, or locks.

`build-release.sh` supplies that cache as `XamarinBuildDownloadDir`. The pinned
`Xamarin.Build.Download` 0.11.4 task consumes `Kind=Uncompressed` items by moving
each archive, rather than retaining its flat cache copy. Verification therefore
accepts exactly one state for each pinned archive:

- `<fileName>` alone at the cache root; or
- `<stem>/<fileName>`, `<stem>.unpacked`, and `<stem>.locked`.

Here `fileName` includes the version and the `.aar` suffix (for example,
`appupdate-2.1.0.aar`), and `stem` removes that suffix. Every consumed directory
contains only its matching original. The marker is exactly the ASCII bytes
`This marks that the extraction completed successfully`, without a newline;
the retained lock must be empty. These sidecars are validated, never created by
verification. A mix of valid flat and consumed states is allowed: the five
package targets identify the paths but do not prove every archive must be
consumed in every successful/no-op invocation.

All five payloads remain mandatory and retain the same size, SHA-256, ZIP
safety, private ownership, no-follow, regular-file and single-link checks.
Missing, duplicate, case-aliased, mixed-within-one-archive, extra or pre-extracted
entries fail closed. Consumed directories and sidecars have bounded,
descriptor-relative custody checks. Layout diagnostics contain only bounded,
escaped expected/observed names, not archive contents or retained build state.

## Exact package evidence

This behavior was checked read-only in the admitted local NuGet package, not
assumed from a current upstream branch and not inferred from an SDK run:

- `xamarin.build.download.0.11.4.nupkg` SHA-256:
  `7747ae961bac494da8be41d2752a448ad743fbafa93c348cf4332cf4abb95435`.
- ZIP member `buildTransitive/Xamarin.Build.Download.dll`, 96192 bytes, SHA-256:
  `90f65c56af1e25f86124de8993712123d6473c8372494bf964430c8131780de0`.
- Static ECMA-335 metadata/IL inspection of
  `<MakeSureLibraryIsInPlace>d__33.MoveNext`, method token `0x060001a4`,
  RVA `0x6ce8`: IL `0x3c5` calls `System.IO.File.Move` with `CacheFile` and
  `Path.Combine(DestinationDir, Path.GetFileName(ToFile))`; IL `0x3d5` writes
  the exact marker above. The success branch leaves to IL `0x4d1`, passing
  stream disposal but skipping the later lock deletion at IL `0x49b`.
- `DownloadUtils.ObtainExclusiveFileLock`, token `0x060000d3`, RVA `0x46c0`:
  `File.Open(path, 4, 3, 0)` means OpenOrCreate, ReadWrite, FileShare.None;
  there is no DeleteOnClose option. After the task completes, the empty lock
  file remains, although the stream/lock is released.

The five pinned Google package `buildTransitive/net10.0-android36.0/*.targets`
all specify `Kind=Uncompressed`, `ToFile=<stem>.aar`, an empty `Sha256` metadata
field, and an `AndroidAarLibrary` path of `<stem>/<stem>.aar`. The canonical
verifier supplies the independent byte pins; it does not trust task markers as
evidence of payload integrity. No `.sha256` sidecar is expected for those empty
task metadata fields.

The original failure at `aar-originals-post-publish` was the old verifier's
flat-name requirement. The shell EXIT trap removed the transient release
directory, including that cache and staged output. This change neither recovers
that output nor authorizes replay of it. Synthetic tests model the exact task
layout and hostile variants; they are not an actual MSBuild task or release
qualification. The source change requires normal PR checks and fresh release
qualification; an older source-bound receipt does not apply.
