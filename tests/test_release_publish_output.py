from __future__ import annotations

import importlib.util
import os
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
    def test_raw_with_optional_sdk_sidecar_selects_only_raw_and_preserves_both(self) -> None:
        module = load_module()
        for sidecar in (False, True):
            with self.subTest(sidecar=sidecar), tempfile.TemporaryDirectory() as temporary:
                publish = Path(temporary)
                raw = publish / f"{PACKAGE_ID}.aab"
                raw.write_bytes(b"raw test bytes; structure checked downstream")
                (publish / "build.pdb").write_bytes(b"ordinary non-AAB output")
                if sidecar:
                    (publish / f"{PACKAGE_ID}-Signed.aab").write_bytes(b"sidecar test bytes; no signer assertion")
                before = {path.name: path.read_bytes() for path in publish.iterdir()}
                self.assertEqual(raw, module.resolve_exact_unsigned_aab(publish, PACKAGE_ID))
                self.assertEqual(before, {path.name: path.read_bytes() for path in publish.iterdir()})

    def test_empty_or_sidecar_only_publish_cannot_supply_raw(self) -> None:
        module = load_module()
        for sidecar in (False, True):
            with self.subTest(sidecar=sidecar), tempfile.TemporaryDirectory() as temporary:
                publish = Path(temporary)
                if sidecar:
                    (publish / f"{PACKAGE_ID}-Signed.aab").write_bytes(b"not raw")
                with self.assertRaisesRegex(ValueError, "missing the exact unsigned"):
                    module.resolve_exact_unsigned_aab(publish, PACKAGE_ID)

    def test_unknown_and_case_aliased_aabs_are_not_ignored(self) -> None:
        module = load_module()
        for name in ("other.aab", "other.AAB", f"{PACKAGE_ID}.AAB", f"{PACKAGE_ID.upper()}.aab",
                     f"{PACKAGE_ID}-signed.aab", f"{PACKAGE_ID}-Signed.AAB", f"{PACKAGE_ID}-Signed.aab.aab"):
            with self.subTest(name=name), tempfile.TemporaryDirectory() as temporary:
                publish = Path(temporary)
                (publish / f"{PACKAGE_ID}.aab").write_bytes(b"raw")
                (publish / name).write_bytes(b"unexpected")
                with self.assertRaisesRegex(ValueError, "identity is unexpected"):
                    module.resolve_exact_unsigned_aab(publish, PACKAGE_ID)

    def test_either_admitted_candidate_must_be_nonempty_regular_single_link(self) -> None:
        module = load_module()
        for suffix in (".aab", "-Signed.aab"):
            for fault in ("empty", "directory", "symlink", "broken-link", "hardlink", "fifo"):
                with self.subTest(suffix=suffix, fault=fault), tempfile.TemporaryDirectory() as temporary:
                    root = Path(temporary)
                    publish = root / "publish"
                    publish.mkdir()
                    (publish / f"{PACKAGE_ID}.aab").write_bytes(b"raw")
                    candidate = publish / f"{PACKAGE_ID}{suffix}"
                    candidate.unlink(missing_ok=True)
                    outside = root / "outside"
                    outside.write_bytes(b"not admitted")
                    if fault == "empty": candidate.touch()
                    elif fault == "directory": candidate.mkdir()
                    elif fault == "symlink": candidate.symlink_to(outside)
                    elif fault == "broken-link": candidate.symlink_to(root / "missing")
                    elif fault == "hardlink": os.link(outside, candidate)
                    else: os.mkfifo(candidate)
                    with self.assertRaisesRegex(ValueError, "nonempty regular single-link"):
                        module.resolve_exact_unsigned_aab(publish, PACKAGE_ID)

    def test_unsigned_selector_rejects_noncanonical_publish_and_package_identity(self) -> None:
        module = load_module()
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            publish = root / "publish"
            publish.mkdir()
            (publish / f"{PACKAGE_ID}.aab").write_bytes(b"raw")
            linked = root / "link"
            linked.symlink_to(publish, target_is_directory=True)
            with self.assertRaisesRegex(ValueError, "regular directory"):
                module.resolve_exact_unsigned_aab(linked, PACKAGE_ID)
            for package in ("", "../other", "com/example", "com.example\n", None):
                with self.subTest(package=package), self.assertRaisesRegex(ValueError, "package identity"):
                    module.resolve_exact_unsigned_aab(publish, package)

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
