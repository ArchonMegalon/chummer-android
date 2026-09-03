from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
MODEL = ROOT / "src/Chummer.Android/Native/Sr5CareerWizardModel.cs"
HUB = ROOT / "src/Chummer.Android/Native/Sr5CareerWizardPage.cs"
PHONE_MODEL = ROOT / "src/Chummer.Android/Native/Sr5CareerWizardPhoneModel.cs"
PHONE_AUTHORITY = ROOT / "src/Chummer.Android/Native/RunnerSessionSr5CareerWizardPhoneAuthority.cs"
COORDINATOR = ROOT / "src/Chummer.Android/Native/RunnerSessionCoordinator.cs"
ACTIVE_SKILL = ROOT / "src/Chummer.Android/Native/Sr5CareerActiveSkillWizardPage.cs"
ACTIVE_SKILL_COORDINATOR = ROOT / "src/Chummer.Android/Native/Sr5CareerActiveSkillCoordinator.cs"
CHECKPOINT_STORE = ROOT / "src/Chummer.Android/Native/Sr5CareerDraftCheckpointStore.cs"
MUTATION_OWNER_STORE = ROOT / "src/Chummer.Android/Native/Sr5CareerMutationOwnerStore.cs"
BUILD = ROOT / "src/Chummer.Android/Native/BuildPage.cs"
PHYSICAL_ACTIVE_SKILL_DRIVER = (
    ROOT / "tests/run_api36_sr5_career_active_skill_wizard_e2e.py"
)


def test_sr5_career_hub_is_edition_gated_and_never_falls_through_to_generic_all_actions() -> None:
    model = MODEL.read_text(encoding="utf-8")
    hub = HUB.read_text(encoding="utf-8")

    assert 'public const string Edition = "SR5";' in model
    assert "IsSr5CareerRunner" in model
    assert "Profile?.Created == true" in hub
    assert "Rules?.GameEdition" in hub
    assert "CollectionItemEditorPage" not in hub
    assert "ApplyCollection" not in hub
    assert "Sr5CareerWizardDesktopSession" in hub
    assert "navigation only" in hub
    assert ".Where(static action => action.CanOpen)" in hub
    assert "OpenTypedDestinationAsync" in hub


def test_phone_action_family_chooser_is_exactly_bound_and_missing_authority_stays_hidden() -> None:
    hub = HUB.read_text(encoding="utf-8")
    phone_model = PHONE_MODEL.read_text(encoding="utf-8")
    phone_authority = PHONE_AUTHORITY.read_text(encoding="utf-8")
    coordinator = COORDINATOR.read_text(encoding="utf-8")

    for binding in (
        "WorkspaceId",
        "WorkspaceRevision",
        "SavedRevision",
        "RulesetId",
        "RuntimeFingerprint",
        "SourceDigest",
        "ContentDigest",
    ):
        assert binding in phone_model
    for action in (
        "AdjustKarma",
        "AdjustNuyen",
        "EditKarmaExpense",
        "EditNuyenExpense",
        "AdvanceAttribute",
        "AdvanceActiveSkill",
        "AdvanceKnowledgeSkill",
        "AdvanceSkillGroup",
        "LearnSpecialization",
        "ChangeQuality",
        "ManageCalendarEntry",
    ):
        assert f"Sr5CareerWizardActionIds.{action}" in hub

    assert "Sr5CareerWizardProjector.Project(binding, authorities)" in phone_model
    assert "Sr5CareerWizardBlockers.AuthorityUnavailable" not in hub
    assert "family.HasAvailableAction" in hub
    assert "action.CanOpen" in hub
    assert phone_authority.count("CaptureSr5CareerWizardWorkspaceAuthorityAsync") == 2
    assert "verified != first" in phone_authority
    assert "RequireCurrent(first)" in phone_authority
    assert "allowReadOnlyProductCapture: true" in coordinator
    assert "expectedPayloadSha256: null" in coordinator
    assert "_checkpointStore.TryWrite(_session)" in hub
    assert "_checkpointStore.Clear()" in hub
    assert "Sr5CareerWizardCheckpointInvalidationReasons.SnapshotChanged" in hub


def test_active_skill_vertical_slice_has_review_apply_and_durable_receipt_boundaries() -> None:
    model = MODEL.read_text(encoding="utf-8")
    page = ACTIVE_SKILL.read_text(encoding="utf-8")

    assert "CharacterCareerActiveSkillAdvanceRules.TryPlanAdvance" in model
    assert "CareerActiveSkillAdvanceRequest" in model
    assert "ExpectedContentRevision" in model
    assert "SavedContentRevision" in model
    assert "Review exact diff" in page
    assert "Apply and verify once" in page
    assert "Interlocked.CompareExchange" in page
    assert "Sr5CareerActiveSkillCoordinator" in page
    assert "fresh typed skill and expense reloads" in page
    assert "cannot be cleared or replayed" in page


