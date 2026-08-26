using System.Globalization;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Android.Native;

/// <summary>
/// Phone-owned manual intake for an exact completed-run proposal. Every value
/// remains inert until the host source binds it to the clean saved workspace,
/// preflights it through Core and durably verifies the canonical proposal.
/// </summary>
public sealed class Sr5AfterRunManualProposalPage : NativePageBase
{
    private readonly CharacterWorkspaceId _workspaceId;
    private readonly long _workspaceRevision;
    private readonly Entry _proposalId;
    private readonly Entry _runId;
    private readonly Entry _characterId;
    private readonly Entry _runTitle;
    private readonly Entry _completedAt;
    private readonly Entry _karmaAward;
    private readonly Entry _nuyenAward;
    private readonly Entry _rewardReceiptDigest;
    private readonly CheckBox _targetsRunner;
    private readonly CheckBox _runCompleted;
    private readonly Entry _currentHeat;
    private readonly Entry _heatDelta;
    private readonly Entry _streetCredDelta;
    private readonly Entry _notorietyDelta;
    private readonly Entry _publicAwarenessDelta;
    private readonly Entry _maximumHeat;
    private readonly Entry _maximumReputation;
    private readonly Entry _maximumConnection;
    private readonly Entry _maximumLoyalty;
    private readonly Entry _karmaPerContactPoint;
    private readonly Switch _allowRewardContacts;
    private readonly Switch _allowPurchasedContacts;
    private readonly Switch _calculatePublicAwareness;
    private readonly Entry _contactId;
    private readonly Entry _contactName;
    private readonly Entry _contactRole;
    private readonly Entry _contactLocation;
    private readonly Entry _contactConnection;
    private readonly Entry _contactLoyalty;
    private readonly Picker _contactKind;
    private readonly Label _contactsSummary;
    private readonly Button _addContact;
    private readonly Button _removeContact;
    private readonly Entry _gmActorId;
    private readonly Entry _gmReviewId;
    private readonly Editor _gmReason;
    private readonly CheckBox _gmApproved;
    private readonly Entry _ownerActorId;
    private readonly Entry _ownerReviewId;
    private readonly Editor _ownerReason;
    private readonly CheckBox _ownerApproved;
    private readonly Label _status;
    private readonly Button _publish;
    private readonly List<CharacterAfterRunContactProposal> _contacts = [];

