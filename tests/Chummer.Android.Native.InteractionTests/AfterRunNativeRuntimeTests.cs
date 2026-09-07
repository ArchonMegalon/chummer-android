using System.Globalization;
using System.Reflection;
using Chummer.Android.Native;
using Chummer.Android.Platform;
using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;
using Chummer.Desktop.Runtime;
using Chummer.Infrastructure.Workspaces;
using Chummer.Presentation;
using Chummer.Presentation.Overview;
using Chummer.Presentation.Shell;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Maui.Storage;

internal static partial class AfterRunAuthorityHarness
{
    public static async Task RunNativeRuntimeCasesAsync(string contentRoot)
    {
        if (!Path.IsPathFullyQualified(contentRoot) || !Directory.Exists(Path.Combine(contentRoot, "data")))
            throw new ArgumentException("Supply an explicit Core directory containing data/.", nameof(contentRoot));
        await NativeRewardCommitReloadsRealPresenterAndShellAsync(contentRoot);
        await NativeRewardRefreshFailureRecoversWithoutCreditAsync(contentRoot, cancel: false);
        await NativeRewardRefreshFailureRecoversWithoutCreditAsync(contentRoot, cancel: true);
        Console.WriteLine("PASS 3 actual native/runtime/file-store integration cases");
    }

    private static async Task NativeRewardCommitReloadsRealPresenterAndShellAsync(string contentRoot)
    {
        await using var runtime = new NativeRewardRuntime(contentRoot);
        await runtime.LoadRunnerAsync();
        var initialReward = new WorkspaceCharacterAfterRunRewardService(new FileWorkspaceStore(runtime.StateDirectory))
            .Read(runtime.Id);
        Require(initialReward.Outcome == CharacterAfterRunRewardOutcome.Available,
            $"Imported canonical runtime document is unavailable to Core rewards: {initialReward.Outcome}; {initialReward.Error}");
        var model = await runtime.PrepareRewardAsync();
        await model.ConfirmAsync();
        Require(model.CanContinue && model.Handoff is not null,
            $"Real native presenter/shell reload did not produce a handoff: {model.Status}; {runtime.Presenter.State.Error}");
        Require(runtime.Coordinator.State.ContentRevision == 2 && runtime.Coordinator.State.SavedRevision == 2
                && runtime.Presenter.State.Progress?.Karma == 38,
            "Actual presenter did not load the committed saved revision and Karma.");
        Require(model.Handoff!.Snapshot.AvailableKarma == 38 && model.Handoff.Snapshot.AvailableNuyen == 13500,
            "Fresh Core balances differ from the committed reward.");
        Require(runtime.Shell.State.ActiveWorkspaceId == runtime.Id,
            "Actual shell did not follow the reloaded runner.");
        var cold = new WorkspaceCharacterAfterRunRewardService(new FileWorkspaceStore(runtime.StateDirectory));
        Require(cold.Read(runtime.Id).Snapshot?.AvailableKarma == 38
                && cold.Lookup(runtime.Id, model.OperationId, model.Checkpoint!.CommandDigest).Receipt is not null,
            "Cold Core file-store read did not find the exact committed reward.");
        var beforeResume = new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!;
        var reopened = runtime.Coordinator.CreateAfterRunRewardModel(DateTime.Now, runtime.Owner);
        await reopened.InitializeAsync();
        Require(reopened.RecordedRewards.Any(r => r.Command.OperationId == model.OperationId),
            "A new native model could not discover the recorded reward.");
        await reopened.ResumeRecordedRewardAsync(model.OperationId);
        Require(reopened.CanContinue && reopened.OperationId == model.OperationId
                && reopened.Checkpoint!.CommandDigest == model.Checkpoint!.CommandDigest,
            "Explicit history resume did not retain the exact original command.");
        RequireSameRewardDocument(beforeResume, new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!);
        Console.WriteLine("PASS native reward commit, actual presenter/shell reload and recorded-history resume");
    }

