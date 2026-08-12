namespace Chummer.Android.Native;

public sealed class AccountPrivacyPage : NativePageBase
{
    private readonly VerticalStackLayout _body = new()
    {
        Padding = new Thickness(20, 18, 20, 40),
        Spacing = 16
    };

    public AccountPrivacyPage(RunnerSessionCoordinator coordinator) : base(coordinator)
    {
        Title = "Account & privacy";
        Content = new ScrollView { Content = _body };
    }

    protected override void Refresh()
    {
        _body.Clear();
        _body.Add(NativeTheme.Eyebrow("Account"));
        _body.Add(NativeTheme.Title("Account & privacy"));

        VerticalStackLayout device = new() { Spacing = 10 };
        device.Add(NativeTheme.Metric("Status", Coordinator.Account.Label));
        Button unlink = NativeTheme.SecondaryButton("Unlink this device");
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
                await Navigation.PopAsync();
            }
        };
        device.Add(unlink);
        _body.Add(NativeTheme.Card(device));

        _body.Add(NativeTheme.NavigationRow(
            "How deletion works",
            "What is removed and how long receipts remain",
            Coordinator.OpenAccountDeletionInfoAsync));
        _body.Add(NativeTheme.NavigationRow(
            "Delete account",
            "Permanently remove your Chummer account",
            async () => await Navigation.PushAsync(new AccountDeletionPage(Coordinator))));
    }
}

public sealed class AccountDeletionPage : NativePageBase
{
    private readonly VerticalStackLayout _body = new()
    {
        Padding = new Thickness(20, 18, 20, 40),
        Spacing = 16
    };

    public AccountDeletionPage(RunnerSessionCoordinator coordinator) : base(coordinator)
    {
        Title = "Delete account";
        Content = new ScrollView { Content = _body };
    }

    protected override void Refresh()
    {
        _body.Clear();
        _body.Add(NativeTheme.Eyebrow("Permanent"));
        _body.Add(NativeTheme.Title("Delete account"));
        _body.Add(NativeTheme.Body(
            "This removes your online runners, memberships, groups you own, campaign data, support messages, and linked devices."));

        Switch removeLocal = new() { IsToggled = true };
        Grid localRow = new()
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            ColumnSpacing = 12
        };
        localRow.Add(NativeTheme.Body("Remove runners saved on this device"));
        localRow.Add(removeLocal, 1);
        _body.Add(NativeTheme.Card(localRow));

        VerticalStackLayout confirm = new() { Spacing = 10 };
        confirm.Add(NativeTheme.Body($"Type {Platform.AndroidAccountErasureConfirmation.RequiredPhrase} to continue."));
        Entry phrase = new()
        {
            Placeholder = Platform.AndroidAccountErasureConfirmation.RequiredPhrase,
            ClearButtonVisibility = ClearButtonVisibility.WhileEditing,
            ReturnType = ReturnType.Done
        };
        Button erase = NativeTheme.PrimaryButton("Delete account");
        erase.BackgroundColor = NativeTheme.Danger;
        erase.IsEnabled = false;
        phrase.TextChanged += (_, args) => erase.IsEnabled = string.Equals(
            args.NewTextValue,
            Platform.AndroidAccountErasureConfirmation.RequiredPhrase,
            StringComparison.Ordinal);
        erase.Clicked += async (_, _) =>
        {
            bool final = await DisplayAlertAsync(
                "Delete your account?",
                "This cannot be undone.",
                "Delete",
                "Cancel");
            if (!final)
            {
                return;
            }

            erase.IsEnabled = false;
            phrase.IsEnabled = false;
            removeLocal.IsEnabled = false;
            try
            {
                NativeAccountErasureResult result = await Coordinator.EraseAccountAsync(removeLocal.IsToggled);
                string message = result.LocalRunnersRemoved
                    ? "Your account and this device are cleared."
                    : "Your account is deleted, but some runners remain on this device.";
                await DisplayAlertAsync("Account deleted", message, "Done");
                await Navigation.PopToRootAsync();
            }
            catch (Exception ex)
            {
                await DisplayAlertAsync("Could not delete account", ex.Message, "OK");
                phrase.IsEnabled = true;
                removeLocal.IsEnabled = true;
                erase.IsEnabled = string.Equals(
                    phrase.Text,
                    Platform.AndroidAccountErasureConfirmation.RequiredPhrase,
                    StringComparison.Ordinal);
            }
        };
        confirm.Add(phrase);
        confirm.Add(erase);
        _body.Add(NativeTheme.Card(confirm));
    }
}
