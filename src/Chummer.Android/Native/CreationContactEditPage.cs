using System.Globalization;
using Chummer.Contracts.Characters;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

/// <summary>Edits one stable Contact identity against one exact creation snapshot.</summary>
public sealed class CreationContactEditPage : NativePageBase
{
    private readonly Guid _contactId;
    private readonly CreationContactsPhoneDraft _draft = new();
    private readonly VerticalStackLayout _body = new()
    {
        Padding = new Thickness(20, 18, 20, 40),
        Spacing = 14
    };
    private Button? _previewButton;
    private IReadOnlyList<string> _prepareBlockers = [];

    internal CreationContactEditPage(
        RunnerSessionCoordinator coordinator,
        Guid contactId) : base(coordinator)
    {
        if (contactId == Guid.Empty)
            throw new ArgumentException("A stable Contact identity is required.", nameof(contactId));
        _contactId = contactId;
        Title = "Edit creation contact";
        AutomationId = "creation-contact-edit-page";
        Content = new ScrollView { Content = _body };
    }

    protected override void Refresh()
    {
        _body.Clear();
        _previewButton = null;
        _body.Add(NativeTheme.Eyebrow("Character creation · Contact"));
        var load = Coordinator.LoadCreationContacts();
        if (!string.Equals(load.Outcome, CharacterCreationContactOutcomes.Available, StringComparison.Ordinal)
            || load.State is not { } state
            || !CreationContactsPhoneAuthority.IsReady(state, Coordinator.State)
            || CreationContactsPhoneAuthority.ResolveUniqueContact(state, _contactId) is not { } contact)
        {
            AddBlockers(
                "Contact authority unavailable",
                load.Blockers.Count > 0
                    ? load.Blockers
                    : [CharacterCreationContactsBlockers.ContactNotFound],
                "creation-contact-edit-blockers");
            return;
        }

        _draft.Bind(state, contact);
        _body.Add(NativeTheme.Title(
            string.IsNullOrWhiteSpace(contact.Identity.Name)
                ? "Unnamed Contact"
                : contact.Identity.Name));
        AddBinding(state, contact);
        AddFieldProjection(state, contact);
        if (_prepareBlockers.Count > 0)
        {
            AddBlockers(
                "Preview blockers",
                _prepareBlockers,
                "creation-contact-preview-blockers");
        }
        AddPreviewAction(state, contact);
    }

    private void AddBinding(
        CharacterCreationContactsInteractionState state,
        CharacterCreationContactProjection contact)
    {
        Label binding = NativeTheme.Body(
            $"Revision {state.Binding.ContentRevision} · snapshot {ShortDigest(state.SnapshotDigest)} · "
            + $"Contact {contact.ContactId:D} · authority {ShortDigest(contact.ContactDigest)}",
            NativeTheme.Muted);
        binding.AutomationId = "creation-contact-edit-binding";
        _body.Add(binding);
    }

    private void AddFieldProjection(
        CharacterCreationContactsInteractionState state,
        CharacterCreationContactProjection contact)
    {
        if (contact.Fields.Count != CharacterCreationContactFieldIds.All.Count)
        {
            AddBlockers(
                "Field projection incomplete",
                [CharacterCreationContactsBlockers.AuthorityUnavailable],
                "creation-contact-fields-incomplete");
            return;
        }

        _body.Add(NativeTheme.Eyebrow("Nineteen typed fields"));
        foreach (CharacterCreationContactFieldAuthority field in contact.Fields)
        {
            VerticalStackLayout card = new() { Spacing = 7 };
            card.Add(NativeTheme.FieldLabel(field.Label));
            switch (field.ValueKind)
            {
                case CharacterCreationContactValueKinds.Text:
                    AddTextField(card, state, contact, field);
                    break;
                case CharacterCreationContactValueKinds.Integer:
                    AddIntegerField(card, state, contact, field);
                    break;
                case CharacterCreationContactValueKinds.Boolean:
                    AddBooleanField(card, state, contact, field);
                    break;
                default:
                    card.Add(NativeTheme.Body(
                        CharacterCreationContactsBlockers.AuthorityUnavailable,
                        NativeTheme.Danger));
                    break;
            }
            if (!field.IsEditable)
            {
                card.Add(NativeTheme.Body(
                    field.Blockers.FirstOrDefault()
                    ?? CharacterCreationContactsBlockers.FieldNotEditable,
                    NativeTheme.Danger));
            }
            card.Add(NativeTheme.Body(
                $"Source · {string.Join(" · ", field.SourceAnchorIds)}",
                NativeTheme.Muted));
            _body.Add(NativeTheme.Card(card, new Thickness(14)));
        }
    }

    private void AddTextField(
        VerticalStackLayout card,
        CharacterCreationContactsInteractionState state,
        CharacterCreationContactProjection contact,
        CharacterCreationContactFieldAuthority field)
    {
        string automationId = $"creation-contact-field-{field.FieldId}";
        string value = _draft.Value(state, contact, field.FieldId);
        InputView input = string.Equals(
            field.FieldId,
            CharacterCreationContactFieldIds.Notes,
            StringComparison.Ordinal)
            ? NativeTheme.TextArea(automationId, value)
            : NativeTheme.TextField(automationId, value);
        input.IsEnabled = field.IsEditable;
        input.MaxLength = field.Maximum ?? -1;
        input.TextChanged += (_, args) =>
        {
            _prepareBlockers = [];
            _draft.TrySetText(state, contact, field.FieldId, args.NewTextValue);
            UpdatePreviewEnabled(state, contact);
        };
        card.Add(input);
    }

