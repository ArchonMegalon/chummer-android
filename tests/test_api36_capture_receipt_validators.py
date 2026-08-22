import ast
import importlib.util
import json
import tempfile
import unittest
from pathlib import Path


REPO = Path(__file__).resolve().parents[1]
SCRIPT = REPO / "scripts" / "materialize_chummer5_editability_inventory.py"
SPEC = importlib.util.spec_from_file_location("chummer5_editability_inventory_capture_receipts", SCRIPT)
assert SPEC is not None and SPEC.loader is not None
inventory = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(inventory)


class Api36CaptureReceiptValidatorTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.presentation_root = REPO.parent / "chummer-presentation"
        cls.core_root = REPO.parent / "chummer-core-engine"
        cls.specs = inventory._capture_only_phone_e2e_specs(
            cls.presentation_root,
            cls.core_root,
        )

    def valid_receipt(self, spec: dict) -> dict:
        controls = {
            control: {proof_key: "pass" for proof_key in spec["proofKeys"]}
            for control in spec["controls"]
        }
        return {
            "schema": "chummer.android.editing-e2e/v1",
            "status": "pass",
            "profile": "phone",
            "journey": spec["journey"],
            "apiLevel": 36,
            "abi": inventory.PHONE_E2E_ABI,
            "package": inventory.PHONE_E2E_PACKAGE,
            "apkSha256": "a" * 64,
            "driverSha256": inventory._sha256_file(spec["driver"]),
            "sharedDriverSha256": inventory._sha256_file(spec["sharedDriver"]),
            **{
                key: inventory._sha256_file(path)
                for key, path in spec["fixtureFiles"].items()
            },
            **{
                key: inventory._sha256_file(path)
                for key, path in spec["sourceFiles"].items()
            },
            "controlCount": len(controls),
            "controls": controls,
            "journeys": {journey: "pass" for journey in spec["journeys"]},
        }

    def test_all_twenty_two_driver_contracts_are_api36_arm64_package_bound(self) -> None:
        self.assertEqual(22, len(self.specs))
        for journey, spec in self.specs.items():
            with self.subTest(journey=journey):
                source = spec["driver"].read_text(encoding="utf-8")
                self.assertIn('api != "36"', source)
                self.assertIn('abi != "arm64-v8a"', source)
                self.assertIn('"abi": abi', source)
                self.assertIn('"package": shared.PACKAGE', source)
                self.assertIn(f'"journey": "{journey}"', source)
                for control in spec["controls"]:
                    self.assertIn(control.split(".")[-1], source)
                for proof_key in spec["proofKeys"]:
                    self.assertIn(f'"{proof_key}"', source)
                for journey_proof in spec["journeys"]:
                    self.assertIn(f'"{journey_proof}"', source)

                tree = ast.parse(source)
                constants = {}
                for node in tree.body:
                    if (
                        isinstance(node, ast.Assign)
                        and len(node.targets) == 1
                        and isinstance(node.targets[0], ast.Name)
                    ):
                        try:
                            constants[node.targets[0].id] = ast.literal_eval(node.value)
                        except (ValueError, TypeError):
                            pass
                self.assertEqual(
                    tuple(constants.get("CONTROL_PROOF_KEYS") or constants.get("PROOF_KEYS")),
                    tuple(spec["proofKeys"]),
                )
                main = next(
                    node
                    for node in tree.body
                    if isinstance(node, ast.FunctionDef) and node.name == "main"
                )
                source_paths = next(
                    node.value
                    for node in ast.walk(main)
                    if isinstance(node, ast.Assign)
                    and len(node.targets) == 1
                    and isinstance(node.targets[0], ast.Name)
                    and node.targets[0].id == "source_paths"
                )
                self.assertEqual(
                    {ast.literal_eval(key) for key in source_paths.keys},
                    {"sharedDriverSha256", *spec["sourceFiles"]},
                )
                receipt = next(
                    node.value
                    for node in ast.walk(main)
                    if isinstance(node, ast.Assign)
                    and len(node.targets) == 1
                    and isinstance(node.targets[0], ast.Name)
                    and node.targets[0].id == "receipt"
                )
                receipt_fields = {
                    ast.literal_eval(key): value
                    for key, value in zip(receipt.keys, receipt.values)
                    if key is not None
                }
                self.assertEqual(
                    {ast.literal_eval(key) for key in receipt_fields["journeys"].keys},
                    set(spec["journeys"]),
                )

    def test_each_journey_accepts_its_exact_synthetic_receipt(self) -> None:
        for journey, original_spec in self.specs.items():
            with self.subTest(journey=journey), tempfile.TemporaryDirectory(dir=REPO / "docs") as temporary:
                spec = {**original_spec, "receipt": Path(temporary) / "receipt.json"}
                receipt = self.valid_receipt(spec)
                spec["receipt"].write_text(json.dumps(receipt), encoding="utf-8")
                validated = inventory._validated_capture_only_phone_e2e_receipt(spec)
                self.assertIsNotNone(validated)
                self.assertEqual("executed_api36", validated["status"])
                self.assertEqual(receipt["apkSha256"], validated["apkSha256"])

    def test_each_journey_fails_closed_for_missing_malformed_and_stale_receipts(self) -> None:
        for journey, original_spec in self.specs.items():
            with self.subTest(journey=journey), tempfile.TemporaryDirectory(dir=REPO / "docs") as temporary:
                spec = {**original_spec, "receipt": Path(temporary) / "receipt.json"}
                self.assertIsNone(inventory._validated_capture_only_phone_e2e_receipt(spec))
                spec["receipt"].write_text("{", encoding="utf-8")
                self.assertIsNone(inventory._validated_capture_only_phone_e2e_receipt(spec))

                valid = self.valid_receipt(spec)
                stale_receipts = []
                for key, value in (
                    ("schema", "wrong"),
                    ("status", "fail"),
                    ("profile", "tablet"),
                    ("journey", "wrong"),
                    ("apiLevel", 35),
                    ("abi", "x86_64"),
                    ("package", "invalid.package"),
                    ("apkSha256", "invalid"),
                    ("driverSha256", "0" * 64),
                    ("sharedDriverSha256", "0" * 64),
                    ("controlCount", 0),
                ):
                    stale = json.loads(json.dumps(valid))
                    stale[key] = value
                    stale_receipts.append((key, stale))

                fixture_key = next(iter(spec["fixtureFiles"]))
                stale = json.loads(json.dumps(valid))
                stale[fixture_key] = "0" * 64
                stale_receipts.append((fixture_key, stale))
                source_key = next(iter(spec["sourceFiles"]))
                stale = json.loads(json.dumps(valid))
                stale[source_key] = "0" * 64
                stale_receipts.append((source_key, stale))
                control = spec["controls"][0]
                stale = json.loads(json.dumps(valid))
                stale["controls"][control].pop(spec["proofKeys"][0])
                stale_receipts.append(("missingProof", stale))
                stale = json.loads(json.dumps(valid))
                stale["controls"]["unexpected.control"] = stale["controls"][control]
                stale_receipts.append(("extraControl", stale))
                stale = json.loads(json.dumps(valid))
                stale["journeys"][spec["journeys"][0]] = "fail"
                stale_receipts.append(("failedJourney", stale))
                stale = json.loads(json.dumps(valid))
                stale["journeys"]["unexpectedJourney"] = "pass"
                stale_receipts.append(("extraJourney", stale))

                for failure, stale in stale_receipts:
                    with self.subTest(journey=journey, failure=failure):
                        spec["receipt"].write_text(json.dumps(stale), encoding="utf-8")
                        self.assertIsNone(inventory._validated_capture_only_phone_e2e_receipt(spec))

    def test_promotion_requires_a_validated_receipt_for_the_exact_driver_ref(self) -> None:
        driver_ref = self.specs["gear-name"]["driver"].relative_to(REPO).as_posix()
        pending = {
            "status": "implemented_pending_emulator",
            "coverageLimit": "API 36 phone driver is present but not yet executed",
            "e2e": {"status": "scripted_not_executed", "ref": driver_ref},
        }
        self.assertEqual(
            "implemented_pending_emulator",
            inventory._promote_capture_only_phone_e2e(dict(pending), {})["status"],
        )
        receipt = {
            "status": "executed_api36",
            "ref": "docs/editability-evidence/test/receipt.json",
            "receiptSha256": "b" * 64,
            "apkSha256": "c" * 64,
        }
        promoted = inventory._promote_capture_only_phone_e2e(
            {**pending, "e2e": dict(pending["e2e"])},
            {driver_ref: receipt},
        )
        self.assertEqual("implemented_verified_api36", promoted["status"])
        self.assertEqual(receipt, promoted["e2e"])
        self.assertIn("executed", promoted["coverageLimit"])

    def test_generated_inventory_does_not_promote_without_receipts(self) -> None:
        payload = json.loads(
            (REPO / "docs" / "ANDROID_CHUMMER5_EDITABILITY_INVENTORY.generated.json").read_text(
                encoding="utf-8"
            )
        )
        driver_refs = {
            spec["driver"].relative_to(REPO).as_posix()
            for spec in self.specs.values()
        }
        rows = [row for row in payload["rows"] if row["e2e"]["phone"].get("ref") in driver_refs]
        self.assertEqual(47, len(rows))
        self.assertTrue(all(row["phone"]["status"] == "implemented_pending_emulator" for row in rows))
        self.assertTrue(all(row["e2e"]["phone"]["status"] == "scripted_not_executed" for row in rows))


if __name__ == "__main__":
    unittest.main()
