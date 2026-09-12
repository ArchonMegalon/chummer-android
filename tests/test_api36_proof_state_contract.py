from __future__ import annotations

import copy
import hashlib
import importlib.util
import itertools
import json
from pathlib import Path
import sys
import tempfile
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
            "versionName": "0.1.0-preview.12",
            "versionCode": "12",
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


def attachment_payload(sequence: int = 7) -> dict[str, object]:
    value = state_payload()
    value["sequence"] = sequence
    value["surface"].update({
        "pageAutomationId": "creation-prerequisite-page",
        "navigationDepth": 2,
        "wizardLane": "creation-prerequisite",
        "stage": "attachment-authority-ready",
    })
    value["transaction"] = None
    value["stateDigest"] = proof.expected_state_digest(value)
    return value


class Api36ProofStateContractTests(unittest.TestCase):
    def test_attachment_reader_waits_for_later_same_process_proof(self) -> None:
        prior = attachment_payload(9)
        observations = [attachment_payload(8), prior, attachment_payload(10)]
        clock = [100.0]
        deadlines = []

        def observe(_device, *, deadline, attempt):
            deadlines.append(deadline)
            return encoded(observations.pop(0)), 4242, {"attempt": attempt}

        with patch.object(proof, "_state_file_observation", side_effect=observe), patch.object(
            proof.time, "monotonic", side_effect=lambda: clock[0]
        ), patch.object(proof.time, "sleep", side_effect=lambda n: clock.__setitem__(0, clock[0] + n)):
            result = proof.wait_for_state(
                SimpleNamespace(), expected=expectation(),
                page_automation_id="creation-prerequisite-page",
                stage="attachment-authority-ready", wizard_lane="creation-prerequisite",
                timeout=30, deadline=105.0, after_same_process_proof=prior,
            )
        self.assertEqual(10, result.payload["sequence"])
        self.assertEqual(3, result.read_observation["attempt"])
        self.assertEqual([105.0] * 3, deadlines)

    def test_attachment_reader_rejects_coherent_process_and_workspace_drift(self) -> None:
        prior = attachment_payload(9)
        cases = (
            ("processId", None, 4243),
            ("processInstanceId", None, "55555555-5555-5555-5555-555555555555"),
            ("e2eAuthorityGeneration", None, 3),
            ("workspace", "workspaceId", "another-workspace"),
            ("workspace", "contentRevision", 32),
            ("workspace", "savedRevision", 30),
            ("workspace", "payloadSha256", "a" * 64),
            ("workspace", "documentSha256", "b" * 64),
        )
        for field, member, replacement in cases:
            with self.subTest(field=field, member=member):
                value = attachment_payload(9)  # Even an old sequence cannot hide identity drift.
                if member is None:
                    value[field] = replacement
                else:
                    value[field][member] = replacement
                value["stateDigest"] = proof.expected_state_digest(value)
                with patch.object(proof, "_state_file_observation", return_value=(
                    encoded(value), value["processId"], {"attempt": 1},
                )) as read, self.assertRaisesRegex(RuntimeError, "exact same-process workspace"):
                    proof.wait_for_state(
                        SimpleNamespace(), expected=expectation(),
                        page_automation_id="creation-prerequisite-page",
                        stage="attachment-authority-ready", wizard_lane="creation-prerequisite",
                        after_same_process_proof=prior,
                    )
                self.assertEqual(1, read.call_count)

    def test_attachment_reader_rejects_stale_forever_and_late_valid_observation(self) -> None:
        for late in (False, True):
            with self.subTest(late=late):
                clock = [100.0]
                deadlines = []
                prior = attachment_payload(9)

                def observe(_device, *, deadline, attempt):
                    deadlines.append(deadline)
                    clock[0] += 0.6 if late else 0.1
                    return encoded(attachment_payload(10) if late else prior), 4242, {"attempt": attempt}

                with patch.object(proof, "_state_file_observation", side_effect=observe), patch.object(
                    proof.time, "monotonic", side_effect=lambda: clock[0]
                ), patch.object(proof.time, "sleep", side_effect=lambda n: clock.__setitem__(0, clock[0] + n)), self.assertRaisesRegex(
                    RuntimeError, "after its deadline" if late else "required_after=9",
                ):
                    proof.wait_for_state(
                        SimpleNamespace(), expected=expectation(),
                        page_automation_id="creation-prerequisite-page",
                        stage="attachment-authority-ready", wizard_lane="creation-prerequisite",
                        timeout=30, deadline=100.5, after_same_process_proof=prior,
                    )
                self.assertTrue(deadlines)
                self.assertTrue(all(value == 100.5 for value in deadlines))
                if late:
                    self.assertEqual(1, len(deadlines))
                else:
                    self.assertEqual(100.5, clock[0])

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
        observed: list[proof.ImportProofStateSnapshot] = []

        class Device:
            def shell(self, *_arguments: str) -> str:
                return "4242"

            def run(self, *_arguments: str, **_kwargs: object) -> SimpleNamespace:
                return SimpleNamespace(returncode=0, stdout=encoded(value))

        with self.assertRaisesRegex(
            RuntimeError,
            "differs from the governed fixture",
        ) as raised:
            proof.wait_for_import_activation(
                Device(),
                expected=expectation(),
                content_sha256="d" * 64,
                timeout=1,
                first_picker_result_observer=observed.append,
            )
        self.assertEqual(1, len(observed))
        self.assertEqual(value, observed[0].payload)
        self.assertIn(f"expected={'d' * 64}", str(raised.exception))
        self.assertIn(f"actual={'b' * 64}", str(raised.exception))
        self.assertIn(
            "displayName='career-calendar-edit-e2e.chum5'",
            str(raised.exception),
        )

    def test_import_reader_observes_only_first_validated_picker_result(self) -> None:
        before_picker = import_state_payload()
        before_picker["stage"] = "picker-launched"
        before_picker["picker"] = None
        before_picker["stream"] = None
        before_picker["workspace"] = None
        before_picker["activationIssued"] = False
        before_picker["stateDigest"] = proof.expected_import_state_digest(before_picker)
        activated = import_state_payload()
        outputs = [encoded(before_picker), encoded(activated)]
        observed: list[proof.ImportProofStateSnapshot] = []

        class Device:
            def shell(self, *_arguments: str) -> str:
                return "4242"

            def run(self, *_arguments: str, **_kwargs: object) -> SimpleNamespace:
                return SimpleNamespace(returncode=0, stdout=outputs.pop(0))

        result = proof.wait_for_import_activation(
            Device(),
            expected=expectation(),
            content_sha256="b" * 64,
            timeout=1,
            first_picker_result_observer=observed.append,
        )

        self.assertEqual(activated, result.payload)
        self.assertEqual([activated], [item.payload for item in observed])

    def test_exact_state_is_digest_process_and_build_bound(self) -> None:
        self.assertEqual(
            "sha256:6d924d3d15c1d073cde12d995b4d75f951e18bbab5daba468895a4c761130269",
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
                self.responses = []
                for inode, raw in enumerate(self.reads, start=101):
                    metadata = f"1:{inode}:{len(raw)}:1788336000:81a4\n"
                    self.responses.extend(
                        (
                            SimpleNamespace(returncode=0, stdout=metadata),
                            SimpleNamespace(returncode=0, stdout=raw),
                            SimpleNamespace(returncode=0, stdout=metadata),
                        )
                    )
                self.arguments: list[tuple[str, ...]] = []

            def shell(self, *_arguments: str, **_kwargs: object) -> str:
                return "4242"

            def run(self, *arguments: str, **_kwargs: object) -> SimpleNamespace:
                self.arguments.append(arguments)
                return self.responses.pop(0)

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
        self.assertEqual([], device.responses)
        self.assertEqual(
            [proof.STAT_ARGUMENTS, proof.READ_ARGUMENTS, proof.STAT_ARGUMENTS] * 2,
            device.arguments,
        )

    def test_reader_accepts_only_metadata_content_metadata_identity(self) -> None:
        raw = encoded(state_payload())
        metadata = f"1:101:{len(raw)}:1788336000:81a4\n"

        class Device:
            def __init__(self, evidence: Path) -> None:
                self.evidence = evidence
                self.responses = [
                    SimpleNamespace(returncode=0, stdout=metadata),
                    SimpleNamespace(returncode=0, stdout=raw),
                    SimpleNamespace(returncode=0, stdout=metadata),
                ]
                self.shell_calls: list[tuple[tuple[str, ...], dict[str, object]]] = []
                self.run_calls: list[tuple[tuple[str, ...], dict[str, object]]] = []

            def shell(self, *arguments: str, **options: object) -> str:
                self.shell_calls.append((arguments, options))
                return "4242"

            def run(self, *arguments: str, **options: object) -> SimpleNamespace:
                self.run_calls.append((arguments, options))
                return self.responses.pop(0)

        with tempfile.TemporaryDirectory() as temporary:
            evidence = Path(temporary)
            device = Device(evidence)
            snapshot = proof.wait_for_state(
                device,
                expected=expectation(),
                page_automation_id="sr5-career/before-run/review",
                stage="review-ready",
                wizard_lane="before-run",
                timeout=30,
            )
            receipt = json.loads(
                (evidence / proof.READ_RECEIPT_NAME).read_text(encoding="utf-8")
            )

        self.assertEqual(hashlib.sha256(raw).hexdigest(), snapshot.serialized_sha256)
        self.assertEqual(
            "metadata-content-metadata-identity",
            snapshot.read_observation["reconciliation"],
        )
        self.assertEqual("pass", receipt["status"])
        self.assertEqual(1, receipt["acceptedAttempt"])
        self.assertEqual(0, receipt["mutationCommandsRetried"])
        self.assertEqual("7d", receipt["attempts"][0]["lastByteHex"])
        self.assertEqual(2, len(device.shell_calls))
        self.assertEqual(
            [proof.STAT_ARGUMENTS, proof.READ_ARGUMENTS, proof.STAT_ARGUMENTS],
            [call[0] for call in device.run_calls],
        )
        self.assertTrue(all("deadline" in options for _, options in device.shell_calls))
        self.assertTrue(all("deadline" in options for _, options in device.run_calls))

    def test_reader_rejects_remote_missing_file_diagnostics_with_local_zero_exit(self) -> None:
        # Run 34678728103 retained these exact lengths/hashes, not raw stdout.
        # Model only transport/clock: the real metadata parser, read reconciliation,
        # wait loop, state validator and evidence writer must reject the diagnostics.
        missing_stat = "stat: 'files/api36-proof/state.v2.json': No such file or directory\n"
        missing_content = b"cat: files/api36-proof/state.v2.json: No such file or directory\n"
        stat_sha = "e1276eb827e6e7a798b29f3044a81c79324072462aa6132bb2cba6a50e9f0f05"
        content_sha = "a413744eadce4498567d1db6461d06a71ec7933f3b8dd1dd064fe49a27e02eb0"
        self.assertEqual((67, stat_sha), (
            len(missing_stat.encode("utf-8")), hashlib.sha256(missing_stat.encode("utf-8")).hexdigest(),
        ))
        self.assertEqual((64, content_sha), (
            len(missing_content), hashlib.sha256(missing_content).hexdigest(),
        ))
        prior = attachment_payload(9)
        fresh = encoded(attachment_payload(10))
        fresh_metadata = f"1:101:{len(fresh)}:1788336000:81a4\n"
        case = self

        for fresh_after_missing in (False, True):
            with self.subTest(fresh_after_missing=fresh_after_missing):
                clock = [100.0]
                deadline = 100.5
                calls = []

                class Device:
                    def __init__(self, evidence: Path) -> None:
                        self.evidence = evidence
                        self.read_count = 0

                    def shell(self, *arguments: str, **options: object) -> str:
                        case.assertEqual(("pidof", proof.PACKAGE), arguments)
                        case.assertEqual(deadline, options["deadline"])
                        calls.append(("shell", arguments))
                        return "4242"

                    def run(self, *arguments: str, **options: object) -> SimpleNamespace:
                        position = self.read_count % 3
                        expected = (proof.STAT_ARGUMENTS, proof.READ_ARGUMENTS, proof.STAT_ARGUMENTS)
                        case.assertEqual(expected[position], arguments)
                        case.assertEqual(deadline, options["deadline"])
                        case.assertIs(False, options["check"])
                        if position == 1:
                            case.assertIs(False, options["text"])
                        calls.append(("run", arguments))
                        ready = fresh_after_missing and self.read_count >= 3
                        self.read_count += 1
                        stdout = (
                            fresh if ready else missing_content
                        ) if position == 1 else (fresh_metadata if ready else missing_stat)
                        return SimpleNamespace(returncode=0, stdout=stdout)

                with tempfile.TemporaryDirectory() as temporary, patch.object(
                    proof.time, "monotonic", side_effect=lambda: clock[0]
                ), patch.object(
                    proof.time, "sleep", side_effect=lambda seconds: clock.__setitem__(0, clock[0] + seconds)
                ), patch.object(proof, "validate_state", wraps=proof.validate_state) as validate:
                    device = Device(Path(temporary))
                    arguments = dict(
                        expected=expectation(), page_automation_id="creation-prerequisite-page",
                        stage="attachment-authority-ready", wizard_lane="creation-prerequisite",
                        timeout=30, deadline=deadline, after_same_process_proof=prior,
                    )
                    if fresh_after_missing:
                        result = proof.wait_for_state(device, **arguments)
                        self.assertEqual(fresh, encoded(result.payload))
                        self.assertEqual(10, result.payload["sequence"])
                        validate.assert_called_once_with(fresh, expected=expectation(), live_process_id=4242)
                    else:
                        with self.assertRaisesRegex(RuntimeError, "^Timed out waiting for exact API-36 proof state: metadata-noncanonical$"):
                            proof.wait_for_state(device, **arguments)
                        validate.assert_not_called()
                        self.assertEqual(deadline, clock[0])
                    receipt = json.loads((device.evidence / proof.READ_RECEIPT_NAME).read_text(encoding="utf-8"))
                self.assertEqual("pass" if fresh_after_missing else "fail", receipt["status"])
                self.assertEqual(2 if fresh_after_missing else None, receipt["acceptedAttempt"])
                self.assertEqual(0, receipt["mutationCommandsRetried"])
                rejected = receipt["attempts"][:-1] if fresh_after_missing else receipt["attempts"]
                self.assertTrue(rejected)
                for observation in rejected:
                    self.assertEqual("retry", observation["status"])
                    self.assertEqual("metadata-noncanonical", observation["reconciliation"])
                    self.assertEqual(4242, observation["processBefore"])
                    self.assertEqual(4242, observation["processAfter"])
                    for suffix in ("Before", "After"):
                        self.assertIsNone(observation[f"metadata{suffix}"])
                        self.assertEqual(0, observation[f"metadata{suffix}ReturnCode"])
                        self.assertEqual({"type": "text", "bytes": 67, "sha256": stat_sha}, observation[f"metadata{suffix}Output"])
                    self.assertEqual(0, observation["contentReturnCode"])
                    self.assertEqual({"type": "bytes", "bytes": 64, "sha256": content_sha}, observation["contentOutput"])
                    self.assertEqual("0a", observation["lastByteHex"])
                    self.assertIs(False, observation["canonicalTerminalByte"])
                    self.assertNotIn("validationFailure", observation)
                self.assertEqual([
                    ("shell", ("pidof", proof.PACKAGE)),
                    ("run", proof.STAT_ARGUMENTS), ("run", proof.READ_ARGUMENTS),
                    ("run", proof.STAT_ARGUMENTS), ("shell", ("pidof", proof.PACKAGE)),
                ] * len(receipt["attempts"]), calls)

    def test_reader_retries_only_a_content_size_mismatch_then_accepts_exact_bytes(
        self,
    ) -> None:
        raw = encoded(state_payload())
        polluted = raw + b"\n"
        metadata = f"1:101:{len(raw)}:1788336000:81a4\n"

        class Device:
            def __init__(self, evidence: Path) -> None:
                self.evidence = evidence
                self.responses = [
                    SimpleNamespace(returncode=0, stdout=metadata),
                    SimpleNamespace(returncode=0, stdout=polluted),
                    SimpleNamespace(returncode=0, stdout=metadata),
                    SimpleNamespace(returncode=0, stdout=metadata),
                    SimpleNamespace(returncode=0, stdout=raw),
                    SimpleNamespace(returncode=0, stdout=metadata),
                ]
                self.arguments: list[tuple[str, ...]] = []

            def shell(self, *_arguments: str, **_options: object) -> str:
                return "4242"

            def run(self, *arguments: str, **_options: object) -> SimpleNamespace:
                self.arguments.append(arguments)
                return self.responses.pop(0)

        with tempfile.TemporaryDirectory() as temporary, patch.object(
            proof.time, "sleep", return_value=None
        ):
            evidence = Path(temporary)
            device = Device(evidence)
            snapshot = proof.wait_for_state(
                device,
                expected=expectation(),
                page_automation_id="sr5-career/before-run/review",
                stage="review-ready",
                wizard_lane="before-run",
                timeout=30,
            )
            receipt = json.loads(
                (evidence / proof.READ_RECEIPT_NAME).read_text(encoding="utf-8")
            )

        self.assertEqual(raw, encoded(snapshot.payload))
        self.assertEqual(2, receipt["acceptedAttempt"])
        self.assertEqual("content-size-mismatch", receipt["attempts"][0]["reconciliation"])
        self.assertEqual("0a", receipt["attempts"][0]["lastByteHex"])
        self.assertEqual("metadata-content-metadata-identity", receipt["attempts"][1]["reconciliation"])
        self.assertEqual(0, receipt["mutationCommandsRetried"])
        self.assertEqual(
            [proof.STAT_ARGUMENTS, proof.READ_ARGUMENTS, proof.STAT_ARGUMENTS] * 2,
            device.arguments,
        )

    def test_reader_retries_metadata_identity_drift_without_accepting_valid_json(
        self,
    ) -> None:
        raw = encoded(state_payload())
        before = f"1:101:{len(raw)}:1788336000:81a4\n"
        after = f"1:102:{len(raw)}:1788336001:81a4\n"

        class Device:
            def __init__(self) -> None:
                self.responses = [
                    SimpleNamespace(returncode=0, stdout=before),
                    SimpleNamespace(returncode=0, stdout=raw),
                    SimpleNamespace(returncode=0, stdout=after),
                    SimpleNamespace(returncode=0, stdout=after),
                    SimpleNamespace(returncode=0, stdout=raw),
                    SimpleNamespace(returncode=0, stdout=after),
                ]

            def shell(self, *_arguments: str, **_options: object) -> str:
                return "4242"

            def run(self, *_arguments: str, **_options: object) -> SimpleNamespace:
                return self.responses.pop(0)

        with patch.object(proof.time, "sleep", return_value=None):
            snapshot = proof.wait_for_state(
                Device(),
                expected=expectation(),
                page_automation_id="sr5-career/before-run/review",
                stage="review-ready",
                wizard_lane="before-run",
                timeout=30,
            )

        self.assertEqual(2, snapshot.read_observation["attempt"])
        self.assertEqual(
            "metadata-content-metadata-identity",
            snapshot.read_observation["reconciliation"],
        )

    def test_reader_fails_immediately_for_stable_noncanonical_publisher_bytes(self) -> None:
        raw = encoded(state_payload()) + b"\n"
        metadata = f"1:101:{len(raw)}:1788336000:81a4\n"

        class Device:
            def __init__(self, evidence: Path) -> None:
                self.evidence = evidence
                self.responses = [
                    SimpleNamespace(returncode=0, stdout=metadata),
                    SimpleNamespace(returncode=0, stdout=raw),
                    SimpleNamespace(returncode=0, stdout=metadata),
                ]

            def shell(self, *_arguments: str, **_options: object) -> str:
                return "4242"

            def run(self, *_arguments: str, **_options: object) -> SimpleNamespace:
                return self.responses.pop(0)

        with tempfile.TemporaryDirectory() as temporary:
            evidence = Path(temporary)
            device = Device(evidence)
            with self.assertRaisesRegex(RuntimeError, "bytes are empty, oversized"):
                proof.wait_for_state(
                    device,
                    expected=expectation(),
                    page_automation_id="sr5-career/before-run/review",
                    stage="review-ready",
                    wizard_lane="before-run",
                    timeout=30,
                )
            receipt = json.loads(
                (evidence / proof.READ_RECEIPT_NAME).read_text(encoding="utf-8")
            )

        self.assertEqual([], device.responses)
        self.assertEqual("fail", receipt["status"])
        self.assertEqual("0a", receipt["attempts"][0]["lastByteHex"])
        self.assertEqual(len(raw), receipt["attempts"][0]["contentBytes"])
        self.assertEqual(
            "API-36 proof-state bytes are empty, oversized, or noncanonical",
            receipt["attempts"][0]["validationFailure"],
        )
        self.assertEqual(0, receipt["mutationCommandsRetried"])

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
            tuple(shared.API36_PROOF_STATE_STAT_ARGUMENTS),
            proof.STAT_ARGUMENTS,
        )
        self.assertEqual(
            tuple(shared.API36_IMPORT_PROOF_STATE_READ_ARGUMENTS),
            proof.IMPORT_READ_ARGUMENTS,
        )
        self.assertEqual(
            ("read-only-retryable", "exact app-private API-36 proof-state observation"),
            shared.adb_command_retry_policy(proof.READ_ARGUMENTS),
        )
        self.assertEqual(
            ("read-only-retryable", "exact app-private API-36 proof-state observation"),
            shared.adb_command_retry_policy(proof.STAT_ARGUMENTS),
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
        broker_complete = callback.index(
            "DocumentIntentBroker.Complete(this, requestCode, documentUri)"
        )
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
        coordinator = (
            ROOT / "src/Chummer.Android/Native/RunnerSessionCoordinator.cs"
        ).read_text(encoding="utf-8")
        driver = (ROOT / "tests/run_api36_creation_prerequisite_e2e.py").read_text(
            encoding="utf-8"
        )
        confirm = page[page.index("private async Task ConfirmAsync()") :]
        confirm = confirm[: confirm.index("private void AddBudgetComparison()")]
        presenter_reload = confirm.index("await _overview.LoadAsync(")
        proof_refresh = confirm.index("RefreshApi36ProofWorkspaceAuthorityAsync(")
        receipt_publish = confirm.index("_receipt = receipt;")
        self.assertIn("Api36ProofStatePublisher.TryPublishCreationResources(", page)
        self.assertLess(presenter_reload, proof_refresh)
        self.assertLess(proof_refresh, receipt_publish)
        self.assertIn("if (AndroidE2EAuthority.Enabled)", confirm)
        self.assertIn("proofAuthority is null", confirm)
        self.assertIn(
            "TryNormalizeRawCharacterXmlSha256(\n"
            "                    receipt.RawCharacterXmlDigest,",
            confirm,
        )
        self.assertIn("out string expectedPayloadSha256", confirm)
        self.assertNotIn('RawCharacterXmlDigest["sha256:".Length..]', confirm)
        self.assertIn("receipt.WorkspaceRevision", confirm)
        self.assertIn("receipt.SavedRevision", confirm)
        refresh_method = "RefreshApi36ProofWorkspaceAuthorityAsync("
        refresh = coordinator[coordinator.index(refresh_method) :]
        refresh = refresh[: refresh.index("public ShellSurfaceState Surface")]
        self.assertIn(
            "#if CHUMMER_API36_PROOF_INSTRUMENTATION",
            coordinator[: coordinator.index(refresh_method)],
        )
        self.assertIn("TryRefreshWorkspaceAuthorityAsync(", refresh)
        self.assertIn("expectedWorkspaceId,", refresh)
        self.assertIn("expectedPayloadSha256,", refresh)
        self.assertIn("ExactWorkspaceAuthoritySnapshotMatches(", refresh)
        self.assertIn("authority.Matches(state)", refresh)
        self.assertIn("authority.WorkspaceId", refresh)
        self.assertIn("authority.ContentRevision == expectedContentRevision", refresh)
        self.assertIn("authority.SavedRevision == expectedSavedRevision", refresh)
        self.assertIn("authority.PayloadSha256", refresh)
        self.assertNotIn("allowReadOnlyProductCapture: true", refresh)
        self.assertIn("shared.force_stop_and_launch_new_process(", driver)
        self.assertIn('open_resources(\n        device,', driver)
        self.assertIn('device.capture(\n        "creation-prerequisite-process-restart"', driver)
        self.assertIn("resourcesSameProcessPersistedAuthority", driver)
        self.assertIn("resourcesRestartedPersistedAuthority", driver)
        self.assertIn("rawCharacterXmlDigest", driver)
        self.assertNotIn('"process-restart-resources": 180_000', driver)

    def test_creation_prerequisite_attachment_proof_is_debug_only_and_fail_closed(self) -> None:
        page = (ROOT / "src/Chummer.Android/Native/CreationPrerequisitePage.cs").read_text(
            encoding="utf-8"
        )
        publisher = (
            ROOT / "src/Chummer.Android/Proof/Api36ProofStatePublisher.cs"
        ).read_text(encoding="utf-8")
        project = (ROOT / "src/Chummer.Android/Chummer.Android.csproj").read_text(
            encoding="utf-8"
        )
        publish_call = page.index(
            "Api36ProofStatePublisher.TryPublishCreationPrerequisiteAttachment("
        )
        self.assertIn(
            "#if CHUMMER_API36_PROOF_INSTRUMENTATION",
            page[:publish_call],
        )
        for marker in (
            "page.IsLoaded",
            "page.Handler is not null",
            "page.Window is not null",
            "ReferenceEquals(navigationStack[^1], page)",
            "navigationStack.Count(candidate => ReferenceEquals(candidate, page)) == 1",
            'page.AutomationId,\n                "creation-prerequisite-page"',
            "prerequisiteAuthorityReady",
            "workspaceId,\n                authority.WorkspaceId",
            "contentRevision == authority.ContentRevision",
            "savedRevision == authority.SavedRevision",
            '$"sha256:{authority.PayloadSha256}"',
            "DeleteObservation();",
            '"creation-prerequisite"',
            '"attachment-authority-ready"',
        ):
            self.assertIn(marker, publisher)
        self.assertIn(
            "CreationPrerequisitePhoneAuthority.IsReady(state, Coordinator.State)",
            page,
        )
        self.assertIn(
            '<Compile Remove="Proof/Api36ProofState.cs;Proof/Api36ProofStatePublisher.cs"',
            project,
        )

    def test_creation_prerequisite_appearance_recaptures_exact_opt_in_proof_authority(self) -> None:
        # Source-contract only: this does not execute the Android lifecycle or
        # prove that a hosted post-confirm attachment successfully wrote its file.
        page = (ROOT / "src/Chummer.Android/Native/CreationPrerequisitePage.cs").read_text(
            encoding="utf-8"
        )
        project = (ROOT / "src/Chummer.Android/Chummer.Android.csproj").read_text(
            encoding="utf-8"
        )
        prepare = page[
            page.index("protected override async Task PrepareForAppearanceRefreshAsync(") :
            page.index("protected override void Refresh()")
        ]
        before, guarded = prepare.split("#if CHUMMER_API36_PROOF_INSTRUMENTATION\n")
        proof_capture, after = guarded.split("#endif\n")
        ordered_before = (
            "long appearance = CaptureAppearanceGeneration();",
            "await Coordinator.RevalidateCreationPrerequisiteAsync(_originalAuthority, cancellationToken,",
            "() => IsCurrentAppearanceGeneration(appearance));",
            "if (!IsCurrentAppearanceGeneration(appearance))\n            return;",
            "CharacterCreationPrerequisiteState? current = loaded.Value is { } loadedState",
            "Coordinator.IsCreationPrerequisiteStateCurrent(loadedState) ? loadedState : null;",
        )
        positions = [before.index(marker) for marker in ordered_before]
        self.assertEqual(sorted(positions), positions)
        self.assertIn("if (AndroidE2EAuthority.Enabled && current is { } proofState)", proof_capture)
        normalize = proof_capture.index(
            "if (!CreationResourcesPhoneAuthority.TryNormalizeRawCharacterXmlSha256(\n"
            "                    proofState.Binding.RawCharacterXmlDigest,\n"
            "                    out string expectedPayloadSha256))"
        )
        rejected_digest = proof_capture.index("current = null;", normalize)
        capture = proof_capture.index(
            "await Coordinator.RefreshApi36ProofWorkspaceAuthorityAsync(\n"
            "                        proofState.Binding.WorkspaceId,\n"
            "                        proofState.Binding.ContentRevision,\n"
            "                        proofState.Binding.SavedRevision,\n"
            "                        expectedPayloadSha256,\n"
            "                        cancellationToken);"
        )
        rejected_capture = proof_capture.index("if (proofAuthority is null)\n                    current = null;")
        self.assertLess(normalize, rejected_digest)
        self.assertLess(rejected_digest, proof_capture.index("else", rejected_digest))
        self.assertLess(proof_capture.index("else", rejected_digest), capture)
        self.assertLess(capture, rejected_capture)
        self.assertEqual(2, proof_capture.count("current = null;"))
        self.assertNotIn("_dashboardAuthority =", before + proof_capture)
        self.assertIn(
            "cancellationToken.ThrowIfCancellationRequested();\n"
            "        if (IsCurrentAppearanceGeneration(appearance))\n"
            "            _dashboardAuthority = current is { } state\n"
            "                                  && Coordinator.IsCreationPrerequisiteStateCurrent(state) ? state : null;",
            after,
        )
        for forbidden in (
            "RefreshApi36ProofWorkspaceAuthorityAsync", "AndroidE2EAuthority", "proofState",
        ):
            self.assertNotIn(forbidden, before + after)
        for forbidden in ("new NativeWorkspaceAuthoritySnapshot", "Task.Delay", "while (", "TryPublish"):
            self.assertNotIn(forbidden, prepare)
        self.assertIn('<Error Condition="\'$(Configuration)\' != \'Debug\'"', project)
        self.assertIn('<Compile Remove="Proof/Api36ProofState.cs;Proof/Api36ProofStatePublisher.cs"', project)

    def test_creation_prerequisite_attachment_publisher_reports_only_exact_write_success(self) -> None:
        publisher = (
            ROOT / "src/Chummer.Android/Proof/Api36ProofStatePublisher.cs"
        ).read_text(encoding="utf-8")
        static_publish = publisher[
            publisher.index("public static bool TryPublishCreationPrerequisiteAttachment(") :
            publisher.index("public bool PublishCreationPrerequisiteAttachment(")
        ]
        exact_publish = publisher[
            publisher.index("public bool PublishCreationPrerequisiteAttachment(") :
            publisher.index("public static void TryPublishCreationResources(")
        ]

        self.assertIn("Api36ProofStatePublisher? publisher = Current();", static_publish)
        self.assertIn(
            "return publisher?.PublishCreationPrerequisiteAttachment(",
            static_publish,
        )
        self.assertIn("prerequisiteAuthorityReady) == true;", static_publish)
        rejection = exact_publish.index(
            "if (!AndroidE2EAuthority.Enabled || !exactAttachment || !exactAuthority)"
        )
        deleted = exact_publish.index("DeleteObservation();", rejection)
        rejected = exact_publish.index("return false;", deleted)
        written = exact_publish.index(
            "WriteAtomically(_path, _temporaryPath, Api36ProofStateContract.Serialize(proof));"
        )
        accepted = exact_publish.index("return true;", written)
        self.assertLess(rejection, deleted)
        self.assertLess(deleted, rejected)
        self.assertLess(rejected, written)
        self.assertLess(written, accepted)
        self.assertEqual(1, exact_publish.count("return false;"))
        self.assertEqual(1, exact_publish.count("return true;"))
        for mismatch in (
            "!AndroidE2EAuthority.Enabled",
            "!exactAttachment",
            "!exactAuthority",
        ):
            self.assertLess(exact_publish.index(mismatch), rejected)

    def test_creation_prerequisite_attachment_publication_latches_every_lifecycle_order(self) -> None:
        page = (ROOT / "src/Chummer.Android/Native/CreationPrerequisitePage.cs").read_text(
            encoding="utf-8"
        )
        constructor = page[
            page.index("public CreationPrerequisitePage(") :
            page.index("protected override void OnAppearing()")
        ]
        appearing = page[
            page.index("protected override void OnAppearing()") :
            page.index("protected override void OnNavigatedTo(")
        ]
        navigated = page[
            page.index("protected override void OnNavigatedTo(") :
            page.index("protected override void OnDisappearing()")
        ]
        disappearing = page[
            page.index("protected override void OnDisappearing()") :
            page.index("protected override void Refresh()")
        ]
        refresh = page[
            page.index("protected override void Refresh()") :
            page.index("private void OnApi36ProofLoaded(")
        ]
        loaded = page[
            page.index("private void OnApi36ProofLoaded(") :
            page.index("private void TryPublishApi36AttachmentProof()")
        ]
        publication_latch = page[
            page.index("private void TryPublishApi36AttachmentProof()") :
            page.index("private void AddBinding(")
        ]

        self.assertEqual(1, constructor.count("Loaded += OnApi36ProofLoaded;"))
        self.assertNotIn("Loaded -=", page)
        self.assertNotIn("TryPublishCreationPrerequisiteAttachment", refresh)
        self.assertEqual(
            4,
            page.count("TryPublishApi36AttachmentProof();"),
        )
        self.assertLess(
            appearing.index("base.OnAppearing();"),
            appearing.index("_api36ProofRouteAppeared = true;"),
        )
        self.assertLess(
            appearing.index("_api36ProofRouteAppeared = true;"),
            appearing.index("TryPublishApi36AttachmentProof();"),
        )
        self.assertLess(
            navigated.index("base.OnNavigatedTo(args);"),
            navigated.index("TryPublishApi36AttachmentProof();"),
        )
        self.assertLess(
            disappearing.index("_api36ProofRouteAppeared = false;"),
            disappearing.index("base.OnDisappearing();"),
        )
        self.assertLess(
            disappearing.index("_latestApi36ProofReadyState = null;"),
            disappearing.index("base.OnDisappearing();"),
        )
        self.assertLess(
            disappearing.index("_api36ProofAttachmentPublished = false;"),
            disappearing.index("base.OnDisappearing();"),
        )
        self.assertNotIn("_api36ProofPageLoaded", page)
        self.assertEqual(1, loaded.count("TryPublishApi36AttachmentProof();"))
        self.assertIn("_api36ProofAttachmentPublished = false;", refresh)
        self.assertIn("_latestApi36ProofReadyState = null;", refresh)
        self.assertIn("_latestApi36ProofReadyState = state;", refresh)
        self.assertIn("TryPublishApi36AttachmentProof();", refresh)
        self.assertLess(
            refresh.index("_api36ProofAttachmentPublished = false;"),
            refresh.index("ResolveCurrentAuthority()"),
        )
        self.assertLess(
            refresh.index("_latestApi36ProofReadyState = state;"),
            refresh.index("TryPublishApi36AttachmentProof();"),
        )
        self.assertIn("_api36ProofAttachmentPublished", publication_latch)
        publication = publication_latch.index(
            "if (Api36ProofStatePublisher.TryPublishCreationPrerequisiteAttachment("
        )
        latched = publication_latch.index("_api36ProofAttachmentPublished = true;")
        for prerequisite in (
            "if (_api36ProofAttachmentPublished",
            "!_api36ProofRouteAppeared",
            "!IsLoaded",
            "Handler is null",
            "Window is null",
            "navigationStack.Count < 2",
            "!ReferenceEquals(navigationStack[^1], this)",
            "navigationStack.Count(candidate => ReferenceEquals(candidate, this)) != 1",
            "_latestApi36ProofReadyState is not { } state",
            "CreationPrerequisitePhoneAuthority.IsReady(state, Coordinator.State)",
        ):
            self.assertLess(publication_latch.index(prerequisite), publication)
        self.assertLess(publication, latched)
        self.assertEqual(
            1,
            page.count("_api36ProofAttachmentPublished = true;"),
        )
        self.assertEqual(
            2,
            page.count("_api36ProofAttachmentPublished = false;"),
        )
        self.assertNotIn("_api36ProofAttachmentPublicationAttempted", page)
        for forbidden in (
            "Task.Delay",
            "while (",
            "Dispatcher.Dispatch",
            "Navigation.Push",
            "Navigation.Pop",
            'device.shell("input", "tap"',
            "PublishApi36AttachmentProofOnceLoaded",
        ):
            self.assertNotIn(
                forbidden,
                appearing + navigated + disappearing + loaded + publication_latch,
            )

    def test_creation_prerequisite_attachment_edges_cover_all_signal_permutations(self) -> None:
        page = (ROOT / "src/Chummer.Android/Native/CreationPrerequisitePage.cs").read_text(
            encoding="utf-8"
        )
        edges = {
            "appearing": page[
                page.index("protected override void OnAppearing()") :
                page.index("protected override void OnNavigatedTo(")
            ],
            "navigated": page[
                page.index("protected override void OnNavigatedTo(") :
                page.index("protected override void OnDisappearing()")
            ],
            "loaded": page[
                page.index("private void OnApi36ProofLoaded(") :
                page.index("private void TryPublishApi36AttachmentProof()")
            ],
            "ready": page[
                page.index("protected override void Refresh()") :
                page.index("private CharacterCreationFoundationResult<")
            ],
        }
        for name, edge in edges.items():
            with self.subTest(edge=name):
                self.assertEqual(1, edge.count("TryPublishApi36AttachmentProof();"))

        # Each edge updates one prerequisite before invoking the same exact,
        # idempotent gate. Whichever signal arrives last must therefore be able
        # to publish once; no ordering depends on a timer or repeated action.
        for ordering in itertools.permutations(edges):
            observed = {name: False for name in edges}
            published = False
            publication_count = 0
            for edge in ordering:
                observed[edge] = True
                if not published and all(observed.values()):
                    published = True
                    publication_count += 1
            with self.subTest(ordering=ordering):
                self.assertTrue(published)
                self.assertEqual(1, publication_count)

    def test_gate_scope_is_unchanged(self) -> None:
        gate = json.loads((ROOT / "eng/api36-sr5-wizard-gate-authority.json").read_text(encoding="utf-8"))
        self.assertEqual(7, gate["requiredJourneyCount"])
        self.assertFalse(gate["publicationAuthorized"])
        self.assertIn("full_editing_pass", gate["doesNotAssert"])
        self.assertIn("tablet_readiness", gate["doesNotAssert"])
        self.assertEqual(
            [
                {
                    "matrixJourney": "full-editing",
                    "status": "deferred",
                    "evidenceClass": "informational_only",
                    "maySatisfyRequiredJourney": False,
                }
            ],
            gate["excludedFromGate"],
        )

    def test_table_proof_rejects_hidden_or_disappearing_page_writers(self) -> None:
        publisher = (
            ROOT / "src/Chummer.Android/Proof/Api36ProofStatePublisher.cs"
        ).read_text(encoding="utf-8")
        entry = publisher[
            publisher.index("public static void TryPublishTableWizard(") :
            publisher.index("public void PublishTableWizard(")
        ]

        visibility_guard = "!ReferenceEquals(Shell.Current?.CurrentPage, page)"
        service_lookup = "IPlatformApplication.Current?.Services"
        self.assertEqual(1, entry.count(visibility_guard))
        self.assertLess(entry.index(visibility_guard), entry.index(service_lookup))


if __name__ == "__main__":
    unittest.main()
