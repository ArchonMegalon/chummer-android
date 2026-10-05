using Chummer.Contracts.Characters;
using Chummer.Contracts.LifeModules;
using Chummer.Presentation.OriginBooks;

namespace Chummer.Android.Native;

/// <summary>
/// Phone rendering for a Core-validated live Life Module decision. Callers own
/// prepare/confirm orchestration; this page never calculates or applies an
/// effect itself.
/// </summary>
internal sealed class OriginDossierLifeModuleDecisionPage : ContentPage
{
    private OriginDossierLifeModuleDecisionState _state;
    private CharacterCreationBudgetState _budget;
    private string _foundationSnapshotDigest;
    private string _boundContentDigest;
    private string _boundSourceDigest;
    private string _boundMechanicsSnapshotDigest;
    private readonly OriginDossierNarrativeLocaleBinding _locale;
    private readonly AndroidSurfaceCopy _copy;
    private Chummer.Contracts.LifeModules.LifeModuleOriginDossierDraftCheckpoint? _storyCheckpoint;
    private readonly Func<string, IReadOnlyDictionary<string, string>?, Task<OriginDossierLifeModulePhoneResult?>> _prepareChoice;
    private readonly Func<string, string, Task<OriginDossierLifeModulePhoneResult?>> _confirmChoice;
    private readonly Func<Task>? _openBook;
    private readonly Func<LifeModuleOriginDossierDraftCheckpoint, Func<bool>, Task<bool>>? _readStoryReady;
    private readonly Func<LifeModuleOriginDossierDraftCheckpoint, Func<bool>, Task>? _openStoryProgress;
    private bool _storyReady;
    private bool _checkingStory;
    private string? _presentedStoryDigest;
    private string? _selectedMetatypeOptionId;
    private int _renderGeneration;
    private bool _actionInFlight;
    private string? _editingChoiceId;
    private readonly Dictionary<string, string> _answers = new(StringComparer.Ordinal);
    private readonly HashSet<string> _explicitCityAnswers = new(StringComparer.Ordinal);
    private OriginBookReadingState? _openingDetails;
    private OriginStoryProfile _storyProfile = new();
    private bool _detailsExpanded;
    private readonly Func<OriginBookReadingState, OriginStoryProfile?, Func<bool>, Task<OriginBookReadingState?>>? _saveOpeningDetails;
    private readonly Func<LifeModuleOriginDossierDraftCheckpoint, Func<bool>, Task<OriginBookReadingState?>>? _loadOpeningDetails;

    public OriginDossierLifeModuleDecisionPage(
        OriginDossierLifeModulePhoneResult opened,
        string activeAppLocale,
        Func<string, IReadOnlyDictionary<string, string>?, Task<OriginDossierLifeModulePhoneResult?>> prepareChoice,
        Func<string, string, Task<OriginDossierLifeModulePhoneResult?>> confirmChoice,
        Func<Task>? openBook = null,
        Func<LifeModuleOriginDossierDraftCheckpoint, Func<bool>, Task<bool>>? readStoryReady = null,
        OriginBookReadingState? openingDetails = null,
        Func<OriginBookReadingState, OriginStoryProfile?, Func<bool>, Task<OriginBookReadingState?>>? saveOpeningDetails = null,
        Func<LifeModuleOriginDossierDraftCheckpoint, Func<bool>, Task>? openStoryProgress = null,
        Func<LifeModuleOriginDossierDraftCheckpoint, Func<bool>, Task<OriginBookReadingState?>>? loadOpeningDetails = null)
    {
        ArgumentNullException.ThrowIfNull(opened);
        if (!TryReadDisplayAuthority(
                opened,
                out OriginDossierLifeModuleDecisionState state,
                out CharacterCreationBudgetState budget))
        {
            throw new InvalidOperationException(
                "The Origin Dossier decision has no exact Life Modules budget authority.");
        }
        _state = state;
        _storyCheckpoint = opened.StoryCheckpoint;
        _budget = budget;
        _foundationSnapshotDigest = opened.FoundationSnapshotDigest!;
        _boundContentDigest = opened.BoundContentDigest!;
        _boundSourceDigest = opened.BoundSourceDigest!;
        _boundMechanicsSnapshotDigest = opened.BoundMechanicsSnapshotDigest!;
        OriginDossierNarrativeLocaleBinding locale =
            OriginDossierNarrativeLocalePolicy.Resolve(activeAppLocale);
        _copy = AndroidSurfaceStrings.Resolve(activeAppLocale);
        if (!locale.CanRenderNarrativeLocale(_state.Locale)
            || string.IsNullOrWhiteSpace(_state.BoundTurnSeedDigest))
        {
            throw new InvalidOperationException(
                "The Origin Dossier decision is not bound to the active app language.");
        }
        _locale = locale;
        _prepareChoice = prepareChoice ?? throw new ArgumentNullException(nameof(prepareChoice));
        _confirmChoice = confirmChoice ?? throw new ArgumentNullException(nameof(confirmChoice));
        _openBook = openBook;
        _readStoryReady = readStoryReady;
        _openStoryProgress = openStoryProgress;
        _openingDetails = openingDetails;
        _storyProfile = openingDetails?.StoryProfile ?? new();
        _saveOpeningDetails = saveOpeningDetails;
        _loadOpeningDetails = loadOpeningDetails;
        _selectedMetatypeOptionId = _state.Choices.Where(choice => choice.IsSelected)
            .Select(MetatypeEffect).SingleOrDefault()?.TargetId;
        Title = _copy["Origin.PageTitle"];
        AutomationId = "origin-life-decision";
        // NativeTheme uses fixed dark text, including outside the white cards.
        // Match NativePageBase instead of inheriting the OS dark background.
        BackgroundColor = NativeTheme.Paper;
        Content = new ScrollView { Content = BuildBody() };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (await RefreshStoryReadinessAsync()) await LeadToUnreadStoryAsync();
    }

