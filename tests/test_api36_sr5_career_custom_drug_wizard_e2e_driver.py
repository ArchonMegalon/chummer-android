from __future__ import annotations

import ast
import copy
import hashlib
from pathlib import Path
import unittest
import uuid
import xml.etree.ElementTree as ET

import run_api36_editing_e2e as shared
import run_api36_sr5_career_custom_drug_wizard_e2e as driver


ROOT = Path(__file__).resolve().parents[1]
DRIVER = ROOT / "tests/run_api36_sr5_career_custom_drug_wizard_e2e.py"
FIXTURE = ROOT / "tests/fixtures/sr5-career-custom-drug-wizard-e2e.chum5"


def wrapper(value: str) -> dict[str, object]:
    return {"Value": value}


def selection() -> dict[str, object]:
    return {
        "Name": driver.RECIPE_NAME,
        "GradeId": wrapper(driver.GRADE_ID),
        "Quantity": 1,
        "Stolen": False,
        "FreeCost": False,
        "MarkupPercent": 0,
        "Components": [
            {
                "ComponentId": wrapper(driver.FOUNDATION_ID),
                "Level": driver.FOUNDATION_LEVEL,
            }
        ],
    }


def checkpoints() -> tuple[
    shared.WorkspaceAuthority,
    shared.WorkspaceAuthority,
    dict[str, object],
    dict[str, object],
]:
    workspace_id = "workspace-custom-drug-test"
    initial = shared.WorkspaceAuthority(workspace_id, 4, 4, "a" * 64, "1" * 64)
    persisted = shared.WorkspaceAuthority(workspace_id, 5, 5, "b" * 64, "2" * 64)
    command: dict[str, object] = {
        "ExpectedContentRevision": 4,
        "ExpectedCharacterDigest": "a" * 64,
        "ExpectedCatalogDigest": "d" * 64,
        "ExpectedRulesDigest": "e" * 64,
        "ExpectedQuoteDigest": "f" * 64,
        "IdempotencyKey": "custom-drug-recipe:" + "3" * 32,
        "Selection": selection(),
        "NewDrugInstanceId": wrapper("11111111-1111-4111-8111-111111111111"),
        "NewComponentInstanceIds": ["22222222-2222-4222-8222-222222222222"],
    }
    reviewed: dict[str, object] = {
        "SchemaId": driver.CHECKPOINT_SCHEMA,
        "WorkspaceId": {"Value": workspace_id},
        "BoundContentRevision": 4,
        "BoundCharacterDigest": "a" * 64,
        "BoundCatalogDigest": "d" * 64,
        "BoundRulesDigest": "e" * 64,
        "Selection": selection(),
        "Phase": 1,
        "Command": command,
        "Receipt": None,
    }
    receipt: dict[str, object] = {
        "PreviousContentRevision": 4,
        "ContentRevision": 5,
        "PreviousCharacterDigest": "a" * 64,
        "CharacterDigest": "b" * 64,
        "CatalogDigest": "d" * 64,
        "RulesDigest": "e" * 64,
        "QuoteDigest": "f" * 64,
        "CommandDigest": driver.command_digest(command),
        "IdempotencyKeyDigest": hashlib.sha256(
            str(command["IdempotencyKey"]).encode("utf-8")
        ).hexdigest(),
        "DrugInstanceId": wrapper("11111111-1111-4111-8111-111111111111"),
        "ComponentInstanceIds": ["22222222-2222-4222-8222-222222222222"],
        "DrugXmlDigest": "c" * 64,
        "ReceiptDigest": "",
    }
    receipt["ReceiptDigest"] = driver.receipt_digest(receipt)
    applied = {
        **reviewed,
        "BoundContentRevision": 5,
        "BoundCharacterDigest": "b" * 64,
        "Phase": 3,
        "Receipt": receipt,
    }
    return initial, persisted, reviewed, applied


