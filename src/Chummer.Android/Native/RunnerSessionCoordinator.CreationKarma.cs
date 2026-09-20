using System.Runtime.CompilerServices;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Rulesets;
using Chummer.Presentation;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

// Phone draft only. Rules, costs, source identities and persistence remain in Core.
internal sealed record CreationKarmaPhoneSelection(
    string MetatypeOptionId,
    string? TalentOptionId = null,
    IReadOnlyList<CharacterCreationKarmaAttributeAllocation>? Attributes = null,
    CharacterCreationKarmaSkillsSelection? Skills = null)
{
    public CreationKarmaPhoneSelection Freeze() => this with
    {
        Attributes = Attributes is null ? null : Array.AsReadOnly(Attributes.ToArray()),
        Skills = Skills is null ? null : Skills with
        {
            Skills = Array.AsReadOnly(Skills.Skills.ToArray()),
            Groups = Array.AsReadOnly(Skills.Groups.ToArray())
        }
    };

    public static CreationKarmaPhoneSelection? Restore(CharacterCreationKarmaMetatypeState state)
        => state.Selection is { Command: { } command }
            ? new CreationKarmaPhoneSelection(command.MetatypeOptionId, command.TalentOptionId,
                command.AttributeAllocations, command.SkillsSelection).Freeze()
            : null;
}

internal sealed record CreationKarmaPhoneConfirmResult(
    string Outcome,
    CharacterCreationKarmaMetatypeCommit? Commit,
    CharacterCreationKarmaMetatypeState? RefreshedState,
    IReadOnlyList<string> Blockers);

public sealed partial class RunnerSessionCoordinator
{
    internal const string KarmaPostCommitRefreshRequired = "creation-karma-post-commit-refresh-required";
    internal const string KarmaOutcomeUnknown = "creation-karma-confirm-outcome-unknown";
    internal const string KarmaConfirmationAlreadyAttempted = "creation-karma-confirm-already-attempted";

    private readonly IOwnerBoundCharacterCreationKarmaMetatypeService? _ownerBoundKarmaService;
    private readonly ConditionalWeakTable<CharacterCreationKarmaMetatypeState, CharacterOverviewState> _karmaStates = new();
    private readonly ConditionalWeakTable<CharacterCreationKarmaMetatypeQuote, KarmaReview> _karmaReviews = new();
    private readonly ConditionalWeakTable<CharacterCreationKarmaMetatypeCommit, CharacterOverviewState> _karmaCommits = new();
    private CharacterCreationKarmaMetatypeState? _karmaCurrentState;
    private CharacterCreationKarmaMetatypeQuote? _karmaCurrentReview;

    internal bool CanOpenCreationKarma()
        => _ownerBoundKarmaService is not null && IsKarmaDisplayCurrent(State);

