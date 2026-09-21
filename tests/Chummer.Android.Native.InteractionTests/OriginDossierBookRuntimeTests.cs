using System.Security.Cryptography;
using System.Text;
using Chummer.Android.Native;
using Chummer.Application.LifeModules;
using Chummer.Contracts.LifeModules;
using Chummer.Contracts.Characters;
using Chummer.Presentation.OriginBooks;
using Microsoft.Maui.Controls;

internal static class OriginDossierBookRuntimeTests
{
    public static async Task RunAsync()
    {
        string digest = Digest("foundation");
        Require(OriginDossierLifeModulePhoneRuntime.MatchesFoundationDigest("sha256:" + digest, digest),
            "A real Foundation digest cannot bind the Origin budget.");
        foreach (string? invalid in new[] { null, string.Empty, digest, "SHA256:" + digest,
                     "sha256:" + digest.ToUpperInvariant(), "sha256:" + Digest("other"), "sha256:bad" })
            Require(!OriginDossierLifeModulePhoneRuntime.MatchesFoundationDigest(invalid, digest),
                "An invalid or different Foundation digest bound the Origin budget.");
        foreach (string? invalid in new[] { null, string.Empty, "sha256:" + digest, digest.ToUpperInvariant(), "bad" })
            Require(!OriginDossierLifeModulePhoneRuntime.MatchesFoundationDigest("sha256:" + digest, invalid),
                "An invalid Origin digest bound the Foundation budget.");
        Console.WriteLine("PASS Origin book: Foundation digest boundary");
        await RunMetatypePageAsync();
        foreach (string scenario in new[] { "terminal", "two-chapters", "cancel-after-commit", "storage-failure", "tampered-pending", "stale-book" })
        {
            string directory = Path.Combine(Path.GetTempPath(), "chummer-origin-book-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var authority = new DecisionAuthority(scenario == "two-chapters" ? 2 : 1);
                var files = new FileOriginDossierDraftTimelineStore(directory);
                var store = new FaultStore(files);
                OriginDossierLifeModulePhoneRuntime Runtime() => new(
                    new LifeModuleOriginDossierInteractionService(new LifeModuleOriginDossierService(authority)), store);
                var runtime = Runtime();
                Require((await runtime.OpenAsync("workspace-1")).IsSuccess, "Initial turn unavailable.");
                var prepared = await runtime.PrepareAsync("workspace-1", "choice-1");
                Require(prepared.IsSuccess && prepared.State?.PendingPreviewDigest is not null, "Preview missing.");
                if (scenario == "tampered-pending")
                {
                    var checkpoint = (await files.LoadAsync("local-single-user", "workspace-1"))!;
                    await files.SaveAsync(checkpoint with { TimelineChapterDigests = [Digest("invented")] });
                    Require(!(await Runtime().OpenAsync("workspace-1")).IsSuccess && authority.MutationCount == 0,
                        "A corrupt pending preview was exposed as a recoverable live decision.");
                    Console.WriteLine("PASS Origin book: " + scenario);
                    continue;
                }
                using var cancellation = new CancellationTokenSource();
                if (scenario == "cancel-after-commit") authority.AfterCommit = cancellation.Cancel;
                if (scenario == "storage-failure") store.FailNextSave = true;
                OriginDossierLifeModulePhoneResult confirmed;
                try
                {
                    confirmed = await runtime.ConfirmAsync("workspace-1", "choice-1",
                        prepared.State!.PendingPreviewDigest!, cancellation.Token);
                    Require(scenario != "storage-failure", "Injected storage error was not exercised.");
                }
                catch (IOException) when (scenario == "storage-failure")
                {
                    // New runtime and disk read: replay only the already accepted command.
                    runtime = Runtime();
                    var recovered = await runtime.OpenAsync("workspace-1");
                    Require(recovered.IsSuccess && recovered.State?.PendingPreviewDigest == prepared.State!.PendingPreviewDigest,
                        "Lost the idempotent recovery preview after a failed chapter save.");
                    confirmed = await runtime.ConfirmAsync("workspace-1", "choice-1", recovered.State!.PendingPreviewDigest!);
                }
                Require(confirmed.IsSuccess && authority.MutationCount == 1, "Confirmation changed mechanics more than once.");
                if (scenario == "two-chapters")
                {
                    Require(!confirmed.Completed && confirmed.State?.Timeline.Count == 1, "The live next turn lost its first chapter.");
                    runtime = Runtime();
                    Require((await runtime.OpenAsync("workspace-1")).State?.Timeline.Count == 1, "The next turn did not reopen.");
                    prepared = await runtime.PrepareAsync("workspace-1", "choice-2");
                    confirmed = await runtime.ConfirmAsync("workspace-1", "choice-2", prepared.State!.PendingPreviewDigest!);
                }
                Require(confirmed.Completed && confirmed.StoryCheckpoint?.Projection.VisibleChapters.Count == authority.MutationCount,
                    "The terminal result discarded confirmed chapters.");
                var persisted = await new FileOriginDossierDraftTimelineStore(directory).LoadAsync("local-single-user", "workspace-1");
                Require(persisted?.CheckpointDigest == confirmed.StoryCheckpoint!.CheckpointDigest && persisted.PendingPreview is null,
                    "The complete sealed book was not retained on disk.");
                if (scenario == "stale-book") authority.Current = authority.Current with { SourceDigest = Digest("changed-source") };
                var reopened = await Runtime().OpenAsync("workspace-1");
                if (scenario == "stale-book")
                    Require(!reopened.IsSuccess && reopened.StoryCheckpoint is null, "A stale book was admitted as live authority.");
                else
                {
                    Require(reopened.IsSuccess && reopened.Completed && reopened.State is null
                        && reopened.StoryCheckpoint!.CheckpointDigest == persisted!.CheckpointDigest,
                        "Terminal book could not reopen without the live-choice projector.");
                    foreach (string locale in new[] { "en-US", "de-DE", "es-ES" })
                    {
                        var reader = new OriginDossierBookPage(reopened.StoryCheckpoint!, locale);
                        Require(reader.AutomationId == "origin-life-book", "Reader route missing after locale change.");
                    }
                }
                Require(await files.LoadAsync("another-owner", "workspace-1") is null, "Story leaked into another owner's storage namespace.");
                Console.WriteLine("PASS Origin book: " + scenario);
            }
            finally { Directory.Delete(directory, recursive: true); }
        }
    }

