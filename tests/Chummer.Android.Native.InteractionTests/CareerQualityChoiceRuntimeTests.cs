using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;
using Chummer.Android.Native;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Infrastructure.Workspaces;
using Chummer.Presentation.Overview;
using Microsoft.Maui.Controls;

internal static partial class AfterRunAuthorityHarness
{
    internal static async Task RunCareerQualityChoicesAsync(string contentRoot)
    {
        using var ui = new IssuedPageUiContext();
        using var culture = new QualityTestCulture("en-GB");
        await ui.RunAsync(async () =>
        {
            // Prove unaffordability using real Core state, rather than assuming
            // the generously funded mutation fixture must contain blocked quotes.
            await using (var constrained = new NativeRewardRuntime(contentRoot,
                careerQualityWorkspaceFactory: client => new AndroidCareerQualityAtomicWorkspace(client)))
            {
                await constrained.Coordinator.InitializeAsync();
                await AccountStartupTask(constrained.Coordinator).WaitAsync(TimeSpan.FromSeconds(10));
                await constrained.LoadRunnerAsync($"""
                    <character><name>Career zero-Karma choices</name><gameedition>SR5</gameedition>
                    <settings>{CharacterCreationBootstrapProfiles.PrioritySettingsProfileId}</settings>
                    <metatype>Human</metatype><metatypeguid>a53d885d-a4a4-443d-b6a6-b0a55b0a96c7</metatypeguid>
                    <buildmethod>Priority</buildmethod><createdversion>5.225.0</createdversion><appversion>5.225.0</appversion>
                    <created>True</created><karma>0</karma><nuyen>1000</nuyen>
                    <qualities/><improvements/><contacts/><expenses/><notes>Retain unrelated data</notes></character>
                    """);
                var constrainedPresenter = new RunnerSessionSr5CareerQualityPresenter(constrained.Coordinator);
                var constrainedAuthority = new Sr5CareerQualityCoordinator(constrainedPresenter, constrainedPresenter);
                var constrainedEditor = await constrainedAuthority.PrepareAsync()
                    ?? throw new InvalidOperationException("SETUP: actual zero-Karma Core editor missing.");
                var constrainedOffers = Sr5CareerQualityWizardPage.SelectableChoices(constrainedEditor)
                    .SelectMany(group => group.Options).ToArray();
                Require(constrainedEditor.Quotes.Count > 0 && constrainedEditor.Quotes.All(quote => quote.AvailableKarma == 0),
                    "SETUP: constrained fixture did not project actual zero-Karma authority.");
                Require(constrainedOffers.Any(quote => quote.CanApply
                        && quote.Definition.Type == CharacterCareerQualityType.Negative)
                    && !constrainedOffers.Any(quote => !quote.CanApply
                        || quote.Definition.Name == "Erased"),
                    "Zero-Karma choices must retain an admitted free negative quality and hide the unaffordable Erased purchase.");
                // Core may retain a blocked quote or omit it before UI intake;
                // neither representation may turn it into an available offer.
                Require(constrainedEditor.Quotes.Where(quote => quote.Definition.Name == "Erased")
                    .All(quote => !quote.CanApply), "Core admitted the 16-Karma Erased purchase at zero Karma.");
            }
            var owners = new ControlledLinkedOwner();
            owners.Set(OwnerScope.LocalSingleUser);
            await using var runtime = new NativeRewardRuntime(contentRoot, linkedOwners: owners,
                careerQualityWorkspaceFactory: client => new AndroidCareerQualityAtomicWorkspace(client));
            await runtime.Coordinator.InitializeAsync();
            await AccountStartupTask(runtime.Coordinator).WaitAsync(TimeSpan.FromSeconds(10));
            await runtime.LoadRunnerAsync($"""
                <character><name>Career quality choices</name><gameedition>SR5</gameedition>
                <settings>{CharacterCreationBootstrapProfiles.PrioritySettingsProfileId}</settings>
                <metatype>Human</metatype><metatypeguid>a53d885d-a4a4-443d-b6a6-b0a55b0a96c7</metatypeguid>
                <buildmethod>Priority</buildmethod><createdversion>5.225.0</createdversion><appversion>5.225.0</appversion>
                <created>True</created><karma>1000</karma><nuyen>1000</nuyen>
                <qualities/><improvements/><contacts/><expenses/><notes>Retain unrelated data</notes></character>
                """);
            var files = new FileWorkspaceStore(runtime.StateDirectory);
            var backend = new MemoryBackend();
            var ownerBackend = new MemoryBackend();
            Guid? selectedSource = null;
            for (int level = 0; level < 2; level++)
            {
                var presenter = new RunnerSessionSr5CareerQualityPresenter(runtime.Coordinator);
                var authority = new Sr5CareerQualityCoordinator(presenter, presenter);
                var editor = await authority.PrepareAsync()
                    ?? throw new InvalidOperationException("Actual Core quality editor missing.");
                var groups = Sr5CareerQualityWizardPage.SelectableChoices(editor);
                var offered = groups.SelectMany(group => group.Options).ToArray();
                Require(offered.Length > 0 && offered.All(quote => quote.CanApply
                    && CharacterCareerQualityRules.IsCoherent(quote)
                    && Sr5CareerQualityDraft.EditorMatchesQuote(editor, quote)
                    && quote.Authority.Effects.IsExact
                    && quote.Authority.Effects.UnsupportedFamilies.Count == 0
                    && editor.Quotes.Any(original => ReferenceEquals(original, quote))),
                    "Filtered choices must retain original exact admitted Core quotes only.");
                Require(!offered.Any(quote => !quote.CanApply), "Unavailable qualities leaked into offers.");
                if (level == 0)
                    Require(offered.Any(quote => quote.Definition.Name == "Erased"
                        && quote.Operation == CharacterCareerQualityOperation.AcquireLevel
                        && quote.CharacterKarmaDelta == -16),
                        "The same source profile must admit Erased with sufficient Karma; the zero-Karma absence cannot be a missing source.");
                Require(Sr5CareerQualityWizardPage.SelectableChoices(editor with { OmittedCandidateCount = 1 }).Count == 0
                    && Sr5CareerQualityWizardPage.SelectableChoices(editor with { OmittedReceiptCount = 1 }).Count == 0,
                    "An incomplete authority must not offer an apparently selectable quality.");
                var selected = offered.Where(quote => quote.Operation == CharacterCareerQualityOperation.AcquireLevel
                    && quote.Definition.Type == CharacterCareerQualityType.Positive
                    && !quote.Definition.NoLevels
                    && (quote.Definition.LimitIsUnlimited || quote.Definition.LevelLimit >= 2)
                    && quote.LevelBefore == level && quote.LevelAfter == level + 1
                    && quote.CharacterKarmaDelta <= 0
                    && -(long)quote.CharacterKarmaDelta * 2 <= quote.AvailableKarma
                    && (selectedSource is null || quote.Definition.SourceId == selectedSource))
                    .OrderBy(quote => quote.Definition.SourceId).FirstOrDefault()
                    ?? throw new InvalidOperationException("SETUP: no exact supported two-level Career quality.");
                selectedSource = selected.Definition.SourceId;
                var family = groups.Single(group => group.Options.Contains(selected));
                Require(groups.Count(group => group.Options.Any(quote => quote.Definition.SourceId == selectedSource
                    && quote.Definition.Type == selected.Definition.Type && quote.Extra == selected.Extra
                    && quote.SourceName == selected.SourceName)) == 1,
                    "The same quality was flattened into repeated catalogue rows.");
                if (level == 1)
                {
                    Require(family.Options.Any(quote => quote.Operation == CharacterCareerQualityOperation.RemoveLevel
                        && quote.LevelBefore == 1 && quote.LevelAfter == 0),
                        "An owned quality lost its exact removal alternative.");
                    string? smokeDirectory = Environment.GetEnvironmentVariable("CHUMMER_CAREER_QUALITY_SMOKE_DIRECTORY");
                    if (!string.IsNullOrWhiteSpace(smokeDirectory))
                    {
                        Require(Path.IsPathFullyQualified(smokeDirectory) && !Directory.Exists(smokeDirectory),
                            "Smoke export needs a new absolute synthetic directory.");
                        Directory.CreateDirectory(smokeDirectory);
                        foreach (string file in Directory.EnumerateFiles(runtime.StateDirectory, "*", SearchOption.AllDirectories))
                        {
                            string destination = Path.Combine(smokeDirectory, Path.GetRelativePath(runtime.StateDirectory, file));
                            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                            File.Copy(file, destination, overwrite: false);
                        }
                        Console.WriteLine($"Career quality synthetic fixture: {runtime.Id.Value}; {selected.Definition.Name}; owned level 1; Karma {selected.AvailableKarma}; next delta {selected.CharacterKarmaDelta}");
                    }
                }
                var live = new Sr5CareerQualityLiveCheckpointAuthority(presenter, editor, () => presenter.Binding);
                var journal = new Sr5CareerQualityCheckpointStore(backend, live, new Sr5CareerMutationOwnerStore(ownerBackend));
                if (level == 1)
                {
                    // A retained intent is not a new offer. A disappeared source quote
                    // must stay visible without gaining resume/delete authority.
                    var heldDraft = await authority.ReviewAsync(editor, selected, Guid.NewGuid(), DateTime.Now);
                    Require(journal.TryCreate(Sr5CareerQualityCheckpoint.FromDraft(heldDraft), out var held, out var blocker), blocker);
                    string heldBytes = backend.Read();
                    byte[] runnerBytes = JsonSerializer.SerializeToUtf8Bytes(files.Get(runtime.Id).Value);
                    var unavailable = editor with { Quotes = Array.Empty<CharacterCareerQualityQuote>() };
                    var unavailableLive = new Sr5CareerQualityLiveCheckpointAuthority(presenter, unavailable, () => presenter.Binding);
                    var unavailableJournal = new Sr5CareerQualityCheckpointStore(backend, unavailableLive, new Sr5CareerMutationOwnerStore(ownerBackend));
                    var heldPage = new Sr5CareerQualityWizardPage(runtime.Coordinator, unavailable, authority, unavailableJournal, unavailableLive);
                    var heldNavigation = new NavigationPage(new ContentPage());
                    await heldNavigation.PushAsync(heldPage, false);
                    var heldWindow = new Window(heldNavigation);
                    using (var alerts = new IssuedPageAlerts(heldPage, heldWindow))
                    {
                        await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => IssuedPageLifecycle(heldPage, "OnAppearing")));
                        Require(MinimalVisible(heldPage).OfType<Label>().Single(x => x.AutomationId == "sr5-career-quality-summary")
                            .Text?.Contains(QualityCatalogStrings.Name(selected.Definition.SourceId, selected.Definition.Name), StringComparison.Ordinal) == true,
                            "Filtering erased the current owner's retained unavailable quality.");
                        Require(!MinimalVisible(heldPage).OfType<Picker>().Any()
                            && !MinimalVisible(heldPage).OfType<Button>().Any(button => button.IsEnabled
                                && button.AutomationId is "sr5-career-quality-review" or "sr5-career-quality-resume" or "sr5-career-quality-abandon-reviewed"),
                            "Unavailable retained intent became a new selectable/resumable/deletable offer.");
                        Require(!unavailableJournal.TryDeleteReviewed(Sr5CareerQualityCheckpointCas.From(held), out _)
                            && backend.Read() == heldBytes
                            && runnerBytes.SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(files.Get(runtime.Id).Value)),
                            "Recovery presentation weakened the stale-review fence or changed saved data.");
                    }
                    IssuedPageLifecycle(heldPage, "OnDisappearing");
                    // The original still-current quote authority can explicitly retire
                    // this uncommitted synthetic review; no mutation has been sent.
                    Require(journal.TryDeleteReviewed(Sr5CareerQualityCheckpointCas.From(held), out blocker), blocker);
                    await AssertCareerQualityReviewFeedbackAsync(ui, runtime, presenter, editor, selected);
                }

                var before = files.Get(runtime.Id).Value!;
                var navigation = new NavigationPage(new ContentPage());
                var page = new Sr5CareerQualityWizardPage(runtime.Coordinator, editor, authority, journal, live);
                await navigation.PushAsync(page, false);
                var window = new Window(navigation);
                using (var alerts = new IssuedPageAlerts(page, window))
                {
                    await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => IssuedPageLifecycle(page, "OnAppearing")));
                    SelectCareerQualityQuote(page, editor, selected);
                    var picker = MinimalVisible(page).OfType<Picker>().Single(x => x.AutomationId == "sr5-career-quality-picker");
                    Require(picker.Items.Count == groups.Count, "Quality picker does not contain exactly one row per admitted family.");
                    Require(MinimalVisible(page).OfType<Picker>().Any(x => x.AutomationId == "sr5-career-quality-level-picker")
                        == (family.Options.Count > 1), "Single-transition quality shows a redundant level picker.");
                    Require(MinimalVisible(page).OfType<Label>().Single(x => x.AutomationId == "sr5-career-quality-summary").Text
                        == $"{Sr5CareerQualityWizardPage.KarmaChangeLabel(selected.CharacterKarmaDelta)} · Karma {selected.AvailableKarma} → {selected.AvailableKarma + selected.CharacterKarmaDelta}",
                        "Selected level lost its exact before/after Karma budget.");
                    if (family.Options.Count > 1)
                    {
                        var removal = family.Options.First(quote => quote.Operation == CharacterCareerQualityOperation.RemoveLevel);
                        SelectCareerQualityQuote(page, editor, removal);
                        Require(ReferenceEquals(SelectedCareerQualityQuote(page), removal), "Level chooser lost exact removal identity.");
                        SelectCareerQualityQuote(page, editor, selected);
                    }
                    AssertQualityCopy(page);
                    var review = MinimalVisible(page).OfType<Button>().Single(x => x.AutomationId == "sr5-career-quality-review");
                    Require(review.IsEnabled, "Exact selected quality level cannot be reviewed.");
                    await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => ((IButtonController)review).SendClicked()));
                    Require(alerts.Titles.Count == 0, "Selection failed: " + string.Join("; ", alerts.Messages));
                }
                Require(navigation.CurrentPage is Sr5CareerQualityReviewPage, "Selected level did not reach review.");
                IssuedPageLifecycle(page, "OnDisappearing");
                var reviewPage = (Sr5CareerQualityReviewPage)navigation.CurrentPage;
                using (var alerts = new IssuedPageAlerts(reviewPage, window))
                {
                    await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => IssuedPageLifecycle(reviewPage, "OnAppearing")));
                    var apply = MinimalVisible(reviewPage).OfType<Button>().Single(x => x.AutomationId == "sr5-career-quality-apply");
                    Require(apply.IsEnabled, "Exact level review was not admitted.");
                    await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => ((IButtonController)apply).SendClicked()));
                    Require(alerts.Titles.Count == 0, "Level save failed: " + string.Join("; ", alerts.Messages));
                }
                Require(navigation.CurrentPage is Sr5CareerQualityReceiptPage, "Level save did not reach its receipt.");
                Require(journal.TryRead(out var applied, out var savedBlocker), savedBlocker);
                var saved = files.Get(runtime.Id).Value!;
                var xml = XElement.Parse(saved.Document.Content);
                Require(saved.ContentRevision == before.ContentRevision + 1 && saved.SavedRevision == saved.ContentRevision
                    && xml.Element("karma")!.Value == (selected.AvailableKarma + selected.CharacterKarmaDelta).ToString(System.Globalization.CultureInfo.InvariantCulture)
                    && xml.Element("qualities")!.Elements("quality").Count() == level + 1
                    && xml.Element("notes")!.Value == "Retain unrelated data",
                    "Exact selected level did not save once with the expected budget and untouched fields.");
                byte[] savedBytes = JsonSerializer.SerializeToUtf8Bytes(saved);
                var reopenedPresenter = new RunnerSessionSr5CareerQualityPresenter(runtime.Coordinator);
                var reopenedAuthority = new Sr5CareerQualityCoordinator(reopenedPresenter, reopenedPresenter);
                var reopenedEditor = (await reopenedAuthority.PrepareAsync())!;
                var reopenedLive = new Sr5CareerQualityLiveCheckpointAuthority(reopenedPresenter, reopenedEditor, () => reopenedPresenter.Binding);
                var reopenedJournal = new Sr5CareerQualityCheckpointStore(backend, reopenedLive, new Sr5CareerMutationOwnerStore(ownerBackend));
                Require(reopenedJournal.TryRead(out var restored, out savedBlocker), savedBlocker);
                string retainedJournal = backend.Read();
                var resolution = await reopenedAuthority.ResolveAsync(restored);
                Require(resolution.Status == Sr5CareerQualityRecoveryStatus.AppliedVerified
                    && resolution.Receipt?.TransactionId == applied.Draft.TransactionId
                    && savedBytes.SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(files.Get(runtime.Id).Value))
                    && retainedJournal == backend.Read(), "Reconstructed level readback changed the runner or journal.");
                Require(reopenedJournal.TryDeleteApplied(Sr5CareerQualityCheckpointCas.From(restored), resolution.Receipt!, out savedBlocker), savedBlocker);
                foreach (var openPage in navigation.Navigation.NavigationStack.OfType<NativePageBase>())
                    IssuedPageLifecycle(openPage, "OnDisappearing");
                if (level == 1)
                {
                    var currentGroups = Sr5CareerQualityWizardPage.SelectableChoices(reopenedEditor);
                    var currentFamily = currentGroups.Single(group => group.Options.Any(quote => quote.Definition.SourceId == selectedSource));
                    Require(currentFamily.Options.Count(quote => quote.Operation == CharacterCareerQualityOperation.RemoveLevel) == 2,
                        "Distinct owned instances were silently deduplicated into one removal.");
                    var stale = new Sr5CareerQualityWizardPage(runtime.Coordinator, reopenedEditor, reopenedAuthority, reopenedJournal, reopenedLive);
                    var staleNavigation = new NavigationPage(new ContentPage());
                    await staleNavigation.PushAsync(stale, false);
                    var staleWindow = new Window(staleNavigation);
                    using var staleAlerts = new IssuedPageAlerts(stale, staleWindow);
                    await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => IssuedPageLifecycle(stale, "OnAppearing")));
                    SelectCareerQualityQuote(stale, reopenedEditor, currentFamily.Options[0]);
                    var heldSelection = SelectedCareerQualityQuote(stale);
                    var qualityPicker = MinimalVisible(stale).OfType<Picker>().Single(x => x.AutomationId == "sr5-career-quality-picker");
                    var levelPicker = MinimalVisible(stale).OfType<Picker>().Single(x => x.AutomationId == "sr5-career-quality-level-picker");
                    owners.Set(new OwnerScope("quality-choice-other"));
                    owners.Set(OwnerScope.LocalSingleUser);
                    // Do not refresh first: callbacks must reject the lost owner themselves.
                    levelPicker.SelectedIndex = (levelPicker.SelectedIndex + 1) % currentFamily.Options.Count;
                    qualityPicker.SelectedIndex = (qualityPicker.SelectedIndex + 1) % currentGroups.Count;
                    Require(ReferenceEquals(SelectedCareerQualityQuote(stale), heldSelection)
                        && !qualityPicker.IsEnabled && !levelPicker.IsEnabled
                        && !MinimalVisible(stale).OfType<Button>().Single(x => x.AutomationId == "sr5-career-quality-review").IsEnabled
                        && savedBytes.SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(files.Get(runtime.Id).Value))
                        && string.IsNullOrEmpty(backend.Read()), "Owner ABA changed selection, revived admission or modified retained bytes.");
                    IssuedPageLifecycle(stale, "OnDisappearing");
                }
            }
            Require(owners.ActiveLeases == 0, "Quality choices retained an owner lease.");
            ui.AssertHealthy();
            Console.WriteLine("PASS Career quality choices: exact admitted families/levels, hidden blocked authority, retained unavailable review, actual level 0→1→2 handlers, reconstructed receipt/no replay, per-instance removals and owner ABA callbacks");
        });
    }

    private static async Task AssertCareerQualityReviewFeedbackAsync(IssuedPageUiContext ui,
        NativeRewardRuntime runtime, RunnerSessionSr5CareerQualityPresenter presenter,
        CareerQualityEditorState editor, CharacterCareerQualityQuote selected)
    {
        var files = new FileWorkspaceStore(runtime.StateDirectory);
        var before = files.Get(runtime.Id).Value!;
        foreach (bool returnDuringReview in new[] { false, true })
        {
            var delayed = new DelayedCareerQualityReviewPresenter(presenter);
            var authority = new Sr5CareerQualityCoordinator(delayed, presenter);
            var live = new Sr5CareerQualityLiveCheckpointAuthority(presenter, editor, () => presenter.Binding);
            var backend = new MemoryBackend();
            var ownerBackend = new MemoryBackend();
            var journal = new Sr5CareerQualityCheckpointStore(backend, live, new Sr5CareerMutationOwnerStore(ownerBackend));
            var page = new Sr5CareerQualityWizardPage(runtime.Coordinator, editor, authority, journal, live);
            var navigation = new NavigationPage(new ContentPage());
            await navigation.PushAsync(page, false);
            var window = new Window(navigation);
            using var alerts = new IssuedPageAlerts(page, window);
            Task? pending = null;
            DelayedCareerQualityReviewPresenter.Gate? gate = null;
            try
            {
                await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => IssuedPageLifecycle(page, "OnAppearing")));
                SelectCareerQualityQuote(page, editor, selected);
                var qualities = Element<Picker>("sr5-career-quality-picker");
                var levels = Element<Picker>("sr5-career-quality-level-picker");
                var review = Element<Button>("sr5-career-quality-review");
                var progress = Element<ActivityIndicator>("sr5-career-quality-review-progress");
                Require(qualities.IsEnabled && levels.IsVisible && levels.IsEnabled && review.IsEnabled,
                    "SETUP: feedback must exercise real admitted multi-level choices.");
                int qualityIndex = qualities.SelectedIndex, levelIndex = levels.SelectedIndex;
                gate = delayed.HoldNext(fail: !returnDuringReview);
                pending = ui.BeginAsyncVoid(() => ((IButtonController)review).SendClicked());
                // Observe immediately, before yielding to the pending presenter.
                AssertBusy();
                await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
                Require(delayed.ReviewCalls == 1 && delayed.Requested is { } requested
                    && JsonSerializer.Serialize(requested) == JsonSerializer.Serialize(CareerQualityWorkflow.CreateDraft(editor, selected)),
                    "Review did not capture exactly the selected Core operation before awaiting.");
                ((IButtonController)review).SendClicked(); // May be suppressed by MAUI while disabled.
                levels.SelectedIndex = (levelIndex + 1) % levels.Items.Count;
                qualities.SelectedIndex = (qualityIndex + 1) % qualities.Items.Count;
                Require(delayed.ReviewCalls == 1 && ReferenceEquals(SelectedCareerQualityQuote(page), selected)
                    && qualities.SelectedIndex == qualityIndex && levels.SelectedIndex == levelIndex,
                    "A duplicate tap or queued selector callback changed the pending operation or issued another request.");
                AssertBusy();
                Require(string.IsNullOrEmpty(backend.Read()), "A pending review created a checkpoint before Core completed.");

                if (returnDuringReview)
                {
                    long oldAppearance = IssuedPageField<long>(page, "_appearanceGeneration");
                    IssuedPageLifecycle(page, "OnDisappearing");
                    await navigation.PopAsync(false);
                    await navigation.PushAsync(page, false);
                    // The existing pending callback owns the UI pump join. Start
                    // this real appearance directly and join both on release.
                    IssuedPageLifecycle(page, "OnAppearing");
                    Require(IssuedPageField<long>(page, "_appearanceGeneration") > oldAppearance
                        && IssuedPageField<int>(page, "_subscribed") == 1,
                        "SETUP: the held review did not cross an actual away/back appearance.");
                }
                var stackBeforeCompletion = navigation.Navigation.NavigationStack.ToArray();
                gate.Release.TrySetResult();
                await JoinIssuedPageAsync(pending);
                pending = null;
                await ui.DrainDispatchedAsyncVoidAsync();
                Require(ReferenceEquals(navigation.CurrentPage, page)
                    && navigation.Navigation.NavigationStack.SequenceEqual(stackBeforeCompletion)
                    && string.IsNullOrEmpty(backend.Read()),
                    "A failed or departed review created a late checkpoint or navigated the newer appearance.");
                Require(alerts.Titles.Count == (returnDuringReview ? 0 : 1),
                    "Review failure was not reported once, or a departed result displayed a late alert.");
                AssertRestored();
                RequireSameRewardDocument(before, files.Get(runtime.Id).Value!);

                // Retry is a new explicit action, including after away/back.
                gate = delayed.HoldNext(fail: false);
                pending = ui.BeginAsyncVoid(() => ((IButtonController)review).SendClicked());
                AssertBusy();
                await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
                Require(delayed.ReviewCalls == 2, "Retry did not issue exactly one new explicit review request.");
                gate.Release.TrySetResult();
                await JoinIssuedPageAsync(pending);
                pending = null;
                await ui.DrainDispatchedAsyncVoidAsync();
                Require(navigation.CurrentPage is Sr5CareerQualityReviewPage
                    && journal.TryRead(out var checkpoint, out _)
                    && JsonSerializer.Serialize(checkpoint.Draft.Review.Draft)
                        == JsonSerializer.Serialize(CareerQualityWorkflow.CreateDraft(editor, selected))
                    && !progress.IsRunning && !progress.IsVisible
                    && review.Text == Sr5CareerFlowStrings.Text("Review quality"),
                    "Successful retry lost the exact operation, failed to checkpoint/navigate or retained busy feedback.");
                Require(journal.TryRead(out var reviewed, out var blocker), blocker);
                Require(journal.TryDeleteReviewed(Sr5CareerQualityCheckpointCas.From(reviewed), out blocker), blocker);
                RequireSameRewardDocument(before, files.Get(runtime.Id).Value!);
                Require(delayed.ConfirmCalls == 0 && delayed.CorrectionCalls == 0,
                    "Review feedback implicitly applied or corrected a Career mutation.");

                T Element<T>(string id) where T : Element => IssuedElements(page).OfType<T>()
                    .Single(element => element.AutomationId == id);
                void AssertBusy()
                {
                    Require(progress.IsVisible && progress.IsRunning
                        && review.Text == Sr5CareerFlowStrings.Text("Checking…")
                        && !qualities.IsEnabled && !levels.IsEnabled
                        && IssuedElements(page).OfType<Button>().All(button => !button.IsEnabled),
                        "Review must show immediate checking/progress and disable selectors and every action before awaiting Core.");
                }
                void AssertRestored()
                {
                    Require(!progress.IsVisible && !progress.IsRunning && qualities.IsEnabled && levels.IsEnabled
                        && review.IsEnabled && review.Text == Sr5CareerFlowStrings.Text("Review quality")
                        && ReferenceEquals(SelectedCareerQualityQuote(page), selected),
                        "Failed or old-appearance review did not restore the current exact editable selection for retry.");
                }
            }
            finally
            {
                gate?.Release.TrySetResult();
                if (pending is not null) await JoinIssuedPageAsync(pending);
                foreach (var openPage in navigation.Navigation.NavigationStack.OfType<NativePageBase>())
                    if (IssuedPageField<int>(openPage, "_subscribed") != 0)
                        IssuedPageLifecycle(openPage, "OnDisappearing");
                await ui.DrainDispatchedAsyncVoidAsync();
            }
        }
        ui.AssertHealthy();
        Console.WriteLine("PASS Career quality review feedback: immediate busy UI, exact captured operation, duplicate/selector rejection, failure retry, away/back rejects old result, fresh retry checkpoint, no runner write");
    }

    private sealed class DelayedCareerQualityReviewPresenter(ISr5CareerQualityPresenter inner) : ISr5CareerQualityPresenter
    {
        internal sealed class Gate(bool fail)
        {
            internal readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            internal readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
            internal bool Fail => fail;
        }
        private Gate? _next;
        internal int ReviewCalls, ConfirmCalls, CorrectionCalls;
        internal CareerQualityDraft? Requested;
        internal Gate HoldNext(bool fail) => _next = new(fail);
        public Sr5CareerRunnerBinding Binding => inner.Binding;
        public Task<CareerQualityEditorState?> LoadAsync(CancellationToken ct) => inner.LoadAsync(ct);
        public Task<CareerQualityAuthoritySnapshot?> ReadRecoverySnapshotAsync(CancellationToken ct)
            => inner.ReadRecoverySnapshotAsync(ct);
        public async Task<CareerQualityReview> ReviewAsync(CareerQualityDraft draft, CancellationToken ct)
        {
            ReviewCalls++; Requested = draft;
            var gate = _next ?? throw new InvalidOperationException("SETUP: review was not explicitly released by this test.");
            _next = null;
            gate.Entered.TrySetResult();
            await gate.Release.Task.WaitAsync(ct);
            if (gate.Fail) throw new IOException("Synthetic quality review read failure.");
            return await inner.ReviewAsync(draft, ct);
        }
        public Task<CareerQualityConfirmation> ConfirmAndRefreshAsync(CareerQualityReview review, Guid id,
            DateTime date, CancellationToken ct)
        { ConfirmCalls++; return inner.ConfirmAndRefreshAsync(review, id, date, ct); }
        public Task<CareerQualityCorrectionConfirmation> CorrectAndRefreshAsync(CareerQualityCorrectionRequest request,
            CancellationToken ct)
        { CorrectionCalls++; return inner.CorrectAndRefreshAsync(request, ct); }
    }

    private static CharacterCareerQualityQuote? SelectedCareerQualityQuote(Sr5CareerQualityWizardPage page)
        => (CharacterCareerQualityQuote?)typeof(Sr5CareerQualityWizardPage)
            .GetField("_selected", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page);

    private static void SelectCareerQualityQuote(Sr5CareerQualityWizardPage page,
        CareerQualityEditorState editor, CharacterCareerQualityQuote quote)
    {
        var groups = Sr5CareerQualityWizardPage.SelectableChoices(editor);
        int groupIndex = groups.ToList().FindIndex(group => group.Options.Any(option => ReferenceEquals(option, quote)));
        Require(groupIndex >= 0, "Test selected an unavailable Core quote.");
        var picker = MinimalVisible(page).OfType<Picker>().Single(x => x.AutomationId == "sr5-career-quality-picker");
        picker.SelectedIndex = groupIndex;
        if (groups[groupIndex].Options.Count > 1)
        {
            var levels = MinimalVisible(page).OfType<Picker>().Single(x => x.AutomationId == "sr5-career-quality-level-picker");
            levels.SelectedIndex = groups[groupIndex].Options.ToList().FindIndex(option => ReferenceEquals(option, quote));
        }
        Require(ReferenceEquals(SelectedCareerQualityQuote(page), quote), "Picker did not retain the exact selected Core quote.");
    }
}
