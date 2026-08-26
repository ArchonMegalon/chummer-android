using Chummer.Android.Native;

internal static class Program
{
    private static async Task Main()
    {
        PolicyEnforcesThresholdVersionCooldownAndClockRollback();
        InstallGateRejectsNonCanonicalProductionBuilds();
        SafetyRejectsEveryMutationBoundary();
        await ForegroundAccountingIsMonotonicSessionSafeAndSuccessBound();
        await InstallIdentityRejectsRestoredBackupAndKillSwitchWins();
        await FakeLauncherAndSilentPlayFailureAreInjectable();
        FileStoreRoundTripsOnlyLocalPolicyData();
        LocalizedManualActionStringsResolveFromResources();
        Console.WriteLine("Play review policy tests passed: 8");
    }

    private static void PolicyEnforcesThresholdVersionCooldownAndClockRollback()
    {
        DateTimeOffset now = new(2026, 8, 26, 12, 0, 0, TimeSpan.Zero);
        long threshold = PlayReviewPolicyOptions.Production.MinimumForegroundMilliseconds;
        Require(!Eligible(new PlayReviewState(threshold - 1, null, null), "v10", now));
        Require(Eligible(new PlayReviewState(threshold, null, null), "v10", now));
        PlayReviewState prior = new(threshold, now - TimeSpan.FromDays(31), "v10");
        Require(!Eligible(prior, "v10", now));
        Require(!Eligible(prior with { LastAttemptUtc = now - TimeSpan.FromDays(29) }, "v11", now));
        Require(Eligible(prior with { LastAttemptUtc = now - TimeSpan.FromDays(30) }, "v11", now));
        Require(!Eligible(prior with { LastAttemptUtc = now + TimeSpan.FromDays(1) }, "v11", now));
        Require(!Eligible(prior with { LastAttemptUtc = null }, "v11", now));
    }

    private static void InstallGateRejectsNonCanonicalProductionBuilds()
    {
        PlayReviewInstallContext canonical = CanonicalInstall("install-current");
        Require(PlayReviewPolicy.IsEligibleInstallation(canonical, false));
        foreach (PlayReviewInstallContext ineligible in new[]
                 {
                     canonical with { ApplicationId = "com.myexternalbrain.chummer.sidecar" },
                     canonical with { InstallerPackageName = "com.android.shell" },
                     canonical with { InstallerPackageName = "org.fdroid.fdroid" },
                     canonical with { IsReleaseBuild = false },
                     canonical with { InstallIdentity = string.Empty }
                 })
        {
            Require(!PlayReviewPolicy.IsEligibleInstallation(ineligible, false));
            Require(PlayReviewPolicy.IsEligibleInstallation(ineligible, true));
        }
    }

    private static void SafetyRejectsEveryMutationBoundary()
    {
        PlayReviewSafetyContext safe = SafeContext();
        Require(safe.IsSafe);
        Require(!(safe with { IsExplicitSafeSurface = false }).IsSafe);
        Require(!(safe with { IsRootNavigation = false }).IsSafe);
        Require(!(safe with { HasModal = true }).IsSafe);
        Require(!(safe with { HasActiveDialog = true }).IsSafe);
        Require(!(safe with { HasUnsavedMutation = true }).IsSafe);
        Require(!(safe with { HasActionInFlight = true }).IsSafe);
        Require(!(safe with { HasBusyWork = true }).IsSafe);
    }

