using Chummer.Contracts.Characters;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

public sealed class TraditionSpiritCategoryPage : NativePageBase
{
    private sealed record SpiritOption(string Value, string Label);

    private readonly TraditionSpiritCategoryEditorState _editor;
    private readonly IReadOnlyDictionary<CharacterTraditionSpiritCategory, Picker> _pickers;
    private readonly Button _save;

    public TraditionSpiritCategoryPage(
        RunnerSessionCoordinator coordinator,
        TraditionSpiritCategoryEditorState editor) : base(coordinator)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        if (editor.Semantics.TraditionId == Guid.Empty
            || editor.Semantics.SourceId != CharacterTraditionNameRules.CustomMagicalTraditionSourceId
            || editor.Semantics.AllowedSpiritNames.Count == 0
            || editor.Semantics.AllowedSpiritNames[0].Length != 0
            || editor.Semantics.Fields.Count != CharacterTraditionSpiritCategoryRules.Categories.Count)
        {
            throw new ArgumentException(
                "Spirit-category editing requires exact Custom MAG catalog semantics and five field revisions.",
                nameof(editor));
        }

        Title = "Tradition spirits";
        AutomationId = "tradition-spirit-categories-page";
        VerticalStackLayout body = new()
        {
            Padding = new Thickness(20, 18, 20, 40),
            Spacing = 14
        };
        body.Add(NativeTheme.Eyebrow("Custom magical tradition"));
        body.Add(NativeTheme.Title("Spirit categories"));
        body.Add(NativeTheme.Body(
            "Choose from the exact active traditions.xml Spirit catalog after this runner's custom-data overlays and Limit Spirit Category effects.",
            NativeTheme.Muted));

        SpiritOption[] options = editor.Semantics.AllowedSpiritNames
            .Select(value => new SpiritOption(value, value.Length == 0 ? "None" : value))
            .ToArray();
        var pickers = new Dictionary<CharacterTraditionSpiritCategory, Picker>();
        foreach (CharacterTraditionSpiritCategoryFieldState field in editor.Semantics.Fields)
        {
            string token = Token(field.Category);
            Picker picker = new()
            {
                Title = Label(field.Category),
                ItemsSource = options,
                ItemDisplayBinding = new Binding(nameof(SpiritOption.Label)),
                SelectedItem = options.Single(option => string.Equals(
                    option.Value,
                    field.SpiritName,
                    StringComparison.Ordinal)),
                AutomationId = $"tradition-spirit-{token}-value"
            };
            pickers.Add(field.Category, picker);
            body.Add(NativeTheme.FieldLabel(Label(field.Category)));
            body.Add(picker);
        }
        _pickers = pickers;

        _save = NativeTheme.PrimaryButton("Save spirit categories");
        _save.AutomationId = "tradition-spirit-categories-save";
        _save.Clicked += async (_, _) => await RunAsync(SaveAsync);
        body.Add(_save);
        Content = new ScrollView { Content = body };
        RefreshEnabledState();
    }

    protected override void Refresh() => RefreshEnabledState();

    private void RefreshEnabledState()
    {
        bool revisionMatches = Coordinator.State.WorkspaceId == _editor.WorkspaceId
            && Coordinator.State.ContentRevision == _editor.ContentRevision;
        foreach (Picker picker in _pickers.Values)
        {
            picker.IsEnabled = revisionMatches;
        }
        _save.IsEnabled = revisionMatches;
    }

    private async Task SaveAsync()
    {
        var edits = new List<TraditionSpiritCategoryFieldEdit>(
            CharacterTraditionSpiritCategoryRules.Categories.Count);
        bool changed = false;
        foreach (CharacterTraditionSpiritCategoryFieldState field in _editor.Semantics.Fields)
        {
            if (!_pickers.TryGetValue(field.Category, out Picker? picker)
                || picker.SelectedItem is not SpiritOption selected
                || !CharacterTraditionSpiritCategoryRules.TryValidateRequestedValue(
                    _editor.Semantics,
                    field.Category,
                    field.Revision,
                    selected.Value,
                    out string validated))
            {
                await DisplayAlertAsync(
                    "Spirit category unavailable",
                    "Choose a value from the exact active Chummer5 Spirit catalog.",
                    "OK");
                return;
            }
            changed |= !string.Equals(validated, field.SpiritName, StringComparison.Ordinal);
            edits.Add(new TraditionSpiritCategoryFieldEdit(
                field.Category,
                field.Revision,
                validated));
        }
        if (!changed)
        {
            await Navigation.PopAsync();
            return;
        }

        await Coordinator.ApplyTraditionSpiritCategoryEditAsync(
            new TraditionSpiritCategoryEditRequest(
                _editor.WorkspaceId,
                _editor.ContentRevision,
                _editor.Semantics.TraditionId,
                _editor.Semantics.SourceId,
                edits));
        if (Coordinator.State.Error is null)
        {
            await Navigation.PopAsync();
        }
    }

    private static string Token(CharacterTraditionSpiritCategory category)
        => category.ToString().ToLowerInvariant();

    private static string Label(CharacterTraditionSpiritCategory category)
        => $"{category} spells";
}