    protected override void OnDisappearing()
    {
        ++_renderGeneration;
        _storyReady = false;
        base.OnDisappearing();
    }

    private bool NeedsStoryBeforeChoices => _storyCheckpoint?.Projection.CurrentTurn.JourneyId == "sr5-life-modules-foundation"
        && _state.StageOrder > LifeModuleJourneyStageOrders.FormativeYears;

    private async Task LeadToUnreadStoryAsync()
    {
        if (_storyReady || !NeedsStoryBeforeChoices || _storyCheckpoint is not { } checkpoint
            || _openStoryProgress is null || _presentedStoryDigest == checkpoint.Projection.SeedDigest) return;
        int generation = _renderGeneration;
        bool Current() => generation == _renderGeneration && ReferenceEquals(_storyCheckpoint, checkpoint);
        // Once per saved chapter, not on every Back/appearance. Returning from
        // an unfinished chapter must never trap the reader in a navigation loop.
        _presentedStoryDigest = checkpoint.Projection.SeedDigest;
        try { await _openStoryProgress(checkpoint, Current); }
        catch (Exception error) when (error is IOException or InvalidOperationException
            or OperationCanceledException or UnauthorizedAccessException)
        {
            if (Current()) _presentedStoryDigest = null;
        }
    }

    private async Task<bool> RefreshStoryReadinessAsync()
    {
        _storyReady = false;
        _checkingStory = NeedsStoryBeforeChoices;
        Content = new ScrollView { Content = BuildBody() };
        int generation = _renderGeneration;
        var checkpoint = _storyCheckpoint;
        bool Current() => generation == _renderGeneration && ReferenceEquals(_storyCheckpoint, checkpoint);
        if (_loadOpeningDetails is not null && checkpoint is not null)
        {
            // A confirmed module advances the workspace revision; returning
            // from the reader may also freeze the first authoring source.
            // Reacquire edit authority instead of keeping a stale picker alive.
            bool wasInFlight = _actionInFlight;
            _actionInFlight = true;
            OriginBookReadingState? details = null;
            try { details = await _loadOpeningDetails(checkpoint, Current); }
            catch (Exception error) when (error is IOException or InvalidOperationException or OperationCanceledException
                or UnauthorizedAccessException or System.Text.Json.JsonException) { }
            finally { _actionInFlight = wasInFlight; }
            if (!Current()) return false;
            if (_openingDetails?.Digest != details?.Digest) _storyProfile = details?.StoryProfile ?? new();
            _openingDetails = details;
            Content = new ScrollView { Content = BuildBody() };
            generation = _renderGeneration;
        }
        if (!_checkingStory) return false;
        bool ready = false;
        try
        {
            if (checkpoint is not null && _readStoryReady is not null)
                ready = await _readStoryReady(checkpoint, Current);
        }
        catch (Exception error) when (error is OperationCanceledException or IOException or InvalidOperationException)
        {
            // No reading proof is no permission to advance. Keep the book and
            // explicit refresh available; never manufacture a read acceptance.
        }
        if (!Current()) return false;
        _checkingStory = false;
        _storyReady = ready;
        Content = new ScrollView { Content = BuildBody() };
        return true;
    }

