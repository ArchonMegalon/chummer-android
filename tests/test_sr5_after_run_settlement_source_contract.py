from pathlib import Path
import re
import xml.etree.ElementTree as ET


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
    assert "() => RunAsync(OpenAfterRunSettlementAsync)" in career
    assert "enabled: canOpenAfterRun" in career
    assert 'blocker.AutomationId = "sr5-career-after-run-unavailable"' in career
    assert "Sr5AfterRunSettlementEntryGuard.TryValidate" in career
    assert 'automationId: "phone-table-after-run"' in table
    assert "Sr5AfterRunSettlementWizardPage" in table
    for surface in (career, table, read("BuildPage.cs")):
        assert "CreateEntryDestinationAsync(Coordinator, editor)" in surface
        assert "Page destination = await Sr5AfterRunSettlementWizardPage" in surface
        assert "editor.Status == Sr5AfterRunCatalogStatus.Missing" not in surface
    assert "TryReadOwnedRecovery" in page
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


def test_entry_routing_prefers_owned_recovery_and_uses_local_rewards_not_manual_ids() -> None:
    page = read("Sr5AfterRunSettlementWizardPage.cs")
    store = read("Sr5AfterRunSettlementCheckpointStore.cs")
    route = page.split("internal static async Task<Page> CreateEntryDestinationAsync", 1)[1]
    route = route.split("protected override void Refresh", 1)[0]
    owned = store.split("internal bool TryReadOwnedRecovery", 1)[1]
    owned = owned.split("public bool TryCreate", 1)[0]

    assert route.index("TryReadOwnedRecovery") < route.index(
        "Sr5AfterRunCatalogStatus.Missing"
    )
    assert route.index("RequireCurrentEntry();") < route.index("CreateDependencies")
    assert "current.WorkspaceId != editor.WorkspaceId" in route
    assert route.count("RequireCurrentEntry();") == 2
    assert "await coordinator.PrepareAfterRunRewardEntryAsync" in route
    assert "new Sr5AfterRunRewardWizardPage(coordinator, reward)" in route
    assert "current.ContentRevision != editor.WorkspaceRevision" in route
    assert "current.IsDirty || current.IsBusy" in route
    assert "string.IsNullOrWhiteSpace(recoveryBlocker)" in route
    assert "SupportsAfterRunRewardEntry" in route
    assert "Sr5AfterRunRewardWizardPage" in route
    assert "Sr5AfterRunManualProposalPage" not in route
    assert "Sr5AfterRunSettlementWizardPage" in route
    assert "checkpoint.Phase == Sr5CareerCheckpointPhase.Reviewed" in owned
    assert "_authority.OwnsReviewed(checkpoint)" in owned
    assert "_authority.OwnsCurrentRunner(checkpoint)" in owned
    assert "replay-blocking" in owned


def test_historical_receipt_route_cannot_resume_or_replay_and_ack_requires_unowned_gate() -> None:
    page = read("Sr5AfterRunSettlementWizardPage.cs")
    store = read("Sr5AfterRunSettlementCheckpointStore.cs")
    history = store.split("internal bool TryReadOwnedRecordedReceipt", 1)[1].split("public bool TryCreate", 1)[0]
    assert "TryReadLocked" in history
    assert "TryReconcileResolvedOwner" not in history
    delete = store.split("private bool TryDelete(", 1)[1].split("private bool TryRequireCasLocked", 1)[0]
    assert delete.index("TryRunWhenUnowned") < delete.index("lock (Gate)")
    assert "OwnsRecordedReceipt" in delete
    assert "TryRequireCasLocked" in delete
    receipt = page.split("public sealed class Sr5AfterRunSettlementReceiptPage", 1)[1]
    for forbidden in ("SettleAsync", "ApplyAsync", "ResolveAsync", "RetryAsync", "SaveAsync"):
        assert forbidden not in receipt
    assert "IsRecordedReceiptForCurrentRunner" in receipt
    assert "AfterRunSettlementRecordedRevisions" in receipt


def test_default_runtime_composition_is_explicitly_unavailable() -> None:
    runner = read("RunnerSessionCoordinator.cs")
    assert "ICharacterAfterRunSettlementService? afterRunSettlementService = null" in runner
    assert "IAndroidAfterRunProposalCatalog? afterRunProposalCatalog = null" in runner
    assert "service is null || catalog is null" in runner
    assert "Sr5AfterRunSettlementEditorState.Unavailable" in runner
    assert "No fallback mutation is available" in runner
    assert "service.Settle(command)" in runner


def test_discard_is_separate_from_resume_and_captures_the_confirmed_checkpoint() -> None:
    page = read("Sr5AfterRunSettlementWizardPage.cs")
    store = read("Sr5AfterRunSettlementCheckpointStore.cs")
    action = page.split("private async Task AbandonAsync()", 1)[1].split("private static string CandidateLabel", 1)[0]
    assert "IsDiscardableReviewForCurrentRunner" in action
    assert action.index("var expected = Sr5AfterRunSettlementCheckpointCas.From(_checkpoint)") < action.index("await _confirmAbandon")
    assert action.index("appearance != Volatile.Read(ref _discardAppearanceGeneration)") < action.index("_store.TryDeleteReviewed")
    assert action.index("runner != Coordinator.CaptureAfterRunRewardBinding(expected.OwnerId)") < action.index("_store.TryDeleteReviewed")
    assert "DisplayAlertAsync(title, message, accept, cancel)" in page
    assert action.index("if (!confirmed)") < action.index("_store.TryDeleteReviewed")
    assert "TryDeleteReviewed(\n                expected," in action
    assert "AfterRunSettlementDiscardPrompt" in action
    assert "TryReadOwnedDiscardableReview" in page
    assert "&& !ownsDiscardableReview" in page  # never a new-reward bypass
    assert "_resume.IsVisible = reviewed" in page
    assert "_abandon.IsVisible = discardable" in page
    discard = store.split("internal bool TryReadOwnedDiscardableReview", 1)[1].split("public bool TryCreate", 1)[0]
    assert "TryReadLocked" in discard
    assert "TryReconcileResolvedOwner" not in discard


def test_discard_copy_has_exact_supported_language_and_placeholder_parity() -> None:
    directory = ROOT / "src/Chummer.Android/Resources/Localization"
    catalogs = [
        {node.attrib["name"]: node.findtext("value") for node in ET.parse(directory / f"PhoneStrings{suffix}.resx").getroot().findall("data")}
        for suffix in ("", ".de", ".es")
    ]
    for key in ("AfterRunSettlementDiscardOnly", "AfterRunSettlementDiscardPrompt"):
        placeholders = set(re.findall(r"\{\d+\}", catalogs[0][key]))
        for catalog in catalogs:
            assert catalog[key].strip()
            assert set(re.findall(r"\{\d+\}", catalog[key])) == placeholders


def test_physical_contract_and_driver_require_the_exact_governed_fixture_and_remain_non_release() -> None:
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
    assert "fixture_path != DEFAULT_FIXTURE.resolve()" in driver
    assert "load_fixture(fixture_path)" in driver
    assert '"fixtureSha256": fixture_path' in driver
    assert "load_and_verify_manifest" in driver
    assert '"releaseEvidenceStatus": "source-and-apk-bound-local-build-not-release-attested"' in driver
    assert '"status": "device-pass-source-bound"' in driver
    assert "sr5-after-run-settlement-e2e.json" in driver
    assert '"executionStatus": "pass"' in driver
    assert '"releaseEvidenceEligible": True' not in driver
    assert '"status": "release-pass"' not in driver
