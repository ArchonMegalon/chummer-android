"""Authenticate a stable journey artifact's producing job, not its consumer attempt.

This is an aggregate input check, not a new receipt or publication authority.
GitHub artifact metadata has no producer-attempt field: the exact successful
matrix job's upload window establishes it, and the API digest binds its ZIP to
the already downloaded files. A sidecar alone cannot establish an older attempt.
"""

from __future__ import annotations

from datetime import datetime
import hashlib
import io
import json
import os
from pathlib import Path, PurePosixPath
import re
import stat
import tempfile
from typing import Any
import zipfile

from api36_proof_environment_authority import StableFile, object_without_duplicates
from api36_wizard_gate_contract import journey_map


REPOSITORY = "ArchonMegalon/chummer-android"
WORKFLOW_PATH = ".github/workflows/api36-editing-e2e.yml"
WORKFLOW_NAME = "API 36 phone beta SR5 wizard E2E"
UPLOAD_STEP = "Upload phone receipts and screenshots"
MAX_ARCHIVE_BYTES = 32 * 1024 * 1024


def positive_integer(value: Any, label: str) -> int:
    if type(value) is not int or value <= 0:
        raise ValueError(f"journey producer {label} must be a positive integer")
    return value


def utc(value: Any) -> datetime:
    if not isinstance(value, str) or not re.fullmatch(
        r"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z", value
    ):
        raise ValueError("journey producer timestamp is not canonical UTC")
    return datetime.strptime(value, "%Y-%m-%dT%H:%M:%SZ")