    private VerticalStackLayout BuildBody()
    {
        int generation = ++_renderGeneration;
        var body = new VerticalStackLayout
        {
            Padding = new Thickness(20, 18, 20, 40),
            Spacing = 14
        };
        body.Add(NativeTheme.Title(_state.RunnerDisplayName));
        string localeCopy = _openingDetails is not null
            ? _copy.Format("Origin.BookLanguage", _storyProfile.StoryLanguage ?? _state.Locale)
            : _locale.UsesEnglishFallback
            ? _copy.Format("Origin.LocaleFallback", _locale.FormattingLocale)
            : _copy.Format("Origin.Locale", _locale.ResourceLanguage.ToUpperInvariant(), _locale.FormattingLocale);
        Label locale = NativeTheme.Body(localeCopy, NativeTheme.Muted);
        locale.AutomationId = "origin-life-locale";
        SemanticProperties.SetDescription(
            locale,
            _openingDetails is not null ? localeCopy : _copy.Format(
                "Origin.LocaleSemantic",
                _locale.ResourceLanguage,
                _locale.FormattingLocale,
                _copy[_locale.UsesEnglishFallback ? "Common.Yes" : "Common.No"]));
        body.Add(locale);
        AddStoryLanguage(body, generation, locale);
        if (_storyCheckpoint?.Projection.CurrentTurn.JourneyId == "sr5-life-modules-foundation"
            && _state.StageOrder <= LifeModuleJourneyStageOrders.FormativeYears)
        {
            var setup = NativeTheme.Body(_copy["Origin.OpeningSetupRequired"], NativeTheme.Muted);
            setup.AutomationId = "origin-life-opening-setup";
            body.Add(setup);
        }
        AddOpeningDetails(body, generation);

        if (_storyCheckpoint?.Projection.VisibleChapters.Count > 0)
        {
            Button readBook = NativeTheme.SecondaryButton(_copy["Origin.ReadBook"]);
            readBook.AutomationId = "origin-life-read-book";
            readBook.Clicked += async (_, _) =>
            {
                if (_actionInFlight || generation != _renderGeneration) return;
                if (_openBook is not null) await _openBook();
                else await Navigation.PushAsync(new OriginDossierBookPage(_storyCheckpoint, _locale.FormattingLocale));
            };
            body.Add(readBook);
        }

        if (NeedsStoryBeforeChoices && !_storyReady)
        {
            var waiting = NativeTheme.Body(_copy[_checkingStory
                ? "Origin.StoryReadChecking" : "Origin.StoryReadRequired"]);
            waiting.AutomationId = "origin-life-story-wait";
            body.Add(NativeTheme.Card(waiting));
            if (_checkingStory)
                body.Add(new ActivityIndicator { IsRunning = true, AutomationId = "origin-life-story-checking" });
            else
            {
                var refresh = NativeTheme.ReadingButton(_copy["Origin.StoryReadRefresh"]);
                refresh.AutomationId = "origin-life-story-refresh";
                refresh.Clicked += async (_, _) =>
                {
                    if (_actionInFlight || generation != _renderGeneration) return;
                    _actionInFlight = true;
                    try
                    {
                        if (!await RefreshStoryReadinessAsync() || _storyReady
                            || _storyCheckpoint is not { } checkpoint || _openStoryProgress is null) return;
                        int readingGeneration = _renderGeneration;
                        bool Current() => readingGeneration == _renderGeneration
                            && ReferenceEquals(_storyCheckpoint, checkpoint);
                        // The reader observes or starts the exact saved chapter;
                        // it never marks prose as read just by opening it.
                        await _openStoryProgress(checkpoint, Current);
                    }
                    catch (Exception error) when (error is IOException or InvalidOperationException
                        or OperationCanceledException or UnauthorizedAccessException)
                    {
                        if (!_checkingStory && ReferenceEquals(Navigation.NavigationStack.LastOrDefault(), this))
                            await DisplayAlertAsync(_copy["Origin.PageTitle"],
                                _copy["Origin.AuthoringStatusInterrupted"], _copy["Common.Ok"]);
                    }
                    finally { _actionInFlight = false; }
                };
                body.Add(refresh);
            }
            return body;
        }

        // The canonical wizard lead-in is not an authored chapter. Once the
        // reader returns from the full chapter, ask the next story question
        // without reintroducing the source template or its mechanics prompt.
        Label prompt = NativeTheme.Title(OriginStoryDecisionText.Prompt(_state), 21);
        prompt.AutomationId = "origin-life-prompt";
        body.Add(prompt);
        if (!_state.Choices.Any(IsAffordable))
            body.Add(NativeTheme.Body(_copy["Origin.NoStoryChoices"], NativeTheme.Danger));

        // This is a view filter over Core's exact composite Foundation choices,
        // not a metatype mutation. Only the subsequent explicit confirmation
        // persists metatype + nationality together, through the Core authority.
        OriginDossierLifeModuleEffectState[] metatypes = _state.Choices
            .Where(IsAffordable)
            .Select(MetatypeEffect).OfType<OriginDossierLifeModuleEffectState>()
            .GroupBy(effect => effect.TargetId, StringComparer.Ordinal)
            .Select(group => group.First()).OrderBy(effect => effect.AfterValue, StringComparer.Ordinal)
            .ToArray();
        if (metatypes.Length > 0)
        {
            body.Add(NativeTheme.Eyebrow(_copy["Origin.ChooseMetatype"]));
            body.Add(NativeTheme.Body(_copy["Origin.MetatypeStoryDetail"], NativeTheme.Muted));
            foreach (OriginDossierLifeModuleEffectState metatype in metatypes)
            {
                Button selectMetatype = _selectedMetatypeOptionId == metatype.TargetId
                    ? NativeTheme.PrimaryButton(metatype.AfterValue)
                    : NativeTheme.SecondaryButton(metatype.AfterValue);
                selectMetatype.AutomationId = "origin-life-metatype-" + metatype.TargetId;
                selectMetatype.Clicked += (_, _) =>
                {
                    if (_actionInFlight || generation != _renderGeneration)
                        return;
                    _selectedMetatypeOptionId = metatype.TargetId;
                    Content = new ScrollView { Content = BuildBody() };
                };
                body.Add(selectMetatype);
            }
            if (_selectedMetatypeOptionId is not null)
                body.Add(NativeTheme.Eyebrow(_copy["Origin.ChooseNationality"]));
        }

        // A selection replaces the scroll content. Put its questions/review
        // immediately below the runner name, ahead of the long setup header as
        // well as alternatives, so the new top viewport exposes the next action.
        // Keep the stable choice identities and the whole-body save guard.
        foreach (int choiceIndex in Enumerable.Range(0, _state.Choices.Count)
                     .Where(index => IsAffordable(_state.Choices[index]))
                     .OrderByDescending(index => _state.Choices[index].ChoiceId == _editingChoiceId)
                     .ThenByDescending(index => _state.Choices[index].IsSelected)
                     .ThenByDescending(index => IsSelectionFinish(_state.Choices[index])))
        {
            OriginDossierLifeModuleChoiceState choice = _state.Choices[choiceIndex];
            if (metatypes.Length > 0 && MetatypeEffect(choice)?.TargetId != _selectedMetatypeOptionId)
                continue;
            var card = new VerticalStackLayout { Spacing = 8 };
            string storyChoice = OriginStoryDecisionText.Choice(_state, choice.ChoiceId);
            Button select = choice.IsSelected
                ? NativeTheme.PrimaryButton(storyChoice)
                : NativeTheme.SecondaryButton(storyChoice);
            select.HeightRequest = -1;
            select.MinimumHeightRequest = 50;
            select.LineBreakMode = LineBreakMode.WordWrap;
            select.AutomationId = $"origin-life-choice-{choiceIndex}";
            string choiceId = choice.ChoiceId;
            select.Clicked += async (_, _) =>
            {
                if (_actionInFlight || generation != _renderGeneration)
                    return;
                if (FollowUps(choiceId).Count > 0)
                {
                    BeginEditing(choiceId);
                    Content = new ScrollView { Content = BuildBody() };
                    return;
                }
                _editingChoiceId = null;
                _actionInFlight = true;
                select.IsEnabled = false;
                try
                {
                    OriginDossierLifeModulePhoneResult? prepared = await _prepareChoice(choiceId, null);
                    if (prepared is not null && generation == _renderGeneration && TryAdoptPrepared(prepared))
                        Content = new ScrollView { Content = BuildBody() };
                }
                finally { _actionInFlight = false; select.IsEnabled = true; }
            };
            card.Add(select);

            if (choiceId == _editingChoiceId)
            {
                AddInputForm(card, choiceId, generation);
                body.Insert(1, NativeTheme.Card(card));
                continue;
            }
            if (!choice.IsSelected || _editingChoiceId is not null)
            {
                body.Add(NativeTheme.Card(card));
                continue;
            }
            if (_storyCheckpoint?.PendingPreview?.InputResolution is { } answers)
                foreach (var question in FollowUps(choiceId))
                    if (answers.Values.TryGetValue(question.PromptId, out string? answer))
                        card.Add(NativeTheme.Body((question.DisplayLabel ?? question.Label) + ": " + answer));

            // Text-only is the Origin product surface, not a preference. Keep
            // the exact compiler review as confirmation authority underneath;
            // never render its costs, ratings or source anchors as story prose.
            var review = _storyCheckpoint?.PendingPreview?.EffectReview;
            if (review is null)
                card.Add(NativeTheme.Body(_copy["Origin.StoryChoiceReviewRequired"], NativeTheme.Muted));
            body.Insert(1, NativeTheme.Card(card));
            AddConfirmation(body, generation);
        }

        return body;
    }

