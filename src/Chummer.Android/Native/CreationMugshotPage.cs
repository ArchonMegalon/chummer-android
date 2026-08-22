using Chummer.Contracts.Characters;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

public sealed class CreationMugshotPage : NativePageBase
{
    private readonly CreationMugshotEditorState _editor;
    private readonly Image _preview;
    private readonly Label _index;
    private readonly Button _previous;
    private readonly Button _next;
    private readonly CheckBox _isMain;
    private readonly Button _save;
    private int _selectedOneBasedIndex;

    public CreationMugshotPage(
        RunnerSessionCoordinator coordinator,
        CreationMugshotEditorState editor) : base(coordinator)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        if (_editor.Items.Count != _editor.MugshotState.Mugshots.Count
            || _editor.Items.Where((item, index) =>
                item.Identity != _editor.MugshotState.Mugshots[index]).Any())
        {
            throw new ArgumentException(
                "Creation mugshot editing requires one exact ordered item for every typed identity.",
                nameof(editor));
        }

        Title = "Creation Mugshots";
        AutomationId = "creation-mugshot-page";
        _selectedOneBasedIndex = editor.MugshotState.DefaultSelectedOneBasedIndex;
        VerticalStackLayout body = new()
        {
            Padding = new Thickness(20, 18, 20, 40),
            Spacing = 14
        };
        body.Add(NativeTheme.Eyebrow("Creation runner portraits"));
        body.Add(NativeTheme.Title("Mugshots"));
        body.Add(NativeTheme.Body(
            "Browse the existing ordered Creation mugshot collection. Only the Main Mugshot checkbox is saved; adding, deleting and importing portraits are separate controls.",
            NativeTheme.Muted));

        _preview = new Image
        {
            AutomationId = "creation-mugshot-preview",
            Aspect = Aspect.AspectFit,
            HeightRequest = 260,
            BackgroundColor = NativeTheme.Surface
        };
        body.Add(NativeTheme.Card(_preview, new Thickness(10)));

        _previous = NativeTheme.SecondaryButton("Previous");
        _previous.AutomationId = "creation-mugshot-previous";
        _previous.Clicked += (_, _) => ChangeSelection(-1);
        _index = NativeTheme.Title(string.Empty, 20);
        _index.AutomationId = "creation-mugshot-index";
        _index.HorizontalTextAlignment = TextAlignment.Center;
        _next = NativeTheme.SecondaryButton("Next");
        _next.AutomationId = "creation-mugshot-next";
        _next.Clicked += (_, _) => ChangeSelection(1);
        Grid selector = new()
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star)
            },
            ColumnSpacing = 10
        };
        selector.Add(_previous, 0, 0);
        selector.Add(_index, 1, 0);
        selector.Add(_next, 2, 0);
        body.Add(selector);

        _isMain = new CheckBox
        {
            AutomationId = "creation-mugshot-main",
            Color = NativeTheme.Signal
        };
        _isMain.CheckedChanged += (_, _) => RefreshEnabledState();
        SemanticProperties.SetDescription(_isMain, "Is Main Mugshot");
        Grid mainRow = new()
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            }
        };
        mainRow.Add(NativeTheme.FieldLabel("Is Main Mugshot"), 0, 0);
        mainRow.Add(_isMain, 1, 0);
        body.Add(NativeTheme.Card(mainRow));

        _save = NativeTheme.PrimaryButton("Save Main Mugshot state");
        _save.AutomationId = "creation-mugshot-save";
        _save.Clicked += async (_, _) => await RunAsync(SaveAsync);
        body.Add(_save);
        Content = new ScrollView { Content = body };
        RenderSelection();
    }

    protected override void Refresh() => RefreshEnabledState();

    private void ChangeSelection(int delta)
    {
        _selectedOneBasedIndex = CharacterCreationMugshotRules.WrapSelection(
            _editor.MugshotState,
            _selectedOneBasedIndex + delta);
        RenderSelection();
    }

    private void RenderSelection()
    {
        CharacterMugshotIdentity? selected = CharacterCreationMugshotRules.ResolveSelection(
            _editor.MugshotState,
            _selectedOneBasedIndex);
        _index.Text = selected is null
            ? "0 of 0"
            : $"{_selectedOneBasedIndex} of {_editor.Items.Count}";
        if (selected is null)
        {
            _preview.Source = null;
            _isMain.IsChecked = false;
        }
        else
        {
            CreationMugshotEditorItem item = _editor.Items[selected.ZeroBasedIndex];
            byte[] imageBytes = Convert.FromBase64String(item.ImageBase64);
            _preview.Source = ImageSource.FromStream(() => new MemoryStream(imageBytes, writable: false));
            _isMain.IsChecked = CharacterCreationMugshotRules.IsSelectedMain(
                _editor.MugshotState,
                _selectedOneBasedIndex);
        }
        RefreshEnabledState();
    }

    private void RefreshEnabledState()
    {
        bool current = Coordinator.State.WorkspaceId == _editor.WorkspaceId
            && Coordinator.State.ContentRevision == _editor.ContentRevision;
        CharacterMugshotIdentity? selected = CharacterCreationMugshotRules.ResolveSelection(
            _editor.MugshotState,
            _selectedOneBasedIndex);
        bool hasSelection = selected is not null;
        _previous.IsEnabled = current && hasSelection;
        _next.IsEnabled = current && hasSelection;
        _isMain.IsEnabled = current && hasSelection;
        _save.IsEnabled = current
            && selected is not null
            && CharacterCreationMugshotRules.TryValidateMainMutation(
                _editor.MugshotState,
                selected,
                _editor.MugshotState.Revision,
                _isMain.IsChecked);
    }

    private async Task SaveAsync()
    {
        CharacterMugshotIdentity? selected = CharacterCreationMugshotRules.ResolveSelection(
            _editor.MugshotState,
            _selectedOneBasedIndex);
        if (selected is null)
        {
            await DisplayAlertAsync(
                "No mugshots",
                "This Creation runner has no existing mugshots to select.",
                "OK");
            return;
        }
        if (!CharacterCreationMugshotRules.TryValidateMainMutation(
                _editor.MugshotState,
                selected,
                _editor.MugshotState.Revision,
                _isMain.IsChecked))
        {
            await Navigation.PopAsync();
            return;
        }

        await Coordinator.ApplyCreationMugshotMainEditAsync(new CreationMugshotMainEditRequest(
            _editor.WorkspaceId,
            _editor.ContentRevision,
            selected,
            _editor.MugshotState.Revision,
            _isMain.IsChecked));
        if (Coordinator.State.Error is null)
        {
            await Navigation.PopAsync();
        }
    }
}
