from __future__ import annotations

import hashlib
import importlib.util
import os
from pathlib import Path
import stat
import sys
import tempfile
import unittest


SCRIPT = Path(__file__).parents[1] / "scripts" / "stage_android_sdk_runtime.py"
SPEC = importlib.util.spec_from_file_location("stage_android_sdk_runtime", SCRIPT)
assert SPEC and SPEC.loader
MODULE = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = MODULE
SPEC.loader.exec_module(MODULE)


TEST_BYTES = b"runtime-fixture-" * 19
TEST_POLICY = MODULE.StagingPolicy(
    source_suffix=MODULE.SOURCE_SUFFIX,
    destination_directory=MODULE.DESTINATION_DIRECTORY,
    destination_name=MODULE.DESTINATION_NAME,
    size_bytes=len(TEST_BYTES),
    sha256=hashlib.sha256(TEST_BYTES).hexdigest(),
)


class AndroidSdkRuntimeStagingTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.source = self.root / MODULE.SOURCE_SUFFIX
        self.source.parent.mkdir(parents=True)
        self.source.write_bytes(TEST_BYTES)
        os.chmod(self.source, 0o744)
        self.intermediate = self.root / "intermediate"
        self.intermediate.mkdir(mode=0o700)

    def tearDown(self) -> None:
        self.temp.cleanup()

    def destination(self) -> Path:
        return self.intermediate / MODULE.DESTINATION_DIRECTORY / MODULE.DESTINATION_NAME

    def test_actual_constants_are_pinned(self) -> None:
        self.assertEqual(MODULE.SOURCE_SUFFIX.as_posix(),
                         "Microsoft.Android.Runtime.36.android/36.1.69/runtimes/android/lib/net10.0/Mono.Android.Runtime.dll")
        self.assertEqual(MODULE.EXPECTED_SIZE_BYTES, 22368)
        self.assertEqual(MODULE.EXPECTED_SHA256,
                         "7d9c809d92b5527556c69988deaa6a1faadbd1757cb11e9e9f167082e37d550f")

    def test_source_0744_creates_new_0600_output(self) -> None:
        result = MODULE.stage(self.source, self.intermediate, policy=TEST_POLICY)
        output = self.destination()
        self.assertFalse(result["reused"])
        self.assertEqual(output.read_bytes(), TEST_BYTES)
        self.assertEqual(stat.S_IMODE(output.stat().st_mode), 0o600)
        self.assertEqual(output.stat().st_nlink, 1)
        self.assertEqual(stat.S_IMODE(self.source.stat().st_mode), 0o744)

    def test_exact_existing_output_is_reused_without_replacement(self) -> None:
        first = MODULE.stage(self.source, self.intermediate, policy=TEST_POLICY)
        output = self.destination()
        identity = (output.stat().st_dev, output.stat().st_ino, output.stat().st_ctime_ns)
        second = MODULE.stage(self.source, self.intermediate, policy=TEST_POLICY)
        self.assertFalse(first["reused"])
        self.assertTrue(second["reused"])
        self.assertEqual(identity, (output.stat().st_dev, output.stat().st_ino, output.stat().st_ctime_ns))

    def test_wrong_existing_output_is_rejected_without_repair(self) -> None:
        destination_root = self.intermediate / MODULE.DESTINATION_DIRECTORY
        destination_root.mkdir(mode=0o700)
        output = destination_root / MODULE.DESTINATION_NAME
        output.write_bytes(b"wrong")
        os.chmod(output, 0o600)
        with self.assertRaises(ValueError):
            MODULE.stage(self.source, self.intermediate, policy=TEST_POLICY)
        self.assertEqual(output.read_bytes(), b"wrong")

    def test_hardlink_and_symlink_outputs_are_rejected(self) -> None:
        destination_root = self.intermediate / MODULE.DESTINATION_DIRECTORY
        destination_root.mkdir(mode=0o700)
        output = destination_root / MODULE.DESTINATION_NAME
        output.hardlink_to(self.source)
        with self.assertRaises(ValueError):
            MODULE.stage(self.source, self.intermediate, policy=TEST_POLICY)
        output.unlink()
        output.symlink_to(self.source)
        with self.assertRaises(ValueError):
            MODULE.stage(self.source, self.intermediate, policy=TEST_POLICY)

    def test_unknown_destination_bytes_and_source_sidecars_are_rejected(self) -> None:
        destination_root = self.intermediate / MODULE.DESTINATION_DIRECTORY
        destination_root.mkdir(mode=0o700)
        (destination_root / "unexpected.bin").write_bytes(b"x")
        with self.assertRaises(ValueError):
            MODULE.stage(self.source, self.intermediate, policy=TEST_POLICY)
        (destination_root / "unexpected.bin").unlink()
        for sidecar in ("Mono.Android.Runtime.pdb", "Mono.Android.Runtime.mdb",
                        "Mono.Android.Runtime.dll.mdb", "Mono.Android.Runtime.dll.config"):
            self.source.with_name(sidecar).write_bytes(b"sidecar")
            with self.assertRaises(ValueError):
                MODULE.stage(self.source, self.intermediate, policy=TEST_POLICY)
            self.source.with_name(sidecar).unlink()

    def test_source_under_intermediate_is_rejected_before_output(self) -> None:
        nested = self.intermediate / MODULE.SOURCE_SUFFIX
        nested.parent.mkdir(parents=True)
        nested.write_bytes(TEST_BYTES)
        os.chmod(nested, 0o744)
        with self.assertRaises(ValueError):
            MODULE.stage(nested, self.intermediate, policy=TEST_POLICY)
        self.assertFalse((self.intermediate / MODULE.DESTINATION_DIRECTORY).exists())

    def test_pinned_source_bytes_and_size_are_required(self) -> None:
        wrong = self.root / MODULE.SOURCE_SUFFIX
        wrong.write_bytes(b"wrong")
        os.chmod(wrong, 0o744)
        with self.assertRaises(ValueError):
            MODULE.stage(wrong, self.intermediate, policy=TEST_POLICY)
        self.assertFalse((self.intermediate / MODULE.DESTINATION_DIRECTORY).exists())

    def test_partial_copy_failure_preserves_output_for_diagnosis(self) -> None:
        original_fsync = MODULE.os.fsync

        def fail_fsync(_descriptor: int) -> None:
            raise OSError("simulated fsync failure")

        MODULE.os.fsync = fail_fsync
        try:
            with self.assertRaises(OSError):
                MODULE.stage(self.source, self.intermediate, policy=TEST_POLICY)
        finally:
            MODULE.os.fsync = original_fsync
        self.assertTrue(self.destination().is_file())

    def test_intermediate_path_rebind_is_rejected(self) -> None:
        original = MODULE._copy_new

        def rebind(*args, **kwargs):
            original(*args, **kwargs)
            moved = self.root / "moved-intermediate"
            self.intermediate.rename(moved)
            self.intermediate.mkdir(mode=0o700)

        MODULE._copy_new = rebind
        try:
            with self.assertRaises(ValueError):
                MODULE.stage(self.source, self.intermediate, policy=TEST_POLICY)
        finally:
            MODULE._copy_new = original
        self.assertTrue((self.root / "moved-intermediate" / MODULE.DESTINATION_DIRECTORY
                         / MODULE.DESTINATION_NAME).is_file())

    def test_destination_path_rebind_is_rejected(self) -> None:
        original = MODULE._copy_new
        destination_root = self.intermediate / MODULE.DESTINATION_DIRECTORY

        def rebind(*args, **kwargs):
            original(*args, **kwargs)
            moved = self.root / "moved-destination"
            destination_root.rename(moved)
            destination_root.mkdir(mode=0o700)

        MODULE._copy_new = rebind
        try:
            with self.assertRaises(ValueError):
                MODULE.stage(self.source, self.intermediate, policy=TEST_POLICY)
        finally:
            MODULE._copy_new = original
        self.assertTrue((self.root / "moved-destination" / MODULE.DESTINATION_NAME).is_file())

    def test_equal_size_overwrite_is_caught_by_fresh_readback(self) -> None:
        original_fsync = MODULE.os.fsync
        corrupted = False

        def corrupt_after_fsync(descriptor: int) -> None:
            nonlocal corrupted
            original_fsync(descriptor)
            if not corrupted:
                os.pwrite(descriptor, b"X" * len(TEST_BYTES), 0)
                corrupted = True

        MODULE.os.fsync = corrupt_after_fsync
        try:
            with self.assertRaises(ValueError):
                MODULE.stage(self.source, self.intermediate, policy=TEST_POLICY)
        finally:
            MODULE.os.fsync = original_fsync
        self.assertTrue(self.destination().is_file())
        self.assertNotEqual(self.destination().read_bytes(), TEST_BYTES)


if __name__ == "__main__":
    unittest.main()
