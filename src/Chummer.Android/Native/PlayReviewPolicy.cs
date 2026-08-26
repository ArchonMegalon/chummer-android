using System.Diagnostics;
using System.Text.Json;

namespace Chummer.Android.Native;

public sealed record PlayReviewState(
    long ForegroundMilliseconds,
    DateTimeOffset? LastAttemptUtc,
    string? LastAttemptVersion,
    string InstallIdentity = "")
{
    public static PlayReviewState Empty { get; } = new(0, null, null);
}

public sealed record PlayReviewPolicyOptions(
    long MinimumForegroundMilliseconds,
    TimeSpan CrossVersionCooldown,
    bool BypassEligibilityHistory)
{
    public static PlayReviewPolicyOptions Production { get; } = new(
        (long)TimeSpan.FromHours(1).TotalMilliseconds,
        TimeSpan.FromDays(30),
        BypassEligibilityHistory: false);

    public static PlayReviewPolicyOptions DebugOverride { get; } = new(
        MinimumForegroundMilliseconds: 0,
        CrossVersionCooldown: TimeSpan.Zero,
        BypassEligibilityHistory: true);
}

public static class PlayReviewPolicy
{
    public const string CanonicalApplicationId = "com.myexternalbrain.chummer";
    public const string GooglePlayInstallerPackage = "com.android.vending";
    public static TimeSpan MeaningfulSuccessWindow { get; } = TimeSpan.FromMinutes(2);

    public static bool IsEligibleInstallation(
        PlayReviewInstallContext install,
        bool explicitTestOverride)
    {
        ArgumentNullException.ThrowIfNull(install);
        if (explicitTestOverride)
        {
            return true;
        }

        return install.IsReleaseBuild
               && string.Equals(
                   install.ApplicationId,
                   CanonicalApplicationId,
                   StringComparison.Ordinal)
               && string.Equals(
                   install.InstallerPackageName,
                   GooglePlayInstallerPackage,
                   StringComparison.Ordinal)
               && !string.IsNullOrWhiteSpace(install.InstallIdentity);
    }

    public static bool ShouldAttempt(
        PlayReviewState state,
        string appVersion,
        DateTimeOffset nowUtc,
        PlayReviewPolicyOptions options)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(appVersion))
        {
            return false;
        }

        if (options.BypassEligibilityHistory)
        {
            return true;
        }

        if (state.ForegroundMilliseconds < options.MinimumForegroundMilliseconds)
        {
            return false;
        }

        bool hasAttemptVersion = !string.IsNullOrWhiteSpace(state.LastAttemptVersion);
        bool hasAttemptTime = state.LastAttemptUtc is not null;
        if (hasAttemptVersion != hasAttemptTime)
        {
            // A partial or damaged history must fail closed instead of producing repeated prompts.
            return false;
        }

        if (!hasAttemptVersion)
        {
            return true;
        }

        if (string.Equals(state.LastAttemptVersion, appVersion, StringComparison.Ordinal))
        {
            return false;
        }

        TimeSpan elapsed = nowUtc - state.LastAttemptUtc!.Value;
        return elapsed >= options.CrossVersionCooldown;
    }
}

public sealed record PlayReviewInstallContext(
    string ApplicationId,
    string? InstallerPackageName,
    string InstallIdentity,
    bool IsReleaseBuild);

public sealed record PlayReviewSafetyContext(
    bool IsExplicitSafeSurface,
    bool IsRootNavigation,
    bool HasModal,
    bool HasActiveDialog,
    bool HasUnsavedMutation,
    bool HasActionInFlight,
    bool HasBusyWork = false)
{
    public bool IsSafe => IsExplicitSafeSurface
                          && IsRootNavigation
                          && !HasModal
                          && !HasActiveDialog
                          && !HasUnsavedMutation
                          && !HasActionInFlight
                          && !HasBusyWork;
}

public interface IPlayReviewClock
{
    DateTimeOffset UtcNow { get; }

    long MonotonicMilliseconds { get; }
}

public sealed class SystemPlayReviewClock : IPlayReviewClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    public long MonotonicMilliseconds => checked(
        (long)(Stopwatch.GetTimestamp() * 1000d / Stopwatch.Frequency));
}

public interface IPlayReviewStateStore
{
    PlayReviewState Load();

