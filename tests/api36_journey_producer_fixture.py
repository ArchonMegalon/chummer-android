"""Synthetic GitHub responses; no network or self-issued producer receipt."""

import copy
import hashlib
import io
import json
from pathlib import Path
import zipfile

from api36_journey_producer_authority import JourneyProducerAuthority, REPOSITORY, WORKFLOW_NAME, WORKFLOW_PATH


HEAD_SHA = "b" * 40


class ProducerFixture:
    def __init__(self, root: Path, *, run_id: int, aggregate_attempt: int, producers: dict[str, int]):
        self.responses = {}
        self.calls = []
        self.run_id = run_id
        self.aggregate_attempt = aggregate_attempt
        self.api = f"repos/{REPOSITORY}"
        self.runs = {}
        self.jobs = {}
        self.artifacts = {"total_count": len(producers), "artifacts": []}
        self.responses[f"{self.api}/actions/runs/{run_id}/artifacts?per_page=100"] = self.artifacts
        for attempt in set(producers.values()) | {aggregate_attempt}:
            run = {
                "id": run_id, "run_attempt": attempt, "head_sha": HEAD_SHA,
                "name": WORKFLOW_NAME, "path": WORKFLOW_PATH, "workflow_id": 44,
                "repository": {"id": 33, "full_name": REPOSITORY},
                "head_repository": {"id": 33, "full_name": REPOSITORY},
                "head_branch": "test-branch", "event": "pull_request",
                "run_started_at": f"2026-09-08T{attempt:02}:00:00Z",
                "url": f"https://api.github.com/{self.api}/actions/runs/{run_id}",
            }
            jobs = {"total_count": 0, "jobs": []}
            self.runs[attempt], self.jobs[attempt] = run, jobs
            self.responses[f"{self.api}/actions/runs/{run_id}/attempts/{attempt}"] = run
            self.responses[f"{self.api}/actions/runs/{run_id}/attempts/{attempt}/jobs?per_page=100"] = jobs
        for index, (journey, attempt) in enumerate(producers.items(), start=1):
            name = f"chummer-android-api36-phone-{journey}-evidence-{run_id}"
            directory = root / name
            archive = io.BytesIO()
            with zipfile.ZipFile(archive, "w") as stream:
                for path in sorted(directory.rglob("*")):
                    if path.is_file():
                        stream.writestr(str(path.relative_to(directory)), path.read_bytes())
            data = archive.getvalue()
            artifact_id, job_id = 1000 + index, 2000 + index
            # Distinct upload windows, independent of the sidecar under test.
            start, end = f"2026-09-08T{attempt:02}:00:00Z", f"2026-09-08T{attempt:02}:01:00Z"
            artifact = {
                "id": artifact_id, "name": name, "expired": False,
                "workflow_run": {"id": run_id, "head_sha": HEAD_SHA,
                                 "repository_id": 33, "head_repository_id": 33,
                                 "head_branch": "test-branch"},
                "created_at": end, "updated_at": end,
                "size_in_bytes": len(data), "digest": "sha256:" + hashlib.sha256(data).hexdigest(),
            }
            job = {
                "id": job_id, "name": f"phone API 36 SR5 wizard persistence ({journey})",
                "status": "completed", "conclusion": "success", "workflow_name": WORKFLOW_NAME,
                "run_id": run_id, "run_attempt": attempt, "head_sha": HEAD_SHA,
                "url": f"https://api.github.com/{self.api}/actions/jobs/{job_id}",
                "html_url": f"https://github.com/{REPOSITORY}/actions/runs/{run_id}/job/{job_id}",
                "check_run_url": f"https://api.github.com/{self.api}/check-runs/{job_id}",
                "started_at": start, "completed_at": end,
                "steps": [{"name": "Upload phone receipts and screenshots", "status": "completed",
                           "conclusion": "success", "started_at": start, "completed_at": end}],
            }
            self.artifacts["artifacts"].append(artifact)
            self.jobs[attempt]["jobs"].append(job)
            self.jobs[attempt]["total_count"] += 1
            self.responses[f"{self.api}/actions/artifacts/{artifact_id}"] = artifact
            self.responses[f"{self.api}/actions/artifacts/{artifact_id}/zip"] = data

    def authority(self):
        return JourneyProducerAuthority(
            self, repository=REPOSITORY, run_id=self.run_id,
            aggregate_attempt=self.aggregate_attempt, head_sha=HEAD_SHA,
        )

    def fetch(self, endpoint: str, *, artifact: bool = False):
        self.calls.append((endpoint, artifact))
        value = self.responses[endpoint]
        return value if isinstance(value, bytes) else json.dumps(copy.deepcopy(value)).encode()
