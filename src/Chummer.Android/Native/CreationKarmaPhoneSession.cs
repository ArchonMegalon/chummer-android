using Chummer.Application.Owners;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Android.Native;

// One in-memory draft shared by the phone's deep pages. It is not a workspace
// writer. Source/revision drift never silently rebases an unsaved selection.
internal sealed class CreationKarmaPhoneSession
{
    private readonly RunnerSessionCoordinator _coordinator;
    private readonly OwnerContextStamp? _owner;
    private readonly CharacterWorkspaceId? _workspace;
    private long _version;
    private long _quotedVersion = -1;
    public CharacterCreationKarmaMetatypeState? Authority { get; private set; }
    public CreationKarmaPhoneSelection? Selection { get; private set; }
    public CharacterCreationKarmaMetatypeQuote? Quote { get; private set; }
    public CharacterCreationKarmaSkillAccess? Access { get; private set; }
    public IReadOnlyList<string> Blockers { get; private set; } = [];
    public bool Halted { get; private set; }
    public bool Saved { get; private set; }
    public bool FrameCurrent => _coordinator.State.WorkspaceId == _workspace
        && _coordinator.State.DisplayOwnerContext == _owner
        && _coordinator.State.Session.OwnerContext == _owner && _coordinator.CanOpenCreationKarma();
    public bool Ready => !Halted && FrameCurrent && Authority is { } state
        && _coordinator.IsCreationKarmaStateCurrent(state);
    public bool QuoteCurrent => Ready && Quote is { } quote && _quotedVersion == _version
        && _coordinator.IsCreationKarmaPreviewCurrent(quote);

    public CreationKarmaPhoneSession(RunnerSessionCoordinator coordinator)
    {
        _coordinator = coordinator;
        _owner = coordinator.State.DisplayOwnerContext;
        _workspace = coordinator.State.WorkspaceId;
    }

    public void Change(CreationKarmaPhoneSelection selection)
    {
        if (!Ready) return;
        Selection = selection.Freeze();
        _version++;
        Saved = false;
        Blockers = [];
        Access = null;
    }

    public async Task ReloadAsync(bool includeSkills, CancellationToken ct, Func<bool> isCurrentPage)
    {
        if (Halted || !FrameCurrent || !isCurrentPage()) return;
        var previous = Authority;
        _quotedVersion = -1;
        if (Ready && Selection is not null && (!includeSkills || previous?.SkillsCatalog is not null))
        {
            // Core Preview already reloads the live workspace and source inputs,
            // then requires the exact original binding/snapshot. Do not precede
            // that with another identical catalog load on every deep-page visit.
            // Only a freshly issued quote permits reuse, never elapsed time or
            // an unchanged owner/workspace ID alone.
            await PreviewAsync(ct, isCurrentPage);
            if (QuoteCurrent || !FrameCurrent || !isCurrentPage()) return;
            // A missing quote may mean source drift or an editable invalid
            // choice. Load below distinguishes these without trusting old data.
        }
        var result = await _coordinator.LoadCreationKarmaAsync(
            includeSkills || previous?.SkillsCatalog is not null, ct, () => FrameCurrent && isCurrentPage());
        if (!FrameCurrent || !isCurrentPage()) return;
        if (result.Value is not { } state)
        { Blockers = result.Blockers; return; }
        if (previous is not null)
        {
            var expected = previous.Binding;
            // The only admissible extension is lazy loading of previously absent
            // skill authority. Existing skill digests cannot be replaced.
            expected = expected with
            {
                SkillsPolicyDigest = expected.SkillsPolicyDigest ?? state.Binding.SkillsPolicyDigest,
                SkillsCatalogDigest = expected.SkillsCatalogDigest ?? state.Binding.SkillsCatalogDigest
            };
            if (expected != state.Binding)
            { Halted = true; Blockers = [CharacterCreationKarmaMetatypeBlockers.StaleBinding]; return; }
        }
        Authority = state;
        if (previous is null) Selection = CreationKarmaPhoneSelection.Restore(state);
        Quote = null;
        Access = null;
        _quotedVersion = -1;
        Blockers = [];
    }

    public async Task PreviewAsync(CancellationToken ct, Func<bool> isCurrentPage)
    {
        if (!Ready || Selection is not { } selection || Authority is not { } state) return;
        long version = _version;
        var result = await _coordinator.PreviewCreationKarmaAsync(state, selection, ct,
            () => Ready && version == _version && isCurrentPage());
        if (!Ready || version != _version || !isCurrentPage()) return;
        Quote = result.Value;
        _quotedVersion = version;
        Blockers = result.Blockers;
        if (state.SkillsCatalog is not null)
        {
            var access = await _coordinator.LoadCreationKarmaSkillAccessAsync(state, selection, ct,
                () => Ready && version == _version && isCurrentPage());
            if (Ready && version == _version && isCurrentPage()) Access = access;
        }
    }

    public async Task ConfirmAsync(CancellationToken ct, Func<bool> isCurrentPage)
    {
        if (!QuoteCurrent || Quote is not { CanSelect: true } quote) return;
        var result = await _coordinator.ConfirmCreationKarmaAsync(quote, true, ct,
            () => FrameCurrent && isCurrentPage());
        // Even a departed page records the known local outcome in its shared
        // session, but the next appearance still checks the original owner.
        _quotedVersion = -1;
        Blockers = result.Blockers;
        if (result.Commit is not null)
        {
            Saved = true;
            if (result.RefreshedState is { } state && _coordinator.CanDisplayCreationKarmaCommit(result.Commit))
            {
                Authority = state;
                Selection = CreationKarmaPhoneSelection.Restore(state);
                Quote = state.Selection?.Quote;
            }
            else Halted = true;
        }
        else Halted = true; // Reopen and obtain a new review; never repeat this operation.
    }
}