    public Sr5AfterRunManualProposalPage(
        RunnerSessionCoordinator coordinator,
        CharacterWorkspaceId workspaceId,
        long workspaceRevision) : base(coordinator)
    {
        if (workspaceRevision <= 0
            || coordinator.State.WorkspaceId != workspaceId
            || coordinator.State.ContentRevision != workspaceRevision
            || coordinator.State.SavedRevision != workspaceRevision
            || coordinator.State.IsDirty
            || !coordinator.SupportsManualAfterRunProposalEntry)
        {
            throw new InvalidOperationException(
                "Manual After Run entry requires the composed host authority and exact clean saved runner revision.");
        }
        _workspaceId = workspaceId;
        _workspaceRevision = workspaceRevision;
        Title = "Enter run result";
        AutomationId = Sr5CareerWizardRoutes.AfterRunEnter;
        VerticalStackLayout body = Sr5AfterRunSettlementWizardPage.Body();
        body.Add(NativeTheme.Eyebrow("SR5 Career · After Run · Manual authority"));
        body.Add(NativeTheme.Title("Enter the completed run exactly"));
        body.Add(NativeTheme.Body(
            "Nothing here mutates the character. Publish becomes available only after the Android host binds every typed fact and both approvals to this exact saved workspace revision, then Core accepts the combined quote.",
            NativeTheme.Muted));
        body.Add(NativeTheme.Card(new VerticalStackLayout
        {
            Spacing = 6,
            Children =
            {
                NativeTheme.Metric("Workspace", workspaceId.Value),
                NativeTheme.Metric("Saved revision", workspaceRevision.ToString(CultureInfo.InvariantCulture))
            }
        }));

        body.Add(NativeTheme.Title("Run identity", 20));
        _proposalId = AddText(body, "Proposal UUID", "sr5-after-run-entry-proposal-id", "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx", 36);
        _runId = AddText(body, "Run UUID", "sr5-after-run-entry-run-id", "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx", 36);
        _characterId = AddText(body, "Character UUID", "sr5-after-run-entry-character-id", "Host-owned character identity", 36);
        _runTitle = AddText(body, "Run title", "sr5-after-run-entry-title", "Completed run", CharacterAfterRunSettlementRules.MaximumTextLength);
        _completedAt = AddText(body, "Completed at (ISO 8601)", "sr5-after-run-entry-completed-at", "2026-08-26T18:00:00Z", 64);
        _targetsRunner = AddCheck(
            body,
            "I confirm this character UUID targets the displayed saved runner",
            "sr5-after-run-entry-target-owned");
        _runCompleted = AddCheck(
            body,
            "I confirm this run is completed",
            "sr5-after-run-entry-completed");

        body.Add(NativeTheme.Title("Recorded rewards", 20));
        body.Add(NativeTheme.Body(
            "Karma and Nuyen are receipt context only. This settlement will not duplicate them as a second character mutation.",
            NativeTheme.Muted));
        _karmaAward = AddNumber(body, "Karma awarded", "sr5-after-run-entry-karma-award", signed: false);
        _nuyenAward = AddNumber(body, "Nuyen awarded", "sr5-after-run-entry-nuyen-award", signed: false);
        _rewardReceiptDigest = AddText(body, "Reward receipt SHA-256", "sr5-after-run-entry-reward-digest", "64 lowercase hexadecimal characters", 64);

        body.Add(NativeTheme.Title("Heat and reputation", 20));
        _currentHeat = AddNumber(body, "Current Heat", "sr5-after-run-entry-current-heat", signed: false);
        _heatDelta = AddNumber(body, "Heat delta", "sr5-after-run-entry-heat-delta", signed: true);
        _streetCredDelta = AddNumber(body, "Street Cred delta", "sr5-after-run-entry-street-cred-delta", signed: true);
        _notorietyDelta = AddNumber(body, "Notoriety delta", "sr5-after-run-entry-notoriety-delta", signed: true);
        _publicAwarenessDelta = AddNumber(body, "Public Awareness delta", "sr5-after-run-entry-public-awareness-delta", signed: true);

        body.Add(NativeTheme.Title("Explicit GM policy", 20));
        _maximumHeat = AddNumber(body, "Maximum Heat", "sr5-after-run-entry-maximum-heat", signed: false);
        _maximumReputation = AddNumber(body, "Maximum reputation", "sr5-after-run-entry-maximum-reputation", signed: false);
        _maximumConnection = AddNumber(body, "Maximum contact Connection", "sr5-after-run-entry-maximum-connection", signed: false);
        _maximumLoyalty = AddNumber(body, "Maximum contact Loyalty", "sr5-after-run-entry-maximum-loyalty", signed: false);
        _karmaPerContactPoint = AddNumber(body, "Karma per purchased contact point", "sr5-after-run-entry-contact-karma-rate", signed: false);
        _allowRewardContacts = AddSwitch(body, "Allow run-reward contacts", "sr5-after-run-entry-allow-reward-contacts");
        _allowPurchasedContacts = AddSwitch(body, "Allow Karma-purchased contacts", "sr5-after-run-entry-allow-purchased-contacts");
        _calculatePublicAwareness = AddSwitch(body, "Calculate Public Awareness from reputation", "sr5-after-run-entry-calculate-awareness");

        body.Add(NativeTheme.Title("Contact proposals", 20));
        body.Add(NativeTheme.Body(
            "Add zero or more contacts. Only contacts visible in the list below are included in the approved proposal.",
            NativeTheme.Muted));
        _contactId = AddText(body, "Contact UUID", "sr5-after-run-entry-contact-id", "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx", 36);
        _contactName = AddText(body, "Contact name", "sr5-after-run-entry-contact-name", "Name", CharacterAfterRunSettlementRules.MaximumTextLength);
        _contactRole = AddText(body, "Contact role", "sr5-after-run-entry-contact-role", "Role", CharacterAfterRunSettlementRules.MaximumTextLength);
        _contactLocation = AddText(body, "Contact location", "sr5-after-run-entry-contact-location", "Location", CharacterAfterRunSettlementRules.MaximumTextLength);
        _contactConnection = AddNumber(body, "Connection", "sr5-after-run-entry-contact-connection", signed: false);
        _contactLoyalty = AddNumber(body, "Loyalty", "sr5-after-run-entry-contact-loyalty", signed: false);
        body.Add(NativeTheme.FieldLabel("Contact origin"));
        _contactKind = new Picker
        {
            AutomationId = "sr5-after-run-entry-contact-kind",
            Title = "Contact origin",
            ItemsSource = new[] { "Run reward", "Karma purchase" },
            SelectedIndex = 0,
            BackgroundColor = NativeTheme.Surface,
            TextColor = NativeTheme.Text
        };
        _contactKind.SelectedIndexChanged += (_, _) => RefreshEnabledState();
        body.Add(_contactKind);
        _addContact = NativeTheme.SecondaryButton("Add exact contact");
        _addContact.AutomationId = "sr5-after-run-entry-contact-add";
        _addContact.Clicked += (_, _) => AddContact();
        body.Add(_addContact);
        _contactsSummary = NativeTheme.Body("No contacts added.", NativeTheme.Muted);
        _contactsSummary.AutomationId = "sr5-after-run-entry-contacts-summary";
        body.Add(NativeTheme.Card(_contactsSummary));
        _removeContact = NativeTheme.SecondaryButton("Remove last contact");
        _removeContact.AutomationId = "sr5-after-run-entry-contact-remove";
        _removeContact.Clicked += (_, _) => RemoveLastContact();
        body.Add(_removeContact);

        body.Add(NativeTheme.Title("GM approval", 20));
        _gmActorId = AddText(body, "GM actor ID", "sr5-after-run-entry-gm-actor", "letters, digits, dot, dash or underscore", CharacterAfterRunSettlementRules.MaximumTextLength);
        _gmReviewId = AddText(body, "GM review UUID", "sr5-after-run-entry-gm-review-id", "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx", 36);
        body.Add(NativeTheme.FieldLabel("GM review reason / note"));
        _gmReason = NativeTheme.TextArea("sr5-after-run-entry-gm-reason", string.Empty, "Optional bounded note");
        _gmReason.MaxLength = CharacterAfterRunSettlementRules.MaximumTextLength;
        _gmReason.TextChanged += (_, _) => RefreshEnabledState();
        body.Add(_gmReason);
        _gmApproved = AddCheck(
            body,
            "As the named GM, I approve this exact run result and policy",
            "sr5-after-run-entry-gm-approved");

        body.Add(NativeTheme.Title("Character-owner approval", 20));
        _ownerActorId = AddText(body, "Owner actor ID", "sr5-after-run-entry-owner-actor", "letters, digits, dot, dash or underscore", CharacterAfterRunSettlementRules.MaximumTextLength);
        _ownerReviewId = AddText(body, "Owner review UUID", "sr5-after-run-entry-owner-review-id", "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx", 36);
        body.Add(NativeTheme.FieldLabel("Owner review reason / note"));
        _ownerReason = NativeTheme.TextArea("sr5-after-run-entry-owner-reason", string.Empty, "Optional bounded note");
        _ownerReason.MaxLength = CharacterAfterRunSettlementRules.MaximumTextLength;
        _ownerReason.TextChanged += (_, _) => RefreshEnabledState();
        body.Add(_ownerReason);
        _ownerApproved = AddCheck(
            body,
            "As the named character owner, I approve this exact run result",
            "sr5-after-run-entry-owner-approved");

        _status = NativeTheme.Body(
            "Complete every required typed identity, amount, policy value, and approval. No proposal is registered yet.",
            NativeTheme.Muted);
        _status.AutomationId = "sr5-after-run-entry-status";
        body.Add(_status);
        _publish = NativeTheme.PrimaryButton("Bind, validate, and publish proposal");
        _publish.AutomationId = "sr5-after-run-entry-publish";
        _publish.Clicked += async (_, _) => await RunAsync(PublishAsync);
        body.Add(_publish);
        Content = new ScrollView { Content = body };
        RefreshEnabledState();
    }

