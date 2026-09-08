using System.Reflection;
using Chummer.Android.Native;
using Chummer.Android.Platform;
using Chummer.Presentation.Overview;
using Chummer.Presentation.Shell;
using Microsoft.Maui.Controls;

internal static class TabletInspectorBindingTests
{
    private const string ItemAId = "11111111-1111-4111-8111-111111111111";
    private const string ItemBId = "22222222-2222-4222-8222-222222222222";
    public static async Task RunAsync()
    {
        DelayedDamageActionCannotMixTracksAndPickers();
        DetachedMoveCannotReorderThePreviousItem();
        DelayedApplyCannotMixTargetsAndControls();
        DetachedSelectionCannotRestoreAnOldEditor();
        RefreshAndDepartureInvalidateApply();
        await QueuedApplyRechecksAuthorityInsideGateAsync();
        await DamageWaitRechecksTrackAndWorkspaceAsync();
        await DeleteConfirmationStaysBoundToTheReviewedItemAsync();
        await DeleteWaitRechecksAuthorityAfterConfirmationAsync();
        NestedChooserKeepsTheCurrentParent();
        Console.WriteLine("PASS tablet inspector action binding (managed native page/coordinator, not device persistence)");
    }

    private static void DelayedDamageActionCannotMixTracksAndPickers()
    {
        using var fixture = new Fixture(condition: true);
        Button oldApply = fixture.Button("tablet-condition-save-physical");
        Button oldClear = fixture.Button("tablet-condition-clear-physical");
        fixture.Click("tablet-condition-track-stun");
        fixture.Picker("tablet-condition-filled-stun").SelectedIndex = 6;
        ((IButtonController)oldApply).SendClicked();
        ((IButtonController)oldClear).SendClicked();
        Require(fixture.ConditionRequests.Count == 0,
            "Detached Physical action forwarded a mutation after selecting Stun: "
            + string.Join(", ", fixture.ConditionRequests.Select(request => $"{request.Track}:{request.Filled}")));
        Require(fixture.Picker("tablet-condition-filled-stun").SelectedIndex == 6,
            "Rejected damage action discarded the current Stun draft.");
        fixture.Click("tablet-condition-save-stun");
        Require(fixture.ConditionRequests.SequenceEqual([new(WorkspaceConditionMonitorTrack.Stun, 6)]),
            "Current Stun Apply did not forward exactly its selected value.");
        fixture.Click("tablet-condition-clear-stun");
        Require(fixture.ConditionRequests.SequenceEqual([
            new(WorkspaceConditionMonitorTrack.Stun, 6), new(WorkspaceConditionMonitorTrack.Stun, 0)]),
            "Current Clear did not preserve the Stun typed identity.");
        fixture.Picker("tablet-condition-filled-stun").SelectedIndex = -1;
        fixture.Click("tablet-condition-save-stun");
        Require(fixture.ConditionRequests.Count == 2, "Missing picker choice was converted into a mutation.");
    }

    private static void DetachedMoveCannotReorderThePreviousItem()
    {
        using var fixture = new Fixture(mutable: true);
        Button oldDown = fixture.Button("tablet-inspector-move-down");
        fixture.Click($"tablet-collection-item-{ItemBId}");
        ((IButtonController)oldDown).SendClicked();
        Require(fixture.Requests.Count == 0, "Detached Move reordered the previously selected item.");
        fixture.Click("tablet-inspector-move-up");
        Require(fixture.Requests.Count == 1
            && fixture.Requests[0] is WorkspaceMoveCollectionItemRequest { Target.ItemId: ItemBId, TargetIndex: 0 },
            "Current Move did not forward B's exact typed order request.");
    }

