import copy
import hashlib
import importlib.util
import json
import shutil
import tempfile
import unittest
from pathlib import Path


REPO = Path(__file__).resolve().parents[1]


def load_script(name: str, relative: str):
    specification = importlib.util.spec_from_file_location(name, REPO / relative)
    if specification is None or specification.loader is None:
        raise RuntimeError(f"Unable to load {relative}")
    module = importlib.util.module_from_spec(specification)
    specification.loader.exec_module(module)
    return module


FINALIZE = load_script(
    "api36_finalize_journey_receipt",
    "scripts/finalize-api36-e2e-journey-receipt.py",
)
GATE = load_script(
    "api36_wizard_gate_contract",
    "scripts/api36_wizard_gate_contract.py",
)
AGGREGATE = load_script(
    "api36_verify_evidence_aggregate",
    "scripts/verify-api36-editing-e2e-aggregate.py",
)

RUN_ID = "424242"
ARTIFACT_ID = "987654"
ARTIFACT_DIGEST = "a" * 64
APK_SHA256 = "b" * 64
JOURNEYS = {
    "creation-prerequisite": "creation-prerequisite",
    "career-active-skill-advance": "career-active-skill-advance",
    "career-weapon-fire": "career-weapon-fire",
    "before-run-edge": "before-run-edge",
    "playtime-short-burst": "playtime-short-burst",
    "downtime-calendar": "sr5-downtime-calendar",
    "after-run-settlement": "sr5-after-run-settlement",
}
CREATION_PROSPECTIVE_PHASE_ELAPSED_MS = {
    # Exact prefix timings from hosted run 33309423140, followed by phase
    # elapsed values that include the observed scan lower bounds and modest
    # bounded work around them. This models a prospective complete receipt;
    # the hosted run itself stopped at 250,054 ms before the later phases.
    "device-preflight-install": 40_079,
    "initial-navigation": 34_239,
    "initial-authority": 70_699,
    "dashboard-proof": 7_441,
    "dashboard-authority-inventory": 13_017,
    "advanced-editor-gate-inventory": 38_278,
    "prerequisite-authority-inventory": 41_987,
    "priority-ranks": 10_000,
    "typed-authority-options": 10_000,
    "talent-active-skill-grant": 8_000,
    "talent-active-skill-preservation": 8_000,
    "talent-active-skill-reset": 8_000,
    "talent-active-skill-reselection": 8_000,
    "talent-active-grant-completion": 80_000,
    "talent-active-preview": 5_000,
    "talent-skill-group-selection": 10_000,
    "talent-skill-group-grant": 8_000,
    "talent-skill-group-preservation": 8_000,
    "talent-skill-group-reset": 8_000,
    "talent-skill-group-reselection": 8_000,
    "talent-skill-group-grant-completion": 80_000,
    "preview-confirm": 8_000,
    "same-process-reopen": 5_000,
    "same-process-authority-options": 5_000,
    "same-process-restored-talent-grant": 5_000,
    # Exact run 33637265813 proved the product route and stable Resources scan,
    # then exhausted the caller-owned observer lease during measured reverse
    # reacquisition. Use the evidence-backed strict cap rather than inventing
    # a faster prospective receipt; the whole-journey target remains fixed.
    "resources-initial-authority": 180_000,
    "resources-preview-confirm": 240_000,
    "resources-same-process-reopen": 120_000,
    "resources-prerequisite-rebind": 180_000,
    "process-restart-reopen": 5_772,
    "process-restart-authority-options": 5_000,
    "process-restart-restored-talent-grant": 5_000,
    "process-restart-resources": 5_000,
}


def talent_reacquisition_scan(phase_id: str) -> dict[str, object]:
    return {
        "scanId": f"{phase_id}-fixture-reacquisition",
        "status": "resolved",
        "phaseId": phase_id,
        "navigationMode": "measured-direction-stable-boundary-overlap-recovery",
        "direction": "none",
        "distanceRatio": 0.60,
        "startingViewport": 0,
        "targetViewport": 0,
        "normalizedTargetViewport": 0,
        "measuredDelta": 0,
        "configuredMaxScrolls": 0,
        "catalogMovementExtent": 11,
        "stableRepeats": 2,
        "stableBoundaryProven": False,
        "primaryDirection": "none",
        "primaryDistanceRatio": 0.60,
        "primaryConfiguredMaxScrolls": 0,
        "primaryStableBoundaryProven": False,
        "primaryScreens": 1,
        "primarySwipes": 0,
        "primaryEmptyHierarchyReads": 0,
        "primarySystemUiDismissals": 0,
        "recoveryEligible": False,
        "recoveryUsed": False,
        "recoveryDirection": "none",
        "recoveryDistanceRatio": 0.22,
        "recoveryConfiguredMaxScrolls": 0,
        "recoveryStableBoundaryProven": False,
        "recoveryScreens": 0,
        "recoverySwipes": 0,
        "recoveryEmptyHierarchyReads": 0,
        "recoverySystemUiDismissals": 0,
        "deadlineEnforced": True,
        "exactResourceIds": [f"{phase_id}-fixture-resource"],
        "screens": 1,
        "swipes": 0,
        "emptyHierarchyReads": 0,
        "systemUiDismissals": 0,
        "maximumEmptyHierarchyReads": 3,
        "maximumSystemUiDismissals": 3,
        "hierarchyReadCount": 1,
        "hierarchyElapsedMs": 400,
        "maximumHierarchyReadMs": 400,
        "elapsedMs": 500,
    }


def talent_overlap_recovery_scan(phase_id: str) -> dict[str, object]:
    scan = talent_reacquisition_scan(phase_id)
    scan.update(
        {
            "direction": "forward",
            "startingViewport": 0,
            "targetViewport": 7,
            "normalizedTargetViewport": 7,
            "measuredDelta": 7,
            "configuredMaxScrolls": 40,
            "distanceRatio": 0.22,
            "exactResourceIds": [
                "creation-prerequisite-talent-active-skill-option-perception"
            ],
            "primaryDirection": "forward",
            "primaryDistanceRatio": 0.22,
            "primaryConfiguredMaxScrolls": 40,
            "primaryStableBoundaryProven": True,
            "primaryScreens": 4,
            "primarySwipes": 3,
            "recoveryEligible": True,
            "recoveryUsed": True,
            "recoveryDirection": "reverse",
            "recoveryConfiguredMaxScrolls": 40,
            "recoveryScreens": 2,
            "recoverySwipes": 2,
            "screens": 6,
            "swipes": 5,
            "hierarchyReadCount": 6,
            "hierarchyElapsedMs": 400,
            "maximumHierarchyReadMs": 100,
            "elapsedMs": 1_500,
        }
    )
    return scan


def confirmed_receipt_back_reacquisition_scan() -> dict[str, object]:
    return {
        "scanId": AGGREGATE.CONFIRMED_RECEIPT_BACK_REACQUISITION_SCAN_ID,
        "status": "resolved",
        "phaseId": "preview-confirm",
        "direction": "forward-from-measured-restored-bottom",
        "distanceRatio": 0.30,
        "screens": 3,
        "swipes": 2,
        "configuredMaxScrolls": 2,
        "emptyHierarchyReads": 0,
        "systemUiDismissals": 0,
        "maximumEmptyHierarchyReads": (
            AGGREGATE.CONFIRMED_RECEIPT_BACK_REACQUISITION_MAX_EMPTY_HIERARCHIES
        ),
        "maximumSystemUiDismissals": (
            AGGREGATE.CONFIRMED_RECEIPT_BACK_REACQUISITION_MAX_SYSTEM_UI_DISMISSALS
        ),
        "downstreamReserveMs": (
            AGGREGATE.CONFIRMED_RECEIPT_BACK_REACQUISITION_DOWNSTREAM_RESERVE_MS
        ),
        "deadlineEnforced": True,
        "hierarchyReadCount": 3,
        "hierarchyElapsedMs": 300,
        "maximumHierarchyReadMs": 100,
        "elapsedMs": 700,
        "maximumElapsedMs": (
            AGGREGATE.CONFIRMED_RECEIPT_BACK_REACQUISITION_MAX_ELAPSED_MS
        ),
    }


def confirmed_receipt_scan(swipes: int = 2) -> dict[str, object]:
    return {
        "scanId": "creation-prerequisite-confirmed-receipt",
        "status": "required-authority-complete",
        "phaseId": "preview-confirm",
        "configuredMaxScrolls": 12,
        "distanceRatio": 0.30,
        "direction": "reverse-from-current-confirmed-bottom",
        "deadlineEnforced": True,
        "screens": swipes + 1,
        "swipes": swipes,
        "elapsedMs": 700,
    }


def post_confirm_dashboard_route_ready_scan() -> dict[str, object]:
    return {
        "scanId": AGGREGATE.POST_CONFIRM_DASHBOARD_ROUTE_READY_SCAN_ID,
        "status": "resolved",
        "phaseId": "preview-confirm",
        "observationMode": "fresh-cleared-main-log-snapshot-poll",
        "logcatReadCount": 2,
        "emptySnapshotCount": 1,
        "logcatElapsedMs": 500,
        "maximumLogcatReadMs": 300,
        "readAttemptMaxMs": (
            AGGREGATE.POST_CONFIRM_DASHBOARD_ROUTE_READY_READ_ATTEMPT_MAX_MS
        ),
        "pollDelayMs": (
            AGGREGATE.POST_CONFIRM_DASHBOARD_ROUTE_READY_POLL_DELAY_MS
        ),
        "expectedContentRevision": 2,
        "observedContentRevision": 2,
        "expectedSavedRevision": 2,
        "observedSavedRevision": 2,
        "workspaceId": "workspace-route-ready",
        "snapshotDigest": ARTIFACT_DIGEST,
        "deadlineEnforced": True,
        "maximumElapsedMs": (
            AGGREGATE.POST_CONFIRM_DASHBOARD_ROUTE_READY_MAX_ELAPSED_MS
        ),
        "elapsedMs": 750,
    }


