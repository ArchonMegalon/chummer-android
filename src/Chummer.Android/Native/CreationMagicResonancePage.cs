using System.Globalization;
using Chummer.Contracts.Characters;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

/// <summary>
/// Native phone Draft step for SR5 Priority/Sum-to-Ten Magic/Resonance. Every displayed cost,
/// budget, prerequisite, blocker and source comes from the current Core/Presentation projection.
/// </summary>
public sealed class CreationMagicResonancePage : NativePageBase
{
    private VerticalStackLayout _technicalDetails = new() { Spacing = 6 };
    private readonly HashSet<string> _shownBlockerMessages = new(StringComparer.Ordinal);
    private readonly CreationMagicResonancePhoneDraft _draft = new();
    private readonly CharacterCreationMagicResonanceCheckpointStore _store;
    private readonly VerticalStackLayout _body = new()
    {
        Padding = new Thickness(20, 18, 20, 40),
        Spacing = 14
    };
    private IReadOnlyList<string> _localBlockers = [];
    private CharacterOverviewState? _loadedDisplay;
    private CharacterCreationFoundationResult<CharacterCreationMagicResonanceState>? _loaded;
    private CharacterCreationMagicResonanceEditorState? _editor;
    private bool _loading = true;

    public CreationMagicResonancePage(RunnerSessionCoordinator coordinator)
        : this(
            coordinator,
            CharacterCreationMagicResonanceCheckpointStore.CreateDefault(
                coordinator.State.DisplayOwnerContext, coordinator.IsCreationMagicOwnerCurrent,
                coordinator.State.WorkspaceId?.Value
                    ?? throw new InvalidOperationException("Open a runner before Magic / Resonance.")))
    {
    }

