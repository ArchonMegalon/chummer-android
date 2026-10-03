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
    private CharacterCreationContactsInteractionLoadResult? _loaded;
    private VerticalStackLayout _technicalDetails = new() { Spacing = 6 };

    internal CreationContactPreviewPage(
        RunnerSessionCoordinator coordinator,
        CharacterCreationContactPreparedPreview prepared) : base(coordinator)
    {
        _prepared = prepared ?? throw new ArgumentNullException(nameof(prepared));
        Title = Copy("Review", "Review changes");
        AutomationId = "creation-contact-preview-page";
        Content = new ScrollView { Content = _body };
    }

    protected override async Task PrepareForAppearanceRefreshAsync(CancellationToken cancellationToken)
    {
        _loaded = null;
        Refresh();
        if (_confirmation?.Receipt is not null) return;
        var loaded = await Coordinator.LoadCreationContactsForDisplayAsync(Coordinator.State, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        _loaded = loaded;
    }

    protected override void Refresh()
    {
        _body.Clear();
        _technicalDetails = new() { Spacing = 6 };
        _body.Add(NativeTheme.Eyebrow(Copy("Review", "Review changes")));
        _body.Add(NativeTheme.Title(_prepared.Edit.ChangeKind switch
        {
            CharacterCreationContactChangeKind.Add => CreationFlowStrings.Get("Contacts.Add", "Add contact"),
            CharacterCreationContactChangeKind.Remove => CreationFlowStrings.Get("Contacts.Remove", "Remove contact"),
            _ => Copy("Edit", "Edit contact")
        }));
        if (_prepared.Edit.ChangeKind == CharacterCreationContactChangeKind.Remove)
            _body.Add(NativeTheme.Body(CreationFlowStrings.Get("Contacts.RemoveWarning",
                "This removes the selected contact, including its notes and attached data. Nothing is removed until you confirm."), NativeTheme.Danger));
        AddBinding();
        AddTargetDiff();
        AddBudgets();
        AddWritePlan();
        AddBlockers();
        AddConfirmation();
        AddReceipt();
        CharacterOverviewState original = Coordinator.State;
        long appearance = CaptureAppearanceGeneration();
        _body.Add(NativeTheme.TechnicalDetails(_technicalDetails, "creation-contact-preview-details",
            () => IsCurrentAppearanceGeneration(appearance)
                  && Coordinator.IsCreationCatalogDisplayCurrent(original)
                  && original.DisplayOwnerContext == _prepared.DisplayOwnerContext
                  && original.WorkspaceId == _prepared.Binding.WorkspaceId));
    }

    private void AddBinding()
    {
        Label binding = NativeTheme.Body(
            $"Revision {_prepared.Binding.ContentRevision} · saved {_prepared.Binding.SavedRevision} · "
            + $"Contact {_prepared.ContactBefore.ContactId:D}",
            NativeTheme.Muted);
        binding.AutomationId = "creation-contact-preview-binding";
        _technicalDetails.Add(binding);
        _technicalDetails.Add(DigestLabel(
            "Preview digest",
            _prepared.PreviewDigest,
            "creation-contact-preview-digest"));
        _technicalDetails.Add(DigestLabel(
            "Atomic plan digest",
            _prepared.WritePlan.PlanDigest,
            "creation-contact-plan-digest"));
    }

    private void AddTargetDiff()
    {
        VerticalStackLayout card = new() { Spacing = 7 };
        card.Add(NativeTheme.Eyebrow(Copy("Changes", "Contact details")));
        if (_prepared.Edit.ChangeKind != CharacterCreationContactChangeKind.Edit)
            card.Add(NativeTheme.Metric(CreationFlowStrings.Get("Contacts.Presence", "Contact entry"),
                _prepared.Edit.ChangeKind == CharacterCreationContactChangeKind.Add
                    ? CreationFlowStrings.Get("Contacts.AddDelta", "Not present → added")
                    : CreationFlowStrings.Get("Contacts.RemoveDelta", "Present → removed")));
        // The write plan belongs in diagnostics, but every changed player value
        // must remain visible, including identity text, notes and intentional clears.
        foreach (CharacterCreationContactFieldAuthority before in _prepared.ContactBefore.Fields)
        {
            CharacterCreationContactFieldAuthority? after = _prepared.ContactAfter.Fields
                .SingleOrDefault(field => field.FieldId == before.FieldId);
            if (after is null) continue;
            bool changed = !string.Equals(before.SerializedValue, after.SerializedValue, StringComparison.Ordinal);
            bool primary = before.FieldId is CharacterCreationContactFieldIds.Name
                or CharacterCreationContactFieldIds.Connection or CharacterCreationContactFieldIds.Loyalty;
            bool collectionValue = _prepared.Edit.ChangeKind != CharacterCreationContactChangeKind.Edit
                && !string.IsNullOrEmpty(before.SerializedValue)
                && !(before.ValueKind == CharacterCreationContactValueKinds.Boolean && before.SerializedValue == "False");
            bool addedValue = _prepared.Edit.ChangeKind == CharacterCreationContactChangeKind.Add
                && !string.IsNullOrEmpty(after.SerializedValue)
                && !(after.ValueKind == CharacterCreationContactValueKinds.Boolean && after.SerializedValue == "False");
            if (!primary && !changed && !collectionValue && !addedValue) continue;
            string value = changed ? $"{DisplayValue(before)} → {DisplayValue(after)}" : DisplayValue(before);
            View metric = NativeTheme.Metric(CreationContactEditPage.FieldLabel(before), value);
            metric.AutomationId = "creation-contact-change-" + before.FieldId;
            card.Add(metric);
        }
        Border border = NativeTheme.Card(card);
        border.AutomationId = "creation-contact-preview-target";
        _body.Add(border);
    }

    private void AddBudgets()
    {
        AddBudget(
            Copy("PointsBefore", "Contact points before"),
            _prepared.ContactBudgetBefore,
            "creation-contact-preview-budget-before");
        AddBudget(
            Copy("PointsAfter", "Contact points after"),
            _prepared.ContactBudgetAfter,
            "creation-contact-preview-budget-after");
        AddBudget(
            Copy("HighPlacesBefore", "Friends in High Places before"),
            _prepared.HighPlacesBudgetBefore,
            "creation-contact-preview-high-places-before");
        AddBudget(
            Copy("HighPlacesAfter", "Friends in High Places after"),
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
        card.Add(NativeTheme.Metric(CreationFlowStrings.Get("Common.Total", "Total"), budget.Total.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric(CreationFlowStrings.Get("Common.Used", "Used"), budget.Used.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric(
            CreationFlowStrings.Get("Common.Remaining", "Remaining"),
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
        _technicalDetails.Add(NativeTheme.Eyebrow("Ordered atomic write plan"));
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
            _technicalDetails.Add(border);
        }

        VerticalStackLayout preservation = new() { Spacing = 6 };
        preservation.Add(NativeTheme.Eyebrow("Preservation authority"));
        preservation.Add(NativeTheme.Metric(
            "Untouched siblings",
            _prepared.WritePlan.PreservesUntouchedSiblingState ? "preserved" : "not proven"));
        preservation.Add(NativeTheme.Metric(
            "Nested target state",
            _prepared.WritePlan.PreservesNestedState ? "preserved" : _prepared.Edit.ChangeKind switch
            {
                CharacterCreationContactChangeKind.Add => CreationFlowStrings.Get("Contacts.AddDelta", "Not present → added"),
                CharacterCreationContactChangeKind.Remove => CreationFlowStrings.Get("Contacts.RemoveDelta", "Present → removed"),
                _ => "not proven"
            }));
        preservation.Add(NativeTheme.Metric(
            "Content before",
            _prepared.WritePlan.ContentDigestBefore));
        preservation.Add(NativeTheme.Metric(
            "Content after",
            _prepared.WritePlan.ContentDigestAfter));
        Border preservationCard = NativeTheme.Card(preservation);
        preservationCard.AutomationId = "creation-contact-preview-preservation";
        _technicalDetails.Add(preservationCard);
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
                Copy("Saved", "Contact change saved."));
            done.AutomationId = "creation-contact-confirmed";
            _body.Add(NativeTheme.Card(done));
            return;
        }

        if (_confirmation?.Receipt is { } committed
            && CreationContactsPhoneAuthority.ReceiptMatches(_prepared, committed))
        {
            Label saved = NativeTheme.Body(
                Copy("SavedReload", "Your change is saved. Reopen the runner in its original account to continue. Do not submit the change again."));
            saved.AutomationId = "creation-contact-committed-reload-required";
            _body.Add(NativeTheme.Card(saved));
            return;
        }

        CheckBox explicitConfirm = new()
        {
            AutomationId = "creation-contact-explicit-confirm",
            IsChecked = _explicitlyConfirmed,
            Color = NativeTheme.Signal
        };
        Label explicitLabel = NativeTheme.Body(
            Copy("ConfirmHelp", "I have reviewed these details and costs and want to save this change."));
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

        Button confirm = NativeTheme.PrimaryButton(Copy("Confirm", "Save contact change"));
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
        var live = _loaded;
        return live?.State is { } state
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
        if (_confirmation?.Receipt is not { } receipt
            || !CreationContactsPhoneAuthority.ReceiptMatches(_prepared, receipt))
        {
            return;
        }
        var refreshed = _confirmation.RefreshedState;

        VerticalStackLayout card = new() { Spacing = 7 };
        card.Add(NativeTheme.Eyebrow(Copy("Saved", "Contact change saved.")));
        card.Add(NativeTheme.Metric(
            Copy("PointsBefore", "Contact points before"),
            receipt.ContactPointsBefore.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric(
            Copy("PointsAfter", "Contact points after"),
            receipt.ContactPointsAfter.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric(
            CreationFlowStrings.Get("Common.Remaining", "Remaining"),
            receipt.ContactPointsRemaining.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric(
            Copy("SavedContacts", "Saved contacts"),
            refreshed is null ? Copy("Reload", "Reopen runner") : refreshed.Contacts.Count.ToString(CultureInfo.InvariantCulture)));
        Border border = NativeTheme.Card(card);
        border.AutomationId = "creation-contact-confirm-receipt";
        SemanticProperties.SetDescription(
            border,
            CreationFlowStrings.Format("Contacts.SavedSemantic", "Saved. Points used: {0} → {1}. Remaining: {2}.",
                receipt.ContactPointsBefore, receipt.ContactPointsAfter, receipt.ContactPointsRemaining));
        _body.Add(border);

        _technicalDetails.Add(DigestLabel("Receipt ID", receipt.ReceiptId, "creation-contact-receipt-id"));
        _technicalDetails.Add(DigestLabel(
            "Previous workspace revision",
            receipt.PreviousWorkspaceRevision.ToString(CultureInfo.InvariantCulture),
            "creation-contact-receipt-previous-workspace-revision"));
        _technicalDetails.Add(DigestLabel(
            "Workspace revision",
            receipt.WorkspaceRevision.ToString(CultureInfo.InvariantCulture),
            "creation-contact-receipt-workspace-revision"));
        _technicalDetails.Add(DigestLabel(
            "Previous content revision",
            receipt.PreviousContentRevision.ToString(CultureInfo.InvariantCulture),
            "creation-contact-receipt-previous-content-revision"));
        _technicalDetails.Add(DigestLabel(
            "Content revision",
            receipt.ContentRevision.ToString(CultureInfo.InvariantCulture),
            "creation-contact-receipt-content-revision"));
        _technicalDetails.Add(DigestLabel(
            "Previous saved revision",
            receipt.PreviousSavedRevision.ToString(CultureInfo.InvariantCulture),
            "creation-contact-receipt-previous-saved-revision"));
        _technicalDetails.Add(DigestLabel(
            "Saved revision",
            receipt.SavedRevision.ToString(CultureInfo.InvariantCulture),
            "creation-contact-receipt-saved-revision"));
        _technicalDetails.Add(DigestLabel(
            "Receipt digest",
            receipt.ReceiptDigest,
            "creation-contact-receipt-digest"));
        _technicalDetails.Add(DigestLabel(
            "Content before",
            receipt.ContentDigestBefore,
            "creation-contact-receipt-content-before"));
        _technicalDetails.Add(DigestLabel(
            "Content after",
            receipt.ContentDigestAfter,
            "creation-contact-receipt-content-after"));
        _technicalDetails.Add(DigestLabel(
            "Idempotency key digest",
            receipt.IdempotencyKeyDigest,
            "creation-contact-receipt-idempotency-digest"));
        _technicalDetails.Add(DigestLabel(
            "Command digest",
            receipt.CommandDigest,
            "creation-contact-receipt-command-digest"));

        Button back = NativeTheme.SecondaryButton(Copy("BackToCreation", "Back to Creation"));
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

    private static string DisplayValue(CharacterCreationContactFieldAuthority field)
        => field.ValueKind == CharacterCreationContactValueKinds.Boolean && bool.TryParse(field.SerializedValue, out bool value)
            ? value ? Copy("Yes", "Yes") : Copy("No", "No")
            : string.IsNullOrEmpty(field.SerializedValue) ? "—" : field.SerializedValue;

    private static string Copy(string key, string fallback) => CreationFlowStrings.Get("Contacts." + key, fallback);
}
