using System.Globalization;
using Chummer.Android.Native;
using Chummer.Android.Sr5AfterRunReward.Tests;
using Chummer.Contracts.Characters;
using Microsoft.Maui.Controls;

internal static partial class RewardPhoneTests
{
    internal static IEnumerable<(string Name, Func<Task> Run)> NativeViewCases =>
    [
        (nameof(NativeRewardEditorsRemainStableAndInvalidateReview), NativeRewardEditorsRemainStableAndInvalidateReview),
        (nameof(NativeRewardButtonsPreviewConfirmAndFinishThroughRealCore), NativeRewardButtonsPreviewConfirmAndFinishThroughRealCore),
        (nameof(NativeRewardDefaultZeroNeedsExplicitNoAward), NativeRewardDefaultZeroNeedsExplicitNoAward),
        (nameof(NativeRewardQueuedClickCannotBorrowNewAppearance), NativeRewardQueuedClickCannotBorrowNewAppearance),
        (nameof(NativeRewardLostAcknowledgementRecoversWithoutSecondCredit), NativeRewardLostAcknowledgementRecoversWithoutSecondCredit),
        (nameof(NativeRewardHistorySelectionLooksUpWithoutCommit), NativeRewardHistorySelectionLooksUpWithoutCommit),
        (nameof(NativeRewardChangedRunnerCannotShowCompletedState), NativeRewardChangedRunnerCannotShowCompletedState),
        (nameof(NativeRewardResourcesResolveAllSupportedLanguages), NativeRewardResourcesResolveAllSupportedLanguages),
        (nameof(NativeRewardHistoryIsPagedAndSelectionIsExplicit), NativeRewardHistoryIsPagedAndSelectionIsExplicit),
        (nameof(NativeRewardBusyControlsRejectSecondConfirmation), NativeRewardBusyControlsRejectSecondConfirmation),
        (nameof(NativeRewardReturningWhileOldActionFinishesDoesNotStayBusy), NativeRewardReturningWhileOldActionFinishesDoesNotStayBusy)
    ];

    private static async Task NativeRewardEditorsRemainStableAndInvalidateReview()
    {
        using var fixture = new RealAfterRunRewardFixture();
        using var host = new Host(fixture);
        var ui = await RewardView.Create(host);
        Entry karma = ui.Find<Entry>("karma");
        karma.Text = "0008";
        ui.Find<Editor>("reason").Text = "Quiet extraction";
        for (int i = 0; i < 20; i++) ui.View.Refresh();
        Require(ReferenceEquals(karma, ui.Find<Entry>("karma")), "Render replaced the native editor.");
        Equal("0008", karma.Text);
        Equal("0008", ui.Model.Draft.Karma);
        await ui.Click("preview");
        Require(ui.Model.CanConfirm, "Native text events did not prepare a real Core preview.");
        karma.Text = "9";
        Require(ui.Model.Review is null && !ui.Find<Button>("confirm").IsEnabled,
            "Editing kept an old confirmation enabled.");
        Equal(0, host.Service.CommitCalls);
    }

    private static async Task NativeRewardButtonsPreviewConfirmAndFinishThroughRealCore()
    {
        using var fixture = new RealAfterRunRewardFixture();
        using var host = new Host(fixture);
        var ui = await RewardView.Create(host);
        ui.Enter("8", "12500");
        await ui.Click("preview");
        Equal(DateTimeKind.Unspecified, ui.Model.Review!.Preview.Command.ExpenseDateLocal.Kind);
        Require(ui.Find<Label>("review").Text.Contains("30") && ui.Find<Label>("review").Text.Contains("38"),
            "Before/after Core balances were not displayed.");
        Equal(0, host.Service.CommitCalls);
        await ui.Click("confirm");
        Equal(1, host.Service.CommitCalls);
        Equal(38, fixture.Service.Read(fixture.WorkspaceId).Snapshot!.AvailableKarma);
        Require(ui.Find<Label>("receipt").IsVisible && ui.Find<Button>("done").IsEnabled,
            "Saved receipt did not enable completion.");
        await ui.Click("done");
        Equal(1, ui.Finishes);
        Equal(1, host.Service.CommitCalls);
    }

    private static async Task NativeRewardDefaultZeroNeedsExplicitNoAward()
    {
        using var fixture = new RealAfterRunRewardFixture();
        using var host = new Host(fixture);
        var ui = await RewardView.Create(host);
        ui.Find<Editor>("reason").Text = "No payment this time";
        await ui.Click("preview");
        Equal(Sr5AfterRunRewardPhoneStatus.InvalidInput, ui.Model.Status);
        Require(!ui.Find<Button>("confirm").IsEnabled, "Default 0/0 was silently confirmed.");
        ui.Find<CheckBox>("no-award").IsChecked = true;
        await ui.Click("preview");
        Equal(CharacterAfterRunRewardKind.NoAward, ui.Model.Review!.Preview.Kind);
        await ui.Click("confirm");
        Equal(30, fixture.Service.Read(fixture.WorkspaceId).Snapshot!.AvailableKarma);
        Equal(0, fixture.Service.Read(fixture.WorkspaceId).Snapshot!.Expenses.Count);
    }