    internal CreationMagicResonancePage(
        RunnerSessionCoordinator coordinator,
        CharacterCreationMagicResonanceCheckpointStore store) : base(coordinator)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        Title = CreationFlowStrings.Get("Magic.PageTitle", "Magic / Resonance");
        AutomationId = "creation-magic-resonance-page";
        Content = new ScrollView { Content = _body };
    }

    protected override async Task PrepareForAppearanceRefreshAsync(CancellationToken cancellationToken)
    {
        _loading = true;
        _loadedDisplay = null;
        _loaded = null;
        _editor = null;
        Refresh();
        CharacterOverviewState original = Coordinator.State;
        var before = _draft.Copy();
        var preparedDraft = before.Copy();
        try
        {
            var loaded = await Coordinator.LoadCreationMagicResonanceForDisplayAsync(original, cancellationToken);
            var editor = await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return loaded.Value is { } core
                    && preparedDraft.TryBindLoaded(core, original, out var projected)
                    ? projected : null;
            }, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!Coordinator.IsCreationCatalogDisplayCurrent(original)
                || !_draft.TryAdoptPrepared(before, preparedDraft))
                return;
            _loadedDisplay = original;
            _loaded = loaded;
            _editor = editor;
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested) _loading = false;
        }
    }

    protected override void Refresh()
    {
        _body.Clear();
        _shownBlockerMessages.Clear();
        // The previous disclosure still owns its native child after _body.Clear().
        // Never attach that child to a new parent during a refresh.
        _technicalDetails = new() { Spacing = 6 };
        _body.Add(NativeTheme.Eyebrow(CreationFlowStrings.Get("Magic.DraftEyebrow", "SR5 · Draft")));
        _body.Add(NativeTheme.Title(CreationFlowStrings.Get("Magic.Heading", "Magic and Resonance")));
        _body.Add(NativeTheme.Body(
            CreationFlowStrings.Get(
                "Magic.Intro",
                "Choose the magic and abilities available to your Talent."),
            NativeTheme.Muted));

        if (_loading)
        {
            _body.Add(new ActivityIndicator { IsRunning = true, AutomationId = "creation-magic-resonance-loading" });
            return;
        }
        if (_loaded is not { } load || _loadedDisplay is not { } original
            || !Coordinator.IsCreationCatalogDisplayCurrent(original))
        {
            AddBlockers([CharacterCreationMagicResonanceBlockers.StaleWorkspaceRevision]);
            _body.Add(NativeTheme.TechnicalDetails(_technicalDetails, "creation-magic-resonance-details"));
            return;
        }
        if (load.Value is { } profileState && HasUnsupportedSeparateMagicProfile(profileState))
        {
            var notice = NativeTheme.Body(CreationFlowStrings.Get("Magic.Mystic.SeparateAttributeUnsupported",
                "This rules profile uses a separate MAGAdept attribute. Its allocation is not supported by this wizard yet. Your draft and rules profile remain unchanged."), NativeTheme.Danger);
            notice.AutomationId = "creation-magic-resonance-separate-attribute-unavailable";
            _body.Add(notice);
        }
        if (_editor is not { } editor)
        {
            AddBlockers(load.Blockers.Count == 0
                ? [CharacterCreationMagicResonanceBlockers.AuthorityUnavailable]
                : load.Blockers);
            _body.Add(NativeTheme.TechnicalDetails(_technicalDetails, "creation-magic-resonance-details"));
            return;
        }

        AddBinding(editor);
        AddTalent(editor.Talent);
        CharacterCreationMagicResonanceReview? review = _draft.Review;
        AddBudgets(review?.Preview, editor.Budgets);
        CharacterCreationMagicResonanceCheckpoint? checkpoint = AddRecovery(editor, original);
        bool laneLocked = checkpoint is not null || HasMalformedCheckpoint();
        AddMysticPowerPoints(editor, laneLocked);
        AddCatalogRoutes(editor, laneLocked);
        AddReview(editor, laneLocked);
        AddBlockers(editor.Blockers
            .Concat(review?.Preview.Blockers ?? [])
            .Concat(_localBlockers));
        _body.Add(NativeTheme.TechnicalDetails(_technicalDetails, "creation-magic-resonance-details"));
    }

    internal static bool HasUnsupportedSeparateMagicProfile(CharacterCreationMagicResonanceState state) =>
        state.Authority.IsAuthoritative
        && state.SelectedTalent is { Kind: CharacterCreationMagicResonanceKinds.MysticAdept, IsEnabled: false }
        && state.Authority.MysticAdeptPowerPointPolicy?.UsesSeparateMagicAttribute == true
        && state.Blockers.Contains(CharacterCreationMagicResonanceBlockers.PowerBudgetUnsupported, StringComparer.Ordinal);

    private void AddMysticPowerPoints(CharacterCreationMagicResonanceEditorState editor, bool laneLocked)
    {
        var purchase = _draft.Review?.Preview.MysticAdeptPowerPoints ?? editor.MysticAdeptPowerPoints;
        if (purchase is null) return;
        var card = new VerticalStackLayout { Spacing = 6 };
        card.AutomationId = "creation-magic-resonance-mystic-purchase";
        card.Add(NativeTheme.Title(CreationFlowStrings.Get("Magic.Mystic.Title", "Mystic Adept power points"), 22));
        card.Add(NativeTheme.Body(MysticPowerPointSummary(purchase), NativeTheme.Muted));
        card.Add(NativeTheme.Body(CreationFlowStrings.Get("Magic.Mystic.Boundary",
            "Karma is reserved in the final build review. Exchanged spell slots reduce your spell choices."), NativeTheme.Muted));
        int selected = _draft.Selections.MysticAdeptPowerPoints;
        var decrease = NativeTheme.SecondaryButton(CreationFlowStrings.Get("Magic.Mystic.Decrease", "Remove one power point"));
        decrease.AutomationId = "creation-magic-resonance-mystic-decrease";
        decrease.IsEnabled = !laneLocked && editor.CanEdit && selected > 0;
        decrease.Clicked += async (_, _) => await RunAsync(() => ChangeMysticPowerPointsAsync(editor, selected - 1));
        card.Add(decrease);
        var increase = NativeTheme.PrimaryButton(CreationFlowStrings.Get("Magic.Mystic.Increase", "Add one power point"));
        increase.AutomationId = "creation-magic-resonance-mystic-increase";
        increase.IsEnabled = !laneLocked && editor.CanEdit && selected < purchase.MaximumPowerPoints;
        increase.Clicked += async (_, _) => await RunAsync(() => ChangeMysticPowerPointsAsync(editor, selected + 1));
        card.Add(increase);
        AddSources(card, purchase.Policy.SourceAnchorIds, _technicalDetails);
        _body.Add(NativeTheme.Card(card));
    }

    internal static string MysticPowerPointSummary(CharacterCreationMysticAdeptPowerPointAllocation purchase) =>
        CreationFlowStrings.Format("Magic.Mystic.Summary",
            "{0} / {1} power points · {2} Karma · {3} exchanged spell slots · {4} spell choices left",
            purchase.PowerPoints, purchase.MaximumPowerPoints, purchase.KarmaCost, purchase.ExchangedSpellSlots, purchase.SpellBudget);

    private async Task ChangeMysticPowerPointsAsync(CharacterCreationMagicResonanceEditorState editor, int powerPoints)
    {
        try
        {
            var candidate = _draft.CreateMysticPowerPointCandidate(powerPoints);
            var review = await PreviewDraftAsync(editor, candidate);
            _localBlockers = review.Preview.Blockers;
        }
        catch (InvalidOperationException exception) { _localBlockers = [exception.Message]; }
    }

    private async Task<CharacterCreationMagicResonanceReview> PreviewDraftAsync(
        CharacterCreationMagicResonanceEditorState editor, CharacterCreationMagicResonanceDesktopDraft candidate)
    {
        long generation = CaptureAppearanceGeneration();
        if (_loadedDisplay is not { } original || !ReferenceEquals(editor, _editor)
            || !Coordinator.IsCreationCatalogDisplayCurrent(original))
            throw new InvalidOperationException(CharacterCreationMagicResonanceBlockers.StaleWorkspaceRevision);
        var before = _draft.Copy();
        var prepared = before.Copy();
        var review = await Coordinator.ReviewCreationMagicResonanceForDisplayAsync(original, editor, candidate);
        bool valid = await Task.Run(() => prepared.TryAdopt(editor, original, review));
        if (!IsCurrentAppearanceGeneration(generation)) throw new OperationCanceledException();
        if (!valid || !Coordinator.IsCreationCatalogDisplayCurrent(original)
            || !_draft.TryAdoptPrepared(before, prepared))
            throw new InvalidOperationException(CharacterCreationMagicResonanceBlockers.DraftConflict);
        return review;
    }

    private void AddBinding(CharacterCreationMagicResonanceEditorState editor)
    {
        CharacterCreationMagicResonanceBinding binding = editor.Binding;
        Label revisions = NativeTheme.Body(
            CreationFlowStrings.Format(
                "Magic.Binding",
                "Revision {0} · prerequisite {1} · attributes {2}",
                binding.ContentRevision.ToString(CultureInfo.InvariantCulture),
                binding.PrerequisiteDraftRevision.ToString(CultureInfo.InvariantCulture),
                binding.AttributesDraftRevision.ToString(CultureInfo.InvariantCulture)),
            NativeTheme.Muted);
        revisions.AutomationId = "creation-magic-resonance-binding";
        _technicalDetails.Add(revisions);
        AddDigest("creation-magic-resonance-authority-digest", binding.AuthorityDigest);
        AddDigest("creation-magic-resonance-source-digest", binding.SourceInputsDigest);
        AddDigest("creation-magic-resonance-custom-data-digest", binding.CustomDataInputsDigest);
        AddDigest("creation-magic-resonance-gm-policy-digest", binding.GmPolicyDigest);
        AddDigest("creation-magic-resonance-runtime-digest", binding.RuntimeDigest);
    }

    private void AddTalent(CharacterCreationMagicResonanceTalentProjection talent)
    {
        VerticalStackLayout card = new() { Spacing = 6 };
        card.Add(NativeTheme.Eyebrow(CreationFlowStrings.Get("Magic.Talent.ReadOnly", "Talent · read only")));
        card.Add(NativeTheme.Title(talent.Name, 22));
        card.Add(NativeTheme.Metric(CreationFlowStrings.Get("Common.Kind", "Kind"), KindLabel(talent.Kind)));
        card.Add(NativeTheme.Metric(CreationFlowStrings.Get("Magic.Talent.PriorityRank", "Priority rank"), talent.Rank));
        _technicalDetails.Add(NativeTheme.Metric(CreationFlowStrings.Get("Magic.Talent.PrioritySourceId", "Priority source id"), talent.Identity.PrioritySourceId));
        _technicalDetails.Add(NativeTheme.Metric(CreationFlowStrings.Get("Magic.Talent.SelectionId", "Talent selection id"), talent.Identity.TalentSelectionId));
        card.Add(NativeTheme.Metric(CreationFlowStrings.Get("Magic.Kind.Magic", "Magic"), talent.Magic.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric(CreationFlowStrings.Get("Magic.Kind.Resonance", "Resonance"), talent.Resonance.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric(CreationFlowStrings.Get("Magic.Kind.Depth", "Depth"), talent.Depth.ToString(CultureInfo.InvariantCulture)));
        AddRequirement(card, CreationFlowStrings.Get("Magic.Talent.RequiredMetatypes", "Required metatypes"), talent.RequiredMetatypeNames);
        AddRequirement(card, CreationFlowStrings.Get("Magic.Talent.RequiredCategories", "Required metatype categories"), talent.RequiredMetatypeCategories);
        AddRequirement(card, CreationFlowStrings.Get("Magic.Talent.ForbiddenMetatypes", "Forbidden metatypes"), talent.ForbiddenMetatypeNames);
        AddSources(card, talent.SourceAnchorIds, _technicalDetails);
        foreach (string blocker in talent.Blockers)
            AddBlocker(card, blocker, _technicalDetails, _shownBlockerMessages);
        Border border = NativeTheme.Card(card);
        border.AutomationId = "creation-magic-resonance-talent";
        _body.Add(border);
    }

    private void AddBudgets(
        CharacterCreationMagicResonancePreview? preview,
        IReadOnlyList<CharacterCreationMagicResonanceBudgetState> projected)
    {
        _body.Add(NativeTheme.Eyebrow(CreationFlowStrings.Get("Magic.ExactBudgets", "Your available choices")));
        CharacterCreationMagicResonanceBudgetState[] budgets = preview is null
            ? projected.ToArray()
            :
            [
                preview.TraditionBudget,
                preview.StreamBudget,
                preview.AdeptPowerPointBudget,
                preview.SpellBudget,
                preview.ComplexFormBudget
            ];
        FlexLayout ribbon = new()
        {
            Direction = Microsoft.Maui.Layouts.FlexDirection.Row,
            Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap
        };
        foreach (CharacterCreationMagicResonanceBudgetState budget in budgets)
        {
            VerticalStackLayout card = new()
            {
                MinimumWidthRequest = 155,
                Spacing = 5
            };
            card.Add(NativeTheme.Eyebrow(KindLabel(budget.Kind)));
            card.Add(NativeTheme.Title(
                CreationFlowStrings.Format("Magic.Left", "{0} left", Decimal(budget.Remaining)),
                20));
            card.Add(NativeTheme.Body(
                CreationFlowStrings.Format(
                    "Magic.BudgetSummary",
                    "{0} / {1} {2}",
                    Decimal(budget.Used),
                    Decimal(budget.Total),
                    BudgetUnit(budget.Kind)),
                budget.Blockers.Count == 0 ? NativeTheme.Muted : NativeTheme.Danger));
            foreach (string blocker in budget.Blockers)
                AddBlocker(card, blocker, _technicalDetails, _shownBlockerMessages);
            Border border = NativeTheme.Card(card, new Thickness(12));
            border.Margin = new Thickness(0, 0, 8, 8);
            border.AutomationId = $"creation-magic-resonance-budget-{Token(budget.Kind)}";
            ribbon.Add(border);
        }
        _body.Add(ribbon);
    }

    private CharacterCreationMagicResonanceCheckpoint? AddRecovery(
        CharacterCreationMagicResonanceEditorState editor, CharacterOverviewState original)
    {
        if (!_store.TryRead(
                out CharacterCreationMagicResonanceCheckpoint checkpoint,
                out string blocker))
        {
            if (!string.IsNullOrWhiteSpace(blocker))
            {
                _technicalDetails.Add(NativeTheme.Body(blocker, NativeTheme.Muted));
                Label malformed = NativeTheme.Body(CreationFlowStrings.MagicBlocker(blocker), NativeTheme.Danger);
                malformed.AutomationId = "creation-magic-resonance-checkpoint-blocker";
                _body.Add(NativeTheme.Card(malformed));
            }
            return null;
        }

        VerticalStackLayout recovery = new() { Spacing = 8 };
        recovery.Add(NativeTheme.Eyebrow(CreationFlowStrings.Get("Common.DurableRecovery", "Durable review recovery")));
        recovery.Add(NativeTheme.Body(
            checkpoint.Phase switch
            {
                CharacterCreationMagicResonanceCheckpointPhase.Reviewed =>
                    CreationFlowStrings.Get(
                        "Magic.Recovery.Reviewed",
                        "A checked selection is ready to resume. Review it before saving."),
                CharacterCreationMagicResonanceCheckpointPhase.Confirming =>
                    CreationFlowStrings.Get(
                        "Magic.Recovery.Confirming",
                        "Saving was interrupted. Use recovery to check or finish the same save; do not start another."),
                CharacterCreationMagicResonanceCheckpointPhase.Confirmed =>
                    CreationFlowStrings.Get(
                        "Magic.Recovery.Confirmed",
                        "Your choices were saved. Open the result to finish this step."),
                _ => CreationFlowStrings.Get("Magic.Recovery.Locked", "The Magic/Resonance lane is locked.")
            },
            NativeTheme.Muted));

        if (checkpoint.Phase ==
                CharacterCreationMagicResonanceCheckpointPhase.Reviewed
            && checkpoint.OwnsExactReview(editor, original))
        {
            Button resume = NativeTheme.PrimaryButton(CreationFlowStrings.Get(
                "Common.ResumeReviewedDraft",
                "Resume reviewed draft"));
            resume.AutomationId = "creation-magic-resonance-resume-reviewed";
            resume.Clicked += async (_, _) => await RunAsync(
                () => ResumeReviewAsync(editor, checkpoint, original));
            recovery.Add(resume);
            Button abandon = NativeTheme.SecondaryButton(CreationFlowStrings.Get(
                "Common.AbandonReviewedDraft",
                "Abandon reviewed draft"));
            abandon.AutomationId = "creation-magic-resonance-abandon-reviewed";
            abandon.Clicked += async (_, _) => await RunAsync(
                () => AbandonReviewedAsync(checkpoint));
            recovery.Add(abandon);
        }
        else if (checkpoint.Phase ==
                     CharacterCreationMagicResonanceCheckpointPhase.Confirming
                 && checkpoint.OwnsRecoveryRevision(original))
        {
            Button resolve = NativeTheme.PrimaryButton(CreationFlowStrings.Get(
                "Common.ResolveInterruptedCommit",
                "Resolve interrupted commit"));
            resolve.AutomationId = "creation-magic-resonance-resolve-confirming";
            resolve.Clicked += async (_, _) => await RunAsync(
                () => ResolveConfirmingAsync(checkpoint, original));
            recovery.Add(resolve);
        }
        else if (checkpoint.Phase ==
                     CharacterCreationMagicResonanceCheckpointPhase.Confirmed
                 && checkpoint.OwnsRecoveryRevision(original)
                 && checkpoint.Confirmation is { } confirmation)
        {
            Button receipt = NativeTheme.PrimaryButton(CreationFlowStrings.Get(
                "Common.OpenSavedReceipt",
                "Open saved receipt"));
            receipt.AutomationId = "creation-magic-resonance-open-receipt";
            receipt.Clicked += async (_, _) => await Navigation.PushAsync(
                new CreationMagicResonanceReceiptPage(
                    Coordinator,
                    checkpoint,
                    confirmation,
                    _store));
            recovery.Add(receipt);
        }
        else
        {
            Label stale = NativeTheme.Body(
                CreationFlowStrings.Get(
                    "Magic.Recovery.Stale",
                    "This saved review no longer matches the runner. Reopen the same runner or use support recovery; do not save again."),
                NativeTheme.Danger);
            stale.AutomationId = "creation-magic-resonance-stale-checkpoint";
            recovery.Add(stale);
        }
        Border card = NativeTheme.Card(recovery);
        card.AutomationId = "creation-magic-resonance-recovery-card";
        _body.Add(card);
        return checkpoint;
    }

    private bool HasMalformedCheckpoint()
        => !_store.TryRead(out _, out string blocker)
           && !string.IsNullOrWhiteSpace(blocker);

    private void AddCatalogRoutes(
        CharacterCreationMagicResonanceEditorState editor,
        bool laneLocked)
    {
        _body.Add(NativeTheme.Eyebrow(CreationFlowStrings.Get("Magic.TypedChoices", "Typed choices")));
        AddCatalogRoute(
            editor,
            CharacterCreationMagicResonanceKinds.Tradition,
            KindLabel(CharacterCreationMagicResonanceKinds.Tradition),
            editor.Traditions,
            editor.Talent.RequiresTradition,
            laneLocked);
        AddCatalogRoute(
            editor,
            CharacterCreationMagicResonanceKinds.Stream,
            KindLabel(CharacterCreationMagicResonanceKinds.Stream),
            editor.Streams,
            editor.Talent.RequiresStream,
            laneLocked);
        AddCatalogRoute(
            editor,
            CharacterCreationMagicResonanceKinds.AdeptPower,
            CreationFlowStrings.Get("Magic.AdeptPowers", "Adept powers"),
            editor.AdeptPowers,
            editor.Talent.AllowsAdeptPowers,
            laneLocked);
        AddCatalogRoute(
            editor,
            CharacterCreationMagicResonanceKinds.Spell,
            KindLabel(CharacterCreationMagicResonanceKinds.Spell),
            editor.Spells,
            editor.Talent.AllowsSpells,
            laneLocked);
        AddCatalogRoute(
            editor,
            CharacterCreationMagicResonanceKinds.ComplexForm,
            KindLabel(CharacterCreationMagicResonanceKinds.ComplexForm),
            editor.ComplexForms,
            editor.Talent.AllowsComplexForms,
            laneLocked);
    }

    private void AddCatalogRoute(
        CharacterCreationMagicResonanceEditorState editor,
        string kind,
        string label,
        IReadOnlyList<CharacterCreationMagicResonanceOptionProjection> options,
        bool allowed,
        bool laneLocked)
    {
        CharacterCreationMagicResonanceBudgetState budget = CurrentBudget(editor, kind);
        int selected = kind switch
        {
            CharacterCreationMagicResonanceKinds.Tradition =>
                _draft.Selections.Tradition is null ? 0 : 1,
            CharacterCreationMagicResonanceKinds.Stream =>
                _draft.Selections.Stream is null ? 0 : 1,
            CharacterCreationMagicResonanceKinds.AdeptPower =>
                _draft.Selections.AdeptPowers.Count,
            CharacterCreationMagicResonanceKinds.Spell =>
                _draft.Selections.Spells.Count,
            CharacterCreationMagicResonanceKinds.ComplexForm =>
                _draft.Selections.ComplexForms.Count,
            _ => 0
        };
        string detail = allowed
            ? CreationFlowStrings.Format(
                "Magic.CatalogRouteDetail",
                "{0} selected · {1} {2} left · {3} Core options",
                selected.ToString(CultureInfo.InvariantCulture),
                Decimal(budget.Remaining),
                BudgetUnit(kind),
                options.Count.ToString(CultureInfo.InvariantCulture))
            : CreationFlowStrings.Get(
                "Magic.NotAllowed",
                "Not allowed by the selected Talent");
        _body.Add(NativeTheme.NavigationRow(
            label,
            detail,
            () => Navigation.PushAsync(new CreationMagicResonanceCatalogPage(
                Coordinator,
                editor,
                kind,
                options,
                _draft)),
            enabled: allowed && !laneLocked,
            automationId: $"creation-magic-resonance-catalog-{Token(kind)}"));
    }

    private CharacterCreationMagicResonanceBudgetState CurrentBudget(
        CharacterCreationMagicResonanceEditorState editor,
        string kind)
    {
        CharacterCreationMagicResonancePreview? preview = _draft.Review?.Preview;
        return preview is null
            ? editor.Budgets.Single(budget =>
                string.Equals(budget.Kind, kind, StringComparison.Ordinal))
            : kind switch
            {
                CharacterCreationMagicResonanceKinds.Tradition => preview.TraditionBudget,
                CharacterCreationMagicResonanceKinds.Stream => preview.StreamBudget,
                CharacterCreationMagicResonanceKinds.AdeptPower => preview.AdeptPowerPointBudget,
                CharacterCreationMagicResonanceKinds.Spell => preview.SpellBudget,
                CharacterCreationMagicResonanceKinds.ComplexForm => preview.ComplexFormBudget,
                _ => throw new InvalidOperationException(
                    CharacterCreationMagicResonanceBlockers.OptionInvalid)
            };
    }

    private void AddReview(
        CharacterCreationMagicResonanceEditorState editor,
        bool laneLocked)
    {
        CharacterCreationMagicResonanceReview? review = _draft.Review;
        Button open = NativeTheme.PrimaryButton(CreationFlowStrings.Get(
            "Magic.ReviewExactDraft",
            "Review exact draft"));
        open.AutomationId = "creation-magic-resonance-open-review";
        open.IsEnabled = !laneLocked && editor.CanEdit;
        open.Clicked += async (_, _) => await RunAsync(() => OpenReviewAsync(editor));
        _body.Add(open);
        if (review is { Preview.CanConfirm: false })
        {
            Label incomplete = NativeTheme.Body(
                CreationFlowStrings.Get(
                    "Magic.DraftIncomplete",
                    "Some choices still need attention. Check the remaining points and messages before saving."),
                NativeTheme.Danger);
            incomplete.AutomationId = "creation-magic-resonance-draft-incomplete";
            _body.Add(incomplete);
        }
        Label boundary = NativeTheme.Body(
            CreationFlowStrings.Get(
                "Magic.FinalizationBoundary",
                "Save your choices here. They take effect when you finish character creation."),
            NativeTheme.Muted);
        boundary.AutomationId = "creation-magic-resonance-finalization-boundary";
        _body.Add(boundary);
    }

    private async Task OpenReviewAsync(
        CharacterCreationMagicResonanceEditorState editor)
    {
        CharacterCreationMagicResonanceReview review;
        try
        {
            CharacterCreationMagicResonanceDesktopDraft draft =
                CreationMagicResonancePhoneAuthority.CreateDraft(
                    editor,
                    _draft.Selections);
            review = await PreviewDraftAsync(editor, draft);
        }
        catch (InvalidOperationException exception)
        {
            _localBlockers = [exception.Message];
            return;
        }
        if (!CreationMagicResonancePhoneAuthority.ReviewMatches(
                editor,
                review,
                requireConfirmable: true))
        {
            _localBlockers = review.Preview.Blockers.Count == 0
                ? [CharacterCreationMagicResonanceBlockers.DraftInvalid]
                : review.Preview.Blockers;
            return;
        }

        CharacterCreationMagicResonanceCheckpoint candidate =
            CharacterCreationMagicResonanceCheckpoint.CreateReviewed(review);
        if (!_store.TryCreate(
                candidate,
                out CharacterCreationMagicResonanceCheckpoint stored,
                out string blocker))
        {
            await DisplayAlertAsync(
                CreationFlowStrings.Get("Common.ReviewNotCheckpointed", "Review not checkpointed"),
                CreationFlowStrings.MagicBlocker(blocker),
                CreationFlowStrings.Get("Common.OK", "OK"));
            return;
        }
        await Navigation.PushAsync(new CreationMagicResonanceReviewPage(
            Coordinator,
            stored,
            _store));
    }

    private async Task ResumeReviewAsync(
        CharacterCreationMagicResonanceEditorState editor,
        CharacterCreationMagicResonanceCheckpoint checkpoint,
        CharacterOverviewState original)
    {
        try
        {
            long generation = CaptureAppearanceGeneration();
            CharacterCreationMagicResonanceReview refreshed =
                await Coordinator.ReviewCreationMagicResonanceForDisplayAsync(original, editor, checkpoint.Review.Draft);
            if (!IsCurrentAppearanceGeneration(generation)) throw new OperationCanceledException();
            if (!Coordinator.IsCreationCatalogDisplayCurrent(original))
                throw new InvalidOperationException(CharacterCreationMagicResonanceBlockers.StaleWorkspaceRevision);
            if (!checkpoint.OwnsExactReview(editor, original)
                || !CreationMagicResonancePhoneAuthority.ReviewsEqual(
                    checkpoint.Review,
                    refreshed))
            {
                throw new InvalidOperationException(
                    CharacterCreationMagicResonanceBlockers.PreviewDigestMismatch);
            }
            await Navigation.PushAsync(new CreationMagicResonanceReviewPage(
                Coordinator,
                checkpoint,
                _store));
        }
        catch (InvalidOperationException exception)
        {
            await DisplayAlertAsync(
                CreationFlowStrings.Get("Common.ReviewCannotResume", "Review cannot resume"),
                CreationFlowStrings.MagicBlocker(exception.Message),
                CreationFlowStrings.Get("Common.OK", "OK"));
        }
    }

    private async Task AbandonReviewedAsync(
        CharacterCreationMagicResonanceCheckpoint checkpoint)
    {
        bool confirmed = await DisplayAlertAsync(
            CreationFlowStrings.Get(
                "Magic.Abandon.Title",
                "Abandon reviewed Magic/Resonance draft?"),
            CreationFlowStrings.Get(
                "Magic.Abandon.Message",
                "This removes only the durable phone review. It does not change Core Creation state or the character document."),
            CreationFlowStrings.Get("Common.Abandon", "Abandon"),
            CreationFlowStrings.Get("Common.Keep", "Keep"));
        if (!confirmed)
            return;
        if (!_store.TryDeleteReviewed(
                CharacterCreationMagicResonanceCheckpointCas.From(checkpoint),
                out string blocker))
        {
            await DisplayAlertAsync(
                CreationFlowStrings.Get("Common.CheckpointNotRemoved", "Checkpoint not removed"),
                CreationFlowStrings.MagicBlocker(blocker),
                CreationFlowStrings.Get("Common.OK", "OK"));
        }
        Refresh();
    }

    private async Task ResolveConfirmingAsync(
        CharacterCreationMagicResonanceCheckpoint checkpoint,
        CharacterOverviewState original)
    {
        if (!Coordinator.IsCreationCatalogDisplayCurrent(original))
        {
            _localBlockers = [CharacterCreationMagicResonanceBlockers.StaleWorkspaceRevision];
            Refresh();
            return;
        }
        CreationMagicResonancePhoneConfirmResult result =
            await Coordinator.ConfirmCreationMagicResonanceAsync(checkpoint, display: original);
        if (result.MutationOutcomeKnown
            && string.Equals(
                result.Outcome,
                CreationMagicResonancePhoneOutcomes.Applied,
                StringComparison.Ordinal)
            && result.Confirmation is { } confirmation)
        {
            if (_store.TryRecordConfirmed(
                    CharacterCreationMagicResonanceCheckpointCas.From(checkpoint),
                    confirmation,
                    out CharacterCreationMagicResonanceCheckpoint confirmed,
                    out string recordBlocker))
            {
                await Navigation.PushAsync(new CreationMagicResonanceReceiptPage(
                    Coordinator,
                    confirmed,
                    confirmation,
                    _store));
                return;
            }
            await DisplayAlertAsync(
                CreationFlowStrings.Get("Common.ReceiptLocked", "Receipt remains locked"),
                CreationFlowStrings.MagicBlocker(recordBlocker),
                CreationFlowStrings.Get("Common.OK", "OK"));
        }
        else if (result.MutationOutcomeKnown
                 && string.Equals(
                     result.Outcome,
                     CreationMagicResonancePhoneOutcomes.RejectedBeforeMutation,
                     StringComparison.Ordinal))
        {
            if (_store.TryReturnToReviewed(
                    CharacterCreationMagicResonanceCheckpointCas.From(checkpoint),
                    out _,
                    out string returnBlocker))
            {
                await DisplayAlertAsync(
                    CreationFlowStrings.Get("Common.CommitNotSaved", "Commit was not saved"),
                    string.Join("\n", result.Blockers.Select(CreationFlowStrings.MagicBlocker)),
                    CreationFlowStrings.Get("Common.OK", "OK"));
            }
            else
            {
                await DisplayAlertAsync(
                    CreationFlowStrings.Get("Common.RecoveryLocked", "Recovery remains locked"),
                    CreationFlowStrings.MagicBlocker(returnBlocker),
                    CreationFlowStrings.Get("Common.OK", "OK"));
            }
        }
        else
        {
            await DisplayAlertAsync(
                CreationFlowStrings.Get("Common.CommitLocked", "Commit remains locked"),
                string.Join("\n", result.Blockers.Select(CreationFlowStrings.MagicBlocker)),
                CreationFlowStrings.Get("Common.OK", "OK"));
        }
        Refresh();
    }

    private void AddBlockers(IEnumerable<string> blockers)
    {
        string[] normalized = blockers
            .Where(static blocker => !string.IsNullOrWhiteSpace(blocker))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static blocker => blocker, StringComparer.Ordinal)
            .ToArray();
        if (normalized.Length == 0)
            return;
        VerticalStackLayout card = new() { Spacing = 6 };
        card.Add(NativeTheme.Eyebrow(CreationFlowStrings.Get("Magic.Blockers.Title", "Before you continue")));
        foreach (string blocker in normalized)
            AddBlocker(card, blocker, _technicalDetails, _shownBlockerMessages);
        if (card.Children.Count == 1) return; // Every hint already accompanies its budget/talent.
        Border border = NativeTheme.Card(card);
        border.AutomationId = "creation-magic-resonance-blockers";
        _body.Add(border);
    }

    private void AddDigest(string automationId, string digest)
    {
        Label label = NativeTheme.Body(digest, NativeTheme.Muted);
        label.AutomationId = automationId;
        label.LineBreakMode = LineBreakMode.CharacterWrap;
        _technicalDetails.Add(label);
    }

    internal static void AddBlocker(VerticalStackLayout layout, string code, VerticalStackLayout diagnostics,
        ISet<string>? shownMessages = null, bool catalogReason = false)
    {
        string message = catalogReason ? CreationFlowStrings.MagicCatalogBlocker(code) : CreationFlowStrings.MagicBlocker(code);
        if (shownMessages is null || shownMessages.Add(message))
            layout.Add(NativeTheme.Body($"• {message}", NativeTheme.Danger));
        // Different exact codes can share a readable explanation. Keep every
        // distinct code in diagnostics even when its visible hint is shared.
        if (!diagnostics.Children.OfType<Label>().Any(label => label.Text == code))
            diagnostics.Add(NativeTheme.Body(code, NativeTheme.Muted));
    }

    internal static void AddSources(
        VerticalStackLayout layout,
        IReadOnlyList<string> sourceAnchorIds,
        VerticalStackLayout? diagnostics = null)
    {
        if (sourceAnchorIds.Count == 0)
        {
            string code = CharacterCreationMagicResonanceBlockers.SourceDrift;
            layout.Add(NativeTheme.Body(CreationFlowStrings.MagicBlocker(code), NativeTheme.Danger));
            if (diagnostics is not null) diagnostics.Add(NativeTheme.Body(code, NativeTheme.Muted));
            else layout.Add(NativeTheme.TechnicalDetails(NativeTheme.Body(code, NativeTheme.Muted),
                (layout.AutomationId ?? "creation-magic-resonance") + "-source-details"));
            return;
        }
        Label sources = NativeTheme.Body(string.Join("\n", sourceAnchorIds), NativeTheme.Muted);
        if (diagnostics is not null) diagnostics.Add(sources);
        else layout.Add(NativeTheme.TechnicalDetails(sources,
            (layout.AutomationId ?? "creation-magic-resonance") + "-sources"));
    }

    private static void AddRequirement(
        VerticalStackLayout layout,
        string label,
        IReadOnlyList<string> values)
    {
        if (values.Count > 0)
            layout.Add(NativeTheme.Metric(label, string.Join(", ", values)));
    }

    internal static string KindLabel(string kind) => kind switch
    {
        CharacterCreationMagicResonanceKinds.Mundane => CreationFlowStrings.Get("Magic.Kind.Mundane", "Mundane"),
        CharacterCreationMagicResonanceKinds.Adept => CreationFlowStrings.Get("Magic.Kind.Adept", "Adept"),
        CharacterCreationMagicResonanceKinds.Magician => CreationFlowStrings.Get("Magic.Kind.Magician", "Magician"),
        CharacterCreationMagicResonanceKinds.MysticAdept => CreationFlowStrings.Get("Magic.Kind.MysticAdept", "Mystic adept"),
        CharacterCreationMagicResonanceKinds.AspectedMagician => CreationFlowStrings.Get("Magic.Kind.AspectedMagician", "Aspected magician"),
        CharacterCreationMagicResonanceKinds.Technomancer => CreationFlowStrings.Get("Magic.Kind.Technomancer", "Technomancer"),
        CharacterCreationMagicResonanceKinds.Tradition => CreationFlowStrings.Get("Magic.Kind.Tradition", "Tradition"),
        CharacterCreationMagicResonanceKinds.Stream => CreationFlowStrings.Get("Magic.Kind.Stream", "Stream"),
        CharacterCreationMagicResonanceKinds.AdeptPower => CreationFlowStrings.Get("Magic.Kind.AdeptPower", "Adept power points"),
        CharacterCreationMagicResonanceKinds.Spell => CreationFlowStrings.Get("Magic.Kind.Spells", "Spells"),
        CharacterCreationMagicResonanceKinds.ComplexForm => CreationFlowStrings.Get("Magic.Kind.ComplexForms", "Complex forms"),
        _ => CreationFlowStrings.Get("Common.Unsupported", "Unsupported")
    };

    internal static string BudgetUnit(string kind)
        => string.Equals(
            kind,
            CharacterCreationMagicResonanceKinds.AdeptPower,
            StringComparison.Ordinal)
            ? CreationFlowStrings.Get("Magic.Unit.PowerPoints", "power points")
            : CreationFlowStrings.Get("Magic.Unit.Choices", "choices");

    internal static string Decimal(decimal value)
        => value.ToString("0.##", CultureInfo.InvariantCulture);

    internal static string Token(string value)
        => new(value.Trim().ToLowerInvariant()
            .Select(static character => char.IsLetterOrDigit(character)
                ? character
                : '-')
            .ToArray());
}

