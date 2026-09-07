# Native interaction regression executable

This executable references the actual `Native.CompileCheck` assembly and its
Core/Presentation dependencies. It does not link substitute native page or
coordinator implementations. Compile it with the same explicit dependency
roots, package feeds and versions as `Native.CompileCheck`, then execute its
`net10.0` DLL. Passing this development harness does not seal those dependencies.

The current 60 top-level cases contain 58 existing interaction cases and two
wrappers: 14 settlement authority cases and 9 native After Run page/entry cases.
Do not add the wrapper and nested counts as separate independent tests.

`AfterRunPageInteractionTests.cs` instantiates the actual settlement page,
reward page/view, coordinator, entry factory, action gate and checkpoint stores.
It exercises delayed discard confirmation, overlapping actions, departure,
selection/revision changes, replaced/applying checkpoints, and factory routing
for missing/available/corrupt catalogs and mixed reward/settlement recovery.
The pending reward command is prepared by the real Core file-store service;
entering its page must not call the injected runtime service or commit it.

Boundaries remain explicit:

- Presenter state/event interfaces are strict test adapters; unexpected calls
  throw. Unused coordinator dependencies are not initialized in these tests.
- Journal storage is an in-memory/fault adapter, not Android Preferences or a
  process-crash persistence proof.
- The platform alert is replaced by a controlled asynchronous answer. The same
  page action used by the button is executed and native controls are inspected.
- The actual disappearance hook is invoked through reflection; the tests do not
  run full coordinator startup, MAUI Android handlers, Activity lifecycle,
  accessibility, pixels, or physical-device navigation.
- The default production constructors still use platform preferences and
  `DisplayAlertAsync`. Test seams are internal, not a public bypass API.

Full API-36 and device qualification, governed package receipts and Play
publication remain separate gates.