    private bool IsOpeningDecision => _storyCheckpoint?.Projection.CurrentTurn.JourneyId == "sr5-life-modules-foundation"
        && _storyCheckpoint.Projection.CanonicalLayer.AcceptedDecisionIds.Count == 0;

    private void AddStoryLanguage(VerticalStackLayout body, int generation, Label languageLabel)
    {
        if (_openingDetails is not { } initial || _saveOpeningDetails is null) return;
        if (initial.Chapters.Count != 0)
        {
            body.Add(NativeTheme.Body(_copy["Origin.StoryLanguageFrozen"], NativeTheme.Muted));
            return;
        }
        string[] languages = ["de-DE", "en-US", "es-ES"];
        var picker = new Picker { Title = _copy["Origin.StoryLanguageChoose"], TextColor = NativeTheme.Ink,
            TitleColor = NativeTheme.Muted, BackgroundColor = NativeTheme.Paper,
            AutomationId = "origin-story-language" };
        picker.Items.Add(_copy.Format("Origin.StoryLanguageDefault", _state.Locale));
        foreach (string name in new[] { "Deutsch", "English", "Español" }) picker.Items.Add(name);
        picker.SelectedIndex = Array.IndexOf(languages, _storyProfile.StoryLanguage!) + 1;
        var status = NativeTheme.Body(_copy["Origin.StoryLanguageDetail"], NativeTheme.Muted);
        status.AutomationId = "origin-story-language-status";
        bool Current() => generation == _renderGeneration;
        picker.SelectedIndexChanged += async (_, _) =>
        {
            if (!Current() || _actionInFlight || picker.SelectedIndex < 0 || picker.SelectedIndex > languages.Length
                || _openingDetails is not { Chapters.Count: 0 } expected) return;
            string? language = picker.SelectedIndex == 0 ? null : languages[picker.SelectedIndex - 1];
            if (language == expected.StoryProfile?.StoryLanguage) return;
            _actionInFlight = true;
            body.IsEnabled = false;
            status.Text = _copy["Origin.StoryLanguageSaving"];
            OriginBookReadingState? saved = null;
            try
            {
                // Selecting a language does not silently commit other, still
                // unconfirmed opening details or submit a chapter request.
                var profile = (expected.StoryProfile ?? new OriginStoryProfile()) with { StoryLanguage = language };
                saved = await _saveOpeningDetails(expected, profile.IsEmpty ? null : profile, Current);
                if (!Current()) return;
                if (saved is not null)
                {
                    _openingDetails = saved;
                    _storyProfile = _storyProfile with { StoryLanguage = language };
                    languageLabel.Text = _copy.Format("Origin.BookLanguage", language ?? _state.Locale);
                    SemanticProperties.SetDescription(languageLabel, languageLabel.Text);
                }
            }
            catch (Exception error) when (error is IOException or InvalidOperationException or OperationCanceledException
                or UnauthorizedAccessException or System.Text.Json.JsonException) { }
            finally
            {
                if (Current())
                {
                    if (saved is null) picker.SelectedIndex = Array.IndexOf(languages, expected.StoryProfile?.StoryLanguage!) + 1;
                    status.Text = _copy[saved is null ? "Origin.StoryLanguageSaveFailed" : "Origin.StoryLanguageSaved"];
                    body.IsEnabled = true;
                }
                _actionInFlight = false;
            }
        };
        body.Add(picker);
        body.Add(status);
    }

