using Chummer.Contracts.LifeModules;

namespace Chummer.Android.Native;

internal sealed class OriginBookScenePage : NativePageBase
{
    private readonly VerticalStackLayout _body = new() { Padding = 20, Spacing = 14 };
    private readonly AndroidSurfaceCopy _copy;
    private readonly OriginNarrativeChapterProjection _chapter;
    private RetainedOriginBook _book;
    private OriginBookScene? _selection;
    private string _description;
    private string? _notice;
    private long _renderGeneration;

    internal OriginBookScenePage(RunnerSessionCoordinator coordinator, RetainedOriginBook book,
        OriginNarrativeChapterProjection chapter, AndroidSurfaceCopy copy) : base(coordinator)
    {
        _book = book; _chapter = chapter; _copy = copy;
        _description = book.Scene(chapter)?.Identity.AltText ?? "";
        Title = copy["Origin.SceneTitle"];
        AutomationId = "origin-book-scene";
        Content = new ScrollView { Content = _body };
    }

    protected override void Refresh()
    {
        _body.Clear();
        long render = ++_renderGeneration;
        long appearance = CaptureAppearanceGeneration();
        var book = _book;
        bool Current() => render == _renderGeneration && IsCurrentAppearanceGeneration(appearance) && ReferenceEquals(_book, book)
            && Coordinator.CanSelectOriginBookScene(book);
        if (!Current()) { _selection = null; _body.Add(NativeTheme.Body(_copy["Origin.BookUnavailable"])); return; }
        _body.Add(NativeTheme.Title(_chapter.Title));
        _body.Add(NativeTheme.Body(_copy["Origin.SceneExplanation"]));
        if (_notice is not null) _body.Add(NativeTheme.Body(_notice));
        var description = new Editor { Text = _description, MaxLength = 1024,
            Placeholder = _copy["Origin.SceneDescription"], AutoSize = EditorAutoSizeOption.TextChanges,
            TextColor = NativeTheme.Ink, BackgroundColor = NativeTheme.Paper,
            AutomationId = "origin-scene-description" };
        SemanticProperties.SetDescription(description, _copy["Origin.SceneDescription"]);
        _body.Add(description);
        var choose = NativeTheme.ReadingButton(_copy["Origin.SceneChoose"]);
        choose.AutomationId = "origin-scene-choose";
        choose.IsEnabled = !string.IsNullOrWhiteSpace(_description);
        choose.Clicked += async (_, _) => await RunAsync(async () =>
        {
            if (!Current() || string.IsNullOrWhiteSpace(_description)) return;
            try
            {
                var selected = await Coordinator.PickOriginBookSceneAsync(book, _chapter, _description, Current, default);
                if (Current()) { _selection = selected ?? _selection; _notice = null; }
            }
            catch (Exception error) when (error is InvalidDataException or FormatException)
            { if (Current()) _notice = _copy["Origin.SceneInvalid"]; }
        });
        _body.Add(choose);
        var preview = _selection ?? book.Scene(_chapter);
        if (preview is not null) _body.Add(SceneImage(preview, "origin-scene-preview", Current));
        var save = NativeTheme.ReadingButton(_copy["Origin.SceneSave"]);
        save.AutomationId = "origin-scene-save";
        save.IsEnabled = preview is not null && !string.IsNullOrWhiteSpace(_description);
        description.TextChanged += (_, _) =>
        {
            if (!Current()) return;
            _description = description.Text ?? "";
            choose.IsEnabled = !string.IsNullOrWhiteSpace(_description);
            save.IsEnabled = preview is not null && !string.IsNullOrWhiteSpace(_description);
        };
        save.Clicked += async (_, _) => await RunAsync(async () =>
        {
            if (!Current() || preview is null || string.IsNullOrWhiteSpace(_description)) return;
            try
            {
                var updated = await Coordinator.SaveOriginBookSceneAsync(book, _chapter,
                    preview.WithDescription(_description), true, Current, default);
                if (updated is not null) { _book = updated; _selection = null; await Navigation.PopAsync(); }
            }
            catch (InvalidDataException) { if (Current()) _notice = _copy["Origin.SceneInvalid"]; }
        });
        _body.Add(save);
        if (book.Scenes!.Scenes.Any(s => s.Identity.ChapterId == _chapter.ChapterId))
        {
            var remove = NativeTheme.ReadingButton(_copy["Origin.SceneRemove"]);
            remove.AutomationId = "origin-scene-remove";
            remove.Clicked += async (_, _) => await RunAsync(async () =>
            {
                if (!Current()) return;
                var updated = await Coordinator.SaveOriginBookSceneAsync(book, _chapter, null, true, Current, default);
                if (updated is not null) { _book = updated; _selection = null; await Navigation.PopAsync(); }
            });
            _body.Add(remove);
        }
    }

    internal static Image SceneImage(OriginBookScene scene, string automationId, Func<bool> isCurrent)
    {
        var image = new Image { Source = ImageSource.FromStream(() => isCurrent() ? scene.Open() : Stream.Null), Aspect = Aspect.AspectFit,
            HeightRequest = 240, AutomationId = automationId };
        SemanticProperties.SetDescription(image, scene.Identity.AltText);
        return image;
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _selection = null;
        _body.Clear();
    }
}
