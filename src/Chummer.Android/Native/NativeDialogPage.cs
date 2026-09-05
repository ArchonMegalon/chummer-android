using Chummer.Presentation.Overview;
using System.Globalization;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Chummer.Android.Native.InteractionTests")]

namespace Chummer.Android.Native;

public sealed class NativeDialogPage : ContentPage
{
    private const string CreateCharacterActionId = "create_character";
    private const string CompleteNewCharacterWorkflowActionId = "complete_new_character_workflow";
    private readonly RunnerSessionCoordinator _coordinator;
    private readonly NativeDialogInteractionGate _interactionGate = new();
    private readonly List<PendingTextField> _pendingTextFields = [];
    private readonly List<InteractiveElement> _interactiveElements = [];
    private readonly ToolbarItem _closeToolbarItem;
    private DesktopDialogState? _renderedDialog;
    private ActivityIndicator? _busyIndicator;
    private Label? _busyLabel;
    private bool _interactionBusy;
    private long _renderGeneration;

    private sealed record PendingTextField(
        NativeDialogFieldBinding Binding,
        Func<string?> ReadValue);

    private sealed record InteractiveElement(
        VisualElement Element,
        bool EnabledWhenIdle);

    public NativeDialogPage(RunnerSessionCoordinator coordinator, DesktopDialogState dialog)
    {
        _coordinator = coordinator;
        BackgroundColor = NativeTheme.Paper;
        Title = dialog.Title;
        _closeToolbarItem = new ToolbarItem
        {
            Text = PhoneStrings.Get("Close", "Close"),
            Command = new Command(async () => await CloseAsync(updatePresenter: true))
        };
        ToolbarItems.Add(_closeToolbarItem);
        Render(dialog);
    }

    public event EventHandler? Closed;

    protected override bool OnBackButtonPressed()
    {
        _ = CloseAsync(updatePresenter: true);
        return true;
    }

