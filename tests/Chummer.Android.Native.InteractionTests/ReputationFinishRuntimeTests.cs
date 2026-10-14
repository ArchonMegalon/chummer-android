using System.Globalization;
using System.Reflection;
using Chummer.Android.Native;
using Chummer.Infrastructure.Workspaces;
using Microsoft.Maui.Controls;

internal static partial class AfterRunAuthorityHarness
{
    internal static async Task RunReputationFinishAsync(string contentRoot)
    {
        using var ui = new IssuedPageUiContext();
        await ui.RunAsync(async () =>
        {
            await using var runtime = new NativeRewardRuntime(contentRoot, reputation: true);
            await runtime.LoadRunnerAsync();
            var reward = await runtime.PrepareRewardAsync();
            await reward.ConfirmAsync();
            Require(reward.CanContinue, "SETUP: real saved reward has no continuation.");
            var reputationOwner = new ReputationFinishOwner(runtime.Owner.CurrentOwnerId);
            var model = await runtime.Coordinator.PrepareCareerReputationEntryAsync(owner: reputationOwner);
            Require(model.UpdateDraft(new("1", "", "", "Career Done navigation")),
                "SETUP: real reputation draft unavailable.");
            await model.PreviewAsync(culture: CultureInfo.InvariantCulture);
            Require(model.CanConfirm, "SETUP: exact Core reputation preview unavailable.");
            await model.ConfirmAsync();
            Require(model.CanFinish && model.Status == Sr5CareerReputationPhoneStatus.Recorded,
                "SETUP: real reputation save did not establish finish authority.");
            Require(!reward.CanContinue && reward.Checkpoint?.Phase == Sr5AfterRunRewardCheckpointPhase.Applied,
                "SETUP: child save must retire the parent handoff, not its durable Applied receipt.");
            var store = new FileWorkspaceStore(runtime.StateDirectory);
            var saved = store.Get(runtime.Id).Value!;
            Require(saved.ContentRevision == 3 && saved.SavedRevision == 3
                && saved.Document.AuxiliaryState.CharacterAfterRunRewardReceipts?.Count == 1
                && saved.Document.AuxiliaryState.CharacterCareerReputationReceipts?.Count == 1,
                "SETUP: expected one reward followed by exactly one reputation save.");
            var savedFiles = ReputationFinishFiles(runtime.StateDirectory);
            Require(savedFiles.ContainsKey("sr5-career-reputation.v1.json"),
                "SETUP: real reputation journal was not persisted.");
            var checkpoint = model.Checkpoint;
            var operation = model.OperationId;
            var pages = new List<Sr5CareerReputationWizardPage>();
            var lifetimes = new List<CancellationTokenSource>();

            async Task<(NavigationPage Navigation, Sr5CareerReputationWizardPage Page)> OpenAsync(
                Page? origin, Sr5AfterRunRewardWizardPage? parentReward = null, Page? intervening = null,
                bool acceptAppearance = true)
            {
                var page = new Sr5CareerReputationWizardPage(runtime.Coordinator, model);
                pages.Add(page);
                var navigation = new NavigationPage(origin ?? parentReward ?? (Page)page);
                if (origin is not null && parentReward is not null)
                    await navigation.PushAsync(parentReward, animated: false);
                if (intervening is not null) await navigation.PushAsync(intervening, animated: false);
                if (!ReferenceEquals(navigation.CurrentPage, page))
                    await navigation.PushAsync(page, animated: false);
                await ui.DrainDispatchedAsyncVoidAsync();
                if (acceptAppearance)
                {
                    var lifetime = InstallManagedReputationLifetime(page);
                    lifetimes.Add(lifetime);
                    await InvokeTokenPageActionAsync(page, "PrepareForAppearanceRefreshAsync", lifetime.Token);
                    Require(ReputationButton(page, "done") is { IsVisible: true, IsEnabled: true },
                        "SETUP: admitted saved reputation has no visible Done button.");
                }
                return (navigation, page);
            }

            async Task ClickDoneAsync(Sr5CareerReputationWizardPage page, bool retained = false)
            {
                var button = ReputationButton(page, "done");
                if (!retained) Require(button.IsVisible && button.IsEnabled,
                    "Done is not visibly available before the actual click.");
                // Retained delivery invokes the registered handler even if the
                // old control is now disabled; it never enables the control or
                // calls FinishAsync directly.
                await ui.BeginAsyncVoid(() =>
                {
                    if (retained) RewardPropagateClick(button);
                    else ((IButtonController)button).SendClicked();
                });
                await ui.DrainDispatchedAsyncVoidAsync();
            }

            void Unchanged(string scenario)
            {
                RequireSameRewardDocument(saved, store.Get(runtime.Id).Value!);
                RequireReputationFinishFiles(savedFiles, runtime.StateDirectory, scenario);
                Require(model.OperationId == operation && ReferenceEquals(model.Checkpoint, checkpoint),
                    scenario + ": navigation replaced the operation or durable receipt.");
            }

            async Task DeniedAsync((NavigationPage Navigation, Sr5CareerReputationWizardPage Page) flow,
                string scenario)
            {
                var stack = flow.Navigation.Navigation.NavigationStack.ToArray();
                var modals = flow.Navigation.Navigation.ModalStack.ToArray();
                await ClickDoneAsync(flow.Page, retained: true);
                Require(flow.Navigation.Navigation.NavigationStack.SequenceEqual(stack)
                    && flow.Navigation.Navigation.ModalStack.SequenceEqual(modals),
                    scenario + ": retained Done changed an unauthorized navigation stack.");
                Unchanged(scenario);
            }

            try
            {
                foreach (bool nested in new[] { false, true })
                foreach (bool wizard in new[] { false, true })
                {
                    Page career = wizard ? new Sr5CareerWizardPage(runtime.Coordinator) : new BuildPage(runtime.Coordinator);
                    var parentReward = nested ? new Sr5AfterRunRewardWizardPage(runtime.Coordinator, reward) : null;
                    var flow = await OpenAsync(career, parentReward);
                    await ClickDoneAsync(flow.Page);
                    Require(ReferenceEquals(flow.Navigation.CurrentPage, career)
                        && flow.Navigation.Navigation.NavigationStack.SequenceEqual(new[] { career }),
                        "Done did not return directly to the exact Career page, retiring only the completed reward.");
                    Unchanged("direct/nested Career finish");
                    await DeniedAsync(flow, "repeated Done after successful finish");
                }

                var table = new PhoneTablePage(runtime.Coordinator);
                var tableFlow = await OpenAsync(table, new Sr5AfterRunRewardWizardPage(runtime.Coordinator, reward));
                await ClickDoneAsync(tableFlow.Page);
                Require(tableFlow.Navigation.CurrentPage is BuildPage tableCareer
                    && tableCareer.UsesCoordinator(runtime.Coordinator)
                    && tableFlow.Navigation.Navigation.NavigationStack.SequenceEqual(new Page[] { table, tableCareer }),
                    "Table continuation must open Career, preserve Table, and retire only its completed reward.");
                Unchanged("Table fallback");
                await DeniedAsync(tableFlow, "repeated Table fallback Done");

                var rootFlow = await OpenAsync(null);
                await ClickDoneAsync(rootFlow.Page);
                Require(rootFlow.Navigation.CurrentPage is BuildPage rootCareer
                    && rootCareer.UsesCoordinator(runtime.Coordinator)
                    && rootFlow.Navigation.Navigation.NavigationStack.SequenceEqual(new Page[] { rootCareer }),
                    "Standalone saved Reputation did not get an explicit Career destination.");
                Unchanged("standalone fallback");

                var unattached = await OpenAsync(new BuildPage(runtime.Coordinator), acceptAppearance: false);
                await DeniedAsync(unattached, "unaccepted appearance");
                var departed = await OpenAsync(new BuildPage(runtime.Coordinator));
                DepartReputationFinishPage(departed.Page);
                await DeniedAsync(departed, "departed appearance");

                var covered = await OpenAsync(new BuildPage(runtime.Coordinator));
                var cover = new ContentPage();
                await covered.Navigation.PushAsync(cover, animated: false);
                await ui.DrainDispatchedAsyncVoidAsync();
                await DeniedAsync(covered, "covered Reputation page");

                var modal = await OpenAsync(new BuildPage(runtime.Coordinator));
                var modalPage = new ContentPage();
                await modal.Navigation.Navigation.PushModalAsync(modalPage, animated: false);
                await ui.DrainDispatchedAsyncVoidAsync();
                Require(modal.Navigation.Navigation.ModalStack.Count == 1,
                    "SETUP: covering modal was not placed in the real MAUI navigation stack.");
                await DeniedAsync(modal, "modal-covered Reputation page");
                await modal.Navigation.Navigation.PopModalAsync(animated: false);
                await ui.DrainDispatchedAsyncVoidAsync();

                var unknown = await OpenAsync(new BuildPage(runtime.Coordinator),
                    intervening: new ContentPage());
                await DeniedAsync(unknown, "unrelated intervening page");
                var unknownOrigin = await OpenAsync(new ContentPage(),
                    new Sr5AfterRunRewardWizardPage(runtime.Coordinator, reward));
                await DeniedAsync(unknownOrigin, "unknown ancestor beneath completed reward");

                var uncommitted = runtime.Coordinator.CreateAfterRunRewardModel(new DateTime(2026, 9, 7), runtime.Owner);
                Require(uncommitted.Checkpoint is null, "SETUP: unsaved reward unexpectedly has a receipt.");
                var unsavedParent = await OpenAsync(new BuildPage(runtime.Coordinator),
                    new Sr5AfterRunRewardWizardPage(runtime.Coordinator, uncommitted));
                await DeniedAsync(unsavedParent, "uncommitted reward parent");

                await using (var foreign = new NativeRewardRuntime(contentRoot, reputation: true))
                {
                    var foreignCareer = await OpenAsync(new BuildPage(foreign.Coordinator));
                    await DeniedAsync(foreignCareer, "foreign Career coordinator");
                    var foreignReward = await OpenAsync(new BuildPage(runtime.Coordinator),
                        new Sr5AfterRunRewardWizardPage(foreign.Coordinator, reward));
                    await DeniedAsync(foreignReward, "foreign reward coordinator");
                }

                var changedOwner = await OpenAsync(new BuildPage(runtime.Coordinator));
                Guid priorOwner = reputationOwner.CurrentOwnerId;
                try
                {
                    // The production Preferences authority captures its identity
                    // at construction. Change the actual injected authority,
                    // not an unrelated later write to the preference store.
                    reputationOwner.CurrentOwnerId = Guid.NewGuid();
                    Require(!model.CanFinish, "SETUP: changed local owner retained finish authority.");
                    await DeniedAsync(changedOwner, "changed local owner");
                }
                finally { reputationOwner.CurrentOwnerId = priorOwner; }

                var stale = await OpenAsync(new BuildPage(runtime.Coordinator));
                // Advance through a separate actual Core command, not an injected
                // revision. The old receipt stays valid history but cannot grant
                // navigation authority for a different current saved binding.
                var laterReward = await runtime.PrepareRewardAsync();
                await laterReward.ConfirmAsync();
                Require(!model.CanFinish, "SETUP: later saved revision did not retire the old fresh binding.");
                var advanced = store.Get(runtime.Id).Value!;
                var advancedFiles = ReputationFinishFiles(runtime.StateDirectory);
                var staleStack = stale.Navigation.Navigation.NavigationStack.ToArray();
                await ClickDoneAsync(stale.Page, retained: true);
                Require(stale.Navigation.Navigation.NavigationStack.SequenceEqual(staleStack),
                    "Stale saved-revision Done changed navigation.");
                RequireSameRewardDocument(advanced, store.Get(runtime.Id).Value!);
                RequireReputationFinishFiles(advancedFiles, runtime.StateDirectory, "stale saved revision");
                Require(advanced.Document.AuxiliaryState.CharacterCareerReputationReceipts?.Count == 1
                    && model.OperationId == operation && ReferenceEquals(model.Checkpoint, checkpoint),
                    "Stale Done replayed or replaced the recorded Reputation operation.");
                ui.AssertHealthy();
                Console.WriteLine("PASS actual Reputation Done: direct/nested Career, Table/root fallback, exact saved reward retirement, repeated/unaccepted/departed/covered/modal/unknown/foreign/stale refusal, unchanged workspace and journal bytes");
            }
            finally
            {
                foreach (var page in pages) DepartReputationFinishPage(page);
                foreach (var lifetime in lifetimes) lifetime.Dispose();
                await ui.DrainDispatchedAsyncVoidAsync();
            }
        });
    }

    private static void DepartReputationFinishPage(Sr5CareerReputationWizardPage page)
        => typeof(Sr5CareerReputationWizardPage).GetMethod("OnDisappearing",
            BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, null);

    private sealed class ReputationFinishOwner(Guid ownerId) : ISr5CareerCheckpointOwnerAuthority
    {
        public Guid CurrentOwnerId { get; set; } = ownerId;
    }

    private static Dictionary<string, byte[]> ReputationFinishFiles(string directory)
        => Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetRelativePath(directory, path), File.ReadAllBytes, StringComparer.Ordinal);

    private static void RequireReputationFinishFiles(Dictionary<string, byte[]> expected, string directory, string scenario)
    {
        var actual = ReputationFinishFiles(directory);
        Require(expected.Count == actual.Count && expected.All(pair =>
            actual.TryGetValue(pair.Key, out var bytes) && pair.Value.AsSpan().SequenceEqual(bytes)),
            scenario + ": navigation changed the saved workspace, journal or another retained file.");
    }
}
