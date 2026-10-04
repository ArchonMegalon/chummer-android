using Chummer.Android.Platform;
using Chummer.Presentation.Overview;
#if CHUMMER_API36_PROOF_INSTRUMENTATION
using Chummer.Android.Proof;
#endif

namespace Chummer.Android.Native;

public class HomePage : NativePageBase, IPlayReviewSafeSurface
{
    private readonly string _runnerRoute;
    private readonly ActivityIndicator _startupProgress = new()
    {
        AutomationId = "home-startup-progress",
        Color = NativeTheme.Text,
        HeightRequest = 32
    };
    private readonly VerticalStackLayout _body = new()
    {
        Padding = new Thickness(20, 20, 20, 36),
        Spacing = 18
    };

    public HomePage(RunnerSessionCoordinator coordinator) : this(coordinator, "//tablet-build", "Home")
    {
    }

    protected HomePage(
        RunnerSessionCoordinator coordinator,
        string runnerRoute,
        string title) : base(coordinator)
    {
        _runnerRoute = runnerRoute;
        Title = title;
        Content = new ScrollView { Content = _body };
        ShowStartupProgress();
    }

    protected override void OnAppearing()
    {
        // The first frame must not wait for local owner/workspace restoration.
        // Do not render runner data or enable actions before the base lifecycle
        // has admitted the current appearance and completed initialization.
        ShowStartupProgress();
        base.OnAppearing();
    }

    protected override void OnDisappearing()
    {
        _startupProgress.IsRunning = false;
        base.OnDisappearing();
    }

    private void ShowStartupProgress()
    {
        _body.Clear();
        var status = NativeTheme.Title(PhoneStrings.Get("HomeLoading", "Opening your runners…"));
        status.AutomationId = "home-startup-status";
        _body.Add(status);
        _startupProgress.IsRunning = true;
        _body.Add(_startupProgress);
    }

#if DEBUG
    protected override Task PrepareForAppearanceRefreshAsync(
        CancellationToken cancellationToken)
        => Coordinator.RefreshDebugWorkspaceAuthorityForPageAppearanceAsync(
            cancellationToken);
#endif

