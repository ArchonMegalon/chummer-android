using System.Globalization;
using Chummer.Contracts.Characters;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

/// <summary>
/// Renders one immutable Core preview. Confirmation is a separate opt-in gesture bound to the
/// exact preview and stable idempotency key retained by Presentation.
/// </summary>
public sealed class CreationContactPreviewPage : NativePageBase
{
    private readonly CharacterCreationContactPreparedPreview _prepared;
    private readonly VerticalStackLayout _body = new()
    {
        Padding = new Thickness(20, 18, 20, 40),
        Spacing = 14
    };
    private CreationContactPhoneConfirmResult? _confirmation;
    private bool _explicitlyConfirmed;

    internal CreationContactPreviewPage(
        RunnerSessionCoordinator coordinator,
        CharacterCreationContactPreparedPreview prepared) : base(coordinator)
    {
        _prepared = prepared ?? throw new ArgumentNullException(nameof(prepared));
        Title = "Review contact change";
        AutomationId = "creation-contact-preview-page";
        Content = new ScrollView { Content = _body };
    }

    protected override void Refresh()
    {
        _body.Clear();
        _body.Add(NativeTheme.Eyebrow("Explicit review"));
        _body.Add(NativeTheme.Title("Creation Contact change"));
        AddBinding();
        AddTargetDiff();
        AddBudgets();
        AddWritePlan();
        AddBlockers();
        AddConfirmation();
        AddReceipt();
    }

    private void AddBinding()
    {
        Label binding = NativeTheme.Body(
            $"Revision {_prepared.Binding.ContentRevision} · saved {_prepared.Binding.SavedRevision} · "
            + $"Contact {_prepared.ContactBefore.ContactId:D}",
            NativeTheme.Muted);
        binding.AutomationId = "creation-contact-preview-binding";
        _body.Add(binding);
        _body.Add(DigestLabel(
            "Preview digest",
            _prepared.PreviewDigest,
            "creation-contact-preview-digest"));
        _body.Add(DigestLabel(
            "Atomic plan digest",
            _prepared.WritePlan.PlanDigest,
            "creation-contact-plan-digest"));
    }

    private void AddTargetDiff()
    {
        VerticalStackLayout card = new() { Spacing = 7 };
        card.Add(NativeTheme.Eyebrow("Target before / after"));
        card.Add(NativeTheme.Metric("Contact", _prepared.ContactBefore.ContactId.ToString("D")));
        card.Add(NativeTheme.Metric(
            "Name",
            $"{_prepared.ContactBefore.Identity.Name} → {_prepared.ContactAfter.Identity.Name}"));
        card.Add(NativeTheme.Metric(
            "Connection",
            $"{_prepared.ContactBefore.Connection} → {_prepared.ContactAfter.Connection}"));
        card.Add(NativeTheme.Metric(
            "Loyalty",
            $"{_prepared.ContactBefore.Loyalty} → {_prepared.ContactAfter.Loyalty}"));
        card.Add(NativeTheme.Metric(
            "Group",
            $"{Bool(_prepared.ContactBefore.IsGroup)} → {Bool(_prepared.ContactAfter.IsGroup)}"));
        card.Add(NativeTheme.Metric(
            "Free",
            $"{Bool(_prepared.ContactBefore.Free)} → {Bool(_prepared.ContactAfter.Free)}"));
        card.Add(NativeTheme.Metric(
            "Family",
            $"{Bool(_prepared.ContactBefore.Family)} → {Bool(_prepared.ContactAfter.Family)}"));
        card.Add(NativeTheme.Metric(
            "Blackmail",
            $"{Bool(_prepared.ContactBefore.Blackmail)} → {Bool(_prepared.ContactAfter.Blackmail)}"));
        Border border = NativeTheme.Card(card);
        border.AutomationId = "creation-contact-preview-target";
        _body.Add(border);
    }

    private void AddBudgets()
    {
        AddBudget(
            "Contact points before",
            _prepared.ContactBudgetBefore,
            "creation-contact-preview-budget-before");
        AddBudget(
            "Contact points after",
            _prepared.ContactBudgetAfter,
            "creation-contact-preview-budget-after");
        AddBudget(
            "Friends in High Places before",
            _prepared.HighPlacesBudgetBefore,
            "creation-contact-preview-high-places-before");
        AddBudget(
            "Friends in High Places after",
            _prepared.HighPlacesBudgetAfter,
            "creation-contact-preview-high-places-after");
    }

