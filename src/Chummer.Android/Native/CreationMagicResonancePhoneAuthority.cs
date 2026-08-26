using Chummer.Contracts.Characters;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

/// <summary>
/// Android trust boundary for the SR5 Standard Priority Magic/Resonance journey. It verifies
/// binding and projection identity, then delegates every draft, preview and confirmation to the
/// renderer-neutral Presentation workflow and Core service. It never interprets XML, custom data,
/// prerequisites, costs, budgets, or character effects.
/// </summary>
internal static class CreationMagicResonancePhoneAuthority
{
    private const string IdempotencySchema =
        "chummer.android.sr5-priority-magic-resonance-command.v1";

    public static bool IsReady(
        CharacterCreationMagicResonanceState? core,
        CharacterCreationMagicResonanceEditorState? editor,
        CharacterOverviewState overview)
    {
        ArgumentNullException.ThrowIfNull(overview);
        if (core is null
            || editor is null
            || overview.Profile?.Created != false
            || overview.WorkspaceId != core.Binding.WorkspaceId
            || overview.ContentRevision != core.Binding.ContentRevision
            || overview.SavedRevision != core.Binding.SavedRevision
            || overview.ContentRevision <= 0
            || overview.SavedRevision != overview.ContentRevision
            || overview.IsDirty
            || !string.IsNullOrWhiteSpace(overview.Error)
            || !core.CanEdit
            || core.Blockers.Count != 0
            || !editor.CanEdit
            || editor.Blockers.Count != 0
            || !CharacterCreationMagicResonancePresentationContract.IsSupportedTalentKind(
                editor.Talent.Kind)
            || !CanonicalBinding(core.Binding)
            || !CharacterCreationMagicResonanceDigest.IsCanonical(core.SnapshotDigest)
            || !CharacterCreationMagicResonanceDigest.EqualsFixedTime(
                core.SnapshotDigest,
                CharacterCreationMagicResonanceDigest.Compute(
                    core with { SnapshotDigest = string.Empty }))
            || !CharacterCreationMagicResonanceWorkflow.TryProject(
                core,
                out CharacterCreationMagicResonanceEditorState? projected)
            || projected is null
            || !EditorEquals(projected, editor))
        {
            return false;
        }

        CharacterCreationMagicResonanceState? overviewCore = overview.CreationMagicResonance;
        CharacterCreationMagicResonanceEditorState? overviewEditor =
            overview.CreationMagicResonanceEditor;
        return overviewCore is not null
               && overviewEditor is not null
               && CharacterCreationMagicResonanceDigest.EqualsFixedTime(
                   overviewCore.SnapshotDigest,
                   core.SnapshotDigest)
               && BindingEquals(overviewCore.Binding, core.Binding)
               && EditorEquals(overviewEditor, editor);
    }

    public static bool MatchesOverview(
        CharacterCreationMagicResonanceState state,
        CharacterOverviewState overview)
        => CharacterCreationMagicResonanceWorkflow.TryProject(
               state,
               out CharacterCreationMagicResonanceEditorState? editor)
           && IsReady(state, editor, overview);

    public static bool BindingEquals(
        CharacterCreationMagicResonanceBinding left,
        CharacterCreationMagicResonanceBinding right)
        => CharacterCreationMagicResonanceDigest.EqualsFixedTime(
            CharacterCreationMagicResonanceDigest.Compute(left),
            CharacterCreationMagicResonanceDigest.Compute(right));

    public static bool EditorEquals(
        CharacterCreationMagicResonanceEditorState left,
        CharacterCreationMagicResonanceEditorState right)
        => CharacterCreationMagicResonanceDigest.EqualsFixedTime(
            CharacterCreationMagicResonanceDigest.Compute(left),
            CharacterCreationMagicResonanceDigest.Compute(right));

