using System.Globalization;
using Chummer.Contracts.Characters;
using static Chummer.Android.Native.Sr5CareerFlowStrings;

namespace Chummer.Android.Native;

/// <summary>Native phone catalog entry point for the Core-owned SR5 vehicle workshop.</summary>
public sealed class Sr5VehicleWorkshopPage : NativePageBase
{
    private readonly RunnerSessionSr5VehicleWorkshopAuthority _authority;
    private readonly Sr5VehicleWorkshopCheckpointStore _store;
    private readonly VerticalStackLayout _body = BodyLayout();
    private Sr5VehicleWorkshopPhoneLoadResult? _load;
    private Sr5VehicleWorkshopCheckpoint? _checkpoint;
    private Sr5VehicleWorkshopDraft? _draft;
    private string _notice = string.Empty;
    private bool _loading;

    public Sr5VehicleWorkshopPage(
        RunnerSessionCoordinator coordinator,
        RunnerSessionSr5VehicleWorkshopAuthority authority) : base(coordinator)
    {
        _authority = authority ?? throw new ArgumentNullException(nameof(authority));
        _store = new Sr5VehicleWorkshopCheckpointStore(
            new PreferencesSr5VehicleWorkshopCheckpointBackend());
        Title = T("Vehicle & Drone Workshop");
        AutomationId = "sr5-vehicle-workshop-page";
        Content = new ScrollView { Content = _body };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadAsync();
    }

    protected override void Refresh()
    {
        _body.Clear();
        _body.Add(NativeTheme.Eyebrow(T("SR5 Career")));
        _body.Add(NativeTheme.Title(T("Vehicle & Drone Workshop")));
        _body.Add(NativeTheme.Body(
            T("Choose an exact stock chassis, modifications, and complete weapon mounts. Core calculates every price and rule."),
            NativeTheme.Muted));
        _body.Add(NativeTheme.Body(
            T("Stock vehicle or drone → modifications → four-part weapon mounts → exact quote and receipt"),
            NativeTheme.Muted));

        if (_loading)
        {
            AddNotice(T("Loading exact vehicle authority…"), NativeTheme.Muted,
                "sr5-vehicle-workshop-loading");
            return;
        }
        if (!string.IsNullOrWhiteSpace(_notice))
            AddNotice(_notice, NativeTheme.Danger, "sr5-vehicle-workshop-notice");

        if (_load?.Snapshot is not { } snapshot)
        {
            AddBlockers(_load?.Blockers ?? [CharacterVehicleWorkshopBlockers.SourceAuthorityUnavailable]);
            return;
        }
        if (_checkpoint is { } boundCheckpoint
            && !string.Equals(
                boundCheckpoint.Draft.Binding.WorkspaceId,
                snapshot.Workspace.Id.Value,
                StringComparison.Ordinal))
        {
            AddNotice(
                T("A durable workshop checkpoint belongs to another runner. Reopen that runner before recovering or replacing it."),
                NativeTheme.Danger,
                "sr5-vehicle-workshop-foreign-checkpoint");
            return;
        }
        if (_checkpoint is { Stage: Sr5VehicleWorkshopCheckpointStage.PendingCommit
            or Sr5VehicleWorkshopCheckpointStage.PendingUndo })
        {
            AddPending(_checkpoint);
            return;
        }
        if (_checkpoint is { Stage: Sr5VehicleWorkshopCheckpointStage.Receipt } receiptCheckpoint)
        {
            AddReceiptRoute(receiptCheckpoint);
            return;
        }
        if (_checkpoint is { Stage: Sr5VehicleWorkshopCheckpointStage.Undone })
        {
            AddNotice(T("The workshop purchase was undone and its Nuyen restored."),
                NativeTheme.Success, "sr5-vehicle-workshop-undone");
        }
        if (_load.Blockers.Count != 0)
        {
            AddBlockers(_load.Blockers);
            AddUnsupportedRows(snapshot.Preparation);
            return;
        }

        AddBinding(snapshot);
        if (_draft is { } resumable
            && resumable.RouteId != Sr5VehicleWorkshopRoutes.Catalog
            && resumable.ChassisSourceId is not null)
        {
            _body.Add(NativeTheme.NavigationRow(
                T("Resume saved workshop"),
                T("Continue from the last durable phone step."),
                () => ResumeAsync(snapshot, resumable),
                automationId: "sr5-vehicle-workshop-resume"));
        }
        AddChassisCatalog(snapshot);
        AddUnsupportedRows(snapshot.Preparation);
    }

    private async Task LoadAsync()
    {
        _loading = true;
        Refresh();
        _notice = string.Empty;
        if (!_store.TryRead(out _checkpoint, out string checkpointReason))
        {
            _store.Remove();
            _checkpoint = null;
            _notice = checkpointReason;
        }
        _load = await _authority.LoadAsync();
        if (_load.Snapshot is { } snapshot)
        {
            if (_checkpoint is { Stage: Sr5VehicleWorkshopCheckpointStage.Draft } saved
                && saved.Draft.Matches(snapshot.Workspace.Id.Value, snapshot.Preparation))
            {
                _draft = saved.Draft;
            }
            else if (_checkpoint is { Stage: Sr5VehicleWorkshopCheckpointStage.Draft })
            {
                _store.Remove();
                _checkpoint = null;
                _notice = T("The saved workshop draft is stale and was not resumed.");
            }
            if (_checkpoint is not { Stage: Sr5VehicleWorkshopCheckpointStage.Draft })
            {
                _draft = Sr5VehicleWorkshopDraft.Create(
                    snapshot.Workspace.Id.Value,
                    snapshot.Preparation);
            }
        }
        _loading = false;
        Refresh();
    }

    private void AddChassisCatalog(Sr5VehicleWorkshopPhoneSnapshot snapshot)
    {
        _body.Add(NativeTheme.Eyebrow(T("Stock vehicles and drones")));
        CharacterVehicleWorkshopChassisEntry[] rows = snapshot.Preparation.Chassis
            .OrderBy(item => item.Kind)
            .ThenBy(item => item.Category, StringComparer.CurrentCulture)
            .ThenBy(item => item.Name, StringComparer.CurrentCulture)
            .ToArray();
        if (rows.Length == 0)
        {
            AddNotice(T("No exact vehicle catalog rows are available."), NativeTheme.Danger,
                "sr5-vehicle-workshop-empty-catalog");
            return;
        }
        foreach (CharacterVehicleWorkshopChassisEntry row in rows)
        {
            bool selectable = row.ProjectionStatus == CharacterVehicleWorkshopProjectionStatus.Exact
                              && row.Posture == CharacterVehicleChassisPosture.Stock;
            string reason = selectable
                ? string.Format(CultureInfo.CurrentCulture,
                    "{0} · {1:N0} ¥ · {2} {3} · {4} {5}",
                    ChassisKind(row.Kind),
                    row.Cost,
                    T("Slots"),
                    row.ModificationSlots,
                    T("Capacity"),
                    row.ModificationCapacity)
                : row.ProjectionStatus == CharacterVehicleWorkshopProjectionStatus.Unsupported
                    ? ExactReason(row.UnsupportedReason)
                    : T("Custom chassis require an exact GM authorization and are unavailable in this stock lane.");
            _body.Add(NativeTheme.NavigationRow(
                row.Name,
                reason,
                () => SelectChassisAsync(snapshot, row),
                selectable,
                $"sr5-vehicle-workshop-chassis-{Token(row.SourceId.Value)}"));
        }
    }

