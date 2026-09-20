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
                var loaded = service.Load(id, includeSkills: true, includeQualities: true);
                Require(loaded.Value is { SkillsCatalog: not null, Talents: not null, QualitiesCatalog: not null },
                    "Profile load failed: " + string.Join(",", loaded.Blockers));
                var state = loaded.Value!;
                Console.WriteLine($"SOURCE load {attempt}: {Stopwatch.GetElapsedTime(start).TotalMilliseconds:F1} ms");
                Console.WriteLine($"SOURCE catalogs: skills={state.SkillsCatalog!.ActiveSkills.Count + state.SkillsCatalog.KnowledgeSkills.Count}, "
                    + $"qualities={state.QualitiesCatalog!.Options.Count}, selectable={state.QualitiesCatalog.Options.Count(option => option.IsSelectable)}, "
                    + $"quality-json-bytes={System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(state.QualitiesCatalog).Length}");
                if (attempt == 0)
                {
                    ProfileQualityDigests(state.QualitiesCatalog.Options);
                    ProfileQualityCatalogDigest(state.QualitiesCatalog);
                }
                start = Stopwatch.GetTimestamp();
                var human = state.Options.Single(option => option.Label == "Human");
                var preview = service.Preview(state.Binding, human.OptionId, "mundane", [], new([], []), qualityOptionIds: []);
                Require(preview.Value is not null, "Profile preview failed: " + string.Join(",", preview.Blockers));
                Console.WriteLine($"SOURCE preview {attempt}: {Stopwatch.GetElapsedTime(start).TotalMilliseconds:F1} ms");
            }
            var after = store.Get(id).Value!;
            Require(before.ContentRevision == after.ContentRevision && before.SavedRevision == after.SavedRevision
                && before.Document.Content == after.Document.Content
                && before.Document.AuxiliaryStateDigest == after.Document.AuxiliaryStateDigest,
                "Read-only profiling changed the workspace.");
            Console.WriteLine("PASS Karma source profile: real catalog/preview, no workspace writes");

            // The emulator draft has seven saved decisions. Measure that shape
            // too; an empty history cannot explain save/reopen costs.
            var writer = new CharacterCreationKarmaMetatypeService(store, resolver);
            for (int decision = 0; decision < 7; decision++)
            {
                var state = writer.Load(id, includeSkills: true, includeQualities: true).Value!;
                var human = state.Options.Single(option => option.Label == "Human");
                var english = state.SkillsCatalog!.KnowledgeSkills.Single(skill => skill.Name == "English");
                var pistols = state.SkillsCatalog.ActiveSkills.Single(skill => skill.Name == "Pistols");
                CharacterCreationKarmaSkillAllocation[] allocations =
                [new(english.SourceSkillId, english.Kind, 0, IsNativeLanguage: true),
                    new(pistols.SourceSkillId, pistols.Kind, 1)];
                var skills = new CharacterCreationKarmaSkillsSelection(allocations, []);
                string[] qualities = [state.QualitiesCatalog!.Options.Single(item => item.Name == "Unsteady Hands").OptionId];
                var quote = writer.Preview(state.Binding, human.OptionId, "mundane", [], skills, 10.5m, qualities).Value!;
                Require(quote.CanSelect, "History profile quote failed.");
                Require(writer.Confirm(new(quote.Binding, human.OptionId, quote.QuoteDigest,
                    Guid.NewGuid(), true, "mundane", [], skills, 10.5m, qualities)).Value is not null,
                    "History profile confirmation failed.");
            }
            long historyStart = Stopwatch.GetTimestamp();
            var saved = store.Get(id).Value!;
            Console.WriteLine($"SOURCE saved read: {Stopwatch.GetElapsedTime(historyStart).TotalMilliseconds:F1} ms");
            historyStart = Stopwatch.GetTimestamp();
            Require(CharacterCreationKarmaMetatypeTransaction.IsValidHistory(saved), "History profile is invalid.");
            Console.WriteLine($"SOURCE history validation: {Stopwatch.GetElapsedTime(historyStart).TotalMilliseconds:F1} ms");
            historyStart = Stopwatch.GetTimestamp();
            Require(service.Open(id).Value?.Quote?.CanSelect == true, "Profile saved Open failed.");
            Console.WriteLine($"SOURCE saved Open: {Stopwatch.GetElapsedTime(historyStart).TotalMilliseconds:F1} ms");
            var reopened = store.Get(id).Value!;
            Require(saved == reopened || saved.Document.AuxiliaryStateDigest == reopened.Document.AuxiliaryStateDigest
                && saved.Document.Content == reopened.Document.Content && saved.ContentRevision == reopened.ContentRevision
                && saved.SavedRevision == reopened.SavedRevision, "Profiling a saved draft changed it.");
            Console.WriteLine("PASS Karma source profile: seven real confirmations, read-only reopen");
        });
    }

    private static void ProfileQualityDigests(IReadOnlyList<CharacterCreationQualityCatalogOption> options)
    {
        // Compare with the unchanged generic canonical serializer on identical
        // real rows in one process. Reflection is confined to this diagnostic.
        var legacy = typeof(CharacterCreationQualitiesRules).Assembly
            .GetType("Chummer.Contracts.Characters.CharacterCreationQualitiesDigest", throwOnError: true)!
            .GetMethod("Compute", BindingFlags.Static | BindingFlags.Public)!
            .MakeGenericMethod(typeof(CharacterCreationQualityCatalogOption))
            .CreateDelegate<Func<CharacterCreationQualityCatalogOption, string>>();
        foreach (var option in options)
            Require(legacy(option with { OptionDigest = string.Empty }) == CharacterCreationQualitiesRules.ComputeOptionDigest(option),
                "Canonical quality digest changed: " + option.OptionId);
        for (int round = 0; round < 3; round++)
        {
            Measure("legacy", option => legacy(option with { OptionDigest = string.Empty }));
            Measure("direct", CharacterCreationQualitiesRules.ComputeOptionDigest);
        }
        void Measure(string kind, Func<CharacterCreationQualityCatalogOption, string> hash)
        {
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            long start = Stopwatch.GetTimestamp();
            foreach (var option in options) _ = hash(option);
            Console.WriteLine($"SOURCE option hashes {kind}: {Stopwatch.GetElapsedTime(start).TotalMilliseconds:F1} ms, "
                + $"allocated={GC.GetAllocatedBytesForCurrentThread() - allocated}");
        }
    }

    private static void ProfileQualityCatalogDigest(CharacterCreationKarmaQualitiesCatalog catalog)
    {
        var legacy = typeof(CharacterCreationQualitiesRules).Assembly
            .GetType("Chummer.Contracts.Characters.CharacterCreationQualitiesDigest", throwOnError: true)!
            .GetMethod("Compute", BindingFlags.Static | BindingFlags.Public)!
            .MakeGenericMethod(typeof(CharacterCreationKarmaQualitiesCatalog))
            .CreateDelegate<Func<CharacterCreationKarmaQualitiesCatalog, string>>();
        string expected = legacy(catalog with { CatalogDigest = string.Empty });
        Require(expected == CharacterCreationKarmaQualitiesRules.CatalogDigest(catalog),
            "Canonical real quality catalog digest changed.");
        for (int round = 0; round < 3; round++)
        {
            Measure("legacy", () => legacy(catalog with { CatalogDigest = string.Empty }));
            Measure("direct", () => CharacterCreationKarmaQualitiesRules.CatalogDigest(catalog));
        }
        void Measure(string kind, Func<string> hash)
        {
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            long start = Stopwatch.GetTimestamp();
            Require(hash() == expected, "Quality catalog hash drifted during profiling.");
            Console.WriteLine($"SOURCE catalog hash {kind}: {Stopwatch.GetElapsedTime(start).TotalMilliseconds:F1} ms, "
                + $"allocated={GC.GetAllocatedBytesForCurrentThread() - allocated}");
        }
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