    private static async Task NativeRewardQueuedClickCannotBorrowNewAppearance()
    {
        using var fixture = new RealAfterRunRewardFixture();
        using var host = new Host(fixture);
        var ui = await RewardView.Create(host);
        ui.Enter("8", "0");
        await ui.Click("preview");
        using var oldAppearance = new CancellationTokenSource();
        ui.View.SetLifetime(oldAppearance.Token);
        ui.Defer = true;
        ui.Send("confirm");
        oldAppearance.Cancel();
        ui.View.SetLifetime(default);
        await ui.Release();
        Equal(0, host.Service.CommitCalls);
        Require(ui.Model.CanConfirm && !ui.Model.HasRetainedIntent,
            "Canceled queued click acquired the next appearance or discarded its review.");
    }

    private static async Task NativeRewardLostAcknowledgementRecoversWithoutSecondCredit()
    {
        using var fixture = new RealAfterRunRewardFixture();
        using var host = new Host(fixture);
        var ui = await RewardView.Create(host);
        ui.Enter("8", "1000");
        await ui.Click("preview");
        Guid operation = ui.Model.OperationId;
        host.Service.AfterCommit = _ => throw new IOException("Lost response after real Commit.");
        await ui.Click("confirm");
        Require(ui.Find<Button>("recover").IsEnabled && !ui.Find<Entry>("karma").IsEnabled,
            "Uncertain reward did not retain read-only recovery.");
        await ui.Click("recover");
        Equal(operation, ui.Model.OperationId);
        Equal(1, host.Service.CommitCalls);
        Require(ui.Model.CanContinue, "Lookup did not verify the original reward.");
    }

    private static async Task NativeRewardHistorySelectionLooksUpWithoutCommit()
    {
        using var fixture = new RealAfterRunRewardFixture();
        using var host = new Host(fixture);
        var first = await RewardView.Create(host);
        first.Enter("8", "1000");
        await first.Click("preview");
        await first.Click("confirm");
        var ui = await RewardView.Create(host);
        var history = ui.Find<Picker>("history");
        Equal(1, history.ItemsSource.Count);
        Require(!ui.Find<Button>("history-resume").IsEnabled, "Appearance selected a reward implicitly.");
        history.SelectedIndex = 0;
        await ui.Click("history-resume");
        Equal(first.Model.OperationId, ui.Model.OperationId);
        Equal(1, host.Service.CommitCalls);
        Require(ui.Model.CanContinue, "Recorded reward did not resolve through Core Lookup.");
    }

    private static async Task NativeRewardChangedRunnerCannotShowCompletedState()
    {
        using var fixture = new RealAfterRunRewardFixture();
        using var host = new Host(fixture);
        var ui = await RewardView.Create(host);
        ui.Enter("8", "0");
        await ui.Click("preview");
        await ui.Click("confirm");
        host.Authority.Current = host.Authority.Current with { ActivationGeneration = 2 };
        ui.View.Refresh();
        Require(!ui.Find<Button>("done").IsEnabled && !ui.Find<Label>("receipt").IsVisible,
            "A different runner selection retained the completed receipt UI.");
        Equal(PhoneStrings.Get("AfterRunRewardRunnerChanged", "missing"), ui.Find<Label>("status").Text);
        await ui.Click("done");
        Equal(0, ui.Finishes);
    }

    private static async Task NativeRewardResourcesResolveAllSupportedLanguages()
    {
        using var fixture = new RealAfterRunRewardFixture();
        using var host = new Host(fixture);
        CultureInfo old = CultureInfo.CurrentUICulture;
        try
        {
            foreach (var (locale, label) in new[]
            {
                ("en-GB", "Review reward"), ("de-AT", "Belohnung prüfen"), ("es-ES", "Revisar recompensa")
            })
            {
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(locale);
                var ui = await RewardView.Create(host);
                Equal(label, ui.Find<Button>("preview").Text);
                Require(PhoneStrings.Get("AfterRunRewardUnknown", "MISSING") != "MISSING",
                    "Recovery copy fell back to missing resources.");
            }
        }
        finally { CultureInfo.CurrentUICulture = old; }
    }

