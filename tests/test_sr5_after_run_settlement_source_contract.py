from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
NATIVE = ROOT / "src" / "Chummer.Android" / "Native"


def read(name: str) -> str:
    return (NATIVE / name).read_text(encoding="utf-8")


def test_phone_surface_has_every_governed_stage_and_two_entry_points() -> None:
    model = read("Sr5CareerWizardModel.cs")
    page = read("Sr5AfterRunSettlementWizardPage.cs")
    career = read("Sr5CareerWizardPage.cs")
    table = read("PhoneShellPages.cs")
    for route in (
        "AfterRunChoose",
        "AfterRunRewards",
        "AfterRunConsequences",
        "AfterRunContacts",
        "AfterRunGmReview",
        "AfterRunOwnerReview",
        "AfterRunReview",
        "AfterRunReceipt",
    ):
        assert f"public const string {route}" in model
        assert f"Sr5CareerWizardRoutes.{route}" in page
    assert "OpenAfterRunSettlementAsync" in career
    assert "RunnerSessionSr5AfterRunSettlementPresenter" in career
    assert 'automationId: "sr5-career-action-after-run"' in career
    assert 'automationId: "phone-table-after-run"' in table
    assert "Sr5AfterRunSettlementWizardPage" in table
    assert "GenericQuickEdit" not in career + table


def test_rewards_are_attested_context_not_a_second_character_mutation() -> None:
    model = read("Sr5AfterRunWizardModel.cs")
    page = read("Sr5AfterRunSettlementWizardPage.cs")
    runner = read("RunnerSessionCoordinator.cs")
    assert "Sr5AfterRunRewardContext" in model
    assert "RewardReceiptDigest" in model
    assert "ContextDigest" in model
    assert "does not award Karma or Nuyen" in model
    assert "does not duplicate the reward ledger" in page
    assert "manual Karma/Nuyen/reputation" in runner
    command = model.split(
        "public CharacterAfterRunSettlementCommand ToCommand()", 1
    )[1].split("public bool IsExact()", 1)[0]
    assert "KarmaAward" not in command
    assert "NuyenAward" not in command
    assert "RewardReceiptDigest" not in command
    assert "GenericQuickEdit" not in model + page
    assert "QuickEdit" not in model + page


def test_core_quote_plan_command_and_all_authority_digests_are_bound() -> None:
    model = read("Sr5AfterRunWizardModel.cs")
    coordinator = read("Sr5AfterRunSettlementCoordinator.cs")
    shared = read("Sr5CareerWizardModel.cs")
    for marker in (
        "CharacterAfterRunSettlementIdentity",
        "CharacterAfterRunSettlementQuoteBinding",
        "CharacterAfterRunSettlementPlan",
        "CharacterAfterRunSettlementCommand ToCommand",
        "CharacterAfterRunSettlementServiceSchemas.CommandV1",
        "ExpectedSourceDigest",
        "ExpectedCustomDataDigest",
        "ExpectedGmPolicyDigest",
        "ExpectedRuntimeDigest",
        "ExpectedLogicalDigest",
        "ExpectedBindingDigest",
        "GmReviewDigest",
        "OwnerReviewDigest",
        "CharacterAfterRunSettlementRules.TryCreatePlan",
    ):
        assert marker in model + coordinator
    assert "FromAfterRunSettlement" in shared
    assert "Sr5CareerActionKind.AfterRunSettlement" in shared
    assert "rewardContextDigest" in shared


def test_both_external_reviews_must_exist_and_be_explicitly_seen() -> None:
    model = read("Sr5AfterRunWizardModel.cs")
    page = read("Sr5AfterRunSettlementWizardPage.cs")
    for marker in (
        "GmApproved",
        "OwnerApproved",
        "GmApprovalReviewed",
        "OwnerApprovalReviewed",
        "GameMasterReview",
        "OwnerReview",
        "Android cannot create or infer it",
    ):
        assert marker in model + page
    assert "Guid.NewGuid()" in page
    assert "ExplicitlyConfirmed: true" in model


def test_manual_phone_intake_covers_typed_result_policy_contacts_and_approvals() -> None:
    page = read("Sr5AfterRunManualProposalPage.cs")
    model = read("Sr5CareerWizardModel.cs")
    source = read("Sr5AfterRunManualProposalSource.cs")
    for marker in (
        "AfterRunEnter",
        "Proposal UUID",
        "Run UUID",
        "Character UUID",
        "Karma awarded",
        "Nuyen awarded",
        "Current Heat",
        "Street Cred delta",
        "Notoriety delta",
        "Public Awareness delta",
        "Contact proposals",
        "GM actor ID",
        "Owner actor ID",
        "GmApproved",
        "OwnerApproved",
        "PublishManualAfterRunProposalAsync",
    ):
        assert marker in page + model + source
    assert "Guid.NewGuid()" not in page
    assert "GenericQuickEdit" not in page