    private static async Task NativeRewardRefreshFailureRecoversWithoutCreditAsync(string contentRoot, bool cancel)
    {
        await using var runtime = new NativeRewardRuntime(contentRoot);
        await runtime.LoadRunnerAsync();
        var model = await runtime.PrepareRewardAsync();
        using var cancellation = new CancellationTokenSource();
        int interruptions = 0;
        EventHandler observer = (_, _) =>
        {
            if (cancel && runtime.Presenter.State.IsBusy && interruptions == 0)
            {
                interruptions++;
                cancellation.Cancel();
            }
        };
        runtime.Presenter.StateChanged += observer;
        runtime.Settings.FailSelectedWorkspaceWrite = !cancel;
        try { await model.ConfirmAsync(cancellation.Token); }
        finally
        {
            runtime.Presenter.StateChanged -= observer;
            runtime.Settings.FailSelectedWorkspaceWrite = false;
        }
        Require(cancel ? interruptions == 1 : runtime.Settings.RejectedWrites == 1,
            "The real native refresh did not reach its controlled interruption boundary.");
        Require(model.Status == Sr5AfterRunRewardPhoneStatus.RefreshRequired && model.Handoff is null
                && model.Checkpoint?.Phase == Sr5AfterRunRewardCheckpointPhase.Applied
                && model.CanRecover && !model.CanRetry && !model.CanConfirm,
            $"Interrupted post-commit refresh lost recovery safety: {model.Status}; {runtime.Presenter.State.Error}");
        var cold = new FileWorkspaceStore(runtime.StateDirectory);
        var committed = cold.Get(runtime.Id).Value!;
        Require(committed.ContentRevision == 2 && committed.SavedRevision == 2
                && committed.Document.AuxiliaryState.CharacterAfterRunRewardReceipts?.Count == 1,
            "The reward was not durably committed before refresh was interrupted.");
        string digest = model.Checkpoint!.CommandDigest;
        Guid operationId = model.OperationId;
        await model.RecoverAsync();
        Require(model.CanContinue && model.Status == Sr5AfterRunRewardPhoneStatus.Recorded
                && runtime.Presenter.State.Error is null && runtime.Presenter.State.Progress?.Karma == 38
                && runtime.Presenter.State.SavedRevision == 2 && runtime.Shell.State.ActiveWorkspaceId == runtime.Id
                && model.OperationId == operationId && model.Checkpoint!.CommandDigest == digest,
            $"Real reload/lookup did not recover the committed reward: {model.Status}; {runtime.Presenter.State.Error}");
        RequireSameRewardDocument(committed, new FileWorkspaceStore(runtime.StateDirectory).Get(runtime.Id).Value!);
        Console.WriteLine($"PASS native reward refresh {(cancel ? "cancellation" : "preferences failure")} and exact read-only recovery");
    }

    private static void RequireSameRewardDocument(WorkspaceStoredDocument before, WorkspaceStoredDocument after)
    {
        Require(before.ContentRevision == after.ContentRevision && before.SavedRevision == after.SavedRevision
                && before.Document.Content == after.Document.Content
                && before.Document.AuxiliaryStateDigest == after.Document.AuxiliaryStateDigest,
            "Native refresh/recovery changed the persisted character or reward ledger.");
    }

    private sealed class NativeRewardRuntime : IAsyncDisposable
    {
        private readonly Dictionary<string, string?> _environment = new();
        private readonly IPreferences _priorPreferences;
        private readonly MethodInfo _setPreferences;
        private readonly ServiceProvider _provider;
        public string StateDirectory { get; }
        public readonly TestOwner Owner = new(OwnerId);
        public readonly RuntimePreferences Settings = new();
        public readonly IChummerClient Client;
        public readonly CharacterOverviewPresenter Presenter;
        public readonly ShellPresenter Shell;
        public readonly RunnerSessionCoordinator Coordinator;
        public CharacterWorkspaceId Id;

