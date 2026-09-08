using System.Globalization;
using System.Reflection;
using System.Xml.Linq;
using Chummer.Android.Native;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Workspaces;
using Microsoft.Maui.Controls;

internal static partial class AfterRunAuthorityHarness
{
    private static async Task NativeReputationContinuationPersistsAndReturnsAsync(string contentRoot)
    {
        await using var runtime = new NativeRewardRuntime(contentRoot, reputation: true);
        await runtime.LoadRunnerAsync();
        Require(runtime.Coordinator.SupportsCareerReputationEntry, "Actual Core/DI reputation capability was not composed.");
        var reward = await runtime.PrepareRewardAsync();
        var page = new Sr5AfterRunRewardWizardPage(runtime.Coordinator, reward);
        var navigation = new NavigationPage(page);
        var rewardView = (Sr5AfterRunRewardView)((ScrollView)page.Content!).Content;
        var reputationButton = ((VerticalStackLayout)rewardView.Content!).Children.OfType<Button>()
            .Single(control => control.AutomationId == "sr5-reward-reputation");
        Require(!reputationButton.IsVisible && !reputationButton.IsEnabled,
            "Unconfirmed rewards exposed an authorized consequence entry.");
        await ExpectConsequencesRejectedAsync(() => page.OpenReputationAsync(default));
        await reward.ConfirmAsync();
        rewardView.Refresh();
        Require(reward.CanContinue && reputationButton.IsVisible && reputationButton.IsEnabled,
            "Saved rewards did not expose ordinary local reputation review.");
        var store = new FileWorkspaceStore(runtime.StateDirectory);
        var rewarded = store.Get(runtime.Id).Value!;
        using (var canceled = new CancellationTokenSource())
        {
            canceled.Cancel();
            await ExpectConsequencesRejectedAsync(() => page.OpenReputationAsync(canceled.Token), canceled: true);
            Require(navigation.Navigation.NavigationStack.Count == 1, "Canceled entry navigated.");
        }
        await page.OpenReputationAsync(default);
        Require(navigation.CurrentPage is Sr5CareerReputationWizardPage, "Reputation entry used the generic XML editor.");
        var reputationPage = (Sr5CareerReputationWizardPage)navigation.CurrentPage;
        var model = ReputationField<Sr5CareerReputationPhoneModel>(reputationPage, "_model");
        var view = ReputationField<Sr5CareerReputationView>(reputationPage, "_view");
        var body = (VerticalStackLayout)view.Content!;
        Require(!ReputationButton(reputationPage, "preview").IsEnabled, "Unattached page enabled actions.");
        using var lifetime = InstallManagedReputationLifetime(reputationPage);
        await InvokeTokenPageActionAsync(reputationPage, "PrepareForAppearanceRefreshAsync", lifetime.Token);
        var coreRead = runtime.ReputationService.Read(runtime.Id);
        Require(model.CanEdit && ReputationButton(reputationPage, "preview").IsEnabled,
            $"Actual saved runner reputation projection unavailable: {model.Status}; {coreRead.Outcome}: {coreRead.Error}");
        var cred = body.Children.OfType<Entry>().Single(entry => entry.AutomationId == "sr5-reputation-street-cred");
        var notoriety = body.Children.OfType<Entry>().Single(entry => entry.AutomationId == "sr5-reputation-notoriety");
        cred.Text = "1";
        notoriety.Text = "-1";
        body.Children.OfType<Editor>().Single().Text = "Local after-run reputation decision";
        Require(model.Draft.StreetCred == "1" && model.Draft.Notoriety == "-1" && model.Draft.PublicAwareness == "",
            "Native controls changed signed input intent or made an empty field a zero.");
        await model.PreviewAsync(culture: CultureInfo.InvariantCulture);
        view.Refresh();
        Require(model.CanConfirm && ReputationButton(reputationPage, "confirm").IsEnabled,
            "Actual Core preview did not become reviewable: " + model.Status);
        RequireSameRewardDocument(rewarded, store.Get(runtime.Id).Value!);
        await model.ConfirmAsync(); // Explicit managed decision, not a native Android click/alert proof.
        view.Refresh();
        Require(model.CanFinish && model.Status == Sr5CareerReputationPhoneStatus.Recorded,
            "Reputation commit failed to reload actual presenter/shell: " + model.Status);
        var saved = store.Get(runtime.Id).Value!;
        var xml = XDocument.Parse(saved.Document.Content).Root!;
        Require(saved.ContentRevision == rewarded.ContentRevision + 1 && saved.SavedRevision == saved.ContentRevision
                && runtime.Coordinator.State.SavedRevision == saved.SavedRevision
                && runtime.Shell.State.ActiveWorkspaceId == runtime.Id,
            "The native presenter did not adopt the exact reputation save.");
        Require(xml.Element("streetcred")?.Value == "11" && xml.Element("notoriety")?.Value == "3"
                && xml.Element("publicawareness")?.Value == "6" && xml.Element("karma")?.Value == "38"
                && xml.Element("nuyen")?.Value == "13500" && xml.Element("notes")?.Value == "Retain unrelated data"
                && saved.Document.AuxiliaryState.CharacterAfterRunRewardReceipts?.Count == 1
                && saved.Document.AuxiliaryState.CharacterCareerReputationReceipts?.Count == 1
                && (saved.Document.AuxiliaryState.CharacterAfterRunSettlementReceipts?.Count ?? 0) == 0,
            "Local reputation changed unrelated values, duplicated rewards or invented GM settlement.");
        await model.ConfirmAsync();
        RequireSameRewardDocument(saved, store.Get(runtime.Id).Value!);
        var reopened = await runtime.Coordinator.PrepareCareerReputationEntryAsync();
        Require(reopened.History.Count == 1 && reopened.CanEdit && !reopened.CanFinish,
            "Reconstructed entry lost history or auto-resumed a write.");
        await reopened.ResumeHistoryAsync(model.OperationId);
        Require(reopened.CanFinish && reopened.Checkpoint?.CommandDigest == model.Checkpoint?.CommandDigest,
            "Explicit receipt history resume did not preserve the original operation.");
        RequireSameRewardDocument(saved, store.Get(runtime.Id).Value!);
        Require(!reward.CanContinue, "Child save left the old reward handoff current.");
        await navigation.PopAsync();
        await InvokeTokenPageActionAsync(page, "PrepareForAppearanceRefreshAsync", default);
        Require(reward.CanContinue && reward.Handoff?.Runner.SavedRevision == saved.SavedRevision,
            "Returning from a saved child left After Run stranded on a stale handoff.");
        RequireSameRewardDocument(saved, store.Get(runtime.Id).Value!);
        Console.WriteLine("PASS saved reward → local reputation controls/preview/confirmed file save/history → fresh After Run return");
    }

