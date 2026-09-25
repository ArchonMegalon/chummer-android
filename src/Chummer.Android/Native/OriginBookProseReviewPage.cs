using System.Globalization;
using Chummer.Contracts.LifeModules;
using Chummer.Presentation.OriginBooks;

namespace Chummer.Android.Native;

internal sealed class OriginBookProseReviewPage : NativePageBase
{
    private readonly RetainedOriginBook _book;
    private readonly OriginNarrativeChapterProjection _chapter;
    private readonly OriginBookProseDraft _draft;
    private readonly VerticalStackLayout _body = new() { Padding = 20, Spacing = 14 };
    private readonly AndroidSurfaceCopy _copy = AndroidSurfaceStrings.Resolve(CultureInfo.CurrentUICulture.Name);
    private bool _finished;

    internal OriginBookProseReviewPage(RunnerSessionCoordinator coordinator, RetainedOriginBook book,
        OriginNarrativeChapterProjection chapter, OriginBookProseDraft draft) : base(coordinator)
    {
        _book = book; _chapter = chapter; _draft = draft;
        Title = _copy["Origin.ReviewProse"]; AutomationId = "origin-prose-review";
        Content = new ScrollView { Content = _body };
    }

    protected override void Refresh()
    {
        _body.Clear();
        if (_finished || !Coordinator.IsRetainedOriginBookCurrent(_book)
            || _book.Pending(_chapter)?.DraftDigest != _draft.DraftDigest)
        { _body.Add(NativeTheme.Body(_copy[_finished ? "Origin.ProseReviewSaved" : "Origin.BookUnavailable"])); return; }
        _body.Add(NativeTheme.Title(_chapter.Title));
        _body.Add(NativeTheme.Body(_copy["Origin.ProseReviewNotice"], NativeTheme.Muted));
        bool matchesChapter = _draft.Matches(_chapter, _book.Locale);
        if (!matchesChapter) _body.Add(NativeTheme.Body(_copy["Origin.ProseStale"]));
        _body.Add(NativeTheme.Title(_copy["Origin.ProseCurrent"], 18));
        _body.Add(NativeTheme.BookProse(_book.ChapterText(_chapter)));
        _body.Add(NativeTheme.Title(_copy["Origin.ProseProposed"], 18));
        var prose = NativeTheme.BookProse(_draft.Text); prose.AutomationId = "origin-prose-proposal"; _body.Add(prose);
        _body.Add(NativeTheme.Body(_copy["Origin.ProseAcknowledgement"]));
        var acknowledgement = new Switch { AutomationId = "origin-prose-confirmed" };
        SemanticProperties.SetDescription(acknowledgement, _copy["Origin.ProseAcknowledgement"]);
        var use = NativeTheme.ReadingButton(_copy["Origin.ProseUse"]);
        use.IsEnabled = false; use.AutomationId = "origin-prose-use";
        var discard = NativeTheme.ReadingButton(_copy["Origin.ProseDiscard"]);
        discard.AutomationId = "origin-prose-discard";
        long appearance = CaptureAppearanceGeneration();
        bool Current() => !_finished && IsCurrentAppearanceGeneration(appearance)
            && Coordinator.IsRetainedOriginBookCurrent(_book) && _body.Contains(use);
        acknowledgement.Toggled += (_, args) => { if (Current()) use.IsEnabled = matchesChapter && args.Value; };
        async Task Decide(bool choose)
        {
            if (!Current() || choose && (!matchesChapter || !acknowledgement.IsToggled)) return;
            use.IsEnabled = discard.IsEnabled = acknowledgement.IsEnabled = false;
            var updated = await Coordinator.ReviewOriginBookProseDraftAsync(_book, _draft, choose,
                acknowledgement.IsToggled, Current, CancellationToken.None);
            if (updated is not null && IsCurrentAppearanceGeneration(appearance)
                && Coordinator.IsRetainedOriginBookCurrent(updated))
            {
                _finished = true;
                // Local adoption commits first. A lost/offline acknowledgement
                // cannot undo that book or turn a retry into new generation.
                if (choose) await Coordinator.RecordOriginBookReaderAcceptanceAsync(updated, _draft,
                    () => IsCurrentAppearanceGeneration(appearance), CancellationToken.None);
            }
        }
        use.Clicked += async (_, _) => await RunAsync(() => Decide(true));
        discard.Clicked += async (_, _) => await RunAsync(() => Decide(false));
        _body.Add(acknowledgement); _body.Add(use); _body.Add(discard);
    }

    protected override void OnDisappearing() { base.OnDisappearing(); _body.Clear(); }
}
