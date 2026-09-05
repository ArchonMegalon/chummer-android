from __future__ import annotations

import copy
import base64
from datetime import UTC, datetime, timedelta
import hashlib
import importlib.util
import json
from pathlib import Path
import re
import subprocess
import tempfile
import unittest
from unittest import mock
import zipfile


REPO = Path(__file__).resolve().parents[1]
SCRIPT = REPO / "scripts/materialize-api36-two-green-eligibility.py"
SPEC = importlib.util.spec_from_file_location("api36_two_green", SCRIPT)
assert SPEC is not None and SPEC.loader is not None
gate = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(gate)
CONSUMER_SCRIPT = REPO / "scripts/verify_api36_two_green_release_eligibility.py"
CONSUMER_SPEC = importlib.util.spec_from_file_location(
    "api36_two_green_release_consumer", CONSUMER_SCRIPT
)
assert CONSUMER_SPEC is not None and CONSUMER_SPEC.loader is not None
consumer = importlib.util.module_from_spec(CONSUMER_SPEC)
CONSUMER_SPEC.loader.exec_module(consumer)
SIGNER_SCRIPT = REPO / "scripts/sign_api36_two_green_release_approval.py"
SIGNER_SPEC = importlib.util.spec_from_file_location(
    "api36_two_green_release_approval_signer", SIGNER_SCRIPT
)
assert SIGNER_SPEC is not None and SIGNER_SPEC.loader is not None
signer = importlib.util.module_from_spec(SIGNER_SPEC)
SIGNER_SPEC.loader.exec_module(signer)
WORKFLOW = REPO / ".github/workflows/api36-two-consecutive-green.yml"


def sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


class FakeAuthenticatedGitHubClient:
    def __init__(self, android_root: Path, responses: dict[str, bytes]) -> None:
        self.android_root = android_root
        self.responses = responses
        self.calls: list[tuple[str, bool]] = []

    def fetch(self, endpoint: str, *, artifact: bool = False) -> bytes:
        self.calls.append((endpoint, artifact))
        if endpoint not in self.responses:
            raise ValueError(f"unexpected authenticated GitHub endpoint: {endpoint}")
        return self.responses[endpoint]


