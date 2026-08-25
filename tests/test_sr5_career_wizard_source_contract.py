from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
MODEL = ROOT / "src/Chummer.Android/Native/Sr5CareerWizardModel.cs"
HUB = ROOT / "src/Chummer.Android/Native/Sr5CareerWizardPage.cs"
ACTIVE_SKILL = ROOT / "src/Chummer.Android/Native/Sr5CareerActiveSkillWizardPage.cs"
ACTIVE_SKILL_COORDINATOR = ROOT / "src/Chummer.Android/Native/Sr5CareerActiveSkillCoordinator.cs"
CHECKPOINT_STORE = ROOT / "src/Chummer.Android/Native/Sr5CareerDraftCheckpointStore.cs"
BUILD = ROOT / "src/Chummer.Android/Native/BuildPage.cs"


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
    assert "TryWriteAndReadBackLocked" in store
    assert "OwnerId" in store
    assert "ExpenseMatches" in coordinator
    assert "loadedSkill.Identity.SourceSkillId" in coordinator
    assert "loadedExpense.ExpenseDateLocal" in coordinator
    assert "loadedExpense.RawKarmaUndoType" in coordinator


def test_created_sr5_build_route_is_user_visible_and_action_boundary_is_rechecked() -> None:
    build = BUILD.read_text(encoding="utf-8")
    coordinator = ACTIVE_SKILL_COORDINATOR.read_text(encoding="utf-8")
    page = ACTIVE_SKILL.read_text(encoding="utf-8")

    assert "AddSr5CareerWizardRoute" in build
    assert '"build-sr5-career-wizard"' in build
    assert "Sr5CareerWizardCatalog.IsSr5CareerRunner" in build
    assert "RequireCreatedSr5" in coordinator
    assert "RequireCreatedSr5" in page
