using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

public sealed record Sr5CareerRunnerBinding(
    bool Created,
    string? GameEdition,
    CharacterWorkspaceId? WorkspaceId,
    long ContentRevision,
    long SavedRevision,
    bool IsDirty,
    string? Error);

public interface ISr5CareerActiveSkillPresenter
{
    Sr5CareerRunnerBinding Binding { get; }

    Task<CareerActiveSkillAdvanceEditorState?> LoadActiveSkillsAsync(CancellationToken cancellationToken);

    Task<CareerKarmaExpenseEditorState?> LoadKarmaExpensesAsync(CancellationToken cancellationToken);

    Task<bool> ApplyAndSaveAsync(
        CareerActiveSkillAdvanceRequest request,
        CancellationToken cancellationToken);
}

internal sealed class RunnerSessionSr5CareerActiveSkillPresenter(
    RunnerSessionCoordinator coordinator) : ISr5CareerActiveSkillPresenter
{
    public Sr5CareerRunnerBinding Binding => new(
        coordinator.State.Profile?.Created == true,
        coordinator.State.Rules?.GameEdition,
        coordinator.State.WorkspaceId,
        coordinator.State.ContentRevision,
        coordinator.State.SavedRevision,
        coordinator.State.IsDirty,
        coordinator.State.Error);

    public Task<CareerActiveSkillAdvanceEditorState?> LoadActiveSkillsAsync(
        CancellationToken cancellationToken)
        => coordinator.PrepareCareerActiveSkillAdvanceAsync(cancellationToken);

    public Task<CareerKarmaExpenseEditorState?> LoadKarmaExpensesAsync(
        CancellationToken cancellationToken)
        => coordinator.PrepareCareerKarmaExpenseEditAsync(cancellationToken);

    public Task<bool> ApplyAndSaveAsync(
        CareerActiveSkillAdvanceRequest request,
        CancellationToken cancellationToken)
        => coordinator.ApplyCareerActiveSkillAdvanceAsync(request, cancellationToken);
}

