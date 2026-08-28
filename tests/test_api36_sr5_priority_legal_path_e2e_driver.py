from __future__ import annotations

import ast
import importlib.util
from pathlib import Path
import sys
import unittest


ROOT = Path(__file__).resolve().parents[1]
DRIVER = ROOT / "tests/run_api36_sr5_priority_legal_path_e2e.py"
sys.path.insert(0, str(DRIVER.parent))
SPEC = importlib.util.spec_from_file_location("sr5_priority_legal_path_driver", DRIVER)
assert SPEC is not None and SPEC.loader is not None
driver = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = driver
SPEC.loader.exec_module(driver)


class Api36Sr5PriorityLegalPathDriverTests(unittest.TestCase):
    def test_driver_is_syntax_valid_and_covers_the_contextual_path(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        ast.parse(source)
        self.assertEqual(
            (
                "basics",
                "method",
                "foundation",
                "attributes",
                "qualities",
                "skills",
                "magic-resonance",
                "resources",
                "contacts-lifestyles",
                "identity-story",
            ),
            tuple(stage.step_id for stage in driver.LEGAL_PATH_STAGES),
        )
        driver.validate_stage_catalog()

    def test_disabled_stage_fails_closed_without_fallback_or_generic_edit(self) -> None:
        source = DRIVER.read_text(encoding="utf-8")
        for marker in (
            'row.attributes.get("enabled") != "true"',
            'row.attributes.get("clickable") != "true"',
            "no fallback is allowed",
            "creation-finalization-authority-ready",
            "creation-finalization-open-review",
            "creation-finalization-confirm",
            "creation-finalization-receipt",
            "creation-finalization-career-reopen",
            "creation-gear-saved-draft-revision",
            "creation-lifestyles-authority",
            "creation-basics-sourcebooks-contract-unavailable",
            '"releaseAuthority": False',
        ):
            self.assertIn(marker, source)
        for forbidden in (
            "AttributeEditRequest",
            "ApplyOriginDossierEditAsync",
            "settings.xml#",
            "character:write",
            "status\": \"pass",
        ):
            self.assertNotIn(forbidden, source)

    def test_current_finalizer_scope_is_explicit_and_not_invented(self) -> None:
        required = {
            stage.step_id
            for stage in driver.LEGAL_PATH_STAGES
            if stage.required_by_finalizer
        }
        self.assertEqual(
            {"method", "attributes", "qualities", "skills", "magic-resonance", "resources"},
            required,
        )
        self.assertNotIn("contacts-lifestyles", required)
        self.assertNotIn("identity-story", required)
        self.assertNotIn("basics", required)

    def test_driver_is_not_activated_as_release_or_workflow_authority(self) -> None:
        relative = DRIVER.relative_to(ROOT).as_posix()
        activation_roots = (ROOT / ".github", ROOT / "scripts")
        matches: list[str] = []
        for activation_root in activation_roots:
            if not activation_root.exists():
                continue
            for path in activation_root.rglob("*"):
                if not path.is_file() or path.suffix not in {".py", ".yml", ".yaml", ".sh", ".json"}:
                    continue
                if relative in path.read_text(encoding="utf-8", errors="replace"):
                    matches.append(path.relative_to(ROOT).as_posix())
        self.assertEqual([], matches)


if __name__ == "__main__":
    unittest.main()
