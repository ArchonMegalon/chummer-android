from __future__ import annotations

import importlib.util
from pathlib import Path
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[1]
SCRIPT = ROOT / "scripts/run-api36-physical-phone-proof.py"


def load_module():
    spec = importlib.util.spec_from_file_location("run_api36_physical_phone_proof", SCRIPT)
    assert spec is not None and spec.loader is not None
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


class PhysicalPhoneProofRunnerTests(unittest.TestCase):
    def setUp(self) -> None:
        self.module = load_module()

    def test_registry_contains_only_real_provenance_bound_device_drivers(self) -> None:
        self.assertEqual(6, len(self.module.JOURNEYS))
        for driver_name in self.module.JOURNEYS.values():
            source = (ROOT / "tests" / driver_name).read_text(encoding="utf-8")
            self.assertIn("--build-provenance-manifest", source)
            self.assertIn("--allow-destructive-disposable-device", source)
            self.assertIn("force_stop_and_launch_new_process", source)
            self.assertIn('"device-pass-source-bound"', source)

    def test_device_observation_requires_exact_api36_arm64_non_emulator(self) -> None:
        values = {
            ("get-state",): "device",
            ("shell", "getprop", "ro.build.version.sdk"): "36",
            ("shell", "getprop", "ro.product.cpu.abi"): "arm64-v8a",
            ("shell", "getprop", "ro.product.cpu.abilist"): "arm64-v8a,armeabi-v7a",
            ("shell", "getprop", "ro.kernel.qemu"): "0",
            ("shell", "getprop", "ro.hardware"): "tensor",
            ("shell", "getprop", "ro.build.characteristics"): "nosdcard",
            ("shell", "getprop", "ro.product.manufacturer"): "Example",
            ("shell", "getprop", "ro.product.model"): "Phone",
            ("shell", "getprop", "ro.build.fingerprint"): "example/fingerprint",
        }

        def invoke(_adb: Path, _serial: str, *arguments: str) -> str:
            return values[arguments]

        observation = self.module.observe_physical_api36_phone(
            Path("/adb"), "100.96.0.9:44301", invoke
        )
        self.assertEqual(36, observation["apiLevel"])
        self.assertEqual("arm64-v8a", observation["abi"])
        self.assertNotIn("100.96.0.9:44301", str(observation))

        values[("shell", "getprop", "ro.kernel.qemu")] = "1"
        with self.assertRaisesRegex(RuntimeError, "emulator marker"):
            self.module.observe_physical_api36_phone(
                Path("/adb"), "100.96.0.9:44301", invoke
            )

    def test_passing_receipt_must_bind_provenance_apk_and_restart_process(self) -> None:
        provenance = {
            "artifact": {"sha256": "a" * 64},
            "authoritySha256": "b" * 64,
        }
        receipt = {
            "status": self.module.PASS_STATUS,
            "executionStatus": self.module.PASS_EXECUTION_STATUS,
            "releaseEvidenceStatus": self.module.SOURCE_BOUND_STATUS,
            "buildProvenance": provenance,
            "apkSha256": "a" * 64,
            "authorityProofStages": {
                "restartProcessIds": [["101"], ["202"], ["303"]]
            },
        }
        summary = self.module.validate_passing_journey_receipt(receipt, provenance)
        self.assertEqual(3, summary["restartProcessObservationCount"])

        receipt["authorityProofStages"] = {}
        with self.assertRaisesRegex(RuntimeError, "process-restart"):
            self.module.validate_passing_journey_receipt(receipt, provenance)

    def test_output_root_must_be_fresh_absolute_and_outside_sources(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            base = Path(temporary).resolve()
            protected = base / "repo"
            protected.mkdir()
            with self.assertRaisesRegex(RuntimeError, "outside"):
                self.module.prepare_fresh_output_root(
                    protected / "evidence", (protected,)
                )
            external = base / "evidence"
            self.assertEqual(
                external,
                self.module.prepare_fresh_output_root(external, (protected,)),
            )
            with self.assertRaisesRegex(RuntimeError, "stale evidence"):
                self.module.prepare_fresh_output_root(external, (protected,))

    def test_authority_inputs_reject_relative_paths_and_symlinks(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary).resolve()
            authority = root / "authority.json"
            authority.write_text("{}", encoding="utf-8")
            with self.assertRaisesRegex(RuntimeError, "absolute"):
                self.module._require_regular_file(Path("authority.json"), "Authority")
            linked = root / "linked.json"
            linked.symlink_to(authority)
            with self.assertRaisesRegex(RuntimeError, "non-symlink"):
                self.module._require_regular_file(linked, "Authority")


if __name__ == "__main__":
    unittest.main()