    protected override void Refresh() => RefreshEnabledState();

    private void AddContact()
    {
        if (!TryPendingContact(out CharacterAfterRunContactProposal contact))
        {
            _status.Text = "Enter a unique contact UUID, name, Connection, Loyalty, and origin before adding it.";
            _status.TextColor = NativeTheme.Danger;
            return;
        }
        _contacts.Add(contact);
        _contacts.Sort(static (left, right) => left.ContactId.CompareTo(right.ContactId));
        _contactId.Text = string.Empty;
        _contactName.Text = string.Empty;
        _contactRole.Text = string.Empty;
        _contactLocation.Text = string.Empty;
        _contactConnection.Text = string.Empty;
        _contactLoyalty.Text = string.Empty;
        RenderContacts();
        RefreshEnabledState();
    }

    private void RemoveLastContact()
    {
        if (_contacts.Count > 0)
        {
            _contacts.RemoveAt(_contacts.Count - 1);
        }
        RenderContacts();
        RefreshEnabledState();
    }

    private void RenderContacts()
    {
        _contactsSummary.Text = _contacts.Count == 0
            ? "No contacts added."
            : string.Join(
                "\n",
                _contacts.Select((contact, index) =>
                    $"{index + 1}. {contact.Name} · {contact.Connection}/{contact.Loyalty} · {contact.Kind} · {contact.ContactId:D}"));
        _removeContact.IsEnabled = _contacts.Count > 0;
    }