/// <summary>Phone-deep list for one Core option kind.</summary>
public sealed class CreationMagicResonanceCatalogPage : NativePageBase
{
    private const int CatalogPageSize = 20;
    private int _catalogOffset;
    private string _filter = string.Empty;
    private long _renderGeneration;
    private long _catalogGeneration;
    private CharacterOverviewState? _display;
    private bool _ready;
    private readonly CharacterCreationMagicResonanceEditorState _editor;
    private readonly string _kind;
    private readonly IReadOnlyList<CharacterCreationMagicResonanceOptionProjection> _options;
    private readonly CreationMagicResonancePhoneDraft _draft;
    private readonly VerticalStackLayout _body = new()
    {
        Padding = new Thickness(20, 18, 20, 40),
        Spacing = 12
    };

    internal CreationMagicResonanceCatalogPage(
        RunnerSessionCoordinator coordinator,
        CharacterCreationMagicResonanceEditorState editor,
        string kind,
        IReadOnlyList<CharacterCreationMagicResonanceOptionProjection> options,
        CreationMagicResonancePhoneDraft draft) : base(coordinator)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _kind = kind ?? throw new ArgumentNullException(nameof(kind));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _draft = draft ?? throw new ArgumentNullException(nameof(draft));
        Title = CreationMagicResonancePage.KindLabel(kind);
        AutomationId = "creation-magic-resonance-catalog-page";
        Content = new ScrollView { Content = _body };
    }

    protected override async Task PrepareForAppearanceRefreshAsync(CancellationToken cancellationToken)
    {
        _ready = false;
        _display = null;
        var original = Coordinator.State;
        var draft = _draft.Copy();
        bool ready = await Task.Run(() => draft.Matches(_editor, original), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (!Coordinator.IsCreationCatalogDisplayCurrent(original)) return;
        _display = original;
        _ready = ready;
    }

    protected override void Refresh()
    {
        long render = ++_renderGeneration;
        long appearance = CaptureAppearanceGeneration();
        CharacterOverviewState? display = _display;
        _body.Clear();
        _body.Add(NativeTheme.Eyebrow(CreationFlowStrings.Get(
            "Magic.Catalog.Eyebrow",
            "SR5 · Draft · Catalog")));
        _body.Add(NativeTheme.Title(CreationMagicResonancePage.KindLabel(_kind)));
        if (!_ready || _display is null || !Coordinator.IsCreationCatalogDisplayCurrent(_display))
        {
            _body.Add(NativeTheme.Body(
                CreationFlowStrings.MagicBlocker(CharacterCreationMagicResonanceBlockers.StaleWorkspaceRevision),
                NativeTheme.Danger));
            return;
        }
        if (_options.Count == 0)
        {
            Label empty = NativeTheme.Body(
                CreationFlowStrings.Get(
                    "Magic.Catalog.Empty",
                    "Core projected no selectable identities for this category. No label-based or custom fallback is available."),
                NativeTheme.Danger);
            empty.AutomationId = "creation-magic-resonance-empty-catalog";
            _body.Add(NativeTheme.Card(empty));
            return;
        }
        bool IsCurrent() => render == _renderGeneration && IsCurrentAppearanceGeneration(appearance)
            && _ready && display is not null && ReferenceEquals(display, _display)
            && Coordinator.IsCreationCatalogDisplayCurrent(display);
        SearchBar search = new()
        {
            AutomationId = "creation-magic-resonance-catalog-search",
            Placeholder = CreationFlowStrings.Get("Magic.Catalog.Search", "Search names or source books"),
            Text = _filter,
            BackgroundColor = NativeTheme.Surface,
            TextColor = NativeTheme.Text,
            PlaceholderColor = NativeTheme.Muted
        };
        var rows = new VerticalStackLayout { Spacing = 12 };
        void ApplyFilter(string? text)
        {
            if (!IsCurrent()) return;
            _filter = text?.Trim() ?? string.Empty;
            _catalogOffset = 0;
            RenderCatalog(rows, IsCurrent);
        }
        search.SearchButtonPressed += (_, _) => ApplyFilter(search.Text);
        search.TextChanged += (_, args) =>
        {
            if (string.IsNullOrWhiteSpace(args.NewTextValue) && !string.IsNullOrWhiteSpace(_filter))
                ApplyFilter(string.Empty);
        };
        _body.Add(search);
        _body.Add(rows);
        RenderCatalog(rows, IsCurrent);
    }

    private void RenderCatalog(VerticalStackLayout rows, Func<bool> isCurrent)
    {
        if (!isCurrent()) return;
        long catalogGeneration = ++_catalogGeneration;
        bool CanUseRows() => catalogGeneration == _catalogGeneration && isCurrent();
        // Filter the retained, validated projection only. Browsing neither reads
        // Core again nor edits selections; option actions still preview through Core.
        var matches = _options.Where(option => (option.IsEnabled && option.Blockers.Count == 0 || _draft.IsSelected(option.Identity))
            && (string.IsNullOrEmpty(_filter)
            || option.Name.Contains(_filter, StringComparison.CurrentCultureIgnoreCase)
            || option.SourceBook.Contains(_filter, StringComparison.CurrentCultureIgnoreCase))).ToArray();
        _catalogOffset = Math.Min(_catalogOffset, Math.Max(0, matches.Length - 1) / CatalogPageSize * CatalogPageSize);
        int end = Math.Min(matches.Length, _catalogOffset + CatalogPageSize);
        rows.Clear();
        if (_options.Any(option => !option.IsEnabled || option.Blockers.Count > 0))
            rows.Add(NativeTheme.Body(CreationFlowStrings.Get("Magic.Catalog.AvailableOnly",
                "Showing available choices. Existing runners keep their saved source settings."), NativeTheme.Muted));
        Label range = NativeTheme.Body(matches.Length == 0
            ? CreationFlowStrings.Get("Magic.Catalog.NoMatches", "No matching choices")
            : CreationFlowStrings.Format("Magic.Catalog.Showing", "Showing {0}–{1} of {2}",
                _catalogOffset + 1, end, matches.Length), NativeTheme.Muted);
        range.AutomationId = "creation-magic-resonance-catalog-range";
        rows.Add(range);
        HorizontalStackLayout pager = new() { Spacing = 10 };
        Button previous = NativeTheme.SecondaryButton(CreationFlowStrings.Get("Magic.Catalog.Previous", "Previous"));
        previous.AutomationId = "creation-magic-resonance-catalog-previous";
        previous.IsEnabled = _catalogOffset > 0;
        previous.Clicked += (_, _) =>
        {
            if (!CanUseRows()) return;
            _catalogOffset = Math.Max(0, _catalogOffset - CatalogPageSize);
            RenderCatalog(rows, isCurrent);
        };
        pager.Add(previous);
        Button next = NativeTheme.SecondaryButton(CreationFlowStrings.Get("Magic.Catalog.Next", "Next"));
        next.AutomationId = "creation-magic-resonance-catalog-next";
        next.IsEnabled = end < matches.Length;
        next.Clicked += (_, _) =>
        {
            if (!CanUseRows() || end >= matches.Length) return;
            _catalogOffset += CatalogPageSize;
            RenderCatalog(rows, isCurrent);
        };
        pager.Add(next);
        rows.Add(pager);
        foreach (CharacterCreationMagicResonanceOptionProjection option in matches.Skip(_catalogOffset).Take(CatalogPageSize))
        {
            bool selected = _draft.IsSelected(option.Identity);
            string detail = CreationFlowStrings.Format(
                "Magic.Catalog.OptionDetail",
                "{0}{1} {2} · {3} {4}",
                selected ? CreationFlowStrings.Get("Common.SelectedPrefix", "Selected · ") : string.Empty,
                CreationMagicResonancePage.Decimal(option.PointCost),
                CreationMagicResonancePage.BudgetUnit(_kind),
                option.SourceBook,
                option.Page);
            if (!option.IsEnabled || option.Blockers.Count > 0)
                detail += $" · {CreationFlowStrings.MagicCatalogBlocker(option.Blockers.FirstOrDefault() ?? CharacterCreationMagicResonanceBlockers.OptionDisabled)}";
            if (_kind == CharacterCreationMagicResonanceKinds.Spell)
                detail += "\n" + CreationSpellInfo.Summary(CreationSpellInfo.Resolve(_display?.CreationMagicResonance, option));
            Border row = NativeTheme.NavigationRow(
                option.Name,
                detail,
                () => CanUseRows() ? Navigation.PushAsync(new CreationMagicResonanceOptionPage(
                    Coordinator,
                    _editor,
                    option,
                    _draft)) : Task.CompletedTask,
                automationId: $"creation-magic-resonance-option-{CreationMagicResonancePage.Token(option.Identity.Kind)}-{CreationMagicResonancePage.Token(option.Identity.SourceId)}");
            rows.Add(row);
        }
    }
}