class Api36TwoGreenEligibilityTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name).resolve()
        self.approver_private_key = self.root / "release-approver.private.pem"
        self.approver_public_key = self.root / "release-approver.public.pem"
        self.github_token = self.root / "github-provenance.token"
        self.github_token.write_text(
            "github_pat_test_provenance_token_0000000000000000\n",
            encoding="ascii",
        )
        self.github_token.chmod(0o600)
        subprocess.run(
            [
                "openssl", "genpkey", "-algorithm", "ED25519", "-out",
                str(self.approver_private_key),
            ],
            check=True,
            capture_output=True,
        )
        self.approver_private_key.chmod(0o600)
        subprocess.run(
            [
                "openssl", "pkey", "-in", str(self.approver_private_key),
                "-pubout", "-out", str(self.approver_public_key),
            ],
            check=True,
            capture_output=True,
        )
        self.original_approver_public_key = consumer.RELEASE_APPROVER_PUBLIC_KEY
        self.original_approver_public_key_sha256 = (
            consumer.RELEASE_APPROVER_PUBLIC_KEY_SHA256
        )
        consumer.RELEASE_APPROVER_PUBLIC_KEY = self.approver_public_key
        consumer.RELEASE_APPROVER_PUBLIC_KEY_SHA256 = sha256(
            self.approver_public_key.read_bytes()
        )
        self.original_signer_approver_public_key = (
            signer.VERIFIER.RELEASE_APPROVER_PUBLIC_KEY
        )
        self.original_signer_approver_public_key_sha256 = (
            signer.VERIFIER.RELEASE_APPROVER_PUBLIC_KEY_SHA256
        )
        signer.VERIFIER.RELEASE_APPROVER_PUBLIC_KEY = self.approver_public_key
        signer.VERIFIER.RELEASE_APPROVER_PUBLIC_KEY_SHA256 = sha256(
            self.approver_public_key.read_bytes()
        )
        self.android = self.root / "android"
        self.source_workflow = self.android / gate.WORKFLOW_PATH
        self.source_workflow.parent.mkdir(parents=True)
        self.source_workflow.write_bytes(
            gate.SOURCE_WORKFLOW.read_bytes()
        )
        project = self.android / "src/Chummer.Android/Chummer.Android.csproj"
        project.parent.mkdir(parents=True)
        project.write_text(
            "<Project><PropertyGroup>"
            "<ApplicationId>com.myexternalbrain.chummer</ApplicationId>"
            "<ApplicationDisplayVersion>0.1.0-preview.11</ApplicationDisplayVersion>"
            "<ApplicationVersion>11</ApplicationVersion>"
            "</PropertyGroup></Project>\n",
            encoding="utf-8",
        )
        self.git("init", "--quiet")
        self.git("config", "user.name", "Two Green Test")
        self.git("config", "user.email", "test@example.invalid")
        self.git("add", gate.WORKFLOW_PATH, "src/Chummer.Android/Chummer.Android.csproj")
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
            "-m", "Merge pull request #33 from feature/two-green",
        )
        self.assertNotEqual(self.review_merge_commit, self.main_merge_commit)
        self.git("checkout", "--quiet", "--detach", self.main_merge_commit)
        self.review_run_id = 33852701828
        self.main_run_id = 33856875877
        self.pull_request_number = 33
        self.review_branch = "codex/android-api36-environment-aggregate-v2-20260903"
        self.policy = self.root / "policy.json"
        self.policy.write_bytes(gate.canonical_json_bytes(gate.expected_policy()))
        self.environment_policy = self.root / "environment-policy.json"
        self.environment_policy.write_bytes(gate.ENVIRONMENT_POLICY_PATH.read_bytes())
        self.environment_policy_authority = gate.ENVIRONMENT.policy_binding(
            gate.StableFile(self.environment_policy, "test proof environment policy")
        )
        self.output = self.root / gate.OUTPUT_NAME
        self.inputs: dict[str, object] = {
            "android_root": self.android,
            "policy": self.policy,
            "environment_policy": self.environment_policy,
            "source_workflow": self.source_workflow,
            "review_run_id": self.review_run_id,
            "review_pull_request_number": self.pull_request_number,
            "review_event_sha": self.review_merge_commit,
            "main_run_id": self.main_run_id,
        }
        self.make_run(
            "review",
            run_id=self.review_run_id,
            event="pull_request",
            branch=self.review_branch,
            head_sha=self.pr_head,
            event_sha=self.review_merge_commit,
            created="2026-09-03T10:00:00Z",
            started="2026-09-03T10:01:00Z",
            completed="2026-09-03T10:30:00Z",
        )
        self.make_run(
            "main",
            run_id=self.main_run_id,
            event="push",
            branch="main",
            head_sha=self.main_merge_commit,
            event_sha=self.main_merge_commit,
            created="2026-09-03T11:00:00Z",
            started="2026-09-03T11:01:00Z",
            completed="2026-09-03T11:30:00Z",
        )
        self.write_pull_request_authority_files()

    def tearDown(self) -> None:
        consumer.RELEASE_APPROVER_PUBLIC_KEY = self.original_approver_public_key
        consumer.RELEASE_APPROVER_PUBLIC_KEY_SHA256 = (
            self.original_approver_public_key_sha256
        )
        signer.VERIFIER.RELEASE_APPROVER_PUBLIC_KEY = (
            self.original_signer_approver_public_key
        )
        signer.VERIFIER.RELEASE_APPROVER_PUBLIC_KEY_SHA256 = (
            self.original_signer_approver_public_key_sha256
        )
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
                    **self.environment_policy_authority,
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
                        "emulatorLiveObservationSha256": sha256(
                            f"{run_id}-{matrix}-emulator-observation".encode()
                        ),
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
        api_root = f"https://api.github.com/repos/{gate.REPOSITORY}"
        html_root = f"https://github.com/{gate.REPOSITORY}"
        check_suite_id = 91743668109 if run_id == 33852701828 else run_id + 500
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
            "url": f"{api_root}/actions/runs/{run_id}",
            "html_url": f"{html_root}/actions/runs/{run_id}",
            "jobs_url": f"{api_root}/actions/runs/{run_id}/jobs",
            "artifacts_url": f"{api_root}/actions/runs/{run_id}/artifacts",
            "check_suite_id": check_suite_id,
            "check_suite_url": f"{api_root}/check-suites/{check_suite_id}",
            "created_at": created,
            "run_started_at": started,
            "updated_at": completed,
            "repository": {
                "full_name": gate.REPOSITORY,
                "url": api_root,
                "html_url": html_root,
            },
            "head_repository": {
                "full_name": gate.REPOSITORY,
                "url": api_root,
                "html_url": html_root,
            },
            # Run 33852701828 is a genuine pull_request run whose canonical
            # Actions response has an empty summary.  Independent PR and Git
            # commit API snapshots below must carry the missing authority.
            "pull_requests": [],
        }
        def job_id(index: int, name: str) -> int:
            if (
                run_id == self.review_run_id
                and name == gate.REQUIRED_JOB_NAMES[-1]
            ):
                return 100971293598
            return run_id * 100 + index

        jobs = {
            "total_count": len(gate.REQUIRED_JOB_NAMES),
            "jobs": [
                {
                    "id": job_id(index, name),
                    "run_id": run_id,
                    "run_attempt": attempt,
                    "head_sha": head_sha,
                    "workflow_name": gate.WORKFLOW_NAME,
                    "name": name,
                    "status": "completed",
                    "conclusion": "success",
                    "started_at": started,
                    "completed_at": completed,
                    "url": f"{api_root}/actions/jobs/{job_id(index, name)}",
                    "html_url": (
                        f"{html_root}/actions/runs/{run_id}/job/"
                        f"{job_id(index, name)}"
                    ),
                    "check_run_url": (
                        f"{api_root}/check-runs/{job_id(index, name)}"
                    ),
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

    def write_pull_request_authority_files(self) -> None:
        api_root = f"https://api.github.com/repos/{gate.REPOSITORY}"
        html_root = f"https://github.com/{gate.REPOSITORY}"

        def commit_payload(
            commit: str, parents: tuple[str, ...]
        ) -> dict[str, object]:
            return {
                "sha": commit,
                "url": f"{api_root}/git/commits/{commit}",
                "html_url": f"{html_root}/commit/{commit}",
                "tree": {
                    "sha": self.tree,
                    "url": f"{api_root}/git/trees/{self.tree}",
                },
                "parents": [
                    {
                        "sha": parent,
                        "url": f"{api_root}/git/commits/{parent}",
                        "html_url": f"{html_root}/commit/{parent}",
                    }
                    for parent in parents
                ],
            }

        repository = {
            "full_name": gate.REPOSITORY,
            "url": api_root,
            "html_url": html_root,
        }

        pull_request = {
            "number": self.pull_request_number,
            "url": f"{api_root}/pulls/{self.pull_request_number}",
            "html_url": f"{html_root}/pull/{self.pull_request_number}",
            "commits_url": (
                f"{api_root}/pulls/{self.pull_request_number}/commits"
            ),
            "statuses_url": f"{api_root}/statuses/{self.pr_head}",
            "state": "closed",
            "merged": True,
            "merged_at": "2026-09-03T10:45:00Z",
            "merge_commit_sha": self.main_merge_commit,
            "base": {
                "ref": "main",
                "sha": self.base_commit,
                "repo": repository,
            },
            "head": {
                "ref": self.review_branch,
                "sha": self.pr_head,
                "repo": repository,
            },
        }
        review_jobs = json.loads(self.inputs["review_jobs"].read_text())
        aggregate_job = next(
            row
            for row in review_jobs["jobs"]
            if row["name"] == gate.REQUIRED_JOB_NAMES[-1]
        )
        aggregate_check_run = {
            "id": 100971293598,
            "name": gate.REQUIRED_JOB_NAMES[-1],
            "head_sha": self.pr_head,
            "status": "completed",
            "conclusion": "success",
            "url": f"{api_root}/check-runs/100971293598",
            "html_url": aggregate_job["html_url"],
            "details_url": aggregate_job["html_url"],
            "check_suite": {"id": 91743668109},
            "app": {"id": 15368, "slug": "github-actions"},
            # The live check-run also omits PRs; this is observed but never
            # substitutes for the commit-associated PR endpoint below.
            "pull_requests": [],
        }
        paths = {
            "review_pull_request": self.root / "review-pull-request.json",
            "review_head_pull_requests": self.root / "head-pull-requests.json",
            "review_aggregate_check_run": self.root / "aggregate-check-run.json",
            "review_base_commit": self.root / "base-commit.json",
            "review_head_commit": self.root / "head-commit.json",
            "review_event_commit": self.root / "review-event-commit.json",
            "main_commit": self.root / "main-commit.json",
        }
        self.write_json(paths["review_pull_request"], pull_request)
        self.write_json(paths["review_head_pull_requests"], [pull_request])
        self.write_json(paths["review_aggregate_check_run"], aggregate_check_run)
        self.write_json(
            paths["review_base_commit"], commit_payload(self.base_commit, ())
        )
        self.write_json(
            paths["review_head_commit"],
            commit_payload(self.pr_head, (self.base_commit,)),
        )
        self.write_json(
            paths["review_event_commit"],
            commit_payload(
                self.review_merge_commit, (self.base_commit, self.pr_head)
            ),
        )
        self.write_json(
            paths["main_commit"],
            commit_payload(
                self.main_merge_commit, (self.base_commit, self.pr_head)
            ),
        )
        self.inputs.update(paths)

    def create(self) -> dict[str, object]:
        return gate.create_authority(**self.inputs)

    def authenticated_github_client(
        self,
        *,
        remote_main: str | None = None,
        overrides: dict[str, bytes] | None = None,
    ) -> FakeAuthenticatedGitHubClient:
        repository = gate.REPOSITORY
        responses: dict[str, bytes] = {}
        for role, run_id in (
            ("review", self.review_run_id),
            ("main", self.main_run_id),
        ):
            run = json.loads(self.inputs[f"{role}_run"].read_text(encoding="utf-8"))
            attempt = run["run_attempt"]
            responses[f"repos/{repository}/actions/runs/{run_id}"] = self.inputs[
                f"{role}_run"
            ].read_bytes()
            responses[
                f"repos/{repository}/actions/runs/{run_id}/attempts/{attempt}/jobs?per_page=100"
            ] = self.inputs[f"{role}_jobs"].read_bytes()
            responses[
                f"repos/{repository}/actions/runs/{run_id}/artifacts?per_page=100"
            ] = self.inputs[f"{role}_artifacts"].read_bytes()
            artifacts = json.loads(
                self.inputs[f"{role}_artifacts"].read_text(encoding="utf-8")
            )["artifacts"]
            for kind, prefix in (
                ("aggregate", "chummer-android-api36-phone-sr5-wizard-aggregate"),
                ("p0", "chummer-android-p0-pr-authority"),
            ):
                row = next(item for item in artifacts if item["name"].startswith(prefix))
                responses[
                    f"repos/{repository}/actions/artifacts/{row['id']}/zip"
                ] = self.inputs[f"{role}_{kind}_archive"].read_bytes()

        review_jobs = json.loads(
            self.inputs["review_jobs"].read_text(encoding="utf-8")
        )["jobs"]
        aggregate_job = next(
            row for row in review_jobs
            if row["name"] == gate.REQUIRED_JOB_NAMES[-1]
        )
        static_paths = {
            f"repos/{repository}/pulls/{self.pull_request_number}": "review_pull_request",
            f"repos/{repository}/commits/{self.pr_head}/pulls": "review_head_pull_requests",
            f"repos/{repository}/check-runs/{aggregate_job['id']}": "review_aggregate_check_run",
            f"repos/{repository}/git/commits/{self.base_commit}": "review_base_commit",
            f"repos/{repository}/git/commits/{self.pr_head}": "review_head_commit",
            f"repos/{repository}/git/commits/{self.review_merge_commit}": "review_event_commit",
            f"repos/{repository}/git/commits/{self.main_merge_commit}": "main_commit",
        }
        for endpoint, name in static_paths.items():
            responses[endpoint] = self.inputs[name].read_bytes()
        responses[f"repos/{repository}/git/ref/heads/main"] = gate.canonical_json_bytes(
            {
                "ref": "refs/heads/main",
                "object": {
                    "type": "commit",
                    "sha": remote_main or self.main_merge_commit,
                },
            }
        )
        responses.update(overrides or {})
        return FakeAuthenticatedGitHubClient(self.android, responses)

    def release_consumer_inputs(
        self, authority: dict[str, object]
    ) -> tuple[Path, Path, Path, Path]:
        receipt = self.root / "release-two-green.json"
        receipt.write_bytes(gate.pretty_json_bytes(authority))
        receipt.chmod(0o600)
        sources = authority["commonAuthority"]["dependencyGraph"]["sources"]
        package_authority = self.root / "release-package-authority.json"
        package_authority.write_bytes(
            gate.canonical_json_bytes(
                {
                    "contractName": consumer.PACKAGE_AUTHORITY_CONTRACT,
                    "packagePins": [
                        {
                            "package_id": package_id,
                            "commit": sources["core-runtime"]["commit"],
                        }
                        for package_id in consumer.RUNTIME_PACKAGES
                    ],
                    "ownerPackagePins": [
                        {
                            "package_id": package_id,
                            "source_commit": sources[source_name]["commit"],
                            "source_tree": sources[source_name]["tree"],
                        }
                        for package_id, source_name in consumer.OWNER_PACKAGES.items()
                    ],
                    "dependencyClosure": [],
                }
            )
        )
        package_authority.chmod(0o600)
        source_graph = self.root / "release-source-graph.json"
        repository_rows = [
            {
                "name": "chummer-android",
                "role": "app",
                "commit": authority["sourceCommit"],
                "tree": authority["sourceTree"],
                "tree_sha256": "1" * 64,
                "repository": "https://github.com/ArchonMegalon/chummer-android.git",
            }
        ]
        for source_name in (
            "presentation",
            "core-runtime",
            "ui-kit",
            "hub",
            "registry",
            "media",
        ):
            repository_name = consumer.SOURCE_GRAPH_REPOSITORIES[source_name]
            role, repository = consumer.SOURCE_GRAPH_REPOSITORY_AUTHORITY[
                repository_name
            ]
            repository_rows.append(
                {
                    "name": repository_name,
                    "role": role,
                    "commit": sources[source_name]["commit"],
                    "tree": sources[source_name]["tree"],
                    "tree_sha256": "2" * 64,
                    "repository": repository,
                }
            )
        repository_rows.append(
            {
                "name": "chummer6-design",
                "role": "validation",
                "commit": "a" * 40,
                "tree": "b" * 40,
                "tree_sha256": "3" * 64,
                "repository": "https://github.com/ArchonMegalon/chummer6-design.git",
            }
        )
        source_graph.write_bytes(
            gate.canonical_json_bytes(
                {
                    "contractName": consumer.SOURCE_GRAPH_CONTRACT,
                    "publicationAuthorized": False,
                    "releaseIdentity": {
                        "packageId": consumer.PACKAGE_ID,
                        "versionName": authority["releaseIdentity"]["versionName"],
                        "versionCode": authority["releaseIdentity"]["versionCode"],
                        "intentAuthority": "explicit_build_input",
                        "minimumExclusiveVersionCode": 10,
                    },
                    "repositories": repository_rows,
                    "ownerPackagePins": [
                        {
                            "package_id": package_id,
                            "source_commit": sources[source_name]["commit"],
                            "source_tree": sources[source_name]["tree"],
                        }
                        for package_id, source_name in consumer.OWNER_PACKAGES.items()
                    ],
                }
            )
        )
        source_graph.chmod(0o600)
        approval = self.write_release_approval(receipt, authority)
        return receipt, approval, package_authority, source_graph

    def write_release_approval(
        self,
        receipt: Path,
        authority: dict[str, object],
        *,
        private_key: Path | None = None,
        generated: datetime | None = None,
        expires: datetime | None = None,
    ) -> Path:
        generated = (generated or datetime.now(UTC)).replace(microsecond=0)
        expires = expires or generated + timedelta(hours=1)
        unsigned = consumer.release_approval_unsigned(
            receipt.read_bytes(),
            authority,
            generated_at_utc=generated.isoformat().replace("+00:00", "Z"),
            expires_at_utc=expires.isoformat().replace("+00:00", "Z"),
            challenge_nonce="4" * 64,
            provenance_validator_sha256=sha256(consumer.TWO_GREEN_PATH.read_bytes()),
            provenance_replay_sha256="5" * 64,
        )
        payload = self.root / "release-approval-payload.json"
        payload.write_bytes(consumer._canonical_json_bytes(unsigned))
        completed = subprocess.run(
            [
                "openssl", "pkeyutl", "-sign", "-inkey",
                str(private_key or self.approver_private_key), "-rawin", "-in",
                str(payload),
            ],
            check=True,
            capture_output=True,
        )
        approval = self.root / "release-two-green.approval.json"
        approval.write_bytes(
            gate.pretty_json_bytes(
                {
                    **unsigned,
                    "signatureBase64": base64.b64encode(completed.stdout).decode("ascii"),
                }
            )
        )
        approval.chmod(0o600)
        return approval

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
        self.assertEqual(self.review_merge_commit, result["reviewRun"]["p0EventSha"])
        self.assertEqual(self.main_merge_commit, result["mainRun"]["p0EventSha"])
        self.assertEqual([], result["reviewRun"]["run"]["reportedPullRequests"])
        self.assertEqual(
            self.pull_request_number, result["reviewPullRequest"]["number"]
        )
        self.assertEqual(
            self.pull_request_number,
            result["reviewPullRequest"]["commitAssociation"]["number"],
        )
        self.assertEqual(
            self.tree, result["reviewPullRequest"]["headCommit"]["tree"]
        )
        self.assertEqual(
            100971293598, result["reviewRun"]["aggregateCheckRun"]["id"]
        )
        self.assertEqual(
            {"id": 15368, "slug": "github-actions"},
            result["reviewRun"]["aggregateCheckRun"]["app"],
        )
        self.assertEqual(
            [], result["reviewRun"]["aggregateCheckRun"]["reportedPullRequests"]
        )
        self.assertEqual(
            [self.base_commit, self.pr_head],
            result["reviewPullRequest"]["reviewEventCommit"]["parents"],
        )
        self.assertEqual(
            [self.base_commit, self.pr_head],
            result["reviewPullRequest"]["mainMergeCommit"]["parents"],
        )
        self.assertEqual(
            self.tree, result["reviewPullRequest"]["reviewEventCommit"]["tree"]
        )
        self.assertEqual(
            self.tree, result["reviewPullRequest"]["mainMergeCommit"]["tree"]
        )
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
        review_graph = self.read_p0("review")["dependencyGraph"]
        for name, source in review_graph["sources"].items():
            if name != "android":
                self.assertEqual(
                    source,
                    result["commonAuthority"]["dependencyGraph"]["sources"][name],
                )
        common_graph = result["commonAuthority"]["dependencyGraph"]
        self.assertEqual(
            gate.canonical_sha256(
                {"mode": common_graph["mode"], "sources": common_graph["sources"]}
            ),
            common_graph["sha256"],
        )
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

    def test_live_review_33852701828_empty_summary_has_independent_authority(self) -> None:
        api_root = f"https://api.github.com/repos/{gate.REPOSITORY}"
        html_root = f"https://github.com/{gate.REPOSITORY}"
        head_sha = "236a25c30f7a3c6df6bb9399b242ac6b447e5b6f"
        base_sha = "11578520aab86922be2d783444d4d60ef85585f8"
        merge_sha = "59f365dc0677153bd83a07853d53f989fe074991"
        run = gate.validate_run_metadata(
            {
                "id": 33852701828,
                "run_attempt": 1,
                "workflow_id": 334405532,
                "name": gate.WORKFLOW_NAME,
                "path": gate.WORKFLOW_PATH,
                "event": "pull_request",
                "status": "completed",
                "conclusion": "success",
                "head_branch": self.review_branch,
                "head_sha": head_sha,
                "url": f"{api_root}/actions/runs/33852701828",
                "html_url": f"{html_root}/actions/runs/33852701828",
                "jobs_url": f"{api_root}/actions/runs/33852701828/jobs",
                "artifacts_url": f"{api_root}/actions/runs/33852701828/artifacts",
                "check_suite_id": 91743668109,
                "check_suite_url": f"{api_root}/check-suites/91743668109",
                "created_at": "2026-09-04T08:17:11Z",
                "run_started_at": "2026-09-04T08:17:11Z",
                "updated_at": "2026-09-04T09:05:50Z",
                "repository": {
                    "full_name": gate.REPOSITORY,
                    "url": api_root,
                    "html_url": html_root,
                },
                "head_repository": {
                    "full_name": gate.REPOSITORY,
                    "url": api_root,
                    "html_url": html_root,
                },
                "pull_requests": [],
            },
            expected_id=33852701828,
            role="review",
        )
        details_url = (
            f"{html_root}/actions/runs/33852701828/job/100971293598"
        )
        check_run_path = self.root / "live-review-check-run.json"
        self.write_json(
            check_run_path,
            {
                "id": 100971293598,
                "name": gate.REQUIRED_JOB_NAMES[-1],
                "head_sha": head_sha,
                "status": "completed",
                "conclusion": "success",
                "url": f"{api_root}/check-runs/100971293598",
                "html_url": details_url,
                "details_url": details_url,
                "check_suite": {"id": 91743668109},
                "app": {"id": 15368, "slug": "github-actions"},
                "pull_requests": [],
            },
        )
        review = {
            "run": run,
            "jobs": {
                gate.REQUIRED_JOB_NAMES[-1]: {
                    "id": 100971293598,
                    "detailsUrl": details_url,
                    "checkRunUrl": f"{api_root}/check-runs/100971293598",
                }
            },
        }
        check_run = gate.validate_aggregate_check_run_authority(
            gate.StableFile(check_run_path, "live review aggregate check-run"),
            review=review,
        )
        self.assertEqual([], check_run["reportedPullRequests"])

        repository = {
            "full_name": gate.REPOSITORY,
            "url": api_root,
            "html_url": html_root,
        }
        associated_path = self.root / "live-head-pull-requests.json"
        self.write_json(
            associated_path,
            [
                {
                    "number": 33,
                    "url": f"{api_root}/pulls/33",
                    "html_url": f"{html_root}/pull/33",
                    "state": "closed",
                    "merged_at": "2026-09-04T09:08:38Z",
                    "merge_commit_sha": merge_sha,
                    "base": {
                        "ref": "main",
                        "sha": base_sha,
                        "repo": repository,
                    },
                    "head": {
                        "ref": self.review_branch,
                        "sha": head_sha,
                        "repo": repository,
                    },
                }
            ],
        )
        association = gate.validate_commit_pull_request_association(
            gate.StableFile(associated_path, "live head pull requests"),
            number=33,
            base_sha=base_sha,
            merged_at="2026-09-04T09:08:38Z",
            review_run=run,
            main_run={"headSha": merge_sha},
        )
        self.assertEqual(33, association["number"])

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
            (
                "main pull request summary",
                "main_run",
                lambda value: value.update(
                    {"pull_requests": [{"number": self.pull_request_number}]}
                ),
            ),
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

    def test_widened_or_noncanonical_p0_wizard_gate_fails_closed(self) -> None:
        def widen_gate(p0: dict[str, object]) -> None:
            p0["inputs"]["wizardGate"] = {
                "schema": "hostile/widened-gate",
                "publicationAuthorized": True,
                "googlePlayUploadAuthorized": True,
            }

        for role in ("review", "main"):
            with zipfile.ZipFile(self.inputs[f"{role}_aggregate_archive"]) as archive:
                aggregate = json.loads(archive.read("receipt.json"))
            self.rewrite_proof(role, aggregate, mutate_p0=widen_gate)

        with self.assertRaisesRegex(
            ValueError, "P0/run/tree/aggregate authority differs"
        ):
            self.create()

    def test_environment_policy_input_and_aggregate_authority_are_exact(self) -> None:
        widened = json.loads(self.environment_policy.read_text(encoding="utf-8"))
        widened["publicationAuthorized"] = True
        self.write_json(self.environment_policy, widened)
        with self.assertRaisesRegex(ValueError, "cannot authorize publication"):
            self.create()

        self.environment_policy.write_bytes(gate.ENVIRONMENT_POLICY_PATH.read_bytes())
        for role in ("review", "main"):
            with zipfile.ZipFile(self.inputs[f"{role}_aggregate_archive"]) as archive:
                aggregate = json.loads(archive.read("receipt.json"))
            aggregate["environmentAuthority"]["policyAuthority"]["sha256"] = "0" * 64
            self.rewrite_proof(role, aggregate)
        with self.assertRaisesRegex(ValueError, "environment policy authority differs"):
            self.create()

    def test_main_event_sha_must_equal_the_actions_push_head(self) -> None:
        with zipfile.ZipFile(self.inputs["main_aggregate_archive"]) as archive:
            aggregate = json.loads(archive.read("receipt.json"))

        def substitute_event_sha(p0: dict[str, object]) -> None:
            replacement = "a" * 40
            p0["githubRun"]["eventSha"] = replacement
            p0["androidSource"]["checkedOutHead"] = replacement
            p0["dependencyGraph"]["sources"]["android"]["commit"] = replacement
            self.rehash_dependency_graph(p0)

        self.rewrite_proof("main", aggregate, mutate_p0=substitute_event_sha)
        with self.assertRaisesRegex(
            ValueError, "P0/run/tree/aggregate authority differs"
        ):
            self.create()

    def test_main_base_sha_must_equal_the_actions_push_head(self) -> None:
        with zipfile.ZipFile(self.inputs["main_aggregate_archive"]) as archive:
            aggregate = json.loads(archive.read("receipt.json"))

        def substitute_base_sha(p0: dict[str, object]) -> None:
            p0["githubRun"]["baseSha"] = "a" * 40

        self.rewrite_proof("main", aggregate, mutate_p0=substitute_base_sha)
        with self.assertRaisesRegex(
            ValueError, "P0/run/tree/aggregate authority differs"
        ):
            self.create()

    def test_pull_request_and_commit_authority_hostile_drift_fails_closed(self) -> None:
        def reject_json(key: str, mutate, expected: str) -> None:
            path = self.inputs[key]
            original = path.read_bytes()
            value = json.loads(original)
            mutate(value)
            self.write_json(path, value)
            try:
                with self.assertRaisesRegex(ValueError, expected):
                    self.create()
            finally:
                path.write_bytes(original)

        cases = (
            (
                "PR head",
                "review_pull_request",
                lambda value: value["head"].update({"sha": "0" * 40}),
                "pull request identity differs",
            ),
            (
                "PR base",
                "review_pull_request",
                lambda value: value["base"].update({"sha": "0" * 40}),
                "base differs from the P0 authority",
            ),
            (
                "PR merge",
                "review_pull_request",
                lambda value: value.update({"merge_commit_sha": "0" * 40}),
                "pull request identity differs",
            ),
            (
                "PR repository",
                "review_pull_request",
                lambda value: value["head"]["repo"].update(
                    {"full_name": "ForeignOwner/chummer-android"}
                ),
                "pull request identity differs",
            ),
            (
                "commit-associated PR",
                "review_head_pull_requests",
                lambda value: value[0].update({"number": 34}),
                "pull request association differs",
            ),
            (
                "commit-associated PR cardinality",
                "review_head_pull_requests",
                lambda value: value.clear(),
                "associated with exactly one pull request",
            ),
            (
                "PR base commit",
                "review_base_commit",
                lambda value: value.update({"sha": "0" * 40}),
                "pull request base commit identity differs",
            ),
            (
                "PR head commit tree",
                "review_head_commit",
                lambda value: value["tree"].update({"sha": "0" * 40}),
                "pull request head commit identity differs",
            ),
            (
                "review commit tree",
                "review_event_commit",
                lambda value: value["tree"].update({"sha": "0" * 40}),
                "review event commit identity differs",
            ),
            (
                "main commit tree",
                "main_commit",
                lambda value: value["tree"].update({"sha": "0" * 40}),
                "main merge commit identity differs",
            ),
            (
                "check URL",
                "review_jobs",
                lambda value: value["jobs"][0].update(
                    {"check_run_url": "https://api.github.com/foreign/check-runs/1"}
                ),
                "job is not exact and successful",
            ),
            (
                "aggregate check URL",
                "review_aggregate_check_run",
                lambda value: value.update(
                    {"url": "https://api.github.com/foreign/check-runs/1"}
                ),
                "aggregate check-run authority differs",
            ),
            (
                "aggregate check details URL",
                "review_aggregate_check_run",
                lambda value: value.update(
                    {"details_url": "https://github.com/foreign/actions/1"}
                ),
                "aggregate check-run authority differs",
            ),
            (
                "aggregate check suite",
                "review_aggregate_check_run",
                lambda value: value["check_suite"].update({"id": 1}),
                "aggregate check-run authority differs",
            ),
            (
                "aggregate check app",
                "review_aggregate_check_run",
                lambda value: value["app"].update({"slug": "foreign-app"}),
                "aggregate check-run authority differs",
            ),
            (
                "run head repository",
                "review_run",
                lambda value: value["head_repository"].update(
                    {"full_name": "ForeignOwner/chummer-android"}
                ),
                "run head repository differs",
            ),
            (
                "nonempty Actions PR summary",
                "review_run",
                lambda value: value.update(
                    {"pull_requests": [{"number": self.pull_request_number}]}
                ),
                "independently authenticated empty pull request summary path",
            ),
            (
                "nonempty check-run PR summary",
                "review_aggregate_check_run",
                lambda value: value.update(
                    {"pull_requests": [{"number": self.pull_request_number}]}
                ),
                "check-run pull request summary is malformed",
            ),
            (
                "details URL",
                "review_jobs",
                lambda value: value["jobs"][0].update(
                    {"html_url": "https://github.com/foreign/actions/runs/1/job/1"}
                ),
                "job is not exact and successful",
            ),
            (
                "review event",
                "review_run",
                lambda value: value.update({"event": "merge_group"}),
                "not an exact pull_request run",
            ),
        )
        for label, key, mutate, expected in cases:
            with self.subTest(label=label):
                reject_json(key, mutate, expected)

        original_number = self.inputs["review_pull_request_number"]
        self.inputs["review_pull_request_number"] = self.pull_request_number + 1
        try:
            with self.assertRaisesRegex(ValueError, "pull request identity differs"):
                self.create()
        finally:
            self.inputs["review_pull_request_number"] = original_number

        original_event_sha = self.inputs["review_event_sha"]
        self.inputs["review_event_sha"] = "0" * 40
        try:
            with self.assertRaisesRegex(ValueError, "differs from the P0 authority"):
                self.create()
        finally:
            self.inputs["review_event_sha"] = original_event_sha

    def test_workflow_must_be_the_clean_tracked_head_blob(self) -> None:
        (self.android / "untracked-hostile-input").write_text(
            "hostile\n", encoding="utf-8"
        )
        with self.assertRaisesRegex(ValueError, "checkout is not clean"):
            self.create()
        (self.android / "untracked-hostile-input").unlink()

        external = self.root / "external-workflow.yml"
        external.write_bytes(self.source_workflow.read_bytes())
        self.inputs["source_workflow"] = external
        with self.assertRaisesRegex(ValueError, "governed checkout path"):
            self.create()
        self.inputs["source_workflow"] = self.source_workflow

        self.git("update-index", "--assume-unchanged", gate.WORKFLOW_PATH)
        hostile = b"name: uncommitted hostile workflow\npublicationAuthorized: true\n"
        self.source_workflow.write_bytes(hostile)
        self.assertEqual(
            "", self.git("status", "--porcelain=v1", "--untracked-files=all")
        )

        def rebind_workflow(p0: dict[str, object]) -> None:
            p0["workflow"] = {
                "path": gate.WORKFLOW_PATH,
                "sha256": sha256(hostile),
                "sizeBytes": len(hostile),
            }

        for role in ("review", "main"):
            with zipfile.ZipFile(self.inputs[f"{role}_aggregate_archive"]) as archive:
                aggregate = json.loads(archive.read("receipt.json"))
            self.rewrite_proof(role, aggregate, mutate_p0=rebind_workflow)
        with self.assertRaisesRegex(ValueError, "workflow bytes differ from tracked HEAD"):
            self.create()

    def test_release_identity_must_come_from_the_tracked_main_tree(self) -> None:
        project_path = Path("src/Chummer.Android/Chummer.Android.csproj")
        self.git("update-index", "--assume-unchanged", project_path.as_posix())
        project = self.android / project_path
        project.write_text(
            project.read_text(encoding="utf-8").replace(
                "0.1.0-preview.11", "0.1.0-preview.12"
            ),
            encoding="utf-8",
        )
        self.assertEqual(
            "", self.git("status", "--porcelain=v1", "--untracked-files=all")
        )
        with self.assertRaisesRegex(ValueError, "project bytes differ from tracked HEAD"):
            self.create()

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

    def test_release_consumer_binds_exact_receipt_commit_graph_version_and_environment(self) -> None:
        self.inputs["policy"] = gate.POLICY_PATH
        self.inputs["environment_policy"] = gate.ENVIRONMENT_POLICY_PATH
        authority = self.create()
        receipt, approval, package_authority, source_graph = (
            self.release_consumer_inputs(authority)
        )
        binding = consumer.verify_release_eligibility(
            receipt,
            approval,
            android_root=self.android,
            expected_version_name="0.1.0-preview.11",
            expected_version_code=11,
            package_authority_path=package_authority,
            source_graph_path=source_graph,
        )
        self.assertTrue(binding["eligible"])
        self.assertTrue(binding["internalTestingEligible"])
        self.assertFalse(binding["publicationAuthorized"])
        self.assertFalse(binding["googlePlayUploadAuthorized"])
        self.assertEqual(self.main_merge_commit, binding["sourceCommit"])
        self.assertEqual(
            authority["commonAuthority"]["dependencyGraph"]["sha256"],
            binding["dependencyGraphSha256"],
        )
        self.assertEqual(
            authority["commonAuthority"]["environmentPolicy"]["sha256"],
            binding["environmentPolicySha256"],
        )
        self.assertEqual("success", binding["mainAggregateConclusion"])
        self.assertEqual("pass", binding["environmentCompatibilityStatus"])
        self.assertEqual(
            consumer.RELEASE_APPROVAL_CONTRACT,
            binding["protectedApproval"]["contractName"],
        )

    def test_release_consumer_cli_never_turns_eligibility_into_signing_or_upload_authority(
        self,
    ) -> None:
        self.inputs["policy"] = gate.POLICY_PATH
        self.inputs["environment_policy"] = gate.ENVIRONMENT_POLICY_PATH
        authority = self.create()
        receipt, approval, package_authority, source_graph = (
            self.release_consumer_inputs(authority)
        )
        arguments = [
            str(CONSUMER_SCRIPT),
            "--receipt",
            str(receipt),
            "--approval",
            str(approval),
            "--android-root",
            str(self.android),
            "--expected-version-name",
            "0.1.0-preview.11",
            "--expected-version-code",
            "11",
            "--package-authority",
            str(package_authority),
            "--source-graph",
            str(source_graph),
        ]
        import contextlib
        import io

        output = io.StringIO()
        with contextlib.redirect_stdout(output):
            return_code = consumer.main(arguments[1:])
        self.assertEqual(0, return_code)
        result = json.loads(output.getvalue())
        self.assertTrue(result["releasePreparationEligible"])
        self.assertFalse(result["signingAuthorizedByReceipt"])
        self.assertFalse(result["publicationAuthorized"])
        self.assertFalse(result["googlePlayUploadAuthorized"])

        damaged = json.loads(approval.read_text(encoding="utf-8"))
        damaged["signatureBase64"] = base64.b64encode(b"0" * 64).decode("ascii")
        approval.write_bytes(gate.pretty_json_bytes(damaged))
        approval.chmod(0o600)
        output = io.StringIO()
        with contextlib.redirect_stdout(output):
            return_code = consumer.main(arguments[1:])
        self.assertEqual(2, return_code)
        failure = json.loads(output.getvalue())
        self.assertFalse(failure["releasePreparationEligible"])
        self.assertFalse(failure["signingAuthorizedByReceipt"])
        self.assertFalse(failure["publicationAuthorized"])
        self.assertFalse(failure["googlePlayUploadAuthorized"])

    def test_release_approval_signer_is_preparation_only_and_signature_bound(self) -> None:
        self.inputs["policy"] = gate.POLICY_PATH
        self.inputs["environment_policy"] = gate.ENVIRONMENT_POLICY_PATH
        authority = self.create()
        receipt, _approval, package_authority, source_graph = (
            self.release_consumer_inputs(authority)
        )
        signed = self.root / "protected-release-approval.json"
        github = self.authenticated_github_client()
        with mock.patch.object(signer, "GitHubApiClient", return_value=github):
            result = signer.sign(
                receipt,
                self.github_token,
                self.approver_private_key,
                signed,
            )
        self.assertTrue(result["releasePreparationApproved"])
        self.assertFalse(result["signingAuthorized"])
        self.assertFalse(result["publicationAuthorized"])
        self.assertFalse(result["googlePlayUploadAuthorized"])
        self.assertEqual(0o600, signed.stat().st_mode & 0o777)
        binding = consumer.verify_release_eligibility(
            receipt,
            signed,
            android_root=self.android,
            expected_version_name="0.1.0-preview.11",
            expected_version_code=11,
            package_authority_path=package_authority,
            source_graph_path=source_graph,
        )
        self.assertEqual(
            sha256(signed.read_bytes()),
            binding["protectedApproval"]["approvalSha256"],
        )
        with mock.patch.object(signer, "GitHubApiClient", return_value=github):
            with self.assertRaisesRegex(ValueError, "output must be new"):
                signer.sign(
                    receipt,
                    self.github_token,
                    self.approver_private_key,
                    signed,
                )

    def test_release_approval_signer_rejects_fabricated_pull_request_provenance(self) -> None:
        self.inputs["policy"] = gate.POLICY_PATH
        self.inputs["environment_policy"] = gate.ENVIRONMENT_POLICY_PATH
        authority = self.create()
        receipt, _approval, _package_authority, _source_graph = (
            self.release_consumer_inputs(authority)
        )
        github = self.authenticated_github_client()
        fabricated = copy.deepcopy(authority)
        fabricated["reviewPullRequest"]["number"] += 1
        unsigned = {
            key: value for key, value in fabricated.items()
            if key != "eligibilitySha256"
        }
        fabricated["eligibilitySha256"] = gate.canonical_sha256(unsigned)
        receipt.write_bytes(gate.pretty_json_bytes(fabricated))
        with mock.patch.object(signer, "GitHubApiClient", return_value=github):
            with self.assertRaisesRegex(ValueError, "authenticated GitHub|does not replay"):
                signer.sign(
                    receipt,
                    self.github_token,
                    self.approver_private_key,
                    self.root / "fabricated-approval.json",
                )

    def test_release_approval_signer_rejects_remote_main_substitution(self) -> None:
        self.inputs["policy"] = gate.POLICY_PATH
        self.inputs["environment_policy"] = gate.ENVIRONMENT_POLICY_PATH
        authority = self.create()
        receipt, _approval, _package_authority, _source_graph = (
            self.release_consumer_inputs(authority)
        )
        github = self.authenticated_github_client(remote_main="0" * 40)
        with mock.patch.object(signer, "GitHubApiClient", return_value=github):
            with self.assertRaisesRegex(ValueError, "remote main"):
                signer.sign(
                    receipt,
                    self.github_token,
                    self.approver_private_key,
                    self.root / "remote-main-substitution.json",
                )

    def test_release_approval_signer_rejects_fabricated_authenticated_pr(self) -> None:
        self.inputs["policy"] = gate.POLICY_PATH
        self.inputs["environment_policy"] = gate.ENVIRONMENT_POLICY_PATH
        authority = self.create()
        receipt, _approval, _package_authority, _source_graph = (
            self.release_consumer_inputs(authority)
        )
        endpoint = f"repos/{gate.REPOSITORY}/pulls/{self.pull_request_number}"
        fabricated_pr = json.loads(
            self.inputs["review_pull_request"].read_text(encoding="utf-8")
        )
        fabricated_pr["head"]["sha"] = "0" * 40
        github = self.authenticated_github_client(
            overrides={endpoint: gate.canonical_json_bytes(fabricated_pr)}
        )
        with mock.patch.object(signer, "GitHubApiClient", return_value=github):
            with self.assertRaises(ValueError):
                signer.sign(
                    receipt,
                    self.github_token,
                    self.approver_private_key,
                    self.root / "fabricated-authenticated-pr.json",
                )

    def test_recomputed_plain_receipt_hash_cannot_replace_protected_approval(self) -> None:
        self.inputs["policy"] = gate.POLICY_PATH
        self.inputs["environment_policy"] = gate.ENVIRONMENT_POLICY_PATH
        authority = self.create()
        receipt, approval, package_authority, source_graph = (
            self.release_consumer_inputs(authority)
        )
        receipt.write_bytes(gate.canonical_json_bytes(authority))
        receipt.chmod(0o600)
        self.assertEqual(
            sha256(receipt.read_bytes()),
            hashlib.sha256(receipt.read_bytes()).hexdigest(),
        )
        with self.assertRaisesRegex(ValueError, "claims differ from the receipt"):
            consumer.verify_release_eligibility(
                receipt,
                approval,
                android_root=self.android,
                expected_version_name="0.1.0-preview.11",
                expected_version_code=11,
                package_authority_path=package_authority,
                source_graph_path=source_graph,
            )

    def test_wrong_key_stale_and_escalating_release_approvals_fail_closed(self) -> None:
        self.inputs["policy"] = gate.POLICY_PATH
        self.inputs["environment_policy"] = gate.ENVIRONMENT_POLICY_PATH
        authority = self.create()
        receipt, approval, package_authority, source_graph = (
            self.release_consumer_inputs(authority)
        )

        wrong_private = self.root / "wrong.private.pem"
        subprocess.run(
            ["openssl", "genpkey", "-algorithm", "ED25519", "-out", str(wrong_private)],
            check=True,
            capture_output=True,
        )
        wrong_private.chmod(0o600)
        approval.unlink()
        wrong_approval = self.write_release_approval(
            receipt, authority, private_key=wrong_private
        )
        with self.assertRaisesRegex(ValueError, "signature is invalid"):
            consumer.verify_release_eligibility(
                receipt,
                wrong_approval,
                android_root=self.android,
                expected_version_name="0.1.0-preview.11",
                expected_version_code=11,
                package_authority_path=package_authority,
                source_graph_path=source_graph,
            )

        wrong_approval.unlink()
        old = datetime.now(UTC).replace(microsecond=0) - timedelta(hours=2)
        stale = self.write_release_approval(
            receipt,
            authority,
            generated=old,
            expires=old + timedelta(hours=1),
        )
        with self.assertRaisesRegex(ValueError, "stale or outside its lifetime"):
            consumer.verify_release_eligibility(
                receipt,
                stale,
                android_root=self.android,
                expected_version_name="0.1.0-preview.11",
                expected_version_code=11,
                package_authority_path=package_authority,
                source_graph_path=source_graph,
            )
        historical = consumer.verify_release_eligibility(
            receipt,
            stale,
            android_root=self.android,
            expected_version_name="0.1.0-preview.11",
            expected_version_code=11,
            package_authority_path=package_authority,
            source_graph_path=source_graph,
            approval_effective_time=old + timedelta(minutes=30),
        )
        self.assertTrue(historical["eligible"])

        escalated = json.loads(stale.read_text(encoding="utf-8"))
        escalated["signingAuthorized"] = True
        stale.write_bytes(gate.pretty_json_bytes(escalated))
        stale.chmod(0o600)
        with self.assertRaisesRegex(ValueError, "posture is invalid"):
            consumer.verify_release_eligibility(
                receipt,
                stale,
                android_root=self.android,
                expected_version_name="0.1.0-preview.11",
                expected_version_code=11,
                package_authority_path=package_authority,
                source_graph_path=source_graph,
            )

    def test_release_consumer_rejects_dirty_or_index_hidden_release_checkout(self) -> None:
        self.inputs["policy"] = gate.POLICY_PATH
        self.inputs["environment_policy"] = gate.ENVIRONMENT_POLICY_PATH
        authority = self.create()
        receipt, approval, package_authority, source_graph = (
            self.release_consumer_inputs(authority)
        )
        untracked = self.android / "untracked-release-input"
        untracked.write_text("hostile\n", encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "checkout is not clean"):
            consumer.verify_release_eligibility(
                receipt,
                approval,
                android_root=self.android,
                expected_version_name="0.1.0-preview.11",
                expected_version_code=11,
                package_authority_path=package_authority,
                source_graph_path=source_graph,
            )
        untracked.unlink()

        project_path = Path("src/Chummer.Android/Chummer.Android.csproj")
        self.git("update-index", "--assume-unchanged", project_path.as_posix())
        project = self.android / project_path
        project.write_text(project.read_text(encoding="utf-8") + "<!-- hostile -->\n")
        self.assertEqual(
            "", self.git("status", "--porcelain=v1", "--untracked-files=all")
        )
        with self.assertRaisesRegex(ValueError, "hidden index flags"):
            consumer.verify_release_eligibility(
                receipt,
                approval,
                android_root=self.android,
                expected_version_name="0.1.0-preview.11",
                expected_version_code=11,
                package_authority_path=package_authority,
                source_graph_path=source_graph,
            )

    def test_release_consumer_rejects_adversarial_receipt_authority(self) -> None:
        self.inputs["policy"] = gate.POLICY_PATH
        self.inputs["environment_policy"] = gate.ENVIRONMENT_POLICY_PATH
        original = self.create()

        def stale_commit(value: dict[str, object]) -> None:
            replacement = "f" * 40
            value["sourceCommit"] = replacement
            value["mainRun"]["run"]["headSha"] = replacement
            value["mainRun"]["p0EventSha"] = replacement
            value["mainRun"]["p0BaseSha"] = replacement

        def different_tree(value: dict[str, object]) -> None:
            replacement = "e" * 40
            value["sourceTree"] = replacement
            value["commonAuthority"]["androidTree"] = replacement
            dependency = value["commonAuthority"]["dependencyGraph"]
            dependency["sources"]["android"]["tree"] = replacement
            dependency["sha256"] = gate.canonical_sha256(
                {"mode": dependency["mode"], "sources": dependency["sources"]}
            )

        def different_graph(value: dict[str, object]) -> None:
            dependency = value["commonAuthority"]["dependencyGraph"]
            dependency["sources"]["core-runtime"]["commit"] = "d" * 40
            dependency["sha256"] = gate.canonical_sha256(
                {"mode": dependency["mode"], "sources": dependency["sources"]}
            )

        def different_version(value: dict[str, object]) -> None:
            value["releaseIdentity"]["versionName"] = "0.1.0-preview.12"
            value["releaseIdentity"]["versionCode"] = 12

        def failed_environment(value: dict[str, object]) -> None:
            value["commonAuthority"]["environmentCompatibilityStatus"] = "fail"

        def different_environment_policy(value: dict[str, object]) -> None:
            value["commonAuthority"]["environmentPolicy"]["sha256"] = "0" * 64

        def failed_main_run(value: dict[str, object]) -> None:
            value["mainRun"]["run"]["conclusion"] = "failure"

        def failed_aggregate(value: dict[str, object]) -> None:
            value["mainRun"]["aggregateStatus"] = "fail"

        def failed_aggregate_job(value: dict[str, object]) -> None:
            aggregate = value["mainRun"]["jobs"][gate.REQUIRED_JOB_NAMES[-1]]
            aggregate["conclusion"] = "failure"

        for label, mutate in (
            ("stale commit", stale_commit),
            ("different tree", different_tree),
            ("different dependency graph", different_graph),
            ("different version", different_version),
            ("failed environment", failed_environment),
            ("different environment policy", different_environment_policy),
            ("failed main run", failed_main_run),
            ("failed aggregate", failed_aggregate),
            ("failed aggregate job", failed_aggregate_job),
        ):
            with self.subTest(label=label):
                candidate = copy.deepcopy(original)
                mutate(candidate)
                unsigned = {
                    key: value
                    for key, value in candidate.items()
                    if key != "eligibilitySha256"
                }
                candidate["eligibilitySha256"] = gate.canonical_sha256(unsigned)
                receipt, approval, package_authority, source_graph = (
                    self.release_consumer_inputs(candidate)
                )
                with self.assertRaises(ValueError):
                    consumer.verify_release_eligibility(
                        receipt,
                        approval,
                        android_root=self.android,
                        expected_version_name="0.1.0-preview.11",
                        expected_version_code=11,
                        package_authority_path=package_authority,
                        source_graph_path=source_graph,
                    )
                receipt.unlink()
                approval.unlink()
                package_authority.unlink()
                source_graph.unlink()

    def test_release_consumer_rejects_a_different_release_source_graph(self) -> None:
        self.inputs["policy"] = gate.POLICY_PATH
        self.inputs["environment_policy"] = gate.ENVIRONMENT_POLICY_PATH
        authority = self.create()
        receipt, approval, package_authority, source_graph = (
            self.release_consumer_inputs(authority)
        )
        graph = json.loads(source_graph.read_text(encoding="utf-8"))
        presentation = next(
            row for row in graph["repositories"] if row["name"] == "chummer6-ui"
        )
        presentation["commit"] = "0" * 40
        source_graph.write_bytes(gate.canonical_json_bytes(graph))
        source_graph.chmod(0o600)
        with self.assertRaisesRegex(ValueError, "presentation"):
            consumer.verify_release_eligibility(
                receipt,
                approval,
                android_root=self.android,
                expected_version_name="0.1.0-preview.11",
                expected_version_code=11,
                package_authority_path=package_authority,
                source_graph_path=source_graph,
            )

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
        self.assertNotIn(
            "pull_request_merge_commit_lineage_reconstruction",
            policy["doesNotAssert"],
        )
        self.assertIn(
            "non_android_dependency_commit_tree_reconstruction",
            policy["doesNotAssert"],
        )
        self.assertEqual(["pull_request"], policy["reviewEvents"])
        self.assertTrue(policy["requiresExactPullRequestAuthority"])
        self.assertTrue(policy["requiresEmptyActionsPullRequestSummaries"])
        self.assertTrue(policy["requiresCommitAssociatedPullRequest"])
        self.assertTrue(policy["requiresExactMergeCommitGraphs"])
        self.assertTrue(policy["requiresExactAggregateCheckRun"])
        self.assertTrue(policy["requiresCanonicalActionsDetailsUrls"])
        self.assertTrue(policy["requiresExactMainCommit"])
        self.assertTrue(policy["requiresExactReleaseIdentity"])
        self.assertTrue(policy["requiresExactDependencyGraph"])
        self.assertTrue(policy["requiresEnvironmentCompatibilityPass"])
        self.assertTrue(policy["requiresSuccessfulMainRun"])
        self.assertTrue(policy["requiresSuccessfulMainAggregate"])
        self.assertFalse(policy["googlePlayUploadAuthorized"])

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
        self.assertIn("review_pull_request_number:", self.text)
        self.assertIn("review_event_sha:", self.text)
        self.assertIn("main_run_id:", self.text)
        self.assertIn("actions: read", self.text)
        self.assertIn("checks: read", self.text)
        self.assertIn("contents: read", self.text)
        self.assertIn("pull-requests: read", self.text)
        self.assertNotIn("actions: write", self.text)
        self.assertNotIn("contents: write", self.text)
        self.assertIn('test "$GITHUB_REF" = refs/heads/main', self.text)

    def test_workflow_queries_explicit_runs_and_never_uploads_to_play(self) -> None:
        self.assertIn("actions/runs/$run_id", self.text)
        self.assertIn("attempts/$run_attempt/jobs?per_page=100", self.text)
        self.assertIn("actions/artifacts/$artifact_id/zip", self.text)
        self.assertIn("pulls/$REVIEW_PULL_REQUEST_NUMBER", self.text)
        self.assertIn("commits/$review_head_sha/pulls", self.text)
        self.assertIn("check-runs/$aggregate_job_id", self.text)
        self.assertIn("git/commits/$review_base_sha", self.text)
        self.assertIn("git/commits/$review_head_sha", self.text)
        self.assertIn("git/commits/$REVIEW_EVENT_SHA", self.text)
        self.assertIn("git/commits/$main_head_sha", self.text)
        self.assertNotIn("google-github-actions/auth", self.text)
        self.assertNotIn("playDeveloper", self.text)
        self.assertNotIn("serviceAccount", self.text)
        self.assertNotIn("gradle-play-publisher", self.text)
        self.assertIn("publicationAuthorized == false", self.text)
        self.assertIn("googlePlayUploadAuthorized == false", self.text)
        self.assertIn("--environment-policy", self.text)
        self.assertIn("--review-head-pull-requests", self.text)
        self.assertIn("--review-aggregate-check-run", self.text)
        self.assertIn("--review-base-commit", self.text)
        self.assertIn("--review-head-commit", self.text)
        self.assertIn("api36-proof-environment-authority.json", self.text)
        self.assertIn('PYTHONDONTWRITEBYTECODE: "1"', self.text)

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
        self.assertIn(
            "REVIEW_PULL_REQUEST_NUMBER: ${{ inputs.review_pull_request_number }}",
            self.text,
        )
        self.assertIn("REVIEW_EVENT_SHA: ${{ inputs.review_event_sha }}", self.text)
        self.assertIn("MAIN_RUN_ID: ${{ inputs.main_run_id }}", self.text)


if __name__ == "__main__":
    unittest.main()