    private async Task SelectChassisAsync(
        Sr5VehicleWorkshopPhoneSnapshot snapshot,
        CharacterVehicleWorkshopChassisEntry chassis)
    {
        if (_draft is null
            || chassis.ProjectionStatus != CharacterVehicleWorkshopProjectionStatus.Exact
            || chassis.Posture != CharacterVehicleChassisPosture.Stock)
            return;
        Sr5VehicleWorkshopDraft next = _draft with
        {
            RouteId = Sr5VehicleWorkshopRoutes.Modifications,
            ChassisSourceId = chassis.SourceId,
            GmAuthorityDigest = string.Empty,
            Modifications = [],
            WeaponMounts = [],
            QuoteDigest = string.Empty
        };
        if (!SaveDraft(next))
            return;
        await Navigation.PushAsync(new Sr5VehicleWorkshopModificationsPage(
            Coordinator, _authority, _store, snapshot, next));
    }

    private async Task ResumeAsync(
        Sr5VehicleWorkshopPhoneSnapshot snapshot,
        Sr5VehicleWorkshopDraft draft)
    {
        Page page = draft.RouteId switch
        {
            Sr5VehicleWorkshopRoutes.Modifications => new Sr5VehicleWorkshopModificationsPage(
                Coordinator, _authority, _store, snapshot, draft),
            Sr5VehicleWorkshopRoutes.WeaponMounts => new Sr5VehicleWorkshopMountsPage(
                Coordinator, _authority, _store, snapshot, draft),
            Sr5VehicleWorkshopRoutes.Review => new Sr5VehicleWorkshopReviewPage(
                Coordinator, _authority, _store, snapshot, draft),
            _ => new Sr5VehicleWorkshopModificationsPage(
                Coordinator, _authority, _store, snapshot, draft)
        };
        await Navigation.PushAsync(page);
    }

    private void AddBinding(Sr5VehicleWorkshopPhoneSnapshot snapshot)
    {
        VerticalStackLayout values = new() { Spacing = 6 };
        values.Add(NativeTheme.Eyebrow(T("Exact authority")));
        values.Add(NativeTheme.Metric(T("Nuyen available"), Nuyen(snapshot.Preparation.AvailableNuyen)));
        values.Add(NativeTheme.Metric(T("Content revision"),
            snapshot.Preparation.ContentRevision.ToString(CultureInfo.InvariantCulture)));
        values.Add(Exact("sr5-vehicle-workshop-character-digest", snapshot.Preparation.CharacterDigest));
        values.Add(Exact("sr5-vehicle-workshop-catalog-digest", snapshot.Preparation.CatalogDigest));
        Border card = NativeTheme.Card(values);
        card.AutomationId = "sr5-vehicle-workshop-authority";
        _body.Add(card);
    }

    private void AddUnsupportedRows(CharacterVehicleWorkshopPreparation preparation)
    {
        if (preparation.UnsupportedRows.Count == 0)
            return;
        _body.Add(NativeTheme.Eyebrow(T("Unsupported source rows")));
        foreach (CharacterVehicleWorkshopUnsupportedRow row in preparation.UnsupportedRows)
        {
            AddNotice($"{row.Name} · {ExactReason(row.Reason)}", NativeTheme.Danger,
                $"sr5-vehicle-workshop-unsupported-{Token(row.SourceId)}");
        }
    }

    private void AddPending(Sr5VehicleWorkshopCheckpoint checkpoint)
    {
        _body.Add(NativeTheme.Eyebrow(T("Outcome recovery")));
        AddNotice(
            string.IsNullOrWhiteSpace(checkpoint.BlockReason)
                ? T("A workshop write has an unknown outcome. Recover it before doing anything else.")
                : checkpoint.BlockReason,
            NativeTheme.Danger,
            "sr5-vehicle-workshop-pending");
        _body.Add(NativeTheme.NavigationRow(
            T("Recover outcome"),
            T("Re-read the exact runner and use the saved idempotent command."),
            () => Navigation.PushAsync(new Sr5VehicleWorkshopRecoveryPage(
                Coordinator, _authority, _store, checkpoint)),
            automationId: "sr5-vehicle-workshop-open-recovery"));
    }

    private void AddReceiptRoute(Sr5VehicleWorkshopCheckpoint checkpoint)
    {
        _body.Add(NativeTheme.NavigationRow(
            T("Reopen workshop receipt"),
            T("Verify the committed transaction and check whether undo is still safe."),
            () => Navigation.PushAsync(new Sr5VehicleWorkshopReceiptPage(
                Coordinator, _authority, _store, checkpoint)),
            automationId: "sr5-vehicle-workshop-reopen-receipt"));
    }

    private bool SaveDraft(Sr5VehicleWorkshopDraft draft)
    {
        if (_store.TryWrite(Sr5VehicleWorkshopCheckpoint.ForDraft(draft), out string reason))
        {
            _draft = draft;
            _checkpoint = Sr5VehicleWorkshopCheckpoint.ForDraft(draft);
            return true;
        }
        _notice = reason;
        Refresh();
        return false;
    }

    private void AddBlockers(IReadOnlyList<string> blockers)
    {
        _body.Add(NativeTheme.Eyebrow(T("Workshop unavailable")));
        foreach (string blocker in blockers.Distinct(StringComparer.Ordinal))
            AddNotice(blocker, NativeTheme.Danger,
                $"sr5-vehicle-workshop-blocker-{Token(blocker)}");
    }

    private void AddNotice(string value, Color color, string automationId)
    {
        Label label = NativeTheme.Body(value, color);
        label.AutomationId = automationId;
        _body.Add(NativeTheme.Card(label));
    }

    internal static VerticalStackLayout BodyLayout() => new()
    {
        Padding = new Thickness(20, 18, 20, 40),
        Spacing = 14
    };

