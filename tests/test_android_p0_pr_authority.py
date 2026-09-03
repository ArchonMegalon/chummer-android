from __future__ import annotations

import hashlib
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest


REPO = Path(__file__).resolve().parents[1]
SCRIPT = REPO / "scripts/materialize-android-p0-pr-authority.py"
SPEC = importlib.util.spec_from_file_location("android_p0_pr_authority", SCRIPT)
assert SPEC is not None and SPEC.loader is not None
authority = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(authority)


def sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


class AndroidP0PrAuthorityTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name).resolve()
        self.android_root = self.root / "android"
        self.workflow = self.android_root / ".github/workflows/api36-editing-e2e.yml"
        self.workflow.parent.mkdir(parents=True)
        self.workflow.write_bytes(b"name: exact API 36 workflow\n")
        self.git("init", "--quiet")
        self.git("config", "user.email", "test@example.invalid")
        self.git("config", "user.name", "P0 Authority Test")
        self.git("remote", "add", "origin", authority.HOSTED.EXPECTED_SOURCES["android"])
        self.git("add", authority.WORKFLOW_RELATIVE_PATH)
        self.git("commit", "--quiet", "-m", "exact source")
        self.x64_apk = self.root / "chummer-android-x64-debug.apk"
        self.x64_apk.write_bytes(b"exact x64 apk bytes")
        self.arm64_apk = self.root / "chummer-android-arm64-debug.apk"
        self.arm64_apk.write_bytes(b"exact arm64 apk bytes")
        self.hosted = self.root / "hosted-build-candidate.json"
        self.aggregate = self.root / "receipt.json"
        self.output = self.root / authority.OUTPUT_NAME
        self.write_json(self.hosted, self.hosted_payload())
        self.write_json(self.aggregate, self.aggregate_payload())

    def tearDown(self) -> None:
        self.temporary.cleanup()

    @staticmethod
    def write_json(path: Path, value: object) -> None:
        path.write_bytes(authority.canonical_json_bytes(value))

    def git(self, *arguments: str) -> str:
        completed = subprocess.run(
            ["git", "-C", str(self.android_root), *arguments],
            check=True,
            capture_output=True,
            text=True,
        )
        return completed.stdout.strip()

    def hosted_payload(self) -> dict[str, object]:
        sources: dict[str, object] = {}
        dependency_index = 1
        for name, repository in authority.HOSTED.EXPECTED_SOURCES.items():
            if name == "android":
                commit = self.git("rev-parse", "HEAD")
                tree = self.git("rev-parse", "HEAD^{tree}")
            else:
                commit = authority.EXPECTED_DEPENDENCY_COMMITS[name]
                tree = f"{dependency_index:x}" * 40
            sources[name] = {
                "commit": commit,
                "repository": repository,
                "tree": tree,
            }
            dependency_index += 1
        unsigned = {
            "contractName": authority.HOSTED.CONTRACT,
            "status": "candidate",
            "evidenceClass": "hosted_debug_build_observation",
            "releaseAttested": False,
            "publicationAuthorized": False,
            "physicalDeviceTested": False,
            "releaseEligible": False,
            "githubRun": {
                "attempt": 2,
                "baseSha": "a" * 40,
                "eventName": "pull_request",
                "eventSha": self.git("rev-parse", "HEAD"),
                "headSha": "b" * 40,
                "id": 424242,
            },
            "dependencyMode": {
                "localCompatibilityTree": True,
                "packageOnly": False,
            },
            "build": {
                "applicationId": authority.HOSTED.APPLICATION_ID,
                "configuration": authority.HOSTED.CONFIGURATION,
                "runtimeIdentifier": authority.HOSTED.RUNTIME,
                "targetFramework": authority.HOSTED.TARGET_FRAMEWORK,
            },
            "sources": {name: sources[name] for name in sorted(sources)},
            "artifact": {
                "apkAbis": ["arm64-v8a"],
                "fileName": self.arm64_apk.name,
                "sha256": sha256(self.arm64_apk.read_bytes()),
                "sizeBytes": self.arm64_apk.stat().st_size,
            },
            "canonicalContentReceipt": {
                "contractName": "chummer.android.content-bundle/v1",
                "coreRevision": "c06f22c185c7b733637fdb76b3cf333f31716781",
                "sha256": "c" * 64,
                "sizeBytes": 100,
                "status": "pass",
            },
            "reviewedInputs": {
                "buildScript": {
                    "path": "build-debug.sh",
                    "sha256": "d" * 64,
                    "sizeBytes": 200,
                },
                "workflow": {
                    "path": self.workflow.name,
                    "sha256": sha256(self.workflow.read_bytes()),
                    "sizeBytes": self.workflow.stat().st_size,
                },
            },
            "doesNotAssert": list(authority.HOSTED.DOES_NOT_ASSERT),
        }
        return {
            **unsigned,
            "observationSha256": authority.HOSTED.canonical_sha256(unsigned),
        }

    def aggregate_payload(self) -> dict[str, object]:
        gate = authority.contract_binding()
        journeys = {
            matrix_journey: {
                "status": "pass",
                "driverJourney": specification[0],
                "receiptSha256": sha256(matrix_journey.encode()),
            }
            for matrix_journey, specification in authority.journey_map().items()
        }
        return {
            "schema": authority.AGGREGATE_SCHEMA,
            "status": "pass",
            "generatedAtUtc": "2026-09-03T00:00:00+00:00",
            "authorityClass": authority.AUTHORITY_CLASS,
            "proofScope": authority.PROOF_SCOPE,
            "publicationAuthorized": False,
            "gateAuthority": gate,
            "artifactAuthority": {
                "schema": "chummer.android.api36-apk-authority/v1",
                "runId": 424242,
                "artifactId": "31337",
                "artifactDigest": "sha256:" + "e" * 64,
                "artifactName": "chummer-android-api36-x64-debug-424242-2",
                "artifactAttempt": 2,
                "apkSha256": sha256(self.x64_apk.read_bytes()),
            },
            "requiredJourneyCount": len(journeys),
            "requiredJourneys": list(journeys),
            "journeyCount": len(journeys),
            "journeys": journeys,
        }

    def inputs(self) -> dict[str, Path]:
        return {
            "android_root": self.android_root,
            "hosted_candidate": self.hosted,
            "aggregate": self.aggregate,
            "workflow": self.workflow,
            "x64_apk": self.x64_apk,
            "arm64_apk": self.arm64_apk,
        }

    def test_materialize_is_deterministic_and_verify_replays_every_input(self) -> None:
        expected = authority.create_authority(**self.inputs())
        authority.validate_authority(expected)
        authority.write_atomically(self.output, expected)
        first = self.output.read_bytes()
        authority.write_atomically(
            self.output, authority.create_authority(**self.inputs())
        )
        self.assertEqual(first, self.output.read_bytes())
        payload = json.loads(first)
        self.assertEqual("pass", payload["status"])
        self.assertFalse(payload["publicationAuthorized"])
        self.assertFalse(payload["humanPullRequestBodyAuthoritative"])
        self.assertEqual(self.git("rev-parse", "HEAD"), payload["androidSource"]["checkedOutHead"])
        self.assertEqual(self.git("rev-parse", "HEAD^{tree}"), payload["androidSource"]["checkedOutTree"])
        self.assertEqual(7, payload["requiredJourneyCount"])
        self.assertEqual(
            ["pass"] * 7, [row["status"] for row in payload["journeys"]]
        )
        self.assertEqual(
            sha256(self.x64_apk.read_bytes()), payload["apks"]["android-x64"]["sha256"]
        )
        self.assertEqual(
            sha256(self.arm64_apk.read_bytes()), payload["apks"]["android-arm64"]["sha256"]
        )
        arguments = [
            "verify",
            "--android-root", str(self.android_root),
            "--hosted-candidate", str(self.hosted),
            "--aggregate", str(self.aggregate),
            "--workflow", str(self.workflow),
            "--x64-apk", str(self.x64_apk),
            "--arm64-apk", str(self.arm64_apk),
            "--authority", str(self.output),
        ]
        self.assertEqual(0, authority.main(arguments))

    def test_claim_graph_workflow_apk_run_and_journey_tampering_fail_closed(self) -> None:
        hostile_cases = []
        hosted_publication = self.hosted_payload()
        hosted_publication["publicationAuthorized"] = True
        hostile_cases.append(("publication", self.hosted, hosted_publication))
        wrong_dependency = self.hosted_payload()
        wrong_dependency["sources"]["presentation"]["commit"] = "f" * 40
        unsigned = {key: value for key, value in wrong_dependency.items() if key != "observationSha256"}
        wrong_dependency["observationSha256"] = authority.HOSTED.canonical_sha256(unsigned)
        hostile_cases.append(("dependency", self.hosted, wrong_dependency))
        wrong_android_tree = self.hosted_payload()
        wrong_android_tree["sources"]["android"]["tree"] = "f" * 40
        unsigned = {
            key: value
            for key, value in wrong_android_tree.items()
            if key != "observationSha256"
        }
        wrong_android_tree["observationSha256"] = authority.HOSTED.canonical_sha256(
            unsigned
        )
        hostile_cases.append(("Android tree", self.hosted, wrong_android_tree))
        failed_journey = self.aggregate_payload()
        failed_journey["journeys"]["before-run-edge"]["status"] = "failed"
        hostile_cases.append(("journey", self.aggregate, failed_journey))
        for label, path, value in hostile_cases:
            with self.subTest(label=label):
                original = path.read_bytes()
                try:
                    self.write_json(path, value)
                    with self.assertRaises(ValueError):
                        authority.create_authority(**self.inputs())
                finally:
                    path.write_bytes(original)

        for label, path in (
            ("workflow", self.workflow),
            ("x64 APK", self.x64_apk),
            ("ARM64 APK", self.arm64_apk),
        ):
            with self.subTest(label=label):
                original = path.read_bytes()
                try:
                    path.write_bytes(original + b"tamper")
                    with self.assertRaises(ValueError):
                        authority.create_authority(**self.inputs())
                finally:
                    path.write_bytes(original)

    def test_duplicate_json_symlink_and_authority_mutation_fail_closed(self) -> None:
        original = self.aggregate.read_bytes()
        self.aggregate.write_bytes(original[:-2] + b',"status":"pass"}\n')
        with self.assertRaisesRegex(ValueError, "duplicate JSON key"):
            authority.create_authority(**self.inputs())
        self.aggregate.write_bytes(original)

        link = self.root / "linked.apk"
        os.symlink(self.x64_apk, link)
        inputs = self.inputs()
        inputs["x64_apk"] = link
        with self.assertRaisesRegex(ValueError, "non-symlink"):
            authority.create_authority(**inputs)

        expected = authority.create_authority(**self.inputs())
        expected["publicationAuthorized"] = True
        with self.assertRaisesRegex(ValueError, "posture"):
            authority.validate_authority(expected)
        with self.assertRaisesRegex(ValueError, "output filename"):
            authority.write_atomically(self.root / "pr-body.json", expected)

    def test_workflow_publishes_only_the_generated_machine_artifact(self) -> None:
        workflow = (REPO / authority.WORKFLOW_RELATIVE_PATH).read_text(encoding="utf-8")
        aggregate_job = workflow[workflow.index("  phone-evidence-aggregate:") :]
        self.assertIn("needs.build.outputs.apk-artifact-id", aggregate_job)
        self.assertIn("needs.build.outputs.arm64-artifact-id", aggregate_job)
        self.assertIn("materialize-android-p0-pr-authority.py", aggregate_job)
        self.assertIn("materialize \"${common[@]}\"", aggregate_job)
        self.assertIn("verify \"${common[@]}\"", aggregate_job)
        self.assertIn(authority.OUTPUT_NAME, aggregate_job)
        for commit in authority.EXPECTED_DEPENDENCY_COMMITS.values():
            self.assertIn(commit, workflow)
        self.assertNotIn("gh pr", aggregate_job.lower())
        self.assertNotIn("pulls/", aggregate_job.lower())
        self.assertNotIn("issue_comment", aggregate_job.lower())
        gate = json.loads(
            (REPO / "eng/api36-sr5-wizard-gate-authority.json").read_text(encoding="utf-8")
        )
        self.assertEqual(7, gate["requiredJourneyCount"])
        self.assertFalse(gate["publicationAuthorized"])


if __name__ == "__main__":
    unittest.main()