    private void AddOpeningDetails(VerticalStackLayout body, int generation)
    {
        if (!IsOpeningDecision || _openingDetails is not { Chapters.Count: 0 } || _saveOpeningDetails is null) return;
        var section = new VerticalStackLayout { Spacing = 10, IsVisible = _detailsExpanded,
            AutomationId = "origin-story-details" };
        var toggle = NativeTheme.SecondaryButton(_copy[_detailsExpanded ? "Origin.SetupCollapse" : "Origin.SetupExpand"]);
        toggle.AutomationId = "origin-story-details-expand";
        bool Current() => generation == _renderGeneration && !_actionInFlight;
        toggle.Clicked += (_, _) =>
        {
            if (!Current()) return;
            _detailsExpanded = !_detailsExpanded;
            section.IsVisible = _detailsExpanded;
            toggle.Text = _copy[_detailsExpanded ? "Origin.SetupCollapse" : "Origin.SetupExpand"];
        };
        body.Add(toggle);
        section.Add(NativeTheme.Body(_copy["Origin.SetupDetail"], NativeTheme.Muted));
        void Select(string id, string key, string prefix, string[] values, string? selected, Action<string?> changed)
        {
            section.Add(NativeTheme.Body(_copy[key]));
            var picker = new Picker { Title = _copy[key], TextColor = NativeTheme.Ink,
                TitleColor = NativeTheme.Muted, BackgroundColor = NativeTheme.Paper, AutomationId = id };
            picker.Items.Add(_copy["Origin.SetupUnspecified"]);
            foreach (var value in values) picker.Items.Add(_copy[prefix + value]);
            picker.SelectedIndex = Array.IndexOf(values, selected!) + 1;
            picker.SelectedIndexChanged += (_, _) =>
            { if (Current()) changed(picker.SelectedIndex > 0 ? values[picker.SelectedIndex - 1] : null); };
            section.Add(picker);
        }
        void Text(string id, string key, string? value, int max, Action<string?> changed,
            string[] suggestions, string? hint = null)
        {
            section.Add(NativeTheme.Body(_copy[key]));
            if (hint is not null) section.Add(NativeTheme.Body(_copy[hint], NativeTheme.Muted));
            string[] options = suggestions.Select(s => _copy["Origin.Suggestion." + s]).ToArray();
            int selected = Array.IndexOf(options, value!);
            int customIndex = options.Length + 1;
            var picker = new Picker { Title = _copy[key], TextColor = NativeTheme.Ink,
                TitleColor = NativeTheme.Muted, BackgroundColor = NativeTheme.Paper, AutomationId = id + "-suggestions" };
            picker.Items.Add(_copy["Origin.SetupUnspecified"]);
            foreach (string option in options) picker.Items.Add(option);
            picker.Items.Add(_copy["Origin.SetupCustom"]);
            // Existing free text remains editable; merely rendering a suggestion
            // must never replace it or invent a detail for an unspecified field.
            picker.SelectedIndex = value is null ? 0 : selected >= 0 ? selected + 1 : customIndex;
            var entry = new Entry { Text = value, MaxLength = max, TextColor = NativeTheme.Ink,
                BackgroundColor = NativeTheme.Paper, AutomationId = id,
                Placeholder = _copy["Origin.SetupCustom"], IsVisible = picker.SelectedIndex == customIndex };
            entry.TextChanged += (_, args) =>
            { if (Current() && picker.SelectedIndex == customIndex)
                changed(string.IsNullOrWhiteSpace(args.NewTextValue) ? null : args.NewTextValue.Trim()); };
            picker.SelectedIndexChanged += (_, _) =>
            {
                if (!Current()) return;
                entry.IsVisible = picker.SelectedIndex == customIndex;
                changed(entry.IsVisible
                    ? (string.IsNullOrWhiteSpace(entry.Text) ? null : entry.Text.Trim())
                    : picker.SelectedIndex > 0 ? options[picker.SelectedIndex - 1] : null);
            };
            section.Add(picker);
            section.Add(entry);
        }
        Select("origin-story-gender", "Origin.SetupGender", "Origin.Gender.", ["male", "female", "other"],
            _storyProfile.Gender, value => _storyProfile = _storyProfile with { Gender = value });
        Text("origin-story-pronouns", "Origin.SetupPronouns", _storyProfile.Pronouns, 120,
            value => _storyProfile = _storyProfile with { Pronouns = value }, ["she", "he", "they"]);
        Select("origin-story-tone", "Origin.SetupTone", "Origin.Tone.", ["dark", "cheerful", "hopeful", "epic", "mixed"],
            _storyProfile.Tone, value => _storyProfile = _storyProfile with { Tone = value });
        Text("origin-story-motivation", "Origin.SetupMotivation", _storyProfile.Motivation, 512,
            value => _storyProfile = _storyProfile with { Motivation = value }, ["belonging", "freedom", "protect"]);
        Text("origin-story-person", "Origin.SetupPerson", _storyProfile.ImportantPerson, 512,
            value => _storyProfile = _storyProfile with { ImportantPerson = value }, ["friend", "sibling", "mentor"]);
        section.Add(NativeTheme.Body(_copy["Origin.BackgroundDetail"], NativeTheme.Muted));
        var background = _storyProfile.Background ?? new OriginStoryBackground();
        void Background(Func<OriginStoryBackground, OriginStoryBackground> change)
        {
            var next = change(_storyProfile.Background ?? new OriginStoryBackground());
            _storyProfile = _storyProfile with { Background = next.IsEmpty ? null : next };
        }
        Text("origin-story-family", "Origin.BackgroundFamily", background.BirthplaceFamily, 256,
            value => Background(b => b with { BirthplaceFamily = value }),
            ["distant-parent", "chosen-family", "relatives", "supportive-family"], "Origin.BackgroundFamilyHint");
        Select("origin-story-period", "Origin.BackgroundWhen", "Origin.Period.", ["childhood", "teen", "adult"],
            background.Period, value => Background(b => b with { Period = value }));
        Text("origin-story-chronology", "Origin.BackgroundChronology", background.Chronology, 256,
            value => Background(b => b with { Chronology = value }),
            ["early-memory", "after-school", "adult-recovery"], "Origin.BackgroundChronologyHint");
        Text("origin-story-experiences", "Origin.BackgroundExperiences", background.Experiences, 256,
            value => Background(b => b with { Experiences = value }),
            ["haunted", "betrayal", "safety"], "Origin.BackgroundExperiencesHint");
        Text("origin-story-addiction", "Origin.BackgroundAddiction", background.AddictionHistory, 256,
            value => Background(b => b with { AddictionHistory = value }),
            ["gambling", "shopping", "alcohol", "substances", "btl"], "Origin.BackgroundAddictionHint");
        Select("origin-story-addiction-status", "Origin.BackgroundStatus", "Origin.Addiction.",
            ["current", "abstinent", "recovery"], background.AddictionStatus,
            value => Background(b => b with { AddictionStatus = value }));
        Text("origin-story-turning-points", "Origin.BackgroundTurningPoints", background.TurningPoints, 256,
            value => Background(b => b with { TurningPoints = value }),
            ["missing-sibling", "promise", "secret"], "Origin.BackgroundTurningPointsHint");
        Text("origin-story-anchors", "Origin.BackgroundAnchors", background.PositiveAnchors, 256,
            value => Background(b => b with { PositiveAnchors = value }),
            ["loyal-friend", "humour", "ritual"], "Origin.BackgroundAnchorsHint");
        body.Add(section);
    }

