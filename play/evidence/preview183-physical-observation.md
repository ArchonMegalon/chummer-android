# Preview 183 — physical Play update and illustrated chapter restoration

On 10 October 2026, 21:26–21:31 UTC, the existing Internal-test phone was
updated from Preview 182 to 183 through the actual Play Store Update control.
The pending update and subsequent installed version were observed. No sideload,
reinstall, account change or data clear was performed.

- Package `com.myexternalbrain.chummer`, `183`, `0.1.0-preview.183`;
  installer `com.android.vending`, last update 10 October 23:26:13 Vienna.
- Physical API 36 ARM64, font scale 1.3; English controls and German chapter.
- Installed base APK: 21,024,530 bytes, SHA-256
  `ddf8863ad6963642b67ed54f369836c0b742de7aaf8f778eb744949a9900bd3e`.
- Expected Play signing certificate:
  `035df37b31c599d3221aabfda7d9e3cdfe65752bd6e60e70c640780c51dd908f`.
- Offline signature log SHA-256:
  `2454cc264bad012f00da3f36e78ad3da32a2039d6a52e42e101571751ca71e9d`.
- Private physical result SHA-256:
  `30b332f74239239d905ca147d2a5cc8fee75a0ef0d29270cf1a12c4959e15459`.

The existing keyless, read-only, network-disabled Docker verifier checked APK
v2/v3 signatures and SourceStamp. The signer matches the established Play
certificate, not the upload key. Two unknown optional v3-attribute warnings
remain recorded. Only the installed base APK was independently verified;
complete split-payload equality with the release bundle is not asserted.

## Changed route and retained content

The selected linked Life Modules runner restored after the update. Normal
Read your story navigation displayed the existing automatically illustrated
chapter. Its entire 17,196-character, 2,447-word text matched the accepted
retained provider result exactly, not a summary. The compact unknown-result
status and unavailable ETA were readable without clipping at font scale 1.3;
their longer accessibility explanations remained present.

The app was force-stopped. Both package process lookup and `/proc` established
that the old process was absent; relaunch created a different process. The
same runner restored, and normal reader entry again showed the illustration
and exactly the same full chapter. Text SHA-256 before and after restart:
`6f37c5aaf49c1b9dd6e5a87f829c9572a92fc61080856228cc40e4ea4c30316a`.
Image restoration is visual/accessibility evidence, not an on-device image-byte
comparison. Private saved-file equality is not claimed.

The original uncertain successor write fence remained byte-identical. No new
provider write, decision, reading acknowledgement, export or Career finalization
was requested. The successor's result remains unresolved; success of this saved
first chapter does not close that incident or establish a complete manuscript.

## Limits and retention

Initial and restart Activity timings were 828 ms and 701 ms. These are not
time-to-usable measurements. Initial screenshots showed loading, and later
observations showed restored content; intervals were not a controlled benchmark.
No startup improvement, general reload-defect closure or complete Creation/
Career result is claimed. This does not test fresh tester diagnostic delivery.

The private `origin-compact-release183-20261010.lNL3Kqf2/physical` packet retains
the base APK, signature output, process observations, screenshots and hierarchies.
Screenshots precede hierarchy capture and are not atomic with it. Raw account,
character and device details are not published. This check builds no new AAB,
does not repeat the upload, and leaves version 183 unchanged.

The earlier [Internal availability record](preview183-internal-observation.json)
retains its observation-time limits. This later physical check supplements it.

## Later physical startup baseline and normal reader re-entry

At 21:54:54 UTC on 10 October, the same Play-installed Preview 183 was
force-stopped and relaunched for a bounded physical-device measurement. The old
process was verified absent and a new process observed. The first fresh
hierarchy capture still showed startup loading. The next showed the exact
selected runner, no startup loading, and an enabled Read your story button by
6.714 seconds from launch-command dispatch. This is an observation upper bound
including ADB and hierarchy-capture overhead, not the exact first usable frame.
The Activity alone reported 667 ms; these two measurements are not equivalent.

Existing metadata-only startup phase logs from that new process recorded:

| Local phase | Duration |
| --- | ---: |
| Local identity | 533 ms |
| Local settings | 3 ms |
| Local adoption recovery | 6 ms |
| Shell initialization | 1,175 ms |
| Presenter initialization | 11 ms |
| Selected-workspace restoration | 1,204 ms |
| Shell finalization | 2 ms |
| Workspace authority refresh | 1 ms |
| Play-state restoration | 4 ms |
| Sum of recorded local phases | 2,939 ms |

Normal Read your story entry, Back to Create, and Read your story again were
then exercised through 21:59:05 UTC without restarting the app or switching
runners. Both reader entries displayed the illustration and the same complete
17,196-character, 2,447-word accepted chapter, matching the text hash above.
Returning to Create retained the selected runner and enabled reader button.
The original uncertain successor fence remained byte-identical. No new paid
generation, decision, reading acknowledgement, export or finalization was
requested. The runtime configuration, including the retained GC mitigation,
was not changed.

This is one current-build baseline and successful normal re-entry, not a
before/after performance comparison or reproduction of the separately reported
failed-refresh incident. It does not establish complete live multi-chapter
Creation/Career acceptance. No new build, signing or upload was needed.

The private `physical-startup183-20261010.xMHnSu86` packet retains the observation
helper, exact hierarchy captures, screenshots and metadata-only timings.
Measurement SHA-256:
`e90137f808b19e8a7a1d038401938c875f778452c9f315432d6d99b32db6ee0c`.
Verified startup/re-entry result SHA-256:
`c110e82c147c659d375c3a903cb15d9e2fe5e16a82bf25583c212a5624ffea24`.
Screenshot/hierarchy non-atomicity and private-data retention limits above still
apply. No raw character prose, account or device identifiers are published.