    private void Render(DesktopDialogState dialog)
    {
        _renderGeneration = _interactionGate.BeginRender();
        _renderedDialog = dialog;
        _pendingTextFields.Clear();
        _interactiveElements.Clear();
        VerticalStackLayout body = new()
        {
            AutomationId = "dialog-surface",
            Padding = new Thickness(20, 18, 20, 32),
            Spacing = 16
        };
        body.Add(NativeTheme.Eyebrow(PhoneStrings.Get("RunnerSetup", "Runner setup")));
        body.Add(NativeTheme.Title(dialog.Title, 24));
        if (!string.IsNullOrWhiteSpace(_coordinator.State.Error))
        {
            Label errorLabel = NativeTheme.Body(_coordinator.State.Error!, NativeTheme.Danger);
            errorLabel.AutomationId = "dialog-error";
            body.Add(errorLabel);
        }
        if (!string.IsNullOrWhiteSpace(dialog.Message))
        {
            body.Add(NativeTheme.Body(dialog.Message, NativeTheme.Muted));
        }

        string? settingsScopeDetail = AndroidDialogSettingsScope.Detail(dialog);
        if (!string.IsNullOrWhiteSpace(settingsScopeDetail))
        {
            Label scopeLabel = NativeTheme.Body(settingsScopeDetail, NativeTheme.Muted);
            scopeLabel.AutomationId = "dialog-settings-scope";
            body.Add(NativeTheme.Card(scopeLabel));
        }

        foreach (DesktopDialogField field in dialog.Fields)
        {
            if (string.Equals(field.LayoutSlot, DesktopDialogFieldLayoutSlots.Hidden, StringComparison.Ordinal))
            {
                continue;
            }

            NativeDialogScopedField scopedField = AndroidDialogSettingsScope.Project(dialog, field);
            if (scopedField.IsVisible)
            {
                body.Add(CreateField(dialog.Id, _renderGeneration, field, scopedField));
            }
        }

        Grid actions = new()
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star)
            },
            ColumnSpacing = 10,
            RowSpacing = 10
        };
        int index = 0;
        foreach (DesktopDialogAction action in dialog.Actions)
        {
            NativeDialogActionBinding binding = new(
                _renderGeneration,
                dialog.Id,
                action.Id,
                action.Label,
                action.IsPrimary);
            Button button = action.IsPrimary
                ? NativeTheme.PrimaryButton(action.Label)
                : NativeTheme.SecondaryButton(action.Label);
            button.AutomationId = $"dialog-action-{Token(action.Id)}";
            TrackInteractive(button, enabledWhenIdle: true);
            button.Clicked += async (_, _) => await ExecuteAsync(binding);
            actions.Add(button, index % 2, index / 2);
            index++;
        }
        int actionsInsertIndex = body.Count;
        if (dialog.Actions.Count > 0)
        {
            body.Add(actions);
        }

        HorizontalStackLayout busy = new()
        {
            AutomationId = "dialog-busy",
            Spacing = 10,
            IsVisible = _interactionBusy,
            VerticalOptions = LayoutOptions.Center
        };
        _busyIndicator = new ActivityIndicator
        {
            AutomationId = "dialog-busy-indicator",
            Color = NativeTheme.Signal,
            IsRunning = _interactionBusy,
            IsVisible = _interactionBusy,
            VerticalOptions = LayoutOptions.Center
        };
        _busyLabel = NativeTheme.Body(
            PhoneStrings.Get("DialogApplyingChoice", "Applying your choice… This may take a moment."),
            NativeTheme.Muted);
        _busyLabel.AutomationId = "dialog-busy-label";
        _busyLabel.IsVisible = _interactionBusy;
        SemanticProperties.SetDescription(_busyLabel, _busyLabel.Text);
        busy.Add(_busyIndicator);
        busy.Add(_busyLabel);
        body.Insert(actionsInsertIndex, busy);

        Content = new ScrollView { Content = body };
        _closeToolbarItem.IsEnabled = !_interactionBusy;
    }

    private View CreateField(
        string dialogId,
        long renderGeneration,
        DesktopDialogField field,
        NativeDialogScopedField scopedField)
    {
        VerticalStackLayout fieldLayout = new() { Spacing = 7 };
        Label label = NativeTheme.Body(scopedField.Label);
        label.FontAttributes = FontAttributes.Bold;
        fieldLayout.Add(label);
        NativeDialogFieldBinding binding = CreateFieldBinding(dialogId, renderGeneration, field);

        if (string.Equals(field.InputType, "select", StringComparison.OrdinalIgnoreCase))
        {
            IReadOnlyList<DesktopDialogFieldOption> options = scopedField.Options ?? [];
            int selectedIndex = options.ToList().FindIndex(option =>
                string.Equals(option.Value, field.Value, StringComparison.Ordinal));
            Picker picker = new()
            {
                AutomationId = $"dialog-field-{Token(field.Id)}",
                Title = string.IsNullOrWhiteSpace(field.Placeholder)
                    ? $"Choose {scopedField.Label}"
                    : field.Placeholder,
                ItemsSource = options.Select(static option => option.Label).ToArray(),
                SelectedIndex = selectedIndex,
                IsEnabled = !field.IsReadOnly && !_interactionBusy,
                BackgroundColor = NativeTheme.Surface,
                TextColor = NativeTheme.Text,
                HeightRequest = 52
            };
            TrackInteractive(picker, enabledWhenIdle: !field.IsReadOnly);
            if (!field.IsReadOnly)
            {
                picker.SelectedIndexChanged += async (_, _) =>
                {
                    if (picker.SelectedIndex >= 0 && picker.SelectedIndex < options.Count)
                    {
                        await UpdateFieldAsync(binding, options[picker.SelectedIndex].Value);
                    }
                };
            }
            fieldLayout.Add(picker);
            return NativeTheme.Card(fieldLayout, new Thickness(14));
        }

        if (string.Equals(field.InputType, "checkbox", StringComparison.OrdinalIgnoreCase))
        {
            Switch toggle = new()
            {
                AutomationId = $"dialog-field-{Token(field.Id)}",
                IsToggled = bool.TryParse(field.Value, out bool enabled) && enabled,
                IsEnabled = !field.IsReadOnly && !_interactionBusy,
                OnColor = NativeTheme.Signal
            };
            TrackInteractive(toggle, enabledWhenIdle: !field.IsReadOnly);
            if (!field.IsReadOnly)
            {
                toggle.Toggled += async (_, args) =>
                    await UpdateFieldAsync(binding, args.Value ? "true" : "false");
            }
            fieldLayout.Add(toggle);
            return NativeTheme.Card(fieldLayout, new Thickness(14));
        }

        if (field.IsMultiline)
        {
            Editor editor = new()
            {
                AutomationId = $"dialog-field-{Token(field.Id)}",
                Text = field.Value,
                Placeholder = field.Placeholder,
                IsReadOnly = field.IsReadOnly,
                AutoSize = EditorAutoSizeOption.TextChanges,
                MinimumHeightRequest = 110,
                BackgroundColor = NativeTheme.Surface,
                TextColor = NativeTheme.Text
            };
            TrackInteractive(editor, enabledWhenIdle: !field.IsReadOnly);
            if (!field.IsReadOnly)
            {
                PendingTextField pending = new(binding, () => editor.Text);
                _pendingTextFields.Add(pending);
                editor.Unfocused += async (_, _) => await UpdateFieldAsync(binding, editor.Text);
            }
            fieldLayout.Add(editor);
        }
        else
        {
            Entry entry = new()
            {
                AutomationId = $"dialog-field-{Token(field.Id)}",
                Text = field.Value,
                Placeholder = field.Placeholder,
                IsReadOnly = field.IsReadOnly,
                BackgroundColor = NativeTheme.Surface,
                TextColor = NativeTheme.Text,
                Keyboard = string.Equals(field.InputType, "number", StringComparison.OrdinalIgnoreCase)
                    ? Keyboard.Numeric
                    : Keyboard.Default
            };
            TrackInteractive(entry, enabledWhenIdle: !field.IsReadOnly);
            if (!field.IsReadOnly)
            {
                PendingTextField pending = new(binding, () => entry.Text);
                _pendingTextFields.Add(pending);
                entry.Unfocused += async (_, _) => await UpdateFieldAsync(binding, entry.Text);
            }
            fieldLayout.Add(entry);
        }

        return NativeTheme.Card(fieldLayout, new Thickness(14));
    }

    private void TrackInteractive(VisualElement element, bool enabledWhenIdle)
    {
        element.IsEnabled = enabledWhenIdle && !_interactionBusy;
        _interactiveElements.Add(new InteractiveElement(element, enabledWhenIdle));
    }

    private void SetInteractionBusy(bool busy)
    {
        _interactionBusy = busy;
        _closeToolbarItem.IsEnabled = !busy;
        foreach (InteractiveElement interactive in _interactiveElements)
        {
            interactive.Element.IsEnabled = interactive.EnabledWhenIdle && !busy;
        }

        if (_busyIndicator is not null)
        {
            _busyIndicator.IsVisible = busy;
            _busyIndicator.IsRunning = busy;
        }
        if (_busyLabel is not null)
        {
            _busyLabel.IsVisible = busy;
        }
    }

    private static async Task YieldBusyFrameAsync()
    {
        await Task.Yield();
    }

    private static string Token(string value)
        => new(value.Trim().ToLowerInvariant().Select(character => char.IsLetterOrDigit(character) ? character : '-').ToArray());

    private static NativeDialogFieldBinding CreateFieldBinding(
        string dialogId,
        long renderGeneration,
        DesktopDialogField field)
        => new(
            renderGeneration,
            dialogId,
            field.Id,
            field.Label,
            field.Placeholder,
            field.InputType,
            field.IsMultiline,
            field.IsReadOnly,
            field.LayoutSlot,
            field.VisualKind,
            OptionsSignature(field.Options));

    private static string OptionsSignature(IReadOnlyList<DesktopDialogFieldOption>? options)
        => string.Concat((options ?? []).Select(static option =>
            $"{option.Value.Length}:{option.Value}{option.Label.Length}:{option.Label};"));

    private Task UpdateFieldAsync(NativeDialogFieldBinding binding, string? value)
        => _interactionGate.RunFieldUpdateAsync(binding.RenderGeneration, async () =>
        {
            try
            {
                DesktopDialogState? previous = _coordinator.State.ActiveDialog;
                if (!TryResolveActiveField(binding, out DesktopDialogField field)
                    || string.Equals(field.Value, value, StringComparison.Ordinal))
                {
                    return;
                }

                await _coordinator.UpdateDialogFieldAsync(binding.FieldId, value);
                DesktopDialogState? next = _coordinator.State.ActiveDialog;
                if (next is not null && RequiresStructuralRerender(previous, next, binding.FieldId))
                {
                    Title = next.Title;
                    Render(next);
                }
            }
            catch (Exception ex)
            {
                await HandleInteractionFailureAsync(ex);
            }
        });

    private async Task CommitPendingTextFieldsCoreAsync()
    {
        PendingTextField[] pending = _pendingTextFields.ToArray();
        foreach (PendingTextField pendingField in pending)
        {
            NativeDialogFieldBinding binding = pendingField.Binding;
            if (!TryResolveActiveField(binding, out DesktopDialogField field))
            {
                throw new InvalidOperationException(
                    $"Dialog field '{binding.FieldId}' changed before it could be committed.");
            }

            string? value = pendingField.ReadValue();
            if (!string.Equals(field.Value, value, StringComparison.Ordinal))
            {
                await _coordinator.UpdateDialogFieldAsync(binding.FieldId, value);
                if (!TryResolveActiveField(binding, out _))
                {
                    throw new InvalidOperationException(
                        $"Dialog field '{binding.FieldId}' changed shape while it was committed.");
                }
            }
        }
    }

    private bool TryResolveActiveField(
        NativeDialogFieldBinding binding,
        out DesktopDialogField field)
    {
        field = null!;
        DesktopDialogState? active = _coordinator.State.ActiveDialog;
        if (active is null || !_interactionGate.IsCurrentRender(binding.RenderGeneration))
        {
            return false;
        }

        DesktopDialogField[] matches = active.Fields
            .Where(candidate => string.Equals(candidate.Id, binding.FieldId, StringComparison.Ordinal))
            .Take(2)
            .ToArray();
        if (matches.Length != 1)
        {
            return false;
        }

        DesktopDialogField candidate = matches[0];
        if (!binding.Matches(
                _interactionGate.CurrentRenderGeneration,
                active.Id,
                candidate.Id,
                candidate.Label,
                candidate.Placeholder,
                candidate.InputType,
                candidate.IsMultiline,
                candidate.IsReadOnly,
                candidate.LayoutSlot,
                candidate.VisualKind,
                OptionsSignature(candidate.Options)))
        {
            return false;
        }

        field = candidate;
        return true;
    }

    private static bool RequiresStructuralRerender(
        DesktopDialogState? previous,
        DesktopDialogState next,
        string changedFieldId)
    {
        if (previous is null
            || !string.Equals(previous.Id, next.Id, StringComparison.Ordinal)
            || !string.Equals(previous.Title, next.Title, StringComparison.Ordinal)
            || !string.Equals(previous.Message, next.Message, StringComparison.Ordinal)
            || previous.Fields.Count != next.Fields.Count
            || previous.Actions.Count != next.Actions.Count)
        {
            return true;
        }

        Dictionary<string, DesktopDialogField> previousFields = previous.Fields
            .ToDictionary(static field => field.Id, StringComparer.Ordinal);
        foreach (DesktopDialogField nextField in next.Fields)
        {
            if (!previousFields.TryGetValue(nextField.Id, out DesktopDialogField? previousField)
                || !FieldShapeMatches(previousField, nextField)
                || (!string.Equals(nextField.Id, changedFieldId, StringComparison.Ordinal)
                    && !string.Equals(previousField.Value, nextField.Value, StringComparison.Ordinal)))
            {
                return true;
            }
        }

        return !previous.Actions.SequenceEqual(next.Actions);
    }

    private static bool FieldShapeMatches(DesktopDialogField previous, DesktopDialogField next)
        => string.Equals(previous.Label, next.Label, StringComparison.Ordinal)
            && string.Equals(previous.Placeholder, next.Placeholder, StringComparison.Ordinal)
            && previous.IsMultiline == next.IsMultiline
            && previous.IsReadOnly == next.IsReadOnly
            && string.Equals(previous.InputType, next.InputType, StringComparison.Ordinal)
            && string.Equals(previous.VisualKind, next.VisualKind, StringComparison.Ordinal)
            && string.Equals(previous.LayoutSlot, next.LayoutSlot, StringComparison.Ordinal)
            && OptionsMatch(previous.Options, next.Options);

    private static bool OptionsMatch(
        IReadOnlyList<DesktopDialogFieldOption>? previous,
        IReadOnlyList<DesktopDialogFieldOption>? next)
    {
        IReadOnlyList<DesktopDialogFieldOption> previousOptions = previous ?? [];
        IReadOnlyList<DesktopDialogFieldOption> nextOptions = next ?? [];
        return previousOptions.SequenceEqual(nextOptions);
    }

    private static bool DialogShapeMatches(DesktopDialogState rendered, DesktopDialogState active)
    {
        if (!string.Equals(rendered.Id, active.Id, StringComparison.Ordinal)
            || !string.Equals(rendered.Title, active.Title, StringComparison.Ordinal)
            || !string.Equals(rendered.Message, active.Message, StringComparison.Ordinal)
            || rendered.Fields.Count != active.Fields.Count
            || rendered.Actions.Count != active.Actions.Count
            || !rendered.Actions.SequenceEqual(active.Actions))
        {
            return false;
        }

        for (int index = 0; index < rendered.Fields.Count; index++)
        {
            DesktopDialogField renderedField = rendered.Fields[index];
            DesktopDialogField activeField = active.Fields[index];
            if (!string.Equals(renderedField.Id, activeField.Id, StringComparison.Ordinal)
                || !FieldShapeMatches(renderedField, activeField))
            {
                return false;
            }
        }

        return true;
    }

    private bool TryResolveActiveAction(
        NativeDialogActionBinding binding,
        out DesktopDialogAction action)
    {
        action = null!;
        DesktopDialogState? active = _coordinator.State.ActiveDialog;
        if (active is null
            || _renderedDialog is null
            || !_interactionGate.IsCurrentRender(binding.RenderGeneration)
            || !DialogShapeMatches(_renderedDialog, active))
        {
            return false;
        }

        DesktopDialogAction[] matches = active.Actions
            .Where(candidate => string.Equals(candidate.Id, binding.ActionId, StringComparison.Ordinal))
            .Take(2)
            .ToArray();
        if (matches.Length != 1)
        {
            return false;
        }

        DesktopDialogAction candidate = matches[0];
        if (!binding.Matches(
                _interactionGate.CurrentRenderGeneration,
                active.Id,
                candidate.Id,
                candidate.Label,
                candidate.IsPrimary))
        {
            return false;
        }

        action = candidate;
        return true;
    }

    private async Task ExecuteAsync(NativeDialogActionBinding binding)
    {
        if (!_interactionGate.TryClaimAction())
        {
            return;
        }

        SetInteractionBusy(true);
        await YieldBusyFrameAsync();
        try
        {
            await _interactionGate.RunClaimedActionAsync(
                async () =>
                {
                    await CommitPendingTextFieldsCoreAsync();
                    if (!TryResolveActiveAction(binding, out DesktopDialogAction action))
                    {
                        throw new InvalidOperationException(
                            $"Dialog action '{binding.ActionId}' changed before it could be executed.");
                    }

                    await _coordinator.ExecuteDialogActionAsync(action.Id);
                    DesktopDialogState? next = _coordinator.State.ActiveDialog;
                    if (next is null)
                    {
                        bool routeToCreationWizard = (string.Equals(
                                                           action.Id,
                                                           CreateCharacterActionId,
                                                           StringComparison.Ordinal)
                                                       || string.Equals(
                                                           action.Id,
                                                           CompleteNewCharacterWorkflowActionId,
                                                           StringComparison.Ordinal))
                            && _coordinator.State.WorkspaceId is not null
                            && _coordinator.State.Profile?.Created == false;
                        await CloseCoreAsync(updatePresenter: false);
                        if (routeToCreationWizard
                            && Shell.Current is Chummer.Android.MainShell { UsesTabletComposition: false } shell)
                        {
                            await shell.GoToAsync(PhoneShellRoutes.RunnerAbsolute);
                        }
                    }
                    else
                    {
                        Title = next.Title;
                        Render(next);
                    }
                },
                HandleInteractionFailureAsync);
        }
        finally
        {
            SetInteractionBusy(false);
        }
    }

    private async Task HandleInteractionFailureAsync(Exception ex)
    {
        RerenderActiveDialogIfOpen();
        await DisplayAlertAsync("Chummer", ex.Message, "OK");
    }

    private void RerenderActiveDialogIfOpen()
    {
        DesktopDialogState? active = _coordinator.State.ActiveDialog;
        if (_interactionGate.IsClosed || active is null)
        {
            return;
        }

        Title = active.Title;
        Render(active);
    }

    private Task CloseAsync(bool updatePresenter)
        => _interactionGate.RunCloseAsync(() => CloseCoreAsync(updatePresenter));

    private async Task CloseCoreAsync(bool updatePresenter)
    {
        if (_interactionGate.IsClosed)
        {
            return;
        }

        if (updatePresenter)
        {
            await _coordinator.CloseDialogAsync();
        }
        if (Navigation.ModalStack.Count > 0)
        {
            await Navigation.PopModalAsync();
        }
        _interactionGate.MarkClosed();
        Closed?.Invoke(this, EventArgs.Empty);
    }
}

