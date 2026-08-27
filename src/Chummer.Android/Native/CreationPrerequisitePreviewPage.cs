using System.Globalization;
using Chummer.Contracts.Characters;

namespace Chummer.Android.Native;

/// <summary>
/// Renders one immutable Core preview and requires a separate explicit confirmation using the
/// exact preview digest. The confirmed write is auxiliary draft state only.
/// </summary>
public sealed class CreationPrerequisitePreviewPage : NativePageBase
{
    private readonly CharacterCreationPrerequisitePreview _preview;
    private readonly IReadOnlyDictionary<string, string> _assignments;
    private readonly CreationPrerequisitePhoneSelections _selections;
    private readonly string _buildMethod;
    private readonly VerticalStackLayout _body = new()
    {
        Padding = new Thickness(20, 18, 20, 40),
        Spacing = 14
    };
    private CreationPrerequisitePhoneConfirmResult? _confirmation;

    internal CreationPrerequisitePreviewPage(
        RunnerSessionCoordinator coordinator,
        CharacterCreationPrerequisitePreview preview,
        IReadOnlyDictionary<string, string> assignments,
        CreationPrerequisitePhoneSelections selections,
        string buildMethod) : base(coordinator)
    {
        _preview = preview ?? throw new ArgumentNullException(nameof(preview));
        _assignments = new Dictionary<string, string>(
            assignments ?? throw new ArgumentNullException(nameof(assignments)),
            StringComparer.Ordinal);
        _selections = selections ?? throw new ArgumentNullException(nameof(selections));
        _buildMethod = buildMethod is (CharacterCreationBuildMethods.Priority
            or CharacterCreationBuildMethods.SumToTen)
            ? buildMethod
            : throw new ArgumentException(
                "A supported authoritative build method is required.",
                nameof(buildMethod));
        Title = WizardStrings.Get("Priority.Preview.PageTitle", "Review assignments");
        AutomationId = "creation-prerequisite-preview-page";
        Content = new ScrollView { Content = _body };
    }

    protected override void Refresh()
    {
        _body.Clear();
        _body.Add(NativeTheme.Eyebrow(WizardStrings.Get("Priority.Preview.Eyebrow", "Explicit review")));
        _body.Add(NativeTheme.Title(
            string.Equals(
                _buildMethod,
                CharacterCreationBuildMethods.SumToTen,
                StringComparison.Ordinal)
                ? WizardStrings.Get("Priority.Preview.SumToTenDraft", "Sum-to-Ten draft")
                : WizardStrings.Get("Priority.Preview.PriorityDraft", "Priority draft")));

        Label binding = NativeTheme.Body(
            WizardStrings.Format(
                "Priority.Preview.Binding",
                "Revision {0} · saved {1} · preview {2}",
                _preview.Binding.ContentRevision,
                _preview.Binding.SavedRevision,
                ShortDigest(_preview.PreviewDigest)),
            NativeTheme.Muted);
        binding.AutomationId = "creation-prerequisite-preview-binding";
        _body.Add(binding);
        AddDigestBinding(
            "creation-prerequisite-preview-digest",
            _preview.PreviewDigest);
        AddDigestBinding(
            "creation-prerequisite-preview-raw-character-xml-digest",
            _preview.Binding.RawCharacterXmlDigest);
        AddDigestBinding(
            "creation-prerequisite-preview-auxiliary-state-digest",
            _preview.Binding.AuxiliaryStateDigest);
        AddDigestBinding(
            "creation-prerequisite-preview-authority-digest",
            _preview.Binding.AuthorityDigest);

        AddAssignments();
        AddHeritageAndTalent();
        AddBudget();
        AddSumToTen();
        AddAttributeGrant();
        AddBlockers();
        AddConfirmation();
        AddReceipt();
    }