    private IReadOnlyList<LifeModuleFollowUpPromptDto> FollowUps(string choiceId)
        => _storyCheckpoint?.Projection.CurrentTurn.LegalChoices
            .SingleOrDefault(choice => choice.ChoiceId == choiceId)?.FollowUps ?? [];

    private void BeginEditing(string choiceId)
    {
        _editingChoiceId = choiceId;
        _answers.Clear();
        _explicitCityAnswers.Clear();
        if (_storyCheckpoint?.PendingPreview?.InputResolution is { } pending && pending.ChoiceId == choiceId)
            foreach (var answer in pending.Values)
            {
                _answers.Add(answer.Key, answer.Value);
                _explicitCityAnswers.Add(answer.Key);
            }
        string? city = LastConfirmedCity(_storyCheckpoint?.Projection);
        if (city is not null)
            foreach (var prompt in FollowUps(choiceId).Where(IsCityPrompt))
                _answers.TryAdd(prompt.PromptId, city);
    }

    internal static bool IsCityPrompt(LifeModuleFollowUpPromptDto prompt)
        => prompt.InputKind == "text" && IsCityLabel(prompt.Label);

    private static bool IsCityLabel(string label)
        => label.Trim().Trim('[', ']').Trim().ToLowerInvariant() is "city" or "stadt" or "ciudad";

    internal static string? LastConfirmedCity(OriginStoryArcSeed? story)
    {
        if (story is null) return null;
        // This is an editable presentation suggestion, never a new rule fact.
        // Read only Core's admitted answers, in decision order, so cold reopen
        // needs no device-global city cache and cannot borrow another runner's city.
        foreach (string decision in story.CanonicalLayer.AcceptedDecisionIds.Reverse())
            foreach (var fact in story.CanonicalLayer.Facts.Reverse())
            {
                if (fact.AcceptedDecisionId != decision || fact.FactKind != "accepted-life-module-answer"
                    || !story.AllowedCanonicalFactIds.Contains(fact.FactId)) continue;
                int separator = fact.LocalizedSummary.IndexOf(": ", StringComparison.Ordinal);
                if (separator < 0 || !IsCityLabel(fact.LocalizedSummary[..separator])) continue;
                string city = fact.LocalizedSummary[(separator + 2)..].Trim();
                if (city.Length is > 0 and <= 1024 && !city.Any(char.IsControl)) return city;
            }
        return null;
    }