    internal static string T(string value) => Sr5CareerFlowStrings.Text(value);
    internal static string Nuyen(decimal value)
        => value.ToString("N0", CultureInfo.CurrentCulture) + " ¥";
    internal static string ExactReason(string value)
        => string.IsNullOrWhiteSpace(value)
            ? CharacterVehicleWorkshopBlockers.UnsupportedSelection
            : value;
    internal static string ChassisKind(CharacterVehicleChassisKind kind)
        => kind switch
        {
            CharacterVehicleChassisKind.Vehicle => T("Vehicle"),
            CharacterVehicleChassisKind.Drone => T("Drone"),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };
    internal static string Token(Guid value) => value.ToString("N");
    internal static string Token(string value) => new(value.Select(character =>
        char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : '-').ToArray());
    internal static Label Exact(string automationId, object value)
    {
        Label label = NativeTheme.Body(
            Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
            NativeTheme.Muted);
        label.AutomationId = automationId;
        return label;
    }
}

public sealed class Sr5VehicleWorkshopModificationsPage : NativePageBase
{
    private readonly RunnerSessionSr5VehicleWorkshopAuthority _authority;
    private readonly Sr5VehicleWorkshopCheckpointStore _store;
    private readonly Sr5VehicleWorkshopPhoneSnapshot _snapshot;
    private readonly VerticalStackLayout _body = Sr5VehicleWorkshopPage.BodyLayout();
    private Sr5VehicleWorkshopDraft _draft;
    private string _notice = string.Empty;

    public Sr5VehicleWorkshopModificationsPage(
        RunnerSessionCoordinator coordinator,
        RunnerSessionSr5VehicleWorkshopAuthority authority,
        Sr5VehicleWorkshopCheckpointStore store,
        Sr5VehicleWorkshopPhoneSnapshot snapshot,
        Sr5VehicleWorkshopDraft draft) : base(coordinator)
    {
        _authority = authority;
        _store = store;
        _snapshot = snapshot;
        _draft = draft;
        Title = T("Vehicle modifications");
        AutomationId = "sr5-vehicle-workshop-modifications-page";
        Content = new ScrollView { Content = _body };
    }

    protected override void Refresh()
    {
        _body.Clear();
        CharacterVehicleWorkshopChassisEntry? chassis = Chassis();
        _body.Add(NativeTheme.Eyebrow(T("Step 2 of 4")));
        _body.Add(NativeTheme.Title(T("Vehicle modifications")));
        _body.Add(NativeTheme.Body(chassis?.Name ?? T("No chassis selected"), NativeTheme.Muted));
        if (!string.IsNullOrWhiteSpace(_notice))
            AddNotice(_notice);
        if (chassis is null || chassis.ProjectionStatus != CharacterVehicleWorkshopProjectionStatus.Exact)
        {
            AddNotice(CharacterVehicleWorkshopBlockers.UnsupportedSelection);
            return;
        }

        _body.Add(NativeTheme.FieldLabel(T("Custom vehicle name (optional)")));
        Entry name = NativeTheme.TextField(
            "sr5-vehicle-workshop-custom-name",
            _draft.CustomName,
            T("Keep the source name"));
        name.Unfocused += (_, _) => UpdateName(name.Text);
        _body.Add(name);

        _body.Add(NativeTheme.Eyebrow(T("Exact modifications")));
        foreach (CharacterVehicleWorkshopModificationEntry entry in _snapshot.Preparation.Modifications
                     .OrderBy(item => item.Category, StringComparer.CurrentCulture)
                     .ThenBy(item => item.Name, StringComparer.CurrentCulture))
        {
            Sr5VehicleWorkshopModificationDraft? selected = _draft.Modifications
                .FirstOrDefault(item => item.SourceId == entry.SourceId);
            bool allowed = entry.AllowedChassis.Count == 0
                           || entry.AllowedChassis.Contains(chassis.SourceId);
            bool exact = entry.ProjectionStatus == CharacterVehicleWorkshopProjectionStatus.Exact
                         && allowed;
            VerticalStackLayout card = new() { Spacing = 7 };
            card.Add(NativeTheme.Title(entry.Name, 18));
            string detail = exact
                ? string.Format(CultureInfo.CurrentCulture,
                    "{0} · {1:N0} ¥ + {2:N0} ¥/{3} · {4} {5}-{6}",
                    entry.Category,
                    entry.BaseCost,
                    entry.CostPerRating,
                    T("Rating"),
                    T("Rating"),
                    entry.MinimumRating,
                    entry.MaximumRating)
                : allowed
                    ? Sr5VehicleWorkshopPage.ExactReason(entry.UnsupportedReason)
                    : T("This modification is not legal for the selected chassis.");
            card.Add(NativeTheme.Body(detail, exact ? NativeTheme.Muted : NativeTheme.Danger));
            if (selected is null)
            {
                Button add = NativeTheme.SecondaryButton(T("Add modification"));
                add.AutomationId = $"sr5-vehicle-workshop-mod-add-{Token(entry.SourceId.Value)}";
                add.IsEnabled = exact;
                add.Clicked += (_, _) => Add(entry);
                card.Add(add);
            }
            else
            {
                card.Add(NativeTheme.Metric(T("Rating"),
                    selected.Rating.ToString(CultureInfo.CurrentCulture)));
                HorizontalStackLayout actions = new() { Spacing = 8 };
                Button down = NativeTheme.SecondaryButton(T("−"));
                down.AutomationId = $"sr5-vehicle-workshop-mod-down-{Token(entry.SourceId.Value)}";
                down.IsEnabled = selected.Rating > entry.MinimumRating;
                down.Clicked += (_, _) => SetRating(entry, selected.Rating - 1);
                actions.Add(down);
                Button up = NativeTheme.SecondaryButton(T("+"));
                up.AutomationId = $"sr5-vehicle-workshop-mod-up-{Token(entry.SourceId.Value)}";
                up.IsEnabled = selected.Rating < entry.MaximumRating;
                up.Clicked += (_, _) => SetRating(entry, selected.Rating + 1);
                actions.Add(up);
                Button remove = NativeTheme.SecondaryButton(T("Remove"));
                remove.AutomationId = $"sr5-vehicle-workshop-mod-remove-{Token(entry.SourceId.Value)}";
                remove.Clicked += (_, _) => Remove(entry.SourceId);
                actions.Add(remove);
                card.Add(actions);
            }
            Border border = NativeTheme.Card(card);
            border.AutomationId = $"sr5-vehicle-workshop-mod-{Token(entry.SourceId.Value)}";
            _body.Add(border);
        }

        Button next = NativeTheme.PrimaryButton(T("Continue to weapon mounts"));
        next.AutomationId = "sr5-vehicle-workshop-modifications-next";
        next.Clicked += async (_, _) => await ContinueAsync(name.Text);
        _body.Add(next);
    }

