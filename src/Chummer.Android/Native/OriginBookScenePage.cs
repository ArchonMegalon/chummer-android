using Chummer.Contracts.LifeModules;
using Chummer.Android.Platform;
using System.Text;

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
    private string _excerpt = "";
    private bool _remoteSelection;
    private long _renderGeneration;

    internal OriginBookScenePage(RunnerSessionCoordinator coordinator, RetainedOriginBook book,
        OriginNarrativeChapterProjection chapter, AndroidSurfaceCopy copy) : base(coordinator)
    {
        _book = book; _chapter = chapter; _copy = copy;
        _description = book.Scene(chapter)?.Identity.AltText ?? "";
        _excerpt = ExcerptFromChapter(book.ChapterText(chapter));
        Title = copy["Origin.SceneTitle"];
        AutomationId = "origin-book-scene";
        Content = new ScrollView { Content = _body };
    }

    internal static string ExcerptFromChapter(string text)
    {
        // Reader prose is a Label, not a copy/paste editor. Offer a literal
        // first paragraph for review, never an invented prompt or consent.
        int paragraphEnd = text.IndexOf("\n\n", StringComparison.Ordinal);
        int crlfEnd = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        if (crlfEnd >= 0 && (paragraphEnd < 0 || crlfEnd < paragraphEnd)) paragraphEnd = crlfEnd;
        string paragraph = paragraphEnd >= 0 ? text[..paragraphEnd] : text;
        int bytes = 0, characters = 0;
        foreach (var rune in paragraph.EnumerateRunes())
        {
            if (bytes + rune.Utf8SequenceLength > 3072) break;
            bytes += rune.Utf8SequenceLength; characters += rune.Utf16SequenceLength;
        }
        return paragraph[..characters].TrimEnd();
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
                if (Current())
                {
                    if (selected is not null) { _selection = selected; _remoteSelection = false; }
                    _notice = null;
                }
            }
            catch (Exception error) when (error is InvalidDataException or FormatException)
            { if (Current()) _notice = _copy["Origin.SceneInvalid"]; }
        });
        _body.Add(choose);
        if (Coordinator.CanRequestOriginBookScene(book, _chapter))
        {
            _body.Add(NativeTheme.Body(_copy["Origin.SceneRemoteExplanation"]));
            var excerpt = new Editor { Text = _excerpt, MaxLength = 3072,
                Placeholder = _copy["Origin.SceneExcerpt"], AutoSize = EditorAutoSizeOption.TextChanges,
                TextColor = NativeTheme.Ink, BackgroundColor = NativeTheme.Paper, AutomationId = "origin-scene-excerpt" };
            SemanticProperties.SetDescription(excerpt, _copy["Origin.SceneExcerpt"]);
            _body.Add(excerpt);
            _body.Add(NativeTheme.Body(_copy["Origin.SceneConsent"]));
            var consent = new Switch { IsToggled = false, AutomationId = "origin-scene-consent" };
            SemanticProperties.SetDescription(consent, _copy["Origin.SceneConsent"]);
            _body.Add(consent);
            var request = NativeTheme.ReadingButton(_copy["Origin.SceneRequest"]);
            request.AutomationId = "origin-scene-request";
            request.IsEnabled = false;
            var read = NativeTheme.ReadingButton(_copy["Origin.SceneRead"]);
            read.AutomationId = "origin-scene-read";
            var busy = new ActivityIndicator { IsVisible = false, AutomationId = "origin-scene-busy", Color = NativeTheme.Ink };
            var waiting = NativeTheme.Body(_copy["Origin.SceneWaiting"]);
            waiting.IsVisible = false;
            void UpdateRequest() => request.IsEnabled = Current() && consent.IsToggled
                && RunnerSessionCoordinator.ValidOriginSceneExcerpt(book.ChapterText(_chapter), _excerpt, _description);
            excerpt.TextChanged += (_, _) => { if (Current()) { _excerpt = excerpt.Text ?? ""; UpdateRequest(); } };
            description.TextChanged += (_, _) => { if (Current()) { _description = description.Text ?? ""; UpdateRequest(); } };
            consent.Toggled += (_, _) => UpdateRequest();
            async Task Synchronize(bool create)
            {
                if (!Current() || create && !request.IsEnabled) return;
                string approvedExcerpt = _excerpt, approvedAltText = _description;
                consent.IsToggled = false; // one explicit request, never retained blanket consent
                busy.IsVisible = busy.IsRunning = waiting.IsVisible = true;
                try
                {
                    var synced = await Coordinator.SyncOriginBookSceneAsync(book, _chapter, approvedExcerpt,
                        approvedAltText, create, Current, default);
                    if (!Current()) return;
                    _notice = SceneNotice(synced.Result);
                    if (synced.Scene is { } scene)
                    { _selection = scene; _remoteSelection = true; _description = scene.Identity.AltText; }
                    else if (_remoteSelection) { _selection = null; _remoteSelection = false; }
                }
                finally { busy.IsVisible = busy.IsRunning = waiting.IsVisible = false; }
            }
            request.Clicked += async (_, _) => await RunAsync(() => Synchronize(true));
            read.Clicked += async (_, _) => await RunAsync(() => Synchronize(false));
            _body.Add(request); _body.Add(read); _body.Add(busy); _body.Add(waiting);
        }
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
                if (_remoteSelection)
                {
                    var decision = await Coordinator.DecideOriginBookSceneAsync(book, _chapter, preview,
                        true, true, Current, default);
                    if (!Current()) return;
                    if (decision.Outcome != AndroidOriginSceneOutcome.Available || decision.State != "persisted")
                    { _notice = SceneNotice(decision); return; }
                }
                var updated = await Coordinator.SaveOriginBookSceneAsync(book, _chapter,
                    preview.WithDescription(_description), true, Current, default);
                if (updated is not null) { _book = updated; _selection = null; await Navigation.PopAsync(); }
            }
            catch (InvalidDataException) { if (Current()) _notice = _copy["Origin.SceneInvalid"]; }
        });
        _body.Add(save);
        if (_remoteSelection && _selection is { } remote)
        {
            var reject = NativeTheme.ReadingButton(_copy["Origin.SceneReject"]);
            reject.AutomationId = "origin-scene-reject";
            reject.Clicked += async (_, _) => await RunAsync(async () =>
            {
                if (!Current()) return;
                var decision = await Coordinator.DecideOriginBookSceneAsync(book, _chapter, remote, false, true, Current, default);
                if (!Current()) return;
                _notice = SceneNotice(decision);
                if (decision.Outcome == AndroidOriginSceneOutcome.Available && decision.State == "rejected")
                { _selection = null; _remoteSelection = false; }
            });
            _body.Add(reject);
        }
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

    private string SceneNotice(AndroidOriginSceneResult result) => _copy[result.UnknownRemoteOutcome
        ? "Origin.SceneUnknown" : result.Outcome switch
        {
            AndroidOriginSceneOutcome.NotFound => "Origin.SceneAbsent",
            AndroidOriginSceneOutcome.Conflict => "Origin.SceneConflict",
            AndroidOriginSceneOutcome.Available => result.State switch
            {
                "review" or "persisted" => "Origin.SceneReview",
                "rejected" or "expired" => "Origin.SceneClosed",
                _ => "Origin.SceneUnknown"
            },
            _ => "Origin.SceneUnavailable"
        }];

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
        _remoteSelection = false;
        _excerpt = "";
        _body.Clear();
    }
}