    private static async Task RunMetatypePageAsync()
    {
        using var ui = new AfterRunAuthorityHarness.IssuedPageUiContext();
        await ui.RunAsync(async () =>
        {
            var authority = new DecisionAuthority(1);
            var original = authority.Current.LegalChoices.Single();
            LifeModuleDecisionAuthorityChoice Choice(string id, string metatype, decimal cost) => original with
            {
                ChoiceId = id, Label = metatype + " · Nationality", DecisionCommandDigest = Digest(id),
                MechanicsPreview = original.MechanicsPreview with
                {
                    KarmaCost = cost, KarmaRaw = cost.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    Items = [new("foundation:requested-metatype", "metatype-choice", metatype,
                        string.Empty, metatype, 0, ["metatypes.xml#" + metatype], string.Empty),
                        .. original.MechanicsPreview.Items]
                }
            };
            authority.Current = authority.Current with { LegalChoices = [Choice("human", "Human", 15), Choice("elf", "Elf", 55)] };
            var interaction = new LifeModuleOriginDossierInteractionService(new LifeModuleOriginDossierService(authority));
            var started = interaction.Start("workspace-1").Value!;
            OriginDossierLifeModulePhoneResult Display(LifeModuleOriginDossierDraftCheckpoint checkpoint) => new(
                LifeModuleOriginDossierOutcomes.Success, OriginDossierLifeModuleInteractionProjector.Project(checkpoint), [],
                LifeModuleBudget: new(CharacterCreationBudgetIds.LifeModules, "Karma", 750, 0, 750, true, [], "karma"),
                FoundationSnapshotDigest: "sha256:" + Digest("foundation"),
                BoundContentDigest: checkpoint.BoundContentDigest, BoundSourceDigest: checkpoint.BoundSourceDigest,
                BoundMechanicsSnapshotDigest: checkpoint.BoundMechanicsSnapshotDigest, StoryCheckpoint: checkpoint);
            int prepared = 0, confirmed = 0;
            var initialDisplay = Display(started);
            string ChoiceControl(string choiceId) => "origin-life-choice-" + Array.FindIndex(
                initialDisplay.State!.Choices.ToArray(), choice => choice.ChoiceId == choiceId);
            string humanControl = ChoiceControl("human"), elfControl = ChoiceControl("elf");
            var page = new OriginDossierLifeModuleDecisionPage(initialDisplay, "en-US", choiceId =>
            {
                prepared++;
                return Task.FromResult<OriginDossierLifeModulePhoneResult?>(Display(interaction.Prepare(started, choiceId).Value!));
            }, (_, _) => { confirmed++; return Task.FromResult(false); });
            Button Button(string id) => Elements(page).OfType<Button>().Single(button => button.AutomationId == id);
            bool Visible(string id) => Elements(page).Any(element => element.AutomationId == id);
            async Task Click(Button button) => await ui.BeginAsyncVoid(() => ((IButtonController)button).SendClicked());

            Require(!Visible(humanControl) && !Visible(elfControl) && prepared == 0,
                "The phone silently chose a default metatype.");
            ((IButtonController)Button("origin-life-metatype-Elf")).SendClicked();
            Require(Visible(elfControl) && !Visible(humanControl) && prepared == 0,
                "Metatype filtering exposed the wrong nationality or dispatched a mechanics action.");
            var oldElf = Button(elfControl);
            await Click(oldElf);
            Require(prepared == 1 && Visible("origin-life-confirm") && confirmed == 0, "Explicit preview was skipped.");
            var oldConfirm = Button("origin-life-confirm");
            ((IButtonController)Button("origin-life-metatype-Human")).SendClicked();
            Require(!Visible("origin-life-confirm") && Visible(humanControl),
                "A hidden Elf preview remained confirmable after choosing Human.");
            await Click(oldConfirm);
            await Click(oldElf);
            Require(confirmed == 0 && prepared == 1, "A stale detached control dispatched an action.");
            await Click(Button(humanControl));
            await Click(Button("origin-life-confirm"));
            Require(prepared == 2 && confirmed == 1 && authority.MutationCount == 0,
                "Rendering applied rules itself or failed to use the supplied confirmation boundary.");
            var departedConfirm = Button("origin-life-confirm");
            typeof(OriginDossierLifeModuleDecisionPage).GetMethod("OnDisappearing",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.DeclaredOnly)!.Invoke(page, null);
            await Click(departedConfirm);
            Require(confirmed == 1, "A control from a departed page dispatched confirmation.");
            typeof(OriginDossierLifeModuleDecisionPage).GetMethod("OnAppearing",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.DeclaredOnly)!.Invoke(page, null);
            Require(Visible("origin-life-confirm"), "Reopening did not issue fresh controls for the reviewed choice.");
            ui.AssertHealthy();
            Console.WriteLine("PASS Origin book: explicit metatype filter, preview, stale-control rejection");
        });
    }

