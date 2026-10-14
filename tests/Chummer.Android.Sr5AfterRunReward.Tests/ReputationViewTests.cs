using System.Globalization;
using System.Resources;
using System.Text.Json;
using Chummer.Android.Native;
using Microsoft.Maui.Controls;

namespace Chummer.Android.Sr5AfterRunReward.Tests;

internal static partial class ReputationTests
{
    internal static IEnumerable<(string Name, Func<Task> Run)> ViewCases =>
    [
        ("NativeReputationActualEditorsPreviewAndConfirm", ViewConfirmation),
        ("NativeReputationActualBurnPreviewAndConfirm", ViewBurnConfirmation),
        ("NativeReputationSupersededCloseShowsClosedResult", ViewSupersededClose),
        ("NativeReputationQueuedOldAppearanceCannotConfirm", ViewOldAppearance),
        ("NativeReputationLostResponseLocksEditorsUntilLookup", ViewRecovery),
        ("NativeReputationHistoryIsPagedAndExplicit", ViewHistory),
        ("NativeReputationDEENSESResourcesAreComplete", ViewLanguages)
    ];

    private static async Task ViewConfirmation()
    {
        using var f = new Fixture();
        var ui = await ReputationView.Create(f);
        var inputs = ui.Find<VerticalStackLayout>("inputs");
        Check(inputs.IsVisible);
        Entry input = ui.Find<Entry>("street-cred");
        input.Text = "+01";
        ui.Find<Entry>("notoriety").Text = "-1";
        ui.Find<Editor>("reason").Text = "Quiet extraction";
        for (int i = 0; i < 15; i++) ui.View.Refresh();
        Check(ReferenceEquals(input, ui.Find<Entry>("street-cred")));
        Equal("+01", input.Text);
        await ui.Click("preview");
        Check(ui.Model.CanConfirm);
        Check(inputs.IsVisible);
        var text = ui.Find<Label>("review").Text;
        Check(text.Contains('6') && text.Contains('7'));
        Equal(0, f.Core.Commits);
        input.Text = "2";
        Check(!ui.Find<Button>("confirm").IsEnabled && ui.Model.Review is null);
        await ui.Click("preview");
        await ui.Click("confirm");
        Equal(1, f.Core.Commits);
        Check(ReferenceEquals(inputs, ui.Find<VerticalStackLayout>("inputs")));
        Check(ReferenceEquals(input, ui.Find<Entry>("street-cred")));
        await SavedInputsIgnoreCallbacks(f, ui);
        Check(ui.Find<Label>("receipt").Text.Contains('2'));
        await ui.Click("done");
        Equal(1, ui.Finishes);
        Equal(1, f.Core.Commits);
    }

    private static async Task ViewBurnConfirmation()
    {
        using var f = new Fixture();
        var ui = await ReputationView.Create(f);
        Check(ui.Find<VerticalStackLayout>("inputs").IsVisible);
        Check(ui.Find<Button>("burn").IsEnabled);
        ui.Find<Editor>("reason").Text = "Keep a lower profile";
        await ui.Click("burn");
        Check(ui.Model.CanConfirm && ui.Find<Button>("confirm").IsVisible);
        Check(ui.Find<VerticalStackLayout>("inputs").IsVisible);
        Equal(2, ui.Model.Review!.Preview.Quote.After.Inputs.BurntStreetCred);
        Equal(0, f.Core.Commits);
        Equal(0, f.Backend.Writes);
        await ui.Click("confirm");
        Equal(1, f.Core.Commits);
        Equal(2, ui.Model.Checkpoint!.Receipt!.Quote.After.Inputs.BurntStreetCred);
        await SavedInputsIgnoreCallbacks(f, ui);
        await ui.Click("done");
        Equal(1, ui.Finishes);
        Equal(1, f.Core.Commits);
    }

    private static void SavedControlsVisible(ReputationView ui)
    {
        Check(ui.Model.CanFinish && !ui.Find<VerticalStackLayout>("inputs").IsVisible);
        Check(ui.Find<Label>("facts").IsVisible && !string.IsNullOrEmpty(ui.Find<Label>("facts").Text));
        Check(ui.Find<Label>("receipt").IsVisible && !string.IsNullOrEmpty(ui.Find<Label>("receipt").Text));
        Check(ui.Find<Button>("done").IsVisible && ui.Find<Button>("done").IsEnabled);
    }

