using Chummer.Android.Platform;
using Microsoft.Maui.ApplicationModel.DataTransfer;

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
        Title = PhoneStrings.Get("AccountPrivacy", "Account & privacy");
        Content = new ScrollView { Content = _body };
    }

    protected override void Refresh()
    {
        _body.Clear();
        _body.Add(NativeTheme.Eyebrow(PhoneStrings.Get("Account", "Account")));
        _body.Add(NativeTheme.Title(PhoneStrings.Get("AccountPrivacy", "Account & privacy")));

        VerticalStackLayout device = new() { Spacing = 10 };
        device.Add(NativeTheme.Metric(
            PhoneStrings.Get("AccountStatus", "Status"),
            Coordinator.Account.Label));
        if (Coordinator.Account.IsLinked)
        {
            Button unlink = NativeTheme.SecondaryButton(
                PhoneStrings.Get("AccountUnlinkDevice", "Unlink this device"));
            unlink.Clicked += async (_, _) =>
            {
                bool confirmed = await DisplayAlertAsync(
                    PhoneStrings.Get("AccountUnlinkDeviceQuestion", "Unlink this device?"),
                    PhoneStrings.Get(
                        "AccountUnlinkImpact",
                        "Online runners and groups will no longer be available here."),
                    PhoneStrings.Get("AccountUnlink", "Unlink"),
                    PhoneStrings.Get("Cancel", "Cancel"));
                if (confirmed)
                {
                    await RunAsync(() => Coordinator.UnlinkAccountAsync());
                    Refresh();
                }
            };
            device.Add(unlink);
        }
        else
        {
            device.Add(NativeTheme.Body(
                PhoneStrings.Get(
                    "AccountLinkDeviceDetail",
                    "Link this device to open online runners, groups, and account controls."),
                NativeTheme.Muted));
            Button link = NativeTheme.PrimaryButton(PhoneStrings.Get("LinkAccount", "Link account"));
            link.Clicked += async (_, _) => await RunAsync(() => Coordinator.BeginAccountLinkAsync());
            device.Add(link);
        }
        _body.Add(NativeTheme.Card(device));

        _body.Add(NativeTheme.NavigationRow(
            PhoneStrings.Get("AccountHowDeletionWorks", "How deletion works"),
            PhoneStrings.Get(
                "AccountDeletionReceiptDetail",
                "What is removed and how long receipts remain"),
            async () => await Navigation.PushAsync(new AccountDeletionInfoPage())));
        _body.Add(NativeTheme.NavigationRow(
            PhoneStrings.Get("DeleteAccount", "Delete account"),
            Coordinator.Account.IsLinked
                ? PhoneStrings.Get("AccountStartVerifiedDeletion", "Start verified account deletion")
                : PhoneStrings.Get("AccountLinkBeforeDeletion", "Link this device before deletion"),
            async () => await Navigation.PushAsync(new AccountDeletionPage(Coordinator))));
    }
}

public sealed class AccountDeletionInfoPage : ContentPage
{
    public AccountDeletionInfoPage()
    {
        Title = "How deletion works";
        BackgroundColor = NativeTheme.Paper;

        VerticalStackLayout removed = new() { Spacing = 10 };
        removed.Add(NativeTheme.Eyebrow("Removed"));
        removed.Add(NativeTheme.Body(
            "Online runners, memberships, groups you own, campaign data, support messages, and linked devices."));

        VerticalStackLayout process = new() { Spacing = 10 };
        process.Add(NativeTheme.Eyebrow("Process"));
        process.Add(NativeTheme.Body(
            "Chummer finishes the online deletion first. This app clears its linked grant and local data only after the server returns a deletion receipt."));
        process.Add(NativeTheme.Body(
            "Hosted Build backup retention, deletion replay, and whole-account erasure limits are still under review. Check the public deletion page for the current policy before you continue.",
            NativeTheme.Muted));

        Uri publicDeletion = ChummerWebRoutes.Resolve(ChummerWebRoutes.AccountDeletion);
        Button copyAddress = NativeTheme.SecondaryButton("Copy public deletion address");
        copyAddress.Clicked += async (_, _) =>
        {
            await Clipboard.Default.SetTextAsync(publicDeletion.ToString());
            copyAddress.Text = "Address copied";
        };

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = new Thickness(20, 18, 20, 40),
                Spacing = 16,
                Children =
                {
                    NativeTheme.Eyebrow("Privacy"),
                    NativeTheme.Title("How deletion works"),
                    NativeTheme.Card(removed),
                    NativeTheme.Card(process),
                    NativeTheme.Body(publicDeletion.ToString(), NativeTheme.Muted),
                    copyAddress
                }
            }
        };
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
            "This starts Chummer's verified deletion transaction for online runners, memberships, groups you own, campaign data, support messages, and linked devices."));

        if (!Coordinator.Account.IsLinked)
        {
            _body.Add(NativeTheme.Card(new VerticalStackLayout
            {
                Spacing = 10,
                Children =
                {
                    NativeTheme.Body(
                        "This device is not linked to a Chummer account. Link it first so Chummer can authenticate the deletion."),
                    CreateLinkButton()
                }
            }));
            return;
        }

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
                "Chummer clears this device only after every server-owned data plane returns a completed receipt. This cannot be undone.",
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
                string receiptLabel = result.Receipt.ReceiptSha256[..12].ToLowerInvariant();
                string message = result.LocalRunnersRemoved
                    ? $"Active account data and this device are cleared. Receipt {receiptLabel}…"
                    : $"Active account data is deleted, but some runners remain on this device. Receipt {receiptLabel}…";
                bool copyReceipt = await DisplayAlertAsync(
                    "Deletion completed",
                    message,
                    "Copy receipt",
                    "Done");
                if (copyReceipt)
                {
                    await Clipboard.Default.SetTextAsync(result.Receipt.ReceiptSha256);
                }
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

    private Button CreateLinkButton()
    {
        Button link = NativeTheme.PrimaryButton("Link account");
        link.Clicked += async (_, _) => await RunAsync(() => Coordinator.BeginAccountLinkAsync());
        return link;
    }
}
