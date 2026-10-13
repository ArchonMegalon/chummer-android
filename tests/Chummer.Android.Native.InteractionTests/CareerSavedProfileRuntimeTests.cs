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
                careerQualityWorkspaceFactory: client => quality = new(new AndroidCareerQualityAtomicWorkspace(client)));
            await runtime.Coordinator.InitializeAsync();
            await AccountStartupTask(runtime.Coordinator).WaitAsync(TimeSpan.FromSeconds(10));
            await runtime.LoadRunnerAsync(xml);
            var store = new FileWorkspaceStore(runtime.StateDirectory);
            var before = store.Get(runtime.Id).Value!;
            var c = runtime.Coordinator;
            foreach (var (name, read, minimum) in new (string, Func<Task<int?>>, int)[]
            {
                ("attributes", async () => (await c.PrepareCareerAttributeAdvanceAsync())?.Attributes.Count, 8),
                // An admitted empty editor is distinct from missing profile
                // authority. This test does not claim support for free skill
                // grants that the current projectors explicitly exclude.
                ("active-skills", async () => (await c.PrepareCareerActiveSkillAdvanceAsync())?.Skills.Count, 0),
                ("skill-groups", async () => (await c.PrepareCareerSkillGroupAdvanceAsync())?.SkillGroups.Count, 0),
                ("specializations", async () => (await c.PrepareCareerSkillSpecializationAsync())?.Skills.Count, 0),
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
}
