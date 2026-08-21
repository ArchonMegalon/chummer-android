import ast
from pathlib import Path
import unittest
import uuid
import xml.etree.ElementTree as ET


REPO = Path(__file__).resolve().parents[1]
DRIVER = REPO / "tests" / "run_api36_sustained_effects_e2e.py"
FIXTURES = (
    REPO / "tests" / "fixtures" / "creation-sustained-effects-e2e.chum5",
    REPO / "tests" / "fixtures" / "career-sustained-effects-e2e.chum5",
)


class Api36SustainedEffectsE2EDriverTests(unittest.TestCase):
    def test_driver_is_phone_only_api36_digest_bound_and_exact(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        ast.parse(source)
        for control in (
            "SustainedObjectControl.nudForce",
            "SustainedObjectControl.nudNetHits",
            "SustainedObjectControl.chkSelfSustained",
            "SustainedObjectControl.cmdDelete",
        ):
            self.assertIn(control, source)
        for marker in (
            '"journey": "sustained-effects"',
            '"profile": "phone"',
            'api != "36"',
            '"linkedTypeGuidOccurrenceIdentity"',
            '"duplicateCastIsolation"',
            '"explicitDeleteConfirmation"',
            '"sustainedEffectsRulesSha256"',
            '"presenterPersistenceSha256"',
            '"workspaceStoreSha256"',
            '"creationFixtureSha256"',
            '"careerFixtureSha256"',
            '"controlCount": len(CONTROLS)',
            '"build-sustained-effects"',
            'f"sustained-effect-force-{effect_token}"',
            'f"sustained-effect-net-hits-{effect_token}"',
            'f"sustained-effect-self-{effect_token}"',
            'f"sustained-effect-delete-{effect_token}"',
        ):
            self.assertIn(marker, source)
        self.assertGreaterEqual(source.count('device.shell("am", "force-stop"'), 2)
        self.assertNotIn('"tablet"', source)

    def test_fixtures_bind_duplicate_occurrence_critter_visibility_and_unrelated_xml(self) -> None:
        all_ids: set[uuid.UUID] = set()
        created_values: list[str | None] = []
        for fixture in FIXTURES:
            root = ET.parse(fixture).getroot()
            created_values.append(root.findtext("created"))
            spell = root.find("./spells/spell")
            critter = root.find("./critterpowers/critterpower")
            self.assertIsNotNone(spell)
            self.assertIsNotNone(critter)
            spell_id = uuid.UUID(spell.findtext("guid", default=""))
            critter_id = uuid.UUID(critter.findtext("guid", default=""))
            self.assertNotIn(spell_id, all_ids)
            self.assertNotIn(critter_id, all_ids)
            all_ids.update((spell_id, critter_id))

            effects = root.findall("./sustainedobjects/sustainedobject")
            self.assertEqual(3, len(effects))
            spell_effects = [
                effect
                for effect in effects
                if effect.findtext("linkedobjecttype") == "Spell"
            ]
            self.assertEqual(2, len(spell_effects))
            self.assertTrue(all(effect.findtext("linkedobject") == str(spell_id) for effect in spell_effects))
            self.assertEqual(["4", "6"], [effect.findtext("force") for effect in spell_effects])
            self.assertEqual(["True", "False"], [effect.findtext("self") for effect in spell_effects])
            critter_effect = effects[2]
            self.assertEqual("CritterPower", critter_effect.findtext("linkedobjecttype"))
            self.assertEqual(str(critter_id), critter_effect.findtext("linkedobject"))
            self.assertTrue(root.findtext("./customstate/sustained", default="").endswith("unrelated sustained text"))

        self.assertEqual(["False", "True"], created_values)
        self.assertEqual(4, len(all_ids))

    def test_phone_source_is_revision_bound_and_routes_all_four_actions(self) -> None:
        build = (REPO / "src/Chummer.Android/Native/BuildPage.cs").read_text(encoding="utf-8")
        page = (REPO / "src/Chummer.Android/Native/SustainedObjectsPage.cs").read_text(encoding="utf-8")
        coordinator = (REPO / "src/Chummer.Android/Native/RunnerSessionCoordinator.cs").read_text(encoding="utf-8")
        self.assertIn('automationId: "build-sustained-effects"', build)
        self.assertIn("new SustainedObjectsPage", build)
        for marker in (
            "CharacterSustainedObjectIdentity",
            'AutomationId = "sustained-effects-page"',
            'AutomationId = $"sustained-effect-editor-{token}"',
            '$"sustained-effect-open-',
            '$"sustained-effect-force-',
            '$"sustained-effect-net-hits-',
            '$"sustained-effect-self-',
            '$"sustained-effect-save-',
            '$"sustained-effect-delete-',
            "CharacterSustainedObjectAction.Update",
            "CharacterSustainedObjectAction.Delete",
            "Confirmed: true",
            "_contentRevision",
        ):
            self.assertIn(marker, page)
        for marker in (
            "PrepareSustainedObjectsEditAsync",
            "ApplySustainedObjectEditAsync",
            "ExpectedContentRevision",
            "_presenter.SaveAsync",
        ):
            self.assertIn(marker, coordinator)


if __name__ == "__main__":
    unittest.main()
