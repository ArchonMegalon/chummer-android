from __future__ import annotations

import copy
import importlib.util
import json
from pathlib import Path
import sys
from types import SimpleNamespace
import unittest
from unittest.mock import patch


ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tests"))
import api36_proof_state as proof


def expectation() -> proof.ProofBuildExpectation:
    return proof.ProofBuildExpectation(
        "1" * 40,
        "2" * 40,
        "3" * 64,
        "hosted-123-1",
    )


def state_payload() -> dict[str, object]:
    value: dict[str, object] = {
        "schema": proof.SCHEMA,
        "sequence": 7,
        "processId": 4242,
        "processInstanceId": "44444444-4444-4444-4444-444444444444",
        "e2eAuthorityGeneration": 2,
        "build": {
            "sourceCommit": "1" * 40,
            "sourceTree": "2" * 40,
            "gateContractSha256": "3" * 64,
            "proofBuildId": "hosted-123-1",
            "packageName": proof.PACKAGE,
            "versionName": "0.1.0-preview.10",
            "versionCode": "10",
            "runtimeIdentifier": "android-x64",
        },
        "surface": {
            "shellDestination": "runner",
            "pageAutomationId": "sr5-career/before-run/review",
            "navigationDepth": 4,
            "wizardLane": "before-run",
            "stage": "review-ready",
            "settled": True,
        },
        "workspace": {
            "workspaceId": "workspace-before-run",
            "contentRevision": 31,
            "savedRevision": 31,
            "payloadSha256": "4" * 64,
            "documentSha256": "5" * 64,
            "snapshotDigest": "sha256:" + "6" * 64,
        },
        "transaction": {
            "checkpointReadStatus": "ready",
            "phase": "reviewed",
            "journalVersion": 1,
            "transactionId": "33333333-3333-3333-3333-333333333333",
            "journalDigest": "sha256:" + "7" * 64,
            "actionId": "before-run.edge.spend",
            "actionKind": "spend-edge",
            "actionDigest": "sha256:" + "8" * 64,
            "expectedWorkspaceRevision": 31,
            "appliedWorkspaceRevision": None,
            "expectedPostconditionDigest": "sha256:" + "9" * 64,
            "observedPostconditionDigest": None,
            "receiptDigest": None,
            "resumeRestored": True,
            "canConfirm": True,
            "statusCode": None,
        },
        "creationResources": None,
        "stateDigest": "",
    }
    value["stateDigest"] = proof.expected_state_digest(value)
    return value


def import_state_payload() -> dict[str, object]:
    value: dict[str, object] = {
        "schema": proof.IMPORT_SCHEMA,
        "sequence": 5,
        "processId": 4242,
        "processInstanceId": "44444444-4444-4444-4444-444444444444",
        "e2eAuthorityGeneration": 2,
        "build": state_payload()["build"],
        "operationId": "55555555-5555-5555-5555-555555555555",
        "stage": "activation-issued",
        "picker": {
            "requestCode": 6411,
            "result": "ok",
            "uriPresent": True,
            "uriSha256": "a" * 64,
        },
        "stream": {
            "displayName": "career-calendar-edit-e2e.chum5",
            "mediaType": "application/octet-stream",
            "byteLength": 123,
            "contentSha256": "b" * 64,
        },
        "workspace": {
            "expectedPayloadSha256": "b" * 64,
            "authority": {
                "workspaceId": "workspace-imported",
                "contentRevision": 1,
                "savedRevision": 0,
                "payloadSha256": "b" * 64,
                "documentSha256": "c" * 64,
                "snapshotDigest": None,
            },
        },
        "activationIssued": True,
        "failureCode": None,
        "stateDigest": "",
    }
    value["stateDigest"] = proof.expected_import_state_digest(value)
    return value


def encoded(value: dict[str, object]) -> bytes:
    return json.dumps(value, separators=(",", ":")).encode("utf-8")