    void Save(PlayReviewState state);
}

public sealed class FilePlayReviewStateStore : IPlayReviewStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly string _path;

    public FilePlayReviewStateStore(string stateDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stateDirectory);
        _path = Path.Combine(stateDirectory, "play-review-policy.json");
    }

    public PlayReviewState Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return PlayReviewState.Empty;
            }

            PlayReviewState? state = JsonSerializer.Deserialize<PlayReviewState>(
                File.ReadAllText(_path),
                JsonOptions);
            if (state is null || state.ForegroundMilliseconds < 0)
            {
                return PlayReviewState.Empty;
            }

            return state;
        }
        catch (Exception)
        {
            return PlayReviewState.Empty;
        }
    }

    public void Save(PlayReviewState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        string? directory = Path.GetDirectoryName(_path);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("The Play review policy store has no parent directory.");
        }

        Directory.CreateDirectory(directory);
        string temporaryPath = _path + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(state, JsonOptions));
        File.Move(temporaryPath, _path, overwrite: true);
    }
}

public interface IPlayReviewLauncher
{
    PlayReviewInstallContext InstallContext { get; }

    bool IsRuntimeAvailable { get; }

    Task RequestReviewAsync(CancellationToken cancellationToken = default);

    Task OpenStoreListingAsync(CancellationToken cancellationToken = default);
}

public interface IPlayReviewService
{
    void OnForegrounded();

    void CheckpointForegroundUse();

    void OnBackgrounded();

    void ConfigureDebugOverride(bool enabled);

    void SignalMeaningfulSuccess();

    Task<bool> TryRequestAtSafeMomentAsync(
        PlayReviewSafetyContext safety,
        CancellationToken cancellationToken = default);

    Task OpenStoreListingAsync(CancellationToken cancellationToken = default);
}

public sealed class PlayReviewService : IPlayReviewService, IDisposable
{
    private readonly object _sync = new();
    private readonly SemaphoreSlim _attemptGate = new(1, 1);
    private readonly IPlayReviewStateStore _store;
    private readonly IPlayReviewClock _clock;
    private readonly IPlayReviewLauncher _launcher;
    private readonly string _appVersion;
    private readonly bool _automaticFlowEnabled;
    private PlayReviewState _state;
    private long? _foregroundAnchorMilliseconds;
    private bool _debugOverride;
    private long? _meaningfulSuccessAtMilliseconds;
    private bool _disposed;

