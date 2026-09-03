from __future__ import annotations

import copy
import hashlib
import importlib.util
import json
from pathlib import Path
import subprocess
import tempfile
import unittest
import zipfile


REPO = Path(__file__).resolve().parents[1]
SCRIPT = REPO / "scripts/materialize-api36-two-green-eligibility.py"
SPEC = importlib.util.spec_from_file_location("api36_two_green", SCRIPT)
assert SPEC is not None and SPEC.loader is not None
gate = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(gate)
WORKFLOW = REPO / ".github/workflows/api36-two-consecutive-green.yml"


def sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


class Api36TwoGreenEligibilityTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name).resolve()
        self.android = self.root / "android"
        self.source_workflow = self.android / gate.WORKFLOW_PATH
        self.source_workflow.parent.mkdir(parents=True)
        self.source_workflow.write_bytes(
            gate.SOURCE_WORKFLOW.read_bytes()
        )
        self.git("init", "--quiet")
        self.git("config", "user.name", "Two Green Test")
        self.git("config", "user.email", "test@example.invalid")
        self.git("add", gate.WORKFLOW_PATH)
        self.git("commit", "--quiet", "-m", "exact common tree")
        self.tree = self.git("rev-parse", "HEAD^{tree}")
        self.commit = self.git("rev-parse", "HEAD")
        self.policy = self.root / "policy.json"
        self.policy.write_bytes(gate.canonical_json_bytes(gate.expected_policy()))
        self.output = self.root / gate.OUTPUT_NAME
        self.inputs: dict[str, object] = {
            "android_root": self.android,
            "policy": self.policy,
            "source_workflow": self.source_workflow,
            "review_run_id": 100,
            "main_run_id": 200,
        }
        self.make_run(
            "review",
            run_id=100,
            event="pull_request",
            branch="feature/two-green",
            head_sha="a" * 40,
            created="2026-09-03T10:00:00Z",
            started="2026-09-03T10:01:00Z",
            completed="2026-09-03T10:30:00Z",
        )
        self.make_run(
            "main",
            run_id=200,
            event="push",
            branch="main",
            head_sha=self.commit,
            created="2026-09-03T11:00:00Z",
            started="2026-09-03T11:01:00Z",
            completed="2026-09-03T11:30:00Z",
        )

    def tearDown(self) -> None:
        self.temporary.cleanup()

    def git(self, *arguments: str) -> str:
        completed = subprocess.run(
            ["git", "-C", str(self.android), *arguments],
            check=True,
            capture_output=True,
            text=True,
        )
        return completed.stdout.strip()

    @staticmethod
    def write_json(path: Path, value: object) -> None:
        path.write_bytes(gate.canonical_json_bytes(value))

    def aggregate(self, run_id: int, attempt: int = 1) -> dict[str, object]:
        wizard_gate = gate.contract_binding()
        journey_compatibility = "9" * 64
        journeys = {
            matrix: {
                "status": "pass",
                "driverJourney": specification[0],
                "receiptSha256": sha256(matrix.encode()),
            }
            for matrix, specification in gate.journey_map().items()
        }
        return {
            "schema": gate.AGGREGATE_SCHEMA,
            "status": "pass",
            "generatedAtUtc": "2026-09-03T10:30:00+00:00",
            "authorityClass": gate.AUTHORITY_CLASS,
            "proofScope": gate.PROOF_SCOPE,
            "publicationAuthorized": False,
            "gateAuthority": wizard_gate,
            "artifactAuthority": {
                "schema": "chummer.android.api36-apk-authority/v1",
                "runId": run_id,
                "artifactId": str(run_id + 10),
                "artifactDigest": "sha256:" + "e" * 64,
                "artifactName": f"chummer-android-api36-x64-debug-{run_id}-{attempt}",
                "artifactAttempt": attempt,
                "apkSha256": "f" * 64,
            },
            "environmentAuthority": {
                "policyAuthority": {
                    "schema": "chummer.android.api36-proof-environment-authority/v2",
                    "sha256": "6" * 64,
                    "sizeBytes": 1000,
                },
                "build": {
                    "receiptSha256": sha256(f"build-{run_id}".encode()),
                    "environmentSha256": sha256(f"environment-{run_id}".encode()),
                    "compatibilitySha256": "7" * 64,
                },
                "journeyCompatibilitySha256": journey_compatibility,
                "journeys": {
                    matrix: {
                        "receiptSha256": sha256(f"{run_id}-{matrix}-receipt".encode()),
                        "environmentSha256": sha256(f"{run_id}-{matrix}-environment".encode()),
                        "compatibilitySha256": journey_compatibility,
                    }
                    for matrix in journeys
                },
            },
            "requiredJourneyCount": len(journeys),
            "requiredJourneys": list(journeys),
            "journeyCount": len(journeys),
            "journeys": journeys,
        }

    def p0(
        self,
        *,
        run_id: int,
        event: str,
        head_sha: str,
        aggregate_bytes: bytes,
        attempt: int = 1,
    ) -> dict[str, object]:
        rows = [
            {
                "matrixJourney": matrix,
                "driverJourney": specification[0],
                "status": "pass",
                "receiptSha256": sha256(matrix.encode()),
            }
            for matrix, specification in gate.journey_map().items()
        ]
        unsigned = {
            "schema": gate.P0_SCHEMA,
            "status": "pass",
            "authorityClass": gate.AUTHORITY_CLASS,
            "proofScope": gate.PROOF_SCOPE,
            "publicationAuthorized": False,
            "humanPullRequestBodyAuthoritative": False,
            "githubRun": {
                "attempt": attempt,
                "baseSha": "b" * 40,
                "eventName": event,
                "eventSha": self.commit,
                "headSha": head_sha,
                "id": run_id,
            },
            "androidSource": {
                "checkedOutHead": self.commit,
                "checkedOutTree": self.tree,
                "repository": "https://github.com/ArchonMegalon/chummer-android.git",
            },
            "dependencyGraph": {
                name: {"commit": commit, "repository": f"https://example.invalid/{name}.git", "tree": "c" * 40}
                for name, commit in sorted(gate.P0.EXPECTED_DEPENDENCY_COMMITS.items())
            },
            "workflow": {
                "path": gate.WORKFLOW_PATH,
                "sha256": sha256(self.source_workflow.read_bytes()),
                "sizeBytes": self.source_workflow.stat().st_size,
            },
            "apks": {
                "android-x64": {"sha256": "f" * 64},
                "android-arm64": {"sha256": "e" * 64},
            },
            "requiredJourneyCount": len(rows),
            "journeys": rows,
            "aggregate": {
                "path": "receipt.json",
                "sha256": sha256(aggregate_bytes),
                "sizeBytes": len(aggregate_bytes),
                "schema": gate.AGGREGATE_SCHEMA,
                "status": "pass",
            },
            "inputs": {
                "hostedCandidate": {"sha256": "d" * 64},
                "wizardGate": gate.contract_binding(),
            },
            "doesNotAssert": list(gate.P0.DOES_NOT_ASSERT),
        }
        return {**unsigned, "authoritySha256": gate.P0.canonical_sha256(unsigned)}

    @staticmethod
    def archive(path: Path, member: str, data: bytes) -> None:
        with zipfile.ZipFile(path, "w", compression=zipfile.ZIP_DEFLATED) as bundle:
            bundle.writestr(member, data)

    def make_run(
        self,
        role: str,
        *,
        run_id: int,
        event: str,
        branch: str,
        head_sha: str,
        created: str,
        started: str,
        completed: str,
    ) -> None:
        root = self.root / role
        root.mkdir()
        attempt = 1
        run = {
            "id": run_id,
            "run_attempt": attempt,
            "workflow_id": 4242,
            "name": gate.WORKFLOW_NAME,
            "path": gate.WORKFLOW_PATH,
            "event": event,
            "status": "completed",
            "conclusion": "success",
            "head_branch": branch,
            "head_sha": head_sha,
            "created_at": created,
            "run_started_at": started,
            "updated_at": completed,
            "repository": {"full_name": gate.REPOSITORY},
            "pull_requests": [{"number": 32}] if event == "pull_request" else [],
        }
        jobs = {
            "total_count": len(gate.REQUIRED_JOB_NAMES),
            "jobs": [
                {
                    "id": run_id * 100 + index,
                    "run_id": run_id,
                    "run_attempt": attempt,
                    "workflow_name": gate.WORKFLOW_NAME,
                    "name": name,
                    "status": "completed",
                    "conclusion": "success",
                    "started_at": started,
                    "completed_at": completed,
                }
                for index, name in enumerate(gate.REQUIRED_JOB_NAMES, start=1)
            ],
        }
        aggregate_bytes = gate.canonical_json_bytes(self.aggregate(run_id, attempt))
        p0_bytes = gate.canonical_json_bytes(
            self.p0(
                run_id=run_id,
                event=event,
                head_sha=head_sha,
                aggregate_bytes=aggregate_bytes,
                attempt=attempt,
            )
        )
        aggregate_archive = root / "aggregate.zip"
        p0_archive = root / "p0.zip"
        self.archive(aggregate_archive, "receipt.json", aggregate_bytes)
        self.archive(p0_archive, gate.P0.OUTPUT_NAME, p0_bytes)
        artifacts = []
        for artifact_id, name, archive in (
            (
                run_id + 1,
                f"chummer-android-api36-phone-sr5-wizard-aggregate-{run_id}-{attempt}",
                aggregate_archive,
            ),
            (
                run_id + 2,
                f"chummer-android-p0-pr-authority-{run_id}-{attempt}",
                p0_archive,
            ),
        ):
            archive_bytes = archive.read_bytes()
            artifacts.append(
                {
                    "id": artifact_id,
                    "name": name,
                    "size_in_bytes": len(archive_bytes),
                    "digest": "sha256:" + sha256(archive_bytes),
                    "expired": False,
                    "created_at": completed,
                    "expires_at": "2026-10-03T11:30:00Z",
                    "workflow_run": {"id": run_id, "head_sha": head_sha},
                }
            )
        paths = {
            "run": root / "run.json",
            "jobs": root / "jobs.json",
            "artifacts": root / "artifacts.json",
            "aggregate_archive": aggregate_archive,
            "p0_archive": p0_archive,
        }
        self.write_json(paths["run"], run)
        self.write_json(paths["jobs"], jobs)
        self.write_json(
            paths["artifacts"], {"total_count": len(artifacts), "artifacts": artifacts}
        )
        for name, path in paths.items():
            self.inputs[f"{role}_{name}"] = path

    def create(self) -> dict[str, object]:
        return gate.create_authority(**self.inputs)

    def rewrite_proof(
        self,
        role: str,
        aggregate: dict[str, object],
        *,
        mutate_p0=None,
    ) -> None:
        run = json.loads(self.inputs[f"{role}_run"].read_text())
        aggregate_bytes = gate.canonical_json_bytes(aggregate)
        p0 = self.p0(
            run_id=run["id"],
            event=run["event"],
            head_sha=run["head_sha"],
            aggregate_bytes=aggregate_bytes,
            attempt=run["run_attempt"],
        )
        if mutate_p0 is not None:
            mutate_p0(p0)
            unsigned = {key: value for key, value in p0.items() if key != "authoritySha256"}
            p0["authoritySha256"] = gate.P0.canonical_sha256(unsigned)
        aggregate_archive = self.inputs[f"{role}_aggregate_archive"]
        p0_archive = self.inputs[f"{role}_p0_archive"]
        self.archive(aggregate_archive, "receipt.json", aggregate_bytes)
        self.archive(p0_archive, gate.P0.OUTPUT_NAME, gate.canonical_json_bytes(p0))
        artifacts_path = self.inputs[f"{role}_artifacts"]
        artifacts = json.loads(artifacts_path.read_text())
        for kind, archive in (("aggregate", aggregate_archive), ("p0", p0_archive)):
            prefix = (
                "chummer-android-api36-phone-sr5-wizard-aggregate"
                if kind == "aggregate"
                else "chummer-android-p0-pr-authority"
            )
            row = next(item for item in artifacts["artifacts"] if item["name"].startswith(prefix))
            data = archive.read_bytes()
            row["size_in_bytes"] = len(data)
            row["digest"] = "sha256:" + sha256(data)
        self.write_json(artifacts_path, artifacts)

    def test_two_exact_runs_materialize_eligibility_without_publication(self) -> None:
        result = self.create()
        self.assertTrue(result["eligible"])
        self.assertTrue(result["internalTestingEligible"])
        self.assertFalse(result["publicationAuthorized"])
        self.assertFalse(result["googlePlayUploadAuthorized"])
        self.assertEqual(self.tree, result["sourceTree"])
        self.assertEqual("pull_request", result["reviewRun"]["run"]["event"])
        self.assertEqual("push", result["mainRun"]["run"]["event"])
        self.assertEqual(gate.REQUIRED_JOB_NAMES, tuple(result["mainRun"]["jobs"]))
        self.assertEqual(
            result["reviewRun"]["artifacts"]["aggregate"]["memberSha256"],
            self.p0_aggregate_sha("review"),
        )
        gate.write_atomically(self.output, result)
        self.assertEqual(0, gate.main(["verify", *self.cli_inputs(), "--authority", str(self.output)]))

    def p0_aggregate_sha(self, role: str) -> str:
        with zipfile.ZipFile(self.inputs[f"{role}_p0_archive"]) as archive:
            p0 = json.loads(archive.read(gate.P0.OUTPUT_NAME))
        return p0["aggregate"]["sha256"]

    def cli_inputs(self) -> list[str]:
        arguments: list[str] = []
        for key, value in self.inputs.items():
            arguments.extend(("--" + key.replace("_", "-"), str(value)))
        return arguments

    def test_wrong_event_order_tree_and_environment_fail_closed(self) -> None:
        cases: list[tuple[str, str, callable]] = [
            ("review event", "review_run", lambda value: value.update({"event": "workflow_dispatch"})),
            ("main branch", "main_run", lambda value: value.update({"head_branch": "release"})),
            ("run conclusion", "main_run", lambda value: value.update({"conclusion": "failure"})),
            ("time ordering", "main_run", lambda value: value.update({"run_started_at": "2026-09-03T10:20:00Z"})),
        ]
        for label, key, mutate in cases:
            with self.subTest(label=label):
                path = self.inputs[key]
                original = path.read_bytes()
                value = json.loads(original)
                mutate(value)
                self.write_json(path, value)
                try:
                    with self.assertRaises(ValueError):
                        self.create()
                finally:
                    path.write_bytes(original)

        original_aggregate = self.inputs["main_aggregate_archive"].read_bytes()
        original_p0 = self.inputs["main_p0_archive"].read_bytes()
        original_artifacts = self.inputs["main_artifacts"].read_bytes()
        with zipfile.ZipFile(self.inputs["main_aggregate_archive"], "r") as archive:
            aggregate = json.loads(archive.read("receipt.json"))
        aggregate["environmentAuthority"]["journeyCompatibilitySha256"] = "1" * 64
        for row in aggregate["environmentAuthority"]["journeys"].values():
            row["compatibilitySha256"] = "1" * 64
        self.rewrite_proof("main", aggregate)
        try:
            with self.assertRaisesRegex(ValueError, "environment compatibility differs"):
                self.create()
        finally:
            self.inputs["main_aggregate_archive"].write_bytes(original_aggregate)
            self.inputs["main_p0_archive"].write_bytes(original_p0)
            self.inputs["main_artifacts"].write_bytes(original_artifacts)

        with zipfile.ZipFile(self.inputs["main_aggregate_archive"], "r") as archive:
            aggregate = json.loads(archive.read("receipt.json"))
        self.rewrite_proof(
            "main",
            aggregate,
            mutate_p0=lambda value: value["androidSource"].update(
                {"checkedOutTree": "0" * 40}
            ),
        )
        try:
            with self.assertRaisesRegex(ValueError, "tree/aggregate authority differs"):
                self.create()
        finally:
            self.inputs["main_aggregate_archive"].write_bytes(original_aggregate)
            self.inputs["main_p0_archive"].write_bytes(original_p0)
            self.inputs["main_artifacts"].write_bytes(original_artifacts)

    def test_jobs_artifact_metadata_and_archive_tampering_fail_closed(self) -> None:
        for key, mutate in (
            ("review_jobs", lambda value: value["jobs"][0].update({"conclusion": "failure"})),
            ("review_jobs", lambda value: value.update({"total_count": 100})),
            ("review_artifacts", lambda value: value["artifacts"][0].update({"expired": True})),
            ("review_artifacts", lambda value: value["artifacts"][0].update({"digest": "sha256:" + "0" * 64})),
        ):
            path = self.inputs[key]
            original = path.read_bytes()
            value = json.loads(original)
            mutate(value)
            self.write_json(path, value)
            try:
                with self.assertRaises(ValueError):
                    self.create()
            finally:
                path.write_bytes(original)

        archive_path = self.inputs["review_p0_archive"]
        original = archive_path.read_bytes()
        self.archive(archive_path, "foreign.json", b"{}\n")
        try:
            with self.assertRaises(ValueError):
                self.create()
        finally:
            archive_path.write_bytes(original)

    def test_duplicate_json_and_claim_escalation_fail_closed(self) -> None:
        path = self.inputs["review_run"]
        original = path.read_bytes()
        path.write_bytes(original[:-2] + b',"status":"completed"}\n')
        try:
            with self.assertRaisesRegex(ValueError, "duplicate JSON key"):
                self.create()
        finally:
            path.write_bytes(original)
        authority = self.create()
        for field in ("publicationAuthorized", "googlePlayUploadAuthorized"):
            tampered = copy.deepcopy(authority)
            tampered[field] = True
            unsigned = {key: value for key, value in tampered.items() if key != "eligibilitySha256"}
            tampered["eligibilitySha256"] = gate.canonical_sha256(unsigned)
            with self.assertRaisesRegex(ValueError, "posture"):
                gate.validate_authority(tampered)

    def test_policy_has_no_live_run_ids_and_remains_nonpublication(self) -> None:
        policy = gate.expected_policy()
        encoded = gate.canonical_json_bytes(policy).decode()
        self.assertNotRegex(encoded, r'"(?:review|main)RunId"')
        self.assertFalse(policy["publicationAuthorized"])
        self.assertTrue(policy["internalTestingEligibleWhenSatisfied"])


class Api36TwoGreenWorkflowSourceTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.text = WORKFLOW.read_text(encoding="utf-8")

    def test_workflow_is_manual_read_only_and_main_ref_bound(self) -> None:
        self.assertIn("workflow_dispatch:", self.text)
        self.assertIn("review_run_id:", self.text)
        self.assertIn("main_run_id:", self.text)
        self.assertIn("actions: read", self.text)
        self.assertIn("contents: read", self.text)
        self.assertNotIn("actions: write", self.text)
        self.assertNotIn("contents: write", self.text)
        self.assertIn('test "$GITHUB_REF" = refs/heads/main', self.text)

    def test_workflow_queries_explicit_runs_and_never_uploads_to_play(self) -> None:
        self.assertIn("actions/runs/$run_id", self.text)
        self.assertIn("attempts/$run_attempt/jobs?per_page=100", self.text)
        self.assertIn("actions/artifacts/$artifact_id/zip", self.text)
        self.assertNotIn("google-github-actions/auth", self.text)
        self.assertNotIn("playDeveloper", self.text)
        self.assertNotIn("serviceAccount", self.text)
        self.assertNotIn("gradle-play-publisher", self.text)
        self.assertIn("publicationAuthorized == false", self.text)
        self.assertIn("googlePlayUploadAuthorized == false", self.text)


if __name__ == "__main__":
    unittest.main()
