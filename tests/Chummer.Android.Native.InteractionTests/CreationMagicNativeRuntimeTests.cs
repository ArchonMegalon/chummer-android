using Chummer.Android.Native;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Rulesets;
using Chummer.Infrastructure.Files;
using Chummer.Infrastructure.Workspaces;
using Chummer.Infrastructure.Xml;
using Chummer.Presentation.Overview;
using Chummer.Rulesets.Hosting;
using Chummer.Rulesets.Sr5;

/// <summary>Actual source catalogs/services and file persistence, with the ordinary
/// phone draft and Presentation projection. This is not an Android handler or device proof.</summary>
internal static class CreationMagicNativeRuntimeTests
{
    public static void Run(string contentRoot)
    {
        Require(Path.IsPathFullyQualified(contentRoot) && Directory.Exists(Path.Combine(contentRoot, "data")),
            "Supply the explicit Core content directory.");
        string directory = Directory.CreateTempSubdirectory("chummer-native-magic-").FullName;
        try
        {
            var store = new FileWorkspaceStore(directory);
            var resolver = new FileSystemCharacterSourceDataResolver(
                new FileSystemContentOverlayCatalogService(contentRoot, contentRoot, null));
            var queries = new XmlCharacterFileQueries(new CharacterFileService());
            var codec = new Sr5WorkspaceCodec(queries,
                new XmlCharacterSectionQueries(new CharacterSectionService(resolver)),
                new XmlCharacterMetadataCommands(new CharacterFileService()));
            var bootstrap = new CharacterCreationBootstrapService(store,
                new RulesetWorkspaceCodecResolver([codec]), queries, resolver);
            var created = bootstrap.Create(new(CharacterCreationBootstrapSchemas.RequestV1,
                CharacterCreationBootstrapStages.AwaitingFoundationSelection, RulesetDefaults.Sr5,
                "Adept phone projection", "Adept", CharacterCreationBuildMethods.Priority,
                CharacterCreationBootstrapProfiles.PrioritySettingsProfileId));
            Require(created.Outcome == CharacterCreationBootstrapOutcomes.Success, string.Join(",", created.Blockers));
            var id = created.Value!.WorkspaceId;
            var prerequisites = new CharacterCreationPrerequisiteService(store, queries, resolver);
            var initial = prerequisites.Load(new(id)).Value!;
            var ranks = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [CharacterCreationPriorityCategoryIds.Heritage] = "E",
                [CharacterCreationPriorityCategoryIds.Talent] = "C",
                [CharacterCreationPriorityCategoryIds.Attributes] = "A",
                [CharacterCreationPriorityCategoryIds.Skills] = "B",
                [CharacterCreationPriorityCategoryIds.Resources] = "D"
            };
            var heritage = initial.Authority.Options.Single(item => item.CategoryId == CharacterCreationPriorityCategoryIds.Heritage
                && item.Rank == "E").HeritageOptions.First(item => item.IsEnabled && item.MetatypeName == "Human" && item.MetavariantSourceId is null);
            var talent = initial.Authority.Options.Single(item => item.CategoryId == CharacterCreationPriorityCategoryIds.Talent
                && item.Rank == "C").TalentOptions.First(item => item.IsEnabled && item.Value == "Adept" && item.Magic == 4);
            string[] skills = talent.ActiveSkillGrant?.Options.Where(item => item.IsEnabled)
                .Take(talent.ActiveSkillGrant.Quantity).Select(item => item.SelectionId).ToArray() ?? [];
            var priority = prerequisites.Preview(new(initial.Binding, ranks)
            { HeritageSelectionId = heritage.SelectionId, TalentSelectionId = talent.SelectionId,
                TalentActiveSkillSelectionIds = skills }).Value!;
            Require(priority.CanConfirm, string.Join(",", priority.Blockers));
            var selected = prerequisites.Confirm(new(priority.Binding, ranks, priority.PreviewDigest, true)
            { HeritageSelectionId = heritage.SelectionId, TalentSelectionId = talent.SelectionId,
                TalentActiveSkillSelectionIds = skills });
            Require(selected.Outcome == CharacterCreationFoundationOutcomes.Success, string.Join(",", selected.Blockers));
            var attributes = new CharacterCreationAttributesService(store, resolver);
            var attributeState = attributes.Load(new(id)).Value!;
            CharacterCreationAttributeAllocation[] allocations = [new("MAG", 1, 0)];
            var attributeReview = attributes.Preview(new(attributeState.Binding, allocations)).Value!;
            Require(attributeReview.CanConfirm, string.Join(",", attributeReview.Blockers));
            var assigned = attributes.Confirm(new(attributeReview.Binding, allocations, attributeReview.PreviewDigest, true));
            Require(assigned.Outcome == CharacterCreationFoundationOutcomes.Success, string.Join(",", assigned.Blockers));
            var service = new CharacterCreationMagicResonanceService(store, resolver);
            var state = service.Load(new(id)).Value!;
            Require(state.CanEdit, string.Join(",", state.Blockers));
            Require(state.SelectedTalent!.Magic == 4 && state.AdeptPowerPointBudget.Total == 5,
                "Canonical C Adept source MAG must remain 4; confirmed MAG and spend budget must be 5.");
            string sourceTalentDigest = CharacterCreationMagicResonanceDigest.Compute(state.SelectedTalent);
            Require(CharacterCreationMagicResonanceWorkflow.TryProject(state, out var projected),
                "Actual Core state rejected: " + ProjectionDiagnostics(state));
            var editor = projected!;
            var wrongSources = state with { PrerequisiteDraft = state.PrerequisiteDraft! with
                { TalentSelection = state.PrerequisiteDraft!.TalentSelection! with
                    { SourceAnchorIds = state.PrerequisiteDraft.TalentSelection!.SourceAnchorIds.Append("invented-source").ToArray() } } };
            wrongSources = wrongSources with { SnapshotDigest = string.Empty };
            wrongSources = wrongSources with { SnapshotDigest = CharacterCreationMagicResonanceDigest.Compute(wrongSources) };
            Require(!CharacterCreationMagicResonanceWorkflow.TryProject(wrongSources, out _),
                "An invented selection source anchor survived the richer provenance comparison.");
            Require(editor.CanEdit && editor.Budgets.Single(item => item.Kind == CharacterCreationMagicResonanceKinds.AdeptPower).Total == 5,
                "Presentation rejected the actual raised-MAG Core state.");
            var light = editor.AdeptPowers.Single(item => item.Name == "Light Body");
            Require(state.Authority.AdeptPowers.Single(item => item.Identity == light.Identity).MaximumLevels == int.MaxValue
                && light.MaximumLevels == 5 && light.IsEnabled,
                "The phone exposed the source instance limit/unbounded rating instead of current MAG.");
            Require(editor.AdeptPowers.Single(item => item.Name == "Traceless Walk").MaximumLevels == 1,
                "A non-levelled power gained extra ranks.");
            Require(editor.AdeptPowers.Single(item => item.Name == "Improved Reflexes").MaximumLevels == 3,
                "An explicit lower source rating cap was lost.");
            var overview = Program.NewCreationOverview(id, state.Binding.ContentRevision, state.Binding.SavedRevision) with
            { CreationMagicResonance = state, CreationMagicResonanceEditor = editor };
            var phone = new CreationMagicResonancePhoneDraft();
            phone.Bind(editor, overview);
            Require(phone.CreatePowerLevelCandidate(light, 5).Selections.AdeptPowers.Single().Levels == 5,
                "Native selection rejected the legal effective rank.");
            ExpectRejected(() => phone.CreatePowerLevelCandidate(light, 6));
            ExpectRejected(() => phone.CreatePowerLevelCandidate(light with { MaximumLevels = 99 }, 6));
            foreach (decimal forgedBudget in new[] { 4m, 6m })
            {
                var forged = state with { AdeptPowerPointBudget = state.AdeptPowerPointBudget with
                    { Total = forgedBudget, Remaining = forgedBudget }, SnapshotDigest = string.Empty };
                forged = forged with { SnapshotDigest = CharacterCreationMagicResonanceDigest.Compute(forged) };
                Require(!CharacterCreationMagicResonanceWorkflow.TryProject(forged, out _),
                    "Rehashed stale/forged effective budget entered Presentation.");
            }
            // Explicit user choices spend 5 PP at full source cost: 1.25 + 1.25 + 1 + 1 + .5.
            (string Name, int Levels)[] choices = [("Light Body", 5), ("Adrenaline Boost", 5),
                ("Missile Parry", 4), ("Traceless Walk", 1), ("Wall Running", 1)];
            var powers = choices.Select(choice => new CharacterCreationAdeptPowerAllocation(
                    editor.AdeptPowers.Single(item => item.Name == choice.Name).Identity, choice.Levels)).ToArray();
            var draft = CreationMagicResonancePhoneAuthority.CreateDraft(editor, new(null, null, powers, [], []));
            var review = CharacterCreationMagicResonanceWorkflow.Review(service, editor, draft);
            Require(review.Preview.CanConfirm && phone.TryAdopt(editor, overview, review), "Native review lost exact Core budget authority.");
            string beforeXml = store.Get(id).Value!.Document.Content;
            string key = CreationMagicResonancePhoneAuthority.ComputeIdempotencyKey(review);
            var confirmed = CharacterCreationMagicResonanceWorkflow.Confirm(service, review, key, explicitlyConfirmed: true);
            var coldStore = new FileWorkspaceStore(directory);
            var coldService = new CharacterCreationMagicResonanceService(coldStore, resolver);
            var cold = coldService.Load(new(id)).Value!;
            var reopened = CharacterCreationMagicResonanceWorkflow.Project(cold);
            Require(reopened.CanEdit && reopened.AdeptPowers.Single(item => item.Identity == light.Identity).MaximumLevels == 5
                && reopened.Budgets.Single(item => item.Kind == CharacterCreationMagicResonanceKinds.AdeptPower).Remaining == 0,
                "Cold file-store/native projection lost the confirmed budget or rating cap.");
            Require(CharacterCreationMagicResonanceDigest.Compute(cold.SelectedTalent!) == sourceTalentDigest
                && coldStore.Get(id).Value!.Document.Content == beforeXml,
                "Wizard confirmation rewrote source Talent or applied character effects before finalization.");
            var replay = CharacterCreationMagicResonanceWorkflow.Confirm(coldService, review, key, explicitlyConfirmed: true);
            Require(replay.Receipt.ReceiptDigest == confirmed.Receipt.ReceiptDigest
                && coldStore.Get(id).Value!.ContentRevision == confirmed.Receipt.ContentRevision, "Retry persisted another mutation.");
            phone.Bind(editor, Program.NewCreationOverview(id, cold.Binding.ContentRevision, cold.Binding.SavedRevision) with
                { CreationMagicResonance = state, CreationMagicResonanceEditor = editor });
            ExpectRejected(() => phone.CreatePowerLevelCandidate(light, 4));
            Console.WriteLine("PASS actual Adept source/raised MAG → Presentation caps → phone review/confirm → cold file-store reopen/replay");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    private static string ProjectionDiagnostics(CharacterCreationMagicResonanceState state)
    {
        bool Probe(string name, params object[] args) => (bool)typeof(CharacterCreationMagicResonanceWorkflow)
            .GetMethod(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.Invoke(null, args)!;
        return $"binding={Probe("BindingDigestsAreCanonical", state.Binding)}; "
            + $"priority={Probe("PrerequisiteSelectsExactTalent", state.PrerequisiteDraft!, state.SelectedTalent!)}; "
            + $"budgets={Probe("BudgetsAreValid", state)}; "
            + $"authority={CharacterCreationMagicResonanceDraftIntegrity.IsValidAuthority(state.Authority)}; "
            + $"revision={state.Binding.ContentRevision}/{state.Binding.SavedRevision}; "
            + $"attributes={CharacterCreationMagicResonanceFinalizationRules.TryResolveEffectiveAttributes(state.SelectedTalent!, state.AttributesDraft!, out _)}; "
            + $"profile={state.PrerequisiteDraft!.SettingsProfileId == state.Authority.SettingsProfileId}; "
            + $"prerequisite-authority={state.Binding.PrerequisiteAuthorityDigest == state.Authority.PrerequisiteAuthorityDigest}; "
            + $"snapshot={state.SnapshotDigest == CharacterCreationMagicResonanceDigest.Compute(state with { SnapshotDigest = string.Empty })}";
    }
    private static void ExpectRejected(Action action)
    {
        try { action(); }
        catch (InvalidOperationException) { return; }
        throw new InvalidOperationException("A stale or out-of-range phone selection was accepted.");
    }
}