    protected override void Refresh()
    {
        _startupProgress.IsRunning = false;
        _body.Clear();
        _body.Add(NativeTheme.Eyebrow("Chummer"));
        _body.Add(NativeTheme.Title(PhoneStrings.Get("HomeYourRunners", "Your runners")));

        string runner = Coordinator.State.Profile?.Alias
            ?? Coordinator.State.Profile?.Name
            ?? PhoneStrings.Get("HomeNoRunner", "No runner open");
        string detail = Coordinator.State.Profile is null
            ? PhoneStrings.Get(
                "HomeStartDetail",
                "Open a file, link your account, or start a runner.")
            : string.Join(" · ", new[]
            {
                Coordinator.State.Profile.Metatype,
                Coordinator.State.Rules?.GameEdition
            }.Where(static value => !string.IsNullOrWhiteSpace(value)));

        VerticalStackLayout current = new() { Spacing = 9 };
        current.Add(NativeTheme.Eyebrow(PhoneStrings.Get("HomeCurrent", "Current")));
        current.Add(NativeTheme.Title(runner, 22));
        current.Add(NativeTheme.Body(detail, NativeTheme.Muted));
        if (Coordinator.State.Profile is not null)
        {
            Button continueButton = NativeTheme.PrimaryButton(
                PhoneStrings.Get("HomeContinue", "Continue building"));
            continueButton.Clicked += async (_, _) => await Shell.Current.GoToAsync(_runnerRoute);
            current.Add(continueButton);
            if (Coordinator.State.WorkspaceId is { } currentId
                && Coordinator.State.Session.FindWorkspace(currentId) is { } currentWorkspace)
                current.Add(CreateDeleteButton(currentWorkspace, "home-delete-current-runner"));
        }
        _body.Add(NativeTheme.Card(current));

#if DEBUG
        AddDebugWorkspaceAuthority();
#endif

        Grid quick = new()
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star)
            },
            ColumnSpacing = 10,
            RowSpacing = 10
        };
        Button open = NativeTheme.PrimaryButton(PhoneStrings.Get("OpenFile", "Open file"));
        open.AutomationId = "home-open-file";
        open.Clicked += async (_, _) => await RunAsync(async () =>
        {
            NativeWorkspaceActivationReceipt? activation = await Coordinator.OpenLocalAsync();
            if (activation?.Matches(
                    Coordinator.State,
                    NativeWorkspaceActivationKind.LocalFile) == true)
            {
                await Shell.Current.GoToAsync(_runnerRoute);
            }
        });
        Button create = NativeTheme.SecondaryButton(PhoneStrings.Get("NewRunner", "New runner"));
        create.AutomationId = "home-new-runner";
        create.Clicked += async (_, _) => await RunAsync(() => Coordinator.CreateRunnerAsync());
        quick.Add(open);
        quick.Add(create, 1);
        _body.Add(quick);

        Button applicationSettings = NativeTheme.SecondaryButton(
            PhoneStrings.Get("ApplicationSettings", "Settings"));
        applicationSettings.AutomationId = "home-application-settings";
        applicationSettings.Clicked += async (_, _) =>
            await Navigation.PushAsync(new ApplicationSettingsPage(Coordinator));
        _body.Add(applicationSettings);

        if (Coordinator.State.WorkspaceId is not null)
        {
            Button favorites = NativeTheme.SecondaryButton(
                PhoneStrings.Get("RosterMetadata", "Roster metadata"));
            favorites.AutomationId = "home-roster-favorites";
            favorites.Clicked += async (_, _) => await Navigation.PushAsync(new RosterFavoritesPage(Coordinator));
            _body.Add(favorites);
        }

        OpenWorkspaceState[] otherRunners = Coordinator.State.OpenWorkspaces
            .Where(workspace => workspace.Id != Coordinator.State.WorkspaceId).ToArray();
        if (otherRunners.Length > 0)
        {
            _body.Add(NativeTheme.Eyebrow(PhoneStrings.Get("OpenNow", "Open now")));
            foreach (OpenWorkspaceState workspace in otherRunners)
            {
                string label = !string.IsNullOrWhiteSpace(workspace.Alias) ? workspace.Alias : workspace.Name;
                Button button = NativeTheme.SecondaryButton(
                    string.IsNullOrWhiteSpace(label)
                        ? PhoneStrings.Get("RunnerFallback", "Runner")
                        : label);
                button.Clicked += async (_, _) => await RunAsync(async () =>
                {
                    NativeWorkspaceActivationReceipt? activation =
                        await Coordinator.SwitchWorkspaceAsync(workspace);
                    if (activation?.Matches(
                            Coordinator.State,
                            NativeWorkspaceActivationKind.WorkspaceSwitch) == true
                        && string.Equals(
                            activation.WorkspaceId.Value,
                            workspace.Id.Value,
                            StringComparison.Ordinal))
                    {
                        await Shell.Current.GoToAsync(_runnerRoute);
                    }
                });
                var row = new VerticalStackLayout { Spacing = 6 };
                row.Add(button);
                row.Add(CreateDeleteButton(workspace, "home-delete-runner"));
                _body.Add(NativeTheme.Card(row));
            }
        }

        AddOnlineSection();
        if (!string.IsNullOrWhiteSpace(Coordinator.Notice))
        {
            _body.Add(NativeTheme.Body(Coordinator.Notice, NativeTheme.Muted));
        }
#if CHUMMER_API36_PROOF_INSTRUMENTATION
        PublishApi36ProofState();
#endif
    }

#if CHUMMER_API36_PROOF_INSTRUMENTATION
    private void PublishApi36ProofState()
    {
        Api36ProofStatePublisher.TryPublishTableWizard(
            this,
            Coordinator,
            PhoneShellRoutes.Runners,
            lane: null,
            stage: "runners-ready",
            settled: Coordinator.DebugWorkspaceAuthority is not null,
            checkpointReadStatus: Sr5TableWizardCheckpointReadStatus.Empty,
            session: null,
            transaction: null,
            statusCode: null);
    }
