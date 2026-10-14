using System.Text.Json;
using System.Reflection;
using System.Xml.Linq;
using Chummer.Android.Native;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Infrastructure.Workspaces;
using Chummer.Presentation.Overview;
using Microsoft.Maui.Controls;

internal static partial class AfterRunAuthorityHarness
{
    // A retained synthetic, app-finalized runner exercises the same Core source
    // resolver as MauiProgram. Never seed the legacy UI settings catalog: doing
    // so would hide the exact-profile regression this route is meant to detect.
    internal static async Task RunCareerSavedProfileAsync(string contentRoot, string fixture)
    {
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(fixture));
        string xml = json.RootElement.GetProperty("Envelope").GetProperty("Payload").GetString()!;
        Require(bool.TryParse(XDocument.Parse(xml).Root!.Element("created")!.Value, out var created) && created,
            "This route requires an app-finalized synthetic Career fixture.");
        using var ui = new IssuedPageUiContext();
        await ui.RunAsync(async () =>
        {
            var owners = new ControlledLinkedOwner();
            ObservedCareerQualityWorkspace? quality = null;
            await using var runtime = new NativeRewardRuntime(contentRoot,
                linkedOwners: owners,
                productionCreationOverview: true,
                careerQualityWorkspaceFactory: client => quality = new(new AndroidCareerQualityAtomicWorkspace(client)),
                careerSkillGroups: true);
            await runtime.Coordinator.InitializeAsync();
            await AccountStartupTask(runtime.Coordinator).WaitAsync(TimeSpan.FromSeconds(10));
            await runtime.LoadRunnerAsync(xml);
            var store = new FileWorkspaceStore(runtime.StateDirectory);
            var before = store.Get(runtime.Id).Value!;
            var c = runtime.Coordinator;
            foreach (var (name, read, minimum) in new (string, Func<Task<int?>>, int)[]
            {
                ("attributes", async () => (await c.PrepareCareerAttributeAdvanceAsync())?.Attributes.Count, 8),
                ("active-skills", async () => (await c.PrepareCareerActiveSkillAdvanceAsync())?.Skills.Count, 1),
                ("skill-groups", async () => (await c.PrepareCareerSkillGroupAdvanceAsync())?.SkillGroups.Count, 1),
                ("specializations", async () => (await c.PrepareCareerSkillSpecializationAsync())?.Skills.Count, 2),
                ("knowledge-skills", async () => (await c.PrepareCareerKnowledgeSkillAdvanceAsync())?.Skills.Count, 0),
                ("karma", async () => await c.PrepareCareerManualKarmaEditAsync() is null ? null : 1, 1),
                ("nuyen", async () => await c.PrepareCareerManualNuyenEditAsync() is null ? null : 1, 1),
                ("calendar", async () => await c.PrepareCareerCalendarEditAsync() is null ? null : 1, 1)
            })
            {
                var count = await read();
                Require(count is not null && count >= minimum,
                    $"Career {name} did not resolve the saved Core profile: {c.State.Error}");
                RequireSameRewardDocument(before, store.Get(runtime.Id).Value!);
                Console.WriteLine($"PASS saved-profile read: {name}, {count} projected entries, no mutation");
            }
            var attributes = (await c.PrepareCareerAttributeAdvanceAsync())!;
            var body = attributes.Attributes.Single(row => row.Identity.Abbreviation == "BOD");
            Require(body.EffectiveValue == 2 && body.KarmaPoints == 0 && body.KarmaCost == 15
                && body.AvailableKarma == 7 && !body.CanAdvance,
                "Life Modules granted Body must count toward the Career price, not paid allocation.");

            var hub = await new RunnerSessionSr5CareerWizardPhoneAuthority(c).LoadAsync();
            Require(hub.IsReady, "Career hub could not admit the finalized fixture.");
            var originalSession = new Sr5CareerWizardDesktopSession();
            originalSession.Bind(hub.Snapshot!);
            var checkpoints = new Sr5CareerWizardPhoneCheckpointStore(
                new PreferencesSr5CareerWizardPhoneCheckpointBackend());
            Require(originalSession.TrySelectAction(Sr5CareerWizardActionIds.AdjustKarma)
                && checkpoints.TryWrite(originalSession), "SETUP: navigation checkpoint not saved.");
            var family = new Sr5CareerActionFamilyPage(c, originalSession, checkpoints,
                Sr5CareerWizardFamilyIds.Economy, c.State.DisplayOwnerContext);
            int reads = quality!.Reads;
            await MinimalPrepareAsync(family);
            await MinimalPrepareAsync(family);
            Require(quality.Reads == reads && quality.Writes == 0,
                "An unchanged family appearance repeated its projection or wrote a mutation.");
            Require(!MinimalVisible(family).OfType<Label>().Any(x =>
                x.AutomationId == "sr5-career-family-navigation-boundary"),
                "Technical family details must start collapsed.");

            var income = (await c.PrepareCareerManualKarmaEditAsync())!;
            await c.ApplyCareerManualKarmaEditAsync(new CareerManualKarmaEditRequest(
                income.WorkspaceId, income.ContentRevision, income.Karma,
                CharacterCareerManualKarmaAction.Gain, 30, "Synthetic Career route award",
                new DateTime(2026, 10, 14, 12, 0, 0), false, false, false));
            Require(c.State.Error is null && !c.State.IsDirty,
                "Career award did not save: " + c.State.Error);
            var award = store.Get(runtime.Id).Value!;
            await MinimalPrepareAsync(family);
            Require(quality.Reads == reads + 1 && quality.Writes == 0
                && originalSession.State.Snapshot.Binding.WorkspaceRevision == before.ContentRevision
                && checkpoints.Read().Status == Sr5CareerWizardPhoneCheckpointReadStatus.Empty,
                "Returning after an award must replace only the family projection and invalidate its old checkpoint.");
            RequireFamilyActionsReady(family);
            await MinimalPrepareAsync(family);
            Require(quality.Reads == reads + 1, "Fresh family was unnecessarily reloaded.");
            RequireSameRewardDocument(award, store.Get(runtime.Id).Value!);
            attributes = (await c.PrepareCareerAttributeAdvanceAsync())!;
            body = attributes.Attributes.Single(row => row.Identity.Abbreviation == "BOD");
            Require(body.CanAdvance && body.AvailableKarma == 37 && body.KarmaCost == 15,
                "Fresh attribute quote did not observe the saved award.");
            var request = new CareerAttributeAdvanceRequest(attributes.WorkspaceId,
                attributes.ContentRevision, body, body.LogicalRevision, body.SourceRevision,
                body.RuleDigest, true, Guid.NewGuid(), new DateTime(2026, 10, 14, 12, 1, 0));
            Require(await c.ApplyCareerAttributeAdvanceAsync(request),
                "Actual native attribute handler failed to persist: " + c.State.Error);
            var saved = new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!;
            var result = XElement.Parse(saved.Document.Content);
            var savedBody = result.Element("attributes")!.Elements("attribute")
                .Single(row => row.Element("name")!.Value == "BOD");
            Require(saved.ContentRevision == 3 && saved.SavedRevision == 3
                && result.Element("karma")!.Value == "22" && savedBody.Element("karma")!.Value == "1"
                && savedBody.Element("totalvalue")!.Value == "3"
                && result.Element("improvements")!.ToString(SaveOptions.DisableFormatting)
                    == XElement.Parse(before.Document.Content).Element("improvements")!.ToString(SaveOptions.DisableFormatting)
                && result.Element("expenses")!.Elements("expense").Count() == 2,
                "Advancement changed granted effects, paid levels, budget or expense count incorrectly.");
            bool replayRejected = false;
            try { await c.ApplyCareerAttributeAdvanceAsync(request); }
            catch (InvalidOperationException) { replayRejected = true; }
            Require(replayRejected, "Stale attribute request was accepted after its save.");
            await runtime.Presenter.LoadAsync(runtime.Id, default);
            var reopened = (await c.PrepareCareerAttributeAdvanceAsync())!;
            var reopenedBody = reopened.Attributes.Single(row => row.Identity.Abbreviation == "BOD");
            Require(reopenedBody.EffectiveValue == 3 && reopenedBody.KarmaPoints == 1
                && reopenedBody.AvailableKarma == 22
                && reopened.RecoverableReceipts.Any(row => row.ExpenseId == request.ExpenseId),
                "Saved Career profile failed to reopen its exact attribute and receipt.");
            RequireSameRewardDocument(saved, new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!);

            await MinimalPrepareAsync(family);
            RequireFamilyActionsReady(family);
            Require(quality.Reads == reads + 2 && quality.Writes == 0,
                "Returning after attribute advancement must read once without another mutation.");
            var currentSession = (Sr5CareerWizardDesktopSession)typeof(Sr5CareerActionFamilyPage)
                .GetField("_session", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(family)!;
            Require(currentSession.State.Snapshot.Binding.WorkspaceRevision == saved.ContentRevision,
                "Family did not bind the persisted advancement revision.");
            saved = await RunSavedFreeSkillsAsync(runtime, ui);
            // Equal account names after an intervening owner are not the same
            // navigation authority, even if stale overview IDs still match.
            OwnerScope originalOwner = owners.Current;
            owners.Set(new OwnerScope("family-navigation-other"));
            owners.Set(originalOwner);
            await MinimalPrepareAsync(family);
            Require(quality.Reads == reads + 2 && quality.Writes == 0
                && MinimalVisible(family).OfType<Label>().Any(x => x.AutomationId == "sr5-career-family-stale")
                && !MinimalVisible(family).OfType<Button>().Any(x => x.AutomationId == "sr5-career-action-karma"),
                "Owner ABA revived a retained Career page or probed the new account.");
            RequireSameRewardDocument(saved, store.Get(runtime.Id).Value!);
            IssuedPageLifecycle(family, "OnDisappearing");
            quality.Dispose();
            ui.AssertHealthy();
            Console.WriteLine("PASS saved Life Modules Career: award 7→37, Body 2→3 for15 Karma, balance22, grants unchanged, stale replay rejected, exact receipt reopened.");
            Console.WriteLine("PASS actual Core/native-handler saved-profile route; not an Android process-restart or full Career mutation test.");
            Console.WriteLine("PASS Career family: cached unchanged entry, fresh actions after award/advancement, no mutation replay, original session unchanged, exact owner ABA rejected.");
        });
    }