    private CharacterVehicleWorkshopChassisEntry? Chassis()
        => _draft.ChassisSourceId is { } sourceId
            ? _snapshot.Preparation.Chassis.SingleOrDefault(item => item.SourceId == sourceId)
            : null;

    private void UpdateName(string? value)
    {
        string next = value?.Trim() ?? string.Empty;
        if (next.Length > CharacterVehicleWorkshopRules.MaximumCustomNameLength)
        {
            _notice = T("The optional vehicle name is too long.");
            Refresh();
            return;
        }
        Save(_draft with { CustomName = next, QuoteDigest = string.Empty });
    }

    private void Add(CharacterVehicleWorkshopModificationEntry entry)
    {
        CharacterVehicleWorkshopChassisEntry? chassis = Chassis();
        if (entry.ProjectionStatus != CharacterVehicleWorkshopProjectionStatus.Exact
            || chassis is null
            || (entry.AllowedChassis.Count != 0
                && !entry.AllowedChassis.Contains(chassis.SourceId)))
            return;
        Save(_draft with
        {
            Modifications = _draft.Modifications.Append(
                new Sr5VehicleWorkshopModificationDraft(
                    entry.SourceId,
                    new CharacterVehicleModificationInstanceId(Guid.NewGuid()),
                    entry.MinimumRating)).ToArray(),
            QuoteDigest = string.Empty
        });
    }

    private void SetRating(CharacterVehicleWorkshopModificationEntry entry, int rating)
    {
        if (rating < entry.MinimumRating || rating > entry.MaximumRating)
            return;
        Save(_draft with
        {
            Modifications = _draft.Modifications.Select(item => item.SourceId == entry.SourceId
                ? item with { Rating = rating }
                : item).ToArray(),
            QuoteDigest = string.Empty
        });
    }

    private void Remove(CharacterVehicleModificationSourceId sourceId)
        => Save(_draft with
        {
            Modifications = _draft.Modifications.Where(item => item.SourceId != sourceId).ToArray(),
            QuoteDigest = string.Empty
        });

    private async Task ContinueAsync(string? name)
    {
        UpdateName(name);
        if (!string.IsNullOrWhiteSpace(_notice))
            return;
        Sr5VehicleWorkshopDraft next = _draft with
        {
            RouteId = Sr5VehicleWorkshopRoutes.WeaponMounts,
            QuoteDigest = string.Empty
        };
        if (!Save(next))
            return;
        await Navigation.PushAsync(new Sr5VehicleWorkshopMountsPage(
            Coordinator, _authority, _store, _snapshot, next));
    }

    private bool Save(Sr5VehicleWorkshopDraft next)
    {
        if (!_store.TryWrite(Sr5VehicleWorkshopCheckpoint.ForDraft(next), out _notice))
        {
            Refresh();
            return false;
        }
        _notice = string.Empty;
        _draft = next;
        Refresh();
        return true;
    }

    private void AddNotice(string reason)
    {
        Label label = NativeTheme.Body(reason, NativeTheme.Danger);
        label.AutomationId = "sr5-vehicle-workshop-modifications-notice";
        _body.Add(NativeTheme.Card(label));
    }

    private static string T(string value) => Sr5VehicleWorkshopPage.T(value);
    private static string Token(Guid value) => Sr5VehicleWorkshopPage.Token(value);
}

public sealed class Sr5VehicleWorkshopMountsPage : NativePageBase
{
    private readonly RunnerSessionSr5VehicleWorkshopAuthority _authority;
    private readonly Sr5VehicleWorkshopCheckpointStore _store;
    private readonly Sr5VehicleWorkshopPhoneSnapshot _snapshot;
    private readonly VerticalStackLayout _body = Sr5VehicleWorkshopPage.BodyLayout();
    private Sr5VehicleWorkshopDraft _draft;
    private int _selectedMountIndex = -1;
    private string _notice = string.Empty;

    public Sr5VehicleWorkshopMountsPage(
        RunnerSessionCoordinator coordinator,
        RunnerSessionSr5VehicleWorkshopAuthority authority,
        Sr5VehicleWorkshopCheckpointStore store,
        Sr5VehicleWorkshopPhoneSnapshot snapshot,
        Sr5VehicleWorkshopDraft draft) : base(coordinator)
    {
        _authority = authority;
        _store = store;
        _snapshot = snapshot;
        _draft = draft;
        _selectedMountIndex = draft.WeaponMounts.Count == 0 ? -1 : draft.WeaponMounts.Count - 1;
        Title = T("Weapon mounts");
        AutomationId = "sr5-vehicle-workshop-mounts-page";
        Content = new ScrollView { Content = _body };
    }

    protected override void Refresh()
    {
        _body.Clear();
        _body.Add(NativeTheme.Eyebrow(T("Step 3 of 4")));
        _body.Add(NativeTheme.Title(T("Weapon mounts")));
        _body.Add(NativeTheme.Body(
            T("Every mount needs one exact Size, Visibility, Flexibility, and Control component."),
            NativeTheme.Muted));
        if (!string.IsNullOrWhiteSpace(_notice))
            AddNotice(_notice);

        for (int index = 0; index < _draft.WeaponMounts.Count; index++)
        {
            int captured = index;
            Sr5VehicleWorkshopMountDraft mount = _draft.WeaponMounts[index];
            _body.Add(NativeTheme.NavigationRow(
                string.Format(CultureInfo.CurrentCulture, T("Weapon mount {0}"), index + 1),
                string.Format(CultureInfo.CurrentCulture, T("{0} of 4 components selected"),
                    mount.Components.Count),
                () =>
                {
                    _selectedMountIndex = captured;
                    Refresh();
                    return Task.CompletedTask;
                },
                automationId: $"sr5-vehicle-workshop-mount-select-{index}"));
        }

        Button addMount = NativeTheme.SecondaryButton(T("Add weapon mount"));
        addMount.AutomationId = "sr5-vehicle-workshop-mount-add";
        addMount.Clicked += (_, _) => AddMount();
        _body.Add(addMount);

        if (_selectedMountIndex >= 0 && _selectedMountIndex < _draft.WeaponMounts.Count)
        {
            AddMountEditor(_selectedMountIndex);
        }

        bool complete = _draft.WeaponMounts.All(IsCompleteMount);
        Button review = NativeTheme.PrimaryButton(T("Review exact quote"));
        review.AutomationId = "sr5-vehicle-workshop-mounts-review";
        review.IsEnabled = complete;
        review.Clicked += async (_, _) => await ReviewAsync();
        _body.Add(review);
        if (!complete)
            AddNotice(T("Complete all four typed parts of every weapon mount before review."));
    }