def saved_xml(
    command: dict[str, object],
    receipt: dict[str, object],
) -> tuple[str, str]:
    drug_id = command["NewDrugInstanceId"]["Value"]
    component_id = receipt["ComponentInstanceIds"][0]
    drug = (
        f"<drug><sourceid>{driver.EMPTY_GUID}</sourceid><guid>{drug_id}</guid>"
        f"<name>{driver.RECIPE_NAME}</name><category>Custom Drug</category>"
        "<quantity>1</quantity><drugcomponents><drugcomponent>"
        f"<sourceid>{driver.FOUNDATION_ID}</sourceid><guid>{component_id}</guid>"
        f"<name>{driver.FOUNDATION_NAME}</name><category>Foundation</category>"
        "<effects><effect><level>0</level>"
        "<attribute><name>BOD</name><value>2</value></attribute>"
        "<attribute><name>CHA</name><value>-2</value></attribute>"
        "<attribute><name>WIL</name><value>1</value></attribute>"
        '<quality rating="3">High Pain Tolerance</quality></effect></effects>'
        "<availability>+4R</availability><cost>75</cost><level>0</level>"
        "<limit>1</limit><rating>6</rating><threshold>2</threshold>"
        "<source>CF</source><page>190</page></drugcomponent></drugcomponents>"
        "<availability>0</availability><grade>Standard</grade><sortorder>0</sortorder>"
        "<stolen>False</stolen><source></source><page></page><notes></notes>"
        "<notesColor>Chocolate</notesColor></drug>"
    )
    payload = (
        "<character><alias>CareerCustomDrugWizardE2E</alias><created>True</created>"
        "<gameedition>SR5</gameedition>"
        "<settings>67e25032-2a4e-42ca-97fa-69f7f608236c</settings>"
        "<nuyen>10000</nuyen><customstate>"
        '<sentinel guid="custom-drug-unrelated-state">keep-nested-structure</sentinel>'
        f"</customstate><drugs>{drug}</drugs><expenses /></character>"
    )
    return payload, drug