/// <summary>Deep immutable details and one typed selection/level mutation.</summary>
public sealed class CreationMagicResonanceOptionPage : NativePageBase
{
    private CharacterOverviewState? _display;
    private bool _ready;
    private bool _previewPending;
    private long _previewGeneration;
    private Shell? _pendingShell;
    private readonly BackButtonBehavior _backBehavior = new();
    private readonly List<(Button Button, bool Enabled)> _selectionActions = [];
    private readonly Label _choiceProgress = NativeTheme.Body(CreationFlowStrings.Get(
        "Magic.Option.Checking", "Checking your choice… You can go back when this finishes."), NativeTheme.Text);
    private readonly CharacterCreationMagicResonanceEditorState _editor;
    private readonly CharacterCreationMagicResonanceOptionProjection _option;
    private readonly CreationMagicResonancePhoneDraft _draft;
    private readonly VerticalStackLayout _body = new()
    {
        Padding = new Thickness(20, 18, 20, 40),
        Spacing = 14
    };
    private IReadOnlyList<string> _blockers = [];

    internal CreationMagicResonanceOptionPage(
        RunnerSessionCoordinator coordinator,
        CharacterCreationMagicResonanceEditorState editor,
        CharacterCreationMagicResonanceOptionProjection option,
        CreationMagicResonancePhoneDraft draft) : base(coordinator)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _option = option ?? throw new ArgumentNullException(nameof(option));
        _draft = draft ?? throw new ArgumentNullException(nameof(draft));
        Title = CreationFlowStrings.Get("Magic.Option.PageTitle", "Configure choice");
        AutomationId = "creation-magic-resonance-option-page";
        _choiceProgress.AutomationId = "creation-magic-resonance-option-progress";
        _choiceProgress.IsVisible = false;
        Shell.SetBackButtonBehavior(this, _backBehavior);
        Content = new ScrollView { Content = _body };
    }

    private bool KeepPendingChoice => _previewPending
        && IsCurrentAppearanceGeneration(_previewGeneration)
        && _display is { } display && Coordinator.IsCreationCatalogDisplayCurrent(display);

    protected override bool OnBackButtonPressed()
        => KeepPendingChoice || base.OnBackButtonPressed();

    private void OnPendingNavigation(object? sender, ShellNavigatingEventArgs args)
    {
        if (sender is Shell shell && ReferenceEquals(shell.CurrentPage, this)
            && KeepPendingChoice && args.CanCancel
            && args.Source is ShellNavigationSource.Pop or ShellNavigationSource.PopToRoot)
            args.Cancel();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        ReleasePendingNavigation();
    }

    private void ReleasePendingNavigation()
    {
        if (_pendingShell is { } shell) shell.Navigating -= OnPendingNavigation;
        _pendingShell = null;
        _backBehavior.IsEnabled = true;
    }

    protected override async Task PrepareForAppearanceRefreshAsync(CancellationToken cancellationToken)
    {
        _ready = false;
        _display = null;
        var original = Coordinator.State;
        var draft = _draft.Copy();
        bool ready = await Task.Run(() => draft.Matches(_editor, original)
            && CreationMagicResonancePhoneAuthority.IsOptionConfigurable(_editor, _option), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (!Coordinator.IsCreationCatalogDisplayCurrent(original)) return;
        _display = original;
        _ready = ready;
    }

    protected override void Refresh()
    {
        if (_previewPending) return;
        _selectionActions.Clear();
        _body.Clear();
        _body.Add(NativeTheme.Eyebrow(CreationFlowStrings.Get(
            "Magic.Option.Eyebrow",
            "SR5 · Draft · Choice")));
        _body.Add(NativeTheme.Title(_option.Name));
        if (_option.Identity.Kind == CharacterCreationMagicResonanceKinds.Spell
            && _display is not null && Coordinator.IsCreationCatalogDisplayCurrent(_display))
        {
            Label summary = NativeTheme.Body(CreationSpellInfo.Summary(
                CreationSpellInfo.Resolve(_display.CreationMagicResonance, _option)), NativeTheme.Text);
            summary.AutomationId = "creation-magic-resonance-spell-summary";
            _body.Add(summary);
        }
        VerticalStackLayout details = new() { Spacing = 6 };
        VerticalStackLayout diagnostics = new() { Spacing = 6 };
        diagnostics.Add(NativeTheme.Metric(CreationFlowStrings.Get("Common.TypedKind", "Typed kind"), _option.Identity.Kind));
        diagnostics.Add(NativeTheme.Metric(CreationFlowStrings.Get("Common.SourceIdentity", "Source identity"), _option.Identity.SourceId));
        details.Add(NativeTheme.Metric(CreationFlowStrings.Get("Common.Category", "Category"), _option.Category));
        details.Add(NativeTheme.Metric(
            CreationFlowStrings.Get("Magic.Option.PointCost", "Point cost"),
            CreationMagicResonancePage.Decimal(_option.PointCost)));
        details.Add(NativeTheme.Metric(
            CreationFlowStrings.Get("Magic.Option.MaximumLevels", "Maximum levels"),
            _option.MaximumLevels.ToString(CultureInfo.InvariantCulture)));
        details.Add(NativeTheme.Metric(CreationFlowStrings.Get("Common.SourceBook", "Source book"), _option.SourceBook));
        details.Add(NativeTheme.Metric(CreationFlowStrings.Get("Common.Page", "Page"), _option.Page));
        if (!string.IsNullOrWhiteSpace(_option.DrainExpression))
            details.Add(NativeTheme.Metric(CreationFlowStrings.Get("Magic.Option.Drain", "Drain"), _option.DrainExpression));
        diagnostics.Add(NativeTheme.Metric(CreationFlowStrings.Get("Common.SourceNodeDigest", "Source node digest"), _option.SourceNodeDigest));
        CreationMagicResonancePage.AddSources(details, _option.SourceAnchorIds, diagnostics);
        Border card = NativeTheme.Card(details);
        card.AutomationId = "creation-magic-resonance-option-authority";
        _body.Add(card);
        _body.Add(_choiceProgress);

        bool exact = _ready && _display is not null && Coordinator.IsCreationCatalogDisplayCurrent(_display);
        if (string.Equals(
                _option.Identity.Kind,
                CharacterCreationMagicResonanceKinds.AdeptPower,
                StringComparison.Ordinal))
        {
            int levels = _draft.PowerLevels(_option.Identity);
            Label selected = NativeTheme.Body(
                CreationFlowStrings.Format(
                    "Magic.Option.SelectedLevels",
                    "Selected levels: {0}",
                    levels.ToString(CultureInfo.InvariantCulture)),
                NativeTheme.Muted);
            selected.AutomationId = "creation-magic-resonance-power-level";
            _body.Add(selected);
            Button decrease = NativeTheme.SecondaryButton(CreationFlowStrings.Get(
                "Magic.Option.Decrease",
                "Decrease level"));
            decrease.AutomationId = "creation-magic-resonance-power-decrease";
            decrease.IsEnabled = exact && levels > 0;
            _selectionActions.Add((decrease, decrease.IsEnabled));
            decrease.Clicked += async (_, _) => await RunAsync(
                () => ChangePowerLevelAsync(levels - 1));
            _body.Add(decrease);
            Button increase = NativeTheme.PrimaryButton(CreationFlowStrings.Get(
                "Magic.Option.Increase",
                "Increase level"));
            increase.AutomationId = "creation-magic-resonance-power-increase";
            increase.IsEnabled = exact && levels < _option.MaximumLevels;
            _selectionActions.Add((increase, increase.IsEnabled));
            increase.Clicked += async (_, _) => await RunAsync(
                () => ChangePowerLevelAsync(levels + 1));
            _body.Add(increase);
        }
        else
        {
            Button toggle = NativeTheme.PrimaryButton(
                _draft.IsSelected(_option.Identity)
                    ? CreationFlowStrings.Get("Common.RemoveFromDraft", "Remove from draft")
                    : CreationFlowStrings.Get("Magic.Option.Select", "Select for draft"));
            toggle.AutomationId = "creation-magic-resonance-option-toggle";
            toggle.IsEnabled = exact;
            _selectionActions.Add((toggle, toggle.IsEnabled));
            toggle.Clicked += async (_, _) => await RunAsync(ToggleAsync);
            _body.Add(toggle);
        }
        VerticalStackLayout notices = new() { Spacing = 6 };
        HashSet<string> shownMessages = new(StringComparer.Ordinal);
        foreach (string blocker in _option.Blockers.Concat(_blockers).Distinct(StringComparer.Ordinal))
            CreationMagicResonancePage.AddBlocker(notices, blocker, diagnostics, shownMessages,
                catalogReason: _option.Blockers.Contains(blocker));
        if (!exact && notices.Children.Count == 0)
            CreationMagicResonancePage.AddBlocker(notices,
                CharacterCreationMagicResonanceBlockers.StaleWorkspaceRevision, diagnostics, shownMessages);
        if (!exact && notices.Children.OfType<Label>().FirstOrDefault() is { } disabled)
            disabled.AutomationId = "creation-magic-resonance-option-disabled-reason";
        if (notices.Children.Count > 0)
        {
            _body.Add(notices);
        }
        _body.Add(NativeTheme.TechnicalDetails(diagnostics, "creation-magic-resonance-option-details"));
    }

    private async Task ToggleAsync()
    {
        try
        {
            CharacterCreationMagicResonanceDesktopDraft candidate =
                _option.Identity.Kind is CharacterCreationMagicResonanceKinds.Tradition
                    or CharacterCreationMagicResonanceKinds.Stream
                    ? _draft.CreateSingleCandidate(_option)
                    : _draft.CreateToggleCandidate(_option);
            await AdoptAsync(candidate);
        }
        catch (InvalidOperationException exception)
        {
            _blockers = [exception.Message];
        }
    }

    private async Task ChangePowerLevelAsync(int levels)
    {
        try
        {
            await AdoptAsync(_draft.CreatePowerLevelCandidate(_option, levels));
        }
        catch (InvalidOperationException exception)
        {
            _blockers = [exception.Message];
        }
    }

    private async Task AdoptAsync(CharacterCreationMagicResonanceDesktopDraft candidate)
    {
        if (_previewPending) return;
        long generation = CaptureAppearanceGeneration();
        if (!_ready || _display is not { } original
            || !Coordinator.IsCreationCatalogDisplayCurrent(original))
            throw new InvalidOperationException(CharacterCreationMagicResonanceBlockers.StaleWorkspaceRevision);
        _previewPending = true;
        _previewGeneration = generation;
        _choiceProgress.IsVisible = true;
        foreach (var action in _selectionActions) action.Button.IsEnabled = false;
        _backBehavior.IsEnabled = false;
        // Guard only this page's ordinary Back transition. Account switches and
        // forced departures still invalidate the generation and reject the result.
        for (Element? parent = Parent; parent is not null; parent = parent.Parent)
        {
            if (parent is not Shell shell) continue;
            _pendingShell = shell;
            shell.Navigating += OnPendingNavigation;
            break;
        }
        try
        {
            var before = _draft.Copy();
            var prepared = before.Copy();
            CharacterCreationMagicResonanceReview review =
                await Coordinator.ReviewCreationMagicResonanceForDisplayAsync(original, _editor, candidate);
            bool valid = await Task.Run(() => prepared.TryAdopt(_editor, original, review));
            if (!IsCurrentAppearanceGeneration(generation)) throw new OperationCanceledException();
            _blockers = review.Preview.Blockers;
            if (!valid || !Coordinator.IsCreationCatalogDisplayCurrent(original)
                || !_draft.TryAdoptPrepared(before, prepared))
                _blockers = _blockers.Append(
                        CharacterCreationMagicResonanceBlockers.DraftConflict)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
        }
        finally
        {
            _previewPending = false;
            _choiceProgress.IsVisible = false;
            ReleasePendingNavigation();
            bool current = IsCurrentAppearanceGeneration(generation)
                && Coordinator.IsCreationCatalogDisplayCurrent(original);
            foreach (var action in _selectionActions) action.Button.IsEnabled = current && action.Enabled;
            // A forced leave-and-return may prepare a new appearance while the
            // old preview drains. Release its render without adopting old work.
            long currentGeneration = CaptureAppearanceGeneration();
            if (currentGeneration != generation && IsCurrentAppearanceGeneration(currentGeneration)) Refresh();
        }
    }
}

