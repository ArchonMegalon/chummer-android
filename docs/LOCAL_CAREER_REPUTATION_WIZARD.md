# Local SR5 reputation wizard — implementation handoff

Status: local native application integration; not a packaged candidate or release authority.

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
- MauiProgram registers the app-private journal against the Core runtime's
  configured reputation service. RunnerSessionCoordinator reuses the existing
  owner, immutable Career selection generation and real presenter/shell reload.
- The Career reputation card and saved-reward continuation open the dedicated
  wizard. Context is checked before/after preparation and at destination entry.
  No generic XML editor fallback or fabricated proposal/GM identity is used.
- NativePageBase wrapper keeps actions disabled until the current appearance's
  observation completes. Overlapping appearance reads drain asynchronously;
  canceled old reads cannot mark a returning page ready or strand its busy view.
  Late departed input callbacks cannot change retained intent.
- Returning from a saved child refreshes an already Applied reward's handoff by
  lookup/reload only. Pending or unknown commands still require explicit recovery.

## Verification and limits

The executable harness compiles these actual native source components and the
actual Core Contracts/Application/Infrastructure projects. It uses real Core
file workspaces and real flushed reputation journals. Its test owner/selection/
presenter-refresh adapters are explicit; they are **not** Android Preferences,
MAUI Activity lifecycle, full RunnerSessionCoordinator startup, or device proof.
The existing reward subprocess recovery test remains in the regression suite;
there is not yet a reputation-specific OS-process-death test.

The separate Native.InteractionTests project now compiles the whole native
source graph, including MauiProgram and the real page/coordinator. Its runtime
cases use the actual Core DI service, file store, native coordinator, shared
operation coordinator, presenter and Shell. Preferences are backed by explicit
test memory; appearance lifetimes and final confirmation are driven by managed
test calls, not an Android Activity or physical touch.

New integration assertions cover saved reward → reputation → saved receipt →
fresh After Run return, history-only reopen, canceled/expired destination entry,
depart/reappear, and actual post-commit presenter reload cancellation/failure.
A controlled decorator blocks only the first real Core Read to reproduce rapid
leave/return; all rule, preview, write and lookup operations remain Core-owned.

Local source composition uses UI `485c1e07c8b43ba1b4e71bc308a025fad34bb8d4`
(based on `80f72ba18e5587679ec2e8838906b97fc99cda63`). The narrow UI fix makes its
Application reference honor ChummerCoreEngineRoot, like its other Core projects,
instead of silently adding an ambient sibling. Neither commit is a package seal.
The complete development graph uses explicit owner project roots, not replacement
DLLs or new claims about the existing frozen package feed.

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

## Remaining qualification and product work

1. Regenerate source inventories in dependency order, review/seal the new
   Core/UI/Android graph, and qualify the actual APK. This local source build
   does not update the checked-in package pins or current candidate authority.
2. Test full MauiProgram startup and Android handler attachment. Managed page
   method calls and in-memory Preferences are not a physical-device lifecycle.
3. Add reputation-specific new-process and Android handler/device evidence,
   including shared-owner durability, interrupted saves and restored navigation.
4. Verify actual Creation → Career output supplies the canonical profile and
   persisted reputation/effect inputs. The old minimal imported test runner
   omitted burntstreetcred/improvements and was correctly unresolved; the full
   integration fixture now includes them. That repair is not proof of the real
   creation output or a reason to fabricate missing inputs during mutation.
5. Keep Astral/Wild reputation explicitly out of this component until their real
   Core capabilities exist. Local contacts, Heat and changed-definition effect
   reactivation remain separate unfinished work, not satisfied by this wizard.
   The old generic page's source remains preserved, but the ordinary Career card
   no longer opens it. Broader parity and existing API-36 scripts need explicit
   reconciliation with the new wizard rather than inheriting old route evidence.

Nothing here grants GM authority, settles a campaign run, enables Rook, or
authorizes signing/upload. The old Play RSA upload key and the separate missing
release-approval signing identity are distinct and unchanged.
