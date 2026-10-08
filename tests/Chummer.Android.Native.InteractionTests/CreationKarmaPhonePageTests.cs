using System.Globalization;
using Chummer.Android.Native;
using Chummer.Contracts.Characters;
using Chummer.Infrastructure.Workspaces;
using Microsoft.Maui.Controls;

internal static partial class AfterRunAuthorityHarness
{
    private sealed class ObservedQualityOptions(IReadOnlyList<CharacterCreationQualityCatalogOption> options,
        Action enumerate) : IReadOnlyList<CharacterCreationQualityCatalogOption>
    {
        public int Count => options.Count;
        public CharacterCreationQualityCatalogOption this[int index] => options[index];
        public IEnumerator<CharacterCreationQualityCatalogOption> GetEnumerator()
        {
            enumerate();
            return options.GetEnumerator();
        }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private static async Task RunKarmaPhonePagesAsync(string contentRoot, bool prerequisitesOnly = false, bool magic = false,
        string? attributeMetatype = null)
    {
        using var ui = new IssuedPageUiContext();
        await ui.RunAsync(async () =>
        {
            var owners = new ControlledLinkedOwner();
            KarmaNativeProbe? probe = null;
            await using var runtime = new NativeRewardRuntime(contentRoot, creationBootstrap: true,
                productionCreationOverview: true, linkedOwners: owners,
                karmaDecorator: actual => probe = new(actual, ui));
            await runtime.Coordinator.InitializeAsync();
            await AccountStartupTask(runtime.Coordinator).WaitAsync(TimeSpan.FromSeconds(10));
            await runtime.Coordinator.CreateRunnerAsync();
            await runtime.Presenter.UpdateDialogFieldAsync("newCharacterName", "Karma phone pages", default);
            await runtime.Presenter.UpdateDialogFieldAsync("newCharacterBuildMethod", "Karma", default);
            await runtime.Coordinator.ExecuteDialogActionAsync("create_character");
            Require(runtime.Coordinator.CanOpenCreationKarma(), "Native Karma entry is unavailable after actual bootstrap.");
            var id = runtime.Coordinator.State.WorkspaceId!.Value;
            var before = new FileWorkspaceStore(runtime.StateDirectory).Get(id).Value!;
            var root = new CreationKarmaPage(runtime.Coordinator);
            var navigation = new NavigationPage(new ContentPage());
            await navigation.PushAsync(root, false);
            var window = new Window(navigation);
            using var alerts = new IssuedPageAlerts(root, window);
            await alerts.PreflightAsync();
            await Appear();
            Require(root.Title == CreationKarmaCopy.Title
                && !IssuedElements(root).OfType<Label>().Any(label => label.Text == root.Title),
                "The navigation title must not be repeated above the Karma choices.");
            Require(Element<Label>("creation-karma-budget").Text == CreationKarmaCopy.ChooseStepFirst(CreationKarmaCopy.Metatype),
                "An empty Karma draft must explain the first choice, not display an ambiguous picker placeholder.");
            AssertPrerequisite("contacts", CreationKarmaCopy.ChooseStepFirst(CreationKarmaCopy.Metatype));
            AssertPrerequisite("lifestyles", CreationKarmaCopy.ChooseStepFirst(CreationKarmaCopy.Metatype));
            var disabledContacts = Element<Button>("karma-open-contacts");
            ((IButtonController)disabledContacts).SendClicked();
            Require(ReferenceEquals(root, Current()), "A disabled Contacts callback navigated before its prerequisites.");
            await Click("karma-open-metatype");
            Require(Element<Label>("karma-metatype-bonuses-08f2c9cc-f9f8-4f1a-9efb-63555af71788").Text
                    == CreationKarmaCopy.MetatypeBonuses(0, 0, 20)
                && Element<Label>("karma-metatype-bonuses-77fa1ed8-f4e6-4763-9f0b-f125318b9782").Text
                    == CreationKarmaCopy.MetatypeBonuses(1, 1, 100),
                "Metatype choice must disclose Core-owned Dwarf/Troll bonuses and lifestyle surcharges before selection.");
            Button human = IssuedElements(Current()).OfType<Button>().Single(b => b.Text == (attributeMetatype ?? "Human"));
            await Click(human.AutomationId);
            AssertPrerequisite("contacts", CreationKarmaCopy.ChooseStepFirst(CreationKarmaCopy.Talent));
            await Click("karma-open-talent");
            var unavailableTalents = IssuedElements(Current()).OfType<Button>()
                .Where(button => button.AutomationId?.StartsWith("karma-talent-", StringComparison.Ordinal) == true && !button.IsEnabled).ToArray();
            Require(unavailableTalents.Length > 0, "The source fixture must exercise an unavailable talent.");
            foreach (var unavailable in unavailableTalents)
            {
                Require(!string.IsNullOrWhiteSpace(Element<Label>(unavailable.AutomationId + "-prerequisite").Text),
                    "A disabled talent must explain why it cannot be selected.");
                var talentPage = Current();
                ((IButtonController)unavailable).SendClicked();
                Require(ReferenceEquals(talentPage, Current()) && Session().Selection!.TalentOptionId is null,
                    "Explaining a disabled talent must not admit its callback.");
            }
            await Click(magic ? "karma-talent-0e741331-d776-4be8-abc5-4101228abdef" : "karma-talent-mundane");
            AssertPrerequisite("contacts", CreationKarmaCopy.ReviewStepFirst(CreationKarmaCopy.Attributes));
            await Click("karma-open-attributes");
            if (attributeMetatype is not null)
            {
                AssertAttributeValues();
                var oldTopPreview = Element<Button>("karma-preview-attributes-top");
                var oldBody = Element<Stepper>("karma-attribute-BOD");
                int originalBody = Session().Quote!.Attributes!.Attributes.Single(a => a.AttributeId == "BOD").Current;
                Require(originalBody == (attributeMetatype == "Troll" ? 5 : 1),
                    "SETUP: real metatype minima must differ from purchased levels.");
                oldBody.Value = 1;
                foreach (var attribute in Session().Quote!.Attributes!.Attributes)
                {
                    Require(Element<Label>("karma-attribute-" + attribute.AttributeId + "-rating").Text
                            == CreationAllocationStrings.Format("Karma.AttributePending", "{0}: awaiting preview",
                                CreationAllocationStrings.AttributeName(attribute.AttributeId))
                        && !Element<Label>("karma-attribute-" + attribute.AttributeId + "-cost").IsVisible,
                        "An edited draft must not present an older rating or cost as current.");
                }
                await Click("karma-preview-attributes-top");
                AssertAttributeValues();
                int previewsAfterTop = probe!.PreviewCalls;
                await ui.BeginAsyncVoid(() => ((IButtonController)oldTopPreview).SendClicked());
                Require(probe.PreviewCalls == previewsAfterTop && Session().QuoteCurrent,
                    "An obsolete top-preview callback revalidated a newer render.");
                Require(Session().Quote!.Attributes!.Attributes.Single(a => a.AttributeId == "BOD").Current == originalBody + 1,
                    "Core did not include the chosen metatype minimum in the final rating.");
                if (magic)
                {
                    Element<Stepper>("karma-attribute-MAG").Value = 2;
                    await Click("karma-preview-attributes");
                    AssertAttributeValues();
                    Require(Session().Quote!.Attributes!.Attributes.Single(a => a.AttributeId == "MAG").Current == 3,
                        "Magic must display Core's starting grant plus purchases, not the number of purchases.");
                }
                var departedTopPreview = Element<Button>("karma-preview-attributes-top");
                await Back();
                var selected = Session().Selection;
                oldBody.Value = 2;
                Require(ReferenceEquals(selected, Session().Selection), "A departed attribute control changed the draft.");
                int previewsAfterDeparture = probe.PreviewCalls;
                // The departed page rejects dispatch before an async action starts.
                ((IButtonController)departedTopPreview).SendClicked();
                Require(probe.PreviewCalls == previewsAfterDeparture && ReferenceEquals(selected, Session().Selection),
                    "A departed top-preview callback read or changed another page's draft.");
                await Click("karma-open-attributes");
                AssertAttributeValues();
                string[] sourceAnchors = Session().Quote!.SourceAnchorIds.ToArray();
                Require(sourceAnchors.Any(anchor => anchor.Contains("#", StringComparison.Ordinal)),
                    "SETUP: review must exercise technical source identities.");
                await Back();
                await Click("karma-open-review");
                Require(!IssuedElements(Current()).OfType<Label>()
                        .Any(label => sourceAnchors.Contains(label.Text, StringComparer.Ordinal)),
                    "Review must not expose raw source anchors or their GUIDs as reader-facing text.");
                Require(sourceAnchors.SequenceEqual(Session().Quote!.SourceAnchorIds, StringComparer.Ordinal),
                    "Hiding technical source text must preserve the quote's source authority.");
                if (magic)
                {
                    await Back();
                    await Click("karma-open-talent");
                    await Click("karma-talent-mundane");
                    await Click("karma-open-attributes");
                    Require(!Session().Quote!.Attributes!.Attributes.Single(a => a.AttributeId == "MAG").IsEnabled,
                        "SETUP: Mundane must disable further Magic purchases.");
                    var magicPurchases = Element<Stepper>("karma-attribute-MAG");
                    Require(magicPurchases.IsEnabled && magicPurchases.Value == 2,
                        "A now-invalid Magic purchase must remain visible and removable.");
                    magicPurchases.Value = 0;
                    await Click("karma-preview-attributes-top");
                    AssertAttributeValues();
                    Require(Session().Selection!.Attributes!.All(a => a.AttributeId != "MAG"),
                        "Removing an invalid Magic purchase did not clear the draft selection.");
                }
                RequireSameRewardDocument(before, new FileWorkspaceStore(runtime.StateDirectory).Get(id).Value!);
                Require(probe!.ConfirmCalls == 0, "Reading or previewing attribute values persisted a draft.");
                Console.WriteLine($"PASS Karma attribute values: {attributeMetatype}, magic={magic}; Core ratings/costs, pending edits, return, no write");
                return;

                void AssertAttributeValues()
                {
                    Require(Session().QuoteCurrent, "SETUP: attribute values need a current real Core quote.");
                    var controls = IssuedElements(Current()).ToArray();
                    Require(Array.FindIndex(controls, e => e.AutomationId == "karma-preview-attributes-top")
                            < Array.FindIndex(controls, e => e.AutomationId == "karma-attribute-BOD-rating")
                        && Element<Button>("karma-preview-attributes").IsEnabled,
                        "Attribute preview must be reachable above the first rating as well as below the last.");
                    foreach (var attribute in Session().Quote!.Attributes!.Attributes)
                    {
                        int purchased = Session().Selection!.Attributes!
                            .SingleOrDefault(a => a.AttributeId == attribute.AttributeId)?.KarmaLevels ?? 0;
                        bool editable = attribute.IsEnabled || purchased > 0;
                        Require(controls.OfType<Stepper>().Any(e => e.AutomationId == "karma-attribute-" + attribute.AttributeId)
                                == editable
                            && controls.OfType<Label>().Any(e => e.AutomationId == "karma-attribute-" + attribute.AttributeId + "-value")
                                == editable,
                            "Read-only zero-purchase attributes must omit purchase controls; existing purchases must remain removable.");
                        string name = CreationAllocationStrings.AttributeName(attribute.AttributeId);
                        var rating = Element<Label>("karma-attribute-" + attribute.AttributeId + "-rating");
                        var cost = Element<Label>("karma-attribute-" + attribute.AttributeId + "-cost");
                        Require(rating.Text == CreationKarmaCopy.Levels(name, attribute.Current)
                            && rating.FontAttributes.HasFlag(FontAttributes.Bold) && rating.FontSize >= 24,
                            "Actual Core attribute values must be large and bold, not replaced by purchase counts.");
                        Require(cost.IsVisible && cost.Text == CreationAllocationStrings.Format("Karma.AttributeRangeCost",
                            "Natural range {0}–{1} · {2} Karma", attribute.Minimum, attribute.Maximum, attribute.KarmaCost),
                            "Attribute range/cost must come from the same Core quote as the displayed rating.");
                    }
                }
            }
            Stepper oldAgility = Element<Stepper>("karma-attribute-AGI");
            Require(Element<Label>("karma-attribute-AGI-value").Text == CreationKarmaCopy.AttributePurchases("Agility", 0),
                "An attribute editor must distinguish purchased levels from the final rating.");
            oldAgility.Value = 1;
            Require(Element<Label>("karma-attribute-AGI-value").Text == CreationKarmaCopy.AttributePurchases("Agility", 1),
                "Changing an attribute must retain the purchased-level caption.");
            if (magic) Element<Stepper>("karma-attribute-MAG").Value = 2;
            Require(Element<Label>("creation-karma-budget").Text == CreationKarmaCopy.Pending,
                "Attribute editing displayed stale budget totals as current.");
            int loadsBeforeBack = probe!.LoadCalls;
            int previewsBeforeBack = probe.PreviewCalls;
            await Back();
            AssertPrerequisite("contacts", CreationKarmaCopy.ReviewStepFirst(CreationKarmaCopy.Skills));
            Require(probe.LoadCalls == loadsBeforeBack && probe.PreviewCalls == previewsBeforeBack + 1,
                "Returning to the overview must freshly preview, without a redundant preceding Load.");
            await Click("karma-open-attributes");
            Require(Element<Stepper>("karma-attribute-AGI").Value == 1,
                "Back navigation silently dropped the uncommitted attribute selection.");
            oldAgility.Value = 2;
            Require(Element<Stepper>("karma-attribute-AGI").Value == 1,
                "An obsolete native Stepper overwrote the returned editor.");
            await Back();
            await Click("karma-open-skills");
            Require(Session().Access is { IsReady: true },
                "The skill chooser must obtain current Core skill access.");
            await Click("karma-filter-knowledge");
            Require(Element<Button>("karma-filter-knowledge").Text == CreationKarmaCopy.KnowledgeSkills,
                "The catalog filter must not use the knowledge-point payment caption.");
            await Search("English");
            var english = IssuedElements(Current()).OfType<Button>().Single(b => b.Text == "English");
            await Click(english.AutomationId);
            Element<Switch>("karma-native-language").IsToggled = true;
            await Click("karma-use-skill");
            await Click("karma-filter-active");
            await Search("Pistols");
            var pistols = IssuedElements(Current()).OfType<Button>().Single(b => b.Text == "Pistols");
            await Click(pistols.AutomationId);
            Element<Stepper>("karma-skill-levels").Value = 1;
            await Click("karma-use-skill");
            Require(new FileWorkspaceStore(runtime.StateDirectory).Get(id).Value!.ContentRevision == before.ContentRevision
                && probe!.ConfirmCalls == 0, "Selecting phone options mutated the workspace without review.");
            var previousAccess = Session().Access;
            Require(previousAccess is { IsReady: true }, "Returning from a skill must refresh chooser access.");
            await Back();
            Require(Session().QuoteCurrent && Session().Access is null,
                "The overview must retain a fresh Core quote without evaluating unused skill-display access.");
            AssertPrerequisite("contacts", CreationKarmaCopy.ReviewOptionalStepFirst(CreationKarmaCopy.Qualities));
            AssertPrerequisite("lifestyles", CreationKarmaCopy.ReviewOptionalStepFirst(CreationKarmaCopy.Qualities));
            await Click("karma-open-skills");
            Require(Session().Access is { IsReady: true } && !ReferenceEquals(previousAccess, Session().Access),
                "Reopening Skills must obtain fresh access, not reuse a departed chooser's projection.");
            await Back();
            bool observeQualityEnumeration = false;
            int qualityEnumerations = 0;
            probe.TransformLoad = state => state.QualitiesCatalog is not { } catalog ? state
                : state with { QualitiesCatalog = catalog with
                {
                    Options = new ObservedQualityOptions(catalog.Options, () =>
                    {
                        if (!observeQualityEnumeration) return;
                        Require(!ReferenceEquals(SynchronizationContext.Current, ui),
                            "Quality catalog filtering/sorting/indexing ran on the Android UI context.");
                        Interlocked.Increment(ref qualityEnumerations);
                    })
                } };
            int previewsBeforeQualities = probe.PreviewCalls, batchesBeforeQualities = probe.QualityBatchCalls;
            int candidatesBeforeQualities = probe.QualityCandidateCount;
            await Click("karma-open-qualities");
            Require(Session().QuoteCurrent && probe.PreviewCalls == previewsBeforeQualities
                && probe.QualityBatchCalls == batchesBeforeQualities + 1
                && probe.QualityCandidateCount - candidatesBeforeQualities is >= 1 and <= 4,
                "Opening Qualities must quote the current draft and candidates in one fresh bounded Core batch.");
            Require(Session().Access is null,
                "Qualities must not recalculate skill-display access merely because its catalog is already loaded.");
            if (magic)
            {
                await Back();
                await Click("karma-open-magic");
                Require(Session().Quote!.Magic is { Access.RequiresTradition: true, CanSelect: false },
                    "The native magic page must expose Core's missing-tradition review.");
                Require(Element<Label>("karma-blocker-" + CharacterCreationMagicResonanceBlockers.TraditionRequired).Text
                        == CreationKarmaCopy.TraditionRequired
                    && CreationKarmaCopy.Blocker(CharacterCreationMagicResonanceBlockers.StreamRequired) == CreationKarmaCopy.StreamRequired
                    && CreationKarmaCopy.Blocker("unknown-core-reason") == "unknown-core-reason",
                    "Required magic choices must explain the next action without hiding unknown Core blockers.");
                await Click("karma-magic-open-tradition");
                ReadableSearch("karma-magic-search").Text = "  Hermetic  ";
                await Click("karma-magic-search-go");
                var tradition = Session().Authority!.MagicCatalog!.Catalogs.Single(slice => slice.Kind == "tradition")
                    .Options.Single(option => option.Name == "Hermetic");
                var oldChoose = Element<Button>("karma-magic-add-" + tradition.Identity.SourceId);
                await Click(oldChoose.AutomationId);
                await Back();
                await Click("karma-magic-open-spell");
                var spell = Session().Authority!.MagicCatalog!.Catalogs.Single(slice => slice.Kind == "spell")
                    .Options.First(option => option.IsEnabled);
                ReadableSearch("karma-magic-search").Text = "  " + spell.Name + "  ";
                await Click("karma-magic-search-go");
                await Click("karma-magic-add-" + spell.Identity.SourceId);
                Require(Session().Quote!.Magic!.Cost.TotalKarma == 5,
                    "Karma magic must charge the profile price, without a Priority free slot.");
                await ui.BeginAsyncVoid(() => ((IButtonController)oldChoose).SendClicked());
                Require(Session().Selection!.MagicSelections!.Spells.Single() == spell.Identity,
                    "A departed tradition control rewrote a newer spell selection.");
                await Back();
                await Back();
                await Click("karma-open-resources");
                Element<Entry>("karma-resource-investment").Text = "0";
                await Click("karma-use-resources");
                await Click("karma-open-gear");
                await Back();
                await Click("karma-open-review");
                Require(Session().Quote!.Magic!.Cost.TotalKarma == 5 && probe!.ConfirmCalls == 0,
                    "Moving through later steps lost magic spending or saved without confirmation.");
                await Click("karma-confirm");
                var savedMagic = new FileWorkspaceStore(runtime.StateDirectory).Get(id).Value!;
                Require(probe.ConfirmCalls == 1 && savedMagic.Document.Content == before.Document.Content
                    && savedMagic.Document.AuxiliaryState.CharacterCreationKarmaMetatypeDecisions!.Single()
                        .Command.MagicSelections!.Spells.Single() == spell.Identity,
                    "Magic selections must persist once without applying the character before completion.");
                if (Environment.GetEnvironmentVariable("CHUMMER_KARMA_COMPLETION_SMOKE_DIRECTORY") is { Length: > 0 } smokeDirectory)
                {
                    Require(Path.IsPathFullyQualified(smokeDirectory) && !Directory.Exists(smokeDirectory),
                        "Karma completion smoke requires a new explicit synthetic directory.");
                    Directory.CreateDirectory(smokeDirectory);
                    foreach (string file in Directory.EnumerateFiles(runtime.StateDirectory, "*", SearchOption.AllDirectories))
                    {
                        string target = Path.Combine(smokeDirectory, Path.GetRelativePath(runtime.StateDirectory, file));
                        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                        File.Copy(file, target, overwrite: false);
                    }
                    Console.WriteLine("Karma completion synthetic fixture: " + id.Value);
                }
                await Back();
                await Click("karma-open-completion");
                Element<Entry>("karma-completion-roll").Text = "4";
                await Click("karma-completion-preview");
                Require(Element<Button>("karma-completion-confirm").IsEnabled,
                    "The saved magic draft did not reach the explicit Career review.");
                AssertReadableCompletion();
                await Click("karma-completion-confirm");
                var completedMagic = new FileWorkspaceStore(runtime.StateDirectory).Get(id).Value!;
                var xml = System.Xml.Linq.XElement.Parse(completedMagic.Document.Content);
                Require(probe.FinalConfirmCalls == 1 && xml.Element("created")!.Value == "True"
                    && xml.Element("spells")!.Elements("spell").Single().Element("sourceid")!.Value == spell.Identity.SourceId
                    && xml.Element("tradition")!.Element("sourceid")!.Value == tradition.Identity.SourceId,
                    "Native Career completion lost the selected magic or finalized more than once.");
                await runtime.Presenter.InitializeAsync(default);
                await runtime.Presenter.LoadAsync(id, default);
                Require(runtime.Coordinator.State.Profile?.Created == true
                    && runtime.Coordinator.State.ContentRevision == completedMagic.ContentRevision,
                    "Cold native Career reopen lost the magical character.");
                IssuedPageLifecycle(Current(), "OnDisappearing");
                ui.AssertHealthy();
                Console.WriteLine("PASS native Karma: phone-magic");
                return;
            }
            if (prerequisitesOnly)
            {
                await Back();
                Require(Session().Selection!.QualityOptionIds is { Count: 0 },
                    "Explicitly reviewing empty Qualities must not select a quality.");
                AssertPrerequisite("contacts", null);
                AssertPrerequisite("lifestyles", CreationKarmaCopy.ApplyResourcesFirst(CreationKarmaCopy.Resources));
                // Simulate a late native event on a departed control; its captured gate stays closed.
                disabledContacts.IsEnabled = true;
                await ui.BeginAsyncVoid(() => ((IButtonController)disabledContacts).SendClicked());
                Require(ReferenceEquals(root, Current()), "An obsolete disabled Contacts callback navigated after unlock.");
                await Click("karma-open-resources");
                Element<Entry>("karma-resource-investment").Text = "0";
                await Click("karma-use-resources");
                AssertPrerequisite("lifestyles", CreationKarmaCopy.ReviewOptionalStepFirst(CreationKarmaCopy.Gear));
                await Click("karma-open-gear");
                await Back();
                Require(Session().Selection!.GearSelections is { Count: 0 },
                    "Reviewing empty Gear must not purchase equipment.");
                AssertPrerequisite("contacts", null);
                AssertPrerequisite("lifestyles", null);
                var unchanged = new FileWorkspaceStore(runtime.StateDirectory).Get(id).Value!;
                Require(unchanged.ContentRevision == before.ContentRevision
                    && unchanged.SavedRevision == before.SavedRevision
                    && unchanged.Document.Content == before.Document.Content
                    && unchanged.Document.AuxiliaryStateDigest == before.Document.AuxiliaryStateDigest
                    && probe.ConfirmCalls == 0,
                    "Prerequisite hints or empty-step review persisted without confirmation.");
                ui.AssertHealthy();
                Console.WriteLine("PASS Karma prerequisite hints: ordered blockers, explicit empty choices, zero resources, disabled/stale callbacks, no writes");
                return;
            }
            await QualitySearch("Code of Honor");
            var unresolvedHonor = Session().Authority!.QualitiesCatalog!.Options
                .Where(option => option.Name == "Code of Honor" && !option.IsSelectable).ToArray();
            Require(unresolvedHonor.Length > 0 && unresolvedHonor.All(option => !IssuedElements(Current()).OfType<Button>()
                    .Any(button => button.AutomationId == "karma-add-quality-" + option.OptionId)),
                "The available-quality picker still lists unresolved source prompts.");
            // A separately sourced, fully defined Code of Honor variant may
            // remain available. Never suppress it merely to match an old catalog.
            await QualitySearch("Unsteady Hands");
            var addNegative = IssuedElements(Current()).OfType<Button>().Single(b => b.AutomationId?.StartsWith("karma-add-quality-", StringComparison.Ordinal) == true);
            string negativeId = addNegative.AutomationId["karma-add-quality-".Length..];
            Require(!IssuedElements(Current()).OfType<Label>().Any(label => label.Text?.Contains("quality:", StringComparison.Ordinal) == true),
                "The ordinary quality picker exposed an internal source identity.");
            var info = Element<Button>("karma-quality-info-" + negativeId);
            Require(info.Text == "!", "Quality effect help is missing.");
            await Click(info.AutomationId);
            Require(Current() is CreationQualityInfoPage
                && IssuedElements(Current()).OfType<Label>().Any(label => label.Text?.Contains("hands shake", StringComparison.Ordinal) == true)
                && !IssuedElements(Current()).OfType<Label>().Any(label => label.Text?.Contains("Rulebook", StringComparison.OrdinalIgnoreCase) == true)
                && probe.ConfirmCalls == 0, "Quality information must explain the effect inline without a book reference or saving.");
            await Back();
            int depthBeforeOldInfo = navigation.Navigation.NavigationStack.Count;
            await ui.BeginAsyncVoid(() => ((IButtonController)info).SendClicked());
            Require(navigation.Navigation.NavigationStack.Count == depthBeforeOldInfo,
                "A detached quality information control navigated again.");
            previewsBeforeQualities = probe.PreviewCalls; batchesBeforeQualities = probe.QualityBatchCalls;
            await Click(addNegative.AutomationId);
            Require(Session().QuoteCurrent && probe.PreviewCalls == previewsBeforeQualities
                && probe.QualityBatchCalls == batchesBeforeQualities + 1,
                "Changing a quality must not reload Core separately for the draft and its candidate page.");
            Require(Element<Label>("karma-quality-totals").Text == CreationKarmaCopy.QualityTotals(0, 7, -7),
                "Quality credit was not projected from the current Core quote.");
            await Click("karma-remove-quality-" + negativeId);
            await ui.BeginAsyncVoid(() => ((IButtonController)addNegative).SendClicked());
            Require(Element<Label>("karma-quality-totals").Text == CreationKarmaCopy.QualityTotals(0, 0, 0),
                "An obsolete quality button restored a removed choice.");
            await Click("karma-add-quality-" + negativeId);
            await QualitySearch("Overclocker");
            var addPositive = IssuedElements(Current()).OfType<Button>().Single(b => b.AutomationId?.StartsWith("karma-add-quality-", StringComparison.Ordinal) == true);
            await Click(addPositive.AutomationId);
            Require(Element<Label>("karma-quality-totals").Text == CreationKarmaCopy.QualityTotals(5, 7, -2)
                && new FileWorkspaceStore(runtime.StateDirectory).Get(id).Value!.ContentRevision == before.ContentRevision,
                "Quality selection must show Core net costs without persisting before review.");
            var qualitySession = Session();
            var qualityState = qualitySession.Authority!;
            var ordinarySelection = qualitySession.Selection!;
            var ordinaryQuote = qualitySession.Quote!;
            var availabilitySelection = ordinarySelection with
            {
                QualityOptionIds = ordinarySelection.QualityOptionIds!.Where(optionId =>
                    qualityState.QualitiesCatalog!.Options.Single(option => option.OptionId == optionId).Name != "Overclocker").ToArray()
            };
            var athletics = qualityState.SkillsCatalog!.SkillGroups.Single(group => group.Name == "Athletics");
            var fullBudgetSelection = availabilitySelection with
            {
                Attributes = new[] { "BOD", "AGI", "REA", "STR", "CHA", "INT", "LOG", "WIL" }
                    .Select(attribute => new CharacterCreationKarmaAttributeAllocation(attribute, 4))
                    .Append(new("EDG", 5)).ToArray(),
                Skills = ordinarySelection.Skills! with
                {
                    Skills = ordinarySelection.Skills!.Skills.Select(skill => skill.IsNativeLanguage ? skill : skill with { KarmaLevels = 6 }).ToArray(),
                    Groups = [new(athletics.GroupId, 4)]
                },
                ResourceKarmaInvestment = null
            };
            var priced = await runtime.Coordinator.PreviewCreationKarmaAsync(qualityState, fullBudgetSelection);
            Require(priced.Value is { CanSelect: true, KarmaBudget.Remaining: >= 0 },
                "SETUP: exact nearly-full Karma budget rejected: " + string.Join(",", priced.Blockers));
            fullBudgetSelection = fullBudgetSelection with { ResourceKarmaInvestment = priced.Value!.KarmaBudget.Remaining };
            var fullBudget = await runtime.Coordinator.PreviewCreationKarmaAsync(qualityState, fullBudgetSelection);
            Require(fullBudget.Value is { CanSelect: true, KarmaBudget.Remaining: 0 },
                "SETUP: exact full-budget Karma fixture rejected: " + string.Join(",", fullBudget.Blockers));
            var noFunds = await runtime.Coordinator.LoadCreationKarmaQualityPageAsync(qualityState,
                fullBudgetSelection, "Overclocker", 0, 20, default, () => true);
            Require(noFunds is { Options.Count: 0 } && runtime.Coordinator.IsCreationKarmaPreviewCurrent(fullBudget.Value!),
                "An unaffordable quality was offered, or browsing replaced the issued confirmation.");
            var funded = await runtime.Coordinator.LoadCreationKarmaQualityPageAsync(qualityState,
                availabilitySelection, "Overclocker", 0, 20, default, () => true);
            Require(funded is { Options.Count: > 0 }, "Affordable exact quality disappeared with the same sources.");
            var ratingIds = new List<string>();
            int previewsBeforePage = probe.PreviewCalls;
            int batchesBeforePage = probe.QualityBatchCalls, candidatesBeforePage = probe.QualityCandidateCount;
            CreationKarmaQualityPage? largerPage;
            observeQualityEnumeration = true;
            try
            {
                largerPage = await runtime.Coordinator.LoadCreationKarmaQualityPageAsync(qualityState,
                    ordinarySelection, "", 0, 6, default, () => true);
            }
            finally { observeQualityEnumeration = false; }
            Require(qualityEnumerations >= 2,
                "The catalog enumeration guard did not observe filtering and the identity index.");
            Require(probe.PreviewCalls == previewsBeforePage && probe.QualityBatchCalls == batchesBeforePage + 1
                && probe.QualityCandidateCount - candidatesBeforePage is > 0 and <= 6,
                "A quality page must use one bounded Core batch without individual source reloads.");
            Require(largerPage is { Options.Count: >= 2 }, "SETUP: expected at least two available source choices.");
            int? cursor = 0;
            do
            {
                previewsBeforePage = probe.PreviewCalls;
                batchesBeforePage = probe.QualityBatchCalls; candidatesBeforePage = probe.QualityCandidateCount;
                var ratingPage = await runtime.Coordinator.LoadCreationKarmaQualityPageAsync(qualityState,
                    ordinarySelection, "", cursor!.Value, 1, default, () => true);
                Require(probe.PreviewCalls == previewsBeforePage && probe.QualityBatchCalls - batchesBeforePage <= 1
                    && probe.QualityCandidateCount - candidatesBeforePage <= 1,
                    "Filtered pagination performed extra full previews to fill or look ahead.");
                Require(ratingPage is not null, "Rating pagination lost its bound source read.");
                ratingIds.AddRange(ratingPage!.Options.Select(option => option.OptionId));
                cursor = ratingPage.NextOffset;
            } while (cursor is not null && ratingIds.Count < 2);
            Require(ratingIds.SequenceEqual(largerPage!.Options.Take(2).Select(option => option.OptionId)),
                "Filtered pagination skipped or duplicated a legal source choice.");
            var canceledRead = new CancellationTokenSource();
            probe.AfterPreview = canceledRead.Cancel;
            bool canceled = false;
            try { await runtime.Coordinator.LoadCreationKarmaQualityPageAsync(qualityState,
                availabilitySelection, "Overclocker", 0, 20, canceledRead.Token, () => true); }
            catch (OperationCanceledException) { canceled = true; }
            finally { probe.AfterPreview = null; canceledRead.Dispose(); }
            Require(canceled, "Canceled quality checking returned actionable choices.");
            bool currentQualityPage = true;
            probe.AfterPreview = () => currentQualityPage = false;
            var departedChoices = await runtime.Coordinator.LoadCreationKarmaQualityPageAsync(qualityState,
                availabilitySelection, "Overclocker", 0, 20, default, () => currentQualityPage);
            probe.AfterPreview = null;
            Require(departedChoices is null && probe.ConfirmCalls == 0,
                "A departed quality page admitted a late result or persisted a choice.");
            probe.FailReads = true;
            var failedChoices = await runtime.Coordinator.LoadCreationKarmaQualityPageAsync(qualityState,
                availabilitySelection, "Overclocker", 0, 20, default, () => true);
            probe.FailReads = false;
            Require(failedChoices is null, "A failed source read was disguised as an empty available catalog.");
            await qualitySession.PreviewAsync(default, () => true);
            Require(qualitySession.QuoteCurrent && qualitySession.Quote!.QuoteDigest == ordinaryQuote.QuoteDigest,
                "Read-only quality checks changed the actual draft budget.");
            previewsBeforePage = probe.PreviewCalls;
            batchesBeforePage = probe.QualityBatchCalls; candidatesBeforePage = probe.QualityCandidateCount;
            var emptyReviewedPage = await qualitySession.PreviewQualityPageAsync("no-quality-matches-this-filter",
                0, 3, default, () => true);
            Require(emptyReviewedPage is { Options.Count: 0, DraftPreview.Value: not null }
                && qualitySession.QuoteCurrent && !ReferenceEquals(qualitySession.Quote, ordinaryQuote)
                && qualitySession.Quote!.QuoteDigest == ordinaryQuote.QuoteDigest
                && probe.PreviewCalls == previewsBeforePage && probe.QualityBatchCalls == batchesBeforePage + 1
                && probe.QualityCandidateCount == candidatesBeforePage + 1,
                "An empty catalog page must still freshly validate the exact draft, without candidate/review substitution.");
            var lastDraftReview = qualitySession.Quote!;
            using (var canceledDraft = new CancellationTokenSource())
            {
                probe.AfterPreview = canceledDraft.Cancel;
                canceled = false;
                try { await qualitySession.PreviewQualityPageAsync("", 0, 3, canceledDraft.Token, () => true); }
                catch (OperationCanceledException) { canceled = true; }
                finally { probe.AfterPreview = null; }
                Require(canceled && !qualitySession.QuoteCurrent
                    && !runtime.Coordinator.IsCreationKarmaPreviewCurrent(lastDraftReview),
                    "A canceled combined check retained an actionable draft review.");
            }
            currentQualityPage = true;
            probe.AfterPreview = () => currentQualityPage = false;
            var departedDraft = await qualitySession.PreviewQualityPageAsync("", 0, 3, default, () => currentQualityPage);
            probe.AfterPreview = null;
            Require(departedDraft is null && !qualitySession.QuoteCurrent,
                "A departed combined check issued a draft or candidate page.");
            probe.FailReads = true;
            var failedDraft = await qualitySession.PreviewQualityPageAsync("", 0, 3, default, () => true);
            probe.FailReads = false;
            Require(failedDraft is null && !qualitySession.QuoteCurrent && probe.ConfirmCalls == 0,
                "A failed combined source check retained a confirmable draft or performed a write.");
            await qualitySession.PreviewQualityPageAsync("", 0, 3, default, () => true);
            Require(qualitySession.QuoteCurrent && qualitySession.Quote!.QuoteDigest == ordinaryQuote.QuoteDigest,
                "Explicit recovery changed the selected qualities or did not issue a fresh review.");
            Console.WriteLine("PASS Karma quality availability: single fresh draft/candidate batch, empty page review, background catalog preparation, exact total budget, source eligibility, filtered rating pages, cancellation/departure/read failure, no writes or browse-review replacement");
            await Back();
            await Click("karma-open-contacts");
            await Click("karma-add-contact");
            var oldContactName = Element<Entry>("karma-contact-name");
            oldContactName.Text = "Mara ü & <Fixer>";
            Element<Entry>("karma-contact-role").Text = "Fixer";
            Element<Entry>("karma-contact-notes").Text = "Madrid — información";
            Element<Stepper>("karma-contact-connection").Value = 2;
            Element<Stepper>("karma-contact-loyalty").Value = 2;
            await Click("karma-use-contact");
            string contactId = IssuedElements(Current()).OfType<Button>().Single(b => b.Text == "Mara ü & <Fixer>").AutomationId;
            oldContactName.Text = "obsolete callback";
            Require(Element<Label>("karma-contact-totals").Text == CreationKarmaCopy.ContactTotals(4, 3, 0, 0, 1)
                && new FileWorkspaceStore(runtime.StateDirectory).Get(id).Value!.ContentRevision == before.ContentRevision,
                "Contact preview must price Core overflow without saving or accepting a departed editor.");
            await Click("karma-add-contact");
            Element<Entry>("karma-contact-name").Text = "The Union";
            Element<Switch>("karma-contact-group").IsToggled = true;
            Element<Stepper>("karma-contact-loyalty").Value = 2;
            await Click("karma-use-contact");
            string groupId = IssuedElements(Current()).OfType<Button>().Single(b => b.Text == "The Union").AutomationId;
            Require(!IssuedElements(Current()).OfType<Label>().Any(label => label.AutomationId == "karma-contact-totals"),
                "An invalid group Loyalty must not retain a previous valid quote.");
            await Click(groupId);
            Element<Stepper>("karma-contact-loyalty").Value = 1;
            await Click("karma-use-contact");
            Require(Element<Label>("karma-contact-totals").Text == CreationKarmaCopy.ContactTotals(4, 3, 0, 0, 3),
                "Group Karma must be charged separately from the free-contact pool.");
            await Click(contactId);
            Require(Element<Entry>("karma-contact-name").Text == "Mara ü & <Fixer>",
                "Returning to the contact editor lost the unsaved identity.");
            await Back();
            await Back();
            await Click("karma-open-resources");
            var resourceInput = Element<Entry>("karma-resource-investment");
            var numberCulture = CultureInfo.CurrentCulture;
            try
            {
                foreach (string language in new[] { "de-AT", "en-US", "es-ES" })
                {
                    CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(language);
                    resourceInput.Text = "not-a-number";
                    Require(!Element<Button>("karma-use-resources").IsEnabled
                        && Element<Label>("karma-resource-invalid").IsVisible,
                        "Invalid resource text must not reuse an earlier amount.");
                    resourceInput.Text = 10.5m.ToString(CultureInfo.CurrentCulture);
                    Require(Element<Button>("karma-use-resources").IsEnabled, "Localized decimal amount was rejected.");
                }
            }
            finally { CultureInfo.CurrentCulture = numberCulture; }
            resourceInput.Text = 10.5m.ToString(CultureInfo.CurrentCulture);
            await Click("karma-use-resources");
            resourceInput.Text = "99";
            Require(Element<Label>("karma-resource-funding").Text == CreationKarmaCopy.ResourceFunding(10.5m, 21000m),
                "An obsolete resource entry altered the selected draft: " + Element<Label>("karma-resource-funding").Text);
            await Click("karma-open-gear");
            ReadableSearch("karma-gear-search").Text = "  Flashlight  ";
            await Click("karma-gear-search-go");
            var addGear = IssuedElements(Current()).OfType<Button>().First(b => b.IsEnabled
                && b.AutomationId?.StartsWith("karma-add-gear-", StringComparison.Ordinal) == true);
            string gearId = addGear.AutomationId["karma-add-gear-".Length..];
            await Click(addGear.AutomationId);
            var oldQuantity = Element<Stepper>("karma-gear-quantity-" + gearId);
            oldQuantity.Value = 2;
            await Click("karma-preview-gear");
            Require(Element<Label>("karma-gear-totals").Text == CreationKarmaCopy.GearTotals(21000m, 50m, 20950m, 0m),
                "Gear quantity was not priced from the current Core resource quote.");
            oldQuantity.Value = 3;
            Require(Element<Stepper>("karma-gear-quantity-" + gearId).Value == 2,
                "An obsolete quantity control changed the current basket.");
            Require(new FileWorkspaceStore(runtime.StateDirectory).Get(id).Value!.ContentRevision == before.ContentRevision
                && probe!.ConfirmCalls == 0, "Editing gear persisted without review.");
            await Back();
            await Click("karma-open-lifestyles");
            var addLow = IssuedElements(Current()).OfType<Button>().Single(b => b.Text == "Low");
            await Click(addLow.AutomationId);
            var oldLifestyleName = Element<Entry>("karma-lifestyle-name");
            oldLifestyleName.Text = "Home ü & <Safe>";
            Element<Entry>("karma-lifestyle-city").Text = "Madrid";
            Element<Entry>("karma-lifestyle-increments").Text = "invalid";
            Require(!Element<Button>("karma-use-lifestyle").IsEnabled
                && Element<Label>("karma-lifestyle-invalid").IsVisible,
                "Invalid lifestyle input must not retain an earlier usable amount.");
            Element<Entry>("karma-lifestyle-increments").Text = "200";
            await Click("karma-use-lifestyle");
            string lowId = IssuedElements(Current()).OfType<Button>().Single(b => b.Text == "Home ü & <Safe>").AutomationId;
            Require(Element<Label>("karma-lifestyle-totals").Text == CreationKarmaCopy.LifestyleTotals(21000m, 50m, 400000m, -379050m, 379050m),
                "An oversized lifestyle must display Core's actual shared-budget overspend.");
            oldLifestyleName.Text = "obsolete lifestyle callback";
            await Click(lowId);
            Require(Element<Entry>("karma-lifestyle-name").Text == "Home ü & <Safe>",
                "A departed lifestyle editor changed the current selection.");
            Element<Entry>("karma-lifestyle-increments").Text = "2";
            await Click("karma-use-lifestyle");
            Require(IssuedElements(Current()).OfType<Label>().Any(label => label.AutomationId
                == "karma-blocker-creation-karma-starting-lifestyle-required"),
                "Adding a lifestyle must not silently select starting cash.");
            await Click("karma-starting-lifestyle-" + lowId["karma-lifestyle-".Length..]);
            var addMedium = IssuedElements(Current()).OfType<Button>().Single(b => b.Text == "Medium");
            await Click(addMedium.AutomationId);
            await Click("karma-use-lifestyle");
            // Catalog and owned rows share source names; resolve the owned row by stable instance ID.
            string mediumId = IssuedElements(Current()).OfType<Button>().Single(b => b.Text == "Medium"
                && b.AutomationId?.StartsWith("karma-lifestyle-", StringComparison.Ordinal) == true).AutomationId;
            await Click("karma-starting-lifestyle-" + mediumId["karma-lifestyle-".Length..]);
            await Click(mediumId);
            var removedLifestyleControl = Element<Entry>("karma-lifestyle-name");
            await Click("karma-remove-lifestyle");
            removedLifestyleControl.Text = "obsolete removed lifestyle";
            Require(IssuedElements(Current()).OfType<Label>().Any(label => label.AutomationId
                == "karma-blocker-creation-karma-starting-lifestyle-required")
                && !IssuedElements(Current()).OfType<Button>().Any(button => button.AutomationId == mediumId),
                "Deleting the starting lifestyle must clear its selection without choosing another one or replaying a stale control.");
            await Click(addMedium.AutomationId);
            await Click("karma-use-lifestyle");
            string replacementMediumId = IssuedElements(Current()).OfType<Button>().Single(b => b.Text == "Medium"
                && b.AutomationId?.StartsWith("karma-lifestyle-", StringComparison.Ordinal) == true).AutomationId;
            Require(replacementMediumId != mediumId, "A newly purchased lifestyle reused a deleted instance identity.");
            mediumId = replacementMediumId;
            await Click("karma-starting-lifestyle-" + mediumId["karma-lifestyle-".Length..]);
            Require(Element<Label>("karma-lifestyle-totals").Text == CreationKarmaCopy.LifestyleTotals(21000m, 50m, 9000m, 11950m, 0m)
                && new FileWorkspaceStore(runtime.StateDirectory).Get(id).Value!.ContentRevision == before.ContentRevision
                && probe.ConfirmCalls == 0,
                "Lifestyle and equipment costs must share one Core budget without a pre-review save.");
            await Back();
            await Click("karma-open-review");
            Require(Element<Button>("karma-confirm").IsEnabled, "The exact Human/Mundane/native-language/Pistols review is not confirmable.");
            var reviewedSession = (CreationKarmaPhoneSession)typeof(CreationKarmaPage)
                .GetField("_session", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(Current())!;
            Console.WriteLine("Karma lifestyle review bytes: "
                + System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(reviewedSession.Quote).Length
                + "; lifestyle projection bytes: "
                + System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(reviewedSession.Quote!.Lifestyles!.ProjectionAuthority).Length);
            await Click("karma-confirm");
            Require(IssuedElements(Current()).OfType<Label>().Any(label => label.AutomationId == "creation-karma-saved"
                    && label.Text == CreationKarmaCopy.Saved)
                && !IssuedElements(Current()).OfType<Button>().Any(b => b.AutomationId == "karma-confirm" && b.IsEnabled),
                "Saved review did not expose its known result or offered another write: "
                    + string.Join(" | ", IssuedElements(Current()).OfType<Label>().Select(label => label.Text)));
            var cold = new FileWorkspaceStore(runtime.StateDirectory).Get(id).Value!;
            var decision = cold.Document.AuxiliaryState.CharacterCreationKarmaMetatypeDecisions!.Single();
            Require(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(decision).Length <= 128 * 1024,
                "The ordinary multi-domain draft no longer fits the unchanged bounded Core decision ledger.");
            Require(probe!.ConfirmCalls == 1 && cold.ContentRevision == before.ContentRevision + 1
                && cold.Document.Content == before.Document.Content
                && decision.Quote.Attributes!.Attributes.Single(a => a.AttributeId == "AGI").Current == 2
                && decision.Quote.Skills!.Skills.Single(s => s.Name == "Pistols").Rating == 1
                && decision.Quote.Skills.NativeLanguagesUsed == 1
                && decision.Command.ResourceKarmaInvestment == 10.5m
                && decision.Quote.Resources!.NuyenFromKarma == 21000m
                && decision.Command.QualityOptionIds!.Count == 2
                && decision.Quote.Qualities!.Costs.NetKarmaSpent == -2
                && decision.Command.GearSelections!.Single().Quantity == 2
                && decision.Quote.Gear!.Budget.BasketCost == 50m
                && decision.Command.ContactSelections!.Count == 2
                && decision.Quote.Contacts!.KarmaUsed == 3
                && decision.Command.LifestyleSelections!.Count == 2
                && decision.Quote.Lifestyles!.LifestyleNuyenUsed == 9000m
                && decision.Quote.Lifestyles.Budget.Remaining == 11950m
                && decision.Command.StartingLifestyleId!.Value.ToString("D") == mediumId["karma-lifestyle-".Length..],
                "Native review did not persist exactly the selected source-bound pending foundation.");
            if (Environment.GetEnvironmentVariable("CHUMMER_KARMA_QUALITY_SMOKE_PATH") is { Length: > 0 } smokePath)
            {
                Require(Path.IsPathFullyQualified(smokePath) && !File.Exists(smokePath),
                    "Native smoke export must be a new, explicit synthetic fixture path.");
                File.Copy(Path.Combine(runtime.StateDirectory, "workspaces", id.Value + ".json"), smokePath, overwrite: false);
                Console.WriteLine("Karma quality smoke fixture: " + id.Value);
            }
            // Recreate all page/session objects and reread the actual durable
            // store. This is managed reopen evidence, not an Android process restart.
            IssuedPageLifecycle(Current(), "OnDisappearing");
            await HydrateFinalizationOwnerAsync(runtime, owners, cold);
            foreach (var corrupt in new Func<CharacterCreationKarmaMetatypeOpen, CharacterCreationKarmaMetatypeOpen>[]
            {
                opened => opened with { Quote = opened.State.Selection!.Quote },
                opened => opened with { Quote = null },
                opened => opened with { Quote = opened.Quote! with { SnapshotDigest = "sha256:" + new string('a', 64) } }
            })
            {
                probe.TransformOpen = corrupt;
                Require((await runtime.Coordinator.OpenCreationKarmaAsync()).Value is null,
                    "Open admitted a historical, missing or mismatched saved review.");
            }
            probe.TransformOpen = null;
            await CheckSavedDashboardAsync();
            int opensBeforeReopen = probe.OpenCalls;
            int loadsBeforeReopen = probe.LoadCalls;
            int previewsBeforeReopen = probe.PreviewCalls;
            var reopened = new CreationKarmaPage(runtime.Coordinator);
            await navigation.PushAsync(reopened, false);
            await Appear();
            Require(probe.OpenCalls == opensBeforeReopen + 1 && probe.LoadCalls == loadsBeforeReopen
                && probe.PreviewCalls == previewsBeforeReopen && probe.ConfirmCalls == 1,
                "Reopening a saved wizard must use one Core Open, without a second Load/Preview or a write.");
            await Click("karma-open-contacts");
            Require(Element<Label>("karma-contact-totals").Text == CreationKarmaCopy.ContactTotals(4, 3, 0, 0, 3),
                "Cold reopen lost the saved contact budget.");
            await Click(contactId);
            Require(Element<Entry>("karma-contact-name").Text == "Mara ü & <Fixer>"
                && Element<Entry>("karma-contact-notes").Text == "Madrid — información"
                && Element<Stepper>("karma-contact-connection").Value == 2
                && Element<Stepper>("karma-contact-loyalty").Value == 2,
                "Cold reopen lost contact identity or ratings.");
            await Back();
            await Back();
            await Click("karma-open-attributes");
            Require(Element<Stepper>("karma-attribute-AGI").Value == 1, "Reopened phone page lost its durable allocation.");
            await Back();
            await Click("karma-open-resources");
            Require(Element<Entry>("karma-resource-investment").Text == 10.5m.ToString(CultureInfo.CurrentCulture),
                "Cold reopen lost the resource investment.");
            await Back();
            await Click("karma-open-gear");
            Require(Element<Stepper>("karma-gear-quantity-" + gearId).Value == 2
                && Element<Label>("karma-lifestyle-totals").Text == CreationKarmaCopy.LifestyleTotals(21000m, 50m, 9000m, 11950m, 0m),
                "Cold reopen lost equipment identity, quantity or funding binding.");
            await Back();
            await Click("karma-open-lifestyles");
            Require(!Element<Button>("karma-starting-lifestyle-" + mediumId["karma-lifestyle-".Length..]).IsEnabled,
                "Cold reopen lost the explicitly selected starting lifestyle.");
            await Click(lowId);
            Require(Element<Entry>("karma-lifestyle-name").Text == "Home ü & <Safe>"
                && Element<Entry>("karma-lifestyle-city").Text == "Madrid"
                && Element<Entry>("karma-lifestyle-increments").Text == "2",
                "Cold reopen lost lifestyle identity, location or duration.");
            await Back();
            await Back();
            await Click("karma-open-qualities");
            Require(IssuedElements(Current()).OfType<Button>().Count(b => b.AutomationId?.StartsWith("karma-remove-quality-", StringComparison.Ordinal) == true) == 2
                && Element<Label>("karma-quality-totals").Text == CreationKarmaCopy.QualityTotals(5, 7, -2),
                "Cold reopen lost the selected quality identities or recalculated profile costs.");
            await Back();
            await Click("karma-open-skills");
            Require(IssuedElements(Current()).OfType<Button>().Count(b => b.AutomationId?.StartsWith("karma-selected-skill-", StringComparison.Ordinal) == true) == 2,
                "Reopened phone page lost the native language or active skill.");
            await Back();
            await Click("karma-open-completion");
            Require(Element<Entry>("karma-completion-roll").Text == string.Empty
                && !Element<Button>("karma-completion-preview").IsEnabled
                && !Element<Button>("karma-completion-confirm").IsEnabled,
                "Completion guessed a dice result or authorized confirmation before review.");
            Element<Entry>("karma-completion-roll").Text = "999";
            await Click("karma-completion-preview");
            Require(!Element<Button>("karma-completion-confirm").IsEnabled && probe.FinalConfirmCalls == 0,
                "An out-of-range starting-cash roll authorized completion.");
            Element<Entry>("karma-completion-roll").Text = "4";
            await Click("karma-completion-preview");
            var oldFinalConfirm = Element<Button>("karma-completion-confirm");
            Require(oldFinalConfirm.IsEnabled, "Core did not admit the saved mundane foundation for completion.");
            Element<Entry>("karma-completion-roll").Text = "5";
            Require(!oldFinalConfirm.IsEnabled, "Editing a roll did not invalidate its exact review.");
            ((IButtonController)oldFinalConfirm).SendClicked(); // MAUI suppresses disabled Clicked events.
            Require(probe.FinalConfirmCalls == 0, "An obsolete review confirmed a different roll.");
            Element<Entry>("karma-completion-roll").Text = "4";
            await Click("karma-completion-preview");
            var review = (CharacterCreationFinalizationReview)typeof(CreationKarmaCompletionPage)
                .GetField("_review", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(Current())!;
            AssertReadableCompletion();
            Require((await runtime.Coordinator.ConfirmKarmaCompletionAsync(review, false, default, () => true)).Value is null
                && (await runtime.Coordinator.ConfirmKarmaCompletionAsync(review with { }, true, default, () => true)).Value is null
                && probe.FinalConfirmCalls == 0, "Completion accepted implicit consent or a forged review identity.");
            Require(new FileWorkspaceStore(runtime.StateDirectory).Get(id).Value!.ContentRevision == cold.ContentRevision,
                "Starting-cash preview mutated the pending workspace.");
            var finalButton = Element<Button>("karma-completion-confirm");
            await Click("karma-completion-confirm");
            Require(Element<Label>("karma-completion-receipt").Text == CreationKarmaCopy.CareerReady
                && Element<Button>("karma-completion-open-career").IsEnabled && probe.FinalConfirmCalls == 1,
                "Atomic completion did not reopen the saved Career runner.");
            var completed = new FileWorkspaceStore(runtime.StateDirectory).Get(id).Value!;
            var finalRoot = System.Xml.Linq.XDocument.Parse(completed.Document.Content).Root!;
            var finalLifestyles = finalRoot.Element("lifestyles")!.Elements("lifestyle").ToArray();
            Require(finalLifestyles.Length == 2
                && finalLifestyles.Single(row => row.Element("name")!.Value == "Home ü & <Safe>").Element("city")!.Value == "Madrid"
                && finalRoot.Element("nuyen")!.Value == "5400",
                "Career transition lost purchased lifestyles or used default Street starting cash.");
            var finalContacts = System.Xml.Linq.XDocument.Parse(completed.Document.Content).Root!.Element("contacts")!.Elements("contact").ToArray();
            Require(finalContacts.Length == 2
                && finalContacts.Single(item => item.Element("name")!.Value == "Mara ü & <Fixer>").Element("notes")!.Value == "Madrid — información"
                && finalContacts.Single(item => item.Element("name")!.Value == "The Union").Element("group")!.Value == "true",
                "Career transition lost or duplicated pending contacts.");
            Require(completed.ContentRevision == cold.ContentRevision + 1 && completed.SavedRevision == completed.ContentRevision
                && completed.Document.AuxiliaryState.CharacterCreationFinalizationArchive?.KarmaAuthority is not null
                && completed.Document.AuxiliaryState.CharacterCreationFinalizationArchive.State.CharacterCreationKarmaMetatypeDecisions!.Single().DecisionDigest == decision.DecisionDigest
                && runtime.Coordinator.State.Profile?.Created == true
                && !runtime.Coordinator.CanOpenCreationKarma(), "Completion lost history or retained Creation mode.");
            await ui.BeginAsyncVoid(() => ((IButtonController)finalButton).SendClicked());
            Require(probe.FinalConfirmCalls == 1 && new FileWorkspaceStore(runtime.StateDirectory).Get(id).Value!.ContentRevision == completed.ContentRevision,
                "An obsolete finalization button replayed the mutation.");
            await runtime.Shell.InitializeAsync(default);
            await runtime.Presenter.InitializeAsync(default);
            await runtime.Presenter.LoadAsync(completed.Id, default);
            Require(runtime.Coordinator.State.Profile?.Created == true
                && runtime.Coordinator.State.ContentRevision == completed.ContentRevision
                && runtime.Coordinator.State.SavedRevision == completed.SavedRevision
                && runtime.Coordinator.State.DisplayOwnerContext == owners.Capture()
                && runtime.Coordinator.State.Session.OwnerContext == owners.Capture(),
                "Cold Career reopen lost the finalization transition.");
            IssuedPageLifecycle(Current(), "OnDisappearing");
            var prior = CultureInfo.CurrentUICulture;
            try
            {
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de-AT");
                Require(CreationKarmaCopy.Title == "Karma-Grunddaten", "German regional resources are missing.");
                Require(CreationKarmaCopy.AttributePurchases("Konstitution", 0).Contains("zugekaufte Stufen: 0", StringComparison.Ordinal)
                    && CreationKarmaCopy.Blocker(CharacterCreationKarmaTalentCatalog.UnsupportedSource).Contains("noch nicht unterstützt", StringComparison.Ordinal),
                    "German attribute/talent explanations are missing.");
                Require(CreationKarmaCopy.Qualities == "Vor- und Nachteile", "German quality resources are missing.");
                Require(CreationKarmaCopy.Gear == "Ausrüstung", "German equipment resources are missing.");
                Require(CreationKarmaCopy.Lifestyles == "Lebensstile", "German lifestyle resources are missing.");
                Require(CreationKarmaCopy.Finish == "Karma-Erstellung abschließen", "German completion resources are missing.");
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("es-MX");
                Require(CreationKarmaCopy.Confirm == "Confirmar y guardar borrador", "Spanish regional resources are missing.");
                Require(CreationKarmaCopy.AttributePurchases("Agilidad", 1).Contains("niveles comprados: 1", StringComparison.Ordinal)
                    && CreationKarmaCopy.Blocker(CharacterCreationKarmaTalentCatalog.SourceDisabled).Contains("no está activado", StringComparison.Ordinal),
                    "Spanish attribute/talent explanations are missing.");
                Require(CreationKarmaCopy.Qualities == "Cualidades", "Spanish quality resources are missing.");
                Require(CreationKarmaCopy.Gear == "Equipo", "Spanish equipment resources are missing.");
                Require(CreationKarmaCopy.Lifestyles == "Estilos de vida", "Spanish lifestyle resources are missing.");
                Require(CreationKarmaCopy.Finish == "Finalizar creación con Karma", "Spanish completion resources are missing.");
            }
            finally { CultureInfo.CurrentUICulture = prior; }
            ui.AssertHealthy();
            Console.WriteLine($"Karma phone source work: opens={probe!.OpenCalls}, loads={probe.LoadCalls}, previews={probe.PreviewCalls}, open-ms={probe.OpenTime.TotalMilliseconds:F0}, load-ms={probe.LoadTime.TotalMilliseconds:F0}, preview-ms={probe.PreviewTime.TotalMilliseconds:F0}");
            Console.WriteLine("PASS Karma native phone deep pages: explicit choices, stale controls, draft Back, review/save, cold reopen, DE/ES resources");

            async Task CheckSavedDashboardAsync()
            {
                // A second, actually loaded Career workspace exercises the
                // retained dashboard's native Picker, not OnAppearing again.
                await runtime.LoadRunnerAsync();
                var careerId = runtime.Id;
                var careerBefore = new FileWorkspaceStore(runtime.StateDirectory).Get(careerId).Value!;
                await HydrateFinalizationOwnerAsync(runtime, owners, cold);
                var dashboard = new BuildPage(runtime.Coordinator);
                // Complete only the headless scroll animation request, not any
                // coordinator/read/mutation work. Native scrolling is smoked on Android.
                var scroll = (IScrollViewController)(ScrollView)dashboard.Content!;
                scroll.ScrollToRequested += (_, _) => scroll.SendScrollFinished();
                await navigation.PushAsync(dashboard, false);
                using var dashboardAlerts = new IssuedPageAlerts(dashboard, window);
                await dashboardAlerts.PreflightAsync();
                int opens = probe.OpenCalls, previews = probe.PreviewCalls;
                await JoinIssuedPageAsync(Appear());
                Require(dashboardAlerts.Titles.Count == 0,
                    "Dashboard appearance failed: " + string.Join("; ", dashboardAlerts.Messages));
                string budget = CreationKarmaCopy.Budget(decision.Quote.KarmaBudget.Used,
                    decision.Quote.KarmaBudget.Total, decision.Quote.KarmaBudget.Remaining);
                Require(!IssuedElements(dashboard).Any(e => e.AutomationId?.StartsWith("creation-budget-", StringComparison.Ordinal) == true)
                    && Element<Label>("creation-karma-dashboard-budget").Text == budget,
                    "Saved Karma dashboard must show its exact Core budget, not generic Priority Not exact ledgers.");
                Require(probe.OpenCalls == opens + 1 && probe.PreviewCalls == previews && probe.ConfirmCalls == 1,
                    "Karma dashboard must freshly open the saved draft once without previewing or writing it.");
                var oldPicker = Element<Picker>("build-workspace-picker");
                int careerIndex = runtime.Coordinator.State.OpenWorkspaces.ToList().FindIndex(w => w.Id == careerId);
                Require(careerIndex >= 0 && careerIndex != oldPicker.SelectedIndex,
                    "SETUP: Career workspace is absent from the actual picker.");
                await ui.BeginAsyncVoid(() => oldPicker.SelectedIndex = careerIndex);
                await JoinIssuedPageAsync(ui.DrainDispatchedAsyncVoidAsync());
                Require(runtime.Coordinator.State.WorkspaceId == careerId
                    && IssuedElements(dashboard).Any(e => e.AutomationId == "phone-runner-sheet"),
                    "The actual picker did not show the selected Career runner.");
                int karmaIndex = runtime.Coordinator.State.OpenWorkspaces.ToList().FindIndex(w => w.Id == id);
                var careerPicker = Element<Picker>("build-workspace-picker");
                int warmOpens = probe.OpenCalls;
                await ui.BeginAsyncVoid(() => careerPicker.SelectedIndex = karmaIndex);
                await JoinIssuedPageAsync(ui.DrainDispatchedAsyncVoidAsync());
                Require(runtime.Coordinator.State.WorkspaceId == id
                    && Element<Label>("creation-karma-dashboard-budget").Text == budget
                    && Element<Button>("creation-stage-method").IsEnabled
                    && probe.OpenCalls == warmOpens + 1,
                    "Warm Career → Karma switch must freshly load the saved quote and enable its editor without another appearance.");
                var currentPicker = Element<Picker>("build-workspace-picker");
                // A native event from a replaced visual tree must not switch
                // the current runner or reload authority.
                await ui.BeginAsyncVoid(() => oldPicker.SelectedIndex = -1);
                await ui.BeginAsyncVoid(() => oldPicker.SelectedIndex = careerIndex);
                Require(runtime.Coordinator.State.WorkspaceId == id
                    && ReferenceEquals(currentPicker, Element<Picker>("build-workspace-picker"))
                    && probe.OpenCalls == warmOpens + 1,
                    "An obsolete picker changed the current workspace or dashboard.");
                // Returning to the same account name is not uninterrupted
                // ownership: the old control still carries the former stamp.
                owners.Set(ContactsOwnerB);
                owners.Set(Chummer.Contracts.Owners.OwnerScope.LocalSingleUser);
                int currentCareerIndex = runtime.Coordinator.State.OpenWorkspaces.ToList().FindIndex(w => w.Id == careerId);
                await ui.BeginAsyncVoid(() => currentPicker.SelectedIndex = currentCareerIndex);
                Require(runtime.Coordinator.State.WorkspaceId == id
                    && probe.OpenCalls == warmOpens + 1 && owners.ActiveLeases == 0,
                    "An owner A→B→A picker callback switched or loaded a runner.");
                IssuedPageLifecycle(dashboard, "OnDisappearing");
                await HydrateFinalizationOwnerAsync(runtime, owners, cold);
                await Appear();
                var careerAfter = new FileWorkspaceStore(runtime.StateDirectory).Get(careerId).Value!;
                Require(careerAfter.ContentRevision == careerBefore.ContentRevision
                    && careerAfter.Document.Content == careerBefore.Document.Content
                    && careerAfter.Document.AuxiliaryStateDigest == careerBefore.Document.AuxiliaryStateDigest,
                    "Warm workspace selection mutated the Career runner.");
                var oldRoute = Element<Button>("creation-stage-method");
                await Click("creation-stage-method");
                Require(Current() is CreationKarmaPage
                    && Element<Label>("creation-karma-budget").Text == budget,
                    "Dashboard correction link must open the exact saved Karma wizard.");
                var openedPage = Current();
                int navigationCount = navigation.Navigation.NavigationStack.Count;
                await ui.BeginAsyncVoid(() => ((IButtonController)oldRoute).SendClicked());
                Require(navigation.Navigation.NavigationStack.Count == navigationCount && ReferenceEquals(Current(), openedPage),
                    "A departed dashboard callback opened another editor.");
                await Back();
                Require(Element<Label>("creation-karma-dashboard-budget").Text == budget,
                    "Returning from the Karma wizard lost its exact saved budget.");
                IssuedPageLifecycle(dashboard, "OnDisappearing");
                probe.FailReads = true;
                await Appear();
                Require(Element<Label>("creation-karma-dashboard-budget").Text == CreationKarmaCopy.Stale,
                    "A failed fresh read must not present the previous Karma budget as current.");
                probe.FailReads = false;
                await Click("creation-stage-method");
                Require(Current() is CreationKarmaPage && Element<Label>("creation-karma-budget").Text == budget,
                    "A source read recovery must remain reachable through the guarded Karma route.");
                IssuedPageLifecycle(Current(), "OnDisappearing");
                await navigation.PopAsync(false);
                await navigation.PopAsync(false);
                var unchanged = new FileWorkspaceStore(runtime.StateDirectory).Get(id).Value!;
                Require(unchanged.ContentRevision == cold.ContentRevision
                    && unchanged.Document.AuxiliaryStateDigest == cold.Document.AuxiliaryStateDigest
                    && probe.ConfirmCalls == 1,
                    "Dashboard display/navigation changed the saved Karma draft.");
                Console.WriteLine("PASS Karma dashboard: fresh budget, warm Career/Karma switch, stale picker/owner ABA rejection, correct editor, departed callback, read failure/recovery, no writes");
            }

            void AssertReadableCompletion()
            {
                var review = (CharacterCreationFinalizationReview)typeof(CreationKarmaCompletionPage)
                    .GetField("_review", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(Current())!;
                string sealedReview = System.Text.Json.JsonSerializer.Serialize(review);
                var saved = new FileWorkspaceStore(runtime.StateDirectory).Get(id).Value!;
                Label[] visible = IssuedElements(Current()).OfType<Label>().Where(label => label.IsVisible).ToArray();
                Require(!visible.Any(label => System.Text.RegularExpressions.Regex.IsMatch(label.Text ?? string.Empty,
                        @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}|\.xml#")),
                    "Karma final review exposes technical identities instead of the sealed source names.");
                foreach (var delta in review.OrderedDeltas.Where(delta => !string.IsNullOrWhiteSpace(delta.TargetName)))
                    Require(Element<Label>("karma-completion-delta-" + delta.Order).Text!.Contains(delta.TargetName!, StringComparison.Ordinal),
                        "Karma final review omitted the exact Core-bound name: " + delta.Kind);
                foreach (var delta in review.OrderedDeltas.Where(delta => delta.TargetId is
                    "magenabled" or "resenabled" or "depenabled" or "spells-karma" or "complex-forms-karma"
                    or "startingnuyen" or "resource-karma-rounding" or "nuyen-carried" or "lifestyle-starting-nuyen"
                    or "contacts-karma" or "qualities-karma-adjustment"))
                    Require(!Element<Label>("karma-completion-delta-" + delta.Order).Text!.StartsWith(delta.TargetId + ":", StringComparison.Ordinal),
                        "Karma final review exposes an internal scalar field: " + delta.TargetId);
                Require(visible.Any(label => label.Text == CreationKarmaCopy.DiceTotalHelp),
                    "Karma starting-cash input must explain the dice sum, not just request a number.");
                var input = Element<Entry>("karma-completion-roll");
                Require(input.TextColor == NativeTheme.Text && input.BackgroundColor == NativeTheme.Surface,
                    "Karma dice input must remain readable in the app's light theme.");
                Label[] sources = IssuedElements(Current()).OfType<Label>()
                    .Where(label => label.AutomationId?.StartsWith("karma-completion-source-", StringComparison.Ordinal) == true).ToArray();
                Require(sources.Length == review.OrderedDeltas.Count && sources.All(label => !label.IsVisible),
                    "Every exact delta must retain collapsed diagnostic detail.");
                ((IButtonController)Element<Button>("karma-completion-details")).SendClicked();
                Require(sources.All(label => label.IsVisible)
                    && review.OrderedDeltas.All(delta => sources.Any(label => label.Text!.Contains(delta.TargetId, StringComparison.Ordinal))),
                    "Technical details must retain the original IDs without substituting the reviewed command.");
                ((IButtonController)Element<Button>("karma-completion-details")).SendClicked();
                Require(sources.All(label => !label.IsVisible) && probe!.FinalConfirmCalls == 0
                    && System.Text.Json.JsonSerializer.Serialize(review) == sealedReview,
                    "Displaying or hiding details changed the sealed review or finalized the runner.");
                RequireSameRewardDocument(saved, new FileWorkspaceStore(runtime.StateDirectory).Get(id).Value!);
                Console.WriteLine("PASS Karma readable completion: bound names, hidden technical IDs, explicit dice help, contrast, unchanged review and saved bytes");
            }

            NativePageBase Current() => (NativePageBase)navigation.Navigation.NavigationStack.Last();
            CreationKarmaPhoneSession Session() => (CreationKarmaPhoneSession)typeof(CreationKarmaPage)
                .GetField("_session", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .GetValue(Current())!;
            T Element<T>(string automationId) where T : Element
                => IssuedElements(Current()).OfType<T>().Single(e => e.AutomationId == automationId);
            void AssertPrerequisite(string step, string? expected)
            {
                string buttonId = "karma-open-" + step;
                var hints = IssuedElements(Current()).OfType<Label>()
                    .Where(label => label.AutomationId == buttonId + "-prerequisite").ToArray();
                Require(Element<Button>(buttonId).IsEnabled == (expected is null),
                    "Unexpected prerequisite navigation state: " + step);
                Require(expected is null ? hints.Length == 0 : hints.Length == 1 && hints[0].Text == expected,
                    "Missing, stale, or incorrect prerequisite hint: " + step);
            }
            async Task Appear()
            {
                var page = Current();
                if (IssuedPageField<int>(page, "_subscribed") == 0)
                    await ui.BeginAsyncVoid(() => IssuedPageLifecycle(page, "OnAppearing"));
            }
            async Task Click(string automationId)
            {
                var previous = Current();
                var button = Element<Button>(automationId);
                Require(button.IsEnabled, "Button disabled: " + automationId);
                await ui.BeginAsyncVoid(() => ((IButtonController)button).SendClicked());
                if (!ReferenceEquals(previous, Current()) && IssuedPageField<int>(previous, "_subscribed") != 0)
                    IssuedPageLifecycle(previous, "OnDisappearing");
                await Appear();
            }
            async Task Back()
            {
                IssuedPageLifecycle(Current(), "OnDisappearing");
                await navigation.PopAsync(false);
                await Appear();
            }
            async Task Search(string term)
            { ReadableSearch("karma-skill-search").Text = "  " + term + "  "; await Click("karma-search"); }
            async Task QualitySearch(string term)
            { ReadableSearch("karma-quality-search").Text = "  " + term + "  "; await Click("karma-quality-search-go"); }
            SearchBar ReadableSearch(string id)
            {
                var search = Element<SearchBar>(id);
                Require(search.TextColor == NativeTheme.Text && search.BackgroundColor == NativeTheme.Surface
                    && search.PlaceholderColor == NativeTheme.Muted && search.CancelButtonColor == NativeTheme.Text,
                    "Karma catalog search must explicitly pair readable text, placeholder and cancel colors with its light surface: " + id);
                return search;
            }
        });
    }

    private static async Task RunKarmaPhoneRevalidationAsync(string contentRoot)
    {
        using var ui = new IssuedPageUiContext();
        await ui.RunAsync(async () =>
        {
            var owners = new ControlledLinkedOwner();
            KarmaNativeProbe? probe = null;
            await using var runtime = new NativeRewardRuntime(contentRoot, creationBootstrap: true,
                productionCreationOverview: true, linkedOwners: owners,
                karmaDecorator: actual => probe = new(actual, ui));
            await runtime.Coordinator.InitializeAsync();
            await AccountStartupTask(runtime.Coordinator).WaitAsync(TimeSpan.FromSeconds(10));
            await runtime.Coordinator.CreateRunnerAsync();
            await runtime.Presenter.UpdateDialogFieldAsync("newCharacterBuildMethod", "Karma", default);
            await runtime.Coordinator.ExecuteDialogActionAsync("create_character");
            var id = runtime.Coordinator.State.WorkspaceId!.Value;
            var before = new FileWorkspaceStore(runtime.StateDirectory).Get(id).Value!;
            var session = new CreationKarmaPhoneSession(runtime.Coordinator);
            await session.ReloadAsync(false, default, () => true);
            var state = session.Authority!;
            var draft = new CreationKarmaPhoneSelection(state.Options.Single(o => o.Label == "Human").OptionId,
                "mundane", [], ResourceKarmaInvestment: 10.5m);
            session.Change(draft);
            await session.PreviewAsync(default, () => true);
            var firstQuote = session.Quote;
            int loads = probe!.LoadCalls, previews = probe.PreviewCalls;
            await session.ReloadAsync(false, default, () => true);
            Require(session.QuoteCurrent && !ReferenceEquals(firstQuote, session.Quote)
                && ReferenceEquals(state, session.Authority)
                && probe.LoadCalls == loads && probe.PreviewCalls == previews + 1,
                "Revisit reused an old quote or redundantly loaded before fresh Core revalidation.");

            session.Change(draft with { Attributes = [new("AGI", 99)] });
            await session.ReloadAsync(false, default, () => true);
            Require(session.Ready && session.QuoteCurrent && session.Quote is { CanSelect: false }
                && probe.LoadCalls == loads,
                "An invalid allocation must remain editable with a freshly validated blocked quote.");
            session.Change(draft);
            await session.ReloadAsync(false, default, () => true);
            Require(session.QuoteCurrent && session.Quote!.CanSelect, "Valid edit failed to recover the draft.");

            probe.FailReads = true;
            await session.ReloadAsync(false, default, () => true);
            await session.ConfirmAsync(default, () => true);
            Require(!session.Ready && !session.QuoteCurrent && probe.ConfirmCalls == 0
                && probe.LoadCalls == loads + 1 && session.Blockers.Contains(CharacterCreationKarmaMetatypeBlockers.StaleBinding),
                "A failed fresh read fell back to cached source or permitted confirmation.");
            probe.FailReads = false;
            await session.ReloadAsync(false, default, () => true);
            await session.PreviewAsync(default, () => true);
            Require(session.QuoteCurrent && session.Selection is { TalentOptionId: "mundane", Attributes.Count: 0 }
                && session.Selection.MetatypeOptionId == draft.MetatypeOptionId,
                "Successful reread lost the unsaved selection or failed to reissue the review.");

            bool current = true;
            probe.AfterPreview = () => current = false;
            await session.ReloadAsync(false, default, () => current);
            Require(!session.QuoteCurrent, "A departed page admitted a completed review.");
            current = true;
            probe.AfterPreview = null;
            await session.ReloadAsync(false, default, () => current);
            Require(session.QuoteCurrent, "Returning to the original frame did not freshly revalidate.");

            probe.AfterPreview = () => { owners.Set(ContactsOwnerB); owners.Set(Chummer.Contracts.Owners.OwnerScope.LocalSingleUser); };
            await session.ReloadAsync(false, default, () => current);
            await session.ConfirmAsync(default, () => current);
            var after = new FileWorkspaceStore(runtime.StateDirectory).Get(id).Value!;
            Require(!session.Ready && !session.QuoteCurrent && probe.ConfirmCalls == 0
                && after.ContentRevision == before.ContentRevision
                && after.Document.AuxiliaryStateDigest == before.Document.AuxiliaryStateDigest
                && owners.ActiveLeases == 0,
                "Owner A→B→A admitted cached authority, changed state or leaked a lease.");
            ui.AssertHealthy();
            Console.WriteLine("PASS Karma phone revalidation: one fresh Preview, editable invalid draft, failed source, departed page, owner ABA, no writes");
        });
    }
}
