# Local SR5 reputation wizard — implementation handoff

Status: local native components, not a registered application route or release authority.

This slice consumes the real Core reputation capability from semantic development
commit `2528bca16315b85b085648e0610991922aa233c7`. It does not change frozen
package pins, the qualified beta, signing trust, or Play publication evidence.

## Implemented

- Core-owned Read/Preview/Commit/Lookup; no native rule arithmetic or character XML writes.
- The existing cross-Career mutation owner and process execution lease, not a
  second reputation-only execution lock. Domain journals retain their typed commands.
- Confirmed identity is durably recorded before Core entry. An Applying record
  and owner survive lost acknowledgements. Recovery looks up the original
  command; only an explicit retry may call Commit for that same command.
- Applied status requires the constructor-owned Core service to re-read an exact,
  coherent durable receipt. A caller-provided or substituted receipt cannot free
  the shared owner. Failed owner release remains recoverable.
- Explicit superseded closure requires Core NotFound **and** a saved revision
  greater than the command's CAS revision while the shared transition gate
  excludes native execution. NotFound alone, missing/unavailable data or an
  in-flight operation cannot release an unknown outcome. The command remains in
  separate not-applied history. Applied history is never relabelled or removed.
- One filesystem durability implementation now serves the typed reward and
  reputation journals. Existing reward filenames, JSON and flush/rename/directory
  fsync semantics are retained. Present empty/BOM-only files are corrupt, not absent.
- Phone model: optional signed manual deltas, distinct Core-computed totals,
  burn preview, explicit confirmation, retained identity, reload-before-completion,
  pending recovery, explicit retry/closure and historical read-only recovery.
- Actual native MAUI controls with stable editors, before/after display, bounded
  20-row history pages, no implicit selection, and canceled-appearance click fences.
  All 39 strings have EN/DE/ES resources, including regional fallback tests.

## Verification and limits

The executable harness compiles these actual native source components and the
actual Core Contracts/Application/Infrastructure projects. It uses real Core
file workspaces and real flushed reputation journals. Its test owner/selection/
presenter-refresh adapters are explicit; they are **not** Android Preferences,
MAUI Activity lifecycle, full RunnerSessionCoordinator startup, or device proof.
The existing reward subprocess recovery test remains in the regression suite;
there is not yet a reputation-specific OS-process-death test.

Run with the repository's SDK 10.0.110 and an explicit Core checkout:

```sh
dotnet restore tests/Chummer.Android.Sr5AfterRunReward.Tests/Chummer.Android.Sr5AfterRunReward.Tests.csproj \
  -p:ChummerCoreRoot=/absolute/reviewed/core -p:RuntimeIdentifiers=linux-x64 --disable-parallel
dotnet build tests/Chummer.Android.Sr5AfterRunReward.Tests/Chummer.Android.Sr5AfterRunReward.Tests.csproj \
  --no-restore -p:ChummerCoreRoot=/absolute/reviewed/core -p:RuntimeIdentifiers=linux-x64 \
  -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false
dotnet tests/Chummer.Android.Sr5AfterRunReward.Tests/bin/Debug/net10.0/Chummer.Android.Sr5AfterRunReward.Tests.dll
```

Restore again when the Core root changes: old project assets can retain a former
source closure even when direct references now identify another checkout.
Do not accept mixed source roots or copy replacement DLLs into a sealed feed.

## Remaining application integration

1. Compose the service over the existing configured IWorkspaceStore/source
   resolver and register the journal with the app-private state directory.
2. Reuse the existing owner/immutable runner-selection and saved-reload host.
   The currently shared types have AfterRunReward names but contain common
   Career selection state; do not invent a second owner or generation counter.
3. Add a lifecycle-bound NativePageBase wrapper and the genuine Career/After Run
   entry routes. Recheck entry identity before and after navigation. The old
   CareerReputationPage and generic edit route are still unchanged; this slice
   must not be reported as already replacing them.
4. Wire full native/page startup and real presenter refresh tests, regenerate
   source inventories in dependency order, and qualify the new Core/UI/Android
   graph. The scoped harness is not a complete Native.CompileCheck or APK build.
5. Add reputation-specific new-process and Android handler/device evidence,
   including shared-owner durability, interrupted saves and restored navigation.
6. Keep Astral/Wild reputation explicitly out of this component until their real
   Core capabilities exist. Local contacts, Heat and changed-definition effect
   reactivation remain separate unfinished work, not satisfied by this wizard.

Nothing here grants GM authority, settles a campaign run, enables Rook, or
authorizes signing/upload. The old Play RSA upload key and the separate missing
release-approval signing identity are distinct and unchanged.
