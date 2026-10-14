using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;
using Chummer.Android.Native;
using Chummer.Contracts.Owners;
using Chummer.Infrastructure.Workspaces;
using Microsoft.Maui.Controls;

internal static partial class AfterRunAuthorityHarness
{
    internal static async Task RunCareerExpenseFeedbackAsync(string contentRoot, string fixture)
    {
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(fixture));
        string xml = json.RootElement.GetProperty("Envelope").GetProperty("Payload").GetString()!;
        using var ui = new IssuedPageUiContext();
        await ui.RunAsync(async () =>
        {
            foreach (string currency in new[] { "karma", "nuyen" })
            foreach (string scenario in new[] { "saved", "departure", "shell-failure", "owner-aba-before", "owner-aba-during" })
            {
                NativePageBase? page = null;
                bool observing = false;
                int pendingObservations = 0;
                int shellListsAfterSave = 0;
                Button? gain = null;
                ActivityIndicator? progress = null;
                var owners = new ControlledLinkedOwner();
                void ObserveSave()
                {
                    if (!observing || pendingObservations != 0) return;
                    void Observe()
                    {
                        pendingObservations++;
                        Require(progress is { IsRunning: true, IsVisible: true },
                            "Career expense edit has no visible pending feedback.");
                        Require(MinimalVisible(page!).OfType<Button>().All(x => !x.IsEnabled)
                            && MinimalVisible(page!).OfType<Entry>().All(x => !x.IsEnabled)
                            && MinimalVisible(page!).OfType<Editor>().All(x => !x.IsEnabled),
                            "Pending expense edit allows editing or another submission.");
                        ((IButtonController)gain!).SendClicked();
                        if (scenario == "departure") IssuedPageLifecycle(page!, "OnDisappearing");
                        if (scenario == "owner-aba-during")
                        {
                            OwnerScope original = owners.Current;
                            owners.Set(new OwnerScope("career-save-other"));
                            owners.Set(original);
                        }
                    }
                    if (ReferenceEquals(SynchronizationContext.Current, ui)) Observe();
                    else
                    {
                        var checkedUi = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                        ui.Post(_ =>
                        {
                            try { Observe(); checkedUi.SetResult(); }
                            catch (Exception error) { checkedUi.SetException(error); }
                        }, null);
                        checkedUi.Task.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
                    }
                }
                await using var runtime = new NativeRewardRuntime(contentRoot,
                    linkedOwners: owners, productionCreationOverview: true,
                    beforeShellWorkspaceList: () =>
                    {
                        if (!observing) return;
                        shellListsAfterSave++;
                        if (scenario == "shell-failure") throw new IOException("Synthetic expense edit shell failure.");
                    });
                await runtime.Coordinator.InitializeAsync();
                await AccountStartupTask(runtime.Coordinator).WaitAsync(TimeSpan.FromSeconds(10));
                await runtime.LoadRunnerAsync(xml);
                var c = runtime.Coordinator;
                var store = new FileWorkspaceStore(runtime.StateDirectory);
                var before = store.Get(runtime.Id).Value!;
                runtime.Presenter.StateChanged += (_, _) =>
                {
                    // Observe actual persistence, not the redundant shell refresh
                    // this regression removes. No fake mutation or save result.
                    if (runtime.Presenter.State.IsBusy && runtime.Presenter.State.IsDirty)
                        ObserveSave();
                };
                if (scenario == "shell-failure")
                {
                    // An incomplete shell must keep the full refresh/failure path.
                    typeof(Chummer.Presentation.Shell.ShellPresenter).GetProperty("State")!
                        .SetValue(runtime.Shell, runtime.Shell.State with { Commands = [] });
                }
                var karmaEditor = currency == "karma" ? await c.PrepareCareerKarmaExpenseEditAsync() : null;
                var nuyenEditor = currency == "nuyen" ? await c.PrepareCareerNuyenExpenseEditAsync() : null;
                page = currency == "karma"
                    ? new CareerKarmaExpensePage(c, karmaEditor!)
                    : new CareerNuyenExpensePage(c, nuyenEditor!);
                int editableIndex = currency == "karma"
                    ? karmaEditor!.Expenses.ToList().FindIndex(x => x.AmountEditable)
                    : nuyenEditor!.Expenses.ToList().FindIndex(x => x.AmountEditable);
                Require(editableIndex >= 0, "Fixture needs a real editable saved expense.");
                string prefix = "career-" + currency + "-expense";
                var root = new ContentPage();
                var navigation = new NavigationPage(root);
                await navigation.PushAsync(page, false);
                var window = new Window(navigation);
                using var alerts = new IssuedPageAlerts(page, window);
                await alerts.PreflightAsync();
                await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => IssuedPageLifecycle(page, "OnAppearing")));
                var body = (VerticalStackLayout)((ScrollView)page.Content!).Content!;
                var options = body.Children.OfType<VerticalStackLayout>().Single(x => x.AutomationId == prefix + "-options");
                var toggle = body.Children.OfType<Button>().Single(x => x.AutomationId == prefix + "-options-toggle");
                gain = body.Children.OfType<Button>().Single(x => x.AutomationId == prefix + "-save");
                progress = body.Children.OfType<ActivityIndicator>().Single(x => x.AutomationId == prefix + "-save-progress");
                Require(!options.IsVisible && body.Children.IndexOf(gain) < body.Children.IndexOf(toggle),
                    "Optional bookkeeping fields hide the main Career action.");
                ((IButtonController)toggle).SendClicked();
                Require(options.IsVisible, "Optional date/exchange settings became inaccessible.");
                ((IButtonController)toggle).SendClicked();
                Require(!options.IsVisible, "Bookkeeping options did not collapse.");
                RequireSameRewardDocument(before, store.Get(runtime.Id).Value!);
                var picker = body.Children.OfType<Picker>().Single(x => x.AutomationId == prefix + "-picker");
                var ids = currency == "karma"
                    ? karmaEditor!.Expenses.Select(x => x.ExpenseId.ToString("D")).ToArray()
                    : nuyenEditor!.Expenses.Select(x => x.ExpenseId.ToString("D")).ToArray();
                Require(picker.ItemsSource.Cast<string>().All(label => ids.All(id => !label.Contains(id)))
                    && MinimalVisible(page).OfType<Label>().All(label => ids.All(id => !(label.Text ?? "").Contains(id))),
                    "Technical expense identities remain in the ordinary editor.");
                picker.SelectedIndex = editableIndex;
                var amount = body.Children.OfType<Entry>().Single(x => x.AutomationId == prefix + "-amount");
                decimal oldAmount = decimal.Parse(amount.Text, System.Globalization.CultureInfo.InvariantCulture);
                amount.Text = "invalid";
                Require(!gain.IsEnabled && body.Children.OfType<Label>()
                    .Single(x => x.AutomationId == prefix + "-save-status").IsVisible,
                    "Invalid amount silently disables Save.");
                amount.Text = (oldAmount + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
                body.Children.OfType<Editor>().Single(x => x.AutomationId == prefix + "-reason").Text = "Edited synthetic expense";
                Require(gain.IsEnabled, "Actual saved runner cannot record a saved expense edit.");
                if (scenario == "owner-aba-before")
                {
                    OwnerScope original = owners.Current;
                    owners.Set(new OwnerScope("expense-editor-other"));
                    owners.Set(original);
                    page.GetType().GetMethod("Refresh", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, null);
                    Require(!gain.IsEnabled && !toggle.IsEnabled
                        && body.Children.OfType<Label>().Single(x => x.AutomationId == prefix + "-save-status").IsVisible,
                        "Owner ABA revived a stale expense edit or left it silently disabled.");
                    ((IButtonController)gain).SendClicked();
                    await ui.DrainDispatchedAsyncVoidAsync();
                    RequireSameRewardDocument(before, store.Get(runtime.Id).Value!);
                    Require(navigation.Navigation.NavigationStack.Count == 2 && !progress.IsRunning,
                        "Stale owner action started work or navigated away.");
                    IssuedPageLifecycle(page, "OnDisappearing");
                    Console.WriteLine($"PASS Career {currency}/owner-aba-before: stale action rejected, guidance visible, saved bytes unchanged.");
                    continue;
                }
                observing = true;
                await JoinIssuedPageAsync(ui.BeginAsyncVoid(() => ((IButtonController)gain).SendClicked()));
                observing = false;
                if (scenario is "saved" or "departure")
                    Require(shellListsAfterSave == 0,
                        "An exact durable economy save unnecessarily repeated shell roster/inbound synchronization.");
                if (scenario == "shell-failure")
                    Require(shellListsAfterSave > 0, "An incomplete shell skipped its required refresh.");
                Require(pendingObservations > 0 && !progress.IsRunning && !progress.IsVisible,
                    "Booking did not show bounded save feedback or left its spinner active.");
                Require(navigation.Navigation.NavigationStack.Count == (scenario == "saved" ? 1 : 2),
                    "Booking failed to continue, or a late completion navigated a departed/failed page.");
                var after = store.Get(runtime.Id).Value!;
                ((IButtonController)gain).SendClicked();
                await ui.DrainDispatchedAsyncVoidAsync();
                RequireSameRewardDocument(after, store.Get(runtime.Id).Value!);
                if (scenario is not ("shell-failure" or "owner-aba-during"))
                {
                    var oldXml = XElement.Parse(before.Document.Content);
                    var savedXml = XElement.Parse(after.Document.Content);
                    Require(after.ContentRevision == before.ContentRevision + 1
                        && after.SavedRevision == after.ContentRevision
                        && decimal.Parse(savedXml.Element(currency)!.Value, System.Globalization.CultureInfo.InvariantCulture)
                            == decimal.Parse(oldXml.Element(currency)!.Value, System.Globalization.CultureInfo.InvariantCulture) + 1
                        && savedXml.Element("expenses")!.Elements("expense").Count()
                            == oldXml.Element("expenses")!.Elements("expense").Count()
                        && savedXml.Element("expenses")!.Elements("expense").Count(x => x.Element("reason")?.Value == "Edited synthetic expense") == 1
                        && XNode.DeepEquals(oldXml.Element("improvements"), savedXml.Element("improvements")),
                        "Expense edit changed the wrong balance, grants, expense count or durable revision.");
                    await runtime.Presenter.LoadAsync(runtime.Id, default);
                    RequireSameRewardDocument(after, store.Get(runtime.Id).Value!);
                }
                Require(!gain.IsEnabled, "Retained completed/uncertain expense edit can be submitted again.");
                IssuedPageLifecycle(page, "OnDisappearing");
                Console.WriteLine($"PASS Career {currency}/{scenario}: actual Core/native handler, visible saving, frozen inputs, one submission, safe return and retained saved state.");
            }
        });
    }
}