    private void AddMountEditor(int index)
    {
        Sr5VehicleWorkshopMountDraft mount = _draft.WeaponMounts[index];
        _body.Add(NativeTheme.Eyebrow(string.Format(
            CultureInfo.CurrentCulture,
            T("Compose weapon mount {0}"),
            index + 1)));
        CharacterVehicleWorkshopChassisEntry? chassis = _draft.ChassisSourceId is { } chassisId
            ? _snapshot.Preparation.Chassis.SingleOrDefault(item => item.SourceId == chassisId)
            : null;
        foreach (CharacterVehicleWeaponMountComponentKind kind in Enum
                     .GetValues<CharacterVehicleWeaponMountComponentKind>())
        {
            _body.Add(NativeTheme.FieldLabel(MountKindLabel(kind)));
            foreach (CharacterVehicleWeaponMountComponentEntry entry in
                     _snapshot.Preparation.WeaponMountComponents
                         .Where(item => item.Kind == kind)
                         .OrderBy(item => item.Name, StringComparer.CurrentCulture))
            {
                bool allowed = chassis is not null
                               && (entry.AllowedChassis.Count == 0
                                   || entry.AllowedChassis.Contains(chassis.SourceId));
                bool exact = entry.ProjectionStatus == CharacterVehicleWorkshopProjectionStatus.Exact
                             && allowed;
                bool selected = mount.Components.Any(item => item.SourceId == entry.SourceId);
                string detail = exact
                    ? string.Format(CultureInfo.CurrentCulture,
                        "{0:N0} ¥ · {1} {2} · {3} {4}{5}",
                        entry.Cost,
                        T("Slots"), entry.Slots,
                        T("Capacity"), entry.Capacity,
                        selected ? " · " + T("Selected") : string.Empty)
                    : allowed
                        ? Sr5VehicleWorkshopPage.ExactReason(entry.UnsupportedReason)
                        : T("This mount component is not legal for the selected chassis.");
                _body.Add(NativeTheme.NavigationRow(
                    entry.Name,
                    detail,
                    () =>
                    {
                        SelectComponent(index, entry);
                        return Task.CompletedTask;
                    },
                    exact,
                    $"sr5-vehicle-workshop-mount-{index}-{kind.ToString().ToLowerInvariant()}-{Token(entry.SourceId.Value)}"));
            }
        }

        Button remove = NativeTheme.SecondaryButton(T("Remove weapon mount"));
        remove.AutomationId = $"sr5-vehicle-workshop-mount-remove-{index}";
        remove.Clicked += (_, _) => RemoveMount(index);
        _body.Add(remove);
    }

    private void AddMount()
    {
        Sr5VehicleWorkshopMountDraft mount = new(
            new CharacterVehicleWeaponMountInstanceId(Guid.NewGuid()),
            []);
        Sr5VehicleWorkshopDraft next = _draft with
        {
            WeaponMounts = _draft.WeaponMounts.Append(mount).ToArray(),
            QuoteDigest = string.Empty
        };
        _selectedMountIndex = next.WeaponMounts.Count - 1;
        Save(next);
    }

    private void SelectComponent(
        int mountIndex,
        CharacterVehicleWeaponMountComponentEntry entry)
    {
        CharacterVehicleWorkshopChassisEntry? chassis = _draft.ChassisSourceId is { } chassisId
            ? _snapshot.Preparation.Chassis.SingleOrDefault(item => item.SourceId == chassisId)
            : null;
        if (entry.ProjectionStatus != CharacterVehicleWorkshopProjectionStatus.Exact
            || chassis is null
            || (entry.AllowedChassis.Count != 0
                && !entry.AllowedChassis.Contains(chassis.SourceId)))
            return;
        Sr5VehicleWorkshopMountDraft mount = _draft.WeaponMounts[mountIndex];
        CharacterVehicleWeaponMountComponentSourceId[] sameKind =
            _snapshot.Preparation.WeaponMountComponents
                .Where(item => item.Kind == entry.Kind)
                .Select(item => item.SourceId)
                .ToArray();
        Sr5VehicleWorkshopMountDraft updated = mount with
        {
            Components = mount.Components
                .Where(item => !sameKind.Contains(item.SourceId))
                .Append(new Sr5VehicleWorkshopMountComponentDraft(
                    entry.SourceId,
                    new CharacterVehicleWeaponMountComponentInstanceId(Guid.NewGuid())))
                .ToArray()
        };
        Save(_draft with
        {
            WeaponMounts = _draft.WeaponMounts.Select((item, index) =>
                index == mountIndex ? updated : item).ToArray(),
            QuoteDigest = string.Empty
        });
    }

    private void RemoveMount(int index)
    {
        Save(_draft with
        {
            WeaponMounts = _draft.WeaponMounts.Where((_, itemIndex) => itemIndex != index).ToArray(),
            QuoteDigest = string.Empty
        });
        _selectedMountIndex = Math.Min(index, _draft.WeaponMounts.Count - 1);
        Refresh();
    }

    private bool IsCompleteMount(Sr5VehicleWorkshopMountDraft mount)
    {
        if (mount.Components.Count != 4)
            return false;
        CharacterVehicleWeaponMountComponentEntry[] entries = mount.Components
            .Select(component => _snapshot.Preparation.WeaponMountComponents.SingleOrDefault(
                item => item.SourceId == component.SourceId))
            .Where(item => item is not null)
            .Cast<CharacterVehicleWeaponMountComponentEntry>()
            .ToArray();
        return entries.Length == 4
               && entries.Select(item => item.Kind).Distinct().Count() == 4;
    }

    private async Task ReviewAsync()
    {
        if (!_draft.WeaponMounts.All(IsCompleteMount))
            return;
        Sr5VehicleWorkshopDraft next = _draft with
        {
            RouteId = Sr5VehicleWorkshopRoutes.Review,
            QuoteDigest = string.Empty
        };
        if (!Save(next))
            return;
        await Navigation.PushAsync(new Sr5VehicleWorkshopReviewPage(
            Coordinator, _authority, _store, _snapshot, next));
    }

    private bool Save(Sr5VehicleWorkshopDraft next)
    {
        if (!_store.TryWrite(Sr5VehicleWorkshopCheckpoint.ForDraft(next), out _notice))
        {
            Refresh();
            return false;
        }
        _notice = string.Empty;
        _draft = next;
        Refresh();
        return true;
    }

    private void AddNotice(string reason)
    {
        Label label = NativeTheme.Body(reason, NativeTheme.Danger);
        label.AutomationId = "sr5-vehicle-workshop-mounts-notice";
        _body.Add(NativeTheme.Card(label));
    }