internal sealed class NativeDialogInteractionGate
{
    private readonly object _sync = new();
    private Task _tail = Task.CompletedTask;
    private bool _actionClaimed;
    private bool _closeRequested;
    private bool _closed;
    private long _renderGeneration;

    public long CurrentRenderGeneration
    {
        get
        {
            lock (_sync)
            {
                return _renderGeneration;
            }
        }
    }

    public bool IsClosed
    {
        get
        {
            lock (_sync)
            {
                return _closed;
            }
        }
    }

    public long BeginRender()
    {
        lock (_sync)
        {
            if (_closed)
            {
                throw new InvalidOperationException("A closed dialog cannot be rendered again.");
            }

            return ++_renderGeneration;
        }
    }

    public bool IsCurrentRender(long renderGeneration)
    {
        lock (_sync)
        {
            return !_closed && renderGeneration == _renderGeneration;
        }
    }

    public bool TryClaimAction()
    {
        lock (_sync)
        {
            if (_closed || _closeRequested || _actionClaimed)
            {
                return false;
            }

            _actionClaimed = true;
            return true;
        }
    }

    public async Task RunClaimedActionAsync(
        Func<Task> operation,
        Func<Exception, Task> onFailure)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(onFailure);
        lock (_sync)
        {
            if (!_actionClaimed)
            {
                throw new InvalidOperationException("The dialog action was not claimed.");
            }
        }

