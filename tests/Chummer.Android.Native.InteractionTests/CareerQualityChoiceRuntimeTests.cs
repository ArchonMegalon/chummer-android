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
                Require(editor.Quotes.Any(quote => !quote.CanApply)
                    && !offered.Any(quote => !quote.CanApply), "Unavailable qualities leaked into offers.");
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