    private static async Task NativeRewardHistoryIsPagedAndSelectionIsExplicit()
    {
        using var fixture = new RealAfterRunRewardFixture();
        using var host = new Host(fixture);
        for (int i = 0; i < 26; i++)
        {
            var model = NewPhone(host, out _);
            await model.InitializeAsync();
            Require(model.UpdateDraft(model.Draft with { Reason = $"No award {i}", NoAward = true }), "Draft unavailable.");
            await model.PreviewAsync(CultureInfo.InvariantCulture);
            await model.ConfirmAsync();
            Require(model.CanContinue, "History fixture was not genuinely committed.");
        }
        var ui = await RewardView.Create(host);
        Equal(25, ui.Find<Picker>("history").ItemsSource.Count);
        await ui.Click("history-next");
        Equal(1, ui.Find<Picker>("history").ItemsSource.Count);
        Equal(-1, ui.Find<Picker>("history").SelectedIndex);
        Require(!ui.Find<Button>("history-next").IsEnabled && ui.Find<Button>("history-previous").IsEnabled,
            "History boundaries were not enforced.");
        await ui.Click("history-previous");
        Equal(25, ui.Find<Picker>("history").ItemsSource.Count);
        Equal(26, host.Service.CommitCalls);
    }

    private static async Task NativeRewardBusyControlsRejectSecondConfirmation()
    {
        using var fixture = new RealAfterRunRewardFixture();
        using var host = new Host(fixture);
        var ui = await RewardView.Create(host);
        ui.Enter("8", "0");
        await ui.Click("preview");
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        host.Service.BeforeCommit = _ => { entered.Set(); Require(release.Wait(TimeSpan.FromSeconds(10)), "Commit release timed out."); };
        ui.Send("confirm");
        Task first = ui.Pending;
        try
        {
            Require(entered.Wait(TimeSpan.FromSeconds(10)), "Commit was not entered.");
            Require(!ui.Find<Button>("confirm").IsEnabled && !ui.Find<Entry>("karma").IsEnabled,
                "Busy controls remained enabled.");
            ui.Send("confirm");
        }
        finally { release.Set(); }
        await first;
        Equal(1, host.Service.CommitCalls);
    }

    private static async Task NativeRewardReturningWhileOldActionFinishesDoesNotStayBusy()
    {
        using var fixture = new RealAfterRunRewardFixture();
        using var host = new Host(fixture);
        var ui = await RewardView.Create(host);
        ui.Enter("8", "0");
        await ui.Click("preview");
        using var oldAppearance = new CancellationTokenSource();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        ui.View.SetLifetime(oldAppearance.Token);
        host.Service.BeforeCommit = _ => { entered.Set(); Require(release.Wait(TimeSpan.FromSeconds(10)), "Release timed out."); };
        ui.Send("confirm");
        Task pending = ui.Pending;
        try
        {
            Require(entered.Wait(TimeSpan.FromSeconds(10)), "Commit not entered.");
            oldAppearance.Cancel();
            ui.View.SetLifetime(default);
            ui.View.Refresh();
            Require(ui.Find<ActivityIndicator>("working").IsRunning, "Fixture did not reappear during the old action.");
        }
        finally { release.Set(); }
        await pending;
        Require(!ui.Find<ActivityIndicator>("working").IsRunning,
            "Returning page stayed busy after its canceled old action completed.");
        Equal(1, host.Service.CommitCalls);
    }

    private sealed class RewardView
    {
        public Sr5AfterRunRewardPhoneModel Model { get; }
        public Sr5AfterRunRewardView View { get; }
        public Task Pending { get; private set; } = Task.CompletedTask;
        public bool Defer { get; set; }
        public int Finishes { get; private set; }
        private Func<Task>? _queued;
        private TaskCompletionSource? _release;

        private RewardView(Sr5AfterRunRewardPhoneModel model)
        {
            Model = model;
            View = new(model, Run, () => { Finishes++; return Task.CompletedTask; });
        }

        public static async Task<RewardView> Create(Host host)
        {
            var model = NewPhone(host, out _);
            await model.InitializeAsync();
            return new(model);
        }

        private Task Run(Func<Task> action)
        {
            if (Defer)
            {
                _queued = action;
                _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
                return Pending = _release.Task;
            }
            return Pending = action();
        }

        public async Task Release()
        {
            await _queued!();
            _release!.SetResult();
            await Pending;
        }

        public T Find<T>(string id) where T : View
            => Descendants(View).OfType<T>().Single(item => item.AutomationId == "sr5-reward-" + id);
        public void Send(string id) => ((IButtonController)Find<Button>(id)).SendClicked();
        public async Task Click(string id) { Send(id); await Pending.WaitAsync(TimeSpan.FromSeconds(20)); }
        public void Enter(string karma, string nuyen)
        {
            Find<Entry>("karma").Text = karma;
            Find<Entry>("nuyen").Text = nuyen;
            Find<Editor>("reason").Text = "Local test reward";
        }

        private static IEnumerable<View> Descendants(View view)
        {
            yield return view;
            IEnumerable<View> children = view switch
            {
                ContentView content => content.Content is { } child ? [child] : [],
                Layout layout => layout.Children.OfType<View>(),
                _ => []
            };
            foreach (var child in children)
                foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
