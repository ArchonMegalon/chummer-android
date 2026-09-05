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
            (nameof(TalentGrantSelectionsRemainExactAndExoticChoicesFailClosedAsync), TalentGrantSelectionsRemainExactAndExoticChoicesFailClosedAsync),
            (nameof(PreAuthorityCreationSnapshotCanScheduleBootstrapAsync), PreAuthorityCreationSnapshotCanScheduleBootstrapAsync),
            (nameof(BuildPageProjectsExactlyOneLifecycleRouteAsync), BuildPageProjectsExactlyOneLifecycleRouteAsync),
            (nameof(CreationIdentityGapFailsClosedAsync), CreationIdentityGapFailsClosedAsync),
            (nameof(DurableSaveNoticeFailsClosedAcrossStateChangesAsync), DurableSaveNoticeFailsClosedAcrossStateChangesAsync),
            (nameof(CoordinatorRefreshBurstsRenderOnlyLatestStateAsync), CoordinatorRefreshBurstsRenderOnlyLatestStateAsync),
            (nameof(PageActionGateRejectsOverlappingActionsAsync), PageActionGateRejectsOverlappingActionsAsync),
            (nameof(ExactWorkspaceAuthoritySnapshotRejectsEveryHostileBindingAsync), ExactWorkspaceAuthoritySnapshotRejectsEveryHostileBindingAsync),
#if DEBUG
            (nameof(HomeAppearanceAuthorityRefreshFailsClosedForEveryHostileBindingAsync), HomeAppearanceAuthorityRefreshFailsClosedForEveryHostileBindingAsync),
#endif
            (nameof(AuthoritativeCreateReusesExactlyOnePresenterShellSyncAsync), AuthoritativeCreateReusesExactlyOnePresenterShellSyncAsync),
            (nameof(AmbiguousOrFailedCreateRetainsFullAndroidShellSyncAsync), AmbiguousOrFailedCreateRetainsFullAndroidShellSyncAsync),
            (nameof(PlayReviewPolicyEnforcesUsageVersionAndCooldownAsync), PlayReviewPolicyEnforcesUsageVersionAndCooldownAsync),
            (nameof(PlayReviewSafetyRejectsEveryMutationBoundaryAsync), PlayReviewSafetyRejectsEveryMutationBoundaryAsync),
            (nameof(PlayReviewInstallGateAndBackupBindingFailClosedAsync), PlayReviewInstallGateAndBackupBindingFailClosedAsync),
            (nameof(PlayReviewForegroundAccountingIsMonotonicAndSessionSafeAsync), PlayReviewForegroundAccountingIsMonotonicAndSessionSafeAsync),
            (nameof(PlayReviewFakeLauncherAndSilentFailureAreInjectableAsync), PlayReviewFakeLauncherAndSilentFailureAreInjectableAsync),
            (nameof(PlayReviewFileStateRoundTripsOnlyLocalPolicyDataAsync), PlayReviewFileStateRoundTripsOnlyLocalPolicyDataAsync),
            (nameof(SlowCreationDashboardProjectionDoesNotBlockCallerAsync), SlowCreationDashboardProjectionDoesNotBlockCallerAsync),
            (nameof(CapturedCreationAuthorityAvoidsUiThreadReloadAsync), CapturedCreationAuthorityAvoidsUiThreadReloadAsync),
            (nameof(CreationProjectionSchedulingPrioritizesWizardAllocationAsync), CreationProjectionSchedulingPrioritizesWizardAllocationAsync),
            (nameof(CreationSkillsCatalogPagingBoundsNativeControlMaterializationAsync), CreationSkillsCatalogPagingBoundsNativeControlMaterializationAsync),
            (nameof(PrerequisiteAuthorityPublishesBeforeSlowLaterPhasesAsync), PrerequisiteAuthorityPublishesBeforeSlowLaterPhasesAsync),
            (nameof(CreationAuthorityPhaseMergesAreIndependentAndDeterministicAsync), CreationAuthorityPhaseMergesAreIndependentAndDeterministicAsync),
            (nameof(CreationDashboardReadyMarkerRequiresCurrentTerminalAuthorityAsync), CreationDashboardReadyMarkerRequiresCurrentTerminalAuthorityAsync),
            (nameof(ExactTypedCreationAuthorityRehydratesConservativeStageAsync), ExactTypedCreationAuthorityRehydratesConservativeStageAsync),
            (nameof(ResourcesAuxiliaryStateDigestUsesRawLowerSha256Async), ResourcesAuxiliaryStateDigestUsesRawLowerSha256Async),
            (nameof(ResourcesRawCharacterXmlDigestNormalizationFailsClosedAsync), ResourcesRawCharacterXmlDigestNormalizationFailsClosedAsync),
            (nameof(ResourcesStageRehydrationRejectsEveryHostileAuthorityShapeAsync), ResourcesStageRehydrationRejectsEveryHostileAuthorityShapeAsync),
            (nameof(CompletedCreationProjectionSurvivesADeferredUiConsumerAsync), CompletedCreationProjectionSurvivesADeferredUiConsumerAsync),
            (nameof(RejectedCreationProjectionForcesCurrentBindingRefreshAsync), RejectedCreationProjectionForcesCurrentBindingRefreshAsync),
            (nameof(LateCreationDashboardProjectionCannotOverwriteNewerBindingAsync), LateCreationDashboardProjectionCannotOverwriteNewerBindingAsync),
            (nameof(CancelledOrFaultedCreationDashboardProjectionIsObservedAsync), CancelledOrFaultedCreationDashboardProjectionIsObservedAsync),
            (nameof(AttributesPreviewAdoptionRequiresCanonicalSuccessAsync), AttributesPreviewAdoptionRequiresCanonicalSuccessAsync),
            (nameof(AttributesBodPreviewCannotConfirmAgiDraftAsync), AttributesBodPreviewCannotConfirmAgiDraftAsync),
            (nameof(AttributesReceiptMustMatchCommittedWorkspaceBeforeActivationAsync), AttributesReceiptMustMatchCommittedWorkspaceBeforeActivationAsync),
            (nameof(SkillsPreviewAdoptionPreservesOnlyCoreProjectionAsync), SkillsPreviewAdoptionPreservesOnlyCoreProjectionAsync),
            (nameof(SkillsCommittedRefreshFailureRemainsCommittedAsync), SkillsCommittedRefreshFailureRemainsCommittedAsync)
        ];

        foreach ((string name, Func<Task> run) in tests)
        {
            await run();
            Console.WriteLine($"PASS {name}");
        }

        Console.WriteLine($"Native dialog interaction tests passed: {tests.Length}");
    }

    private static Task CoordinatorRefreshBurstsRenderOnlyLatestStateAsync()
    {
        var coalescer = new NativeRefreshCoalescer();
        Require(coalescer.Request(), "The first refresh request did not acquire dispatcher ownership.");
        for (int index = 0; index < 128; index++)
        {
            Require(!coalescer.Request(), "A refresh burst scheduled more than one dispatcher owner.");
        }
        Require(coalescer.TryTakePending(), "The refresh burst did not retain its latest state.");
        Require(!coalescer.TryTakePending(), "One refresh burst was consumed more than once.");

        for (int index = 0; index < 128; index++)
        {
            Require(!coalescer.Request(), "A request during rendering bypassed the active owner.");
        }
        Require(
            coalescer.Complete(allowReschedule: true),
            "A state change during rendering did not reserve exactly one follow-up pass.");
        Require(coalescer.TryTakePending(), "The follow-up pass lost the newest state.");
        coalescer.DiscardPending();
        Require(
            !coalescer.Complete(allowReschedule: true),
            "A completed refresh with no pending state scheduled an empty pass.");

        Require(coalescer.Request(), "The coalescer did not accept a later independent burst.");
        coalescer.DiscardPending();
        Require(
            !coalescer.Complete(allowReschedule: false),
            "A disappearing page retained dispatcher ownership.");
        return Task.CompletedTask;
    }

    private static Task PageActionGateRejectsOverlappingActionsAsync()
    {
        var gate = new NativePageActionGate();
        Require(!gate.IsClaimed, "A new page action gate started claimed.");
        Require(gate.TryClaim(), "The first page action did not acquire ownership.");
        Require(gate.IsClaimed, "The page action claim was not observable.");
        for (int index = 0; index < 128; index++)
        {
            Require(!gate.TryClaim(), "An overlapping page action acquired ownership.");
        }

        gate.Release();
        Require(!gate.IsClaimed, "The completed page action retained ownership.");
        Require(gate.TryClaim(), "A later independent page action was rejected.");
        gate.Release();
        return Task.CompletedTask;
    }

    private static Task BuildPageProjectsExactlyOneLifecycleRouteAsync()
    {
        var workspaceId = new CharacterWorkspaceId("phone-route-projection");
        CharacterOverviewState creation = NewCreationOverview(workspaceId, 7, 7);
        CharacterOverviewState career = creation with
        {
            Profile = creation.Profile! with { Created = true }
        };

        BuildPageRouteMarker[] projected =
        [
            BuildPageUiProjection.RouteMarker(null),
            BuildPageUiProjection.RouteMarker(creation.Profile),
            BuildPageUiProjection.RouteMarker(career.Profile)
        ];
        Require(
            projected.Select(marker => marker.AutomationId).SequenceEqual(
            ["phone-runner-empty", "phone-runner-create", "phone-runner-sheet"]),
            "Each lifecycle must project exactly its one route marker.");
        Require(
            projected.All(marker => !string.IsNullOrWhiteSpace(marker.Label)),
            "Every projected route marker must retain an accessible label.");
        Require(
            projected.Select(marker => marker.AutomationId).Distinct(StringComparer.Ordinal).Count() == 3,
            "Empty, creation, and career route identities must remain disjoint.");
        return Task.CompletedTask;
    }

    private static Task CreationIdentityGapFailsClosedAsync()
    {
        CreationIdentityRouteState projected = BuildPageUiProjection.CreationIdentityRoute(
            ["core-identity-draft-blocked"]);
        Require(!projected.IsEnabled, "A missing typed Creation Identity draft contract enabled a route.");
        Require(
            projected.Blocker == "core-identity-draft-blocked",
            "The exact Core Identity blocker was replaced by presentation text.");

        CreationIdentityRouteState missing = BuildPageUiProjection.CreationIdentityRoute([]);
        Require(!missing.IsEnabled, "An Identity stage without a Core blocker enabled a fallback route.");
        Require(
            missing.Blocker == BuildPageUiProjection.CreationIdentityDraftContractUnavailable,
            "The missing Creation Identity contract was not exposed explicitly.");
        return Task.CompletedTask;
    }

    private static Task ExactWorkspaceAuthoritySnapshotRejectsEveryHostileBindingAsync()
    {
        var workspaceId = new CharacterWorkspaceId("proof-workspace");
        CharacterOverviewState state = NewCreationOverview(workspaceId, 17, 17);
        string payloadSha256 = new('a', 64);
        var authority = new NativeWorkspaceAuthoritySnapshot(
            workspaceId.Value,
            state.ContentRevision,
            state.SavedRevision,
            payloadSha256,
            new string('b', 64));
        Require(
            RunnerSessionCoordinator.ExactWorkspaceAuthoritySnapshotMatches(
                authority,
                state,
                workspaceId,
                17,
                17,
                payloadSha256),
            "The exact current workspace authority was rejected.");

        (string Name, NativeWorkspaceAuthoritySnapshot? Authority)[] hostile =
        [
            ("null", null),
            ("workspace", authority with { WorkspaceId = "other-workspace" }),
            ("content revision", authority with { ContentRevision = 18 }),
            ("saved revision", authority with { SavedRevision = 16 }),
            ("payload", authority with { PayloadSha256 = new string('c', 64) })
        ];
        foreach ((string name, NativeWorkspaceAuthoritySnapshot? candidate) in hostile)
        {
            Require(
                !RunnerSessionCoordinator.ExactWorkspaceAuthoritySnapshotMatches(
                    candidate,
                    state,
                    workspaceId,
                    17,
                    17,
                    payloadSha256),
                $"Hostile {name} authority matched the exact workspace binding.");
        }

        (string Name, CharacterOverviewState State)[] stateDrift =
        [
            ("workspace", NewCreationOverview(
                new CharacterWorkspaceId("other-workspace"), 17, 17)),
            ("content revision", NewCreationOverview(workspaceId, 18, 17)),
            ("saved revision", NewCreationOverview(workspaceId, 17, 16))
        ];
        foreach ((string name, CharacterOverviewState candidate) in stateDrift)
        {
            Require(
                !RunnerSessionCoordinator.ExactWorkspaceAuthoritySnapshotMatches(
                    authority,
                    candidate,
                    workspaceId,
                    17,
                    17,
                    payloadSha256),
                $"Hostile state-side {name} drift matched the exact workspace binding.");
        }
        return Task.CompletedTask;
    }

