"""Exercise the real After Run producer/consumer shape, without ADB or authority claims."""
import copy
import hashlib
import json
from pathlib import Path
import sys
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "scripts"))
import api36_arm64_physical_contract as contract
import run_api36_sr5_after_run_settlement_e2e as driver


def producer_projection():
    fixture = driver.load_fixture()
    common = dict(
        workspace_id="workspace-test", workspace_revision=41,
        character_projection_digest=fixture["runner"]["expectedSha256"],
        owner_id="ffffffff-ffff-ffff-ffff-ffffffffffff",
        transaction_id="eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee",
    )
    reviewed = driver._expected_after_run_authority(fixture, version=1, phase=0, **common)
    applied = driver._expected_after_run_authority(fixture, version=3, phase=2, **common)
    validation = {key: value for key, value in common.items()
                  if key not in {"owner_id", "transaction_id"}}
    review = driver.validate_checkpoint(reviewed, fixture, version=1, phase=0, **validation)
    receipt = driver.validate_checkpoint(applied, fixture, version=3, phase=2, **validation)
    saved = dict(workspaceId="workspace-test", contentRevision=41, savedRevision=41,
                 payloadSha256=fixture["runner"]["expectedSha256"], documentSha256="a" * 64)
    successor = dict(saved, contentRevision=42, savedRevision=42,
                     payloadSha256="b" * 64, documentSha256="c" * 64)
    seal = lambda value: hashlib.sha256(json.dumps(value).encode()).hexdigest()
    return review, receipt, {
        "import": dict(saved, savedRevision=0), "initialSaved": saved,
        "restoredBeforeApply": dict(saved), "savedSuccessor": successor,
        "finalRestartSuccessor": dict(successor), "reviewedCheckpoint": reviewed,
        "reviewedCheckpointSha256": seal(reviewed), "appliedCheckpoint": applied,
        "appliedCheckpointSha256": seal(applied), "transactionAndReviewAuthority": receipt,
        "restartProcessIds": [["101"], ["102"], ["103"]],
    }


class AfterRunProducerConsumerTests(unittest.TestCase):
    def test_real_producer_checkpoints_and_unsaved_import_are_consumable(self):
        _, _, proof = producer_projection()
        contract.validate_proof_stages("after-run", proof)

    def test_final_summary_carries_validated_committed_receipt_not_review_placeholder(self):
        review, applied, proof = producer_projection()
        before = copy.deepcopy((review, applied))
        actual = driver.confirmed_transaction_authority(review, applied)
        self.assertEqual(applied, actual)
        self.assertEqual(proof["appliedCheckpoint"]["Receipt"]["ReceiptDigest"], actual["receiptDigest"])
        self.assertEqual(before, (review, applied))
        self.assertIsNot(actual, applied)
        source = Path(driver.__file__).read_text(encoding="utf-8")
        self.assertIn('"transactionAndReviewAuthority": transaction_projection,', source)

    def test_summary_rejects_precommit_foreign_or_malformed_receipts(self):
        review, applied, _ = producer_projection()
        for field, value in (("receiptDigest", ""), ("receiptDigest", "A" * 64),
                             ("transactionId", "dddddddd-dddd-dddd-dddd-dddddddddddd"),
                             ("gmReviewDigest", "d" * 64), ("ownerReviewDigest", "e" * 64),
                             ("extra", True)):
            with self.subTest(field=field, value=value), self.assertRaises(RuntimeError):
                driver.confirmed_transaction_authority(review, dict(applied, **{field: value}))

    def test_physical_consumer_rejects_omitted_or_inconsistent_save_chain(self):
        _, _, original = producer_projection()
        for stage, field, value in (
            ("import", "savedRevision", -1), ("import", "savedRevision", True),
            ("import", "savedRevision", 42), ("initialSaved", "savedRevision", 0),
            ("initialSaved", "contentRevision", 42), ("initialSaved", "payloadSha256", "d" * 64),
            ("restoredBeforeApply", "workspaceId", "another-workspace"),
            ("savedSuccessor", "contentRevision", 43), ("savedSuccessor", "savedRevision", 0),
            ("finalRestartSuccessor", "documentSha256", "d" * 64),
        ):
            proof = copy.deepcopy(original)
            proof[stage][field] = value
            with self.subTest(stage=stage, field=field), self.assertRaises(ValueError):
                contract.validate_proof_stages("after-run", proof)
        omitted = copy.deepcopy(original)
        del omitted["initialSaved"]
        with self.assertRaises(ValueError):
            contract.validate_proof_stages("after-run", omitted)
        with self.assertRaises(ValueError):
            contract.validate_workspace(original["import"], "non-import durable workspace")

    def test_physical_consumer_binds_summary_to_actual_review_plan_and_receipt(self):
        _, _, original = producer_projection()
        for field in ("transactionId", "gmReviewDigest", "ownerReviewDigest", "receiptDigest"):
            proof = copy.deepcopy(original)
            proof["transactionAndReviewAuthority"][field] = "d" * 64
            with self.subTest(field=field), self.assertRaises(ValueError):
                contract.validate_proof_stages("after-run", proof)
        for stage in ("reviewedCheckpoint", "appliedCheckpoint"):
            proof = copy.deepcopy(original)
            proof[stage]["IdempotencyKey"] = "different"
            with self.subTest(stage=stage), self.assertRaises(ValueError):
                contract.validate_proof_stages("after-run", proof)

    def test_review_state_consent_and_typed_identity_cannot_be_relabelled(self):
        _, _, original = producer_projection()
        for stage, field, value in (
            ("reviewedCheckpoint", "SchemaVersion", 2),
            ("appliedCheckpoint", "SchemaVersion", True),
            ("reviewedCheckpoint", "Phase", 2),
            ("appliedCheckpoint", "Version", 1),
            ("reviewedCheckpoint", "RouteId", "sr5-career/after-run/settlement/receipt"),
            ("appliedCheckpoint", "RouteId", "sr5-career/after-run/settlement/receipt"),
        ):
            proof = copy.deepcopy(original)
            proof[stage][field] = value
            with self.subTest(stage=stage, field=field), self.assertRaises(ValueError):
                contract.validate_proof_stages("after-run", proof)
        proof = copy.deepcopy(original)
        for stage in ("reviewedCheckpoint", "appliedCheckpoint"):
            proof[stage]["Draft"]["Acknowledgements"]["OwnerApprovalReviewed"] = False
        with self.assertRaises(ValueError):
            contract.validate_proof_stages("after-run", proof)
        proof = copy.deepcopy(original)
        proof["appliedCheckpoint"]["Receipt"]["Identity"]["RunId"] = "different-run"
        with self.assertRaises(ValueError):
            contract.validate_proof_stages("after-run", proof)


if __name__ == "__main__":
    unittest.main()
