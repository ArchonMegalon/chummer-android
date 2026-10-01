using System.Reflection;
using System.Globalization;
using System.Text.RegularExpressions;
using Chummer.Android.Native;
using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Infrastructure.Workspaces;
using Chummer.Presentation.Overview;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using Microsoft.Maui.Controls;

internal static partial class AfterRunAuthorityHarness
{
    private static void VerifyQualitySummaryContent(string contentRoot)
    {
        var catalog = System.Xml.Linq.XDocument.Load(Path.Combine(contentRoot, "data", "qualities.xml"))
            .Root!.Element("qualities")!.Elements("quality").ToArray();
        var oldCulture = CultureInfo.CurrentUICulture;
        try
        {
            foreach (string locale in new[] { "en-GB", "de-AT", "es-MX" })
            {
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(locale);
                int authored = 0, missing = 0, partial = 0;
                foreach (var quality in catalog)
                {
                    string summary = CreationFlowStrings.Get("Qualities.Summary." + quality.Element("id")!.Value, "");
                    if (summary.Length > 0) authored++;
                    var lines = CreationQualityInfo.Effects(quality.ToString());
                    Require(lines.Count > 0 && lines.All(line => !string.IsNullOrWhiteSpace(line)),
                        "Every catalog entry needs an explanation or an honest missing-description state.");
                    string text = string.Join(" ", lines);
                    Require(!Regex.IsMatch(text, @"[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}", RegexOptions.IgnoreCase)
                        && !text.Contains("rulebook", StringComparison.OrdinalIgnoreCase)
                        && !text.Contains("Regelbuch", StringComparison.OrdinalIgnoreCase)
                        && !text.Contains("consulta el manual", StringComparison.OrdinalIgnoreCase),
                        "Quality information exposed a machine identity or deferred to a book.");
                    if (lines.Contains(CreationFlowStrings.Get("Qualities.Info.Manual", ""))) missing++;
                    if (lines.Contains(CreationFlowStrings.Get("Qualities.Info.Additional", ""))) partial++;
                    if (summary.Length > 0) Require(lines[0] == summary, "The original summary must precede technical effects.");
                }
                Require(authored >= 59, "Localized source-identity summaries were not loaded from the real catalog.");
                string fractionalEssence = string.Join(" ", CreationQualityInfo.Effects(
                    "<quality><bonus><essencepenaltyt100>-150</essencepenaltyt100></bonus></quality>"));
                Require(fractionalEssence.Contains((-1.5m).ToString(CultureInfo.CurrentUICulture))
                    && !fractionalEssence.Contains("-150"), "Hundredths must use readable, localized display units.");
                Console.WriteLine($"QUALITY_COPY locale={locale} catalog={catalog.Length} authored={authored} missing={missing} partial={partial}");
            }
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-GB");
            string Effect(string name) => string.Join(" ", CreationQualityInfo.Effects(
                catalog.Single(quality => quality.Element("name")!.Value == name).ToString()));
            Require(Effect("Catlike").Contains("Sneaking") && Effect("Catlike").Contains("Bonus: 2"),
                "Specific-skill modifiers must be shown, not silently replaced by generic copy.");
            Require(Effect("Exceptional Attribute").Contains("Maximum change: 1")
                && Effect("Exceptional Attribute").Contains("Except: Edge"),
                "Nested attribute choice must retain its maximum and Edge exclusion.");
            Require(Effect("Will to Live").Contains("Additional overflow boxes: 1"),
                "Overflow boxes must not be confused with damage resistance.");
            Require(Effect("Quick Healer").Contains("Heal") && Effect("Quick Healer").Contains("Modifier: 2"),
                "Spell-specific healing modifier was lost.");
            Require(Effect("Uneducated").Contains("Cannot default")
                && Effect("Uneducated").Contains("Percent of normal cost: 200")
                && Effect("Uneducated").Contains("Specialization Karma cost"),
                "Skill restrictions and double training costs must be distinguished from bonus dice.");
            Require(Effect("Jack of All Trades Master of None").Contains("After character creation")
                && Effect("Jack of All Trades Master of None").Contains("Maximum: 5")
                && Effect("Jack of All Trades Master of None").Contains("Minimum: 6")
                && !Effect("Jack of All Trades Master of None").Contains("/character/"),
                "Karma cost conditions and rating boundaries must be retained in readable language.");
            Require(Effect("Sensitive System").Contains("Cyberware Essence cost (% of normal): 200")
                && Effect("Sensitive System").Contains("Cannot use bioware"),
                "Essence multipliers and the bioware restriction were lost.");
            Require(Effect("Dependent (Nuisance)").Contains("Lifestyle cost change (%): 10"),
                "A lifestyle percentage must not be presented as nuyen or dice.");
            Require(Effect("Celerity").Contains("Replacement walking multiplier")
                && Effect("Celerity").Contains("Multiplier: 3")
                && Effect("Celerity").Contains("Replacement running multiplier")
                && Effect("Celerity").Contains("Multiplier: 6")
                && Effect("Celerity").Contains("Meters per hit: 1")
                && !Effect("Celerity").Contains("100"), "Replacement rates and extra sprint meters must not become 100 dice or a percentage.");
            Require(Effect("Satyr Legs").Contains("Meters per hit: 1")
                && !Effect("Satyr Legs").Contains("Replacement walking multiplier"),
                "Satyr Legs must not borrow Celerity's walking benefit.");
            Require(Effect("Consummate Professional").Contains("Extra earned Karma needed per Street Cred: 10")
                && Effect("Consummate Professional").Contains("per 20 Karma"),
                "Street Cred's extra Karma divisor is not a reputation multiplier.");
            Require(Effect("Resonant Burnout").Contains("Essence-related special-attribute loss (% of normal): 20"),
                "Reduced attribute loss must not be described as cheaper augmentation Essence.");
            Require(Effect("Barehanded Adept").Contains("Based on attribute: Magic")
                && Effect("Barehanded Adept").Contains("Half the rating, rounded up; touch range only")
                && Effect("Barehanded Adept").Contains("Permitted spell range: Touch (area)"),
                "Free spells must retain their Magic, rounding and Touch restrictions.");
            Require(Effect("Dedicated Spellslinger").Contains("Based on skill: Spellcasting")
                && Effect("Dedicated Spellslinger").Contains("Unavailable skill: Summoning")
                && Effect("Dedicated Spellslinger").Contains("Unavailable skill: Binding"),
                "Skill-based free spells must retain their unavailable magic skills.");
            string deadSin = Effect("Dead SIN");
            Require(deadSin.Contains("Granted equipment · Fake SIN") && deadSin.Contains("Rating: 3")
                && Regex.Matches(deadSin, "Included equipment · Fake License").Count == 4,
                "A granted SIN and four identical licenses must retain all four child items and their ratings.");
            string banshee = Effect("Infected: Banshee");
            Require(banshee.Contains("Granted power: Dual Natured") && banshee.Contains("Granted power: Allergy")
                && banshee.Contains("Fixed detail: Sunlight, Severe")
                && banshee.Contains("Choose from these powers, not all of them")
                && banshee.Contains("Number of choices: 1") && banshee.Contains("Power option: Enhanced Senses")
                && banshee.Contains(CreationFlowStrings.Get("Qualities.Info.Additional", "")),
                "Granted powers, selected details and optional choices must be distinguished; power names are not full rules.");
            Require(Effect("Electroception (Electrosense)").Contains("Additional specialization option, not automatically learned")
                && Effect("Electroception (Electrosense)").Contains("Specialization: Electroception")
                && Effect("Electroception (Electrosense)").Contains("Skill: Perception"),
                "A new specialization option must not be confused with automatically learning it.");
            string inspired(string id) => string.Join(" ", CreationQualityInfo.Effects(
                catalog.Single(quality => quality.Element("id")!.Value == id).ToString()));
            Require(inspired("fd9b9b6d-c969-40f1-8dc7-61f8e5d9cd4d").Contains("Choose a free expertise specialization")
                && !inspired("f8f216b5-1c29-467d-9fb5-c9812408203d").Contains("Choose a free expertise specialization"),
                "Same-name qualities must not share source-identity summaries or expertise effects.");
            string Describe(string bonus) => string.Join(" ", CreationQualityInfo.Effects("<quality><bonus>" + bonus + "</bonus></quality>"));
            string powers = Describe("<critterpowers><power rating='2'>Armor</power><power select='Fire'>Immunity</power></critterpowers>");
            Require(powers.Contains("Granted power: Armor · Rating: 2") && powers.Contains("Fixed detail: Fire"),
                "Granted power ratings and fixed selections cannot be dropped.");
            string optional = Describe("<optionalpowers count='2'><optionalpower>Armor</optionalpower><optionalpower>Fear</optionalpower></optionalpowers>");
            Require(optional.Contains("Number of choices: 2") && !optional.Contains("Granted power:"),
                "Optional power count is a choice count, not a grant of every listed power.");
            string gearPrice = Describe("<addgear><name>Test item</name><rating>Rating + 1</rating><quantity>2</quantity><fullcost /></addgear>");
            Require(gearPrice.Contains("Pay full price") && gearPrice.Contains("Quantity: 2")
                && gearPrice.Contains("Rating: Rating + 1") && !gearPrice.Contains("No nuyen cost"),
                "Full-cost grants must not be described as free; retain quantity and symbolic rating.");
            string hiddenGearCondition = Describe("<addgear><name>Test item</name><children><child><name>Child</name><futurecondition>unknown</futurecondition></child></children></addgear>");
            Require(hiddenGearCondition.Contains(CreationFlowStrings.Get("Qualities.Info.Additional", "")),
                "Unknown conditions on included equipment must keep the whole explanation partial.");
            string deeperGear = Describe("<addgear><name>Parent</name><children><child><name>Child</name><children><child><name>Not an admitted grandchild</name></child></children></child></children></addgear>");
            Require(!deeperGear.Contains("Not an admitted grandchild")
                && deeperGear.Contains(CreationFlowStrings.Get("Qualities.Info.Additional", "")),
                "Display must not grant recursively nested equipment beyond Core's immediate-child contract.");
            string unresolved = Describe("<addqualities><addquality>00000000-0000-0000-0000-000000000001</addquality></addqualities>");
            Require(!unresolved.Contains("00000000") && unresolved.Contains(CreationFlowStrings.Get("Qualities.Info.Additional", "")),
                "An unresolved quality reference stays hidden and partial, not apparently complete.");
            string specialization = Describe("<selectexpertise limittoskill='Artisan' limittospecialization='Painting,Sculpture' />");
            Require(specialization.Contains("Choose from: Artisan") && specialization.Contains("Choose specialization from: Painting,Sculpture"),
                "Expertise must preserve both skill and specialization restrictions.");
            string sprint = Describe("<movementreplace><category>Fly</category><speed>sprint</speed><val>500</val></movementreplace>");
            Require(sprint.Contains("Replacement sprint distance") && sprint.Contains("Category: Flying")
                && sprint.Contains("Meters per hit: 5") && !sprint.Contains("500"), "Replacement sprint distance is also stored in hundredths.");
            string noCategory = Describe("<movementreplace><val>3</val></movementreplace>");
            Require(noCategory.Contains("Replacement walking multiplier") && noCategory.Contains("All movement types"),
                "A replacement without explicit speed/category defaults to walking for all movement types.");
            string mixedUnits = Describe("<sprintbonus><category>Ground</category><val>50</val><percent>25</percent></sprintbonus>");
            Require(mixedUnits.Contains("Meters per hit: 0.5") && mixedUnits.Contains("Percentage change: 25"),
                "Only the sprint-distance value uses hundredths; percentage changes must remain unchanged.");
            string symbolic = Describe("<sprintbonus><category>Ground</category><val>Rating * 100</val></sprintbonus>");
            Require(symbolic.Contains("Meters per hit: (Rating * 100) / 100"),
                "Symbolic unit conversion must remain an unevaluated expression.");
            string crystal = Describe("<essencepenaltyt100>-150</essencepenaltyt100><essencepenaltymagonlyt100>150</essencepenaltymagonlyt100>");
            Require(crystal.Contains("Essence change: -1.5") && crystal.Contains("Essence adjustment for Magic loss only: 1.5"),
                "Magic-specific Essence adjustment is not a grant of Magic attribute points.");
            string legacyInitiative = Describe("<initiativepass precedence='0'>1</initiativepass>");
            Require(legacyInitiative.Contains("Initiative dice: 1")
                && legacyInitiative.Contains(CreationFlowStrings.Get("Qualities.Info.Additional", "")),
                "Legacy initiative tag means dice, but unexplained stacking precedence remains partial.");
            Require(Describe("<movementreplace><speed>unknown</speed><val>500</val></movementreplace>")
                == CreationFlowStrings.Get("Qualities.Info.Manual", ""), "Unknown movement types must not be guessed as walking.");
            string fading = Describe("<fadingvalue specific='Resonance Spike'>-2</fadingvalue>");
            Require(fading.Contains("Fading value change: -2") && fading.Contains("Only for: Resonance Spike"),
                "A targeted Fading modifier must not appear to apply to every complex form.");
            string limit = Describe("<limitmodifier><limit>Mental</limit><value>1</value><condition>LimitCondition_SkillsKnowledgeAcademic</condition></limitmodifier>");
            Require(limit.Contains("Limit: Mental") && limit.Contains("Modifier: 1")
                && limit.Contains("Academic Knowledge") && !limit.Contains("LimitCondition_"),
                "Limit values and translated conditions were omitted.");
            string restricted = Describe("<selectskill minimumrating='4' limittoskill='Hacking'><val>-2</val><disablespecializationeffects /></selectskill>");
            Require(restricted.Contains("Minimum skill rating: 4") && restricted.Contains("Choose from: Hacking")
                && restricted.Contains("Specialization bonuses do not apply"), "Skill-choice restrictions were lost.");
            string partialText = Describe("<specificskill><name>Perception</name><bonus>1</bonus><futurecondition>unknown</futurecondition></specificskill>");
            Require(partialText.Contains("Bonus: 1") && partialText.Contains(CreationFlowStrings.Get("Qualities.Info.Additional", "")),
                "An unknown field must produce a partial-description warning instead of disappearing silently.");
            string duplicatePartial = Describe("<specificskill><name>Perception</name><bonus>1</bonus></specificskill>"
                + "<specificskill><name>Perception</name><bonus>1</bonus><futurecondition>unknown</futurecondition></specificskill>");
            Require(duplicatePartial.Contains(CreationFlowStrings.Get("Qualities.Info.Additional", "")),
                "Text deduplication must not discard an unknown condition on the second effect.");
            string conditionalLifestyle = Describe("<lifestylecost lifestyle='Low' condition='once'>10</lifestylecost>");
            Require(conditionalLifestyle.Contains("Lifestyle: Low") && conditionalLifestyle.Contains("When: One-time cost"),
                "Lifestyle scope and one-time conditions must remain visible.");
            string expression = string.Join(" ", CreationQualityInfo.Effects(
                "<quality><bonus><specificskill><name>Test skill</name><bonus>Rating + 1</bonus><condition>Only at night</condition></specificskill></bonus></quality>"));
            Require(expression.Contains("Rating + 1") && expression.Contains("Only at night"),
                "Read-only help must preserve expressions and conditions without evaluating rules.");
            string unknown = string.Join(" ", CreationQualityInfo.Effects(
                "<quality><name>Catlike</name><id>00000000-0000-0000-0000-000000000001</id><bonus><unrecognized /></bonus></quality>"));
            Require(unknown == CreationFlowStrings.Get("Qualities.Info.Manual", ""),
                "An unknown source identity must not borrow another quality's prose from its display name.");
            try
            {
                CreationQualityInfo.Effects("<!DOCTYPE quality [<!ENTITY x 'unsafe'>]><quality>&x;</quality>");
                throw new InvalidOperationException("Quality help accepted a DTD.");
            }
            catch (System.Xml.XmlException) { }
        }
        finally { CultureInfo.CurrentUICulture = oldCulture; }
    }

