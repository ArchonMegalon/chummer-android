using System.Globalization;
using Chummer.Contracts.Characters;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

/// <summary>
/// Native phone Draft step for SR5 Standard Priority Magic/Resonance. Every displayed cost,
/// budget, prerequisite, blocker and source comes from the current Core/Presentation projection.
/// </summary>
public sealed class CreationMagicResonancePage : NativePageBase
{
    private readonly CreationMagicResonancePhoneDraft _draft = new();
    private readonly CharacterCreationMagicResonanceCheckpointStore _store;
    private readonly VerticalStackLayout _body = new()
    {
        Padding = new Thickness(20, 18, 20, 40),
        Spacing = 14
    };
    private IReadOnlyList<string> _localBlockers = [];

    public CreationMagicResonancePage(RunnerSessionCoordinator coordinator)
        : this(
            coordinator,
            CharacterCreationMagicResonanceCheckpointStore.CreateDefault())
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

    protected override void Refresh()
    {
        _body.Clear();
        _body.Add(NativeTheme.Eyebrow(CreationFlowStrings.Get("Magic.DraftEyebrow", "SR5 Priority · Draft")));
        _body.Add(NativeTheme.Title(CreationFlowStrings.Get("Magic.Heading", "Magic and Resonance")));
        _body.Add(NativeTheme.Body(
            CreationFlowStrings.Get(
                "Magic.Intro",
                "The Talent is owned by the Priority prerequisite. This phone step selects only Core-issued typed identities; unsupported custom semantics and artificial-intelligence Talent stay fail-closed."),
            NativeTheme.Muted));

        CharacterCreationFoundationResult<CharacterCreationMagicResonanceState> load =
            Coordinator.LoadCreationMagicResonance();
        if (load.Value is not { } core
            || !CharacterCreationMagicResonanceWorkflow.TryProject(
                core,
                out CharacterCreationMagicResonanceEditorState? editor)
            || editor is null
            || !CreationMagicResonancePhoneAuthority.IsReady(
                core,
                editor,
                Coordinator.State))
        {
            AddBlockers(load.Blockers.Count == 0
                ? [CharacterCreationMagicResonanceBlockers.AuthorityUnavailable]
                : load.Blockers);
            return;
        }

        _draft.Bind(editor, Coordinator.State);
        if (!_draft.Matches(editor, Coordinator.State))
        {
            AddBlockers(
                [CharacterCreationMagicResonanceBlockers.StaleWorkspaceRevision]);
            return;
        }

        AddBinding(editor);
        AddTalent(editor.Talent);
        CharacterCreationMagicResonanceReview? review = _draft.Review;
        AddBudgets(review?.Preview, editor.Budgets);
        CharacterCreationMagicResonanceCheckpoint? checkpoint = AddRecovery(editor);
        bool laneLocked = checkpoint is not null || HasMalformedCheckpoint();
        AddCatalogRoutes(editor, laneLocked);
        AddReview(editor, laneLocked);
        AddBlockers(editor.Blockers
            .Concat(review?.Preview.Blockers ?? [])
            .Concat(_localBlockers));
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
        _body.Add(revisions);
        AddDigest("creation-magic-resonance-authority-digest", binding.AuthorityDigest);
        AddDigest("creation-magic-resonance-source-digest", binding.SourceInputsDigest);
        AddDigest("creation-magic-resonance-custom-data-digest", binding.CustomDataInputsDigest);
        AddDigest("creation-magic-resonance-gm-policy-digest", binding.GmPolicyDigest);
        AddDigest("creation-magic-resonance-runtime-digest", binding.RuntimeDigest);
    }