    private void AddHeritageAndTalent()
    {
        if (_preview.HeritageSelection is { } heritage)
        {
            VerticalStackLayout card = new() { Spacing = 6 };
            card.Add(NativeTheme.Eyebrow(WizardStrings.Get("Priority.Preview.HeritageSelection", "Heritage selection")));
            card.Add(NativeTheme.Title(
                string.IsNullOrWhiteSpace(heritage.MetavariantName)
                    ? heritage.MetatypeName
                    : $"{heritage.MetatypeName} · {heritage.MetavariantName}",
                18));
            card.Add(NativeTheme.Metric(WizardStrings.Get("Common.SelectionId", "Selection ID"), heritage.SelectionId));
            card.Add(NativeTheme.Metric(
                WizardStrings.Get("Common.SpecialAttributePointsLabel", "Special Attribute points"),
                heritage.SpecialAttributePoints.ToString(CultureInfo.InvariantCulture)));
            card.Add(NativeTheme.Metric(
                WizardStrings.Get("Priority.Preview.KarmaCost", "Karma cost"),
                heritage.KarmaCost.ToString(CultureInfo.InvariantCulture)));
            card.Add(NativeTheme.Body(
                heritage.HalvesNormalAttributePoints
                    ? WizardStrings.Get(
                        "Priority.Preview.HalveAttributePoints",
                        "Core applies halveattributepoints to the raw Attribute grant.")
                    : WizardStrings.Get(
                        "Priority.Preview.KeepAttributePoints",
                        "Core keeps the raw normal Attribute grant."),
                NativeTheme.Muted));
            foreach (string anchor in heritage.SourceAnchorIds)
                card.Add(NativeTheme.Body(
                    WizardStrings.Format("Common.SourceAnchor", "Source anchor · {0}", anchor),
                    NativeTheme.Muted));
            Border border = NativeTheme.Card(card);
            border.AutomationId = "creation-prerequisite-preview-heritage";
            _body.Add(border);
        }

        if (_preview.TalentSelection is { } talent)
        {
            VerticalStackLayout card = new() { Spacing = 6 };
            card.Add(NativeTheme.Eyebrow(WizardStrings.Get("Priority.Preview.TalentSelection", "Talent selection")));
            card.Add(NativeTheme.Title(talent.Name, 18));
            card.Add(NativeTheme.Metric(WizardStrings.Get("Common.SelectionId", "Selection ID"), talent.SelectionId));
            card.Add(NativeTheme.Metric(WizardStrings.Get("Common.Value", "Value"), talent.Value));
            card.Add(NativeTheme.Metric(
                WizardStrings.Get("Common.SpecialAttributePointsLabel", "Special Attribute points"),
                talent.SpecialAttributePoints.ToString(CultureInfo.InvariantCulture)));
            foreach (string quality in talent.GrantedQualities)
                card.Add(NativeTheme.Body(
                    WizardStrings.Format("Priority.Preview.GrantedQuality", "Granted quality · {0}", quality),
                    NativeTheme.Muted));
            if (talent.GrantPlan is { } grantPlan)
            {
                card.Add(NativeTheme.Metric(
                    WizardStrings.Get("Priority.Preview.GrantPlan", "Grant plan"),
                    WizardStrings.Format(
                        "Priority.Preview.GrantPlanSummary",
                        "{0} active skills · {1} skill groups",
                        grantPlan.ActiveSkills.Count.ToString(CultureInfo.InvariantCulture),
                        grantPlan.SkillGroups.Count.ToString(CultureInfo.InvariantCulture))));
                Label planDigest = NativeTheme.Body(grantPlan.PlanDigest, NativeTheme.Muted);
                planDigest.AutomationId = "creation-prerequisite-preview-talent-grant-plan-digest";
                card.Add(planDigest);
                for (int index = 0; index < grantPlan.ActiveSkills.Count; index++)
                {
                    CharacterCreationTalentActiveSkillGrantPlanEntry entry =
                        grantPlan.ActiveSkills[index];
                    Label grant = NativeTheme.Body(
                        WizardStrings.Format(
                            "Priority.Preview.GrantSlot",
                            "Slot {0} · {1} · rating {2} · {3}",
                            index + 1,
                            entry.CanonicalName,
                            entry.BaseRating.ToString(CultureInfo.InvariantCulture),
                            entry.ImprovementKind),
                        NativeTheme.Muted);
                    grant.AutomationId =
                        $"creation-prerequisite-preview-talent-active-skill-{Token(entry.SelectionId)}";
                    card.Add(grant);
                }
                for (int index = 0; index < grantPlan.SkillGroups.Count; index++)
                {
                    CharacterCreationTalentSkillGroupGrantPlanEntry entry =
                        grantPlan.SkillGroups[index];
                    Label grant = NativeTheme.Body(
                        WizardStrings.Format(
                            "Priority.Preview.GrantSlot",
                            "Slot {0} · {1} · rating {2} · {3}",
                            index + 1,
                            entry.CanonicalName,
                            entry.BaseRating.ToString(CultureInfo.InvariantCulture),
                            entry.ImprovementKind),
                        NativeTheme.Muted);
                    grant.AutomationId =
                        $"creation-prerequisite-preview-talent-skill-group-{Token(entry.SelectionId)}";
                    card.Add(grant);
                }
                foreach (string anchor in grantPlan.SourceAnchorIds)
                    card.Add(NativeTheme.Body(
                        WizardStrings.Format("Priority.Preview.GrantSourceAnchor", "Grant source anchor · {0}", anchor),
                        NativeTheme.Muted));
            }
            foreach (string anchor in talent.SourceAnchorIds)
                card.Add(NativeTheme.Body(
                    WizardStrings.Format("Common.SourceAnchor", "Source anchor · {0}", anchor),
                    NativeTheme.Muted));
            Border border = NativeTheme.Card(card);
            border.AutomationId = "creation-prerequisite-preview-talent";
            _body.Add(border);
        }
    }