/// <summary>Immutable typed Review followed by one durable explicit Confirm transition.</summary>
public sealed class CreationMagicResonanceReviewPage : NativePageBase
{
    private VerticalStackLayout _technicalDetails = new() { Spacing = 6 };
    private IReadOnlyList<CharacterCreationMagicResonanceOptionProjection> _reviewOptions = [];
    private readonly CharacterOverviewState _display;
    private CharacterCreationMagicResonanceCheckpoint _checkpoint;
    private readonly CharacterCreationMagicResonanceCheckpointStore _store;
    private readonly VerticalStackLayout _body = new()
    {
        Padding = new Thickness(20, 18, 20, 40),
        Spacing = 14
    };
    private IReadOnlyList<string> _blockers = [];
    private int _confirmStarted;
    private readonly Button _confirm;
    // Keep saving feedback readable without a continuous animation competing
    // with the fresh rule checks and post-save overview preparation.
    private readonly Label _confirmProgress = NativeTheme.Body(string.Empty);

    internal CreationMagicResonanceReviewPage(
        RunnerSessionCoordinator coordinator,
        CharacterCreationMagicResonanceCheckpoint checkpoint,
        CharacterCreationMagicResonanceCheckpointStore store) : base(coordinator)
    {
        _checkpoint = checkpoint ?? throw new ArgumentNullException(nameof(checkpoint));
        _display = coordinator.State;
        _store = store ?? throw new ArgumentNullException(nameof(store));
        if (!_checkpoint.IsStructurallyValid()
            || _checkpoint.Phase !=
            CharacterCreationMagicResonanceCheckpointPhase.Reviewed)
        {
            throw new InvalidOperationException(
                "The review page requires one exact Reviewed checkpoint.");
        }
        Title = CreationFlowStrings.Get("Magic.Review.PageTitle", "Review Magic / Resonance");
        AutomationId = "creation-magic-resonance-review-page";
        Content = new ScrollView { Content = _body };
        _confirmProgress.AutomationId = "creation-magic-resonance-confirm-progress";
        _confirmProgress.IsVisible = false;
        _confirm = NativeTheme.PrimaryButton(string.Empty);
        _confirm.AutomationId = "creation-magic-resonance-confirm-draft";
        _confirm.Clicked += async (_, _) => await RunAsync(ConfirmAsync);
    }