    private static async Task ViewSupersededClose()
    {
        using var f = new Fixture();
        var ui = await ReputationView.Create(f);
        ui.Find<Entry>("street-cred").Text = "1";
        await ui.Click("preview");
        Guid operation = ui.Model.OperationId;
        f.Core.BeforeCommit = _ => f.SavedEdit();
        await ui.Click("confirm");
        Equal(Sr5CareerReputationPhoneStatus.OutcomeUnknown, ui.Model.Status);
        Check(ui.Find<VerticalStackLayout>("inputs").IsVisible);
        Check(ui.Find<Button>("close-superseded").IsVisible && ui.Find<Button>("close-superseded").IsEnabled);
        await ui.Click("close-superseded");
        Equal(Sr5CareerReputationPhoneStatus.Superseded, ui.Model.Status);
        Equal(operation, ui.Model.OperationId);
        var checkpoint = ui.Model.Checkpoint!;
        Check(checkpoint.Receipt is null);
        SavedControlsVisible(ui);
        string closedFormat = PhoneStrings.Get("ReputationWizardClosedReceipt", string.Empty);
        Check(!string.IsNullOrEmpty(closedFormat));
        Equal(string.Format(CultureInfo.CurrentCulture, closedFormat, checkpoint.SupersededAtRevision!),
            ui.Find<Label>("receipt").Text);
        Equal(1, f.Core.Commits);
        await ui.Click("done");
        Equal(1, ui.Finishes);
        Equal(1, f.Core.Commits);
        Check(ui.Model.Checkpoint!.Receipt is null);
    }

    private static async Task SavedInputsIgnoreCallbacks(Fixture f, ReputationView ui)
    {
        SavedControlsVisible(ui);
        var draft = ui.Model.Draft;
        var checkpoint = ui.Model.Checkpoint;
        var snapshot = ui.Model.Snapshot;
        Guid operation = ui.Model.OperationId;
        int commits = f.Core.Commits, writes = f.Backend.Writes, coreCalls = f.Core.Threads.Count;
        string workspace = JsonSerializer.Serialize(f.Store.Get(f.Id).Value);
        // Exercise retained native controls even though their parent is hidden.
        ui.Find<Entry>("street-cred").Text = "999";
        ui.Find<Entry>("notoriety").Text = "999";
        ui.Find<Entry>("awareness").Text = "999";
        ui.Find<Editor>("reason").Text = "Retained editor callback";
        await ui.Click("preview");
        await ui.Click("burn");
        Equal(draft, ui.Model.Draft);
        Equal(operation, ui.Model.OperationId);
        Equal(checkpoint, ui.Model.Checkpoint);
        Equal(snapshot, ui.Model.Snapshot);
        Equal(draft.StreetCred, ui.Find<Entry>("street-cred").Text);
        Equal(draft.Notoriety, ui.Find<Entry>("notoriety").Text);
        Equal(draft.PublicAwareness, ui.Find<Entry>("awareness").Text);
        Equal(draft.Reason, ui.Find<Editor>("reason").Text);
        Equal(commits, f.Core.Commits);
        Equal(writes, f.Backend.Writes);
        Equal(coreCalls, f.Core.Threads.Count);
        Equal(workspace, JsonSerializer.Serialize(f.Store.Get(f.Id).Value));
        SavedControlsVisible(ui);
    }

    private static async Task ViewOldAppearance()
    {
        using var f = new Fixture();
        var ui = await ReputationView.Create(f);
        ui.Find<Entry>("street-cred").Text = "1";
        await ui.Click("preview");
        using var old = new CancellationTokenSource();
        ui.View.SetLifetime(old.Token);
        ui.Defer = true;
        ui.Send("confirm");
        old.Cancel();
        ui.View.SetLifetime(default);
        await ui.Release();
        Equal(0, f.Core.Commits);
        Check(ui.Model.CanConfirm && !ui.Model.HasRetainedIntent);
    }

    private static async Task ViewRecovery()
    {
        using var f = new Fixture();
        var ui = await ReputationView.Create(f);
        ui.Find<Entry>("street-cred").Text = "1";
        await ui.Click("preview");
        f.Core.LoseResponse = true;
        await ui.Click("confirm");
        Check(ui.Find<VerticalStackLayout>("inputs").IsVisible);
        Check(!ui.Find<Button>("done").IsVisible);
        Check(ui.Find<Button>("recover").IsEnabled && !ui.Find<Entry>("street-cred").IsEnabled);
        Check(!ui.Find<Button>("preview").IsEnabled && !ui.Find<Button>("burn").IsEnabled);
        Guid operation = ui.Model.OperationId;
        await ui.Click("recover");
        Equal(1, f.Core.Commits);
        Equal(operation, ui.Model.OperationId);
        Check(ui.Model.CanFinish);
        SavedControlsVisible(ui);
    }

