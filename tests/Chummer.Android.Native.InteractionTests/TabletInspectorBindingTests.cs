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
        DelayedApplyCannotMixTargetsAndControls();
        DetachedSelectionCannotRestoreAnOldEditor();
        RefreshAndDepartureInvalidateApply();
        await QueuedApplyRechecksAuthorityInsideGateAsync();
        Console.WriteLine("PASS tablet inspector action binding (managed native page/coordinator, not device persistence)");
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
        public readonly RunnerSessionCoordinator Coordinator;
        public readonly TabletBuildPage Page;

        public Fixture()
        {
            var presenter = TabletMutationProxy.Create(() => State, Requests.Add);
            Coordinator = new RunnerSessionCoordinator(presenter,
                null!, null!, null!, null!, null!, null!, StrictPageProxy.Create<IShellPresenter>(),
                null!, null!, null!, null!, null!, StrictPageProxy.Create<IAndroidAccountLinkService>(),
                null!, null!);
            Page = new(Coordinator);
            Refresh();
        }

        public InputView Notes => Elements(Page).OfType<InputView>()
            .Single(input => input.AutomationId == "tablet-field-notes");
        public Button Button(string id) => Elements(Page).OfType<Button>()
            .Single(button => button.AutomationId == id);
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

    public static ICharacterOverviewPresenter Create(Func<CharacterOverviewState> state,
        Action<WorkspaceCollectionMutationRequest> observe)
    {
        var instance = Create<ICharacterOverviewPresenter, TabletMutationProxy>();
        var proxy = (TabletMutationProxy)(object)instance;
        proxy._state = state;
        proxy._observe = observe;
        return instance;
    }

    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        string name = method?.Name ?? throw new InvalidOperationException("Missing method.");
        if (name.StartsWith("add_", StringComparison.Ordinal) || name.StartsWith("remove_", StringComparison.Ordinal))
            return null;
        if (name == "get_State") return _state();
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
