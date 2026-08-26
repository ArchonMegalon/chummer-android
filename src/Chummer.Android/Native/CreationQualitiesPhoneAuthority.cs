using Chummer.Contracts.Characters;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

/// <summary>
/// Trust boundary for Core's SR5 Standard Priority quality projection. Android validates
/// identity, revision and digest shape, but never derives eligibility, price, limits or Karma.
/// </summary>
internal static class CreationQualitiesPhoneAuthority
{
    public static bool MatchesOverview(
        CharacterCreationQualitiesState state,
        CharacterOverviewState overview)
        => overview.Profile?.Created == false
           && overview.WorkspaceId == state.Binding.WorkspaceId
           && overview.ContentRevision == state.Binding.ContentRevision
           && overview.SavedRevision == state.Binding.SavedRevision
           && state.Binding.ContentRevision > 0
           && state.Binding.SavedRevision == state.Binding.ContentRevision
           && Canonical(state.Binding.RawCharacterXmlDigest)
           && CanonicalAuxiliary(state.Binding.AuxiliaryStateDigest)
           && Canonical(state.Binding.PrerequisiteDraftDigest)
           && Canonical(state.Binding.AttributesDraftDigest)
           && Canonical(state.Binding.AuthorityDigest)
           && Canonical(state.Binding.RuntimeDigest);

    public static bool IsReady(
        CharacterCreationQualitiesState state,
        CharacterOverviewState overview)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(overview);
        if (!MatchesOverview(state, overview)
            || !string.Equals(state.Schema, CharacterCreationQualitiesSchemas.StateV1, StringComparison.Ordinal)
            || !state.CanEdit
            || state.Blockers.Count != 0
            || !Canonical(state.SnapshotDigest)
            || !CharacterCreationQualitiesRules.DigestsEqual(
                state.SnapshotDigest,
                CharacterCreationQualitiesRules.ComputeStateDigest(state))
            || state.Binding.CharacterCreated
            || !string.Equals(state.Binding.RulesetId, "sr5", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(
                state.Binding.BuildMethod,
                CharacterCreationBuildMethods.Priority,
                StringComparison.Ordinal)
            || state.Binding.PrerequisiteDraftRevision <= 0
            || state.Binding.AttributesDraftRevision <= 0
            || state.PrerequisiteDraft is not { } prerequisite
            || state.AttributesDraft is not { } attributes
            || prerequisite.DraftRevision != state.Binding.PrerequisiteDraftRevision
            || attributes.DraftRevision != state.Binding.AttributesDraftRevision
            || !Equal(prerequisite.DraftDigest, state.Binding.PrerequisiteDraftDigest)
            || !Equal(attributes.DraftDigest, state.Binding.AttributesDraftDigest)
            || !AuthorityIsExact(state.Authority)
            || !Equal(state.Authority.AuthorityDigest, state.Binding.AuthorityDigest)
            || !Equal(state.Authority.RuntimeDigest, state.Binding.RuntimeDigest)
            || !PreviewMatchesState(state))
        {
            return false;
        }

        return state.PendingDraft is null
            ? state.Preview.Selections.Count == 0
            : PendingDraftMatches(state.PendingDraft, state);
    }

    public static CharacterCreationQualitiesEditorState ProjectEditor(
        CharacterCreationQualitiesState state,
        CharacterOverviewState overview)
    {
        if (!IsReady(state, overview))
            throw new InvalidOperationException(
                "The SR5 Priority quality editor requires one exact Core state projection.");
        CharacterCreationQualitiesAuthoritySnapshot snapshot = new(
            new CharacterCreationQualitiesInput(
                state.Binding,
                state.Authority,
                state.PendingDraft?.SelectedOptionIds ?? []),
            PersistedReceipts: [],
            ReservedTransactionIds: []);
        CharacterCreationQualitiesEditorState editor = CharacterCreationQualitiesWorkflow.Project(snapshot);
        if (editor.WorkspaceId != state.Binding.WorkspaceId
            || editor.ContentRevision != state.Binding.ContentRevision
            || editor.SavedRevision != state.Binding.SavedRevision
            || !Equal(editor.AuthorityDigest, state.Binding.AuthorityDigest)
            || !Equal(editor.RuntimeDigest, state.Binding.RuntimeDigest)
            || !Equal(editor.Preview.PreviewDigest, state.Preview.PreviewDigest))
        {
            throw new InvalidOperationException(
                "Presentation did not preserve the exact Core quality projection.");
        }
        return editor;
    }