/// <summary>
/// SR5-only public action boundary. The adapter's typed loaders perform fresh
/// revision-bound workspace reads, so a receipt is produced from reloaded
/// skill and expense projections rather than the reviewed draft.
/// </summary>
public sealed class Sr5CareerActiveSkillCoordinator(
    ISr5CareerActiveSkillPresenter presenter)
{
    public async Task<CareerActiveSkillAdvanceEditorState?> PrepareAsync(
        CancellationToken cancellationToken = default)
    {
        RequireCreatedSr5(presenter.Binding);
        CareerActiveSkillAdvanceEditorState? editor =
            await presenter.LoadActiveSkillsAsync(cancellationToken).ConfigureAwait(false);
        Sr5CareerRunnerBinding after = presenter.Binding;
        RequireCreatedSr5(after);
        if (editor is not null
            && (after.WorkspaceId != editor.WorkspaceId
                || after.ContentRevision != editor.ContentRevision))
        {
            throw new InvalidOperationException(
                "The SR5 runner changed while its active skills were being loaded.");
        }
        return editor;
    }

    public async Task<Sr5CareerApplyResult> ApplyAsync(
        Sr5CareerActiveSkillDraft draft,
        Sr5CareerDraftCheckpoint applyingCheckpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(applyingCheckpoint);
        Sr5CareerRunnerBinding before = presenter.Binding;
        RequireCreatedSr5(before);
        if (before.WorkspaceId != draft.WorkspaceId
            || before.ContentRevision != draft.ExpectedContentRevision)
        {
            throw new InvalidOperationException(
                "The reviewed SR5 action does not own the current runner revision.");
        }
        if (applyingCheckpoint.Phase != Sr5CareerCheckpointPhase.Applying
            || applyingCheckpoint.WorkspaceId != draft.WorkspaceId.Value
            || applyingCheckpoint.OwnerId != draft.OwnerId
            || applyingCheckpoint.ActionId != draft.Plan.ExpenseId
            || applyingCheckpoint.IdempotencyKey != draft.ActionPlan.IdempotencyKey)
        {
            throw new InvalidOperationException(
                "The exact Applying checkpoint does not own this SR5 action.");
        }

        _ = await presenter.ApplyAndSaveAsync(draft.ToRequest(), cancellationToken)
            .ConfigureAwait(false);
        Sr5CareerRecoveryResolution resolution = await ResolveAsync(
            applyingCheckpoint,
            cancellationToken).ConfigureAwait(false);
        return resolution.Status switch
        {
            Sr5CareerRecoveryStatus.AppliedVerified when resolution.Receipt is { } receipt =>
                new Sr5CareerApplyResult(
                    Sr5CareerApplyStatus.Applied,
                    draft.ActionPlan,
                    receipt.SavedContentRevision,
                    receipt,
                    resolution,
                    resolution.Message),
            Sr5CareerRecoveryStatus.NotAppliedVerified =>
                new Sr5CareerApplyResult(
                    Sr5CareerApplyStatus.RejectedBeforeMutation,
                    draft.ActionPlan,
                    SavedContentRevision: null,
                    Receipt: null,
                    resolution,
                    resolution.Message),
            _ => new Sr5CareerApplyResult(
                Sr5CareerApplyStatus.OutcomeUnknown,
                draft.ActionPlan,
                SavedContentRevision: null,
                Receipt: null,
                resolution,
                resolution.Message)
        };
    }

    public async Task<Sr5CareerRecoveryResolution> ResolveAsync(
        Sr5CareerDraftCheckpoint checkpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        Sr5CareerRunnerBinding before = presenter.Binding;
        RequireCreatedSr5(before);
        if (before.WorkspaceId?.Value != checkpoint.WorkspaceId)
        {
            throw new InvalidOperationException(
                "The recovery checkpoint belongs to another SR5 runner.");
        }

        CareerActiveSkillAdvanceEditorState? skills =
            await presenter.LoadActiveSkillsAsync(cancellationToken).ConfigureAwait(false);
        CareerKarmaExpenseEditorState? expenses =
            await presenter.LoadKarmaExpensesAsync(cancellationToken).ConfigureAwait(false);
        Sr5CareerRunnerBinding after = presenter.Binding;
        RequireCreatedSr5(after);
        if (before.WorkspaceId != after.WorkspaceId
            || before.ContentRevision != after.ContentRevision
            || before.SavedRevision != after.SavedRevision)
        {
            return Unknown(checkpoint, "The runner changed during authoritative outcome lookup.");
        }

        return Resolve(checkpoint, after, skills, expenses);
    }

    public static void RequireCreatedSr5(Sr5CareerRunnerBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        if (!Sr5CareerWizardCatalog.IsSr5CareerRunner(binding.Created, binding.GameEdition))
        {
            throw new InvalidOperationException(
                "SR5 Career actions require a created Shadowrun Fifth Edition runner.");
        }
        if (binding.WorkspaceId is not { } workspaceId
            || string.IsNullOrWhiteSpace(workspaceId.Value)
            || binding.ContentRevision <= 0)
        {
            throw new InvalidOperationException(
                "SR5 Career actions require an exact saved runner identity and revision.");
        }
    }

    internal static Sr5CareerRecoveryResolution Resolve(
        Sr5CareerDraftCheckpoint checkpoint,
        Sr5CareerRunnerBinding binding,
        CareerActiveSkillAdvanceEditorState? skills,
        CareerKarmaExpenseEditorState? expenses)
    {
        if (!checkpoint.IsStructurallyValid()
            || binding.WorkspaceId?.Value != checkpoint.WorkspaceId
            || skills is null
            || expenses is null
            || skills.WorkspaceId.Value != checkpoint.WorkspaceId
            || expenses.WorkspaceId.Value != checkpoint.WorkspaceId
            || skills.ContentRevision != binding.ContentRevision
            || expenses.ContentRevision != binding.ContentRevision
            || binding.IsDirty
            || !string.IsNullOrWhiteSpace(binding.Error))
        {
            return Unknown(checkpoint, "Fresh typed skill and expense projections were not available for one clean revision.");
        }

        CharacterCareerActiveSkillAdvanceQuote[] matchingSkills = skills.Skills
            .Where(candidate => candidate.Identity.SkillId == checkpoint.SkillId
                && candidate.Identity.SourceSkillId == checkpoint.SourceSkillId)
            .Take(2)
            .ToArray();
        CharacterCareerKarmaExpenseEntry[] matchingExpenses = expenses.Expenses
            .Where(candidate => candidate.ExpenseId == checkpoint.ActionId)
            .Take(2)
            .ToArray();
        if (matchingSkills.Length != 1 || matchingExpenses.Length > 1)
        {
            return Unknown(checkpoint, "The saved skill or expense identity is missing or ambiguous.");
        }

        CharacterCareerActiveSkillAdvanceQuote loadedSkill = matchingSkills[0];
        bool sourceMatches = string.Equals(
            loadedSkill.SourceRevision,
            checkpoint.SourceRevision,
            StringComparison.Ordinal);
        bool appliedRevision = checkpoint.ExpectedContentRevision < long.MaxValue
            && binding.ContentRevision == checkpoint.ExpectedContentRevision + 1
            && binding.SavedRevision == binding.ContentRevision;
        bool skillApplied = loadedSkill.TotalBaseRating == checkpoint.TargetRating;
        bool expenseApplied = matchingExpenses.Length == 1
            && ExpenseMatches(checkpoint, matchingExpenses[0]);
        if (appliedRevision
            && sourceMatches
            && skillApplied
            && expenseApplied
            && expenses.AvailableKarma == checkpoint.SavedKarma)
        {
            CharacterCareerKarmaExpenseEntry loadedExpense = matchingExpenses[0];
            CharacterCareerKarmaExpenseSourceAuthority loadedSource =
                loadedExpense.SourceAuthority;
            Sr5CareerActiveSkillReceipt receipt = new(
                checkpoint.OwnerId,
                checkpoint.ActionId,
                checkpoint.IdempotencyKey,
                Sr5CareerWizardRoutes.ActiveSkillReceipt,
                skills.WorkspaceId,
                checkpoint.ExpectedContentRevision,
                binding.ContentRevision,
                loadedSkill.Identity.SkillId,
                loadedSkill.Identity.SourceSkillId,
                loadedSkill.Name,
                checkpoint.PreviousRating,
                loadedSkill.TotalBaseRating,
                checked((int)-loadedExpense.Amount),
                expenses.AvailableKarma,
                loadedExpense.ExpenseId,
                loadedExpense.ExpenseDateLocal,
                loadedExpense.Reason,
                loadedSource.RawExpenseType!,
                loadedExpense.Refund,
                loadedExpense.ForceCareerVisible,
                loadedExpense.RawKarmaUndoType!,
                loadedSource.RawNuyenUndoType!,
                loadedSource.RawUndoObjectId!,
                loadedSource.UndoQuantity!.Value,
                loadedSource.RawUndoExtra!,
                loadedSkill.RuleDigest,
                loadedSkill.SourceRevision);
            return new Sr5CareerRecoveryResolution(
                Sr5CareerRecoveryStatus.AppliedVerified,
                checkpoint.WorkspaceId,
                checkpoint.OwnerId,
                checkpoint.ActionId,
                checkpoint.Version,
                receipt,
                "Fresh typed projections verified the saved skill and every exact Karma expense and undo value.");
        }

        int previousKarma;
        try
        {
            previousKarma = checked(checkpoint.SavedKarma - decimal.ToInt32(checkpoint.ExpenseAmount));
        }
        catch (OverflowException)
        {
            return Unknown(checkpoint, "The checkpoint's saved Karma delta is outside the supported range.");
        }
        bool notAppliedRevision = binding.ContentRevision == checkpoint.ExpectedContentRevision
            && binding.SavedRevision == checkpoint.ExpectedContentRevision;
        bool skillNotApplied = loadedSkill.TotalBaseRating == checkpoint.PreviousRating
            && sourceMatches
            && loadedSkill.AvailableKarma == previousKarma;
        if (notAppliedRevision && skillNotApplied && matchingExpenses.Length == 0)
        {
            return new Sr5CareerRecoveryResolution(
                Sr5CareerRecoveryStatus.NotAppliedVerified,
                checkpoint.WorkspaceId,
                checkpoint.OwnerId,
                checkpoint.ActionId,
                checkpoint.Version,
                Receipt: null,
                "Fresh typed projections prove that neither the skill nor expense mutation was saved.");
        }

        return Unknown(
            checkpoint,
            "The authoritative state is partial or conflicts with the reviewed action. Do not replay or clear it.");
    }

    private static bool ExpenseMatches(
        Sr5CareerDraftCheckpoint checkpoint,
        CharacterCareerKarmaExpenseEntry expense)
        => expense.ExpenseId == checkpoint.ActionId
           && expense.ExpenseDateLocal.Kind == DateTimeKind.Unspecified
           && expense.ExpenseDateLocal == DateTime.SpecifyKind(
               checkpoint.ExpenseDateLocal,
               DateTimeKind.Unspecified)
           && expense.Amount == checkpoint.ExpenseAmount
           && string.Equals(expense.Reason, checkpoint.ExpenseReason, StringComparison.Ordinal)
           && expense.SourceAuthority.RefundElementPresent
           && expense.Refund == checkpoint.ExpenseRefund
           && expense.SourceAuthority.ForceCareerVisibleElementPresent
           && expense.ForceCareerVisible == checkpoint.ExpenseForceCareerVisible
           && expense.KarmaUndoTypeElementPresent
           && string.Equals(
               expense.RawKarmaUndoType,
               checkpoint.KarmaUndoType,
               StringComparison.Ordinal)
           && expense.SourceAuthority.ExpenseTypeElementPresent
           && string.Equals(
               expense.SourceAuthority.RawExpenseType,
               checkpoint.ExpenseType,
               StringComparison.Ordinal)
           && expense.SourceAuthority.NuyenUndoTypeElementPresent
           && string.Equals(
               expense.SourceAuthority.RawNuyenUndoType,
               checkpoint.NuyenUndoType,
               StringComparison.Ordinal)
           && expense.SourceAuthority.UndoObjectIdElementPresent
           && string.Equals(
               expense.SourceAuthority.RawUndoObjectId,
               checkpoint.UndoObjectId,
               StringComparison.Ordinal)
           && expense.SourceAuthority.UndoQuantityElementPresent
           && expense.SourceAuthority.UndoQuantity == checkpoint.UndoQuantity
           && expense.SourceAuthority.UndoExtraElementPresent
           && string.Equals(
               expense.SourceAuthority.RawUndoExtra,
               checkpoint.UndoExtra,
               StringComparison.Ordinal);

    private static Sr5CareerRecoveryResolution Unknown(
        Sr5CareerDraftCheckpoint checkpoint,
        string message)
        => new(
            Sr5CareerRecoveryStatus.OutcomeUnknown,
            checkpoint.WorkspaceId,
            checkpoint.OwnerId,
            checkpoint.ActionId,
            checkpoint.Version,
            Receipt: null,
            message);
}
