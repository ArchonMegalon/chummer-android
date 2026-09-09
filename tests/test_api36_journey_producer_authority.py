import contextlib
import copy
import hashlib
import io
import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest import mock
import zipfile

import test_api36_e2e_artifact_authority as aggregate_tests
from api36_journey_producer_fixture import HEAD_SHA, ProducerFixture
from api36_journey_producer_authority import JourneyProducerAuthority, REPOSITORY


JOURNEY = "career-active-skill-advance"
RUN_ID = int(aggregate_tests.RUN_ID)


class JourneyProducerAuthorityTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.receipts = aggregate_tests.Api36ArtifactAuthorityTests()
        self.directory = self.receipts.materialize_journey(self.root, JOURNEY)
        self.execution = {"runId": RUN_ID, "runAttempt": 1, "matrixJourney": JOURNEY}

    def fixture(self):
        return ProducerFixture(
            self.root, run_id=RUN_ID, aggregate_attempt=2, producers={JOURNEY: 1},
        )

    def require(self, fixture, *, execution=None):
        with contextlib.redirect_stdout(io.StringIO()):
            authority = fixture.authority()
            result = authority.require(
                journey=JOURNEY, execution=self.execution if execution is None else execution,
                directory=self.directory,
            )
            authority.recheck()
            return result

    def test_earlier_success_requires_the_exact_downloaded_archive(self):
        bindings = self.require(self.fixture())
        self.assertEqual(
            {path.name: hashlib.sha256(path.read_bytes()).hexdigest()
             for path in self.directory.iterdir() if path.is_file()}, bindings,
        )

    def test_zero_future_noninteger_or_foreign_execution_is_rejected(self):
        mutations = [("runAttempt", value) for value in (0, -1, 3, 1.0, "1", True, None)]
        mutations += [("runId", value) for value in (0, RUN_ID + 1, str(RUN_ID), True)]
        mutations += [("matrixJourney", "career-weapon-fire")]
        for field, value in mutations:
            with self.subTest(field=field, value=value), self.assertRaises(ValueError):
                self.require(self.fixture(), execution={**self.execution, field: value})

    def test_actual_current_attempt_job_is_accepted(self):
        self.receipts.materialize_journey(self.root, JOURNEY, producer_attempt=2)
        fixture = ProducerFixture(
            self.root, run_id=RUN_ID, aggregate_attempt=2, producers={JOURNEY: 2},
        )
        self.assertIn("emulator-live-observation.json", self.require(
            fixture, execution={**self.execution, "runAttempt": 2},
        ))

    def test_copied_successful_job_is_not_a_new_attempt_producer(self):
        fixture = self.fixture()
        copied = copy.deepcopy(fixture.jobs[1]["jobs"][0])
        copied["run_attempt"] = 2
        fixture.jobs[2]["jobs"] = [copied]
        fixture.jobs[2]["total_count"] = 1
        with self.assertRaisesRegex(ValueError, "outside the upload window"):
            self.require(fixture, execution={**self.execution, "runAttempt": 2})

    def test_initial_run_context_is_not_inferred_from_the_receipt(self):
        for field, value in (
            ("repository", "other/android"), ("head_sha", "c" * 40),
            ("run_id", RUN_ID + 1), ("aggregate_attempt", 0),
            ("aggregate_attempt", 2.0), ("aggregate_attempt", True),
        ):
            fixture = self.fixture()
            # Constructor inputs are CI context, separate from API/ZIP data.
            authority = {"repository": REPOSITORY, "run_id": RUN_ID,
                         "aggregate_attempt": 2, "head_sha": HEAD_SHA}
            authority[field] = value
            if field == "run_id":
                fixture.responses[f"{fixture.api}/actions/runs/{value}/attempts/2"] = fixture.runs[2]
            with self.subTest(field=field, value=value), self.assertRaises(ValueError):
                JourneyProducerAuthority(fixture, **authority)

    def test_ci_token_bridge_reuses_client_and_removes_private_temporary_input(self):
        import sign_api36_two_green_release_approval as transport

        captured = []
        fixture = self.fixture()

        def client(token_path):
            captured.append(token_path)
            self.assertEqual(0o600, token_path.stat().st_mode & 0o777)
            self.assertEqual("test-provenance-token-only", token_path.read_text())
            return fixture

        with mock.patch.dict(os.environ, {"GH_TOKEN": "test-provenance-token-only"}), mock.patch.object(
            transport, "GitHubApiClient", side_effect=client,
        ) as constructor:
            authority = JourneyProducerAuthority.from_environment(
                repository=REPOSITORY, run_id=RUN_ID, aggregate_attempt=2, head_sha=HEAD_SHA,
            )
        constructor.assert_called_once()
        self.assertEqual(2, authority.aggregate_attempt)
        self.assertFalse(captured[0].exists())
        with mock.patch.dict(os.environ, {"GH_TOKEN": ""}), self.assertRaisesRegex(
            ValueError, "authenticated GitHub token is missing",
        ):
            JourneyProducerAuthority.from_environment(
                repository=REPOSITORY, run_id=RUN_ID, aggregate_attempt=2, head_sha=HEAD_SHA,
            )

    def test_forged_run_job_artifact_and_upload_metadata_is_rejected(self):
        mutations = [
            ("run", "id", RUN_ID + 1), ("run", "run_attempt", 0),
            ("run", "run_attempt", True), ("run", "head_sha", "c" * 40),
            ("run", "path", ".github/workflows/foreign.yml"),
            ("run", "name", "foreign"), ("run", "workflow_id", 45),
            ("run", "url", "https://example.invalid/run"),
            ("run", "run_started_at", "2026-09-08T01:02:00Z"),
            ("repository", "id", 34), ("repository", "full_name", "other/android"),
            ("head_repository", "id", 34), ("head_repository", "full_name", "other/android"),
            ("job", "id", False), ("job", "name", "phone API 36 SR5 wizard persistence (career-weapon-fire)"),
            ("job", "run_id", RUN_ID + 1), ("job", "run_attempt", 2),
            ("job", "run_attempt", "1"), ("job", "head_sha", "c" * 40),
            ("job", "status", "in_progress"), ("job", "conclusion", "failure"),
            ("job", "workflow_name", "foreign"), ("job", "url", "foreign"),
            ("job", "html_url", "foreign"), ("job", "check_run_url", "foreign"),
            ("artifact", "id", 0), ("artifact", "name", "foreign"),
            ("artifact", "expired", True), ("artifact", "digest", "sha256:" + "f" * 64),
            ("artifact", "size_in_bytes", 1), ("artifact", "size_in_bytes", True),
            ("artifact", "created_at", "2026-09-08T02:00:00Z"),
            ("artifact", "updated_at", "2026-09-08T00:00:00Z"),
            ("artifact_run", "id", RUN_ID + 1), ("artifact_run", "head_sha", "c" * 40),
            ("artifact_run", "repository_id", 34), ("artifact_run", "head_repository_id", 34),
            ("artifact_run", "head_branch", "other-branch"),
            ("upload", "name", "foreign"), ("upload", "conclusion", "failure"),
            ("upload", "status", "in_progress"),
            ("upload", "completed_at", "2026-09-08T00:00:00Z"),
        ]
        for target, field, value in mutations:
            with self.subTest(target=target, field=field, value=value):
                fixture = self.fixture()
                artifact = fixture.artifacts["artifacts"][0]
                job = fixture.jobs[1]["jobs"][0]
                objects = {"run": fixture.runs[1], "job": job, "artifact": artifact,
                           "repository": fixture.runs[1]["repository"],
                           "head_repository": fixture.runs[1]["head_repository"],
                           "artifact_run": artifact["workflow_run"], "upload": job["steps"][0]}
                objects[target][field] = value
                with self.assertRaises(ValueError):
                    self.require(fixture)

    def test_incomplete_or_ambiguous_api_responses_are_rejected(self):
        for target in ("artifacts", "jobs"):
            for change in ("truncated", "duplicate"):
                with self.subTest(target=target, change=change):
                    fixture = self.fixture()
                    response = fixture.artifacts if target == "artifacts" else fixture.jobs[1]
                    response["total_count"] += 1
                    if change == "duplicate":
                        response[target].append(copy.deepcopy(response[target][0]))
                    with self.assertRaises(ValueError):
                        self.require(fixture)

    def test_archive_and_local_member_tampering_are_rejected(self):
        fixture = self.fixture()
        endpoint = f"{fixture.api}/actions/artifacts/1001/zip"
        fixture.responses[endpoint] += b"forged"
        with self.assertRaisesRegex(ValueError, "archive size/digest differs"):
            self.require(fixture)
        fixture = self.fixture()
        (self.directory / "receipt.json").write_bytes(b"forged")
        with self.assertRaisesRegex(ValueError, "downloaded artifact member differs"):
            self.require(fixture)

    def test_unsafe_duplicate_or_extra_archive_members_are_rejected(self):
        for member in ("../receipt.json", "/receipt.json", "nested/../receipt.json", "receipt.json"):
            with self.subTest(member=member):
                archive = io.BytesIO()
                with zipfile.ZipFile(archive, "w") as stream:
                    stream.writestr("receipt.json", (self.directory / "receipt.json").read_bytes())
                    with mock.patch("warnings.warn"):
                        stream.writestr(member, b"forged")
                with self.assertRaisesRegex(ValueError, "archive member is unsafe"):
                    JourneyProducerAuthority.require_archive_files(archive.getvalue(), self.directory)
        fixture = self.fixture()
        (self.directory / "unbound.txt").write_text("foreign")
        with self.assertRaisesRegex(ValueError, "downloaded artifact members differ"):
            self.require(fixture)

    def test_archive_links_and_directories_are_rejected(self):
        for name, mode in (("link", 0o120777), ("directory/", 0o40755)):
            with self.subTest(name=name):
                archive = io.BytesIO()
                member = zipfile.ZipInfo(name)
                member.create_system = 3
                member.external_attr = mode << 16
                with zipfile.ZipFile(archive, "w") as stream:
                    stream.writestr(member, b"receipt.json")
                with self.assertRaisesRegex(ValueError, "archive member is unsafe"):
                    JourneyProducerAuthority.require_archive_files(archive.getvalue(), self.directory)

    def test_stable_name_overwrite_before_seal_is_rejected(self):
        fixture = self.fixture()
        with contextlib.redirect_stdout(io.StringIO()):
            authority = fixture.authority()
            authority.require(journey=JOURNEY, execution=self.execution, directory=self.directory)
        fixture.artifacts["artifacts"][0]["id"] += 1
        with self.assertRaisesRegex(ValueError, "stable artifact changed before seal"):
            authority.recheck()

    def test_artifact_detail_drift_after_archive_capture_is_rejected(self):
        fixture = self.fixture()
        detail = f"{fixture.api}/actions/artifacts/1001"
        fixture.responses[detail] = {**fixture.responses[detail], "id": 1002}
        with self.assertRaisesRegex(ValueError, "artifact metadata changed"):
            self.require(fixture)

    def test_authenticated_archive_cannot_replace_own_sidecar_equality(self):
        self.receipts.materialize_all(self.root)
        path = self.directory / "emulator-live-observation.json"
        sidecar = json.loads(path.read_text())
        sidecar["execution"]["runAttempt"] = 2
        sidecar["authoritySha256"] = aggregate_tests.ENVIRONMENT.canonical_sha256(
            {**sidecar, "authoritySha256": None}
        )
        path.write_text(json.dumps(sidecar) + "\n")
        fixture = ProducerFixture(
            self.root, run_id=RUN_ID, aggregate_attempt=2,
            producers={journey: 2 if journey == JOURNEY else 1 for journey in aggregate_tests.JOURNEYS},
        )
        with contextlib.redirect_stdout(io.StringIO()), self.assertRaisesRegex(
            ValueError, "emulator live observation differs",
        ):
            self.receipts.validate(self.root, aggregate_attempt="2", producer_authority=fixture.authority())

    def test_authority_for_another_aggregate_attempt_is_rejected(self):
        self.receipts.materialize_all(self.root)
        with self.assertRaisesRegex(ValueError, "producer aggregate execution differs"):
            self.receipts.validate(self.root, aggregate_attempt="1", producer_authority=self.fixture().authority())


if __name__ == "__main__":
    unittest.main()