    private bool TryPendingContact(out CharacterAfterRunContactProposal contact)
    {
        contact = null!;
        if (!Guid.TryParse(_contactId.Text?.Trim(), out Guid id)
            || id == Guid.Empty
            || _contacts.Any(existing => existing.ContactId == id)
            || string.IsNullOrWhiteSpace(_contactName.Text)
            || !TryInt(_contactConnection, out int connection)
            || !TryInt(_contactLoyalty, out int loyalty)
            || connection <= 0
            || loyalty <= 0
            || _contactKind.SelectedIndex is < 0 or > 1)
        {
            return false;
        }
        contact = new CharacterAfterRunContactProposal(
            id,
            _contactName.Text.Trim(),
            _contactRole.Text?.Trim() ?? string.Empty,
            _contactLocation.Text?.Trim() ?? string.Empty,
            connection,
            loyalty,
            _contactKind.SelectedIndex == 0
                ? CharacterAfterRunContactProposalKind.RunReward
                : CharacterAfterRunContactProposalKind.KarmaPurchase);
        return Sr5AfterRunManualProposalSource.ValidContact(contact);
    }

    private async Task PublishAsync()
    {
        if (!TryCreateSubmission(
                out Sr5AfterRunManualProposalSubmission submission,
                out string blocker))
        {
            _status.Text = blocker;
            _status.TextColor = NativeTheme.Danger;
            return;
        }
        _publish.IsEnabled = false;
        Sr5AfterRunManualProposalPublishResult result = await Coordinator
            .PublishManualAfterRunProposalAsync(submission);
        if (!result.Published || result.Proposal is null)
        {
            _status.Text = result.Blocker;
            _status.TextColor = NativeTheme.Danger;
            RefreshEnabledState();
            return;
        }

        var authority = new Sr5AfterRunSettlementCoordinator(
            new RunnerSessionSr5AfterRunSettlementPresenter(Coordinator),
            new PreferencesSr5CareerCheckpointOwnerAuthority());
        Sr5AfterRunSettlementEditorState editor = await authority.PrepareAsync();
        if (editor.Status != Sr5AfterRunCatalogStatus.Available
            || editor.Candidates.Count(candidate =>
                candidate.Binding.Identity == result.Proposal.Projection.Identity) != 1)
        {
            _status.Text =
                "The proposal is durable, but Core did not return its exact quote. It remains unavailable and no character mutation occurred.";
            _status.TextColor = NativeTheme.Danger;
            return;
        }

        Page? previous = Navigation.NavigationStack.Count >= 2
            ? Navigation.NavigationStack[^2]
            : null;
        var replacement = new Sr5AfterRunSettlementWizardPage(Coordinator, editor);
        Navigation.InsertPageBefore(replacement, this);
        if (previous is Sr5AfterRunSettlementWizardPage)
        {
            Navigation.RemovePage(previous);
        }
        await Navigation.PopAsync();
    }