    private static IEnumerable<Element> Elements(Element root)
    {
        yield return root;
        IEnumerable<Element> children = root switch
        {
            ContentPage page when page.Content is not null => [page.Content],
            ScrollView scroll when scroll.Content is not null => [scroll.Content],
            Border border when border.Content is not null => [border.Content],
            Layout layout => layout.Children.OfType<Element>(), _ => []
        };
        foreach (Element child in children)
        foreach (Element descendant in Elements(child)) yield return descendant;
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private static string Digest(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private sealed class FaultStore(IOriginDossierDraftTimelineStore inner) : IOriginDossierDraftTimelineStore
    {
        public bool FailNextSave { get; set; }
        public Task<LifeModuleOriginDossierDraftCheckpoint?> LoadAsync(string owner, string workspace, CancellationToken ct = default)
            => inner.LoadAsync(owner, workspace, ct);
        public Task SaveAsync(LifeModuleOriginDossierDraftCheckpoint checkpoint, CancellationToken ct = default)
        {
            if (FailNextSave) { FailNextSave = false; throw new IOException("Injected chapter write failure"); }
            return inner.SaveAsync(checkpoint, ct);
        }
        public Task DeleteAsync(string owner, string workspace, CancellationToken ct = default)
            => throw new InvalidOperationException("Confirmed story must never be deleted by the wizard.");
    }

    // A deterministic authority double exercises the real interaction, Core
    // projection and Android storage. It does not claim full Life Modules rules.
    private sealed class DecisionAuthority(int terminalAfter) : ILifeModuleDecisionAuthority
    {
        private readonly Dictionary<string, LifeModuleDecisionAcceptance> _accepted = new(StringComparer.Ordinal);
        public int MutationCount { get; private set; }
        public Action? AfterCommit { get; set; }
        public LifeModuleDecisionAuthorityStep Current { get; set; } = new(
            OriginDossierSchemas.DecisionAuthorityStepV1, "sr5", "workspace-1", 1,
            "local-single-user", "runner-1", "Neon", "en-US", "journey-1", "nationality", 1, "turn-1", 1,
            "A runner's story begins.", "Which path?", [Choice(1)], [], [],
            LifeModuleOriginDossierService.TurnLedgerRootDigest, Digest("graph-1"), Digest("decision-1"),
            Digest("content-1"), Digest("source"), Digest("rules"), Digest("runtime"), Digest("mechanics-0"));

        public LifeModuleDecisionAuthorityResult<LifeModuleDecisionAuthorityStep> Load(string workspaceId)
            => new(LifeModuleOriginDossierOutcomes.Success, Current, []);
        public LifeModuleDecisionAuthorityResult<LifeModuleDecisionAcceptance> FindAcceptance(string workspaceId, string key)
            => _accepted.TryGetValue(key, out var found)
                ? new(LifeModuleOriginDossierOutcomes.Success, found, [])
                : new(LifeModuleOriginDossierOutcomes.Missing, null, []);

        public LifeModuleDecisionAuthorityResult<LifeModuleDecisionAcceptance> Accept(LifeModuleDecisionAcceptanceCommand command)
        {
            Require(command.WorkspaceRevision == Current.WorkspaceRevision
                && command.ExpectedContentDigest == Current.ContentDigest, "Authority received stale command.");
            int number = ++MutationCount;
            string id = "decision-" + number;
            var fact = new OriginCanonicalNarrativeFact("fact-" + number, "accepted-life-module",
                "Accepted fact " + number, id, ["lifemodules.xml#module:" + number], string.Empty);
            var receipt = new LifeModuleAcceptedDecisionReceipt(
                OriginDossierSchemas.AcceptedDecisionReceiptV1, id, command.ChoiceId,
                command.DecisionCommandDigest, command.IdempotencyKeyDigest,
                Current.WorkspaceRevision, Current.WorkspaceRevision + 1,
                Current.ContentDigest, Digest("content-" + (number + 1)), Current.SourceDigest, Current.RulesDigest,
                Current.RuntimeDigest, Current.DecisionDigest, Current.MechanicsSnapshotDigest,
                Digest("graph-" + (number + 1)), Digest("mechanics-" + number),
                "Accepted consequence " + number + ".", [fact], Digest("receipt-" + number));
            Current = Current with
            {
                WorkspaceRevision = receipt.WorkspaceRevision,
                StageId = "stage-" + (number + 1), StageOrder = number + 1,
                TurnId = "turn-" + (number + 1), TurnSequence = number + 1,
                DecisionLeadInMarkdown = "The next scene begins.", DecisionPrompt = "Continue?",
                LegalChoices = number == terminalAfter ? [] : [Choice(number + 1)],
                CanonicalFacts = [.. Current.CanonicalFacts, fact], AcceptedDecisionIds = [.. Current.AcceptedDecisionIds, id],
                PreviousTurnDigest = command.ExpectedTurnSeedDigest,
                DecisionGraphDigest = receipt.AcceptedDecisionGraphDigest, DecisionDigest = Digest("decision-" + (number + 1)),
                ContentDigest = receipt.ContentDigest, MechanicsSnapshotDigest = receipt.MechanicsSnapshotDigest,
                IsTerminal = number == terminalAfter
            };
            var acceptance = new LifeModuleDecisionAcceptance(receipt, Current);
            _accepted.Add(command.IdempotencyKeyDigest, acceptance);
            AfterCommit?.Invoke();
            return new(LifeModuleOriginDossierOutcomes.Success, acceptance, []);
        }

        private static LifeModuleDecisionAuthorityChoice Choice(int number)
        {
            string anchor = "lifemodules.xml#module:" + number;
            var preview = new LifeModuleMechanicsPreview(15, "15", true,
                [new("effect-" + number, "active-skill", "Etiquette", "0", "1", 0, [anchor], string.Empty)],
                [], [anchor], string.Empty);
            return new("choice-" + number, "Path " + number, "RF", "66", Digest("command-" + number), preview, [anchor], [], true);
        }
    }
}
