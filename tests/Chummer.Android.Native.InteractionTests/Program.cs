using Chummer.Android.Native;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Files;
using Chummer.Infrastructure.Workspaces;
using Chummer.Infrastructure.Xml;
using Chummer.Presentation.Overview;

internal static class Program
{
    private static async Task Main()
    {
        (string Name, Func<Task> Run)[] tests =
        [
            (nameof(QueuedOlderUnfocusedCannotOverwriteActionInputAsync), QueuedOlderUnfocusedCannotOverwriteActionInputAsync),
            (nameof(StaleGenerationAndSameIdShapeChangesFailClosedAsync), StaleGenerationAndSameIdShapeChangesFailClosedAsync),
            (nameof(ReadOnlyTransitionFailsClosedAsync), ReadOnlyTransitionFailsClosedAsync),
            (nameof(DoubleTapExecutesExactlyOnceAsync), DoubleTapExecutesExactlyOnceAsync),
            (nameof(CloseWaitsForClaimedActionAsync), CloseWaitsForClaimedActionAsync),
            (nameof(FailureRerendersBeforeQueueAdvancesAsync), FailureRerendersBeforeQueueAdvancesAsync),
            (nameof(CanonicalDigestPrefixIsTwelveLowerHexAsync), CanonicalDigestPrefixIsTwelveLowerHexAsync),
            (nameof(CanonicalPriorityAuthorityIsPhoneReadyAsync), CanonicalPriorityAuthorityIsPhoneReadyAsync),
            (nameof(AttributesPreviewAdoptionRequiresCanonicalSuccessAsync), AttributesPreviewAdoptionRequiresCanonicalSuccessAsync),
            (nameof(AttributesBodPreviewCannotConfirmAgiDraftAsync), AttributesBodPreviewCannotConfirmAgiDraftAsync),
            (nameof(AttributesReceiptMustMatchCommittedWorkspaceBeforeActivationAsync), AttributesReceiptMustMatchCommittedWorkspaceBeforeActivationAsync)
        ];

        foreach ((string name, Func<Task> run) in tests)
        {
            await run();
            Console.WriteLine($"PASS {name}");
        }

        Console.WriteLine($"Native dialog interaction tests passed: {tests.Length}");
    }

    private static Task CanonicalDigestPrefixIsTwelveLowerHexAsync()
    {
        const string hex = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        const string canonical = $"sha256:{hex}";
        Require(
            CreationPrerequisiteDigestText.CanonicalPrefix(canonical) == "0123456789ab",
            "The readable binding must expose twelve digest hex characters, not the sha256 prefix.");
        foreach (string? invalid in new string?[]
                 {
                     null,
                     string.Empty,
                     hex,
                     $"sha256:{hex.ToUpperInvariant()}",
                     $"sha256:{hex[..^1]}",
                     $"sha256:{new string('g', 64)}"
                 })
        {
            Require(
                CreationPrerequisiteDigestText.CanonicalPrefix(invalid) == "unavailable",
                $"The readable binding must fail closed for a non-canonical digest: {invalid}");
        }

        return Task.CompletedTask;
    }

