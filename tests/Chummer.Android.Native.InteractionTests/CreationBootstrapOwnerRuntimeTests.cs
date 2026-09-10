using System.Text.Json;
using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Infrastructure.Workspaces;
using Chummer.Presentation.Overview;
using Chummer.Presentation.Shell;

internal static partial class AfterRunAuthorityHarness
{
    public static async Task RunCreationBootstrapOwnerCasesAsync(string contentRoot)
    {
        var failures = new List<string>();
        foreach (string scenario in new[] { "local", "linked", "dialog-owner-change", "dialog-owner-aba" })
        {
            var owners = new ControlledLinkedOwner();
            owners.Set(scenario == "local" ? OwnerScope.LocalSingleUser : ContactsOwnerA);
            await using var runtime = new NativeRewardRuntime(contentRoot, linkedOwners: owners, creationBootstrap: true);
            var store = new FileWorkspaceStore(runtime.StateDirectory);
            await runtime.Coordinator.CreateRunnerAsync();
            Require(runtime.Coordinator.State.ActiveDialog?.Id == "dialog.new_character",
                "Actual native New Runner command did not open the canonical dialog: " + runtime.Coordinator.State.Error);
            await runtime.Presenter.UpdateDialogFieldAsync("newCharacterName", "Owner bound bootstrap fixture", default);
            await runtime.Presenter.UpdateDialogFieldAsync("newCharacterAlias", "Native", default);
            if (scenario is "dialog-owner-change" or "dialog-owner-aba") owners.Set(ContactsOwnerB);
            if (scenario == "dialog-owner-aba") owners.Set(ContactsOwnerA);
            Exception? failure = null;
            try { await runtime.Coordinator.ExecuteDialogActionAsync("create_character"); }
            catch (Exception exception) when (exception is not OutOfMemoryException) { failure = exception; }
            int local = store.List().Count;
            int a = store.List(ContactsOwnerA).Count;
            int b = store.List(ContactsOwnerB).Count;
            bool shouldCreate = scenario is "local" or "linked";
            bool correct = failure is null && b == 0
                && local == (scenario == "local" ? 1 : 0)
                && a == (scenario == "linked" ? 1 : 0)
                && (!shouldCreate || runtime.Coordinator.State.WorkspaceId is not null
                    && runtime.Coordinator.State.Profile?.Created == false
                    && runtime.Coordinator.State.Error is null);
            Console.WriteLine("BOOTSTRAP_OWNER_OBSERVATION " + JsonSerializer.Serialize(new
            {
                scenario, local, a, b, correct, exception = failure?.GetType().Name,
                runtime.Coordinator.State.Error, runtime.Coordinator.State.Notice,
                workspace = runtime.Coordinator.State.WorkspaceId?.Value,
                displayOwner = runtime.Coordinator.State.DisplayOwnerContext?.Owner.Value,
                currentOwner = owners.Current.Value
            }));
            Require(owners.ActiveLeases == 0, "Actual Bootstrap leaked the owner lease.");
            if (!correct) failures.Add(scenario);
        }
        Require(failures.Count == 0, "Actual native Bootstrap owner failures: " + string.Join(", ", failures));
        foreach (string scenario in new[] { "admission-change", "admission-aba", "postcommit-change", "postcommit-cancel",
            "postcommit-validation-fault", "activation-fallback", "missing-companion", "opening-change", "opening-aba" })
            await RunBootstrapBoundaryAsync(contentRoot, scenario);
        foreach (string scenario in new[] { "overlapping-submit", "closed-dialog", "replacement-dialog" })
            await RunBootstrapDialogRaceAsync(contentRoot, scenario);
        foreach (string scenario in new[] { "initialized-local", "initialized-linked", "reinitialize-owner-b", "reinitialize-owner-aba" })
            await RunBootstrapShellOwnerIntegrationAsync(contentRoot, scenario);
        foreach (bool linked in new[] { false, true })
            await RunBootstrapPreferencesAsync(contentRoot, linked);
        Console.WriteLine("PASS actual native Bootstrap and Shell owner cases: 22");
    }