        try
        {
            await EnqueueAsync(async () =>
            {
                try
                {
                    await operation();
                }
                catch (Exception ex)
                {
                    await onFailure(ex);
                }
            });
        }
        finally
        {
            lock (_sync)
            {
                _actionClaimed = false;
            }
        }
    }

    public Task RunFieldUpdateAsync(long renderGeneration, Func<Task> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return EnqueueAsync(async () =>
        {
            if (!IsCurrentRender(renderGeneration))
            {
                return;
            }

            await operation();
        });
    }

    public Task RunCloseAsync(Func<Task> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        lock (_sync)
        {
            if (_closed || _closeRequested)
            {
                return Task.CompletedTask;
            }

            _closeRequested = true;
        }

        return RunClaimedCloseAsync(operation);
    }

    public void MarkClosed()
    {
        lock (_sync)
        {
            if (_closed)
            {
                return;
            }

            _closed = true;
            _closeRequested = true;
            _renderGeneration++;
        }
    }

    private async Task RunClaimedCloseAsync(Func<Task> operation)
    {
        try
        {
            await EnqueueAsync(async () =>
            {
                if (IsClosed)
                {
                    return;
                }

                await operation();
                MarkClosed();
            });
        }
        finally
        {
            lock (_sync)
            {
                if (!_closed)
                {
                    _closeRequested = false;
                }
            }
        }
    }

    private Task EnqueueAsync(Func<Task> operation)
    {
        Task predecessor;
        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_sync)
        {
            predecessor = _tail;
            _tail = completion.Task;
        }

        return RunAfterAsync(predecessor, operation, completion);
    }

    private static async Task RunAfterAsync(
        Task predecessor,
        Func<Task> operation,
        TaskCompletionSource completion)
    {
        try
        {
            await predecessor;
            await operation();
        }
        finally
        {
            completion.TrySetResult();
        }
    }
}

