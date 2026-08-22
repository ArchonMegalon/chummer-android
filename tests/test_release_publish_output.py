from __future__ import annotations

import importlib.util
import tempfile
import unittest
from pathlib import Path


REPO = Path(__file__).resolve().parents[1]
SCRIPT = REPO / "scripts" / "verify_release_publish_output.py"
PACKAGE_ID = "com.myexternalbrain.chummer"


def load_module():
    spec = importlib.util.spec_from_file_location("verify_release_publish_output", SCRIPT)
    assert spec is not None and spec.loader is not None
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


class ReleasePublishOutputTests(unittest.TestCase):
    def test_empty_unique_staging_is_required_before_publish(self) -> None:
        module = load_module()
        with tempfile.TemporaryDirectory() as temporary:
            publish = Path(temporary) / "publish"
            publish.mkdir()
            module.require_empty(publish)

            (publish / f"{PACKAGE_ID}-Signed.aab").write_bytes(b"stale")
            with self.assertRaisesRegex(ValueError, "contains preexisting output"):
                module.require_empty(publish)

    def test_exact_new_signed_aab_is_resolved(self) -> None:
        module = load_module()
        with tempfile.TemporaryDirectory() as temporary:
            publish = Path(temporary) / "publish"
            publish.mkdir()
            candidate = publish / f"{PACKAGE_ID}-Signed.aab"
            candidate.write_bytes(b"new")

            self.assertEqual(candidate, module.resolve_exact_signed_aab(publish, PACKAGE_ID))

    def test_multiple_unexpected_or_symlink_candidates_fail_closed(self) -> None:
        module = load_module()
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            publish = root / "publish"
            publish.mkdir()
            expected = publish / f"{PACKAGE_ID}-Signed.aab"
            expected.write_bytes(b"new")
            (publish / "other-Signed.aab").write_bytes(b"other")
            with self.assertRaisesRegex(ValueError, "exactly one signed AAB"):
                module.resolve_exact_signed_aab(publish, PACKAGE_ID)

            (publish / "other-Signed.aab").unlink()
            expected.unlink()
            outside = root / "outside.aab"
            outside.write_bytes(b"stale")
            expected.symlink_to(outside)
            with self.assertRaisesRegex(ValueError, "regular non-symlink"):
                module.resolve_exact_signed_aab(publish, PACKAGE_ID)


if __name__ == "__main__":
    unittest.main()