    private static async Task RunBootstrapPreferencesAsync(string contentRoot, bool linked)
    {
        DesktopPreferenceState previous = DesktopPreferenceStateRuntime.Current;
        try
        {
            var owners = new ControlledLinkedOwner();
            owners.Set(linked ? ContactsOwnerA : OwnerScope.LocalSingleUser);
            await using var runtime = new NativeRewardRuntime(contentRoot, linkedOwners: owners, creationBootstrap: true);
            DesktopPreferenceStateRuntime.SetCurrent(previous with
            {
                Language = "es-es", SheetLanguage = "es-es", UiScalePercent = 125,
                AnalyticsOptIn = false, DisableAiFeatures = true
            });
            DesktopPreferenceState expected = DesktopPreferenceStateRuntime.Current;
            await runtime.Shell.InitializeAsync(default);
            await runtime.Presenter.InitializeAsync(default);
            Require(runtime.Presenter.State.Preferences == expected,
                "Initial owner bootstrap replaced current app preferences with defaults.");

            owners.Set(ContactsOwnerB);
            DesktopPreferenceStateRuntime.SetCurrent(expected with { Language = "de-de", UiScalePercent = 115 });
            expected = DesktopPreferenceStateRuntime.Current;
            await runtime.Shell.InitializeAsync(default);
            await runtime.Presenter.InitializeAsync(default);
            Require(runtime.Presenter.State.Session.OwnerContext == owners.Capture()
                && runtime.Presenter.State.Preferences == expected,
                "Owner reinitialization lost current app preferences while clearing the old projection.");
            Require(owners.ActiveLeases == 0, "Preference initialization leaked an owner lease.");
            Console.WriteLine("PASS actual Bootstrap preferences: " + (linked ? "linked" : "local"));
        }
        finally
        {
            DesktopPreferenceStateRuntime.SetCurrent(previous);
        }
    }