class Api36ProofStateContractTests(unittest.TestCase):
    def test_exact_import_state_binds_callback_stream_workspace_and_activation(self) -> None:
        value = import_state_payload()
        snapshot = proof.validate_import_state(
            encoded(value),
            expected=expectation(),
            live_process_id=4242,
        )

        self.assertEqual("activation-issued", snapshot.payload["stage"])
        self.assertEqual("b" * 64, snapshot.payload["stream"]["contentSha256"])
        self.assertEqual(
            "b" * 64,
            snapshot.payload["workspace"]["expectedPayloadSha256"],
        )
        self.assertEqual(64, len(snapshot.serialized_sha256))

    def test_hostile_import_states_fail_closed(self) -> None:
        cases = (
            ("stale process", ("processId",), 9999),
            ("wrong request", ("picker", "requestCode"), 6412),
            ("raw URI injection", ("picker", "uri"), "content://private/raw"),
            ("stream mismatch", ("stream", "contentSha256"), "d" * 64),
            ("workspace mismatch", ("workspace", "expectedPayloadSha256"), "e" * 64),
            ("activation withheld", ("activationIssued",), False),
            ("unbounded display name", ("stream", "displayName"), "x" * 257),
        )
        for label, path, replacement in cases:
            with self.subTest(label=label):
                value = copy.deepcopy(import_state_payload())
                target = value
                for member in path[:-1]:
                    target = target[member]  # type: ignore[index,assignment]
                target[path[-1]] = replacement  # type: ignore[index]
                if label != "stale process":
                    value["stateDigest"] = proof.expected_import_state_digest(value)
                with self.assertRaises(RuntimeError):
                    proof.validate_import_state(
                        encoded(value),
                        expected=expectation(),
                        live_process_id=4242,
                    )

    def test_import_reader_fails_immediately_on_cancelled_callback(self) -> None:
        value = import_state_payload()
        value.update(
            {
                "stage": "cancelled",
                "picker": {
                    "requestCode": 6411,
                    "result": "cancelled",
                    "uriPresent": False,
                    "uriSha256": None,
                },
                "stream": None,
                "workspace": None,
                "activationIssued": False,
            }
        )
        value["stateDigest"] = proof.expected_import_state_digest(value)

        class Device:
            def shell(self, *_arguments: str) -> str:
                return "4242"

            def run(self, *_arguments: str, **_kwargs: object) -> SimpleNamespace:
                return SimpleNamespace(returncode=0, stdout=encoded(value))

        with self.assertRaisesRegex(RuntimeError, "stage='cancelled'"):
            proof.wait_for_import_activation(
                Device(),
                expected=expectation(),
                content_sha256="b" * 64,
                timeout=1,
            )

    def test_import_reader_rejects_a_different_governed_fixture(self) -> None:
        value = import_state_payload()

        class Device:
            def shell(self, *_arguments: str) -> str:
                return "4242"

            def run(self, *_arguments: str, **_kwargs: object) -> SimpleNamespace:
                return SimpleNamespace(returncode=0, stdout=encoded(value))

        with self.assertRaisesRegex(RuntimeError, "differs from the governed fixture"):
            proof.wait_for_import_activation(
                Device(),
                expected=expectation(),
                content_sha256="d" * 64,
                timeout=1,
            )

    def test_exact_state_is_digest_process_and_build_bound(self) -> None:
        self.assertEqual(
            "sha256:5d5ec21d03f3054d3f265b5f307357d42646ba229870d595c6f9e3b8b643456f",
            state_payload()["stateDigest"],
        )
        snapshot = proof.validate_state(
            encoded(state_payload()),
            expected=expectation(),
            live_process_id=4242,
        )
        self.assertEqual("review-ready", snapshot.payload["surface"]["stage"])
        self.assertEqual(64, len(snapshot.serialized_sha256))

    def test_hostile_state_fails_closed(self) -> None:
        cases = (
            ("stale process", ("processId",), 9999),
            ("ARM64", ("build", "runtimeIdentifier"), "android-arm64"),
            ("wrong source", ("build", "sourceCommit"), "a" * 40),
            ("foreign gate", ("build", "gateContractSha256"), "b" * 64),
            ("wrong revision", ("transaction", "expectedWorkspaceRevision"), 30),
            ("extra field", ("unexpected",), True),
            ("uppercase digest", ("workspace", "payloadSha256"), "A" * 64),
            ("unbounded workspace", ("workspace", "workspaceId"), "w" * 257),
        )
        for label, path, replacement in cases:
            with self.subTest(label=label):
                value = copy.deepcopy(state_payload())
                target = value
                for member in path[:-1]:
                    target = target[member]  # type: ignore[index,assignment]
                target[path[-1]] = replacement  # type: ignore[index]
                if label != "stale process":
                    value["stateDigest"] = proof.expected_state_digest(value)
                with self.assertRaises(RuntimeError):
                    proof.validate_state(
                        encoded(value),
                        expected=expectation(),
                        live_process_id=4242,
                    )

    def test_duplicate_partial_and_noncanonical_json_fail_closed(self) -> None:
        value = state_payload()
        raw = encoded(value)
        duplicate = raw[:-1] + b',"schema":"chummer.android.api36-proof-state/v2"}'
        for hostile in (duplicate, raw + b"\n", raw[: len(raw) // 2]):
            with self.assertRaises(RuntimeError):
                proof.validate_state(
                    hostile,
                    expected=expectation(),
                    live_process_id=4242,
                )

    def test_reader_tolerates_only_a_stale_preceding_process_observation(self) -> None:
        stale = state_payload()
        stale["processId"] = 1111
        stale["stateDigest"] = proof.expected_state_digest(stale)
        current = state_payload()

        class Device:
            def __init__(self) -> None:
                self.reads = [encoded(stale), encoded(current)]

            def shell(self, *_arguments: str) -> str:
                return "4242"

            def run(self, *_arguments: str, **_kwargs: object) -> SimpleNamespace:
                return SimpleNamespace(returncode=0, stdout=self.reads.pop(0))

        device = Device()
        with patch.object(proof.time, "sleep", return_value=None):
            snapshot = proof.wait_for_state(
                device,
                expected=expectation(),
                page_automation_id="sr5-career/before-run/review",
                stage="review-ready",
                wizard_lane="before-run",
                timeout=1,
            )
        self.assertEqual(4242, snapshot.payload["processId"])
        self.assertEqual([], device.reads)

    def test_build_and_source_contract_excludes_normal_debug_release_play_and_arm64(self) -> None:
        project = (ROOT / "src/Chummer.Android/Chummer.Android.csproj").read_text(encoding="utf-8")
        build = (ROOT / "scripts/build-debug.sh").read_text(encoding="utf-8")
        workflow = (ROOT / ".github/workflows/api36-editing-e2e.yml").read_text(encoding="utf-8")
        publisher = (ROOT / "src/Chummer.Android/Proof/Api36ProofStatePublisher.cs").read_text(encoding="utf-8")
        self.assertIn("<ChummerApi36ProofInstrumentation Condition=", project)
        self.assertIn('<Compile Remove="Proof/Api36ProofState.cs;Proof/Api36ProofStatePublisher.cs"', project)
        self.assertIn("API-36 proof instrumentation is allowed only", project)
        self.assertIn("restricted to the hosted x86_64 APK", project)
        self.assertIn('CHUMMER_API36_PROOF_INSTRUMENTATION:-false', build)
        self.assertIn('runtime_identifier" != "android-x64', build)
        x64_step = workflow.index("Build the emulator APK and native compile gate")
        arm64_step = workflow.index("Build the ARM64 hosted debug candidate")
        self.assertIn("CHUMMER_API36_PROOF_INSTRUMENTATION", workflow[x64_step:arm64_step])
        self.assertNotIn("CHUMMER_API36_PROOF_INSTRUMENTATION", workflow[arm64_step:])
        for exported_surface in ("[Activity", "[Service", "[BroadcastReceiver", "ContentProvider", "HttpListener", "Socket"):
            self.assertNotIn(exported_surface, publisher)

    def test_creation_resources_state_is_exactly_page_workspace_and_digest_bound(self) -> None:
        value = state_payload()
        value["surface"] = {
            "shellDestination": "runner",
            "pageAutomationId": "creation-resources-page",
            "navigationDepth": 3,
            "wizardLane": "creation-resources",
            "stage": "authority-ready",
            "settled": True,
        }
        value["workspace"] = {
            "workspaceId": "workspace-resources",
            "contentRevision": 42,
            "savedRevision": 42,
            "payloadSha256": "4" * 64,
            "documentSha256": "5" * 64,
            "snapshotDigest": "sha256:" + "6" * 64,
        }
        value["transaction"] = None
        value["creationResources"] = {
            "pageIdentity": "creation-resources-page",
            "workspaceId": "workspace-resources",
            "workspaceRevision": 42,
            "contentRevision": 42,
            "savedRevision": 42,
            "authorityDigest": "sha256:" + "7" * 64,
            "sourceDigest": "sha256:" + "8" * 64,
            "rulesDigest": "sha256:" + "9" * 64,
            "runtimeDigest": "sha256:" + "a" * 64,
            "snapshotDigest": "sha256:" + "6" * 64,
            "rawCharacterXmlDigest": "sha256:" + "b" * 64,
            "auxiliaryStateDigest": "c" * 64,
            "prerequisiteDraftRevision": 5,
            "prerequisiteDraftDigest": "sha256:" + "d" * 64,
            "priorityNuyen": 50000,
            "totalStartingNuyen": 50000,
            "pendingOptionId": "karma:0",
            "pendingDraftRevision": 1,
            "pendingDraftDigest": "sha256:" + "e" * 64,
        }
        value["stateDigest"] = proof.expected_state_digest(value)
        snapshot = proof.validate_state(
            encoded(value), expected=expectation(), live_process_id=4242
        )
        self.assertEqual(
            "sha256:" + "7" * 64,
            snapshot.payload["creationResources"]["authorityDigest"],
        )

        cases = (
            ("absent", ("creationResources",), None),
            ("wrong page", ("creationResources", "pageIdentity"), "other-page"),
            ("foreign workspace", ("creationResources", "workspaceId"), "foreign"),
            ("wrong workspace revision", ("creationResources", "workspaceRevision"), 41),
            ("wrong saved revision", ("creationResources", "savedRevision"), 41),
            ("foreign snapshot", ("creationResources", "snapshotDigest"), "sha256:" + "f" * 64),
            ("missing pending digest", ("creationResources", "pendingDraftDigest"), None),
            ("untyped authority", ("creationResources", "authorityDigest"), "7" * 64),
            ("nonfinite budget", ("creationResources", "priorityNuyen"), float("inf")),
        )
        for label, path, replacement in cases:
            with self.subTest(label=label):
                hostile = copy.deepcopy(value)
                target = hostile
                for member in path[:-1]:
                    target = target[member]  # type: ignore[index,assignment]
                target[path[-1]] = replacement  # type: ignore[index]
                hostile["stateDigest"] = proof.expected_state_digest(hostile)
                with self.assertRaises(RuntimeError):
                    proof.validate_state(
                        encoded(hostile), expected=expectation(), live_process_id=4242
                    )

    def test_transport_is_one_exact_read_only_argument_vector(self) -> None:
        shared_path = ROOT / "tests/run_api36_editing_e2e.py"
        spec = importlib.util.spec_from_file_location("proof_shared_driver", shared_path)
        assert spec is not None and spec.loader is not None
        shared = importlib.util.module_from_spec(spec)
        sys.modules[spec.name] = shared
        spec.loader.exec_module(shared)
        self.assertEqual(tuple(shared.API36_PROOF_STATE_READ_ARGUMENTS), proof.READ_ARGUMENTS)
        self.assertEqual(
            tuple(shared.API36_IMPORT_PROOF_STATE_READ_ARGUMENTS),
            proof.IMPORT_READ_ARGUMENTS,
        )
        self.assertEqual(
            ("read-only-retryable", "exact app-private API-36 proof-state observation"),
            shared.adb_command_retry_policy(proof.READ_ARGUMENTS),
        )
        self.assertEqual(
            "non-replayable",
            shared.adb_command_retry_policy(
                ("exec-out", "run-as", proof.PACKAGE, "cat", "files/other")
            )[0],
        )

    def test_import_instrumentation_snapshots_uri_before_base_callback(self) -> None:
        activity = (ROOT / "src/Chummer.Android/Platforms/Android/MainActivity.cs").read_text(
            encoding="utf-8"
        )
        callback = activity[activity.index("protected override void OnActivityResult") :]
        callback = callback[: callback.index("public async Task<AndroidUpdateCheckResult>")]
        uri_snapshot = callback.index("documentUri =")
        proof_callback = callback.index("TryRecordDocumentPickerCallback")
        base_callback = callback.index("base.OnActivityResult")
        broker_complete = callback.index("DocumentIntentBroker.Complete(documentUri)")
        self.assertLess(uri_snapshot, proof_callback)
        self.assertLess(proof_callback, base_callback)
        self.assertLess(base_callback, broker_complete)
        self.assertNotIn("Complete(resultCode == Result.Ok ? data?.Data", callback)

    def test_import_evidence_is_proof_only_and_covers_stream_and_workspace(self) -> None:
        document_service = (
            ROOT / "src/Chummer.Android/Platforms/Android/AndroidDocumentService.cs"
        ).read_text(encoding="utf-8")
        coordinator = (
            ROOT / "src/Chummer.Android/Native/RunnerSessionCoordinator.cs"
        ).read_text(encoding="utf-8")
        project = (ROOT / "src/Chummer.Android/Chummer.Android.csproj").read_text(
            encoding="utf-8"
        )
        self.assertIn("#if CHUMMER_API36_PROOF_INSTRUMENTATION", document_service)
        self.assertIn("TryBeginDocumentImport", document_service)
        self.assertIn("TryRecordDocumentStream", document_service)
        self.assertIn("TryRecordDocumentWorkspace", coordinator)
        self.assertIn("workspace-not-activated", coordinator)
        self.assertIn(
            '<Compile Remove="Proof/Api36ProofState.cs;Proof/Api36ProofStatePublisher.cs"',
            project,
        )

    def test_before_run_receipt_retains_black_box_proof_and_adds_instrumentation(self) -> None:
        driver = (ROOT / "tests/run_api36_sr5_before_run_edge_physical_e2e.py").read_text(
            encoding="utf-8"
        )
        result_start = driver.index('    result = {\n        "scope": {')
        instrumentation = driver.index('result["api36ProofInstrumentation"]', result_start)
        final_return = driver.index("    return result", instrumentation)
        self.assertLess(result_start, instrumentation)
        self.assertLess(instrumentation, final_return)
        self.assertIn('device.wait("sr5-table-wizard-receipt", timeout=180)', driver)
        self.assertIn("read_transaction(device, spec.checkpoint_key)", driver)
        self.assertIn("root_for_authority(device, final_saved, spec.fixture_alias)", driver)

    def test_creation_resources_observer_retains_black_box_lifecycle_and_xml_proof(self) -> None:
        page = (ROOT / "src/Chummer.Android/Native/CreationResourcesPage.cs").read_text(
            encoding="utf-8"
        )
        driver = (ROOT / "tests/run_api36_creation_prerequisite_e2e.py").read_text(
            encoding="utf-8"
        )
        self.assertIn("Api36ProofStatePublisher.TryPublishCreationResources(", page)
        self.assertIn("shared.force_stop_and_launch_new_process(", driver)
        self.assertIn('open_resources(\n        device,', driver)
        self.assertIn('device.capture(\n        "creation-prerequisite-process-restart"', driver)
        self.assertIn("resourcesSameProcessPersistedAuthority", driver)
        self.assertIn("resourcesRestartedPersistedAuthority", driver)
        self.assertIn("rawCharacterXmlDigest", driver)
        self.assertNotIn('"process-restart-resources": 180_000', driver)

    def test_gate_scope_is_unchanged(self) -> None:
        gate = json.loads((ROOT / "eng/api36-sr5-wizard-gate-authority.json").read_text(encoding="utf-8"))
        self.assertEqual(7, gate["requiredJourneyCount"])
        self.assertFalse(gate["publicationAuthorized"])
        self.assertIn("full_editing_pass", gate["doesNotAssert"])
        self.assertIn("tablet_readiness", gate["doesNotAssert"])


if __name__ == "__main__":
    unittest.main()