    public static bool BindingEquals(
        CharacterCreationQualitiesBinding left,
        CharacterCreationQualitiesBinding right)
        => left.WorkspaceId == right.WorkspaceId
           && left.ContentRevision == right.ContentRevision
           && left.SavedRevision == right.SavedRevision
           && left.PrerequisiteDraftRevision == right.PrerequisiteDraftRevision
           && left.AttributesDraftRevision == right.AttributesDraftRevision
           && left.CharacterCreated == right.CharacterCreated
           && left.CreationKarmaTotal == right.CreationKarmaTotal
           && left.CreationKarmaUsedBeforeQualities == right.CreationKarmaUsedBeforeQualities
           && string.Equals(left.RulesetId, right.RulesetId, StringComparison.OrdinalIgnoreCase)
           && string.Equals(left.BuildMethod, right.BuildMethod, StringComparison.Ordinal)
           && Equal(left.RawCharacterXmlDigest, right.RawCharacterXmlDigest)
           && Equal(left.AuxiliaryStateDigest, right.AuxiliaryStateDigest)
           && Equal(left.PrerequisiteDraftDigest, right.PrerequisiteDraftDigest)
           && Equal(left.AttributesDraftDigest, right.AttributesDraftDigest)
           && Equal(left.AuthorityDigest, right.AuthorityDigest)
           && Equal(left.RuntimeDigest, right.RuntimeDigest);

    /// <summary>
    /// A blocked preview may be displayed so the user can see Core's exact budget failure.
    /// It can never be promoted to Review/Apply.
    /// </summary>
    public static bool CanDisplayPreview(
        CharacterCreationQualitiesState state,
        CharacterOverviewState overview,
        CharacterCreationFoundationResult<CharacterCreationQualitiesPreview> result,
        IReadOnlyList<string> selectedOptionIds)
    {
        if (!IsReady(state, overview)
            || result.Value is not { } preview
            || !BindingEquals(state.Binding, preview.Binding)
            || !SelectedIdsAreProjected(state, selectedOptionIds)
            || !PreviewSelectionsMatch(preview, selectedOptionIds)
            || !Canonical(preview.PreviewDigest)
            || !CanonicalPreviewMatches(state, preview, selectedOptionIds)
            || !result.Blockers.ToHashSet(StringComparer.Ordinal)
                .SetEquals(preview.Blockers))
        {
            return false;
        }

        return string.Equals(result.Outcome, CharacterCreationFoundationOutcomes.Success, StringComparison.Ordinal)
               || string.Equals(result.Outcome, CharacterCreationFoundationOutcomes.Blocked, StringComparison.Ordinal);
    }

    public static bool CanConfirmPreview(
        CharacterCreationQualitiesState state,
        CharacterOverviewState overview,
        CharacterCreationQualitiesPreview preview,
        IReadOnlyList<string> selectedOptionIds)
        => IsReady(state, overview)
           && BindingEquals(state.Binding, preview.Binding)
           && SelectedIdsAreProjected(state, selectedOptionIds)
           && PreviewSelectionsMatch(preview, selectedOptionIds)
           && preview.RequiresExplicitConfirmation
           && preview.CanConfirm
           && preview.Blockers.Count == 0
           && Canonical(preview.PreviewDigest)
           && CanonicalPreviewMatches(state, preview, selectedOptionIds);

    public static bool CanonicallyEquals(
        CharacterCreationQualitiesPreview left,
        CharacterCreationQualitiesPreview right)
        => Equal(left.PreviewDigest, right.PreviewDigest)
           && BindingEquals(left.Binding, right.Binding);

    public static string ComputeIdempotencyKey(
        CharacterCreationQualitiesPreview preview,
        IReadOnlyList<string> selectedOptionIds)
        => CharacterCreationQualitiesRules.ComputeCommandDigest(
            preview.Binding,
            selectedOptionIds.OrderBy(static item => item, StringComparer.Ordinal).ToArray(),
            preview.PreviewDigest);

    public static bool TryCreatePlan(
        CharacterCreationQualitiesPreview preview,
        IReadOnlyList<string> selectedOptionIds,
        string idempotencyKey,
        Guid transactionId,
        out CharacterCreationQualitiesDraftPlan plan)
    {
        plan = null!;
        return PreviewSelectionsMatch(preview, selectedOptionIds)
               && string.Equals(
                   idempotencyKey,
                   ComputeIdempotencyKey(preview, selectedOptionIds),
                   StringComparison.Ordinal)
               && CharacterCreationQualitiesRules.TryPlan(
                   preview,
                   preview.PreviewDigest,
                   idempotencyKey,
                   explicitlyConfirmed: true,
                   transactionIdAlreadyExists: false,
                   transactionId,
                   out plan);
    }

