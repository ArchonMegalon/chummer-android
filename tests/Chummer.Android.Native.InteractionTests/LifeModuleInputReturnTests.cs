using System.Reflection;
using System.Globalization;
using System.Text.Json;
using Chummer.Android.Native;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Infrastructure.Workspaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;

internal static partial class AfterRunAuthorityHarness
{
    internal static async Task RunLifeModuleInputReturnAsync(string contentRoot)
    {
        var originalCulture = CultureInfo.CurrentUICulture;
        try
        {
            foreach (string locale in new[] { "en", "de", "es" })
            {
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(locale);
                foreach (var (code, key) in new[] {
                    (CharacterCreationLifeModuleContactsQuote.ContactLimitExceeded, "LifeCompletion.ContactLimit"),
                    (CharacterCreationContactsBlockers.ContactInvalid, "LifeCompletion.ContactInvalid"),
                    (CharacterCreationAttributesBlockers.GlobalKarmaExceeded, "LifeCompletion.KarmaExceeded"),
                    (CharacterCreationSkillsBlockers.NativeLanguageInvalid, "Skills.Message.NativeChoice") })
                {
                    string expected = CreationAllocationStrings.Get(key, "missing-translation");
                    Require(expected != "missing-translation" && CreationKarmaCopy.Blocker(code) == expected,
                        "Missing readable input blocker in " + locale + ": " + key);
                }
                Console.WriteLine("PASS Life Modules input blocker copy: " + locale);
            }
        }
        finally { CultureInfo.CurrentUICulture = originalCulture; }
        using var ui = new IssuedPageUiContext();
        await ui.RunAsync(async () =>
        {
            foreach (string kind in new[] { "contact", "skill" })
            foreach (string scenario in new[] { "remove-blocked", "invalid", "current", "stack-change", "departure", "return", "owner-aba" })
            {
                var owners = new ControlledLinkedOwner();
                LifeCompletionNativeProbe? probe = null;
                await using var runtime = new NativeRewardRuntime(contentRoot, creationBootstrap: true,
                    productionCreationOverview: true, linkedOwners: owners, lifeModuleInputDrafts: true,
                    lifeCompletionDecorator: actual => probe = new(actual));
                await runtime.Coordinator.InitializeAsync();
                await AccountStartupTask(runtime.Coordinator).WaitAsync(TimeSpan.FromSeconds(10));
                await runtime.Coordinator.CreateRunnerAsync();
                await runtime.Presenter.UpdateDialogFieldAsync("newCharacterName", "Life input " + kind + " " + scenario, default);
                await runtime.Presenter.UpdateDialogFieldAsync("newCharacterBuildMethod", CharacterCreationBuildMethods.LifeModules, default);
                await runtime.Coordinator.ExecuteDialogActionAsync("create_character");
                var id = runtime.Coordinator.State.WorkspaceId!.Value;
                await Task.Run(() => SeedNativeLifeSequence(runtime.Services.GetRequiredService<CharacterCreationFoundationService>(), id));
                await runtime.Presenter.LoadAsync(id, default);
                var store = new FileWorkspaceStore(runtime.StateDirectory);
                string runnerBefore = JsonSerializer.Serialize(store.Get(id).Value!);
                var root = new ContentPage();
                var navigation = new NavigationPage(root);
                var overview = new LifeModuleCompletionPage(runtime.Coordinator);
                await navigation.PushAsync(overview, false);
                var window = new Window(navigation);
                await Appear();
                var session = (LifeModuleCompletionSession)typeof(LifeModuleCompletionPage)
                    .GetField("_session", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(overview)!;
                Require(session.Ready && session.Preview?.ModuleSequence is not null, "Life input setup failed.");
                session.Change(session.Input! with
                {
                    QualityInstanceValues = session.Preview!.ModuleSequence!.QualityLevels.Where(x => x.InstancePrompt is not null)
                        .ToDictionary(x => x.InstancePrompt!.PromptId, _ => "Renraku"),
                    TalentSelection = new("mundane"), AttributePurchases = []
                });
                await session.ReviewAsync(default, () => true);
                var language = session.Preview!.SkillsCatalog!.KnowledgeSkills.First(x => x.CanBeNativeLanguage);
                session.Change(session.Input! with
                {
                    SkillSelection = new([new(language.SourceSkillId, language.Kind, 0, IsNativeLanguage: true)], []),
                    KarmaResourceInvestment = 0, GearSelection = [], LifestyleSelection = [], ContactSelection = [],
                    MagicSelection = new(null, null, [], [], []), StartingNuyenDiceTotal = 6
                });
                await session.ReviewAsync(default, () => true);
                Require(session.CanConfirm, "Life input fixture is not valid: " + string.Join(", ", session.Blockers));
                if (scenario == "remove-blocked")
                {
                    if (kind == "contact")
                        session.Change(session.Input! with { ContactSelection = [new(Guid.NewGuid(),
                            new("Other contact", "", "", "", "", "", "", "", "", "", "", "", ""), 12, 1)] });
                    else
                    {
                        var other = session.Preview!.SkillsCatalog!.KnowledgeSkills.First(x => x.SourceSkillId != language.SourceSkillId);
                        session.Change(session.Input! with { SkillSelection = session.Input!.SkillSelection! with {
                            Skills = session.Input.SkillSelection.Skills.Append(new(other.SourceSkillId, other.Kind, 99)).ToArray() } });
                    }
                    await session.ReviewAsync(default, () => true);
                    Require(!session.CanConfirm && session.Blockers.Count > 0, "Expected a blocked second draft row.");
                }
                typeof(LifeModuleCompletionPage).GetMethod("Refresh", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(overview, null);
                await Click("life-open-" + (kind == "contact" ? "contacts" : "skills"));
                var parent = Current();
                if (kind == "contact")
                {
                    await Click("life-add-contact");
                    Element<Entry>("life-contact-name").Text = "Mara";
                    Element<Entry>("life-contact-role").Text = "Fixer";
                    if (scenario == "invalid") Element<Stepper>("life-contact-connection").Value = 12;
                }
                else
                {
                    await Click("life-selected-skill-" + language.Kind + "-" + language.SourceSkillId + "-");
                    if (scenario == "invalid") Element<Stepper>("life-skill-levels").Value = 1;
                }
                var editor = (NativePageBase)Current();
                var unrelated = new ContentPage();
                var submit = Element<Button>("life-use-" + kind);
                probe!.AfterPreview = () =>
                {
                    probe.AfterPreview = null;
                    var observed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    ui.Post(async _ =>
                    {
                        try
                        {
                            // A second tap must not queue another review/write.
                            ((IButtonController)submit).SendClicked();
                            if (scenario is "departure" or "return")
                            {
                                IssuedPageLifecycle(editor, "OnDisappearing");
                                if (scenario == "return") IssuedPageLifecycle(editor, "OnAppearing");
                            }
                            if (scenario == "stack-change") await navigation.PushAsync(unrelated, false);
                            observed.TrySetResult();
                        }
                        catch (Exception error) { observed.TrySetException(error); }
                    }, null);
                    Require(observed.Task.Wait(TimeSpan.FromSeconds(5)), "Life input review blocked the UI thread.");
                    observed.Task.GetAwaiter().GetResult();
                    if (scenario == "owner-aba") { owners.Set(ContactsOwnerB); owners.Set(OwnerScope.LocalSingleUser); }
                };
                await Click("life-use-" + kind);
                if (scenario == "invalid")
                {
                    Require(session.Blockers.Count > 0 && !session.CanConfirm, "Core accepted the deliberately invalid input.");
                    Require(ReferenceEquals(Current(), editor), "Rejected " + kind + " input silently left its editor.");
                    Require(IssuedElements(editor).Any(x => x.AutomationId?.StartsWith("life-blocker-", StringComparison.Ordinal) == true),
                        "Rejected input did not explain the Core blocker.");
                    var ordered = IssuedElements(editor).ToList();
                    int submitIndex = ordered.FindIndex(x => x.AutomationId == "life-use-" + kind);
                    Require(submitIndex >= 0 && ordered.FindIndex(x => x.AutomationId?.StartsWith("life-blocker-", StringComparison.Ordinal) == true)
                        < submitIndex, "Rejected-input guidance was hidden below its submit/remove actions.");
                    Guid? contactId = session.Input!.ContactSelection!.SingleOrDefault()?.ContactId;
                    if (kind == "contact")
                    {
                        Require(Element<Entry>("life-contact-name").Text == "Mara" && Element<Entry>("life-contact-role").Text == "Fixer",
                            "Rejected contact input lost its entered identity.");
                        Element<Stepper>("life-contact-connection").Value = 1;
                    }
                    else Element<Stepper>("life-skill-levels").Value = 0;
                    await Click("life-use-" + kind);
                    Require(ReferenceEquals(Current(), parent) && session.Reviewed && session.Saved && session.CanConfirm,
                        "Corrected input did not return to its list after the exact Core review.");
                    if (kind == "contact") Require(session.Input!.ContactSelection is { Count: 1 }
                        && session.Input.ContactSelection[0].ContactId == contactId,
                        "Correcting a rejected new contact appended a duplicate identity.");
                }
                else if (scenario == "remove-blocked")
                {
                    Require(ReferenceEquals(Current(), editor), "Section with another blocked row was accepted.");
                    await Click("life-remove-" + kind);
                    Require(ReferenceEquals(Current(), parent) && session.Saved && session.Reviewed && !session.CanConfirm,
                        "Deleting a draft row left its editor open or admitted a remaining invalid row: "
                        + $"parent={ReferenceEquals(Current(), parent)}, saved={session.Saved}, reviewed={session.Reviewed}, confirm={session.CanConfirm}, "
                        + $"contactsQuote={session.Preview?.ContactsQuote is not null}, skillsQuote={session.Preview?.SkillsQuote is not null}, "
                        + string.Join(", ", session.Blockers));
                    Require(kind == "contact" ? session.Input!.ContactSelection!.Count == 1
                        && session.Input.ContactSelection[0].Identity.Name == "Other contact"
                        : session.Input!.SkillSelection!.Skills.Count == 1
                        && session.Input.SkillSelection.Skills[0].SourceSkillId != language.SourceSkillId,
                        "Deleting one input changed another draft row.");
                }
                else if (scenario == "current")
                {
                    Require(ReferenceEquals(Current(), parent) && session.Reviewed && session.Saved,
                        "A current accepted input did not return automatically.");
                }
                else Require(ReferenceEquals(Current(), scenario == "stack-change" ? unrelated : editor),
                    "Late " + kind + " review stole navigation after " + scenario + ".");

                Require(JsonSerializer.Serialize(store.Get(id).Value!) == runnerBefore && probe.ConfirmCalls == 0,
                    "Input review changed the canonical runner before final confirmation.");
                Require(owners.ActiveLeases == 0, "Input review leaked an owner lease.");
                if (scenario is "invalid" or "current")
                {
                    // Real draft-store/session reopen retains the accepted identity,
                    // then removing this input also returns without finalizing.
                    string expected = JsonSerializer.Serialize(session.Input);
                    IssuedPageLifecycle((NativePageBase)Current(), "OnDisappearing");
                    var reopened = new LifeModuleCompletionPage(runtime.Coordinator, store: new(runtime.StateDirectory));
                    await navigation.PushAsync(reopened, false);
                    await Appear();
                    var cold = (LifeModuleCompletionSession)typeof(LifeModuleCompletionPage)
                        .GetField("_session", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(reopened)!;
                    Require(cold.Reviewed && cold.Saved && JsonSerializer.Serialize(cold.Input) == expected,
                        "Cold input reopen lost or duplicated the selection.");
                    await Click("life-open-" + (kind == "contact" ? "contacts" : "skills"));
                    var list = Current();
                    await Click(kind == "contact" ? "life-contact-" + cold.Input!.ContactSelection!.Single().ContactId
                        : "life-selected-skill-" + language.Kind + "-" + language.SourceSkillId + "-");
                    await Click("life-remove-" + kind);
                    Require(ReferenceEquals(Current(), list) && cold.Reviewed && cold.Saved,
                        "Removing a choice did not return to the current list.");
                    Require(kind == "contact" ? cold.Input!.ContactSelection!.Count == 0 : cold.Input!.SkillSelection!.Skills.Count == 0,
                        "Removing a choice retained its draft row.");
                    Require(JsonSerializer.Serialize(store.Get(id).Value!) == runnerBefore, "Removing a draft changed the runner.");
                }
                if (Current() is NativePageBase currentPage) IssuedPageLifecycle(currentPage, "OnDisappearing");
                ui.AssertHealthy();
                Console.WriteLine("PASS actual Core/MAUI Life Modules input return: " + kind + " / " + scenario);

                ContentPage Current() => (ContentPage)navigation.Navigation.NavigationStack.Last();
                T Element<T>(string key) where T : Element => IssuedElements(Current()).OfType<T>().Single(x => x.AutomationId == key);
                async Task Appear()
                {
                    if (Current() is NativePageBase page && IssuedPageField<int>(page, "_subscribed") == 0)
                        await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => IssuedPageLifecycle(page, "OnAppearing")));
                }
                async Task Click(string key)
                {
                    var previous = Current();
                    var button = Element<Button>(key);
                    Require(button.IsEnabled, "Disabled input action: " + key);
                    using var alerts = new IssuedPageAlerts(previous, window);
                    await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => ((IButtonController)button).SendClicked()));
                    Require(alerts.Titles.Count == 0, "Unexpected input alert: " + string.Join("; ", alerts.Messages));
                    if (!ReferenceEquals(previous, Current()) && previous is NativePageBase previousPage
                        && IssuedPageField<int>(previousPage, "_subscribed") != 0)
                        IssuedPageLifecycle(previousPage, "OnDisappearing");
                    await Appear();
                }
            }
        });
    }
}
