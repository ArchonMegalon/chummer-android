using System.Globalization;
using Chummer.Contracts.Characters;
using Chummer.Presentation.Creation;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

/// <summary>Read-only completion advice followed by one explicit, Core-reviewed atomic Apply.</summary>
public sealed class CreationCompletionPage : NativePageBase
{
    private readonly VerticalStackLayout _body = new() { Padding = new Thickness(20, 18), Spacing = 12 };
    private readonly VerticalStackLayout _footer = new() { Padding = new Thickness(20, 10, 20, 16), Spacing = 8 };
    private CharacterOverviewState? _display;
    private CharacterCreationCompletionReviewInputs? _inputs;
    private CharacterCreationPrerequisiteState? _missingPrerequisite;
    private CharacterCreationStartingNuyenSource[] _cashSources = [];
    private CompletionArchetypeSuggestion? _archetype;
    private CompletionAssistantProposal? _proposal;
    private CharacterCreationCompletionApplyReview? _review;
    private CharacterCreationFoundationResult<CharacterCreationFinalizationReceipt>? _completion;
    private CancellationTokenSource? _reads;
    private string _archetypeId = string.Empty, _roll = string.Empty;
    private string? _cashDigest, _nativeLanguageId, _idempotencyKey;
    private CharacterCreationMagicResonanceOptionIdentity? _preferredTradition, _preferredStream;
    private IReadOnlyList<string> _blockers = [];
    private bool _busy, _visible, _applyStarted, _outcomeLookupAllowed, _leaving;
    private long _render, _inputVersion;
    private ReadAnalysis? _analysis;
    private Grid? _analysisView;
    private ActivityIndicator? _analysisSpinner;
    private ProgressBar? _analysisBar;
    private Label? _analysisStatus, _analysisCount;

    private sealed class ReadAnalysis(CompletionAnalysisProgress initial)
    {
        public CompletionAnalysisProgress Status { get; set; } = initial;
        public Action<CompletionAnalysisProgress> Report { get; set; } = null!;
    }

    // Local harness observation only: no character/source text, exception messages,
    // stacks, persistence, telemetry or UI diagnostics. Each appearance owns its list.
    internal sealed record LoadObservation(string Stage, string? Outcome, IReadOnlyList<string> Blockers,
        string? ExceptionType, bool AppearanceCurrent, bool DisplayCurrent, bool? InputsCurrent);
    internal IReadOnlyList<LoadObservation> LoadObservations { get; private set; } = [];
    internal sealed record PreviewObservation(string Stage, long ElapsedMilliseconds, string? Outcome);
    internal IReadOnlyList<PreviewObservation> PreviewObservations { get; private set; } = [];

    public CreationCompletionPage(RunnerSessionCoordinator coordinator) : base(coordinator)
    {
        Title = Copy("Title", "Finish this character");
        AutomationId = "creation-completion-page";
        Grid layout = new()
        {
            RowDefinitions = { new RowDefinition(GridLength.Star), new RowDefinition(GridLength.Auto) }
        };
        layout.Add(new ScrollView { Content = _body });
        layout.Add(_footer, 0, 1);
        Content = layout;
        _body.Add(NativeTheme.Body(CreationKarmaCopy.Loading, NativeTheme.Muted));
    }

    internal static bool Supports(CharacterOverviewState state)
        => state.Profile?.Created == false && state.CreationWizard is { RulesetId: "sr5" } wizard
           && wizard.BuildMethod is CharacterCreationBuildMethods.Priority or CharacterCreationBuildMethods.SumToTen;

    protected override void OnAppearing()
    {
        _visible = true;
        _leaving = false;
        _reads = new CancellationTokenSource();
        base.OnAppearing();
    }

    protected override void OnDisappearing()
    {
        _visible = false;
        EndReadAnalysis(_analysis);
        _render++;
        _inputVersion++;
        _reads?.Cancel();
        _reads?.Dispose();
        _reads = null;
        _body.IsEnabled = false;
        _footer.IsEnabled = false;
        base.OnDisappearing();
    }

    protected override async Task PrepareForAppearanceRefreshAsync(CancellationToken cancellationToken)
    {
        // A dispatched review is never regenerated, including after navigation.
        if (_applyStarted) return;
        long appearance = CaptureAppearanceGeneration();
        var original = Coordinator.State;
        var display = original;
        var observations = new List<LoadObservation>();
        LoadObservations = observations;
        string stage = "completion-inputs";
        CharacterCreationCompletionReviewInputs? observedInputs = null;
        _display = original;
        _inputs = null;
        _missingPrerequisite = null;
        _proposal = null;
        _review = null;
        _cashSources = [];
        _blockers = [];
        _busy = true;
        if (!Supports(original)) { _busy = false; return; }
        long inputGeneration = _inputVersion;
        var analysis = BeginReadAnalysis(new(CompletionAnalysisStage.LoadingChoices, 0, 3),
            () => inputGeneration == _inputVersion && CurrentLoad(), cancellationToken);
        Refresh();
        try
        {
            Observe();
            var loaded = await Coordinator.LoadCreationCompletionInputsAsync(cancellationToken);
            observedInputs = loaded.Value;
            Observe(loaded.Outcome, loaded.Blockers);
            var replacement = Coordinator.State;
            if (!CurrentLoad() && CanRetryInitialRead(replacement, loaded))
            {
                // A same-saved-state hydration retires profile-bound authority.
                // Acquire fresh inputs once; never rebind the discarded inputs.
                display = replacement;
                _display = display;
                observedInputs = null;
                stage = "completion-inputs-retry";
                Observe();
                loaded = await Coordinator.LoadCreationCompletionInputsAsync(cancellationToken);
                observedInputs = loaded.Value;
                Observe(loaded.Outcome, loaded.Blockers);
            }
            if (!CurrentLoad()) return;
            _blockers = loaded.Blockers;
            if (loaded is not { Outcome: CharacterCreationFoundationOutcomes.Success, Value: { } inputs })
            {
                // A downstream input (for example Skills) can be absent because
                // the real priority prerequisite has not been confirmed yet.
                // Only a current, owner-bound Core read may choose that guidance.
                stage = "prerequisite-fallback";
                analysis.Report(new(CompletionAnalysisStage.Prerequisites, 0, 3));
                Observe();
                var prerequisite = await Coordinator.LoadCreationPrerequisiteAsync(cancellationToken);
                Observe(prerequisite.Outcome, prerequisite.Blockers);
                if (!CurrentLoad()) return;
                if (prerequisite is { Outcome: CharacterCreationFoundationOutcomes.Success, Value: { CanEnterAttributes: false } required }
                    && Coordinator.IsCreationPrerequisiteStateCurrent(required))
                    _missingPrerequisite = required;
                return;
            }
            if (!Coordinator.IsCreationCompletionInputsCurrent(inputs)) return;
            stage = "archetype";
            analysis.Report(new(CompletionAnalysisStage.Archetype, 1, 3));
            Observe();
            var suggestion = await Task.Run(() => CompletionRecommendationBuilder.SuggestArchetype(
                inputs, cancellationToken: cancellationToken), cancellationToken);
            Observe(suggestion.Outcome, suggestion.Blockers);
            if (!CurrentLoad() || !Coordinator.IsCreationCompletionInputsCurrent(inputs)) return;
            stage = "finalization-cash";
            analysis.Report(new(CompletionAnalysisStage.StartingCash, 2, 3));
            Observe();
            var cash = await Task.Run(() => Coordinator.LoadCreationFinalizationInBackground(
                display, cancellationToken), cancellationToken);
            Observe(cash.Outcome, cash.Blockers);
            if (!CurrentLoad() || !Coordinator.IsCreationCompletionInputsCurrent(inputs)) return;
            _inputs = inputs;
            _archetype = suggestion.Value;
            _archetypeId = suggestion.Value?.ArchetypeId ?? CompletionAssistantRules.GeneralSurvivor;
            _blockers = suggestion.Blockers;
            // CanReview describes the ORIGINAL build, not our proposed one.
            // Only use its exact source terms here, never its admission flag.
            if (cash.Value is { } authority)
                _cashSources = authority.StartingCashSources?.ToArray()
                    ?? (authority.StartingCashSource is { } source ? [source] : []);
            _cashDigest = _cashSources.FirstOrDefault()?.AuthorityDigest;
            _roll = string.Empty;
            _nativeLanguageId = null;
            _preferredTradition = null;
            _preferredStream = null;
            _inputVersion++;
            stage = "choices-ready";
            Observe();
        }
        catch (OperationCanceledException error) when (cancellationToken.IsCancellationRequested) { Observe(error: error); }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            Observe(error: error);
            if (CurrentLoad()) _blockers = ["completion-ui-load-failed"];
        }
        finally
        {
            EndReadAnalysis(analysis);
            if (IsCurrentAppearanceGeneration(appearance)) _busy = false;
        }
        bool PageLoadCurrent() => IsCurrentPage(appearance) && ReferenceEquals(Shell.Current?.CurrentPage, this)
            && !_leaving && !_applyStarted
            && !cancellationToken.IsCancellationRequested && !ReadToken.IsCancellationRequested;
        bool CurrentLoad() => PageLoadCurrent() && !Coordinator.State.IsBusy
            && Coordinator.State.ConflictState is null
            && Coordinator.State.Session.ActiveWorkspaceId == original.Session.ActiveWorkspaceId
            && Supports(Coordinator.State)
            && Coordinator.State.CreationWizard?.BuildMethod == original.CreationWizard?.BuildMethod
            && Coordinator.IsCreationFinalizationDisplayCurrent(display);
        bool CanRetryInitialRead(CharacterOverviewState candidate,
            CharacterCreationFoundationResult<CharacterCreationCompletionReviewInputs> result)
            => PageLoadCurrent() && _inputs is null
               && original.Error is null && !original.IsBusy && original.ConflictState is null
               && original.DisplayOwnerContext is { IsValid: true }
               && candidate.DisplayOwnerContext == original.DisplayOwnerContext
               && candidate.Session.OwnerContext == original.Session.OwnerContext
               && candidate.WorkspaceId is not null && candidate.WorkspaceId == original.WorkspaceId
               && candidate.Session.ActiveWorkspaceId == original.Session.ActiveWorkspaceId
               && candidate.Session.ActiveWorkspaceId == original.WorkspaceId
               && candidate.ContentRevision == original.ContentRevision && candidate.SavedRevision == original.SavedRevision
               && candidate.ActiveTabId == original.ActiveTabId && candidate.ActiveActionId == original.ActiveActionId
               && candidate.ActiveSectionId == original.ActiveSectionId
               && Supports(candidate) && candidate.CreationWizard?.BuildMethod == original.CreationWizard?.BuildMethod
               && candidate.Error is null && !candidate.IsBusy && candidate.ConflictState is null
               && !ReferenceEquals(candidate.Profile, original.Profile)
               // This also checks the live owner transition stamp, including ABA.
               && Coordinator.IsCreationFinalizationDisplayCurrent(candidate)
               && (result is { Outcome: CharacterCreationFoundationOutcomes.Success, Value: not null }
                   || result is { Outcome: CharacterCreationFoundationOutcomes.Blocked, Value: null }
                      && result.Blockers.Count == 1
                      && result.Blockers[0] == CharacterCreationFinalizationBlockers.StaleWorkspaceRevision);

