#!/usr/bin/env python3
"""Select exact inputs for the existing two-green verifier, without authority.

Only trusted default-branch code runs here. GitHub metadata and downloaded P0
JSON are data; this selector never checks out or executes a reviewed PR. The
existing materializer remains responsible for the complete qualification.
"""

from __future__ import annotations

import argparse
import importlib.util
import json
from pathlib import Path
import subprocess
import sys


SCRIPT_DIRECTORY = Path(__file__).resolve().parent
SPEC = importlib.util.spec_from_file_location(
    "api36_two_green_selector_gate",
    SCRIPT_DIRECTORY / "materialize-api36-two-green-eligibility.py",
)
assert SPEC is not None and SPEC.loader is not None
GATE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(GATE)
REPOSITORY = GATE.REPOSITORY
API_PREFIX = f"repos/{REPOSITORY}/"


def strict_json(raw: bytes):
    return json.loads(
        raw.decode("utf-8"),
        object_pairs_hook=GATE.object_without_duplicates,
        parse_constant=lambda _: (_ for _ in ()).throw(ValueError("non-finite JSON")),
    )


def github_json(endpoint: str):
    # Never follow a caller-supplied URL, host, repository, or API next link.
    if not endpoint.startswith(API_PREFIX) or ".." in endpoint:
        raise ValueError("GitHub endpoint is outside the canonical repository")
    result = subprocess.run(
        ["gh", "api", "--hostname", "github.com", "--method", "GET",
         "-H", "Accept: application/vnd.github+json",
         "-H", "X-GitHub-Api-Version: 2022-11-28", endpoint],
        check=False, capture_output=True, timeout=60,
    )
    if result.returncode or len(result.stdout) > GATE.MAX_JSON_ARTIFACT_BYTES:
        raise ValueError("canonical GitHub metadata fetch failed or exceeded its limit")
    return strict_json(result.stdout)


def positive_input(value: object, label: str) -> int:
    if not isinstance(value, str) or not value.isascii() or not value.isdecimal():
        raise ValueError(f"{label} is not a canonical positive integer")
    number = int(value)
    if number < 1 or str(number) != value:
        raise ValueError(f"{label} is not a canonical positive integer")
    return number


def repository_matches(value: object) -> bool:
    return isinstance(value, dict) and (
        value.get("full_name") == REPOSITORY
        and value.get("url") == f"https://api.github.com/repos/{REPOSITORY}"
        and value.get("html_url") == f"https://github.com/{REPOSITORY}"
    )


def read_run(fetch, run_id: int, role: str, workflow_id: int) -> dict:
    value = fetch(f"{API_PREFIX}actions/runs/{run_id}")
    run = GATE.validate_run_metadata(value, expected_id=run_id, role=role)
    if run["workflowId"] != workflow_id:
        raise ValueError(f"{role} run workflow ID differs from the canonical workflow")
    return run


