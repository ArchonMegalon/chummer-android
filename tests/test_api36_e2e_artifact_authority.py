import hashlib
import importlib.util
import json
import shutil
import tempfile
import unittest
from pathlib import Path


REPO = Path(__file__).resolve().parents[1]


def load_script(name: str, relative: str):
    specification = importlib.util.spec_from_file_location(name, REPO / relative)
    if specification is None or specification.loader is None:
        raise RuntimeError(f"Unable to load {relative}")
    module = importlib.util.module_from_spec(specification)
    specification.loader.exec_module(module)
    return module


FINALIZE = load_script(
    "api36_finalize_journey_receipt",
    "scripts/finalize-api36-e2e-journey-receipt.py",
)
AGGREGATE = load_script(
    "api36_verify_evidence_aggregate",
    "scripts/verify-api36-editing-e2e-aggregate.py",
)

RUN_ID = "424242"
ARTIFACT_ID = "987654"
ARTIFACT_DIGEST = "a" * 64
APK_SHA256 = "b" * 64
JOURNEYS = {
    "full-editing": "full",
    "creation-prerequisite": "creation-prerequisite",
    "career-active-skill-advance": "career-active-skill-advance",
    "career-weapon-fire": "career-weapon-fire",
}


class Api36ArtifactAuthorityTests(unittest.TestCase):
    def authority(self, attempt: str = "1", **overrides: str) -> dict[str, str]:
        values = {
            "run_id": RUN_ID,
            "artifact_id": ARTIFACT_ID,
            "artifact_digest": ARTIFACT_DIGEST,
            "artifact_name": (
                f"chummer-android-api36-x64-debug-{RUN_ID}-{attempt}"
            ),
            "artifact_attempt": attempt,
            "apk_sha256": APK_SHA256,
        }
        values.update(overrides)
        return values

    def raw_receipt(self, journey: str) -> dict[str, object]:
        driver_journey = JOURNEYS[journey]
        receipt: dict[str, object] = {
            "schema": (
                "chummer.android.creation-prerequisite-e2e/v1"
                if journey == "creation-prerequisite"
                else "chummer.android.editing-e2e/v1"
            ),
            "status": "pass",
            "profile": "phone",
            "apkSha256": APK_SHA256,
        }
        if journey == "creation-prerequisite":
            receipt["executionStatus"] = "pass"
            receipt["timing"] = {
                "schema": AGGREGATE.CREATION_PROGRESS_SCHEMA,
                "status": "timing-complete",
                "clock": "time.monotonic",
                "configuredTotalTargetMs": AGGREGATE.CREATION_TOTAL_TARGET_MS,
                "totalElapsedMs": 8_000,
                "withinConfiguredTotalTarget": True,
                "phaseBudgetsMs": dict(AGGREGATE.CREATION_PHASE_BUDGETS_MS),
                "phases": [
                    {
                        "ordinal": ordinal,
                        "phaseId": phase_id,
                        "status": "pass",
                        "elapsedMs": 1_000,
                        "budgetMs": budget_ms,
                        "withinBudget": True,
                    }
                    for ordinal, (phase_id, budget_ms) in enumerate(
                        AGGREGATE.CREATION_PHASE_BUDGETS_MS.items(),
                        start=1,
                    )
                ],
                "scans": [],
            }
        else:
            receipt["journey"] = driver_journey
        return receipt

    def materialize_journey(
        self,
        root: Path,
        journey: str,
        *,
        attempt: str = "1",
        authority_overrides: dict[str, str] | None = None,
    ) -> Path:
        authority = self.authority(attempt, **(authority_overrides or {}))
        driver_journey = JOURNEYS[journey]
        receipt = FINALIZE.bind_receipt(
            self.raw_receipt(journey),
            matrix_journey=journey,
            driver_journey=driver_journey,
            **authority,
        )
        directory = root / AGGREGATE.expected_artifact_directory(journey, RUN_ID)
        if directory.exists():
            shutil.rmtree(directory)
        directory.mkdir(parents=True)
        receipt_path = directory / "receipt.json"
        receipt_path.write_text(json.dumps(receipt, indent=2) + "\n", encoding="utf-8")
        digest = hashlib.sha256(receipt_path.read_bytes()).hexdigest()
        (directory / "receipt.json.sha256").write_text(
            f"{digest}  receipt.json\n",
            encoding="utf-8",
        )
        (directory / "execution-started.txt").write_text(
            "\n".join(
                (
                    "profile=phone",
                    f"matrix_journey={journey}",
                    f"driver_journey={driver_journey}",
                    f"artifact_id={authority['artifact_id']}",
                    f"artifact_digest={authority['artifact_digest']}",
                    f"artifact_name={authority['artifact_name']}",
                    f"artifact_attempt={authority['artifact_attempt']}",
                    f"apk_sha256={authority['apk_sha256']}",
                )
            )
            + "\n",
            encoding="utf-8",
        )
        return directory

    def materialize_all(self, root: Path, *, attempt: str = "1") -> None:
        for journey in JOURNEYS:
            self.materialize_journey(root, journey, attempt=attempt)

    def validate(self, root: Path, *, attempt: str = "1", **overrides: str):
        return AGGREGATE.validate_aggregate(
            root,
            build_result="success",
            matrix_result="success",
            **self.authority(attempt, **overrides),
        )

    def reseal(self, directory: Path) -> None:
        receipt = directory / "receipt.json"
        digest = hashlib.sha256(receipt.read_bytes()).hexdigest()
        (directory / "receipt.json.sha256").write_text(
            f"{digest}  receipt.json\n",
            encoding="utf-8",
        )

    def test_explicit_full_editing_mapping_rejects_default_drift(self) -> None:
        receipt = self.raw_receipt("full-editing")
        receipt["journey"] = "contact-pet"
        with self.assertRaisesRegex(ValueError, "driver route differs"):
            FINALIZE.bind_receipt(
                receipt,
                matrix_journey="full-editing",
                driver_journey="full",
                **self.authority(),
            )
        with self.assertRaisesRegex(ValueError, "matrix/driver journey mismatch"):
            FINALIZE.bind_receipt(
                self.raw_receipt("full-editing"),
                matrix_journey="full-editing",
                driver_journey="condition-monitor",
                **self.authority(),
            )

    def test_finalizer_rejects_mismatched_apk_sha_and_attempt_name(self) -> None:
        receipt = self.raw_receipt("full-editing")
        receipt["apkSha256"] = "c" * 64
        with self.assertRaisesRegex(ValueError, "APK SHA-256 differs"):
            FINALIZE.bind_receipt(
                receipt,
                matrix_journey="full-editing",
                driver_journey="full",
                **self.authority(),
            )
        with self.assertRaisesRegex(ValueError, "not bound to run and attempt"):
            FINALIZE.bind_receipt(
                self.raw_receipt("full-editing"),
                matrix_journey="full-editing",
                driver_journey="full",
                **self.authority(artifact_name="stale-snapshot"),
            )

    def test_rerun_failed_keeps_one_stable_receipt_per_journey(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            self.materialize_all(root, attempt="1")
            # A rerun-failed replaces only its stable journey artifact and retains
            # the original build authority used by the successful prerequisite jobs.
            self.materialize_journey(root, "full-editing", attempt="1")
            aggregate = self.validate(root, attempt="1")
            self.assertEqual(4, aggregate["journeyCount"])
            self.assertEqual(ARTIFACT_ID, aggregate["artifactAuthority"]["artifactId"])

    def test_rerun_all_replaces_all_stable_evidence_with_new_build_authority(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            self.materialize_all(root, attempt="1")
            self.materialize_all(root, attempt="2")
            aggregate = self.validate(root, attempt="2")
            self.assertEqual(2, aggregate["artifactAuthority"]["artifactAttempt"])

    def test_multiple_attempt_or_differing_snapshot_authority_fails_closed(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            self.materialize_all(root, attempt="2")
            self.materialize_journey(root, "creation-prerequisite", attempt="1")
            with self.assertRaisesRegex(ValueError, "authority differs"):
                self.validate(root, attempt="2")

    def test_mismatched_artifact_id_and_apk_sha_fail_closed(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            self.materialize_all(root)
            directory = root / AGGREGATE.expected_artifact_directory(
                "career-active-skill-advance",
                RUN_ID,
            )
            receipt_path = directory / "receipt.json"
            receipt = json.loads(receipt_path.read_text(encoding="utf-8"))
            receipt["artifactAuthority"]["artifactId"] = "111111"
            receipt_path.write_text(json.dumps(receipt) + "\n", encoding="utf-8")
            self.reseal(directory)
            with self.assertRaisesRegex(ValueError, "authority differs"):
                self.validate(root)

            self.materialize_journey(root, "career-active-skill-advance")
            with self.assertRaisesRegex(ValueError, "execution-started authority differs"):
                self.validate(root, apk_sha256="c" * 64)

    def test_missing_or_expired_artifact_receipt_fails_closed(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            self.materialize_all(root)
            # An expired exact-ID evidence artifact is indistinguishable from
            # a missing download here; cardinality must fail rather than reuse
            # or select another visible run-attempt snapshot.
            shutil.rmtree(
                root
                / AGGREGATE.expected_artifact_directory(
                    "creation-prerequisite",
                    RUN_ID,
                )
            )
            with self.assertRaisesRegex(ValueError, "cardinality/name mismatch"):
                self.validate(root)

    def test_duplicate_journey_receipt_or_artifact_fails_closed(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            self.materialize_all(root)
            full = root / AGGREGATE.expected_artifact_directory("full-editing", RUN_ID)
            nested = full / "duplicate"
            nested.mkdir()
            shutil.copy2(full / "receipt.json", nested / "receipt.json")
            with self.assertRaisesRegex(ValueError, "exactly 4"):
                self.validate(root)

            shutil.rmtree(nested)
            (root / f"{full.name}-old-attempt").mkdir()
            with self.assertRaisesRegex(ValueError, "cardinality/name mismatch"):
                self.validate(root)

    def test_duplicate_json_keys_and_failed_matrix_fail_closed(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            self.materialize_all(root)
            full = root / AGGREGATE.expected_artifact_directory("full-editing", RUN_ID)
            (full / "receipt.json").write_text(
                '{"schema":"x","schema":"y"}\n',
                encoding="utf-8",
            )
            self.reseal(full)
            with self.assertRaisesRegex(ValueError, "duplicate JSON key"):
                self.validate(root)

            self.materialize_journey(root, "full-editing")
            with self.assertRaisesRegex(ValueError, "matrix did not succeed"):
                AGGREGATE.validate_aggregate(
                    root,
                    build_result="success",
                    matrix_result="failure",
                    **self.authority(),
                )

    def test_creation_timing_outside_explicit_budgets_fails_closed(self) -> None:
        cases = (
            ("missingTiming", "timing evidence is missing"),
            ("withinBudget", "phase timing is outside budget"),
            ("elapsedMs", "phase timing is outside budget"),
            ("withinConfiguredTotalTarget", "total timing target was exceeded"),
            ("totalElapsedMs", "total timing target was exceeded"),
        )
        for field, expected_error in cases:
            with self.subTest(field=field), tempfile.TemporaryDirectory() as temporary:
                root = Path(temporary)
                self.materialize_all(root)
                directory = root / AGGREGATE.expected_artifact_directory(
                    "creation-prerequisite",
                    RUN_ID,
                )
                receipt_path = directory / "receipt.json"
                receipt = json.loads(receipt_path.read_text(encoding="utf-8"))
                if field == "missingTiming":
                    del receipt["timing"]
                elif field == "withinConfiguredTotalTarget":
                    receipt["timing"][field] = False
                elif field == "totalElapsedMs":
                    receipt["timing"][field] = AGGREGATE.CREATION_TOTAL_TARGET_MS + 1
                elif field == "withinBudget":
                    receipt["timing"]["phases"][1][field] = False
                else:
                    receipt["timing"]["phases"][1][field] = (
                        AGGREGATE.CREATION_PHASE_BUDGETS_MS["initial-authority"] + 1
                    )
                receipt_path.write_text(json.dumps(receipt) + "\n", encoding="utf-8")
                self.reseal(directory)

                with self.assertRaisesRegex(ValueError, expected_error):
                    self.validate(root)


if __name__ == "__main__":
    unittest.main()