    private void AddInputForm(VerticalStackLayout card, string choiceId, int generation)
    {
        var prompts = FollowUps(choiceId);
        var cityFields = new List<Entry>();
        var editedCities = new HashSet<Entry>();
        bool copyingCity = false;
        var review = NativeTheme.PrimaryButton(_copy["Origin.ReviewAnswers"]);
        review.AutomationId = "origin-life-review-answers";
        void UpdateReady() => review.IsEnabled = !_actionInFlight && prompts.All(prompt => !prompt.IsRequired
            || _answers.TryGetValue(prompt.PromptId, out var answer) && !string.IsNullOrWhiteSpace(answer));
        card.Add(NativeTheme.Body(_copy["Origin.AnswersDetail"], NativeTheme.Muted));
        foreach (var prompt in prompts)
        {
            card.Add(NativeTheme.Body((prompt.DisplayLabel ?? prompt.Label) + (prompt.IsRequired ? " *" : string.Empty)));
            string promptId = prompt.PromptId;
            _answers.TryGetValue(promptId, out var previous);
            if (prompt.InputKind == "single-select")
            {
                var options = prompt.Options.Where(option => option.IsEnabled).ToArray();
                var picker = new Picker
                {
                    Title = _copy["Origin.ChooseAnswer"],
                    TextColor = NativeTheme.Text,
                    TitleColor = NativeTheme.Muted,
                    BackgroundColor = NativeTheme.Surface,
                    AutomationId = "origin-life-answer-" + promptId,
                    ItemsSource = options,
                    ItemDisplayBinding = new Binding(nameof(LifeModuleFollowUpOptionDto.Label)),
                    SelectedIndex = Array.FindIndex(options, option => option.SourceValue == previous)
                };
                picker.SelectedIndexChanged += (_, _) =>
                {
                    if (_actionInFlight || generation != _renderGeneration) return;
                    if (picker.SelectedIndex >= 0 && picker.SelectedIndex < options.Length)
                        _answers[promptId] = options[picker.SelectedIndex].SourceValue;
                    else _answers.Remove(promptId);
                    UpdateReady();
                };
                card.Add(picker);
            }
            else
            {
                var entry = new Entry
                {
                    AutomationId = "origin-life-answer-" + promptId,
                    TextColor = NativeTheme.Text,
                    PlaceholderColor = NativeTheme.Muted,
                    BackgroundColor = NativeTheme.Surface,
                    Text = previous, MaxLength = 1024, Placeholder = _copy["Origin.EnterAnswer"]
                };
                entry.TextChanged += (_, _) =>
                {
                    if (_actionInFlight || generation != _renderGeneration) return;
                    _answers[promptId] = entry.Text?.Trim() ?? string.Empty;
                    if (!copyingCity && IsCityPrompt(prompt))
                    {
                        _explicitCityAnswers.Add(promptId);
                        editedCities.Add(entry);
                        copyingCity = true;
                        try
                        {
                            foreach (var other in cityFields.Where(other => other != entry && !editedCities.Contains(other)))
                                other.Text = entry.Text;
                        }
                        finally { copyingCity = false; }
                    }
                    UpdateReady();
                };
                if (IsCityPrompt(prompt))
                {
                    cityFields.Add(entry);
                    if (_explicitCityAnswers.Contains(promptId)) editedCities.Add(entry);
                }
                card.Add(entry);
            }
        }
        UpdateReady();
        review.Clicked += async (_, _) =>
        {
            if (_actionInFlight || generation != _renderGeneration) return;
            _actionInFlight = true;
            review.IsEnabled = false;
            try
            {
                var answers = new Dictionary<string, string>(_answers, StringComparer.Ordinal);
                var prepared = await _prepareChoice(choiceId, answers);
                if (generation == _renderGeneration && prepared is not null && TryAdoptPrepared(prepared))
                {
                    _editingChoiceId = null;
                    Content = new ScrollView { Content = BuildBody() };
                }
            }
            finally { _actionInFlight = false; UpdateReady(); }
        };
        card.Add(review);
    }

    // Only called after rendering the selected, metatype-filtered card and its
    // exact Core preview. Changing metatype hides the old preview and action.
    private void AddConfirmation(VerticalStackLayout body, int generation)
    {
        if (_state.SelectedChoiceId is { } selectedChoiceId
            && _state.PendingPreviewDigest is { } previewDigest)
        {
            Label preview = NativeTheme.Body(
                _copy["Origin.StoryChoiceReview"] + "\n" + _copy["Origin.AutomaticStoryNotice"],
                NativeTheme.Ink);
            preview.AutomationId = "origin-life-preview";
            body.Insert(2, preview);
            Button confirm = NativeTheme.PrimaryButton(_copy["Origin.Confirm"]);
            confirm.AutomationId = "origin-life-confirm";
            confirm.IsEnabled = CanConfirmReviewed;
            var progress = new ActivityIndicator
            {
                AutomationId = "origin-life-saving", IsVisible = false,
                WidthRequest = 24, HeightRequest = 24,
                HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center
            };
            // Reserve space so showing feedback does not move the tapped action.
            var confirmationRow = new Grid
            {
                HeightRequest = confirm.HeightRequest, ColumnSpacing = 10,
                ColumnDefinitions = { new(GridLength.Star), new(new GridLength(32)) }
            };
            confirmationRow.Add(confirm, 0);
            confirmationRow.Add(progress, 1);
            confirm.Clicked += async (_, _) =>
            {
                if (_actionInFlight || generation != _renderGeneration || !CanConfirmReviewed)
                    return;
                _actionInFlight = true;
                confirm.IsEnabled = false;
                body.IsEnabled = false;
                confirm.Text = _copy["Origin.SavingDecision"];
                progress.IsVisible = progress.IsRunning = true;
                try
                {
                    if (IsOpeningDecision && _openingDetails is { } expected
                        && _storyProfile != (expected.StoryProfile ?? new OriginStoryProfile()))
                    {
                        OriginBookReadingState? saved = null;
                        try
                        {
                            if (_storyProfile.IsValid && _saveOpeningDetails is not null)
                                saved = await _saveOpeningDetails(expected, _storyProfile.IsEmpty ? null : _storyProfile,
                                    () => generation == _renderGeneration);
                        }
                        catch (Exception error) when (error is IOException or InvalidOperationException or OperationCanceledException
                            or UnauthorizedAccessException or System.Text.Json.JsonException) { }
                        if (generation != _renderGeneration) return;
                        if (saved is null)
                        {
                            await DisplayAlertAsync(_copy["Origin.PageTitle"], _copy["Origin.SetupSaveFailed"], _copy["Common.Ok"]);
                            return;
                        }
                        _openingDetails = saved;
                    }
                    var confirmed = await _confirmChoice(selectedChoiceId, previewDigest);
                    if (generation != _renderGeneration || confirmed?.IsSuccess != true)
                        return;
                    if (confirmed.Completed)
                    {
                        // The terminal chapter still belongs before completion.
                        // Pop this decision first, then present its full reader.
                        await Navigation.PopAsync();
                        if (_openBook is not null) await _openBook();
                    }
                    else if (TryAdoptConfirmed(confirmed))
                    {
                        if (await RefreshStoryReadinessAsync()) await LeadToUnreadStoryAsync();
                    }
                }
                finally
                {
                    _actionInFlight = false;
                    progress.IsRunning = progress.IsVisible = false;
                    body.IsEnabled = true;
                    confirm.Text = _copy["Origin.Confirm"];
                    confirm.IsEnabled = generation == _renderGeneration && CanConfirmReviewed;
                }
            };
            body.Insert(3, confirmationRow);
        }

    }