#if DEBUG
    private static Task HomeAppearanceAuthorityRefreshFailsClosedForEveryHostileBindingAsync()
    {
        var workspaceId = new CharacterWorkspaceId("home-appearance-workspace");
        CharacterOverviewState state = NewCreationOverview(workspaceId, 23, 23);
        var current = new NativeWorkspaceAuthoritySnapshot(
            workspaceId.Value,
            state.ContentRevision,
            state.SavedRevision,
            new string('a', 64),
            new string('b', 64));

        Require(
            !RunnerSessionCoordinator.ShouldRefreshWorkspaceAuthorityForPageAppearance(
                debugE2EAuthorityEnabled: false,
                state,
                authority: null),
            "A normal product appearance attempted to activate the debug E2E authority surface.");
        Require(
            !RunnerSessionCoordinator.ShouldRefreshWorkspaceAuthorityForPageAppearance(
                debugE2EAuthorityEnabled: true,
                state,
                current),
            "An exact current authority scheduled a redundant page-appearance read.");
        Require(
            RunnerSessionCoordinator.ShouldRefreshWorkspaceAuthorityForPageAppearance(
                debugE2EAuthorityEnabled: true,
                state,
                authority: null),
            "A missing E2E authority was not reacquired for the appearing Runners page.");

        NativeWorkspaceAuthoritySnapshot[] hostile =
        [
            current with { WorkspaceId = "foreign-workspace" },
            current with { ContentRevision = 24 },
            current with { SavedRevision = 22 }
        ];
        foreach (NativeWorkspaceAuthoritySnapshot authority in hostile)
        {
            Require(
                RunnerSessionCoordinator.ShouldRefreshWorkspaceAuthorityForPageAppearance(
                    debugE2EAuthorityEnabled: true,
                    state,
                    authority),
                "A stale or foreign cached authority suppressed the exact appearance refresh.");
        }

        CharacterOverviewState noWorkspace = state with { WorkspaceId = null };
        Require(
            !RunnerSessionCoordinator.ShouldRefreshWorkspaceAuthorityForPageAppearance(
                debugE2EAuthorityEnabled: true,
                noWorkspace,
                authority: null),
            "An appearance without an active workspace attempted an authority read.");
        return Task.CompletedTask;
    }