    private bool TryCreateSubmission(
        out Sr5AfterRunManualProposalSubmission submission,
        out string blocker)
    {
        submission = null!;
        blocker = "Enter every required run-result field and both explicit approvals.";
        if (!Guid.TryParse(_proposalId.Text?.Trim(), out Guid proposalId)
            || !Guid.TryParse(_runId.Text?.Trim(), out Guid runId)
            || !Guid.TryParse(_characterId.Text?.Trim(), out Guid characterId)
            || !DateTimeOffset.TryParse(
                _completedAt.Text?.Trim(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out DateTimeOffset completedAt)
            || !TryInt(_karmaAward, out int karmaAward)
            || !TryDecimal(_nuyenAward, out decimal nuyenAward)
            || !TryInt(_currentHeat, out int currentHeat)
            || !TryInt(_heatDelta, out int heatDelta)
            || !TryInt(_streetCredDelta, out int streetCredDelta)
            || !TryInt(_notorietyDelta, out int notorietyDelta)
            || !TryInt(_publicAwarenessDelta, out int publicAwarenessDelta)
            || !TryInt(_maximumHeat, out int maximumHeat)
            || !TryInt(_maximumReputation, out int maximumReputation)
            || !TryInt(_maximumConnection, out int maximumConnection)
            || !TryInt(_maximumLoyalty, out int maximumLoyalty)
            || !TryInt(_karmaPerContactPoint, out int karmaPerContactPoint)
            || !Guid.TryParse(_gmReviewId.Text?.Trim(), out Guid gmReviewId)
            || !Guid.TryParse(_ownerReviewId.Text?.Trim(), out Guid ownerReviewId)
            || string.IsNullOrWhiteSpace(_runTitle.Text)
            || string.IsNullOrWhiteSpace(_gmActorId.Text)
            || string.IsNullOrWhiteSpace(_ownerActorId.Text)
            || !_targetsRunner.IsChecked
            || !_runCompleted.IsChecked
            || !_gmApproved.IsChecked
            || !_ownerApproved.IsChecked)
        {
            return false;
        }

        submission = new Sr5AfterRunManualProposalSubmission(
            _workspaceId,
            _workspaceRevision,
            new CharacterAfterRunSettlementIdentity(proposalId, runId, characterId),
            _runTitle.Text,
            completedAt,
            karmaAward,
            nuyenAward,
            _rewardReceiptDigest.Text ?? string.Empty,
            _targetsRunner.IsChecked,
            _runCompleted.IsChecked,
            currentHeat,
            heatDelta,
            streetCredDelta,
            notorietyDelta,
            publicAwarenessDelta,
            new CharacterAfterRunSettlementSettings(
                maximumHeat,
                maximumReputation,
                maximumConnection,
                maximumLoyalty,
                karmaPerContactPoint,
                _allowRewardContacts.IsToggled,
                _allowPurchasedContacts.IsToggled,
                _calculatePublicAwareness.IsToggled),
            _contacts.ToArray(),
            _gmActorId.Text,
            gmReviewId,
            _gmReason.Text ?? string.Empty,
            _gmApproved.IsChecked,
            _ownerActorId.Text,
            ownerReviewId,
            _ownerReason.Text ?? string.Empty,
            _ownerApproved.IsChecked);
        blocker = string.Empty;
        return true;
    }

    private void RefreshEnabledState()
    {
        bool exactRunner = Coordinator.State.WorkspaceId == _workspaceId
            && Coordinator.State.ContentRevision == _workspaceRevision
            && Coordinator.State.SavedRevision == _workspaceRevision
            && !Coordinator.State.IsDirty
            && string.IsNullOrWhiteSpace(Coordinator.State.Error);
        _addContact.IsEnabled = TryPendingContact(out _);
        _removeContact.IsEnabled = _contacts.Count > 0;
        _publish.IsEnabled = exactRunner
            && TryCreateSubmission(out _, out _);
    }

    private Entry AddText(
        VerticalStackLayout body,
        string label,
        string automationId,
        string placeholder,
        int maxLength)
    {
        body.Add(NativeTheme.FieldLabel(label));
        Entry entry = NativeTheme.TextField(automationId, string.Empty, placeholder);
        entry.MaxLength = maxLength;
        entry.TextChanged += (_, _) => RefreshEnabledState();
        body.Add(entry);
        return entry;
    }

    private Entry AddNumber(
        VerticalStackLayout body,
        string label,
        string automationId,
        bool signed)
    {
        Entry entry = AddText(
            body,
            label,
            automationId,
            signed ? "Signed whole number" : "Non-negative number",
            32);
        entry.Keyboard = Keyboard.Numeric;
        return entry;
    }

    private CheckBox AddCheck(
        VerticalStackLayout body,
        string label,
        string automationId)
    {
        var check = new CheckBox
        {
            AutomationId = automationId,
            Color = NativeTheme.Signal
        };
        check.CheckedChanged += (_, _) => RefreshEnabledState();
        Grid row = ToggleRow(label, check);
        body.Add(NativeTheme.Card(row));
        return check;
    }

    private Switch AddSwitch(
        VerticalStackLayout body,
        string label,
        string automationId)
    {
        var value = new Switch
        {
            AutomationId = automationId,
            OnColor = NativeTheme.Signal
        };
        value.Toggled += (_, _) => RefreshEnabledState();
        body.Add(NativeTheme.Card(ToggleRow(label, value)));
        return value;
    }

    private static Grid ToggleRow(string label, View control)
    {
        var row = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            ColumnSpacing = 12
        };
        row.Add(NativeTheme.Body(label, NativeTheme.Text), 0, 0);
        row.Add(control, 1, 0);
        return row;
    }

    private static bool TryInt(Entry entry, out int value)
        => int.TryParse(
            entry.Text?.Trim(),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out value);

    private static bool TryDecimal(Entry entry, out decimal value)
        => decimal.TryParse(
                entry.Text?.Trim(),
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out value)
            || decimal.TryParse(
                entry.Text?.Trim(),
                NumberStyles.Number,
                CultureInfo.CurrentCulture,
                out value);
}