    internal static async Task RunCreationQualityDetailsAsync(string contentRoot, string? smokeWorkspacePath = null)
    {
        VerifyQualitySummaryContent(contentRoot);
        using var ui = new IssuedPageUiContext();
        await ui.RunAsync(async () =>
        {
            var owners = new ControlledLinkedOwner();
            await using var runtime = new NativeRewardRuntime(contentRoot, linkedOwners: owners,
                creationFinalization: true, productionCreationOverview: true);
            var before = PrepareActualFinalizationReadyContext(runtime, stopBeforeQualities: true);
            if (smokeWorkspacePath is not null)
            {
                Require(Path.IsPathFullyQualified(smokeWorkspacePath), "Use an explicit private smoke fixture path.");
                // Preserve unmodified real Core output for the isolated AVD's
                // affected-route smoke, not a claim of UI-driven earlier steps.
                File.Copy(Path.Combine(runtime.StateDirectory, "workspaces", runtime.Id.Value + ".json"),
                    smokeWorkspacePath, overwrite: false);
                Console.WriteLine("QUALITY_SMOKE_WORKSPACE " + runtime.Id.Value);
            }
            await HydrateFinalizationOwnerAsync(runtime, owners, before);
            var coordinator = runtime.Coordinator;
            var original = coordinator.State;
            var loaded = await coordinator.LoadCreationQualitiesForDisplayAsync(original, default);
            var state = loaded.Value ?? throw new InvalidOperationException("SETUP: quality authority missing.");
            var editor = CreationQualitiesPhoneAuthority.ProjectEditor(state, original);
            var draft = new CreationQualitiesPhoneDraft();
            draft.Bind(state, original);
            var option = draft.AvailableOptions(state, original, editor, default)
                .Where(item => !item.IsMetagenic)
                .OrderByDescending(item => !string.IsNullOrWhiteSpace(item.FollowUpChoiceLabel)).First();
            var configure = new CreationQualityConfigurePage(coordinator, state, editor, option, draft, original);
            var previewResult = coordinator.PreviewCreationQualities(state.Binding, [option.OptionId], original);
            Require(draft.TryAdopt(state, original, previewResult, [option.OptionId])
                && previewResult.Value is { CanConfirm: true }, "SETUP: selected quality must have a real confirmable preview.");
            var preview = previewResult.Value!;
            var checkpoint = CharacterCreationQualitiesCheckpoint.CreateReviewed(preview, [option.OptionId], Guid.NewGuid());
            var store = CharacterCreationQualitiesCheckpointStore.CreateDefault(original.DisplayOwnerContext,
                coordinator.IsCreationQualitiesOwnerCurrent);
            Require(store.TryCreate(checkpoint, out checkpoint, out string blocker), blocker);
            var review = new CreationQualitiesReviewPage(coordinator, checkpoint, store, original);
            var oldCulture = CultureInfo.CurrentUICulture;
            try
            {
                foreach (string locale in new[] { "en-GB", "de-AT", "es-MX" })
                {
                    CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(locale);
                    VerifyQualityDisclosure(configure, "creation-quality-configure-technical-details",
                        "creation-quality-configure-toggle", option.OptionId, option.SourceId.ToString("D"));
                    string configureText = MinimalVisibleText(configure);
                    Require(configureText.Contains(option.Name) && configureText.Contains(CreationQualitiesPage.Signed(option.KarmaCost))
                        && (string.IsNullOrWhiteSpace(option.FollowUpChoiceLabel) || configureText.Contains(option.FollowUpChoiceLabel)),
                        "Configure lost the quality name, exact cost or readable follow-up.");
                    VerifyQualityDisclosure(review, "creation-qualities-review-technical-details",
                        "creation-qualities-confirm-draft", checkpoint.TransactionId.ToString("D"), preview.PreviewDigest,
                        preview.AuthorityDigest, preview.Binding.RawCharacterXmlDigest, preview.Binding.AuxiliaryStateDigest,
                        option.OptionId, option.SourceId.ToString("D"));
                    Require(MinimalVisibleText(review).Contains(option.Name)
                        && MinimalVisible(review).OfType<Button>().Single(button => button.AutomationId == "creation-qualities-confirm-draft").Text
                            == CreationFlowStrings.Get("Qualities.Review.Confirm", "missing"),
                        "Review lost the selected name or localized save action.");
                    var emptyPreview = coordinator.PreviewCreationQualities(state.Binding, [], original).Value!;
                    var empty = new CreationQualitiesReviewPage(coordinator,
                        CharacterCreationQualitiesCheckpoint.CreateReviewed(emptyPreview, [], Guid.NewGuid()), store, original);
                    MinimalRender(empty);
                    Require(MinimalVisibleText(empty).Contains(CreationFlowStrings.Get("Qualities.Review.Empty", "missing"))
                        && MinimalVisible(empty).OfType<Button>().Single(button => button.AutomationId == "creation-qualities-confirm-draft").IsEnabled,
                        "An empty valid review must explain that no additional qualities are selected.");
                    MinimalRequireNoMachineValues(empty);
                    foreach (string name in new[] { "Analytical Mind", "Catlike", "Unsteady Hands", "Aptitude" })
                    {
                        var helpOption = state.Authority.Options.First(item => item.Name == name);
                        var help = new CreationQualityInfoPage(coordinator, original, helpOption);
                        string helpText = MinimalVisibleText(help);
                        string expected = CreationFlowStrings.Get("Qualities.Summary." + helpOption.SourceId.ToString("D"), "");
                        Require(expected.Length > 40 && helpText.Contains(expected)
                            && !helpText.Contains("Rulebook", StringComparison.OrdinalIgnoreCase)
                            && !helpText.Contains("Regelbuch", StringComparison.OrdinalIgnoreCase)
                            && !helpText.Contains("página", StringComparison.OrdinalIgnoreCase),
                            "Quality help must contain the localized inline explanation, not a book citation.");
                        MinimalRequireNoMachineValues(help);
                    }
                }
                RequireSameRewardDocument(before, new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!);
                Require(store.TryRead(out var unchanged, out blocker) && unchanged.CheckpointDigest == checkpoint.CheckpointDigest,
                    "Rendering/disclosure mutated the durable review.");

                Require(store.TryBeginApply(CharacterCreationQualitiesCheckpointCas.From(checkpoint), out var applying, out blocker), blocker);
                var result = await coordinator.ConfirmCreationQualitiesAsync(applying, display: original);
                Require(result is { Receipt: not null, MutationOutcomeKnown: true, Outcome: CreationQualitiesPhoneOutcomes.Applied },
                    "Actual quality confirmation failed.");
                Require(store.TryRecordApplied(CharacterCreationQualitiesCheckpointCas.From(applying), result.Receipt!, out var applied, out blocker), blocker);
                var saved = new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!;
                Require(saved.ContentRevision == before.ContentRevision + 1 && saved.SavedRevision == saved.ContentRevision
                    && saved.Document.Content == before.Document.Content, "Quality confirmation must save one auxiliary revision, not live character effects.");
                await HydrateFinalizationOwnerAsync(runtime, owners, saved);
                var cold = coordinator.LoadCreationQualities().Value!;
                Require(CreationQualitiesPhoneAuthority.ReceiptMatchesPersistedState(applying, result.Receipt!, cold),
                    "Exact quality selection/receipt did not survive cold-store reopen.");
                var receipt = new CreationQualitiesReceiptPage(coordinator, applied, result.Receipt!, store);
                foreach (string locale in new[] { "en-GB", "de-AT", "es-MX" })
                {
                    CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(locale);
                    VerifyQualityDisclosure(receipt, "creation-qualities-receipt-technical-details",
                        "creation-qualities-receipt-acknowledge", result.Receipt!.TransactionId.ToString("D"),
                        result.Receipt.ReceiptDigest, result.Receipt.DraftDigest, result.Receipt.PlanDigest, result.Receipt.CommandDigest);
                    Require(MinimalVisibleText(receipt).Contains(CreationFlowStrings.Get("Qualities.Receipt.Safe", "missing"))
                        && MinimalVisible(receipt).OfType<Button>().Single(button => button.AutomationId == "creation-qualities-receipt-acknowledge").Text
                            == CreationFlowStrings.Get("Qualities.Receipt.Continue", "missing"),
                        "Saved quality confirmation lost localized continuation or pending-finalization guidance.");
                }
                RequireSameRewardDocument(saved, new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!);
                Require(store.TryRead(out var retained, out blocker) && retained.CheckpointDigest == applied.CheckpointDigest,
                    "Receipt disclosure acknowledged or changed the pending receipt.");
                Require(store.TryAcknowledgeApplied(CharacterCreationQualitiesCheckpointCas.From(applied), out blocker), blocker);
                var owner = owners.Current;
                owners.Set(ContactsOwnerB);
                owners.Set(owner);
                foreach (NativePageBase stale in new NativePageBase[] { configure, review, receipt })
                {
                    MinimalRender(stale);
                    Require(!MinimalVisible(stale).OfType<Button>().Any(), "Stale owner generation still exposes quality actions or diagnostics.");
                    MinimalRequireNoMachineValues(stale);
                }
                RequireSameRewardDocument(saved, new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!);
            }
            finally { CultureInfo.CurrentUICulture = oldCulture; }
            Console.WriteLine("PASS quality configure/review/receipt: EN/DE/ES, exact hidden diagnostics, stale controls, one save/cold reopen, no display mutations");
        });
    }