class Api36Sr5CareerCustomDrugWizardE2EDriverTests(unittest.TestCase):
    def test_driver_is_non_gating_phone_wizard_proof(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        ast.parse(source)
        self.assertIn('JOURNEY = "sr5-career-custom-drug-wizard"', source)
        self.assertIn('abi != "x86_64"', source)
        self.assertIn('api != "36"', source)
        self.assertIn('"gateRegistered": False', source)
        self.assertIn('"aggregateJourneyCountContribution": 0', source)
        self.assertIn('"publicationAuthorized": False', source)
        self.assertIn('"releaseClaim": "none"', source)
        self.assertIn('"no generic or full editing parity"', source)
        self.assertIn('"no tablet support"', source)
        self.assertIn('"career-custom-drug-review"', source)
        self.assertIn('"career-custom-drug-confirm"', source)
        self.assertIn('"career-custom-drug-receipt"', source)
        self.assertIn('device.wait("Confirm exact custom-drug recipe"', source)
        self.assertIn("force_stop_and_launch_new_process", source)
        self.assertNotIn("collection-editor", source)
        self.assertNotIn("Full Editing", source)

    def test_fixture_is_exact_created_sr5_cf_runner_with_empty_drug_container(self) -> None:
        root = ET.parse(FIXTURE).getroot()
        self.assertEqual("character", root.tag)
        self.assertEqual("CareerCustomDrugWizardE2E", root.findtext("alias"))
        self.assertEqual("True", root.findtext("created"))
        self.assertEqual("SR5", root.findtext("gameedition"))
        self.assertEqual(
            "67e25032-2a4e-42ca-97fa-69f7f608236c",
            root.findtext("settings"),
        )
        self.assertEqual("10000", root.findtext("nuyen"))
        self.assertEqual(1, len(root.findall("./drugs")))
        self.assertEqual([], root.findall("./drugs/drug"))
        self.assertEqual([], root.findall("./expenses/expense"))
        sentinel = root.find("./customstate/sentinel")
        self.assertIsNotNone(sentinel)
        self.assertEqual("custom-drug-unrelated-state", sentinel.get("guid"))
        self.assertEqual("keep-nested-structure", sentinel.text)

    def test_reviewed_and_applied_checkpoint_contracts_are_exact(self) -> None:
        initial, persisted, reviewed, applied = checkpoints()
        reviewed_command, reviewed_receipt = driver.validate_checkpoint(
            reviewed,
            workspace_id=initial.workspace_id,
            initial=initial,
            persisted=None,
            phase=1,
        )
        self.assertIsNone(reviewed_receipt)
        applied_command, applied_receipt = driver.validate_checkpoint(
            applied,
            workspace_id=initial.workspace_id,
            initial=initial,
            persisted=persisted,
            phase=3,
        )
        self.assertEqual(reviewed_command, applied_command)
        self.assertIsNotNone(applied_receipt)

    def test_hostile_checkpoint_fields_identity_and_receipt_digest_fail_closed(self) -> None:
        initial, persisted, reviewed, applied = checkpoints()
        hostile_extra = copy.deepcopy(reviewed)
        hostile_extra["DirectApply"] = True
        hostile_grade = copy.deepcopy(reviewed)
        hostile_grade["Selection"]["GradeId"]["Value"] = str(uuid.uuid4())
        hostile_receipt = copy.deepcopy(applied)
        hostile_receipt["Receipt"]["ReceiptDigest"] = "0" * 64
        hostile_component = copy.deepcopy(applied)
        hostile_component["Receipt"]["ComponentInstanceIds"] = [
            "33333333-3333-4333-8333-333333333333"
        ]
        for payload, phase, saved in (
            (hostile_extra, 1, None),
            (hostile_grade, 1, None),
            (hostile_receipt, 3, persisted),
            (hostile_component, 3, persisted),
        ):
            with self.subTest(payload=payload):
                with self.assertRaises(RuntimeError):
                    driver.validate_checkpoint(
                        payload,
                        workspace_id=initial.workspace_id,
                        initial=initial,
                        persisted=saved,
                        phase=phase,
                    )

    def test_persisted_xml_is_bound_to_exact_identities_receipt_and_unrelated_state(self) -> None:
        _, _, _, applied = checkpoints()
        command = applied["Command"]
        receipt = applied["Receipt"]
        payload, drug_xml = saved_xml(command, receipt)
        receipt["DrugXmlDigest"] = hashlib.sha256(drug_xml.encode("utf-8")).hexdigest()
        receipt["ReceiptDigest"] = driver.receipt_digest(receipt)
        projection = driver.assert_persisted_xml(payload, command, receipt)
        self.assertEqual(command["NewDrugInstanceId"]["Value"], projection["drugInstanceId"])
        self.assertEqual(receipt["ComponentInstanceIds"], projection["componentInstanceIds"])

        with self.assertRaises(RuntimeError):
            driver.assert_persisted_xml(
                payload.replace("<nuyen>10000</nuyen>", "<nuyen>9999</nuyen>"),
                command,
                receipt,
            )
        with self.assertRaises(RuntimeError):
            driver.assert_persisted_xml(
                payload.replace(
                    "custom-drug-unrelated-state",
                    "hostile-unrelated-state",
                ),
                command,
                receipt,
            )

    def test_driver_is_absent_from_seven_journey_gate_and_aggregate(self) -> None:
        authority_files = (
            ROOT / "scripts/api36_wizard_gate_contract.py",
            ROOT / "scripts/run-api36-editing-e2e-ci.sh",
            ROOT / "scripts/verify-api36-editing-e2e-aggregate.py",
            ROOT / ".github/workflows/api36-editing-e2e.yml",
        )
        for path in authority_files:
            with self.subTest(path=path):
                self.assertNotIn(
                    driver.JOURNEY,
                    path.read_text(encoding="utf-8"),
                )


if __name__ == "__main__":
    unittest.main()
