using Chummer.Android.Platform;

namespace Chummer.Android.Native;

public sealed class MorePage : NativePageBase
{
    private string? _updateStatus;
    private readonly VerticalStackLayout _body = new()
    {
        Padding = new Thickness(20, 18, 20, 40),
        Spacing = 16
    };

    public MorePage(RunnerSessionCoordinator coordinator) : base(coordinator)
    {
        Title = "More";
        Content = new ScrollView { Content = _body };
    }

    protected override void Refresh()
    {
        _body.Clear();
        _body.Add(NativeTheme.Eyebrow("Chummer"));
        _body.Add(NativeTheme.Title("More"));
        AddFiles();
        AddAccount();
        AddApp();
    }

    private void AddFiles()
    {
        VerticalStackLayout files = new() { Spacing = 10 };
        files.Add(NativeTheme.Eyebrow("Runner file"));
        Button open = NativeTheme.SecondaryButton("Open file");
        open.Clicked += async (_, _) => await RunAsync(() => Coordinator.OpenLocalAsync());
        Button save = NativeTheme.SecondaryButton("Save");
        save.IsEnabled = Coordinator.State.Profile is not null;
        save.Clicked += async (_, _) => await RunAsync(() => Coordinator.SaveAsync());
        Button export = NativeTheme.SecondaryButton("Export");
        export.IsEnabled = Coordinator.State.Profile is not null;
        export.Clicked += async (_, _) => await RunAsync(() => Coordinator.ExportAsync());
        Button print = NativeTheme.SecondaryButton("Print");
        print.IsEnabled = Coordinator.State.Profile is not null;
        print.Clicked += async (_, _) => await RunAsync(() => Coordinator.PrintAsync());
        Button allActions = NativeTheme.PrimaryButton("All actions");
        allActions.Clicked += async (_, _) => await Navigation.PushAsync(new NativeCommandPage(Coordinator));
        files.Add(open);
        files.Add(save);
        files.Add(export);
        files.Add(print);
        files.Add(allActions);
        _body.Add(NativeTheme.Card(files));
    }

    private void AddAccount()
    {
        VerticalStackLayout account = new() { Spacing = 10 };
        account.Add(NativeTheme.Eyebrow("Account"));
        account.Add(NativeTheme.Title(Coordinator.Account.Label, 21));
        if (!string.IsNullOrWhiteSpace(Coordinator.Account.Detail))
        {
            account.Add(NativeTheme.Body(Coordinator.Account.Detail, NativeTheme.Muted));
        }

        if (Coordinator.Account.IsLinked)
        {
            Button refresh = NativeTheme.SecondaryButton("Refresh account data");
            refresh.Clicked += async (_, _) => await RunAsync(() => Coordinator.RefreshLinkedDataAsync());
            Button manage = NativeTheme.SecondaryButton("Manage account");
            manage.Clicked += async (_, _) => await RunAsync(() => Coordinator.OpenAccountAsync());
            Button unlink = NativeTheme.SecondaryButton("Unlink this device");
            unlink.TextColor = NativeTheme.Danger;
            unlink.Clicked += async (_, _) =>
            {
                bool confirmed = await DisplayAlertAsync(
                    "Unlink this device?",
                    "Online runners and groups will no longer be available here.",
                    "Unlink",
                    "Cancel");
                if (confirmed)
                {
                    await RunAsync(() => Coordinator.UnlinkAccountAsync());
                }
            };
            account.Add(refresh);
            account.Add(manage);
            account.Add(unlink);
        }
        else
        {
            Button link = NativeTheme.PrimaryButton("Link account");
            link.Clicked += async (_, _) => await RunAsync(() => Coordinator.BeginAccountLinkAsync());
            account.Add(link);
        }
        _body.Add(NativeTheme.Card(account));
    }

    private void AddApp()
    {
        VerticalStackLayout app = new() { Spacing = 10 };
        app.Add(NativeTheme.Eyebrow("App"));
        app.Add(NativeTheme.Metric("Version", AppInfo.Current.VersionString));
        Button updates = NativeTheme.SecondaryButton(_updateStatus ?? "Check for updates");
        updates.Clicked += async (_, _) => await CheckUpdatesAsync(updates);
        app.Add(updates);
        _body.Add(NativeTheme.Card(app));
    }

    private async Task CheckUpdatesAsync(Button button)
    {
        button.IsEnabled = false;
        button.Text = "Checking";
        try
        {
            AndroidUpdateCheckResult result = await Coordinator.CheckForUpdatesAsync();
            _updateStatus = result switch
            {
                AndroidUpdateCheckResult.Current => "Up to date",
                AndroidUpdateCheckResult.Started => "Update started",
                AndroidUpdateCheckResult.ReadyToInstall => "Ready to install",
                AndroidUpdateCheckResult.PlayManagedRequired => "Updates come through Google Play",
                AndroidUpdateCheckResult.Checking => "Checking",
                _ => "Update check unavailable"
            };
            button.Text = _updateStatus;
        }
        catch (Exception ex)
        {
            _updateStatus = "Update check unavailable";
            button.Text = _updateStatus;
            await DisplayAlertAsync("Chummer", ex.Message, "OK");
        }
        finally
        {
            button.IsEnabled = true;
        }
    }
}