        public NativeRewardRuntime(string contentRoot)
        {
            _priorPreferences = Preferences.Default;
            _setPreferences = typeof(Preferences).GetMethod("SetDefault",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new InvalidOperationException("MAUI Preferences test adapter entry is unavailable.");
            StateDirectory = Directory.CreateTempSubdirectory("chummer-native-reward-runtime-").FullName;
            try
            {
                _setPreferences.Invoke(null, [Settings]);
                SetEnvironment("CHUMMER_STATE_PATH", StateDirectory);
                SetEnvironment("CHUMMER_WORKSPACE_STORE_PATH", StateDirectory);
                SetEnvironment("CHUMMER_CLIENT_MODE", "local");
                SetEnvironment("CHUMMER_DESKTOP_CLIENT_MODE", "local");
                var services = new ServiceCollection();
                services.AddChummerLocalRuntimeClient(contentRoot, contentRoot);
                services.Replace(ServiceDescriptor.Singleton<IWorkspaceStore>(new FileWorkspaceStore(StateDirectory)));
                services.AddSingleton<ICommandAvailabilityEvaluator, DefaultCommandAvailabilityEvaluator>();
                services.AddSingleton<IShellSurfaceResolver, ShellSurfaceResolver>();
                _provider = services.BuildServiceProvider();
                Client = _provider.GetRequiredService<IChummerClient>();
                Require(Client is InProcessChummerClient, "Runtime integration must never use a network client.");
                Shell = new ShellPresenter(Client);
                Presenter = new CharacterOverviewPresenter(Client, shellPresenter: Shell);
                var store = _provider.GetRequiredService<IWorkspaceStore>();
                var checkpoints = new Sr5AfterRunRewardCheckpointStore(
                    new FileSr5AfterRunRewardJournalBackend(StateDirectory),
                    new Sr5CareerMutationOwnerStore(new MemoryBackend()));
                Coordinator = new RunnerSessionCoordinator(Presenter, Client, new WorkspaceOperationCoordinator(),
                    null!, null!, null!, null!, Shell,
                    _provider.GetRequiredService<IShellSurfaceResolver>(),
                    _provider.GetRequiredService<ICommandAvailabilityEvaluator>(),
                    null!, null!, null!, StrictPageProxy.Create<IAndroidAccountLinkService>(), null!, null!,
                    afterRunRewardService: new WorkspaceCharacterAfterRunRewardService(store),
                    afterRunRewardCheckpoints: checkpoints);
            }
            catch
            {
                try { _provider?.Dispose(); }
                finally { RestoreHost(); }
                throw;
            }
        }

        public async Task LoadRunnerAsync()
        {
            var imported = await Client.ImportAsync(new WorkspaceImportDocument(
                """
                <character><name>Native reward runner</name><gameedition>SR5</gameedition>
                <metatype>Human</metatype><buildmethod>Priority</buildmethod>
                <createdversion>5.225.0</createdversion><appversion>5.225.0</appversion>
                <created>True</created><karma>30</karma><nuyen>1000</nuyen>
                <streetcred>10</streetcred><notoriety>4</notoriety><publicawareness>6</publicawareness>
                <contacts/><expenses/><notes>Retain unrelated data</notes></character>
                """, "sr5"), default);
            Id = imported.Id;
            Require((await Client.SaveAsync(Id, default)).Success, "Imported runtime runner could not be saved.");
            var validation = await Client.ValidateAsync(Id, default);
            Require(validation.IsValid, "Runtime fixture failed canonical validation: "
                + System.Text.Json.JsonSerializer.Serialize(validation.Issues));
            await Presenter.LoadAsync(Id, default);
            Require(Presenter.State.WorkspaceId == Id && Presenter.State.ContentRevision == 1
                    && Presenter.State.SavedRevision == 1 && Presenter.State.Profile?.Created == true
                    && Presenter.State.Rules?.GameEdition == "SR5" && Presenter.State.Error is null,
                $"Actual presenter failed to load initial saved SR5 runner: {Presenter.State.Error}");
        }

        public async Task<Sr5AfterRunRewardPhoneModel> PrepareRewardAsync()
        {
            var model = Coordinator.CreateAfterRunRewardModel(new DateTime(2026, 9, 7, 12, 0, 0), Owner);
            await model.InitializeAsync();
            Require(model.UpdateDraft(model.Draft with { Karma = "8", Nuyen = "12500", Reason = "Native integration run" }),
                "The current native reward draft was not editable.");
            await model.PreviewAsync(CultureInfo.InvariantCulture);
            Require(model.CanConfirm, $"Real runtime preview unavailable: {model.Status}");
            return model;
        }

        private void SetEnvironment(string name, string? value)
        {
            _environment.Add(name, Environment.GetEnvironmentVariable(name));
            Environment.SetEnvironmentVariable(name, value);
        }

        private void RestoreHost()
        {
            try { _setPreferences.Invoke(null, [_priorPreferences]); }
            finally
            {
                foreach (var pair in _environment) Environment.SetEnvironmentVariable(pair.Key, pair.Value);
                Directory.Delete(StateDirectory, recursive: true);
            }
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                Coordinator.Dispose();
                await Presenter.DisposeAsync();
            }
            finally
            {
                try { await _provider.DisposeAsync(); }
                finally { RestoreHost(); }
            }
        }
    }

    private sealed class RuntimePreferences : IPreferences
    {
        private readonly Dictionary<(string?, string), object> _values = new();
        public bool FailSelectedWorkspaceWrite { get; set; }
        public int RejectedWrites { get; private set; }
        public bool ContainsKey(string key, string? sharedName = null)
        { lock (_values) return _values.ContainsKey((sharedName, key)); }
        public void Remove(string key, string? sharedName = null)
        { lock (_values) _values.Remove((sharedName, key)); }
        public void Clear(string? sharedName = null)
        { lock (_values) foreach (var key in _values.Keys.Where(k => k.Item1 == sharedName).ToArray()) _values.Remove(key); }
        public void Set<T>(string key, T value, string? sharedName = null)
        {
            lock (_values)
            {
                if (FailSelectedWorkspaceWrite && key == "chummer.android.selected-workspace.v1")
                {
                    RejectedWrites++;
                    throw new IOException("Controlled selected-workspace preference failure after reward commit.");
                }
                _values[(sharedName, key)] = value!;
            }
        }
        public T Get<T>(string key, T defaultValue, string? sharedName = null)
        { lock (_values) return _values.TryGetValue((sharedName, key), out var value) ? (T)value : defaultValue; }
    }
}