    private static string T(string value) => Sr5VehicleWorkshopPage.T(value);
    private static string Token(Guid value) => Sr5VehicleWorkshopPage.Token(value);
    private static string MountKindLabel(CharacterVehicleWeaponMountComponentKind kind)
        => kind switch
        {
            CharacterVehicleWeaponMountComponentKind.Size => T("Size"),
            CharacterVehicleWeaponMountComponentKind.Visibility => T("Visibility"),
            CharacterVehicleWeaponMountComponentKind.Flexibility => T("Flexibility"),
            CharacterVehicleWeaponMountComponentKind.Control => T("Control"),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };
}

public sealed class Sr5VehicleWorkshopReviewPage : NativePageBase
{
    private readonly RunnerSessionSr5VehicleWorkshopAuthority _authority;
    private readonly Sr5VehicleWorkshopCheckpointStore _store;
    private readonly Sr5VehicleWorkshopPhoneSnapshot _snapshot;
    private readonly VerticalStackLayout _body = Sr5VehicleWorkshopPage.BodyLayout();
    private Sr5VehicleWorkshopDraft _draft;
    private CharacterVehicleWorkshopQuote? _quote;
    private string _notice = string.Empty;
    private bool _confirming;

    public Sr5VehicleWorkshopReviewPage(
        RunnerSessionCoordinator coordinator,
        RunnerSessionSr5VehicleWorkshopAuthority authority,
        Sr5VehicleWorkshopCheckpointStore store,
        Sr5VehicleWorkshopPhoneSnapshot snapshot,
        Sr5VehicleWorkshopDraft draft) : base(coordinator)
    {
        _authority = authority;
        _store = store;
        _snapshot = snapshot;
        _draft = draft;
        Title = T("Review workshop quote");
        AutomationId = "sr5-vehicle-workshop-review-page";
        Content = new ScrollView { Content = _body };
        PrepareQuote();
    }

    protected override void Refresh()
    {
        _body.Clear();
        _body.Add(NativeTheme.Eyebrow(T("Step 4 of 4")));
        _body.Add(NativeTheme.Title(T("Review workshop quote")));
        if (!string.IsNullOrWhiteSpace(_notice))
            AddNotice(_notice);
        if (_quote is not { } quote)
        {
            AddNotice(CharacterVehicleWorkshopBlockers.StaleQuote);
            return;
        }

        VerticalStackLayout totals = new() { Spacing = 6 };
        totals.Add(NativeTheme.Metric(T("Vehicle"), quote.DisplayName));
        totals.Add(NativeTheme.Metric(T("Kind"), Sr5VehicleWorkshopPage.ChassisKind(quote.Kind)));
        totals.Add(NativeTheme.Metric(T("Total cost"), Sr5VehicleWorkshopPage.Nuyen(quote.TotalCost)));
        totals.Add(NativeTheme.Metric(T("Slots used"), $"{quote.SlotsUsed} / {quote.SlotsUsed + quote.SlotsRemaining}"));
        totals.Add(NativeTheme.Metric(T("Capacity used"), $"{quote.CapacityUsed} / {quote.CapacityUsed + quote.CapacityRemaining}"));
        totals.Add(NativeTheme.Metric(T("Availability"), $"{quote.Availability.Value}{Legality(quote.Availability.Legality)}"));
        totals.Add(Sr5VehicleWorkshopPage.Exact("sr5-vehicle-workshop-quote-digest", quote.QuoteDigest));
        Border card = NativeTheme.Card(totals);
        card.AutomationId = "sr5-vehicle-workshop-quote";
        _body.Add(card);

        _body.Add(NativeTheme.Eyebrow(T("Quote lines")));
        foreach (CharacterVehicleWorkshopQuoteLine line in quote.Lines)
        {
            string detail = line.Exact
                ? $"{Sr5VehicleWorkshopPage.Nuyen(line.Cost)} · {T("Slots")} {line.Slots} · {T("Capacity")} {line.Capacity}"
                : line.BlockReason;
            _body.Add(NativeTheme.NavigationRow(
                string.IsNullOrWhiteSpace(line.Name) ? line.Kind : line.Name,
                detail,
                () => Task.CompletedTask,
                enabled: false,
                automationId: $"sr5-vehicle-workshop-quote-line-{Sr5VehicleWorkshopPage.Token(line.InstanceId)}"));
        }
        foreach (string blocker in quote.Blockers)
            AddNotice(blocker);

        Button confirm = NativeTheme.PrimaryButton(T("Confirm purchase"));
        confirm.AutomationId = "sr5-vehicle-workshop-confirm";
        confirm.IsEnabled = quote.Exact && !_confirming;
        confirm.Clicked += async (_, _) => await ConfirmAsync();
        _body.Add(confirm);
        _body.Add(NativeTheme.Body(
            T("Confirmation persists one new vehicle, its composed children, one expense, and the exact Nuyen delta atomically."),
            NativeTheme.Muted));
    }

    private void PrepareQuote()
    {
        CharacterVehicleWorkshopQuote quote = _authority.Quote(_snapshot, _draft);
        _quote = quote;
        if (!quote.Exact)
            return;
        _draft = _draft with { QuoteDigest = quote.QuoteDigest };
        if (!_store.TryWrite(Sr5VehicleWorkshopCheckpoint.ForDraft(_draft), out _notice))
            _quote = null;
    }

    private async Task ConfirmAsync()
    {
        if (_confirming || _quote is not { Exact: true } quote
            || !_draft.TryCreateSelection(out CharacterVehicleWorkshopSelection selection))
            return;
        _confirming = true;
        Refresh();
        var command = new CharacterVehicleWorkshopCommitCommand(
            _draft.Binding.ContentRevision,
            _draft.Binding.CharacterDigest,
            _draft.Binding.CatalogDigest,
            quote.QuoteDigest,
            $"android-sr5-vehicle-workshop:{Guid.NewGuid():D}",
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            selection);
        CharacterVehicleWorkshopCommitResult prepared = _authority.PrepareCommit(_snapshot, command);
        if (prepared.Status != CharacterVehicleWorkshopCommitStatus.Committed
            || prepared.Receipt is null)
        {
            _notice = prepared.BlockReason.Length == 0
                ? CharacterVehicleWorkshopBlockers.StaleQuote
                : prepared.BlockReason;
            _confirming = false;
            Refresh();
            return;
        }

        var pending = new Sr5VehicleWorkshopCheckpoint(
            Sr5VehicleWorkshopCheckpoint.CurrentSchemaVersion,
            Sr5VehicleWorkshopCheckpointStage.PendingCommit,
            _draft with { RouteId = Sr5VehicleWorkshopRoutes.Recovery },
            command,
            null,
            prepared.NewContentRevision,
            prepared.NewCharacterDigest,
            string.Empty);
        if (!_store.TryWrite(pending, out _notice))
        {
            _confirming = false;
            Refresh();
            return;
        }

        Sr5VehicleWorkshopPhoneMutationResult persisted =
            await _authority.PersistPreparedAsync(_snapshot, prepared);
        if (persisted.Status == Sr5VehicleWorkshopPhoneMutationStatus.Completed
            && persisted.Receipt is { } receipt)
        {
            var completed = pending with
            {
                Stage = Sr5VehicleWorkshopCheckpointStage.Receipt,
                Draft = pending.Draft with { RouteId = Sr5VehicleWorkshopRoutes.Receipt },
                Receipt = receipt
            };
            if (!_store.TryWrite(completed, out _notice))
            {
                _confirming = false;
                Refresh();
                return;
            }
            await Navigation.PushAsync(new Sr5VehicleWorkshopReceiptPage(
                Coordinator, _authority, _store, completed));
            return;
        }

        Sr5VehicleWorkshopCheckpoint unknown = pending with
        {
            BlockReason = persisted.BlockReason
        };
        _store.TryWrite(unknown, out _);
        await Navigation.PushAsync(new Sr5VehicleWorkshopRecoveryPage(
            Coordinator, _authority, _store, unknown));
    }