def test_shared_action_boundary_has_route_idempotency_and_fail_closed_crash_recovery() -> None:
    model = MODEL.read_text(encoding="utf-8")
    page = ACTIVE_SKILL.read_text(encoding="utf-8")
    coordinator = ACTIVE_SKILL_COORDINATOR.read_text(encoding="utf-8")
    store = CHECKPOINT_STORE.read_text(encoding="utf-8")
    owner_store = MUTATION_OWNER_STORE.read_text(encoding="utf-8")

    for contract in (
        "Sr5CareerCostQuote",
        "Sr5CareerActionPlan",
        "Sr5CareerApplyResult",
        "IdempotencyKey",
        "Sr5CareerWizardRoutes",
    ):
        assert contract in model
    assert "Sr5CareerCheckpointPhase.Applying" in page
    assert "ResolveCheckpointAsync" in page
    assert "do not retry" in model.lower()
    assert "Preferences.Default.Set" in store
    assert "JsonSerializer.Serialize" in store
    assert "TryBeginApply" in store
    assert "TryRecordAuthoritativeResolution" in store
    assert "Sr5CareerRecoveryProof.Verifies" in store
    assert "ReceiptMatchesCheckpoint" in store
    assert "TryWriteAndReadBackLocked" in store
    assert "AcquireDurableApplyingLeaseAsync" in store
    assert "_mutationOwners.AcquireExecutionLeaseAsync" in store
    assert "_mutationOwners.TryBegin" in store
    assert "_mutationOwners.TryComplete" in store
    assert "ProcessGate.Wait(0)" in owner_store
    assert "ProcessGate.WaitAsync(cancellationToken)" in owner_store
    assert "OwnerId" in store
    assert "ExpenseMatches" in coordinator
    assert "loadedSkill.Identity.SourceSkillId" in coordinator
    assert "loadedExpense.ExpenseDateLocal" in coordinator
    assert "loadedExpense.RawKarmaUndoType" in coordinator
    for exact_field in (
        "ExpenseTypeElementPresent",
        "RawExpenseType",
        "RefundElementPresent",
        "Refund",
        "ForceCareerVisibleElementPresent",
        "ForceCareerVisible",
        "NuyenUndoTypeElementPresent",
        "RawNuyenUndoType",
        "UndoObjectIdElementPresent",
        "RawUndoObjectId",
        "UndoQuantityElementPresent",
        "UndoQuantity",
        "UndoExtraElementPresent",
        "RawUndoExtra",
    ):
        assert exact_field in coordinator


def test_globally_loaded_reviewed_checkpoint_is_authenticated_before_ui_or_delete() -> None:
    model = MODEL.read_text(encoding="utf-8")
    page = ACTIVE_SKILL.read_text(encoding="utf-8")
    store = CHECKPOINT_STORE.read_text(encoding="utf-8")

    assert "PreferencesSr5CareerCheckpointOwnerAuthority" in page
    assert "_reviewedAuthority.CurrentOwnerId" in page
    assert "TryAuthenticateReviewedCheckpoint(checkpoint, draft" in page
    assert "checkpoint.MatchesReviewedDraft(draft)" in page
    assert "currentAccess.Owns(checkpoint)" in page
    assert "TryDeleteReviewed" in page
    assert "TryDeleteApplied" in page
    assert "Sr5CareerReviewedCheckpointAccess" in model
    for binding in (
        "CharacterCreated",
        "GameEdition",
        "OwnerId",
        "WorkspaceId",
        "ExpectedContentRevision",
        "ActionId",
        "IdempotencyKey",
        "SchemaVersion",
        "RouteId",
    ):
        assert binding in model
    assert "_reviewedAuthority.Owns(current)" in store
    assert "_reviewedAuthority.OwnsCurrentRunner(current)" in store
    assert "MatchesActionDraft" in model
    assert "OwnsCurrentRunner" in page
    assert 'StorageKey = "sr5.career.active-skill.draft.v1"' in store


def test_active_skill_deep_return_reloads_durable_recovery_before_routing() -> None:
    page = ACTIVE_SKILL.read_text(encoding="utf-8")
    appearing = page.split("protected override async void OnAppearing()", maxsplit=1)[1]
    appearing = appearing.split("protected override void Refresh()", maxsplit=1)[0]

    assert "await Coordinator.InitializeAsync();" in appearing
    assert "LoadRecoveryCheckpoint();" in appearing
    assert "RefreshEnabledState();" in appearing
    assert appearing.index("LoadRecoveryCheckpoint();") < appearing.index(
        "RefreshEnabledState();"
    )
    assert appearing.index("RefreshEnabledState();") < appearing.index(
        "_checkpoint?.Phase"
    )

    refresh = page.split("private void RefreshEnabledState()", maxsplit=1)[1]
    refresh = refresh.split("private async Task OpenReviewAsync()", maxsplit=1)[0]
    assert "_resolve.IsEnabled = _resolve.IsVisible" in refresh
    assert "_checkpoint is not null" in refresh
    assert "_reviewedAuthority.OwnsCurrentRunner(_checkpoint)" in refresh


