using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Chummer.Android.Native;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Workspaces;
using Microsoft.Maui.Controls;

internal static partial class AfterRunAuthorityHarness
{
    // Controlled pre-TalentAccess serialized fixtures from actual catalogs. This
    // is NOT an exported historical Play build or an Android Activity/alert test.
    public static async Task RunSkillsReReviewCasesAsync(string contentRoot, string sourceDirectory,
        ICharacterSourceDataResolver resolver, CharacterWorkspaceId id, CharacterCreationSkillsState sourceState,
        CharacterCreationSkillsPreview sourcePreview, CharacterCreationSkillsReceipt sourceReceipt)
    {
        foreach (var locale in new[] { "de-AT", "en-GB", "es-MX" })
            foreach (var key in new[] { "Title", "Intro", "ConfirmBody", "Removed", "Restore", "Check", "Unavailable" })
                Require(CreationAllocationStrings.Get("SkillsReReview." + key, "missing", CultureInfo.GetCultureInfo(locale)) != "missing",
                    "Missing real re-review satellite resource: " + locale + "/" + key);
        foreach (var (obsolete, failCommittedRead) in new[] { (false, false), (true, false), (false, true) })
        {
            string? path = null;
            SkillsReReviewReadProbe? entryProbe = null;
            await using var runtime = new NativeRewardRuntime(contentRoot, creationSkillsSeed: target =>
                path = SeedHistoricalSkills(target, sourceDirectory, resolver, id, sourceState, sourcePreview, sourceReceipt, obsolete),
                skillsDecorator: inner => failCommittedRead ? new FailedCommittedSkillsRead(inner)
                    : entryProbe = new SkillsReReviewReadProbe(inner));
            runtime.Id = id;
            await runtime.Presenter.LoadAsync(id, default);
            Require(runtime.Coordinator.State.Profile?.Created == false && runtime.Coordinator.State.WorkspaceId == id,
                "Real Presentation did not load the historical Creation runner.");
            byte[] before = File.ReadAllBytes(path!);
            var store = new FileWorkspaceStore(runtime.StateDirectory);
            var original = store.Get(id).Value!;
            var originalReceipt = original.Document.AuxiliaryState.CharacterCreationSkillsReceipts!.Single();
            Require(runtime.Coordinator.LoadCreationSkills().Value is null,
                "Ordinary phone Skills editing was silently unlocked for pre-policy history.");
            var loaded = runtime.Coordinator.LoadCreationSkillsReReview();
            Require(loaded.Value is not null, "Actual native re-review unavailable: " + string.Join(",", loaded.Blockers));
            var state = loaded.Value!;
            if (entryProbe is not null)
                await SkillsReReviewDashboardEntryAsync(runtime.Coordinator, entryProbe, obsolete);
            Require(File.ReadAllBytes(path!).SequenceEqual(before), "Opening or leaving the actual dashboard recovery route changed history.");
            var draft = new CreationSkillsReReviewPhoneDraft();
            Require(draft.Bind(state, runtime.Coordinator.State) && !state.CurrentState.CanEdit,
                "Re-review forged ordinary editable authority.");
            Require(draft.CanConfirm(runtime.Coordinator.State) == !obsolete, "Historical choices were confused with current legality.");
            Require(!draft.Bind(state with { SnapshotDigest = "invented" }, runtime.Coordinator.State), "Accepted invented review hash.");
            Require(!draft.Bind(state, Program.NewCreationOverview(id, state.Binding.Current.ContentRevision + 1, state.Binding.Current.SavedRevision)),
                "Re-review accepted a stale overview.");
            var page = new CreationSkillsReReviewPage(runtime.Coordinator, state);
            RefreshSkillsReReview(page);
            Require(!SkillsReviewButton(page).IsEnabled, "A detached native page enabled confirmation.");
            // Managed appearance stand-in only; no Activity attachment claim.
            typeof(CreationSkillsReReviewPage).GetField("_attached", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(page, true);
            ((VerticalStackLayout)((ScrollView)page.Content!).Content).IsEnabled = true;
            RefreshSkillsReReview(page);
            var body = (VerticalStackLayout)((ScrollView)page.Content!).Content;
            Require(body.Children.OfType<Border>().Any(row => row.AutomationId?.StartsWith("creation-skills-rereview-change-", StringComparison.Ordinal) == true),
                "Actual native page omitted the historical/current comparison.");
            Require(SkillsReviewButton(page).IsEnabled == !obsolete, "Native Apply ignored Core blockers.");
            var nativeDraft = (CreationSkillsReReviewPhoneDraft)typeof(CreationSkillsReReviewPage)
                .GetField("_draft", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;
            Require((await runtime.Coordinator.ConfirmCreationSkillsReReviewAsync(draft.Preview!, draft.Skills, draft.Groups, false)).Receipt is null,
                "Coordinator saved without explicit historical review.");
            if (obsolete)
            {
                foreach (string name in new[] { "Compiling", "Decompiling" })
                {
                    string sourceId = state.CurrentState.Authority.ActiveSkills.Single(row => row.Name == name).SourceSkillId;
                    var proposed = nativeDraft.Skills.Where(row => row.SourceSkillId != sourceId).ToArray();
                    await (Task)typeof(CreationSkillsReReviewPage).GetMethod("ProposeAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .Invoke(page, [proposed, nativeDraft.Groups])!;
                    RefreshSkillsReReview(page);
                    Require(nativeDraft.Preview!.Changes.Single(row => row.SourceId == sourceId).Removed,
                        "Native correction erased rather than displayed the removed historical row.");
                    Require(SkillsReviewButton(page).IsEnabled == (name == "Decompiling"),
                        "An intermediate blocked proposal was discarded or became saveable.");
                }
                // Explicit restoration is a proposal, not a history mutation.
                string compiling = state.CurrentState.Authority.ActiveSkills.Single(row => row.Name == "Compiling").SourceSkillId;
                var restore = nativeDraft.Skills.Concat(state.HistoricalDraft.Allocations.Where(row => row.SourceSkillId == compiling)).ToArray();
                var restored = runtime.Coordinator.PreviewCreationSkillsReReview(state.Binding, restore, nativeDraft.Groups);
                Require(restored.Value is { CurrentPreview.CanConfirm: false } && restored.Value.Changes.Single(row => row.SourceId == compiling).Removed == false,
                    "Restoring an obsolete choice bypassed current Core rules.");
            }
            var preview = nativeDraft.Preview!;
            var skills = nativeDraft.Skills.ToArray();
            var groups = nativeDraft.Groups.ToArray();
            var hostile = preview with { Changes = preview.Changes.Select((row, index) => index == 0 ? row with { CandidatePointCost = 0, Name = "invented" } : row).ToArray(), PreviewDigest = string.Empty };
            hostile = hostile with { PreviewDigest = CharacterCreationSkillsDigest.Compute(hostile) };
            Require((await runtime.Coordinator.ConfirmCreationSkillsReReviewAsync(hostile, skills, groups, true)).Receipt is null,
                "Self-rehashed comparison bypassed authoritative Core reprojection.");
            Require(File.ReadAllBytes(path!).SequenceEqual(before), "Load, preview, cancel or rejected confirmation changed history.");
            typeof(CreationSkillsReReviewPage).GetMethod("OnDisappearing", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, null);
            await (Task)typeof(CreationSkillsReReviewPage).GetMethod("ProposeAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(page, [Array.Empty<CharacterCreationSkillAllocation>(), groups])!;
            Require(nativeDraft.Preview!.PreviewDigest == preview.PreviewDigest,
                "A queued action changed the proposal after page departure.");
            var confirmed = await runtime.Coordinator.ConfirmCreationSkillsReReviewAsync(preview, skills, groups, true);
            Require(confirmed.Outcome == CharacterCreationFoundationOutcomes.Success && confirmed.Receipt is not null,
                "Actual coordinator commit lost its durable receipt: " + string.Join(",", confirmed.Blockers));
            if (failCommittedRead)
            {
                Require(confirmed.RefreshedState is null && confirmed.Blockers.Contains(CharacterCreationSkillsBlockers.PostCommitRefreshRequired),
                    "A failed post-commit read hid the save or claimed fresh state.");
                typeof(CreationSkillsReReviewPage).GetField("_confirmation", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(page, confirmed);
                RefreshSkillsReReview(page);
                Require(!body.Children.OfType<Button>().Any(button => button.AutomationId == "creation-skills-rereview-confirm")
                    && body.Children.OfType<Label>().Any(label => label.AutomationId == "creation-skills-rereview-receipt"),
                    "The native receipt-bearing failure view offered another apply.");
                await runtime.Presenter.LoadAsync(id, default); // Explicit reopen, not a retry of the mutation.
            }
            else Require(confirmed.RefreshedState is not null && confirmed.Blockers.Count == 0,
                "Actual presenter/shell refresh failed: " + string.Join(",", confirmed.Blockers));
            var current = new CharacterCreationSkillsService(new FileWorkspaceStore(runtime.StateDirectory), resolver).Load(new(id)).Value!;
            var coldPhone = new CreationSkillsPhoneDraft();
            coldPhone.Bind(current, runtime.Coordinator.State);
            Require(coldPhone.Matches(current, runtime.Coordinator.State), "Cold ordinary Skills phone did not reopen after explicit re-review.");
            string key = CreationSkillsReReviewPhoneAuthority.IdempotencyKey(preview);
            Require(CreationSkillsReReviewPhoneAuthority.ReceiptMatches(confirmed.Receipt!, preview, current, key),
                "Phone receipt did not match the exact reviewed projections and prior receipt.");
            var wrongHistory = confirmed.Receipt! with { PreviousReceiptDigest = sourceReceipt.ReceiptDigest, ReceiptDigest = string.Empty };
            wrongHistory = wrongHistory with { ReceiptDigest = CharacterCreationSkillsDigest.ComputeReceipt(wrongHistory) };
            Require(!CreationSkillsReReviewPhoneAuthority.ReceiptMatches(wrongHistory, preview, current, key),
                "An independently rehashed receipt lost the historical chain binding.");
            var after = store.Get(id).Value!;
            Require(after.ContentRevision == original.ContentRevision + 1
                && after.Document.Content == original.Document.Content
                && CreationSkillsReReviewPhoneAuthority.Equal(after.Document.AuxiliaryState.CharacterCreationPrerequisiteDraft,
                    original.Document.AuxiliaryState.CharacterCreationPrerequisiteDraft)
                && CreationSkillsReReviewPhoneAuthority.Equal(after.Document.AuxiliaryState.CharacterCreationAttributesDraft,
                    original.Document.AuxiliaryState.CharacterCreationAttributesDraft),
                "Re-review changed unrelated source choices, attributes or character XML.");
            Require(after.Document.AuxiliaryState.CharacterCreationSkillsReceipts is { Count: 2 } receipts
                && receipts[0].ReceiptDigest == originalReceipt.ReceiptDigest
                && receipts[1].PreviousReceiptDigest == originalReceipt.ReceiptDigest,
                "Explicit re-review failed to append to immutable history.");
            var replay = new CharacterCreationSkillsService(new FileWorkspaceStore(runtime.StateDirectory), resolver).ConfirmReReview(
                new(preview.Binding, skills, groups, preview.PreviewDigest, CreationSkillsReReviewPhoneAuthority.IdempotencyKey(preview), true, true));
            Require(replay.Value?.ReceiptDigest == confirmed.Receipt!.ReceiptDigest
                && store.Get(id).Value!.ContentRevision == after.ContentRevision, "Exact cold retry produced another mutation.");
            Require(runtime.Coordinator.LoadCreationSkillsReReview().Value is null,
                "A completed migration remained eligible for automatic reapplication.");
            Console.WriteLine("PASS native historical Skills review → " + (obsolete ? "two incremental repairs" : "unchanged legal choices")
                + (failCommittedRead ? " → failed committed read retains receipt → explicit reopen" : " → real presenter/shell")
                + " → cold reopen/replay (managed; no Activity proof)");
        }
    }

    private static void RefreshSkillsReReview(CreationSkillsReReviewPage page) => typeof(CreationSkillsReReviewPage)
        .GetMethod("Refresh", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(page, null);
    private static Button SkillsReviewButton(CreationSkillsReReviewPage page) =>
        ((VerticalStackLayout)((ScrollView)page.Content!).Content).Children.OfType<Button>()
            .Single(button => button.AutomationId == "creation-skills-rereview-confirm");

    private static async Task SkillsReReviewDashboardEntryAsync(RunnerSessionCoordinator coordinator,
        SkillsReReviewReadProbe probe, bool exerciseLateRead)
    {
        Require(coordinator.State.CreationWizard is { } wizard
            && wizard.Steps.Any(step => step.StepId == CharacterCreationWizardStepIds.Skills),
            "The real historical runner has no Creation dashboard/Skills stage.");
        var build = new BuildPage(coordinator);
        var navigation = new NavigationPage(build);
        var open = typeof(BuildPage).GetMethod("OpenCreationSkillsReReviewAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var disappear = typeof(BuildPage).GetMethod("OnDisappearing", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var lifetimeField = typeof(BuildPage).GetField("_creationDashboardRouteReadyLifetime", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var generationField = typeof(BuildPage).GetField("_creationDashboardAppearanceGeneration", BindingFlags.Instance | BindingFlags.NonPublic)!;
        int beforeReads = probe.ReadCount;
        await ((Task)open.Invoke(build, null)!).WaitAsync(TimeSpan.FromSeconds(20));
        Require(probe.ReadCount == beforeReads && navigation.Navigation.NavigationStack.Count == 1,
            "An unattached dashboard scheduled Core reads or navigation.");
        using var lifetime = new CancellationTokenSource();
        lifetimeField.SetValue(build, lifetime);
        generationField.SetValue(build, 1L); // Managed appearance, not Activity proof.
        try
        {
            typeof(BuildPage).GetMethod("AddLegalNextSteps", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(build,
                [coordinator.State.CreationWizard, null, null, null, coordinator.LoadCreationSkills(), null, null]);
            var body = (VerticalStackLayout)((ScrollView)build.Content!).Content;
            var recovery = body.Children.OfType<Border>().SelectMany(border => border.Content is Grid grid
                    ? grid.Children.OfType<Button>() : Enumerable.Empty<Button>())
                .Single(button => button.AutomationId == "creation-skills-rereview-open");
            Require(recovery.IsEnabled, "The real dashboard omitted the separate recovery route for blocked Skills.");
            await ((Task)open.Invoke(build, null)!).WaitAsync(TimeSpan.FromSeconds(20));
            Require(navigation.CurrentPage is CreationSkillsReReviewPage && navigation.Navigation.NavigationStack.Count == 2,
                "Dashboard recovery did not reach the comparison page or used the ordinary invalid Skills editor.");
            var target = (CreationSkillsReReviewPage)navigation.CurrentPage;
            RefreshSkillsReReview(target);
            Require(((VerticalStackLayout)((ScrollView)target.Content!).Content).Children.OfType<Label>()
                .Any(label => label.AutomationId == "creation-skills-rereview-binding"),
                "The dashboard-created destination did not retain the exact Core review binding.");
        }
        finally { disappear.Invoke(build, null); }
        if (!exerciseLateRead) return;

        var departed = new BuildPage(coordinator);
        var departedNavigation = new NavigationPage(departed);
        using var departedLifetime = new CancellationTokenSource();
        using var returnedLifetime = new CancellationTokenSource();
        lifetimeField.SetValue(departed, departedLifetime);
        generationField.SetValue(departed, 1L);
        probe.PauseNextRead();
        Task pending = (Task)open.Invoke(departed, null)!;
        try
        {
            await probe.ReadEntered.Task.WaitAsync(TimeSpan.FromSeconds(20));
            disappear.Invoke(departed, null);
            // Reappearing on the same runner must not rehabilitate the old
            // response merely because there is now another active lifetime.
            lifetimeField.SetValue(departed, returnedLifetime);
            generationField.SetValue(departed, 3L);
            probe.ReleaseRead.TrySetResult();
            await pending.WaitAsync(TimeSpan.FromSeconds(20));
            Require(departedNavigation.Navigation.NavigationStack.Count == 1
                && ReferenceEquals(departedNavigation.CurrentPage, departed),
                "A late Core response reopened a wizard after dashboard departure.");
            await ((Task)open.Invoke(departed, null)!).WaitAsync(TimeSpan.FromSeconds(20));
            Require(departedNavigation.CurrentPage is CreationSkillsReReviewPage,
                "Rejecting an old response stranded a fresh explicit dashboard entry.");
        }
        finally
        {
            probe.ReleaseRead.TrySetResult();
            await pending.WaitAsync(TimeSpan.FromSeconds(20));
            disappear.Invoke(departed, null);
        }
        Console.WriteLine("PASS actual historical dashboard recovery entry → native comparison; departed/reappearing late read remains inert; fresh entry succeeds (managed navigation)");
    }

    private sealed class SkillsReReviewReadProbe(ICharacterCreationSkillsService inner)
        : ICharacterCreationSkillsService, ICharacterCreationSkillsReReviewService
    {
        private readonly ICharacterCreationSkillsReReviewService _review = (ICharacterCreationSkillsReReviewService)inner;
        private int _pause, _reads;
        public int ReadCount => Volatile.Read(ref _reads);
        public TaskCompletionSource ReadEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseRead { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void PauseNextRead() => Interlocked.Exchange(ref _pause, 1);
        public CharacterCreationFoundationResult<CharacterCreationSkillsState> Load(CharacterCreationSkillsLoadRequest request) => inner.Load(request);
        public CharacterCreationFoundationResult<CharacterCreationSkillsPreview> Preview(CharacterCreationSkillsPreviewRequest request) => inner.Preview(request);
        public CharacterCreationFoundationResult<CharacterCreationSkillsReceipt> Confirm(CharacterCreationSkillsConfirmRequest request) => inner.Confirm(request);
        public CharacterCreationFoundationResult<CharacterCreationSkillsReReviewState> LoadReReview(CharacterCreationSkillsLoadRequest request)
        {
            Interlocked.Increment(ref _reads);
            var result = _review.LoadReReview(request);
            if (Interlocked.Exchange(ref _pause, 0) != 0)
            {
                ReadEntered.TrySetResult();
                ReleaseRead.Task.WaitAsync(TimeSpan.FromSeconds(20)).GetAwaiter().GetResult();
            }
            return result;
        }
        public CharacterCreationFoundationResult<CharacterCreationSkillsReReviewPreview> PreviewReReview(CharacterCreationSkillsReReviewPreviewRequest request) => _review.PreviewReReview(request);
        public CharacterCreationFoundationResult<CharacterCreationSkillsReceipt> ConfirmReReview(CharacterCreationSkillsReReviewConfirmRequest request) => _review.ConfirmReReview(request);
    }

    private sealed class FailedCommittedSkillsRead(ICharacterCreationSkillsService inner)
        : ICharacterCreationSkillsService, ICharacterCreationSkillsReReviewService
    {
        private readonly ICharacterCreationSkillsReReviewService _review = (ICharacterCreationSkillsReReviewService)inner;
        private bool _committed;
        public CharacterCreationFoundationResult<CharacterCreationSkillsState> Load(CharacterCreationSkillsLoadRequest request) =>
            _committed ? throw new IOException("Injected post-commit observation failure") : inner.Load(request);
        public CharacterCreationFoundationResult<CharacterCreationSkillsPreview> Preview(CharacterCreationSkillsPreviewRequest request) => inner.Preview(request);
        public CharacterCreationFoundationResult<CharacterCreationSkillsReceipt> Confirm(CharacterCreationSkillsConfirmRequest request) => inner.Confirm(request);
        public CharacterCreationFoundationResult<CharacterCreationSkillsReReviewState> LoadReReview(CharacterCreationSkillsLoadRequest request) => _review.LoadReReview(request);
        public CharacterCreationFoundationResult<CharacterCreationSkillsReReviewPreview> PreviewReReview(CharacterCreationSkillsReReviewPreviewRequest request) => _review.PreviewReReview(request);
        public CharacterCreationFoundationResult<CharacterCreationSkillsReceipt> ConfirmReReview(CharacterCreationSkillsReReviewConfirmRequest request)
        {
            var result = _review.ConfirmReReview(request);
            _committed = result.Outcome == CharacterCreationFoundationOutcomes.Success && result.Value is not null;
            return result;
        }
    }

    private static string SeedHistoricalSkills(string target, string sourceDirectory, ICharacterSourceDataResolver resolver,
        CharacterWorkspaceId id, CharacterCreationSkillsState state, CharacterCreationSkillsPreview preview,
        CharacterCreationSkillsReceipt saved, bool obsolete)
    {
        var workspace = new FileWorkspaceStore(sourceDirectory).Get(id).Value!;
        Require(resolver.TryCreateContext(workspace.Document.Content)!.TryResolveCreationSkillsAuthority(out var catalog)
            && catalog.TalentAccess is null, "The historical fixture requires a recognized pre-policy catalog.");
        var old = workspace.Document.AuxiliaryState.CharacterCreationSkillsDraft!;
        var rows = old.Skills.ToList();
        if (obsolete)
            foreach (string name in new[] { "Compiling", "Decompiling" })
            {
                var source = catalog.ActiveSkills.Single(row => row.Name == name);
                rows.Add(new(source.SourceSkillId, source.Kind, source.Name, source.Category, source.DefaultAttribute,
                    source.SkillGroup, 1, 1, 1, null, null, false, true, [], source.SourceAnchorIds));
            }
        var historicalRows = rows.OrderBy(row => row.Kind, StringComparer.Ordinal).ThenBy(row => row.SourceSkillId, StringComparer.Ordinal).ToArray();
        var choices = historicalRows.Select(row => new CharacterCreationSkillAllocation(row.SourceSkillId, row.Kind, row.Rating,
            row.SpecializationOptionId, row.IsNativeLanguage)).ToArray();
        var historicalPreview = preview with { Binding = preview.Binding with { SkillsAuthorityDigest = catalog.AuthorityDigest },
            Skills = historicalRows, ActiveSkillPointBudget = preview.ActiveSkillPointBudget with
            { Used = preview.ActiveSkillPointBudget.Used + (obsolete ? 2 : 0), Remaining = preview.ActiveSkillPointBudget.Remaining - (obsolete ? 2 : 0) }, PreviewDigest = string.Empty };
        historicalPreview = historicalPreview with { PreviewDigest = CharacterCreationSkillsDigest.Compute(historicalPreview) };
        string command = CharacterCreationSkillsDigest.Compute(new
        {
            Schema = "chummer.character_creation_skills_command.v1", historicalPreview.Binding,
            Allocations = choices, GroupAllocations = old.GroupAllocations.OrderBy(row => row.GroupId, StringComparer.Ordinal).ToArray(),
            historicalPreview.PreviewDigest, ExplicitlyConfirmed = true
        });
        old = old with { SkillsAuthorityDigest = catalog.AuthorityDigest, Skills = historicalRows, Allocations = choices,
            ActivePointUsed = (int)historicalPreview.ActiveSkillPointBudget.Used,
            SourceAnchorIds = state.PrerequisiteDraft!.SourceAnchorIds.Concat(catalog.SourceAnchorIds).Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal).ToArray(), LastPreviewDigest = historicalPreview.PreviewDigest,
            LastCommandDigest = command, DraftDigest = string.Empty };
        old = old with { DraftDigest = CharacterCreationSkillsDraftIntegrity.ComputeDigest(old) };
        var receipt = saved with { SkillsAuthorityDigest = catalog.AuthorityDigest, DraftDigest = old.DraftDigest,
            PreviewDigest = old.LastPreviewDigest, CommandDigest = command, ActivePointsRemaining = old.ActivePointTotal - old.ActivePointUsed,
            ReceiptDigest = string.Empty };
        receipt = receipt with { ReceiptDigest = CharacterCreationSkillsDigest.ComputeReceipt(receipt) };
        string sourcePath = Directory.GetFiles(sourceDirectory, id.Value + ".json", SearchOption.AllDirectories).Single();
        var record = JsonNode.Parse(File.ReadAllText(sourcePath))!.AsObject();
        record["AuxiliaryState"] = JsonSerializer.SerializeToNode(workspace.Document.AuxiliaryState with
            { CharacterCreationSkillsDraft = old, CharacterCreationSkillsReceipts = [receipt] });
        string path = Path.Combine(target, Path.GetRelativePath(sourceDirectory, sourcePath));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, record.ToJsonString());
        return path;
    }
}