def select_inputs(event: dict, event_name: str, fetch=github_json) -> dict:
    repository = event.get("repository")
    if not repository_matches(repository) or repository.get("default_branch") != "main":
        raise ValueError("event is not from the canonical default-branch repository")
    workflow = fetch(f"{API_PREFIX}actions/workflows/api36-editing-e2e.yml")
    if workflow.get("name") != GATE.WORKFLOW_NAME or workflow.get("path") != GATE.WORKFLOW_PATH:
        raise ValueError("canonical source workflow identity differs")
    workflow_id = GATE._positive_integer(workflow.get("id"), "source workflow ID")
    if event_name == "workflow_run":
        trigger = event.get("workflow_run")
        if event.get("action") != "completed" or not isinstance(trigger, dict):
            raise ValueError("automatic trigger is not a completed workflow run")
        main_id = GATE._positive_integer(trigger.get("id"), "trigger run ID")
        main = read_run(fetch, main_id, "main", workflow_id)
        expected = {
            "id": main["id"], "run_attempt": main["attempt"],
            "head_sha": main["headSha"], "workflow_id": workflow_id,
            "event": "push", "head_branch": "main",
            "status": "completed", "conclusion": "success",
        }
        if (not repository_matches(trigger.get("head_repository"))
                or any(trigger.get(key) != value for key, value in expected.items())):
            raise ValueError("trigger identity/attempt differs from authenticated main run")
        associated = fetch(f"{API_PREFIX}commits/{main['headSha']}/pulls?per_page=100")
        if not isinstance(associated, list) or len(associated) != 1:
            raise ValueError("main commit must resolve to exactly one merged pull request")
        number = GATE._positive_integer(associated[0].get("number"), "pull request number")
        pull = fetch(f"{API_PREFIX}pulls/{number}")
        base, head = pull.get("base", {}), pull.get("head", {})
        if (pull.get("number") != number or pull.get("merged") is not True
                or pull.get("state") != "closed"
                or pull.get("merge_commit_sha") != main["headSha"]
                or associated[0].get("merge_commit_sha") != main["headSha"]
                or base.get("ref") != "main"
                or not repository_matches(base.get("repo"))
                or not repository_matches(head.get("repo"))):
            raise ValueError("main commit does not bind an exact merged same-repository PR")
        head_sha = GATE._sha40(head.get("sha"), "review head SHA")
        merged = GATE._utc(pull.get("merged_at"), "PR merge time")
        if merged > GATE._utc(main["startedAtUtc"], "main start time"):
            raise ValueError("PR merged after main started")
        listing = fetch(
            f"{API_PREFIX}actions/workflows/{workflow_id}/runs"
            f"?event=pull_request&status=success&head_sha={head_sha}&per_page=100"
        )
        rows = listing.get("workflow_runs")
        if (not isinstance(rows, list) or type(listing.get("total_count")) is not int
                or listing["total_count"] != len(rows)):
            raise ValueError("review run listing is truncated or malformed; use manual recovery")
        candidates, seen = [], set()
        for row in rows:
            run_id = GATE._positive_integer(row.get("id"), "review run ID")
            if run_id in seen:
                raise ValueError("duplicate review run identity")
            seen.add(run_id)
            run = read_run(fetch, run_id, "review", workflow_id)
            if run["headSha"] != head_sha or run["headBranch"] != head.get("ref"):
                raise ValueError("review run does not bind the merged PR head")
            if run_id < main_id and GATE._utc(run["completedAtUtc"], "review completion") <= merged:
                candidates.append(run)
        if len(candidates) != 1:
            raise ValueError("expected exactly one eligible review run; use manual recovery")
        review = candidates[0]
        explicit_event_sha = ""
    elif event_name == "workflow_dispatch":
        inputs = event.get("inputs", {})
        main_id = positive_input(inputs.get("main_run_id"), "main run ID")
        review_id = positive_input(inputs.get("review_run_id"), "review run ID")
        number = positive_input(inputs.get("review_pull_request_number"), "PR number")
        explicit_event_sha = GATE._sha40(inputs.get("review_event_sha"), "review event SHA")
        main = read_run(fetch, main_id, "main", workflow_id)
        review = read_run(fetch, review_id, "review", workflow_id)
    else:
        raise ValueError("unsupported qualification trigger")
    if (review["id"] >= main["id"]
            or GATE._utc(review["completedAtUtc"], "review completion")
            >= GATE._utc(main["startedAtUtc"], "main start")):
        raise ValueError("main run must follow the distinct completed review run")
    return {
        "review_run_id": review["id"], "review_run_attempt": review["attempt"],
        "review_head_sha": review["headSha"], "review_pull_request_number": number,
        "explicit_review_event_sha": explicit_event_sha,
        "main_run_id": main["id"], "main_run_attempt": main["attempt"],
        "main_head_sha": main["headSha"],
    }


def review_event_sha(evidence: Path, explicit: str) -> str:
    run_json = strict_json((evidence / "review/run.json").read_bytes())
    run = GATE.validate_run_metadata(run_json, expected_id=run_json["id"], role="review")
    metadata = GATE.validate_artifact_metadata(
        strict_json((evidence / "review/artifacts.json").read_bytes()), run=run, role="review",
    )["p0"]
    p0, _, _ = GATE.extract_exact_json_archive(
        GATE.StableFile((evidence / "review/p0.zip").resolve(), "review P0 archive"),
        metadata=metadata, expected_member=GATE.P0.OUTPUT_NAME,
    )
    github_run = p0.get("githubRun", {})
    producer_attempt = GATE._positive_integer(github_run.get("attempt"), "review producer attempt")
    if (github_run.get("id") != run["id"] or github_run.get("eventName") != "pull_request"
            or github_run.get("headSha") != run["headSha"] or producer_attempt > run["attempt"]):
        raise ValueError("review P0 run identity differs")
    event_sha = GATE._sha40(github_run.get("eventSha"), "review P0 event SHA")
    if explicit and explicit != event_sha:
        raise ValueError("explicit review event SHA differs from digest-bound P0 data")
    return event_sha


def check_runs(evidence: Path, fetch=github_json) -> None:
    # A rerun must not silently retarget an in-flight handoff to another attempt.
    # Producer attempts can still precede the selected aggregate attempt.
    for role in ("review", "main"):
        snapshot = strict_json((evidence / role / "run.json").read_bytes())
        expected = GATE.validate_run_metadata(snapshot, expected_id=snapshot["id"], role=role)
        current = read_run(fetch, expected["id"], role, expected["workflowId"])
        if current != expected:
            raise ValueError(f"{role} run changed during eligibility handoff")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest="command", required=True)
    select = sub.add_parser("select")
    select.add_argument("--event-file", required=True, type=Path)
    select.add_argument("--event-name", required=True, choices=("workflow_run", "workflow_dispatch"))
    event = sub.add_parser("review-event")
    event.add_argument("--evidence-root", required=True, type=Path)
    event.add_argument("--explicit-event-sha", default="")
    check = sub.add_parser("check-runs")
    check.add_argument("--evidence-root", required=True, type=Path)
    args = parser.parse_args()
    try:
        if args.command == "select":
            selected = select_inputs(strict_json(args.event_file.read_bytes()), args.event_name)
            # Every output is a validated integer, SHA, or the empty optional SHA.
            for key, value in selected.items():
                print(f"{key}={value}")
        elif args.command == "review-event":
            print(review_event_sha(args.evidence_root, args.explicit_event_sha))
        else:
            check_runs(args.evidence_root)
    except (ValueError, KeyError, TypeError, OSError, subprocess.TimeoutExpired) as error:
        print(f"two-green input selection failed: {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