    private void AddBudget(
        string title,
        CharacterCreationContactBudget budget,
        string automationId)
    {
        VerticalStackLayout card = new() { Spacing = 6 };
        card.Add(NativeTheme.Eyebrow(title));
        card.Add(NativeTheme.Metric("Total", budget.Total.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric("Used", budget.Used.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric(
            "Remaining",
            budget.Remaining.ToString(CultureInfo.InvariantCulture)));
        Border border = NativeTheme.Card(card);
        border.AutomationId = automationId;
        SemanticProperties.SetDescription(
            border,
            $"{title}. Total {budget.Total}. Used {budget.Used}. Remaining {budget.Remaining}.");
        _body.Add(border);
    }

    private void AddWritePlan()
    {
        _body.Add(NativeTheme.Eyebrow("Ordered atomic write plan"));
        foreach (CharacterCreationContactWriteOperation operation in _prepared.WritePlan.Operations)
        {
            VerticalStackLayout card = new() { Spacing = 6 };
            card.Add(NativeTheme.Title(
                $"{operation.Order}. {RunnerSessionCoordinator.HumanizeId(operation.FieldId)}",
                18));
            card.Add(NativeTheme.Metric("Before", operation.BeforeValue));
            card.Add(NativeTheme.Metric("After", operation.AfterValue));
            card.Add(NativeTheme.Body(
                $"Source · {string.Join(" · ", operation.SourceAnchorIds)}",
                NativeTheme.Muted));
            Border border = NativeTheme.Card(card, new Thickness(14));
            border.AutomationId =
                $"creation-contact-write-{operation.Order}-{operation.FieldId}";
            _body.Add(border);
        }

        VerticalStackLayout preservation = new() { Spacing = 6 };
        preservation.Add(NativeTheme.Eyebrow("Preservation authority"));
        preservation.Add(NativeTheme.Metric(
            "Untouched siblings",
            _prepared.WritePlan.PreservesUntouchedSiblingState ? "preserved" : "not proven"));
        preservation.Add(NativeTheme.Metric(
            "Nested target state",
            _prepared.WritePlan.PreservesNestedState ? "preserved" : "not proven"));
        preservation.Add(NativeTheme.Metric(
            "Content before",
            _prepared.WritePlan.ContentDigestBefore));
        preservation.Add(NativeTheme.Metric(
            "Content after",
            _prepared.WritePlan.ContentDigestAfter));
        Border preservationCard = NativeTheme.Card(preservation);
        preservationCard.AutomationId = "creation-contact-preview-preservation";
        _body.Add(preservationCard);
    }

    private void AddBlockers()
    {
        string[] blockers = _prepared.Blockers
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
        border.AutomationId = "creation-contact-confirm-blockers";
        _body.Add(border);
    }

    private void AddConfirmation()
    {
        if (_confirmation is
            {
                Outcome: CharacterCreationContactOutcomes.Applied or CharacterCreationContactOutcomes.Replayed,
                Receipt: not null,
                RefreshedState: not null
            })
        {
            Label done = NativeTheme.Body(
                _confirmation.RecoveredByReceiptLookup
                    ? "Confirmed and recovered by the stable idempotency receipt lookup."
                    : "Confirmed, atomically checkpointed, and reloaded from Core.");
            done.AutomationId = "creation-contact-confirmed";
            _body.Add(NativeTheme.Card(done));
            return;
        }

        CheckBox explicitConfirm = new()
        {
            AutomationId = "creation-contact-explicit-confirm",
            IsChecked = _explicitlyConfirmed,
            Color = NativeTheme.Signal
        };
        Label explicitLabel = NativeTheme.Body(
            "I explicitly confirm this exact preview and atomic write plan.");
        Grid explicitRow = new()
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star)
            },
            ColumnSpacing = 10
        };
        explicitRow.Add(explicitConfirm);
        explicitRow.Add(explicitLabel, 1);
        _body.Add(NativeTheme.Card(explicitRow, new Thickness(14)));