    private void AddAssignments()
    {
        _body.Add(NativeTheme.Eyebrow(WizardStrings.Get("Priority.Preview.Assignments", "Five ordered assignments")));
        for (int index = 0; index < _preview.Assignments.Count; index++)
        {
            CharacterCreationPriorityAssignment assignment = _preview.Assignments[index];
            VerticalStackLayout card = new() { Spacing = 6 };
            card.Add(NativeTheme.Title(
                WizardStrings.Format(
                    "Priority.Preview.AssignmentTitle",
                    "{0}. {1}",
                    assignment.Order + 1,
                    WizardStrings.PriorityCategory(
                        assignment.CategoryId,
                        RunnerSessionCoordinator.HumanizeId(assignment.CategoryId))),
                18));
            card.Add(NativeTheme.Metric(WizardStrings.Get("Common.RankLabel", "Rank"), assignment.Rank));
            card.Add(NativeTheme.Metric(WizardStrings.Get("Common.SourceId", "Source ID"), assignment.SourceId));
            card.Add(NativeTheme.Metric(WizardStrings.Get("Common.SourceNode", "Source node"), assignment.SourceNodeDigest));
            card.Add(NativeTheme.Metric(
                WizardStrings.Get("Priority.Preview.SumToTenValue", "Sum-to-Ten value"),
                assignment.SumToTenValue.ToString(CultureInfo.InvariantCulture)));
            if (assignment.BaseNormalAttributePoints is int raw)
            {
                card.Add(NativeTheme.Metric(
                    WizardStrings.Get("Priority.Attributes.RawGrantLabel", "Raw normal Attribute grant"),
                    raw.ToString(CultureInfo.InvariantCulture)));
            }
            foreach (string anchor in assignment.SourceAnchorIds)
                card.Add(NativeTheme.Body(
                    WizardStrings.Format("Common.SourceAnchor", "Source anchor · {0}", anchor),
                    NativeTheme.Muted));
            Border border = NativeTheme.Card(card, new Thickness(14));
            border.AutomationId =
                $"creation-prerequisite-preview-assignment-{Token(assignment.CategoryId)}";
            _body.Add(border);
        }
    }

