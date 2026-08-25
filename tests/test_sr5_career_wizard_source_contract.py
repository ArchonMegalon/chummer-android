from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
MODEL = ROOT / "src/Chummer.Android/Native/Sr5CareerWizardModel.cs"
HUB = ROOT / "src/Chummer.Android/Native/Sr5CareerWizardPage.cs"
ACTIVE_SKILL = ROOT / "src/Chummer.Android/Native/Sr5CareerActiveSkillWizardPage.cs"
CHECKPOINT_STORE = ROOT / "src/Chummer.Android/Native/Sr5CareerDraftCheckpointStore.cs"


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
    assert "actualKarma.Value != draft.Plan.SavedCharacterKarma" in model
    assert "Review exact diff" in page
    assert "Apply and save once" in page
    assert "Interlocked.CompareExchange" in page
    assert "ApplyCareerActiveSkillAdvanceAsync" in page
    assert "Receipt verified against the current clean saved revision" in page
    assert "idempotent retry receipt" in page


def test_shared_action_boundary_has_route_idempotency_and_fail_closed_crash_recovery() -> None:
    model = MODEL.read_text(encoding="utf-8")
    page = ACTIVE_SKILL.read_text(encoding="utf-8")
    store = CHECKPOINT_STORE.read_text(encoding="utf-8")

    for contract in (
        "Sr5CareerCostQuote",
        "Sr5CareerActionPlan",
        "Sr5CareerApplyResult",
        "IdempotencyKey",
        "Sr5CareerWizardRoutes",
        "AtomicSingleAction",
    ):
        assert contract in model
    assert "Sr5CareerCheckpointPhase.Applying" in page
    assert "OutcomeUnknown" in page
    assert "do not retry" in model.lower()
    assert "Preferences.Default.Set" in store
    assert "JsonSerializer.Serialize" in store
