using Chummer.Presentation.Overview;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Chummer.Android.Native.InteractionTests")]

namespace Chummer.Android.Native;

public sealed class NativeDialogPage : ContentPage
{
    private const string CompleteNewCharacterWorkflowActionId = "complete_new_character_workflow";
    private readonly RunnerSessionCoordinator _coordinator;
    private readonly NativeDialogInteractionGate _interactionGate = new();
    private readonly List<PendingTextField> _pendingTextFields = [];
    private DesktopDialogState? _renderedDialog;
    private long _renderGeneration;

    private sealed record PendingTextField(
        NativeDialogFieldBinding Binding,
        Func<string?> ReadValue);

    public NativeDialogPage(RunnerSessionCoordinator coordinator, DesktopDialogState dialog)
    {
        _coordinator = coordinator;
        BackgroundColor = NativeTheme.Paper;
        Title = dialog.Title;
        ToolbarItems.Add(new ToolbarItem
        {
            Text = "Close",
            Command = new Command(async () => await CloseAsync(updatePresenter: true))
        });
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
        VerticalStackLayout body = new()
        {
            AutomationId = "dialog-surface",
            Padding = new Thickness(20, 18, 20, 32),
            Spacing = 16
        };
        body.Add(NativeTheme.Eyebrow("Runner setup"));
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

        foreach (DesktopDialogField field in dialog.Fields)
        {
            if (string.Equals(field.LayoutSlot, DesktopDialogFieldLayoutSlots.Hidden, StringComparison.Ordinal))
            {
                continue;
            }

            body.Add(CreateField(dialog.Id, _renderGeneration, field));
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
            button.Clicked += async (_, _) => await ExecuteAsync(binding);
            actions.Add(button, index % 2, index / 2);
            index++;
        }
        if (dialog.Actions.Count > 0)
        {
            body.Add(actions);
        }

        Content = new ScrollView { Content = body };
    }

    private View CreateField(string dialogId, long renderGeneration, DesktopDialogField field)
    {
        VerticalStackLayout fieldLayout = new() { Spacing = 7 };
        Label label = NativeTheme.Body(field.Label);
        label.FontAttributes = FontAttributes.Bold;
        fieldLayout.Add(label);
        NativeDialogFieldBinding binding = CreateFieldBinding(dialogId, renderGeneration, field);

        if (string.Equals(field.InputType, "select", StringComparison.OrdinalIgnoreCase))
        {
            IReadOnlyList<DesktopDialogFieldOption> options = field.Options ?? [];
            int selectedIndex = options.ToList().FindIndex(option =>
                string.Equals(option.Value, field.Value, StringComparison.Ordinal));
            Picker picker = new()
            {
                AutomationId = $"dialog-field-{Token(field.Id)}",
                Title = string.IsNullOrWhiteSpace(field.Placeholder) ? $"Choose {field.Label}" : field.Placeholder,
                ItemsSource = options.Select(static option => option.Label).ToArray(),
                SelectedIndex = selectedIndex,
                IsEnabled = !field.IsReadOnly,
                BackgroundColor = NativeTheme.Surface,
                TextColor = NativeTheme.Text,
                HeightRequest = 52
            };
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
                IsEnabled = !field.IsReadOnly,
                OnColor = NativeTheme.Signal
            };
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
                    bool routeToCreationWizard = string.Equals(
                            action.Id,
                            CompleteNewCharacterWorkflowActionId,
                            StringComparison.Ordinal)
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