    internal async Task<CharacterCreationKarmaSkillAccess?> LoadCreationKarmaSkillAccessAsync(
        CharacterCreationKarmaMetatypeState state, CreationKarmaPhoneSelection selection,
        CancellationToken cancellationToken, Func<bool> isCurrentPage)
    {
        if (!isCurrentPage() || !IsCreationKarmaStateCurrent(state)
            || state.SkillsCatalog is not { } catalog || state.Talents is not { } talents
            || selection.TalentOptionId is not { } talent
            || state.Options.SingleOrDefault(o => o.OptionId == selection.MetatypeOptionId) is not { } metatype)
            return null;
        string? unlock = selection.Skills?.TalentUnlock;
        var result = await Task.Run(() => CharacterCreationKarmaSkillAccessRules.Evaluate(
            catalog, talents, metatype, talent, unlock), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return isCurrentPage() && IsCreationKarmaStateCurrent(state) ? result : null;
    }

    private sealed record KarmaReview(CharacterCreationKarmaMetatypeState State,
        CharacterOverviewState Original, CharacterCreationKarmaMetatypeConfirmRequest Command)
    {
        public bool ConfirmationStarted { get; set; }
    }

    private static CharacterCreationFoundationResult<T> KarmaStale<T>() where T : class
        => new(CharacterCreationFoundationOutcomes.Conflict, null, [CharacterCreationKarmaMetatypeBlockers.StaleBinding]);

    private bool IsKarmaDisplayCurrent(CharacterOverviewState original)
        => original.Profile?.Created == false && IsNativeEditDisplayCurrent(original)
           && original.CreationWizard is
           {
               BuildMethod: CharacterCreationBuildMethods.Karma,
               RulesetId: RulesetDefaults.Sr5,
               CharacterCreated: false
           } wizard
           && wizard.WorkspaceId == original.WorkspaceId?.Value
           && wizard.WorkspaceRevision == original.ContentRevision;

    private static bool KarmaMatchesDisplay(CharacterCreationKarmaMetatypeState state, CharacterOverviewState original)
        => state.Schema == CharacterCreationKarmaMetatypeSchemas.SnapshotV1
           && state.Binding.WorkspaceId == original.WorkspaceId
           && state.Binding.ContentRevision == original.ContentRevision
           && state.Binding.SavedRevision == original.SavedRevision
           && CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(state.SnapshotDigest);

    internal bool IsCreationKarmaStateCurrent(CharacterCreationKarmaMetatypeState state)
        => ReferenceEquals(_karmaCurrentState, state)
           && _karmaStates.TryGetValue(state, out var original)
           && IsKarmaDisplayCurrent(original) && KarmaMatchesDisplay(state, State);

    internal Task<CharacterCreationFoundationResult<CharacterCreationKarmaMetatypeState>> LoadCreationKarmaAsync(
        bool includeSkills = false, CancellationToken cancellationToken = default, Func<bool>? isCurrentPage = null)
    {
        var original = State;
        return WithWorkspaceActivationGateAsync(async () =>
        {
            if (isCurrentPage?.Invoke() == false || !IsKarmaDisplayCurrent(original)
                || _ownerBoundKarmaService is not { } service
                || original.DisplayOwnerContext is not { IsValid: true } owner
                || original.WorkspaceId is not { } workspace)
                return KarmaStale<CharacterCreationKarmaMetatypeState>();
            var result = await Task.Run(() => service.Load(owner, workspace, includeSkills), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (isCurrentPage?.Invoke() == false || !IsKarmaDisplayCurrent(original))
                return KarmaStale<CharacterCreationKarmaMetatypeState>();
            return AcceptKarmaState(result, original);
        }, cancellationToken);
    }

    internal Task<CharacterCreationFoundationResult<CharacterCreationKarmaMetatypeOpen>> OpenCreationKarmaAsync(
        bool includeSkills = false, CancellationToken cancellationToken = default, Func<bool>? isCurrentPage = null)
    {
        var original = State;
        return WithWorkspaceActivationGateAsync(async () =>
        {
            if (isCurrentPage?.Invoke() == false || !IsKarmaDisplayCurrent(original)
                || _ownerBoundKarmaService is not { } service
                || original.DisplayOwnerContext is not { IsValid: true } owner
                || original.WorkspaceId is not { } workspace)
                return KarmaStale<CharacterCreationKarmaMetatypeOpen>();
            _karmaCurrentState = null;
            _karmaCurrentReview = null;
            var result = await Task.Run(() => service.Open(owner, workspace, includeSkills), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (isCurrentPage?.Invoke() == false || !IsKarmaDisplayCurrent(original))
                return KarmaStale<CharacterCreationKarmaMetatypeOpen>();
            if (result is not { Outcome: CharacterCreationFoundationOutcomes.Success, Value: { } opened })
                return result;
            var accepted = AcceptKarmaState(new(result.Outcome, opened.State, result.Blockers), original);
            if (accepted.Value is not { } state)
                return new(accepted.Outcome, null, accepted.Blockers);
            var selection = CreationKarmaPhoneSelection.Restore(state);
            if (selection is null ? opened.Quote is not null
                : opened.Quote is null || AcceptKarmaPreview(
                    new(result.Outcome, opened.Quote, result.Blockers), state, selection, original).Value is null)
            {
                _karmaCurrentState = null;
                _karmaCurrentReview = null;
                return KarmaStale<CharacterCreationKarmaMetatypeOpen>();
            }
            return result;
        }, cancellationToken);
    }

    private CharacterCreationFoundationResult<CharacterCreationKarmaMetatypeState> AcceptKarmaState(
        CharacterCreationFoundationResult<CharacterCreationKarmaMetatypeState> result, CharacterOverviewState original)
    {
        _karmaCurrentState = null;
        _karmaCurrentReview = null;
        if (result is not { Outcome: CharacterCreationFoundationOutcomes.Success, Value: { } state }) return result;
        if (!KarmaMatchesDisplay(state, original) || !IsKarmaDisplayCurrent(original))
            return KarmaStale<CharacterCreationKarmaMetatypeState>();
        var issuer = _karmaStates.GetValue(state, _ => original);
        if (!IsKarmaDisplayCurrent(issuer)) return KarmaStale<CharacterCreationKarmaMetatypeState>();
        _karmaCurrentState = state;
        return result;
    }

    internal Task<CharacterCreationFoundationResult<CharacterCreationKarmaMetatypeQuote>> PreviewCreationKarmaAsync(
        CharacterCreationKarmaMetatypeState state, CreationKarmaPhoneSelection selection,
        CancellationToken cancellationToken = default, Func<bool>? isCurrentPage = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(selection);
        var frozen = selection.Freeze();
        return WithWorkspaceActivationGateAsync(async () =>
        {
            if (isCurrentPage?.Invoke() == false || !IsCreationKarmaStateCurrent(state)
                || !_karmaStates.TryGetValue(state, out var original)
                || original.DisplayOwnerContext is not { IsValid: true } owner
                || _ownerBoundKarmaService is not { } service)
                return KarmaStale<CharacterCreationKarmaMetatypeQuote>();
            _karmaCurrentReview = null;
            var result = await Task.Run(() => service.Preview(owner, state.Binding, frozen.MetatypeOptionId,
                frozen.TalentOptionId, frozen.Attributes, frozen.Skills), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (isCurrentPage?.Invoke() == false || !IsCreationKarmaStateCurrent(state))
                return KarmaStale<CharacterCreationKarmaMetatypeQuote>();
            return AcceptKarmaPreview(result, state, frozen, original);
        }, cancellationToken);
    }

    private CharacterCreationFoundationResult<CharacterCreationKarmaMetatypeQuote> AcceptKarmaPreview(
        CharacterCreationFoundationResult<CharacterCreationKarmaMetatypeQuote> result,
        CharacterCreationKarmaMetatypeState state, CreationKarmaPhoneSelection frozen,
        CharacterOverviewState original)
    {
        if (result is { Outcome: CharacterCreationFoundationOutcomes.Success, Value: { } quote })
        {
            if (quote.Binding != state.Binding || quote.SnapshotDigest != state.SnapshotDigest
                || quote.Schema != CharacterCreationKarmaMetatypeSchemas.QuoteV1
                || quote.Metatype.OptionId != frozen.MetatypeOptionId
                || quote.Talent?.OptionId != frozen.TalentOptionId
                || !CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(quote.QuoteDigest))
                return KarmaStale<CharacterCreationKarmaMetatypeQuote>();
            // The operation ID and exact reviewed command are issued once,
            // not rebuilt from mutable page controls on the Confirm click.
            var command = new CharacterCreationKarmaMetatypeConfirmRequest(state.Binding,
                frozen.MetatypeOptionId, quote.QuoteDigest, Guid.NewGuid(), true,
                frozen.TalentOptionId, frozen.Attributes, frozen.Skills);
            _karmaReviews.Add(quote, new(state, original, command));
            _karmaCurrentReview = quote;
        }
        return result;
    }

    internal bool IsCreationKarmaPreviewCurrent(CharacterCreationKarmaMetatypeQuote quote)
        => ReferenceEquals(_karmaCurrentReview, quote)
           && _karmaReviews.TryGetValue(quote, out var issued) && !issued.ConfirmationStarted
           && IsCreationKarmaStateCurrent(issued.State);

    internal bool CanDisplayCreationKarmaCommit(CharacterCreationKarmaMetatypeCommit commit)
        => _karmaCommits.TryGetValue(commit, out var original)
           && IsPrerequisiteOriginalOwnerVisible(original);

    internal Task<CreationKarmaPhoneConfirmResult> ConfirmCreationKarmaAsync(
        CharacterCreationKarmaMetatypeQuote quote, bool explicitlyConfirmed,
        CancellationToken cancellationToken = default, Func<bool>? isCurrentPage = null)
    {
        ArgumentNullException.ThrowIfNull(quote);
        if (!explicitlyConfirmed) return Task.FromResult(Rejected(CharacterCreationKarmaMetatypeBlockers.ConfirmationRequired));
        if (!_karmaReviews.TryGetValue(quote, out var issued))
            return Task.FromResult(Rejected(CharacterCreationKarmaMetatypeBlockers.StaleBinding));
        return WithWorkspaceActivationGateAsync(async () =>
        {
            if (issued.ConfirmationStarted) return Rejected(KarmaConfirmationAlreadyAttempted);
            if (isCurrentPage?.Invoke() == false || !IsCreationKarmaPreviewCurrent(quote)
                || !quote.CanSelect || quote.Blockers.Count != 0
                || issued.Original.DisplayOwnerContext is not { IsValid: true } owner
                || _ownerBoundKarmaService is not { } service)
                return Rejected(CharacterCreationKarmaMetatypeBlockers.StaleBinding);
            cancellationToken.ThrowIfCancellationRequested();
            issued.ConfirmationStarted = true;
            int entered = 0;
            CharacterCreationFoundationResult<CharacterCreationKarmaMetatypeCommit> result;
            try
            {
                result = await Task.Run(() =>
                {
                    Interlocked.Exchange(ref entered, 1);
                    return service.Confirm(owner, issued.Command);
                }, cancellationToken);
            }
            catch (OperationCanceledException) when (Volatile.Read(ref entered) == 0)
            {
                issued.ConfirmationStarted = false;
                throw;
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                // Never create a new operation or silently retry after an unknown
                // return. Reopening loads Core's durable pending-decision ledger.
                return new("outcome-unknown", null, null, [KarmaOutcomeUnknown]);
            }
            if (result is not { Outcome: CharacterCreationFoundationOutcomes.Success, Value: { } commit })
                return new(result.Outcome, result.Value, null, result.Blockers);
            _karmaCommits.Add(commit, issued.Original);
            _karmaCurrentState = null;
            _karmaCurrentReview = null;
            try
            {
                // Cancellation/route changes AFTER Core commits affect refresh,
                // never turn an acknowledged durable save into a canceled write.
                if (cancellationToken.IsCancellationRequested
                    || isCurrentPage?.Invoke() == false || !CanDisplayCreationKarmaCommit(commit)
                    || _presenter is not IOwnerBoundWorkspaceRefreshPresenter refresh)
                    return SavedNeedsRefresh();
                await refresh.LoadAsync(owner, commit.Decision.Command.Binding.WorkspaceId, cancellationToken);
                if (cancellationToken.IsCancellationRequested
                    || isCurrentPage?.Invoke() == false || !CanDisplayCreationKarmaCommit(commit)) return SavedNeedsRefresh();
                await SyncShellAsync(cancellationToken);
                if (cancellationToken.IsCancellationRequested
                    || isCurrentPage?.Invoke() == false || !CanDisplayCreationKarmaCommit(commit)) return SavedNeedsRefresh();
                var refreshed = State;
                var loaded = await Task.Run(() => service.Load(owner, commit.Decision.Command.Binding.WorkspaceId,
                    issued.State.SkillsCatalog is not null), cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (isCurrentPage?.Invoke() == false || !CanDisplayCreationKarmaCommit(commit)
                    || !IsKarmaDisplayCurrent(refreshed)) return SavedNeedsRefresh();
                var accepted = AcceptKarmaState(loaded, refreshed);
                if (accepted.Value is not { } state
                    || state.Selection?.DecisionDigest != commit.Decision.DecisionDigest
                    || state.Binding.ContentRevision != commit.Decision.CommittedContentRevision)
                    return SavedNeedsRefresh();
                return new(result.Outcome, commit, state, []);
            }
            catch (Exception error) when (error is not OutOfMemoryException) { return SavedNeedsRefresh(); }
            CreationKarmaPhoneConfirmResult SavedNeedsRefresh()
                => new(result.Outcome, commit, null, [KarmaPostCommitRefreshRequired]);
        }, cancellationToken);

        static CreationKarmaPhoneConfirmResult Rejected(string blocker)
            => new(CharacterCreationFoundationOutcomes.Conflict, null, null, [blocker]);
    }
}
