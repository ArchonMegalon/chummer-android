using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using Chummer.Android.Native;
using Chummer.Application.Owners;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Workspaces;
using Chummer.Presentation;
using Chummer.Presentation.Overview;
using Microsoft.Maui;
using Microsoft.Maui.Controls;

internal static partial class AfterRunAuthorityHarness
{
    internal static async Task RunCareerEntryFailureAsync(string contentRoot)
    {
        using var ui = new IssuedPageUiContext();
        await ui.RunAsync(async () =>
        {
            var priorApp = Application.Current;
            var app = new ReviewRouteApplication();
            Application.Current = app;
            var window = (Window)((Microsoft.Maui.IApplication)app).CreateWindow(null!);
            using var account = new ActualAccountFixture();
            await account.Account.InitializeAsync();
            ObservedCareerQualityWorkspace? quality = null;
            await using var runtime = new NativeRewardRuntime(contentRoot,
                linkedOwners: account.Owner, accountService: account.Account,
                careerQualityWorkspaceFactory: client => quality = new(
                    new AndroidCareerQualityAtomicWorkspace(client)));
            BuildPage? page = null;
            try
            {
                await runtime.Coordinator.InitializeAsync();
                await runtime.LoadRunnerAsync();
                var before = new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!;
                page = new BuildPage(runtime.Coordinator);
                var shell = new Shell();
                shell.Items.Add(new ShellContent { Content = page });
                var scroll = (ScrollView)page.Content!;
                scroll.ScrollToRequested += (_, _) => scroll.SendScrollFinished();
                window.Page = shell;
                await ui.DrainDispatchedAsyncVoidAsync();
                using var alerts = new IssuedPageAlerts(page, window);
                IssuedPageLifecycle(page, "OnDisappearing");
                await ui.BeginAsyncVoid(() => IssuedPageLifecycle(page, "OnAppearing"));
                await ui.DrainDispatchedAsyncVoidAsync();
                Require(alerts.Titles.Count == 0, "SETUP: Career appearance failed before the tested click.");
                Require(ReferenceEquals(Shell.Current?.CurrentPage, page), "SETUP: Career page is not current.");

                // Use the registered Android adapter and exact local client, not
                // a fabricated null snapshot. This intentionally incomplete
                // fixture has no exact metatype GUID and must remain denied.
                for (int click = 1; click <= 2; click++)
                {
                    var button = MinimalVisible(page).OfType<Button>().Single(x =>
                        x.AutomationId == "build-career-quality");
                    await ui.BeginAsyncVoid(() => ((IButtonController)button).SendClicked());
                    await ui.DrainDispatchedAsyncVoidAsync();
                    Require(quality!.Reads == click && quality.Writes == 0,
                        "Failed Career entry replayed a read or attempted a mutation.");
                    Require(alerts.Titles.Count == click && alerts.Messages.Count == click
                        && alerts.Messages[^1] == "Exact SR5 quality authority is unavailable for this dossier.",
                        "Unavailable quality authority must remain visible without escaping the button handler.");
                    Require(ReferenceEquals(Shell.Current?.CurrentPage, page)
                        && page.Navigation.NavigationStack.Count == 1,
                        "Unavailable authority opened an editor or left the Career page.");
                    RequireSameRewardDocument(before,
                        new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!);
                }
                ui.AssertHealthy();
                Console.WriteLine("PASS Career entry: actual quality button, local capability denial contained, explicit retry available, no automatic replay/mutation, cold saved runner unchanged");
            }
            finally
            {
                if (page is not null) IssuedPageLifecycle(page, "OnDisappearing");
                quality?.Dispose();
                app.CloseWindow(window);
                Application.Current = priorApp!;
            }
        });
    }

    private sealed class ObservedCareerQualityWorkspace(AndroidCareerQualityAtomicWorkspace inner)
        : ICareerQualityAtomicWorkspace, IOwnerBoundCareerQualityAtomicWorkspace, IDisposable
    {
        internal int Reads { get; private set; }
        internal int Writes { get; private set; }
        public ICareerQualityAtomicWorkspace ForOwner(OwnerContextStamp expectedOwner)
            => new Bound(this, inner.ForOwner(expectedOwner));
        private sealed class Bound(ObservedCareerQualityWorkspace parent, ICareerQualityAtomicWorkspace workspace)
            : ICareerQualityAtomicWorkspace
        {
            public Task<CareerQualityAuthoritySnapshot?> ReadAsync(CharacterWorkspaceId id, CancellationToken ct)
            {
                parent.Reads++;
                return workspace.ReadAsync(id, ct);
            }
            public Task<CareerQualityAtomicCommitResult?> CommitAsync(CharacterCareerQualityPlan plan, CancellationToken ct)
                => parent.CommitAsync(plan, ct);
            public Task<CareerQualityAtomicCorrectionResult?> CorrectAsync(CharacterCareerQualityCorrectionPlan plan, CancellationToken ct)
                => parent.CorrectAsync(plan, ct);
        }
        public Task<CareerQualityAuthoritySnapshot?> ReadAsync(CharacterWorkspaceId id, CancellationToken ct)
        {
            Reads++;
            return inner.ReadAsync(id, ct);
        }
        public Task<CareerQualityAtomicCommitResult?> CommitAsync(CharacterCareerQualityPlan plan, CancellationToken ct)
        {
            Writes++;
            throw new InvalidOperationException("Read-only entry must never commit a quality.");
        }
        public Task<CareerQualityAtomicCorrectionResult?> CorrectAsync(CharacterCareerQualityCorrectionPlan plan, CancellationToken ct)
        {
            Writes++;
            throw new InvalidOperationException("Read-only entry must never correct a quality.");
        }
        public void Dispose() => inner.Dispose();
    }

