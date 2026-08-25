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
        Title = "Review assignments";
        AutomationId = "creation-prerequisite-preview-page";
        Content = new ScrollView { Content = _body };
    }

    protected override void Refresh()
    {
        _body.Clear();
        _body.Add(NativeTheme.Eyebrow("Explicit review"));
        _body.Add(NativeTheme.Title(
            string.Equals(
                _buildMethod,
                CharacterCreationBuildMethods.SumToTen,
                StringComparison.Ordinal)
                ? "Sum-to-Ten draft"
                : "Priority draft"));

        Label binding = NativeTheme.Body(
            $"Revision {_preview.Binding.ContentRevision} · saved {_preview.Binding.SavedRevision} · "
            + $"preview {ShortDigest(_preview.PreviewDigest)}",
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
            card.Add(NativeTheme.Eyebrow("Heritage selection"));
            card.Add(NativeTheme.Title(
                string.IsNullOrWhiteSpace(heritage.MetavariantName)
                    ? heritage.MetatypeName
                    : $"{heritage.MetatypeName} · {heritage.MetavariantName}",
                18));
            card.Add(NativeTheme.Metric("Selection ID", heritage.SelectionId));
            card.Add(NativeTheme.Metric(
                "Special Attribute points",
                heritage.SpecialAttributePoints.ToString(CultureInfo.InvariantCulture)));
            card.Add(NativeTheme.Metric(
                "Karma cost",
                heritage.KarmaCost.ToString(CultureInfo.InvariantCulture)));
            card.Add(NativeTheme.Body(
                heritage.HalvesNormalAttributePoints
                    ? "Core applies halveattributepoints to the raw Attribute grant."
                    : "Core keeps the raw normal Attribute grant.",
                NativeTheme.Muted));
            foreach (string anchor in heritage.SourceAnchorIds)
                card.Add(NativeTheme.Body($"Source anchor · {anchor}", NativeTheme.Muted));
            Border border = NativeTheme.Card(card);
            border.AutomationId = "creation-prerequisite-preview-heritage";
            _body.Add(border);
        }

        if (_preview.TalentSelection is { } talent)
        {
            VerticalStackLayout card = new() { Spacing = 6 };
            card.Add(NativeTheme.Eyebrow("Talent selection"));
            card.Add(NativeTheme.Title(talent.Name, 18));
            card.Add(NativeTheme.Metric("Selection ID", talent.SelectionId));
            card.Add(NativeTheme.Metric("Value", talent.Value));
            card.Add(NativeTheme.Metric(
                "Special Attribute points",
                talent.SpecialAttributePoints.ToString(CultureInfo.InvariantCulture)));
            foreach (string quality in talent.GrantedQualities)
                card.Add(NativeTheme.Body($"Granted quality · {quality}", NativeTheme.Muted));
            foreach (string anchor in talent.SourceAnchorIds)
                card.Add(NativeTheme.Body($"Source anchor · {anchor}", NativeTheme.Muted));
            Border border = NativeTheme.Card(card);
            border.AutomationId = "creation-prerequisite-preview-talent";
            _body.Add(border);
        }
    }

    private void AddAssignments()
    {
        _body.Add(NativeTheme.Eyebrow("Five ordered assignments"));
        for (int index = 0; index < _preview.Assignments.Count; index++)
        {
            CharacterCreationPriorityAssignment assignment = _preview.Assignments[index];
            VerticalStackLayout card = new() { Spacing = 6 };
            card.Add(NativeTheme.Title(
                $"{assignment.Order + 1}. {RunnerSessionCoordinator.HumanizeId(assignment.CategoryId)}",
                18));
            card.Add(NativeTheme.Metric("Rank", assignment.Rank));
            card.Add(NativeTheme.Metric("Source ID", assignment.SourceId));
            card.Add(NativeTheme.Metric("Source node", assignment.SourceNodeDigest));
            card.Add(NativeTheme.Metric(
                "Sum-to-Ten value",
                assignment.SumToTenValue.ToString(CultureInfo.InvariantCulture)));
            if (assignment.BaseNormalAttributePoints is int raw)
            {
                card.Add(NativeTheme.Metric(
                    "Raw normal Attribute grant",
                    raw.ToString(CultureInfo.InvariantCulture)));
            }
            foreach (string anchor in assignment.SourceAnchorIds)
                card.Add(NativeTheme.Body($"Source anchor · {anchor}", NativeTheme.Muted));
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
        card.Add(NativeTheme.Eyebrow("Global Creation Karma"));
        card.Add(NativeTheme.Metric("Total", total));
        card.Add(NativeTheme.Metric("Used", used));
        card.Add(NativeTheme.Metric("Remaining", remaining));
        card.Add(NativeTheme.Body(
            budget.IsExact ? "Exact authoritative budget" : "Budget is not exact",
            budget.IsExact ? NativeTheme.Muted : NativeTheme.Danger));
        Border border = NativeTheme.Card(card);
        border.AutomationId = "creation-prerequisite-preview-karma-budget";
        SemanticProperties.SetDescription(
            border,
            $"Global Creation Karma. Total {total}. Used {used}. Remaining {remaining}.");
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
        card.Add(NativeTheme.Eyebrow("Sum-to-Ten"));
        card.Add(NativeTheme.Metric(
            "Used / target",
            $"{_preview.SumToTenUsed.ToString(CultureInfo.InvariantCulture)} / "
            + target.ToString(CultureInfo.InvariantCulture)));
        Border border = NativeTheme.Card(card);
        border.AutomationId = "creation-prerequisite-preview-sum-to-ten";
        _body.Add(border);
    }

    private void AddAttributeGrant()
    {
        VerticalStackLayout card = new() { Spacing = 6 };
        card.Add(NativeTheme.Eyebrow("Attributes prerequisite"));
        card.Add(NativeTheme.Metric(
            "Raw normal Attribute grant",
            _preview.BaseNormalAttributePoints.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric(
            "Effective normal Attribute grant",
            _preview.EffectiveNormalAttributePoints.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric(
            "Total special Attribute points",
            _preview.TotalSpecialAttributePoints.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Body(
            _preview.RequiresMetatypeAttributeAdjustment
                ? "Heritage/metatype halveattributepoints adjustment is still required. Attributes remain disabled."
                : "Core resolved the Heritage/metatype adjustment; Attributes can enter their dedicated wizard stage after confirmation.",
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
        card.Add(NativeTheme.Eyebrow("Blockers"));
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
                "Creation-method draft confirmed and authoritative state reloaded.");
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
        Button confirm = NativeTheme.PrimaryButton("Confirm assignments draft");
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
                ? "Confirmation is a separate explicit action bound to this exact preview digest."
                : "The authority did not request explicit confirmation.",
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
        card.Add(NativeTheme.Eyebrow("Atomic draft receipt"));
        card.Add(NativeTheme.Metric(
            "Previous revision",
            receipt.PreviousContentRevision.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric(
            "Content revision",
            receipt.ContentRevision.ToString(CultureInfo.InvariantCulture)));
        AddReceiptValue(
            card,
            "creation-prerequisite-receipt-content-revision",
            receipt.ContentRevision.ToString(CultureInfo.InvariantCulture));
        card.Add(NativeTheme.Metric(
            "Saved revision",
            receipt.SavedRevision.ToString(CultureInfo.InvariantCulture)));
        AddReceiptValue(
            card,
            "creation-prerequisite-receipt-saved-revision",
            receipt.SavedRevision.ToString(CultureInfo.InvariantCulture));
        card.Add(NativeTheme.Metric(
            "Draft revision",
            receipt.DraftRevision.ToString(CultureInfo.InvariantCulture)));
        AddReceiptValue(
            card,
            "creation-prerequisite-receipt-draft-revision",
            receipt.DraftRevision.ToString(CultureInfo.InvariantCulture));
        card.Add(NativeTheme.Metric("Draft digest", receipt.DraftDigest));
        card.Add(NativeTheme.Metric(
            "Creation Karma remaining",
            receipt.CreationKarmaRemaining.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric(
            "Raw normal Attribute grant",
            receipt.BaseNormalAttributePoints.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric(
            "Effective normal Attribute grant",
            receipt.EffectiveNormalAttributePoints.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric(
            "Total special Attribute points",
            receipt.TotalSpecialAttributePoints.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric(
            "Character document changed",
            receipt.CharacterDocumentChanged.ToString().ToLowerInvariant()));
        card.Add(NativeTheme.Body(
            refreshed.RequiresMetatypeAttributeAdjustment
                ? "Attributes remain disabled: Heritage/metatype halveattributepoints adjustment is required."
                : refreshed.CanEnterAttributes
                    ? "Core prerequisite complete: Attributes can enter their dedicated wizard stage."
                    : "The rules-authoritative Attributes prerequisite remains closed.",
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

        Button back = NativeTheme.SecondaryButton("Back to Build");
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
