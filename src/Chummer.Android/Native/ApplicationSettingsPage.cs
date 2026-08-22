using Chummer.Contracts.Api;

namespace Chummer.Android.Native;

/// <summary>
/// Phone-only Chummer5 Global Options surface for confirmation settings.
/// The switches are local drafts; only the explicit Save action invokes persistence.
/// </summary>
public sealed class ApplicationSettingsPage : NativePageBase
{
    private readonly ApplicationDeleteConfirmationState _baseline;
    private readonly Switch _confirmDelete;
    private readonly Switch _confirmKarmaExpense;
    private readonly Label _revision;

    public ApplicationSettingsPage(RunnerSessionCoordinator coordinator) : base(coordinator)
    {
        Title = "Application settings";
        AutomationId = "application-settings-page";
        _baseline = coordinator.ApplicationSettings;

        VerticalStackLayout body = new()
        {
            Padding = new Thickness(20, 20, 20, 36),
            Spacing = 18
        };
        body.Add(NativeTheme.Eyebrow("Global options"));
        body.Add(NativeTheme.Title("Confirmations"));
        body.Add(NativeTheme.Body(
            "Matches Chummer5’s confirmdelete and confirmkarmaexpense options. These settings do not modify runner XML.",
            NativeTheme.Muted));

        _confirmDelete = new Switch
        {
            AutomationId = "settings-confirm-delete",
            IsToggled = _baseline.ConfirmDelete
        };
        Grid row = new()
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            ColumnSpacing = 12
        };
        VerticalStackLayout labels = new() { Spacing = 3 };
        labels.Add(NativeTheme.Title("Ask before deleting items", 20));
        labels.Add(NativeTheme.Body(
            "Changes remain a draft until Save. Back discards the draft.",
            NativeTheme.Muted));
        row.Add(labels);
        row.Add(_confirmDelete, 1);
        body.Add(NativeTheme.Card(row));

        _confirmKarmaExpense = new Switch
        {
            AutomationId = "settings-confirm-karma-expense",
            IsToggled = _baseline.ConfirmKarmaExpense
        };
        Grid karmaRow = new()
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            ColumnSpacing = 12
        };
        VerticalStackLayout karmaLabels = new() { Spacing = 3 };
        karmaLabels.Add(NativeTheme.Title("Ask before Karma expenses", 20));
        karmaLabels.Add(NativeTheme.Body(
            "Changes remain a draft until Save. Back discards both confirmation drafts.",
            NativeTheme.Muted));
        karmaRow.Add(karmaLabels);
        karmaRow.Add(_confirmKarmaExpense, 1);
        body.Add(NativeTheme.Card(karmaRow));

        _revision = NativeTheme.Body(string.Empty, NativeTheme.Muted);
        _revision.AutomationId = "settings-revision";
        body.Add(_revision);

        Button save = NativeTheme.PrimaryButton("Save");
        save.AutomationId = "settings-save";
        save.Clicked += async (_, _) => await RunAsync(async () =>
        {
            await Coordinator.SaveApplicationConfirmationSettingsAsync(
                _confirmDelete.IsToggled,
                _confirmKarmaExpense.IsToggled,
                _baseline.Revision);
            await Navigation.PopAsync();
        });
        body.Add(save);

        Content = new ScrollView { Content = body };
    }

    protected override void Refresh()
    {
        _revision.Text = $"Settings revision {_baseline.Revision}";
    }
}
