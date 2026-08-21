using System.Globalization;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

public sealed class CareerReputationPage : NativePageBase
{
    private readonly CareerReputationEditorState _editor;
    private readonly Picker _streetCred;
    private readonly Picker _notoriety;
    private readonly Picker _publicAwareness;
    private readonly Picker? _astralReputation;
    private readonly Picker? _wildReputation;
    private readonly Button _burnStreetCred;
    private readonly Button _save;

    public CareerReputationPage(
        RunnerSessionCoordinator coordinator,
        CareerReputationEditorState editor) : base(coordinator)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        Title = "Reputation";
        AutomationId = "career-reputation";

        VerticalStackLayout body = new()
        {
            Padding = new Thickness(20, 18, 20, 40),
            Spacing = 14
        };
        body.Add(NativeTheme.Eyebrow("Career runner"));
        body.Add(NativeTheme.Title("Reputation"));
        body.Add(NativeTheme.Body(
            "Manual reputation values stored in this runner. Source-specific fields appear only when the saved character settings enable their books.",
            NativeTheme.Muted));
        body.Add(NativeTheme.Body(
            $"Total Street Cred: {editor.TotalStreetCred.ToString(CultureInfo.InvariantCulture)} · already burnt: {editor.BurntStreetCred.ToString(CultureInfo.InvariantCulture)}",
            NativeTheme.Muted));

        _burnStreetCred = NativeTheme.SecondaryButton("Burn 2 Street Cred");
        _burnStreetCred.AutomationId = "career-reputation-burn-street-cred";
        _burnStreetCred.Clicked += async (_, _) => await RunAsync(BurnStreetCredAsync);
        body.Add(_burnStreetCred);
        if (!editor.CanBurnStreetCred)
        {
            body.Add(NativeTheme.Body(
                editor.BurnStreetCredUnavailableReason
                ?? "At least 2 total Street Cred is required.",
                NativeTheme.Muted));
        }

        _streetCred = AddPicker(body, "Street Cred", "career-reputation-street-cred", editor.StreetCred);
        _notoriety = AddPicker(body, "Notoriety", "career-reputation-notoriety", editor.Notoriety);
        _publicAwareness = AddPicker(body, "Public Awareness", "career-reputation-public-awareness", editor.PublicAwareness);
        if (editor.AstralReputationVisible)
        {
            _astralReputation = AddPicker(
                body,
                "Astral Reputation",
                "career-reputation-astral",
                editor.AstralReputation);
        }
        if (editor.WildReputationVisible)
        {
            _wildReputation = AddPicker(
                body,
                "Wild Reputation",
                "career-reputation-wild",
                editor.WildReputation);
        }

        _save = NativeTheme.PrimaryButton("Save reputation");
        _save.AutomationId = "career-reputation-save";
        _save.Clicked += async (_, _) => await RunAsync(SaveAsync);
        body.Add(_save);
        Content = new ScrollView { Content = body };
    }

    protected override void Refresh()
    {
        bool exactRevision = Coordinator.State.WorkspaceId == _editor.WorkspaceId
            && Coordinator.State.ContentRevision == _editor.ContentRevision;
        _streetCred.IsEnabled = exactRevision;
        _notoriety.IsEnabled = exactRevision;
        _publicAwareness.IsEnabled = exactRevision;
        if (_astralReputation is not null)
        {
            _astralReputation.IsEnabled = exactRevision;
        }
        if (_wildReputation is not null)
        {
            _wildReputation.IsEnabled = exactRevision;
        }
        _burnStreetCred.IsEnabled = exactRevision && _editor.CanBurnStreetCred;
        _save.IsEnabled = exactRevision;
    }

    private async Task BurnStreetCredAsync()
    {
        bool confirmed = await DisplayAlertAsync(
            "Burn Street Cred?",
            "Burning Street Cred is permanent and reduces total Street Cred by 2.",
            "Burn",
            "Cancel");
        if (!confirmed)
        {
            return;
        }

        await Coordinator.ApplyBurnStreetCredAsync(new BurnStreetCredRequest(
            _editor.WorkspaceId,
            _editor.ContentRevision));
        if (Coordinator.State.Error is null)
        {
            await Navigation.PopAsync();
        }
    }

    private async Task SaveAsync()
    {
        await Coordinator.ApplyCareerReputationEditAsync(new CareerReputationEditRequest(
            _editor.WorkspaceId,
            _editor.ContentRevision,
            SelectedValue(_streetCred, _editor.StreetCred),
            SelectedValue(_notoriety, _editor.Notoriety),
            SelectedValue(_publicAwareness, _editor.PublicAwareness),
            _astralReputation is null
                ? null
                : SelectedValue(_astralReputation, _editor.AstralReputation),
            _wildReputation is null
                ? null
                : SelectedValue(_wildReputation, _editor.WildReputation)));
        if (Coordinator.State.Error is null)
        {
            await Navigation.PopAsync();
        }
    }

    private static Picker AddPicker(
        VerticalStackLayout body,
        string label,
        string automationId,
        int value)
    {
        body.Add(NativeTheme.FieldLabel(label));
        string[] values = Enumerable.Range(0, 101)
            .Select(candidate => candidate.ToString(CultureInfo.InvariantCulture))
            .ToArray();
        Picker picker = new()
        {
            AutomationId = automationId,
            Title = label,
            ItemsSource = values,
            SelectedIndex = Math.Clamp(value, 0, 100),
            BackgroundColor = NativeTheme.Surface,
            TextColor = NativeTheme.Text
        };
        body.Add(picker);
        return picker;
    }

    private static int SelectedValue(Picker picker, int fallback)
        => picker.SelectedItem is string selected
            && int.TryParse(selected, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
                ? value
                : fallback;
}