    private static void VerifyQualityDisclosure(NativePageBase page, string panelId, string actionId, params string[] exactValues)
    {
        MinimalRender(page);
        MinimalRequireNoMachineValues(page);
        Require(!MinimalVisibleText(page).Contains("CharacterDocumentChanged"), "Ordinary guidance leaks implementation jargon.");
        var body = (VerticalStackLayout)((ScrollView)page.Content!).Content!;
        var panel = body.Children.OfType<VerticalStackLayout>().Single(item => item.AutomationId == panelId);
        var toggle = panel.Children.OfType<Button>().Single();
        var action = MinimalVisible(page).OfType<Button>().Single(item => item.AutomationId == actionId);
        Require(action.IsEnabled && body.Children.IndexOf(action) < body.Children.IndexOf(panel)
            && toggle.Text == CreationFlowStrings.Get("Qualities.ShowDetails", "missing"),
            "Technical details displaced or disabled the primary action.");
        ((IButtonController)toggle).SendClicked();
        Require(exactValues.All(value => MinimalVisibleText(page).Contains(value, StringComparison.Ordinal))
            && toggle.Text == CreationFlowStrings.Get("Qualities.HideDetails", "missing"),
            "Expanded quality diagnostics lost exact values or localization.");
        ((IButtonController)toggle).SendClicked();
        MinimalRequireNoMachineValues(page);
        MinimalRender(page);
        ((IButtonController)toggle).SendClicked();
        MinimalRequireNoMachineValues(page);
        Require(!((VisualElement)panel.Children[1]).IsVisible,
            "A detached quality disclosure reopened its old data after refresh.");
    }