    private void AddTalent(CharacterCreationMagicResonanceTalentProjection talent)
    {
        VerticalStackLayout card = new() { Spacing = 6 };
        card.Add(NativeTheme.Eyebrow(CreationFlowStrings.Get("Magic.Talent.ReadOnly", "Priority Talent · read only")));
        card.Add(NativeTheme.Title(talent.Name, 22));
        card.Add(NativeTheme.Metric(CreationFlowStrings.Get("Common.Kind", "Kind"), KindLabel(talent.Kind)));
        card.Add(NativeTheme.Metric(CreationFlowStrings.Get("Magic.Talent.PriorityRank", "Priority rank"), talent.Rank));
        card.Add(NativeTheme.Metric(CreationFlowStrings.Get("Magic.Talent.PrioritySourceId", "Priority source id"), talent.Identity.PrioritySourceId));
        card.Add(NativeTheme.Metric(CreationFlowStrings.Get("Magic.Talent.SelectionId", "Talent selection id"), talent.Identity.TalentSelectionId));
        card.Add(NativeTheme.Metric(CreationFlowStrings.Get("Magic.Kind.Magic", "Magic"), talent.Magic.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric(CreationFlowStrings.Get("Magic.Kind.Resonance", "Resonance"), talent.Resonance.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric(CreationFlowStrings.Get("Magic.Kind.Depth", "Depth"), talent.Depth.ToString(CultureInfo.InvariantCulture)));
        AddRequirement(card, CreationFlowStrings.Get("Magic.Talent.RequiredMetatypes", "Required metatypes"), talent.RequiredMetatypeNames);
        AddRequirement(card, CreationFlowStrings.Get("Magic.Talent.RequiredCategories", "Required metatype categories"), talent.RequiredMetatypeCategories);
        AddRequirement(card, CreationFlowStrings.Get("Magic.Talent.ForbiddenMetatypes", "Forbidden metatypes"), talent.ForbiddenMetatypeNames);
        AddSources(card, talent.SourceAnchorIds);
        foreach (string blocker in talent.Blockers)
            card.Add(NativeTheme.Body($"• {blocker}", NativeTheme.Danger));
        Border border = NativeTheme.Card(card);
        border.AutomationId = "creation-magic-resonance-talent";
        _body.Add(border);
    }

    private void AddBudgets(
        CharacterCreationMagicResonancePreview? preview,
        IReadOnlyList<CharacterCreationMagicResonanceBudgetState> projected)
    {
        _body.Add(NativeTheme.Eyebrow(CreationFlowStrings.Get("Magic.ExactBudgets", "Exact Core budgets")));
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
                card.Add(NativeTheme.Body($"• {blocker}", NativeTheme.Danger));
            Border border = NativeTheme.Card(card, new Thickness(12));
            border.Margin = new Thickness(0, 0, 8, 8);
            border.AutomationId = $"creation-magic-resonance-budget-{Token(budget.Kind)}";
            ribbon.Add(border);
        }
        _body.Add(ribbon);
    }

    private CharacterCreationMagicResonanceCheckpoint? AddRecovery(
        CharacterCreationMagicResonanceEditorState editor)
    {
        if (!_store.TryRead(
                out CharacterCreationMagicResonanceCheckpoint checkpoint,
                out string blocker))
        {
            if (!string.IsNullOrWhiteSpace(blocker))
            {
                Label malformed = NativeTheme.Body(blocker, NativeTheme.Danger);
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
                        "A typed blocker-free Core review can be resumed without reconstructing rules data."),
                CharacterCreationMagicResonanceCheckpointPhase.Confirming =>
                    CreationFlowStrings.Get(
                        "Magic.Recovery.Confirming",
                        "An interrupted Core commit is locked to its exact idempotent command and can only be replayed."),
                CharacterCreationMagicResonanceCheckpointPhase.Confirmed =>
                    CreationFlowStrings.Get(
                        "Magic.Recovery.Confirmed",
                        "A digest-verified Core receipt is waiting for acknowledgement."),
                _ => CreationFlowStrings.Get("Magic.Recovery.Locked", "The Magic/Resonance lane is locked.")
            },
            NativeTheme.Muted));