    public static bool IsOptionConfigurable(
        CharacterCreationMagicResonanceEditorState editor,
        CharacterCreationMagicResonanceOptionProjection option)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(option);
        IReadOnlyList<CharacterCreationMagicResonanceOptionProjection> catalog =
            Catalog(editor, option.Identity.Kind);
        return KindAllowed(editor.Talent, option.Identity.Kind)
               && catalog.Count(candidate => candidate.Identity == option.Identity) == 1
               && option.IsEnabled
               && option.Blockers.Count == 0
               && !string.IsNullOrWhiteSpace(option.Identity.SourceId)
               && option.PointCost >= 0m
               && option.MaximumLevels >= 1
               && option.SourceAnchorIds.Count > 0
               && option.SourceAnchorIds.All(static anchor =>
                   !string.IsNullOrWhiteSpace(anchor))
               && CharacterCreationMagicResonanceDigest.IsCanonical(
                   option.SourceNodeDigest);
    }

    public static CharacterCreationMagicResonanceDesktopDraft CreateDraft(
        CharacterCreationMagicResonanceEditorState editor,
        CharacterCreationMagicResonanceSelections selections)
        => CharacterCreationMagicResonanceWorkflow.CreateDraft(
            editor,
            selections.Tradition,
            selections.Stream,
            selections.AdeptPowers,
            selections.Spells,
            selections.ComplexForms);

    public static bool ReviewMatches(
        CharacterCreationMagicResonanceEditorState editor,
        CharacterCreationMagicResonanceReview review,
        bool requireConfirmable)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(review);
        CharacterCreationMagicResonancePreview preview = review.Preview;
        bool basic = BindingEquals(editor.Binding, review.Draft.ExpectedBinding)
                     && CharacterCreationMagicResonanceDigest.EqualsFixedTime(
                         editor.CoreSnapshotDigest,
                         review.Draft.ExpectedCoreSnapshotDigest)
                     && BindingEquals(preview.Binding, review.Draft.ExpectedBinding)
                     && CharacterCreationMagicResonanceDigest.EqualsFixedTime(
                         CharacterCreationMagicResonanceDigest.Compute(
                             preview.Selections),
                         CharacterCreationMagicResonanceDigest.Compute(
                             review.Draft.Selections))
                     && CharacterCreationMagicResonanceDigest.IsCanonical(
                         preview.PreviewDigest)
                     && CharacterCreationMagicResonanceDigest.EqualsFixedTime(
                         preview.PreviewDigest,
                         CharacterCreationMagicResonanceDigest.Compute(
                             preview with { PreviewDigest = string.Empty }))
                     && preview.RequiresExplicitConfirmation
                     && preview.CanConfirm == (preview.Blockers.Count == 0)
                     && BudgetsAreExact(preview)
                     && preview.SourceAnchorIds.Count > 0
                     && preview.SourceAnchorIds.All(static anchor =>
                         !string.IsNullOrWhiteSpace(anchor));
        return basic
               && (!requireConfirmable
                   || preview.CanConfirm
                   && preview.Blockers.Count == 0
                   && AllBudgets(preview).All(static budget => budget.Remaining == 0m));
    }

    public static bool ReviewsEqual(
        CharacterCreationMagicResonanceReview left,
        CharacterCreationMagicResonanceReview right)
        => CharacterCreationMagicResonanceDigest.EqualsFixedTime(
            CharacterCreationMagicResonanceDigest.Compute(left),
            CharacterCreationMagicResonanceDigest.Compute(right));

    public static string ComputeIdempotencyKey(
        CharacterCreationMagicResonanceReview review)
        => "android-magic-resonance:"
           + CharacterCreationMagicResonanceDigest.Compute(new
           {
               Schema = IdempotencySchema,
               review.Draft.ExpectedBinding,
               review.Draft.Selections,
               review.Preview.PreviewDigest,
               ExplicitlyConfirmed = true
           });

    public static bool ConfirmationMatches(
        CharacterCreationMagicResonanceReview review,
        string idempotencyKey,
        CharacterCreationMagicResonanceConfirmation confirmation)
    {
        ArgumentNullException.ThrowIfNull(review);
        ArgumentNullException.ThrowIfNull(confirmation);
        CharacterCreationMagicResonanceReceipt receipt = confirmation.Receipt;
        CharacterCreationMagicResonanceEditorState persisted = confirmation.PersistedState;
        CharacterCreationMagicResonanceBinding expected = review.Draft.ExpectedBinding;
        return string.Equals(
                   idempotencyKey,
                   ComputeIdempotencyKey(review),
                   StringComparison.Ordinal)
               && confirmation.IsCurrentDraft
               && !receipt.CharacterDocumentChanged
               && CharacterCreationMagicResonanceDigest.IsValidReceipt(
                   receipt,
                   expected.WorkspaceId,
                   persisted.Binding.ContentRevision)
               && receipt.PreviousContentRevision == expected.ContentRevision
               && receipt.ContentRevision == expected.ContentRevision + 1
               && receipt.SavedRevision == receipt.ContentRevision
               && persisted.Binding.ContentRevision == receipt.ContentRevision
               && persisted.Binding.SavedRevision == receipt.SavedRevision
               && CharacterCreationMagicResonanceDigest.EqualsFixedTime(
                   receipt.PreviewDigest,
                   review.Preview.PreviewDigest)
               && CharacterCreationMagicResonanceDigest.EqualsFixedTime(
                   receipt.IdempotencyKeyDigest,
                   CharacterCreationMagicResonanceDigest.ComputeUtf8(
                       idempotencyKey))
               && CharacterCreationMagicResonanceDigest.EqualsFixedTime(
                   receipt.AuthorityDigest,
                   expected.AuthorityDigest)
               && CharacterCreationMagicResonanceDigest.EqualsFixedTime(
                   receipt.SourceInputsDigest,
                   expected.SourceInputsDigest)
               && CharacterCreationMagicResonanceDigest.EqualsFixedTime(
                   receipt.CustomDataInputsDigest,
                   expected.CustomDataInputsDigest)
               && CharacterCreationMagicResonanceDigest.EqualsFixedTime(
                   receipt.GmPolicyDigest,
                   expected.GmPolicyDigest)
               && CharacterCreationMagicResonanceDigest.EqualsFixedTime(
                   receipt.RuntimeDigest,
                   expected.RuntimeDigest)
               && CharacterCreationMagicResonanceDigest.EqualsFixedTime(
                   persisted.Binding.RawCharacterXmlDigest,
                   expected.RawCharacterXmlDigest)
               && CharacterCreationMagicResonanceDigest.IsCanonical(
                   persisted.CoreSnapshotDigest);
    }

    public static bool OverviewMatchesReceipt(
        CharacterOverviewState overview,
        CharacterCreationMagicResonanceReceipt receipt)
        => overview.Profile?.Created == false
           && overview.WorkspaceId == receipt.WorkspaceId
           && overview.ContentRevision == receipt.ContentRevision
           && overview.SavedRevision == receipt.SavedRevision
           && !overview.IsDirty
           && string.IsNullOrWhiteSpace(overview.Error)
           && !receipt.CharacterDocumentChanged;

    private static bool CanonicalBinding(
        CharacterCreationMagicResonanceBinding binding)
        => CharacterCreationMagicResonanceDigest.IsCanonical(
               binding.RawCharacterXmlDigest)
           && binding.AuxiliaryStateDigest is { Length: 64 }
           && binding.AuxiliaryStateDigest.All(static character =>
               character is >= '0' and <= '9' or >= 'a' and <= 'f')
           && CharacterCreationMagicResonanceDigest.IsCanonical(
               binding.PrerequisiteDraftDigest)
           && CharacterCreationMagicResonanceDigest.IsCanonical(
               binding.PrerequisiteAuthorityDigest)
           && CharacterCreationMagicResonanceDigest.IsCanonical(
               binding.AttributesDraftDigest)
           && CharacterCreationMagicResonanceDigest.IsCanonical(
               binding.AuthorityDigest)
           && CharacterCreationMagicResonanceDigest.IsCanonical(
               binding.SourceInputsDigest)
           && CharacterCreationMagicResonanceDigest.IsCanonical(
               binding.CustomDataInputsDigest)
           && CharacterCreationMagicResonanceDigest.IsCanonical(
               binding.GmPolicyDigest)
           && CharacterCreationMagicResonanceDigest.IsCanonical(
               binding.RuntimeDigest);

    private static IReadOnlyList<CharacterCreationMagicResonanceOptionProjection> Catalog(
        CharacterCreationMagicResonanceEditorState editor,
        string kind) => kind switch
    {
        CharacterCreationMagicResonanceKinds.Tradition => editor.Traditions,
        CharacterCreationMagicResonanceKinds.Stream => editor.Streams,
        CharacterCreationMagicResonanceKinds.AdeptPower => editor.AdeptPowers,
        CharacterCreationMagicResonanceKinds.Spell => editor.Spells,
        CharacterCreationMagicResonanceKinds.ComplexForm => editor.ComplexForms,
        _ => []
    };

    private static bool KindAllowed(
        CharacterCreationMagicResonanceTalentProjection talent,
        string kind) => kind switch
    {
        CharacterCreationMagicResonanceKinds.Tradition => talent.RequiresTradition,
        CharacterCreationMagicResonanceKinds.Stream => talent.RequiresStream,
        CharacterCreationMagicResonanceKinds.AdeptPower => talent.AllowsAdeptPowers,
        CharacterCreationMagicResonanceKinds.Spell => talent.AllowsSpells,
        CharacterCreationMagicResonanceKinds.ComplexForm => talent.AllowsComplexForms,
        _ => false
    };

    private static bool BudgetsAreExact(
        CharacterCreationMagicResonancePreview preview)
    {
        CharacterCreationMagicResonanceBudgetState[] budgets = AllBudgets(preview);
        string[] expectedKinds =
        [
            CharacterCreationMagicResonanceKinds.Tradition,
            CharacterCreationMagicResonanceKinds.Stream,
            CharacterCreationMagicResonanceKinds.AdeptPower,
            CharacterCreationMagicResonanceKinds.Spell,
            CharacterCreationMagicResonanceKinds.ComplexForm
        ];
        return budgets.Select(static budget => budget.Kind)
                   .SequenceEqual(expectedKinds, StringComparer.Ordinal)
               && budgets.All(static budget =>
                   budget.Total >= 0m
                   && budget.Used >= 0m
                   && budget.Used <= budget.Total
                   && budget.Remaining == budget.Total - budget.Used
                   && budget.Blockers is not null);
    }

    private static CharacterCreationMagicResonanceBudgetState[] AllBudgets(
        CharacterCreationMagicResonancePreview preview) =>
    [
        preview.TraditionBudget,
        preview.StreamBudget,
        preview.AdeptPowerPointBudget,
        preview.SpellBudget,
        preview.ComplexFormBudget
    ];
}