    protected override void Refresh()
    {
        UpdateConfirmationFeedback();
        _body.Clear();
        _technicalDetails = new() { Spacing = 6 };
        _reviewOptions = [];
        if (!Coordinator.IsCreationMagicOwnerCurrent(_display.DisplayOwnerContext))
        {
            _body.Add(NativeTheme.Body("Reopen Magic / Resonance for the current account.", NativeTheme.Muted));
            return;
        }
        CharacterCreationMagicResonanceReview review = _checkpoint.Review;
        CharacterCreationMagicResonancePreview preview = review.Preview;
        var editor = _display.CreationMagicResonanceEditor;
        if (editor is not null && _checkpoint.OwnsExactReview(editor, _display))
            _reviewOptions = editor.Traditions.Concat(editor.Streams).Concat(editor.AdeptPowers)
                .Concat(editor.Spells).Concat(editor.ComplexForms).ToArray();
        _body.Add(NativeTheme.Eyebrow(CreationFlowStrings.Get("Magic.Review.Eyebrow", "SR5 · Review")));
        _body.Add(NativeTheme.Title(CreationFlowStrings.Get(
            "Magic.Review.Heading",
            "Review exact typed draft")));
        _technicalDetails.Add(NativeTheme.Body(
            CreationFlowStrings.Format(
                "Magic.Review.Binding",
                "Revision {0} · {1}",
                preview.Binding.ContentRevision.ToString(CultureInfo.InvariantCulture),
                CreationMagicResonancePage.KindLabel(preview.Talent.Kind)),
            NativeTheme.Muted));
        AddDigest("creation-magic-resonance-review-preview-digest", preview.PreviewDigest);
        AddDigest("creation-magic-resonance-review-authority-digest", preview.Binding.AuthorityDigest);
        AddDigest("creation-magic-resonance-review-source-digest", preview.Binding.SourceInputsDigest);
        AddDigest("creation-magic-resonance-review-custom-data-digest", preview.Binding.CustomDataInputsDigest);
        AddDigest("creation-magic-resonance-review-gm-policy-digest", preview.Binding.GmPolicyDigest);
        AddDigest("creation-magic-resonance-review-runtime-digest", preview.Binding.RuntimeDigest);
        AddBudget(preview.TraditionBudget);
        AddBudget(preview.StreamBudget);
        AddBudget(preview.AdeptPowerPointBudget);
        AddBudget(preview.SpellBudget);
        AddBudget(preview.ComplexFormBudget);
        if (preview.MysticAdeptPowerPoints is { } purchase)
            _body.Add(NativeTheme.Body(CreationMagicResonancePage.MysticPowerPointSummary(purchase), NativeTheme.Muted));
        AddSelections(preview.Selections);
        VerticalStackLayout sources = new() { Spacing = 5 };
        CreationMagicResonancePage.AddSources(sources, preview.SourceAnchorIds, _technicalDetails);
        if (sources.Children.Count > 0) _body.Add(NativeTheme.Card(sources));
        foreach (string blocker in preview.Blockers.Concat(_blockers)
                     .Distinct(StringComparer.Ordinal))
            CreationMagicResonancePage.AddBlocker(_body, blocker, _technicalDetails);
        _body.Add(_confirmProgress);
        _body.Add(_confirm);
        Label boundary = NativeTheme.Body(
            CreationFlowStrings.Get(
                "Magic.Review.Boundary",
                "Confirm to save these choices. You can finish character creation after the remaining steps."),
            NativeTheme.Muted);
        boundary.AutomationId = "creation-magic-resonance-review-auxiliary-only";
        _body.Add(boundary);
        _body.Add(NativeTheme.TechnicalDetails(_technicalDetails, "creation-magic-resonance-review-details"));
    }

