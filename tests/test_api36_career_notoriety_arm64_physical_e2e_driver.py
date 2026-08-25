import unittest

import run_api36_career_notoriety_arm64_physical_e2e as physical


class FakeDevice:
    def __init__(self, responses):
        self.responses = responses
        self.calls = []

    def shell(self, *arguments, timeout=120):
        self.calls.append((arguments, timeout))
        return self.responses[arguments]


class PhysicalArm64DriverTests(unittest.TestCase):
    def test_installed_apk_snapshot_binds_exact_device_bytes(self):
        digest = "a" * 64
        path = "/data/app/isolated-proof/base.apk"
        device = FakeDevice(
            {
                ("pm", "path", physical.PACKAGE): f"package:{path}",
                ("sha256sum", path): f"{digest}  {path}",
            }
        )

        self.assertEqual(
            {
                "package": physical.PACKAGE,
                "path": path,
                "sha256": digest,
            },
            physical.installed_apk_snapshot(device, physical.PACKAGE, digest),
        )
        self.assertEqual(300, device.calls[-1][1])

    def test_installed_apk_snapshot_rejects_digest_mismatch(self):
        expected = "a" * 64
        actual = "b" * 64
        path = "/data/app/isolated-proof/base.apk"
        device = FakeDevice(
            {
                ("pm", "path", physical.PACKAGE): f"package:{path}",
                ("sha256sum", path): f"{actual}  {path}",
            }
        )

        with self.assertRaisesRegex(RuntimeError, "Installed APK digest mismatch"):
            physical.installed_apk_snapshot(device, physical.PACKAGE, expected)

    def test_installed_apk_snapshot_rejects_split_or_unsafe_paths(self):
        digest = "a" * 64
        split_device = FakeDevice(
            {
                ("pm", "path", physical.PACKAGE): (
                    "package:/data/app/proof/base.apk\n"
                    "package:/data/app/proof/split_config.apk"
                )
            }
        )
        with self.assertRaisesRegex(RuntimeError, "exposed 2 base APK paths"):
            physical.installed_apk_snapshot(split_device, physical.PACKAGE, digest)

        unsafe_device = FakeDevice(
            {("pm", "path", physical.PACKAGE): "package:/sdcard/base.apk"}
        )
        with self.assertRaisesRegex(RuntimeError, "Unsafe installed APK path"):
            physical.installed_apk_snapshot(unsafe_device, physical.PACKAGE, digest)


if __name__ == "__main__":
    unittest.main()