        if (checkpoint.Phase ==
                CharacterCreationMagicResonanceCheckpointPhase.Reviewed
            && checkpoint.OwnsExactReview(editor, Coordinator.State))
        {
            Button resume = NativeTheme.PrimaryButton(CreationFlowStrings.Get(
                "Common.ResumeReviewedDraft",
                "Resume reviewed draft"));
            resume.AutomationId = "creation-magic-resonance-resume-reviewed";
            resume.Clicked += async (_, _) => await RunAsync(
                () => ResumeReviewAsync(editor, checkpoint));
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
                 && checkpoint.OwnsRecoveryRevision(Coordinator.State))
        {
            Button resolve = NativeTheme.PrimaryButton(CreationFlowStrings.Get(
                "Common.ResolveInterruptedCommit",
                "Resolve interrupted commit"));
            resolve.AutomationId = "creation-magic-resonance-resolve-confirming";
            resolve.Clicked += async (_, _) => await RunAsync(
                () => ResolveConfirmingAsync(checkpoint));
            recovery.Add(resolve);
        }
        else if (checkpoint.Phase ==
                     CharacterCreationMagicResonanceCheckpointPhase.Confirmed
                 && checkpoint.OwnsRecoveryRevision(Coordinator.State)
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
                    "This checkpoint belongs to another revision or authority digest. The lane stays fail-closed; reopen the exact runner or use support recovery."),
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
                "Not allowed by the exact Priority Talent");
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
                    "Core has previewed this draft, but exact remaining budgets or blockers prevent confirmation."),
                NativeTheme.Danger);
            incomplete.AutomationId = "creation-magic-resonance-draft-incomplete";
            _body.Add(incomplete);
        }
        Label boundary = NativeTheme.Body(
            CreationFlowStrings.Get(
                "Magic.FinalizationBoundary",
                "Confirm commits only Core auxiliary Creation state. CharacterDocumentChanged must stay false until whole-build finalization."),
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
            review = Coordinator.ReviewCreationMagicResonance(editor, draft);
        }
        catch (InvalidOperationException exception)
        {
            _localBlockers = [exception.Message];
            Refresh();
            return;
        }
        if (!_draft.TryAdopt(editor, Coordinator.State, review)
            || !CreationMagicResonancePhoneAuthority.ReviewMatches(
                editor,
                review,
                requireConfirmable: true))
        {
            _localBlockers = review.Preview.Blockers.Count == 0
                ? [CharacterCreationMagicResonanceBlockers.DraftInvalid]
                : review.Preview.Blockers;
            Refresh();
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
                blocker,
                CreationFlowStrings.Get("Common.OK", "OK"));
            Refresh();
            return;
        }
        await Navigation.PushAsync(new CreationMagicResonanceReviewPage(
            Coordinator,
            stored,
            _store));
    }

    private async Task ResumeReviewAsync(
        CharacterCreationMagicResonanceEditorState editor,
        CharacterCreationMagicResonanceCheckpoint checkpoint)
    {
        try
        {
            CharacterCreationMagicResonanceReview refreshed =
                Coordinator.ReviewCreationMagicResonance(
                    editor,
                    checkpoint.Review.Draft);
            if (!checkpoint.OwnsExactReview(editor, Coordinator.State)
                || !CreationMagicResonancePhoneAuthority.ReviewsEqual(
                    checkpoint.Review,
                    refreshed))
            {
                throw new InvalidOperationException(
                    CreationFlowStrings.Get(
                        "Magic.ReviewChanged",
                        "The Core preview, source/custom/GM authority, or workspace revision changed."));
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
                exception.Message,
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
                blocker,
                CreationFlowStrings.Get("Common.OK", "OK"));
        }
        Refresh();
    }

    private async Task ResolveConfirmingAsync(
        CharacterCreationMagicResonanceCheckpoint checkpoint)
    {
        CreationMagicResonancePhoneConfirmResult result =
            await Coordinator.ConfirmCreationMagicResonanceAsync(checkpoint);
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
                recordBlocker,
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
                    string.Join("\n", result.Blockers),
                    CreationFlowStrings.Get("Common.OK", "OK"));
            }
            else
            {
                await DisplayAlertAsync(
                    CreationFlowStrings.Get("Common.RecoveryLocked", "Recovery remains locked"),
                    returnBlocker,
                    CreationFlowStrings.Get("Common.OK", "OK"));
            }
        }
        else
        {
            await DisplayAlertAsync(
                CreationFlowStrings.Get("Common.CommitLocked", "Commit remains locked"),
                string.Join("\n", result.Blockers),
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
        card.Add(NativeTheme.Eyebrow(CreationFlowStrings.Get("Common.CoreBlockers", "Core blockers")));
        foreach (string blocker in normalized)
            card.Add(NativeTheme.Body($"• {blocker}", NativeTheme.Danger));
        Border border = NativeTheme.Card(card);
        border.AutomationId = "creation-magic-resonance-blockers";
        _body.Add(border);
    }

    private void AddDigest(string automationId, string digest)
    {
        Label label = NativeTheme.Body(digest, NativeTheme.Muted);
        label.AutomationId = automationId;
        label.LineBreakMode = LineBreakMode.CharacterWrap;
        _body.Add(label);
    }

    internal static void AddSources(
        VerticalStackLayout layout,
        IReadOnlyList<string> sourceAnchorIds)
    {
        layout.Add(NativeTheme.Eyebrow(CreationFlowStrings.Get("Common.SourceAnchors", "Source anchors")));
        layout.Add(NativeTheme.Body(
            sourceAnchorIds.Count == 0
                ? CharacterCreationMagicResonanceBlockers.SourceDrift
                : string.Join("\n", sourceAnchorIds),
            sourceAnchorIds.Count == 0 ? NativeTheme.Danger : NativeTheme.Muted));
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

    protected override void Refresh()
    {
        _body.Clear();
        _body.Add(NativeTheme.Eyebrow(CreationFlowStrings.Get(
            "Magic.Catalog.Eyebrow",
            "SR5 Priority · Draft · Catalog")));
        _body.Add(NativeTheme.Title(CreationMagicResonancePage.KindLabel(_kind)));
        if (!_draft.Matches(_editor, Coordinator.State))
        {
            _body.Add(NativeTheme.Body(
                CharacterCreationMagicResonanceBlockers.StaleWorkspaceRevision,
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
        foreach (CharacterCreationMagicResonanceOptionProjection option in _options)
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
                detail += $" · {option.Blockers.FirstOrDefault() ?? CharacterCreationMagicResonanceBlockers.OptionDisabled}";
            Border row = NativeTheme.NavigationRow(
                option.Name,
                detail,
                () => Navigation.PushAsync(new CreationMagicResonanceOptionPage(
                    Coordinator,
                    _editor,
                    option,
                    _draft)),
                automationId: $"creation-magic-resonance-option-{CreationMagicResonancePage.Token(option.Identity.Kind)}-{CreationMagicResonancePage.Token(option.Identity.SourceId)}");
            _body.Add(row);
        }
    }
}

/// <summary>Deep immutable details and one typed selection/level mutation.</summary>
public sealed class CreationMagicResonanceOptionPage : NativePageBase
{
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
        Content = new ScrollView { Content = _body };
    }

    protected override void Refresh()
    {
        _body.Clear();
        _body.Add(NativeTheme.Eyebrow(CreationFlowStrings.Get(
            "Magic.Option.Eyebrow",
            "SR5 Priority · Draft · Choice")));
        _body.Add(NativeTheme.Title(_option.Name));
        VerticalStackLayout details = new() { Spacing = 6 };
        details.Add(NativeTheme.Metric(CreationFlowStrings.Get("Common.TypedKind", "Typed kind"), _option.Identity.Kind));
        details.Add(NativeTheme.Metric(CreationFlowStrings.Get("Common.SourceIdentity", "Source identity"), _option.Identity.SourceId));
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
        details.Add(NativeTheme.Metric(CreationFlowStrings.Get("Common.SourceNodeDigest", "Source node digest"), _option.SourceNodeDigest));
        CreationMagicResonancePage.AddSources(details, _option.SourceAnchorIds);
        foreach (string blocker in _option.Blockers.Concat(_blockers)
                     .Distinct(StringComparer.Ordinal))
            details.Add(NativeTheme.Body($"• {blocker}", NativeTheme.Danger));
        Border card = NativeTheme.Card(details);
        card.AutomationId = "creation-magic-resonance-option-authority";
        _body.Add(card);

        bool exact = _draft.Matches(_editor, Coordinator.State)
                     && CreationMagicResonancePhoneAuthority.IsOptionConfigurable(
                         _editor,
                         _option);
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
            decrease.Clicked += async (_, _) => await RunAsync(
                () => ChangePowerLevelAsync(levels - 1));
            _body.Add(decrease);
            Button increase = NativeTheme.PrimaryButton(CreationFlowStrings.Get(
                "Magic.Option.Increase",
                "Increase level"));
            increase.AutomationId = "creation-magic-resonance-power-increase";
            increase.IsEnabled = exact && levels < _option.MaximumLevels;
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
            toggle.Clicked += async (_, _) => await RunAsync(ToggleAsync);
            _body.Add(toggle);
        }
        if (!exact)
        {
            Label disabled = NativeTheme.Body(
                _option.Blockers.FirstOrDefault()
                ?? CharacterCreationMagicResonanceBlockers.OptionDisabled,
                NativeTheme.Danger);
            disabled.AutomationId = "creation-magic-resonance-option-disabled-reason";
            _body.Add(disabled);
        }
    }

    private Task ToggleAsync()
    {
        try
        {
            CharacterCreationMagicResonanceDesktopDraft candidate =
                _option.Identity.Kind is CharacterCreationMagicResonanceKinds.Tradition
                    or CharacterCreationMagicResonanceKinds.Stream
                    ? _draft.CreateSingleCandidate(_option)
                    : _draft.CreateToggleCandidate(_option);
            Adopt(candidate);
        }
        catch (InvalidOperationException exception)
        {
            _blockers = [exception.Message];
        }
        Refresh();
        return Task.CompletedTask;
    }

    private Task ChangePowerLevelAsync(int levels)
    {
        try
        {
            Adopt(_draft.CreatePowerLevelCandidate(_option, levels));
        }
        catch (InvalidOperationException exception)
        {
            _blockers = [exception.Message];
        }
        Refresh();
        return Task.CompletedTask;
    }

    private void Adopt(CharacterCreationMagicResonanceDesktopDraft candidate)
    {
        CharacterCreationMagicResonanceReview review =
            Coordinator.ReviewCreationMagicResonance(_editor, candidate);
        _blockers = review.Preview.Blockers;
        if (!_draft.TryAdopt(_editor, Coordinator.State, review))
            _blockers = _blockers.Append(
                    CharacterCreationMagicResonanceBlockers.DraftConflict)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
    }
}

/// <summary>Immutable typed Review followed by one durable explicit Confirm transition.</summary>
public sealed class CreationMagicResonanceReviewPage : NativePageBase
{
    private CharacterCreationMagicResonanceCheckpoint _checkpoint;
    private readonly CharacterCreationMagicResonanceCheckpointStore _store;
    private readonly VerticalStackLayout _body = new()
    {
        Padding = new Thickness(20, 18, 20, 40),
        Spacing = 14
    };
    private IReadOnlyList<string> _blockers = [];
    private int _confirmStarted;

    internal CreationMagicResonanceReviewPage(
        RunnerSessionCoordinator coordinator,
        CharacterCreationMagicResonanceCheckpoint checkpoint,
        CharacterCreationMagicResonanceCheckpointStore store) : base(coordinator)
    {
        _checkpoint = checkpoint ?? throw new ArgumentNullException(nameof(checkpoint));
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
    }

    protected override void Refresh()
    {
        _body.Clear();
        CharacterCreationMagicResonanceReview review = _checkpoint.Review;
        CharacterCreationMagicResonancePreview preview = review.Preview;
        _body.Add(NativeTheme.Eyebrow(CreationFlowStrings.Get("Magic.Review.Eyebrow", "SR5 Priority · Review")));
        _body.Add(NativeTheme.Title(CreationFlowStrings.Get(
            "Magic.Review.Heading",
            "Review exact typed draft")));
        _body.Add(NativeTheme.Body(
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
        AddSelections(preview.Selections);
        VerticalStackLayout sources = new() { Spacing = 5 };
        CreationMagicResonancePage.AddSources(sources, preview.SourceAnchorIds);
        _body.Add(NativeTheme.Card(sources));
        foreach (string blocker in preview.Blockers.Concat(_blockers)
                     .Distinct(StringComparer.Ordinal))
            _body.Add(NativeTheme.Body($"• {blocker}", NativeTheme.Danger));
        Button confirm = NativeTheme.PrimaryButton(CreationFlowStrings.Get(
            "Magic.Review.Confirm",
            "Confirm Magic/Resonance Creation draft"));
        confirm.AutomationId = "creation-magic-resonance-confirm-draft";
        confirm.IsEnabled = _checkpoint.Phase ==
                                CharacterCreationMagicResonanceCheckpointPhase.Reviewed
                            && preview.RequiresExplicitConfirmation
                            && preview.CanConfirm
                            && preview.Blockers.Count == 0
                            && Volatile.Read(ref _confirmStarted) == 0;
        confirm.Clicked += async (_, _) => await RunAsync(ConfirmAsync);
        _body.Add(confirm);
        Label boundary = NativeTheme.Body(
            CreationFlowStrings.Get(
                "Magic.Review.Boundary",
                "Core atomically commits only the auxiliary creation ledger. No XML or character effect is mutated here."),
            NativeTheme.Muted);
        boundary.AutomationId = "creation-magic-resonance-review-auxiliary-only";
        _body.Add(boundary);
    }

    private async Task ConfirmAsync()
    {
        if (Interlocked.CompareExchange(ref _confirmStarted, 1, 0) != 0)
            return;
        try
        {
            CharacterCreationFoundationResult<CharacterCreationMagicResonanceState> load =
                Coordinator.LoadCreationMagicResonance();
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
                Coordinator.ReviewCreationMagicResonance(
                    editor,
                    _checkpoint.Review.Draft);
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
                await Coordinator.ConfirmCreationMagicResonanceAsync(confirming);
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
                    "No follow-up identities are required by this exact Talent."),
                NativeTheme.Muted));
        }
    }

    private void AddIdentity(
        CharacterCreationMagicResonanceOptionIdentity identity,
        int? levels)
    {
        VerticalStackLayout card = new() { Spacing = 5 };
        card.Add(NativeTheme.Metric(CreationFlowStrings.Get("Common.Kind", "Kind"), identity.Kind));
        card.Add(NativeTheme.Metric(CreationFlowStrings.Get("Common.SourceIdentity", "Source identity"), identity.SourceId));
        if (levels is not null)
            card.Add(NativeTheme.Metric(CreationFlowStrings.Get("Magic.Option.Levels", "Levels"), levels.Value.ToString(CultureInfo.InvariantCulture)));
        _body.Add(NativeTheme.Card(card));
    }

    private void AddDigest(string automationId, string digest)
    {
        Label label = NativeTheme.Body(digest, NativeTheme.Muted);
        label.AutomationId = automationId;
        label.LineBreakMode = LineBreakMode.CharacterWrap;
        _body.Add(label);
    }
}