    private void AddNotice(string reason)
    {
        Label label = NativeTheme.Body(reason, NativeTheme.Danger);
        label.AutomationId = "sr5-vehicle-workshop-review-notice";
        _body.Add(NativeTheme.Card(label));
    }

    private static string Legality(CharacterVehicleWorkshopLegality legality)
        => legality switch
        {
            CharacterVehicleWorkshopLegality.Restricted => "R",
            CharacterVehicleWorkshopLegality.Forbidden => "F",
            _ => string.Empty
        };
    private static string T(string value) => Sr5VehicleWorkshopPage.T(value);
}

public sealed class Sr5VehicleWorkshopReceiptPage : NativePageBase
{
    private readonly RunnerSessionSr5VehicleWorkshopAuthority _authority;
    private readonly Sr5VehicleWorkshopCheckpointStore _store;
    private readonly VerticalStackLayout _body = Sr5VehicleWorkshopPage.BodyLayout();
    private Sr5VehicleWorkshopCheckpoint _checkpoint;
    private CharacterVehicleWorkshopCommitReceipt? _receipt;
    private string _notice = string.Empty;
    private bool _busy;

    public Sr5VehicleWorkshopReceiptPage(
        RunnerSessionCoordinator coordinator,
        RunnerSessionSr5VehicleWorkshopAuthority authority,
        Sr5VehicleWorkshopCheckpointStore store,
        Sr5VehicleWorkshopCheckpoint checkpoint) : base(coordinator)
    {
        _authority = authority;
        _store = store;
        _checkpoint = checkpoint;
        _receipt = checkpoint.Receipt;
        Title = T("Workshop receipt");
        AutomationId = "sr5-vehicle-workshop-receipt-page";
        Content = new ScrollView { Content = _body };
    }

    protected override void Refresh()
    {
        _body.Clear();
        _body.Add(NativeTheme.Eyebrow(T("Durable receipt")));
        _body.Add(NativeTheme.Title(T("Workshop receipt")));
        if (!string.IsNullOrWhiteSpace(_notice))
            AddNotice(_notice, NativeTheme.Danger);
        if (_checkpoint.Stage == Sr5VehicleWorkshopCheckpointStage.Undone)
        {
            AddNotice(T("The workshop purchase was undone and its Nuyen restored."), NativeTheme.Success);
            AddStartAnother();
            return;
        }
        if (_receipt is not { } receipt)
        {
            AddNotice(CharacterVehicleWorkshopBlockers.StaleReceipt, NativeTheme.Danger);
            return;
        }

        VerticalStackLayout values = new() { Spacing = 6 };
        values.Add(NativeTheme.Metric(T("Content revision"), receipt.ContentRevision.ToString(CultureInfo.InvariantCulture)));
        values.Add(NativeTheme.Metric(T("Nuyen delta"), Sr5VehicleWorkshopPage.Nuyen(receipt.NuyenDelta)));
        values.Add(Sr5VehicleWorkshopPage.Exact("sr5-vehicle-workshop-receipt-vehicle-id", receipt.VehicleInstanceId.Value));
        values.Add(Sr5VehicleWorkshopPage.Exact("sr5-vehicle-workshop-receipt-expense-id", receipt.ExpenseId));
        values.Add(Sr5VehicleWorkshopPage.Exact("sr5-vehicle-workshop-receipt-digest", receipt.ReceiptDigest));
        Border card = NativeTheme.Card(values);
        card.AutomationId = "sr5-vehicle-workshop-receipt";
        _body.Add(card);

        Button reopen = NativeTheme.SecondaryButton(T("Verify and reopen receipt"));
        reopen.AutomationId = "sr5-vehicle-workshop-receipt-reopen";
        reopen.IsEnabled = !_busy;
        reopen.Clicked += async (_, _) => await ReopenAsync();
        _body.Add(reopen);

        Button undo = NativeTheme.PrimaryButton(T("Undo workshop purchase"));
        undo.AutomationId = "sr5-vehicle-workshop-undo";
        undo.IsEnabled = receipt.UndoReady && !_busy;
        undo.Clicked += async (_, _) => await UndoAsync(receipt);
        _body.Add(undo);
        if (!receipt.UndoReady)
            AddNotice(T("Undo is no longer safe because the runner changed after this receipt."), NativeTheme.Danger);
        AddStartAnother();
    }

    private async Task ReopenAsync()
    {
        _busy = true;
        Refresh();
        Sr5VehicleWorkshopPhoneMutationResult reopened =
            await _authority.ReopenReceiptAsync(_checkpoint);
        if (reopened.Status == Sr5VehicleWorkshopPhoneMutationStatus.Completed
            && reopened.Receipt is { } receipt)
        {
            _receipt = receipt;
            _checkpoint = _checkpoint with { Receipt = receipt, BlockReason = string.Empty };
            _store.TryWrite(_checkpoint, out _notice);
        }
        else
        {
            _notice = reopened.BlockReason;
        }
        _busy = false;
        Refresh();
    }