def creation_receipt_with_timing(
    timing: dict[str, object],
) -> dict[str, object]:
    return {
        "timing": timing,
        "journeys": {
            "confirmedRevisions": {
                "contentRevision": 2,
                "savedRevision": 2,
                "draftRevision": 1,
            }
        },
    }


class Api36ArtifactAuthorityTests(unittest.TestCase):
    def authority(self, attempt: str = "1", **overrides: str) -> dict[str, str]:
        values = {
            "run_id": RUN_ID,
            "artifact_id": ARTIFACT_ID,
            "artifact_digest": ARTIFACT_DIGEST,
            "artifact_name": (
                f"chummer-android-api36-x64-debug-{RUN_ID}-{attempt}"
            ),
            "artifact_attempt": attempt,
            "apk_sha256": APK_SHA256,
        }
        values.update(overrides)
        return values

    def raw_receipt(self, journey: str) -> dict[str, object]:
        driver_journey = JOURNEYS[journey]
        receipt_schemas = {
            "creation-prerequisite": "chummer.android.creation-prerequisite-e2e/v1",
            "career-active-skill-advance": "chummer.android.editing-e2e/v1",
            "career-weapon-fire": "chummer.android.editing-e2e/v1",
            "before-run-edge": "chummer.android.sr5-before-run-edge-e2e/v1",
            "playtime-short-burst": "chummer.android.editing-e2e/v1",
            "downtime-calendar": "chummer.android.editing-e2e/v1",
            "after-run-settlement": (
                "chummer.android.sr5-after-run-settlement-hosted-e2e/v1"
            ),
        }
        receipt: dict[str, object] = {
            "schema": receipt_schemas[journey],
            "status": "pass",
            "profile": "phone",
            "apkSha256": APK_SHA256,
        }
        if journey == "creation-prerequisite":
            total_elapsed_ms = sum(CREATION_PROSPECTIVE_PHASE_ELAPSED_MS.values())
            receipt["executionStatus"] = "pass"
            receipt["journeys"] = {
                "confirmedRevisions": {
                    "contentRevision": 2,
                    "savedRevision": 2,
                    "draftRevision": 1,
                }
            }
            receipt["timing"] = {
                "schema": AGGREGATE.CREATION_PROGRESS_SCHEMA,
                "status": "timing-complete",
                "clock": "time.monotonic",
                "configuredTotalTargetMs": AGGREGATE.CREATION_TOTAL_TARGET_MS,
                "totalElapsedMs": total_elapsed_ms,
                "withinConfiguredTotalTarget": True,
                "phaseBudgetsMs": dict(AGGREGATE.CREATION_PHASE_BUDGETS_MS),
                "phases": [
                    {
                        "ordinal": ordinal,
                        "phaseId": phase_id,
                        "status": "pass",
                        "elapsedMs": CREATION_PROSPECTIVE_PHASE_ELAPSED_MS[phase_id],
                        "budgetMs": budget_ms,
                        "withinBudget": True,
                    }
                    for ordinal, (phase_id, budget_ms) in enumerate(
                        AGGREGATE.CREATION_PHASE_BUDGETS_MS.items(),
                        start=1,
                    )
                ],
                "milestones": [
                    {
                        "milestoneId": milestone_id,
                        "phaseId": phase_id,
                        "ordinal": ordinal,
                        "phaseElapsedMs": phase_elapsed_ms,
                        "segmentElapsedMs": segment_elapsed_ms,
                        "totalElapsedMs": total_elapsed_ms,
                    }
                    for ordinal, (
                        milestone_id,
                        phase_id,
                        phase_elapsed_ms,
                        segment_elapsed_ms,
                        total_elapsed_ms,
                    ) in enumerate(
                        (
                            ("app-cold-start-complete", "initial-navigation", 11_000, 11_000, 51_079),
                            ("phone-shell-locale-complete", "initial-navigation", 22_000, 11_000, 62_079),
                            ("dialog-acquisition-complete", "initial-navigation", 34_239, 12_239, 74_318),
                            ("create-bootstrap-transaction-complete", "initial-authority", 70_699, 70_699, 145_017),
                            ("dashboard-render-complete", "dashboard-proof", 7_441, 7_441, 152_458),
                        ),
                        start=1,
                    )
                ],
                "scans": [
                    {
                        "scanId": "dashboard-authority-poll",
                        "phaseId": "dashboard-authority-inventory",
                        "elapsedMs": 13_017,
                    },
                    {
                        "scanId": "advanced-editor-gate-initial",
                        "phaseId": "advanced-editor-gate-inventory",
                        "elapsedMs": 33_278,
                    },
                    {
                        "scanId": (
                            AGGREGATE.CREATION_METHOD_REACQUISITION_SCAN_ID
                        ),
                        "status": "resolved",
                        "phaseId": "advanced-editor-gate-inventory",
                        "direction": "down",
                        "distanceRatio": 0.60,
                        "screens": 8,
                        "swipes": 7,
                        "configuredMaxScrolls": 18,
                        "stableRepeats": 2,
                        "emptyHierarchyReads": 0,
                        "maximumEmptyHierarchyReads": 3,
                        "systemUiDismissals": 0,
                        "maximumSystemUiDismissals": 3,
                        "deadlineEnforced": True,
                        "phaseBudgetMs": 90_000,
                        "hierarchyReadCount": 8,
                        "hierarchyElapsedMs": 4_000,
                        "maximumHierarchyReadMs": 500,
                        "elapsedMs": 5_400,
                    },
                    {
                        "scanId": "prerequisite-authority",
                        "phaseId": "prerequisite-authority-inventory",
                        "elapsedMs": 36_987,
                    },
                    *(
                        talent_reacquisition_scan(phase_id)
                        for phase_id in AGGREGATE.TALENT_REACQUISITION_PHASES
                    ),
                    confirmed_receipt_scan(),
                    confirmed_receipt_back_reacquisition_scan(),
                    post_confirm_dashboard_route_ready_scan(),
                ],
            }
        else:
            receipt["journey"] = driver_journey
        if journey in {
            "before-run-edge",
            "playtime-short-burst",
            "downtime-calendar",
            "after-run-settlement",
        }:
            receipt["publicationAuthorized"] = False
        return receipt

    def test_contextual_journeys_are_gate_driven_in_finalizer_and_aggregate(self) -> None:
        expected = {
            "playtime-short-burst": (
                "playtime-short-burst",
                "chummer.android.editing-e2e/v1",
            ),
            "downtime-calendar": (
                "sr5-downtime-calendar",
                "chummer.android.editing-e2e/v1",
            ),
            "after-run-settlement": (
                "sr5-after-run-settlement",
                "chummer.android.sr5-after-run-settlement-hosted-e2e/v1",
            ),
        }
        for matrix_journey, (driver_journey, schema) in expected.items():
            with self.subTest(matrix_journey=matrix_journey):
                self.assertEqual(
                    (driver_journey, schema),
                    FINALIZE.JOURNEYS[matrix_journey],
                )
                self.assertEqual(driver_journey, AGGREGATE.JOURNEYS[matrix_journey])

    def materialize_journey(
        self,
        root: Path,
        journey: str,
        *,
        attempt: str = "1",
        authority_overrides: dict[str, str] | None = None,
    ) -> Path:
        authority = self.authority(attempt, **(authority_overrides or {}))
        driver_journey = JOURNEYS[journey]
        receipt = FINALIZE.bind_receipt(
            self.raw_receipt(journey),
            matrix_journey=journey,
            driver_journey=driver_journey,
            **authority,
        )
        directory = root / AGGREGATE.expected_artifact_directory(journey, RUN_ID)
        if directory.exists():
            shutil.rmtree(directory)
        directory.mkdir(parents=True)
        receipt_path = directory / "receipt.json"
        receipt_path.write_text(json.dumps(receipt, indent=2) + "\n", encoding="utf-8")
        digest = hashlib.sha256(receipt_path.read_bytes()).hexdigest()
        (directory / "receipt.json.sha256").write_text(
            f"{digest}  receipt.json\n",
            encoding="utf-8",
        )
        (directory / "execution-started.txt").write_text(
            "\n".join(
                (
                    "profile=phone",
                    f"matrix_journey={journey}",
                    f"driver_journey={driver_journey}",
                    f"gate_contract_sha256={GATE.contract_binding()['contractSha256']}",
                    f"artifact_id={authority['artifact_id']}",
                    f"artifact_digest={authority['artifact_digest']}",
                    f"artifact_name={authority['artifact_name']}",
                    f"artifact_attempt={authority['artifact_attempt']}",
                    f"apk_sha256={authority['apk_sha256']}",
                )
            )
            + "\n",
            encoding="utf-8",
        )
        return directory

    def materialize_all(self, root: Path, *, attempt: str = "1") -> None:
        for journey in JOURNEYS:
            self.materialize_journey(root, journey, attempt=attempt)

    def validate(self, root: Path, *, attempt: str = "1", **overrides: str):
        return AGGREGATE.validate_aggregate(
            root,
            build_result="success",
            matrix_result="success",
            **self.authority(attempt, **overrides),
        )

    def reseal(self, directory: Path) -> None:
        receipt = directory / "receipt.json"
        digest = hashlib.sha256(receipt.read_bytes()).hexdigest()
        (directory / "receipt.json.sha256").write_text(
            f"{digest}  receipt.json\n",
            encoding="utf-8",
        )

    def test_finalizer_rejects_full_editing_as_wizard_authority(self) -> None:
        receipt = {
            "schema": "chummer.android.editing-e2e/v1",
            "status": "pass",
            "profile": "phone",
            "apkSha256": APK_SHA256,
            "journey": "full",
        }
        with self.assertRaisesRegex(ValueError, "unsupported matrix journey"):
            FINALIZE.bind_receipt(
                receipt,
                matrix_journey="full-editing",
                driver_journey="full",
                **self.authority(),
            )

    def test_after_run_uses_only_its_unique_hosted_driver_contract(self) -> None:
        self.assertEqual(
            (
                "sr5-after-run-settlement",
                "chummer.android.sr5-after-run-settlement-hosted-e2e/v1",
            ),
            GATE.journey_map()["after-run-settlement"],
        )

    def test_finalizer_rejects_mismatched_apk_sha_and_attempt_name(self) -> None:
        receipt = self.raw_receipt("career-active-skill-advance")
        receipt["apkSha256"] = "c" * 64
        with self.assertRaisesRegex(ValueError, "APK SHA-256 differs"):
            FINALIZE.bind_receipt(
                receipt,
                matrix_journey="career-active-skill-advance",
                driver_journey="career-active-skill-advance",
                **self.authority(),
            )
        with self.assertRaisesRegex(ValueError, "not bound to run and attempt"):
            FINALIZE.bind_receipt(
                self.raw_receipt("career-active-skill-advance"),
                matrix_journey="career-active-skill-advance",
                driver_journey="career-active-skill-advance",
                **self.authority(artifact_name="stale-snapshot"),
            )

    def test_contextual_journeys_cannot_authorize_publication(self) -> None:
        for matrix_journey in (
            "before-run-edge",
            "playtime-short-burst",
            "downtime-calendar",
            "after-run-settlement",
        ):
            with self.subTest(matrix_journey=matrix_journey):
                receipt = self.raw_receipt(matrix_journey)
                receipt["publicationAuthorized"] = True
                with self.assertRaisesRegex(ValueError, "cannot authorize publication"):
                    FINALIZE.bind_receipt(
                        receipt,
                        matrix_journey=matrix_journey,
                        driver_journey=JOURNEYS[matrix_journey],
                        **self.authority(),
                    )

    def test_rerun_failed_keeps_one_stable_receipt_per_journey(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            self.materialize_all(root, attempt="1")
            # A rerun-failed replaces only its stable journey artifact and retains
            # the original build authority used by the successful prerequisite jobs.
            self.materialize_journey(root, "career-weapon-fire", attempt="1")
            aggregate = self.validate(root, attempt="1")
            self.assertEqual(len(JOURNEYS), aggregate["journeyCount"])
            self.assertEqual(len(JOURNEYS), aggregate["requiredJourneyCount"])
            self.assertEqual(list(JOURNEYS), aggregate["requiredJourneys"])
            self.assertEqual(GATE.AGGREGATE_SCHEMA, aggregate["schema"])
            self.assertEqual("sr5_wizards_only", aggregate["proofScope"])
            self.assertFalse(aggregate["publicationAuthorized"])
            self.assertEqual(ARTIFACT_ID, aggregate["artifactAuthority"]["artifactId"])

    def test_rerun_all_replaces_all_stable_evidence_with_new_build_authority(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            self.materialize_all(root, attempt="1")
            self.materialize_all(root, attempt="2")
            aggregate = self.validate(root, attempt="2")
            self.assertEqual(2, aggregate["artifactAuthority"]["artifactAttempt"])

    def test_multiple_attempt_or_differing_snapshot_authority_fails_closed(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            self.materialize_all(root, attempt="2")
            self.materialize_journey(root, "creation-prerequisite", attempt="1")
            with self.assertRaisesRegex(ValueError, "authority differs"):
                self.validate(root, attempt="2")

    def test_mismatched_artifact_id_and_apk_sha_fail_closed(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            self.materialize_all(root)
            directory = root / AGGREGATE.expected_artifact_directory(
                "career-active-skill-advance",
                RUN_ID,
            )
            receipt_path = directory / "receipt.json"
            receipt = json.loads(receipt_path.read_text(encoding="utf-8"))
            receipt["artifactAuthority"]["artifactId"] = "111111"
            receipt_path.write_text(json.dumps(receipt) + "\n", encoding="utf-8")
            self.reseal(directory)
            with self.assertRaisesRegex(ValueError, "authority differs"):
                self.validate(root)

            self.materialize_journey(root, "career-active-skill-advance")
            with self.assertRaisesRegex(ValueError, "execution-started authority differs"):
                self.validate(root, apk_sha256="c" * 64)

    def test_missing_or_expired_artifact_receipt_fails_closed(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            self.materialize_all(root)
            # An expired exact-ID evidence artifact is indistinguishable from
            # a missing download here; cardinality must fail rather than reuse
            # or select another visible run-attempt snapshot.
            shutil.rmtree(
                root
                / AGGREGATE.expected_artifact_directory(
                    "creation-prerequisite",
                    RUN_ID,
                )
            )
            with self.assertRaisesRegex(ValueError, "cardinality/name mismatch"):
                self.validate(root)

    def test_duplicate_journey_receipt_or_artifact_fails_closed(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            self.materialize_all(root)
            weapon = root / AGGREGATE.expected_artifact_directory(
                "career-weapon-fire", RUN_ID
            )
            nested = weapon / "duplicate"
            nested.mkdir()
            shutil.copy2(weapon / "receipt.json", nested / "receipt.json")
            with self.assertRaisesRegex(ValueError, f"exactly {len(JOURNEYS)}"):
                self.validate(root)

            shutil.rmtree(nested)
            (root / f"{weapon.name}-old-attempt").mkdir()
            with self.assertRaisesRegex(ValueError, "cardinality/name mismatch"):
                self.validate(root)

    def test_full_editing_artifact_is_rejected_as_extra_evidence(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            self.materialize_all(root)
            full = root / AGGREGATE.expected_artifact_directory(
                "full-editing", RUN_ID
            )
            full.mkdir()
            with self.assertRaisesRegex(ValueError, "cardinality/name mismatch"):
                self.validate(root)

    def test_aggregate_rejects_publication_claim_in_contextual_receipts(self) -> None:
        for matrix_journey in (
            "before-run-edge",
            "playtime-short-burst",
            "downtime-calendar",
            "after-run-settlement",
        ):
            with self.subTest(matrix_journey=matrix_journey), tempfile.TemporaryDirectory() as temporary:
                root = Path(temporary)
                self.materialize_all(root)
                directory = root / AGGREGATE.expected_artifact_directory(
                    matrix_journey, RUN_ID
                )
                receipt_path = directory / "receipt.json"
                receipt = json.loads(receipt_path.read_text(encoding="utf-8"))
                receipt["publicationAuthorized"] = True
                receipt_path.write_text(json.dumps(receipt) + "\n", encoding="utf-8")
                self.reseal(directory)
                with self.assertRaisesRegex(ValueError, "cannot authorize publication"):
                    self.validate(root)

    def test_duplicate_json_keys_and_failed_matrix_fail_closed(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            self.materialize_all(root)
            weapon = root / AGGREGATE.expected_artifact_directory(
                "career-weapon-fire", RUN_ID
            )
            (weapon / "receipt.json").write_text(
                '{"schema":"x","schema":"y"}\n',
                encoding="utf-8",
            )
            self.reseal(weapon)
            with self.assertRaisesRegex(ValueError, "duplicate JSON key"):
                self.validate(root)

            self.materialize_journey(root, "career-weapon-fire")
            with self.assertRaisesRegex(ValueError, "matrix did not succeed"):
                AGGREGATE.validate_aggregate(
                    root,
                    build_result="success",
                    matrix_result="failure",
                    **self.authority(),
                )

    def test_gate_contract_rejects_missing_extra_full_editing_and_stale_count(self) -> None:
        cases = {}
        missing = GATE.expected_contract()
        missing["requiredJourneys"].pop()
        cases["missing"] = missing
        extra = GATE.expected_contract()
        extra["requiredJourneys"].append(
            {
                "matrixJourney": "career-before-run",
                "driverJourney": "career-before-run",
                "receiptSchema": "chummer.android.editing-e2e/v1",
            }
        )
        cases["extra"] = extra
        full_required = GATE.expected_contract()
        full_required["requiredJourneys"][0] = {
            "matrixJourney": "full-editing",
            "driverJourney": "full",
            "receiptSchema": "chummer.android.editing-e2e/v1",
        }
        cases["full-editing-required"] = full_required
        stale_count = GATE.expected_contract()
        stale_count["requiredJourneyCount"] = len(GATE.REQUIRED_JOURNEY_SPECS) - 1
        cases["stale-journey-count"] = stale_count

        for name, value in cases.items():
            with self.subTest(name=name), self.assertRaisesRegex(
                ValueError,
                f"exact {len(GATE.REQUIRED_JOURNEY_SPECS)}-journey wizard-only authority",
            ):
                GATE.validate_contract(copy.deepcopy(value))

    def test_aggregate_receipt_rejects_stale_five_journey_schema(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            self.materialize_all(root)
            aggregate = self.validate(root)
            gate_authority = GATE.contract_binding()

            stale_schema = copy.deepcopy(aggregate)
            stale_schema["schema"] = (
                "chummer.android.api36-editing-e2e-aggregate/v1"
            )
            with self.assertRaisesRegex(ValueError, "stale or unsupported"):
                AGGREGATE.validate_aggregate_receipt(
                    stale_schema,
                    gate_authority,
                )

            stale_five = copy.deepcopy(aggregate)
            stale_five["requiredJourneyCount"] = len(JOURNEYS) + 1
            stale_five["journeyCount"] = len(JOURNEYS) + 1
            stale_five["requiredJourneys"].append("full-editing")
            stale_five["journeys"]["full-editing"] = {"status": "pass"}
            with self.assertRaisesRegex(ValueError, f"exactly {len(JOURNEYS)}"):
                AGGREGATE.validate_aggregate_receipt(stale_five, gate_authority)

    def test_creation_timing_outside_explicit_budgets_fails_closed(self) -> None:
        cases = (
            ("missingTiming", "timing evidence is missing"),
            ("authorityWithinBudget", "phase timing is outside budget"),
            ("authorityElapsedMs", "phase timing is outside budget"),
            ("dashboardWithinBudget", "phase timing is outside budget"),
            ("dashboardElapsedMs", "phase timing is outside budget"),
            ("withinConfiguredTotalTarget", "total timing target was exceeded"),
            ("totalElapsedMs", "total timing target was exceeded"),
        )
        for field, expected_error in cases:
            with self.subTest(field=field), tempfile.TemporaryDirectory() as temporary:
                root = Path(temporary)
                self.materialize_all(root)
                directory = root / AGGREGATE.expected_artifact_directory(
                    "creation-prerequisite",
                    RUN_ID,
                )
                receipt_path = directory / "receipt.json"
                receipt = json.loads(receipt_path.read_text(encoding="utf-8"))
                if field == "missingTiming":
                    del receipt["timing"]
                elif field == "withinConfiguredTotalTarget":
                    receipt["timing"][field] = False
                elif field == "totalElapsedMs":
                    receipt["timing"][field] = AGGREGATE.CREATION_TOTAL_TARGET_MS + 1
                elif field == "authorityWithinBudget":
                    receipt["timing"]["phases"][2]["withinBudget"] = False
                elif field == "dashboardWithinBudget":
                    receipt["timing"]["phases"][3]["withinBudget"] = False
                elif field == "dashboardElapsedMs":
                    receipt["timing"]["phases"][3]["elapsedMs"] = (
                        AGGREGATE.CREATION_PHASE_BUDGETS_MS["dashboard-proof"] + 1
                    )
                else:
                    receipt["timing"]["phases"][2]["elapsedMs"] = (
                        AGGREGATE.CREATION_PHASE_BUDGETS_MS["initial-authority"] + 1
                    )
                receipt_path.write_text(json.dumps(receipt) + "\n", encoding="utf-8")
                self.reseal(directory)

                with self.assertRaisesRegex(ValueError, expected_error):
                    self.validate(root)

    def test_creation_timing_uses_the_exact_current_thirty_three_phase_map(self) -> None:
        expected = {
            "device-preflight-install": 180_000,
            "initial-navigation": 60_000,
            "initial-authority": 90_000,
            "dashboard-proof": 30_000,
            "dashboard-authority-inventory": 30_000,
            "advanced-editor-gate-inventory": 90_000,
            "prerequisite-authority-inventory": 120_000,
            "priority-ranks": 150_000,
            "typed-authority-options": 150_000,
            "talent-active-skill-grant": 180_000,
            "talent-active-skill-preservation": 150_000,
            "talent-active-skill-reset": 150_000,
            "talent-active-skill-reselection": 150_000,
            "talent-active-grant-completion": 180_000,
            "talent-active-preview": 150_000,
            "talent-skill-group-selection": 150_000,
            "talent-skill-group-grant": 180_000,
            "talent-skill-group-preservation": 150_000,
            "talent-skill-group-reset": 150_000,
            "talent-skill-group-reselection": 150_000,
            "talent-skill-group-grant-completion": 180_000,
            "preview-confirm": 360_000,
            "same-process-reopen": 90_000,
            "same-process-authority-options": 120_000,
            "same-process-restored-talent-grant": 90_000,
            "resources-initial-authority": 180_000,
            "resources-preview-confirm": 240_000,
            "resources-same-process-reopen": 120_000,
            "resources-prerequisite-rebind": 180_000,
            "process-restart-reopen": 240_000,
            "process-restart-authority-options": 120_000,
            "process-restart-restored-talent-grant": 90_000,
            "process-restart-resources": 120_000,
        }
        self.assertEqual(expected, AGGREGATE.CREATION_PHASE_BUDGETS_MS)
        self.assertEqual(
            tuple(expected),
            tuple(CREATION_PROSPECTIVE_PHASE_ELAPSED_MS),
        )
        self.assertEqual(17, AGGREGATE.CREATION_TIMING_ROUNDING_TOLERANCE_MS)
        self.assertEqual(
            1_268_512,
            sum(CREATION_PROSPECTIVE_PHASE_ELAPSED_MS.values()),
        )

    def test_prerequisite_observer_budget_rejects_upper_bound_tampering(self) -> None:
        phase_id = "prerequisite-authority-inventory"
        self.assertEqual(
            120_000,
            AGGREGATE.CREATION_PHASE_BUDGETS_MS[phase_id],
        )
        self.assertEqual(45 * 60 * 1000, AGGREGATE.CREATION_TOTAL_TARGET_MS)

        timing = json.loads(json.dumps(
            self.raw_receipt("creation-prerequisite")["timing"]
        ))
        timing["phaseBudgetsMs"][phase_id] += 1
        phase = next(
            item for item in timing["phases"] if item["phaseId"] == phase_id
        )
        phase["budgetMs"] += 1
        with self.assertRaisesRegex(
            ValueError,
            "creation prerequisite phase timing budgets differ",
        ):
            AGGREGATE.require_creation_timing_within_budget(
                creation_receipt_with_timing(timing)
            )

    def test_same_process_observer_reserves_do_not_widen_journey_target(self) -> None:
        self.assertEqual(
            180_000,
            AGGREGATE.CREATION_PHASE_BUDGETS_MS["resources-initial-authority"],
        )
        self.assertEqual(
            120_000,
            AGGREGATE.CREATION_PHASE_BUDGETS_MS[
                "same-process-authority-options"
            ],
        )
        self.assertEqual(
            90_000,
            AGGREGATE.CREATION_PHASE_BUDGETS_MS[
                "same-process-restored-talent-grant"
            ],
        )
        self.assertEqual(
            AGGREGATE.CREATION_PHASE_BUDGETS_MS[
                "process-restart-restored-talent-grant"
            ],
            AGGREGATE.CREATION_PHASE_BUDGETS_MS[
                "same-process-restored-talent-grant"
            ],
        )
        self.assertEqual(45 * 60 * 1000, AGGREGATE.CREATION_TOTAL_TARGET_MS)

    def test_each_new_authority_phase_accepts_exact_limit_and_rejects_plus_one(
        self,
    ) -> None:
        for phase_id in (
            "dashboard-authority-inventory",
            "advanced-editor-gate-inventory",
            "prerequisite-authority-inventory",
            "talent-skill-group-preservation",
            "talent-skill-group-reset",
            "talent-skill-group-reselection",
            "talent-skill-group-grant-completion",
            "same-process-restored-talent-grant",
            "same-process-authority-options",
            "resources-initial-authority",
            "resources-preview-confirm",
            "resources-same-process-reopen",
            "resources-prerequisite-rebind",
            "process-restart-restored-talent-grant",
        ):
            with self.subTest(phase_id=phase_id, boundary="exact"):
                timing = json.loads(json.dumps(
                    self.raw_receipt("creation-prerequisite")["timing"]
                ))
                phase = next(
                    phase
                    for phase in timing["phases"]
                    if phase["phaseId"] == phase_id
                )
                phase["elapsedMs"] = phase["budgetMs"]
                timing["totalElapsedMs"] = sum(
                    item["elapsedMs"] for item in timing["phases"]
                )
                AGGREGATE.require_creation_timing_within_budget(creation_receipt_with_timing(timing))

            with self.subTest(phase_id=phase_id, boundary="plus-one"):
                timing = json.loads(json.dumps(
                    self.raw_receipt("creation-prerequisite")["timing"]
                ))
                phase = next(
                    phase
                    for phase in timing["phases"]
                    if phase["phaseId"] == phase_id
                )
                phase["elapsedMs"] = phase["budgetMs"] + 1
                timing["totalElapsedMs"] = sum(
                    item["elapsedMs"] for item in timing["phases"]
                )
                with self.assertRaisesRegex(ValueError, phase_id):
                    AGGREGATE.require_creation_timing_within_budget(
                        creation_receipt_with_timing(timing)
                    )

    def test_creation_timing_requires_exactly_one_method_reacquisition_scan(
        self,
    ) -> None:
        for case in ("omitted", "duplicated"):
            with self.subTest(case=case):
                timing = json.loads(json.dumps(
                    self.raw_receipt("creation-prerequisite")["timing"]
                ))
                method = next(
                    scan
                    for scan in timing["scans"]
                    if scan.get("scanId")
                    == AGGREGATE.CREATION_METHOD_REACQUISITION_SCAN_ID
                )
                if case == "omitted":
                    timing["scans"].remove(method)
                else:
                    timing["scans"].append(dict(method))
                with self.assertRaisesRegex(ValueError, "scan cardinality differs"):
                    AGGREGATE.require_creation_timing_within_budget(
                        creation_receipt_with_timing(timing)
                    )

    def test_creation_timing_rejects_forged_method_reacquisition_authority(
        self,
    ) -> None:
        cases = {
            "status": "failed",
            "phaseId": "dashboard-authority-inventory",
            "direction": "up",
            "distanceRatio": 0.61,
            "configuredMaxScrolls": 19,
            "stableRepeats": 3,
            "maximumEmptyHierarchyReads": 4,
            "maximumSystemUiDismissals": 4,
            "deadlineEnforced": False,
            "phaseBudgetMs": 90_001,
        }
        for field, forged in cases.items():
            with self.subTest(field=field):
                timing = json.loads(json.dumps(
                    self.raw_receipt("creation-prerequisite")["timing"]
                ))
                method = next(
                    scan
                    for scan in timing["scans"]
                    if scan.get("scanId")
                    == AGGREGATE.CREATION_METHOD_REACQUISITION_SCAN_ID
                )
                method[field] = forged
                with self.assertRaisesRegex(ValueError, "scan authority differs"):
                    AGGREGATE.require_creation_timing_within_budget(
                        creation_receipt_with_timing(timing)
                    )

        timing = json.loads(json.dumps(
            self.raw_receipt("creation-prerequisite")["timing"]
        ))
        method = next(
            scan
            for scan in timing["scans"]
            if scan.get("scanId")
            == AGGREGATE.CREATION_METHOD_REACQUISITION_SCAN_ID
        )
        method["deadlineEnforced"] = 1
        with self.assertRaisesRegex(ValueError, "JSON boolean true"):
            AGGREGATE.require_creation_timing_within_budget(creation_receipt_with_timing(timing))

    def test_creation_timing_rejects_omitted_method_reacquisition_fields(
        self,
    ) -> None:
        for field in ("direction", "deadlineEnforced", "hierarchyReadCount"):
            with self.subTest(field=field):
                timing = json.loads(json.dumps(
                    self.raw_receipt("creation-prerequisite")["timing"]
                ))
                method = next(
                    scan
                    for scan in timing["scans"]
                    if scan.get("scanId")
                    == AGGREGATE.CREATION_METHOD_REACQUISITION_SCAN_ID
                )
                method.pop(field)
                with self.assertRaisesRegex(
                    ValueError,
                    "authority differs|timing/count data differs",
                ):
                    AGGREGATE.require_creation_timing_within_budget(
                        creation_receipt_with_timing(timing)
                    )

    def test_creation_timing_rejects_forged_method_reacquisition_relationships(
        self,
    ) -> None:
        cases = {
            "screens": 7,
            "swipes": 19,
            "emptyHierarchyReads": 4,
            "systemUiDismissals": 4,
            "hierarchyReadCount": 7,
            "hierarchyElapsedMs": 5_005,
            "maximumHierarchyReadMs": 499,
            "elapsedMs": (
                CREATION_PROSPECTIVE_PHASE_ELAPSED_MS[
                    "advanced-editor-gate-inventory"
                ]
                + 1
            ),
            "mandatory-wait-elapsedMs": 4_000,
        }
        for field, forged in cases.items():
            with self.subTest(field=field):
                timing = json.loads(json.dumps(
                    self.raw_receipt("creation-prerequisite")["timing"]
                ))
                method = next(
                    scan
                    for scan in timing["scans"]
                    if scan.get("scanId")
                    == AGGREGATE.CREATION_METHOD_REACQUISITION_SCAN_ID
                )
                target_field = (
                    "elapsedMs"
                    if field == "mandatory-wait-elapsedMs"
                    else field
                )
                method[target_field] = forged
                with self.assertRaisesRegex(
                    ValueError,
                    "did not reconcile|timing/count data differs",
                ):
                    AGGREGATE.require_creation_timing_within_budget(
                        creation_receipt_with_timing(timing)
                    )

    def test_creation_timing_requires_exactly_one_confirmed_receipt_back_reacquisition_scan(
        self,
    ) -> None:
        for case in ("omitted", "duplicated"):
            with self.subTest(case=case):
                timing = json.loads(json.dumps(
                    self.raw_receipt("creation-prerequisite")["timing"]
                ))
                scan = next(
                    scan
                    for scan in timing["scans"]
                    if scan.get("scanId")
                    == AGGREGATE.CONFIRMED_RECEIPT_BACK_REACQUISITION_SCAN_ID
                )
                if case == "omitted":
                    timing["scans"].remove(scan)
                else:
                    timing["scans"].append(dict(scan))
                with self.assertRaisesRegex(
                    ValueError,
                    "confirmed-receipt Back reacquisition scan cardinality differs",
                ):
                    AGGREGATE.require_creation_timing_within_budget(
                        creation_receipt_with_timing(timing)
                    )

    def test_creation_timing_requires_exactly_one_confirmed_receipt_scan(
        self,
    ) -> None:
        for case in ("omitted", "duplicated"):
            with self.subTest(case=case):
                timing = json.loads(json.dumps(
                    self.raw_receipt("creation-prerequisite")["timing"]
                ))
                scan = next(
                    scan
                    for scan in timing["scans"]
                    if scan.get("scanId")
                    == "creation-prerequisite-confirmed-receipt"
                )
                if case == "omitted":
                    timing["scans"].remove(scan)
                else:
                    timing["scans"].append(dict(scan))
                with self.assertRaisesRegex(
                    ValueError,
                    "confirmed-receipt.*cardinality differs",
                ):
                    AGGREGATE.require_creation_timing_within_budget(
                        creation_receipt_with_timing(timing)
                    )

    def test_creation_timing_requires_exactly_one_post_confirm_route_ready_scan(
        self,
    ) -> None:
        for case in ("omitted", "duplicated"):
            with self.subTest(case=case):
                timing = json.loads(json.dumps(
                    self.raw_receipt("creation-prerequisite")["timing"]
                ))
                scan = next(
                    scan
                    for scan in timing["scans"]
                    if scan.get("scanId")
                    == AGGREGATE.POST_CONFIRM_DASHBOARD_ROUTE_READY_SCAN_ID
                )
                if case == "omitted":
                    timing["scans"].remove(scan)
                else:
                    timing["scans"].append(dict(scan))
                with self.assertRaisesRegex(
                    ValueError,
                    "post-confirm dashboard route-ready scan cardinality differs",
                ):
                    AGGREGATE.require_creation_timing_within_budget(
                        creation_receipt_with_timing(timing)
                    )

    def test_creation_timing_rejects_forged_post_confirm_route_ready_authority(
        self,
    ) -> None:
        cases: dict[str, object] = {
            "status": "pass",
            "phaseId": "same-process-reopen",
            "observationMode": "uncleared-log",
            "readAttemptMaxMs": 5_001,
            "pollDelayMs": 251,
            "deadlineEnforced": False,
            "maximumElapsedMs": 30_001,
        }
        for field, forged in cases.items():
            with self.subTest(field=field):
                timing = json.loads(json.dumps(
                    self.raw_receipt("creation-prerequisite")["timing"]
                ))
                scan = next(
                    scan
                    for scan in timing["scans"]
                    if scan.get("scanId")
                    == AGGREGATE.POST_CONFIRM_DASHBOARD_ROUTE_READY_SCAN_ID
                )
                scan[field] = forged
                with self.assertRaisesRegex(
                    ValueError,
                    "post-confirm dashboard route-ready authority differs",
                ):
                    AGGREGATE.require_creation_timing_within_budget(
                        creation_receipt_with_timing(timing)
                    )

    def test_creation_timing_rejects_forged_post_confirm_route_ready_binding(
        self,
    ) -> None:
        cases: dict[str, object] = {
            "observedContentRevision": 3,
            "observedSavedRevision": 3,
            "workspaceId": " ",
            "snapshotDigest": "not-a-digest",
            "emptySnapshotCount": 2,
            "maximumLogcatReadMs": 501,
            "elapsedMs": 30_002,
        }
        for field, forged in cases.items():
            with self.subTest(field=field):
                timing = json.loads(json.dumps(
                    self.raw_receipt("creation-prerequisite")["timing"]
                ))
                scan = next(
                    scan
                    for scan in timing["scans"]
                    if scan.get("scanId")
                    == AGGREGATE.POST_CONFIRM_DASHBOARD_ROUTE_READY_SCAN_ID
                )
                scan[field] = forged
                with self.assertRaisesRegex(
                    ValueError,
                    "post-confirm dashboard route-ready scan did not reconcile",
                ):
                    AGGREGATE.require_creation_timing_within_budget(
                        creation_receipt_with_timing(timing)
                    )

    def test_creation_timing_binds_post_confirm_expected_revisions_to_receipt(
        self,
    ) -> None:
        receipt = json.loads(json.dumps(
            self.raw_receipt("creation-prerequisite")
        ))
        scan = next(
            scan
            for scan in receipt["timing"]["scans"]
            if scan.get("scanId")
            == AGGREGATE.POST_CONFIRM_DASHBOARD_ROUTE_READY_SCAN_ID
        )
        scan["expectedContentRevision"] = 3
        scan["observedContentRevision"] = 3
        scan["expectedSavedRevision"] = 3
        scan["observedSavedRevision"] = 3
        with self.assertRaisesRegex(
            ValueError,
            "post-confirm dashboard route-ready scan did not reconcile",
        ):
            AGGREGATE.require_creation_timing_within_budget(receipt)

    def test_creation_timing_uses_count_derived_snapshot_rounding_allowance(
        self,
    ) -> None:
        receipt = json.loads(json.dumps(
            self.raw_receipt("creation-prerequisite")
        ))
        scan = next(
            candidate
            for candidate in receipt["timing"]["scans"]
            if candidate.get("scanId")
            == AGGREGATE.POST_CONFIRM_DASHBOARD_ROUTE_READY_SCAN_ID
        )
        scan.update({
            "logcatReadCount": 3,
            "emptySnapshotCount": 2,
            "logcatElapsedMs": 3,
            "maximumLogcatReadMs": 1,
            "elapsedMs": 500,
        })
        AGGREGATE.require_creation_timing_within_budget(receipt)

        scan["elapsedMs"] = 499
        with self.assertRaisesRegex(
            ValueError,
            "post-confirm dashboard route-ready scan did not reconcile",
        ):
            AGGREGATE.require_creation_timing_within_budget(receipt)

    def test_creation_timing_rejects_too_small_snapshot_read_maximum(self) -> None:
        receipt = json.loads(json.dumps(
            self.raw_receipt("creation-prerequisite")
        ))
        scan = next(
            candidate
            for candidate in receipt["timing"]["scans"]
            if candidate.get("scanId")
            == AGGREGATE.POST_CONFIRM_DASHBOARD_ROUTE_READY_SCAN_ID
        )
        scan["maximumLogcatReadMs"] = 249
        with self.assertRaisesRegex(
            ValueError,
            "post-confirm dashboard route-ready scan did not reconcile",
        ):
            AGGREGATE.require_creation_timing_within_budget(receipt)

    def test_creation_timing_requires_exact_confirmed_revision_authority(self) -> None:
        for case, mutate in (
            (
                "missing",
                lambda receipt: receipt.pop("journeys"),
            ),
            (
                "boolean-content",
                lambda receipt: receipt["journeys"]["confirmedRevisions"].__setitem__(
                    "contentRevision", True
                ),
            ),
            (
                "float-saved",
                lambda receipt: receipt["journeys"]["confirmedRevisions"].__setitem__(
                    "savedRevision", 2.0
                ),
            ),
        ):
            with self.subTest(case=case):
                receipt = json.loads(json.dumps(
                    self.raw_receipt("creation-prerequisite")
                ))
                mutate(receipt)
                with self.assertRaisesRegex(
                    ValueError,
                    "confirmed revisions are missing|confirmed revisions are invalid",
                ):
                    AGGREGATE.require_creation_timing_within_budget(receipt)

    def test_creation_timing_rejects_boolean_post_confirm_route_ready_integer(
        self,
    ) -> None:
        timing = json.loads(json.dumps(
            self.raw_receipt("creation-prerequisite")["timing"]
        ))
        scan = next(
            scan
            for scan in timing["scans"]
            if scan.get("scanId")
            == AGGREGATE.POST_CONFIRM_DASHBOARD_ROUTE_READY_SCAN_ID
        )
        scan["observedContentRevision"] = True
        with self.assertRaisesRegex(
            ValueError,
            "post-confirm dashboard route-ready timing/revision data differs",
        ):
            AGGREGATE.require_creation_timing_within_budget(creation_receipt_with_timing(timing))

    def test_creation_timing_rejects_forged_confirmed_receipt_scan_authority(
        self,
    ) -> None:
        for field, forged in (
            ("status", "pass"),
            ("phaseId", "same-process-reopen"),
        ):
            with self.subTest(field=field):
                timing = json.loads(json.dumps(
                    self.raw_receipt("creation-prerequisite")["timing"]
                ))
                scan = next(
                    scan
                    for scan in timing["scans"]
                    if scan.get("scanId")
                    == "creation-prerequisite-confirmed-receipt"
                )
                scan[field] = forged
                with self.assertRaisesRegex(
                    ValueError,
                    "confirmed-receipt.*differs",
                ):
                    AGGREGATE.require_creation_timing_within_budget(
                        creation_receipt_with_timing(timing)
                    )

    def test_creation_timing_rejects_invalid_confirmed_receipt_scan_swipes(
        self,
    ) -> None:
        for case, forged in (
            ("boolean", True),
            ("negative", -1),
            ("aboveBound", 13),
        ):
            with self.subTest(case=case):
                timing = json.loads(json.dumps(
                    self.raw_receipt("creation-prerequisite")["timing"]
                ))
                scan = next(
                    scan
                    for scan in timing["scans"]
                    if scan.get("scanId")
                    == "creation-prerequisite-confirmed-receipt"
                )
                scan["swipes"] = forged
                with self.assertRaisesRegex(
                    ValueError,
                    "confirmed-receipt.*swipes",
                ):
                    AGGREGATE.require_creation_timing_within_budget(
                        creation_receipt_with_timing(timing)
                    )

    def test_creation_timing_rejects_confirmed_receipt_cross_scan_mismatch(
        self,
    ) -> None:
        timing = json.loads(json.dumps(
            self.raw_receipt("creation-prerequisite")["timing"]
        ))
        reacquisition = next(
            scan
            for scan in timing["scans"]
            if scan.get("scanId")
            == AGGREGATE.CONFIRMED_RECEIPT_BACK_REACQUISITION_SCAN_ID
        )
        reacquisition["configuredMaxScrolls"] = 3
        with self.assertRaisesRegex(
            ValueError,
            "confirmed-receipt Back configuredMaxScrolls differs.*confirmed-receipt swipes",
        ):
            AGGREGATE.require_creation_timing_within_budget(creation_receipt_with_timing(timing))

    def test_creation_timing_rejects_forged_confirmed_receipt_back_authority(
        self,
    ) -> None:
        cases = {
            "status": "failed",
            "phaseId": "same-process-reopen",
            "direction": "backward",
            "distanceRatio": 0.31,
            "deadlineEnforced": False,
            "maximumEmptyHierarchyReads": 4,
            "maximumSystemUiDismissals": 4,
            "downstreamReserveMs": 81_001,
        }
        for field, forged in cases.items():
            with self.subTest(field=field):
                timing = json.loads(json.dumps(
                    self.raw_receipt("creation-prerequisite")["timing"]
                ))
                scan = next(
                    scan
                    for scan in timing["scans"]
                    if scan.get("scanId")
                    == AGGREGATE.CONFIRMED_RECEIPT_BACK_REACQUISITION_SCAN_ID
                )
                scan[field] = forged
                with self.assertRaisesRegex(
                    ValueError,
                    "confirmed-receipt Back reacquisition authority differs",
                ):
                    AGGREGATE.require_creation_timing_within_budget(
                        creation_receipt_with_timing(timing)
                    )

        timing = json.loads(json.dumps(
            self.raw_receipt("creation-prerequisite")["timing"]
        ))
        scan = next(
            scan
            for scan in timing["scans"]
            if scan.get("scanId")
            == AGGREGATE.CONFIRMED_RECEIPT_BACK_REACQUISITION_SCAN_ID
        )
        scan["deadlineEnforced"] = 1
        with self.assertRaisesRegex(
            ValueError,
            "deadlineEnforced must be the JSON boolean true",
        ):
            AGGREGATE.require_creation_timing_within_budget(creation_receipt_with_timing(timing))

    def test_creation_timing_rejects_non_integer_confirmed_receipt_back_counts(
        self,
    ) -> None:
        for field in (
            "screens",
            "swipes",
            "configuredMaxScrolls",
            "emptyHierarchyReads",
            "systemUiDismissals",
            "hierarchyReadCount",
            "hierarchyElapsedMs",
            "maximumHierarchyReadMs",
            "maximumElapsedMs",
            "elapsedMs",
        ):
            with self.subTest(field=field):
                timing = json.loads(json.dumps(
                    self.raw_receipt("creation-prerequisite")["timing"]
                ))
                scan = next(
                    scan
                    for scan in timing["scans"]
                    if scan.get("scanId")
                    == AGGREGATE.CONFIRMED_RECEIPT_BACK_REACQUISITION_SCAN_ID
                )
                scan[field] = True
                with self.assertRaisesRegex(
                    ValueError,
                    "confirmed-receipt Back reacquisition timing/count data differs",
                ):
                    AGGREGATE.require_creation_timing_within_budget(
                        creation_receipt_with_timing(timing)
                    )

    def test_creation_timing_rejects_confirmed_receipt_back_bound_forgery(
        self,
    ) -> None:
        cases = {
            "swipesBeyondConfigured": {"configuredMaxScrolls": 1},
            "noScreen": {"screens": 0},
            "emptyHierarchyBound": {"emptyHierarchyReads": 4},
            "systemUiBound": {"systemUiDismissals": 4},
            "hierarchyCardinality": {"hierarchyReadCount": 4},
            "gestureCardinality": {"screens": 4},
            "maximumReadBelowAverage": {"maximumHierarchyReadMs": 99},
            "maximumReadAboveTotal": {"maximumHierarchyReadMs": 301},
        }
        for case, mutations in cases.items():
            with self.subTest(case=case):
                timing = json.loads(json.dumps(
                    self.raw_receipt("creation-prerequisite")["timing"]
                ))
                scan = next(
                    scan
                    for scan in timing["scans"]
                    if scan.get("scanId")
                    == AGGREGATE.CONFIRMED_RECEIPT_BACK_REACQUISITION_SCAN_ID
                )
                scan.update(mutations)
                if case == "swipesBeyondConfigured":
                    source = next(
                        source
                        for source in timing["scans"]
                        if source.get("scanId")
                        == "creation-prerequisite-confirmed-receipt"
                    )
                    source["swipes"] = scan["configuredMaxScrolls"]
                with self.assertRaisesRegex(
                    ValueError,
                    "confirmed-receipt Back reacquisition scan did not reconcile",
                ):
                    AGGREGATE.require_creation_timing_within_budget(
                        creation_receipt_with_timing(timing)
                    )

    def test_creation_timing_rejects_confirmed_receipt_back_timing_forgery(
        self,
    ) -> None:
        cases = {
            "hierarchyBeyondElapsed": {
                "hierarchyElapsedMs": 704,
                "maximumHierarchyReadMs": 235,
            },
            "mandatoryWaitMissing": {"elapsedMs": 696},
            "maximumElapsedZero": {"maximumElapsedMs": 0},
            "maximumElapsedBeyondAuthority": {"maximumElapsedMs": 45_001},
            "elapsedBeyondReceiptMaximum": {"maximumElapsedMs": 698},
            "elapsedBeyondPhase": {"elapsedMs": 8_001},
        }
        for case, mutations in cases.items():
            with self.subTest(case=case):
                timing = json.loads(json.dumps(
                    self.raw_receipt("creation-prerequisite")["timing"]
                ))
                scan = next(
                    scan
                    for scan in timing["scans"]
                    if scan.get("scanId")
                    == AGGREGATE.CONFIRMED_RECEIPT_BACK_REACQUISITION_SCAN_ID
                )
                scan.update(mutations)
                with self.assertRaisesRegex(
                    ValueError,
                    "confirmed-receipt Back reacquisition scan did not reconcile",
                ):
                    AGGREGATE.require_creation_timing_within_budget(
                        creation_receipt_with_timing(timing)
                    )

    def test_creation_timing_accepts_confirmed_receipt_back_exact_bounds(
        self,
    ) -> None:
        timing = json.loads(json.dumps(
            self.raw_receipt("creation-prerequisite")["timing"]
        ))
        scan = next(
            scan
            for scan in timing["scans"]
            if scan.get("scanId")
            == AGGREGATE.CONFIRMED_RECEIPT_BACK_REACQUISITION_SCAN_ID
        )
        scan.update(
            {
                "screens": 13,
                "swipes": 12,
                "configuredMaxScrolls": 12,
                "hierarchyReadCount": 13,
                "hierarchyElapsedMs": 1_300,
                "maximumHierarchyReadMs": 100,
                "maximumElapsedMs": 3_699,
                "elapsedMs": 3_700,
            }
        )
        source = next(
            source
            for source in timing["scans"]
            if source.get("scanId")
            == "creation-prerequisite-confirmed-receipt"
        )
        source["swipes"] = scan["configuredMaxScrolls"]
        source["screens"] = source["swipes"] + 1
        AGGREGATE.require_creation_timing_within_budget(creation_receipt_with_timing(timing))

        scan.update(
            {
                "screens": 4,
                "swipes": 0,
                "emptyHierarchyReads": 3,
                "systemUiDismissals": 3,
                "hierarchyReadCount": 7,
                "hierarchyElapsedMs": 700,
                "maximumHierarchyReadMs": 100,
                "maximumElapsedMs": 7_299,
                "elapsedMs": 7_300,
            }
        )
        AGGREGATE.require_creation_timing_within_budget(creation_receipt_with_timing(timing))

        maximum_elapsed = confirmed_receipt_back_reacquisition_scan()
        maximum_elapsed.update(
            {
                "screens": 1,
                "swipes": 0,
                "configuredMaxScrolls": 0,
                "hierarchyReadCount": 1,
                "hierarchyElapsedMs": 45_001,
                "maximumHierarchyReadMs": 45_001,
                "maximumElapsedMs": 45_000,
                "elapsedMs": 45_001,
            }
        )
        AGGREGATE.require_confirmed_receipt_back_reacquisition_scan(
            {"scans": [confirmed_receipt_scan(0), maximum_elapsed]},
            preview_phase_elapsed_ms=45_001,
        )

    def test_creation_timing_rejects_adversarial_phase_topology(self) -> None:
        cases = (
            "missing",
            "duplicate",
            "reordered",
            "extra",
            "wrongBudget",
            "wrongOrdinal",
            "boolElapsed",
        )
        for case in cases:
            with self.subTest(case=case):
                timing = json.loads(json.dumps(
                    self.raw_receipt("creation-prerequisite")["timing"]
                ))
                phases = timing["phases"]
                dashboard_index = tuple(AGGREGATE.CREATION_PHASE_BUDGETS_MS).index(
                    "dashboard-authority-inventory"
                )
                advanced_index = tuple(AGGREGATE.CREATION_PHASE_BUDGETS_MS).index(
                    "advanced-editor-gate-inventory"
                )
                if case == "missing":
                    phases.pop(dashboard_index)
                elif case == "duplicate":
                    phases.insert(dashboard_index, dict(phases[dashboard_index]))
                elif case == "reordered":
                    phases[dashboard_index], phases[advanced_index] = (
                        phases[advanced_index],
                        phases[dashboard_index],
                    )
                elif case == "extra":
                    phases.append(dict(phases[-1]))
                elif case == "wrongBudget":
                    phases[advanced_index]["budgetMs"] += 1
                elif case == "wrongOrdinal":
                    phases[advanced_index]["ordinal"] = 99
                else:
                    phases[advanced_index]["elapsedMs"] = True

                with self.assertRaisesRegex(
                    ValueError,
                    "phase timing|phase cardinality",
                ):
                    AGGREGATE.require_creation_timing_within_budget(
                        creation_receipt_with_timing(timing)
                    )

    def test_creation_timing_requires_exact_grant_completion_phases(self) -> None:
        cases = ("missing", "duplicate", "reordered", "wrongBudget")
        completion_phases = (
            "talent-active-grant-completion",
            "talent-skill-group-grant-completion",
        )
        for completion_phase in completion_phases:
            for case in cases:
                with self.subTest(completion_phase=completion_phase, case=case):
                    timing = json.loads(json.dumps(
                        self.raw_receipt("creation-prerequisite")["timing"]
                    ))
                    phases = timing["phases"]
                    completion_index = tuple(
                        AGGREGATE.CREATION_PHASE_BUDGETS_MS
                    ).index(completion_phase)
                    if case == "missing":
                        phases.pop(completion_index)
                    elif case == "duplicate":
                        phases.insert(
                            completion_index,
                            dict(phases[completion_index]),
                        )
                    elif case == "reordered":
                        phases[completion_index], phases[completion_index + 1] = (
                            phases[completion_index + 1],
                            phases[completion_index],
                        )
                    else:
                        phases[completion_index]["budgetMs"] += 1

                    with self.assertRaisesRegex(
                        ValueError,
                        "phase timing|phase cardinality",
                    ):
                        AGGREGATE.require_creation_timing_within_budget(
                            creation_receipt_with_timing(timing)
                        )

    def test_creation_timing_rejects_legacy_phase_maps(
        self,
    ) -> None:
        current_items = tuple(AGGREGATE.CREATION_PHASE_BUDGETS_MS.items())
        for phase_count in (11, 14, 16, 19, 20, 21, 25, 28, 30):
            legacy_map = dict(current_items[:phase_count])
            self.assertEqual(phase_count, len(legacy_map))
            with self.subTest(phase_count=phase_count):
                timing = json.loads(json.dumps(
                    self.raw_receipt("creation-prerequisite")["timing"]
                ))
                timing["phaseBudgetsMs"] = legacy_map
                with self.assertRaisesRegex(ValueError, "phase timing budgets differ"):
                    AGGREGATE.require_creation_timing_within_budget(
                        creation_receipt_with_timing(timing)
                    )

    def test_creation_timing_reconciles_phase_sum_with_exact_seventeen_ms_tolerance(
        self,
    ) -> None:
        for offset in (-17, 17):
            with self.subTest(offset=offset, accepted=True):
                timing = json.loads(json.dumps(
                    self.raw_receipt("creation-prerequisite")["timing"]
                ))
                phase_sum = sum(phase["elapsedMs"] for phase in timing["phases"])
                timing["totalElapsedMs"] = phase_sum + offset
                AGGREGATE.require_creation_timing_within_budget(creation_receipt_with_timing(timing))

        for offset in (-18, 18):
            with self.subTest(offset=offset, accepted=False):
                timing = json.loads(json.dumps(
                    self.raw_receipt("creation-prerequisite")["timing"]
                ))
                phase_sum = sum(phase["elapsedMs"] for phase in timing["phases"])
                timing["totalElapsedMs"] = phase_sum + offset
                with self.assertRaisesRegex(ValueError, "does not reconcile"):
                    AGGREGATE.require_creation_timing_within_budget(
                        creation_receipt_with_timing(timing)
                    )

    def test_creation_timing_rejects_forged_talent_reacquisition_scans(self) -> None:
        cases = (
            "missingPhase",
            "wrongPhase",
            "wrongNavigationMode",
            "wrongDirection",
            "wrongRatio",
            "wrongStableRepeats",
            "forgedStableBoundary",
            "deadlineDisabled",
            "integerDeadline",
            "duplicateResource",
            "wrongNormalizedTarget",
            "wrongCatalogBound",
            "swipesBeyondBound",
            "booleanCount",
            "hierarchyCardinality",
            "retryAuthority",
            "impossibleTiming",
            "elapsedBeyondPhase",
            "phaseElapsedOvercommitted",
        )
        for case in cases:
            with self.subTest(case=case):
                timing = json.loads(json.dumps(
                    self.raw_receipt("creation-prerequisite")["timing"]
                ))
                scans = [
                    scan
                    for scan in timing["scans"]
                    if isinstance(scan, dict) and "exactResourceIds" in scan
                ]
                target = scans[0]
                if case == "missingPhase":
                    timing["scans"].remove(target)
                elif case == "wrongPhase":
                    target["phaseId"] = "preview-confirm"
                elif case == "wrongNavigationMode":
                    target["navigationMode"] = "invertible-measured-delta"
                elif case == "wrongDirection":
                    target["direction"] = "reverse"
                elif case == "wrongRatio":
                    target["distanceRatio"] = 0.61
                elif case == "wrongStableRepeats":
                    target["stableRepeats"] = 1
                elif case == "forgedStableBoundary":
                    target["stableBoundaryProven"] = True
                elif case == "deadlineDisabled":
                    target["deadlineEnforced"] = False
                elif case == "integerDeadline":
                    target["deadlineEnforced"] = 1
                elif case == "duplicateResource":
                    target["exactResourceIds"] *= 2
                elif case == "wrongNormalizedTarget":
                    target["normalizedTargetViewport"] = 1
                elif case == "wrongCatalogBound":
                    target["configuredMaxScrolls"] = 1
                elif case == "swipesBeyondBound":
                    target["swipes"] = 1
                elif case == "booleanCount":
                    target["hierarchyReadCount"] = True
                elif case == "hierarchyCardinality":
                    target["screens"] = 2
                elif case == "retryAuthority":
                    target["maximumEmptyHierarchyReads"] = 4
                elif case == "impossibleTiming":
                    target["hierarchyElapsedMs"] = 600
                elif case == "elapsedBeyondPhase":
                    target["elapsedMs"] = (
                        AGGREGATE.CREATION_PHASE_BUDGETS_MS[
                            str(target["phaseId"])
                        ]
                        + 1
                    )
                else:
                    duplicate = dict(target)
                    duplicate["scanId"] = (
                        duplicate["scanId"].removesuffix("-reacquisition")
                        + "-duplicate-reacquisition"
                    )
                    duplicate["exactResourceIds"] = [
                        target["exactResourceIds"][0] + "-duplicate"
                    ]
                    duplicate["elapsedMs"] = 7_800
                    timing["scans"].append(duplicate)

                with self.assertRaisesRegex(ValueError, "Talent reacquisition"):
                    AGGREGATE.require_creation_timing_within_budget(
                        creation_receipt_with_timing(timing)
                    )

    def test_creation_timing_accepts_exact_talent_overlap_recovery_scan(self) -> None:
        timing = json.loads(json.dumps(
            self.raw_receipt("creation-prerequisite")["timing"]
        ))
        target_index = next(
            index
            for index, scan in enumerate(timing["scans"])
            if isinstance(scan, dict) and "exactResourceIds" in scan
        )
        phase_id = str(timing["scans"][target_index]["phaseId"])
        timing["scans"][target_index] = talent_overlap_recovery_scan(phase_id)
        AGGREGATE.require_creation_timing_within_budget(creation_receipt_with_timing(timing))

    def test_creation_timing_keeps_coarse_primary_for_non_option_groups(self) -> None:
        timing = json.loads(json.dumps(
            self.raw_receipt("creation-prerequisite")["timing"]
        ))
        target_index = next(
            index
            for index, scan in enumerate(timing["scans"])
            if isinstance(scan, dict) and "exactResourceIds" in scan
        )
        phase_id = str(timing["scans"][target_index]["phaseId"])
        target = talent_reacquisition_scan(phase_id)
        target.update(
            {
                "direction": "forward",
                "targetViewport": 1,
                "normalizedTargetViewport": 1,
                "measuredDelta": 1,
                "configuredMaxScrolls": 40,
                "primaryDirection": "forward",
                "primaryConfiguredMaxScrolls": 40,
                "primaryScreens": 2,
                "primarySwipes": 1,
                "screens": 2,
                "swipes": 1,
                "hierarchyReadCount": 2,
                "hierarchyElapsedMs": 100,
                "maximumHierarchyReadMs": 60,
                "elapsedMs": 400,
            }
        )
        timing["scans"][target_index] = target
        AGGREGATE.require_creation_timing_within_budget(creation_receipt_with_timing(timing))
        for field in ("distanceRatio", "primaryDistanceRatio"):
            with self.subTest(field=field):
                forged = json.loads(json.dumps(timing))
                forged["scans"][target_index][field] = 0.22
                with self.assertRaisesRegex(ValueError, "Talent reacquisition"):
                    AGGREGATE.require_creation_timing_within_budget(
                        creation_receipt_with_timing(forged)
                    )

    def test_creation_timing_rejects_forged_talent_overlap_recovery_fields(
        self,
    ) -> None:
        for field, forged in (
            ("recoveryEligible", False),
            ("recoveryEligible", 1),
            ("recoveryUsed", False),
            ("recoveryUsed", 1),
            ("recoveryDirection", "forward"),
            ("recoveryDistanceRatio", 0.23),
            ("recoveryConfiguredMaxScrolls", 41),
            ("distanceRatio", 0.60),
            ("primaryDirection", "reverse"),
            ("primaryDistanceRatio", 0.60),
            ("primaryDistanceRatio", 0.61),
            ("primaryConfiguredMaxScrolls", 39),
            ("primaryStableBoundaryProven", False),
            ("primaryStableBoundaryProven", 1),
            ("recoveryStableBoundaryProven", True),
            ("recoveryStableBoundaryProven", 0),
            ("primarySwipes", 4),
            ("recoverySwipes", 41),
            ("primaryScreens", 5),
            ("recoveryScreens", 3),
            ("recoveryEmptyHierarchyReads", 4),
            ("recoverySystemUiDismissals", 4),
        ):
            with self.subTest(field=field, forged=forged):
                timing = json.loads(json.dumps(
                    self.raw_receipt("creation-prerequisite")["timing"]
                ))
                target_index = next(
                    index
                    for index, scan in enumerate(timing["scans"])
                    if isinstance(scan, dict) and "exactResourceIds" in scan
                )
                phase_id = str(timing["scans"][target_index]["phaseId"])
                target = talent_overlap_recovery_scan(phase_id)
                target[field] = forged
                timing["scans"][target_index] = target
                with self.assertRaisesRegex(ValueError, "Talent reacquisition"):
                    AGGREGATE.require_creation_timing_within_budget(
                        creation_receipt_with_timing(timing)
                    )

    def test_creation_timing_rejects_reconciled_recovery_without_boundary_gestures(
        self,
    ) -> None:
        for primary_swipes in (0, 1):
            with self.subTest(primary_swipes=primary_swipes):
                timing = json.loads(json.dumps(
                    self.raw_receipt("creation-prerequisite")["timing"]
                ))
                target_index = next(
                    index
                    for index, scan in enumerate(timing["scans"])
                    if isinstance(scan, dict) and "exactResourceIds" in scan
                )
                phase_id = str(timing["scans"][target_index]["phaseId"])
                target = talent_overlap_recovery_scan(phase_id)
                target["primarySwipes"] = primary_swipes
                target["primaryScreens"] = primary_swipes + 1
                target["swipes"] = primary_swipes + int(target["recoverySwipes"])
                target["screens"] = int(target["primaryScreens"]) + int(
                    target["recoveryScreens"]
                )
                target["hierarchyReadCount"] = target["screens"]
                target["maximumHierarchyReadMs"] = 200
                timing["scans"][target_index] = target
                with self.assertRaisesRegex(ValueError, "Talent reacquisition"):
                    AGGREGATE.require_creation_timing_within_budget(
                        creation_receipt_with_timing(timing)
                    )

    def test_creation_timing_rejects_reconciled_recovery_without_opposite_gesture(
        self,
    ) -> None:
        timing = json.loads(json.dumps(
            self.raw_receipt("creation-prerequisite")["timing"]
        ))
        target_index = next(
            index
            for index, scan in enumerate(timing["scans"])
            if isinstance(scan, dict) and "exactResourceIds" in scan
        )
        phase_id = str(timing["scans"][target_index]["phaseId"])
        target = talent_overlap_recovery_scan(phase_id)
        target.update(
            {
                "recoverySwipes": 0,
                "recoveryScreens": 1,
                "recoverySystemUiDismissals": 1,
                "swipes": 3,
                "screens": 5,
                "systemUiDismissals": 1,
                "hierarchyReadCount": 5,
                "elapsedMs": 3_000,
            }
        )
        timing["scans"][target_index] = target
        with self.assertRaisesRegex(ValueError, "Talent reacquisition"):
            AGGREGATE.require_creation_timing_within_budget(creation_receipt_with_timing(timing))

    def test_creation_timing_rejects_overlap_recovery_for_non_option_authority(
        self,
    ) -> None:
        timing = json.loads(json.dumps(
            self.raw_receipt("creation-prerequisite")["timing"]
        ))
        target_index = next(
            index
            for index, scan in enumerate(timing["scans"])
            if isinstance(scan, dict) and "exactResourceIds" in scan
        )
        phase_id = str(timing["scans"][target_index]["phaseId"])
        target = talent_overlap_recovery_scan(phase_id)
        target["exactResourceIds"] = [
            "creation-prerequisite-talent-grant-authority"
        ]
        timing["scans"][target_index] = target
        with self.assertRaisesRegex(ValueError, "Talent reacquisition"):
            AGGREGATE.require_creation_timing_within_budget(creation_receipt_with_timing(timing))

    def test_creation_timing_requires_exact_ordered_milestones(self) -> None:
        cases = (
            "missing",
            "duplicate",
            "reordered",
            "wrongPhase",
            "wrongOrdinal",
        )
        for case in cases:
            with self.subTest(case=case), tempfile.TemporaryDirectory() as temporary:
                root = Path(temporary)
                self.materialize_all(root)
                directory = root / AGGREGATE.expected_artifact_directory(
                    "creation-prerequisite",
                    RUN_ID,
                )
                receipt_path = directory / "receipt.json"
                receipt = json.loads(receipt_path.read_text(encoding="utf-8"))
                milestones = receipt["timing"]["milestones"]
                if case == "missing":
                    milestones.pop()
                elif case == "duplicate":
                    milestones.append(dict(milestones[-1]))
                elif case == "reordered":
                    milestones[0], milestones[1] = milestones[1], milestones[0]
                elif case == "wrongPhase":
                    milestones[3]["phaseId"] = "dashboard-proof"
                else:
                    milestones[3]["ordinal"] = 99
                receipt_path.write_text(json.dumps(receipt) + "\n", encoding="utf-8")
                self.reseal(directory)

                with self.assertRaisesRegex(ValueError, "milestone"):
                    self.validate(root)

    def test_creation_timing_rejects_cross_field_and_schema_forgery(self) -> None:
        cases = (
            ("phaseOverSum", "does not reconcile"),
            ("totalOverPhaseSum", "does not reconcile"),
            ("milestoneTotalZero", "milestone timing differs"),
            ("phaseBoolOrdinal", "phase timing is outside budget"),
            ("milestoneBoolOrdinal", "milestone identity differs"),
            ("schemaV1", "timing schema differs"),
        )
        for case, expected_error in cases:
            with self.subTest(case=case), tempfile.TemporaryDirectory() as temporary:
                root = Path(temporary)
                self.materialize_all(root)
                directory = root / AGGREGATE.expected_artifact_directory(
                    "creation-prerequisite",
                    RUN_ID,
                )
                receipt_path = directory / "receipt.json"
                receipt = json.loads(receipt_path.read_text(encoding="utf-8"))
                timing = receipt["timing"]
                if case == "phaseOverSum":
                    for phase in timing["phases"]:
                        phase["elapsedMs"] = phase["budgetMs"]
                elif case == "totalOverPhaseSum":
                    timing["totalElapsedMs"] += (
                        AGGREGATE.CREATION_TIMING_ROUNDING_TOLERANCE_MS + 1
                    )
                elif case == "milestoneTotalZero":
                    timing["milestones"][0]["totalElapsedMs"] = 0
                elif case == "phaseBoolOrdinal":
                    timing["phases"][0]["ordinal"] = True
                elif case == "milestoneBoolOrdinal":
                    timing["milestones"][0]["ordinal"] = True
                else:
                    timing["schema"] = "chummer.android.creation-prerequisite-progress/v1"
                receipt_path.write_text(json.dumps(receipt) + "\n", encoding="utf-8")
                self.reseal(directory)

                with self.assertRaisesRegex(ValueError, expected_error):
                    self.validate(root)


if __name__ == "__main__":
    unittest.main()