    public static bool ReceiptMatchesBeforeActivation(
        CharacterCreationQualitiesCheckpoint checkpoint,
        CharacterCreationQualitiesDraftReceipt receipt,
        CharacterCreationQualitiesState committed,
        CharacterOverviewState beforeActivation)
        => MatchesCheckpointBeforeActivation(checkpoint, beforeActivation)
           && ReceiptMatchesState(checkpoint, receipt, committed);

    public static bool ReceiptMatches(
        CharacterCreationQualitiesCheckpoint checkpoint,
        CharacterCreationQualitiesDraftReceipt receipt,
        CharacterCreationQualitiesState refreshed,
        CharacterOverviewState overview)
        => IsReady(refreshed, overview)
           && ReceiptMatchesState(checkpoint, receipt, refreshed);

    public static bool ReceiptMatchesPersistedState(
        CharacterCreationQualitiesCheckpoint checkpoint,
        CharacterCreationQualitiesDraftReceipt receipt,
        CharacterCreationQualitiesState persisted)
        => checkpoint.IsStructurallyValid()
           && checkpoint.Phase == CharacterCreationQualitiesCheckpointPhase.Applying
           && ReceiptMatchesState(checkpoint, receipt, persisted);

    public static bool MatchesCheckpointBeforeActivation(
        CharacterCreationQualitiesCheckpoint checkpoint,
        CharacterOverviewState overview)
        => checkpoint.IsStructurallyValid()
           && checkpoint.Phase == CharacterCreationQualitiesCheckpointPhase.Applying
           && overview.Profile?.Created == false
           && overview.WorkspaceId == checkpoint.Preview.Binding.WorkspaceId
           && overview.ContentRevision == checkpoint.Preview.Binding.ContentRevision
           && overview.SavedRevision == checkpoint.Preview.Binding.SavedRevision
           && !overview.IsDirty
           && string.IsNullOrWhiteSpace(overview.Error);

    public static bool IsOptionConfigurable(CharacterCreationQualitiesDesktopOption option)
        => option.IsSelectable
           && string.IsNullOrWhiteSpace(option.DisableReasonKey)
           && option.SourceId != Guid.Empty
           && !string.IsNullOrWhiteSpace(option.OptionId)
           && !string.IsNullOrWhiteSpace(option.SelectionKey)
           && !string.IsNullOrWhiteSpace(option.Name)
           && option.Rating > 0
           && (option.Type == CharacterCreationQualityType.Positive
               ? option.KarmaCost >= 0
               : option.KarmaCost <= 0)
           && option.SourceAnchorIds.Count > 0
           && option.SourceAnchorIds.All(static anchor => !string.IsNullOrWhiteSpace(anchor));

    private static bool ReceiptMatchesState(
        CharacterCreationQualitiesCheckpoint checkpoint,
        CharacterCreationQualitiesDraftReceipt receipt,
        CharacterCreationQualitiesState state)
    {
        if (!string.Equals(state.Schema, CharacterCreationQualitiesSchemas.StateV1, StringComparison.Ordinal)
            || !state.CanEdit
            || state.Blockers.Count != 0
            || !Canonical(state.SnapshotDigest)
            || !Equal(
                state.SnapshotDigest,
                CharacterCreationQualitiesRules.ComputeStateDigest(state))
            || !AuthorityIsExact(state.Authority)
            || !Equal(state.Binding.AuthorityDigest, state.Authority.AuthorityDigest)
            || !Equal(state.Binding.RuntimeDigest, state.Authority.RuntimeDigest)
            || !PreviewMatchesState(state)
            || !TryCreatePlan(
                checkpoint.Preview,
                checkpoint.SelectedOptionIds,
                checkpoint.IdempotencyKey,
                checkpoint.TransactionId,
                out CharacterCreationQualitiesDraftPlan plan)
            || !CharacterCreationQualitiesRules.IsValidReceipt(receipt, plan, receipt.DraftDigest)
            || state.PendingDraft is not { } draft)
        {
            return false;
        }

        string[] selected = checkpoint.SelectedOptionIds
            .OrderBy(static item => item, StringComparer.Ordinal)
            .ToArray();
        return receipt.WorkspaceId == state.Binding.WorkspaceId
               && receipt.ContentRevision == state.Binding.ContentRevision
               && receipt.SavedRevision == state.Binding.SavedRevision
               && receipt.ContentRevision == checkpoint.Preview.Binding.ContentRevision + 1
               && !receipt.CharacterDocumentChanged
               && !draft.CharacterEffectsApplied
               && Equal(receipt.DraftDigest, draft.DraftDigest)
               && Equal(receipt.IdempotencyKeyDigest, draft.LastIdempotencyKeyDigest)
               && Equal(receipt.PreviewDigest, draft.LastPreviewDigest)
               && Equal(receipt.CommandDigest, draft.LastCommandDigest)
               && Equal(receipt.AuthorityDigest, draft.AuthorityDigest)
               && Equal(receipt.RuntimeDigest, draft.RuntimeDigest)
               && selected.SequenceEqual(
                   draft.SelectedOptionIds.OrderBy(static item => item, StringComparer.Ordinal),
                   StringComparer.Ordinal)
               && selected.SequenceEqual(
                   state.Preview.Selections.Select(static item => item.OptionId)
                       .OrderBy(static item => item, StringComparer.Ordinal),
                   StringComparer.Ordinal)
               && draft.PositiveKarmaUsed == checkpoint.Preview.PositiveQualityBudget.Used
               && draft.NegativeKarmaUsed == checkpoint.Preview.NegativeQualityBudget.Used
               && draft.KarmaRemaining == checkpoint.Preview.KarmaRemaining;
    }