    private async Task UndoAsync(CharacterVehicleWorkshopCommitReceipt receipt)
    {
        _busy = true;
        Refresh();
        Sr5VehicleWorkshopPhoneLoadResult load = await _authority.LoadAsync();
        if (load.Snapshot is not { } snapshot || load.Blockers.Count != 0)
        {
            _notice = load.Blockers.FirstOrDefault()
                      ?? CharacterVehicleWorkshopBlockers.StaleReceipt;
            _busy = false;
            Refresh();
            return;
        }
        CharacterVehicleWorkshopCommitResult prepared = _authority.PrepareUndo(snapshot, receipt);
        if (prepared.Status != CharacterVehicleWorkshopCommitStatus.Undone)
        {
            _notice = prepared.BlockReason;
            _busy = false;
            Refresh();
            return;
        }
        var pending = _checkpoint with
        {
            Stage = Sr5VehicleWorkshopCheckpointStage.PendingUndo,
            Draft = _checkpoint.Draft with { RouteId = Sr5VehicleWorkshopRoutes.Recovery },
            ExpectedOutputRevision = prepared.NewContentRevision,
            ExpectedOutputDigest = prepared.NewCharacterDigest,
            BlockReason = string.Empty
        };
        if (!_store.TryWrite(pending, out _notice))
        {
            _busy = false;
            Refresh();
            return;
        }
        Sr5VehicleWorkshopPhoneMutationResult persisted =
            await _authority.PersistPreparedAsync(snapshot, prepared);
        if (persisted.Status == Sr5VehicleWorkshopPhoneMutationStatus.Completed)
        {
            _checkpoint = pending with
            {
                Stage = Sr5VehicleWorkshopCheckpointStage.Undone,
                Draft = pending.Draft with { RouteId = Sr5VehicleWorkshopRoutes.Receipt }
            };
            _store.TryWrite(_checkpoint, out _notice);
            _busy = false;
            Refresh();
            return;
        }
        Sr5VehicleWorkshopCheckpoint unknown = pending with { BlockReason = persisted.BlockReason };
        _store.TryWrite(unknown, out _);
        await Navigation.PushAsync(new Sr5VehicleWorkshopRecoveryPage(
            Coordinator, _authority, _store, unknown));
    }

    private void AddStartAnother()
    {
        Button another = NativeTheme.SecondaryButton(T("Start another workshop purchase"));
        another.AutomationId = "sr5-vehicle-workshop-start-another";
        another.Clicked += async (_, _) =>
        {
            _store.Remove();
            await Navigation.PushAsync(new Sr5VehicleWorkshopPage(Coordinator, _authority));
        };
        _body.Add(another);
    }

    private void AddNotice(string reason, Color color)
    {
        Label label = NativeTheme.Body(reason, color);
        label.AutomationId = "sr5-vehicle-workshop-receipt-notice";
        _body.Add(NativeTheme.Card(label));
    }

    private static string T(string value) => Sr5VehicleWorkshopPage.T(value);
}

public sealed class Sr5VehicleWorkshopRecoveryPage : NativePageBase
{
    private readonly RunnerSessionSr5VehicleWorkshopAuthority _authority;
    private readonly Sr5VehicleWorkshopCheckpointStore _store;
    private readonly VerticalStackLayout _body = Sr5VehicleWorkshopPage.BodyLayout();
    private Sr5VehicleWorkshopCheckpoint _checkpoint;
    private string _notice;
    private bool _busy;

    public Sr5VehicleWorkshopRecoveryPage(
        RunnerSessionCoordinator coordinator,
        RunnerSessionSr5VehicleWorkshopAuthority authority,
        Sr5VehicleWorkshopCheckpointStore store,
        Sr5VehicleWorkshopCheckpoint checkpoint) : base(coordinator)
    {
        _authority = authority;
        _store = store;
        _checkpoint = checkpoint;
        _notice = checkpoint.BlockReason;
        Title = T("Recover workshop outcome");
        AutomationId = "sr5-vehicle-workshop-recovery-page";
        Content = new ScrollView { Content = _body };
    }

    protected override void Refresh()
    {
        _body.Clear();
        _body.Add(NativeTheme.Eyebrow(T("Outcome recovery")));
        _body.Add(NativeTheme.Title(T("Recover workshop outcome")));
        _body.Add(NativeTheme.Body(
            T("The saved command is idempotent. Recovery first checks the exact current runner, then safely completes or reports why it cannot."),
            NativeTheme.Muted));
        if (!string.IsNullOrWhiteSpace(_notice))
        {
            Label notice = NativeTheme.Body(_notice, NativeTheme.Danger);
            notice.AutomationId = "sr5-vehicle-workshop-recovery-notice";
            _body.Add(NativeTheme.Card(notice));
        }
        Button recover = NativeTheme.PrimaryButton(T("Recover outcome"));
        recover.AutomationId = "sr5-vehicle-workshop-recover";
        recover.IsEnabled = !_busy;
        recover.Clicked += async (_, _) => await RecoverAsync();
        _body.Add(recover);
    }

    private async Task RecoverAsync()
    {
        _busy = true;
        Refresh();
        Sr5VehicleWorkshopPhoneMutationResult result =
            _checkpoint.Stage == Sr5VehicleWorkshopCheckpointStage.PendingUndo
                ? await _authority.RecoverUndoAsync(_checkpoint)
                : await _authority.RecoverCommitAsync(_checkpoint);
        if (result.Status == Sr5VehicleWorkshopPhoneMutationStatus.Completed)
        {
            if (_checkpoint.Stage == Sr5VehicleWorkshopCheckpointStage.PendingUndo)
            {
                _checkpoint = _checkpoint with
                {
                    Stage = Sr5VehicleWorkshopCheckpointStage.Undone,
                    Draft = _checkpoint.Draft with { RouteId = Sr5VehicleWorkshopRoutes.Receipt },
                    BlockReason = string.Empty
                };
                _store.TryWrite(_checkpoint, out _notice);
                await Navigation.PushAsync(new Sr5VehicleWorkshopReceiptPage(
                    Coordinator, _authority, _store, _checkpoint));
                return;
            }
            if (result.Receipt is { } receipt)
            {
                _checkpoint = _checkpoint with
                {
                    Stage = Sr5VehicleWorkshopCheckpointStage.Receipt,
                    Draft = _checkpoint.Draft with { RouteId = Sr5VehicleWorkshopRoutes.Receipt },
                    Receipt = receipt,
                    BlockReason = string.Empty
                };
                if (!_store.TryWrite(_checkpoint, out _notice))
                {
                    _busy = false;
                    Refresh();
                    return;
                }
                await Navigation.PushAsync(new Sr5VehicleWorkshopReceiptPage(
                    Coordinator, _authority, _store, _checkpoint));
                return;
            }
        }
        _notice = result.BlockReason;
        _checkpoint = _checkpoint with { BlockReason = _notice };
        _store.TryWrite(_checkpoint, out _);
        _busy = false;
        Refresh();
    }

    private static string T(string value) => Sr5VehicleWorkshopPage.T(value);
}