    private void AddBudget()
    {
        CharacterCreationBudgetState budget = _preview.CreationKarmaBudget;
        string total = FormatBudget(budget.Total, budget.Unit);
        string used = FormatBudget(budget.Used, budget.Unit);
        string remaining = FormatBudget(budget.Remaining, budget.Unit);
        VerticalStackLayout card = new() { Spacing = 7 };
        card.Add(NativeTheme.Eyebrow(WizardStrings.Get("Priority.Karma.Heading", "Global Creation Karma")));
        card.Add(NativeTheme.Metric(WizardStrings.Get("Common.Total", "Total"), total));
        card.Add(NativeTheme.Metric(WizardStrings.Get("Common.Used", "Used"), used));
        card.Add(NativeTheme.Metric(WizardStrings.Get("Common.Remaining", "Remaining"), remaining));
        card.Add(NativeTheme.Body(
            budget.IsExact
                ? WizardStrings.Get("Priority.Karma.Exact", "Exact authoritative budget")
                : WizardStrings.Get("Priority.Karma.Inexact", "Budget is not exact"),
            budget.IsExact ? NativeTheme.Muted : NativeTheme.Danger));
        Border border = NativeTheme.Card(card);
        border.AutomationId = "creation-prerequisite-preview-karma-budget";
        SemanticProperties.SetDescription(
            border,
            WizardStrings.Format(
                "Priority.Karma.Semantic",
                "Global Creation Karma. Total {0}. Used {1}. Remaining {2}.",
                total,
                used,
                remaining));
        _body.Add(border);
    }

    private void AddSumToTen()
    {
        if (!string.Equals(
                _buildMethod,
                CharacterCreationBuildMethods.SumToTen,
                StringComparison.Ordinal)
            || _preview.SumToTenTarget is not int target)
            return;
        VerticalStackLayout card = new() { Spacing = 6 };
        card.Add(NativeTheme.Eyebrow(WizardStrings.Get("Priority.SumToTen", "Sum-to-Ten")));
        card.Add(NativeTheme.Metric(
            WizardStrings.Get("Priority.Preview.UsedTarget", "Used / target"),
            $"{_preview.SumToTenUsed.ToString(CultureInfo.InvariantCulture)} / "
            + target.ToString(CultureInfo.InvariantCulture)));
        Border border = NativeTheme.Card(card);
        border.AutomationId = "creation-prerequisite-preview-sum-to-ten";
        _body.Add(border);
    }