    internal static async Task RunMinimalUiAsync(string contentRoot)
    {
        using var ui = new IssuedPageUiContext();
        await ui.RunAsync(async () =>
        {
            foreach (var (locale, saveGear, saveBudget) in new[]
            {
                ("en-GB", "Save equipment", "Save budget"),
                ("de-AT", "Ausrüstung speichern", "Budget speichern"),
                ("es-MX", "Guardar equipo", "Guardar presupuesto")
            })
            {
                var localized = AndroidSurfaceStrings.Resolve(locale);
                Require(localized["GearPreview.Confirm"] == saveGear
                    && localized["ResourcesPreview.Confirm"] == saveBudget,
                    "Creation confirmation must describe the action in the player's language.");
                foreach (var key in new[] { "Resources.CurrentBudget", "Resources.CoreAuthority",
                    "Gear.DraftBasket", "Gear.ActiveCatalog", "GearPreview.ExactProjection" })
                    Require(!Regex.IsMatch(localized[key], "Core|XML|authority|autoridad|Autorität",
                        RegexOptions.IgnoreCase), "Player-facing headings expose implementation jargon.");
            }
            const string id = "c49a893a-d445-4aac-bec0-c8501cba4c2c";
            var label = NativeTheme.Body(id);
            var details = NativeTheme.TechnicalDetails(label, "test-diagnostics");
            var root = new VerticalStackLayout { details };
            var toggle = details.Children.OfType<Button>().Single();
            Require(!label.IsVisible && !MinimalVisibleText(root).Contains(id),
                "Diagnostics are exposed by default.");
            ((IButtonController)toggle).SendClicked();
            Require(label.IsVisible && label.Text == id && MinimalVisibleText(root).Contains(id),
                "Explicit troubleshooting lost the exact value.");
            ((IButtonController)toggle).SendClicked();
            Require(!label.IsVisible, "Diagnostics cannot be collapsed.");
            root.Clear();
            ((IButtonController)toggle).SendClicked();
            Require(!label.IsVisible, "A detached disclosure reopened old diagnostics.");
            Require(NativeTheme.Body(id).Text == id && NativeTheme.BookProse(id).Text == id,
                "Minimalism must not rewrite arbitrary player text or prose.");
            Require(NativeTheme.Body("Readable").FontSize >= 15
                && NativeTheme.BookProse("Chapter").FontSize >= 18
                && NativeTheme.PrimaryButton("Continue").HeightRequest >= 48,
                "Simplification shrank readable text or touch targets.");
            var overlay = NativeAuthoritySemantics.Overlay(NativeTheme.Body("Saved"),
                NativeAuthoritySemantics.Identifier("test-machine-id", id));
            Require(!MinimalVisibleText(overlay).Contains(id),
                "Ordinary-build TalkBack exposes invisible machine values.");

            var owners = new ControlledLinkedOwner();
            await using var runtime = new NativeRewardRuntime(contentRoot, linkedOwners: owners,
                creationFinalization: true, productionCreationOverview: true, creationPrerequisite: true);
            var before = PrepareActualFinalizationReadyContext(runtime);
            await HydrateFinalizationOwnerAsync(runtime, owners, before);
            AssertCreationReadinessCopy(runtime.Coordinator);
            var currentCulture = CultureInfo.CurrentUICulture;
            try
            {
                foreach (var (locale, words) in new[] { ("en-GB", "saved Attributes"),
                             ("de-AT", "gespeicherten Attribute"), ("es-MX", "Atributos guardados") })
                {
                    CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(locale);
                    const string locked = "creation-prerequisite-dependent-attributes-draft-exists";
                    string message = CreationFlowStrings.DashboardBlocker(locked);
                    Require(message.Contains(words) && message == CreationFlowStrings.Get("Dashboard.MethodLocked", "missing"),
                        "Locked method lacks localized dependent-Attributes guidance.");
                    Require(CreationFlowStrings.DashboardBlocker("fixture-unknown-9d74b5ce")
                        == CreationFlowStrings.Get("Dashboard.StepBlocked", "missing"),
                        "Unknown codes must stay in diagnostics rather than leak into ordinary guidance.");
                }
            }
            finally { CultureInfo.CurrentUICulture = currentCulture; }
            var lockedMethod = await runtime.Coordinator.LoadCreationPrerequisiteAsync();
            Require(lockedMethod.Blockers.Concat(lockedMethod.Value?.Blockers ?? []).Contains(
                "creation-prerequisite-dependent-attributes-draft-exists"),
                "SETUP: saved Attributes must actually lock prerequisite edits.");
            var dashboard = new BuildPage(runtime.Coordinator);
            var snapshot = runtime.Coordinator.State.CreationWizard!;
            Require(CreationDashboardProjectionBinding.TryCreate(runtime.Coordinator.State, snapshot, out var binding),
                "SETUP: current dashboard binding missing.");
            var projection = CreationDashboardAuthorityProjection.Loading(binding!) with
            {
                Prerequisite = lockedMethod,
                Progress = CreationDashboardAuthorityPhaseProgress.ForBuildMethod(snapshot.BuildMethod) with
                { Prerequisite = CreationDashboardAuthorityPhaseState.Ready }
            };
            var methodRoute = (CreationBudgetRoute)typeof(BuildPage).GetMethod("AddCreationMethodRoute",
                BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(dashboard, [snapshot, projection, lockedMethod])!;
            Require(!methodRoute.CanOpen && methodRoute.Detail == CreationFlowStrings.DashboardBlocker(
                "creation-prerequisite-dependent-attributes-draft-exists")
                && methodRoute.Blockers.SequenceEqual(["creation-prerequisite-dependent-attributes-draft-exists"])
                && !MinimalVisible(dashboard).OfType<Button>().Single(x =>
                    x.AutomationId == "creation-stage-method").IsEnabled,
                "Plain locked-method guidance changed readiness or gave a misleading Karma reason.");
            var header = new VerticalStackLayout();
            bool hasPicker = (bool)typeof(BuildPage).GetMethod("AddWorkspacePicker",
                BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(dashboard, [header])!;
            Require(hasPicker == (runtime.Coordinator.State.OpenWorkspaces.Count > 1)
                && (!hasPicker || header.Children.OfType<Picker>().Single().AutomationId == "build-workspace-picker"),
                "Dashboard heading must retain the workspace selector only when needed.");
            var actual = new CharacterCreationGearInteractionPresenter(
                runtime.Services.GetRequiredService<ICharacterCreationGearService>(),
                runtime.Services.GetRequiredService<IOwnerBoundCharacterCreationGearService>());
            // Priority editing is intentionally locked once dependent stages
            // exist. Use a real new runner, not the ready-for-Gear fixture.
            await using var priorityRuntime = new NativeRewardRuntime(contentRoot,
                linkedOwners: owners, creationPrerequisite: true, creationAttributes: true,
                productionCreationOverview: true);
            await priorityRuntime.Coordinator.InitializeAsync();
            await AccountStartupTask(priorityRuntime.Coordinator).WaitAsync(TimeSpan.FromSeconds(10));
            var prioritySeed = PreparePrerequisiteOwnerFixture(priorityRuntime);
            await HydrateFinalizationOwnerAsync(priorityRuntime, owners, prioritySeed);
            var prerequisite = (await priorityRuntime.Coordinator.LoadCreationPrerequisiteAsync()).Value
                ?? throw new InvalidOperationException("SETUP: real prerequisite authority unavailable.");
            var (assignments, selections) = PrerequisiteSelections(prerequisite);
            var assignmentsPreview = (await priorityRuntime.Coordinator.PreviewCreationPrerequisiteAsync(
                prerequisite.Binding, assignments, selections)).Value!;
            var assignmentReview = new CreationPrerequisitePreviewPage(priorityRuntime.Coordinator,
                assignmentsPreview, assignments, selections, CharacterCreationBuildMethods.Priority);
            await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => IssuedPageLifecycle(assignmentReview, "OnAppearing")));
            MinimalRequireNoMachineValues(assignmentReview);
            MinimalRequireFreshDisclosure(assignmentReview);
            Require(MinimalVisibleText(assignmentReview).Contains(assignmentsPreview.TalentSelection!.Name)
                && MinimalVisible(assignmentReview).OfType<Button>().Single(x =>
                    x.AutomationId == "creation-prerequisite-confirm").IsEnabled,
                "Readable assignments lost the actual Talent or exact confirmation.");
            ((IButtonController)MinimalVisible(assignmentReview).OfType<Button>().Single(x =>
                x.AutomationId == "creation-prerequisite-preview-details-toggle")).SendClicked();
            Require(MinimalVisibleText(assignmentReview).Contains(assignmentsPreview.PreviewDigest),
                "Review diagnostics must retain the exact preview digest.");
            IssuedPageLifecycle(assignmentReview, "OnDisappearing");
            var priorities = new CreationPrerequisitePage(priorityRuntime.Coordinator, prerequisite);
            MinimalRender(priorities);
            MinimalRequireNoMachineValues(priorities);
            Require(MinimalVisible(priorities).OfType<Button>().Any(x =>
                    x.AutomationId == "creation-prerequisite-prepare-preview"),
                "Minimal Priorities hid its review action.");
            var draft = new CreationPrerequisitePhoneDraft();
            draft.Bind(prerequisite, priorityRuntime.Coordinator.State);
            var category = new CreationPriorityCategoryPage(priorityRuntime.Coordinator, draft, prerequisite,
                CharacterCreationPriorityCategoryIds.Attributes);
            MinimalRender(category);
            MinimalRequireNoMachineValues(category);
            Require(MinimalVisible(category).OfType<Button>().Count(x =>
                    x.AutomationId?.StartsWith("creation-prerequisite-rank-") == true) == 5,
                "Minimal rank list removed choices or their unavailability explanations.");
            Require(draft.TrySelect(prerequisite, priorityRuntime.Coordinator.State,
                CharacterCreationPriorityCategoryIds.Heritage, "A"), "SETUP: Heritage rank unavailable.");
            var heritage = new CreationPriorityDetailPage(priorityRuntime.Coordinator, draft, prerequisite,
                CharacterCreationPriorityCategoryIds.Heritage);
            MinimalRender(heritage);
            MinimalRequireNoMachineValues(heritage);
            Require(MinimalVisible(heritage).OfType<Button>().Any(x =>
                x.AutomationId?.StartsWith("creation-prerequisite-heritage-option-") == true),
                "Minimal Heritage list has no actual choices.");
            Require(draft.TrySelect(prerequisite, priorityRuntime.Coordinator.State,
                CharacterCreationPriorityCategoryIds.Talent, "B"), "SETUP: Talent rank unavailable.");
            var talent = new CreationPriorityDetailPage(priorityRuntime.Coordinator, draft, prerequisite,
                CharacterCreationPriorityCategoryIds.Talent);
            MinimalRender(talent);
            MinimalRequireNoMachineValues(talent);
            Require(MinimalVisible(talent).OfType<Button>().Any(x =>
                x.AutomationId?.StartsWith("creation-prerequisite-talent-option-") == true),
                "Minimal Talent list has no actual choices.");
            foreach (var grantedTalent in draft.TalentOptions(prerequisite, priorityRuntime.Coordinator.State)
                .Where(option => option.IsEnabled && option.Blockers.Count == 0
                    && (option.ActiveSkillGrant is not null || option.SkillGroupGrant is not null)
                    && CreationPrerequisitePhoneAuthority.IsTalentGrantAuthoritySupported(option)))
            {
                Require(draft.TrySelectTalent(prerequisite, priorityRuntime.Coordinator.State, grantedTalent.SelectionId),
                    "SETUP: supported granted-skill Talent cannot be selected.");
                var grantPage = new CreationTalentSkillGrantPage(priorityRuntime.Coordinator, draft, prerequisite, grantedTalent.SelectionId);
                MinimalRender(grantPage);
                MinimalRequireNoMachineValues(grantPage);
                MinimalRequireFreshDisclosure(grantPage);
                Require(MinimalVisibleText(grantPage).Contains(grantedTalent.Name)
                    && MinimalVisibleText(grantPage).Contains("Granted rating"),
                    "Talent skill choices lost the readable Talent or actual granted rating.");
                ((IButtonController)MinimalVisible(grantPage).OfType<Button>().Single(button =>
                    button.AutomationId == "creation-prerequisite-talent-grant-details-toggle")).SendClicked();
                Require(MinimalVisibleText(grantPage).Contains(grantedTalent.ActiveSkillGrant?.GrantDigest
                    ?? grantedTalent.SkillGroupGrant!.GrantDigest), "Talent diagnostics lost the exact grant digest.");
            }
            var confirmedAssignments = await priorityRuntime.Coordinator.ConfirmCreationPrerequisiteAsync(
                assignmentsPreview, assignments, selections);
            Require(confirmedAssignments.Outcome == CharacterCreationFoundationOutcomes.Success,
                "SETUP: prerequisite confirmation failed.");
            var attributesAuthority = priorityRuntime.Coordinator.LoadCreationAttributes().Value
                ?? throw new InvalidOperationException("SETUP: Attribute authority unavailable.");
            Require(CreationAttributesPhoneAuthority.IsReady(
                attributesAuthority, priorityRuntime.Coordinator.State), "SETUP: editable Attribute authority unavailable.");
            var attributesPage = new CreationAttributesPage(priorityRuntime.Coordinator, attributesAuthority);
            await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => IssuedPageLifecycle(attributesPage, "OnAppearing")));
            var scroll = (ScrollView)attributesPage.Content!;
            Element? scrollTarget = null;
            ((IScrollViewController)scroll).ScrollToRequested += (_, request) =>
            {
                scrollTarget = request.Element;
                ((IScrollViewController)scroll).SendScrollFinished();
            };
            var jump = MinimalVisible(attributesPage).OfType<Button>().Single(x =>
                x.AutomationId == "creation-attributes-budget-normal-jump");
            ((IButtonController)jump).SendClicked();
            Require(scrollTarget?.AutomationId == "creation-attributes-normal-heading",
                "Normal points must jump to the actual normal Attribute list.");
            foreach (var attribute in attributesAuthority.Attributes)
            {
                var value = MinimalVisible(attributesPage).OfType<Label>().Single(x =>
                    x.AutomationId == "creation-attributes-open-" + CreationAttributesPage.Token(attribute.AttributeId) + "-value");
                Require(value.Text == attribute.Current.ToString(CultureInfo.InvariantCulture)
                    && value.FontAttributes.HasFlag(FontAttributes.Bold) && value.FontSize >= 24
                    && value.TextColor == NativeTheme.Text, "Current Attribute rating is not prominent/readable.");
            }
            scrollTarget = null;
            MinimalRender(attributesPage);
            ((IButtonController)jump).SendClicked();
            Require(scrollTarget is null, "A detached budget control still scrolls a refreshed screen.");
            MinimalRequireNoMachineValues(attributesPage);
            MinimalRequireFreshDisclosure(attributesPage);
            IssuedPageLifecycle(attributesPage, "OnDisappearing");
            var gear = new CreationGearPage(runtime.Coordinator, actual, runtime.Presenter);
            await MinimalPrepareAsync(gear);
            var visible = MinimalVisibleText(gear);
            MinimalRequireNoMachineValues(gear);
            Require(MinimalVisible(gear).OfType<Button>().Any(x => x.AutomationId == "creation-gear-preview")
                && MinimalVisible(gear).OfType<SearchBar>().Any()
                && visible.Contains("¥"), "Minimal Gear hid budget, search or review.");
            var copy = AndroidSurfaceStrings.Resolve();
            var unchanged = MinimalVisible(gear).OfType<Label>()
                .Single(x => x.AutomationId == "creation-gear-preview-authority");
            Require(unchanged.Text == copy["Gear.ChangeBasket"]
                && unchanged.TextColor.Equals(NativeTheme.Muted)
                && !MinimalVisible(gear).OfType<Button>()
                    .Single(x => x.AutomationId == "creation-gear-preview").IsEnabled,
                "An unchanged saved basket is neutral guidance, not an error or a new save.");
            var disclosure = MinimalVisible(gear).OfType<Button>()
                .Single(x => x.AutomationId == "creation-gear-details-toggle");
            ((IButtonController)disclosure).SendClicked();
            Require(MinimalVisible(gear).OfType<Label>().Any(x =>
                    x.AutomationId == "creation-gear-binding-snapshot-digest"),
                "Gear diagnostic anchors were deleted instead of disclosed.");
            // A refresh must not retain an expanded old snapshot.
            MinimalRender(gear);
            MinimalRequireNoMachineValues(gear);

            var original = runtime.Coordinator.State;
            var prepared = actual.Prepare(original, [new("gear:" + id, 1)]).PreparedPreview
                ?? throw new InvalidOperationException("SETUP: actual Gear preview unavailable.");
            var preview = new CreationGearPreviewPage(runtime.Coordinator, actual, runtime.Presenter,
                prepared, AndroidSurfaceStrings.Resolve(), original);
            await MinimalPrepareAsync(preview);
            MinimalRequireNoMachineValues(preview);
            Require(MinimalVisible(preview).OfType<Button>().Any(x =>
                    x.AutomationId == "creation-gear-confirm" && x.IsEnabled
                    && x.Text == copy["GearPreview.Confirm"]),
                "Minimal preview hid or disabled explicit confirmation.");

            var resources = new CharacterCreationResourcesInteractionPresenter(
                runtime.Services.GetRequiredService<ICharacterCreationResourcesService>(),
                runtime.Services.GetRequiredService<IOwnerBoundCharacterCreationResourcesService>());
            var resourcePage = new CreationResourcesPage(runtime.Coordinator, resources, runtime.Presenter, actual);
            await MinimalPrepareAsync(resourcePage);
            MinimalRequireNoMachineValues(resourcePage);
            var resourceText = MinimalVisibleText(resourcePage);
            Require(resourceText.Contains("¥")
                && !resourceText.Contains(copy["Common.DraftRevision"])
                && !resourceText.Contains(copy["Resources.ExactBudget"])
                && MinimalVisible(resourcePage).OfType<Button>().Any(x =>
                    x.AutomationId == "creation-resources-open-gear" && x.IsEnabled),
                "Resources must retain budget and next action without repeating technical status.");
            // Exercise the warning branch without modifying the workspace or
            // pretending a fabricated budget is admissible for a save.
            var budget = resources.Load(original).State!.Budget;
            typeof(CreationResourcesPage).GetMethod("AddBudget", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(resourcePage, [budget with { IsExact = false }, "Warning test", "test-incomplete-budget"]);
            Require(MinimalVisible(resourcePage).OfType<Label>().Any(x =>
                x.Text == copy["Resources.IncompleteBudget"] && x.TextColor.Equals(NativeTheme.Danger)),
                "Minimal Resources suppressed an incomplete-cost warning.");
            Require(FinalizationDocumentDigest(new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!)
                    == FinalizationDocumentDigest(before),
                "Rendering/disclosing minimal UI mutated the workspace.");
        });
        Console.WriteLine("PASS minimal native UI: localized plain actions, collapsed diagnostics, neutral unchanged basket, retained budget/warnings/confirm, unchanged prose and persistence");
    }

    private static async Task MinimalPrepareAsync(NativePageBase page)
    {
        await (Task)page.GetType().GetMethod("PrepareForAppearanceRefreshAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, [CancellationToken.None])!;
        MinimalRender(page);
    }

    private static void MinimalRequireFreshDisclosure(NativePageBase page)
    {
        var field = page.GetType().GetField("_technicalDetails", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var previous = (View)field.GetValue(page)!;
        var parent = previous.Parent;
        MinimalRender(page);
        var current = (View)field.GetValue(page)!;
        Require(!ReferenceEquals(previous, current) && parent is not null
            && ReferenceEquals(previous.Parent, parent) && current.Parent is not null
            && !ReferenceEquals(current.Parent, parent),
            "A refreshed disclosure reused a native child still owned by its previous container.");
    }

    private static void MinimalRender(NativePageBase page) => page.GetType()
        .GetMethod("Refresh", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, null);

    private static IEnumerable<VisualElement> MinimalVisible(IVisualTreeElement element)
    {
        if (element is VisualElement { IsVisible: false }) yield break;
        if (element is VisualElement view) yield return view;
        foreach (var child in element.GetVisualChildren())
            foreach (var descendant in MinimalVisible(child))
                yield return descendant;
    }

    private static string MinimalVisibleText(IVisualTreeElement element) => string.Join("\n",
        MinimalVisible(element).SelectMany(view => new[]
        {
            view is Label label ? label.Text : view is Button button ? button.Text : string.Empty,
            SemanticProperties.GetDescription(view)
        }));

    private static void MinimalRequireNoMachineValues(IVisualTreeElement element)
    {
        Require(!Regex.IsMatch(MinimalVisibleText(element),
            @"sha256:|\b(?:[0-9a-f]{32}|[0-9a-f]{64})\b|\b[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}\b",
            RegexOptions.IgnoreCase), "Normal UI/accessibility contains machine identifiers.");
    }
}
