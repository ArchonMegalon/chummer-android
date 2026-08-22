using Chummer.Contracts.Characters;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

public sealed class FreeSpriteConversionPage : NativePageBase
{
    private readonly FreeSpriteConversionEditorState _editor;
    private readonly Button _convert;

    public FreeSpriteConversionPage(
        RunnerSessionCoordinator coordinator,
        FreeSpriteConversionEditorState editor) : base(coordinator)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        if (!editor.Conversion.CanConvert
            || editor.Conversion.Revision.Length != CharacterFreeSpriteConversionRules.RevisionHexLength
            || editor.Conversion.Economics is not { KarmaDelta: 0, NuyenDelta: 0m })
        {
            throw new ArgumentException(
                "Convert to Free Sprite requires exact eligible Sprite state.",
                nameof(editor));
        }

        Title = "Convert to Free Sprite";
        AutomationId = "free-sprite-conversion-page";
        VerticalStackLayout body = new()
        {
            Padding = new Thickness(20, 18, 20, 40),
            Spacing = 14
        };
        body.Add(NativeTheme.Eyebrow(editor.Conversion.Created ? "Career Sprite" : "Creation Sprite"));
        body.Add(NativeTheme.Title("Become a Free Sprite"));
        body.Add(NativeTheme.Body(
            "Matches Chummer5: add Denial without counting toward the Critter Power limit, then set the metatype category to Free Sprite.",
            NativeTheme.Muted));
        Label summary = NativeTheme.Body(
            $"{editor.Conversion.CritterPowerIds.Count} saved Critter Powers · 0 Karma · 0 Nuyen",
            NativeTheme.Muted);
        summary.AutomationId = "free-sprite-conversion-summary";
        body.Add(NativeTheme.Card(summary));

        _convert = NativeTheme.PrimaryButton("Convert to Free Sprite");
        _convert.AutomationId = "free-sprite-conversion-save";
        _convert.Clicked += async (_, _) => await RunAsync(ConvertAsync);
        body.Add(_convert);
        Content = new ScrollView { Content = body };
        RefreshEnabledState();
    }

    protected override void Refresh() => RefreshEnabledState();

    private void RefreshEnabledState()
        => _convert.IsEnabled = Coordinator.State.WorkspaceId == _editor.WorkspaceId
            && Coordinator.State.ContentRevision == _editor.ContentRevision;

    private async Task ConvertAsync()
    {
        if (!CharacterFreeSpriteConversionRules.TryCreateIdentity(
                _editor.Conversion,
                Guid.NewGuid(),
                out CharacterFreeSpriteConversionIdentity identity))
        {
            await DisplayAlertAsync(
                "Conversion unavailable",
                "Reload this Sprite before converting it.",
                "OK");
            return;
        }

        await Coordinator.ApplyFreeSpriteConversionAsync(new FreeSpriteConversionRequest(
            _editor.WorkspaceId,
            _editor.ContentRevision,
            identity,
            _editor.Conversion.Revision));
        if (Coordinator.State.Error is null)
        {
            await Navigation.PopAsync();
        }
    }
}
