using Chummer.Android.Platform;

namespace Chummer.Android.Native;

public class MorePage : NativePageBase, IPlayReviewSafeSurface
{
    private readonly bool _showUnrestrictedActions;
    private readonly string? _runnerRouteAfterOpen;
    private string? _updateStatus;
    private readonly VerticalStackLayout _body = new()
    {
        Padding = new Thickness(20, 18, 20, 40),
        Spacing = 16
    };

    public MorePage(RunnerSessionCoordinator coordinator)
        : this(coordinator, showUnrestrictedActions: true, runnerRouteAfterOpen: null)
    {
    }

    protected MorePage(
        RunnerSessionCoordinator coordinator,
        bool showUnrestrictedActions,
        string? runnerRouteAfterOpen) : base(coordinator)
    {
        _showUnrestrictedActions = showUnrestrictedActions;
        _runnerRouteAfterOpen = runnerRouteAfterOpen;
        Title = PhoneStrings.Get("ShellMore", "More");
        Content = new ScrollView { Content = _body };
    }

    protected override void Refresh()
    {
        _body.Clear();
        _body.Add(NativeTheme.Eyebrow("Chummer"));
        _body.Add(NativeTheme.Title(PhoneStrings.Get("ShellMore", "More")));
        AddFiles();
        AddAccount();
        AddApp();
    }

    private void AddFiles()
    {
        VerticalStackLayout files = new() { Spacing = 10 };
        files.Add(NativeTheme.Eyebrow(PhoneStrings.Get("MoreRunnerFile", "Runner file")));
        Button open = NativeTheme.SecondaryButton(PhoneStrings.Get("OpenFile", "Open file"));
        open.AutomationId = "more-open-file";
        open.Clicked += async (_, _) => await RunAsync(async () =>
        {
            NativeWorkspaceActivationReceipt? activation = await Coordinator.OpenLocalAsync();
            if (_runnerRouteAfterOpen is not null
                && activation?.Matches(
                    Coordinator.State,
                    NativeWorkspaceActivationKind.LocalFile) == true)
            {
                await Shell.Current.GoToAsync(_runnerRouteAfterOpen);
            }
        });
        Button save = NativeTheme.SecondaryButton(PhoneStrings.Get("Save", "Save"));
        save.AutomationId = "more-save-runner";
        save.IsEnabled = Coordinator.State.Profile is not null;
        save.Clicked += async (_, _) => await RunAsync(() => Coordinator.SaveAsync());
        Button export = NativeTheme.SecondaryButton(PhoneStrings.Get("Export", "Export"));
        export.IsEnabled = Coordinator.State.Profile is not null;
        export.Clicked += async (_, _) => await RunAsync(() => Coordinator.ExportAsync());
        Button print = NativeTheme.SecondaryButton(PhoneStrings.Get("Print", "Print"));
        print.IsEnabled = Coordinator.State.Profile is not null;
        print.Clicked += async (_, _) => await RunAsync(() => Coordinator.PrintAsync());
        files.Add(open);
        files.Add(save);
        files.Add(export);
        files.Add(print);
        if (_showUnrestrictedActions)
        {
            Button allActions = NativeTheme.PrimaryButton(PhoneStrings.Get("AllActions", "All actions"));
            allActions.AutomationId = "more-all-actions";
            allActions.Clicked += async (_, _) => await Navigation.PushAsync(new NativeCommandPage(Coordinator));
            files.Add(allActions);
        }
        _body.Add(NativeTheme.Card(files));
    }