    internal static async Task RunCareerMinimalUiAsync(string contentRoot)
    {
        using var ui = new IssuedPageUiContext();
        await ui.RunAsync(async () =>
        {
            await using var runtime = new NativeRewardRuntime(contentRoot);
            await runtime.LoadRunnerAsync();
            var store = new FileWorkspaceStore(runtime.StateDirectory);
            var before = store.Get(runtime.Id).Value!;
            CultureInfo prior = CultureInfo.CurrentUICulture;
            try
            {
                foreach (var (locale, heading, open, quality) in new[]
                {
                    ("en-GB", "Career", "Open Career", "Change a quality"),
                    ("de-AT", "Karriere", "Karriere öffnen", "Vor- oder Nachteil ändern"),
                    ("es-MX", "Carrera", "Abrir Carrera", "Cambiar una cualidad")
                })
                {
                    CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(locale);
                    var dashboard = new BuildPage(runtime.Coordinator);
                    typeof(BuildPage).GetMethod("AddSummary",
                        BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(dashboard, null);
                    Require(MinimalVisibleText(dashboard).Contains("Native reward runner"),
                        "Career summary lost the runner's name.");
                    MinimalRequireNoMachineValues(dashboard);
                    typeof(BuildPage).GetMethod("AddSr5CareerWizardRoute",
                        BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(dashboard, null);
                    string dashboardText = MinimalVisibleText(dashboard);
                    Require(dashboardText.Contains(heading) && dashboardText.Contains(open)
                        && dashboardText.Contains(quality), "Career landing copy is not localized: " + locale);
                    Require(!Regex.IsMatch(dashboardText,
                        "InternalId|SourceId|Core|atomic|four-revision|Direct deep link|Source-bound|Exact chassis"),
                        "Career landing still exposes implementation jargon: " + locale);
                    foreach (string id in new[] { "build-sr5-career-wizard", "build-career-quality",
                        "build-career-skill-group", "build-career-specialization", "build-career-commerce",
                        "build-career-vehicle-workshop" })
                        Require(MinimalVisible(dashboard).OfType<Button>().Single(x => x.AutomationId == id).IsEnabled,
                            "Copy polish removed or disabled a Career destination: " + id);
                    MinimalRequireNoMachineValues(dashboard);

                    var page = new Sr5CareerWizardPage(runtime.Coordinator);
                    await MinimalPrepareAsync(page);
                    var toggle = MinimalVisible(page).OfType<Button>().Single(x =>
                        x.AutomationId == "sr5-career-wizard-details-toggle");
                    Require(!MinimalVisible(page).OfType<Label>().Any(x =>
                        x.AutomationId == "sr5-career-wizard-binding"), "Career diagnostics start expanded.");
                    MinimalRequireNoMachineValues(page);
                    ((IButtonController)toggle).SendClicked();
                    var binding = MinimalVisible(page).OfType<Label>().Single(x =>
                        x.AutomationId == "sr5-career-wizard-binding");
                    string exactBinding = binding.Text;
                    Require(exactBinding.Contains(runtime.Id.Value), "Expanded diagnostics lost the real runner ID.");
                    ((IButtonController)toggle).SendClicked();
                    Require(!binding.IsVisible && binding.Text == exactBinding,
                        "Collapsing diagnostics changed the underlying binding.");
                    MinimalRequireNoMachineValues(page);
                    MinimalRender(page);
                    ((IButtonController)toggle).SendClicked();
                    Require(!binding.IsVisible, "Detached diagnostic button reopened a stale binding.");
                    MinimalRequireNoMachineValues(page);
                    Require(MinimalVisible(page).OfType<Button>().Any(x =>
                        x.AutomationId == "sr5-career-action-after-run" && x.IsEnabled),
                        "Career polish removed the ordinary run-result action.");
                }
            }
            finally { CultureInfo.CurrentUICulture = prior; }
            RequireSameRewardDocument(before, store.Get(runtime.Id).Value!);
        });
        Console.WriteLine("PASS Career minimal UI: EN/DE/ES player copy, six destinations, collapsed exact diagnostics, detached-button rejection, unchanged saved runner");
    }
}
