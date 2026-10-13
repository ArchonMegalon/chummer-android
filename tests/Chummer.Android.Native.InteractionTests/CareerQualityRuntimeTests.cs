using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Chummer.Android.Native;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Infrastructure.Workspaces;
using Microsoft.Maui.Controls;

internal static partial class AfterRunAuthorityHarness
{
    internal static async Task RunCareerQualityAsync(string contentRoot)
    {
        foreach (var (culture, spend, gain, unchanged) in new[]
        {
            ("en-GB", "Spend 16 Karma", "Gain 8 Karma", "No Karma change"),
            ("de-AT", "16 Karma ausgeben", "8 Karma erhalten", "Karma unverändert"),
            ("es-ES", "Gastar 16 de Karma", "Ganar 8 de Karma", "Karma sin cambios")
        })
        {
            using var copyCulture = new QualityTestCulture(culture);
            Require(Sr5CareerQualityWizardPage.KarmaChangeLabel(-16) == spend
                && Sr5CareerQualityWizardPage.KarmaChangeLabel(8) == gain
                && Sr5CareerQualityWizardPage.KarmaChangeLabel(0) == unchanged,
                $"Quality cost direction or localization is incorrect for {culture}.");
        }
        using var ui = new IssuedPageUiContext();
        await ui.RunAsync(async () =>
        {
            foreach (bool linked in new[] { false, true })
            {
                var owners = new ControlledLinkedOwner();
                var scope = linked ? new OwnerScope("career-quality-linked") : OwnerScope.LocalSingleUser;
                owners.Set(scope);
                using var oldCulture = new QualityTestCulture(linked ? "de-AT" : "en-GB");
                await using var runtime = new NativeRewardRuntime(contentRoot, linkedOwners: owners,
                    careerQualityWorkspaceFactory: client => new AndroidCareerQualityAtomicWorkspace(client));
                await runtime.Coordinator.InitializeAsync();
                await AccountStartupTask(runtime.Coordinator).WaitAsync(TimeSpan.FromSeconds(10));
                await runtime.LoadRunnerAsync($"""
                    <character><name>Career quality runner</name><gameedition>SR5</gameedition>
                    <settings>{CharacterCreationBootstrapProfiles.PrioritySettingsProfileId}</settings>
                    <metatype>Human</metatype><metatypeguid>a53d885d-a4a4-443d-b6a6-b0a55b0a96c7</metatypeguid>
                    <buildmethod>Priority</buildmethod><createdversion>5.225.0</createdversion><appversion>5.225.0</appversion>
                    <created>True</created><karma>100</karma><nuyen>1000</nuyen>
                    <qualities/><improvements/><contacts/><expenses/><notes>Retain unrelated data</notes></character>
                    """);
                var presenter = new RunnerSessionSr5CareerQualityPresenter(runtime.Coordinator);
                var authority = new Sr5CareerQualityCoordinator(presenter, presenter);
                var editor = await authority.PrepareAsync() ?? throw new InvalidOperationException("Actual Core quality editor missing.");
                Require(editor.OwnerId == scope.NormalizedValue, "Device owner GUID replaced actual Core scope.");
                var originalOwner = runtime.Coordinator.State.DisplayOwnerContext;
                Require(await runtime.Coordinator.PrepareCareerQualityAsync() is null,
                    "Quality reads without explicit owner custody must stay unavailable.");
                var hubStore = new FileWorkspaceStore(runtime.StateDirectory);
                var beforeHub = (linked ? hubStore.Get(scope, runtime.Id) : hubStore.Get(runtime.Id)).Value!;
                var hub = await new RunnerSessionSr5CareerWizardPhoneAuthority(runtime.Coordinator).LoadAsync();
                Require(hub.IsReady, "Career hub projection unavailable: " + hub.Blocker);
                Require(hub.Snapshot!.Families.SelectMany(x => x.Actions)
                    .Single(x => x.ActionId == Chummer.Presentation.Overview.Sr5CareerWizardActionIds.ChangeQuality).CanOpen,
                    "The Career hub dropped a working quality route because it lost the original owner.");
                Require(!hub.Snapshot.Families.SelectMany(x => x.Actions)
                    .Single(x => x.ActionId == Chummer.Presentation.Overview.Sr5CareerWizardActionIds.EditKarmaExpense).CanOpen,
                    "An absent Karma expense must remain unavailable without hiding quality changes.");
                RequireSameRewardDocument(beforeHub,
                    (linked ? hubStore.Get(scope, runtime.Id) : hubStore.Get(runtime.Id)).Value!);
                var eligible = editor.Quotes.Single(x => x.Definition.Name == "Erased"
                    && x.Operation == CharacterCareerQualityOperation.AcquireLevel);
                Require(eligible.CanApply && eligible.RuleKarmaCost == 16, "Real Career cost or admission is wrong.");
                var live = new Sr5CareerQualityLiveCheckpointAuthority(presenter, editor, () => presenter.Binding);
                var backend = new MemoryBackend();
                var ownerBackend = new MemoryBackend();
                var journal = new Sr5CareerQualityCheckpointStore(backend, live, new Sr5CareerMutationOwnerStore(ownerBackend));
                var root = new ContentPage();
                var navigation = new NavigationPage(root);
                var choose = new Sr5CareerQualityWizardPage(runtime.Coordinator, editor, authority, journal, live);
                await navigation.PushAsync(choose, false);
                var window = new Window(navigation);
                using (var alerts = new IssuedPageAlerts(choose, window))
                {
                    await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => IssuedPageLifecycle(choose, "OnAppearing")));
                    var picker = MinimalVisible(choose).OfType<Picker>().Single();
                    var negative = editor.Quotes.First(x => x.CanApply && x.RuleKarmaCost < 0
                        && x.CharacterKarmaDelta == 0
                        && x.Operation == CharacterCareerQualityOperation.AcquireLevel);
                    picker.SelectedIndex = editor.Quotes.ToList().IndexOf(negative);
                    var summary = MinimalVisible(choose).OfType<Label>()
                        .Single(x => x.AutomationId == "sr5-career-quality-summary");
                    string unchanged = linked ? "Karma unverändert" : "No Karma change";
                    Require(picker.Items[picker.SelectedIndex].Contains(unchanged, StringComparison.Ordinal)
                        && summary.Text == $"{unchanged} · Karma 100 → 100",
                        "The real negative-quality rule value was presented as spendable Career Karma.");
                    picker.SelectedIndex = editor.Quotes.ToList().IndexOf(eligible);
                    string spend = linked ? "16 Karma ausgeben" : "Spend 16 Karma";
                    Require(picker.Items[picker.SelectedIndex].Contains(spend, StringComparison.Ordinal)
                        && summary.Text == $"{spend} · Karma 100 → 84",
                        "Positive quality display does not match the actual Core Karma delta.");
                    AssertQualityCopy(choose);
                    var review = MinimalVisible(choose).OfType<Button>().Single(x => x.AutomationId == "sr5-career-quality-review");
                    Require(review.IsEnabled, "Actual eligible quality cannot be reviewed.");
                    await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => ((IButtonController)review).SendClicked()));
                    Require(alerts.Titles.Count == 0, "Choose failed: " + string.Join("; ", alerts.Messages));
                }
                Require(navigation.CurrentPage is Sr5CareerQualityReviewPage, "Review did not advance the wizard.");
                var reviewPage = (Sr5CareerQualityReviewPage)navigation.CurrentPage;
                using (var alerts = new IssuedPageAlerts(reviewPage, window))
                {
                    await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => IssuedPageLifecycle(reviewPage, "OnAppearing")));
                    AssertQualityCopy(reviewPage);
                    var apply = MinimalVisible(reviewPage).OfType<Button>().Single(x => x.AutomationId == "sr5-career-quality-apply");
                    Require(apply.IsEnabled, "Exact owner-bound review cannot apply.");
                    await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => ((IButtonController)apply).SendClicked()));
                    Require(alerts.Titles.Count == 0, "Apply failed: " + string.Join("; ", alerts.Messages));
                }
                Require(navigation.CurrentPage is Sr5CareerQualityReceiptPage, "Saved quality did not advance to its receipt.");
                AssertQualityCopy(navigation.CurrentPage);
                var store = new FileWorkspaceStore(runtime.StateDirectory);
                var saved = (linked ? store.Get(scope, runtime.Id) : store.Get(runtime.Id)).Value!;
                var xml = XElement.Parse(saved.Document.Content);
                Require(saved.ContentRevision == 2 && saved.SavedRevision == 2
                    && xml.Element("karma")!.Value == "84"
                    && xml.Element("qualities")!.Elements("quality").Single().Element("name")!.Value == "Erased"
                    && xml.Element("improvements")!.Elements("improvement").Any(x => x.Element("improvementttype")?.Value == "Erased")
                    && xml.Element("notes")!.Value == "Retain unrelated data", "Actual graph/budget was not saved exactly.");
                Require(journal.TryRead(out var applied, out var blocker), blocker);
                byte[] retained = JsonSerializer.SerializeToUtf8Bytes(saved);
                // Reconstruct the phone journal/presenter and read actual files.
                // This is managed cold readback, not an Android process-death claim.
                var reopenedPresenter = new RunnerSessionSr5CareerQualityPresenter(runtime.Coordinator);
                var reopenedAuthority = new Sr5CareerQualityCoordinator(reopenedPresenter, reopenedPresenter);
                var reopenedEditor = (await reopenedAuthority.PrepareAsync())!;
                var reopenedLive = new Sr5CareerQualityLiveCheckpointAuthority(reopenedPresenter, reopenedEditor, () => reopenedPresenter.Binding);
                var reopenedJournal = new Sr5CareerQualityCheckpointStore(backend, reopenedLive, new Sr5CareerMutationOwnerStore(ownerBackend));
                Require(reopenedJournal.TryRead(out var restored, out blocker), blocker);
                var resolution = await reopenedAuthority.ResolveAsync(restored);
                Require(resolution.Status == Sr5CareerQualityRecoveryStatus.AppliedVerified
                    && resolution.Receipt?.TransactionId == applied.Draft.TransactionId, "Cold readback lost the exact operation.");
                Require(retained.SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(
                    (linked ? store.Get(scope, runtime.Id) : store.Get(runtime.Id)).Value)), "Readback replayed a write.");
                var correction = await reopenedAuthority.CorrectAsync(restored, resolution.Receipt!, Guid.NewGuid(), "Accidental selection");
                // Simulate process loss before retiring the phone journal: no
                // mutation result is retained by the reconstructed reader.
                var correctedPresenter = new RunnerSessionSr5CareerQualityPresenter(runtime.Coordinator);
                var correctedAuthority = new Sr5CareerQualityCoordinator(correctedPresenter, correctedPresenter);
                var correctedEditor = (await correctedAuthority.PrepareAsync())!;
                var correctedLive = new Sr5CareerQualityLiveCheckpointAuthority(correctedPresenter, correctedEditor, () => correctedPresenter.Binding);
                var correctedJournal = new Sr5CareerQualityCheckpointStore(backend, correctedLive, new Sr5CareerMutationOwnerStore(ownerBackend));
                Require(correctedJournal.TryRead(out var interrupted, out blocker), blocker);
                var correctionReadback = await correctedAuthority.ResolveCorrectionAsync(interrupted);
                Require(correctionReadback?.Correction.CorrectionId == correction.CorrectionId,
                    "The committed correction was not recovered from actual Core files.");
                Require(correctedJournal.TryDeleteCorrected(Sr5CareerQualityCheckpointCas.From(interrupted),
                    correctionReadback!.Receipt, correctionReadback.Correction, out blocker), blocker);
                var final = (linked ? new FileWorkspaceStore(runtime.StateDirectory).Get(scope, runtime.Id)
                    : new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id)).Value!;
                var finalXml = XElement.Parse(final.Document.Content);
                Require(final.ContentRevision == 3 && final.SavedRevision == 3 && finalXml.Element("karma")!.Value == "100"
                    && !finalXml.Element("qualities")!.Elements().Any() && !finalXml.Element("expenses")!.Elements().Any(),
                    "Confirmed correction did not restore the actual saved values.");
                owners.Set(new OwnerScope("career-quality-other"));
                owners.Set(scope);
                Require(reopenedPresenter.CurrentOwnerId == Guid.Empty, "A -> B -> A revived the prior phone owner session.");
                Require(await runtime.Coordinator.PrepareCareerQualityAsync(default, originalOwner) is null,
                    "A -> B -> A revived the captured quality-probe owner.");
                Require(owners.ActiveLeases == 0, "Phone quality flow retained an owner lease.");
                foreach (var page in navigation.Navigation.NavigationStack.OfType<NativePageBase>())
                    IssuedPageLifecycle(page, "OnDisappearing");
                ui.AssertHealthy();
                Console.WriteLine($"PASS Career quality {(linked ? "linked" : "local")}: owner-bound Career hub despite unrelated unavailable actions, real selection/review/apply handlers, 100→84, full saved effect/receipt, readable copy, reconstructed journal/readback, explicit managed correction 84→100, owner ABA fence");
            }
        });
    }

    private static void AssertQualityCopy(Page page)
    {
        string text = string.Join("\n", MinimalVisible(page).OfType<Label>().Select(x => x.Text));
        Require(!Regex.IsMatch(text, @"\b[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\b|\b[0-9a-f]{64}\b", RegexOptions.IgnoreCase),
            "Ordinary quality screen exposes machine identifiers.");
    }

    private sealed class QualityTestCulture : IDisposable
    {
        private readonly CultureInfo _before = CultureInfo.CurrentUICulture;
        public QualityTestCulture(string culture)
            => CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        public void Dispose() => CultureInfo.CurrentUICulture = _before;
    }
}