internal sealed record NativeDialogScopedField(
    bool IsVisible,
    string Label,
    IReadOnlyList<DesktopDialogFieldOption>? Options);

/// <summary>
/// Applies Android-owned capability semantics to the desktop-compatible dialog projection without
/// changing dialog, field, option, or persisted settings identities. Character-settings sections
/// are an explicit allowlist: an unknown section or an unexpected custom-data field shape is hidden
/// until the phone has deliberately classified it. Every other dialog passes through unchanged so
/// creation and career wizard behavior remains presentation-owned.
/// </summary>
internal static class AndroidDialogSettingsScope
{
    internal const string CharacterSettingsDialogId = "dialog.character_settings";
    internal const string ProfileFieldId = "characterSettingsProfile";
    internal const string ProfileNameFieldId = "characterSettingsProfileName";
    internal const string SectionFieldId = "characterSettingsSection";
    internal const string LoadedProfileFieldId = "characterSettingsLoadedProfile";
    internal const string DraftXmlFieldId = "characterSettingsDraftXml";
    internal const string ControlFieldPrefix = "characterSettingsControl-";
    internal const string CustomDataSectionId = "custom-data";
    internal const string CustomDataFieldId = "characterSettingsControl-treCustomDataDirectories";

    private static readonly HashSet<string> KnownRulesSections = new(StringComparer.Ordinal)
    {
        "ware",
        "sourcebooks",
        "rules",
        "formulas",
        "karma",
        "limits",
        "build"
    };