    public PlayReviewService(
        IPlayReviewStateStore store,
        IPlayReviewClock clock,
        IPlayReviewLauncher launcher,
        string appVersion,
        bool automaticFlowEnabled = true)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));
        _appVersion = string.IsNullOrWhiteSpace(appVersion)
            ? throw new ArgumentException("An app version is required.", nameof(appVersion))
            : appVersion;
        _automaticFlowEnabled = automaticFlowEnabled;
        PlayReviewState loaded = Sanitize(_store.Load());
        _state = string.Equals(
            loaded.InstallIdentity,
            _launcher.InstallContext.InstallIdentity,
            StringComparison.Ordinal)
            ? loaded
            : PlayReviewState.Empty with
            {
                InstallIdentity = _launcher.InstallContext.InstallIdentity
            };
        if (_state != loaded)
        {
            _ = TryPersistLocked();
        }
    }

    public PlayReviewState State
    {
        get
        {
            lock (_sync)
            {
                return _state;
            }
        }
    }

    public void OnForegrounded()
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            if (_foregroundAnchorMilliseconds is null)
            {
                _foregroundAnchorMilliseconds = _clock.MonotonicMilliseconds;
                _meaningfulSuccessAtMilliseconds = null;
            }
        }
    }

    public void CheckpointForegroundUse()
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            CheckpointForegroundUseLocked();
        }
    }

    public void OnBackgrounded()
    {
        lock (_sync)
        {
            if (_disposed || _foregroundAnchorMilliseconds is null)
            {
                return;
            }

            CheckpointForegroundUseLocked();
            _foregroundAnchorMilliseconds = null;
        }
    }

    public void ConfigureDebugOverride(bool enabled)
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            _debugOverride = enabled;
        }
    }

    public void SignalMeaningfulSuccess()
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            _meaningfulSuccessAtMilliseconds = _clock.MonotonicMilliseconds;
        }
    }

    public async Task<bool> TryRequestAtSafeMomentAsync(
        PlayReviewSafetyContext safety,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(safety);
        if (!safety.IsSafe || !_automaticFlowEnabled)
        {
            return false;
        }

        await _attemptGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            PlayReviewState attemptedState;
            lock (_sync)
            {
                ThrowIfDisposed();
                CheckpointForegroundUseLocked();
                PlayReviewInstallContext install = _launcher.InstallContext;
                if (!BindCurrentInstallIdentityLocked(install.InstallIdentity))
                {
                    return false;
                }
                PlayReviewPolicyOptions options = _debugOverride
                    ? PlayReviewPolicyOptions.DebugOverride
                    : PlayReviewPolicyOptions.Production;
                if (!safety.IsSafe
                    || !_automaticFlowEnabled
                    || !HasFreshMeaningfulSuccessLocked()
                    || !_launcher.IsRuntimeAvailable
                    || !PlayReviewPolicy.IsEligibleInstallation(
                        install,
                        _debugOverride)
                    || !PlayReviewPolicy.ShouldAttempt(_state, _appVersion, _clock.UtcNow, options))
                {
                    return false;
                }

                attemptedState = _state with
                {
                    LastAttemptUtc = _clock.UtcNow,
                    LastAttemptVersion = _appVersion
                };
                _state = attemptedState;
                _meaningfulSuccessAtMilliseconds = null;
                if (!TryPersistLocked())
                {
                    // Do not call Play unless the at-most-once attempt is durable.
                    return false;
                }
            }

            try
            {
                await _launcher.RequestReviewAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Play quota, missing services, and flow errors must not alter normal app flow.
            }

            return true;
        }
        finally
        {
            _attemptGate.Release();
        }
    }

    public async Task OpenStoreListingAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _launcher.OpenStoreListingAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // A device without a compatible store or browser remains usable.
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            if (_foregroundAnchorMilliseconds is not null)
            {
                CheckpointForegroundUseLocked();
                _foregroundAnchorMilliseconds = null;
            }
            _disposed = true;
        }
        _attemptGate.Dispose();
    }

    private void CheckpointForegroundUseLocked()
    {
        if (_foregroundAnchorMilliseconds is not long anchor)
        {
            return;
        }

        long now = _clock.MonotonicMilliseconds;
        long elapsed = Math.Max(0, now - anchor);
        _foregroundAnchorMilliseconds = now;
        if (elapsed == 0)
        {
            return;
        }

        _state = _state with
        {
            ForegroundMilliseconds = SaturatingAdd(_state.ForegroundMilliseconds, elapsed)
        };
        _ = TryPersistLocked();
    }

    private bool TryPersistLocked()
    {
        try
        {
            _store.Save(_state);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private bool BindCurrentInstallIdentityLocked(string installIdentity)
    {
        if (string.IsNullOrWhiteSpace(installIdentity))
        {
            // Production must never consume restored policy state without a current
            // no-backup identity. The explicit Debug-only override remains able to
            // exercise a fake/non-Play manager on unusual test environments.
            return _debugOverride;
        }

        if (string.Equals(_state.InstallIdentity, installIdentity, StringComparison.Ordinal))
        {
            return true;
        }

        _state = PlayReviewState.Empty with { InstallIdentity = installIdentity };
        _meaningfulSuccessAtMilliseconds = null;
        return TryPersistLocked();
    }

    private bool HasFreshMeaningfulSuccessLocked()
    {
        if (_meaningfulSuccessAtMilliseconds is not long signaledAt)
        {
            return false;
        }

        long elapsed = _clock.MonotonicMilliseconds - signaledAt;
        bool fresh = elapsed >= 0
                     && elapsed <= PlayReviewPolicy.MeaningfulSuccessWindow.TotalMilliseconds;
        if (!fresh)
        {
            _meaningfulSuccessAtMilliseconds = null;
        }
        return fresh;
    }

    private static PlayReviewState Sanitize(PlayReviewState? state)
        => state is null || state.ForegroundMilliseconds < 0
            ? PlayReviewState.Empty
            : state;

    private static long SaturatingAdd(long left, long right)
        => left > long.MaxValue - right ? long.MaxValue : left + right;

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(_disposed, this);
}
