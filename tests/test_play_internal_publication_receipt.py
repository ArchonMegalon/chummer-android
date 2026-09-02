from __future__ import annotations

import copy
import hashlib
import importlib.util
import json
import sys
import tempfile
import unittest
from pathlib import Path


REPO = Path(__file__).resolve().parents[1]
SCRIPT = REPO / "scripts/verify_play_internal_publication_receipt.py"
RECEIPT = REPO / "play/evidence/preview10-internal-publication.json"
SCHEMA = REPO / "play/release-receipt.schema.json"


def load_module():
    spec = importlib.util.spec_from_file_location("verify_play_internal_publication_receipt", SCRIPT)
    assert spec is not None and spec.loader is not None
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


def write_json(path: Path, payload: dict) -> Path:
    path.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")
    return path


class PlayInternalPublicationReceiptTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.module = load_module()
        cls.payload = json.loads(RECEIPT.read_text(encoding="utf-8"))

    def verify_mutation(self, mutation) -> dict:
        with tempfile.TemporaryDirectory() as temporary:
            payload = copy.deepcopy(self.payload)
            mutation(payload)
            candidate = write_json(Path(temporary) / "receipt.json", payload)
            return self.module.verify(candidate)

    def test_tracked_preview10_receipt_passes_without_byte_retrieval_claim(self) -> None:
        result = self.module.verify(RECEIPT)
        self.assertEqual("pass", result["status"], result["failures"])
        self.assertTrue(result["browserReadbackVerified"])
        self.assertFalse(result["byteEvidenceSupplied"])
        self.assertFalse(result["byteEvidenceVerified"])
        self.assertFalse(result["authorization"]["productionAuthorized"])
        self.assertIn("tester_installation", result["doesNotClaim"])
        self.assertIn("play_artifact_byte_retrievability", result["doesNotClaim"])

    def test_schema_is_closed_and_covers_release_truth_sections(self) -> None:
        schema = json.loads(SCHEMA.read_text(encoding="utf-8"))
        self.assertFalse(schema["additionalProperties"])
        self.assertEqual(self.module.CONTRACT, schema["properties"]["contractName"]["const"])
        for section in (
            "application",
            "track",
            "release",
            "artifact",
            "browserReadback",
            "artifactLinkage",
            "authorization",
        ):
            self.assertFalse(schema["properties"][section]["additionalProperties"], section)
        artifact = schema["properties"]["artifact"]["required"]
        self.assertIn("aabSha256", artifact)
        self.assertIn("sourceGraphSha256", artifact)
        self.assertIn("androidSourceCommit", artifact)

    def test_unknown_and_duplicate_fields_fail_closed(self) -> None:
        result = self.verify_mutation(lambda payload: payload.update({"unexpected": True}))
        self.assertEqual("fail", result["status"])
        self.assertIn("publication receipt fields are not exact", result["failures"])

        raw = RECEIPT.read_text(encoding="utf-8")
        duplicate = raw.replace(
            '"contractName": "chummer.android.play-internal-publication-receipt/v2",',
            '"contractName": "chummer.android.play-internal-publication-receipt/v2",\n'
            '  "contractName": "chummer.android.play-internal-publication-receipt/v2",',
            1,
        )
        with tempfile.TemporaryDirectory() as temporary:
            candidate = Path(temporary) / "duplicate.json"
            candidate.write_text(duplicate, encoding="utf-8")
            result = self.module.verify(candidate)
        self.assertEqual("fail", result["status"])
        self.assertIn("duplicate key", result["failures"][0])

    def test_artifact_and_source_graph_tampering_fail_closed(self) -> None:
        cases = (
            ("aabSha256", "0" * 64, "AAB digest is not the exact Preview.10 digest"),
            ("sourceGraphSha256", "1" * 64, "source-graph digest is not exact"),
            ("androidSourceCommit", "2" * 40, "Android source commit is not exact"),
        )
        for field, value, failure in cases:
            with self.subTest(field=field):
                result = self.verify_mutation(
                    lambda payload, f=field, v=value: payload["artifact"].update({f: v})
                )
                self.assertEqual("fail", result["status"])
                self.assertIn(failure, result["failures"])

    def test_play_identity_version_status_time_and_join_tampering_fail_closed(self) -> None:
        mutations = (
            (
                lambda payload: payload["application"].update({"playApplicationId": "1"}),
                "Play application ID is incorrect",
            ),
            (
                lambda payload: payload["track"].update({"trackId": "1"}),
                "Play track ID is incorrect",
            ),
            (
                lambda payload: payload["track"].update({"joinUrl": "https://example.com/invite"}),
                "join URL is not the exact Internal testing URL",
            ),
            (
                lambda payload: payload["release"].update({"versionCode": 11}),
                "version code is incorrect",
            ),
            (
                lambda payload: payload["release"].update({"status": "Draft"}),
                "Play release status is incorrect",
            ),
            (
                lambda payload: payload["release"]["releasedAt"].update(
                    {"consoleDisplay": "3 Sept 02:04"}
                ),
                "release console time is incorrect",
            ),
            (
                lambda payload: payload["release"]["releasedAt"].update(
                    {"normalizedUtc": "2026-09-02T23:04:00Z"}
                ),
                "release time invents a normalized UTC value",
            ),
        )
        for mutation, failure in mutations:
            with self.subTest(failure=failure):
                result = self.verify_mutation(mutation)
                self.assertEqual("fail", result["status"])
                self.assertIn(failure, result["failures"])

    def test_authorization_linkage_and_nonclaim_widening_fail_closed(self) -> None:
        mutations = (
            (
                lambda payload: payload["authorization"].update({"productionAuthorized": True}),
                "receipt authorizes production",
            ),
            (
                lambda payload: payload["artifactLinkage"].update(
                    {"artifactDownloadedBackFromPlay": True}
                ),
                "receipt claims AAB retrieval from Play",
            ),
            (
                lambda payload: payload["browserReadback"].update(
                    {"credentialOrSessionDataRecorded": True}
                ),
                "receipt contains credential/session authority",
            ),
            (
                lambda payload: payload["doesNotClaim"].remove("tester_installation"),
                "nonclaim set is not exact",
            ),
        )
        for mutation, failure in mutations:
            with self.subTest(failure=failure):
                result = self.verify_mutation(mutation)
                self.assertEqual("fail", result["status"])
                self.assertIn(failure, result["failures"])

    def test_list_type_confusion_and_exact_observation_drift_fail_closed(self) -> None:
        mutations = (
            (
                lambda payload: payload["browserReadback"].update({"fieldsObserved": [{}]}),
                "browser readback fields are not unique",
            ),
            (
                lambda payload: payload.update({"doesNotClaim": [{}]}),
                "nonclaims are not unique",
            ),
            (
                lambda payload: payload["release"].update({"supportedAndroidDevices": 1}),
                "supported-device count is not exact",
            ),
            (
                lambda payload: payload.update({"recordedAtUtc": "2026-09-02T23:37:57Z"}),
                "recordedAtUtc is not exact",
            ),
            (
                lambda payload: payload["artifact"].update(
                    {"sourceGraphGeneratedAtUtc": "2026-09-02T22:18:57Z"}
                ),
                "source-graph generation time is not exact",
            ),
        )
        for mutation, failure in mutations:
            with self.subTest(failure=failure):
                result = self.verify_mutation(mutation)
                self.assertEqual("fail", result["status"])
                self.assertIn(failure, result["failures"])

    def test_byte_verification_is_optional_but_requires_both_inputs(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            aab = Path(temporary) / "candidate.aab"
            aab.write_bytes(b"bounded-aab")
            result = self.module.verify(RECEIPT, aab_path=aab)
        self.assertEqual("fail", result["status"])
        self.assertIn(
            "AAB and source graph must be supplied together for byte verification",
            result["failures"],
        )

    def test_supplied_bytes_are_rehashed_and_bound_to_android_source(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            aab = root / "candidate.aab"
            aab.write_bytes(b"bounded-aab")
            graph = root / "source-graph.json"
            write_json(
                graph,
                {
                    "contractName": self.module.SOURCE_GRAPH_CONTRACT,
                    "repositories": [
                        {
                            "name": "chummer-android",
                            "commit": self.module.ANDROID_SOURCE_COMMIT,
                            "tree": self.module.ANDROID_SOURCE_TREE,
                        }
                    ],
                },
            )
            payload = copy.deepcopy(self.payload)
            payload["artifact"]["aabSha256"] = hashlib.sha256(aab.read_bytes()).hexdigest()
            payload["artifact"]["aabSizeBytes"] = aab.stat().st_size
            payload["artifact"]["sourceGraphSha256"] = hashlib.sha256(graph.read_bytes()).hexdigest()
            receipt = write_json(root / "receipt.json", payload)
            result = self.module.verify(
                receipt,
                aab_path=aab,
                source_graph_path=graph,
                expected_aab_sha256=payload["artifact"]["aabSha256"],
                expected_aab_size=payload["artifact"]["aabSizeBytes"],
                expected_source_graph_sha256=payload["artifact"]["sourceGraphSha256"],
            )
        self.assertEqual("pass", result["status"], result["failures"])
        self.assertTrue(result["byteEvidenceSupplied"])
        self.assertTrue(result["byteEvidenceVerified"])


if __name__ == "__main__":
    unittest.main()