        void Observe(string? outcome = null, IReadOnlyList<string>? blockers = null, Exception? error = null)
            => observations.Add(new(stage, outcome, blockers?.ToArray() ?? [], error?.GetType().FullName,
                IsCurrentAppearanceGeneration(appearance), Coordinator.IsCreationFinalizationDisplayCurrent(display),
                observedInputs is null ? null : Coordinator.IsCreationCompletionInputsCurrent(observedInputs)));
    }

    protected override void Refresh()
    {
        long render = ++_render, appearance = CaptureAppearanceGeneration();
        DetachReadProgress();
        _body.Clear();
        _footer.Clear();
        _body.IsEnabled = !_busy;
        _footer.IsEnabled = true;
        _body.Add(NativeTheme.Title(Title));
        if (_completion is { Outcome: CharacterCreationFoundationOutcomes.Success, Value: { } receipt })
        {
            RenderSaved(receipt, appearance, render);
            return;
        }
        if (_applyStarted)
        {
            if (!_busy && _completion is { Value: null } rejected && rejected.Outcome != "outcome-unknown")
            {
                _body.Add(NativeTheme.Body(Copy("Rejected", "These changes were not applied. Return to creation and review your choices."), NativeTheme.Danger));
                AddBlockers(rejected.Blockers);
                AddCancel(appearance, render, afterRejection: true);
                return;
            }
            _body.Add(NativeTheme.Body(_busy ? Copy("Saving", "Saving and opening Career…")
                : Copy("Unknown", "The save result needs checking. Do not submit another completion."), NativeTheme.Muted));
            if (!_busy && _outcomeLookupAllowed && _completion?.Outcome == "outcome-unknown")
                AddAction(_footer, Copy("CheckResult", "Check saved result"), "creation-completion-check-result",
                    () => ApplyAsync(recoveryOnly: true), appearance, render);
            return;
        }
        if (_busy && _analysis is not null) AddReadProgress();
        AddCancel(appearance, render);
        if (_display is null || !Supports(_display))
        {
            _body.Add(NativeTheme.Body(Copy("Unsupported", "Starter suggestions currently support SR5 Priority and Sum-to-Ten. Continue with this build method’s normal creation steps.")));
            return;
        }
        if (_inputs is null)
        {
            if (_busy) _body.Add(NativeTheme.Body(CreationKarmaCopy.Loading, NativeTheme.Muted));
            else if (_missingPrerequisite is { } prerequisite)
                _body.Add(NativeTheme.Body(Coordinator.IsCreationPrerequisiteStateCurrent(prerequisite)
                    ? Copy("LoadBlocked", "Complete Build method first, then reopen these suggestions. Your existing choices are unchanged.")
                    : Copy("Stale", "Your runner or account changed. Return to creation and open a fresh review."), NativeTheme.Muted));
            else AddBlockers(_blockers.Count == 0 ? ["completion-ui-load-failed"] : _blockers);
            return;
        }
        if (!InputsCurrent())
        {
            _body.Add(NativeTheme.Body(Copy("Stale", "Your runner or account changed. Return to creation and open a fresh review."), NativeTheme.Danger));
            return;
        }
        _body.Add(NativeTheme.Body(Copy("Scope", "Fill unused points and suggest contacts, a lifestyle, basic supplies, a weapon and armor. Some equipment and magic choices still need review. Nothing changes until Apply."), NativeTheme.Muted));
        if (_inputs.Magic.SelectedTalent.Kind == CharacterCreationMagicResonanceKinds.MysticAdept)
        {
            Label boundary = NativeTheme.Body(CreationAllocationStrings.Get("Completion.MysticFoundation",
                "Suggestions are only a foundation. Review tradition and spells, and choose adept powers manually; this is not a complete Mystic Adept build."), NativeTheme.Muted);
            boundary.AutomationId = "creation-completion-mystic-foundation";
            _body.Add(boundary);
        }
        if (_review is not null)
        {
            RenderFinalReview(appearance, render);
            return;
        }
        RenderChoices(appearance, render);
        if (_proposal is null)
        {
            AddAction(_footer, Copy("Preview", "Preview suggestions"), "creation-completion-preview",
                BuildAsync, appearance, render, !_busy && CompletionRecommendationBuilder.GetRecommendationPack(_archetypeId) is not null);
        }
        else
        {
            RenderProposal();
            RenderCashAndReview(appearance, render);
        }
        AddBlockers(_blockers);
    }

    private void AddReadProgress()
    {
        Grid progress = new()
        {
            AutomationId = "creation-completion-read-busy",
            ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) },
            RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto) },
            ColumnSpacing = 10, RowSpacing = 6
        };
        _analysisView = progress;
        _analysisSpinner = new ActivityIndicator
        {
            AutomationId = "creation-completion-read-progress", IsRunning = true,
            Color = NativeTheme.Text, WidthRequest = 24, HeightRequest = 24,
            VerticalOptions = LayoutOptions.Center
        };
        progress.Add(_analysisSpinner);
        _analysisStatus = NativeTheme.Body(string.Empty);
        _analysisStatus.AutomationId = "creation-completion-read-status";
        _analysisStatus.VerticalOptions = LayoutOptions.Center;
        progress.Add(_analysisStatus, 1);
        _analysisBar = new ProgressBar
        {
            AutomationId = "creation-completion-stage-progress", ProgressColor = NativeTheme.Text,
            BackgroundColor = NativeTheme.Surface, HeightRequest = 6
        };
        progress.Add(_analysisBar, 0, 1);
        Grid.SetColumnSpan(_analysisBar, 2);
        _analysisCount = NativeTheme.Body(string.Empty, NativeTheme.Muted);
        _analysisCount.AutomationId = "creation-completion-stage-count";
        progress.Add(_analysisCount, 0, 2);
        Grid.SetColumnSpan(_analysisCount, 2);
        _footer.Add(progress);
        UpdateReadProgress();
    }

    private ReadAnalysis BeginReadAnalysis(CompletionAnalysisProgress initial, Func<bool> current,
        CancellationToken cancellationToken)
    {
        EndReadAnalysis(_analysis);
        var analysis = new ReadAnalysis(initial);
        _analysis = analysis;
        var dispatcher = Dispatcher;
        analysis.Report = status =>
        {
            // Never capture UI authority on the worker thread. Recheck all of it
            // at dispatch time, including operation identity and the live owner.
            dispatcher.Dispatch(() =>
            {
                if (!ReferenceEquals(_analysis, analysis) || !_busy || !_visible || _leaving || _applyStarted
                    || cancellationToken.IsCancellationRequested || ReadToken.IsCancellationRequested || !current()
                    || status.TotalStages != analysis.Status.TotalStages
                    || status.CompletedStages < analysis.Status.CompletedStages
                    || status.CompletedStages == analysis.Status.CompletedStages && (int)status.Stage < (int)analysis.Status.Stage
                    || status.CompletedStages < 0 || status.CompletedStages > status.TotalStages
                    || !Enum.IsDefined(status.Stage)) return;
                analysis.Status = status;
                UpdateReadProgress(); // no Refresh: keep controls and cancel handlers attached
            });
        };
        return analysis;
    }

    private void EndReadAnalysis(ReadAnalysis? analysis)
    {
        if (!ReferenceEquals(_analysis, analysis)) return;
        _analysis = null;
        DetachReadProgress();
    }

    private void DetachReadProgress()
    {
        if (_analysisSpinner is not null) _analysisSpinner.IsRunning = false;
        if (_analysisView is not null) _analysisView.IsVisible = false;
        _analysisView = null;
        _analysisSpinner = null;
        _analysisBar = null;
        _analysisStatus = _analysisCount = null;
    }

    private void UpdateReadProgress()
    {
        if (_analysis is not { Status: var status } || _analysisBar is null) return;
        _analysisBar.Progress = (double)status.CompletedStages / status.TotalStages;
        _analysisStatus!.Text = AnalysisStageText(status.Stage);
        _analysisCount!.Text = string.Format(CultureInfo.CurrentUICulture,
            CreationAllocationStrings.Get("Completion.Progress.Count",
                "Analysis stages completed: {0} of {1}. Stage count, not a time estimate."),
            status.CompletedStages, status.TotalStages);
    }

    internal static string AnalysisStageText(CompletionAnalysisStage stage)
        => CreationAllocationStrings.Get("Completion.Progress." + stage, stage switch
        {
            CompletionAnalysisStage.LoadingChoices => "Loading choices and checking rules…",
            CompletionAnalysisStage.Prerequisites => "Checking build method prerequisites…",
            CompletionAnalysisStage.Archetype => "Analyzing your existing role…",
            CompletionAnalysisStage.PreparingChoices => "Preparing your choices and locks…",
            CompletionAnalysisStage.Skills => "Analyzing skill and group suggestions…",
            CompletionAnalysisStage.Magic => "Preparing magic and resonance suggestions…",
            CompletionAnalysisStage.Attributes => "Checking attribute allocations…",
            CompletionAnalysisStage.Contacts => "Checking contacts and their budgets…",
            CompletionAnalysisStage.RulesAndBudgets => "Checking skills, magic, qualities and budgets together…",
            CompletionAnalysisStage.Equipment => "Checking equipment and lifestyle options…",
            CompletionAnalysisStage.Karma => "Analyzing remaining Karma options…",
            CompletionAnalysisStage.FinalBudgets => "Checking the complete plan and final budgets…",
            CompletionAnalysisStage.PreparingReview => "Preparing your proposal review…",
            CompletionAnalysisStage.StartingCash => "Checking starting-cash terms…",
            CompletionAnalysisStage.FinalReview => "Checking final budgets and completion…",
            _ => "Checking your choices…"
        });

    private void RenderChoices(long appearance, long render)
    {
        // The inferred identity belongs to the original inputs, not the current
        // suggestion style. Keep it available after the player tries another style.
        string[] archetypes = new[] { _archetypeId, _archetype?.ArchetypeId ?? _archetypeId, CompletionAssistantRules.GeneralSurvivor,
            CompletionAssistantRules.StreetSamurai, CompletionAssistantRules.Face, CompletionAssistantRules.Mage,
            CompletionAssistantRules.Decker, CompletionAssistantRules.Rigger }.Distinct(StringComparer.Ordinal).ToArray();
        Picker picker = new()
        {
            Title = Copy("Archetype", "Archetype"), AutomationId = "creation-completion-archetype",
            ItemsSource = archetypes.Select(ArchetypeLabel).ToArray(), SelectedIndex = Array.IndexOf(archetypes, _archetypeId),
            TextColor = NativeTheme.Text, TitleColor = NativeTheme.Muted, BackgroundColor = NativeTheme.Surface
        };
        picker.SelectedIndexChanged += (_, _) =>
        {
            if (!Current(appearance, render) || _busy || !ReferenceEquals(picker.Parent, _body) || picker.SelectedIndex < 0
                || picker.SelectedIndex >= archetypes.Length) return;
            _archetypeId = archetypes[picker.SelectedIndex];
            InvalidateProposal();
        };
        _body.Add(NativeTheme.FieldLabel(Copy("Archetype", "Archetype")));
        _body.Add(picker);
        _body.Add(NativeTheme.Body(_archetype?.Confidence == CompletionArchetypeConfidence.NoEvidence
            ? Copy("Neutral", "No clear role yet: a general starter is selected. You can change it.")
            : Copy("Prefill", "Suggested from your current choices. You can change the archetype."), NativeTheme.Muted));
        if (CompletionRecommendationBuilder.GetRecommendationPack(_archetypeId) is null)
            _body.Add(NativeTheme.Body(Copy("NoPack", "This archetype has no starter suggestions yet. Keep your talent and continue manually, or choose another suggestion style."), NativeTheme.Danger));
        _body.Add(NativeTheme.Body(Copy("KeepChoices", "Your existing choices stay unchanged. Suggestions only fill choices you have not made yet."), NativeTheme.Muted));
        if (!_inputs!.Skills.Allocations.Any(row => row.IsNativeLanguage))
        {
            var languages = _inputs.Skills.Authority.KnowledgeSkills.Where(row => row.CanBeNativeLanguage).ToArray();
            var language = new Picker
            {
                Title = CreationAllocationStrings.Get("Skills.ChooseNativeLanguage", "Choose native language"),
                AutomationId = "creation-completion-native-language", TextColor = NativeTheme.Text,
                TitleColor = NativeTheme.Danger, BackgroundColor = NativeTheme.Surface,
                ItemsSource = languages.Select(row => SkillCatalogStrings.SkillName(row.Kind, row.SourceSkillId, row.Name)).ToArray(),
                SelectedIndex = Array.FindIndex(languages, row => row.SourceSkillId ==
                    (_nativeLanguageId ?? _proposal?.Request.Skills.NativeLanguageSourceId))
            };
            language.SelectedIndexChanged += (_, _) =>
            {
                if (!Current(appearance, render) || _busy || language.SelectedIndex < 0) return;
                _nativeLanguageId = languages[language.SelectedIndex].SourceSkillId;
                InvalidateProposal();
            };
            _body.Add(language);
        }
        RenderMagicIdentityChoice(CharacterCreationMagicResonanceKinds.Tradition,
            Copy("MagicTradition", "Tradition"), "creation-completion-tradition", _inputs.Magic.Selections.Tradition,
            _preferredTradition, value => _preferredTradition = value, appearance, render);
        RenderMagicIdentityChoice(CharacterCreationMagicResonanceKinds.Stream,
            Copy("MagicStream", "Stream"), "creation-completion-stream", _inputs.Magic.Selections.Stream,
            _preferredStream, value => _preferredStream = value, appearance, render);
    }

    private void RenderMagicIdentityChoice(string kind, string title, string automationId,
        CharacterCreationMagicResonanceOptionIdentity? original,
        CharacterCreationMagicResonanceOptionIdentity? preferred,
        Action<CharacterCreationMagicResonanceOptionIdentity> select, long appearance, long render)
    {
        if (original is not null)
        {
            Label retained = NativeTheme.Body(title + ": " + MagicOptionLabel(original)
                + " · " + Copy("Locked", "kept unchanged"), NativeTheme.Muted);
            retained.AutomationId = automationId + "-retained";
            _body.Add(retained);
            return;
        }
        var choices = CompletionRecommendationBuilder.GetMagicIdentityChoices(_inputs!, kind).ToArray();
        if (choices.Length == 0) return;
        // Initialize visibly before subscribing. Refreshes retain the player's
        // exact choice; a missing prior choice is never silently replaced.
        if (preferred is null)
        {
            preferred = choices[0].Identity;
            select(preferred);
        }
        Picker picker = new()
        {
            Title = title, AutomationId = automationId,
            ItemsSource = choices.Select(option => MagicCatalogStrings.OptionName(
                option.Identity.Kind, option.Identity.SourceId, option.Name)).ToArray(),
            SelectedIndex = Array.FindIndex(choices, option => option.Identity == preferred),
            TextColor = NativeTheme.Text, TitleColor = NativeTheme.Muted, BackgroundColor = NativeTheme.Surface
        };
        picker.SelectedIndexChanged += (_, _) =>
        {
            if (!Current(appearance, render) || _busy || picker.SelectedIndex < 0
                || picker.SelectedIndex >= choices.Length) return;
            select(choices[picker.SelectedIndex].Identity);
            InvalidateProposal();
        };
        _body.Add(NativeTheme.FieldLabel(title));
        _body.Add(picker);
        _body.Add(NativeTheme.Body(Copy("MagicChoiceHelp",
            "This visible selection is a suggestion. You can change it before previewing. Saved magic choices stay unchanged."), NativeTheme.Muted));
    }

    private void InvalidateProposal()
    {
        _inputVersion++;
        _proposal = null;
        _review = null;
        _roll = string.Empty;
        _blockers = [];
        Refresh();
    }

    private async Task BuildAsync()
    {
        if (_busy || !InputsCurrent() || _inputs is not { } inputs
            || _display?.DisplayOwnerContext is not { } owner
            || CompletionRecommendationBuilder.GetRecommendationPack(_archetypeId) is null) return;
        long appearance = CaptureAppearanceGeneration(), version = _inputVersion;
        var permissions = new CompletionAdditivePermissions([], []);
        var intent = new CompletionAssistantIntent(owner, inputs.SnapshotDigest, _archetypeId, permissions, new())
        {
            NativeLanguageSourceId = _nativeLanguageId,
            PreferredTradition = inputs.Magic.Selections.Tradition is null ? _preferredTradition : null,
            PreferredStream = inputs.Magic.Selections.Stream is null ? _preferredStream : null,
            StartingLifestyleId = SelectedLifestyleId(inputs),
            // Finish may propose spending the remaining creation budget; Core
            // independently enforces actual affordability and all reserves.
            // This is a proposal ceiling, not money or permission to save.
            MaximumAdditionalPurchaseSpend = decimal.MaxValue
        };
        _busy = true;
        CancellationToken readToken = ReadToken;
        var analysis = BeginReadAnalysis(new(CompletionAnalysisStage.PreparingChoices, 0,
                CompletionAnalysisProgress.MissingOnlyStageCount + 1),
            () => CurrentInput(appearance, version), readToken);
        Refresh();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var observations = new List<PreviewObservation>();
        PreviewObservations = observations;
        try
        {
            Observe("proposal-start");
            var result = await Coordinator.BuildCreationCompletionProposalAsync(inputs, intent, readToken,
                status => analysis.Report(status with { TotalStages = status.TotalStages + 1 }));
            Observe("proposal-returned", result.Outcome);
            if (!CurrentInput(appearance, version)) return;
            _proposal = result.Value;
            _blockers = RequiredProposalBlockers(_proposal, result.Blockers);
            _roll = string.Empty;
            // Retire the previous proposal's terms before the next authority
            // read. An exception/cancellation must not make stale cash reviewable.
            var candidateCashSources = _cashSources;
            _cashSources = [];
            _cashDigest = null;
            if (_proposal is { } proposal)
            {
                // A suggested home is not saved yet. Read its genuine source
                // terms through the admitted proposal, never use the original
                // Street fallback or construct an authority from display values.
                Observe("cash-start");
                analysis.Report(new(CompletionAnalysisStage.StartingCash,
                    CompletionAnalysisProgress.MissingOnlyStageCount, CompletionAnalysisProgress.MissingOnlyStageCount + 1));
                var cash = await Coordinator.LoadCreationCompletionStartingCashAsync(proposal, readToken);
                Observe("cash-returned", cash.Outcome);
                if (!CurrentInput(appearance, version) || !ReferenceEquals(_proposal, proposal)) return;
                if (cash is { Outcome: CharacterCreationFoundationOutcomes.Success, Value: { } source, Blockers.Count: 0 })
                {
                    _cashSources = candidateCashSources.Any(row => row.AuthorityDigest == source.AuthorityDigest)
                        ? candidateCashSources.Select(row => row.AuthorityDigest == source.AuthorityDigest ? source : row).ToArray()
                        : [source];
                    _cashDigest = source.AuthorityDigest;
                }
                else
                {
                    _cashSources = [];
                    _cashDigest = null;
                    _blockers = [.. _blockers, .. cash.Blockers];
                }
            }
        }
        catch (OperationCanceledException) { Observe("canceled"); }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            Observe("failed", error.GetType().FullName);
            if (CurrentInput(appearance, version)) _blockers = ["completion-ui-load-failed"];
        }
        finally
        {
            EndReadAnalysis(analysis);
            if (IsCurrentAppearanceGeneration(appearance))
            {
                _busy = false;
                if (CurrentInput(appearance, version)) Refresh();
            }
            Observe("callback-finished");
        }
        void Observe(string stage, string? outcome = null)
            => observations.Add(new(stage, clock.ElapsedMilliseconds, outcome));
    }

    private void RenderProposal()
    {
        _body.Add(NativeTheme.Eyebrow(Copy("Proposal", "Proposed ratings")));
        foreach (var suggestion in _proposal!.Suggestions)
        {
            string label = SuggestionLabel(suggestion.Choice);
            string actual = suggestion.EvaluatedRating?.ToString(CultureInfo.CurrentCulture) ?? "—";
            if (_proposal.FillsRemainingResources)
            {
                if (suggestion.EvaluatedRating is > 0 && suggestion.State is
                        CompletionSuggestionState.TargetMet or CompletionSuggestionState.TargetUnmet)
                    _body.Add(NativeTheme.Metric(label, actual));
                continue;
            }
            string state = suggestion.State switch
            {
                CompletionSuggestionState.TargetMet => Copy("Met", "target met"),
                CompletionSuggestionState.Locked or CompletionSuggestionState.Excluded => Copy("Locked", "kept unchanged"),
                CompletionSuggestionState.SourceUnavailable => Copy("Unavailable", "source unavailable"),
                CompletionSuggestionState.TargetUnmet => Copy("Unmet", "target not reached"),
                _ => Copy("NotEvaluated", "not evaluated")
            };
            _body.Add(NativeTheme.Body(CreationFlowStrings.Format("Completion.Rating", "{0}: proposed {1}, target {2} · {3}",
                label, actual, suggestion.TargetRating, state)));
        }
        RenderMagicProposal();
        RenderKarmaQualityProposal();
        RenderResourceProposal();
        RenderLoadoutProposal();
        RenderOptionalAttributeKarmaGoals();
        _body.Add(NativeTheme.Eyebrow(Copy("Remaining", "Still needs attention")));
        foreach (var task in _proposal.RemainingTasks.Where(key => key != "completion.remaining.finalization-review").Distinct())
            _body.Add(NativeTheme.Body(RemainingTask(task), NativeTheme.Muted));
        if (_proposal.Evaluation.Value?.Review.Balances is { } balance)
        {
            _body.Add(NativeTheme.Metric(Copy("KarmaBefore", "Creation Karma remaining"), Number(balance.RemainingCreationKarma)));
            if (balance.RemainingCreationKarma is > 0
                && _proposal.Coverage.FirstOrDefault(row => row.Category == "karma-remaining") is { } remainder)
            {
                Label explanation = NativeTheme.Body(KarmaExplanation(remainder.ExplanationKey), NativeTheme.Muted);
                explanation.AutomationId = "creation-completion-karma-remainder";
                _body.Add(explanation);
            }
            _body.Add(NativeTheme.Metric(Copy("NuyenBefore", "Creation nuyen remaining"), Number(balance.RemainingNuyen)));
        }
    }

    private void RenderKarmaQualityProposal()
    {
        if (_inputs is not { } inputs || _proposal?.Evaluation.Value is not { } plan) return;
        VerticalStackLayout changes = new() { AutomationId = "creation-completion-quality-changes", Spacing = 6 };
        foreach (string id in plan.Selections.QualityOptionIds.Except(inputs.Lifestyles.Qualities.SelectedOptionIds))
        {
            var option = inputs.Lifestyles.Qualities.Authority.Options.SingleOrDefault(row => row.OptionId == id);
            if (option is null) continue;
            Label name = NativeTheme.Body(SafeReviewName(QualityCatalogStrings.Name(option.SourceId, option.Name)));
            name.AutomationId = "creation-completion-proposed-quality";
            changes.Add(name);
            var reason = _proposal.Coverage.FirstOrDefault(row => row.Category == "karma-quality-" + option.SourceId);
            if (reason is not null) changes.Add(NativeTheme.Body(KarmaExplanation(reason.ExplanationKey), NativeTheme.Muted));
        }
        if (changes.Children.Count == 0) return;
        _body.Add(NativeTheme.Eyebrow(Copy("Karma.Qualities", "Proposed positive qualities")));
        _body.Add(changes);
    }

    internal static string KarmaExplanation(string key)
        => key switch
        {
            "completion.karma.explicit-reserve" => Copy("Karma.Reserved", "This Karma is reserved and stays unspent."),
            "completion.karma.reason.courage" => Copy("Karma.Courage", "Helps this character face fear."),
            "completion.karma.reason.handedness" => Copy("Karma.Handedness", "Supports using either hand."),
            "completion.karma.reason.judgment" => Copy("Karma.Judgment", "Supports practical judgment in risky situations."),
            "completion.karma.reason.timing" => Copy("Karma.Timing", "Supports precise timing."),
            "completion.karma.reason.reading" => Copy("Karma.Reading", "Helps process written information quickly."),
            "completion.karma.reason.driving" => Copy("Karma.Driving", "Fits this character's vehicle-focused role."),
            "completion.karma.reason.repair" => Copy("Karma.Repair", "Fits hands-on technical improvisation."),
            "completion.karma.reason.matrix" => Copy("Karma.Matrix", "Fits this character's Matrix-focused role."),
            "completion.karma.reason.recovery" => Copy("Karma.Recovery", "Supports recovery after injury."),
            "completion.karma.reason.stealth" => Copy("Karma.Stealth", "Supports moving unnoticed."),
            "completion.karma.reason.mobility" => Copy("Karma.Mobility", "Supports physical mobility."),
            "completion.karma.reason.resilience" => Copy("Karma.Resilience", "Supports surviving physical danger."),
            "completion.karma.reason.memory" => Copy("Karma.Memory", "Supports remembering useful details."),
            "completion.karma.reason.analysis" => Copy("Karma.Analysis", "Supports analytical problem-solving."),
            "completion.karma.reason.first-impression" => Copy("Karma.FirstImpression", "Fits a role that depends on meeting new people."),
            _ => Copy("Karma.Remaining", "The checked suggestions leave Karma available. Further legal options may exist; Karma purchases for skills and specializations still need manual selection.")
        };

    private void RenderMagicProposal()
    {
        if (_inputs is not { } inputs || _proposal?.Evaluation.Value is not { } plan) return;
        var original = inputs.Magic.Selections;
        var proposed = plan.Selections.MagicSelections;
        VerticalStackLayout changes = new() { AutomationId = "creation-completion-magic-changes", Spacing = 6 };
        if (original.Tradition is null && proposed.Tradition is { } tradition)
            Add(tradition, Copy("MagicTradition", "Tradition"), "creation-completion-proposed-tradition");
        if (original.Stream is null && proposed.Stream is { } stream)
            Add(stream, Copy("MagicStream", "Stream"), "creation-completion-proposed-stream");
        foreach (var spell in proposed.Spells.Where(identity => !original.Spells.Contains(identity)))
            Add(spell, Copy("MagicSpell", "Spell"), "creation-completion-proposed-spell");
        foreach (var form in proposed.ComplexForms.Where(identity => !original.ComplexForms.Contains(identity)))
            Add(form, Copy("MagicComplexForm", "Complex form"), "creation-completion-proposed-complex-form");
        if (changes.Children.Count == 0) return;
        _body.Add(NativeTheme.Eyebrow(Copy("MagicChanges", "Proposed magic choices")));
        _body.Add(changes);

        void Add(CharacterCreationMagicResonanceOptionIdentity identity, string title, string automationId)
        {
            Label value = NativeTheme.Body(title + ": " + MagicOptionLabel(identity));
            value.AutomationId = automationId;
            changes.Add(value);
        }
    }

    private string MagicOptionLabel(CharacterCreationMagicResonanceOptionIdentity identity)
    {
        var authority = _inputs!.Magic.Authority;
        IReadOnlyList<CharacterCreationMagicResonanceCatalogOption> options = identity.Kind switch
        {
            CharacterCreationMagicResonanceKinds.Tradition => authority.Traditions,
            CharacterCreationMagicResonanceKinds.Stream => authority.Streams,
            CharacterCreationMagicResonanceKinds.Spell => authority.Spells,
            CharacterCreationMagicResonanceKinds.ComplexForm => authority.ComplexForms,
            _ => []
        };
        var matches = options.Where(option => option.Identity == identity).Take(2).ToArray();
        return matches.Length == 1 && !string.IsNullOrWhiteSpace(matches[0].Name)
            ? MagicCatalogStrings.OptionName(identity.Kind, identity.SourceId, matches[0].Name)
            : Copy("MagicUnknown", "Unavailable magic choice");
    }

    private void RenderResourceProposal()
    {
        if (_inputs is not { } inputs || _proposal?.Evaluation.Value is not { } plan) return;
        if (!inputs.Skills.Allocations.Any(row => row.IsNativeLanguage)
            && plan.Selections.SkillAllocations.SingleOrDefault(row => row.IsNativeLanguage) is { } native
            && inputs.Skills.Authority.KnowledgeSkills.FirstOrDefault(row => row.SourceSkillId == native.SourceSkillId) is { } language)
        {
            Label value = NativeTheme.Body(CreationKarmaCopy.NativeLanguage + ": "
                + SkillCatalogStrings.SkillName(language.Kind, language.SourceSkillId, language.Name));
            value.AutomationId = "creation-completion-proposed-native-language";
            _body.Add(value);
        }
        foreach (var contact in plan.Selections.Contacts.Where(row =>
                     !inputs.Contacts.Contacts.Any(original => original.ContactId == row.ContactId)))
        {
            Label value = NativeTheme.Body(CreationFlowStrings.Format("Completion.ContactAddition",
                "{0} · Connection {1}, Loyalty {2}", contact.Identity.Name, contact.Connection, contact.Loyalty));
            value.AutomationId = "creation-completion-proposed-contact";
            _body.Add(value);
        }
        if (plan.Review.Lifestyles is not { } purchases) return;
        foreach (var home in purchases.Lifestyles.Where(row =>
                     !inputs.Lifestyles.Lifestyles.Any(original => original.LifestyleId == row.Configuration.LifestyleId)))
        {
            Label value = NativeTheme.Body(home.BaseLifestyleName + ": " + Number(home.Economics.TotalCost) + " ¥");
            value.AutomationId = "creation-completion-proposed-lifestyle";
            _body.Add(value);
        }
        foreach (var item in purchases.Gear.Lines.Where(row =>
                     inputs.Lifestyles.Gear.State.PendingDraft?.Lines.Any(original => original.OptionId == row.OptionId) != true))
        {
            Label value = NativeTheme.Body(item.Name + " × " + item.Quantity.ToString(CultureInfo.CurrentCulture)
                + ": " + Number(item.TotalCost) + " ¥");
            value.AutomationId = "creation-completion-proposed-equipment";
            _body.Add(value);
        }
    }

    private void RenderLoadoutProposal()
    {
        if (_proposal?.Loadout is not { Lines.Count: > 0 } loadout) return;
        _body.Add(NativeTheme.Eyebrow(Copy("LoadoutItems", "Proposed weapons and armor (including retained choices)")));
        foreach (var item in loadout.Lines)
        {
            Label value = NativeTheme.Body(CreationFlowStrings.Format("Completion.LoadoutLine", "{0} × {1}: {2} ¥",
                item.Name, item.Quantity.ToString(CultureInfo.CurrentCulture), Number(item.TotalCost)));
            value.AutomationId = "creation-completion-proposed-loadout";
            _body.Add(value);
        }
    }

    private void RenderCashAndReview(long appearance, long render)
    {
        if (_cashSources.FirstOrDefault(row => row.AuthorityDigest == _cashDigest) is not { } source)
        {
            _body.Add(NativeTheme.Body(Copy("CashUnavailable", "Starting-cash terms are unavailable. Review your lifestyle in creation, then reopen these suggestions."), NativeTheme.Danger));
            return;
        }
        _body.Add(NativeTheme.Eyebrow(CreationKarmaCopy.DiceTotal));
        _body.Add(NativeTheme.Body(CreationKarmaCopy.StartingCash(source.Name, source.Dice, source.Multiplier)));
        if (_cashSources.Length > 1)
        {
            Picker lifestyle = new()
            {
                Title = CreationAllocationStrings.Get("Finalization.StartingLifestyle", "Lifestyle for starting cash"),
                AutomationId = "creation-completion-cash-source", TextColor = NativeTheme.Text,
                BackgroundColor = NativeTheme.Surface,
                ItemsSource = _cashSources.Select(row => CreationKarmaCopy.StartingCash(row.Name, row.Dice, row.Multiplier)).ToArray(),
                SelectedIndex = Array.FindIndex(_cashSources, row => row.AuthorityDigest == _cashDigest)
            };
            lifestyle.SelectedIndexChanged += (_, _) =>
            {
                if (!Current(appearance, render) || _busy || lifestyle.SelectedIndex < 0) return;
                _cashDigest = _cashSources[lifestyle.SelectedIndex].AuthorityDigest;
                _roll = string.Empty;
                InvalidateProposal();
            };
            _body.Add(lifestyle);
        }
        _body.Add(NativeTheme.Body(CreationKarmaCopy.DiceTotalHelp, NativeTheme.Muted));
        Entry dice = new()
        {
            Text = _roll, Keyboard = Keyboard.Numeric, TextColor = NativeTheme.Text,
            BackgroundColor = NativeTheme.Surface, AutomationId = "creation-completion-cash-roll"
        };
        Button review = AddAction(_footer, Copy("Review", "Review and check completion"), "creation-completion-review",
            ReviewAsync, appearance, render, CanReview());
        dice.TextChanged += (_, _) =>
        {
            if (!Current(appearance, render) || _busy) return;
            _roll = dice.Text ?? string.Empty;
            _inputVersion++;
            _review = null;
            review.IsEnabled = CanReview();
        };
        _body.Add(dice);
        bool CanReview() => !_busy && _proposal?.Evaluation.Value is not null
            && int.TryParse(_roll, NumberStyles.None, CultureInfo.InvariantCulture, out _);
    }

    private async Task ReviewAsync()
    {
        if (_busy || !InputsCurrent() || _proposal is not { } proposal
            || !Coordinator.IsCreationCompletionProposalCurrent(proposal)
            || _cashSources.FirstOrDefault(row => row.AuthorityDigest == _cashDigest) is not { } source
            || !int.TryParse(_roll, NumberStyles.None, CultureInfo.InvariantCulture, out int dice)) return;
        long appearance = CaptureAppearanceGeneration(), version = _inputVersion;
        _busy = true;
        CancellationToken readToken = ReadToken;
        var analysis = BeginReadAnalysis(new(CompletionAnalysisStage.FinalReview, 0, 1),
            () => CurrentInput(appearance, version) && ReferenceEquals(_proposal, proposal)
                && Coordinator.IsCreationCompletionProposalCurrent(proposal), readToken);
        Refresh();
        try
        {
            var result = await Coordinator.ReviewCreationCompletionAsync(proposal, new(source.AuthorityDigest, dice), readToken);
            if (!CurrentInput(appearance, version)) return;
            _blockers = result.Blockers;
            _review = result is { Outcome: CharacterCreationFoundationOutcomes.Success, Value: { } review, Blockers.Count: 0 }
                && Coordinator.IsCreationCompletionReviewCurrent(review) ? review : null;
            if (_review is not null)
                _idempotencyKey = $"creation-completion:{_review.Projection.Binding.WorkspaceId.Value}:{_review.Projection.Binding.ContentRevision}:{_review.Projection.ProjectionDigest}";
        }
        catch (OperationCanceledException) { }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            if (CurrentInput(appearance, version)) _blockers = ["completion-ui-load-failed"];
        }
        finally
        {
            EndReadAnalysis(analysis);
            if (IsCurrentAppearanceGeneration(appearance))
            {
                _busy = false;
                if (CurrentInput(appearance, version)) Refresh();
            }
        }
    }

    private void RenderFinalReview(long appearance, long render)
    {
        var projection = _review!.Projection;
        _body.Add(NativeTheme.Eyebrow(Copy("FinalReview", "Review before Apply")));
        RenderOptionalAttributeKarmaGoals();
        var rows = FinalReviewRows(projection.OrderedDeltas, _inputs!, _proposal!.Evaluation.Value!);
        AddReviewGroups(_body, rows.Where(row => !row.Retained && !row.Setup));
        var details = new VerticalStackLayout { Spacing = 8, IsVisible = false,
            AutomationId = "creation-completion-retained-details" };
        AddReviewGroups(details, rows.Where(row => row.Retained || row.Setup));
        if (details.Children.Count > 0)
        {
            Button toggle = NativeTheme.PrimaryButton(Copy("Review.ShowRetained", "Show retained choices and Career setup"));
            toggle.AutomationId = "creation-completion-retained-toggle";
            // This display-only gesture must not use RunAsync: its final Refresh
            // replaces these controls with a newly collapsed details group.
            toggle.Clicked += (_, _) =>
            {
                if (!Current(appearance, render) || _busy || _applyStarted || _leaving
                    || !ReferenceEquals(toggle.Parent, _body) || !ReferenceEquals(details.Parent, _body)
                    || !toggle.IsEnabled) return;
                details.IsVisible = !details.IsVisible;
                toggle.Text = details.IsVisible ? Copy("Review.HideRetained", "Hide retained choices and Career setup")
                    : Copy("Review.ShowRetained", "Show retained choices and Career setup");
            };
            toggle.HeightRequest = -1;
            toggle.MinimumHeightRequest = 50;
            toggle.LineBreakMode = LineBreakMode.WordWrap;
            toggle.BackgroundColor = Colors.Transparent;
            toggle.TextColor = NativeTheme.Ink;
            toggle.BorderColor = NativeTheme.Line;
            toggle.BorderWidth = 1;
            _body.Add(toggle);
            _body.Add(details);
        }
        var remaining = _proposal.RemainingTasks.Where(key => key != "completion.remaining.finalization-review")
            .Select(RemainingTask).Distinct().ToArray();
        if (remaining.Length > 0)
        {
            _body.Add(NativeTheme.Eyebrow(Copy("Remaining", "Still needs attention")));
            foreach (string task in remaining) _body.Add(NativeTheme.Body(task, NativeTheme.Muted));
        }
        _body.Add(NativeTheme.Metric(Copy("KarmaAfter", "Career Karma"), Number(projection.KarmaRemaining)));
        _body.Add(NativeTheme.Metric(Copy("NuyenAfter", "Career nuyen"), Number(projection.NuyenRemaining)));
        var losses = LossItems(projection);
        if (losses.Count > 0)
        {
            Label warning = NativeTheme.Eyebrow(Copy("Loss.Title", "Unused resources that will be lost"));
            warning.TextColor = NativeTheme.Danger;
            _body.Add(warning);
            foreach (var loss in losses)
            {
                Label amount = NativeTheme.Body(loss.Label + ": " + Number(loss.Amount), NativeTheme.Danger);
                amount.AutomationId = "creation-completion-loss-" + loss.Id;
                _body.Add(amount);
            }
            _body.Add(NativeTheme.Body(Copy("Loss.Help", "These resources will not carry into Career. Keep editing to use them, or explicitly continue."), NativeTheme.Muted));
        }
        if (losses.Count == 0)
            _body.Add(NativeTheme.Body(Copy("NoLoss", "No unused points or excess funds will be lost."), NativeTheme.Muted));
        _body.Add(NativeTheme.Body(Copy("ConfirmHelp", "Apply saves these reviewed changes together and enters Career. Cancel returns to creation without changing your runner."), NativeTheme.Muted));
        AddAction(_footer, Copy("Apply", "Apply and enter Career"), "creation-completion-apply",
            () => ApplyAsync(recoveryOnly: false), appearance, render,
            !_busy && Coordinator.IsCreationCompletionReviewCurrent(_review));
    }

    private async Task ApplyAsync(bool recoveryOnly)
    {
        if (_busy || _review is not { } review || _idempotencyKey is not { } key
            || recoveryOnly != _applyStarted || !IsCurrentPage(CaptureAppearanceGeneration())
            || recoveryOnly && !_outcomeLookupAllowed
            || !recoveryOnly && !Coordinator.IsCreationCompletionReviewCurrent(review)) return;
        long appearance = CaptureAppearanceGeneration();
        Page[] stack = Navigation.NavigationStack.ToArray();
        if (!recoveryOnly)
        {
            var losses = LossItems(review.Projection);
            if (losses.Count > 0)
            {
                // The acknowledgement applies only to this exact admitted
                // projection. It is neither a reusable setting nor save authority.
                string projectionDigest = review.Projection.ProjectionDigest;
                bool accepted = await DisplayAlertAsync(Copy("Loss.Title", "Unused resources that will be lost"),
                    string.Join(Environment.NewLine, losses.Select(loss => loss.Label + ": " + Number(loss.Amount)))
                        + Environment.NewLine + Environment.NewLine
                        + Copy("Loss.Help", "These resources will not carry into Career. Keep editing to use them, or explicitly continue."),
                    Copy("Loss.Continue", "Continue with these losses"), Copy("Loss.Back", "Keep editing"));
                if (!accepted || _busy || _applyStarted || _leaving || ReadToken.IsCancellationRequested
                    || !ReferenceEquals(_review, review)
                    || _idempotencyKey != key || review.Projection.ProjectionDigest != projectionDigest
                    || !IsCurrentPage(appearance) || !Navigation.NavigationStack.SequenceEqual(stack)
                    || !Coordinator.IsCreationCompletionReviewCurrent(review)) return;
            }
        }
        _applyStarted = true;
        _busy = true;
        Refresh();
        try
        {
            // Once dispatched, observe the result even if the page disappears.
            // The coordinator consumes this exact review and owns receipt-only recovery.
            _completion = await Coordinator.ApplyCreationCompletionAsync(review, key, explicitlyConfirmed: true);
            _outcomeLookupAllowed = _completion.Outcome == "outcome-unknown";
            if (!IsCurrentPage(appearance) || !Navigation.NavigationStack.SequenceEqual(stack)) return;
            if (_completion is { Outcome: CharacterCreationFoundationOutcomes.Success, Value: not null })
                await OpenSavedAsync(appearance, stack);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // A navigation failure must never erase a durable receipt. If the
            // call itself unexpectedly throws, do not assume dispatch occurred
            // and offer a recovery call that might start a fresh Apply.
            if (_completion?.Value is null)
            {
                _outcomeLookupAllowed = false;
                _completion = new("outcome-unknown", null, ["creation-completion-apply-outcome-unknown"]);
            }
        }
        finally
        {
            _busy = false;
            if (IsCurrentPage(CaptureAppearanceGeneration())) Refresh();
        }
    }

    internal static IReadOnlyList<(string Id, string Label, decimal Amount)> LossItems(
        CharacterCreationCompletionFinalizationProjection projection)
    {
        // Display differences from Core's reviewed projection, not local
        // carryover rules. Starting cash must never conceal discarded funds.
        (string Id, string Label, decimal Amount)[] losses =
        [
            ("normal-attributes", Copy("Loss.NormalAttributes", "Normal attribute points"), projection.UnusedPoints.NormalAttributes),
            ("special-attributes", Copy("Loss.SpecialAttributes", "Special attribute points"), projection.UnusedPoints.SpecialAttributes),
            ("active-skills", Copy("Loss.ActiveSkills", "Active skill points"), projection.UnusedPoints.ActiveSkills),
            ("skill-groups", Copy("Loss.SkillGroups", "Skill group points"), projection.UnusedPoints.SkillGroups),
            ("knowledge-skills", Copy("Loss.KnowledgeSkills", "Knowledge skill points"), projection.UnusedPoints.KnowledgeSkills),
            ("karma", Copy("Loss.Karma", "Creation Karma"), projection.KarmaBeforeCarryover - projection.KarmaRemaining),
            ("nuyen", Copy("Loss.Nuyen", "Creation nuyen"), projection.NuyenBeforeCarryover - projection.CarriedNuyen)
        ];
        return losses.Where(loss => loss.Amount > 0).ToArray();
    }

    private void RenderSaved(CharacterCreationFinalizationReceipt receipt, long appearance, long render)
    {
        if (!Coordinator.CanDisplayCreationFinalizationReceipt(receipt))
        {
            _body.Add(NativeTheme.Body(Copy("ReceiptAccount", "Return to the original account to view this confirmation."), NativeTheme.Danger));
            return;
        }
        _body.Add(NativeTheme.Body(Copy("Saved", "Your changes are saved. Continue to your saved Career runner.")));
        AddAction(_footer, Copy("Continue", "Continue to saved runner"), "creation-completion-continue",
            () => OpenSavedAsync(appearance, Navigation.NavigationStack.ToArray()), appearance, render, !_busy);
    }

    private async Task OpenSavedAsync(long appearance, IReadOnlyList<Page> stack)
    {
        bool Current() => IsCurrentPage(appearance) && Navigation.NavigationStack.SequenceEqual(stack);
        if (!Current() || _completion?.Value is not { } receipt || !Coordinator.CanDisplayCreationFinalizationReceipt(receipt)) return;
        if (_completion.Blockers.Count == 0 && Coordinator.IsCreationFinalizationReceiptCurrent(receipt))
        {
            try { await Navigation.PopToRootAsync(animated: false); return; }
            catch (Exception error) when (error is not OutOfMemoryException) { }
        }
        if (Current() && Coordinator.CanDisplayCreationFinalizationReceipt(receipt))
            await Navigation.PushAsync(new CreationFinalizationReceiptPage(Coordinator, receipt, _completion.Blockers));
    }

    private void AddCancel(long appearance, long render, bool afterRejection = false)
    {
        Button cancel = NativeTheme.SecondaryButton(Copy("Cancel", "Cancel / fine-tune in creation"));
        cancel.AutomationId = "creation-completion-cancel";
        cancel.Clicked += async (_, _) =>
        {
            if (!IsCurrentPage(appearance) || render != _render || _leaving
                || _applyStarted && !afterRejection) return;
            _leaving = true;
            cancel.IsEnabled = false;
            _reads?.Cancel();
            EndReadAnalysis(_analysis);
            try { await Navigation.PopAsync(); }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                if (IsCurrentPage(appearance))
                    _body.Add(NativeTheme.Body(Copy("BackFailed", "Could not return to creation. Please try Back again."), NativeTheme.Danger));
            }
            finally
            {
                _leaving = false;
                if (IsCurrentPage(appearance)) cancel.IsEnabled = true;
            }
        };
        _footer.Add(cancel);
    }

    private Button AddAction(VerticalStackLayout parent, string title, string id, Func<Task> action,
        long appearance, long render, bool enabled = true)
    {
        Button button = NativeTheme.PrimaryButton(title);
        button.AutomationId = id;
        button.IsEnabled = enabled;
        button.HeightRequest = -1;
        button.MinimumHeightRequest = 50;
        button.Clicked += async (_, _) =>
        {
            if (IsCurrentPage(appearance) && render == _render && ReferenceEquals(button.Parent, parent) && button.IsEnabled)
                await RunAsync(action);
        };
        parent.Add(button);
        return button;
    }

    // Display classification only. A rejected candidate can leave a legal partial
    // plan; only its accepted complete review tells us what is actually affordable.
    // Neither this disclosure nor the plan outcome authorizes Review or Apply.
    internal static IReadOnlyList<(string AttributeId, int Actual, int Desired)> OptionalAttributeKarmaShortfalls(
        CompletionAssistantProposal? proposal)
    {
        if (proposal is not { Request.AllocateRemainingAttributeKarma: true,
                Evaluation.Outcome: CharacterCreationFoundationOutcomes.Success or CharacterCreationFoundationOutcomes.Blocked,
                Evaluation.Value.Attributes: not null,
                Evaluation.Value.Review: { Blockers.Count: 0, Skills: { Blockers.Count: 0,
                    Attributes: { CanConfirm: true, Blockers.Count: 0 } } } review }
            || proposal.Request.Attributes.KarmaTargets is not { Count: > 0 } goals
            || proposal.Evaluation.Value.Blockers.Contains(CharacterCreationAttributeCompletionBlockers.AutomaticPermissionUnavailable)
            || review.Balances.RemainingCreationKarma is not int karma
            || review.Balances.RemainingNuyen is not decimal nuyen
            || review.Balances.KarmaOverspend != 0 || review.Balances.NuyenOverspend != 0)
            return [];

        var request = proposal.Request;
        var skills = review.Skills!;
        var attributes = skills.Attributes;
        if (karma < Math.Max(0, request.KarmaToReserve) || nuyen < Math.Max(0m, request.NuyenToReserve)
            || attributes.NormalPointBudget.Remaining < request.Attributes.NormalPointsToReserve
            || attributes.SpecialPointBudget.Remaining < request.Attributes.SpecialPointsToReserve
            || skills.ActiveSkillPointBudget.Remaining < request.Skills.ActivePointsToReserve
            || skills.SkillGroupPointBudget.Remaining < request.Skills.GroupPointsToReserve
            || skills.KnowledgeSkillPointBudget.Remaining < request.Skills.KnowledgePointsToReserve)
            return [];

        List<(string AttributeId, int Actual, int Desired)> shortfalls = [];
        foreach (var goal in goals)
        {
            var actual = attributes.Attributes.FirstOrDefault(row => row.AttributeId == goal.AttributeId);
            if (actual is null || !request.Attributes.PermittedAttributeOrder.Contains(goal.AttributeId)
                || CreationAllocationStrings.AttributeName(goal.AttributeId) == goal.AttributeId)
                return []; // Unknown identities must never become user-facing labels.
            if (actual.UnaugmentedCurrent < goal.TargetUnaugmentedRating)
                shortfalls.Add((goal.AttributeId, actual.UnaugmentedCurrent, goal.TargetUnaugmentedRating));
        }
        return shortfalls;
    }

    private static bool IsAttributeKarmaFundingFinding(string code) => code is
        CharacterCreationAttributeCompletionBlockers.KarmaSpendLimit
        or CharacterCreationCompletionPlanBlockers.KarmaReserveRequired
        or CharacterCreationCompletionPlanBlockers.NuyenReserveRequired
        or CharacterCreationAttributesBlockers.GlobalKarmaExceeded
        or CharacterCreationFinalizationBlockers.GlobalKarmaExceeded
        or CharacterCreationResourcesBlockers.InsufficientCreationKarma;

    internal static IReadOnlyList<string> RequiredProposalBlockers(
        CompletionAssistantProposal? proposal, IReadOnlyList<string> blockers)
        => OptionalAttributeKarmaShortfalls(proposal).Count == 0 ? blockers
            : blockers.Where(code => !proposal!.Evaluation.Value!.Attributes!.Blockers.Contains(code)
                || code != CharacterCreationAttributeCompletionBlockers.KarmaTargetsUnfulfilled
                    && !IsAttributeKarmaFundingFinding(code)).ToArray();

    private void RenderOptionalAttributeKarmaGoals()
    {
        var shortfalls = OptionalAttributeKarmaShortfalls(_proposal);
        if (shortfalls.Count == 0) return;
        string message = _proposal!.Evaluation.Value!.Attributes!.Blockers.Any(IsAttributeKarmaFundingFinding)
            ? Copy("OptionalKarmaFunding", "More attribute recommendations could not be funded within the requested limits. Existing choices and reserves are preserved. Review this proposal before choosing Apply.")
            : Copy("OptionalKarmaGoals", "Some optional attribute goals remain unmet. Existing choices and reserves are preserved. Review this proposal before choosing Apply.");
        Label explanation = NativeTheme.Body(message, NativeTheme.Muted);
        explanation.AutomationId = "creation-completion-optional-karma";
        _body.Add(explanation);
        foreach (var goal in shortfalls)
        {
            Label rating = NativeTheme.Body(CreationFlowStrings.Format("Completion.OptionalKarmaRating",
                "{0}: proposed {1}, optional goal {2}", CreationAllocationStrings.AttributeName(goal.AttributeId),
                goal.Actual, goal.Desired), NativeTheme.Muted);
            rating.AutomationId = "creation-completion-optional-karma-" + goal.AttributeId;
            _body.Add(rating);
        }
    }

    private void AddBlockers(IEnumerable<string> blockers)
    {
        foreach (string message in blockers.Select(code => code switch
        {
            CharacterCreationFinalizationBlockers.StartingCashChoiceInvalid => CreationAllocationStrings.Get("Finalization.InvalidDiceTotal", "That total does not match these dice. Check your roll and try again."),
            _ => CreationFlowStrings.FinalizationBlocker(code) is var known && known != code ? known
                : Copy("Blocked", "Some choices could not be verified. Return to creation, review the required steps, then try a fresh proposal.")
        }).Distinct(StringComparer.Ordinal)) _body.Add(NativeTheme.Body(message, NativeTheme.Danger));
    }

    private string SuggestionLabel(CompletionChoiceIdentity choice) => choice.Domain switch
    {
        CompletionChoiceDomain.Attribute => CreationAllocationStrings.AttributeName(choice.SourceId),
        CompletionChoiceDomain.ActiveSkill => _inputs!.Skills.Authority.ActiveSkills.FirstOrDefault(row => row.SourceSkillId == choice.SourceId) is { } skill
            ? SkillCatalogStrings.SkillName(skill.Kind, skill.SourceSkillId, skill.Name) : Copy("UnknownSkill", "Unavailable skill"),
        CompletionChoiceDomain.KnowledgeSkill => _inputs!.Skills.Authority.KnowledgeSkills.FirstOrDefault(row => row.SourceSkillId == choice.SourceId) is { } knowledge
            ? SkillCatalogStrings.SkillName(knowledge.Kind, knowledge.SourceSkillId, knowledge.Name) : Copy("UnknownSkill", "Unavailable skill"),
        CompletionChoiceDomain.SkillGroup => _inputs!.Skills.Authority.SkillGroups.FirstOrDefault(row => row.GroupId == choice.SourceId) is { } group
            ? SkillCatalogStrings.GroupName(group.Name) : Copy("SkillGroup", "Skill group"),
        _ => Copy("SkillGroup", "Skill group")
    };

    private static string RemainingTask(string key) => key switch
    {
        "completion.remaining.practical-loadout" => Copy("Loadout", "Basic supplies, a weapon and armor do not make a complete loadout."),
        "completion.remaining.ammo-modifications-rated-gear" => Copy("LoadoutExtras", "Review ammunition, modifications and rated gear in creation; these are not included in the weapon and armor suggestions."),
        "completion.remaining.ammo-binding-loading-modifications" => Copy("AmmoExtras", "Loose ammunition may be proposed. Weapon assignment, loading and modifications still need review."),
        "completion.remaining.commlinks-rated-gear-child-graphs" => Copy("AdvancedGear", "Commlinks, rated gear and bundled accessories are not included in these suggestions."),
        "completion.remaining.weapon-choices" => Copy("WeaponNeeded", "Review your weapon choices in creation."),
        "completion.remaining.armor-choices" => Copy("ArmorNeeded", "Review your armor choices in creation."),
        "completion.remaining.identity-review" => Copy("Identity", "Review your name, identity and story yourself."),
        "completion.remaining.starter-targets" => Copy("Targets", "Some starter targets are locked, unavailable or unmet. They are advice, not automatic permission to change your choices."),
        "completion.remaining.magic-choices" or "completion.remaining.magic-evaluation" => Copy("Magic", "Review the magic choices required by your talent."),
        "completion.remaining.skills-choices" or "completion.remaining.skills-evaluation" => Copy("SkillsNeeded", "Review skills, native language and any unallocated skill points."),
        "completion.remaining.attributes-choices" or "completion.remaining.attributes-evaluation" => Copy("AttributesNeeded", "Review your attribute allocations."),
        "completion.remaining.attribute-choice-intent" => Copy("AttributeChoiceIntent", "Your saved attribute allocation stays unchanged. The assistant cannot tell which values you deliberately kept, so finish any remaining attribute points in creation."),
        "completion.remaining.lifestyle-choices" or "completion.remaining.lifestyle-evaluation" => Copy("LifestyleNeeded", "Review your lifestyle and starting-cash selection."),
        "completion.remaining.contacts-choices" or "completion.remaining.contacts-evaluation" => Copy("ContactsNeeded", "Review your contacts."),
        "completion.remaining.resources-choices" or "completion.remaining.resources-evaluation" => Copy("ResourcesNeeded", "Review your creation funding."),
        "completion.remaining.gear-choices" or "completion.remaining.gear-evaluation" => Copy("GearNeeded", "Review your equipment choices."),
        "completion.remaining.qualities-choices" or "completion.remaining.qualities-evaluation" => Copy("QualitiesNeeded", "Review your qualities."),
        _ => Copy("ManualNeeded", "This part still needs manual review in creation.")
    };

    private static string ArchetypeLabel(string id) => id switch
    {
        CompletionAssistantRules.GeneralSurvivor => Copy("Archetype.General", "General survivor"),
        CompletionAssistantRules.StreetSamurai => Copy("Archetype.Samurai", "Street samurai"),
        CompletionAssistantRules.Face => Copy("Archetype.Face", "Face"),
        CompletionAssistantRules.Mage => Copy("Archetype.Mage", "Mage"),
        CompletionAssistantRules.Decker => Copy("Archetype.Decker", "Decker"),
        CompletionAssistantRules.Rigger => Copy("Archetype.Rigger", "Rigger"),
        CharacterCreationMagicResonanceKinds.Adept => CreationAllocationStrings.Get("Completion.Archetype.Adept", "Adept"),
        CharacterCreationMagicResonanceKinds.MysticAdept => CreationAllocationStrings.Get("Completion.Archetype.MysticAdept", "Mystic adept"),
        CharacterCreationMagicResonanceKinds.Technomancer => CreationAllocationStrings.Get("Completion.Archetype.Technomancer", "Technomancer"),
        CharacterCreationMagicResonanceKinds.ArtificialIntelligence => CreationAllocationStrings.Get("Completion.Archetype.ArtificialIntelligence", "Artificial intelligence"),
        _ => CreationAllocationStrings.Get("Completion.Archetype.Unknown", "Other suggestion style")
    };

    internal sealed record FinalReviewRow(string Area, string Label, string Value, bool Retained = false, bool Setup = false);

    private static void AddReviewGroups(VerticalStackLayout parent, IEnumerable<FinalReviewRow> rows)
    {
        foreach (var group in rows.GroupBy(row => row.Area))
        {
            parent.Add(NativeTheme.Eyebrow(group.Key));
            foreach (var row in group)
                parent.Add(NativeTheme.Body(row.Label + ": " + row.Value));
        }
    }

    // Core deltas describe the complete projected graph, including baseline assignments.
    // This is display only: compare admitted original rows, never alter the review or its digest.
    internal static IReadOnlyList<FinalReviewRow> FinalReviewRows(
        IEnumerable<CharacterCreationFinalizationDelta> deltas, CharacterCreationCompletionReviewInputs inputs,
        CharacterCreationCompletionPlan plan)
    {
        List<FinalReviewRow> rows = [];
        foreach (var delta in deltas.OrderBy(row => row.Order))
        {
            string area = Copy("Review.Other", "Other reviewed details");
            string label = Copy("Review.Unknown", "Choice details unavailable");
            string? before = delta.BeforeValue, after = delta.AfterValue;
            bool retained = false, setup = false;
            bool names = false;
            switch (delta.Kind)
            {
                case CharacterCreationFinalizationDeltaKinds.Build:
                    area = Copy("Review.Build", "Build choices");
                    var assignment = inputs.Skills.PrerequisiteDraft.Assignments.FirstOrDefault(row =>
                        delta.DeltaId == "priority:" + row.CategoryId && delta.TargetId == row.CategoryId
                        && delta.AfterValue == string.Create(CultureInfo.InvariantCulture, $"{row.Rank},{row.SumToTenValue}"));
                    if (assignment is not null && assignment.Rank is "A" or "B" or "C" or "D" or "E")
                    {
                        label = WizardStrings.PriorityCategory(assignment.CategoryId, label);
                        after = inputs.Skills.PrerequisiteDraft.BuildMethod switch
                        {
                            CharacterCreationBuildMethods.Priority => CreationFlowStrings.Format("Completion.Review.Priority", "Priority {0}", assignment.Rank),
                            CharacterCreationBuildMethods.SumToTen => CreationFlowStrings.Format("Completion.Review.SumToTen", "{0} ({1} Sum-to-Ten points)", assignment.Rank, assignment.SumToTenValue),
                            _ => Copy("Review.Unknown", "Choice details unavailable")
                        };
                        before = after;
                        retained = names = true;
                    }
                    else if (delta.DeltaId == "qualities:karma-adjustment")
                        label = Copy("Review.QualityKarma", "Quality Karma after limits and rebates");
                    break;
                case CharacterCreationFinalizationDeltaKinds.Metatype:
                    area = Copy("Review.Build", "Build choices");
                    label = Copy("Review.Metatype", "Metatype");
                    var heritage = inputs.Skills.PrerequisiteDraft.HeritageSelection;
                    retained = heritage is not null && heritage.MetatypeSourceId == delta.TargetId && heritage.MetatypeName == after;
                    if (retained) before = after;
                    before = before is null ? null : MagicCatalogStrings.MetatypeName(before);
                    after = after is null ? null : MagicCatalogStrings.MetatypeName(after);
                    names = true;
                    break;
                case CharacterCreationFinalizationDeltaKinds.Attribute:
                    area = Copy("Review.Attributes", "Attributes");
                    label = CreationAllocationStrings.AttributeName(delta.TargetId);
                    if (label == delta.TargetId) label = Copy("Review.Unknown", "Choice details unavailable");
                    if (inputs.Skills.Attributes.Attributes.FirstOrDefault(row => row.AttributeId == delta.TargetId) is { } attribute)
                        before = attribute.Current.ToString(CultureInfo.InvariantCulture);
                    retained = before == after;
                    break;
                case CharacterCreationFinalizationDeltaKinds.Skill:
                    area = Copy("Review.Skills", "Skills and languages");
                    var skill = plan.Review.Skills?.Skills.FirstOrDefault(row =>
                        delta.DeltaId == $"skill:{row.Kind}:{row.SourceSkillId}");
                    if (skill is not null)
                    {
                        label = SkillCatalogStrings.SkillName(skill.Kind, skill.SourceSkillId, skill.Name);
                        var original = inputs.Skills.SavedSkillsState?.Skills.FirstOrDefault(row => row.Kind == skill.Kind && row.SourceSkillId == skill.SourceSkillId);
                        var allocation = inputs.Skills.Allocations.FirstOrDefault(row => row.Kind == skill.Kind && row.SourceSkillId == skill.SourceSkillId);
                        before = original is not null ? SkillValue(original.IsNativeLanguage, original.EffectiveRating)
                            : allocation is not null ? SkillValue(allocation.IsNativeLanguage, allocation.Rating) : null;
                        after = SkillValue(skill.IsNativeLanguage, skill.EffectiveRating);
                        // Specializations are part of a skill choice, even when its rating is unchanged.
                        string? oldSpecialization = original?.SpecializationName
                            ?? inputs.Skills.Authority.ActiveSkills.Concat(inputs.Skills.Authority.KnowledgeSkills)
                                .FirstOrDefault(row => row.Kind == skill.Kind && row.SourceSkillId == skill.SourceSkillId)
                                ?.Specializations.FirstOrDefault(row => row.OptionId == allocation?.SpecializationOptionId)?.Name;
                        if (oldSpecialization is not null) before += " (" + SafeReviewName(SkillCatalogStrings.SpecializationName(skill.Kind, skill.SourceSkillId, skill.Name, oldSpecialization)) + ")";
                        if (skill.SpecializationName is { } specialization) after += " (" + SafeReviewName(SkillCatalogStrings.SpecializationName(skill.Kind, skill.SourceSkillId, skill.Name, specialization)) + ")";
                        retained = before == after;
                        names = true;
                    }
                    break;
                case CharacterCreationFinalizationDeltaKinds.SkillGroup:
                    area = Copy("Review.Skills", "Skills and languages");
                    if (inputs.Skills.Authority.SkillGroups.FirstOrDefault(row => row.GroupId == delta.TargetId) is { } group)
                    {
                        label = SkillCatalogStrings.GroupName(group.Name);
                        before = (inputs.Skills.SavedSkillsState?.SkillGroups.FirstOrDefault(row => row.GroupId == group.GroupId)?.Rating
                            ?? inputs.Skills.GroupAllocations.FirstOrDefault(row => row.GroupId == group.GroupId)?.Rating)?.ToString(CultureInfo.InvariantCulture);
                        retained = before == after;
                    }
                    break;
                case CharacterCreationFinalizationDeltaKinds.Quality:
                    area = Copy("Review.Qualities", "Qualities");
                    if (Guid.TryParse(delta.TargetId, out var qualityId) && delta.TargetName is { } qualityName)
                        label = QualityCatalogStrings.Name(qualityId, qualityName);
                    retained = inputs.Lifestyles.Qualities.SelectedOptionIds.Any(id => delta.DeltaId == "quality:" + id)
                        || delta.DeltaId.StartsWith("talent-quality:", StringComparison.Ordinal)
                        || delta.DeltaId.StartsWith("racial-quality:", StringComparison.Ordinal);
                    if (retained) before = after;
                    names = after == delta.TargetName;
                    if (names) { after = label; if (retained) before = after; }
                    break;
                case CharacterCreationFinalizationDeltaKinds.Gear:
                case "weapon":
                case "armor":
                    area = Copy("Review.Equipment", "Equipment");
                    label = delta.TargetName ?? label;
                    if (delta.Kind == CharacterCreationFinalizationDeltaKinds.Gear)
                        before = inputs.Lifestyles.Gear.State.PendingDraft?.Lines.FirstOrDefault(row => delta.DeltaId == "gear:" + row.OptionId)?.Quantity.ToString(CultureInfo.InvariantCulture);
                    else if (delta.Kind == "weapon")
                        before = inputs.Loadout?.OriginalSelections.Weapons.FirstOrDefault(row => row.OptionId == delta.TargetId)?.Quantity.ToString(CultureInfo.InvariantCulture);
                    else
                        before = inputs.Loadout?.OriginalSelections.Armor.FirstOrDefault(row => row.OptionId == delta.TargetId)?.Quantity.ToString(CultureInfo.InvariantCulture);
                    retained = before == after;
                    if (delta.DeltaId.StartsWith("talent-gear:", StringComparison.Ordinal))
                    { before = after; retained = names = true; }
                    else label = CreationFlowStrings.Format("Completion.Review.Quantity", "{0} · quantity", SafeReviewName(label));
                    break;
                case "contact":
                    area = Copy("Review.Contacts", "Contacts and lifestyle");
                    var contact = plan.Selections.Contacts.FirstOrDefault(row => row.ContactId.ToString("D") == delta.TargetId);
                    var oldContact = inputs.Contacts.Contacts.FirstOrDefault(row => row.ContactId.ToString("D") == delta.TargetId);
                    label = Copy("Review.Contact", "Contact");
                    before = oldContact is null ? null : ContactValue(oldContact);
                    after = contact is null ? delta.AfterValue : ContactValue(contact);
                    retained = oldContact is not null && oldContact == contact;
                    names = true;
                    break;
                case "lifestyle":
                    area = Copy("Review.Contacts", "Contacts and lifestyle");
                    var home = plan.Review.Lifestyles?.Lifestyles.FirstOrDefault(row => row.Configuration.LifestyleId.ToString("D") == delta.TargetId);
                    var oldHome = inputs.Lifestyles.Lifestyles.FirstOrDefault(row => row.LifestyleId.ToString("D") == delta.TargetId);
                    // Only the admitted configuration supplies a home name; raw delta keys are not labels.
                    if (home is not null)
                    {
                        label = SafeReviewName(home.Configuration.Name);
                        if (!string.Equals(home.Configuration.Name, home.BaseLifestyleName, StringComparison.Ordinal))
                            label += " (" + SafeReviewName(home.BaseLifestyleName) + ")";
                    }
                    before = oldHome?.Increments.ToString(CultureInfo.InvariantCulture);
                    retained = home is not null && oldHome is not null
                        && CharacterCreationFinalizationDigest.Compute(oldHome) == CharacterCreationFinalizationDigest.Compute(home.Configuration);
                    label = CreationFlowStrings.Format("Completion.Review.Increments", "{0} · periods", SafeReviewName(label));
                    break;
                case CharacterCreationFinalizationDeltaKinds.MagicResonance:
                    area = Copy("Review.Magic", "Magic and resonance");
                    var catalog = inputs.Magic.Authority;
                    var option = catalog.Traditions.Concat(catalog.Streams).Concat(catalog.Spells).Concat(catalog.ComplexForms).Concat(catalog.AdeptPowers)
                        .FirstOrDefault(row => delta.DeltaId == row.Identity.Kind + ":" + row.Identity.SourceId);
                    if (option is not null)
                    {
                        string optionName = MagicCatalogStrings.OptionName(option.Identity.Kind, option.Identity.SourceId, option.Name);
                        label = option.Identity.Kind switch
                        {
                            CharacterCreationMagicResonanceKinds.Tradition => Copy("MagicTradition", "Tradition"),
                            CharacterCreationMagicResonanceKinds.Stream => Copy("MagicStream", "Stream"),
                            CharacterCreationMagicResonanceKinds.Spell => Copy("MagicSpell", "Spell"),
                            CharacterCreationMagicResonanceKinds.ComplexForm => Copy("MagicComplexForm", "Complex form"),
                            _ => optionName
                        };
                        var original = inputs.Magic.Selections;
                        retained = original.Tradition == option.Identity || original.Stream == option.Identity
                            || original.Spells.Contains(option.Identity) || original.ComplexForms.Contains(option.Identity);
                        after = optionName;
                        before = retained ? after : null;
                        if (option.Identity.Kind == CharacterCreationMagicResonanceKinds.AdeptPower)
                        {
                            label = optionName;
                            before = original.AdeptPowers.FirstOrDefault(row => row.Identity == option.Identity)?.Levels.ToString(CultureInfo.InvariantCulture);
                            after = plan.Selections.MagicSelections.AdeptPowers.FirstOrDefault(row => row.Identity == option.Identity)?.Levels.ToString(CultureInfo.InvariantCulture);
                            retained = before == after && before is not null;
                        }
                        names = true;
                    }
                    else if (delta.DeltaId == "talent-flag:" + delta.TargetId && after == "True"
                        && delta.TargetId is "magenabled" or "resenabled" or "depenabled" or "adept" or "magician" or "technomancer" or "ai")
                    {
                        label = delta.TargetId switch
                        {
                            "magenabled" => Copy("Review.MagicEnabled", "Magic attribute enabled"),
                            "resenabled" => Copy("Review.ResonanceEnabled", "Resonance attribute enabled"),
                            "depenabled" => Copy("Review.DepthEnabled", "Depth attribute enabled"),
                            "adept" => Copy("Review.Adept", "Adept talent"),
                            "magician" => Copy("Review.Magician", "Magician talent"),
                            "technomancer" => Copy("Review.Technomancer", "Technomancer talent"),
                            "ai" => Copy("Review.Ai", "AI talent"),
                            _ => label
                        };
                        // These are effects of the unchanged, admitted talent, not new talent choices.
                        before = after = Copy("Review.Enabled", "Enabled");
                        retained = names = true;
                    }
                    else if (delta.DeltaId == "mystic-adept:power-points")
                    {
                        label = Copy("Review.PowerPoints", "Purchased power points");
                        before = inputs.Magic.Selections.MysticAdeptPowerPoints.ToString(CultureInfo.InvariantCulture);
                        retained = before == after;
                    }
                    break;
                case CharacterCreationFinalizationDeltaKinds.Resources:
                    area = Copy("Review.Resources", "Resources and carryover");
                    label = delta.DeltaId switch
                    {
                        "resources:starting-nuyen" => Copy("Review.Funding", "Creation funds"),
                        "resources:remaining-nuyen" => Copy("NuyenBefore", "Creation nuyen remaining"),
                        "carryover:karma" => Copy("Review.KarmaCarryover", "Karma carried into Career"),
                        "carryover:nuyen" => Copy("Review.NuyenCarryover", "Nuyen carried into Career"),
                        "starting-cash:lifestyle" => Copy("Review.CashLifestyle", "Starting-cash lifestyle"),
                        "starting-cash:dice" => Copy("Review.CashDice", "Starting-cash dice total"),
                        "starting-cash:nuyen" => Copy("Review.CashAdded", "Nuyen after starting cash"),
                        _ => label
                    };
                    names = delta.DeltaId == "starting-cash:lifestyle";
                    break;
                case CharacterCreationFinalizationDeltaKinds.Lifecycle:
                    area = Copy("Review.Career", "Career");
                    if (delta.TargetId == "created" && before == "False" && after == "True")
                    {
                        rows.Add(new(area, Copy("Review.Mode", "Mode"), Copy("CareerTransition", "Creation → Career")));
                        continue;
                    }
                    if (delta.DeltaId == "career-initialization:" + delta.TargetId)
                    {
                        setup = true;
                        area = Copy("Review.Setup", "Career setup");
                        label = delta.TargetId switch
                        {
                            "streetcred" => Copy("Review.StreetCred", "Street Cred"),
                            "notoriety" => Copy("Review.Notoriety", "Notoriety"),
                            "publicawareness" => Copy("Review.PublicAwareness", "Public Awareness"),
                            "burntstreetcred" => Copy("Review.BurntStreetCred", "Spent Street Cred"),
                            "expenses" => Copy("Review.Expenses", "Expense history"),
                            "improvements" => Copy("Review.Effects", "Effects"),
                            "contacts" => Copy("Review.ContactList", "Contact list"),
                            _ => label
                        };
                    }
                    break;
            }
            string value = retained ? CreationFlowStrings.Format("Completion.Review.Retained", "{0} · retained", ReviewValue(after, names))
                : before is null ? ReviewValue(after, names)
                : CreationFlowStrings.Format("Completion.Review.BeforeAfter", "before {0} → after {1}", ReviewValue(before, names), ReviewValue(after, names));
            if (delta.KarmaCost != 0 || delta.NuyenCost != 0)
                value += " · " + CreationFlowStrings.Format("Completion.Review.Cost", "reviewed total: {0} Karma, {1} ¥", Number(delta.KarmaCost), Number(delta.NuyenCost));
            rows.Add(new(area, SafeReviewName(label), value, retained, setup));
        }
        return rows;
    }

    private static string? SkillValue(bool native, int? rating) => native ? CreationKarmaCopy.NativeLanguage : rating?.ToString(CultureInfo.InvariantCulture);
    private static string ContactValue(CharacterCreationKarmaContactSelection contact) => CreationFlowStrings.Format(
        "Completion.ContactAddition", "{0} · Connection {1}, Loyalty {2}", SafeReviewName(contact.Identity.Name), contact.Connection, contact.Loyalty);
    private static string ReviewValue(string? value, bool name) => string.IsNullOrEmpty(value) ? "—"
        : name ? SafeReviewName(value)
        : decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number)
            ? Number(number) : Copy("Review.Unknown", "Choice details unavailable");

    // Punctuation belongs in real names (for example, Dr.-Singer-Schule).
    // Unknown delta semantics are handled by their typed branches, not guessed from punctuation.
    private static string SafeReviewName(string? value) => string.IsNullOrWhiteSpace(value)
        || System.Text.RegularExpressions.Regex.IsMatch(value,
            @"(?i)[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}|\b[0-9a-f]{32,}\b|\binternal[._:/][a-z0-9_.:/-]+\b")
        ? Copy("Review.Unknown", "Choice details unavailable") : value;
    private Guid? SelectedLifestyleId(CharacterCreationCompletionReviewInputs inputs)
    {
        var source = _cashSources.FirstOrDefault(row => row.AuthorityDigest == _cashDigest);
        var option = inputs.Lifestyles.Authority.LifestyleOptions.FirstOrDefault(row => row.SourceId.ToString("D") == source?.SourceId);
        return option is null ? null : inputs.Lifestyles.Lifestyles.FirstOrDefault(row => row.BaseLifestyleOptionId == option.OptionId)?.LifestyleId;
    }
    private bool InputsCurrent() => _inputs is not null && Coordinator.IsCreationCompletionInputsCurrent(_inputs);
    private bool IsCurrentPage(long appearance) => _visible && IsCurrentAppearanceGeneration(appearance)
        && ReferenceEquals(Navigation.NavigationStack.LastOrDefault(), this);
    private bool Current(long appearance, long render) => IsCurrentPage(appearance) && render == _render && InputsCurrent();
    private bool CurrentInput(long appearance, long version) => IsCurrentPage(appearance) && !_leaving
        && !ReadToken.IsCancellationRequested && version == _inputVersion && InputsCurrent();
    private CancellationToken ReadToken => _reads?.Token ?? new CancellationToken(canceled: true);
    private static string Number(decimal? value) => value?.ToString("0.##", CultureInfo.CurrentCulture) ?? "—";
    internal static string Copy(string key, string fallback) => CreationFlowStrings.Get("Completion." + key, fallback);
}