    private static async Task RunBootstrapShellOwnerIntegrationAsync(string contentRoot, string scenario)
    {
        var owners = new ControlledLinkedOwner();
        owners.Set(scenario == "initialized-local" ? OwnerScope.LocalSingleUser : ContactsOwnerA);
        await using var runtime = new NativeRewardRuntime(contentRoot, linkedOwners: owners, creationBootstrap: true);
        await runtime.Shell.InitializeAsync(default);
        await runtime.Presenter.InitializeAsync(default);
        OwnerContextStamp original = owners.Capture();
        Require(runtime.Presenter.State.Session.OwnerContext == original
            && runtime.Shell.State.OwnerContext == original, "Initial bootstrap lost its roster owner.");
        await runtime.Coordinator.CreateRunnerAsync();
        await runtime.Presenter.UpdateDialogFieldAsync("newCharacterName", "Shell owner integration", default);
        await runtime.Coordinator.ExecuteDialogActionAsync("create_character");
        var created = runtime.Presenter.State;
        Require(created.WorkspaceId is { } id && created.Error is null
            && created.Session.OwnerContext == original && created.DisplayOwnerContext == original
            && runtime.Shell.State.ActiveWorkspaceId == id
            && runtime.Shell.State.OpenWorkspaces.Any(row => row.Id == id),
            "Actual native creation did not publish the original-owner runner into Shell: " + created.Error);
        if (scenario.StartsWith("reinitialize-", StringComparison.Ordinal))
        {
            owners.Set(ContactsOwnerB);
            if (scenario == "reinitialize-owner-aba") owners.Set(ContactsOwnerA);
            OwnerContextStamp next = owners.Capture();
            await runtime.Shell.InitializeAsync(default);
            await runtime.Presenter.InitializeAsync(default);
            var expectedShell = runtime.Shell.State;
            Require(runtime.Presenter.State.Session.OwnerContext == next && runtime.Presenter.State.Profile is null
                && runtime.Presenter.State.DisplayOwnerContext is null, "Reinitialization retained old character display authority.");
            Require(runtime.Presenter.State.OpenWorkspaces.Count == (scenario == "reinitialize-owner-aba" ? 1 : 0),
                "New-account roster reused another account's entries or lost the current account's entries.");
            if (scenario == "reinitialize-owner-aba")
            {
                Require(runtime.Shell.State.Notice == runtime.Presenter.State.Notice
                    && runtime.Shell.State.Notice?.Contains("Restored 1", StringComparison.Ordinal) == true,
                    "Actual original-owner feedback did not reach the Shell.");
                var projector = typeof(CharacterOverviewPresenter).GetMethod("CreateShellOverviewFeedback",
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
                    ?? throw new InvalidOperationException("Actual feedback projector unavailable.");
                var altered = runtime.Presenter.State with { Notice = "Unbound replacement message" };
                var feedback = (ShellOverviewFeedback)projector.Invoke(null, [altered])!;
                Require(feedback.FeedbackOwnerContext is null && feedback.RosterOwnerContext == next,
                    "Changed feedback text inherited the original message's owner stamp.");
                runtime.Shell.SyncOverviewFeedback(feedback);
                Require(runtime.Shell.State.Notice == expectedShell.Notice, "Unbound text replaced grounded feedback.");
                expectedShell = runtime.Shell.State;
            }
            bool rejected = false;
            try { await ShellWorkspaceContextSynchronization.SynchronizeAsync(runtime.Shell, runtime.Client, created, default); }
            catch (InvalidOperationException) { rejected = true; }
            Require(rejected && ReferenceEquals(expectedShell, runtime.Shell.State),
                "Old navigation did not preserve the freshly initialized Shell.");
        }
        Require(owners.ActiveLeases == 0, "Shell integration leaked an owner lease.");
        Console.WriteLine("PASS actual Bootstrap Shell integration: " + scenario);
    }

    private static async Task RunBootstrapBoundaryAsync(string contentRoot, string scenario)
    {
        var owners = new ControlledLinkedOwner();
        owners.Set(ContactsOwnerA);
        using var cancel = new CancellationTokenSource();
        BootstrapBoundaryDecorator? boundary = null;
        var opening = new BootstrapOpeningDispatcher(() =>
        {
            if (scenario is "opening-change" or "opening-aba") owners.Set(ContactsOwnerB);
            if (scenario == "opening-aba") owners.Set(ContactsOwnerA);
        });
        await using var runtime = new NativeRewardRuntime(contentRoot, linkedOwners: owners, creationBootstrap: true,
            bootstrapCommandDispatcher: opening,
            bootstrapDecorator: inner =>
            {
                boundary = new BootstrapBoundaryDecorator(inner)
                {
                    BeforeCreate = () =>
                    {
                        if (scenario is "admission-change" or "admission-aba") owners.Set(ContactsOwnerB);
                        if (scenario == "admission-aba") owners.Set(ContactsOwnerA);
                    },
                    AfterCreate = () =>
                    {
                        if (scenario == "postcommit-change") owners.Set(ContactsOwnerB);
                        if (scenario == "postcommit-cancel") cancel.Cancel();
                    },
                    FailValidation = scenario == "postcommit-validation-fault",
                    OmitBundle = scenario == "activation-fallback"
                };
                return scenario == "missing-companion" ? null : boundary;
            });
        var store = new FileWorkspaceStore(runtime.StateDirectory);
        await runtime.Coordinator.CreateRunnerAsync();
        if (scenario.StartsWith("opening-", StringComparison.Ordinal))
        {
            Require(runtime.Coordinator.State.ActiveDialog is null, "Old dialog completion published into a changed account.");
            Require(store.List().Count == 0 && store.List(ContactsOwnerA).Count == 0 && store.List(ContactsOwnerB).Count == 0,
                "Opening a stale dialog wrote a runner.");
            Console.WriteLine("PASS actual Bootstrap boundary: " + scenario);
            return;
        }
        await runtime.Presenter.UpdateDialogFieldAsync("newCharacterName", "Bootstrap boundary fixture", default);
        Exception? failure = null;
        try { await runtime.Presenter.ExecuteDialogActionAsync("create_character", cancel.Token); }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
        catch (Exception ex) { failure = ex; }
        bool committed = scenario.StartsWith("postcommit-", StringComparison.Ordinal) || scenario == "activation-fallback";
        Require(failure is null, "Bootstrap boundary exception: " + scenario + ": " + failure);
        Require(store.List().Count == 0 && store.List(ContactsOwnerB).Count == 0
            && store.List(ContactsOwnerA).Count == (committed ? 1 : 0),
            "Wrong durable partition or duplicate Bootstrap commit at " + scenario);
        if (committed)
        {
            Require(boundary!.Receipt is { } receipt && CharacterCreationBootstrapReceiptDigest.IsValid(receipt),
                "Test must retain a genuine Core creation receipt.");
            Require(runtime.Presenter.State.ActiveDialog?.Actions.All(action => action.Id != "create_character") != false,
                "Committed create still offers a duplicate creation action at " + scenario);
            if (scenario == "activation-fallback")
                Require(runtime.Presenter.State.WorkspaceId == boundary.Receipt!.WorkspaceId
                    && runtime.Presenter.State.DisplayOwnerContext == owners.Capture(),
                    "Committed fallback did not reload under the original account.");
            if (scenario is "postcommit-cancel" or "postcommit-validation-fault")
                Require(runtime.Presenter.State.Notice?.Contains(boundary.Receipt!.WorkspaceId.Value, StringComparison.Ordinal) == true,
                    "Successful commit became an ambiguous failure at " + scenario);
            if (scenario == "postcommit-change")
                Require(runtime.Presenter.State.WorkspaceId is null && runtime.Presenter.State.DisplayOwnerContext is null,
                    "Completed A create was activated into B.");
            await runtime.Presenter.ExecuteDialogActionAsync("create_character", default);
            Require(store.List(ContactsOwnerA).Count == 1 && boundary.CreateCount == 1,
                "Retry replayed a committed create at " + scenario);
        }
        Require(owners.ActiveLeases == 0, "Bootstrap boundary leaked an owner lease.");
        Console.WriteLine("PASS actual Bootstrap boundary: " + scenario);
    }

    private sealed class BootstrapBoundaryDecorator(IOwnerBoundCharacterCreationBootstrapService inner)
        : IOwnerBoundCharacterCreationBootstrapService
    {
        public Action? BeforeCreate { get; init; }
        public Action? AfterCreate { get; init; }
        public bool FailValidation { get; init; }
        public bool OmitBundle { get; init; }
        public int CreateCount { get; private set; }
        public CharacterCreationBootstrapReceipt? Receipt { get; private set; }
        public CharacterCreationBootstrapResult<CharacterCreationBootstrapReceipt> Create(OwnerContextStamp owner, CharacterCreationBootstrapRequest request)
        {
            ++CreateCount;
            BeforeCreate?.Invoke();
            var result = inner.Create(owner, request);
            Receipt = result.Value;
            AfterCreate?.Invoke();
            return result;
        }
        public CharacterCreationBootstrapActivationAttempt CreateActivation(OwnerContextStamp owner, CharacterCreationBootstrapRequest request)
        {
            ++CreateCount;
            BeforeCreate?.Invoke();
            var result = inner.CreateActivation(owner, request);
            Receipt = result.Receipt;
            AfterCreate?.Invoke();
            return OmitBundle ? result with { Bundle = null, Blockers = [CharacterCreationBootstrapBlockers.ActivationProjectionUnavailable] } : result;
        }
        public bool TryValidateCurrent(OwnerContextStamp owner, CharacterCreationBootstrapActivationBundle activation, out IReadOnlyList<string> blockers)
        {
            if (FailValidation) throw new IOException("Injected post-commit activation read failure.");
            return inner.TryValidateCurrent(owner, activation, out blockers);
        }
    }

    private static async Task RunBootstrapDialogRaceAsync(string contentRoot, string scenario)
    {
        var owners = new ControlledLinkedOwner();
        owners.Set(ContactsOwnerA);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        BootstrapBoundaryDecorator? boundary = null;
        await using var runtime = new NativeRewardRuntime(contentRoot, linkedOwners: owners, creationBootstrap: true,
            bootstrapDecorator: inner => boundary = new BootstrapBoundaryDecorator(inner)
            {
                BeforeCreate = () =>
                {
                    // Before actual Core admission: never wait while holding an owner lease.
                    Require(owners.ActiveLeases == 0, "Dialog test gate entered under a live owner lease.");
                    entered.TrySetResult();
                    Require(release.Wait(TimeSpan.FromSeconds(30)), "Dialog race test did not release the create boundary.");
                }
            });
        await runtime.Coordinator.CreateRunnerAsync();
        var stamp = owners.Capture();
        string serialized = JsonSerializer.Serialize(runtime.Presenter.State.ActiveDialog);
        Require(!serialized.Contains(stamp.AuthorityInstanceId, StringComparison.Ordinal)
            && !serialized.Contains("CreationAuthority", StringComparison.Ordinal), "Dialog serialized transient owner authority.");
        Task first = runtime.Presenter.ExecuteDialogActionAsync("create_character", default);
        DesktopDialogState? replacement = null;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            if (scenario == "overlapping-submit")
                await runtime.Presenter.ExecuteDialogActionAsync("create_character", default);
            else if (scenario == "closed-dialog")
                await runtime.Presenter.CloseDialogAsync(default);
            else
            {
                await runtime.Presenter.ExecuteCommandAsync("new_character", default);
                replacement = runtime.Presenter.State.ActiveDialog;
            }
        }
        finally { release.Set(); }
        await first.WaitAsync(TimeSpan.FromSeconds(30));
        BootstrapBoundaryDecorator actualBoundary = boundary ?? throw new InvalidOperationException("Actual Bootstrap decorator was not composed.");
        var store = new FileWorkspaceStore(runtime.StateDirectory);
        Require(store.List().Count == 0 && store.List(ContactsOwnerA).Count == 1 && store.List(ContactsOwnerB).Count == 0
            && actualBoundary.CreateCount == 1 && owners.ActiveLeases == 0,
            "Dialog race duplicated creation or changed the durable owner: " + scenario);
        if (scenario == "overlapping-submit")
            Require(runtime.Presenter.State.WorkspaceId == actualBoundary.Receipt!.WorkspaceId
                && runtime.Presenter.State.DisplayOwnerContext == stamp, "Original successful create did not activate.");
        else
            Require(runtime.Presenter.State.WorkspaceId is null
                && ReferenceEquals(runtime.Presenter.State.ActiveDialog, replacement),
                "Late creation completion overwrote a closed/replacement dialog: " + scenario);
        Console.WriteLine("PASS actual Bootstrap dialog race: " + scenario);
    }

    private sealed class BootstrapOpeningDispatcher(Action atOpening) : IOverviewCommandDispatcher
    {
        public async Task DispatchAsync(string command, OverviewCommandExecutionContext context, CancellationToken ct)
        {
            if (command == "new_character") { await Task.Yield(); atOpening(); }
            await new OverviewCommandDispatcher().DispatchAsync(command, context, ct);
        }
    }
}