    private static async Task DamageWaitRechecksTrackAndWorkspaceAsync()
    {
        foreach (string change in new[] { "workspace", "revision", "track", "refresh", "departure", "unchanged" })
        {
            using var fixture = new Fixture(condition: true);
            SemaphoreSlim gate = fixture.ActivationGate;
            Require(gate.Wait(0), "Could not reserve workspace gate.");
            Task<bool> action;
            try
            {
                action = (Task<bool>)typeof(TabletBuildPage).GetMethod("ApplyConditionInspectorAsync",
                    BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(fixture.Page,
                    [new ConditionMonitorEditRequest(WorkspaceConditionMonitorTrack.Physical, 7),
                        fixture.State, fixture.Generation])!;
                Require(!action.IsCompleted && fixture.ConditionRequests.Count == 0,
                    "Damage action did not wait for actual activation.");
                switch (change)
                {
                    case "workspace": fixture.State = fixture.State with { WorkspaceId = new("other-runner") }; break;
                    case "revision": fixture.AdvanceRevision(); break;
                    case "track": fixture.Click("tablet-condition-track-stun"); break;
                    case "refresh": fixture.Refresh(); break;
                    case "departure": fixture.Depart(); break;
                }
            }
            finally { gate.Release(); }
            if (change == "unchanged")
            {
                bool observed = false;
                try { await action; } catch (OperationCanceledException) { observed = true; }
                Require(observed && fixture.ConditionRequests.SequenceEqual([new(WorkspaceConditionMonitorTrack.Physical, 7)]),
                    "Unchanged queued damage action did not forward the captured value exactly once.");
            }
            else Require(!await action && fixture.ConditionRequests.Count == 0,
                $"Damage applied after {change} changed behind the gate.");
        }
    }

    private static async Task DeleteConfirmationStaysBoundToTheReviewedItemAsync()
    {
        foreach (string change in new[] { "declined", "selection", "workspace", "revision", "departure", "unchanged" })
        {
            using var fixture = new Fixture(mutable: true);
            var answer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            fixture.Confirmation = answer.Task;
            Button oldDelete = fixture.Button("tablet-inspector-delete");
            Task action = fixture.DeleteAsync();
            Require(!action.IsCompleted && fixture.DialogCalls == 1
                && fixture.Prompt.Contains(ItemAId, StringComparison.Ordinal),
                "Delete did not wait for confirmation of the exact selected item.");
            await fixture.DeleteAsync();
            Require(fixture.DialogCalls == 1, "Overlapping Delete opened a second confirmation.");
            switch (change)
            {
                case "selection": fixture.Click($"tablet-collection-item-{ItemBId}"); break;
                case "workspace": fixture.State = fixture.State with { WorkspaceId = new("other-runner") }; break;
                case "revision": fixture.AdvanceRevision(); break;
                case "departure": fixture.Depart(); break;
            }
            answer.SetResult(change != "declined");
            await action;
            if (change == "unchanged")
                Require(fixture.Requests.Count == 1 && fixture.Requests[0] is WorkspaceDeleteCollectionItemRequest
                    { Target.ItemId: ItemAId }, "Confirmed current Delete did not preserve A's typed identity.");
            else
                Require(fixture.Requests.Count == 0, $"Delete mutated after {change} while awaiting confirmation.");
            if (change == "selection")
            {
                int dialogs = fixture.DialogCalls;
                ((IButtonController)oldDelete).SendClicked();
                Require(fixture.DialogCalls == dialogs, "Detached Delete opened a fresh dialog for A.");
                fixture.Confirmation = Task.FromResult(true);
                fixture.Click("tablet-inspector-delete");
                Require(fixture.Requests.Count == 1 && fixture.Requests[0].Target.ItemId == ItemBId,
                    "Current B Delete did not remain usable after rejecting A's confirmation.");
            }
        }
    }

    private static async Task DeleteWaitRechecksAuthorityAfterConfirmationAsync()
    {
        foreach (string change in new[] { "selection", "workspace", "revision", "departure", "unchanged" })
        {
            using var fixture = new Fixture(mutable: true);
            fixture.Confirmation = Task.FromResult(true);
            SemaphoreSlim gate = fixture.ActivationGate;
            Require(gate.Wait(0), "Could not reserve the Delete activation gate.");
            Task action;
            try
            {
                action = fixture.DeleteAsync();
                Require(fixture.DialogCalls == 1 && !action.IsCompleted && fixture.Requests.Count == 0,
                    "Confirmed Delete did not wait for the actual activation gate.");
                await fixture.DeleteAsync();
                Require(fixture.DialogCalls == 1, "Queued confirmed Delete admitted a second dialog.");
                switch (change)
                {
                    case "selection": fixture.Click($"tablet-collection-item-{ItemBId}"); break;
                    case "workspace": fixture.State = fixture.State with { WorkspaceId = new("other-runner") }; break;
                    case "revision": fixture.AdvanceRevision(); break;
                    case "departure": fixture.Depart(); break;
                }
            }
            finally { gate.Release(); }
            await action;
            if (change == "unchanged")
                Require(fixture.Requests.Count == 1 && fixture.Requests[0] is WorkspaceDeleteCollectionItemRequest
                    { Target.ItemId: ItemAId }, "Unchanged confirmed Delete did not forward A exactly once after gate release.");
            else
                Require(fixture.Requests.Count == 0,
                    $"Delete mutated after {change} changed between confirmation and activation.");
        }
    }

    private static void NestedChooserKeepsTheCurrentParent()
    {
        using var fixture = new Fixture(nested: true);
        var navigation = new NavigationPage(fixture.Page);
        Button oldAdd = fixture.Button("tablet-inspector-add-gear");
        fixture.Click($"tablet-collection-item-{ItemBId}");
        ((IButtonController)oldAdd).SendClicked();
        Require(navigation.Navigation.NavigationStack.Count == 1 && navigation.CurrentPage == fixture.Page,
            "Detached A Add opened a chooser after selecting B.");

        fixture.Click("tablet-inspector-add-gear");
        // The real managed stack changes synchronously. With no Android handler,
        // the later Pushed animation event is not native-navigation completion proof.
        Page destination = navigation.CurrentPage;
        Require(navigation.Navigation.NavigationStack.Count == 2
            && ReferenceEquals(navigation.CurrentPage, destination)
            && destination is NestedCollectionAddPage && destination.AutomationId == "nested-add-gear",
            "Current B Add did not open exactly the real nested Gear chooser.");
        var parent = (WorkspaceCollectionItemTarget)typeof(NestedCollectionAddPage)
            .GetField("_parent", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(destination)!;
        var kind = (WorkspaceNestedCollectionKind)typeof(NestedCollectionAddPage)
            .GetField("_nestedKind", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(destination)!;
        Require(parent == fixture.State.ActiveCollectionEditor!.Items[1].Target
            && kind == WorkspaceNestedCollectionKind.Gear,
            "Nested chooser did not retain B's exact parent identity and nested kind.");
        Require(fixture.Requests.Count == 0, "Opening the nested chooser mutated the runner.");
    }

    private static void DelayedApplyCannotMixTargetsAndControls()
    {
        using var fixture = new Fixture();
        Button oldApply = fixture.Button("tablet-inspector-save");
        fixture.Click($"tablet-collection-item-{ItemBId}");
        fixture.Notes.Text = "B's unsaved notes";
        ((IButtonController)oldApply).SendClicked();
        Require(fixture.Requests.Count == 0,
            "Detached A Apply submitted a mutation after selecting B: "
            + string.Join(", ", fixture.Requests.Select(request => request.Target.ItemId)));
        Require(fixture.Notes.Text == "B's unsaved notes", "Rejected A Apply lost B's draft.");

        fixture.Click("tablet-inspector-save");
        Require(fixture.Requests.Count == 1
            && fixture.Requests[0] is WorkspacePatchCollectionItemRequest
            {
                Target.ItemId: ItemBId, TextValues: { } values
            }
            && values[WorkspaceCollectionTextField.Notes] == "B's unsaved notes",
            "Current B Apply did not forward exactly B's typed patch.");
    }

    private static void DetachedSelectionCannotRestoreAnOldEditor()
    {
        using var fixture = new Fixture();
        Button oldB = fixture.Button($"tablet-collection-item-{ItemBId}");
        fixture.State = fixture.State with { ActiveCollectionEditor = Editor("new A", "new B") };
        fixture.Refresh();
        fixture.Notes.Text = "retained A draft";
        ((IButtonController)oldB).SendClicked();
        Require(fixture.Notes.Text == "retained A draft",
            "Detached selection restored an obsolete editor and lost the current draft.");
        fixture.Click($"tablet-collection-item-{ItemBId}");
        Require(fixture.Notes.Text == "new B", "Current selection did not use the refreshed editor.");
        Require(fixture.Requests.Count == 0, "Selection mutated the runner.");
    }

    private static void RefreshAndDepartureInvalidateApply()
    {
        foreach (bool departure in new[] { false, true })
        {
            using var fixture = new Fixture();
            Button oldApply = fixture.Button("tablet-inspector-save");
            fixture.Notes.Text = "must not be written";
            if (departure) fixture.Depart();
            else fixture.Refresh();
            ((IButtonController)oldApply).SendClicked();
            Require(fixture.Requests.Count == 0, "Old Apply survived same-target refresh or departure.");
            if (!departure)
            {
                fixture.Notes.Text = "current A draft";
                fixture.Click("tablet-inspector-save");
                Require(fixture.Requests.Count == 1 && fixture.Requests[0].Target.ItemId == ItemAId,
                    "Refresh disabled the replacement Apply control.");
            }
        }
    }

    private static async Task QueuedApplyRechecksAuthorityInsideGateAsync()
    {
        foreach (string change in new[]
        {
            "workspace", "revision", "saved-revision", "section", "editor", "selection",
            "refresh", "departure", "busy", "error", "disposed", "unchanged"
        })
        {
            using var fixture = new Fixture();
            fixture.Notes.Text = "queued A draft";
            var gate = (SemaphoreSlim)typeof(RunnerSessionCoordinator).GetField("_workspaceActivationGate",
                BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(fixture.Coordinator)!;
            Require(gate.Wait(0), "Could not reserve the real workspace activation gate.");
            Task<bool> action;
            try
            {
                long generation = (long)typeof(TabletBuildPage).GetField("_inspectorGeneration",
                    BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(fixture.Page)!;
                action = (Task<bool>)typeof(TabletBuildPage).GetMethod("SaveInspectorAsync",
                    BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(fixture.Page,
                        [fixture.State.ActiveCollectionEditor!.Items[0], fixture.State, generation])!;
                Require(!action.IsCompleted && fixture.Requests.Count == 0,
                    "Apply did not wait for the actual activation gate.");
                var workspace = fixture.State.ActiveWorkspace!;
                switch (change)
                {
                    case "workspace": fixture.State = fixture.State with { WorkspaceId = new("other-runner") }; break;
                    case "revision":
                    case "saved-revision":
                        var changed = workspace with
                        {
                            ContentRevision = change == "revision" ? 6 : workspace.ContentRevision,
                            SavedRevision = change == "saved-revision" ? 4 : workspace.SavedRevision
                        };
                        fixture.State = fixture.State with
                        {
                            OpenWorkspaces = [changed],
                            Session = new(changed.Id, [changed], [changed.Id])
                        };
                        break;
                    case "section": fixture.State = fixture.State with { ActiveSectionId = "weapons" }; break;
                    case "editor": fixture.State = fixture.State with { ActiveCollectionEditor = Editor("new A", "new B") }; break;
                    case "selection": fixture.Click($"tablet-collection-item-{ItemBId}"); break;
                    case "refresh": fixture.Refresh(); break;
                    case "departure": fixture.Depart(); break;
                    case "busy": fixture.State = fixture.State with { IsBusy = true }; break;
                    case "error": fixture.State = fixture.State with { Error = "Source unavailable" }; break;
                    case "disposed": fixture.Coordinator.Dispose(); break;
                }
            }
            finally { gate.Release(); }
            if (change == "unchanged")
            {
                bool boundaryCancellation = false;
                try { await action; }
                catch (OperationCanceledException) { boundaryCancellation = true; }
                Require(boundaryCancellation && fixture.Requests.Count == 1
                    && fixture.Requests[0] is WorkspacePatchCollectionItemRequest
                    { Target.ItemId: ItemAId, TextValues: { } values }
                    && values[WorkspaceCollectionTextField.Notes] == "queued A draft",
                    "Unchanged queued Apply did not forward exactly its captured typed patch.");
            }
            else
                Require(!await action && fixture.Requests.Count == 0,
                    $"Queued Apply forwarded an obsolete request after {change} changed.");
        }
    }

    private static WorkspaceCollectionEditorState Editor(string a, string b)
        => new("gear", WorkspaceCollectionKind.Gear, null,
        [Item(ItemAId, 0, a), Item(ItemBId, 1, b)]);

    private static WorkspaceCollectionItemEditorState Item(string id, int index, string notes)
        => new(new(WorkspaceCollectionKind.Gear, id), index, id,
            [new(WorkspaceCollectionTextField.Notes, notes)], null, null, [], [],
            CanDelete: false, CanMove: false);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class Fixture : IDisposable
    {
        public CharacterOverviewState State = Program.NewCreationOverview(new("tablet-runner"), 5, 5) with
        {
            ActiveSectionId = "gear",
            ActiveCollectionEditor = Editor("saved A", "saved B")
        };
        public readonly List<WorkspaceCollectionMutationRequest> Requests = [];
        public readonly List<ConditionMonitorEditRequest> ConditionRequests = [];
        public readonly RunnerSessionCoordinator Coordinator;
        public readonly TabletBuildPage Page;
        public Task<bool> Confirmation = Task.FromResult(false);
        public int DialogCalls;
        public string Prompt = string.Empty;

        public Fixture(bool condition = false, bool mutable = false, bool nested = false)
        {
            if (condition)
                State = State with
                {
                    Profile = State.Profile! with { Created = true },
                    ActiveSectionId = "conditionmonitor", ActiveCollectionEditor = null,
                    ActiveConditionMonitor = new(true, [
                        new(WorkspaceConditionMonitorTrack.Physical, "Physical", 2, 10, 0, 10, 0, "", false),
                        new(WorkspaceConditionMonitorTrack.Stun, "Stun", 3, 10, 0, 10, 0, "", false)])
                };
            if (mutable)
                State = State with { ActiveCollectionEditor = State.ActiveCollectionEditor! with
                {
                    Items = State.ActiveCollectionEditor!.Items.Select(item => item with
                    { CanMove = true, CanDelete = true }).ToArray()
                }};
            if (nested)
                State = State with { ActiveCollectionEditor = State.ActiveCollectionEditor! with
                {
                    Items = State.ActiveCollectionEditor!.Items.Select(item => item with
                    { AddableNestedKinds = [WorkspaceNestedCollectionKind.Gear] }).ToArray()
                }};
            var presenter = TabletMutationProxy.Create(() => State, Requests.Add, ConditionRequests.Add);
            Coordinator = new RunnerSessionCoordinator(presenter,
                null!, null!, null!, null!, null!, null!, StrictPageProxy.Create<IShellPresenter>(),
                null!, null!, null!, null!, null!, StrictPageProxy.Create<IAndroidAccountLinkService>(),
                null!, null!);
            Page = new(Coordinator, (_, message, _, _) =>
            {
                DialogCalls++;
                Prompt = message;
                return Confirmation;
            });
            Refresh();
        }

        public InputView Notes => Elements(Page).OfType<InputView>()
            .Single(input => input.AutomationId == "tablet-field-notes");
        public Button Button(string id) => Elements(Page).OfType<Button>()
            .Single(button => button.AutomationId == id);
        public Picker Picker(string id) => Elements(Page).OfType<Picker>()
            .Single(picker => picker.AutomationId == id);
        public long Generation => (long)typeof(TabletBuildPage).GetField("_inspectorGeneration",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Page)!;
        public SemaphoreSlim ActivationGate => (SemaphoreSlim)typeof(RunnerSessionCoordinator)
            .GetField("_workspaceActivationGate", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Coordinator)!;
        public Task DeleteAsync() => (Task)typeof(TabletBuildPage).GetMethod("DeleteInspectorItemAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(Page,
                [State.ActiveCollectionEditor!.Items[0], State, Generation])!;
        public void AdvanceRevision()
        {
            var changed = State.ActiveWorkspace! with { ContentRevision = State.ContentRevision + 1 };
            State = State with { OpenWorkspaces = [changed], Session = new(changed.Id, [changed], [changed.Id]) };
        }
        public void Click(string id) => ((IButtonController)Button(id)).SendClicked();
        public void Refresh() => typeof(TabletBuildPage).GetMethod("Refresh",
            BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(Page, null);
        public void Depart() => typeof(TabletBuildPage).GetMethod("OnDisappearing",
            BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(Page, null);
        public void Dispose() => Coordinator.Dispose();
    }

    private static IEnumerable<Element> Elements(Element root)
    {
        yield return root;
        IEnumerable<Element> children = root switch
        {
            ContentPage page when page.Content is not null => [page.Content],
            ScrollView scroll when scroll.Content is not null => [scroll.Content],
            Border border when border.Content is not null => [border.Content],
            Layout layout => layout.Children.OfType<Element>(),
            _ => []
        };
        foreach (Element child in children)
        foreach (Element descendant in Elements(child)) yield return descendant;
    }
}

public class TabletMutationProxy : DispatchProxy
{
    private Func<CharacterOverviewState> _state = null!;
    private Action<WorkspaceCollectionMutationRequest> _observe = null!;
    private Action<ConditionMonitorEditRequest> _condition = null!;

    public static ICharacterOverviewPresenter Create(Func<CharacterOverviewState> state,
        Action<WorkspaceCollectionMutationRequest> observe,
        Action<ConditionMonitorEditRequest> condition)
    {
        var instance = Create<ICharacterOverviewPresenter, TabletMutationProxy>();
        var proxy = (TabletMutationProxy)(object)instance;
        proxy._state = state;
        proxy._observe = observe;
        proxy._condition = condition;
        return instance;
    }

    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        string name = method?.Name ?? throw new InvalidOperationException("Missing method.");
        if (name.StartsWith("add_", StringComparison.Ordinal) || name.StartsWith("remove_", StringComparison.Ordinal))
            return null;
        if (name == "get_State") return _state();
        if (name == "ApplyConditionMonitorEditAsync")
        {
            _condition((ConditionMonitorEditRequest)args![0]!);
            return Task.FromCanceled(new CancellationToken(canceled: true));
        }
        if (name == "ApplyCollectionMutationAsync")
        {
            _observe((WorkspaceCollectionMutationRequest)args![0]!);
            // Observe the real coordinator's typed boundary; do not fabricate Core writes,
            // receipts, or persistence. Cancellation prevents unrelated shell side effects.
            return Task.FromCanceled(new CancellationToken(canceled: true));
        }
        throw new InvalidOperationException($"Unexpected tablet dependency: {name}");
    }
}
