# Karma Contacts and Lifestyle through Career

The combined native route passed locally on 8 October 2026 using the existing
Preview145 diagnostic APK. One saved contact and one purchased lifestyle survived
Career finalization and a verified process stop/restart without duplicate records
or changed saved bytes. This closes the route left open by the earlier
[runner-switch feedback smoke](HOME_SWITCH_FEEDBACK_20261008.md).

## Tested input and route

Producer: `90b6ae8006db9e7bf16a9a71d0bc9b3a64caa0e1`.
Tree: `a858ecaaf3b5187795a306229f5d97528b27c839`.
Installed APK SHA-256:
`1d22c0203e3ef73eff82d82925c546f8beeae2f4208472b62e789e536a82173d`.
Core and Presentation inputs are unchanged from Preview145. The test used the
separate SDK-test-signed Release x64 package on an API36 emulator, not the
Play-signed ARM64 app or a physical phone.

1. Explicitly saved the current synthetic draft before creating one new Human,
   Mundane Karma runner with base attributes and English as its native language.
2. Added one Connection1/Loyalty1 contact with Family and Blackmail selected.
   The preview showed 5/3 contact points and 2 additional Karma.
3. Converted 10 Karma to 20,000 nuyen. Purchased two monthly periods of Low
   lifestyle for 4,000 nuyen and selected it for starting cash. Equipment and
   qualities were empty. Review showed 12 Karma spent and 788 remaining.
4. Saved the pending draft once at revision2/2. The saved auxiliary decision
   retained both selections and the starting lifestyle. Runtime contacts remain
   pending until finalization; this was not mistaken for data loss.
5. Entered synthetic dice total9 for the displayed 3d6 ×60 starting cash.
   Reviewed the 7 Karma and 5,000 nuyen carryover caps plus 540 starting cash,
   then confirmed Career exactly once. Result: revision3/3, 7 Karma, 5,540 nuyen,
   one contact, one lifestyle and one finalization receipt.
6. Verified process termination, restarted into Career and confirmed the same
   workspace and revision through opt-in technical details, then hid them again.
   Settled Career controls were visually inspected.

## Persistence result

Saved Career and reopened JSON bytes are identical:
`b08fa48342e8092b9eaa5827e4e8825741ecae0f7570a5d081e691ef394b26be`.
Finalization receipt digest:
`sha256:e9cf36b598663b602d687fc37084ee1b0559ab5eb4a30c3b99f2b2b882ffdf44`.
All eight saved workspace files remained unchanged across that restart. Of the
seven pre-existing runners, only the explicitly saved current draft changed
before this route; the other six retained their original hashes.

The retained private packet contains screenshots, hierarchies and three workspace
snapshots. Its result document SHA-256 is
`869a46f55458efbbe643adb94c5676f10fdb8c238ac0e40355b7d8802b65d584`.
The read-only state verifier passed; its SHA-256 is
`3b0e1687e9b7bb301c16b258f49bc290db42d80ceb88eafdfcee2b06905071b8`.
Raw workspace snapshots and device details are not published.

## Limits

Boot needed one observed System UI Wait action. The owned emulator unit's memory
limit was raised from 4 to 6 GiB after memory-pressure events, with no OOM kills.
Loading and null-hierarchy observations were retained before settled captures;
no uncertain mutation was replayed. Slow startup and catalog loading remain open
and this is not a performance or clean-start qualification.

No application source, package, provider job, signing or Play upload changed.
Preview145 remains the existing available Internal build; no version was reused.
The owned emulator was stopped. Physical Play installation, other Karma options
and subsequent Career actions remain outside this bounded result. Per-chapter
app/EPUB illustrations are unchanged and retain their existing scoped evidence.