    private static Task CanonicalPriorityAuthorityIsPhoneReadyAsync()
    {
        const string settingsId = "223a11ff-80e0-428b-89a9-6ef1c243b8b6";
        string coreRoot = ResolveCoreRoot();
        string workspaceRoot = Path.Combine(
            Path.GetTempPath(),
            $"chummer-android-prerequisite-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workspaceRoot);
        try
        {
            var overlays = new FileSystemContentOverlayCatalogService(
                coreRoot,
                coreRoot,
                null);
            var resolver = new FileSystemCharacterSourceDataResolver(overlays);
            var store = new FileWorkspaceStore(workspaceRoot);
            var workspaceId = new CharacterWorkspaceId("phone-canonical-priority");
            string characterXml = $"""
                                  <character>
                                    <name>Canonical Priority Runner</name>
                                    <alias>Authority Probe</alias>
                                    <metatype>Human</metatype>
                                    <buildmethod>Priority</buildmethod>
                                    <createdversion>5.225.0</createdversion>
                                    <appversion>5.225.0</appversion>
                                    <karma>25</karma>
                                    <nuyen>0</nuyen>
                                    <created>false</created>
                                    <settings>{settingsId}</settings>
                                  </character>
                                  """;
            Require(
                store.CreateWorkspaceDocument(
                    workspaceId,
                    new WorkspaceDocument(characterXml, RulesetDefaults.Sr5)).Success,
                "The canonical Priority probe workspace must be created.");
            var service = new CharacterCreationPrerequisiteService(
                store,
                new XmlCharacterFileQueries(new CharacterFileService()),
                resolver);
            CharacterCreationFoundationResult<CharacterCreationPrerequisiteState> loaded =
                service.Load(new CharacterCreationPrerequisiteLoadRequest(workspaceId));
            if (loaded.Outcome != CharacterCreationFoundationOutcomes.Success
                || loaded.Value is not CharacterCreationPrerequisiteState state)
            {
                throw new InvalidOperationException(
                    $"Core must publish a Priority prerequisite state: {loaded.Outcome} · "
                    + string.Join(",", loaded.Blockers));
            }
            Require(
                state.Blockers.Count == 0,
                "The canonical Priority prerequisite state must be blocker-free: "
                + string.Join(",", state.Blockers));
            string auxiliaryStateDigest = state.Binding.AuxiliaryStateDigest;
            Require(
                CreationPrerequisitePhoneAuthority.IsCanonicalAuxiliaryStateDigest(
                    auxiliaryStateDigest),
                "The phone gate must accept Core's exact bare lower-hex auxiliary digest.");
            foreach (string invalid in new[]
                     {
                         $"sha256:{auxiliaryStateDigest}",
                         auxiliaryStateDigest.ToUpperInvariant(),
                         auxiliaryStateDigest[..^1],
                         new string('g', 64)
                     })
            {
                Require(
                    !CreationPrerequisitePhoneAuthority.IsCanonicalAuxiliaryStateDigest(invalid),
                    $"The phone gate must reject a non-canonical auxiliary digest: {invalid}");
            }

            OpenWorkspaceState openWorkspace = new(
                workspaceId,
                "Canonical Priority Runner",
                "Authority Probe",
                DateTimeOffset.UtcNow,
                RulesetDefaults.Sr5,
                state.Binding.ContentRevision,
                state.Binding.SavedRevision);
            CharacterOverviewState overview = CharacterOverviewState.Empty with
            {
                WorkspaceId = workspaceId,
                OpenWorkspaces = [openWorkspace],
                Session = new WorkspaceSessionState(
                    workspaceId,
                    [openWorkspace],
                    [workspaceId]),
                Profile = new CharacterProfileSection(
                    "Canonical Priority Runner",
                    "Authority Probe",
                    string.Empty,
                    "Human",
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    "5.225.0",
                    "5.225.0",
                    CharacterCreationBuildMethods.Priority,
                    string.Empty,
                    Created: false,
                    Adept: false,
                    Magician: false,
                    Technomancer: false,
                    AI: false,
                    MainMugshotIndex: 0,
                    MugshotCount: 0),
                CreationWizard = new CharacterCreationWizardSnapshot(
                    CharacterCreationWizardSchemas.SnapshotV1,
                    workspaceId.Value,
                    state.Binding.ContentRevision,
                    state.Binding.RawCharacterXmlDigest,
                    state.Binding.AuthorityDigest,
                    RulesetDefaults.Sr5,
                    "test-runtime",
                    CharacterCreationBuildMethods.Priority,
                    CharacterCreated: false,
                    CharacterCreationWizardStepIds.Foundation,
                    [],
                    [],
                    new Dictionary<string, IReadOnlyList<CharacterCreationLegalOption>>(),
                    [],
                    [],
                    CanFinalize: false,
                    state.SnapshotDigest)
            };

            Require(
                CreationPrerequisitePhoneAuthority.IsReady(state, overview),
                "A canonical blocker-free Core Priority authority must be accepted by the phone gate.");
            return Task.CompletedTask;
        }
        finally
        {
            Directory.Delete(workspaceRoot, recursive: true);
        }
    }

    private static string ResolveCoreRoot()
    {
        string? configured = Environment.GetEnvironmentVariable("CHUMMER_CORE_ENGINE_ROOT");
        if (!string.IsNullOrWhiteSpace(configured) && Directory.Exists(configured))
            return Path.GetFullPath(configured);

        string siblingCheckout = Path.GetFullPath(Path.Combine(
            Directory.GetCurrentDirectory(),
            "..",
            "chummer-core-engine"));
        if (Directory.Exists(siblingCheckout))
            return siblingCheckout;

        throw new DirectoryNotFoundException(
            "Set CHUMMER_CORE_ENGINE_ROOT or provide the governed sibling chummer-core-engine checkout.");
    }

    private static Task AttributesPreviewAdoptionRequiresCanonicalSuccessAsync()
    {
        AttributesFixture fixture = NewAttributesFixture();
        CharacterCreationFoundationResult<CharacterCreationAttributesPreview> success = new(
            CharacterCreationFoundationOutcomes.Success,
            fixture.Preview,
            []);
        Require(
            CreationAttributesPhoneAuthority.CanAdoptPreview(
                fixture.State,
                fixture.Overview,
                success,
                fixture.Allocations),
            "A complete canonical Core success preview must be adoptable.");

        Require(
            !CreationAttributesPhoneAuthority.CanAdoptPreview(
                fixture.State,
                fixture.Overview,
                success with { Outcome = CharacterCreationFoundationOutcomes.Blocked },
                fixture.Allocations),
            "A value attached to a non-success outcome must never be adopted.");
        Require(
            !CreationAttributesPhoneAuthority.CanAdoptPreview(
                fixture.State,
                fixture.Overview,
                new CharacterCreationFoundationResult<CharacterCreationAttributesPreview>(
                    CharacterCreationFoundationOutcomes.Success,
                    fixture.Preview with { CanConfirm = false },
                    []),
                fixture.Allocations),
            "A preview which Core cannot confirm must never be adopted.");
        Require(
            !CreationAttributesPhoneAuthority.CanAdoptPreview(
                fixture.State,
                fixture.Overview,
                new CharacterCreationFoundationResult<CharacterCreationAttributesPreview>(
                    CharacterCreationFoundationOutcomes.Success,
                    fixture.Preview with { RequiresExplicitConfirmation = false },
                    []),
                fixture.Allocations),
            "The phone flow must reject a preview which does not require explicit confirmation.");
        Require(
            !CreationAttributesPhoneAuthority.CanAdoptPreview(
                fixture.State,
                fixture.Overview,
                new CharacterCreationFoundationResult<CharacterCreationAttributesPreview>(
                    CharacterCreationFoundationOutcomes.Success,
                    fixture.Preview with { PreviewDigest = new string('a', 64) },
                    []),
                fixture.Allocations),
            "The phone flow must reject a non-canonical preview digest.");
        return Task.CompletedTask;
    }

    private static Task AttributesBodPreviewCannotConfirmAgiDraftAsync()
    {
        AttributesFixture fixture = NewAttributesFixture();
        CharacterCreationAttributeAllocation[] agiDraft =
        [
            new("BOD", 0, 0),
            new("AGI", 1, 0)
        ];
        Require(
            !CreationAttributesPhoneAuthority.CanConfirmPreview(
                fixture.State,
                fixture.Overview,
                fixture.Preview,
                agiDraft),
            "A BOD projection must not authorize an AGI allocation draft.");

        CharacterCreationAttributeProjection[] agiProjection =
        [
            NewAttribute("BOD", current: 1, priorityPoints: 0),
            NewAttribute("AGI", current: 2, priorityPoints: 1)
        ];
        Require(
            !CreationAttributesPhoneAuthority.CanonicallyEquals(
                fixture.Preview,
                fixture.Preview with { Attributes = agiProjection }),
            "Canonical preview equality must bind every projected attribute value.");
        Require(
            !CreationAttributesPhoneAuthority.CanonicallyEquals(
                fixture.Preview,
                fixture.Preview with
                {
                    NormalPointBudget = fixture.Preview.NormalPointBudget with
                    {
                        Used = 2,
                        Remaining = 8
                    }
                }),
            "Canonical preview equality must bind all exact budget values.");
        return Task.CompletedTask;
    }

    private static Task AttributesReceiptMustMatchCommittedWorkspaceBeforeActivationAsync()
    {
        AttributesFixture fixture = NewAttributesFixture();
        string committedAuxiliaryDigest = new('b', 64);
        CharacterCreationAttributesBinding committedBinding = fixture.State.Binding with
        {
            ContentRevision = 6,
            SavedRevision = 6,
            AuxiliaryStateDigest = committedAuxiliaryDigest
        };
        CharacterCreationAttributesDraft draft = new(
            CharacterCreationAttributesSchemas.DraftV1,
            fixture.State.Binding.WorkspaceId,
            DraftRevision: 1,
            BaseContentRevision: 5,
            fixture.State.Binding.RawCharacterXmlDigest,
            fixture.State.Binding.PrerequisiteDraftRevision,
            fixture.State.Binding.PrerequisiteDraftDigest,
            fixture.State.Binding.PrerequisiteAuthorityDigest,
            "11111111-1111-1111-1111-111111111111",
            CanonicalDigest('e'),
            HalvesNormalAttributePoints: false,
            NormalPointTotal: 10,
            NormalPointUsed: 1,
            SpecialPointTotal: 0,
            SpecialPointUsed: 0,
            CreationKarmaTotal: 25,
            CreationKarmaUsed: 0,
            fixture.Allocations,
            fixture.Preview.Attributes,
            ["metatypes.xml#human"],
            CharacterEffectsApplied: false,
            CanonicalDigest('f'));
        CharacterCreationAttributesState committedState = fixture.State with
        {
            Binding = committedBinding,
            PendingDraft = draft,
            Attributes = fixture.Preview.Attributes,
            NormalPointBudget = fixture.Preview.NormalPointBudget,
            SpecialPointBudget = fixture.Preview.SpecialPointBudget,
            CreationKarmaBudget = fixture.Preview.CreationKarmaBudget,
            SnapshotDigest = CanonicalDigest('9')
        };
        CharacterCreationAttributesReceipt receipt = new(
            fixture.State.Binding.WorkspaceId,
            PreviousContentRevision: 5,
            ContentRevision: 6,
            SavedRevision: 6,
            DraftRevision: 1,
            draft.DraftDigest,
            NormalPointsRemaining: 9,
            SpecialPointsRemaining: 0,
            CreationKarmaRemaining: 25,
            CharacterDocumentChanged: false);

        Require(
            CreationAttributesPhoneAuthority.ReceiptMatchesBeforeActivation(
                receipt,
                fixture.Preview,
                fixture.Allocations,
                committedState,
                fixture.Overview),
            "An exact receipt and direct Core reload must validate before presenter activation.");
        Require(
            !CreationAttributesPhoneAuthority.ReceiptMatchesBeforeActivation(
                receipt with { ContentRevision = 7 },
                fixture.Preview,
                fixture.Allocations,
                committedState,
                fixture.Overview),
            "A receipt which skips the committed revision must fail closed.");
        Require(
            !CreationAttributesPhoneAuthority.ReceiptMatchesBeforeActivation(
                receipt,
                fixture.Preview,
                fixture.Allocations,
                committedState with
                {
                    Binding = committedBinding with
                    {
                        AuxiliaryStateDigest = fixture.State.Binding.AuxiliaryStateDigest
                    }
                },
                fixture.Overview),
            "An unchanged auxiliary workspace digest must fail before activation.");

        CharacterCreationAttributeAllocation[] agiDraft =
        [
            new("BOD", 0, 0),
            new("AGI", 1, 0)
        ];
        CharacterCreationAttributeProjection[] agiProjection =
        [
            NewAttribute("BOD", current: 1, priorityPoints: 0),
            NewAttribute("AGI", current: 2, priorityPoints: 1)
        ];
        CharacterCreationAttributesDraft substitutedDraft = draft with
        {
            Allocations = agiDraft,
            Attributes = agiProjection
        };
        Require(
            !CreationAttributesPhoneAuthority.ReceiptMatchesBeforeActivation(
                receipt,
                fixture.Preview,
                fixture.Allocations,
                committedState with
                {
                    PendingDraft = substitutedDraft,
                    Attributes = agiProjection
                },
                fixture.Overview),
            "A BOD preview receipt must not activate a workspace containing an AGI draft.");
        return Task.CompletedTask;
    }

    private static AttributesFixture NewAttributesFixture()
    {
        CharacterWorkspaceId workspaceId = new("attributes-phone-authority");
        CharacterCreationMetatypeAttributeProjection[] heritageAttributes =
        [
            new("BOD", 1, 6, 10),
            new("AGI", 1, 6, 10)
        ];
        CharacterCreationPrerequisiteDraft prerequisite = new(
            CharacterCreationPrerequisiteSchemas.DraftV1,
            workspaceId,
            DraftRevision: 4,
            BaseContentRevision: 4,
            CanonicalDigest('1'),
            CanonicalDigest('2'),
            CharacterCreationBuildMethods.Priority,
            "223a11ff-80e0-428b-89a9-6ef1c243b8b6",
            "Standard",
            ["A", "B", "C", "D", "E"],
            SumToTenTarget: null,
            [],
            CreationKarmaTotal: 25,
            CreationKarmaUsed: 0,
            ["priority.xml#standard"],
            CanonicalDigest('3'))
        {
            HeritageSelection = new CharacterCreationPriorityHeritageSelection(
                "human",
                CharacterCreationPriorityChildKinds.Metatype,
                "priority-heritage",
                "11111111-1111-1111-1111-111111111111",
                MetavariantSourceId: null,
                "Human",
                MetavariantName: null,
                SpecialAttributePoints: 0,
                KarmaCost: 0,
                HalvesNormalAttributePoints: false,
                heritageAttributes,
                CanonicalDigest('4'),
                CanonicalDigest('5'),
                ["metatypes.xml#human"]),
            TalentSelection = new CharacterCreationPriorityTalentSelection(
                "mundane",
                "priority-talent",
                "Mundane",
                "Mundane",
                SpecialAttributePoints: 0,
                Magic: null,
                Resonance: null,
                Depth: null,
                [],
                CanonicalDigest('6'),
                ["priority.xml#mundane"]),
            EffectiveNormalAttributePoints = 10,
            TotalSpecialAttributePoints = 0
        };
        CharacterCreationAttributesBinding binding = new(
            workspaceId,
            ContentRevision: 5,
            SavedRevision: 5,
            CanonicalDigest('1'),
            new string('a', 64),
            prerequisite.DraftRevision,
            prerequisite.DraftDigest,
            prerequisite.AuthorityDigest);
        CharacterCreationAttributeAllocation[] allocations =
        [
            new("BOD", 1, 0),
            new("AGI", 0, 0)
        ];
        CharacterCreationAttributeProjection[] attributes =
        [
            NewAttribute("BOD", current: 2, priorityPoints: 1),
            NewAttribute("AGI", current: 1, priorityPoints: 0)
        ];
        CharacterCreationBudgetState normal = NewBudget("normal", 10, 1);
        CharacterCreationBudgetState special = NewBudget("special", 0, 0);
        CharacterCreationBudgetState karma = NewBudget("karma", 25, 0);
        CharacterCreationAttributesState state = new(
            CharacterCreationAttributesSchemas.SnapshotV1,
            binding,
            prerequisite,
            PendingDraft: null,
            attributes,
            normal,
            special,
            karma,
            MaxNumberMaxAttributesCreate: 1,
            [],
            CanEdit: true,
            CanonicalDigest('7'))
        {
            KarmaAttribute = 5
        };
        CharacterCreationAttributesPreview preview = new(
            CharacterCreationAttributesSchemas.PreviewV1,
            binding,
            attributes,
            normal,
            special,
            karma,
            [],
            RequiresExplicitConfirmation: true,
            CanConfirm: true,
            CanonicalDigest('8'));
        return new AttributesFixture(
            state,
            NewCreationOverview(workspaceId, 5, 5),
            preview,
            allocations);
    }

    private static CharacterCreationAttributeProjection NewAttribute(
        string id,
        int current,
        int priorityPoints)
        => new(
            id,
            CharacterCreationAttributeCategories.Normal,
            Minimum: 1,
            Maximum: 6,
            AugmentedMaximum: 10,
            current,
            PriorityPointsSpent: priorityPoints,
            KarmaLevels: 0,
            PriorityPointCost: priorityPoints,
            KarmaCost: 0,
            IsEnabled: true,
            [],
            [$"metatypes.xml#human:{id}"]);

    private static CharacterCreationBudgetState NewBudget(
        string id,
        decimal total,
        decimal used)
        => new(
            id,
            id,
            total,
            used,
            total - used,
            IsExact: true,
            [],
            "points");

    private static CharacterOverviewState NewCreationOverview(
        CharacterWorkspaceId workspaceId,
        long contentRevision,
        long savedRevision)
    {
        OpenWorkspaceState openWorkspace = new(
            workspaceId,
            "Attributes Runner",
            "Authority Probe",
            DateTimeOffset.UtcNow,
            RulesetDefaults.Sr5,
            contentRevision,
            savedRevision);
        return CharacterOverviewState.Empty with
        {
            WorkspaceId = workspaceId,
            OpenWorkspaces = [openWorkspace],
            Session = new WorkspaceSessionState(workspaceId, [openWorkspace], [workspaceId]),
            Profile = new CharacterProfileSection(
                "Attributes Runner",
                "Authority Probe",
                string.Empty,
                "Human",
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                "5.225.0",
                "5.225.0",
                CharacterCreationBuildMethods.Priority,
                string.Empty,
                Created: false,
                Adept: false,
                Magician: false,
                Technomancer: false,
                AI: false,
                MainMugshotIndex: 0,
                MugshotCount: 0)
        };
    }

    private static string CanonicalDigest(char value)
        => $"sha256:{new string(value, 64)}";

    private sealed record AttributesFixture(
        CharacterCreationAttributesState State,
        CharacterOverviewState Overview,
        CharacterCreationAttributesPreview Preview,
        IReadOnlyList<CharacterCreationAttributeAllocation> Allocations);

    private static async Task QueuedOlderUnfocusedCannotOverwriteActionInputAsync()
    {
        NativeDialogInteractionGate gate = new();
        long generation = gate.BeginRender();
        TaskCompletionSource blockerEntered = NewSignal();
        TaskCompletionSource releaseBlocker = NewSignal();
        TaskCompletionSource actionEntered = NewSignal();
        TaskCompletionSource releaseAction = NewSignal();
        List<string> sequence = [];
        string presenterValue = "presenter";
        string currentControlValue = "typed-current";
        int actionCount = 0;
        int failureCount = 0;

        Task blocker = gate.RunFieldUpdateAsync(generation, async () =>
        {
            sequence.Add("blocker");
            blockerEntered.SetResult();
            await releaseBlocker.Task;
        });
        await blockerEntered.Task;

        Task olderUnfocused = gate.RunFieldUpdateAsync(generation, () =>
        {
            presenterValue = "typed-older-capture";
            sequence.Add("older-unfocused");
            return Task.CompletedTask;
        });
        Require(gate.TryClaimAction(), "The first action claim must succeed.");
        Task action = gate.RunClaimedActionAsync(
            async () =>
            {
                sequence.Add("flush");
                Require(
                    presenterValue == "typed-older-capture",
                    "A field update queued before the tap must run before the flush.");
                presenterValue = currentControlValue;
                actionCount++;
                actionEntered.SetResult();
                await releaseAction.Task;
                gate.BeginRender();
            },
            _ =>
            {
                failureCount++;
                return Task.CompletedTask;
            });

        releaseBlocker.SetResult();
        await actionEntered.Task;
        Task staleAfterAction = gate.RunFieldUpdateAsync(generation, () =>
        {
            presenterValue = "stale-overwrite";
            sequence.Add("stale-after-action");
            return Task.CompletedTask;
        });
        releaseAction.SetResult();
        await Task.WhenAll(blocker, olderUnfocused, action, staleAfterAction);

        Require(actionCount == 1, "The action must execute exactly once.");
        Require(failureCount == 0, "The valid action must not use the failure path.");
        Require(
            presenterValue == currentControlValue,
            "The action-bound flush must win with the exact current control value.");
        Require(!sequence.Contains("stale-after-action"), "The old generation must be ignored after the action.");
        Require(
            sequence.IndexOf("older-unfocused") < sequence.IndexOf("flush"),
            "Invocation order must be preserved at the action boundary.");
    }

    private static Task StaleGenerationAndSameIdShapeChangesFailClosedAsync()
    {
        NativeDialogInteractionGate gate = new();
        long firstGeneration = gate.BeginRender();
        NativeDialogFieldBinding binding = NewBinding(firstGeneration);
        Require(Matches(binding, firstGeneration), "The exact rendered shape must match.");

        long secondGeneration = gate.BeginRender();
        Require(
            !Matches(binding, secondGeneration),
            "A same-dialog, same-field binding from an older render must fail closed.");

        NativeDialogFieldBinding current = NewBinding(secondGeneration);
        Require(
            !current.Matches(
                secondGeneration,
                "dialog",
                "field",
                "Alias",
                "Enter alias",
                "text",
                true,
                false,
                "full",
                "default",
                ""),
            "An Entry-to-Editor shape change must fail closed even when the input type is unchanged.");
        Require(
            !current.Matches(
                secondGeneration,
                "dialog",
                "field",
                "Alias",
                "Enter alias",
                "text",
                false,
                false,
                "hidden",
                "default",
                ""),
            "A layout change must fail closed.");
        Require(
            !current.Matches(
                secondGeneration,
                "dialog",
                "field",
                "Alias",
                "Enter alias",
                "text",
                false,
                false,
                "full",
                "detail",
                ""),
            "A visual-kind change must fail closed.");
        return Task.CompletedTask;
    }

    private static Task ReadOnlyTransitionFailsClosedAsync()
    {
        NativeDialogInteractionGate gate = new();
        long generation = gate.BeginRender();
        NativeDialogFieldBinding binding = NewBinding(generation);
        Require(
            !binding.Matches(
                generation,
                "dialog",
                "field",
                "Alias",
                "Enter alias",
                "text",
                false,
                true,
                "full",
                "default",
                ""),
            "An editable field that becomes read-only must fail closed.");
        return Task.CompletedTask;
    }

    private static async Task DoubleTapExecutesExactlyOnceAsync()
    {
        NativeDialogInteractionGate gate = new();
        gate.BeginRender();
        Require(gate.TryClaimAction(), "The first tap must claim the action.");
        Require(!gate.TryClaimAction(), "A second tap must not claim an in-flight action.");
        int actionCount = 0;
        int failureCount = 0;
        await gate.RunClaimedActionAsync(
            () =>
            {
                actionCount++;
                gate.BeginRender();
                return Task.CompletedTask;
            },
            _ =>
            {
                failureCount++;
                return Task.CompletedTask;
            });
        Require(actionCount == 1, "A double tap must execute one action.");
        Require(failureCount == 0, "The double-tap guard must not report a failure.");
    }

    private static async Task CloseWaitsForClaimedActionAsync()
    {
        NativeDialogInteractionGate gate = new();
        gate.BeginRender();
        Require(gate.TryClaimAction(), "The action claim must succeed before the close race.");
        TaskCompletionSource actionEntered = NewSignal();
        TaskCompletionSource releaseAction = NewSignal();
        List<string> sequence = [];

        Task action = gate.RunClaimedActionAsync(
            async () =>
            {
                sequence.Add("action-start");
                actionEntered.SetResult();
                await releaseAction.Task;
                sequence.Add("action-end");
            },
            _ => Task.CompletedTask);
        await actionEntered.Task;

        Task close = gate.RunCloseAsync(() =>
        {
            sequence.Add("close");
            return Task.CompletedTask;
        });
        await Task.Yield();
        Require(!close.IsCompleted, "Close must wait for the claimed action.");
        Require(!gate.TryClaimAction(), "A close request must reject any further action claim.");

        releaseAction.SetResult();
        await Task.WhenAll(action, close);
        Require(
            sequence.SequenceEqual(["action-start", "action-end", "close"]),
            "Close must run after the action without interleaving.");
        Require(gate.IsClosed, "The serialized close must permanently close the interaction gate.");
    }

    private static async Task FailureRerendersBeforeQueueAdvancesAsync()
    {
        NativeDialogInteractionGate gate = new();
        long failedGeneration = gate.BeginRender();
        Require(gate.TryClaimAction(), "The failing action claim must succeed.");
        int executeCount = 0;
        int failureCount = 0;
        int staleMutationCount = 0;
        List<string> sequence = [];

        Task action = gate.RunClaimedActionAsync(
            () =>
            {
                sequence.Add("flush-invalid");
                throw new InvalidOperationException("invalid value");
            },
            _ =>
            {
                failureCount++;
                sequence.Add("rerender");
                gate.BeginRender();
                return Task.CompletedTask;
            });
        Task stale = gate.RunFieldUpdateAsync(failedGeneration, () =>
        {
            staleMutationCount++;
            return Task.CompletedTask;
        });
        await Task.WhenAll(action, stale);

        Require(executeCount == 0, "An invalid flush must not execute the action.");
        Require(failureCount == 1, "The invalid flush must invoke one failure rerender.");
        Require(staleMutationCount == 0, "The rerender must invalidate callbacks queued behind the failure.");
        Require(sequence.SequenceEqual(["flush-invalid", "rerender"]), "Rerender must occur inside the action boundary.");
    }

    private static NativeDialogFieldBinding NewBinding(long generation)
        => new(
            generation,
            "dialog",
            "field",
            "Alias",
            "Enter alias",
            "text",
            false,
            false,
            "full",
            "default",
            "");

    private static bool Matches(NativeDialogFieldBinding binding, long generation)
        => binding.Matches(
            generation,
            "dialog",
            "field",
            "Alias",
            "Enter alias",
            "text",
            false,
            false,
            "full",
            "default",
            "");

    private static TaskCompletionSource NewSignal()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