    private bool CanConfirmReviewed => _state.CanConfirm && _storyCheckpoint?.PendingPreview?.EffectReview is not null;

    // Both operands are exact Core projections from the same display authority.
    // This only hides unaffordable rows; prepare/confirm still enforce all rules.
    private bool IsAffordable(OriginDossierLifeModuleChoiceState choice)
        => choice.KarmaCost >= 0 && choice.KarmaCost <= _budget.Remaining;

    private static OriginDossierLifeModuleEffectState? MetatypeEffect(OriginDossierLifeModuleChoiceState choice)
        => choice.Effects.SingleOrDefault(effect => effect.Domain == "metatype-choice");

    // Core determines whether finishing is available. Only promote an already
    // admitted typed action; never infer permission from stage, cost or label.
    private static bool IsSelectionFinish(OriginDossierLifeModuleChoiceState choice)
        => choice.Effects.Any(effect => effect.Domain == "creation-stage"
            && effect.TargetId == CharacterCreationLifeModuleStageIds.SelectionFinished
            && effect.AfterValue == "true");

    private bool TryAdoptPrepared(OriginDossierLifeModulePhoneResult prepared)
    {
        if (!TryReadDisplayAuthority(
                prepared,
                out OriginDossierLifeModuleDecisionState state,
                out CharacterCreationBudgetState budget)
            || !string.Equals(state.WorkspaceId, _state.WorkspaceId, StringComparison.Ordinal)
            || state.WorkspaceRevision != _state.WorkspaceRevision
            || !string.Equals(
                prepared.FoundationSnapshotDigest,
                _foundationSnapshotDigest,
                StringComparison.Ordinal)
            || !string.Equals(prepared.BoundContentDigest, _boundContentDigest, StringComparison.Ordinal)
            || !string.Equals(prepared.BoundSourceDigest, _boundSourceDigest, StringComparison.Ordinal)
            || !string.Equals(
                prepared.BoundMechanicsSnapshotDigest,
                _boundMechanicsSnapshotDigest,
                StringComparison.Ordinal)
            || budget.Total != _budget.Total
            || budget.Used != _budget.Used
            || budget.Remaining != _budget.Remaining
            || !string.Equals(budget.Unit, _budget.Unit, StringComparison.Ordinal))
        {
            return false;
        }

        _state = state;
        _budget = budget;
        _storyCheckpoint = prepared.StoryCheckpoint;
        return true;
    }

    private bool TryAdoptConfirmed(OriginDossierLifeModulePhoneResult confirmed)
    {
        if (!TryReadDisplayAuthority(confirmed, out var state, out var budget)
            || confirmed.StoryCheckpoint is not { } checkpoint
            || state.WorkspaceId != _state.WorkspaceId || state.OwnerId != _state.OwnerId
            || state.Locale != _state.Locale || state.WorkspaceRevision != _state.WorkspaceRevision + 1
            || state.TurnSequence != _state.TurnSequence + 1 || state.StageOrder < _state.StageOrder
            || state.CanConfirm || checkpoint.PendingPreview is not null
            || checkpoint.Projection.CurrentTurn.PreviousTurnDigest != _state.BoundTurnSeedDigest
            || confirmed.BoundSourceDigest != _boundSourceDigest
            || budget.Total != _budget.Total || budget.Unit != _budget.Unit)
            return false;

        _state = state;
        _budget = budget;
        _storyCheckpoint = checkpoint;
        _foundationSnapshotDigest = confirmed.FoundationSnapshotDigest!;
        _boundContentDigest = confirmed.BoundContentDigest!;
        _boundSourceDigest = confirmed.BoundSourceDigest!;
        _boundMechanicsSnapshotDigest = confirmed.BoundMechanicsSnapshotDigest!;
        _selectedMetatypeOptionId = null;
        _editingChoiceId = null;
        _answers.Clear();
        _explicitCityAnswers.Clear();
        return true;
    }

    private static bool TryReadDisplayAuthority(
        OriginDossierLifeModulePhoneResult result,
        out OriginDossierLifeModuleDecisionState state,
        out CharacterCreationBudgetState budget)
    {
        state = result.State!;
        budget = result.LifeModuleBudget!;
        return result.IsSuccess
               && result.State is not null
               && result.LifeModuleBudget is not null
               && string.Equals(
                   result.LifeModuleBudget.BudgetId,
                   CharacterCreationBudgetIds.LifeModules,
                   StringComparison.Ordinal)
               && result.LifeModuleBudget.IsExact
               && result.LifeModuleBudget.Blockers.Count == 0
               && !string.IsNullOrWhiteSpace(result.LifeModuleBudget.Unit)
               && !string.IsNullOrWhiteSpace(result.FoundationSnapshotDigest)
               && !string.IsNullOrWhiteSpace(result.BoundContentDigest)
               && !string.IsNullOrWhiteSpace(result.BoundSourceDigest)
               && !string.IsNullOrWhiteSpace(result.BoundMechanicsSnapshotDigest);
    }
}
