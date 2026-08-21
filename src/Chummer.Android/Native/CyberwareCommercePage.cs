using System.Globalization;
using Chummer.Contracts.Characters;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

public sealed class CyberwareCommercePage : NativePageBase
{
    private readonly CyberwareCommerceEditorState _state;
    private readonly CharacterCyberwareCommerceSnapshot? _snapshot;
    private readonly Picker _grade;
    private readonly Entry _rating;
    private readonly Entry _refundPercentage;
    private readonly Switch _freeCost;
    private readonly Button _upgrade;
    private readonly Button _sell;

    public CyberwareCommercePage(
        RunnerSessionCoordinator coordinator,
        CyberwareCommerceEditorState state) : base(coordinator)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.CyberwareId == Guid.Empty)
        {
            throw new ArgumentException("Cyberware commerce requires a stable Cyberware identity.", nameof(state));
        }

        _state = state;
        _snapshot = state.Semantics.Snapshot;
        string token = state.CyberwareId.ToString("N");
        Title = "Cyberware Commerce";
        AutomationId = $"cyberware-commerce-page-{token}";

        VerticalStackLayout body = new()
        {
            Padding = new Thickness(20, 18, 20, 40),
            Spacing = 14
        };
        body.Add(NativeTheme.Eyebrow("Career Cyberware"));
        body.Add(NativeTheme.Title(string.IsNullOrWhiteSpace(state.CyberwareName)
            ? "Cyberware Commerce"
            : state.CyberwareName));
        body.Add(NativeTheme.Body(
            "Upgrade and sale quotes are recalculated from the exact saved source profile before one atomic save.",
            NativeTheme.Muted));

        CharacterCyberwareGradeOption[] grades = _snapshot?.GradeOptions.ToArray() ?? [];
        _grade = new Picker
        {
            Title = "Grade",
            AutomationId = $"cyberware-commerce-grade-{token}",
            ItemsSource = grades.Select(option => option.Name).ToArray(),
            BackgroundColor = NativeTheme.Surface,
            TextColor = NativeTheme.Text
        };
        _grade.SelectedIndex = _snapshot is null
            ? -1
            : Array.FindIndex(grades, option => string.Equals(
                option.Id,
                _snapshot.CurrentGradeId,
                StringComparison.OrdinalIgnoreCase));
        body.Add(NativeTheme.FieldLabel("Upgrade grade"));
        body.Add(_grade);

        _rating = new Entry
        {
            Text = (_snapshot?.CurrentRating ?? 0).ToString(CultureInfo.InvariantCulture),
            Keyboard = Keyboard.Numeric,
            AutomationId = $"cyberware-commerce-rating-{token}",
            BackgroundColor = NativeTheme.Surface,
            TextColor = NativeTheme.Text
        };
        body.Add(NativeTheme.FieldLabel("Upgrade rating"));
        body.Add(_rating);

        _refundPercentage = new Entry
        {
            Text = CharacterCyberwareCommerceRules.DefaultRefundPercentage.ToString("0.00", CultureInfo.InvariantCulture),
            Keyboard = Keyboard.Numeric,
            AutomationId = $"cyberware-commerce-refund-percent-{token}",
            BackgroundColor = NativeTheme.Surface,
            TextColor = NativeTheme.Text
        };
        body.Add(NativeTheme.FieldLabel("Refund / sale percent (0.00–9999.99)"));
        body.Add(_refundPercentage);

        _freeCost = new Switch
        {
            IsToggled = false,
            AutomationId = $"cyberware-commerce-free-cost-{token}",
            OnColor = NativeTheme.Signal
        };
        Grid freeCostRow = new()
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            }
        };
        freeCostRow.Add(NativeTheme.FieldLabel("Free upgrade cost"), 0, 0);
        freeCostRow.Add(_freeCost, 1, 0);
        body.Add(NativeTheme.Card(freeCostRow));

        AddAvailability(body, "Upgrade", state.Semantics.UpgradeExact, state.Semantics.UpgradeBlockReason, token);
        AddAvailability(body, "Sell", state.Semantics.SellExact, state.Semantics.SellBlockReason, token);

        _upgrade = NativeTheme.PrimaryButton("Upgrade Cyberware");
        _upgrade.AutomationId = $"cyberware-commerce-upgrade-{token}";
        _upgrade.Clicked += async (_, _) => await RunAsync(UpgradeAsync);
        body.Add(_upgrade);

        _sell = NativeTheme.PrimaryButton("Sell Cyberware");
        _sell.BackgroundColor = NativeTheme.Danger;
        _sell.AutomationId = $"cyberware-commerce-sell-{token}";
        _sell.Clicked += async (_, _) => await RunAsync(SellAsync);
        body.Add(_sell);
        Content = new ScrollView { Content = body };
        RefreshEnabledState();
    }

    protected override void Refresh() => RefreshEnabledState();

    private void RefreshEnabledState()
    {
        bool revisionMatches = Coordinator.State.WorkspaceId == _state.WorkspaceId
            && Coordinator.State.ContentRevision == _state.ContentRevision;
        _grade.IsEnabled = revisionMatches && _state.Semantics.UpgradeExact;
        _rating.IsEnabled = revisionMatches && _state.Semantics.UpgradeExact;
        _refundPercentage.IsEnabled = revisionMatches
            && (_state.Semantics.UpgradeExact || _state.Semantics.SellExact);
        _freeCost.IsEnabled = revisionMatches && _state.Semantics.UpgradeExact;
        _upgrade.IsEnabled = revisionMatches && _state.Semantics.UpgradeExact;
        _sell.IsEnabled = revisionMatches && _state.Semantics.SellExact;
    }

    private async Task UpgradeAsync()
    {
        if (_snapshot is null
            || _grade.SelectedIndex < 0
            || _grade.SelectedIndex >= _snapshot.GradeOptions.Count
            || !int.TryParse(_rating.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int rating)
            || !TryReadPercentage(out decimal percentage))
        {
            await DisplayAlertAsync("Invalid upgrade", "Choose an exact grade, rating, and refund percentage.", "OK");
            return;
        }

        CharacterCyberwareGradeOption grade = _snapshot.GradeOptions[_grade.SelectedIndex];
        CharacterCyberwareCommerceQuote quote = CharacterCyberwareCommerceRules.QuoteUpgrade(
            _state.Semantics,
            grade.Id,
            rating,
            percentage,
            _freeCost.IsToggled);
        if (!quote.Exact)
        {
            await DisplayAlertAsync("Upgrade unavailable", quote.BlockReason, "OK");
            return;
        }

        bool confirmed = await DisplayAlertAsync(
            "Confirm Cyberware upgrade",
            $"Grade {quote.GradeName}, rating {quote.Rating.ToString(CultureInfo.InvariantCulture)}. "
            + $"Nuyen change: {quote.NuyenDelta.ToString(CultureInfo.InvariantCulture)}; "
            + $"Essence Hole delta: {quote.EssenceDelta.ToString(CultureInfo.InvariantCulture)}. Continue?",
            "Upgrade",
            "Cancel");
        if (!confirmed)
        {
            return;
        }

        await Coordinator.ApplyCyberwareCommerceEditAsync(new CyberwareCommerceRequest(
            _state.WorkspaceId,
            _state.ContentRevision,
            _state.CyberwareId,
            CharacterCyberwareCommerceAction.Upgrade,
            grade.Id,
            rating,
            percentage,
            _freeCost.IsToggled,
            Confirmed: true,
            QuoteDigest: quote.QuoteDigest));
        if (Coordinator.State.Error is null)
        {
            await Navigation.PopAsync();
        }
    }

    private async Task SellAsync()
    {
        if (_snapshot is null || !TryReadPercentage(out decimal percentage))
        {
            await DisplayAlertAsync("Invalid sale", "Enter an exact sale percentage.", "OK");
            return;
        }

        CharacterCyberwareCommerceQuote quote = CharacterCyberwareCommerceRules.QuoteSale(
            _state.Semantics,
            percentage);
        if (!quote.Exact)
        {
            await DisplayAlertAsync("Sale unavailable", quote.BlockReason, "OK");
            return;
        }

        bool confirmed = await DisplayAlertAsync(
            "Confirm Cyberware sale",
            $"Permanently remove this Cyberware and receive {quote.NuyenDelta.ToString(CultureInfo.InvariantCulture)} Nuyen?",
            "Sell",
            "Cancel");
        if (!confirmed)
        {
            return;
        }

        await Coordinator.ApplyCyberwareCommerceEditAsync(new CyberwareCommerceRequest(
            _state.WorkspaceId,
            _state.ContentRevision,
            _state.CyberwareId,
            CharacterCyberwareCommerceAction.Sell,
            GradeId: string.Empty,
            Rating: _snapshot.CurrentRating,
            RefundPercentage: percentage,
            FreeCost: false,
            Confirmed: true,
            QuoteDigest: quote.QuoteDigest));
        if (Coordinator.State.Error is null)
        {
            await Navigation.PopAsync();
        }
    }

    private bool TryReadPercentage(out decimal percentage)
        => decimal.TryParse(
                _refundPercentage.Text,
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out percentage)
            && CharacterCyberwareCommerceRules.TryNormalizeRefundPercentage(percentage, out _);

    private static void AddAvailability(
        VerticalStackLayout body,
        string action,
        bool exact,
        string reason,
        string token)
    {
        Label status = NativeTheme.Body(
            exact ? $"{action}: exact source-backed quote available." : $"{action}: {reason}",
            exact ? NativeTheme.Muted : NativeTheme.Danger);
        status.AutomationId = $"cyberware-commerce-{action.ToLowerInvariant()}-status-{token}";
        body.Add(status);
    }
}