class JourneyProducerAuthority:
    def __init__(
        self, client: Any, *, repository: str, run_id: int,
        aggregate_attempt: int, head_sha: str,
    ) -> None:
        if repository != REPOSITORY or not isinstance(head_sha, str) or not re.fullmatch(
            r"[0-9a-f]{40}", head_sha
        ):
            raise ValueError("journey producer repository/head authority differs")
        self.client = client
        self.run_id = positive_integer(run_id, "run ID")
        self.aggregate_attempt = positive_integer(aggregate_attempt, "aggregate attempt")
        self.head_sha = head_sha
        self.api = f"repos/{repository}"
        self.api_url = f"https://api.github.com/{self.api}"
        self.html_url = f"https://github.com/{repository}"
        self.run = self.fetch_json(
            f"{self.api}/actions/runs/{run_id}/attempts/{aggregate_attempt}"
        )
        self.require_run(self.run, aggregate_attempt)
        self.attempt_started = {aggregate_attempt: utc(self.run.get("run_started_at"))}
        self.jobs: dict[int, list[dict[str, Any]]] = {}
        self.verified_artifacts: dict[str, dict[str, Any]] = {}
        self.artifacts = self.fetch_json(
            f"{self.api}/actions/runs/{run_id}/artifacts?per_page=100"
        )
        self.rows(self.artifacts, "artifacts")

    @classmethod
    def from_environment(cls, **authority: Any) -> JourneyProducerAuthority:
        # Reuse the existing read-only provenance client (canonical TLS/redirect
        # handling and bounded reads). Never invoke any signing operation.
        from sign_api36_two_green_release_approval import GitHubApiClient

        token = os.environ.get("GH_TOKEN", "")
        if not token:
            raise ValueError("journey producer authenticated GitHub token is missing")
        with tempfile.NamedTemporaryFile(mode="w", encoding="ascii") as stream:
            os.fchmod(stream.fileno(), 0o600)
            stream.write(token)
            stream.flush()
            client = GitHubApiClient(Path(stream.name))
        return cls(client, **authority)

    def fetch_json(self, endpoint: str) -> dict[str, Any]:
        try:
            value = json.loads(
                self.client.fetch(endpoint), object_pairs_hook=object_without_duplicates,
                parse_constant=lambda token: (_ for _ in ()).throw(
                    ValueError("journey producer JSON contains a non-finite value")
                ),
            )
        except (UnicodeDecodeError, json.JSONDecodeError) as error:
            raise ValueError("journey producer GitHub response is not JSON") from error
        if not isinstance(value, dict):
            raise ValueError("journey producer GitHub response is not one object")
        return value

    @staticmethod
    def rows(value: dict[str, Any], field: str) -> list[dict[str, Any]]:
        rows = value.get(field)
        if (
            type(value.get("total_count")) is not int
            or not isinstance(rows, list) or value["total_count"] != len(rows)
            or any(not isinstance(row, dict) for row in rows)
        ):
            raise ValueError(f"journey producer {field} response is incomplete")
        return rows

    def require_run(self, run: dict[str, Any], attempt: int) -> None:
        repository = run.get("repository", {})
        head_repository = run.get("head_repository", {})
        if (
            not isinstance(repository, dict) or not isinstance(head_repository, dict)
            or positive_integer(run.get("id"), "run ID") != self.run_id
            or positive_integer(run.get("run_attempt"), "run attempt") != attempt
            or run.get("head_sha") != self.head_sha
            or run.get("path") != WORKFLOW_PATH or run.get("name") != WORKFLOW_NAME
            or repository.get("full_name") != REPOSITORY
            or head_repository.get("full_name") != REPOSITORY
            or positive_integer(repository.get("id"), "repository ID")
            != positive_integer(head_repository.get("id"), "head repository ID")
            or run.get("url") != f"{self.api_url}/actions/runs/{self.run_id}"
        ):
            raise ValueError("journey producer run/repository/workflow authority differs")
        positive_integer(run.get("workflow_id"), "workflow ID")
        if hasattr(self, "run") and any(
            run.get(field) != self.run.get(field)
            for field in ("workflow_id", "repository", "head_repository", "head_branch", "event")
        ):
            raise ValueError("journey producer run changed between attempts")

    def require(
        self, *, journey: str, execution: dict[str, Any], directory: Path,
    ) -> dict[str, str]:
        attempt = positive_integer(execution.get("runAttempt"), "attempt")
        if (
            journey not in journey_map() or attempt > self.aggregate_attempt
            or execution != {"runId": self.run_id, "runAttempt": attempt, "matrixJourney": journey}
            or type(execution.get("runId")) is not int
        ):
            raise ValueError("journey producer execution authority differs")
        if attempt not in self.jobs:
            run = self.fetch_json(
                f"{self.api}/actions/runs/{self.run_id}/attempts/{attempt}"
            )
            self.require_run(run, attempt)
            self.attempt_started[attempt] = utc(run.get("run_started_at"))
            self.jobs[attempt] = self.rows(self.fetch_json(
                f"{self.api}/actions/runs/{self.run_id}/attempts/{attempt}/jobs?per_page=100"
            ), "jobs")
        name = f"chummer-android-api36-phone-{journey}-evidence-{self.run_id}"
        matches = [
            row for row in self.artifacts["artifacts"] if row.get("name") == name
        ]
        jobs = [
            row for row in self.jobs[attempt]
            if row.get("name") == f"phone API 36 SR5 wizard persistence ({journey})"
        ]
        if len(matches) != 1 or len(jobs) != 1:
            raise ValueError("journey producer artifact/job cardinality differs")
        artifact, job = matches[0], jobs[0]
        artifact_id = positive_integer(artifact.get("id"), "artifact ID")
        job_id = positive_integer(job.get("id"), "job ID")
        workflow_run = artifact.get("workflow_run")
        if (
            artifact.get("expired") is not False or not isinstance(workflow_run, dict)
            or positive_integer(workflow_run.get("id"), "artifact run ID") != self.run_id
            or workflow_run.get("head_sha") != self.head_sha
            or workflow_run.get("head_branch") != self.run.get("head_branch")
            or positive_integer(workflow_run.get("repository_id"), "artifact repository ID")
            != self.run["repository"]["id"]
            or positive_integer(workflow_run.get("head_repository_id"), "artifact head repository ID")
            != self.run["head_repository"]["id"]
            or job.get("status") != "completed" or job.get("conclusion") != "success"
            or job.get("workflow_name") != WORKFLOW_NAME
            or positive_integer(job.get("run_id"), "job run ID") != self.run_id
            or positive_integer(job.get("run_attempt"), "job attempt") != attempt
            or job.get("head_sha") != self.head_sha
            or job.get("url") != f"{self.api_url}/actions/jobs/{job_id}"
            or job.get("html_url") != f"{self.html_url}/actions/runs/{self.run_id}/job/{job_id}"
            or job.get("check_run_url") != f"{self.api_url}/check-runs/{job_id}"
        ):
            raise ValueError("journey producer artifact/job authority differs")
        steps = job.get("steps")
        uploads = [
            step for step in steps
            if isinstance(step, dict) and step.get("name") == UPLOAD_STEP
        ] if isinstance(steps, list) else []
        if (
            len(uploads) != 1 or uploads[0].get("status") != "completed"
            or uploads[0].get("conclusion") != "success"
        ):
            raise ValueError("journey producer successful upload step is missing")
        upload = uploads[0]
        # Rerun APIs copy successful jobs into the later attempt, including
        # their old upload timestamps. Such a copied row is not a new producer.
        if not (
            self.attempt_started[attempt] <= utc(job.get("started_at"))
            <= utc(upload.get("started_at"))
            <= utc(artifact.get("created_at")) <= utc(artifact.get("updated_at"))
            <= utc(upload.get("completed_at")) <= utc(job.get("completed_at"))
        ) or (
            attempt < self.aggregate_attempt
            and utc(job.get("completed_at")) > self.attempt_started[self.aggregate_attempt]
        ):
            raise ValueError("journey producer artifact is outside the upload window")
        size = positive_integer(artifact.get("size_in_bytes"), "archive size")
        digest = artifact.get("digest")
        if (
            size > MAX_ARCHIVE_BYTES or not isinstance(digest, str)
            or not re.fullmatch(r"sha256:[0-9a-f]{64}", digest)
        ):
            raise ValueError("journey producer archive size/digest is invalid")
        archive_bytes = self.client.fetch(
            f"{self.api}/actions/artifacts/{artifact_id}/zip", artifact=True,
        )
        if (
            len(archive_bytes) != size
            or "sha256:" + hashlib.sha256(archive_bytes).hexdigest() != digest
        ):
            raise ValueError("journey producer archive size/digest differs")
        bindings = self.require_archive_files(archive_bytes, directory)
        # A stable-name overwrite while validation runs invalidates this capture.
        current = self.fetch_json(f"{self.api}/actions/artifacts/{artifact_id}")
        if current != artifact:
            raise ValueError("journey producer artifact metadata changed")
        self.verified_artifacts[name] = artifact
        print(
            f"api36_journey_producer=verified journey={journey} run_id={self.run_id} "
            f"aggregate_attempt={self.aggregate_attempt} producer_attempt={attempt} "
            f"job_id={job_id} artifact_id={artifact_id} digest={digest}"
        )
        return bindings

    def recheck(self) -> None:
        current = self.rows(self.fetch_json(
            f"{self.api}/actions/runs/{self.run_id}/artifacts?per_page=100"
        ), "artifacts")
        for name, artifact in self.verified_artifacts.items():
            if [row for row in current if row.get("name") == name] != [artifact]:
                raise ValueError("journey producer stable artifact changed before seal")

    @staticmethod
    def require_archive_files(data: bytes, directory: Path) -> dict[str, str]:
        bindings: dict[str, str] = {}
        try:
            with zipfile.ZipFile(io.BytesIO(data)) as archive:
                members = archive.infolist()
                if sum(member.file_size for member in members) > MAX_ARCHIVE_BYTES:
                    raise ValueError("journey producer expanded archive is oversized")
                for member in members:
                    path = PurePosixPath(member.filename)
                    mode = (member.external_attr >> 16) & 0xFFFF
                    if (
                        member.is_dir() or member.flag_bits & 1
                        or stat.S_IFMT(mode) not in (0, stat.S_IFREG)
                        or path.is_absolute() or ".." in path.parts
                        or str(path) != member.filename or "\\" in member.filename
                        or member.filename in bindings
                    ):
                        raise ValueError("journey producer archive member is unsafe")
                    snapshot = StableFile(
                        directory / member.filename, "journey producer artifact member",
                    )
                    payload = archive.read(member)
                    if snapshot.data != payload:
                        raise ValueError("journey producer downloaded artifact member differs")
                    bindings[member.filename] = snapshot.sha256
                    snapshot.recheck()
        except (zipfile.BadZipFile, RuntimeError) as error:
            raise ValueError("journey producer archive is invalid") from error
        actual = {
            str(path.relative_to(directory))
            for path in directory.rglob("*") if not path.is_dir()
        }
        if set(bindings) != actual:
            raise ValueError("journey producer downloaded artifact members differ")
        return bindings
