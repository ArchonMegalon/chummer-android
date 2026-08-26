import ast
from pathlib import Path
import unittest


REPO = Path(__file__).resolve().parents[1]
TESTS = REPO / "tests"


class Api36Sr5CareerPhysicalProofContractTests(unittest.TestCase):
    def read(self, name: str) -> str:
        source = (TESTS / name).read_text(encoding="utf-8")
        ast.parse(source)
        return source

    def test_active_attribute_and_knowledge_require_one_shared_manifest_contract(self) -> None:
        for name in (
            "run_api36_sr5_career_active_skill_wizard_e2e.py",
            "run_api36_sr5_career_attribute_wizard_e2e.py",
            "run_api36_sr5_career_knowledge_language_wizard_e2e.py",
        ):
            with self.subTest(name=name):
                source = self.read(name)
                self.assertIn("--build-provenance-manifest", source)
                self.assertIn("load_and_verify_manifest", source)
                self.assertIn("require_transport_stability", source)
                self.assertIn("install_verified", source)
                self.assertLess(
                    source.index("require_transport_stability"),
                    source.index("install_verified"),
                )
                self.assertNotIn("CORE_REVISION =", source)
                self.assertNotIn("PRESENTATION_REVISION =", source)
                self.assertNotIn("--acknowledge-unverified-build-provenance", source)

    def test_quality_is_a_fail_closed_physical_restart_and_save_journey(self) -> None:
        source = self.read("run_api36_sr5_career_quality_wizard_e2e.py")
        for token in (
            "sr5-career-action-quality",
            "sr5-career-quality-review",
            "sr5-career-quality-resume",
            "sr5-career-quality-apply",
            "sr5-career-quality-receipt-acknowledge",
            "force_stop_and_launch_new_process",
            "require_saved_authority",
            "load_and_verify_manifest",
            "require_transport_stability",
            "install_verified",
            'action.get("Kind") != 3',
        ):
            self.assertIn(token, source)
        self.assertIn('"status": "device-pass-source-bound"', source)
        self.assertLess(
            source.index("require_transport_stability"),
            source.index("install_verified"),
        )
        self.assertNotIn('"releaseAttested": True', source)

    def test_skill_group_is_explicitly_unavailable_without_a_fixture(self) -> None:
        source = self.read("run_api36_sr5_career_skill_group_wizard_e2e.py")
        self.assertIn('"status": "unavailable"', source)
        self.assertIn('"physicalDeviceProof": False', source)
        self.assertIn('"releaseEvidenceEligible": False', source)
        self.assertIn("career-skill-group-advance-e2e.chum5", source)
        self.assertIn("return 3", source)
        self.assertNotIn("device-pass", source)


if __name__ == "__main__":
    unittest.main()