    private static readonly HashSet<string> KnownSections = new(KnownRulesSections, StringComparer.Ordinal)
    {
        CustomDataSectionId
    };

    private static readonly HashSet<string> StructuralFieldIds = new(StringComparer.Ordinal)
    {
        ProfileFieldId,
        ProfileNameFieldId,
        SectionFieldId,
        LoadedProfileFieldId,
        DraftXmlFieldId
    };

    internal static NativeDialogScopedField Project(
        DesktopDialogState dialog,
        DesktopDialogField field,
        CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        ArgumentNullException.ThrowIfNull(field);

        if (!IsCharacterSettings(dialog))
        {
            return new NativeDialogScopedField(true, field.Label, field.Options);
        }

        if (string.Equals(field.Id, SectionFieldId, StringComparison.Ordinal))
        {
            if (!IsExpectedSectionField(field))
            {
                return new NativeDialogScopedField(false, field.Label, field.Options);
            }

            DesktopDialogFieldOption[] options = (field.Options ?? [])
                .Where(option => KnownSections.Contains(option.Value))
                .Select(option => string.Equals(option.Value, CustomDataSectionId, StringComparison.Ordinal)
                    ? new DesktopDialogFieldOption(
                        option.Value,
                        PhoneStrings.Get(
                            "CharacterSettingsCustomDataSection",
                            "Custom data (desktop compatibility)",
                            culture))
                    : option)
                .ToArray();
            return new NativeDialogScopedField(true, field.Label, options);
        }

        if (StructuralFieldIds.Contains(field.Id))
        {
            return new NativeDialogScopedField(true, field.Label, field.Options);
        }

        string? sectionId = SelectedSectionId(dialog);
        if (sectionId is not null
            && KnownRulesSections.Contains(sectionId)
            && field.Id.StartsWith(ControlFieldPrefix, StringComparison.Ordinal))
        {
            return new NativeDialogScopedField(true, field.Label, field.Options);
        }

        if (string.Equals(sectionId, CustomDataSectionId, StringComparison.Ordinal)
            && IsExpectedCustomDataField(field))
        {
            return new NativeDialogScopedField(
                true,
                PhoneStrings.Get(
                    "CharacterSettingsCustomDataField",
                    "Custom data entries (desktop profile compatibility)",
                    culture),
                field.Options);
        }

        return new NativeDialogScopedField(false, field.Label, field.Options);
    }