    private static bool AuthorityIsExact(CharacterCreationQualitiesAuthority authority)
        => authority.IsAuthoritative
           && string.Equals(authority.Schema, CharacterCreationQualitiesSchemas.AuthorityV1, StringComparison.Ordinal)
           && string.Equals(authority.RulesetId, "sr5", StringComparison.OrdinalIgnoreCase)
           && !string.IsNullOrWhiteSpace(authority.SettingsProfileId)
           && authority.Blockers.Count == 0
           && authority.QualityKarmaLimit >= 0
           && authority.MetagenicLimit >= 0
           && Canonical(authority.SourceDigest)
           && Canonical(authority.ProfileDigest)
           && Canonical(authority.GmPolicyDigest)
           && Canonical(authority.RuntimeDigest)
           && Canonical(authority.AuthorityDigest)
           && Equal(
               authority.AuthorityDigest,
               CharacterCreationQualitiesRules.ComputeAuthorityDigest(authority))
           && authority.SourceAnchorIds.Count > 0
           && UniqueNonBlank(authority.SourceAnchorIds)
           && authority.Options.Count <= 65_536
           && authority.Options.Select(static option => option.OptionId)
               .Distinct(StringComparer.Ordinal).Count() == authority.Options.Count
           && authority.Options.All(OptionIsExact)
           && authority.GrantedQualities.Count <= 65_536
           && authority.GrantedQualities.Select(static grant => grant.GrantId)
               .Distinct(StringComparer.Ordinal).Count() == authority.GrantedQualities.Count
           && authority.GrantedQualities.All(GrantIsExact);

    private static bool OptionIsExact(CharacterCreationQualityCatalogOption option)
        => !string.IsNullOrWhiteSpace(option.OptionId)
           && option.SourceId != Guid.Empty
           && !string.IsNullOrWhiteSpace(option.SelectionKey)
           && !string.IsNullOrWhiteSpace(option.Name)
           && option.Rating > 0
           && option.MaximumSelections > 0
           && (option.Type == CharacterCreationQualityType.Positive
               ? option.KarmaCost >= 0
               : option.KarmaCost <= 0)
           && UniqueNonBlank(option.SourceAnchorIds)
           && (!option.IsSelectable || option.EligibilityIsExact)
           && (option.IsSelectable || !string.IsNullOrWhiteSpace(option.DisableReasonKey))
           && ((option.FollowUpChoiceId is null && option.FollowUpChoiceLabel is null)
               || (!string.IsNullOrWhiteSpace(option.FollowUpChoiceId)
                   && !string.IsNullOrWhiteSpace(option.FollowUpChoiceLabel)))
           && Canonical(option.OptionDigest)
           && Equal(option.OptionDigest, CharacterCreationQualitiesRules.ComputeOptionDigest(option));

    private static bool GrantIsExact(CharacterCreationGrantedQuality grant)
        => !string.IsNullOrWhiteSpace(grant.GrantId)
           && grant.SourceId != Guid.Empty
           && !string.IsNullOrWhiteSpace(grant.SelectionKey)
           && !string.IsNullOrWhiteSpace(grant.Name)
           && !string.IsNullOrWhiteSpace(grant.Origin)
           && grant.Rating > 0
           && (grant.Type == CharacterCreationQualityType.Positive
               ? grant.KarmaCost >= 0
               : grant.KarmaCost <= 0)
           && UniqueNonBlank(grant.SourceAnchorIds)
           && Canonical(grant.GrantDigest)
           && Equal(grant.GrantDigest, CharacterCreationQualitiesRules.ComputeGrantDigest(grant));