    private void AddAttributeGrant()
    {
        VerticalStackLayout card = new() { Spacing = 6 };
        card.Add(NativeTheme.Eyebrow(WizardStrings.Get("Priority.Attributes.Prerequisite", "Attributes prerequisite")));
        card.Add(NativeTheme.Metric(
            WizardStrings.Get("Priority.Attributes.RawGrantLabel", "Raw normal Attribute grant"),
            _preview.BaseNormalAttributePoints.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric(
            WizardStrings.Get("Priority.Attributes.EffectiveGrantLabel", "Effective normal Attribute grant"),
            _preview.EffectiveNormalAttributePoints.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric(
            WizardStrings.Get("Priority.Attributes.TotalSpecialLabel", "Total special Attribute points"),
            _preview.TotalSpecialAttributePoints.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Body(
            _preview.RequiresMetatypeAttributeAdjustment
                ? WizardStrings.Get(
                    "Priority.Preview.AttributesAdjustmentRequired",
                    "Heritage/metatype halveattributepoints adjustment is still required. Attributes remain disabled.")
                : WizardStrings.Get(
                    "Priority.Preview.AttributesReady",
                    "Core resolved the Heritage/metatype adjustment; Attributes can enter their dedicated wizard stage after confirmation."),
            _preview.RequiresMetatypeAttributeAdjustment ? NativeTheme.Danger : NativeTheme.Muted));
        Border border = NativeTheme.Card(card);
        border.AutomationId = _preview.RequiresMetatypeAttributeAdjustment
            ? "creation-prerequisite-preview-attributes-disabled"
            : "creation-prerequisite-preview-attributes-ready";
        _body.Add(border);
    }

    private void AddBlockers()
    {
        string[] blockers = _preview.Blockers
            .Concat(_preview.CreationKarmaBudget.Blockers)
            .Concat(_confirmation?.Blockers ?? [])
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (blockers.Length == 0)
            return;

        VerticalStackLayout card = new() { Spacing = 6 };
        card.Add(NativeTheme.Eyebrow(WizardStrings.Get("Common.Blockers", "Blockers")));
        foreach (string blocker in blockers)
            card.Add(NativeTheme.Body(blocker, NativeTheme.Danger));
        Border border = NativeTheme.Card(card);
        border.AutomationId = "creation-prerequisite-preview-blockers";
        _body.Add(border);
    }

    private void AddConfirmation()
    {
        bool confirmed = string.Equals(
            _confirmation?.Outcome,
            CharacterCreationFoundationOutcomes.Success,
            StringComparison.Ordinal);
        if (confirmed)
        {
            Label complete = NativeTheme.Body(
                WizardStrings.Get(
                    "Priority.Preview.Confirmed",
                    "Creation-method draft confirmed and authoritative state reloaded."));
            complete.AutomationId = "creation-prerequisite-confirmed";
            _body.Add(NativeTheme.Card(complete));
            return;
        }

        CharacterCreationFoundationResult<CharacterCreationPrerequisiteState> live =
            Coordinator.LoadCreationPrerequisite();
        bool exactLiveBinding = live.Value is { } state
                                && CreationPrerequisitePhoneAuthority.BindingEquals(
                                    _preview.Binding,
                                    state.Binding);
        Button confirm = NativeTheme.PrimaryButton(WizardStrings.Get("Priority.Preview.Confirm", "Confirm assignments draft"));
        confirm.AutomationId = "creation-prerequisite-confirm";
        confirm.IsEnabled = exactLiveBinding
                            && _preview.RequiresExplicitConfirmation
                            && _preview.CanConfirm
                            && _preview.Blockers.Count == 0
                            && _preview.CreationKarmaBudget.IsExact
                            && _preview.CreationKarmaBudget.Blockers.Count == 0
                            && CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(
                                _preview.PreviewDigest);
        confirm.Clicked += async (_, _) => await RunAsync(async () =>
        {
            _confirmation = await Coordinator.ConfirmCreationPrerequisiteAsync(
                _preview,
                _assignments,
                _selections);
        });
        _body.Add(confirm);

        Label explicitAction = NativeTheme.Body(
            _preview.RequiresExplicitConfirmation
                ? WizardStrings.Get(
                    "Priority.Preview.ExplicitConfirmation",
                    "Confirmation is a separate explicit action bound to this exact preview digest.")
                : WizardStrings.Get(
                    "Priority.Preview.NoExplicitConfirmation",
                    "The authority did not request explicit confirmation."),
            NativeTheme.Muted);
        explicitAction.AutomationId = "creation-prerequisite-explicit-confirmation";
        _body.Add(explicitAction);
    }

    private void AddReceipt()
    {
        if (_confirmation is not
            {
                Outcome: CharacterCreationFoundationOutcomes.Success,
                Receipt: { } receipt,
                RefreshedState: { } refreshed
            })
        {
            return;
        }

        VerticalStackLayout card = new() { Spacing = 6 };
        card.Add(NativeTheme.Eyebrow(WizardStrings.Get("Priority.Preview.Receipt", "Atomic draft receipt")));
        card.Add(NativeTheme.Metric(
            WizardStrings.Get("Priority.Preview.PreviousRevision", "Previous revision"),
            receipt.PreviousContentRevision.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric(
            WizardStrings.Get("Priority.Preview.ContentRevision", "Content revision"),
            receipt.ContentRevision.ToString(CultureInfo.InvariantCulture)));
        AddReceiptValue(
            card,
            "creation-prerequisite-receipt-content-revision",
            receipt.ContentRevision.ToString(CultureInfo.InvariantCulture));
        card.Add(NativeTheme.Metric(
            WizardStrings.Get("Priority.Preview.SavedRevision", "Saved revision"),
            receipt.SavedRevision.ToString(CultureInfo.InvariantCulture)));
        AddReceiptValue(
            card,
            "creation-prerequisite-receipt-saved-revision",
            receipt.SavedRevision.ToString(CultureInfo.InvariantCulture));
        card.Add(NativeTheme.Metric(
            WizardStrings.Get("Priority.Draft.Revision", "Draft revision"),
            receipt.DraftRevision.ToString(CultureInfo.InvariantCulture)));
        AddReceiptValue(
            card,
            "creation-prerequisite-receipt-draft-revision",
            receipt.DraftRevision.ToString(CultureInfo.InvariantCulture));
        card.Add(NativeTheme.Metric(WizardStrings.Get("Priority.Draft.Digest", "Draft digest"), receipt.DraftDigest));
        card.Add(NativeTheme.Metric(
            WizardStrings.Get("Priority.Preview.KarmaRemaining", "Creation Karma remaining"),
            receipt.CreationKarmaRemaining.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric(
            WizardStrings.Get("Priority.Attributes.RawGrantLabel", "Raw normal Attribute grant"),
            receipt.BaseNormalAttributePoints.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric(
            WizardStrings.Get("Priority.Attributes.EffectiveGrantLabel", "Effective normal Attribute grant"),
            receipt.EffectiveNormalAttributePoints.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric(
            WizardStrings.Get("Priority.Attributes.TotalSpecialLabel", "Total special Attribute points"),
            receipt.TotalSpecialAttributePoints.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric(
            WizardStrings.Get("Priority.Preview.DocumentChanged", "Character document changed"),
            receipt.CharacterDocumentChanged.ToString().ToLowerInvariant()));
        card.Add(NativeTheme.Body(
            refreshed.RequiresMetatypeAttributeAdjustment
                ? WizardStrings.Get(
                    "Priority.Preview.ReceiptAttributesDisabled",
                    "Attributes remain disabled: Heritage/metatype halveattributepoints adjustment is required.")
                : refreshed.CanEnterAttributes
                    ? WizardStrings.Get(
                        "Priority.Preview.ReceiptAttributesReady",
                        "Core prerequisite complete: Attributes can enter their dedicated wizard stage.")
                    : WizardStrings.Get(
                        "Priority.Preview.ReceiptAttributesClosed",
                        "The rules-authoritative Attributes prerequisite remains closed."),
            refreshed.CanEnterAttributes && !refreshed.RequiresMetatypeAttributeAdjustment
                ? NativeTheme.Muted
                : NativeTheme.Danger));
        AddReceiptDigest(
            card,
            "creation-prerequisite-receipt-draft-digest",
            receipt.DraftDigest);
        AddReceiptDigest(
            card,
            "creation-prerequisite-receipt-raw-character-xml-digest",
            receipt.RawCharacterXmlDigest);
        AddReceiptDigest(
            card,
            "creation-prerequisite-receipt-auxiliary-state-digest",
            refreshed.Binding.AuxiliaryStateDigest);
        AddReceiptDigest(
            card,
            "creation-prerequisite-receipt-authority-digest",
            receipt.AuthorityDigest);
        Border border = NativeTheme.Card(card);
        border.AutomationId = "creation-prerequisite-confirm-receipt";
        _body.Add(border);

        Button back = NativeTheme.SecondaryButton(WizardStrings.Get("Priority.Preview.BackToBuild", "Back to Build"));
        back.AutomationId = "creation-prerequisite-back-to-build";
        back.Clicked += async (_, _) => await BackToBuildAsync();
        _body.Add(back);
    }

    private async Task BackToBuildAsync()
    {
        await Navigation.PopAsync(animated: false);
        if (Navigation.NavigationStack.LastOrDefault() is CreationPrerequisitePage)
            await Navigation.PopAsync();
    }

    private static string FormatBudget(decimal value, string unit)
        => $"{value.ToString("0.##", CultureInfo.InvariantCulture)} {unit}".TrimEnd();

    private static string ShortDigest(string digest)
        => CreationPrerequisiteDigestText.CanonicalPrefix(digest);

    private void AddDigestBinding(string automationId, string digest)
    {
        Label label = NativeTheme.Body(digest, NativeTheme.Muted);
        label.AutomationId = automationId;
        _body.Add(label);
    }

    private static void AddReceiptDigest(
        VerticalStackLayout card,
        string automationId,
        string digest)
    {
        Label label = NativeTheme.Body(digest, NativeTheme.Muted);
        label.AutomationId = automationId;
        card.Add(label);
    }

    private static void AddReceiptValue(
        VerticalStackLayout card,
        string automationId,
        string value)
    {
        Label label = NativeTheme.Body(value, NativeTheme.Muted);
        label.AutomationId = automationId;
        card.Add(label);
    }

    private static string Token(string value)
        => new(value.Trim().ToLowerInvariant().Select(character =>
            char.IsLetterOrDigit(character) ? character : '-').ToArray());
}
