from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
MODEL = ROOT / "src/Chummer.Android/Native/Sr5CareerWizardModel.cs"
HUB = ROOT / "src/Chummer.Android/Native/Sr5CareerWizardPage.cs"
ACTIVE_SKILL = ROOT / "src/Chummer.Android/Native/Sr5CareerActiveSkillWizardPage.cs"
ACTIVE_SKILL_COORDINATOR = ROOT / "src/Chummer.Android/Native/Sr5CareerActiveSkillCoordinator.cs"
CHECKPOINT_STORE = ROOT / "src/Chummer.Android/Native/Sr5CareerDraftCheckpointStore.cs"
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
    assert "generic All actions" in hub


def test_requested_sr5_career_lanes_are_present_and_missing_authority_stays_blocked() -> None:
    model = MODEL.read_text(encoding="utf-8")
    hub = HUB.read_text(encoding="utf-8")

    for lane in (
        "Advancement",
        "BeforeRun",
        "LiveRun",
        "AfterRun",
        "Downtime",
        "Corrections",
    ):
        assert f"Sr5CareerWizardLane.{lane}" in model
    for blocker in (
        "atomic run-closeout bundle",
        "Undo Karma/Nuyen Expense",
        "survive restart",
        "stable selected weapon context",
        "There is no Heat field",
    ):
        assert blocker in hub


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
    assert "RequireCreatedSr5" in coordinator
    assert "RequireCreatedSr5" in page


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
    assert "expected-android-head" in driver
    assert "expected-apk-sha256" in driver
    assert "allow-destructive-disposable-device" in driver
    assert "acknowledge-unverified-build-provenance" in driver
    assert '"status": "device-pass-non-release"' in driver
    assert '"releaseEvidenceEligible": False' in driver
    assert '"externalBuildAuthorityManifest": None' in driver
    assert "locate_explicit_receipt" in driver
    assert "reject_symlink_components" in driver
    assert "safe_fixture_basename" in driver
    assert "outside every source worktree" in driver
    assert "validate_output_layout" in driver
    assert "separate non-overlapping" in driver
    assert "label_bound_value" in driver
    assert "write_receipt_atomically" in driver
    assert driver.count("shared.force_stop_and_launch_new_process") == 3
