from __future__ import annotations

import copy
import hashlib
import importlib.util
import io
import json
import os
from pathlib import Path
import re
import subprocess
import tempfile
import unittest
import zipfile


ROOT = Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location(
    "two_green_input_resolver", ROOT / "scripts/resolve-api36-two-green-inputs.py",
)
assert SPEC and SPEC.loader
resolver = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(resolver)
GATE = resolver.GATE
PREFIX = resolver.API_PREFIX
REPOSITORY = {
    "full_name": GATE.REPOSITORY,
    "url": f"https://api.github.com/repos/{GATE.REPOSITORY}",
    "html_url": f"https://github.com/{GATE.REPOSITORY}",
    "default_branch": "main",
}
MAIN_SHA, REVIEW_SHA, EVENT_SHA = "a" * 40, "b" * 40, "c" * 40


def run_metadata(run_id=200, *, review=False, attempt=1):
    api, html = REPOSITORY["url"], REPOSITORY["html_url"]
    return {
        "id": run_id, "run_attempt": attempt, "workflow_id": 42,
        "name": GATE.WORKFLOW_NAME, "path": GATE.WORKFLOW_PATH,
        "event": "pull_request" if review else "push",
        "head_branch": "topic" if review else "main",
        "head_sha": REVIEW_SHA if review else MAIN_SHA,
        "repository": copy.deepcopy(REPOSITORY), "head_repository": copy.deepcopy(REPOSITORY),
        "status": "completed", "conclusion": "success", "pull_requests": [],
        "check_suite_id": run_id + 500,
        "url": f"{api}/actions/runs/{run_id}",
        "html_url": f"{html}/actions/runs/{run_id}",
        "jobs_url": f"{api}/actions/runs/{run_id}/jobs",
        "artifacts_url": f"{api}/actions/runs/{run_id}/artifacts",
        "check_suite_url": f"{api}/check-suites/{run_id + 500}",
        "created_at": f"2026-09-15T{'10' if review else '12'}:00:00Z",
        "run_started_at": f"2026-09-15T{'10' if review else '12'}:00:01Z",
        "updated_at": f"2026-09-15T{'10' if review else '12'}:30:00Z",
    }


