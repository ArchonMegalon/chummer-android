import copy
from contextlib import redirect_stderr
import hashlib
import io
import json
from pathlib import Path
import tempfile
import unittest
import xml.etree.ElementTree as ET

import run_api36_sr5_after_run_settlement_e2e as driver


ROOT = Path(__file__).resolve().parents[1]
DRIVER = ROOT / "tests/run_api36_sr5_after_run_settlement_e2e.py"


def checkpoint(fixture: dict[str, object], *, applied: bool) -> dict[str, object]:
    identity = {
        "ProposalId": fixture["identity"]["proposalId"],
        "RunId": fixture["identity"]["runId"],
        "CharacterId": fixture["identity"]["characterId"],
    }
    contacts = [
        {
            "ContactId": item["contactId"], "Name": item["name"],
            "Role": item["role"], "Location": item["location"],
            "Connection": item["connection"], "Loyalty": item["loyalty"],
            "Kind": 0 if item["kind"] == "Run reward" else 1,
        }
        for item in fixture["contacts"]
    ]
    quote = {
        "Identity": identity,
        "HeatBefore": 1, "HeatDelta": 2, "HeatAfter": 3,
        "StreetCredBefore": 10, "StreetCredDelta": 2, "StreetCredAfter": 12,
        "NotorietyBefore": 4, "NotorietyDelta": 1, "NotorietyAfter": 5,
        "PublicAwarenessBefore": 6, "PublicAwarenessAfter": 7,
        "KarmaBefore": 30, "KarmaAfter": 19, "ContactKarmaCost": 11,
        "Contacts": contacts,
        "GmReviewDigest": "b" * 64, "OwnerReviewDigest": "c" * 64,
        "SourceDigest": "1" * 64, "CustomDataDigest": "2" * 64,
        "GmPolicyDigest": "3" * 64, "RuntimeDigest": "4" * 64,
        "LogicalDigest": "5" * 64,
    }
    transaction = "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"
    draft = {
        "OwnerId": "ffffffff-ffff-ffff-ffff-ffffffffffff",
        "Candidate": {
            "RewardContext": {
                "Identity": identity,
                "RunTitle": fixture["reward"]["runTitle"],
                "CompletedAt": "2026-08-26T20:00:00+00:00",
                "KarmaAward": 8, "NuyenAward": 12500,
                "RewardReceiptDigest": "a" * 64, "ContextDigest": "6" * 64,
            },
            "Binding": {
                "WorkspaceId": {"Value": "workspace-test"},
                "WorkspaceRevision": 41, "Identity": identity, "Quote": quote,
            },
        },
        "Plan": {
            "TransactionId": transaction, "PlanDigest": "d" * 64,
            "GmReviewDigest": "b" * 64, "OwnerReviewDigest": "c" * 64,
        },
        "Acknowledgements": {
            "RunContextReviewed": True, "RewardsReviewed": True,
            "ConsequencesReviewed": True, "ContactsReviewed": True,
            "GmApprovalReviewed": True, "OwnerApprovalReviewed": True,
        },
    }
    receipt = None
    if applied:
        receipt = {
            "TransactionId": transaction,
            "HeatBefore": 1, "HeatAfter": 3,
            "StreetCredBefore": 10, "StreetCredAfter": 12,
            "NotorietyBefore": 4, "NotorietyAfter": 5,
            "PublicAwarenessBefore": 6, "PublicAwarenessAfter": 7,
            "KarmaBefore": 30, "KarmaAfter": 19,
            "AddedContacts": contacts, "ReceiptDigest": "f" * 64,
        }
    return {
        "SchemaVersion": 1, "Version": 3 if applied else 1,
        "RouteId": driver.REVIEW_ROUTE, "Phase": 2 if applied else 0,
        "Draft": draft, "Receipt": receipt, "IdempotencyKey": "e" * 64,
    }


