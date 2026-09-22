using System.Text.Json;
using Chummer.Application.Owners;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Android.Native;

internal sealed class LifeModuleCompletionSession
{
    private readonly RunnerSessionCoordinator _coordinator;
    private readonly LifeModuleCompletionDraftStore? _store;
    private readonly OwnerContextStamp? _owner;
    private readonly CharacterWorkspaceId? _workspace;
    private long _version, _quoted = -1, _saved = -1;
    public CharacterCreationFoundationState? State { get; private set; }
    public CharacterCreationFoundationFinalizationPreviewRequest? Input { get; private set; }
    public CharacterCreationFoundationFinalizationPreview? Preview { get; private set; }
    public CharacterCreationFoundationFinalizationReceipt? Receipt { get; private set; }
    public IReadOnlyList<string> Blockers { get; private set; } = [];
    public bool Halted { get; private set; }
    public bool FrameCurrent => _owner is not null && _workspace is not null
        && _coordinator.State.WorkspaceId == _workspace && _coordinator.State.DisplayOwnerContext == _owner
        && _coordinator.State.Session.OwnerContext == _owner;
    public bool Ready => !Halted && FrameCurrent && State is { } state && _coordinator.IsLifeModuleCompletionStateCurrent(state);
    public bool Reviewed => Ready && _quoted == _version && Preview is { } p && _coordinator.IsLifeModuleCompletionPreviewCurrent(p);
    public bool Saved => _saved == _version;
    public bool CanDiscardInputDraft => Halted && FrameCurrent && Receipt is null && State is { } state
        && _coordinator.IsLifeModuleCompletionStateCurrent(state)
        && Blockers.Any(x => x is "life-module-input-draft-unreadable" or "life-module-input-draft-stale");
    public bool CanConfirm => Reviewed && Saved && Preview is { CanApply: true, CanConfirm: true, FinalizationBlocked.Count: 0, FinalizationPlan: not null };

    public LifeModuleCompletionSession(RunnerSessionCoordinator coordinator, LifeModuleCompletionDraftStore? store = null)
    {
        _coordinator = coordinator; _store = store ?? coordinator.LifeModuleInputDrafts;
        _owner = coordinator.State.DisplayOwnerContext; _workspace = coordinator.State.WorkspaceId;
    }

    public void Change(CharacterCreationFoundationFinalizationPreviewRequest input)
    {
        if (!Ready || State is null || !Matches(input, State)) return;
        Input = JsonSerializer.SerializeToElement(input).Deserialize<CharacterCreationFoundationFinalizationPreviewRequest>()!;
        _version++; _quoted = -1; Blockers = [];
    }

    public async Task OpenAsync(CancellationToken ct, Func<bool> current)
    {
        if (Halted || !FrameCurrent || !current() || Receipt is not null) return;
        if (State is null)
        {
            var opened = await _coordinator.LoadLifeModuleCompletionAsync(ct, () => FrameCurrent && current());
            if (!FrameCurrent || !current()) return;
            Blockers = opened.Blockers;
            if (opened.Value is not { } state) return;
            State = state;
            if (_store is not null)
            {
                try { Input = await _store.LoadAsync(_owner!.Value.Owner.Value, state.Binding.WorkspaceId, ct); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
                { Halted = true; Blockers = ["life-module-input-draft-unreadable"]; return; }
            }
            if (!Ready || !current()) return;
            if (Input is not null && !Matches(Input, state))
            { Halted = true; Blockers = ["life-module-input-draft-stale"]; return; }
            Input ??= new(state.Binding, state.PendingDraft!.DraftRevision, state.PendingDraft.DraftDigest);
        }
        await ReviewAsync(ct, current);
    }

    public async Task ReviewAsync(CancellationToken ct, Func<bool> current)
    {
        if (!Ready || Input is not { } input || State is not { } state || !current()) return;
        long version = _version;
        _quoted = -1;
        if (_store is not null)
            await _store.SaveAsync(_owner!.Value.Owner.Value, input, ct);
        if (!Ready || !current() || version != _version) return;
        _saved = version;
        var result = await _coordinator.PreviewLifeModuleCompletionAsync(state, input, ct,
            () => Ready && current() && version == _version);
        if (!Ready || !current() || version != _version) return;
        Preview = result.Value; Blockers = result.Blockers; _quoted = version;
    }

    public async Task ConfirmAsync(CancellationToken ct, Func<bool> current)
    {
        if (!CanConfirm || !current()) return;
        var result = await _coordinator.ConfirmLifeModuleCompletionAsync(Preview!, true, ct, () => FrameCurrent && current());
        _quoted = -1; Receipt = result.Value; Blockers = result.Blockers;
        Halted = true; // A missing observer result is not permission to repeat the write.
    }

    public async Task DiscardInputDraftAsync(CancellationToken ct, Func<bool> current)
    {
        if (!CanDiscardInputDraft || !current()) return;
        Input = new(State!.Binding, State.PendingDraft!.DraftRevision, State.PendingDraft.DraftDigest);
        _version++; _quoted = -1; Halted = false; Preview = null; Blockers = [];
        await ReviewAsync(ct, current);
    }

    private static bool Matches(CharacterCreationFoundationFinalizationPreviewRequest input, CharacterCreationFoundationState state)
        => JsonSerializer.Serialize(input.Binding) == JsonSerializer.Serialize(state.Binding)
           && input.DraftRevision == state.PendingDraft?.DraftRevision && input.DraftDigest == state.PendingDraft?.DraftDigest;
}