    private static bool PreviewMatchesState(CharacterCreationQualitiesState state)
    {
        IReadOnlyList<string> selected = state.PendingDraft?.SelectedOptionIds ?? [];
        CharacterCreationQualitiesPreview expected = CharacterCreationQualitiesRules.Evaluate(new(
            state.Binding,
            state.Authority,
            selected));
        return Equal(expected.PreviewDigest, state.Preview.PreviewDigest);
    }

    private static bool CanonicalPreviewMatches(
        CharacterCreationQualitiesState state,
        CharacterCreationQualitiesPreview preview,
        IReadOnlyList<string> selectedOptionIds)
    {
        CharacterCreationQualitiesPreview expected = CharacterCreationQualitiesRules.Evaluate(new(
            state.Binding,
            state.Authority,
            selectedOptionIds.OrderBy(static item => item, StringComparer.Ordinal).ToArray()));
        return Equal(expected.PreviewDigest, preview.PreviewDigest);
    }

    private static bool PendingDraftMatches(
        CharacterCreationQualitiesDraft draft,
        CharacterCreationQualitiesState state)
        => string.Equals(draft.Schema, CharacterCreationQualitiesSchemas.DraftV1, StringComparison.Ordinal)
           && draft.WorkspaceId == state.Binding.WorkspaceId
           && draft.DraftRevision > 0
           && draft.BaseContentRevision > 0
           && draft.BaseContentRevision < state.Binding.ContentRevision
           && !draft.CharacterEffectsApplied
           && Canonical(draft.DraftDigest)
           && Equal(draft.DraftDigest, CharacterCreationQualitiesRules.ComputeDraftDigest(draft))
           && Equal(draft.BaseRawCharacterXmlDigest, state.Binding.RawCharacterXmlDigest)
           && Equal(draft.PrerequisiteDraftDigest, state.Binding.PrerequisiteDraftDigest)
           && Equal(draft.AttributesDraftDigest, state.Binding.AttributesDraftDigest)
           && Equal(draft.AuthorityDigest, state.Binding.AuthorityDigest)
           && Equal(draft.RuntimeDigest, state.Binding.RuntimeDigest)
           && draft.SelectedOptionIds.SequenceEqual(
               state.Preview.Selections.Select(static item => item.OptionId),
               StringComparer.Ordinal);

    private static bool SelectedIdsAreProjected(
        CharacterCreationQualitiesState state,
        IReadOnlyList<string> selectedOptionIds)
    {
        if (selectedOptionIds.Count > state.Authority.Options.Count
            || selectedOptionIds.Any(string.IsNullOrWhiteSpace)
            || selectedOptionIds.Distinct(StringComparer.Ordinal).Count() != selectedOptionIds.Count)
            return false;
        Dictionary<string, CharacterCreationQualityCatalogOption> options = state.Authority.Options
            .ToDictionary(static option => option.OptionId, StringComparer.Ordinal);
        return selectedOptionIds.All(optionId =>
            options.TryGetValue(optionId, out CharacterCreationQualityCatalogOption? option)
            && option.IsSelectable
            && option.EligibilityIsExact
            && string.IsNullOrWhiteSpace(option.DisableReasonKey));
    }

    private static bool PreviewSelectionsMatch(
        CharacterCreationQualitiesPreview preview,
        IReadOnlyList<string> selectedOptionIds)
        => selectedOptionIds.OrderBy(static item => item, StringComparer.Ordinal)
            .SequenceEqual(
                preview.Selections.Select(static item => item.OptionId)
                    .OrderBy(static item => item, StringComparer.Ordinal),
                StringComparer.Ordinal);

    private static bool UniqueNonBlank(IReadOnlyList<string> values)
        => values.Count > 0
           && values.All(static value => !string.IsNullOrWhiteSpace(value))
           && values.Distinct(StringComparer.Ordinal).Count() == values.Count;

    private static bool Canonical(string? value)
        => CharacterCreationQualitiesRules.IsCanonicalDigest(value);

    private static bool CanonicalAuxiliary(string? value) => Canonical(value);

    private static bool Equal(string? left, string? right)
        => CharacterCreationQualitiesRules.DigestsEqual(left, right);
}
