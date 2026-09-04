from __future__ import annotations

import copy
import hashlib
import importlib.util
import json
from pathlib import Path
import re
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
        self.git("commit", "--quiet", "-m", "base for realistic merge identities")
        self.tree = self.git("rev-parse", "HEAD^{tree}")
        self.base_commit = self.git("rev-parse", "HEAD")
        self.pr_head = self.git(
            "commit-tree", self.tree, "-p", self.base_commit,
            "-m", "PR head with the exact candidate tree",
        )
        self.review_merge_commit = self.git(
            "commit-tree", self.tree,
            "-p", self.base_commit,
            "-p", self.pr_head,
            "-m", f"Merge {self.pr_head} into {self.base_commit}",
        )
        self.main_merge_commit = self.git(
            "commit-tree", self.tree,
            "-p", self.base_commit,
            "-p", self.pr_head,
            "-m", "Merge pull request #32 from feature/two-green",
        )
        self.assertNotEqual(self.review_merge_commit, self.main_merge_commit)
        self.git("checkout", "--quiet", "--detach", self.main_merge_commit)
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
            head_sha=self.pr_head,
            event_sha=self.review_merge_commit,
            created="2026-09-03T10:00:00Z",
            started="2026-09-03T10:01:00Z",
            completed="2026-09-03T10:30:00Z",
        )
        self.make_run(
            "main",
            run_id=200,
            event="push",
            branch="main",
            head_sha=self.main_merge_commit,
            event_sha=self.main_merge_commit,
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
        event_sha: str,
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
                "baseSha": (
                    self.base_commit if event == "pull_request" else event_sha
                ),
                "eventName": event,
                "eventSha": event_sha,
                "headSha": head_sha,
                "id": run_id,
            },
            "androidSource": {
                "checkedOutHead": event_sha,
                "checkedOutTree": self.tree,
                "repository": "https://github.com/ArchonMegalon/chummer-android.git",
            },
            "dependencyGraph": self.dependency_graph(event_sha),
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

    def dependency_graph(self, android_commit: str) -> dict[str, object]:
        sources = {
            name: {
                "commit": (
                    android_commit
                    if name == "android"
                    else gate.P0.EXPECTED_DEPENDENCY_COMMITS[name]
                ),
                "repository": repository,
                "tree": self.tree if name == "android" else "c" * 40,
            }
            for name, repository in sorted(gate.P0.HOSTED.EXPECTED_SOURCES.items())
        }
        unsigned = {
            "mode": {"localCompatibilityTree": True, "packageOnly": False},
            "sources": sources,
        }
        return {**unsigned, "sha256": gate.canonical_sha256(unsigned)}

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
        event_sha: str,
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
                event_sha=event_sha,
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

    def read_p0(self, role: str) -> dict[str, object]:
        with zipfile.ZipFile(self.inputs[f"{role}_p0_archive"]) as archive:
            return json.loads(archive.read(gate.P0.OUTPUT_NAME))

    @staticmethod
    def rehash_dependency_graph(p0: dict[str, object]) -> None:
        graph = p0["dependencyGraph"]
        unsigned = {"mode": graph["mode"], "sources": graph["sources"]}
        graph["sha256"] = gate.canonical_sha256(unsigned)

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
            event_sha=(
                self.review_merge_commit
                if role == "review"
                else self.main_merge_commit
            ),
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
        self.assertEqual(
            self.review_merge_commit,
            self.read_p0("review")["androidSource"]["checkedOutHead"],
        )
        self.assertEqual(
            self.main_merge_commit,
            self.read_p0("main")["androidSource"]["checkedOutHead"],
        )
        self.assertNotEqual(self.review_merge_commit, self.main_merge_commit)
        common_android = result["commonAuthority"]["dependencyGraph"]["sources"]["android"]
        self.assertEqual(
            {
                "repository": gate.P0.HOSTED.EXPECTED_SOURCES["android"],
                "tree": self.tree,
            },
            common_android,
        )
        self.assertNotIn("commit", common_android)
        self.assertEqual(gate.REQUIRED_JOB_NAMES, tuple(result["mainRun"]["jobs"]))
        self.assertEqual(
            result["reviewRun"]["artifacts"]["aggregate"]["memberSha256"],
            self.p0_aggregate_sha("review"),
        )
        self.assertEqual(
            0,
            gate.main(
                ["materialize", *self.cli_inputs(), "--output", str(self.output)]
            ),
        )
        self.assertEqual(result, json.loads(self.output.read_text(encoding="utf-8")))
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

        def drift_android_tree(value: dict[str, object]) -> None:
            value["androidSource"]["checkedOutTree"] = "0" * 40
            value["dependencyGraph"]["sources"]["android"]["tree"] = "0" * 40
            self.rehash_dependency_graph(value)

        self.rewrite_proof("main", aggregate, mutate_p0=drift_android_tree)
        try:
            with self.assertRaisesRegex(ValueError, "tree/aggregate authority differs"):
                self.create()
        finally:
            self.inputs["main_aggregate_archive"].write_bytes(original_aggregate)
            self.inputs["main_p0_archive"].write_bytes(original_p0)
            self.inputs["main_artifacts"].write_bytes(original_artifacts)

        def drift_non_android_dependency(value: dict[str, object]) -> None:
            value["dependencyGraph"]["sources"]["presentation"]["commit"] = "0" * 40
            self.rehash_dependency_graph(value)

        self.rewrite_proof(
            "main",
            aggregate,
            mutate_p0=drift_non_android_dependency,
        )
        try:
            with self.assertRaisesRegex(
                ValueError,
                "dependency commit differs: presentation",
            ):
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
        self.assertEqual(
            "reviewed_green_followed_later_by_main_green_not_run_adjacency",
            policy["sequenceSemantics"],
        )
        self.assertIn("zero_intervening_workflow_runs", policy["doesNotAssert"])

    def test_cli_rejects_symlinked_output_and_authority_before_resolution(self) -> None:
        output_target = self.root / "output-target.json"
        output_target.write_text("{}\n", encoding="utf-8")
        output_link = self.root / gate.OUTPUT_NAME
        output_link.symlink_to(output_target)
        with self.assertRaisesRegex(ValueError, "absolute non-symlink"):
            gate.main(["materialize", *self.cli_inputs(), "--output", str(output_link)])

        output_link.unlink()
        authority = self.create()
        gate.write_atomically(self.output, authority)
        authority_link = self.root / "authority-link.json"
        authority_link.symlink_to(self.output)
        with self.assertRaisesRegex(ValueError, "absolute canonical non-symlink"):
            gate.main(["verify", *self.cli_inputs(), "--authority", str(authority_link)])


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

    def test_workflow_dispatch_inputs_never_interpolate_inside_run_scripts(self) -> None:
        run_blocks: list[str] = []
        lines = self.text.splitlines()
        index = 0
        while index < len(lines):
            match = re.fullmatch(r"(\s*)run:\s*\|\s*", lines[index])
            if match is None:
                index += 1
                continue
            indentation = len(match.group(1))
            index += 1
            body: list[str] = []
            while index < len(lines):
                line = lines[index]
                if line.strip() and len(line) - len(line.lstrip()) <= indentation:
                    break
                body.append(line)
                index += 1
            run_blocks.append("\n".join(body))
        self.assertTrue(run_blocks)
        for body in run_blocks:
            self.assertNotRegex(body, re.escape("${{ inputs."))
        self.assertIn("REVIEW_RUN_ID: ${{ inputs.review_run_id }}", self.text)
        self.assertIn("MAIN_RUN_ID: ${{ inputs.main_run_id }}", self.text)


if __name__ == "__main__":
    unittest.main()