    internal static string? Detail(DesktopDialogState dialog, CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        if (!IsCharacterSettings(dialog))
        {
            return null;
        }

        string? sectionId = SelectedSectionId(dialog);
        if (string.Equals(sectionId, CustomDataSectionId, StringComparison.Ordinal))
        {
            return PhoneStrings.Get(
                "CharacterSettingsCustomDataScope",
                "Android uses bundled game data. These entries stay in the settings profile for desktop custom-data compatibility.",
                culture);
        }

        if (sectionId is not null && KnownRulesSections.Contains(sectionId))
        {
            return PhoneStrings.Get(
                "CharacterSettingsRulesScope",
                "These runner rules belong to the settings profile and apply across Chummer platforms.",
                culture);
        }

        return PhoneStrings.Get(
            "CharacterSettingsUnsupportedScope",
            "This settings section is not available on Android. Its saved values remain in the profile.",
            culture);
    }

    private static bool IsCharacterSettings(DesktopDialogState dialog)
        => string.Equals(dialog.Id, CharacterSettingsDialogId, StringComparison.Ordinal);

    private static string? SelectedSectionId(DesktopDialogState dialog)
    {
        DesktopDialogField[] matches = dialog.Fields
            .Where(field => string.Equals(field.Id, SectionFieldId, StringComparison.Ordinal))
            .Take(2)
            .ToArray();
        return matches.Length == 1 && KnownSections.Contains(matches[0].Value)
            ? matches[0].Value
            : null;
    }