    private static void RequireFamilyActionsReady(Sr5CareerActionFamilyPage page)
    {
        Require(!MinimalVisible(page).OfType<Label>().Any(x => x.AutomationId == "sr5-career-family-stale")
            && MinimalVisible(page).OfType<Button>().Any(x => x.AutomationId == "sr5-career-action-karma" && x.IsEnabled),
            "Career family remained stale instead of offering the freshly saved runner's actions.");
        MinimalRequireNoMachineValues(page);
    }

    private static async Task RunCompactCareerSkillUiAsync(NativeRewardRuntime runtime,
        IssuedPageUiContext ui, CareerActiveSkillAdvanceEditorState editor,
        CharacterCareerActiveSkillAdvanceQuote skill)
    {
        // Actual saved Core quote, production phone journal and button handlers.
        // The compact view must not replace admission or persistence with a UI-only result.
        var store = new FileWorkspaceStore(runtime.StateDirectory);
        var before = store.Get(runtime.Id).Value!;
        var root = new ContentPage();
        var navigation = new NavigationPage(root);
        var choose = new Sr5CareerActiveSkillWizardPage(runtime.Coordinator, editor);
        await navigation.PushAsync(choose, false);
        var window = new Window(navigation);
        using (var alerts = new IssuedPageAlerts(choose, window))
        {
            await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => IssuedPageLifecycle(choose, "OnAppearing")));
            MinimalVisible(choose).OfType<Picker>().Single().SelectedIndex = editor.Skills.ToList().IndexOf(skill);
            var review = MinimalVisible(choose).OfType<Button>().Single(x => x.AutomationId == "sr5-career-active-skill-review");
            Require(review.IsEnabled, "The saved granted skill cannot be reviewed.");
            await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => ((IButtonController)review).SendClicked()));
            Require(alerts.Titles.Count == 0, "Skill choose failed: " + string.Join("; ", alerts.Messages));
        }
        Require(navigation.CurrentPage is Sr5CareerActiveSkillReviewPage, "Skill review did not advance.");
        var reviewPage = (Sr5CareerActiveSkillReviewPage)navigation.CurrentPage;
        using (var alerts = new IssuedPageAlerts(reviewPage, window))
        {
            await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => IssuedPageLifecycle(reviewPage, "OnAppearing")));
            RequireCompactCareerDetails(reviewPage, "sr5-career-active-skill-review-details",
                "sr5-career-active-skill-apply", skill.Identity.SkillId.ToString("D"));
            var labels = MinimalVisible(reviewPage).OfType<Label>().Select(x => x.Text).ToArray();
            Require(labels.Contains("Computer") && labels.Contains("5 → 6") && labels.Contains("22 → 10")
                && labels.Contains("12"), "Compact review lost the name, real rating, cost or remaining Karma.");
            RequireSameRewardDocument(before, store.Get(runtime.Id).Value!);
            var apply = MinimalVisible(reviewPage).OfType<Button>().Single(x => x.AutomationId == "sr5-career-active-skill-apply");
            Require(apply.IsEnabled, "Compact review disabled a valid exact advancement.");
            await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => ((IButtonController)apply).SendClicked()));
            Require(alerts.Titles.Count == 0, "Skill apply failed: " + string.Join("; ", alerts.Messages));
        }
        Require(navigation.CurrentPage is Sr5CareerActiveSkillReceiptPage, "Skill purchase did not reach its verified receipt.");
        var receiptPage = (Sr5CareerActiveSkillReceiptPage)navigation.CurrentPage;
        var saved = store.Get(runtime.Id).Value!;
        using (var alerts = new IssuedPageAlerts(receiptPage, window))
        {
            await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => IssuedPageLifecycle(receiptPage, "OnAppearing")));
            RequireCompactCareerDetails(receiptPage, "sr5-career-active-skill-receipt-details",
                "sr5-career-active-skill-receipt-acknowledge", skill.Identity.SkillId.ToString("D"));
            var labels = MinimalVisible(receiptPage).OfType<Label>().Select(x => x.Text).ToArray();
            Require(labels.Contains("Computer") && labels.Contains("5 → 6")
                && labels.Contains("12") && labels.Contains("10"), "Compact receipt lost saved values.");
            RequireSameRewardDocument(saved, store.Get(runtime.Id).Value!);
            var next = MinimalVisible(receiptPage).OfType<Button>().Single(x =>
                x.AutomationId == "sr5-career-active-skill-receipt-acknowledge");
            Require(next.Text == "Continue", "Receipt still asks the player to acknowledge technical evidence.");
            await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => ((IButtonController)next).SendClicked()));
            Require(alerts.Titles.Count == 0 && ReferenceEquals(navigation.CurrentPage, root),
                "Continue failed to finish the saved advancement.");
        }
        RequireSameRewardDocument(saved, store.Get(runtime.Id).Value!);
        foreach (var page in new NativePageBase[] { choose, reviewPage, receiptPage })
            IssuedPageLifecycle(page, "OnDisappearing");
        Console.WriteLine("PASS compact Career skill: real choose/review/save/Continue, visible rating/cost/balance, IDs collapsed, exact expandable details, no write on disclosure/acknowledgement.");
    }

    private static void RequireCompactCareerDetails(ContentPage page, string detailsId, string primaryId, string exactIdentity)
    {
        MinimalRequireNoMachineValues(page);
        var body = (VerticalStackLayout)((ScrollView)page.Content!).Content!;
        var primary = body.Children.OfType<Button>().Single(x => x.AutomationId == primaryId);
        var details = body.Children.OfType<VerticalStackLayout>().Single(x => x.AutomationId == detailsId);
        Require(body.Children.IndexOf(primary) < body.Children.IndexOf(details),
            "Technical disclosure displaced the ordinary confirmation/Continue action.");
        var toggle = MinimalVisible(details).OfType<Button>().Single();
        string compactText = MinimalVisibleText(page);
        ((IButtonController)toggle).SendClicked();
        Require(MinimalVisibleText(page).Contains(exactIdentity, StringComparison.Ordinal),
            "Expanded details no longer contain the exact underlying identity.");
        ((IButtonController)toggle).SendClicked();
        Require(MinimalVisibleText(page) == compactText, "Collapsing details changed the player-facing result.");
        MinimalRequireNoMachineValues(page);
    }

    private static async Task<Chummer.Application.Workspaces.WorkspaceStoredDocument> RunSavedFreeSkillsAsync(
        NativeRewardRuntime runtime, IssuedPageUiContext ui)
    {
        var c = runtime.Coordinator;
        var store = new FileWorkspaceStore(runtime.StateDirectory);
        var before = XElement.Parse(store.Get(runtime.Id).Value!.Document.Content);
        var active = (await c.PrepareCareerActiveSkillAdvanceAsync())!;
        var computer = active.Skills.Single(row => row.Name == "Computer");
        Require(computer.TotalBaseRating == 5 && computer.KarmaPoints == 0 && computer.KarmaCost == 12
            && computer.CanAdvance && computer.AvailableKarma == 22,
            "Computer must price its two module levels plus three Electronics levels exactly.");
        var skillRequest = new CareerActiveSkillAdvanceRequest(active.WorkspaceId, active.ContentRevision,
            computer, computer.RuleDigest, true, Guid.NewGuid(), new DateTime(2026, 10, 14, 12, 2, 0));
        await RunCompactCareerSkillUiAsync(runtime, ui, active, computer);
        var skillSaved = store.Get(runtime.Id).Value!;
        var skillXml = XElement.Parse(skillSaved.Document.Content);
        Require(skillSaved.ContentRevision == 4 && skillSaved.SavedRevision == 4
            && skillXml.Element("karma")!.Value == "10"
            && XNode.DeepEquals(before.Element("improvements"), skillXml.Element("improvements")),
            "Skill purchase must change paid allocation and budget, never module grants.");
        bool replayRejected = false;
        try { await c.ApplyCareerActiveSkillAdvanceAsync(skillRequest); }
        catch (InvalidOperationException) { replayRejected = true; }
        Require(replayRejected, "A stale skill request was applied twice.");

        var groups = (await c.PrepareCareerSkillGroupAdvanceAsync())!;
        var closeCombat = groups.SkillGroups.Single(row => row.Name == "Close Combat");
        Require(closeCombat.CanAdvance && closeCombat.GroupRating == 1 && closeCombat.CostRating == 1
            && closeCombat.KarmaPoints == 0 && closeCombat.KarmaCost == 10 && closeCombat.AvailableKarma == 10,
            "Close Combat's free level must remain separate from the first paid group point.");
        Require(CharacterCareerSkillGroupAdvanceServiceIntegrity.TryComputeBindingDigest(
            groups.WorkspaceId, groups.ContentRevision, closeCombat, out string binding),
            "UI group quote did not bind to the exact atomic Core transaction.");
        var command = new CharacterCareerSkillGroupAdvanceCommand(
            CharacterCareerSkillGroupAdvanceServiceSchemas.CommandV1, groups.WorkspaceId,
            groups.ContentRevision, closeCombat.Identity, closeCombat.LogicalRevision,
            closeCombat.SourceRevision, closeCombat.RuleDigest, binding, Guid.NewGuid(),
            new DateTime(2026, 10, 14, 12, 3, 0), true);
        var result = await c.AdvanceCareerSkillGroupAsync(command);
        Require(result?.Outcome == CharacterCareerSkillGroupAdvanceServiceOutcome.Applied,
            "Actual atomic skill-group handler did not apply the reviewed UI quote: "
                + result?.Outcome + " " + string.Join(",", result?.Blockers ?? []));
        var saved = store.Get(runtime.Id).Value!;
        var savedXml = XElement.Parse(saved.Document.Content);
        Require(saved.ContentRevision == 5 && saved.SavedRevision == 5
            && savedXml.Element("karma")!.Value == "0"
            && XNode.DeepEquals(before.Element("improvements"), savedXml.Element("improvements"))
            && XNode.DeepEquals(skillXml.Element("newskills")!.Element("skills"), savedXml.Element("newskills")!.Element("skills")),
            "Atomic group save must retain grants and individual allocations with the exact budget.");
        var replay = await c.AdvanceCareerSkillGroupAsync(command);
        Require(replay?.Outcome == CharacterCareerSkillGroupAdvanceServiceOutcome.Replayed
            && replay.Receipt == result!.Receipt, "Group recovery must return its exact original receipt.");
        await runtime.Presenter.LoadAsync(runtime.Id, default);
        var finalSkill = (await c.PrepareCareerActiveSkillAdvanceAsync())!.Skills.Single(row => row.Name == "Computer");
        var finalGroup = (await c.PrepareCareerSkillGroupAdvanceAsync())!.SkillGroups.Single(row => row.Name == "Close Combat");
        Require(finalSkill.TotalBaseRating == 6 && finalSkill.KarmaPoints == 1
            && finalGroup.GroupRating == 2 && finalGroup.KarmaPoints == 1
            && finalSkill.AvailableKarma == 0 && finalGroup.AvailableKarma == 0,
            "Saved free/paid skill/group ratings did not reopen exactly.");
        RequireSameRewardDocument(saved, store.Get(runtime.Id).Value!);
        Console.WriteLine("PASS saved Life Modules: Computer5→6 for12, Close Combat1→2 for10, balance0, grants retained, atomic receipt recovered, saved state reopened.");

        var award = (await c.PrepareCareerManualKarmaEditAsync())!;
        await c.ApplyCareerManualKarmaEditAsync(new CareerManualKarmaEditRequest(
            award.WorkspaceId, award.ContentRevision, award.Karma, CharacterCareerManualKarmaAction.Gain,
            7, "Synthetic specialization award", new DateTime(2026, 10, 14, 12, 4, 0), false, false, false));
        Require(c.State.Error is null && !c.State.IsDirty, "Specialization award did not persist.");
        var specs = (await c.PrepareCareerSkillSpecializationAsync())!;
        var target = specs.Skills.Single(row => row.SkillName == "Computer");
        var option = target.AvailableOptions.First(row => row.Kind == CharacterCareerSkillSpecializationOptionKind.SourceCatalog);
        var selection = new CharacterCareerSkillSpecializationSelection(option.Name, option.Kind, option.OptionIdentity);
        var quote = (await c.PrepareCareerSkillSpecializationQuoteAsync(new(specs.WorkspaceId,
            specs.ContentRevision, target.Identity, selection)))!;
        Require(quote.CanAdd && quote.TotalBaseRating == 6 && quote.KarmaCost == 7 && quote.AvailableKarma == 7,
            "Saved module-granted Computer specialization must price the exact current rating and source option.");
        var specRequest = new CareerSkillSpecializationRequest(specs.WorkspaceId, specs.ContentRevision,
            quote, quote.CharacterRevision, quote.SourceRevision, quote.RuleDigest, quote.LogicalRevision,
            true, Guid.NewGuid(), Guid.NewGuid(), new DateTime(2026, 10, 14, 12, 5, 0));
        Require(await c.ApplyCareerSkillSpecializationAsync(specRequest) is not null,
            "Actual native specialization handler did not persist the exact free-rating purchase.");
        var specialized = store.Get(runtime.Id).Value!;
        var specializedXml = XElement.Parse(specialized.Document.Content);
        Require(specialized.ContentRevision == 7 && specialized.SavedRevision == 7
            && specializedXml.Element("karma")!.Value == "0"
            && XNode.DeepEquals(before.Element("improvements"), specializedXml.Element("improvements"))
            && XNode.DeepEquals(savedXml.Element("newskills")!.Element("groups"),
                specializedXml.Element("newskills")!.Element("groups")),
            "Specialization must retain free grants, paid group allocation and exact budget.");
        replayRejected = false;
        try { await c.ApplyCareerSkillSpecializationAsync(specRequest); }
        catch (InvalidOperationException) { replayRejected = true; }
        Require(replayRejected, "A stale specialization request was applied twice.");
        await runtime.Presenter.LoadAsync(runtime.Id, default);
        var reopenedSpec = (await c.PrepareCareerSkillSpecializationAsync())!.Skills.Single(row => row.Identity == target.Identity);
        var reopenedActive = (await c.PrepareCareerActiveSkillAdvanceAsync())!.Skills.Single(row => row.Name == "Computer");
        Require(reopenedSpec.TotalBaseRating == 6 && reopenedSpec.ExistingSpecializationCount == target.ExistingSpecializationCount + 1
            && reopenedActive.KarmaPoints == 1 && reopenedActive.AvailableKarma == 0,
            "Specialization did not reopen with the identical skill allocation and one added specialization.");
        RequireSameRewardDocument(specialized, store.Get(runtime.Id).Value!);
        Console.WriteLine("PASS saved Life Modules Computer specialization: exact source option, cost7, balance0, unchanged grants/paid levels, stale replay rejected, cold store reopen.");
        return specialized;
    }
}