class Api36Sr5AfterRunSettlementContractTests(unittest.TestCase):
    def test_governed_fixture_materializes_exact_runner_bytes(self) -> None:
        fixture = driver.load_fixture()
        payload = driver.render_runner_xml(fixture)
        self.assertEqual(
            fixture["runner"]["expectedSha256"], hashlib.sha256(payload).hexdigest()
        )
        root = ET.fromstring(payload)
        self.assertEqual("True", root.findtext("created"))
        self.assertEqual("SR5", root.findtext("gameedition"))
        self.assertEqual("10", root.findtext("streetcred"))
        self.assertEqual("4", root.findtext("notoriety"))
        self.assertEqual("6", root.findtext("publicawareness"))
        self.assertEqual("30", root.findtext("karma"))
        self.assertEqual("1", root.findtext("heat"))

    def test_fixture_tampering_and_duplicate_keys_fail_closed(self) -> None:
        fixture = driver.load_fixture()
        tampered = copy.deepcopy(fixture)
        tampered["identity"]["runId"] = tampered["identity"]["proposalId"]
        with self.assertRaises(RuntimeError):
            driver.validate_fixture(tampered)
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "fixture.json"
            path.write_text('{"schema":"x","schema":"y"}', encoding="utf-8")
            with self.assertRaises(RuntimeError):
                driver.load_fixture(path)

    def test_review_and_applied_checkpoint_bind_exact_ids_deltas_and_digests(self) -> None:
        fixture = driver.load_fixture()
        reviewed = checkpoint(fixture, applied=False)
        authority = driver.validate_checkpoint(
            reviewed, fixture, workspace_id="workspace-test",
            workspace_revision=41, version=1, phase=0,
        )
        self.assertEqual("e" * 8 + "-" + "e" * 4 + "-" + "e" * 4 + "-" + "e" * 4 + "-" + "e" * 12, authority["transactionId"])
        self.assertNotEqual(authority["gmReviewDigest"], authority["ownerReviewDigest"])
        applied = checkpoint(fixture, applied=True)
        self.assertEqual(
            authority,
            driver.validate_checkpoint(
                applied, fixture, workspace_id="workspace-test",
                workspace_revision=41, version=3, phase=2,
            ),
        )
        driver.require_same_draft(reviewed, applied)
        tampered = copy.deepcopy(applied)
        tampered["Receipt"]["HeatAfter"] = 4
        with self.assertRaises(RuntimeError):
            driver.validate_checkpoint(
                tampered, fixture, workspace_id="workspace-test",
                workspace_revision=41, version=3, phase=2,
            )

    def test_driver_is_apk_source_arm64_restart_and_receipt_bound(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        compile(source, str(DRIVER), "exec")
        for marker in (
            "load_and_verify_manifest", "source_graph_snapshot",
            "android_device_observation", "expected_apk_sha256",
            "exactProposalRunCharacterIds", "gmAndOwnerReviewDigests",
            "atomicCoreReceiptAndSuccessor", "receiptRestartRecovery",
            "sr5-after-run-entry-proposal-id", "sr5-after-run-entry-run-id",
            "sr5-after-run-entry-character-id", "sr5-after-run-receipt-acknowledge",
        ):
            self.assertIn(marker, source)
        self.assertGreaterEqual(source.count("force_stop_and_launch_new_process"), 3)
        self.assertIn('"status": "device-pass-source-bound"', source)
        self.assertNotIn('"releaseAttested": True', source)

    def test_argument_failure_writes_nonpassing_receipt_without_device_access(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            receipt = Path(temporary) / "receipt.json"
            with redirect_stderr(io.StringIO()):
                self.assertEqual(2, driver.main(["--receipt", str(receipt)]))
            value = json.loads(receipt.read_text(encoding="utf-8"))
            self.assertEqual("fail", value["status"])
            self.assertEqual("manifest-not-verified", value["releaseEvidenceStatus"])


if __name__ == "__main__":
    unittest.main()