        Button confirm = NativeTheme.PrimaryButton("Confirm Contact change");
        confirm.AutomationId = "creation-contact-confirm";
        confirm.IsEnabled = CanConfirm() && _explicitlyConfirmed;
        explicitConfirm.CheckedChanged += (_, args) =>
        {
            _explicitlyConfirmed = args.Value;
            confirm.IsEnabled = CanConfirm() && _explicitlyConfirmed;
        };
        confirm.Clicked += async (_, _) => await RunAsync(async () =>
        {
            _confirmation = await Coordinator.ConfirmCreationContactAsync(_prepared);
        });
        _body.Add(confirm);
    }

    private bool CanConfirm()
    {
        var live = Coordinator.LoadCreationContacts();
        return live.State is { } state
               && live.Blockers.Count == 0
               && _prepared.RequiresExplicitConfirmation
               && _prepared.CanConfirm
               && _prepared.Blockers.Count == 0
               && CreationContactsPhoneAuthority.PreparedMatches(
                   _prepared,
                   state,
                   Coordinator.State);
    }

    private void AddReceipt()
    {
        if (_confirmation is not
            {
                Outcome: CharacterCreationContactOutcomes.Applied or CharacterCreationContactOutcomes.Replayed,
                Receipt: { } receipt,
                RefreshedState: { } refreshed
            })
        {
            return;
        }

        VerticalStackLayout card = new() { Spacing = 7 };
        card.Add(NativeTheme.Eyebrow("Atomic creation receipt"));
        card.Add(NativeTheme.Metric(
            "Previous revision",
            receipt.PreviousContentRevision.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric(
            "Content revision",
            receipt.ContentRevision.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric(
            "Saved revision",
            receipt.SavedRevision.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric(
            "Contact points before",
            receipt.ContactPointsBefore.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric(
            "Contact points after",
            receipt.ContactPointsAfter.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric(
            "Remaining",
            receipt.ContactPointsRemaining.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric(
            "Reloaded contacts",
            refreshed.Contacts.Count.ToString(CultureInfo.InvariantCulture)));
        Border border = NativeTheme.Card(card);
        border.AutomationId = "creation-contact-confirm-receipt";
        SemanticProperties.SetDescription(
            border,
            $"Atomic creation receipt. Previous revision {receipt.PreviousContentRevision}. "
            + $"Content revision {receipt.ContentRevision}. Saved revision {receipt.SavedRevision}. "
            + $"Used before {receipt.ContactPointsBefore}. Used after {receipt.ContactPointsAfter}. "
            + $"Remaining {receipt.ContactPointsRemaining}.");
        _body.Add(border);

        _body.Add(DigestLabel("Receipt ID", receipt.ReceiptId, "creation-contact-receipt-id"));
        _body.Add(DigestLabel(
            "Previous workspace revision",
            receipt.PreviousWorkspaceRevision.ToString(CultureInfo.InvariantCulture),
            "creation-contact-receipt-previous-workspace-revision"));
        _body.Add(DigestLabel(
            "Workspace revision",
            receipt.WorkspaceRevision.ToString(CultureInfo.InvariantCulture),
            "creation-contact-receipt-workspace-revision"));
        _body.Add(DigestLabel(
            "Previous content revision",
            receipt.PreviousContentRevision.ToString(CultureInfo.InvariantCulture),
            "creation-contact-receipt-previous-content-revision"));
        _body.Add(DigestLabel(
            "Content revision",
            receipt.ContentRevision.ToString(CultureInfo.InvariantCulture),
            "creation-contact-receipt-content-revision"));
        _body.Add(DigestLabel(
            "Previous saved revision",
            receipt.PreviousSavedRevision.ToString(CultureInfo.InvariantCulture),
            "creation-contact-receipt-previous-saved-revision"));
        _body.Add(DigestLabel(
            "Saved revision",
            receipt.SavedRevision.ToString(CultureInfo.InvariantCulture),
            "creation-contact-receipt-saved-revision"));
        _body.Add(DigestLabel(
            "Receipt digest",
            receipt.ReceiptDigest,
            "creation-contact-receipt-digest"));
        _body.Add(DigestLabel(
            "Content before",
            receipt.ContentDigestBefore,
            "creation-contact-receipt-content-before"));
        _body.Add(DigestLabel(
            "Content after",
            receipt.ContentDigestAfter,
            "creation-contact-receipt-content-after"));
        _body.Add(DigestLabel(
            "Idempotency key digest",
            receipt.IdempotencyKeyDigest,
            "creation-contact-receipt-idempotency-digest"));
        _body.Add(DigestLabel(
            "Command digest",
            receipt.CommandDigest,
            "creation-contact-receipt-command-digest"));

        Button back = NativeTheme.SecondaryButton("Back to Build");
        back.AutomationId = "creation-contact-back-to-build";
        back.Clicked += async (_, _) => await BackToBuildAsync();
        _body.Add(back);
    }

    private async Task BackToBuildAsync()
    {
        // Shell omits the current tab's ShellContent root (BuildPage) from this
        // child navigation stack. Pop every Contacts/Edit/Preview route to reveal
        // that root; looking for BuildPage in NavigationStack leaves one route
        // behind and a root-type guard incorrectly terminates the process.
        await Navigation.PopToRootAsync(animated: false);
    }

    private static Border DigestLabel(string title, string value, string automationId)
    {
        Label label = NativeTheme.Body(value, NativeTheme.Muted);
        label.AutomationId = automationId;
        SemanticProperties.SetDescription(label, value);
        VerticalStackLayout card = new() { Spacing = 5 };
        card.Add(NativeTheme.Eyebrow(title));
        card.Add(label);
        return NativeTheme.Card(card, new Thickness(14));
    }

    private static string Bool(bool value) => value ? "Yes" : "No";
}