def test_android_host_source_is_workspace_bound_durable_and_fail_closed() -> None:
    source = read("Sr5AfterRunManualProposalSource.cs")
    snapshot = read("AndroidAfterRunWorkspaceSnapshotSource.cs")
    runner = read("RunnerSessionCoordinator.cs")
    for marker in (
        "ICharacterAfterRunSettlementProposalProjectionSource",
        "IAndroidAfterRunProposalCatalog",
        "ISr5AfterRunManualProposalAuthority",
        "CharacterProjectionDigest",
        "ComputeProposalDigest",
        "ComputeLedgerDigest",
        "FileOptions.WriteThrough",
        "write/read-back verification",
        "CharacterAfterRunSettlementRules.TryCreateQuote",
        "quote.CanSettle",
        "CharacterAfterRunSettlementProposalProjectionOutcome.Conflict",
        "Sr5AfterRunCatalogStatus.Corrupt",
    ):
        assert marker in source + runner
    assert "IWorkspaceStore" in snapshot
    assert "SavedRevision != saved.ContentRevision" in snapshot
    assert "SHA256.HashData" in snapshot
    assert "ReplaceWorkspaceDocument" not in snapshot


def test_runtime_composes_one_shared_host_instance_before_core_runtime() -> None:
    composition = (ROOT / "src" / "Chummer.Android" / "MauiProgram.cs").read_text(
        encoding="utf-8"
    )
    source_registration = composition.index(
        "ICharacterAfterRunSettlementProposalProjectionSource"
    )
    runtime_registration = composition.index("AddChummerLocalRuntimeClient(")
    assert source_registration < runtime_registration
    assert composition.count("GetRequiredService<Sr5AfterRunManualProposalSource>()") == 3
    assert "FileSr5AfterRunManualProposalBackend(statePath)" in composition


def test_shared_owner_cas_receipt_and_unknown_recovery_fail_closed() -> None:
    store = read("Sr5AfterRunSettlementCheckpointStore.cs")
    coordinator = read("Sr5AfterRunSettlementCoordinator.cs")
    owner = read("Sr5CareerMutationOwnerStore.cs")
    for marker in (
        "Sr5CareerMutationOwnerStore",
        "TryBeginApply",
        "AcquireDurableApplyingLeaseAsync",
        "TryRecordAuthoritativeResolution",
        "replay-blocking",
        "write/read-back",
        "Sr5AfterRunSettlementRecoveryProof.Verifies",
        "OutcomeUnknown must remain Applying",
    ):
        assert marker in store
    assert "Sr5CareerMutationDomains.AfterRunSettlement" in store
    assert 'AfterRunSettlement = "after-run-settlement"' in owner
    assert "CharacterAfterRunSettlementServiceIntegrity.TryComputeResultDigest" in coordinator
    assert "ReceiptMatchesDraft" in coordinator
    assert "Do not replay, clear, or claim success" in coordinator


def test_default_runtime_composition_is_explicitly_unavailable() -> None:
    runner = read("RunnerSessionCoordinator.cs")
    assert "ICharacterAfterRunSettlementService? afterRunSettlementService = null" in runner
    assert "IAndroidAfterRunProposalCatalog? afterRunProposalCatalog = null" in runner
    assert "service is null || catalog is null" in runner
    assert "Sr5AfterRunSettlementEditorState.Unavailable" in runner
    assert "No fallback mutation is available" in runner
    assert "service.Settle(command)" in runner


def test_physical_contract_and_driver_cannot_claim_a_pass_without_fixture() -> None:
    model = read("Sr5AfterRunWizardModel.cs")
    driver = (ROOT / "tests" / "run_api36_sr5_after_run_settlement_e2e.py").read_text(
        encoding="utf-8"
    )
    for marker in (
        "FixtureSha256",
        "ApiLevel >= 36",
        "BeforeWorkspaceSha256",
        "AfterWorkspaceSha256",
        "CoreReceiptSha256",
        "ProcessIdBeforeRestart",
        "ProcessIdAfterRestart",
        "RequiredRoutes",
    ):
        assert marker in model
    assert '"status": "unavailable"' in driver
    assert '"physicalDeviceProof": False' in driver
    assert '"releaseEvidenceEligible": False' in driver
    assert "sr5-after-run-settlement-e2e.json" in driver
    assert "return 3" in driver
    assert "device-pass" not in driver
