from __future__ import annotations

import ast
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
DRIVER = ROOT / "tests/run_api36_creation_prerequisite_e2e.py"


class Api36CreationResourcesPriorityE2EContractTests(unittest.TestCase):
    def test_driver_proves_exact_rank_d_resources_preview_and_receipt(self) -> None:
        text = DRIVER.read_text(encoding="utf-8")
        ast.parse(text)
        for expression in (
            '"resources-preview-confirm": 150_000',
            '"creation-stage-resources"',
            'option_id = "creation-resources-option-karma-0"',
            'preview["optionId"] != "karma:0"',
            'preview["priorityGrant"] != 50_000',
            'preview["totalStartingNuyen"] != 50_000',
            '"creation-resources-confirm"',
            'receipt["workspaceRevision"] != before["contentRevision"] + 1',
            'receipt["savedRevision"] != before["savedRevision"] + 1',
            'receipt["previewDigest"] != preview["previewDigest"]',
        ):
            self.assertIn(expression, text)

    def test_driver_reopens_digest_bound_resources_after_process_restart(self) -> None:
        text = DRIVER.read_text(encoding="utf-8")
        restart = text[text.index('progress.advance("process-restart-reopen")') :]
        self.assertIn("shared.force_stop_and_launch_new_process", restart)
        self.assertIn("open_resources(device)", restart)
        self.assertIn("read_persisted_resources_authority(device, resources_receipt)", restart)
        for field in (
            'saved["optionId"] != expected_receipt["optionId"]',
            'saved["draftRevision"] != expected_receipt["draftRevision"]',
            'saved["draftDigest"] != expected_receipt["draftDigest"]',
        ):
            self.assertIn(field, text)

    def test_driver_rebinds_prerequisite_after_resources_advances_auxiliary_state(self) -> None:
        text = DRIVER.read_text(encoding="utf-8")
        self.assertLess(
            text.index('progress.advance("same-process-reopen")'),
            text.index('progress.advance("resources-preview-confirm")'),
        )
        for expression in (
            'post_resources_prerequisite_authority = read_persisted_prerequisite_authority(device)',
            'post_resources_binding_digests["auxiliaryState"]',
            '== confirmed_binding_digests["auxiliaryState"]',
            'int(resources_receipt["workspaceRevision"])',
            'int(resources_receipt["savedRevision"])',
            '"prerequisiteAuthorityAfterResources": post_resources_prerequisite_authority',
        ):
            self.assertIn(expression, text)


if __name__ == "__main__":
    unittest.main()
