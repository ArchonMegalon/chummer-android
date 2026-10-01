using Chummer.Contracts.Characters;

namespace Chummer.Android.Native;

public sealed class CreationMagicReReviewPage : NativePageBase
{
    private readonly CharacterCreationMagicResonanceReReviewState _state;
    private readonly VerticalStackLayout _body = new() { Padding = new Thickness(20), Spacing = 12 };
    private CreationMagicReReviewConfirmResult? _confirmation;
    private bool _attached;
    private bool _saving;
    private long _generation;

    internal CreationMagicReReviewPage(RunnerSessionCoordinator coordinator,
        CharacterCreationMagicResonanceReReviewState state) : base(coordinator)
    {
        _state = state;
        Title = Text("Title", "Review saved Magic choices");
        AutomationId = "creation-magic-rereview-page";
        Content = new ScrollView { Content = _body };
    }

    protected override void OnAppearing() { _attached = true; base.OnAppearing(); }
    protected override void OnDisappearing()
    {
        _attached = false;
        Interlocked.Increment(ref _generation);
        base.OnDisappearing();
    }

    protected override void Refresh()
    {
        // Preserve saving feedback only while the original account/runner is
        // still displayed. A switch must hide its private choices immediately.
        if (_saving && Coordinator.CanDisplayCreationMagicReReview(_state)) return;
        _body.Clear();
        _body.Add(NativeTheme.Title(Title));
        if (_confirmation?.Receipt is { } receipt && Coordinator.CanDisplayCreationMagicReReviewReceipt(receipt))
        {
            var saved = NativeTheme.Body(_confirmation.Blockers.Count == 0
                ? Text("Saved", "Review saved. Your choices and earlier receipts are preserved.")
                : Text("SavedRefresh", "Saved with a receipt. Reopen the runner to refresh; do not apply it again."));
            saved.AutomationId = "creation-magic-rereview-saved";
            _body.Add(saved);
            var identity = NativeTheme.Body(receipt.ReceiptDigest, NativeTheme.Muted);
            identity.AutomationId = "creation-magic-rereview-receipt";
            _body.Add(NativeTheme.TechnicalDetails(identity, "creation-magic-rereview-receipt-details"));
            AddExit();
            return;
        }
        if (_confirmation is not null || !Coordinator.IsCreationMagicReReviewCurrent(_state))
        {
            _body.Add(NativeTheme.Body(_confirmation is not null
                ? Text("CheckSave", "Reopen the runner to check the save result before trying again.")
                : Text("Stale", "The runner changed. Reopen this review to check the current state."), NativeTheme.Danger));
            AddExit();
            return;
        }
        _body.Add(NativeTheme.Body(Text("Intro",
            "Your Attributes changed after these choices were saved. Core has checked that the same Magic choices and budgets remain valid. Nothing changes until you confirm.")));
        var binding = NativeTheme.Body(CreationAllocationStrings.Format("MagicReReview.Binding",
            "Attributes revision {0} → {1}. Keep all saved choices:",
            _state.HistoricalDraft.AttributesDraftRevision, _state.Binding.Current.AttributesDraftRevision), NativeTheme.Muted);
        binding.AutomationId = "creation-magic-rereview-binding";
        var choices = _state.HistoricalDraft.Selections;
        AddChoice(choices.Tradition);
        AddChoice(choices.Stream);
        foreach (var choice in choices.AdeptPowers) AddChoice(choice.Identity, choice.Levels);
        foreach (var choice in choices.Spells) AddChoice(choice);
        foreach (var choice in choices.ComplexForms) AddChoice(choice);
        var current = _state.InitialPreview.CurrentPreview;
        foreach (var budget in new[] { current.TraditionBudget, current.StreamBudget, current.AdeptPowerPointBudget,
                     current.SpellBudget, current.ComplexFormBudget }.Where(row => row.Total > 0))
            _body.Add(NativeTheme.Body(CreationAllocationStrings.Format("MagicReReview.Budget", "{0}: {1} / {2}",
                CreationMagicResonancePage.KindLabel(budget.Kind), budget.Used, budget.Total)));
        var confirm = NativeTheme.PrimaryButton(Text("Confirm", "Keep choices and confirm current Attributes"));
        confirm.AutomationId = "creation-magic-rereview-confirm";
        confirm.IsEnabled = _attached;
        var progress = new ActivityIndicator { IsRunning = false, IsVisible = false, AutomationId = "creation-magic-rereview-progress" };
        confirm.Clicked += async (_, _) => await RunAsync(async () =>
        {
            long generation = Volatile.Read(ref _generation);
            if (!_attached || _saving || _confirmation is not null || !Coordinator.IsCreationMagicReReviewCurrent(_state)) return;
            bool accepted = await DisplayAlertAsync(Title,
                Text("ConfirmBody", "Keep exactly these choices and bind them to the current Attributes?"),
                Text("Apply", "Confirm"), Text("Cancel", "Cancel"));
            if (!accepted || !_attached || generation != Volatile.Read(ref _generation)
                || !Coordinator.IsCreationMagicReReviewCurrent(_state)) return;
            _saving = true;
            confirm.IsEnabled = false;
            confirm.Text = Text("Saving", "Saving reviewed choices…");
            progress.IsVisible = progress.IsRunning = true;
            try { _confirmation = await Coordinator.ConfirmCreationMagicReReviewAsync(_state, explicitlyReviewed: true); }
            finally { _saving = false; }
        });
        _body.Add(confirm);
        _body.Add(progress);
        AddExit();
        _body.Add(NativeTheme.TechnicalDetails(binding, "creation-magic-rereview-details"));
    }

    private void AddChoice(CharacterCreationMagicResonanceOptionIdentity? identity, int? levels = null)
    {
        if (identity is null) return;
        var authority = _state.CurrentState.Authority;
        var name = authority.Traditions.Concat(authority.Streams).Concat(authority.AdeptPowers)
            .Concat(authority.Spells).Concat(authority.ComplexForms).Single(row => row.Identity == identity).Name;
        _body.Add(NativeTheme.Body(levels is null ? name
            : CreationAllocationStrings.Format("MagicReReview.Power", "{0} · level {1}", name, levels)));
    }

    private void AddExit()
    {
        var back = NativeTheme.SecondaryButton(Text("Back", "Back without further changes"));
        back.AutomationId = "creation-magic-rereview-back";
        back.Clicked += async (_, _) => await RunAsync(async () => { if (_attached) await Navigation.PopAsync(); });
        _body.Add(back);
    }

    internal static string Text(string key, string fallback) => CreationAllocationStrings.Get("MagicReReview." + key, fallback);
}