    private void UpdateConfirmationFeedback()
    {
        bool pending = Volatile.Read(ref _confirmStarted) != 0;
        CharacterCreationMagicResonancePreview preview = _checkpoint.Review.Preview;
        _confirm.Text = pending
            ? CreationFlowStrings.Get("Magic.Review.Saving", "Saving…")
            : CreationFlowStrings.Get("Magic.Review.Confirm", "Save choices");
        _confirm.IsEnabled = !pending
            && Coordinator.IsCreationMagicOwnerCurrent(_display.DisplayOwnerContext)
            && _checkpoint.Phase == CharacterCreationMagicResonanceCheckpointPhase.Reviewed
            && preview.RequiresExplicitConfirmation && preview.CanConfirm && preview.Blockers.Count == 0;
        _confirmProgress.Text = CreationFlowStrings.Get(
            "Magic.Review.Confirming", "Checking and saving choices…");
        _confirmProgress.IsVisible = pending;
    }

    private async Task ConfirmAsync()
    {
        if (Interlocked.CompareExchange(ref _confirmStarted, 1, 0) != 0)
            return;
        try
        {
            // RunAsync suppresses coordinator refreshes while this action owns the gate.
            // Update the existing controls before awaiting; do not rebuild a scrolled review.
            UpdateConfirmationFeedback();
            long generation = CaptureAppearanceGeneration();
            CharacterOverviewState original = _display;
            CharacterCreationFoundationResult<CharacterCreationMagicResonanceState> load =
                await Coordinator.LoadCreationMagicResonanceForDisplayAsync(original, CancellationToken.None);
            if (!IsCurrentAppearanceGeneration(generation)) throw new OperationCanceledException();
            if (!Coordinator.IsCreationCatalogDisplayCurrent(original))
                throw new InvalidOperationException(CharacterCreationMagicResonanceBlockers.StaleWorkspaceRevision);
            if (load.Value is not { } core
                || !CharacterCreationMagicResonanceWorkflow.TryProject(
                    core,
                    out CharacterCreationMagicResonanceEditorState? editor)
                || editor is null
                || !_checkpoint.OwnsExactReview(editor, Coordinator.State))
            {
                _blockers = load.Blockers.Append(
                        CharacterCreationMagicResonanceBlockers.StaleWorkspaceRevision)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                return;
            }
            CharacterCreationMagicResonanceReview refreshed =
                await Coordinator.ReviewCreationMagicResonanceForDisplayAsync(original, editor, _checkpoint.Review.Draft);
            if (!IsCurrentAppearanceGeneration(generation)) throw new OperationCanceledException();
            if (!Coordinator.IsCreationCatalogDisplayCurrent(original))
                throw new InvalidOperationException(CharacterCreationMagicResonanceBlockers.StaleWorkspaceRevision);
            if (!CreationMagicResonancePhoneAuthority.ReviewsEqual(
                    refreshed,
                    _checkpoint.Review))
            {
                _blockers = [CharacterCreationMagicResonanceBlockers.PreviewDigestMismatch];
                return;
            }
            if (!_store.TryBeginConfirm(
                    CharacterCreationMagicResonanceCheckpointCas.From(_checkpoint),
                    out CharacterCreationMagicResonanceCheckpoint confirming,
                    out string beginBlocker))
            {
                _blockers = [beginBlocker];
                return;
            }
            _checkpoint = confirming;
            CreationMagicResonancePhoneConfirmResult result =
                await Coordinator.ConfirmCreationMagicResonanceAsync(confirming, display: original);
            _blockers = result.Blockers;
            if (result.MutationOutcomeKnown
                && string.Equals(
                    result.Outcome,
                    CreationMagicResonancePhoneOutcomes.Applied,
                    StringComparison.Ordinal)
                && result.Confirmation is { } confirmation)
            {
                if (!_store.TryRecordConfirmed(
                        CharacterCreationMagicResonanceCheckpointCas.From(confirming),
                        confirmation,
                        out CharacterCreationMagicResonanceCheckpoint confirmed,
                        out string recordBlocker))
                {
                    _blockers =
                    [
                        recordBlocker,
                        CreationMagicResonancePhoneBlockers.OutcomeUnknown
                    ];
                    return;
                }
                _checkpoint = confirmed;
                await Navigation.PushAsync(new CreationMagicResonanceReceiptPage(
                    Coordinator,
                    confirmed,
                    confirmation,
                    _store));
                return;
            }
            if (result.MutationOutcomeKnown
                && string.Equals(
                    result.Outcome,
                    CreationMagicResonancePhoneOutcomes.RejectedBeforeMutation,
                    StringComparison.Ordinal)
                && _store.TryReturnToReviewed(
                    CharacterCreationMagicResonanceCheckpointCas.From(confirming),
                    out CharacterCreationMagicResonanceCheckpoint reviewed,
                    out string returnBlocker))
            {
                _checkpoint = reviewed;
                _blockers = result.Blockers;
                return;
            }
            _blockers = result.Blockers.Count == 0
                ? [CreationMagicResonancePhoneBlockers.OutcomeUnknown]
                : result.Blockers;
        }
        finally
        {
            Interlocked.Exchange(ref _confirmStarted, 0);
            Refresh();
        }
    }

    private void AddBudget(CharacterCreationMagicResonanceBudgetState budget)
    {
        VerticalStackLayout card = new() { Spacing = 5 };
        card.Add(NativeTheme.Eyebrow(CreationMagicResonancePage.KindLabel(budget.Kind)));
        card.Add(NativeTheme.Metric(CreationFlowStrings.Get("Common.Total", "Total"), CreationMagicResonancePage.Decimal(budget.Total)));
        card.Add(NativeTheme.Metric(CreationFlowStrings.Get("Common.Used", "Used"), CreationMagicResonancePage.Decimal(budget.Used)));
        card.Add(NativeTheme.Metric(CreationFlowStrings.Get("Common.Remaining", "Remaining"), CreationMagicResonancePage.Decimal(budget.Remaining)));
        _body.Add(NativeTheme.Card(card));
    }

    private void AddSelections(CharacterCreationMagicResonanceSelections selections)
    {
        _body.Add(NativeTheme.Eyebrow(CreationFlowStrings.Get("Magic.Review.TypedIdentities", "Typed identities")));
        if (selections.Tradition is { } tradition)
            AddIdentity(tradition, levels: null);
        if (selections.Stream is { } stream)
            AddIdentity(stream, levels: null);
        foreach (CharacterCreationAdeptPowerAllocation power in selections.AdeptPowers)
            AddIdentity(power.Identity, power.Levels);
        foreach (CharacterCreationMagicResonanceOptionIdentity spell in selections.Spells)
            AddIdentity(spell, levels: null);
        foreach (CharacterCreationMagicResonanceOptionIdentity form in selections.ComplexForms)
            AddIdentity(form, levels: null);
        if (selections.Tradition is null
            && selections.Stream is null
            && selections.AdeptPowers.Count == 0
            && selections.Spells.Count == 0
            && selections.ComplexForms.Count == 0)
        {
            _body.Add(NativeTheme.Body(
                CreationFlowStrings.Get(
                    "Magic.Review.NoIdentities",
                    "No further choices are needed for this talent."),
                NativeTheme.Muted));
        }
    }