class InputSelectionTests(unittest.TestCase):
    def setUp(self):
        self.main, self.review = run_metadata(), run_metadata(100, review=True)
        self.event = {
            "repository": copy.deepcopy(REPOSITORY), "action": "completed",
            "workflow_run": copy.deepcopy(self.main),
        }
        self.pull = {
            "number": 65, "merged": True, "state": "closed", "merge_commit_sha": MAIN_SHA,
            "merged_at": "2026-09-15T11:00:00Z",
            "base": {"ref": "main", "repo": copy.deepcopy(REPOSITORY)},
            "head": {"ref": "topic", "sha": REVIEW_SHA, "repo": copy.deepcopy(REPOSITORY)},
        }
        self.runs_endpoint = (
            f"{PREFIX}actions/workflows/42/runs?event=pull_request&status=success"
            f"&head_sha={REVIEW_SHA}&per_page=100"
        )
        self.responses = {
            f"{PREFIX}actions/workflows/api36-editing-e2e.yml": {
                "id": 42, "name": GATE.WORKFLOW_NAME, "path": GATE.WORKFLOW_PATH,
            },
            f"{PREFIX}actions/runs/200": self.main,
            f"{PREFIX}actions/runs/100": self.review,
            f"{PREFIX}commits/{MAIN_SHA}/pulls?per_page=100": [copy.deepcopy(self.pull)],
            f"{PREFIX}pulls/65": self.pull,
            self.runs_endpoint: {"total_count": 1, "workflow_runs": [{"id": 100}]},
        }
        self.calls = []

    def fetch(self, endpoint):
        self.calls.append(endpoint)
        return copy.deepcopy(self.responses[endpoint])

    def select(self):
        return resolver.select_inputs(self.event, "workflow_run", self.fetch)

    def test_frozen_candidate_resolves_without_reading_moving_main(self):
        selected = self.select()
        self.assertEqual(MAIN_SHA, selected["main_head_sha"])
        self.assertEqual(100, selected["review_run_id"])
        self.assertEqual(65, selected["review_pull_request_number"])
        self.assertEqual("", selected["explicit_review_event_sha"])
        self.assertFalse(any("ref/heads/main" in path or "commits/main" in path for path in self.calls))

    def test_trigger_must_match_authenticated_run_not_just_workflow_name(self):
        for field, replacement in (
            ("id", 201), ("run_attempt", 2), ("head_sha", "d" * 40),
            ("workflow_id", 43), ("event", "pull_request"), ("head_branch", "topic"),
            ("conclusion", "failure"), ("status", "in_progress"),
        ):
            with self.subTest(field=field):
                event = copy.deepcopy(self.event)
                event["workflow_run"][field] = replacement
                with self.assertRaises((ValueError, KeyError)):
                    resolver.select_inputs(event, "workflow_run", self.fetch)

    def test_foreign_event_or_pr_repositories_are_rejected(self):
        for target in (self.event["repository"], self.event["workflow_run"]["head_repository"],
                       self.main["repository"], self.main["head_repository"],
                       self.pull["head"]["repo"], self.pull["base"]["repo"]):
            with self.subTest(target=target):
                target["full_name"] = "someone/fork"
                with self.assertRaises(ValueError):
                    self.select()
                target["full_name"] = GATE.REPOSITORY

    def test_forged_workflow_name_does_not_hide_wrong_registered_id(self):
        self.main["workflow_id"] = 43
        self.event["workflow_run"]["workflow_id"] = 43
        with self.assertRaisesRegex(ValueError, "workflow ID"):
            self.select()

    def test_exactly_one_merged_pr_is_required(self):
        endpoint = f"{PREFIX}commits/{MAIN_SHA}/pulls?per_page=100"
        for rows in ([], [self.pull, self.pull]):
            self.responses[endpoint] = rows
            with self.assertRaisesRegex(ValueError, "exactly one"):
                self.select()

    def test_unmerged_or_different_merge_commit_cannot_supply_review(self):
        for key, value in (("merged", False), ("merge_commit_sha", "d" * 40), ("state", "open")):
            original = self.pull[key]
            self.pull[key] = value
            with self.assertRaisesRegex(ValueError, "exact merged"):
                self.select()
            self.pull[key] = original

    def test_multiple_successful_review_runs_are_ambiguous(self):
        self.responses[f"{PREFIX}actions/runs/101"] = run_metadata(101, review=True)
        self.responses[self.runs_endpoint] = {"total_count": 2, "workflow_runs": [{"id": 100}, {"id": 101}]}
        with self.assertRaisesRegex(ValueError, "exactly one eligible review"):
            self.select()

    def test_truncated_and_duplicate_run_lists_fail(self):
        for listing in (
            {"total_count": 101, "workflow_runs": [{"id": 100}]},
            {"total_count": True, "workflow_runs": [{"id": 100}]},
            {"total_count": 2, "workflow_runs": [{"id": 100}, {"id": 100}]},
        ):
            self.responses[self.runs_endpoint] = listing
            with self.assertRaises(ValueError):
                self.select()

    def test_review_must_bind_pr_head_and_complete_before_merge(self):
        for field, value in (("head_sha", "d" * 40), ("head_branch", "other"),
                             ("updated_at", "2026-09-15T11:30:00Z")):
            original = self.review[field]
            self.review[field] = value
            with self.assertRaises(ValueError):
                self.select()
            self.review[field] = original

    def test_manual_recovery_retains_explicit_selection(self):
        event = {"repository": copy.deepcopy(REPOSITORY), "inputs": {
            "main_run_id": "200", "review_run_id": "100",
            "review_pull_request_number": "65", "review_event_sha": EVENT_SHA,
        }}
        selected = resolver.select_inputs(event, "workflow_dispatch", self.fetch)
        self.assertEqual(EVENT_SHA, selected["explicit_review_event_sha"])
        self.assertEqual(100, selected["review_run_id"])
        self.assertNotIn(self.runs_endpoint, self.calls)
        for value in ("0100", "100\n", "1; echo unsafe", "０１００"):
            event["inputs"]["review_run_id"] = value
            with self.assertRaises(ValueError):
                resolver.select_inputs(event, "workflow_dispatch", self.fetch)

    def test_current_attempt_recheck_rejects_rerun_during_collection(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            for role, run in (("review", self.review), ("main", self.main)):
                (root / role).mkdir()
                (root / role / "run.json").write_text(json.dumps(run))
            resolver.check_runs(root, self.fetch)
            self.main["run_attempt"] = 2
            with self.assertRaisesRegex(ValueError, "changed during"):
                resolver.check_runs(root, self.fetch)
            self.main["run_attempt"] = 1
            self.review["status"] = "in_progress"
            with self.assertRaisesRegex(ValueError, "not completed"):
                resolver.check_runs(root, self.fetch)


class ReviewArchiveTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name).resolve()
        (self.root / "review").mkdir()
        self.run = run_metadata(100, review=True, attempt=2)
        self.p0 = {"githubRun": {
            "id": 100, "attempt": 1, "eventName": "pull_request",
            "headSha": REVIEW_SHA, "eventSha": EVENT_SHA,
        }}
        self.write_archive()

    def write_archive(self, member=GATE.P0.OUTPUT_NAME):
        buffer = io.BytesIO()
        with zipfile.ZipFile(buffer, "w") as archive:
            archive.writestr(member, json.dumps(self.p0))
        raw = buffer.getvalue()
        (self.root / "review/p0.zip").write_bytes(raw)
        (self.root / "review/run.json").write_text(json.dumps(self.run))
        rows = []
        for artifact_id, name in (
            (1, "chummer-android-api36-phone-sr5-wizard-aggregate-100-2"),
            (2, "chummer-android-p0-pr-authority-100-2"),
        ):
            rows.append({
                "id": artifact_id, "name": name, "expired": False,
                "workflow_run": {"id": 100, "head_sha": REVIEW_SHA},
                "digest": "sha256:" + hashlib.sha256(raw).hexdigest(), "size_in_bytes": len(raw),
                "created_at": "2026-09-15T10:20:00Z", "expires_at": "2026-10-15T10:20:00Z",
            })
        (self.root / "review/artifacts.json").write_text(json.dumps({"total_count": 2, "artifacts": rows}))

    def test_digest_bound_review_event_allows_earlier_apk_producer_attempt(self):
        self.assertEqual(EVENT_SHA, resolver.review_event_sha(self.root, ""))
        self.assertEqual(EVENT_SHA, resolver.review_event_sha(self.root, EVENT_SHA))

    def test_explicit_manual_event_must_match_archive(self):
        with self.assertRaisesRegex(ValueError, "explicit review event"):
            resolver.review_event_sha(self.root, "d" * 40)

    def test_substituted_archive_fails_before_selector_is_consumed(self):
        archive = self.root / "review/p0.zip"
        raw = archive.read_bytes()
        archive.write_bytes(raw.replace(EVENT_SHA.encode(), b"d" * 40))
        with self.assertRaisesRegex(ValueError, "digest differs"):
            resolver.review_event_sha(self.root, "")

    def test_p0_run_and_producer_identity_cannot_drift(self):
        for field, value in (("id", 101), ("attempt", 3), ("eventName", "push"), ("headSha", MAIN_SHA)):
            original = self.p0["githubRun"][field]
            self.p0["githubRun"][field] = value
            self.write_archive()
            with self.assertRaises(ValueError):
                resolver.review_event_sha(self.root, "")
            self.p0["githubRun"][field] = original

    def test_unsafe_archive_member_never_becomes_executable_input(self):
        self.write_archive("../selector.py")
        with self.assertRaisesRegex(ValueError, "exactly"):
            resolver.review_event_sha(self.root, "")


class WorkflowBoundaryTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.workflow = (ROOT / ".github/workflows/api36-two-consecutive-green.yml").read_text()

    def test_candidate_guard_accepts_later_controller_but_not_wrong_head_or_policy(self):
        block = re.search(
            r"- name: Bind candidate HEAD.*?        run: \|\n(.*?)(?=\n      - name:)",
            self.workflow, re.S,
        ).group(1)
        script = "\n".join(line[10:] for line in block.splitlines())
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            for name in ("chummer-android", "chummer-android-controller"):
                repo = root / name
                (repo / "eng").mkdir(parents=True)
                for policy in ("api36-sr5-wizard-gate-authority.json", "design-policy-authority.json",
                               "api36-proof-environment-authority.json", "api36-two-consecutive-green-authority.json"):
                    (repo / "eng" / policy).write_text("{}\n")
                (repo / "ordinary-source.txt").write_text(name)
                subprocess.run(["git", "init", "-q", str(repo)], check=True)
                subprocess.run(["git", "-C", str(repo), "add", "."], check=True)
                subprocess.run(["git", "-C", str(repo), "-c", "user.name=Fixture", "-c",
                                "user.email=fixture@example.invalid", "commit", "-qm", name], check=True)
            candidate = root / "chummer-android"
            expected = subprocess.check_output(["git", "-C", str(candidate), "rev-parse", "HEAD"], text=True).strip()
            env = {**os.environ, "MAIN_HEAD_SHA": expected}
            def invoke():
                return subprocess.run(["bash", "-c", script], cwd=root, env=env, capture_output=True)
            self.assertEqual(0, invoke().returncode)
            env["MAIN_HEAD_SHA"] = "f" * 40
            self.assertNotEqual(0, invoke().returncode)
            env["MAIN_HEAD_SHA"] = expected
            (candidate / "eng/design-policy-authority.json").write_text('{"changed":true}\n')
            self.assertNotEqual(0, invoke().returncode)

    def test_only_default_branch_controller_scripts_are_executed(self):
        self.assertIn("ref: ${{ github.sha }}", self.workflow)
        self.assertIn("ref: ${{ steps.resolve.outputs.main_head_sha }}", self.workflow)
        self.assertIn("github.ref == 'refs/heads/main'", self.workflow)
        self.assertIn("github.event.workflow_run.event == 'push'", self.workflow)
        self.assertIn("github.event.workflow_run.head_repository.full_name == github.repository", self.workflow)
        self.assertNotRegex(self.workflow, r"python3[^\n]* chummer-android/scripts/")
        self.assertNotIn("secrets.", self.workflow)
        self.assertNotIn(": write", self.workflow)
        self.assertIn("github.event.workflow_run.id || inputs.main_run_id", self.workflow)
        self.assertIn('test "$run_attempt" = "$expected_attempt"', self.workflow)
        self.assertEqual(2, self.workflow.count("check-runs --evidence-root"))

    def test_shell_steps_have_valid_syntax(self):
        for block in re.findall(r"        run: \|\n((?:          .*\n|\n)+)", self.workflow):
            script = "\n".join(line[10:] for line in block.splitlines())
            result = subprocess.run(["bash", "-n"], input=script, text=True, capture_output=True)
            self.assertEqual(0, result.returncode, result.stderr)


if __name__ == "__main__":
    unittest.main()