    private static async Task NativeReputationEntryLifecycleFencesAsync(string contentRoot)
    {
        await using var runtime = new NativeRewardRuntime(contentRoot, reputation: true);
        await runtime.LoadRunnerAsync();
        var store = new FileWorkspaceStore(runtime.StateDirectory);
        var original = store.Get(runtime.Id).Value!;
        var model = await runtime.Coordinator.PrepareCareerReputationEntryAsync();
        int checks = 0;
        var changed = new Sr5CareerReputationWizardPage(runtime.Coordinator, model, () => ++checks == 1);
        using var changedLifetime = InstallManagedReputationLifetime(changed);
        await ExpectConsequencesRejectedAsync(() => InvokeTokenPageActionAsync(changed,
            "PrepareForAppearanceRefreshAsync", changedLifetime.Token));
        Require(checks == 2 && !ReputationButton(changed, "preview").IsEnabled,
            "An expired contextual handoff enabled the destination after observation.");
        var replaced = new Sr5CareerReputationWizardPage(runtime.Coordinator,
            await runtime.Coordinator.PrepareCareerReputationEntryAsync());
        using (var missingLifetime = new CancellationTokenSource())
        {
            await ExpectConsequencesRejectedAsync(() => InvokeTokenPageActionAsync(replaced,
                "PrepareForAppearanceRefreshAsync", missingLifetime.Token), canceled: true);
        }
        Require(!ReputationButton(replaced, "preview").IsEnabled, "Missing appearance was treated as attached.");
        using var activeLifetime = InstallManagedReputationLifetime(replaced);
        await InvokeTokenPageActionAsync(replaced, "PrepareForAppearanceRefreshAsync", activeLifetime.Token);
        Require(ReputationButton(replaced, "preview").IsEnabled, "Current appearance did not enable read-only review.");
        typeof(Sr5CareerReputationWizardPage).GetMethod("OnDisappearing", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(replaced, null);
        Require(!ReputationButton(replaced, "preview").IsEnabled && activeLifetime.IsCancellationRequested,
            "Departure retained action authority.");
        var detachedModel = ReputationField<Sr5CareerReputationPhoneModel>(replaced, "_model");
        var beforeDraft = detachedModel.Draft;
        var detachedView = ReputationField<Sr5CareerReputationView>(replaced, "_view");
        ((VerticalStackLayout)detachedView.Content!).Children.OfType<Entry>().First().Text = "9";
        Require(detachedModel.Draft == beforeDraft, "A late detached editor callback changed retained intent.");
        using var nextLifetime = InstallManagedReputationLifetime(replaced);
        await InvokeTokenPageActionAsync(replaced, "PrepareForAppearanceRefreshAsync", nextLifetime.Token);
        Require(ReputationButton(replaced, "preview").IsEnabled, "Returning page could not reacquire its same runner.");
        RequireSameRewardDocument(original, store.Get(runtime.Id).Value!);
        Console.WriteLine("PASS actual reputation destination context checks, unattached/departed/reappearing lifecycle segments");
    }

    private static T ReputationField<T>(Sr5CareerReputationWizardPage page, string name)
        => (T)typeof(Sr5CareerReputationWizardPage).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;

    private static async Task NativeReputationRefreshInterruptionRecoversAsync(string contentRoot, bool cancel)
    {
        await using var runtime = new NativeRewardRuntime(contentRoot, reputation: true);
        await runtime.LoadRunnerAsync();
        var model = await runtime.Coordinator.PrepareCareerReputationEntryAsync();
        Require(model.UpdateDraft(new("1", "", "", "Interrupted native reputation refresh")), "Initial draft unavailable.");
        await model.PreviewAsync(culture: CultureInfo.InvariantCulture);
        Require(model.CanConfirm, "Exact Core reputation preview unavailable.");
        using var cancellation = new CancellationTokenSource();
        int interruptions = 0;
        EventHandler observer = (_, _) =>
        {
            if (cancel && runtime.Presenter.State.IsBusy && interruptions == 0)
            {
                interruptions++;
                cancellation.Cancel();
            }
        };
        runtime.Presenter.StateChanged += observer;
        runtime.Settings.FailSelectedWorkspaceWrite = !cancel;
        try { await model.ConfirmAsync(cancellation.Token); }
        finally
        {
            runtime.Presenter.StateChanged -= observer;
            runtime.Settings.FailSelectedWorkspaceWrite = false;
        }
        Require(cancel ? interruptions == 1 : runtime.Settings.RejectedWrites == 1,
            "The configured fault did not reach actual native reload.");
        Require(model.Status == Sr5CareerReputationPhoneStatus.RefreshRequired && !model.CanFinish
                && model.CanRecover && !model.CanRetry && !model.CanConfirm
                && model.Checkpoint?.Phase == Sr5CareerReputationPhase.Applied,
            "Interrupted refresh lost an already committed reputation outcome: " + model.Status);
        var store = new FileWorkspaceStore(runtime.StateDirectory);
        var committed = store.Get(runtime.Id).Value!;
        Require(committed.SavedRevision == 2 && committed.ContentRevision == 2
                && committed.Document.AuxiliaryState.CharacterCareerReputationReceipts?.Count == 1,
            "Reputation was not durably saved before interrupted reload.");
        Guid operation = model.OperationId;
        string digest = model.Checkpoint!.CommandDigest;
        await model.RecoverAsync();
        Require(model.CanFinish && model.Status == Sr5CareerReputationPhoneStatus.Recorded
                && model.OperationId == operation && model.Checkpoint?.CommandDigest == digest
                && runtime.Presenter.State.SavedRevision == 2 && runtime.Presenter.State.Error is null
                && runtime.Shell.State.ActiveWorkspaceId == runtime.Id,
            "Lookup plus actual native reload did not recover exactly the original reputation save: " + model.Status);
        RequireSameRewardDocument(committed, store.Get(runtime.Id).Value!);
        Console.WriteLine($"PASS actual reputation save/refresh {(cancel ? "cancellation" : "preference failure")} and lookup-only recovery");
    }

    private static Button ReputationButton(Sr5CareerReputationWizardPage page, string id)
        => ((VerticalStackLayout)ReputationField<Sr5CareerReputationView>(page, "_view").Content!).Children
            .OfType<Button>().Single(button => button.AutomationId == "sr5-reputation-" + id);

    private static async Task NativeReputationOverlappingAppearancesAsync(string contentRoot)
    {
        BlockingNativeReputationRead? gate = null;
        await using var runtime = new NativeRewardRuntime(contentRoot, reputation: true,
            reputationDecorator: service => gate = new(service));
        await runtime.LoadRunnerAsync();
        var model = runtime.Coordinator.CreateCareerReputationModel();
        var page = new Sr5CareerReputationWizardPage(runtime.Coordinator, model);
        using var firstLifetime = InstallManagedReputationLifetime(page);
        Task first = InvokeTokenPageActionAsync(page, "PrepareForAppearanceRefreshAsync", firstLifetime.Token);
        Task? second = null;
        try
        {
            await gate!.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            typeof(Sr5CareerReputationWizardPage).GetMethod("OnDisappearing", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(page, null);
            using var secondLifetime = InstallManagedReputationLifetime(page);
            second = InvokeTokenPageActionAsync(page, "PrepareForAppearanceRefreshAsync", secondLifetime.Token);
            Require(!second.IsCompleted && !ReputationButton(page, "preview").IsEnabled,
                "New appearance completed while an older canceled observation still owned the model.");
            gate.Release.Set();
            await ExpectConsequencesRejectedAsync(() => first, canceled: true);
            await second;
            Require(!model.IsBusy && model.CanEdit && ReputationButton(page, "preview").IsEnabled,
                "Rapid leave/return stranded the actual native page on a stale busy render.");
            Require(gate.Reads == 2, "The new appearance reused the prior canceled source observation.");
            var saved = new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!;
            Require(saved.ContentRevision == 1 && saved.SavedRevision == 1
                    && (saved.Document.AuxiliaryState.CharacterCareerReputationReceipts?.Count ?? 0) == 0,
                "Appearance recovery wrote the character.");
        }
        finally
        {
            gate?.Release.Set();
            try { await first; } catch (OperationCanceledException) { }
            if (second is not null) try { await second; } catch (OperationCanceledException) { }
            gate?.Release.Dispose();
        }
        Console.WriteLine("PASS overlapping native reputation appearances drain old read and freshly enable the returning page");
    }

    private sealed class BlockingNativeReputationRead(ICharacterCareerReputationService inner) : ICharacterCareerReputationService
    {
        public readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly ManualResetEventSlim Release = new(false);
        public int Reads;
        public CharacterCareerReputationReadResult Read(CharacterWorkspaceId id)
        {
            if (Interlocked.Increment(ref Reads) == 1)
            {
                Entered.SetResult();
                if (!Release.Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException("Controlled read gate was not released.");
            }
            return inner.Read(id);
        }
        public CharacterCareerReputationPreviewResult Preview(CharacterCareerReputationRequest request) => inner.Preview(request);
        public CharacterCareerReputationResult Commit(CharacterCareerReputationCommand command, CancellationToken token = default)
            => inner.Commit(command, token);
        public CharacterCareerReputationResult Lookup(CharacterWorkspaceId id, Guid operation, string digest)
            => inner.Lookup(id, operation, digest);
    }

    // Exercise actual page preparation/departure with a controlled managed
    // appearance lifetime. This is not handler attachment or OS lifecycle proof.
    private static CancellationTokenSource InstallManagedReputationLifetime(Sr5CareerReputationWizardPage page)
    {
        var lifetime = new CancellationTokenSource();
        typeof(Sr5CareerReputationWizardPage).GetField("_lifetime", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(page, lifetime);
        return lifetime;
    }
}