    private static async Task ForegroundAccountingIsMonotonicSessionSafeAndSuccessBound()
    {
        long minute = (long)TimeSpan.FromMinutes(1).TotalMilliseconds;
        var clock = new FakeClock { UtcNow = DateTimeOffset.UtcNow, MonotonicMilliseconds = 10_000 };
        var store = new FakeStore(new PlayReviewState(59 * minute, null, null, "test-install"));
        var launcher = new FakeLauncher { IsRuntimeAvailable = true };
        using var service = new PlayReviewService(store, clock, launcher, "v10");

        clock.MonotonicMilliseconds += 90_000;
        service.CheckpointForegroundUse();
        Require(service.State.ForegroundMilliseconds == 59 * minute);
        service.OnForegrounded();
        service.OnForegrounded();
        clock.MonotonicMilliseconds += 30_000;
        service.CheckpointForegroundUse();
        clock.MonotonicMilliseconds += 30_000;
        service.OnBackgrounded();
        service.OnBackgrounded();
        clock.MonotonicMilliseconds += 90_000;
        service.CheckpointForegroundUse();
        Require(service.State.ForegroundMilliseconds == 60 * minute);
        Require(store.State.ForegroundMilliseconds == 60 * minute);
        Require(!await service.TryRequestAtSafeMomentAsync(SafeContext()));
        service.SignalMeaningfulSuccess();
        clock.MonotonicMilliseconds +=
            (long)PlayReviewPolicy.MeaningfulSuccessWindow.TotalMilliseconds + 1;
        Require(!await service.TryRequestAtSafeMomentAsync(SafeContext()));
        Require(launcher.RequestCount == 0);
        service.SignalMeaningfulSuccess();
        Require(await service.TryRequestAtSafeMomentAsync(SafeContext()));
        Require(launcher.RequestCount == 1);
        service.SignalMeaningfulSuccess();
        Require(!await service.TryRequestAtSafeMomentAsync(SafeContext()));
        Require(launcher.RequestCount == 1);
    }

    private static async Task InstallIdentityRejectsRestoredBackupAndKillSwitchWins()
    {
        var clock = new FakeClock { UtcNow = DateTimeOffset.UtcNow, MonotonicMilliseconds = 1 };
        var launcher = new FakeLauncher
        {
            IsRuntimeAvailable = true,
            InstallContext = CanonicalInstall("install-current")
        };
        var restored = new FakeStore(new PlayReviewState(
            PlayReviewPolicyOptions.Production.MinimumForegroundMilliseconds,
            null,
            null,
            "foreign-install"));
        using var rebound = new PlayReviewService(restored, clock, launcher, "v10");
        Require(rebound.State.ForegroundMilliseconds == 0);
        Require(rebound.State.InstallIdentity == "install-current");

        var rotatingStore = new FakeStore(new PlayReviewState(
            PlayReviewPolicyOptions.Production.MinimumForegroundMilliseconds,
            null,
            null,
            "install-current"));
        using var rotating = new PlayReviewService(rotatingStore, clock, launcher, "v10");
        launcher.InstallContext = CanonicalInstall("install-next");
        rotating.SignalMeaningfulSuccess();
        Require(!await rotating.TryRequestAtSafeMomentAsync(SafeContext()));
        Require(rotating.State.ForegroundMilliseconds == 0);
        Require(rotating.State.InstallIdentity == "install-next");
        Require(launcher.RequestCount == 0);

        using var killed = new PlayReviewService(
            new FakeStore(PlayReviewState.Empty with { InstallIdentity = "install-current" }),
            clock,
            launcher,
            "v10",
            automaticFlowEnabled: false);
        killed.ConfigureDebugOverride(true);
        killed.SignalMeaningfulSuccess();
        Require(!await killed.TryRequestAtSafeMomentAsync(SafeContext()));
        PlayReviewState beforeManualAction = killed.State;
        await killed.OpenStoreListingAsync();
        Require(launcher.OpenListingCount == 1);
        Require(killed.State == beforeManualAction);
    }