    private static async Task ViewHistory()
    {
        using var f = new Fixture();
        for (int i = 0; i < 21; i++)
        {
            var preview = await f.Coordinator.PreviewAsync(f.Request() with { Adjustment = new(1, null, null) });
            Recorded(await f.Coordinator.ConfirmAsync(preview.Review!));
            await f.Refresh.ReloadAsync(f.Id, default);
        }
        var ui = await ReputationView.Create(f);
        Check(ui.Find<VerticalStackLayout>("inputs").IsVisible);
        Equal(20, ui.Find<Picker>("history").ItemsSource.Count);
        Equal(-1, ui.Find<Picker>("history").SelectedIndex);
        Check(!ui.Find<Button>("resume").IsEnabled);
        await ui.Click("older");
        Equal(1, ui.Find<Picker>("history").ItemsSource.Count);
        ui.Find<Picker>("history").SelectedIndex = 0;
        await ui.Click("resume");
        Equal(21, f.Core.Commits);
        Check(ui.Model.CanFinish);
        SavedControlsVisible(ui);
        Equal(2L, ui.Model.Checkpoint!.Receipt!.CommittedWorkspaceRevision);
        Equal(22L, ui.Model.Snapshot!.SavedRevision);
    }

    private static async Task ViewLanguages()
    {
        using var f = new Fixture();
        var original = CultureInfo.CurrentUICulture;
        var manager = new ResourceManager("Chummer.Android.Resources.Localization.PhoneStrings", typeof(Sr5CareerReputationView).Assembly);
        try
        {
            var keys = manager.GetResourceSet(CultureInfo.InvariantCulture, true, false)!
                .Cast<System.Collections.DictionaryEntry>().Select(pair => (string)pair.Key)
                .Where(key => key.StartsWith("ReputationWizard", StringComparison.Ordinal)).ToArray();
            Equal(39, keys.Length);
            foreach (string language in new[] { "en-GB", "de-AT", "es-MX" })
            {
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
                foreach (string key in keys) Check(!string.IsNullOrEmpty(manager.GetString(key, CultureInfo.CurrentUICulture)));
                var ui = await ReputationView.Create(f);
                Equal(manager.GetString("ReputationWizardConfirm", CultureInfo.CurrentUICulture), ui.Find<Button>("confirm").Text);
                ui.Find<Entry>("street-cred").Text = "1";
                await ui.Click("preview");
                Check(ui.Model.CanConfirm && !ui.Find<Label>("review").Text.Contains("{0}"));
            }
            foreach (string language in new[] { "de", "es" })
            {
                var satellite = manager.GetResourceSet(CultureInfo.GetCultureInfo(language), true, false)!;
                foreach (string key in keys) Check(!string.IsNullOrEmpty(satellite.GetString(key)));
            }
        }
        finally { CultureInfo.CurrentUICulture = original; }
        Equal(0, f.Core.Commits);
    }

    private sealed class ReputationView
    {
        public Sr5CareerReputationPhoneModel Model { get; }
        public Sr5CareerReputationView View { get; }
        private Task _pending = Task.CompletedTask;
        private Func<Task>? _queued;
        private TaskCompletionSource? _released;
        public bool Defer { get; set; }
        public int Finishes { get; private set; }
        private ReputationView(Sr5CareerReputationPhoneModel model)
        {
            Model = model;
            View = new(model, action =>
            {
                if (!Defer) return _pending = action();
                _queued = action;
                _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
                return _pending = _released.Task;
            }, () => { Finishes++; return Task.CompletedTask; });
        }
        public static async Task<ReputationView> Create(Fixture fixture)
        { var model = fixture.Phone(); await model.InitializeAsync(); return new(model); }
        public T Find<T>(string id) where T : View
            => Descendants(View).OfType<T>().Single(control => control.AutomationId == "sr5-reputation-" + id);
        public void Send(string id) => ((IButtonController)Find<Button>(id)).SendClicked();
        public async Task Click(string id) { Send(id); await _pending.WaitAsync(TimeSpan.FromSeconds(20)); }
        public async Task Release() { await _queued!(); _released!.SetResult(); await _pending; }
        private static IEnumerable<Element> Descendants(Element element)
        {
            yield return element;
            if (element is IElementController controller)
                foreach (var child in controller.LogicalChildren)
                    foreach (var nested in Descendants(child)) yield return nested;
        }
    }
}