    private void AddAccount()
    {
        VerticalStackLayout account = new() { Spacing = 10 };
        account.Add(NativeTheme.Eyebrow(PhoneStrings.Get("Account", "Account")));
        account.Add(NativeTheme.Title(Coordinator.Account.Label, 21));
        if (!string.IsNullOrWhiteSpace(Coordinator.Account.Detail))
        {
            account.Add(NativeTheme.Body(Coordinator.Account.Detail, NativeTheme.Muted));
        }

        if (Coordinator.Account.IsLinked)
        {
            Button refresh = NativeTheme.SecondaryButton(
                PhoneStrings.Get("RefreshAccount", "Refresh account data"));
            refresh.Clicked += async (_, _) => await RunAsync(() => Coordinator.RefreshLinkedDataAsync());
            account.Add(refresh);
        }
        else
        {
            Button link = NativeTheme.PrimaryButton(PhoneStrings.Get("LinkAccount", "Link account"));
            link.IsEnabled = !Coordinator.Account.IsLoading;
            link.Clicked += async (_, _) => await RunAsync(() => Coordinator.BeginAccountLinkAsync());
            account.Add(link);
        }
        account.Add(NativeTheme.NavigationRow(
            PhoneStrings.Get("AccountPrivacy", "Account & privacy"),
            Coordinator.Account.IsLinked
                ? PhoneStrings.Get("LinkedDeviceDeletion", "Linked device and deletion")
                : PhoneStrings.Get("LinkingDeletion", "Linking and deletion"),
            async () => await Navigation.PushAsync(new AccountPrivacyPage(Coordinator))));
        _body.Add(NativeTheme.Card(account));
    }

    private void AddApp()
    {
        VerticalStackLayout app = new() { Spacing = 10 };
        app.Add(NativeTheme.Eyebrow(PhoneStrings.Get("App", "App")));
        app.Add(NativeTheme.Metric(
            PhoneStrings.Get("Version", "Version"),
            AppInfo.Current.VersionString));
        app.Add(NativeTheme.NavigationRow(
            PhoneStrings.Get("ApplicationSettings", "Settings"),
            PhoneStrings.Get(
                "SettingsPhoneOnlyDetail",
                "Only options that change how Chummer behaves on this phone appear here."),
            () => Navigation.PushAsync(new ApplicationSettingsPage(Coordinator)),
            automationId: "more-application-settings"));
        Button updates = NativeTheme.SecondaryButton(
            _updateStatus ?? PhoneStrings.Get("CheckUpdates", "Check for updates"));
        updates.AutomationId = "more-check-play-updates";
        updates.Clicked += async (_, _) => await CheckUpdatesAsync(updates);
        app.Add(updates);
        Label updateAuthority = NativeTheme.Body(
            PhoneStrings.Get(
                "SettingsUpdatesPlayManaged",
                "Updates and preview access are managed by Google Play, not inside Chummer."),
            NativeTheme.Muted);
        updateAuthority.AutomationId = "more-updates-play-managed";
        app.Add(updateAuthority);
        _body.Add(NativeTheme.Card(app));
    }

    private async Task CheckUpdatesAsync(Button button)
    {
        button.IsEnabled = false;
        button.Text = PhoneStrings.Get("Checking", "Checking");
        try
        {
            AndroidUpdateCheckResult result = await Coordinator.CheckForUpdatesAsync();
            _updateStatus = result switch
            {
                AndroidUpdateCheckResult.Current => PhoneStrings.Get("UpToDate", "Up to date"),
                AndroidUpdateCheckResult.Started => PhoneStrings.Get("UpdateStarted", "Update started"),
                AndroidUpdateCheckResult.ReadyToInstall => PhoneStrings.Get("ReadyInstall", "Ready to install"),
                AndroidUpdateCheckResult.PlayManagedRequired => PhoneStrings.Get(
                    "PlayUpdates",
                    "Updates come through Google Play"),
                AndroidUpdateCheckResult.Checking => PhoneStrings.Get("Checking", "Checking"),
                _ => PhoneStrings.Get("UpdateUnavailable", "Update check unavailable")
            };
            button.Text = _updateStatus;
        }
        catch (Exception ex)
        {
            _updateStatus = PhoneStrings.Get("UpdateUnavailable", "Update check unavailable");
            button.Text = _updateStatus;
            await DisplayAlertAsync("Chummer", ex.Message, PhoneStrings.Get("Ok", "OK"));
        }
        finally
        {
            button.IsEnabled = true;
        }
    }
}
