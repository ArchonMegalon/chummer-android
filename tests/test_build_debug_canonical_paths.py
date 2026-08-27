import unittest
from pathlib import Path


REPO = Path(__file__).resolve().parents[1]


class BuildDebugCanonicalPathTests(unittest.TestCase):
    def test_local_tree_references_use_one_physical_project_identity(self) -> None:
        script = (REPO / "scripts/build-debug.sh").read_text(encoding="utf-8")

        self.assertIn('presentation_root="$(cd "${CHUMMER_PRESENTATION_ROOT:', script)
        self.assertIn('core_engine_root="$(cd "${CHUMMER_CORE_ENGINE_ROOT:', script)
        for property_name in (
            "ChummerPresentationRoot",
            "ChummerCoreEngineRoot",
            "ChummerLocalContractsProject",
            "ChummerLocalCampaignContractsProject",
            "ChummerLocalHubRegistryContractsProject",
            "ChummerLocalRunContractsProject",
            "ChummerLocalUiKitProject",
            "ChummerLocalMediaContractsProject",
        ):
            self.assertIn(f'"-p:{property_name}=', script)
        self.assertGreaterEqual(script.count('"${local_tree_args[@]}"'), 6)


if __name__ == "__main__":
    unittest.main()
