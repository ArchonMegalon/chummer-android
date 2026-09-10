using System.Security.Cryptography;
using System.Text.Json;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

// Native, unsubmitted input only. These records are not Core mutations, quotes,
// saved-workspace checkpoints, or proof of process-death recovery.
internal sealed record TabletInspectorKey(
    CharacterWorkspaceId WorkspaceId,
    string SectionId,
    WorkspaceCollectionItemTarget? Item = null,
    WorkspaceConditionMonitorTrack? Track = null,
    string? Attribute = null,
    OwnerScope? Owner = null);

internal sealed record TabletInspectorValues(
    IReadOnlyDictionary<string, string?> Text,
    IReadOnlyDictionary<string, bool> Toggles,
    IReadOnlyDictionary<string, string?> Pickers)
{
    public bool SameAs(TabletInspectorValues other)
        => Same(Text, other.Text) && Same(Toggles, other.Toggles) && Same(Pickers, other.Pickers);

    private static bool Same<T>(IReadOnlyDictionary<string, T> left, IReadOnlyDictionary<string, T> right)
        => left.Count == right.Count && left.All(pair =>
            right.TryGetValue(pair.Key, out T? value) && EqualityComparer<T>.Default.Equals(pair.Value, value));
}

internal sealed record TabletInspectorDraft(
    TabletInspectorKey Key,
    string Authority,
    TabletInspectorValues Original,
    TabletInspectorValues Values)
{
    public bool HasChanges => !Values.SameAs(Original);
}

/// <summary>
/// Session-owned retained input, independent of page/control lifetimes. Never
/// evicts a dirty draft or rebases it onto a different authority. Disposing the
/// coordinator clears this memory; durable process-death recovery is separate.
/// </summary>
internal sealed class TabletInspectorDraftStore
{
    private readonly Dictionary<TabletInspectorKey, TabletInspectorDraft> _drafts = new(KeyComparer.Instance);
    private readonly Dictionary<(OwnerScope?, CharacterWorkspaceId, string), TabletInspectorKey> _selections = [];
    private readonly object _sync = new();
    private bool _closed;
    private long _nextLease;
    private long _activeLease;
    private TabletInspectorKey? _activeKey;

    public TabletInspectorKey? Selection(CharacterWorkspaceId workspace, string section, OwnerScope? owner = null)
    {
        lock (_sync) return _selections.GetValueOrDefault((owner, workspace, section.ToLowerInvariant()));
    }

    public long Acquire(TabletInspectorKey key)
    {
        lock (_sync)
        {
            if (_closed) return 0;
            _selections[(key.Owner, key.WorkspaceId, key.SectionId.ToLowerInvariant())] = key;
            _activeKey = key;
            return _activeLease = checked(++_nextLease);
        }
    }
    public bool Owns(long lease)
    {
        lock (_sync) return !_closed && lease > 0 && lease == _activeLease;
    }

    public void Release(long lease)
    {
        lock (_sync) if (Owns(lease)) _activeLease = 0;
    }

    public TabletInspectorDraft? Find(TabletInspectorKey key)
    {
        lock (_sync) return _drafts.GetValueOrDefault(key);
    }

    public TabletInspectorDraft Retain(TabletInspectorDraft draft, long lease)
    {
        lock (_sync)
        {
            if (!Owns(lease) || !KeyComparer.Instance.Equals(_activeKey, draft.Key)) return draft;
            // Preserve the incarnation across lifecycle captures with identical
            // input. Changing input (including A -> B -> A) creates a new one.
            if (_drafts.TryGetValue(draft.Key, out TabletInspectorDraft? current)
                && current.Authority == draft.Authority
                && current.Original.SameAs(draft.Original) && current.Values.SameAs(draft.Values))
                return current;
            if (draft.HasChanges) _drafts[draft.Key] = draft;
            else _drafts.Remove(draft.Key);
            return draft;
        }
    }

    public bool DiscardObservedIfUnchanged(TabletInspectorDraft expected)
    {
        lock (_sync)
        {
            // An already dispatched operation can finish after page departure.
            // This is not a user-discard permission: the caller must first prove
            // the exact successor projection. Identity CAS protects newer input.
            if (_closed || !_drafts.TryGetValue(expected.Key, out TabletInspectorDraft? actual)
                || !ReferenceEquals(actual, expected)) return false;
            _drafts.Remove(expected.Key);
            // A replacement page may still show the old baseline. It must bind
            // the successor before it can recapture or apply those old controls.
            if (KeyComparer.Instance.Equals(_activeKey, expected.Key)) _activeLease = 0;
            return true;
        }
    }

    public bool DiscardIfUnchanged(TabletInspectorDraft expected, long lease)
    {
        lock (_sync)
        {
            if (!Owns(lease) || !KeyComparer.Instance.Equals(_activeKey, expected.Key)) return false;
            if (!_drafts.TryGetValue(expected.Key, out TabletInspectorDraft? actual)
                || !ReferenceEquals(actual, expected))
                return false;
            return _drafts.Remove(expected.Key);
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            _closed = true;
            _activeLease = 0;
            _activeKey = null;
            _drafts.Clear();
            _selections.Clear();
        }
    }

    public static string Authority(CharacterOverviewState state, object projection)
    {
        // A change detector, not a new rules authority. Freeze the relevant
        // projection's bytes instead of retaining a mutable editor reference.
        if (projection is WorkspaceCollectionItemEditorState item)
            projection = item with { Target = item.Target with
            {
                ItemId = item.Target.ItemId.ToLowerInvariant(),
                NestedItemId = item.Target.NestedItemId?.ToLowerInvariant()
            }};
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            // Stable owner partitions retain input for explicit review after
            // relinking. The full stamp makes the old grant's input conflict;
            // matching workspace bytes never authorize automatic reapplication.
            OriginalOwner = state.DisplayOwnerContext,
            state.WorkspaceId, state.ContentRevision, state.SavedRevision,
            state.ActiveSectionId, state.ActiveSectionJson,
            state.ActiveWorkspace?.RulesetId, state.Rules,
            state.Profile?.Created, Projection = projection
        });
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    private sealed class KeyComparer : IEqualityComparer<TabletInspectorKey>
    {
        public static KeyComparer Instance { get; } = new();
        public bool Equals(TabletInspectorKey? left, TabletInspectorKey? right)
            => left is not null && right is not null && left.Owner == right.Owner
                && left.WorkspaceId == right.WorkspaceId
                && StringComparer.OrdinalIgnoreCase.Equals(left.SectionId, right.SectionId)
                && left.Track == right.Track
                && StringComparer.OrdinalIgnoreCase.Equals(left.Attribute, right.Attribute)
                && (left.Item is null ? right.Item is null
                    : right.Item is not null && CollectionItemEditorPage.TargetsMatch(left.Item, right.Item));

        public int GetHashCode(TabletInspectorKey key)
        {
            HashCode hash = new();
            hash.Add(key.Owner);
            hash.Add(key.WorkspaceId);
            hash.Add(key.SectionId, StringComparer.OrdinalIgnoreCase);
            hash.Add(key.Track);
            hash.Add(key.Attribute, StringComparer.OrdinalIgnoreCase);
            hash.Add(key.Item?.Kind);
            hash.Add(key.Item?.ItemId, StringComparer.OrdinalIgnoreCase);
            hash.Add(key.Item?.NestedKind);
            hash.Add(key.Item?.NestedItemId, StringComparer.OrdinalIgnoreCase);
            return hash.ToHashCode();
        }
    }
}
