using Chummer.Android.Native;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;
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
        foreach ((string locale, string title) in new[]
                 { ("de-AT", "Talentauswahl"), ("en-GB", "Talent choices"), ("es-MX", "Opciones del talento") })
        {
            var culture = System.Globalization.CultureInfo.GetCultureInfo(locale);
            Require(CreationFlowStrings.Get("TalentChoices.Title", "missing", culture) == title,
                "Talent choice title did not use the actual satellite resource.");
            foreach (string key in new[] { "SelectionOnly", "Continue", "ChooseMore", "SelectionSlot" })
                Require(CreationFlowStrings.Get("TalentChoices." + key, "missing", culture) != "missing", key);
            Require(CreationFlowStrings.Format(culture, "TalentChoices.SelectionSlot", "missing", 1, "Sorcery")
                    .Contains("Sorcery", StringComparison.Ordinal),
                "Translated copy must preserve the Core-provided selected group.");
        }
        foreach (string aspect in new[] { "Conjuring", "Enchanting", "Sorcery" })
            RunTalent(contentRoot, technomancer: false, aspectedGroup: aspect);
        RunTalent(contentRoot, technomancer: false);
        RunTalent(contentRoot, technomancer: true);
        RunTalent(contentRoot, technomancer: false, mysticAdept: true);
    }

    private static void RunTalent(string contentRoot, bool technomancer, bool mysticAdept = false,
        string? aspectedGroup = null)
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
                "Awakened phone projection", technomancer ? "Technomancer" : "Adept", CharacterCreationBuildMethods.Priority,
                CharacterCreationBootstrapProfiles.PrioritySettingsProfileId));
            Require(created.Outcome == CharacterCreationBootstrapOutcomes.Success, string.Join(",", created.Blockers));
            var id = created.Value!.WorkspaceId;
            var prerequisites = new CharacterCreationPrerequisiteService(store, queries, resolver);
            var initial = prerequisites.Load(new(id)).Value!;
            var ranks = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [CharacterCreationPriorityCategoryIds.Heritage] = "E",
                [CharacterCreationPriorityCategoryIds.Talent] = aspectedGroup is null ? "C" : "D",
                [CharacterCreationPriorityCategoryIds.Attributes] = "A",
                [CharacterCreationPriorityCategoryIds.Skills] = "B",
                [CharacterCreationPriorityCategoryIds.Resources] = aspectedGroup is null ? "D" : "C"
            };
            var heritage = initial.Authority.Options.Single(item => item.CategoryId == CharacterCreationPriorityCategoryIds.Heritage
                && item.Rank == "E").HeritageOptions.First(item => item.IsEnabled && item.MetatypeName == "Human" && item.MetavariantSourceId is null);
            var talent = initial.Authority.Options.Single(item => item.CategoryId == CharacterCreationPriorityCategoryIds.Talent
                && item.Rank == ranks[CharacterCreationPriorityCategoryIds.Talent]).TalentOptions.First(item => item.IsEnabled
                    && (aspectedGroup is not null ? item.Value == "Aspected Magician"
                        : technomancer ? item.Value == "Technomancer" : mysticAdept ? item.Value == "Mystic Adept"
                        : item.Value == "Adept" && item.Magic == 4));
            string[] skills = talent.ActiveSkillGrant?.Options.Where(item => item.IsEnabled)
                .Take(talent.ActiveSkillGrant.Quantity).Select(item => item.SelectionId).ToArray() ?? [];
            string[] groups = [];
            if (aspectedGroup is not null)
            {
                var priorityOverview = PriorityOverview(initial);
                var priorityPhone = new CreationPrerequisitePhoneDraft();
                priorityPhone.Bind(initial, priorityOverview);
                foreach (var rank in ranks)
                    Require(priorityPhone.TrySelect(initial, priorityOverview, rank.Key, rank.Value),
                        $"Phone rejected source Priority {rank.Key}/{rank.Value}; ready={CreationPrerequisitePhoneAuthority.IsReady(initial, priorityOverview)}.");
                Require(priorityPhone.TrySelectHeritage(initial, priorityOverview, heritage.SelectionId), "Phone rejected Human.");
                Require(priorityPhone.TrySelectTalent(initial, priorityOverview, talent.SelectionId),
                    "Phone rejected a mandatory source choice merely because it grants zero skill levels.");
                Require(!priorityPhone.CanPrepare(initial, priorityOverview) && priorityPhone.Selections(initial, priorityOverview) is null,
                    "The phone silently chose an aspect or skipped the mandatory zero-rating prompt.");
                Require(!priorityPhone.TryToggleTalentSkillGroup(initial, priorityOverview, "invented-group"), "Phone accepted an invented aspect.");
                var group = talent.SkillGroupGrant!.Options.Single(item => item.CanonicalName == aspectedGroup);
                Require(talent.SkillGroupGrant.BaseRating == 0 && talent.SkillGroupGrant.Quantity == 1,
                    "The canonical Priority D source must require one choice without free levels.");
                Require(priorityPhone.TryToggleTalentSkillGroup(initial, priorityOverview, group.SelectionId), "Phone rejected the explicit aspect.");
                Require(!priorityPhone.TryToggleTalentSkillGroup(initial, priorityOverview,
                    talent.SkillGroupGrant.Options.First(item => item.SelectionId != group.SelectionId).SelectionId),
                    "The phone accepted more than the source-owned choice quantity.");
                Require(priorityPhone.CanPrepare(initial, priorityOverview), "An explicit zero-rating choice did not complete Priority.");
                groups = priorityPhone.Selections(initial, priorityOverview)!.TalentSkillGroupSelectionIds.ToArray();
                Require(groups.SequenceEqual(new[] { group.SelectionId }), "Phone selection was not source-bound.");
                var omitted = prerequisites.Preview(new(initial.Binding, ranks)
                    { HeritageSelectionId = heritage.SelectionId, TalentSelectionId = talent.SelectionId });
                Require(omitted.Value is not { CanConfirm: true }, "Core skipped a required zero-rating choice.");
                var duplicated = prerequisites.Preview(new(initial.Binding, ranks)
                    { HeritageSelectionId = heritage.SelectionId, TalentSelectionId = talent.SelectionId,
                        TalentSkillGroupSelectionIds = [group.SelectionId, group.SelectionId] });
                Require(duplicated.Value is not { CanConfirm: true }, "Core accepted a repeated source choice.");
            }
            var priority = prerequisites.Preview(new(initial.Binding, ranks)
            { HeritageSelectionId = heritage.SelectionId, TalentSelectionId = talent.SelectionId,
                TalentActiveSkillSelectionIds = skills, TalentSkillGroupSelectionIds = groups }).Value!;
            Require(priority.CanConfirm, string.Join(",", priority.Blockers));
            var selected = prerequisites.Confirm(new(priority.Binding, ranks, priority.PreviewDigest, true)
            { HeritageSelectionId = heritage.SelectionId, TalentSelectionId = talent.SelectionId,
                TalentActiveSkillSelectionIds = skills, TalentSkillGroupSelectionIds = groups });
            Require(selected.Outcome == CharacterCreationFoundationOutcomes.Success, string.Join(",", selected.Blockers));
            if (aspectedGroup is not null)
            {
                var reloadedPriority = new CharacterCreationPrerequisiteService(new FileWorkspaceStore(directory), queries, resolver)
                    .Load(new(id)).Value!;
                var reloadedOverview = PriorityOverview(reloadedPriority);
                var reloadedPhone = new CreationPrerequisitePhoneDraft();
                reloadedPhone.Bind(reloadedPriority, reloadedOverview);
                Require(reloadedPhone.CanPrepare(reloadedPriority, reloadedOverview)
                    && reloadedPhone.TalentSkillGroupSelectionIds(reloadedPriority, reloadedOverview).SequenceEqual(groups),
                    "Cold Priority phone lost the explicit zero-rating selection before dependent drafts exist: "
                    + PriorityDiagnostics(reloadedPriority, reloadedOverview, reloadedPhone));
            }
            var attributes = new CharacterCreationAttributesService(store, resolver);
            var attributeState = attributes.Load(new(id)).Value!;
            CharacterCreationAttributeAllocation[] allocations = [new(technomancer ? "RES" : "MAG", 1, 0)];
            var attributeReview = attributes.Preview(new(attributeState.Binding, allocations)).Value!;
            Require(attributeReview.CanConfirm, string.Join(",", attributeReview.Blockers));
            var assigned = attributes.Confirm(new(attributeReview.Binding, allocations, attributeReview.PreviewDigest, true));
            Require(assigned.Outcome == CharacterCreationFoundationOutcomes.Success, string.Join(",", assigned.Blockers));
            var firstSkillsCommand = RunSkillAccess(store, resolver, id, directory, technomancer, aspectedGroup);
            var service = new CharacterCreationMagicResonanceService(store, resolver);
            var state = service.Load(new(id)).Value!;
            Require(state.CanEdit, string.Join(",", state.Blockers));
            if (aspectedGroup is not null)
            {
                RunAspected(store, resolver, service, state, id, directory, aspectedGroup);
                RunSkillsRevisit(resolver, id, directory, firstSkillsCommand, technomancer);
                return;
            }
            if (technomancer)
            {
                RunTechnomancer(store, resolver, service, state, id, directory);
                RunSkillsRevisit(resolver, id, directory, firstSkillsCommand, technomancer);
                return;
            }
            if (mysticAdept)
            {
                RunMysticAdept(store, resolver, service, state, id, directory);
                RunSkillsRevisit(resolver, id, directory, firstSkillsCommand, technomancer);
                return;
            }
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
            RunSkillsRevisit(resolver, id, directory, firstSkillsCommand, technomancer);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static CharacterCreationSkillsConfirmRequest RunSkillAccess(FileWorkspaceStore store, FileSystemCharacterSourceDataResolver resolver,
        CharacterWorkspaceId id, string directory, bool technomancer, string? aspect)
    {
        var service = new CharacterCreationSkillsService(store, resolver);
        var state = service.Load(new(id)).Value!;
        var overview = Program.NewCreationOverview(id, state.Binding.ContentRevision, state.Binding.SavedRevision);
        Require(CreationSkillsPhoneAuthority.IsReady(state, overview), "Source-bound Skills unavailable: " + string.Join(",", state.Blockers));
        var phone = new CreationSkillsPhoneDraft();
        phone.Bind(state, overview);
        var forbidden = state.Authority.ActiveSkills.First(item => item.Category == (technomancer ? "Magical Active" : "Resonance Active"));
        var forbiddenGroup = state.Authority.SkillGroups.Single(item => item.Name == (technomancer ? "Sorcery" : "Tasking"));
        Require(!CreationSkillsPhoneAuthority.AvailableActiveSkills(state).Any(item => item.SourceSkillId == forbidden.SourceSkillId)
            && !CreationSkillsPhoneAuthority.AvailableGroups(state).Any(item => item.GroupId == forbiddenGroup.GroupId),
            "The native picker exposed a skill/group outside Core's source permissions.");
        Require(phone.WithSkill(forbidden, 1).SequenceEqual(phone.Skills)
            && phone.WithGroup(forbiddenGroup, 1).SequenceEqual(phone.Groups),
            "The native draft accepted a hidden, unauthorized catalog entry.");
        if (aspect is not null)
        {
            foreach (var group in state.Authority.SkillGroups.Where(item => item.Name is "Sorcery" or "Conjuring" or "Enchanting"))
                Require(CreationSkillsPhoneAuthority.AvailableGroups(state).Any(item => item.GroupId == group.GroupId) == (group.Name == aspect),
                    "The Skills picker did not preserve the selected Aspected Priority D group.");
        }
        var language = state.Authority.KnowledgeSkills.First(item => item.CanBeNativeLanguage);
        var allowed = state.Authority.ActiveSkills.Single(item => item.Name == (technomancer ? "Compiling" : "Arcana"));
        CharacterCreationSkillAllocation[] illegal =
            [new(language.SourceSkillId, CharacterCreationSkillKinds.Knowledge, null, null, true),
                new(forbidden.SourceSkillId, CharacterCreationSkillKinds.Active, 1, null, false)];
        var rejected = service.Preview(new(state.Binding, illegal, phone.Groups));
        Require(!phone.TryAdopt(state, overview, rejected, illegal, phone.Groups),
            "The phone adopted a rejected Core skill purchase.");
        var access = state.Authority.TalentAccess!;
        var forgedAccess = access with { AllowedActiveSkillSourceIds = access.AllowedActiveSkillSourceIds.Append(forbidden.SourceSkillId)
            .OrderBy(item => item, StringComparer.Ordinal).ToArray(), AccessDigest = string.Empty };
        forgedAccess = forgedAccess with { AccessDigest = CharacterCreationSkillsDigest.Compute(forgedAccess) };
        var forgedAuthority = state.Authority with { TalentAccess = forgedAccess, AuthorityDigest = string.Empty };
        forgedAuthority = forgedAuthority with { AuthorityDigest = CharacterCreationSkillsDigest.Compute(forgedAuthority) };
        var forgedState = state with { Authority = forgedAuthority,
            Binding = state.Binding with { SkillsAuthorityDigest = forgedAuthority.AuthorityDigest }, SnapshotDigest = string.Empty };
        forgedState = forgedState with { SnapshotDigest = CharacterCreationSkillsDigest.Compute(forgedState) };
        Require(!CreationSkillsPhoneAuthority.IsReady(forgedState, overview),
            "The phone accepted a rehashed skill-permission list inconsistent with source effects.");
        void Adopt(IReadOnlyList<CharacterCreationSkillAllocation> skills, IReadOnlyList<CharacterCreationSkillGroupAllocation> groups)
        {
            var preview = service.Preview(new(state.Binding, skills, groups));
            Require(phone.TryAdopt(state, overview, preview, skills, groups), "Native rejected legal Core Skills preview: " + string.Join(",", preview.Blockers));
        }
        Adopt(phone.WithSkill(language, 0, native: true), phone.Groups);
        Adopt(phone.WithSkill(allowed, 1), phone.Groups);
        if (aspect is not null)
            Adopt(phone.Skills, phone.WithGroup(state.Authority.SkillGroups.Single(item => item.Name == aspect), 1));
        var review = phone.Preview!;
        var command = new CharacterCreationSkillsConfirmRequest(review.Binding, phone.Skills, phone.Groups,
            review.PreviewDigest, "native-talent-skill-access", ExplicitlyConfirmed: true);
        var saved = service.Confirm(command);
        Require(saved.Outcome == CharacterCreationFoundationOutcomes.Success, string.Join(",", saved.Blockers));
        var coldStore = new FileWorkspaceStore(directory);
        var coldService = new CharacterCreationSkillsService(coldStore, resolver);
        var cold = coldService.Load(new(id)).Value!;
        var coldOverview = Program.NewCreationOverview(id, cold.Binding.ContentRevision, cold.Binding.SavedRevision);
        var coldPhone = new CreationSkillsPhoneDraft();
        coldPhone.Bind(cold, coldOverview);
        Require(coldPhone.Matches(cold, coldOverview) && coldPhone.Skills.SequenceEqual(phone.Skills)
            && coldPhone.Groups.SequenceEqual(phone.Groups), "Cold phone lost legal skill purchases or source access.");
        long revision = coldStore.Get(id).Value!.ContentRevision;
        Require(coldService.Confirm(command).Value!.ReceiptDigest == saved.Value!.ReceiptDigest
            && coldStore.Get(id).Value!.ContentRevision == revision, "Skill replay wrote a second mutation.");
        Require(!CreationSkillsPhoneAuthority.IsReady(cold, overview), "Phone accepted a previous workspace revision.");
        Console.WriteLine("PASS actual source skill access → native picker/review → confirmed save → cold reopen/replay");
        return command;
    }

    private static void RunSkillsRevisit(FileSystemCharacterSourceDataResolver resolver, CharacterWorkspaceId id,
        string directory, CharacterCreationSkillsConfirmRequest firstCommand, bool technomancer)
    {
        var store = new FileWorkspaceStore(directory);
        var before = store.Get(id).Value!;
        var firstReceipt = before.Document.AuxiliaryState.CharacterCreationSkillsReceipts!.Single();
        var magicDraft = before.Document.AuxiliaryState.CharacterCreationMagicResonanceDraft!;
        var magicReceipts = before.Document.AuxiliaryState.CharacterCreationMagicResonanceReceipts!;
        Require(before.ContentRevision > firstReceipt.ContentRevision && magicReceipts.Count > 0,
            "The actual Magic wizard must have saved between Skills visits.");
        var service = new CharacterCreationSkillsService(store, resolver);
        var state = service.Load(new(id)).Value!;
        var overview = Program.NewCreationOverview(id, state.Binding.ContentRevision, state.Binding.SavedRevision);
        var phone = new CreationSkillsPhoneDraft();
        phone.Bind(state, overview);
        Require(phone.Matches(state, overview), "Skills became unavailable after Magic: " + string.Join(",", state.Blockers));
        Require(!CreationSkillsPhoneAuthority.IsReady(state,
            Program.NewCreationOverview(id, firstCommand.Binding.ContentRevision, firstCommand.Binding.SavedRevision)),
            "Returning to Skills accepted the old host revision.");
        var source = CreationSkillsPhoneAuthority.AvailableActiveSkills(state)
            .Single(item => item.Name == (technomancer ? "Compiling" : "Arcana"));
        var choices = phone.WithSkill(source, 1);
        var preview = service.Preview(new(state.Binding, choices, phone.Groups));
        Require(phone.TryAdopt(state, overview, preview, choices, phone.Groups),
            "The revisited phone rejected a legal Skills increase: " + string.Join(",", preview.Blockers));
        var review = phone.Preview!;
        var command = new CharacterCreationSkillsConfirmRequest(review.Binding, phone.Skills, phone.Groups,
            review.PreviewDigest, "native-skills-after-magic", ExplicitlyConfirmed: true);
        Require(service.Confirm(command with { ExplicitlyConfirmed = false }).Outcome != CharacterCreationFoundationOutcomes.Success,
            "Revisiting Skills bypassed explicit review.");
        Require(store.Get(id).Value!.Document.AuxiliaryStateDigest == before.Document.AuxiliaryStateDigest,
            "Preview or rejected confirmation changed the saved wizard data.");
        var result = service.Confirm(command);
        Require(result.Outcome == CharacterCreationFoundationOutcomes.Success,
            "Skills confirmation rejected a legitimate inter-wizard revision gap: " + string.Join(",", result.Blockers));
        var receipt = result.Value!;
        Require(receipt.PreviousContentRevision == before.ContentRevision
            && receipt.ContentRevision == before.ContentRevision + 1
            && receipt.DraftRevision == firstReceipt.DraftRevision + 1
            && receipt.PreviousReceiptDigest == firstReceipt.ReceiptDigest,
            "The new Skills save did not extend its own receipt chain at the current workspace revision.");

        var coldStore = new FileWorkspaceStore(directory);
        var coldService = new CharacterCreationSkillsService(coldStore, resolver);
        var cold = coldService.Load(new(id)).Value!;
        var coldOverview = Program.NewCreationOverview(id, cold.Binding.ContentRevision, cold.Binding.SavedRevision);
        var coldPhone = new CreationSkillsPhoneDraft();
        coldPhone.Bind(cold, coldOverview);
        Require(coldPhone.Matches(cold, coldOverview) && coldPhone.Skills.SequenceEqual(phone.Skills)
            && coldPhone.Groups.SequenceEqual(phone.Groups), "Cold phone lost revisited Skills choices.");
        Require(coldService.Confirm(firstCommand).Value!.ReceiptDigest == firstReceipt.ReceiptDigest
            && coldService.Confirm(command).Value!.ReceiptDigest == receipt.ReceiptDigest,
            "Exact replay did not preserve the old and new Skills receipts.");
        Require(coldService.Confirm(firstCommand with { Allocations = command.Allocations }).Outcome
            == CharacterCreationFoundationOutcomes.Conflict, "An old command key accepted changed choices.");
        Require(coldService.Confirm(firstCommand with { IdempotencyKey = "native-stale-revisit" }).Outcome
            != CharacterCreationFoundationOutcomes.Success, "A new command accepted the stale pre-Magic binding.");
        var after = coldStore.Get(id).Value!;
        Require(after.ContentRevision == receipt.ContentRevision && after.SavedRevision == receipt.SavedRevision
            && after.Document.Content == before.Document.Content
            && after.Document.AuxiliaryState.CharacterCreationSkillsReceipts is { Count: 2 }
            && after.Document.AuxiliaryState.CharacterCreationMagicResonanceDraft!.DraftDigest == magicDraft.DraftDigest
            && after.Document.AuxiliaryState.CharacterCreationMagicResonanceReceipts!.Select(item => item.ReceiptDigest)
                .SequenceEqual(magicReceipts.Select(item => item.ReceiptDigest)),
            "Skills revisit or replay rewrote Magic, applied character effects or saved another revision.");
        var magic = new CharacterCreationMagicResonanceService(coldStore, resolver).Load(new(id)).Value!;
        Require(CharacterCreationMagicResonanceWorkflow.TryProject(magic, out var editor) && editor!.CanEdit,
            "Returning to Skills invalidated the saved Magic projection.");
        var magicPhone = new CreationMagicResonancePhoneDraft();
        var magicOverview = coldOverview with { CreationMagicResonance = magic, CreationMagicResonanceEditor = editor };
        magicPhone.Bind(editor!, magicOverview);
        Require(magicPhone.Matches(editor!, magicOverview)
            && CharacterCreationMagicResonanceDigest.Compute(magicPhone.Selections)
                == CharacterCreationMagicResonanceDigest.Compute(editor!.Selections),
            "The cold Magic phone did not retain its saved choices after the Skills revisit.");
        Require(after.Document.AuxiliaryStateDigest == coldStore.Get(id).Value!.Document.AuxiliaryStateDigest,
            "Cold projection or phone binding persisted data.");
        Console.WriteLine("PASS actual Skills → Magic → Skills phone revisit → cold reopen → both receipts replay without another write");
    }

    private static void RunAspected(FileWorkspaceStore store, FileSystemCharacterSourceDataResolver resolver,
        CharacterCreationMagicResonanceService service, CharacterCreationMagicResonanceState state,
        CharacterWorkspaceId id, string directory, string aspect)
    {
        Require(CharacterCreationMagicResonanceWorkflow.TryProject(state, out var projection),
            "Aspected Core state rejected: " + ProjectionDiagnostics(state));
        var editor = projection!;
        var overview = Program.NewCreationOverview(id, state.Binding.ContentRevision, state.Binding.SavedRevision) with
            { CreationMagicResonance = state, CreationMagicResonanceEditor = editor };
        var phone = new CreationMagicResonancePhoneDraft();
        phone.Bind(editor, overview);
        var tradition = editor.Traditions.First(item => item.IsEnabled);
        var review = CharacterCreationMagicResonanceWorkflow.Review(service, editor, phone.CreateSingleCandidate(tradition));
        Require(review.Preview.CanConfirm && phone.TryAdopt(editor, overview, review),
            "Phone lost the Aspected tradition or required an unrelated selection: " + string.Join(",", review.Preview.Blockers));
        string beforeXml = store.Get(id).Value!.Document.Content;
        string key = CreationMagicResonancePhoneAuthority.ComputeIdempotencyKey(review);
        var confirmed = CharacterCreationMagicResonanceWorkflow.Confirm(service, review, key, explicitlyConfirmed: true);
        var coldStore = new FileWorkspaceStore(directory);
        var coldService = new CharacterCreationMagicResonanceService(coldStore, resolver);
        var cold = coldService.Load(new(id)).Value!;
        var reopened = CharacterCreationMagicResonanceWorkflow.Project(cold);
        Require(reopened.CanEdit && reopened.Selections.Tradition == tradition.Identity,
            "Cold native projection lost the user's tradition.");
        var coldPrerequisites = new CharacterCreationPrerequisiteService(coldStore,
            new XmlCharacterFileQueries(new CharacterFileService()), resolver).Load(new(id)).Value!;
        var coldOverview = PriorityOverview(coldPrerequisites);
        var coldPhone = new CreationPrerequisitePhoneDraft();
        coldPhone.Bind(coldPrerequisites, coldOverview);
        var chosen = coldPrerequisites.PendingDraft!.TalentSelection!.GrantPlan!.SkillGroups.Single();
        // After Attributes is confirmed, Core intentionally locks Priority edits.
        // Keep the saved choice and the lock; never forge an editable snapshot.
        Require(chosen.CanonicalName == aspect && chosen.BaseRating == 0
            && coldPrerequisites.Blockers.Contains(CharacterCreationPrerequisiteBlockers.DependentAttributesDraftExists)
            && !coldPhone.CanPrepare(coldPrerequisites, coldOverview),
            "Cold Priority lost the saved aspect or its dependent-draft lock: " + PriorityDiagnostics(coldPrerequisites, coldOverview, coldPhone));
        Require(coldStore.Get(id).Value!.Document.Content == beforeXml,
            "A wizard step applied character effects before finalization.");
        var replay = CharacterCreationMagicResonanceWorkflow.Confirm(coldService, review, key, explicitlyConfirmed: true);
        Require(replay.Receipt.ReceiptDigest == confirmed.Receipt.ReceiptDigest
            && coldStore.Get(id).Value!.ContentRevision == confirmed.Receipt.ContentRevision,
            "Retry applied the Aspected draft twice.");
        Console.WriteLine($"PASS actual Aspected Priority D/{aspect} → mandatory selection-only phone choice → tradition → cold reopen/replay");
    }

    // The test supplies host navigation context, not a second rule authority.
    // Every revision/source/selection below comes from the actual Core load.
    private static CharacterOverviewState PriorityOverview(CharacterCreationPrerequisiteState state)
        => Program.NewCreationOverview(state.Binding.WorkspaceId, state.Binding.ContentRevision, state.Binding.SavedRevision) with
        {
            CreationWizard = new CharacterCreationWizardSnapshot(
                CharacterCreationWizardSchemas.SnapshotV1, state.Binding.WorkspaceId.Value,
                state.Binding.ContentRevision, state.Binding.RawCharacterXmlDigest, state.Binding.AuthorityDigest,
                state.RulesetId, string.Empty, state.BuildMethod, state.CharacterCreated,
                CharacterCreationWizardStepIds.Foundation, [], [],
                new Dictionary<string, IReadOnlyList<CharacterCreationLegalOption>>(), [], [], false, state.SnapshotDigest)
        };

    private static string PriorityDiagnostics(CharacterCreationPrerequisiteState state, CharacterOverviewState overview,
        CreationPrerequisitePhoneDraft phone)
    {
        var pending = state.PendingDraft!;
        var talentRank = state.Authority.Options.Single(item => item.CategoryId == CharacterCreationPriorityCategoryIds.Talent
            && item.Rank == pending.Assignments.Single(assignment => assignment.CategoryId == CharacterCreationPriorityCategoryIds.Talent).Rank);
        var talent = talentRank.TalentOptions.Single(item => item.SelectionId == pending.TalentSelection!.SelectionId);
        var heritageRank = state.Authority.Options.Single(item => item.CategoryId == CharacterCreationPriorityCategoryIds.Heritage
            && item.Rank == pending.Assignments.Single(assignment => assignment.CategoryId == CharacterCreationPriorityCategoryIds.Heritage).Rank);
        var heritage = heritageRank.HeritageOptions.Single(item => item.SelectionId == pending.HeritageSelection!.SelectionId);
        return $"ready={CreationPrerequisitePhoneAuthority.IsReady(state, overview)}; "
            + $"assignments={phone.Assignments(state, overview).Count}; "
            + $"talent={CreationPrerequisitePhoneAuthority.TalentSelectionMatchesOption(pending.TalentSelection!, talent, talentRank.SourceId)}; "
            + $"heritage={CreationPrerequisitePhoneAuthority.HeritageSelectionMatchesOption(pending.HeritageSelection!, heritage, heritageRank.SourceId)}; "
            + $"karma={pending.CreationKarmaUsed}/{state.CreationKarmaBudget.Used}; "
            + $"attributes={pending.EffectiveNormalAttributePoints}/{state.EffectiveNormalAttributePoints}; "
            + $"special={pending.TotalSpecialAttributePoints}/{state.TotalSpecialAttributePoints}; "
            + $"draftAuthority={pending.AuthorityDigest == state.Binding.AuthorityDigest}; blockers={string.Join(',', state.Blockers)}";
    }

    private static void RunTechnomancer(FileWorkspaceStore store, FileSystemCharacterSourceDataResolver resolver,
        CharacterCreationMagicResonanceService service, CharacterCreationMagicResonanceState state,
        CharacterWorkspaceId id, string directory)
    {
        Require(CharacterCreationMagicResonanceWorkflow.TryProject(state, out var projection),
            "Technomancer Core state rejected: " + ProjectionDiagnostics(state));
        var editor = projection!;
        var overview = Program.NewCreationOverview(id, state.Binding.ContentRevision, state.Binding.SavedRevision) with
            { CreationMagicResonance = state, CreationMagicResonanceEditor = editor };
        var phone = new CreationMagicResonancePhoneDraft();
        phone.Bind(editor, overview);
        var stream = editor.Streams.Single(item => item.Name == "Default");
        var review = CharacterCreationMagicResonanceWorkflow.Review(service, editor, phone.CreateSingleCandidate(stream));
        Require(phone.TryAdopt(editor, overview, review), "Phone lost the user-selected stream.");
        var forms = editor.ComplexForms.Where(item => item.IsEnabled).Take(state.SelectedTalent!.ComplexFormBudget).ToArray();
        foreach (var form in forms)
        {
            review = CharacterCreationMagicResonanceWorkflow.Review(service, editor, phone.CreateToggleCandidate(form));
            Require(phone.TryAdopt(editor, overview, review), "Phone lost the selected Complex Form.");
        }
        Require(review.Preview.CanConfirm, string.Join(",", review.Preview.Blockers));
        string beforeXml = store.Get(id).Value!.Document.Content;
        string key = CreationMagicResonancePhoneAuthority.ComputeIdempotencyKey(review);
        var confirmed = CharacterCreationMagicResonanceWorkflow.Confirm(service, review, key, explicitlyConfirmed: true);
        var coldStore = new FileWorkspaceStore(directory);
        var coldService = new CharacterCreationMagicResonanceService(coldStore, resolver);
        var cold = coldService.Load(new(id)).Value!;
        var reopened = CharacterCreationMagicResonanceWorkflow.Project(cold);
        Require(reopened.CanEdit && reopened.Selections.Stream == stream.Identity
            && reopened.Selections.ComplexForms.SequenceEqual(forms.Select(item => item.Identity)),
            "Cold phone projection lost the user's stream/forms.");
        var contribution = coldStore.Get(id).Value!.Document.AuxiliaryState.CharacterCreationMagicResonanceDraft!.FinalizationContribution!;
        var quality = contribution.Talent.GrantedQualitySources!.Single();
        Require(quality.GrantedGearSources!.Single().Name == "Living Persona"
            && contribution.EffectiveAttributes!.Resonance == state.SelectedTalent.Resonance + 1,
            "Source-defined gear or confirmed RES was lost in the actual phone confirmation.");
        Require(CharacterCreationMagicResonanceDigest.Compute(cold.SelectedTalent!)
                == CharacterCreationMagicResonanceDigest.Compute(state.SelectedTalent)
            && coldStore.Get(id).Value!.Document.Content == beforeXml,
            "Phone step changed source grants or applied effects before finalization.");
        var replay = CharacterCreationMagicResonanceWorkflow.Confirm(coldService, review, key, explicitlyConfirmed: true);
        Require(replay.Receipt.ReceiptDigest == confirmed.Receipt.ReceiptDigest
            && coldStore.Get(id).Value!.ContentRevision == confirmed.Receipt.ContentRevision,
            "Retry applied the Technomancer choices twice.");
        // Display labels are not command authority; only the source identity is
        // carried into the draft. Replacing a label must not rename a stream.
        Require(CharacterCreationMagicResonanceDigest.Compute(phone.CreateSingleCandidate(stream))
                == CharacterCreationMagicResonanceDigest.Compute(phone.CreateSingleCandidate(stream with { Name = "invented stream" })),
            "Display-only input changed the typed source selection.");
        ExpectRejected(() => phone.CreateSingleCandidate(stream with
            { Identity = stream.Identity with { SourceId = Guid.NewGuid().ToString("D") } }));
        Console.WriteLine("PASS actual Technomancer source/raised RES → native stream/forms choices → cold file-store reopen/replay");
    }

    private static void RunMysticAdept(FileWorkspaceStore store, FileSystemCharacterSourceDataResolver resolver,
        CharacterCreationMagicResonanceService service, CharacterCreationMagicResonanceState state,
        CharacterWorkspaceId id, string directory)
    {
        foreach (var (language, title) in new[]
        {
            ("en-GB", "Mystic Adept power points"), ("de-AT", "Kraftpunkte für Mystische Adepten"),
            ("es-MX", "Puntos de poder del adepto místico")
        })
        {
            var culture = System.Globalization.CultureInfo.GetCultureInfo(language);
            Require(CreationFlowStrings.Get("Magic.Mystic.Title", "missing", culture) == title,
                "Real Creation resources did not resolve the phone region's language: " + language);
            foreach (string resourceKey in new[] { "Summary", "Boundary", "Decrease", "Increase", "SeparateAttributeUnsupported" })
                Require(CreationFlowStrings.Get("Magic.Mystic." + resourceKey, "missing", culture) != "missing", resourceKey);
            Require(CreationFlowStrings.Format(culture, "Magic.Mystic.Summary", "missing", 2, 4, 10, 0, 5).Contains("10", StringComparison.Ordinal),
                "The translated Core quote did not render.");
        }
        Require(!CreationMagicResonancePage.HasUnsupportedSeparateMagicProfile(state),
            "An ordinary Mystic Adept purchase must not show the separate-attribute notice.");
        var unsupportedProfile = state with
        {
            Authority = state.Authority with
            {
                MysticAdeptPowerPointPolicy = state.Authority.MysticAdeptPowerPointPolicy! with { UsesSeparateMagicAttribute = true }
            },
            SelectedTalent = state.SelectedTalent! with { IsEnabled = false },
            Blockers = [CharacterCreationMagicResonanceBlockers.PowerBudgetUnsupported]
        };
        // Display-only predicate: the forged fixture is never offered to Core or persisted.
        Require(CreationMagicResonancePage.HasUnsupportedSeparateMagicProfile(unsupportedProfile),
            "A blocked separate-attribute profile needs an explanatory notice.");
        Require(!CreationMagicResonancePage.HasUnsupportedSeparateMagicProfile(unsupportedProfile with { Blockers = [] })
            && !CreationMagicResonancePage.HasUnsupportedSeparateMagicProfile(unsupportedProfile with
            { SelectedTalent = state.SelectedTalent! with { Kind = CharacterCreationMagicResonanceKinds.Adept, IsEnabled = false } }),
            "Unrelated blocked talents must not be described as unsupported separate Magic.");
        Require(CharacterCreationMagicResonanceWorkflow.TryProject(state, out var projected),
            "Mystic Adept Core state rejected: " + ProjectionDiagnostics(state));
        foreach (var anchors in new[]
        {
            state.SelectedTalent!.SourceAnchorIds.Append("settings.xml#invented-free-power-points").ToArray(),
            state.SelectedTalent!.SourceAnchorIds.Except(state.Authority.MysticAdeptPowerPointPolicy!.SourceAnchorIds,
                StringComparer.Ordinal).ToArray()
        })
        {
            var hostileTalent = state.SelectedTalent! with
            {
                SourceAnchorIds = anchors.Distinct(StringComparer.Ordinal).OrderBy(anchor => anchor, StringComparer.Ordinal).ToArray()
            };
            var hostileAuthority = state.Authority with
            {
                Talents = state.Authority.Talents.Select(item => item.Identity == hostileTalent.Identity ? hostileTalent : item).ToArray(),
                AuthorityDigest = string.Empty
            };
            hostileAuthority = hostileAuthority with { AuthorityDigest = CharacterCreationMagicResonanceDigest.Compute(hostileAuthority) };
            var hostile = state with
            {
                SelectedTalent = hostileTalent,
                Authority = hostileAuthority,
                Binding = state.Binding with { AuthorityDigest = hostileAuthority.AuthorityDigest },
                SnapshotDigest = string.Empty
            };
            hostile = hostile with { SnapshotDigest = CharacterCreationMagicResonanceDigest.Compute(hostile) };
            Require(CharacterCreationMagicResonanceDraftIntegrity.IsValidAuthority(hostileAuthority),
                "The hostile fixture must reach the Priority-versus-Magic source join, not fail only an outer digest.");
            Require(!CharacterCreationMagicResonanceWorkflow.TryProject(hostile, out _),
                "Missing or invented Mystic policy anchors survived the exact source join.");
        }
        var editor = projected!;
        Require(editor.MysticAdeptPowerPoints is { PowerPoints: 0, KarmaCost: 0 }
            && editor.MysticAdeptPowerPoints.MaximumPowerPoints == state.SelectedTalent!.Magic + 1,
            "Mystic Adept PP became free MAG or failed to use confirmed MAG as the purchase cap.");
        var overview = Program.NewCreationOverview(id, state.Binding.ContentRevision, state.Binding.SavedRevision) with
            { CreationMagicResonance = state, CreationMagicResonanceEditor = editor };
        var phone = new CreationMagicResonancePhoneDraft();
        phone.Bind(editor, overview);
        ExpectRejected(() => phone.CreateMysticPowerPointCandidate(-1));
        ExpectRejected(() => phone.CreateMysticPowerPointCandidate(editor.MysticAdeptPowerPoints!.MaximumPowerPoints + 1));
        var review = CharacterCreationMagicResonanceWorkflow.Review(service, editor, phone.CreateMysticPowerPointCandidate(2));
        Require(phone.TryAdopt(editor, overview, review), "Phone did not adopt the explicit PP purchase.");
        Require(review.Preview.MysticAdeptPowerPoints is { PowerPoints: 2, KarmaCost: 10 }
            && review.Preview.AdeptPowerPointBudget.Total == 2 && !review.Preview.CanConfirm,
            "Incomplete preview lost the profile-backed purchase quote or allowed missing spells/powers.");
        review = CharacterCreationMagicResonanceWorkflow.Review(service, editor,
            phone.CreateSingleCandidate(editor.Traditions.Single(item => item.Name == "Hermetic")));
        Require(phone.TryAdopt(editor, overview, review), "Tradition selection dropped the PP purchase.");
        decimal remaining = 2;
        foreach (var power in editor.AdeptPowers.Where(item => item.IsEnabled && item.PointCost > 0)
            .OrderByDescending(item => item.PointCost))
        {
            int levels = (int)Math.Min(power.MaximumLevels, decimal.Floor(remaining / power.PointCost));
            if (levels == 0) continue;
            review = CharacterCreationMagicResonanceWorkflow.Review(service, editor, phone.CreatePowerLevelCandidate(power, levels));
            Require(phone.TryAdopt(editor, overview, review), "Power selection dropped the PP purchase.");
            remaining -= levels * power.PointCost;
        }
        Require(remaining == 0, "Source power selection did not fill the reviewed PP budget.");
        foreach (var spell in editor.Spells.Where(item => item.IsEnabled).Take(state.SelectedTalent!.SpellBudget))
        {
            review = CharacterCreationMagicResonanceWorkflow.Review(service, editor, phone.CreateToggleCandidate(spell));
            Require(phone.TryAdopt(editor, overview, review), "Spell selection dropped the PP purchase.");
        }
        Require(review.Preview.CanConfirm && review.Draft.Selections.MysticAdeptPowerPoints == 2,
            string.Join(",", review.Preview.Blockers));
        var forgedPreview = review.Preview with
        {
            MysticAdeptPowerPoints = review.Preview.MysticAdeptPowerPoints! with { KarmaCost = 0 },
            PreviewDigest = string.Empty
        };
        forgedPreview = forgedPreview with { PreviewDigest = CharacterCreationMagicResonanceDigest.Compute(forgedPreview) };
        Require(!phone.TryAdopt(editor, overview, review with { Preview = forgedPreview }),
            "Native draft accepted a rehashed free-PP price despite the current source-owned quote.");
        var forgedState = state with { MysticAdeptPowerPoints = state.MysticAdeptPowerPoints! with { KarmaCost = 10 }, SnapshotDigest = string.Empty };
        forgedState = forgedState with { SnapshotDigest = CharacterCreationMagicResonanceDigest.Compute(forgedState) };
        Require(!CharacterCreationMagicResonanceWorkflow.TryProject(forgedState, out _), "Rehashed invented quote survived Presentation validation.");
        string key = CreationMagicResonancePhoneAuthority.ComputeIdempotencyKey(review);
        string beforeXml = store.Get(id).Value!.Document.Content;
        var confirmed = CharacterCreationMagicResonanceWorkflow.Confirm(service, review, key, explicitlyConfirmed: true);
        var coldStore = new FileWorkspaceStore(directory);
        var coldService = new CharacterCreationMagicResonanceService(coldStore, resolver);
        var cold = coldService.Load(new(id)).Value!;
        var reopened = CharacterCreationMagicResonanceWorkflow.Project(cold);
        Require(reopened.Selections.MysticAdeptPowerPoints == 2
            && reopened.MysticAdeptPowerPoints is { PowerPoints: 2, KarmaCost: 10 }
            && reopened.Budgets.Single(item => item.Kind == CharacterCreationMagicResonanceKinds.AdeptPower).Total == 2,
            "Cold phone draft lost the purchased PP or its Karma cost.");
        var replay = CharacterCreationMagicResonanceWorkflow.Confirm(coldService, review, key, explicitlyConfirmed: true);
        Require(replay.Receipt.ReceiptDigest == confirmed.Receipt.ReceiptDigest
            && coldStore.Get(id).Value!.ContentRevision == confirmed.Receipt.ContentRevision
            && coldStore.Get(id).Value!.Document.Content == beforeXml,
            "Phone purchase mutated character XML before finalization or replayed a write.");
        Console.WriteLine("PASS actual Mystic Adept profile/raised MAG → native PP purchase/powers/spells → cold file-store reopen/replay");
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
            + $"priority={Probe("PrerequisiteSelectsExactTalent", state.PrerequisiteDraft!, state.SelectedTalent!, state.Authority.MysticAdeptPowerPointPolicy!)}; "
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
