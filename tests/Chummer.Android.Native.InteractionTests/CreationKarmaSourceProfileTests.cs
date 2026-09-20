using System.Diagnostics;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Infrastructure.Workspaces;
using Microsoft.Extensions.DependencyInjection;

internal static partial class AfterRunAuthorityHarness
{
    // Opt-in diagnostic on the real local runtime. No elapsed-time assertion:
    // host timing is evidence for investigation, not phone release authority.
    private static async Task RunKarmaSourceProfileAsync(string contentRoot)
    {
        using var ui = new IssuedPageUiContext();
        await ui.RunAsync(async () =>
        {
            await using var runtime = new NativeRewardRuntime(contentRoot, creationBootstrap: true);
            await runtime.Coordinator.InitializeAsync();
            await AccountStartupTask(runtime.Coordinator).WaitAsync(TimeSpan.FromSeconds(10));
            await runtime.Coordinator.CreateRunnerAsync();
            await runtime.Presenter.UpdateDialogFieldAsync("newCharacterName", "Source profile fixture", default);
            await runtime.Presenter.UpdateDialogFieldAsync("newCharacterBuildMethod", "Karma", default);
            await runtime.Coordinator.ExecuteDialogActionAsync("create_character");
            Require(runtime.Coordinator.State.WorkspaceId is not null, "Profile bootstrap failed.");
            var id = runtime.Coordinator.State.WorkspaceId!.Value;
            var store = new FileWorkspaceStore(runtime.StateDirectory);
            var before = store.Get(id).Value!;
            var resolver = runtime.Services.GetRequiredService<ICharacterSourceDataResolver>();
            var observed = new KarmaSourceResolverProbe(resolver);
            var service = new CharacterCreationKarmaMetatypeService(store, observed);
            for (int attempt = 0; attempt < 2; attempt++)
            {
                long start = Stopwatch.GetTimestamp();
                var loaded = service.Load(id, includeSkills: true);
                Require(loaded.Value is { SkillsCatalog: not null, Talents: not null },
                    "Profile load failed: " + string.Join(",", loaded.Blockers));
                var state = loaded.Value!;
                Console.WriteLine($"SOURCE load {attempt}: {Stopwatch.GetElapsedTime(start).TotalMilliseconds:F1} ms");
                start = Stopwatch.GetTimestamp();
                var human = state.Options.Single(option => option.Label == "Human");
                var preview = service.Preview(state.Binding, human.OptionId, "mundane", [], new([], []));
                Require(preview.Value is not null, "Profile preview failed: " + string.Join(",", preview.Blockers));
                Console.WriteLine($"SOURCE preview {attempt}: {Stopwatch.GetElapsedTime(start).TotalMilliseconds:F1} ms");
            }
            var after = store.Get(id).Value!;
            Require(before.ContentRevision == after.ContentRevision && before.SavedRevision == after.SavedRevision
                && before.Document.Content == after.Document.Content
                && before.Document.AuxiliaryStateDigest == after.Document.AuxiliaryStateDigest,
                "Read-only profiling changed the workspace.");
            Console.WriteLine("PASS Karma source profile: real catalog/preview, no workspace writes");
        });
    }

    private sealed class KarmaSourceResolverProbe(ICharacterSourceDataResolver actual) : ICharacterSourceDataResolver
    {
        public ICharacterSourceDataContext? TryCreateContext(string characterXml)
        {
            long start = Stopwatch.GetTimestamp();
            var context = actual.TryCreateContext(characterXml);
            PrintSourceProfile(actual, "context", start);
            if (context is null) return null;
            var proxy = DispatchProxy.Create<ICharacterSourceDataContext, KarmaSourceContextProbe>();
            var probe = (KarmaSourceContextProbe)(object)proxy;
            probe.Actual = context;
            probe.Resolver = actual;
            return proxy;
        }
    }

    public class KarmaSourceContextProbe : DispatchProxy
    {
        public ICharacterSourceDataContext Actual { get; set; } = null!;
        public ICharacterSourceDataResolver Resolver { get; set; } = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            long start = Stopwatch.GetTimestamp();
            try { return method!.Invoke(Actual, args); }
            catch (TargetInvocationException error) when (error.InnerException is not null)
            { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
            finally { PrintSourceProfile(Resolver, method!.Name, start); }
        }
    }

    private static void PrintSourceProfile(ICharacterSourceDataResolver resolver, string phase, long start)
    {
        // Internal diagnostics remain internal to Core. Reflection here is only
        // in this opt-in managed test, never shipped in the Android application.
        object? diagnostic = resolver.GetType().GetProperty("LastSourceInputSnapshotDiagnostics",
            BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(resolver);
        object? Value(string name) => diagnostic?.GetType().GetProperty(name)?.GetValue(diagnostic);
        Console.WriteLine($"SOURCE {phase}: {Stopwatch.GetElapsedTime(start).TotalMilliseconds:F1} ms; "
            + $"reads={Value("PhysicalReadCount")}, parses={Value("PhysicalXmlParseCount")}, "
            + $"validations={Value("ValidationReadCount")}, bytes={Value("ValidationBytesRead")}, "
            + $"directories={Value("DirectoryValidationCount")}, hits={Value("CacheHitCount")}");
    }
}