/// <summary>Confirmed Core receipt; acknowledgement removes only the phone recovery journal.</summary>
public sealed class CreationMagicResonanceReceiptPage : NativePageBase
{
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
        _checkpoint = checkpoint ?? throw new ArgumentNullException(nameof(checkpoint));
        _confirmation = confirmation ?? throw new ArgumentNullException(nameof(confirmation));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        if (!_checkpoint.IsStructurallyValid()
            || _checkpoint.Phase !=
            CharacterCreationMagicResonanceCheckpointPhase.Confirmed
            || _checkpoint.Confirmation != _confirmation)
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
        CharacterCreationMagicResonanceReceipt receipt = _confirmation.Receipt;
        _body.Add(NativeTheme.Eyebrow(CreationFlowStrings.Get("Magic.Receipt.Eyebrow", "SR5 Priority · Confirm")));
        _body.Add(NativeTheme.Title(CreationFlowStrings.Get("Common.DraftSaved", "Creation draft saved")));
        VerticalStackLayout card = new() { Spacing = 6 };
        card.Add(NativeTheme.Metric(
            CreationFlowStrings.Get("Common.PreviousRevision", "Previous revision"),
            receipt.PreviousContentRevision.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric(
            CreationFlowStrings.Get("Common.ContentRevision", "Content revision"),
            receipt.ContentRevision.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric(
            CreationFlowStrings.Get("Common.SavedRevision", "Saved revision"),
            receipt.SavedRevision.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric(
            CreationFlowStrings.Get("Common.DraftRevision", "Draft revision"),
            receipt.DraftRevision.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric(CreationFlowStrings.Get("Magic.Receipt.TalentKind", "Talent kind"), receipt.TalentKind));
        card.Add(NativeTheme.Metric(
            CreationFlowStrings.Get("Magic.Receipt.PowerRemaining", "Power points remaining"),
            CreationMagicResonancePage.Decimal(receipt.AdeptPowerPointsRemaining)));
        card.Add(NativeTheme.Metric(
            CreationFlowStrings.Get("Magic.Receipt.SpellsRemaining", "Spells remaining"),
            receipt.SpellsRemaining.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric(
            CreationFlowStrings.Get("Magic.Receipt.FormsRemaining", "Complex forms remaining"),
            receipt.ComplexFormsRemaining.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric(
            CreationFlowStrings.Get("Magic.Receipt.IdempotentReplay", "Idempotent replay"),
            _confirmation.IsIdempotentReplay.ToString().ToLowerInvariant()));
        card.Add(NativeTheme.Metric(
            CreationFlowStrings.Get("Magic.Receipt.CurrentDraft", "Current draft"),
            _confirmation.IsCurrentDraft.ToString().ToLowerInvariant()));
        card.Add(NativeTheme.Metric(
            CreationFlowStrings.Get("Common.DocumentChanged", "Character document changed"),
            receipt.CharacterDocumentChanged.ToString().ToLowerInvariant()));
        AddDigest(card, "creation-magic-resonance-receipt-digest", receipt.ReceiptDigest);
        AddDigest(card, "creation-magic-resonance-receipt-draft-digest", receipt.DraftDigest);
        AddDigest(card, "creation-magic-resonance-receipt-command-digest", receipt.CommandDigest);
        AddDigest(card, "creation-magic-resonance-receipt-preview-digest", receipt.PreviewDigest);
        AddDigest(card, "creation-magic-resonance-receipt-authority-digest", receipt.AuthorityDigest);
        Border receiptCard = NativeTheme.Card(card);
        receiptCard.AutomationId = "creation-magic-resonance-confirm-receipt";
        _body.Add(receiptCard);
        Label boundary = NativeTheme.Body(
            !receipt.CharacterDocumentChanged
                ? CreationFlowStrings.Get(
                    "Magic.Receipt.Safe",
                    "Typed choices are durable in Core auxiliary state. Character effects remain pending whole-build finalization.")
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
    }

    private async Task AcknowledgeAsync()
    {
        if (!_store.TryAcknowledgeConfirmed(
                CharacterCreationMagicResonanceCheckpointCas.From(_checkpoint),
                out string blocker))
        {
            await DisplayAlertAsync(
                CreationFlowStrings.Get("Common.ReceiptNotAcknowledged", "Receipt not acknowledged"),
                blocker,
                CreationFlowStrings.Get("Common.OK", "OK"));
            return;
        }
        await Navigation.PopAsync(animated: false);
        while (Navigation.NavigationStack.LastOrDefault() is
               (CreationMagicResonanceReviewPage
                   or CreationMagicResonanceCatalogPage
                   or CreationMagicResonanceOptionPage
                   or CreationMagicResonancePage))
        {
            await Navigation.PopAsync(animated: false);
        }
    }

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