#endif

#if DEBUG
    private void AddDebugWorkspaceAuthority()
    {
        if (Coordinator.DebugWorkspaceAuthority is not { } authority)
        {
            return;
        }

        VerticalStackLayout proof = new() { Spacing = 5 };
        proof.Add(NativeTheme.Eyebrow("Diagnostic workspace authority"));
        AddProofValue(proof, "home-e2e-workspace-id", authority.WorkspaceId);
        AddProofValue(
            proof,
            "home-e2e-content-revision",
            authority.ContentRevision.ToString(System.Globalization.CultureInfo.InvariantCulture));
        AddProofValue(
            proof,
            "home-e2e-saved-revision",
            authority.SavedRevision.ToString(System.Globalization.CultureInfo.InvariantCulture));
        AddProofValue(proof, "home-e2e-payload-sha256", authority.PayloadSha256);
        AddProofValue(proof, "home-e2e-document-sha256", authority.DocumentSha256);
        _body.Add(NativeTheme.Card(proof));
    }

    private static void AddProofValue(
        VerticalStackLayout proof,
        string automationId,
        string value)
    {
        Label label = NativeTheme.Body(value, NativeTheme.Muted);
        label.AutomationId = automationId;
        proof.Add(label);
    }
#endif

    private Button CreateDeleteButton(OpenWorkspaceState workspace, string automationId)
    {
        NativeRunnerDeletionRequest? request = Coordinator.CaptureRunnerDeletionRequest(workspace);
        string name = !string.IsNullOrWhiteSpace(workspace.Alias) ? workspace.Alias : workspace.Name;
        if (string.IsNullOrWhiteSpace(name)) name = PhoneStrings.Get("RunnerFallback", "Runner");
        var button = NativeTheme.SecondaryButton(PhoneStrings.Get("DeleteRunner", "Delete runner"));
        button.AutomationId = automationId;
        button.TextColor = NativeTheme.Danger;
        button.IsEnabled = request is not null;
        SemanticProperties.SetDescription(button, PhoneStrings.Format("DeleteRunnerNamed", "Delete runner {0}", name));
        button.Clicked += async (_, _) => await RunAsync(async () =>
        {
            if (request is null) return;
            long appearance = CaptureAppearanceGeneration();
            NativeRunnerDeletionRequest prepared = await Coordinator.PrepareRunnerDeletionAsync(request);
            if (!IsCurrentAppearanceGeneration(appearance)) return;
            bool confirmed = await DisplayAlertAsync(
                PhoneStrings.Get("DeleteRunner", "Delete runner"),
                PhoneStrings.Format("DeleteRunnerQuestion",
                    "Delete “{0}” from this device, including unsaved changes? This cannot be undone. Online copies, online books and exported files are kept.", name),
                PhoneStrings.Get("DeleteRunnerConfirm", "Delete from device"),
                PhoneStrings.Get("Cancel", "Cancel"));
            await Coordinator.DeleteRunnerAsync(prepared, confirmed && IsCurrentAppearanceGeneration(appearance));
        });
        return button;
    }

    private void AddOnlineSection()
    {
        AndroidAccountLinkSnapshot account = Coordinator.Account;
        VerticalStackLayout online = new() { Spacing = 10 };
        online.Add(NativeTheme.Eyebrow("Chummer.run"));
        online.Add(NativeTheme.Title(
            account.IsLinked
                ? PhoneStrings.Get("HomeOnlineRunners", "Online runners")
                : PhoneStrings.Get("HomeLinkAccount", "Link your account"),
            21));

        if (!account.IsLinked)
        {
            online.Add(NativeTheme.Body(
                PhoneStrings.Get(
                    "HomeOpenAccountRunners",
                    "Open runners saved to your Chummer account."),
                NativeTheme.Muted));
            if (account.IsLoading)
            {
                online.Add(NativeTheme.Body(account.Label, NativeTheme.Muted));
                online.Add(new ActivityIndicator
                {
                    AutomationId = "home-account-recovery",
                    IsRunning = true,
                    Color = NativeTheme.Ink
                });
            }
            Button link = NativeTheme.PrimaryButton(PhoneStrings.Get("LinkAccount", "Link account"));
            link.AutomationId = "home-account-link";
            link.IsEnabled = !account.IsLoading;
            link.Clicked += async (_, _) => await RunAsync(() => Coordinator.BeginAccountLinkAsync());
            online.Add(link);
        }
        else
        {
            Button refresh = NativeTheme.SecondaryButton(
                Coordinator.OnlineCharacters.Count == 0
                    ? PhoneStrings.Get("LoadOnlineRunners", "Load online runners")
                    : PhoneStrings.Get("Refresh", "Refresh"));
            refresh.AutomationId = "home-load-online-runners";
            refresh.Clicked += async (_, _) => await RunLinkedDataRefreshAsync(refresh);
            online.Add(refresh);
            foreach (AndroidOnlineCharacter character in Coordinator.OnlineCharacters.Take(6))
            {
                string name = !string.IsNullOrWhiteSpace(character.Alias)
                    ? character.Alias
                    : !string.IsNullOrWhiteSpace(character.Name)
                        ? character.Name
                        : PhoneStrings.Get("RunnerFallback", "Runner");
                Button button = NativeTheme.SecondaryButton(name);
                button.IsEnabled = Coordinator.HasCompleteOnlineContinuation(character);
                button.Clicked += async (_, _) => await RunAsync(async () =>
                {
                    using NativeWorkspaceContinuationReview? review = await Coordinator.ReviewOnlineAsync(character);
                    if (review is null) return;
                    if (!review.CanConfirm)
                    {
                        await DisplayAlertAsync("Chummer", PhoneStrings.Get("OnlineContinuationRejected",
                            "This workspace cannot be restored safely. Keep the local runner and resolve its conflicts first."), "OK");
                        return;
                    }
                    bool confirmed = await DisplayAlertAsync(
                        PhoneStrings.Get("OnlineContinuationTitle", "Restore workspace"),
                        PhoneStrings.Format("OnlineContinuationReview",
                            "Open {0} with its complete wizard history? Content revision: {1}; saved revision: {2}. Local conflicts will not be overwritten.",
                            name, review.ContentRevision, review.SavedRevision),
                        PhoneStrings.Get("OnlineContinuationConfirm", "Restore and open"),
                        PhoneStrings.Get("Cancel", "Cancel"));
                    var restored = await Coordinator.ConfirmOnlineAsync(review, confirmed);
                    if (confirmed && restored.Restore.Outcome is not (
                        Chummer.Contracts.Workspaces.WorkspaceContinuationRestoreOutcome.Applied
                        or Chummer.Contracts.Workspaces.WorkspaceContinuationRestoreOutcome.Recovered
                        or Chummer.Contracts.Workspaces.WorkspaceContinuationRestoreOutcome.AlreadyCurrent
                        or Chummer.Contracts.Workspaces.WorkspaceContinuationRestoreOutcome.Canceled))
                    {
                        await DisplayAlertAsync("Chummer", PhoneStrings.Get("OnlineContinuationRejected",
                            "This workspace cannot be restored safely. Keep the local runner and resolve its conflicts first."), "OK");
                    }
                    NativeWorkspaceActivationReceipt? activation = restored.Activation;
                    if (Coordinator.IsWorkspaceActivationCurrent(activation, NativeWorkspaceActivationKind.OnlineCharacter))
                    {
                        await Shell.Current.GoToAsync(_runnerRoute);
                    }
                });
                online.Add(button);
                if (!button.IsEnabled)
                    online.Add(NativeTheme.Body(PhoneStrings.Get("OnlineContinuationUnavailable",
                        "This runner has no complete restorable workspace. Refresh or upload it from a supported client."), NativeTheme.Muted));
            }
        }

        _body.Add(NativeTheme.Card(online));
    }
}