    private static async Task FakeLauncherAndSilentPlayFailureAreInjectable()
    {
        var clock = new FakeClock { UtcNow = DateTimeOffset.UtcNow, MonotonicMilliseconds = 1 };
        var launcher = new FakeLauncher
        {
            IsRuntimeAvailable = true,
            ThrowOnRequest = true,
            InstallContext = CanonicalInstall("test-install")
        };
        var store = new FakeStore(PlayReviewState.Empty with { InstallIdentity = "test-install" });
        using var service = new PlayReviewService(store, clock, launcher, "debug");
        service.ConfigureDebugOverride(true);
        service.SignalMeaningfulSuccess();
        Require(await service.TryRequestAtSafeMomentAsync(SafeContext()));
        Require(launcher.RequestCount == 1);
        Require(store.State.LastAttemptVersion == "debug");

        var unavailableLauncher = new FakeLauncher { IsRuntimeAvailable = false };
        var unavailableStore = new FakeStore(PlayReviewState.Empty with { InstallIdentity = "test-install" });
        using var unavailable = new PlayReviewService(unavailableStore, clock, unavailableLauncher, "debug2");
        unavailable.ConfigureDebugOverride(true);
        unavailable.SignalMeaningfulSuccess();
        Require(!await unavailable.TryRequestAtSafeMomentAsync(SafeContext()));
        Require(unavailableStore.State.LastAttemptUtc is null);
    }

    private static void FileStoreRoundTripsOnlyLocalPolicyData()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"chummer-review-{Guid.NewGuid():N}");
        try
        {
            var store = new FilePlayReviewStateStore(directory);
            var expected = new PlayReviewState(
                3_600_000,
                new DateTimeOffset(2026, 8, 26, 12, 0, 0, TimeSpan.Zero),
                "v10",
                "install-current");
            store.Save(expected);
            Require(store.Load() == expected);
            string json = File.ReadAllText(Path.Combine(directory, "play-review-policy.json"));
            foreach (string required in new[]
                     { "foregroundMilliseconds", "lastAttemptUtc", "lastAttemptVersion", "installIdentity" })
                Require(json.Contains(required, StringComparison.Ordinal));
            foreach (string forbidden in new[]
                     { "rating", "stars", "displayed", "submitted", "analytics", "telemetry" })
                Require(!json.Contains(forbidden, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }

    private static void LocalizedManualActionStringsResolveFromResources()
    {
        Require(
            PlayReviewStrings.RateOnGooglePlay(
                System.Globalization.CultureInfo.GetCultureInfo("en"))
            == "Rate Chummer on Google Play");
        Require(
            PlayReviewStrings.RateOnGooglePlay(
                System.Globalization.CultureInfo.GetCultureInfo("de"))
            == "Chummer bei Google Play bewerten");
    }

    private static bool Eligible(PlayReviewState state, string version, DateTimeOffset now)
        => PlayReviewPolicy.ShouldAttempt(state, version, now, PlayReviewPolicyOptions.Production);

    private static PlayReviewInstallContext CanonicalInstall(string identity)
        => new(
            PlayReviewPolicy.CanonicalApplicationId,
            PlayReviewPolicy.GooglePlayInstallerPackage,
            identity,
            IsReleaseBuild: true);

    private static PlayReviewSafetyContext SafeContext()
        => new(true, true, false, false, false, false);

    private static void Require(bool condition)
    {
        if (!condition)
            throw new InvalidOperationException("Play review policy assertion failed.");
    }

    private sealed class FakeClock : IPlayReviewClock
    {
        public DateTimeOffset UtcNow { get; set; }
        public long MonotonicMilliseconds { get; set; }
    }

    private sealed class FakeStore : IPlayReviewStateStore
    {
        public FakeStore(PlayReviewState state) => State = state;
        public PlayReviewState State { get; private set; }
        public PlayReviewState Load() => State;
        public void Save(PlayReviewState state) => State = state;
    }

    private sealed class FakeLauncher : IPlayReviewLauncher
    {
        public PlayReviewInstallContext InstallContext { get; set; } = CanonicalInstall("test-install");
        public bool IsRuntimeAvailable { get; init; }
        public bool ThrowOnRequest { get; init; }
        public int RequestCount { get; private set; }
        public int OpenListingCount { get; private set; }

        public Task RequestReviewAsync(CancellationToken cancellationToken = default)
        {
            RequestCount++;
            return ThrowOnRequest
                ? Task.FromException(new InvalidOperationException("fake Play failure"))
                : Task.CompletedTask;
        }

        public Task OpenStoreListingAsync(CancellationToken cancellationToken = default)
        {
            OpenListingCount++;
            return Task.CompletedTask;
        }
    }
}
