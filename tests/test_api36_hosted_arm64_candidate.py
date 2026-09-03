import copy
import importlib.util
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
import zipfile


REPO_ROOT = Path(__file__).resolve().parents[1]
SCRIPT = REPO_ROOT / "scripts" / "materialize-api36-hosted-arm64-candidate.py"
SPEC = importlib.util.spec_from_file_location("hosted_arm64_candidate", SCRIPT)
assert SPEC is not None and SPEC.loader is not None
candidate = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(candidate)
PHYSICAL_SPEC = importlib.util.spec_from_file_location(
    "physical_contract", REPO_ROOT / "scripts" / "api36_arm64_physical_contract.py"
)
assert PHYSICAL_SPEC is not None and PHYSICAL_SPEC.loader is not None
physical = importlib.util.module_from_spec(PHYSICAL_SPEC)
sys.modules[PHYSICAL_SPEC.name] = physical
PHYSICAL_SPEC.loader.exec_module(physical)


class Api36HostedArm64CandidateTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name).resolve()
        self.sources: list[list[str]] = []
        for name, repository in candidate.EXPECTED_SOURCES.items():
            source_root = self.root / name
            source_root.mkdir()
            self.git(source_root, "init", "--quiet")
            self.git(source_root, "config", "user.email", "test@example.invalid")
            self.git(source_root, "config", "user.name", "Hosted Candidate Test")
            (source_root / "source.txt").write_text(f"{name}\n", encoding="utf-8")
            self.git(source_root, "add", "source.txt")
            self.git(source_root, "commit", "--quiet", "-m", "source")
            self.git(source_root, "remote", "add", "origin", repository)
            commit = self.git(source_root, "rev-parse", "HEAD")
            self.sources.append([name, repository, commit, str(source_root)])
        self.android_commit = next(row[2] for row in self.sources if row[0] == "android")
        self.apk = self.root / "chummer-android-arm64-debug.apk"
        with zipfile.ZipFile(self.apk, "w") as archive:
            archive.writestr("AndroidManifest.xml", b"manifest")
            archive.writestr("lib/arm64-v8a/libmonodroid.so", b"arm64")
        self.apk_sha256 = candidate.hashlib.sha256(self.apk.read_bytes()).hexdigest()
        self.content_receipt = self.root / "canonical-content-receipt.json"
        self.content_receipt.write_text(
            json.dumps(
                {
                    "status": "pass",
                    "schema": "chummer.android.content-bundle/v1",
                    "coreRevision": "c06f22c185c7b733637fdb76b3cf333f31716781",
                    "apkVerified": True,
                    "apkSha256": self.apk_sha256,
                    "issues": [],
                },
                sort_keys=True,
            )
            + "\n",
            encoding="utf-8",
        )
        self.workflow = self.root / "api36-editing-e2e.yml"
        self.workflow.write_text("name: hosted candidate\n", encoding="utf-8")
        self.build_script = self.root / "build-debug.sh"
        self.build_script.write_text("#!/bin/sh\nexit 0\n", encoding="utf-8")
        self.output = self.root / "hosted-build-candidate.json"

    def tearDown(self) -> None:
        self.temporary.cleanup()

    @staticmethod
    def git(root: Path, *arguments: str) -> str:
        completed = subprocess.run(
            ["git", "-C", str(root), *arguments],
            check=True,
            capture_output=True,
            text=True,
        )
        return completed.stdout.strip()

    def keyword_arguments(self) -> dict[str, object]:
        return {
            "sources": self.sources,
            "runtime": "android-arm64",
            "application_id": "com.myexternalbrain.chummer",
            "apk": self.apk,
            "content_receipt": self.content_receipt,
            "workflow": self.workflow,
            "build_script": self.build_script,
            "event_name": "pull_request",
            "event_sha": self.android_commit,
            "head_sha": "a" * 40,
            "base_sha": "b" * 40,
            "run_id": "123",
            "run_attempt": "2",
        }

    def materialize_arguments(self) -> list[str]:
        arguments = ["materialize"]
        for row in self.sources:
            arguments.extend(("--source", *row))
        arguments.extend(
            (
                "--runtime", "android-arm64",
                "--application-id", "com.myexternalbrain.chummer",
                "--apk", str(self.apk),
                "--content-receipt", str(self.content_receipt),
                "--workflow", str(self.workflow),
                "--build-script", str(self.build_script),
                "--event-name", "pull_request",
                "--event-sha", self.android_commit,
                "--head-sha", "a" * 40,
                "--base-sha", "b" * 40,
                "--run-id", "123",
                "--run-attempt", "2",
                "--output", str(self.output),
            )
        )
        return arguments

    def test_materialize_and_verify_bind_sources_apk_runtime_and_nonclaims(self) -> None:
        self.assertEqual(0, candidate.main(self.materialize_arguments()))
        self.assertEqual(0o600, self.output.stat().st_mode & 0o777)
        payload = json.loads(self.output.read_text(encoding="utf-8"))
        self.assertEqual(candidate.CONTRACT, payload["contractName"])
        self.assertEqual("hosted_debug_build_observation", payload["evidenceClass"])
        self.assertFalse(payload["releaseAttested"])
        self.assertFalse(payload["publicationAuthorized"])
        self.assertFalse(payload["physicalDeviceTested"])
        self.assertFalse(payload["releaseEligible"])
        self.assertEqual(list(candidate.DOES_NOT_ASSERT), payload["doesNotAssert"])
        self.assertEqual(
            {"localCompatibilityTree": True, "packageOnly": False},
            payload["dependencyMode"],
        )
        self.assertEqual("com.myexternalbrain.chummer", payload["build"]["applicationId"])
        self.assertEqual("android-arm64", payload["build"]["runtimeIdentifier"])
        self.assertEqual(["arm64-v8a"], payload["artifact"]["apkAbis"])
        self.assertEqual(set(candidate.EXPECTED_SOURCES), set(payload["sources"]))
        self.assertEqual(self.android_commit, payload["githubRun"]["eventSha"])
        self.assertEqual(123, payload["githubRun"]["id"])
        self.assertEqual(2, payload["githubRun"]["attempt"])
        self.assertEqual(0, candidate.main(["verify", "--receipt", str(self.output)]))

    def test_merge_group_is_an_allowed_review_trigger(self) -> None:
        arguments = self.keyword_arguments()
        arguments["event_name"] = "merge_group"
        observation = candidate.create_observation(**arguments)
        self.assertEqual("merge_group", observation["githubRun"]["eventName"])
        candidate.validate_observation(observation)

    def test_cli_requires_the_exact_subcommand_before_options(self) -> None:
        base = [sys.executable, str(SCRIPT)]
        omitted = subprocess.run(
            [*base, "--runtime", "android-arm64"],
            check=False,
            capture_output=True,
            text=True,
        )
        wrong = subprocess.run(
            [*base, "check-inputs", "--runtime", "android-arm64"],
            check=False,
            capture_output=True,
            text=True,
        )
        self.assertNotEqual(0, omitted.returncode)
        self.assertNotEqual(0, wrong.returncode)
        self.assertIn("invalid choice", omitted.stderr)
        self.assertIn("invalid choice", wrong.stderr)

    def test_rejects_fork_runtime_abi_event_and_physical_authority_names(self) -> None:
        forked = copy.deepcopy(self.sources)
        forked[0][1] = "https://github.com/SomeoneElse/chummer-android.git"
        arguments = self.keyword_arguments()
        arguments["sources"] = forked
        with self.assertRaisesRegex(ValueError, "canonical source"):
            candidate.create_observation(**arguments)
        arguments = self.keyword_arguments()
        arguments["runtime"] = "android-x64"
        with self.assertRaisesRegex(ValueError, "runtime must be exactly"):
            candidate.create_observation(**arguments)
        arguments = self.keyword_arguments()
        arguments["application_id"] = "com.example.lookalike"
        with self.assertRaisesRegex(ValueError, "application ID must be exactly"):
            candidate.create_observation(**arguments)
        arguments = self.keyword_arguments()
        arguments["event_sha"] = "c" * 40
        with self.assertRaisesRegex(ValueError, "checked-out Android source"):
            candidate.create_observation(**arguments)
        wrong_abi = self.root / "wrong.apk"
        with zipfile.ZipFile(wrong_abi, "w") as archive:
            archive.writestr("lib/x86_64/libmonodroid.so", b"x64")
        arguments = self.keyword_arguments()
        arguments["apk"] = wrong_abi
        with self.assertRaisesRegex(ValueError, "only arm64-v8a"):
            candidate.create_observation(**arguments)
        observation = candidate.create_observation(**self.keyword_arguments())
        for name in ("build-provenance.json", "physical-authority.json"):
            with self.subTest(name=name):
                with self.assertRaisesRegex(ValueError, "hosted candidate filename"):
                    candidate.write_exclusive(self.root / name, observation)

    def test_schema_and_claim_escalation_tampering_fail_closed(self) -> None:
        observation = candidate.create_observation(**self.keyword_arguments())
        for field in (
            "releaseAttested", "publicationAuthorized", "physicalDeviceTested",
            "releaseEligible",
        ):
            with self.subTest(field=field):
                tampered = copy.deepcopy(observation)
                tampered[field] = True
                with self.assertRaisesRegex(ValueError, "claims more than it proves"):
                    candidate.validate_observation(tampered)
        tampered = copy.deepcopy(observation)
        tampered["doesNotAssert"] = []
        with self.assertRaisesRegex(ValueError, "claims more than it proves"):
            candidate.validate_observation(tampered)
        for claim in (
            "apk_install",
            "dependency_closure_attestation",
            "physical_device_observation",
            "physical_journey_pass",
            "google_play_processing",
            "tester_distribution",
            "tester_installation",
        ):
            with self.subTest(missing_nonclaim=claim):
                tampered = copy.deepcopy(observation)
                tampered["doesNotAssert"].remove(claim)
                with self.assertRaisesRegex(ValueError, "claims more than it proves"):
                    candidate.validate_observation(tampered)
        tampered = copy.deepcopy(observation)
        tampered["build"]["applicationId"] = "com.example.lookalike"
        with self.assertRaisesRegex(ValueError, "build identity is not exact"):
            candidate.validate_observation(tampered)
        tampered = copy.deepcopy(observation)
        tampered["unexpected"] = "claim"
        with self.assertRaisesRegex(ValueError, "schema is not exact"):
            candidate.validate_observation(tampered)

    def test_hosted_observation_is_rejected_by_physical_build_and_device_contracts(self) -> None:
        observation = candidate.create_observation(**self.keyword_arguments())
        encoded = candidate.canonical_json_bytes(observation)
        bound = physical.BoundBytes(
            path=self.output,
            data=encoded,
            sha256=candidate.hashlib.sha256(encoded).hexdigest(),
            size_bytes=len(encoded),
        )
        apk_bytes = self.apk.read_bytes()
        apk = physical.BoundBytes(
            path=self.apk,
            data=apk_bytes,
            sha256=self.apk_sha256,
            size_bytes=len(apk_bytes),
        )
        with self.assertRaisesRegex(ValueError, "WP1 build provenance keys are not exact"):
            physical.validate_build_provenance(bound, bound, apk)
        with self.assertRaisesRegex(ValueError, "physical device observation keys are not exact"):
            physical.validate_device_observation(bound)


if __name__ == "__main__":
    unittest.main()