    private static bool IsExpectedSectionField(DesktopDialogField field)
        => string.Equals(field.InputType, "select", StringComparison.OrdinalIgnoreCase)
            && !field.IsReadOnly
            && !field.IsMultiline
            && field.Options is not null;

    private static bool IsExpectedCustomDataField(DesktopDialogField field)
        => string.Equals(field.Id, CustomDataFieldId, StringComparison.Ordinal)
            && string.Equals(field.InputType, "text", StringComparison.OrdinalIgnoreCase)
            && !field.IsReadOnly
            && field.IsMultiline
            && (field.Options is null || field.Options.Count == 0);
}

internal sealed record NativeDialogFieldBinding(
    long RenderGeneration,
    string DialogId,
    string FieldId,
    string Label,
    string Placeholder,
    string InputType,
    bool IsMultiline,
    bool IsReadOnly,
    string LayoutSlot,
    string VisualKind,
    string OptionsSignature)
{
    public bool Matches(
        long currentRenderGeneration,
        string dialogId,
        string fieldId,
        string label,
        string placeholder,
        string inputType,
        bool isMultiline,
        bool isReadOnly,
        string layoutSlot,
        string visualKind,
        string optionsSignature)
        => RenderGeneration == currentRenderGeneration
            && string.Equals(DialogId, dialogId, StringComparison.Ordinal)
            && string.Equals(FieldId, fieldId, StringComparison.Ordinal)
            && string.Equals(Label, label, StringComparison.Ordinal)
            && string.Equals(Placeholder, placeholder, StringComparison.Ordinal)
            && string.Equals(InputType, inputType, StringComparison.Ordinal)
            && IsMultiline == isMultiline
            && IsReadOnly == isReadOnly
            && !isReadOnly
            && string.Equals(LayoutSlot, layoutSlot, StringComparison.Ordinal)
            && string.Equals(VisualKind, visualKind, StringComparison.Ordinal)
            && string.Equals(OptionsSignature, optionsSignature, StringComparison.Ordinal);
}

internal sealed record NativeDialogActionBinding(
    long RenderGeneration,
    string DialogId,
    string ActionId,
    string Label,
    bool IsPrimary)
{
    public bool Matches(
        long currentRenderGeneration,
        string dialogId,
        string actionId,
        string label,
        bool isPrimary)
        => RenderGeneration == currentRenderGeneration
            && string.Equals(DialogId, dialogId, StringComparison.Ordinal)
            && string.Equals(ActionId, actionId, StringComparison.Ordinal)
            && string.Equals(Label, label, StringComparison.Ordinal)
            && IsPrimary == isPrimary;
}