    private void AddIntegerField(
        VerticalStackLayout card,
        CharacterCreationContactsInteractionState state,
        CharacterCreationContactProjection contact,
        CharacterCreationContactFieldAuthority field)
    {
        CharacterCreationContactOption[] options = field.LegalOptions
            .OrderBy(option => int.TryParse(
                    option.SerializedValue,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int value)
                ? value
                : int.MaxValue)
            .ToArray();
        Picker picker = new()
        {
            AutomationId = $"creation-contact-field-{field.FieldId}",
            Title = field.Label,
            IsEnabled = field.IsEditable
        };
        foreach (CharacterCreationContactOption option in options)
            picker.Items.Add(option.Label);
        string current = _draft.Value(state, contact, field.FieldId);
        picker.SelectedIndex = Array.FindIndex(options, option =>
            string.Equals(option.SerializedValue, current, StringComparison.Ordinal));
        picker.SelectedIndexChanged += (_, _) =>
        {
            if (picker.SelectedIndex < 0 || picker.SelectedIndex >= options.Length)
                return;
            if (int.TryParse(
                    options[picker.SelectedIndex].SerializedValue,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int selected))
            {
                _prepareBlockers = [];
                _draft.TrySetInteger(state, contact, field.FieldId, selected);
                UpdatePreviewEnabled(state, contact);
            }
        };
        card.Add(picker);
    }

    private void AddBooleanField(
        VerticalStackLayout card,
        CharacterCreationContactsInteractionState state,
        CharacterCreationContactProjection contact,
        CharacterCreationContactFieldAuthority field)
    {
        bool value = bool.TryParse(
            _draft.Value(state, contact, field.FieldId),
            out bool parsed) && parsed;
        Switch toggle = new()
        {
            AutomationId = $"creation-contact-field-{field.FieldId}",
            IsToggled = value,
            IsEnabled = field.IsEditable,
            HorizontalOptions = LayoutOptions.Start
        };
        toggle.Toggled += (_, args) =>
        {
            _prepareBlockers = [];
            _draft.TrySetBoolean(state, contact, field.FieldId, args.Value);
            UpdatePreviewEnabled(state, contact);
        };
        card.Add(toggle);
    }

    private void AddPreviewAction(
        CharacterCreationContactsInteractionState state,
        CharacterCreationContactProjection contact)
    {
        _previewButton = NativeTheme.PrimaryButton("Preview exact change");
        _previewButton.AutomationId = "creation-contact-preview";
        _previewButton.IsEnabled = _draft.HasChanges(state, contact);
        _previewButton.Clicked += async (_, _) => await PreparePreviewAsync(state, contact);
        _body.Add(_previewButton);

        Label scope = NativeTheme.Body(
            "This is a local typed draft. Core alone calculates budgets and the ordered atomic write plan. "
            + "A separate explicit confirmation is required; the global Build Save is not used.",
            NativeTheme.Muted);
        scope.AutomationId = "creation-contact-draft-scope";
        _body.Add(scope);
    }

    private void UpdatePreviewEnabled(
        CharacterCreationContactsInteractionState state,
        CharacterCreationContactProjection contact)
    {
        if (_previewButton is not null)
            _previewButton.IsEnabled = _draft.HasChanges(state, contact);
    }

    private async Task PreparePreviewAsync(
        CharacterCreationContactsInteractionState state,
        CharacterCreationContactProjection contact)
    {
        _prepareBlockers = [];
        CharacterCreationContactEditInput? input = _draft.ToInput(state, contact);
        if (input is null)
        {
            _prepareBlockers = [CharacterCreationContactsBlockers.MutationEmpty];
            Refresh();
            return;
        }

        var result = Coordinator.PrepareCreationContact(input);
        if (!string.Equals(result.Outcome, CharacterCreationContactOutcomes.Available, StringComparison.Ordinal)
            || result.State is not { } preparedState
            || result.PreparedPreview is not { } prepared
            || result.Blockers.Count > 0
            || !CreationContactsPhoneAuthority.PreparedMatches(
                prepared,
                preparedState,
                Coordinator.State))
        {
            _prepareBlockers = result.Blockers.Count > 0
                ? result.Blockers
                : [CharacterCreationContactsBlockers.AuthorityUnavailable];
            Refresh();
            return;
        }

        await Navigation.PushAsync(new CreationContactPreviewPage(Coordinator, prepared));
    }

    private void AddBlockers(string title, IReadOnlyList<string> blockers, string automationId)
    {
        VerticalStackLayout card = new() { Spacing = 6 };
        card.Add(NativeTheme.Eyebrow(title));
        foreach (string blocker in blockers.Distinct(StringComparer.Ordinal))
            card.Add(NativeTheme.Body(blocker, NativeTheme.Danger));
        Border border = NativeTheme.Card(card);
        border.AutomationId = automationId;
        _body.Add(border);
    }

    private static string ShortDigest(string value)
        => string.IsNullOrWhiteSpace(value) ? "unavailable" : value[..Math.Min(19, value.Length)];
}