def test_creation_budget_ribbon_uses_typed_skill_budget_ids() -> None:
    build = BUILD.read_text(encoding="utf-8")
    for budget_id in (
        "CharacterCreationBudgetIds.ActiveSkills",
        "CharacterCreationBudgetIds.SkillGroups",
        "CharacterCreationBudgetIds.KnowledgeSkills",
    ):
        assert budget_id in build
    for literal in ('"active-skills" =>', '"skill-groups" =>', '"knowledge-skills" =>'):
        assert literal not in build


def test_authority_harness_explicitly_compiles_real_sources_and_projection_tests() -> None:
    harness = (
        ROOT / "tests/Chummer.Android.Sr5CareerAuthority.Tests/Chummer.Android.Sr5CareerAuthority.Tests.csproj"
    ).read_text(encoding="utf-8")

    assert "AUTHORITY_LIGHTWEIGHT" in harness
    for real_source in (
        "Sr5CareerActiveSkillCoordinator.cs",
        "Sr5CareerActiveSkillWizardPage.cs",
        "Sr5CareerDraftCheckpointStore.cs",
        "Sr5CareerWizardModel.cs",
        "CareerKarmaExpenseEditRequest.cs",
    ):
        assert real_source in harness


def test_created_sr5_build_route_is_user_visible_and_action_boundary_is_rechecked() -> None:
    build = BUILD.read_text(encoding="utf-8")
    coordinator = ACTIVE_SKILL_COORDINATOR.read_text(encoding="utf-8")
    page = ACTIVE_SKILL.read_text(encoding="utf-8")

    assert "AddSr5CareerWizardRoute" in build
    assert '"build-sr5-career-wizard"' in build
    assert "Sr5CareerWizardCatalog.IsSr5CareerRunner" in build
    assert "Sr5CareerRunnerGuard.RequireCreated" in coordinator
    assert "Sr5CareerRunnerGuard.RequireCreated" in page
    assert "RequireCreatedSr5" not in coordinator
    assert "RequireCreatedSr5" not in page


def test_created_sr5_initial_phone_sheet_is_wizard_only() -> None:
    build = BUILD.read_text(encoding="utf-8")
    career_branch = build.split("if (isSr5CareerRunner)", maxsplit=1)[1]
    career_branch = career_branch.split("AddSummary();", maxsplit=2)

    assert "AddSr5CareerWizardRoute();" in career_branch[0]
    assert "AddFeedback();" in career_branch[1]
    assert "return;" in career_branch[1]
    assert "AddDossier();" not in career_branch[0] + career_branch[1]
    assert "AddBuildAreas();" not in career_branch[0] + career_branch[1]
    fallback = career_branch[2].split("private void AddRouteMarker", maxsplit=1)[0]
    assert 'unavailable.AutomationId = "build-career-wizard-unavailable";' in fallback
    assert "no authorized Career wizard" in fallback
    assert "AddDossier();" not in fallback
    assert "AddBuildAreas();" not in fallback


def test_staged_active_skill_has_a_dedicated_physical_arm64_api36_proof_boundary() -> None:
    driver = PHYSICAL_ACTIVE_SKILL_DRIVER.read_text(encoding="utf-8")

    assert 'abi != "arm64-v8a"' in driver
    assert 'api != "36"' in driver
    assert 'device.serial.startswith("emulator-")' in driver
    assert '"classification": "non-emulator-arm64-api36"' in driver
    assert "non-cryptographic getprop and adb serial observations" in driver
    for route in (
        "build-sr5-career-wizard",
        "sr5-career/advancement",
        "sr5-career/advancement/active-skill/choose",
        "sr5-career/advancement/active-skill/review",
        "sr5-career/advancement/active-skill/receipt",
    ):
        assert route in driver
    assert "reviewedCheckpointSha256" in driver
    assert "appliedCheckpointSha256" in driver
    assert "expected_idempotency_key" in driver
    assert "require_same_action(reviewed.payload, applied.payload)" in driver
    assert "--build-provenance-manifest" in driver
    assert "load_and_verify_manifest" in driver
    assert 'repositories["android"]["commit"]' in driver
    assert 'artifact["sha256"]' in driver
    assert "allow-destructive-disposable-device" in driver
    assert '"status": "device-pass-source-bound"' in driver
    assert '"releaseEvidenceStatus": "source-and-apk-bound-local-build-not-release-attested"' in driver
    assert 'context["buildProvenance"] = build_provenance' in driver
    assert '"buildProvenance": context["buildProvenance"]' in driver
    assert "locate_explicit_receipt" in driver
    assert "reject_symlink_components" in driver
    assert "safe_fixture_basename" in driver
    assert "outside every source worktree" in driver
    assert "validate_output_layout" in driver
    assert "separate non-overlapping" in driver
    assert "label_bound_value" in driver
    assert "write_receipt_atomically" in driver
    assert driver.count("shared.force_stop_and_launch_new_process") == 3
