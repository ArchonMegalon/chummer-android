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
    return driver._expected_after_run_authority(
        fixture, workspace_id="workspace-test", workspace_revision=41,
        character_projection_digest=fixture["runner"]["expectedSha256"],
        owner_id="ffffffff-ffff-ffff-ffff-ffffffffffff",
        transaction_id="eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee",
        version=3 if applied else 1, phase=2 if applied else 0,
    )


def validate(value: dict[str, object], fixture: dict[str, object], *, applied: bool) -> dict[str, str]:
    return driver.validate_checkpoint(
        value, fixture, workspace_id="workspace-test", workspace_revision=41,
        character_projection_digest=fixture["runner"]["expectedSha256"],
        version=3 if applied else 1, phase=2 if applied else 0,
    )


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
        authority = validate(reviewed, fixture, applied=False)
        self.assertEqual("e" * 8 + "-" + "e" * 4 + "-" + "e" * 4 + "-" + "e" * 4 + "-" + "e" * 12, authority["transactionId"])
        self.assertNotEqual(authority["gmReviewDigest"], authority["ownerReviewDigest"])
        applied = checkpoint(fixture, applied=True)
        applied_authority = validate(applied, fixture, applied=True)
        for field in ("transactionId", "gmReviewDigest", "ownerReviewDigest"):
            self.assertEqual(authority[field], applied_authority[field])
        self.assertRegex(applied_authority["receiptDigest"], r"^[0-9a-f]{64}$")
        driver.require_same_draft(reviewed, applied)
        tampered = copy.deepcopy(applied)
        tampered["Receipt"]["HeatAfter"] = 4
        with self.assertRaises(RuntimeError):
            validate(tampered, fixture, applied=True)

    def test_hostile_self_consistent_foreign_review_authority_is_rejected(self) -> None:
        fixture = driver.load_fixture()
        foreign = copy.deepcopy(fixture)
        foreign["reviews"]["gm"]["actorId"] = "gm-18"
        payload = driver._expected_after_run_authority(
            foreign, workspace_id="workspace-test", workspace_revision=41,
            character_projection_digest=fixture["runner"]["expectedSha256"],
            owner_id="ffffffff-ffff-ffff-ffff-ffffffffffff",
            transaction_id="eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee", version=3, phase=2,
        )
        with self.assertRaises(RuntimeError):
            validate(payload, fixture, applied=True)

    def test_hostile_arbitrary_receipt_and_logical_digests_are_rejected(self) -> None:
        fixture = driver.load_fixture()
        for path in (
            ("Receipt", "ReceiptDigest"),
            ("Draft", "Candidate", "Binding", "Quote", "LogicalDigest"),
            ("Draft", "Plan", "PlanDigest"),
        ):
            payload = checkpoint(fixture, applied=True)
            target = payload
            for part in path[:-1]:
                target = target[part]
            target[path[-1]] = "0" * 64
            with self.subTest(path=path), self.assertRaises(RuntimeError):
                validate(payload, fixture, applied=True)

    def test_hostile_source_runtime_rewards_and_contact_costs_are_rejected(self) -> None:
        fixture = driver.load_fixture()
        mutations = (
            lambda value: value["Draft"]["Candidate"]["Binding"]["Quote"].__setitem__("RuntimeDigest", "0" * 64),
            lambda value: value["Draft"]["Candidate"]["RewardContext"].__setitem__("NuyenAward", 12501),
            lambda value: value["Draft"]["Plan"]["ContactsToAdd"][1].__setitem__("KarmaCost", 10),
            lambda value: value["Receipt"].__setitem__("ExpenseAmount", -10),
        )
        for mutate in mutations:
            payload = checkpoint(fixture, applied=True)
            mutate(payload)
            with self.subTest(mutate=mutate), self.assertRaises(RuntimeError):
                validate(payload, fixture, applied=True)

    def test_hostile_unrelated_name_contact_identity_and_nonpermitted_xml_fail(self) -> None:
        fixture = driver.load_fixture()
        transaction = "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"
        valid = driver._expected_successor_runner(
            fixture, transaction, "2026-08-28T12:34:56"
        )
        driver._assert_successor_runner(valid, fixture, transaction)
        for mutate in (
            lambda root: setattr(root.find("name"), "text", "Foreign Runner"),
            lambda root: setattr(root.find("./contacts/contact/guid"), "text", "99999999-9999-9999-9999-999999999999"),
            lambda root: ET.SubElement(root, "foreign"),
        ):
            changed = ET.fromstring(ET.tostring(valid))
            mutate(changed)
            with self.subTest(mutate=mutate), self.assertRaises(RuntimeError):
                driver._assert_successor_runner(changed, fixture, transaction)

    def test_unknown_fields_wrong_types_duplicate_contacts_and_foreign_projection_fail(self) -> None:
        fixture = driver.load_fixture()
        cases = []
        unknown = checkpoint(fixture, applied=True)
        unknown["Receipt"]["Foreign"] = True
        cases.append(unknown)
        wrong_type = checkpoint(fixture, applied=True)
        wrong_type["Receipt"]["KarmaAfter"] = "19"
        cases.append(wrong_type)
        duplicate = checkpoint(fixture, applied=True)
        duplicate["Receipt"]["AddedContacts"][1] = copy.deepcopy(
            duplicate["Receipt"]["AddedContacts"][0]
        )
        cases.append(duplicate)
        for payload in cases:
            with self.subTest(payload=payload), self.assertRaises(RuntimeError):
                validate(payload, fixture, applied=True)
        with self.assertRaises(RuntimeError):
            driver.validate_checkpoint(
                checkpoint(fixture, applied=False), fixture,
                workspace_id="workspace-test", workspace_revision=41,
                character_projection_digest="0" * 64, version=1, phase=0,
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