#endif

    private static Task ExactTypedCreationAuthorityRehydratesConservativeStageAsync()
    {
        CharacterCreationWizardStageState resources = ConservativeStage(
            CharacterCreationWizardStepIds.Resources);
        string[] admitted =
        [
            CharacterCreationWizardStepIds.Attributes,
            CharacterCreationWizardStepIds.Skills,
            CharacterCreationWizardStepIds.ContactsLifestyles,
            CharacterCreationWizardStepIds.Resources
        ];
        string[] rejected =
        [
            CharacterCreationWizardStepIds.Qualities,
            CharacterCreationWizardStepIds.MagicResonance,
            CharacterCreationWizardStepIds.IdentityStory,
            CharacterCreationWizardStepIds.Review,
            "unknown-created-stage"
        ];

        Require(
            BuildPageUiProjection.CanOpenExactTypedCreationStage(
                resources,
                exactTypedAuthorityReady: true),
            "An exact typed Resources projection did not rehydrate its conservative generic stage after reopen.");
        Require(
            !BuildPageUiProjection.CanOpenExactTypedCreationStage(
                resources,
                exactTypedAuthorityReady: false),
            "A generic Resources placeholder opened without exact typed authority.");
        foreach (string stepId in admitted)
        {
            Require(
                BuildPageUiProjection.CanOpenExactTypedCreationStage(
                    ConservativeStage(stepId),
                    exactTypedAuthorityReady: true),
                $"The exact typed {stepId} route was omitted from the closed rehydration set.");
            Require(
                !BuildPageUiProjection.CanOpenExactTypedCreationStage(
                    ConservativeStage(stepId) with { IsAvailable = true },
                    exactTypedAuthorityReady: false),
                $"The generic {stepId} flag opened without exact typed authority.");
        }
        foreach (string stepId in rejected)
        {
            Require(
                !BuildPageUiProjection.CanOpenExactTypedCreationStage(
                    ConservativeStage(stepId),
                    exactTypedAuthorityReady: true),
                $"Typed route rehydration escaped to non-owned stage {stepId}.");
        }
        Require(
            !resources.IsAvailable
            && resources.Blockers.SequenceEqual(
                ["creation-wizard-legal-options-authority-unavailable"],
                StringComparer.Ordinal),
            "Typed route rehydration mutated or discarded the immutable generic snapshot evidence.");
        Require(
            !BuildPageUiProjection.CanOpenExactTypedCreationStage(
                resources with
                {
                    Blockers = ["creation-stage-prerequisite-incomplete"]
                },
                exactTypedAuthorityReady: true),
            "Exact typed authority overrode a generic blocker that was not the conservative legal-options placeholder.");
        Require(
            !BuildPageUiProjection.CanOpenExactTypedCreationStage(
                resources with
                {
                    IsAvailable = true,
                    Blockers = ["creation-stage-prerequisite-incomplete"]
                },
                exactTypedAuthorityReady: true),
            "Exact typed authority trusted a contradictory generic available flag with a semantic blocker.");
        return Task.CompletedTask;
    }

    private static Task ResourcesStageRehydrationRejectsEveryHostileAuthorityShapeAsync()
    {
        ResourcesFixture fixture = NewResourcesFixture();
        Require(
            BuildPageUiProjection.HasExactTypedResourcesAuthority(
                fixture.Load,
                fixture.Overview),
            "The valid exact typed Resources authority was not accepted.");
        Require(
            BuildPageUiProjection.CanOpenExactTypedCreationStage(
                ConservativeStage(CharacterCreationWizardStepIds.Resources),
                BuildPageUiProjection.HasExactTypedResourcesAuthority(
                    fixture.Load,
                    fixture.Overview)),
            "A conservative generic Resources stage did not open from its exact typed authority.");

        var foreignWorkspace = new CharacterWorkspaceId("resources-foreign-workspace");
        var disabledOption = fixture.State.Options.Single() with { IsEnabled = false };
        var invalidAllocationDigest = fixture.State.Options.Single() with { OptionDigest = "invalid" };
        CharacterCreationResourcePriorityOption priority = fixture.State.Authority.PriorityOptions.Single();
        (string Name, CharacterCreationResourcesInteractionLoadResult Load, CharacterOverviewState Overview)[] hostile =
        [
            ("outcome", fixture.Load with { Outcome = CharacterCreationResourcesOutcomes.Blocked }, fixture.Overview),
            ("wrong workspace", fixture.Load with { State = fixture.State with { Binding = fixture.State.Binding with { WorkspaceId = foreignWorkspace } } }, fixture.Overview),
            ("workspace revision", fixture.Load with { State = fixture.State with { Binding = fixture.State.Binding with { WorkspaceRevision = fixture.State.Binding.WorkspaceRevision + 1 } } }, fixture.Overview),
            ("content revision", fixture.Load with { State = fixture.State with { Binding = fixture.State.Binding with { ContentRevision = fixture.State.Binding.ContentRevision + 1 } } }, fixture.Overview),
            ("saved revision", fixture.Load with { State = fixture.State with { Binding = fixture.State.Binding with { SavedRevision = fixture.State.Binding.SavedRevision + 1 } } }, fixture.Overview),
            ("raw XML mismatch", fixture.Load with { State = fixture.State with { Binding = fixture.State.Binding with { RawCharacterXmlDigest = CanonicalDigest('e') } } }, fixture.Overview),
            ("raw XML digest", fixture.Load with { State = fixture.State with { Binding = fixture.State.Binding with { RawCharacterXmlDigest = "invalid" } } }, fixture.Overview),
            ("auxiliary digest", fixture.Load with { State = fixture.State with { Binding = fixture.State.Binding with { AuxiliaryStateDigest = "invalid" } } }, fixture.Overview),
            ("prerequisite digest", fixture.Load with { State = fixture.State with { Binding = fixture.State.Binding with { PrerequisiteDraftDigest = "invalid" } } }, fixture.Overview),
            ("binding authority digest", fixture.Load with { State = fixture.State with { Binding = fixture.State.Binding with { AuthorityDigest = "invalid" } } }, fixture.Overview),
            ("binding source digest", fixture.Load with { State = fixture.State with { Binding = fixture.State.Binding with { SourceDigest = "invalid" } } }, fixture.Overview),
            ("binding rules digest", fixture.Load with { State = fixture.State with { Binding = fixture.State.Binding with { RulesDigest = "invalid" } } }, fixture.Overview),
            ("binding runtime digest", fixture.Load with { State = fixture.State with { Binding = fixture.State.Binding with { RuntimeDigest = "invalid" } } }, fixture.Overview),
            ("snapshot digest", fixture.Load with { State = fixture.State with { SnapshotDigest = "invalid" } }, fixture.Overview),
            ("canonical binding authority mismatch", fixture.Load with { State = fixture.State with { Binding = fixture.State.Binding with { AuthorityDigest = CanonicalDigest('b') } } }, fixture.Overview),
            ("canonical binding source mismatch", fixture.Load with { State = fixture.State with { Binding = fixture.State.Binding with { SourceDigest = CanonicalDigest('c') } } }, fixture.Overview),
            ("canonical binding rules mismatch", fixture.Load with { State = fixture.State with { Binding = fixture.State.Binding with { RulesDigest = CanonicalDigest('d') } } }, fixture.Overview),
            ("canonical binding runtime mismatch", fixture.Load with { State = fixture.State with { Binding = fixture.State.Binding with { RuntimeDigest = CanonicalDigest('e') } } }, fixture.Overview),
            ("authority digest", fixture.Load with { State = fixture.State with { Authority = fixture.State.Authority with { AuthorityDigest = "invalid" } } }, fixture.Overview),
            ("authority source digest", fixture.Load with { State = fixture.State with { Authority = fixture.State.Authority with { SourceDigest = "invalid" } } }, fixture.Overview),
            ("authority profile digest", fixture.Load with { State = fixture.State with { Authority = fixture.State.Authority with { ProfileDigest = "invalid" } } }, fixture.Overview),
            ("authority rules digest", fixture.Load with { State = fixture.State with { Authority = fixture.State.Authority with { RulesDigest = "invalid" } } }, fixture.Overview),
            ("authority runtime digest", fixture.Load with { State = fixture.State with { Authority = fixture.State.Authority with { RuntimeDigest = "invalid" } } }, fixture.Overview),
            ("priority source node digest", fixture.Load with { State = fixture.State with { Authority = fixture.State.Authority with { PriorityOptions = [priority with { SourceNodeDigest = "invalid" }] } } }, fixture.Overview),
            ("priority option digest", fixture.Load with { State = fixture.State with { Authority = fixture.State.Authority with { PriorityOptions = [priority with { OptionDigest = "invalid" }] } } }, fixture.Overview),
            ("allocation option digest", fixture.Load with { State = fixture.State with { Options = [invalidAllocationDigest] } }, fixture.Overview),
            ("cannot edit", fixture.Load with { State = fixture.State with { CanEdit = false } }, fixture.Overview),
            ("state blocker", fixture.Load with { State = fixture.State with { Blockers = [CharacterCreationResourcesBlockers.AuthorityUnavailable] } }, fixture.Overview),
            ("budget blocker", fixture.Load with { State = fixture.State with { Budget = fixture.State.Budget with { Blockers = [CharacterCreationResourcesBlockers.InsufficientCreationKarma] } } }, fixture.Overview),
            ("budget inexact", fixture.Load with { State = fixture.State with { Budget = fixture.State.Budget with { IsExact = false } } }, fixture.Overview),
            ("budget mismatch", fixture.Load with { State = fixture.State with { Budget = fixture.State.Budget with { TotalStartingNuyen = fixture.State.Budget.TotalStartingNuyen + 1m } } }, fixture.Overview),
            ("zero options", fixture.Load with { State = fixture.State with { Options = [] } }, fixture.Overview),
            ("no enabled option", fixture.Load with { State = fixture.State with { Options = [disabledOption] } }, fixture.Overview)
        ];
        foreach ((string name, CharacterCreationResourcesInteractionLoadResult load, CharacterOverviewState overview) in hostile)
        {
            bool ready = BuildPageUiProjection.HasExactTypedResourcesAuthority(load, overview);
            Require(!ready, $"Hostile Resources authority shape '{name}' was accepted.");
            Require(
                !BuildPageUiProjection.CanOpenExactTypedCreationStage(
                    ConservativeStage(CharacterCreationWizardStepIds.Resources) with { IsAvailable = true },
                    ready),
                $"Hostile Resources authority shape '{name}' opened through the generic stage flag.");
        }

        Require(
            CreationDashboardProjectionBinding.TryCreate(
                fixture.Overview,
                fixture.Overview.CreationWizard!,
                out CreationDashboardProjectionBinding? projectionBinding)
            && projectionBinding is not null,
            "The Resources blocker fixture did not produce an exact dashboard binding.");
        var blockedLoad = fixture.Load with
        {
            State = fixture.State with
            {
                Blockers = [CharacterCreationResourcesBlockers.AuthorityUnavailable]
            }
        };
        var blockedProjection = new CreationDashboardAuthorityProjection(
            projectionBinding!,
            CreationDashboardAuthorityPhaseProgress
                .ForBuildMethod(CharacterCreationBuildMethods.Priority)
                .WithTerminal(CreationDashboardAuthorityPhase.Resources, failed: false),
            Prerequisite: null,
            Attributes: null,
            Skills: null,
            Contacts: null,
            Resources: blockedLoad);
        Require(
            string.Equals(
                BuildPageUiProjection.CreationResourcesStageBlocker(blockedProjection),
                CharacterCreationResourcesBlockers.AuthorityUnavailable,
                StringComparison.Ordinal),
            "A terminal-loaded but semantically blocked Resources result hid its direct typed blocker.");
        return Task.CompletedTask;
    }

    private static Task ResourcesAuxiliaryStateDigestUsesRawLowerSha256Async()
    {
        ResourcesFixture fixture = NewResourcesFixture();
        Require(
            fixture.State.Binding.AuxiliaryStateDigest == new string('2', 64),
            "The valid Resources fixture did not use the Core/Workspace raw auxiliary SHA-256 contract.");
        Require(
            BuildPageUiProjection.HasExactTypedResourcesAuthority(fixture.Load, fixture.Overview),
            "An exact 64-character lowercase raw Resources auxiliary SHA-256 was rejected.");

        (string Name, string Digest)[] hostile =
        [
            ("prefixed", CanonicalDigest('2')),
            ("uppercase", new string('A', 64)),
            ("short", new string('a', 63)),
            ("nonhex", new string('g', 64)),
            ("empty", string.Empty)
        ];
        foreach ((string name, string digest) in hostile)
        {
            CharacterCreationResourcesInteractionLoadResult load = fixture.Load with
            {
                State = fixture.State with
                {
                    Binding = fixture.State.Binding with
                    {
                        AuxiliaryStateDigest = digest
                    }
                }
            };
            Require(
                !BuildPageUiProjection.HasExactTypedResourcesAuthority(load, fixture.Overview),
                $"The {name} Resources auxiliary digest escaped the exact raw lowercase SHA-256 boundary.");
        }
        return Task.CompletedTask;
    }

    private static Task ResourcesRawCharacterXmlDigestNormalizationFailsClosedAsync()
    {
        string canonical = "sha256:" + new string('a', 64);
        Require(
            CreationResourcesPhoneAuthority.TryNormalizeRawCharacterXmlSha256(
                canonical,
                out string normalized)
            && normalized == new string('a', 64),
            "The canonical Resources raw-XML digest was not normalized exactly.");

        string?[] hostile =
        [
            null,
            string.Empty,
            "sha256:",
            "sha256:" + new string('a', 63),
            "sha256:" + new string('a', 65),
            "sha256:" + new string('A', 64),
            "sha512:" + new string('a', 64),
            canonical + "\n"
        ];
        foreach (string? candidate in hostile)
        {
            Require(
                !CreationResourcesPhoneAuthority.TryNormalizeRawCharacterXmlSha256(
                    candidate,
                    out string rejected)
                && rejected == string.Empty,
                $"Malformed Resources raw-XML digest '{candidate ?? "<null>"}' was normalized.");
        }
        return Task.CompletedTask;
    }

    private static CharacterCreationWizardStageState ConservativeStage(string stepId)
        => new(
            stepId,
            stepId,
            CharacterCreationWizardStepStatuses.Blocked,
            IsRequired: true,
            IsAvailable: false,
            IsComplete: false,
            [CharacterCreationBudgetIds.Resources],
            ["creation-wizard-legal-options-authority-unavailable"],
            [],
            []);

    private static Task PreAuthorityCreationSnapshotCanScheduleBootstrapAsync()
    {
        var workspaceId = new CharacterWorkspaceId("phone-pre-authority-bootstrap");
        CharacterOverviewState overview = NewCreationOverview(workspaceId, 11, 10);
        var snapshot = new CharacterCreationWizardSnapshot(
            CharacterCreationWizardSchemas.SnapshotV1,
            workspaceId.Value,
            WorkspaceRevision: 11,
            ContentDigest: CanonicalDigest('1'),
            SourceDigest: string.Empty,
            RulesetDefaults.Sr5,
            RuntimeFingerprint: string.Empty,
            CharacterCreationBuildMethods.Priority,
            CharacterCreated: false,
            CharacterCreationWizardStepIds.Foundation,
            [],
            [],
            new Dictionary<string, IReadOnlyList<CharacterCreationLegalOption>>(),
            [],
            [],
            CanFinalize: false,
            SnapshotDigest: CanonicalDigest('2'));

        Require(
            CreationDashboardProjectionBinding.TryCreate(overview, snapshot, out CreationDashboardProjectionBinding? binding)
            && binding is not null,
            "The initial snapshot must be able to schedule the Core load that obtains source/runtime authority.");
        CreationDashboardProjectionBinding bootstrapBinding = binding!;
        Require(
            bootstrapBinding.SourceDigest.Length == 0 && bootstrapBinding.RuntimeFingerprint.Length == 0,
            "The bootstrap binding must preserve absent authority instead of inventing authority values.");
        Require(
            !CreationDashboardProjectionBinding.TryCreate(
                overview,
                snapshot with { SourceDigest = null! },
                out _)
            && !CreationDashboardProjectionBinding.TryCreate(
                overview,
                snapshot with { SourceDigest = " " },
                out _)
            && !CreationDashboardProjectionBinding.TryCreate(
                overview,
                snapshot with { RuntimeFingerprint = null! },
                out _)
            && !CreationDashboardProjectionBinding.TryCreate(
                overview,
                snapshot with { RuntimeFingerprint = "\t" },
                out _),
            "Only the explicit empty bootstrap sentinel or a populated authority value may be bound.");
        Require(
            !CreationDashboardProjectionBinding.TryCreate(
                overview,
                snapshot with { ContentDigest = string.Empty },
                out _),
            "Bootstrap scheduling must still reject a snapshot without content identity.");
        Require(
            !CreationDashboardProjectionBinding.TryCreate(
                overview,
                snapshot with { WorkspaceRevision = 12 },
                out _),
            "Bootstrap scheduling must still reject revision drift.");
        Require(
            !bootstrapBinding.Matches(
                overview,
                snapshot with { SourceDigest = CanonicalDigest('3') })
            && !bootstrapBinding.Matches(
                overview,
                snapshot with { RuntimeFingerprint = "runtime-authority-v1" }),
            "Populated authority must change the binding key and invalidate the bootstrap request.");
        return Task.CompletedTask;
    }

    private static Task DurableSaveNoticeFailsClosedAcrossStateChangesAsync()
    {
        var workspaceId = new CharacterWorkspaceId("phone-save-proof");
        NativeDurableSaveNotice notice = new(workspaceId, SavedRevision: 12);
        CharacterOverviewState saved = NewCreationOverview(workspaceId, 12, 12);

        Require(notice.Matches(saved), "An exact clean saved revision must match its durable notice.");
        Require(
            !notice.Matches(saved with { Error = "save failed" }),
            "A failed rerender must invalidate the prior durable save notice.");
        Require(
            !notice.Matches(NewCreationOverview(workspaceId, 13, 12)),
            "A later dirty revision must invalidate the prior durable save notice.");
        Require(
            !notice.Matches(NewCreationOverview(new CharacterWorkspaceId("foreign"), 12, 12)),
            "A different workspace must not inherit a durable save notice.");
        Require(
            BuildPageUiProjection.SaveToolbarText(hasDurableSaveNotice: true) == "Saved."
            && BuildPageUiProjection.SaveToolbarText(hasDurableSaveNotice: false) == "Save",
            "The toolbar must expose Saved. only for an exact durable notice match.");
        return Task.CompletedTask;
    }

    private static async Task AuthoritativeCreateReusesExactlyOnePresenterShellSyncAsync()
    {
        var previousWorkspace = new CharacterWorkspaceId("previous-runner");
        var createdWorkspace = new CharacterWorkspaceId("created-runner");
        CharacterOverviewState created = NewCreationOverview(createdWorkspace, 1, 1);
        var timing = new NativeCreationBootstrapTimingSnapshot(
            StartedTimestamp: 10,
            LoadStartedTimestamp: 20,
            WorkspaceStatePublishedTimestamp: 30,
            PublishedWorkspaceId: createdWorkspace.Value);

        bool reuse = RunnerSessionCoordinator.CanReusePresenterShellSync(
            "create_character",
            previousWorkspace,
            created,
            timing);
        Require(
            reuse,
            "An exact successful authoritative create must reuse Presentation's completed shell sync.");

        int fullShellSyncCount = 0;
        int retainedAndroidRefreshCount = 0;
        await RunnerSessionCoordinator.ExecutePostDialogShellSyncAsync(
            reuse,
            _ =>
            {
                fullShellSyncCount++;
                return Task.CompletedTask;
            },
            _ =>
            {
                retainedAndroidRefreshCount++;
                return Task.CompletedTask;
            },
            CancellationToken.None);
        Require(
            fullShellSyncCount == 0 && retainedAndroidRefreshCount == 1,
            "Successful create must skip exactly the one duplicate shell sync while retaining one Android refresh.");
    }

    private static async Task AmbiguousOrFailedCreateRetainsFullAndroidShellSyncAsync()
    {
        var previousWorkspace = new CharacterWorkspaceId("previous-runner");
        var createdWorkspace = new CharacterWorkspaceId("created-runner");
        CharacterOverviewState created = NewCreationOverview(createdWorkspace, 1, 1);
        var exactTiming = new NativeCreationBootstrapTimingSnapshot(
            StartedTimestamp: 10,
            LoadStartedTimestamp: 20,
            WorkspaceStatePublishedTimestamp: 30,
            PublishedWorkspaceId: createdWorkspace.Value);
        DesktopDialogState stillOpen = new(
            "dialog.new_character",
            "New runner",
            null,
            [],
            [new DesktopDialogAction("create_character", "Create")]);
        (string Name, string ActionId, CharacterWorkspaceId? Before, CharacterOverviewState State, NativeCreationBootstrapTimingSnapshot? Timing)[] rejected =
        [
            ("other action", "save", previousWorkspace, created, exactTiming),
            ("missing timing", "create_character", previousWorkspace, created, null),
            ("load start absent", "create_character", previousWorkspace, created,
                exactTiming with { LoadStartedTimestamp = 0 }),
            ("workspace publication absent", "create_character", previousWorkspace, created,
                exactTiming with { WorkspaceStatePublishedTimestamp = 0 }),
            ("published workspace mismatch", "create_character", previousWorkspace, created,
                exactTiming with { PublishedWorkspaceId = "different-runner" }),
            ("same workspace", "create_character", createdWorkspace, created, exactTiming),
            ("presenter failure", "create_character", previousWorkspace,
                created with { Error = "bootstrap failed" }, exactTiming),
            ("presenter still busy", "create_character", previousWorkspace,
                created with { IsBusy = true }, exactTiming),
            ("dialog still open", "create_character", previousWorkspace,
                created with { ActiveDialog = stillOpen }, exactTiming),
            ("career profile", "create_character", previousWorkspace,
                created with { Profile = created.Profile! with { Created = true } }, exactTiming)
        ];

        foreach (var candidate in rejected)
        {
            bool reuse = RunnerSessionCoordinator.CanReusePresenterShellSync(
                candidate.ActionId,
                candidate.Before,
                candidate.State,
                candidate.Timing);
            Require(!reuse, $"{candidate.Name} must not reuse Presenter shell synchronization.");

            int fullShellSyncCount = 0;
            int retainedAndroidRefreshCount = 0;
            await RunnerSessionCoordinator.ExecutePostDialogShellSyncAsync(
                reuse,
                _ =>
                {
                    fullShellSyncCount++;
                    return Task.CompletedTask;
                },
                _ =>
                {
                    retainedAndroidRefreshCount++;
                    return Task.CompletedTask;
                },
                CancellationToken.None);
            Require(
                fullShellSyncCount == 1 && retainedAndroidRefreshCount == 0,
                $"{candidate.Name} must retain exactly one full Android shell sync.");
        }
    }

    private static Task PlayReviewPolicyEnforcesUsageVersionAndCooldownAsync()
    {
        DateTimeOffset now = new(2026, 8, 26, 12, 0, 0, TimeSpan.Zero);
        long threshold = PlayReviewPolicyOptions.Production.MinimumForegroundMilliseconds;
        Require(
            !PlayReviewPolicy.ShouldAttempt(
                new PlayReviewState(threshold - 1, null, null),
                "0.1.0-preview.10+10",
                now,
                PlayReviewPolicyOptions.Production),
            "The automatic review must not become eligible before one foreground hour.");
        Require(
            PlayReviewPolicy.ShouldAttempt(
                new PlayReviewState(threshold, null, null),
                "0.1.0-preview.10+10",
                now,
                PlayReviewPolicyOptions.Production),
            "The foreground threshold must admit the next safe moment.");

        PlayReviewState prior = new(
            threshold,
            now - TimeSpan.FromDays(31),
            "0.1.0-preview.10+10");
        Require(
            !PlayReviewPolicy.ShouldAttempt(
                prior,
                "0.1.0-preview.10+10",
                now,
                PlayReviewPolicyOptions.Production),
            "One app version must never receive a second production attempt.");
        Require(
            !PlayReviewPolicy.ShouldAttempt(
                prior with { LastAttemptUtc = now - TimeSpan.FromDays(29) },
                "0.1.0-preview.11+11",
                now,
                PlayReviewPolicyOptions.Production),
            "A new version must still observe the thirty-day cross-version cooldown.");
        Require(
            PlayReviewPolicy.ShouldAttempt(
                prior with { LastAttemptUtc = now - TimeSpan.FromDays(30) },
                "0.1.0-preview.11+11",
                now,
                PlayReviewPolicyOptions.Production),
            "The next version may request only after the full cooldown.");
        Require(
            !PlayReviewPolicy.ShouldAttempt(
                prior with { LastAttemptUtc = now + TimeSpan.FromDays(1) },
                "0.1.0-preview.11+11",
                now,
                PlayReviewPolicyOptions.Production),
            "A wall-clock rollback must fail closed.");
        Require(
            !PlayReviewPolicy.ShouldAttempt(
                prior with { LastAttemptUtc = null },
                "0.1.0-preview.11+11",
                now,
                PlayReviewPolicyOptions.Production),
            "Partial durable attempt history must fail closed.");
        return Task.CompletedTask;
    }

    private static Task PlayReviewSafetyRejectsEveryMutationBoundaryAsync()
    {
        PlayReviewSafetyContext safe = new(
            IsExplicitSafeSurface: true,
            IsRootNavigation: true,
            HasModal: false,
            HasActiveDialog: false,
            HasUnsavedMutation: false,
            HasActionInFlight: false);
        Require(safe.IsSafe, "The explicit clean root idle state must be eligible.");
        Require(!(safe with { IsExplicitSafeSurface = false }).IsSafe, "A wizard/editor surface must block review.");
        Require(!(safe with { IsRootNavigation = false }).IsSafe, "A nested draft/review page must block review.");
        Require(!(safe with { HasModal = true }).IsSafe, "A modal must block review.");
        Require(!(safe with { HasActiveDialog = true }).IsSafe, "A shared dialog must block review.");
        Require(!(safe with { HasUnsavedMutation = true }).IsSafe, "An unsaved mutation must block review.");
        Require(!(safe with { HasActionInFlight = true }).IsSafe, "Apply/conflict work must block review.");
        Require(!(safe with { HasBusyWork = true }).IsSafe, "Coordinator background work must block review.");
        return Task.CompletedTask;
    }

    private static async Task PlayReviewForegroundAccountingIsMonotonicAndSessionSafeAsync()
    {
        long minute = (long)TimeSpan.FromMinutes(1).TotalMilliseconds;
        var clock = new FakePlayReviewClock
        {
            UtcNow = new DateTimeOffset(2026, 8, 26, 12, 0, 0, TimeSpan.Zero),
            MonotonicMilliseconds = 10_000
        };
        var store = new FakePlayReviewStateStore(
            new PlayReviewState(59 * minute, null, null, "test-install"));
        var launcher = new FakePlayReviewLauncher { IsRuntimeAvailable = true };
        using var service = new PlayReviewService(
            store,
            clock,
            launcher,
            "0.1.0-preview.10+10");

        service.OnForegrounded();
        service.OnForegrounded();
        clock.MonotonicMilliseconds += 30_000;
        service.CheckpointForegroundUse();
        clock.MonotonicMilliseconds += 30_000;
        service.OnBackgrounded();
        service.OnBackgrounded();
        clock.MonotonicMilliseconds += 90_000;
        service.CheckpointForegroundUse();
        Require(
            service.State.ForegroundMilliseconds == 60 * minute,
            "Duplicate lifecycle callbacks must neither reset nor double-count monotonic foreground time.");
        Require(
            store.State.ForegroundMilliseconds == 60 * minute,
            "Each foreground checkpoint must durably preserve cumulative use.");

        Require(
            !await service.TryRequestAtSafeMomentAsync(SafeReviewContext()),
            "Eligibility alone must not launch without a meaningful safe success/library-idle signal.");
        service.SignalMeaningfulSuccess();
        clock.MonotonicMilliseconds +=
            (long)PlayReviewPolicy.MeaningfulSuccessWindow.TotalMilliseconds + 1;
        Require(
            !await service.TryRequestAtSafeMomentAsync(SafeReviewContext())
            && launcher.RequestCount == 0,
            "An old success signal must not let a later unrelated heartbeat launch review.");
        service.SignalMeaningfulSuccess();
        bool attempted = await service.TryRequestAtSafeMomentAsync(SafeReviewContext());
        Require(attempted && launcher.RequestCount == 1, "The injected launcher must receive one eligible attempt.");
        Require(
            store.State.LastAttemptVersion == "0.1.0-preview.10+10"
            && store.State.LastAttemptUtc == clock.UtcNow,
            "Attempt version and timestamp must be durable before invoking Play.");
        Require(
            !await service.TryRequestAtSafeMomentAsync(SafeReviewContext())
            && launcher.RequestCount == 1,
            "The same version must not issue a second request.");
    }

    private static async Task PlayReviewFakeLauncherAndSilentFailureAreInjectableAsync()
    {
        var clock = new FakePlayReviewClock
        {
            UtcNow = new DateTimeOffset(2026, 8, 26, 12, 0, 0, TimeSpan.Zero),
            MonotonicMilliseconds = 1
        };
        var throwingLauncher = new FakePlayReviewLauncher
        {
            IsRuntimeAvailable = true,
            ThrowOnRequest = true
        };
        var store = new FakePlayReviewStateStore(PlayReviewState.Empty);
        using var service = new PlayReviewService(store, clock, throwingLauncher, "debug+1");
        service.ConfigureDebugOverride(enabled: true);
        service.SignalMeaningfulSuccess();
        Require(
            await service.TryRequestAtSafeMomentAsync(SafeReviewContext()),
            "The debug override must exercise the injected launcher without an hour wait.");
        Require(
            throwingLauncher.RequestCount == 1 && store.State.LastAttemptVersion == "debug+1",
            "A Play error must be silent while retaining its durable attempt boundary.");

        var unavailableLauncher = new FakePlayReviewLauncher { IsRuntimeAvailable = false };
        var unavailableStore = new FakePlayReviewStateStore(PlayReviewState.Empty);
        using var unavailable = new PlayReviewService(
            unavailableStore,
            clock,
            unavailableLauncher,
            "debug+2");
        unavailable.ConfigureDebugOverride(enabled: true);
        unavailable.SignalMeaningfulSuccess();
        Require(
            !await unavailable.TryRequestAtSafeMomentAsync(SafeReviewContext())
            && unavailableStore.State.LastAttemptUtc is null,
            "A non-Play install must fail harmlessly without consuming an attempt.");
    }

    private static async Task PlayReviewInstallGateAndBackupBindingFailClosedAsync()
    {
        PlayReviewInstallContext canonical = new(
            PlayReviewPolicy.CanonicalApplicationId,
            PlayReviewPolicy.GooglePlayInstallerPackage,
            "install-current",
            IsReleaseBuild: true);
        Require(
            PlayReviewPolicy.IsEligibleInstallation(canonical, explicitTestOverride: false),
            "Only the canonical release package installed by Google Play may auto-request.");
        foreach (PlayReviewInstallContext ineligible in new[]
                 {
                     canonical with { ApplicationId = "com.myexternalbrain.chummer.sidecar" },
                     canonical with { InstallerPackageName = "com.android.shell" },
                     canonical with { InstallerPackageName = "org.fdroid.fdroid" },
                     canonical with { IsReleaseBuild = false },
                     canonical with { InstallIdentity = string.Empty }
                 })
        {
            Require(
                !PlayReviewPolicy.IsEligibleInstallation(ineligible, explicitTestOverride: false),
                $"A sidecar/debug/ADB/alternate-store install escaped the production gate: {ineligible}");
            Require(
                PlayReviewPolicy.IsEligibleInstallation(ineligible, explicitTestOverride: true),
                "The explicit test override must remain injectable without weakening production.");
        }

        var launcher = new FakePlayReviewLauncher
        {
            IsRuntimeAvailable = true,
            InstallContext = canonical
        };
        var restoredBackup = new FakePlayReviewStateStore(new PlayReviewState(
            PlayReviewPolicyOptions.Production.MinimumForegroundMilliseconds,
            null,
            null,
            "install-from-another-device"));
        var clock = new FakePlayReviewClock
        {
            UtcNow = new DateTimeOffset(2026, 8, 26, 12, 0, 0, TimeSpan.Zero),
            MonotonicMilliseconds = 100
        };
        using var service = new PlayReviewService(restoredBackup, clock, launcher, "release+10");
        Require(
            service.State.ForegroundMilliseconds == 0
            && service.State.InstallIdentity == "install-current",
            "Restored policy state must reset when the no-backup install identity changes.");

        using var killed = new PlayReviewService(
            new FakePlayReviewStateStore(PlayReviewState.Empty with { InstallIdentity = "install-current" }),
            clock,
            launcher,
            "release+10",
            automaticFlowEnabled: false);
        killed.ConfigureDebugOverride(enabled: true);
        killed.SignalMeaningfulSuccess();
        Require(
            !await killed.TryRequestAtSafeMomentAsync(SafeReviewContext()),
            "The build/local kill switch must dominate the debug override.");
    }

    private static Task PlayReviewFileStateRoundTripsOnlyLocalPolicyDataAsync()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"chummer-play-review-{Guid.NewGuid():N}");
        try
        {
            var store = new FilePlayReviewStateStore(directory);
            var expected = new PlayReviewState(
                ForegroundMilliseconds: 3_600_000,
                LastAttemptUtc: new DateTimeOffset(2026, 8, 26, 12, 0, 0, TimeSpan.Zero),
                LastAttemptVersion: "0.1.0-preview.10+10",
                InstallIdentity: "local-install-identity");
            store.Save(expected);
            Require(store.Load() == expected, "The local policy store must round-trip exactly its policy fields.");
            string json = File.ReadAllText(Path.Combine(directory, "play-review-policy.json"));
            Require(
                json.Contains("foregroundMilliseconds", StringComparison.Ordinal)
                && json.Contains("lastAttemptUtc", StringComparison.Ordinal)
                && json.Contains("lastAttemptVersion", StringComparison.Ordinal)
                && json.Contains("installIdentity", StringComparison.Ordinal),
                "The policy store lost a required local field.");
            foreach (string forbidden in new[] { "rating", "stars", "displayed", "submitted", "analytics", "telemetry" })
            {
                Require(
                    !json.Contains(forbidden, StringComparison.OrdinalIgnoreCase),
                    $"The policy store must not persist review telemetry: {forbidden}");
            }
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        return Task.CompletedTask;
    }

    private static PlayReviewSafetyContext SafeReviewContext()
        => new(
            IsExplicitSafeSurface: true,
            IsRootNavigation: true,
            HasModal: false,
            HasActiveDialog: false,
            HasUnsavedMutation: false,
            HasActionInFlight: false);

    private static async Task SlowCreationDashboardProjectionDoesNotBlockCallerAsync()
    {
        using var queue = new LatestBackgroundProjectionQueue<string, string>();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var completion = new TaskCompletionSource<BackgroundProjectionCompletion<string, string>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        queue.Completed += value => completion.TrySetResult(value);

        bool scheduled = queue.TryRequest(
            "workspace-1/revision-1/source-a",
            (_, cancellationToken) =>
            {
                entered.Set();
                release.Wait(cancellationToken);
                return "authoritative";
            },
            out BackgroundProjectionRequest<string> request);

        Require(scheduled, "The initial dashboard projection must be scheduled once.");
        Require(entered.Wait(TimeSpan.FromSeconds(5)), "The slow projection never entered its worker.");
        Require(
            !completion.Task.IsCompleted,
            "Request returned only after the slow projection completed; this would block Save rerendering.");
        Require(
            !queue.TryAccept(request),
            "A projection must not be accepted before its background result is ready.");

        release.Set();
        BackgroundProjectionCompletion<string, string> completed = await completion.Task
            .WaitAsync(TimeSpan.FromSeconds(5));
        Require(completed.Request == request, "The completed generation changed unexpectedly.");
        Require(completed.Result == "authoritative", "The worker result was not preserved.");
        Require(queue.TryAccept(completed.Request), "The exact completed generation was not accepted.");
    }

    private static Task CapturedCreationAuthorityAvoidsUiThreadReloadAsync()
    {
        var captured = new object();
        int loadCount = 0;
        object? reused = CreationPageAuthorityCache.Resolve(
            captured,
            candidate => ReferenceEquals(candidate, captured),
            () =>
            {
                loadCount++;
                return new object();
            });
        Require(
            ReferenceEquals(reused, captured) && loadCount == 0,
            "A current dashboard authority was synchronously reloaded on deep navigation.");

        var refreshed = new object();
        object? reloaded = CreationPageAuthorityCache.Resolve(
            captured,
            _ => false,
            () =>
            {
                loadCount++;
                return refreshed;
            });
        Require(
            ReferenceEquals(reloaded, refreshed) && loadCount == 1,
            "A stale dashboard authority did not reload exactly once from the current workspace.");
        return Task.CompletedTask;
    }

    private static Task CreationProjectionSchedulingPrioritizesWizardAllocationAsync()
    {
        CreationDashboardAuthorityPhaseProgress initial =
            CreationDashboardAuthorityPhaseProgress.ForBuildMethod(CharacterCreationBuildMethods.Priority);
        Require(
            CreationDashboardProjectionScheduler.NextBatch(initial).SequenceEqual(
                [CreationDashboardAuthorityPhase.Prerequisite]),
            "Priority Creation started lower-value projections before prerequisite authority.");

        CreationDashboardAuthorityPhaseProgress prerequisiteReady = initial.WithTerminal(
            CreationDashboardAuthorityPhase.Prerequisite,
            failed: false);
        Require(
            CreationDashboardProjectionScheduler.NextBatch(prerequisiteReady).SequenceEqual(
                [CreationDashboardAuthorityPhase.Attributes, CreationDashboardAuthorityPhase.Skills]),
            "Priority Creation did not give the two allocation projections exclusive next-batch priority.");
        Require(
            CreationDashboardProjectionScheduler.ShouldRenderAfterCompletion(
                CreationDashboardAuthorityPhase.Prerequisite,
                prerequisiteReady),
            "Priority Creation did not render its prerequisite authority as soon as it was ready.");

        CreationDashboardAuthorityPhaseProgress attributesReady = prerequisiteReady
            .WithTerminal(CreationDashboardAuthorityPhase.Attributes, failed: false);
        Require(
            !CreationDashboardProjectionScheduler.ShouldRenderAfterCompletion(
                CreationDashboardAuthorityPhase.Attributes,
                attributesReady),
            "Priority Creation rebuilt the dashboard before its allocation batch was terminal.");
        Require(
            !CreationDashboardProjectionScheduler.ShouldAdvanceWithoutRenderAfterCompletion(
                CreationDashboardAuthorityPhase.Attributes,
                attributesReady),
            "Priority Creation advanced before its allocation batch was terminal.");

        CreationDashboardAuthorityPhaseProgress allocationReady = prerequisiteReady
            .WithTerminal(CreationDashboardAuthorityPhase.Attributes, failed: false)
            .WithTerminal(CreationDashboardAuthorityPhase.Skills, failed: false);
        Require(
            CreationDashboardProjectionScheduler.NextBatch(allocationReady).SequenceEqual(
                [CreationDashboardAuthorityPhase.Contacts, CreationDashboardAuthorityPhase.Resources]),
            "Contacts and Resources did not wait for the allocation projections to become terminal.");
        Require(
            !CreationDashboardProjectionScheduler.ShouldRenderAfterCompletion(
                CreationDashboardAuthorityPhase.Skills,
                allocationReady),
            "Priority Creation rebuilt the dashboard merely to schedule its final batch.");
        Require(
            CreationDashboardProjectionScheduler.ShouldAdvanceWithoutRenderAfterCompletion(
                CreationDashboardAuthorityPhase.Skills,
                allocationReady),
            "Priority Creation did not advance its final batch without a full dashboard rebuild.");

        CreationDashboardAuthorityPhaseProgress contactsReady = allocationReady.WithTerminal(
            CreationDashboardAuthorityPhase.Contacts,
            failed: false);
        Require(
            !CreationDashboardProjectionScheduler.ShouldRenderAfterCompletion(
                CreationDashboardAuthorityPhase.Contacts,
                contactsReady),
            "Priority Creation rebuilt before its final batch was terminal.");

        CreationDashboardAuthorityPhaseProgress allReady = contactsReady.WithTerminal(
            CreationDashboardAuthorityPhase.Resources,
            failed: false);
        Require(
            CreationDashboardProjectionScheduler.ShouldRenderAfterCompletion(
                CreationDashboardAuthorityPhase.Resources,
                allReady),
            "Priority Creation did not rebuild after its final batch became terminal.");

        CreationDashboardAuthorityPhaseProgress lifeModules =
            CreationDashboardAuthorityPhaseProgress.ForBuildMethod("life-modules");
        Require(
            CreationDashboardProjectionScheduler.NextBatch(lifeModules).SequenceEqual(
                [CreationDashboardAuthorityPhase.Contacts]),
            "A build method without prerequisite allocation authority did not proceed directly to Contacts.");

        return Task.CompletedTask;
    }

    private static Task CreationSkillsCatalogPagingBoundsNativeControlMaterializationAsync()
    {
        const int pageSize = 20;
        Require(
            CreationSkillsCatalogPaging.NormalizeOffset(0, 87, pageSize) == 0,
            "The first Skills catalog page did not begin at zero.");
        Require(
            CreationSkillsCatalogPaging.NextOffset(0, 87, pageSize) == 20,
            "The Skills catalog did not advance by one bounded native-control page.");
        Require(
            CreationSkillsCatalogPaging.NextOffset(80, 87, pageSize) == 80,
            "The final partial Skills page advanced beyond the catalog.");
        Require(
            CreationSkillsCatalogPaging.PreviousOffset(40, pageSize) == 20,
            "The Skills catalog did not return by one page.");
        Require(
            CreationSkillsCatalogPaging.NormalizeOffset(400, 87, pageSize) == 80,
            "A stale Skills offset was not clamped to the current authority.");
        Require(
            CreationSkillsCatalogPaging.NormalizeOffset(20, 0, pageSize) == 0,
            "An empty Skills catalog retained a stale offset.");
        return Task.CompletedTask;
    }

    private static async Task PrerequisiteAuthorityPublishesBeforeSlowLaterPhasesAsync()
    {
        using var prerequisiteQueue = new LatestBackgroundProjectionQueue<string, string>();
        using var attributesQueue = new LatestBackgroundProjectionQueue<string, string>();
        using var skillsQueue = new LatestBackgroundProjectionQueue<string, string>();
        using var attributesEntered = new ManualResetEventSlim();
        using var skillsEntered = new ManualResetEventSlim();
        using var releaseLaterPhases = new ManualResetEventSlim();
        var prerequisiteReady = new TaskCompletionSource<BackgroundProjectionCompletion<string, string>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var attributesReady = new TaskCompletionSource<BackgroundProjectionCompletion<string, string>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var skillsReady = new TaskCompletionSource<BackgroundProjectionCompletion<string, string>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        prerequisiteQueue.Completed += value => prerequisiteReady.TrySetResult(value);
        attributesQueue.Completed += value => attributesReady.TrySetResult(value);
        skillsQueue.Completed += value => skillsReady.TrySetResult(value);

        attributesQueue.TryRequest(
            "bound-revision",
            (_, cancellationToken) =>
            {
                attributesEntered.Set();
                releaseLaterPhases.Wait(cancellationToken);
                return "attributes";
            },
            out _);
        skillsQueue.TryRequest(
            "bound-revision",
            (_, cancellationToken) =>
            {
                skillsEntered.Set();
                releaseLaterPhases.Wait(cancellationToken);
                return "skills";
            },
            out _);
        Require(attributesEntered.Wait(TimeSpan.FromSeconds(5)), "The Attributes phase never started.");
        Require(skillsEntered.Wait(TimeSpan.FromSeconds(5)), "The Skills phase never started.");

        prerequisiteQueue.TryRequest(
            "bound-revision",
            (_, _) => "prerequisite",
            out BackgroundProjectionRequest<string> prerequisiteRequest);
        BackgroundProjectionCompletion<string, string> prerequisite = await prerequisiteReady.Task
            .WaitAsync(TimeSpan.FromSeconds(5));
        Require(
            prerequisiteQueue.TryTake(prerequisiteRequest, out string accepted, out Exception? error),
            "The prerequisite terminal outcome was not independently consumable.");
        Require(error is null && accepted == "prerequisite", "The prerequisite outcome changed during acceptance.");
        Require(
            !attributesReady.Task.IsCompleted && !skillsReady.Task.IsCompleted,
            "A later phase had to finish before prerequisite authority was publishable.");

        releaseLaterPhases.Set();
        await Task.WhenAll(attributesReady.Task, skillsReady.Task).WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static Task CreationAuthorityPhaseMergesAreIndependentAndDeterministicAsync()
    {
        CreationDashboardAuthorityPhaseProgress initial =
            CreationDashboardAuthorityPhaseProgress.ForBuildMethod(CharacterCreationBuildMethods.Priority);
        Require(
            initial.Prerequisite == CreationDashboardAuthorityPhaseState.Loading
            && initial.Attributes == CreationDashboardAuthorityPhaseState.Loading
            && initial.Skills == CreationDashboardAuthorityPhaseState.Loading
            && initial.Contacts == CreationDashboardAuthorityPhaseState.Loading,
            "Priority must begin with every authority phase explicitly fail-closed and loading.");
        Require(
            initial.HasLoading && initial.LoadingCount == 5,
            "Priority must expose every pending authority phase to the progressive phone loading state.");

        CreationDashboardAuthorityPhaseProgress prerequisiteAccepted = initial.WithTerminal(
            CreationDashboardAuthorityPhase.Prerequisite,
            failed: false);
        Require(
            prerequisiteAccepted.Prerequisite == CreationDashboardAuthorityPhaseState.Ready
            && prerequisiteAccepted.Attributes == CreationDashboardAuthorityPhaseState.Loading
            && prerequisiteAccepted.Skills == CreationDashboardAuthorityPhaseState.Loading
            && prerequisiteAccepted.Contacts == CreationDashboardAuthorityPhaseState.Loading,
            "Accepting prerequisite authority must not wait for or invent later phase outcomes.");

        CreationDashboardAuthorityPhaseProgress laterFailure = prerequisiteAccepted
            .WithTerminal(CreationDashboardAuthorityPhase.Skills, failed: false)
            .WithTerminal(CreationDashboardAuthorityPhase.Attributes, failed: true)
            .WithTerminal(CreationDashboardAuthorityPhase.Contacts, failed: false);
        Require(
            laterFailure.Prerequisite == CreationDashboardAuthorityPhaseState.Ready,
            "A failed later phase erased already accepted prerequisite authority.");
        Require(
            laterFailure.Attributes == CreationDashboardAuthorityPhaseState.Failed
            && laterFailure.Skills == CreationDashboardAuthorityPhaseState.Ready
            && laterFailure.Contacts == CreationDashboardAuthorityPhaseState.Ready,
            "Out-of-order later phase merges were not deterministic and isolated.");
        Require(
            laterFailure.HasLoading && laterFailure.LoadingCount == 1,
            "A terminal failure must not hide the independently pending Resources phase.");

        CreationDashboardAuthorityPhaseProgress terminal = laterFailure.WithTerminal(
            CreationDashboardAuthorityPhase.Resources,
            failed: false);
        Require(
            !terminal.HasLoading && terminal.LoadingCount == 0,
            "A terminal dashboard retained a false progressive-loading state.");

        CreationDashboardAuthorityPhaseProgress sumToTen =
            CreationDashboardAuthorityPhaseProgress.ForBuildMethod(CharacterCreationBuildMethods.SumToTen);
        Require(
            sumToTen.Prerequisite == CreationDashboardAuthorityPhaseState.Loading
            && sumToTen.Attributes == CreationDashboardAuthorityPhaseState.Loading
            && sumToTen.Skills == CreationDashboardAuthorityPhaseState.NotApplicable
            && sumToTen.Contacts == CreationDashboardAuthorityPhaseState.Loading,
            "Contacts must remain an independent Core phase even when Priority-only Skills do not apply.");
        return Task.CompletedTask;
    }

    private static Task CreationDashboardReadyMarkerRequiresCurrentTerminalAuthorityAsync()
    {
        var workspaceId = new CharacterWorkspaceId("phone-dashboard-ready-marker");
        CharacterOverviewState overview = NewCreationOverview(workspaceId, 12, 11);
        var snapshot = new CharacterCreationWizardSnapshot(
            CharacterCreationWizardSchemas.SnapshotV1,
            workspaceId.Value,
            WorkspaceRevision: 12,
            ContentDigest: CanonicalDigest('1'),
            SourceDigest: CanonicalDigest('2'),
            RulesetDefaults.Sr5,
            RuntimeFingerprint: string.Empty,
            CharacterCreationBuildMethods.Priority,
            CharacterCreated: false,
            CharacterCreationWizardStepIds.Foundation,
            [],
            [],
            new Dictionary<string, IReadOnlyList<CharacterCreationLegalOption>>(),
            [],
            [],
            CanFinalize: false,
            SnapshotDigest: CanonicalDigest('4'));
        Require(
            CreationDashboardProjectionBinding.TryCreate(
                overview,
                snapshot,
                out CreationDashboardProjectionBinding? binding)
            && binding is not null,
            "The exact dashboard marker fixture did not produce a current binding.");

        CreationDashboardAuthorityPhaseProgress ready =
            CreationDashboardAuthorityPhaseProgress
                .ForBuildMethod(CharacterCreationBuildMethods.Priority)
                .WithTerminal(CreationDashboardAuthorityPhase.Prerequisite, failed: false)
                .WithTerminal(CreationDashboardAuthorityPhase.Attributes, failed: false)
                .WithTerminal(CreationDashboardAuthorityPhase.Skills, failed: false)
                .WithTerminal(CreationDashboardAuthorityPhase.Contacts, failed: false)
                .WithTerminal(CreationDashboardAuthorityPhase.Resources, failed: false);
        var projection = new CreationDashboardAuthorityProjection(
            binding!,
            ready,
            Prerequisite: null,
            Attributes: null,
            Skills: null,
            Contacts: null,
            Resources: null);
        CreationDashboardRouteReadyMarker? marker =
            BuildPageUiProjection.CreationDashboardRouteReady(
                overview,
                snapshot,
                projection);
        Require(
            marker is
            {
                Schema: "chummer.android.creation-dashboard-route-ready/v1",
                RouteAutomationId: "phone-runner-create",
                DashboardAutomationId: "creation-wizard-dashboard",
                CharacterCreated: false,
                AuthorityReady: true
            }
            && marker.WorkspaceId == workspaceId.Value
            && marker.ContentRevision == 12
            && marker.SavedRevision == 11
            && marker.RuntimeFingerprint == string.Empty
            && marker.BuildMethod == CharacterCreationBuildMethods.Priority
            && marker.SnapshotDigest == snapshot.SnapshotDigest,
            "The ready marker did not preserve the exact current workspace, typed build method, absent runtime authority, and route authority.");

        Require(
            BuildPageUiProjection.CreationDashboardRouteReady(
                overview,
                snapshot,
                projection with
                {
                    Progress = ready with
                    {
                        Contacts = CreationDashboardAuthorityPhaseState.Loading
                    }
                }) is null,
            "A still-loading dashboard emitted a route-ready marker.");
        Require(
            BuildPageUiProjection.CreationDashboardRouteReady(
                overview,
                snapshot,
                projection with
                {
                    Progress = ready with
                    {
                        Resources = CreationDashboardAuthorityPhaseState.Failed
                    },
                    ResourcesFailureReason = "creation-resources-authority-load-failed"
                }) is null,
            "A failed dashboard authority phase emitted a route-ready marker.");
        Require(
            BuildPageUiProjection.CreationDashboardRouteReady(
                overview,
                snapshot with { SnapshotDigest = CanonicalDigest('5') },
                projection) is null,
            "A stale snapshot emitted the current dashboard route-ready marker.");
        Require(
            BuildPageUiProjection.CreationDashboardRouteReady(
                overview with { Profile = overview.Profile! with { Created = true } },
                snapshot,
                projection) is null,
            "A Career runner emitted a Creation dashboard route-ready marker.");
        return Task.CompletedTask;
    }

    private static async Task CompletedCreationProjectionSurvivesADeferredUiConsumerAsync()
    {
        using var queue = new LatestBackgroundProjectionQueue<string, string>();
        var notification = new TaskCompletionSource<BackgroundProjectionCompletion<string, string>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        queue.Completed += value => notification.TrySetResult(value);
        queue.TryRequest(
            "workspace-1/revision-1/source-a",
            (_, _) => "authoritative",
            out BackgroundProjectionRequest<string> request);

        BackgroundProjectionCompletion<string, string> published = await notification.Task
            .WaitAsync(TimeSpan.FromSeconds(5));
        Require(
            published.Request == request,
            "The deferred UI notification lost its bound generation.");

        // Model a page dispatcher that was not yet attached when the best-effort
        // notification fired.  The terminal outcome must remain in the queue
        // until a later UI refresh atomically consumes it.
        Require(
            queue.TryTake(request, out string recovered, out Exception? error),
            "A completed projection was lost before the UI consumer became ready.");
        Require(error is null, "A successful deferred projection recovered as a failure.");
        Require(recovered == "authoritative", "The deferred projection result changed.");
        Require(
            !queue.TryTake(request, out _, out _),
            "A terminal projection was admitted more than once.");
    }

    private static async Task RejectedCreationProjectionForcesCurrentBindingRefreshAsync()
    {
        using var queue = new LatestBackgroundProjectionQueue<CreationDashboardProjectionBinding, string>();
        var notification = new TaskCompletionSource<
            BackgroundProjectionCompletion<CreationDashboardProjectionBinding, string>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        queue.Completed += value => notification.TrySetResult(value);
        var binding = new CreationDashboardProjectionBinding(
            "workspace-1",
            ContentRevision: 1,
            SavedRevision: 0,
            ContentDigest: "content-a",
            SourceDigest: "source-a",
            RuntimeFingerprint: "runtime-a",
            BuildMethod: CharacterCreationBuildMethods.Priority,
            SnapshotDigest: "snapshot-a");
        queue.TryRequest(binding, (_, _) => "stale-authority", out _);

        BackgroundProjectionCompletion<CreationDashboardProjectionBinding, string> completed =
            await notification.Task.WaitAsync(TimeSpan.FromSeconds(5));
        bool refreshCurrentBinding = BuildPageUiProjection.ConsumeRejectedCreationPhaseForRefresh(
            queue,
            completed.Request);

        Require(
            refreshCurrentBinding,
            "A terminal projection rejected by a changed page binding did not request a current-binding refresh.");
        Require(
            !queue.TryAccept(completed.Request),
            "The rejected terminal projection remained current and could strand the fail-closed loading UI.");
    }

    private static async Task LateCreationDashboardProjectionCannotOverwriteNewerBindingAsync()
    {
        using var queue = new LatestBackgroundProjectionQueue<string, string>();
        using var oldEntered = new ManualResetEventSlim();
        using var releaseOld = new ManualResetEventSlim();
        var completions = new List<BackgroundProjectionCompletion<string, string>>();
        var latest = new TaskCompletionSource<BackgroundProjectionCompletion<string, string>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        queue.Completed += completion =>
        {
            lock (completions)
            {
                completions.Add(completion);
            }
            if (completion.Request.Key == "workspace-1/revision-2/source-b")
                latest.TrySetResult(completion);
        };

        queue.TryRequest(
            "workspace-1/revision-1/source-a",
            (_, _) =>
            {
                oldEntered.Set();
                // Deliberately ignore cancellation to model an uncooperative
                // filesystem projection already inside a synchronous read.
                releaseOld.Wait();
                return "stale";
            },
            out BackgroundProjectionRequest<string> oldRequest);
        Require(oldEntered.Wait(TimeSpan.FromSeconds(5)), "The old projection never entered its worker.");
        queue.TryRequest(
            "workspace-1/revision-2/source-b",
            (_, _) => "current",
            out BackgroundProjectionRequest<string> currentRequest);
        releaseOld.Set();

        BackgroundProjectionCompletion<string, string> completed = await latest.Task
            .WaitAsync(TimeSpan.FromSeconds(5));
        Require(completed.Request == currentRequest, "The newer generation did not win.");
        Require(completed.Result == "current", "The stale result replaced the current projection.");
        Require(!queue.TryAccept(oldRequest), "The superseded generation remained admissible.");
        Require(queue.TryAccept(currentRequest), "The current generation was rejected.");
        lock (completions)
        {
            Require(
                completions.All(item => item.Request != oldRequest),
                "A cancelled late projection was published to the UI boundary.");
        }
    }

    private static async Task CancelledOrFaultedCreationDashboardProjectionIsObservedAsync()
    {
        using var queue = new LatestBackgroundProjectionQueue<string, string>();
        var failure = new TaskCompletionSource<BackgroundProjectionFailure<string>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        queue.Failed += value => failure.TrySetResult(value);
        queue.TryRequest(
            "workspace-1/revision-3/source-c",
            (_, _) => throw new InvalidOperationException("deterministic projection failure"),
            out BackgroundProjectionRequest<string> failedRequest);

        BackgroundProjectionFailure<string> observed = await failure.Task
            .WaitAsync(TimeSpan.FromSeconds(5));
        Require(observed.Request == failedRequest, "The failure lost its bound generation.");
        Require(
            observed.Error is InvalidOperationException,
            "The background failure was not observed at the fail-closed boundary.");
        Require(
            queue.TryTake(failedRequest, out _, out Exception? observedError),
            "An observed failure could not enter retryable failed state.");
        Require(
            observedError is InvalidOperationException,
            "The stored failure outcome was not recoverable by a deferred UI consumer.");

        var retry = new TaskCompletionSource<BackgroundProjectionCompletion<string, string>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        queue.Completed += value =>
        {
            if (value.Request.Key == "workspace-1/revision-3/source-c")
                retry.TrySetResult(value);
        };
        queue.TryRequest(
            "workspace-1/revision-3/source-c",
            (_, _) => "retry-success",
            out BackgroundProjectionRequest<string> retryRequest);
        BackgroundProjectionCompletion<string, string> retried = await retry.Task
            .WaitAsync(TimeSpan.FromSeconds(5));
        queue.Cancel();
        Require(
            !queue.TryAccept(retried.Request),
            "OnDisappearing cancellation admitted a callback that was already queued to the dispatcher.");

        using var disposedQueue = new LatestBackgroundProjectionQueue<string, string>();
        var disposedCompletion = new TaskCompletionSource<BackgroundProjectionCompletion<string, string>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        disposedQueue.Completed += value => disposedCompletion.TrySetResult(value);
        disposedQueue.TryRequest("disposed", (_, _) => "done", out _);
        BackgroundProjectionCompletion<string, string> beforeDispose = await disposedCompletion.Task
            .WaitAsync(TimeSpan.FromSeconds(5));
        disposedQueue.Dispose();
        Require(
            !disposedQueue.TryAccept(beforeDispose.Request),
            "Dispose admitted a dispatcher callback after the page lifetime ended.");
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

    private static Task TalentGrantSelectionsRemainExactAndExoticChoicesFailClosedAsync()
    {
        const string arcanaId = "74a68a9e-8c5b-4998-8dbb-08c1e768afc3";
        const string exoticId = "a1366ec2-772d-4f08-8c65-5f79464d975b";
        string skillsDigest = CanonicalDigest('6');
        CharacterCreationTalentActiveSkillChoiceProjection arcana = new(
            arcanaId,
            arcanaId,
            "Arcana",
            "Pseudo-Magical Active",
            null,
            CanonicalDigest('7'),
            skillsDigest,
            [$"skills.xml#skill:{arcanaId}"]);
        CharacterCreationTalentActiveSkillChoiceProjection exotic = new(
            exoticId,
            exoticId,
            "Exotic Melee Weapon",
            "Combat Active",
            null,
            CanonicalDigest('8'),
            skillsDigest,
            [$"skills.xml#skill:{exoticId}"])
        {
            IsExotic = true,
            IsEnabled = false,
            Blockers =
            [
                CharacterCreationPrerequisiteBlockers
                    .TalentExoticSkillSpecializationRequired
            ]
        };
        CharacterCreationTalentActiveSkillChoiceProjection[] options = [arcana, exotic];
        CharacterCreationTalentActiveSkillGrantProjection grant = new(
            Quantity: 1,
            BaseRating: 4,
            SkillType: CharacterCreationTalentSkillGrantTypes.Active,
            Options: options,
            GrantDigest: CharacterCreationTalentGrantAuthorityDigest.ComputeActiveGrant(
                1,
                4,
                CharacterCreationTalentSkillGrantTypes.Active,
                CharacterCreationTalentGrantImprovementKinds.SkillBase,
                CharacterCreationTalentSkillGrantTypes.Active,
                CharacterCreationTalentGrantSelectorTypeSources.SkillType,
                string.Empty,
                skillsDigest,
                options.Select(option => option.SelectionId)),
            IsSupported: true,
            Blockers: [],
            SourceAnchorIds: ["priorities.xml#talent:adept", "skills.xml"])
        {
            ImprovementKind = CharacterCreationTalentGrantImprovementKinds.SkillBase,
            RawSelectorType = CharacterCreationTalentSkillGrantTypes.Active,
            SelectorTypeSource = CharacterCreationTalentGrantSelectorTypeSources.SkillType
        };
        CharacterCreationPriorityTalentOptionProjection talent = new(
            "adept",
            "Adept",
            "Adept",
            0,
            6,
            null,
            null,
            [],
            CanonicalDigest('9'),
            IsEnabled: true,
            Blockers: [],
            SourceAnchorIds: ["priorities.xml#talent:adept"])
        {
            ActiveSkillGrant = grant
        };

        Require(
            CreationPrerequisitePhoneAuthority.IsTalentGrantAuthoritySupported(talent),
            "A digest-bound supported active-skill prompt must be accepted.");
        Require(
            !CreationPrerequisitePhoneAuthority.TalentGrantSelectionsComplete(talent, [], []),
            "A required grant cannot complete without its exact quantity.");
        Require(
            !CreationPrerequisitePhoneAuthority.TalentGrantSelectionsComplete(
                talent,
                [exoticId],
                []),
            "An exotic skill must remain blocked until Core publishes typed specialization authority.");
        Require(
            CreationPrerequisitePhoneAuthority.TalentGrantSelectionsComplete(
                talent,
                [arcanaId],
                []),
            "The exact enabled Core option must complete its one-slot prompt.");

        var entry = new CharacterCreationTalentActiveSkillGrantPlanEntry(
            arcana.SelectionId,
            "active-skill",
            arcana.SourceId,
            arcana.CanonicalName,
            arcana.Category,
            arcana.SkillGroup,
            grant.BaseRating,
            grant.ImprovementKind,
            arcana.SourceNodeDigest,
            arcana.SkillsSourceDigest,
            arcana.SourceAnchorIds);
        string[] anchors = entry.SourceAnchorIds.Concat(grant.SourceAnchorIds)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(anchor => anchor, StringComparer.Ordinal)
            .ToArray();
        var plan = new CharacterCreationTalentGrantPlanContribution(
            CharacterCreationPrerequisiteSchemas.TalentGrantPlanV1,
            [entry],
            [],
            anchors,
            CanonicalDigest('a'));
        Require(
            CreationPrerequisitePhoneAuthority.TalentGrantPlanMatchesSelections(
                plan,
                talent,
                [arcanaId],
                []),
            "Preview verification must match the exact ordered Core plan.");
        Require(
            !CreationPrerequisitePhoneAuthority.TalentGrantPlanMatchesSelections(
                plan with { ActiveSkills = [entry with { BaseRating = 5 }] },
                talent,
                [arcanaId],
                []),
            "A preview with a forged grant rating must fail closed.");
        Require(
            !CreationPrerequisitePhoneAuthority.IsTalentGrantAuthoritySupported(
                talent with { ActiveSkillGrant = grant with { BaseRating = 0 } }),
            "A zero-rating prompt must not unlock a Talent choice.");
        return Task.CompletedTask;
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

    private static Task SkillsPreviewAdoptionPreservesOnlyCoreProjectionAsync()
    {
        SkillsFixture fixture = NewSkillsFixture();
        Require(
            CharacterCreationSkillsDraftIntegrity.IsValidAuthority(fixture.State.Authority),
            "The Skills fixture must carry an exactly valid Core authority packet.");
        Require(
            CharacterCreationSkillsDraftIntegrity.IsValidStateProjection(fixture.State),
            "The Skills fixture must carry an exactly valid Core state projection.");
        Require(
            CharacterCreationSkillsDigest.EqualsFixedTime(
                fixture.State.SnapshotDigest,
                CharacterCreationSkillsDigest.Compute(
                    fixture.State with { SnapshotDigest = string.Empty })),
            "The Skills fixture snapshot digest must bind its exact state projection.");
        Require(
            CreationSkillsPhoneAuthority.IsReady(fixture.State, fixture.Overview),
            "The phone gate must accept an exactly digest-valid Core Skills packet.");
        Require(
            !CreationSkillsPhoneAuthority.IsReady(
                fixture.State with { SnapshotDigest = CanonicalDigest('9') },
                fixture.Overview),
            "The phone gate must recompute and reject a forged Skills snapshot digest.");
        CharacterCreationSkillCatalogEntry tamperedCatalog = fixture.ActiveSource with
        {
            Name = "Forged Pistols"
        };
        CharacterCreationSkillsAuthority tamperedAuthority = fixture.State.Authority with
        {
            ActiveSkills = [tamperedCatalog],
            AuthorityDigest = string.Empty
        };
        tamperedAuthority = tamperedAuthority with
        {
            AuthorityDigest = CharacterCreationSkillsDigest.Compute(
                tamperedAuthority with { AuthorityDigest = string.Empty })
        };
        CharacterCreationSkillsState tamperedState = fixture.State with
        {
            Authority = tamperedAuthority,
            Binding = fixture.State.Binding with
            {
                SkillsAuthorityDigest = tamperedAuthority.AuthorityDigest
            },
            SnapshotDigest = string.Empty
        };
        tamperedState = tamperedState with
        {
            SnapshotDigest = CharacterCreationSkillsDigest.Compute(
                tamperedState with { SnapshotDigest = string.Empty })
        };
        Require(
            !CreationSkillsPhoneAuthority.IsReady(tamperedState, fixture.Overview),
            "A re-sealed packet with a forged catalog projection must fail Core integrity validation.");
        var phoneDraft = new CreationSkillsPhoneDraft();
        phoneDraft.Bind(fixture.State, fixture.Overview);
        var success = new CharacterCreationFoundationResult<CharacterCreationSkillsPreview>(
            CharacterCreationFoundationOutcomes.Success,
            fixture.Preview,
            []);
        Require(
            phoneDraft.TryAdopt(
                fixture.State,
                fixture.Overview,
                success,
                fixture.Allocations,
                []),
            "A canonical Core Skills projection must be adopted.");

        CharacterCreationSkillAllocation increased = phoneDraft
            .WithSkill(fixture.ActiveSource, 1)
            .Single(item => item.SourceSkillId == fixture.ActiveSource.SourceSkillId);
        Require(increased.Rating == 3, "The next request must start from Core's projected rating.");
        Require(
            increased.SpecializationOptionId == fixture.Specialization.OptionId,
            "Rating changes must preserve the Core-projected specialization.");
        Require(
            phoneDraft.WithSpecialization(fixture.LanguageSource, "invented").SequenceEqual(phoneDraft.Skills),
            "A specialization must never manufacture a missing or native allocation.");

        CharacterCreationFoundationResult<CharacterCreationSkillsPreview> stale = success with
        {
            Value = fixture.Preview with
            {
                Binding = fixture.Preview.Binding with { ContentRevision = 99 }
            }
        };
        Require(
            !phoneDraft.TryAdopt(
                fixture.State,
                fixture.Overview,
                stale,
                fixture.Allocations,
                []),
            "A stale Core Skills projection must fail closed.");
        return Task.CompletedTask;
    }

    private static Task SkillsCommittedRefreshFailureRemainsCommittedAsync()
    {
        SkillsFixture fixture = NewSkillsFixture();
        CharacterCreationSkillsReceipt receipt = new(
            CharacterCreationSkillsSchemas.ReceiptV1,
            fixture.State.Binding.WorkspaceId,
            PreviousContentRevision: 6,
            ContentRevision: 7,
            SavedRevision: 7,
            DraftRevision: 1,
            CanonicalDigest('1'),
            fixture.Preview.PreviewDigest,
            CanonicalDigest('2'),
            CanonicalDigest('3'),
            CharacterCreationSkillsDigest.ReceiptLedgerRootDigest,
            fixture.State.Authority.AuthorityDigest,
            fixture.State.Authority.RuntimeDigest,
            ActivePointsRemaining: 25,
            SkillGroupPointsRemaining: 2,
            KnowledgePointsRemaining: 10,
            KnowledgePointOverflowToActive: 0,
            CharacterDocumentChanged: false,
            CanonicalDigest('4'));
        CreationSkillsPhoneConfirmResult result = CreationSkillsPhoneAuthority
            .CommittedRefreshRequired(receipt, fixture.State, ["presenter-refresh-failed"]);
        Require(
            result.Outcome == CharacterCreationFoundationOutcomes.Success
            && result.Receipt == receipt
            && result.RefreshedState == fixture.State,
            "A validated durable commit must not be relabeled as an uncommitted conflict.");
        Require(
            result.Blockers.Contains(
                CharacterCreationSkillsBlockers.PostCommitRefreshRequired,
                StringComparer.Ordinal),
            "A durable commit with a failed phone refresh must carry explicit recovery semantics.");
        return Task.CompletedTask;
    }

    private static SkillsFixture NewSkillsFixture()
    {
        AttributesFixture attributesFixture = NewAttributesFixture();
        CharacterWorkspaceId workspaceId = attributesFixture.State.Binding.WorkspaceId;
        CharacterCreationPrerequisiteDraft prerequisite = attributesFixture.State.PrerequisiteDraft!;
        CharacterCreationAttributesDraft attributes = new(
            CharacterCreationAttributesSchemas.DraftV1,
            workspaceId,
            DraftRevision: 5,
            BaseContentRevision: 5,
            attributesFixture.State.Binding.RawCharacterXmlDigest,
            prerequisite.DraftRevision,
            prerequisite.DraftDigest,
            prerequisite.AuthorityDigest,
            "11111111-1111-1111-1111-111111111111",
            CanonicalDigest('4'),
            HalvesNormalAttributePoints: false,
            NormalPointTotal: 10,
            NormalPointUsed: 1,
            SpecialPointTotal: 0,
            SpecialPointUsed: 0,
            CreationKarmaTotal: 25,
            CreationKarmaUsed: 0,
            attributesFixture.Allocations,
            attributesFixture.Preview.Attributes,
            ["metatypes.xml#human"],
            CharacterEffectsApplied: false,
            CanonicalDigest('5'));
        string effectiveInputs = CanonicalDigest('6');
        var specialization = new CharacterCreationSkillSpecializationOption(
            "spec-pistols",
            "Semi-Automatics",
            "skills.xml#pistols/spec:semi-automatics");
        string activeId = "11111111-1111-1111-1111-111111111111";
        string[] activeAnchors = [$"skills.xml#skill:{activeId}"];
        var active = new CharacterCreationSkillCatalogEntry(
            activeId,
            CharacterCreationSkillKinds.Active,
            "Pistols",
            "Combat Active",
            "AGI",
            SkillGroup: null,
            IsExotic: false,
            CharacterCreationStandardPrioritySkillsRules.ComputeCatalogProjectionDigest(
                effectiveInputs,
                activeId,
                CharacterCreationSkillKinds.Active,
                "Pistols",
                "Combat Active",
                "AGI",
                null,
                false,
                [specialization],
                activeAnchors),
            [specialization],
            activeAnchors);
        string languageId = "22222222-2222-2222-2222-222222222222";
        string[] languageAnchors = [$"skills.xml#skill:{languageId}"];
        var language = new CharacterCreationSkillCatalogEntry(
            languageId,
            CharacterCreationSkillKinds.Knowledge,
            "English",
            "Language",
            "INT",
            SkillGroup: null,
            IsExotic: false,
            CharacterCreationStandardPrioritySkillsRules.ComputeCatalogProjectionDigest(
                effectiveInputs,
                languageId,
                CharacterCreationSkillKinds.Knowledge,
                "English",
                "Language",
                "INT",
                null,
                false,
                [],
                languageAnchors,
                canBeNativeLanguage: true),
            [],
            languageAnchors)
        {
            CanBeNativeLanguage = true
        };
        string runtimeDigest = CharacterCreationStandardPrioritySkillsRules.ComputeRuntimeDigest(
            usePointsOnBrokenGroups: false,
            strictSkillGroupsInCreateMode: false,
            specializationsBreakSkillGroups: true);
        var authority = new CharacterCreationSkillsAuthority(
            CharacterCreationSkillsSchemas.AuthorityV1,
            "standard",
            effectiveInputs,
            CanonicalDigest('7'),
            CharacterCreationStandardPrioritySkillsRules.KnowledgePointsExpression,
            6,
            6,
            6,
            1,
            false,
            false,
            true,
            [active],
            [language],
            [],
            [],
            ["priorities.xml#category:Skills", "settings.xml#setting:standard", "skills.xml"],
            [],
            true,
            runtimeDigest,
            string.Empty);
        authority = authority with
        {
            AuthorityDigest = CharacterCreationSkillsDigest.Compute(
                authority with { AuthorityDigest = string.Empty })
        };
        CharacterCreationSkillsBinding binding = new(
            workspaceId,
            ContentRevision: 6,
            SavedRevision: 6,
            attributesFixture.State.Binding.RawCharacterXmlDigest,
            new string('b', 64),
            prerequisite.DraftRevision,
            prerequisite.DraftDigest,
            prerequisite.AuthorityDigest,
            attributes.DraftRevision,
            attributes.DraftDigest,
            authority.AuthorityDigest,
            authority.RuntimeDigest,
            CharacterCreationSkillsDigest.Compute(Array.Empty<CharacterCreationKnowledgePointContribution>()));
        CharacterCreationSkillAllocation[] allocations =
        [
            new(activeId, CharacterCreationSkillKinds.Active, 2, specialization.OptionId, false),
            new(languageId, CharacterCreationSkillKinds.Knowledge, null, null, true)
        ];
        CharacterCreationSkillProjection[] projections =
        [
            new(activeId, CharacterCreationSkillKinds.Active, "Pistols", "Combat Active", "AGI", null,
                2, 2, 3, specialization.OptionId, specialization.Name, false, true, [], activeAnchors),
            new(languageId, CharacterCreationSkillKinds.Knowledge, "English", "Language", "INT", null,
                null, null, 0, null, null, true, true, [], languageAnchors)
        ];
        CharacterCreationBudgetState stateActiveBudget = NewBudget("active-skills", 28, 0);
        CharacterCreationBudgetState stateGroupBudget = NewBudget("skill-groups", 2, 0);
        CharacterCreationBudgetState stateKnowledgeBudget = NewBudget("knowledge-skills", 10, 0);
        CharacterCreationBudgetState previewActiveBudget = NewBudget("active-skills", 28, 3);
        CharacterCreationBudgetState previewGroupBudget = NewBudget("skill-groups", 2, 0);
        CharacterCreationBudgetState previewKnowledgeBudget = NewBudget("knowledge-skills", 10, 0);
        var state = new CharacterCreationSkillsState(
            CharacterCreationSkillsSchemas.SnapshotV1,
            binding,
            authority,
            prerequisite,
            attributes,
            PendingDraft: null,
            [],
            [],
            [],
            stateActiveBudget,
            stateGroupBudget,
            stateKnowledgeBudget,
            IntuitionUnaugmented: 3,
            LogicUnaugmented: 2,
            [],
            CanEdit: true,
            SnapshotDigest: string.Empty)
        {
            SelectedActiveSkillPoints = 28,
            SelectedSkillGroupPoints = 2
        };
        state = state with
        {
            SnapshotDigest = CharacterCreationSkillsDigest.Compute(
                state with { SnapshotDigest = string.Empty })
        };
        var preview = new CharacterCreationSkillsPreview(
            CharacterCreationSkillsSchemas.PreviewV1,
            binding,
            projections,
            [],
            [],
            previewActiveBudget,
            previewGroupBudget,
            previewKnowledgeBudget,
            KnowledgePointOverflowToActive: 0,
            [],
            RequiresExplicitConfirmation: true,
            CanConfirm: true,
            CanonicalDigest('a'));
        return new SkillsFixture(
            state,
            NewCreationOverview(workspaceId, 6, 6),
            preview,
            allocations,
            active,
            language,
            specialization);
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

    private static ResourcesFixture NewResourcesFixture()
    {
        var workspaceId = new CharacterWorkspaceId("resources-authority-runner");
        const long revision = 8;
        string contentDigest = CanonicalDigest('1');
        string auxiliaryDigest = new string('2', 64);
        string prerequisiteDigest = CanonicalDigest('3');
        string sourceDigest = CanonicalDigest('4');
        string profileDigest = CanonicalDigest('5');
        string rulesDigest = CanonicalDigest('6');
        string runtimeDigest = CanonicalDigest('7');

        var priority = new CharacterCreationResourcePriorityOption(
            "11111111-1111-1111-1111-111111111111",
            "D",
            50_000m,
            CanonicalDigest('8'),
            [CharacterCreationResourcesSourceAnchors.PriorityCatalog],
            string.Empty);
        priority = priority with
        {
            OptionDigest = CharacterCreationResourcesRules.ComputePriorityOptionDigest(priority)
        };
        var authority = new CharacterCreationResourcesAuthority(
            CharacterCreationResourcesSchemas.AuthorityV1,
            RulesetDefaults.Sr5,
            "standard",
            CharacterCreationBuildMethods.Priority,
            2_000m,
            10,
            5_000m,
            12,
            UnrestrictedNuyen: false,
            [priority],
            CharacterCreationResourcesSourceAnchors.All,
            [],
            IsAuthoritative: true,
            sourceDigest,
            profileDigest,
            rulesDigest,
            runtimeDigest,
            string.Empty);
        authority = authority with
        {
            AuthorityDigest = CharacterCreationResourcesRules.ComputeAuthorityDigest(authority)
        };
        var option = new CharacterCreationResourceAllocationOption(
            "karma:0",
            KarmaInvestment: 0,
            NuyenFromKarma: 0m,
            TotalStartingNuyen: 50_000m,
            IsEnabled: true,
            [],
            [CharacterCreationResourcesSourceAnchors.Step],
            string.Empty);
        option = option with
        {
            OptionDigest = CharacterCreationResourcesRules.ComputeAllocationOptionDigest(option)
        };
        var budget = new CharacterCreationResourcesBudget(
            PriorityNuyen: 50_000m,
            KarmaInvestment: 0,
            NuyenFromKarma: 0m,
            TotalStartingNuyen: 50_000m,
            KnownPurchaseCost: 0m,
            RemainingNuyen: 50_000m,
            Overspend: 0m,
            CarryoverLimit: 5_000m,
            CarryoverExcess: 45_000m,
            IsExact: true,
            [],
            [CharacterCreationResourcesSourceAnchors.Step]);
        var binding = new CharacterCreationResourcesBinding(
            workspaceId,
            WorkspaceRevision: revision,
            ContentRevision: revision,
            SavedRevision: revision,
            contentDigest,
            auxiliaryDigest,
            PrerequisiteDraftRevision: 1,
            prerequisiteDigest,
            authority.AuthorityDigest,
            sourceDigest,
            rulesDigest,
            runtimeDigest);
        var state = new CharacterCreationResourcesInteractionState(
            binding,
            authority,
            PrerequisiteDraft: null,
            PendingDraft: null,
            [option],
            budget,
            [],
            CanEdit: true,
            CanonicalDigest('9'));
        CharacterOverviewState overview = NewCreationOverview(workspaceId, revision, revision) with
        {
            CreationWizard = new CharacterCreationWizardSnapshot(
                CharacterCreationWizardSchemas.SnapshotV1,
                workspaceId.Value,
                WorkspaceRevision: revision,
                contentDigest,
                SourceDigest: CanonicalDigest('f'),
                RulesetDefaults.Sr5,
                RuntimeFingerprint: string.Empty,
                CharacterCreationBuildMethods.Priority,
                CharacterCreated: false,
                CharacterCreationWizardStepIds.Resources,
                [ConservativeStage(CharacterCreationWizardStepIds.Resources)],
                [],
                new Dictionary<string, IReadOnlyList<CharacterCreationLegalOption>>(),
                [],
                [],
                CanFinalize: false,
                CanonicalDigest('a'))
        };
        return new ResourcesFixture(
            state,
            overview,
            new CharacterCreationResourcesInteractionLoadResult(
                CharacterCreationResourcesOutcomes.Available,
                state,
                []));
    }

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

    private sealed record SkillsFixture(
        CharacterCreationSkillsState State,
        CharacterOverviewState Overview,
        CharacterCreationSkillsPreview Preview,
        IReadOnlyList<CharacterCreationSkillAllocation> Allocations,
        CharacterCreationSkillCatalogEntry ActiveSource,
        CharacterCreationSkillCatalogEntry LanguageSource,
        CharacterCreationSkillSpecializationOption Specialization);

    private sealed record AttributesFixture(
        CharacterCreationAttributesState State,
        CharacterOverviewState Overview,
        CharacterCreationAttributesPreview Preview,
        IReadOnlyList<CharacterCreationAttributeAllocation> Allocations);

    private sealed record ResourcesFixture(
        CharacterCreationResourcesInteractionState State,
        CharacterOverviewState Overview,
        CharacterCreationResourcesInteractionLoadResult Load);

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

    private sealed class FakePlayReviewClock : IPlayReviewClock
    {
        public DateTimeOffset UtcNow { get; set; }

        public long MonotonicMilliseconds { get; set; }
    }

    private sealed class FakePlayReviewStateStore : IPlayReviewStateStore
    {
        public FakePlayReviewStateStore(PlayReviewState state)
        {
            State = state;
        }

        public PlayReviewState State { get; private set; }

        public PlayReviewState Load() => State;

        public void Save(PlayReviewState state) => State = state;
    }

    private sealed class FakePlayReviewLauncher : IPlayReviewLauncher
    {
        public PlayReviewInstallContext InstallContext { get; init; } = new(
            PlayReviewPolicy.CanonicalApplicationId,
            PlayReviewPolicy.GooglePlayInstallerPackage,
            "test-install",
            IsReleaseBuild: true);

        public bool IsRuntimeAvailable { get; init; }

        public bool ThrowOnRequest { get; init; }

        public int RequestCount { get; private set; }

        public Task RequestReviewAsync(CancellationToken cancellationToken = default)
        {
            RequestCount++;
            return ThrowOnRequest
                ? Task.FromException(new InvalidOperationException("simulated Play failure"))
                : Task.CompletedTask;
        }

        public Task OpenStoreListingAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
