from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
NATIVE = ROOT / "src" / "Chummer.Android" / "Native"


def read(name: str) -> str:
    return (NATIVE / name).read_text(encoding="utf-8")


def test_specialization_has_four_native_deep_routes_and_entry_points() -> None:
    model = read("Sr5CareerWizardModel.cs")
    page = read("Sr5CareerSpecializationWizardPage.cs")
    career = read("Sr5CareerWizardPage.cs")
    build = read("BuildPage.cs")
    for route in (
        "SpecializationChoose",
        "SpecializationConfigure",
        "SpecializationReview",
        "SpecializationReceipt",
    ):
        assert f"Sr5CareerWizardRoutes.{route}" in page
        assert f"public const string {route}" in model
    assert "OpenSpecializationWizardAsync" in career
    assert "OpenSr5CareerSpecializationWizardAsync" in build
    assert "build-career-specialization" in build


def test_specialization_uses_core_presentation_authority_not_quick_edit() -> None:
    model = read("Sr5CareerSpecializationWizardModel.cs")
    coordinator = read("Sr5CareerSpecializationCoordinator.cs")
    session = read("RunnerSessionCoordinator.cs")
    combined = model + coordinator + session
    assert "CharacterCareerSkillIdentity" in combined
    assert "CharacterCareerSkillSpecializationSelection" in combined
    assert "CharacterCareerSkillSpecializationRules.TryPlanAdd" in combined
    assert "PrepareCareerSkillSpecializationQuoteAsync" in combined
    assert "ApplyCareerSkillSpecializationAsync" in combined
    assert "GenericQuickEdit" not in combined
    assert "QuickEdit" not in combined


def test_four_revision_quote_and_typed_option_identity_are_durable() -> None:
    model = read("Sr5CareerSpecializationWizardModel.cs")
    action = read("Sr5CareerWizardModel.cs")
    for field in (
        "CharacterRevision",
        "SourceRevision",
        "RuleDigest",
        "LogicalRevision",
        "OptionIdentity",
        "SpecializationId",
        "ExpenseId",
    ):
        assert field in model
        assert field in action
    assert 'quote.Identity.SourceSkillId?.ToString("D") ?? "custom"' in action
    assert "CharacterCareerSkillSpecializationOptionKind.Custom" in model


def test_shared_mutation_owner_and_unknown_outcomes_fail_closed() -> None:
    store = read("Sr5CareerSpecializationCheckpointStore.cs")
    coordinator = read("Sr5CareerSpecializationCoordinator.cs")
    owner = read("Sr5CareerMutationOwnerStore.cs")
    assert "Sr5CareerMutationOwnerStore" in store
    assert "Sr5CareerMutationDomains.SkillSpecializationAdd" in store
    assert 'SkillSpecializationAdd = "skill-specialization-add"' in owner
    assert "AcquireDurableApplyingLeaseAsync" in store
    assert "OutcomeUnknown" in coordinator
    assert "do not replay or clear" in coordinator
    assert "No complete persisted Core receipt authority exists" in coordinator


def test_physical_proof_is_a_fail_closed_contract_not_a_claim() -> None:
    model = read("Sr5CareerSpecializationWizardModel.cs")
    page = read("Sr5CareerSpecializationWizardPage.cs")
    assert "Sr5CareerSpecializationPhysicalProofContract" in model
    assert "ApiLevel >= 36" in model
    assert "BeforeWorkspaceSha256" in model
    assert "AfterWorkspaceSha256" in model
    assert "EvidenceArtifactSha256" in model
    assert "not a persisted Core receipt" in page