    private void AddIdentity(
        CharacterCreationMagicResonanceOptionIdentity identity,
        int? levels)
    {
        VerticalStackLayout card = new() { Spacing = 5 };
        // Names belong to the exact reviewed catalog, never a newer ambient
        // catalog or a name-based lookup used to reconstruct a mutation.
        var matches = _reviewOptions.Where(option => option.Identity == identity).ToArray();
        string name = matches.Length == 1 ? matches[0].Name
            : CreationFlowStrings.Get("Magic.Review.UnavailableChoice", "Reopen Magic / Resonance to view this choice.");
        card.Add(NativeTheme.Title(name, 18));
        _technicalDetails.Add(NativeTheme.Metric(CreationFlowStrings.Get("Common.SourceIdentity", "Source identity"), identity.SourceId));
        if (levels is not null)
            card.Add(NativeTheme.Metric(CreationFlowStrings.Get("Magic.Option.Levels", "Levels"), levels.Value.ToString(CultureInfo.InvariantCulture)));
        _body.Add(NativeTheme.Card(card));
    }

    private void AddDigest(string automationId, string digest)
    {
        Label label = NativeTheme.Body(digest, NativeTheme.Muted);
        label.AutomationId = automationId;
        label.LineBreakMode = LineBreakMode.CharacterWrap;
        _technicalDetails.Add(label);
    }
}

/// <summary>Confirmed Core receipt; acknowledgement removes only the phone recovery journal.</summary>
public sealed class CreationMagicResonanceReceiptPage : NativePageBase
{
    private readonly Chummer.Application.Owners.OwnerContextStamp? _originalOwner;
    private readonly CharacterCreationMagicResonanceCheckpoint _checkpoint;
    private readonly CharacterCreationMagicResonanceConfirmation _confirmation;
    private readonly CharacterCreationMagicResonanceCheckpointStore _store;
    private readonly VerticalStackLayout _body = new()
    {
        Padding = new Thickness(20, 18, 20, 40),
        Spacing = 14
    };

    internal CreationMagicResonanceReceiptPage(
        RunnerSessionCoordinator coordinator,
        CharacterCreationMagicResonanceCheckpoint checkpoint,
        CharacterCreationMagicResonanceConfirmation confirmation,
        CharacterCreationMagicResonanceCheckpointStore store) : base(coordinator)
    {
        _originalOwner = coordinator?.State.DisplayOwnerContext;
        _checkpoint = checkpoint ?? throw new ArgumentNullException(nameof(checkpoint));
        _confirmation = confirmation ?? throw new ArgumentNullException(nameof(confirmation));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        if (!_checkpoint.IsStructurallyValid()
            || _checkpoint.Phase !=
            CharacterCreationMagicResonanceCheckpointPhase.Confirmed
            || _checkpoint.Confirmation is null
            // The store returns a deserialized confirmation. Its nested option
            // collections have new references, so record equality is not a
            // durable identity check. Compare the complete canonical content.
            || !CharacterCreationMagicResonanceDigest.EqualsFixedTime(
                CharacterCreationMagicResonanceDigest.Compute(_checkpoint.Confirmation),
                CharacterCreationMagicResonanceDigest.Compute(_confirmation)))
        {
            throw new InvalidOperationException(
                "The receipt page requires one exact durable Confirmed checkpoint.");
        }
        Title = CreationFlowStrings.Get("Magic.Receipt.PageTitle", "Magic / Resonance receipt");
        AutomationId = "creation-magic-resonance-receipt-page";
        Content = new ScrollView { Content = _body };
    }

    protected override void Refresh()
    {
        _body.Clear();
        if (!CanDisplayReceipt())
        {
            _body.Add(NativeTheme.Body(CreationFlowStrings.Get("Magic.Recovery.Stale",
                "Reopen the same runner to check this saved selection."), NativeTheme.Muted));
            return;
        }
        CharacterCreationMagicResonanceReceipt receipt = _confirmation.Receipt;
        _body.Add(NativeTheme.Eyebrow(CreationFlowStrings.Get("Magic.Receipt.Eyebrow", "SR5 · Confirm")));
        _body.Add(NativeTheme.Title(CreationFlowStrings.Get("Common.DraftSaved", "Creation draft saved")));
        VerticalStackLayout card = new() { Spacing = 6 };
        VerticalStackLayout diagnostics = new() { Spacing = 6 };
        diagnostics.Add(NativeTheme.Metric(
            CreationFlowStrings.Get("Common.PreviousRevision", "Previous revision"),
            receipt.PreviousContentRevision.ToString(CultureInfo.InvariantCulture)));
        diagnostics.Add(NativeTheme.Metric(
            CreationFlowStrings.Get("Common.ContentRevision", "Content revision"),
            receipt.ContentRevision.ToString(CultureInfo.InvariantCulture)));
        diagnostics.Add(NativeTheme.Metric(
            CreationFlowStrings.Get("Common.SavedRevision", "Saved revision"),
            receipt.SavedRevision.ToString(CultureInfo.InvariantCulture)));
        diagnostics.Add(NativeTheme.Metric(
            CreationFlowStrings.Get("Common.DraftRevision", "Draft revision"),
            receipt.DraftRevision.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric(CreationFlowStrings.Get("Magic.Receipt.TalentKind", "Talent kind"),
            CreationMagicResonancePage.KindLabel(receipt.TalentKind)));
        card.Add(NativeTheme.Metric(
            CreationFlowStrings.Get("Magic.Receipt.PowerRemaining", "Power points remaining"),
            CreationMagicResonancePage.Decimal(receipt.AdeptPowerPointsRemaining)));
        card.Add(NativeTheme.Metric(
            CreationFlowStrings.Get("Magic.Receipt.SpellsRemaining", "Spells remaining"),
            receipt.SpellsRemaining.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric(
            CreationFlowStrings.Get("Magic.Receipt.FormsRemaining", "Complex forms remaining"),
            receipt.ComplexFormsRemaining.ToString(CultureInfo.InvariantCulture)));
        diagnostics.Add(NativeTheme.Metric(
            CreationFlowStrings.Get("Magic.Receipt.IdempotentReplay", "Idempotent replay"),
            _confirmation.IsIdempotentReplay.ToString().ToLowerInvariant()));
        diagnostics.Add(NativeTheme.Metric(
            CreationFlowStrings.Get("Magic.Receipt.CurrentDraft", "Current draft"),
            _confirmation.IsCurrentDraft.ToString().ToLowerInvariant()));
        diagnostics.Add(NativeTheme.Metric(
            CreationFlowStrings.Get("Common.DocumentChanged", "Character document changed"),
            receipt.CharacterDocumentChanged.ToString().ToLowerInvariant()));
        AddDigest(diagnostics, "creation-magic-resonance-receipt-digest", receipt.ReceiptDigest);
        AddDigest(diagnostics, "creation-magic-resonance-receipt-draft-digest", receipt.DraftDigest);
        AddDigest(diagnostics, "creation-magic-resonance-receipt-command-digest", receipt.CommandDigest);
        AddDigest(diagnostics, "creation-magic-resonance-receipt-preview-digest", receipt.PreviewDigest);
        AddDigest(diagnostics, "creation-magic-resonance-receipt-authority-digest", receipt.AuthorityDigest);
        Border receiptCard = NativeTheme.Card(card);
        receiptCard.AutomationId = "creation-magic-resonance-confirm-receipt";
        _body.Add(receiptCard);
        Label boundary = NativeTheme.Body(
            !receipt.CharacterDocumentChanged
                ? CreationFlowStrings.Get(
                    "Magic.Receipt.Safe",
                    "Your choices are saved. They take effect when you finish character creation.")
                : CreationFlowStrings.Get(
                    "Magic.Receipt.Unsafe",
                    "Unsafe receipt: the character document changed before finalization."),
            !receipt.CharacterDocumentChanged ? NativeTheme.Muted : NativeTheme.Danger);
        boundary.AutomationId = "creation-magic-resonance-receipt-finalization-state";
        _body.Add(boundary);
        Button acknowledge = NativeTheme.PrimaryButton(CreationFlowStrings.Get(
            "Common.AcknowledgeReceipt",
            "Acknowledge receipt"));
        acknowledge.AutomationId = "creation-magic-resonance-receipt-acknowledge";
        acknowledge.IsEnabled = !receipt.CharacterDocumentChanged
                                && _checkpoint.OwnsRecoveryRevision(Coordinator.State);
        acknowledge.Clicked += async (_, _) => await RunAsync(AcknowledgeAsync);
        _body.Add(acknowledge);
        _body.Add(NativeTheme.TechnicalDetails(diagnostics, "creation-magic-resonance-receipt-details"));
    }

    private async Task AcknowledgeAsync()
    {
        // A retained button must not acknowledge another account's or runner's
        // journal, including A → B → A transitions with a newer owner stamp.
        if (!CanDisplayReceipt() || !_checkpoint.OwnsRecoveryRevision(Coordinator.State)) return;
        if (!_store.TryAcknowledgeConfirmed(
                CharacterCreationMagicResonanceCheckpointCas.From(_checkpoint),
                out string blocker))
        {
            await DisplayAlertAsync(
                CreationFlowStrings.Get("Common.ReceiptNotAcknowledged", "Receipt not acknowledged"),
                CreationFlowStrings.MagicBlocker(blocker),
                CreationFlowStrings.Get("Common.OK", "OK"));
            return;
        }
        // Keep navigation attached while returning to the runner. Popping this
        // receipt first detaches its proxy and can strand the user in Magic.
        if (Shell.Current is not MainShell { UsesTabletComposition: false } shell)
            throw new InvalidOperationException("Creation returns through the phone Runner route.");
        await shell.GoToAsync(PhoneShellRoutes.RunnerAbsolute, animate: false);
    }

    private bool CanDisplayReceipt() => Coordinator.IsCreationMagicOwnerCurrent(_originalOwner)
        && Coordinator.State.WorkspaceId == _confirmation.Receipt.WorkspaceId;

    private static void AddDigest(
        VerticalStackLayout card,
        string automationId,
        string digest)
    {
        Label label = NativeTheme.Body(digest, NativeTheme.Muted);
        label.AutomationId = automationId;
        label.LineBreakMode = LineBreakMode.CharacterWrap;
        card.Add(label);
    }
}
