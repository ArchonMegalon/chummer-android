using Chummer.Android.Native;
using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Infrastructure.Workspaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;

internal static partial class AfterRunAuthorityHarness
{
    internal static async Task RunLifeModuleCompletionForwardAsync(string contentRoot, string? smokeDirectory = null)
    {
        using var ui = new IssuedPageUiContext();
        await ui.RunAsync(async () =>
        {
            foreach (string scenario in smokeDirectory is not null ? new[] { "current" } : new[] {
                         "current", "departure", "return", "stack-change", "owner-b", "owner-aba", "shell-fault", "unknown" })
            {
                var owners = new ControlledLinkedOwner();
                LifeCompletionNativeProbe? probe = null;
                bool committed = false;
                await using var runtime = new NativeRewardRuntime(contentRoot, creationBootstrap: true,
                    productionCreationOverview: true, linkedOwners: owners, lifeModuleInputDrafts: true,
                    lifeCompletionDecorator: actual => probe = new(actual),
                    beforeShellWorkspaceList: () =>
                    {
                        if (committed && scenario == "shell-fault") throw new IOException("Synthetic shell read failure.");
                    });
                await runtime.Coordinator.InitializeAsync();
                await AccountStartupTask(runtime.Coordinator).WaitAsync(TimeSpan.FromSeconds(10));
                await runtime.Coordinator.CreateRunnerAsync();
                await runtime.Presenter.UpdateDialogFieldAsync("newCharacterName", "Life forward " + scenario, default);
                await runtime.Presenter.UpdateDialogFieldAsync("newCharacterBuildMethod", CharacterCreationBuildMethods.LifeModules, default);
                await runtime.Coordinator.ExecuteDialogActionAsync("create_character");
                var id = runtime.Coordinator.State.WorkspaceId!.Value;
                await Task.Run(() => SeedNativeLifeSequence(runtime.Services.GetRequiredService<CharacterCreationFoundationService>(), id));
                await runtime.Presenter.LoadAsync(id, default);
                var store = new FileWorkspaceStore(runtime.StateDirectory);
                var before = store.Get(id).Value!;
                var root = new BuildPage(runtime.Coordinator);
                var scroll = (ScrollView)root.Content!;
                scroll.ScrollToRequested += (_, _) => scroll.SendScrollFinished();
                var navigation = new NavigationPage(root);
                var overview = new LifeModuleCompletionPage(runtime.Coordinator);
                await navigation.PushAsync(overview, false);
                var window = new Window(navigation);
                using var overviewAlerts = new IssuedPageAlerts(overview, window);
                await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => IssuedPageLifecycle(overview, "OnAppearing")));
                var session = (LifeModuleCompletionSession)typeof(LifeModuleCompletionPage)
                    .GetField("_session", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(overview)!;
                Require(session.Ready && session.Preview?.ModuleSequence is not null,
                    "SETUP: actual Life Modules completion was not ready.");
                session.Change(session.Input! with
                {
                    QualityInstanceValues = session.Preview!.ModuleSequence!.QualityLevels.Where(row => row.InstancePrompt is not null)
                        .ToDictionary(row => row.InstancePrompt!.PromptId, _ => "Renraku"),
                    TalentSelection = new("mundane"), AttributePurchases = []
                });
                await session.ReviewAsync(default, () => true);
                var language = session.Preview!.SkillsCatalog!.KnowledgeSkills.First(row => row.CanBeNativeLanguage);
                session.Change(session.Input! with
                {
                    SkillSelection = new([new(language.SourceSkillId, language.Kind, 0, IsNativeLanguage: true)], []),
                    KarmaResourceInvestment = 0, GearSelection = [], LifestyleSelection = [], ContactSelection = [],
                    MagicSelection = new(null, null, [], [], []), StartingNuyenDiceTotal = 6
                });
                await session.ReviewAsync(default, () => true);
                Require(session.CanConfirm, "SETUP: Core rejected the final allocation: " + string.Join(", ", session.Blockers));
                if (smokeDirectory is not null)
                {
                    Require(Path.IsPathFullyQualified(smokeDirectory) && !Directory.Exists(smokeDirectory),
                        "Smoke export needs a new explicit synthetic directory.");
                    Directory.CreateDirectory(smokeDirectory);
                    foreach (string file in Directory.EnumerateFiles(runtime.StateDirectory, "*", SearchOption.AllDirectories))
                    {
                        string target = Path.Combine(smokeDirectory, Path.GetRelativePath(runtime.StateDirectory, file));
                        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                        File.Copy(file, target, overwrite: false);
                    }
                    Console.WriteLine("Life Modules finalization synthetic fixture: " + id.Value);
                }
                var openReview = IssuedElements(overview).OfType<Button>().Single(x => x.AutomationId == "life-open-review");
                await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => ((IButtonController)openReview).SendClicked()));
                IssuedPageLifecycle(overview, "OnDisappearing");
                var page = (LifeModuleCompletionPage)navigation.Navigation.NavigationStack.Last();
                using var alerts = new IssuedPageAlerts(page, window);
                await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => IssuedPageLifecycle(page, "OnAppearing")));
                var confirm = IssuedElements(page).OfType<Button>().Single(x => x.AutomationId == "life-confirm-completion");
                Require(!confirm.IsEnabled, "The forward transition bypassed explicit confirmation.");
                IssuedElements(page).OfType<Switch>().Single(x => x.AutomationId == "life-completion-confirmed").IsToggled = true;
                var unrelated = new ContentPage();
                probe!.AfterConfirm = () =>
                {
                    committed = true;
                    var observed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    ui.Post(async _ =>
                    {
                        try
                        {
                            ((IButtonController)confirm).SendClicked();
                            if (scenario is "departure" or "return")
                            {
                                IssuedPageLifecycle(page, "OnDisappearing");
                                if (scenario == "return") IssuedPageLifecycle(page, "OnAppearing");
                            }
                            if (scenario == "stack-change") await navigation.PushAsync(unrelated, false);
                            observed.TrySetResult();
                        }
                        catch (Exception error) { observed.TrySetException(error); }
                    }, null);
                    Require(observed.Task.Wait(TimeSpan.FromSeconds(5)), "Core confirmation blocked the UI thread.");
                    observed.Task.GetAwaiter().GetResult();
                    if (scenario is "owner-b" or "owner-aba") owners.Set(ContactsOwnerB);
                    if (scenario == "owner-aba") owners.Set(OwnerScope.LocalSingleUser);
                    if (scenario == "unknown") throw new IOException("Synthetic lost confirmation result.");
                };
                await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => ((IButtonController)confirm).SendClicked()));
                committed = false;
                var saved = new FileWorkspaceStore(runtime.StateDirectory).Get(id).Value!;
                Require(probe.ConfirmCalls == 1 && saved.ContentRevision == before.ContentRevision + 1
                    && saved.SavedRevision == saved.ContentRevision
                    && saved.Document.AuxiliaryState.CharacterCreationFinalizationArchive is not null,
                    "Life Modules did not make exactly one durable Career transition: " + scenario);
                if (scenario == "current")
                {
                    Require(navigation.Navigation.NavigationStack.Count == 1
                        && ReferenceEquals(navigation.Navigation.NavigationStack[0], root),
                        "Life Modules finalization still waits for a redundant Open Career tap.");
                    await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => IssuedPageLifecycle(root, "OnAppearing")));
                    Require(IssuedElements(root).Any(x => x.AutomationId == "build-sr5-career-wizard")
                        && runtime.Coordinator.State.Profile?.Created == true,
                        "Automatic continuation did not expose the usable Career screen.");
                    IssuedPageLifecycle(root, "OnDisappearing");
                }
                else
                {
                    Require(ReferenceEquals(navigation.Navigation.NavigationStack.Last(),
                        scenario == "stack-change" ? unrelated : page),
                        "Late or unverified finalization stole navigation: " + scenario);
                    if (scenario == "shell-fault")
                        Require(session.Receipt is not null
                            && session.Blockers.Contains(CharacterCreationFinalizationBlockers.PostCommitReopenRequired)
                            && IssuedElements(page).Any(x => x.AutomationId == "life-completion-saved"),
                            "A failed shell refresh lost the known saved receipt.");
                }
                ((IButtonController)confirm).SendClicked();
                await ui.DrainDispatchedAsyncVoidAsync();
                Require(probe.ConfirmCalls == 1 && alerts.Titles.Count == 0 && overviewAlerts.Titles.Count == 0,
                    "A consumed confirmation dispatched again or raised an alert: " + scenario);
                RequireSameRewardDocument(saved, new FileWorkspaceStore(runtime.StateDirectory).Get(id).Value!);
                Require(owners.ActiveLeases == 0, "Finalization leaked its owner lease.");
                IssuedPageLifecycle(page, "OnDisappearing");
                ui.AssertHealthy();
                Console.WriteLine("PASS actual Core/MAUI Life Modules finalization forward: " + scenario);
            }
        });
    }
}
