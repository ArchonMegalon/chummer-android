from __future__ import annotations

import copy
import json
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import time
import unittest
from unittest import mock
import zipfile

from scripts import api36_arm64_physical_contract as physical_contract
from tests import api36_physical_build_provenance as provenance
from tests import test_api36_arm64_physical_contract as consumer_contract_tests


REPO_ROOT = Path(__file__).resolve().parents[1]


def write_json(path: Path, payload: object) -> None:
    path.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")


class Api36PhysicalBuildProvenanceTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name).resolve()
        self.core = self.root / "core"
        self.core.mkdir()
        subprocess.run(["git", "init", "-q"], cwd=self.core, check=True)
        subprocess.run(["git", "config", "user.email", "proof@example.invalid"], cwd=self.core, check=True)
        subprocess.run(["git", "config", "user.name", "Proof Test"], cwd=self.core, check=True)
        (self.core / "Chummer/data").mkdir(parents=True)
        (self.core / "Chummer/lang").mkdir(parents=True)
        (self.core / "Chummer/data/lifemodules.xml").write_text("d", encoding="utf-8")
        (self.core / "Chummer/lang/en-us.xml").write_text("l", encoding="utf-8")
        subprocess.run(["git", "add", "."], cwd=self.core, check=True)
        subprocess.run(["git", "commit", "-qm", "content"], cwd=self.core, check=True)
        self.core_commit = subprocess.run(
            ["git", "rev-parse", "HEAD"], cwd=self.core, check=True,
            capture_output=True, text=True,
        ).stdout.strip()

        self.android = self.root / "android"
        self.android.mkdir()
        subprocess.run(["git", "init", "-q"], cwd=self.android, check=True)
        subprocess.run(["git", "config", "user.email", "proof@example.invalid"], cwd=self.android, check=True)
        subprocess.run(["git", "config", "user.name", "Proof Test"], cwd=self.android, check=True)
        (self.android / "eng").mkdir()
        self.package_authority = self.android / "eng/internal-phone-beta-package-authority.json"
        write_json(self.package_authority, self.package_authority_payload())
        (self.android / "scripts").mkdir()
        self.content_manifest = self.android / "src/Chummer.Android/Content/chummer-content-manifest.json"
        self.content_manifest.parent.mkdir(parents=True)
        write_json(self.content_manifest, {
            "schema": provenance.CONTENT_CONTRACT,
            "coreRevision": self.core_commit,
            "bundleDigest": "a" * 64,
            "files": [
                {"path": "data/lifemodules.xml", "size": 1, "sha256": "b" * 64},
                {"path": "lang/en-us.xml", "size": 1, "sha256": "c" * 64},
            ],
        })
        (self.android / "product.txt").write_text("reviewed product source\n", encoding="utf-8")
        subprocess.run(["git", "add", "."], cwd=self.android, check=True)
        subprocess.run(["git", "commit", "-qm", "authority source"], cwd=self.android, check=True)
        (self.android / "tests").mkdir()
        (self.android / "tests/api36_physical_build_provenance.py").write_text("build plane\n", encoding="utf-8")
        self.lock = self.android / "src/Chummer.Android/packages.lock.json"
        self.lock.parent.mkdir(parents=True, exist_ok=True)
        write_json(self.lock, self.lock_payload())
        subprocess.run(["git", "add", "."], cwd=self.android, check=True)
        subprocess.run(["git", "commit", "-qm", "build plane"], cwd=self.android, check=True)
        self.android_commit = self.git("rev-parse", "HEAD")
        self.android_tree = self.git("rev-parse", "HEAD^{tree}")

        self.presentation = self.root / "presentation"
        self.presentation.mkdir()
        subprocess.run(["git", "init", "-q"], cwd=self.presentation, check=True)
        subprocess.run(["git", "config", "user.email", "proof@example.invalid"], cwd=self.presentation, check=True)
        subprocess.run(["git", "config", "user.name", "Proof Test"], cwd=self.presentation, check=True)
        (self.presentation / "source.txt").write_text("current UI source\n", encoding="utf-8")
        (self.presentation / "config").mkdir()
        self.presentation_lock = self.presentation / "config/package-plane.lock.json"
        self.producer_lock = self.presentation / "config/ui-owner-package-plane.lock.json"
        self.presentation_lock.write_text('{"version":1}\n', encoding="utf-8")
        self.producer_lock.write_text('{"version":1}\n', encoding="utf-8")
        subprocess.run(["git", "add", "."], cwd=self.presentation, check=True)
        subprocess.run(["git", "commit", "-qm", "current UI source"], cwd=self.presentation, check=True)
        self.presentation_commit = subprocess.run(
            ["git", "rev-parse", "HEAD"], cwd=self.presentation, check=True,
            capture_output=True, text=True,
        ).stdout.strip()
        self.presentation_tree = subprocess.run(
            ["git", "rev-parse", "HEAD^{tree}"], cwd=self.presentation, check=True,
            capture_output=True, text=True,
        ).stdout.strip()
        write_json(self.package_authority, self.package_authority_payload())
        subprocess.run(["git", "add", "."], cwd=self.android, check=True)
        subprocess.run(["git", "commit", "-qm", "bind current package authority"], cwd=self.android, check=True)
        self.android_commit = self.git("rev-parse", "HEAD")
        self.android_tree = self.git("rev-parse", "HEAD^{tree}")

        self.ui_authority_receipt = self.root / "ui-authority-receipt.json"
        write_json(self.ui_authority_receipt, self.ui_authority_receipt_payload())
        self.package_cache = self.root / "package-cache"
        self.package_feed = self.package_cache / "packages"
        self.offline_feed = self.root / "offline-feed"
        self.nuget_packages = self.root / "nuget-packages"
        for directory in (
            self.package_feed, self.offline_feed, self.nuget_packages,
        ):
            directory.mkdir(parents=True)
        self.package_cache_manifest = self.package_cache / "owner-package-cache.json"
        write_json(self.package_cache_manifest, {"contract": "fixture", "packages": []})
        self.content_source = self.root / "content-source.json"
        write_json(self.content_source, self.content_payload(apk=False))
        self.apk = self.root / "com.myexternalbrain.chummer-Signed.apk"
        with zipfile.ZipFile(self.apk, "w") as archive:
            archive.writestr("lib/arm64-v8a/libmonodroid.so", b"arm64")
            archive.writestr("assets/chummer-content/data/lifemodules.xml", b"content")
        self.content_apk = self.root / "content-apk.json"
        write_json(self.content_apk, self.content_payload(apk=True))
        self.assets = self.root / "project.assets.json"
        write_json(self.assets, self.assets_payload())
        self.sdk_root = self.root / "android-sdk"
        self.platform_root = self.sdk_root / "platforms/android-36"
        self.build_tools_root = self.sdk_root / "build-tools/36.0.0"
        self.platform_tools_root = self.sdk_root / "platform-tools"
        for directory in (self.platform_root, self.build_tools_root / "lib", self.platform_tools_root):
            directory.mkdir(parents=True)
        self.sdk_packages = self.root / "selected-packages.xml"
        self.sdk_packages.write_text(self.sdk_inventory_xml(), encoding="utf-8")
        (self.platform_root / "package.xml").write_text(
            self.sdk_package_xml("platforms;android-36", 2, 0, 0), encoding="utf-8",
        )
        (self.build_tools_root / "package.xml").write_text(
            self.sdk_package_xml("build-tools;36.0.0", 36, 0, 0), encoding="utf-8",
        )
        (self.platform_tools_root / "package.xml").write_text(
            self.sdk_package_xml("platform-tools", 36, 0, 0), encoding="utf-8",
        )
        (self.platform_root / "android.jar").write_bytes(b"fixture-android-36")
        (self.build_tools_root / "lib/apksigner.jar").write_bytes(b"fixture-apksigner-jar")
        self.workloads = self.root / "dotnet-workloads.json"
        write_json(self.workloads, {
            "installed": ["maui-android"], "updateAvailable": [],
            "workloadSetVersion": provenance.WORKLOAD_SET_VERSION,
            "manifestVersions": {
                "maui-android": provenance.MAUI_ANDROID_MANIFEST_VERSION,
                "microsoft.net.sdk.android": provenance.ANDROID_WORKLOAD_MANIFEST_VERSION,
            },
            "runtimeVersion": provenance.DOTNET_RUNTIME_VERSION,
        })
        self.manifest_root = self.root / "sdk-manifests/10.0.100"
        self.android_workload_manifest = (
            self.manifest_root / "microsoft.net.sdk.android/36.1.69/WorkloadManifest.json"
        )
        self.maui_workload_manifest = (
            self.manifest_root / "microsoft.net.sdk.maui/10.0.20/WorkloadManifest.json"
        )
        for path, version in (
            (self.android_workload_manifest, "36.1.69"),
            (self.maui_workload_manifest, "10.0.20"),
        ):
            path.parent.mkdir(parents=True)
            write_json(path, {"version": version, "packs": {}})
        self.jdk_root = self.root / "microsoft-jdk"
        self.bin = self.jdk_root / "bin"
        self.bin.mkdir(parents=True)
        self.java = self.bin / "java"
        self.javac = self.bin / "javac"
        self.jarsigner = self.bin / "jarsigner"
        self.keytool = self.bin / "keytool"
        self.dotnet_root = self.root / "dotnet"
        self.dotnet_root.mkdir()
        self.dotnet = self.dotnet_root / "dotnet"
        self.python = self.bin / "python3"
        native_fixture = Path("/usr/bin/true")
        for executable in (
            self.java, self.javac, self.jarsigner, self.keytool, self.dotnet, self.python,
            self.build_tools_root / "aapt2", self.build_tools_root / "zipalign",
            self.platform_tools_root / "adb",
        ):
            shutil.copyfile(native_fixture, executable)
            executable.chmod(0o755)
        self.apksigner = self.build_tools_root / "apksigner"
        self.apksigner.write_text("#!/bin/sh\nexit 0\n", encoding="utf-8")
        self.apksigner.chmod(0o755)
        (self.jdk_root / "release").write_text(
            'IMPLEMENTOR="Microsoft"\n'
            'IMPLEMENTOR_VERSION="Microsoft-10800290"\n'
            'JAVA_RUNTIME_VERSION="17.0.14+7-LTS"\n'
            'JAVA_VERSION="17.0.14"\n'
            'JAVA_VERSION_DATE="2025-01-21"\n'
            'LIBC="gnu"\n'
            'MODULES="java.base"\n'
            'OS_ARCH="x86_64"\n'
            'OS_NAME="Linux"\n'
            'SOURCE=".:git:fixture"\n',
            encoding="utf-8",
        )
        self.toolchain_log = self.root / "toolchain.log"
        self.toolchain_log.write_text("dotnet_workload_inventory=pass\n", encoding="utf-8")
        self.restore_log = self.root / "restore.log"
        self.restore_log.write_text("All projects are up-to-date for restore.\n", encoding="utf-8")
        self.build_log = self.root / "build.log"
        self.build_log.write_text(
            "Build succeeded.\n    0 Warning(s)\n    0 Error(s)\n", encoding="utf-8",
        )
        self.signing_phase_log = self.root / "signing-phase.log"
        self.signing_phase_log.write_text("apk_signing_verification=pass\n", encoding="utf-8")
        self.apksigner_log = self.root / "apksigner.log"
        self.certificate_sha256 = "d" * 64
        self.apksigner_log.write_text(
            "Verified using v2 scheme (APK Signature Scheme v2): true\n"
            f"Signer #1 certificate SHA-256 digest: {self.certificate_sha256}\n",
            encoding="utf-8",
        )
        self.jarsigner_log = self.root / "jarsigner.log"
        self.jarsigner_log.write_text("jar verified.\n", encoding="utf-8")
        self.signing_receipt = self.root / "signing-receipt.json"
        write_json(self.signing_receipt, self.signing_receipt_payload())
        self.package_authority_log = self.root / "package-authority.log"
        self.package_authority_log.write_text("package authority exact\n", encoding="utf-8")
        self.package_authority_binding = self.root / "package-authority-binding.json"
        self.package_authority_binding.write_bytes(self.package_authority.read_bytes())
        self.content_source_log = self.root / "content-source.log"
        self.content_source_log.write_text("content source exact\n", encoding="utf-8")
        self.build_inputs_log = self.root / "build-inputs.log"
        self.build_inputs_log.write_text("build inputs exact\n", encoding="utf-8")
        self.content_apk_log = self.root / "content-apk.log"
        self.content_apk_log.write_text("APK content exact\n", encoding="utf-8")
        self.package_authority_seal_log = self.root / "package-authority-seal.log"
        self.package_authority_seal_log.write_text("package authority remained exact\n", encoding="utf-8")
        self.package_authority_seal = self.root / "package-authority-seal.json"
        self.package_authority_seal.write_bytes(self.package_authority.read_bytes())
        self.journal = self.root / "journal.jsonl"
        self.raw_journal = self.root / "raw-journal.jsonl"
        self.delegate_journal = self.root / "delegate-journal.jsonl"
        self.write_journal()
        self.manifest = self.root / "provenance.json"

        self.toolchain_authority = {
            "dotnet": provenance.file_sha256(self.dotnet),
            "jdk_release": provenance.file_sha256(self.jdk_root / "release"),
            "java": provenance.file_sha256(self.java),
            "javac": provenance.file_sha256(self.javac),
            "jarsigner": provenance.file_sha256(self.jarsigner),
            "keytool": provenance.file_sha256(self.keytool),
            "platform_package": provenance.file_sha256(self.platform_root / "package.xml"),
            "android_jar": provenance.file_sha256(self.platform_root / "android.jar"),
            "build_tools_package": provenance.file_sha256(self.build_tools_root / "package.xml"),
            "apksigner": provenance.file_sha256(self.apksigner),
            "apksigner_jar": provenance.file_sha256(self.build_tools_root / "lib/apksigner.jar"),
            "aapt2": provenance.file_sha256(self.build_tools_root / "aapt2"),
            "zipalign": provenance.file_sha256(self.build_tools_root / "zipalign"),
            "platform_tools_package": provenance.file_sha256(self.platform_tools_root / "package.xml"),
            "adb": provenance.file_sha256(self.platform_tools_root / "adb"),
            "android_workload_manifest": provenance.file_sha256(self.android_workload_manifest),
            "maui_workload_manifest": provenance.file_sha256(self.maui_workload_manifest),
        }

        self.patches = [
            mock.patch.object(provenance, "UI_AUTHORITY_RECEIPT_SHA256", provenance.file_sha256(self.ui_authority_receipt)),
            mock.patch.object(provenance, "UI_AUTHORITY_RECEIPT_SIZE", self.ui_authority_receipt.stat().st_size),
            mock.patch.object(provenance, "PACKAGE_AUTHORITY_SHA256", provenance.file_sha256(self.package_authority)),
            mock.patch.object(provenance, "PACKAGE_CACHE_MANIFEST_SHA256", provenance.file_sha256(self.package_cache_manifest)),
            mock.patch.object(provenance, "PRESENTATION_COMMIT", self.presentation_commit),
            mock.patch.object(provenance, "PRESENTATION_TREE", self.presentation_tree),
            mock.patch.object(provenance, "PRESENTATION_PACKAGE_LOCK_SHA256", provenance.file_sha256(self.presentation_lock)),
            mock.patch.object(provenance, "PRESENTATION_PRODUCER_LOCK_SHA256", provenance.file_sha256(self.producer_lock)),
            mock.patch.object(provenance, "FULL_PROJECT_LOCK_SHA256", provenance.file_sha256(self.lock)),
            mock.patch.object(provenance, "FULL_PROJECT_LOCK_SIZE", self.lock.stat().st_size),
            mock.patch.object(provenance, "CORE_CONTENT_REVISION", self.core_commit),
            mock.patch.object(provenance, "CORE_CONTENT_DIGEST", "a" * 64),
            mock.patch.object(provenance, "ANDROID_SDK_ROOT_AUTHORITY", self.sdk_root),
            mock.patch.object(provenance, "JDK_ROOT_AUTHORITY", self.jdk_root),
            mock.patch.object(provenance, "DOTNET_HOST_AUTHORITY", self.dotnet),
            mock.patch.object(provenance, "DOTNET_CLI_HOME_AUTHORITY", self.root),
            mock.patch.object(
                provenance, "ANDROID_WORKLOAD_MANIFEST_AUTHORITY", self.android_workload_manifest,
            ),
            mock.patch.object(
                provenance, "MAUI_WORKLOAD_MANIFEST_AUTHORITY", self.maui_workload_manifest,
            ),
            mock.patch.object(provenance, "TOOLCHAIN_SHA256_AUTHORITY", self.toolchain_authority),
            mock.patch.object(physical_contract, "TRUSTED_ANDROID_SDK_ROOT", str(self.sdk_root)),
            mock.patch.object(physical_contract, "TRUSTED_TOOLCHAIN_SHA256", self.toolchain_authority),
            mock.patch.object(
                provenance, "_probe_version",
                lambda path, _label: (
                    'openjdk version "17.0.14" 2025-01-21 LTS'
                    if Path(path).name == "java" else "javac 17.0.14"
                ),
            ),
        ]
        for patcher in self.patches:
            patcher.start()

    def tearDown(self) -> None:
        for patcher in reversed(self.patches):
            patcher.stop()
        self.temporary.cleanup()

    def git(self, *arguments: str) -> str:
        return subprocess.run(
            ["git", *arguments], cwd=self.android, check=True,
            capture_output=True, text=True,
        ).stdout.strip()

    @staticmethod
    def verified_package_authority(*_args: Path) -> dict[str, object]:
        return {"status": "pass", "contractName": provenance.PACKAGE_AUTHORITY_CONTRACT}

    @staticmethod
    def verified_content(_android: Path, _core: Path) -> list[str]:
        return []

    @staticmethod
    def sdk_package_xml(package_id: str, major: int, minor: int, micro: int) -> str:
        return (
            '<repository><localPackage path="' + package_id + '"><revision>'
            f"<major>{major}</major><minor>{minor}</minor><micro>{micro}</micro>"
            "</revision></localPackage></repository>\n"
        )

    def sdk_inventory_xml(self) -> str:
        return (
            "<repository>"
            '<localPackage path="platforms;android-36"><revision><major>2</major><minor>0</minor><micro>0</micro></revision></localPackage>'
            '<localPackage path="build-tools;36.0.0"><revision><major>36</major><minor>0</minor><micro>0</micro></revision></localPackage>'
            '<localPackage path="platform-tools"><revision><major>36</major><minor>0</minor><micro>0</micro></revision></localPackage>'
            "</repository>\n"
        )

    def signing_receipt_payload(self) -> dict[str, object]:
        return {
            "contractName": "chummer.android.apk-signing-verification/v1",
            "status": "pass", "apkSha256": provenance.file_sha256(self.apk),
            "certificateSha256": self.certificate_sha256, "verifiedSchemes": [2],
            "apksignerSha256": provenance.file_sha256(self.apksigner),
            "jarsignerSha256": provenance.file_sha256(self.jarsigner),
            "apksignerOutputSha256": provenance.file_sha256(self.apksigner_log),
            "jarsignerOutputSha256": provenance.file_sha256(self.jarsigner_log),
            "warningsAsErrors": True, "publicationAuthorized": False,
        }

    def package_authority_payload(self) -> dict[str, object]:
        presentation_commit = getattr(self, "presentation_commit", provenance.PRESENTATION_COMMIT)
        presentation_tree = getattr(self, "presentation_tree", provenance.PRESENTATION_TREE)
        core_recipe_commit = getattr(self, "core_commit", provenance.CORE_CONTENT_REVISION)
        return {
            "contractName": provenance.PACKAGE_AUTHORITY_CONTRACT,
            "authorityClass": "internal_phone_beta_only",
            "authorityState": "current_graph_verified",
            "publicationAuthorized": False,
            "presentationSource": {
                "commit": presentation_commit,
                "tree": presentation_tree,
                "repository": provenance.PRESENTATION_REPOSITORY,
            },
            "packagePlaneLock": {},
            "verificationReceipt": {},
            "artifactCache": {},
            "sourceGraph": {
                "corePackageRecipeCommit": core_recipe_commit,
                "coreRuntimeSourceCommit": provenance.CORE_RUNTIME_REVISION,
                "hubProducerCommit": provenance.HUB_REVISION,
                "registryCommit": provenance.REGISTRY_REVISION,
                "uiKitCommit": provenance.UI_KIT_REVISION,
            },
            "dependencyMode": {
                "packageOnly": True, "restoreLockedMode": True,
                "sourceCheckoutsPresent": False, "siblingsAllowed": False,
            },
            "sdkAuthority": {},
            "headlessRuntimeBinding": {},
            "androidConsumerLocks": [],
            "doesNotAssert": [],
        }

    def ui_authority_receipt_payload(self) -> dict[str, object]:
        return {
            "contractName": provenance.UI_AUTHORITY_RECEIPT_CONTRACT,
            "contractVersion": 11,
            "status": "passed",
            "consumerCommit": self.presentation_commit,
        }

    def lock_payload(self) -> dict[str, object]:
        direct = {
            "Microsoft.Maui.Controls": "10.0.20",
            "Microsoft.Extensions.Logging.Debug": "10.0.0",
            "Xamarin.Google.Android.Play.App.Update": "2.1.0.19",
            "Xamarin.Google.Android.Play.Review": "2.0.2.9",
            "Xamarin.AndroidX.Activity.Ktx": "1.13.0.1",
            "Xamarin.AndroidX.Collection.Ktx": "1.6.0.1",
            "Xamarin.AndroidX.Fragment.Ktx": "1.8.9.4",
            "Xamarin.AndroidX.Lifecycle.LiveData": "2.11.0.1",
            "Xamarin.AndroidX.Lifecycle.LiveData.Core.Ktx": "2.11.0.1",
            "Xamarin.AndroidX.Lifecycle.Process": "2.11.0.1",
            "Xamarin.AndroidX.Lifecycle.Runtime.Ktx": "2.11.0.1",
            "Xamarin.AndroidX.Lifecycle.Runtime.Ktx.Android": "2.11.0.1",
            "Xamarin.AndroidX.Lifecycle.ViewModel.Ktx": "2.11.0.1",
            "Xamarin.AndroidX.SavedState.SavedState.Ktx": "1.5.0.1",
        }
        packages: dict[str, object] = {
            package_id: {
                "type": "Direct", "requested": f"[{version}, )", "resolved": version,
                "contentHash": "sha512-fixture",
            }
            for package_id, version in direct.items()
        }
        packages.update({
            package_id: {"type": "Transitive", "resolved": version, "contentHash": "sha512-fixture"}
            for package_id, version in provenance.EXPECTED_PACKAGE_VERSIONS.items()
        })
        return {
            "version": 1,
            "dependencies": {
                provenance.TARGET_FRAMEWORK: packages,
                f"{provenance.TARGET_FRAMEWORK}/{provenance.RUNTIME_IDENTIFIER}": {},
            },
        }

    def content_payload(self, *, apk: bool) -> dict[str, object]:
        return {
            "status": "pass", "schema": provenance.CONTENT_CONTRACT,
            "coreRevision": self.core_commit,
            "bundleDigest": "a" * 64,
            "manifestSha256": provenance.file_sha256(self.content_manifest),
            "apkSha256": provenance.file_sha256(self.apk) if apk and self.apk.exists() else None,
            "canonicalFileCount": 2, "canonicalByteCount": 2,
            "apkCanonicalFileCount": 2 if apk else 0,
            "apkVerified": apk, "issues": [],
        }

    def assets_payload(self) -> dict[str, object]:
        libraries = {
            "Chummer.Desktop.Runtime/1.0.0": {"type": "project"},
            "Chummer.Presentation/1.0.0": {"type": "project"},
        }
        for package_id, version in provenance.EXPECTED_PACKAGE_VERSIONS.items():
            libraries[f"{package_id}/{version}"] = {"type": "package"}
        return {"version": 3, "libraries": libraries}

    def write_journal(self) -> None:
        phases = (
            ("toolchain-intake", self.toolchain_log),
            ("package-authority-intake", self.package_authority_log),
            ("core-content-intake", self.content_source_log),
            ("current-build-input-intake", self.build_inputs_log),
            ("locked-full-restore", self.restore_log),
            ("serialized-full-maui-build", self.build_log),
            ("apk-signature-verification", self.signing_phase_log),
            ("apk-content-verification", self.content_apk_log),
            ("post-build-package-authority-seal", self.package_authority_seal_log),
        )
        project = str(self.android / "src/Chummer.Android/Chummer.Android.csproj")
        materializer = str(self.android / "scripts/materialize-api36-physical-build-provenance.py")
        package_authority_verifier = str(self.android / "scripts/verify_internal_phone_beta_package_authority.py")
        content_verifier = str(self.android / "scripts/verify_android_content_bundle.py")
        content_manifest = str(self.content_manifest)
        package_args = [
            f"-p:ChummerPresentationRoot={self.presentation}",
            f"-p:ChummerCoreEngineRoot={self.core}",
            "-p:ChummerDesktopRuntimeIdentifiers=",
            "-p:ChummerUseLocalCompatibilityTree=false",
            "-p:ChummerUseLockedOwnerContractPackages=true",
            "-p:RestoreLockedMode=true", "-p:RestorePackagesWithLockFile=true",
            "-p:NuGetAudit=false", f"-p:AndroidSdkDirectory={self.sdk_root}",
            "-p:AndroidSdkBuildToolsVersion=36.0.0",
            f"-p:JavaSdkDirectory={self.jdk_root}",
            f"-p:ChummerContractsPackageVersion={provenance.CORE_PACKAGE_VERSION}",
            f"-p:ChummerCoreRuntimePackageVersion={provenance.CORE_PACKAGE_VERSION}",
            "-p:ChummerCampaignContractsPackageVersion=0.1.0-preview",
            f"-p:ChummerRunContractsPackageVersion={provenance.HUB_PACKAGE_VERSION}",
            f"-p:ChummerHubRegistryContractsPackageVersion={provenance.HUB_PACKAGE_VERSION}",
            "-p:ChummerUiKitPackageVersion=0.1.0-preview",
        ]
        commands = {
            "toolchain-intake": [
                str(self.python), materializer, "capture-workloads", "--dotnet",
                str(self.dotnet), "--android-workload-manifest",
                str(self.android_workload_manifest), "--maui-workload-manifest",
                str(self.maui_workload_manifest), "--android-sdk-packages", str(self.sdk_packages),
                "--android-sdk-root", str(self.sdk_root), "--java", str(self.java),
                "--javac", str(self.javac), "--jarsigner", str(self.jarsigner),
                "--apksigner", str(self.apksigner), "--output", str(self.workloads),
            ],
            "package-authority-intake": [
                str(self.python), package_authority_verifier,
                "--android-root", str(self.android),
                "--presentation-root", str(self.presentation),
                "--receipt", str(self.ui_authority_receipt),
                "--package-feed", str(self.package_feed),
                "--output", str(self.package_authority_binding),
            ],
            "core-content-intake": [
                str(self.python), content_verifier, "--repo-root", str(self.android),
                "--core-root", str(self.core), "--manifest", content_manifest,
                "--receipt", str(self.content_source), "--check",
            ],
            "current-build-input-intake": [
                str(self.python), materializer, "check-inputs", "--android-root", str(self.android),
                "--presentation-root", str(self.presentation), "--core-content-root", str(self.core),
                "--ui-authority-receipt", str(self.ui_authority_receipt),
                "--package-feed", str(self.package_feed),
                "--package-authority", str(self.package_authority),
                "--content-source-receipt", str(self.content_source), "--full-project-lock", str(self.lock),
            ],
            "locked-full-restore": [
                str(self.dotnet), "restore", project, "--locked-mode", "--disable-parallel",
                "--no-http-cache", "--packages", str(self.nuget_packages),
                "--source", str(self.package_feed), "--source", str(self.offline_feed), *package_args,
            ],
            "serialized-full-maui-build": [
                str(self.dotnet), "build", project, "--configuration", provenance.CONFIGURATION,
                "--framework", provenance.TARGET_FRAMEWORK,
                "--runtime", provenance.RUNTIME_IDENTIFIER, "--no-restore", "--warnaserror",
                "-m:1", "-nr:false", "--disable-build-servers",
                "-p:UseSharedCompilation=false", "-p:BuildInParallel=false",
                "-p:AndroidPackageFormats=apk", *package_args,
            ],
            "apk-signature-verification": [
                str(self.python), materializer, "verify-apk-signing", "--apk", str(self.apk),
                "--apksigner", str(self.apksigner), "--jarsigner", str(self.jarsigner),
                "--receipt", str(self.signing_receipt), "--apksigner-log",
                str(self.apksigner_log), "--jarsigner-log", str(self.jarsigner_log),
            ],
            "apk-content-verification": [
                str(self.python), content_verifier, "--repo-root", str(self.android),
                "--core-root", str(self.core), "--manifest", content_manifest,
                "--apk", str(self.apk), "--receipt", str(self.content_apk), "--check",
            ],
            "post-build-package-authority-seal": [
                str(self.python), package_authority_verifier,
                "--android-root", str(self.android),
                "--presentation-root", str(self.presentation),
                "--receipt", str(self.ui_authority_receipt),
                "--package-feed", str(self.package_feed),
                "--output", str(self.package_authority_seal),
            ],
        }
        environment = {
            "ANDROID_HOME": str(self.sdk_root), "ANDROID_SDK_ROOT": str(self.sdk_root),
            "DOTNET_CLI_HOME": str(self.root), "DOTNET_ROOT": str(self.dotnet.parent),
            "HOME": str(self.root), "JAVA_HOME": str(self.jdk_root),
            "NUGET_PACKAGES": str(self.nuget_packages),
            "DOTNET_CLI_TELEMETRY_OPTOUT": "1", "DOTNET_CLI_USE_MSBUILD_SERVER": "0",
            "MSBUILDDISABLENODEREUSE": "1", "LANG": "C.UTF-8", "LC_ALL": "C.UTF-8",
            "PATH": f"{self.bin}:{self.dotnet.parent}:/usr/bin:/bin", "TMPDIR": "/tmp",
        }
        environment.update({
            "CHUMMER_ANDROID_REVISION": self.android_commit,
            "CHUMMER_PRESENTATION_REVISION": self.presentation_commit,
            "CHUMMER_CORE_ENGINE_REVISION": provenance.CORE_RUNTIME_REVISION,
            "CHUMMER_UI_KIT_REVISION": provenance.UI_KIT_REVISION,
            "CHUMMER_RUN_SERVICES_REVISION": provenance.HUB_REVISION,
            "CHUMMER_HUB_REGISTRY_REVISION": provenance.REGISTRY_REVISION,
        })
        rows = []
        raw_rows = []
        delegate_rows = []
        invocation_epoch = 1000.0
        deadline_epoch = invocation_epoch + provenance.TOTAL_DEADLINE_SECONDS
        for phase, output in phases:
            common = {
                "contractName": provenance.COMMAND_JOURNAL_CONTRACT,
                "phase": phase, "argv": commands[phase],
                "workingDirectory": str(self.android), "environment": environment,
                "timeoutSeconds": provenance.PER_PHASE_TIMEOUT_SECONDS,
                "deadlineEpoch": deadline_epoch, "startedEpoch": 1001.0,
                "invocationStartedEpoch": invocation_epoch,
                "totalDeadlineSeconds": provenance.TOTAL_DEADLINE_SECONDS,
                "outputPath": str(output),
                "processGroupTermination": True, "publicationAuthorized": False,
            }
            rows.extend([
                {**common, "event": "started"},
                {
                    **common, "event": "finished", "exitCode": 0,
                    "outputSha256": provenance.file_sha256(output),
                    "timedOut": False, "elapsedSeconds": 1.0,
                    "termination": {
                        "groupAbsent": True, "sigtermSent": False, "sigkillSent": False,
                    },
                },
            ])
            raw_rows.extend([
                {
                    "contractName": provenance.RAW_COMMAND_JOURNAL_CONTRACT,
                    "phase": phase, "event": "started", "command": commands[phase],
                    "timeoutSeconds": provenance.PER_PHASE_TIMEOUT_SECONDS,
                    "deadlineEpoch": deadline_epoch,
                    "invocationStartedEpoch": invocation_epoch,
                    "totalDeadlineSeconds": provenance.TOTAL_DEADLINE_SECONDS,
                    "processGroupTermination": True,
                    "publicationAuthorized": False,
                },
                {
                    "contractName": provenance.RAW_COMMAND_JOURNAL_CONTRACT,
                    "phase": phase, "event": "finished", "command": commands[phase],
                    "exitCode": 0,
                    "timedOut": False, "elapsedSeconds": 1.0,
                    "timeoutSeconds": provenance.PER_PHASE_TIMEOUT_SECONDS,
                    "deadlineEpoch": deadline_epoch,
                    "invocationStartedEpoch": invocation_epoch,
                    "totalDeadlineSeconds": provenance.TOTAL_DEADLINE_SECONDS,
                    "outputSha256": provenance.file_sha256(output),
                    "termination": {
                        "groupAbsent": True, "sigtermSent": False, "sigkillSent": False,
                    },
                    "processGroupTermination": True, "publicationAuthorized": False,
                },
            ])
            delegate_rows.extend([
                {
                    "contractName": provenance.DELEGATE_COMMAND_JOURNAL_CONTRACT,
                    "phase": phase, "event": "started", "command": commands[phase],
                    "timeoutSeconds": provenance.PER_PHASE_TIMEOUT_SECONDS,
                    "processGroupTermination": True, "publicationAuthorized": False,
                },
                {
                    "contractName": provenance.DELEGATE_COMMAND_JOURNAL_CONTRACT,
                    "phase": phase, "event": "finished", "exitCode": 0,
                    "timedOut": False, "elapsedSeconds": 1.0,
                    "outputSha256": provenance.file_sha256(output),
                    "termination": {
                        "groupAbsent": True, "sigtermSent": False, "sigkillSent": False,
                    },
                    "processGroupTermination": True, "publicationAuthorized": False,
                },
            ])
        self.journal.write_text(
            "".join(json.dumps(row) + "\n" for row in rows), encoding="utf-8",
        )
        self.raw_journal.write_text(
            "".join(json.dumps(row) + "\n" for row in raw_rows), encoding="utf-8",
        )
        self.delegate_journal.write_text(
            "".join(json.dumps(row) + "\n" for row in delegate_rows), encoding="utf-8",
        )

    def create_arguments(self) -> dict[str, object]:
        return {
            "android_root": self.android,
            "presentation_root": self.presentation,
            "core_content_root": self.core,
            "apk": self.apk,
            "ui_authority_receipt_path": self.ui_authority_receipt,
            "package_authority_path": self.package_authority,
            "content_source_receipt_path": self.content_source,
            "content_apk_receipt_path": self.content_apk,
            "full_project_lock_path": self.lock,
            "assets_path": self.assets,
            "toolchain_log_path": self.toolchain_log,
            "package_authority_log_path": self.package_authority_log,
            "package_authority_binding_path": self.package_authority_binding,
            "content_source_log_path": self.content_source_log,
            "build_inputs_log_path": self.build_inputs_log,
            "restore_log_path": self.restore_log,
            "build_log_path": self.build_log,
            "signing_phase_log_path": self.signing_phase_log,
            "apksigner_log_path": self.apksigner_log,
            "jarsigner_log_path": self.jarsigner_log,
            "signing_receipt_path": self.signing_receipt,
            "content_apk_log_path": self.content_apk_log,
            "package_authority_seal_log_path": self.package_authority_seal_log,
            "package_authority_seal_path": self.package_authority_seal,
            "command_journal_path": self.journal,
            "raw_command_journal_path": self.raw_journal,
            "delegate_command_journal_path": self.delegate_journal,
            "android_sdk_packages_path": self.sdk_packages,
            "android_sdk_root": self.sdk_root,
            "android_workload_manifest_path": self.android_workload_manifest,
            "maui_workload_manifest_path": self.maui_workload_manifest,
            "dotnet_workloads_path": self.workloads,
            "java_path": self.java, "javac_path": self.javac,
            "jarsigner_path": self.jarsigner, "apksigner_path": self.apksigner,
            "dotnet_path": self.dotnet, "python_path": self.python,
            "package_feed_path": self.package_feed,
            "offline_feed_path": self.offline_feed,
            "nuget_packages_path": self.nuget_packages,
            "android_build_tools_version": "36.0.0",
            "dotnet_version": provenance.DOTNET_SDK_VERSION,
            "package_authority_verifier": self.verified_package_authority,
            "content_verifier": self.verified_content,
            "remote_reachability_verifier": lambda *_: None,
        }

    @staticmethod
    def consumer_source_graph(manifest: dict[str, object]) -> dict[str, object]:
        graph = consumer_contract_tests.Api36Arm64PhysicalContractTests.graph_payload()
        repositories = {
            row["name"]: row for row in graph["repositories"]
        }
        source_head = manifest["sourceHead"]
        presentation = manifest["presentationBuildSource"]
        package_source = manifest["packageAuthority"]["sourceGraph"]
        content_source = manifest["content"]["sourceRepository"]
        repositories["chummer-android"].update({
            "commit": source_head["commit"], "tree": source_head["tree"],
        })
        repositories["chummer6-ui"].update({
            "commit": presentation["commit"], "tree": presentation["tree"],
        })
        repositories["chummer6-core"].update({
            "commit": package_source["coreRuntimeSourceCommit"],
            "tree": content_source["tree"],
        })
        repositories["chummer6-hub"]["commit"] = package_source["hubProducerCommit"]
        repositories["chummer6-hub-registry"]["commit"] = package_source["registryCommit"]
        repositories["chummer6-ui-kit"]["commit"] = package_source["uiKitCommit"]
        for row in graph["packagePins"]:
            row["commit"] = repositories["chummer6-core"]["commit"]
        for row in graph["ownerPackagePins"]:
            owner = repositories[row["owner_repository"]]
            row.update({"source_commit": owner["commit"], "source_tree": owner["tree"]})
        graph["presentationSource"].update({
            "commit": presentation["commit"], "tree": presentation["tree"],
        })
        return graph

    @staticmethod
    def reseal_for_consumer(payload: dict[str, object]) -> None:
        authority = copy.deepcopy(payload)
        authority.pop("authoritySha256", None)
        authority.pop("generatedAtUtc", None)
        payload["authoritySha256"] = physical_contract.canonical_sha256(authority)

    def consumer_references(self) -> physical_contract.BuildProvenanceReferences:
        return physical_contract.BuildProvenanceReferences(
            package_authority=physical_contract.bind_regular(
                self.package_authority, "consumer committed package authority",
            ),
            package_authority_intake=physical_contract.bind_regular(
                self.package_authority_binding, "consumer package authority intake",
            ),
            package_authority_post_build=physical_contract.bind_regular(
                self.package_authority_seal, "consumer package authority post-build seal",
            ),
            content_manifest=physical_contract.bind_regular(
                self.content_manifest, "consumer content manifest",
            ),
            content_source_receipt=physical_contract.bind_regular(
                self.content_source, "consumer content source receipt",
            ),
            content_apk_receipt=physical_contract.bind_regular(
                self.content_apk, "consumer content APK receipt",
            ),
        )

    def consumer_validation_arguments(self) -> dict[str, object]:
        return {
            "repository_root": self.android,
            "references": self.consumer_references(),
        }

    def validate_package_authority_fixture(self) -> dict[str, object]:
        return provenance.validate_current_package_authority(
            android_root=self.android,
            presentation_root=self.presentation,
            receipt_path=self.ui_authority_receipt,
            package_feed=self.package_feed,
            manifest_path=self.package_authority,
            committed_path=self.package_authority,
            verifier=self.verified_package_authority,
        )

    def test_full_v3_provenance_round_trip_binds_inputs_without_device_claims(self) -> None:
        manifest = provenance.create_manifest(**self.create_arguments())
        provenance.write_manifest(self.manifest, manifest)
        self.assertEqual(
            manifest,
            provenance.load_and_verify_manifest(self.manifest, **self.create_arguments()),
        )
        self.assertEqual(self.android_commit, manifest["sourceHead"]["commit"])
        self.assertEqual(provenance.HUB_REVISION, manifest["packageAuthority"]["sourceGraph"]["hubProducerCommit"])
        self.assertEqual(provenance.PACKAGE_AUTHORITY_CONTRACT, manifest["packageAuthority"]["contractName"])
        self.assertTrue(manifest["artifact"]["fullMauiArtifact"])
        self.assertFalse(manifest["artifact"]["installed"])
        self.assertFalse(manifest["publicationAuthorized"])
        self.assertIn("api36_device_execution", manifest["doesNotAssert"])

    def test_materialized_v3_is_consumed_and_both_legacy_v2_shapes_fail_closed(self) -> None:
        manifest = provenance.create_manifest(**self.create_arguments())
        provenance.write_manifest(self.manifest, manifest)
        graph_path = self.root / "consumer-release-source-graph.json"
        write_json(graph_path, self.consumer_source_graph(manifest))

        accepted = physical_contract.validate_build_provenance(
            physical_contract.bind_regular(self.manifest, "materialized v3 provenance"),
            physical_contract.bind_regular(graph_path, "consumer release source graph"),
            physical_contract.bind_regular(self.apk, "materialized producer APK"),
            **self.consumer_validation_arguments(),
        )
        self.assertEqual(provenance.SCHEMA, accepted["schema"])
        core_row = next(
            row for row in self.consumer_source_graph(manifest)["repositories"]
            if row["name"] == "chummer6-core"
        )
        self.assertEqual(
            provenance.CORE_RUNTIME_REVISION,
            manifest["packageAuthority"]["sourceGraph"]["coreRuntimeSourceCommit"],
        )
        self.assertEqual(provenance.CORE_RUNTIME_REVISION, core_row["commit"])
        self.assertEqual(
            self.core_commit,
            manifest["packageAuthority"]["sourceGraph"]["corePackageRecipeCommit"],
        )
        self.assertEqual(self.core_commit, manifest["content"]["coreRevision"])
        self.assertNotEqual(
            manifest["packageAuthority"]["sourceGraph"]["coreRuntimeSourceCommit"],
            manifest["packageAuthority"]["sourceGraph"]["corePackageRecipeCommit"],
        )

        producer_v2 = copy.deepcopy(manifest)
        producer_v2["schema"] = "chummer.android.api36-arm64-physical-build-provenance/v2"
        self.reseal_for_consumer(producer_v2)
        write_json(self.manifest, producer_v2)
        with self.assertRaisesRegex(ValueError, "pass/scope/publication posture"):
            physical_contract.validate_build_provenance(
                physical_contract.bind_regular(self.manifest, "legacy producer-shaped v2"),
                physical_contract.bind_regular(graph_path, "consumer release source graph"),
                physical_contract.bind_regular(self.apk, "materialized producer APK"),
                **self.consumer_validation_arguments(),
            )

        legacy_consumer_v2 = copy.deepcopy(manifest)
        legacy_consumer_v2["schema"] = "chummer.android.api36-arm64-physical-build-provenance/v2"
        legacy_consumer_v2["dependencyMode"] = "locked_w5_packages_no_owner_siblings"
        legacy_consumer_v2["sourceGraph"] = {
            "sha256": provenance.file_sha256(graph_path),
            "sizeBytes": graph_path.stat().st_size,
            "contractName": physical_contract.SOURCE_GRAPH_SCHEMA,
            "repositories": self.consumer_source_graph(manifest)["repositories"],
            "packageAuthority": {"sha256": "7" * 64, "sizeBytes": 7},
            "packageAuthorityContract": "chummer.android.release-package-authority/v2",
            "packageAuthorityPublicationAuthorized": False,
        }
        legacy_consumer_v2["w5CompileProof"] = {}
        legacy_consumer_v2.pop("sourceHead")
        legacy_consumer_v2["presentationBuildSource"] = {
            "productionSource": False, "publicationAuthorized": False,
        }
        legacy_consumer_v2["packageAuthority"] = {}
        legacy_consumer_v2["content"] = {}
        legacy_consumer_v2["restore"] = {
            "lockedMode": True, "networkSourcesAllowed": False,
        }
        self.reseal_for_consumer(legacy_consumer_v2)
        write_json(self.manifest, legacy_consumer_v2)
        with self.assertRaisesRegex(ValueError, "keys are not exact"):
            physical_contract.validate_build_provenance(
                physical_contract.bind_regular(self.manifest, "legacy consumer-shaped v2"),
                physical_contract.bind_regular(graph_path, "consumer release source graph"),
                physical_contract.bind_regular(self.apk, "materialized producer APK"),
                **self.consumer_validation_arguments(),
            )

        tampered_v3 = copy.deepcopy(manifest)
        tampered_v3["packageAuthority"]["sourceGraph"]["hubProducerCommit"] = "0" * 40
        self.reseal_for_consumer(tampered_v3)
        write_json(self.manifest, tampered_v3)
        with self.assertRaisesRegex(ValueError, "source graph is not exact"):
            physical_contract.validate_build_provenance(
                physical_contract.bind_regular(self.manifest, "tampered v3 provenance"),
                physical_contract.bind_regular(graph_path, "consumer release source graph"),
                physical_contract.bind_regular(self.apk, "materialized producer APK"),
                **self.consumer_validation_arguments(),
            )

    def test_consumer_rejects_resealed_runtime_content_receipt_and_coordinated_authority_tamper(self) -> None:
        manifest = provenance.create_manifest(**self.create_arguments())
        provenance.write_manifest(self.manifest, manifest)
        graph_path = self.root / "consumer-release-source-graph.json"
        write_json(graph_path, self.consumer_source_graph(manifest))

        def validate(payload: dict[str, object], label: str) -> None:
            self.reseal_for_consumer(payload)
            write_json(self.manifest, payload)
            physical_contract.validate_build_provenance(
                physical_contract.bind_regular(self.manifest, label),
                physical_contract.bind_regular(graph_path, "consumer release source graph"),
                physical_contract.bind_regular(self.apk, "materialized producer APK"),
                **self.consumer_validation_arguments(),
            )

        tampered = copy.deepcopy(manifest)
        tampered["packageAuthority"]["sourceGraph"]["coreRuntimeSourceCommit"] = "0" * 40
        with self.assertRaisesRegex(ValueError, "source graph is not exact"):
            validate(tampered, "resealed runtime tamper")

        tampered = copy.deepcopy(manifest)
        tampered["content"]["bundleDigest"] = "0" * 64
        with self.assertRaisesRegex(ValueError, "content payload differs"):
            validate(tampered, "resealed content digest tamper")

        tampered = copy.deepcopy(manifest)
        tampered["content"]["sourceReceipt"]["sha256"] = "0" * 64
        with self.assertRaisesRegex(ValueError, "exact referenced bytes"):
            validate(tampered, "resealed content source receipt tamper")

        original_authority = self.package_authority.read_bytes()
        original_intake = self.package_authority_binding.read_bytes()
        original_post = self.package_authority_seal.read_bytes()
        try:
            authority = json.loads(original_authority)
            authority["doesNotAssert"] = ["resealed-coordinated-tamper"]
            write_json(self.package_authority, authority)
            self.package_authority_binding.write_bytes(self.package_authority.read_bytes())
            self.package_authority_seal.write_bytes(self.package_authority.read_bytes())
            tampered = copy.deepcopy(manifest)
            binding = {
                "sha256": provenance.file_sha256(self.package_authority),
                "sizeBytes": self.package_authority.stat().st_size,
            }
            tampered["packageAuthority"].update(binding)
            tampered["packageAuthority"]["intakeBinding"] = dict(binding)
            tampered["packageAuthority"]["postBuildBinding"] = dict(binding)
            with self.assertRaisesRegex(ValueError, "exact clean source-graph authority"):
                validate(tampered, "resealed coordinated package authority tamper")
        finally:
            self.package_authority.write_bytes(original_authority)
            self.package_authority_binding.write_bytes(original_intake)
            self.package_authority_seal.write_bytes(original_post)

    def test_ui_authority_receipt_digest_status_and_verifier_are_fail_closed(self) -> None:
        original = self.ui_authority_receipt.read_text(encoding="utf-8")
        payload = json.loads(original)
        payload["status"] = "blocked"
        write_json(self.ui_authority_receipt, payload)
        with self.assertRaisesRegex(ValueError, "receipt bytes"):
            provenance.create_manifest(**self.create_arguments())
        self.ui_authority_receipt.write_text(original, encoding="utf-8")
        with mock.patch.object(
            provenance,
            "UI_AUTHORITY_RECEIPT_SHA256",
            provenance.file_sha256(self.ui_authority_receipt),
        ):
            arguments = self.create_arguments()
            arguments["package_authority_verifier"] = lambda *_: {
                "status": "blocked",
                "contractName": provenance.PACKAGE_AUTHORITY_CONTRACT,
            }
            with self.assertRaisesRegex(ValueError, "committed verifier"):
                provenance.create_manifest(**arguments)

    def test_v2_package_authority_source_graph_and_posture_are_fail_closed(self) -> None:
        original = self.package_authority.read_bytes()
        for key, value in (
            ("contractName", "chummer.android.internal-phone-beta-package-authority/v1"),
            ("authorityState", "stale"),
            ("publicationAuthorized", True),
        ):
            with self.subTest(posture_key=key):
                payload = self.package_authority_payload()
                payload[key] = value
                write_json(self.package_authority, payload)
                with mock.patch.object(
                    provenance,
                    "PACKAGE_AUTHORITY_SHA256",
                    provenance.file_sha256(self.package_authority),
                ):
                    with self.assertRaisesRegex(ValueError, "authority posture"):
                        self.validate_package_authority_fixture()
        self.package_authority.write_bytes(original)
        for key, value in (
            ("corePackageRecipeCommit", "0" * 40),
            ("coreRuntimeSourceCommit", "1" * 40),
            ("hubProducerCommit", "2" * 40),
            ("registryCommit", "3" * 40),
            ("uiKitCommit", "4" * 40),
        ):
            with self.subTest(source_graph_key=key):
                payload = self.package_authority_payload()
                payload["sourceGraph"][key] = value
                write_json(self.package_authority, payload)
                with mock.patch.object(
                    provenance,
                    "PACKAGE_AUTHORITY_SHA256",
                    provenance.file_sha256(self.package_authority),
                ):
                    with self.assertRaisesRegex(ValueError, "source graph"):
                        self.validate_package_authority_fixture()
        self.package_authority.write_bytes(original)

    def test_package_authority_tamper_fails_closed(self) -> None:
        payload = json.loads(self.package_authority.read_text(encoding="utf-8"))
        payload["sourceGraph"]["hubProducerCommit"] = "f" * 40
        write_json(self.package_authority, payload)
        with self.assertRaisesRegex(ValueError, "package-authority digest"):
            self.validate_package_authority_fixture()

    def test_package_authority_intake_and_post_build_seal_are_cross_bound(self) -> None:
        original = self.package_authority_binding.read_bytes()
        self.package_authority_binding.write_bytes(original + b" ")
        try:
            with self.assertRaisesRegex(ValueError, "changed during the physical build"):
                provenance.create_manifest(**self.create_arguments())
        finally:
            self.package_authority_binding.write_bytes(original)
        original = self.package_authority_seal.read_bytes()
        self.package_authority_seal.write_bytes(original + b" ")
        try:
            with self.assertRaisesRegex(ValueError, "changed during the physical build"):
                provenance.create_manifest(**self.create_arguments())
        finally:
            self.package_authority_seal.write_bytes(original)

    def test_core_content_source_identity_and_verifier_are_required(self) -> None:
        arguments = self.create_arguments()
        arguments["content_verifier"] = lambda *_: ["forged-content"]
        with self.assertRaisesRegex(ValueError, "content verifier blocked"):
            provenance.create_manifest(**arguments)
        (self.core / "Chummer/data/lifemodules.xml").write_text("dirty", encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "Core content source is dirty"):
            provenance.create_manifest(**self.create_arguments())

    def test_content_source_and_apk_receipts_are_cross_bound(self) -> None:
        payload = self.content_payload(apk=True)
        payload["bundleDigest"] = "c" * 64
        write_json(self.content_apk, payload)
        with self.assertRaisesRegex(ValueError, "pre/post Core content"):
            provenance.create_manifest(**self.create_arguments())
        write_json(self.content_apk, self.content_payload(apk=True))
        with zipfile.ZipFile(self.apk, "a") as archive:
            archive.writestr("tamper", b"x")
        with self.assertRaisesRegex(ValueError, "complete APK content"):
            provenance.create_manifest(**self.create_arguments())

    def test_apk_must_contain_only_arm64_native_payload(self) -> None:
        with zipfile.ZipFile(self.apk, "a") as archive:
            archive.writestr("lib/x86_64/libmonodroid.so", b"x86")
        write_json(self.content_apk, self.content_payload(apk=True))
        with self.assertRaisesRegex(ValueError, "exactly arm64"):
            provenance.create_manifest(**self.create_arguments())

    def test_lock_assets_and_execution_evidence_are_fail_closed(self) -> None:
        original_lock = self.lock.read_bytes()
        lock = self.lock_payload()
        del lock["dependencies"][provenance.TARGET_FRAMEWORK]["Chummer.Play.Contracts"]
        write_json(self.lock, lock)
        with self.assertRaisesRegex(ValueError, "Chummer.Play.Contracts"):
            provenance.validate_full_project_lock(self.lock)
        self.lock.write_bytes(original_lock)
        assets = self.assets_payload()
        del assets["libraries"]["Chummer.Ui.Kit/0.1.0-preview"]
        write_json(self.assets, assets)
        with self.assertRaisesRegex(ValueError, "Chummer.Ui.Kit"):
            provenance.create_manifest(**self.create_arguments())
        write_json(self.assets, self.assets_payload())
        self.build_log.write_text("Build succeeded.\n1 Warning(s)\n0 Error(s)\n", encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "warnings=0"):
            provenance.create_manifest(**self.create_arguments())

    def test_bounded_journal_output_and_phase_prefix_are_authenticated(self) -> None:
        rows = [json.loads(line) for line in self.journal.read_text(encoding="utf-8").splitlines()]
        rows[9]["outputSha256"] = "0" * 64
        self.journal.write_text("".join(json.dumps(row) + "\n" for row in rows), encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "failed or terminated phase"):
            provenance.create_manifest(**self.create_arguments())
        self.write_journal()
        rows = [json.loads(line) for line in self.journal.read_text(encoding="utf-8").splitlines()]
        rows.pop(0)
        self.journal.write_text("".join(json.dumps(row) + "\n" for row in rows), encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "row count"):
            provenance.create_manifest(**self.create_arguments())

    def test_bounded_journal_exact_schema_types_command_environment_and_deadline(self) -> None:
        mutations = (
            (lambda rows: rows[0].__setitem__("unknownClaim", True), "keys are not exact"),
            (lambda rows: rows[0].__setitem__("timeoutSeconds", "100"), "timeout/deadline"),
            (lambda rows: rows[0].__setitem__("workingDirectory", str(self.root)), "context is not exact"),
            (lambda rows: rows[0]["environment"].__setitem__("SECRET", "x"), "environment allowlist"),
            (
                lambda rows: rows[0]["environment"].pop("CHUMMER_ANDROID_REVISION"),
                "environment allowlist",
            ),
            (
                lambda rows: rows[0]["environment"].__setitem__(
                    "CHUMMER_PRESENTATION_REVISION", "not-a-revision",
                ),
                "environment values",
            ),
            (
                lambda rows: rows[0]["environment"].__setitem__(
                    "CHUMMER_CORE_ENGINE_REVISION", "0" * 40,
                ),
                "environment values",
            ),
            (
                lambda rows: rows[0]["environment"].__setitem__(
                    "CHUMMER_HOSTILE_REVISION", "f" * 40,
                ),
                "environment allowlist",
            ),
            (
                lambda rows: rows[0]["environment"].__setitem__(
                    "CHUMMER_RELEASE_WORKSPACE_ROOT", str(self.root),
                ),
                "environment",
            ),
            (lambda rows: rows[0]["argv"].append("--forged"), "argv is not exact"),
            (
                lambda rows: rows[6]["argv"].remove("--ui-authority-receipt"),
                "argv is not exact",
            ),
            (lambda rows: rows[9].__setitem__("exitCode", True), "failed or terminated"),
            (lambda rows: rows[9]["termination"].__setitem__("sigkillSent", "false"), "failed or terminated"),
            (lambda rows: rows.__setitem__(slice(0, 2), [rows[2], rows[3]]), "phase order/context"),
        )
        for mutate, error in mutations:
            with self.subTest(error=error):
                self.write_journal()
                rows = [json.loads(line) for line in self.journal.read_text(encoding="utf-8").splitlines()]
                mutate(rows)
                self.journal.write_text("".join(json.dumps(row) + "\n" for row in rows), encoding="utf-8")
                with self.assertRaisesRegex(ValueError, error):
                    provenance.create_manifest(**self.create_arguments())
        self.write_journal()
        raw_rows = [json.loads(line) for line in self.raw_journal.read_text(encoding="utf-8").splitlines()]
        raw_rows[0]["command"].append("--raw-forgery")
        self.raw_journal.write_text("".join(json.dumps(row) + "\n" for row in raw_rows), encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "does not cross-bind"):
            provenance.create_manifest(**self.create_arguments())
        self.write_journal()
        raw_rows = [json.loads(line) for line in self.raw_journal.read_text(encoding="utf-8").splitlines()]
        raw_rows[1]["timedOut"] = "false"
        self.raw_journal.write_text("".join(json.dumps(row) + "\n" for row in raw_rows), encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "does not cross-bind"):
            provenance.create_manifest(**self.create_arguments())

    def test_bounded_wrapper_executes_real_command_and_seals_exact_raw_and_canonical_rows(self) -> None:
        canonical = self.root / "wrapper-journal.jsonl"
        raw = self.root / "wrapper-raw-journal.jsonl"
        delegate = self.root / "wrapper-delegate-journal.jsonl"
        output = self.root / "wrapper-output.log"
        environment = {key: str(self.root) for key in provenance.ENVIRONMENT_ALLOWLIST}
        environment.update({
            "ANDROID_HOME": str(self.root), "JAVA_HOME": str(self.root),
            "NUGET_PACKAGES": str(self.nuget_packages),
            "DOTNET_CLI_TELEMETRY_OPTOUT": "1", "DOTNET_CLI_USE_MSBUILD_SERVER": "0",
            "MSBUILDDISABLENODEREUSE": "1", "LANG": "C.UTF-8", "LC_ALL": "C.UTF-8",
        })
        invocation = time.time()
        command = [
            sys.executable, str(REPO_ROOT / "scripts/materialize-api36-physical-build-provenance.py"),
            "run-bounded", "--journal", str(canonical), "--raw-journal", str(raw),
            "--delegate-journal", str(delegate),
            "--output", str(output), "--phase", "toolchain-intake",
            "--timeout-seconds", "1800", "--deadline-epoch", str(invocation + 7200),
            "--invocation-started-epoch", str(invocation),
            "--working-directory", str(self.android),
        ]
        for key in sorted(environment):
            command.extend(("--environment", f"{key}={environment[key]}"))
        command.extend(("--", sys.executable, "-c", "print('bounded-wrapper-pass')"))
        completed = subprocess.run(command, check=False, capture_output=True, text=True, timeout=30)
        self.assertEqual(0, completed.returncode, completed.stderr)
        self.assertEqual("bounded-wrapper-pass\n", output.read_text(encoding="utf-8"))
        canonical_rows = [json.loads(line) for line in canonical.read_text(encoding="utf-8").splitlines()]
        raw_rows = [json.loads(line) for line in raw.read_text(encoding="utf-8").splitlines()]
        delegate_rows = [json.loads(line) for line in delegate.read_text(encoding="utf-8").splitlines()]
        self.assertEqual(["started", "finished"], [row["event"] for row in canonical_rows])
        self.assertEqual(["started", "finished"], [row["event"] for row in raw_rows])
        self.assertEqual(canonical_rows[0]["argv"], raw_rows[0]["command"])
        self.assertEqual(provenance.file_sha256(output), canonical_rows[1]["outputSha256"])
        self.assertEqual(provenance.COMMAND_JOURNAL_CONTRACT, canonical_rows[0]["contractName"])
        self.assertEqual(provenance.RAW_COMMAND_JOURNAL_CONTRACT, raw_rows[0]["contractName"])
        self.assertEqual(provenance.DELEGATE_COMMAND_JOURNAL_CONTRACT, delegate_rows[0]["contractName"])

    def test_authority_descriptor_snapshot_swap_races_are_rejected(self) -> None:
        targets = (
            ("UI authority receipt", self.ui_authority_receipt),
            ("package-authority intake binding", self.package_authority_binding),
            ("post-build package-authority seal", self.package_authority_seal),
            ("restore log", self.restore_log),
            ("Core content receipt", self.content_source),
            ("APK plus APK content receipt", self.apk),
        )
        for index, (label, target) in enumerate(targets):
            with self.subTest(label=label):
                original = target.read_bytes()
                receipt_original = self.content_apk.read_bytes()

                def swap() -> None:
                    if index < 3:
                        replacement = target.with_name(target.name + ".race-replacement")
                        replacement.write_bytes(original)
                        replacement.replace(target)
                    else:
                        target.write_bytes(original + b" ")
                    if target == self.apk:
                        payload = self.content_payload(apk=True)
                        write_json(self.content_apk, payload)

                arguments = self.create_arguments()
                arguments["before_final_recheck"] = swap
                try:
                    with self.assertRaisesRegex(ValueError, "changed before provenance seal"):
                        provenance.create_manifest(**arguments)
                finally:
                    target.write_bytes(original)
                    self.content_apk.write_bytes(receipt_original)

    def test_remaining_authority_lock_asset_log_and_cache_swaps_are_rejected(self) -> None:
        targets = (
            self.package_authority,
            self.presentation_lock,
            self.producer_lock,
            self.package_cache_manifest,
            self.assets,
            self.build_log,
            self.workloads,
            self.sdk_packages,
            self.raw_journal,
        )
        for target in targets:
            with self.subTest(target=target.name):
                original = target.read_bytes()
                arguments = self.create_arguments()
                arguments["before_final_recheck"] = lambda target=target, original=original: target.write_bytes(original + b" ")
                try:
                    with self.assertRaisesRegex(ValueError, "changed before provenance seal"):
                        provenance.create_manifest(**arguments)
                finally:
                    target.write_bytes(original)

    def test_final_android_and_product_source_identity_is_rechecked(self) -> None:
        targets = (
            (self.android / "product.txt", "Android repository is dirty"),
            (self.presentation / "source.txt", "current Presentation build source is dirty"),
            (self.core / "Chummer/data/lifemodules.xml", "Core content source is dirty"),
        )
        for product, error in targets:
            with self.subTest(product=product):
                original = product.read_bytes()
                arguments = self.create_arguments()
                arguments["before_final_recheck"] = lambda product=product: product.write_bytes(b"raced source\n")
                try:
                    with self.assertRaisesRegex(ValueError, error):
                        provenance.create_manifest(**arguments)
                finally:
                    product.write_bytes(original)

    def test_toolchain_is_structured_real_and_command_bound(self) -> None:
        original_xml = self.sdk_packages.read_bytes()
        original_java = self.java.read_bytes()
        original_javac = self.javac.read_bytes()
        original_workloads = self.workloads.read_bytes()
        cases = (
            (self.sdk_packages, b"not xml", "not XML"),
            (
                self.sdk_packages,
                b'<sdk:sdk-repository xmlns:sdk="urn:android"><localPackage path="platform-tools" /></sdk:sdk-repository>',
                "exactly three",
            ),
            (self.java, b"#!/bin/sh\nprintf 'arbitrary java\\n'\n", "native binary"),
            (self.javac, b"#!/bin/sh\nprintf 'javac 22.0.1\\n'\n", "native binary"),
            (self.workloads, b'{"installed":[],"updateAvailable":[]}\n', "keys are not exact"),
        )
        for path, replacement, error in cases:
            with self.subTest(error=error):
                path.write_bytes(replacement)
                if path in (self.java, self.javac):
                    path.chmod(0o755)
                try:
                    with self.assertRaisesRegex(ValueError, error):
                        provenance.create_manifest(**self.create_arguments())
                finally:
                    originals = {
                        self.sdk_packages: original_xml, self.java: original_java,
                        self.javac: original_javac, self.workloads: original_workloads,
                    }
                    path.write_bytes(originals[path])
                    if path in (self.java, self.javac):
                        path.chmod(0o755)
        self.write_journal()
        rows = [json.loads(line) for line in self.journal.read_text(encoding="utf-8").splitlines()]
        build_started = rows[10]
        build_started["argv"].remove("-p:AndroidSdkBuildToolsVersion=36.0.0")
        rows[11]["argv"] = build_started["argv"]
        self.journal.write_text("".join(json.dumps(row) + "\n" for row in rows), encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "argv is not exact"):
            provenance.create_manifest(**self.create_arguments())

    def test_apk_output_inventory_rejects_extra_architecture_and_symlink_artifacts(self) -> None:
        extra = self.root / "com.myexternalbrain.chummer-x86-Signed.apk"
        extra.write_bytes(self.apk.read_bytes())
        try:
            with self.assertRaisesRegex(ValueError, "exactly one"):
                provenance.create_manifest(**self.create_arguments())
        finally:
            extra.unlink()
        sibling = self.root / "alias.apk"
        sibling.symlink_to(self.apk)
        try:
            with self.assertRaisesRegex(ValueError, "symlink"):
                provenance.create_manifest(**self.create_arguments())
        finally:
            sibling.unlink()

    def test_final_seal_reenumerates_apk_directory_after_authenticated_inputs(self) -> None:
        sibling = self.root / "late-sibling.apk"
        arguments = self.create_arguments()
        arguments["before_final_recheck"] = lambda: sibling.write_bytes(self.apk.read_bytes())
        try:
            with self.assertRaisesRegex(ValueError, "exactly one"):
                provenance.create_manifest(**arguments)
        finally:
            sibling.unlink(missing_ok=True)
        symlink = self.root / "late-alias.apk"
        arguments = self.create_arguments()
        arguments["before_final_recheck"] = lambda: symlink.symlink_to(self.apk)
        try:
            with self.assertRaisesRegex(ValueError, "symlink"):
                provenance.create_manifest(**arguments)
        finally:
            symlink.unlink(missing_ok=True)

    def test_presentation_remote_reachability_is_required(self) -> None:
        arguments = self.create_arguments()
        arguments["remote_reachability_verifier"] = lambda *_: (_ for _ in ()).throw(
            ValueError("verified Presentation commit is not remotely reachable")
        )
        with self.assertRaisesRegex(ValueError, "remotely reachable"):
            provenance.create_manifest(**arguments)

    def test_presentation_remote_accepts_only_canonical_ui_origin(self) -> None:
        def verify_with_origin(origin: str) -> None:
            answers = {
                ("remote", "get-url", "origin"): origin,
                (
                    "rev-parse",
                    "--verify",
                    provenance.PRESENTATION_REMOTE_REF,
                ): provenance.PRESENTATION_COMMIT,
            }

            def fake_git(_root: Path, *arguments: str) -> str:
                return answers[arguments]

            with (
                mock.patch.object(provenance, "_git", side_effect=fake_git),
                mock.patch.object(
                    provenance.subprocess,
                    "run",
                    return_value=subprocess.CompletedProcess([], 0),
                ),
            ):
                provenance.require_presentation_remote_reachability(self.presentation)

        verify_with_origin(provenance.PRESENTATION_REPOSITORY)
        for rejected in (
            "https://github.com/ArchonMegalon/chummer6-ui-kit.git",
            "https://github.com/example/chummer6-ui.git",
        ):
            with self.subTest(origin=rejected):
                with self.assertRaisesRegex(ValueError, "source remote is not exact"):
                    verify_with_origin(rejected)

    def test_presentation_remote_ref_must_resolve_to_verified_commit(self) -> None:
        answers = {
            ("remote", "get-url", "origin"): provenance.PRESENTATION_REPOSITORY,
            (
                "rev-parse",
                "--verify",
                provenance.PRESENTATION_REMOTE_REF,
            ): "0" * 40,
        }

        with mock.patch.object(
            provenance,
            "_git",
            side_effect=lambda _root, *arguments: answers[arguments],
        ):
            with self.assertRaisesRegex(ValueError, "does not resolve"):
                provenance.require_presentation_remote_reachability(self.presentation)

    def test_signature_receipt_logs_tools_and_certificate_are_exact(self) -> None:
        original_receipt = self.signing_receipt.read_bytes()
        payload = self.signing_receipt_payload()
        cases = (
            ("publicationAuthorized", True),
            ("warningsAsErrors", False),
            ("certificateSha256", "e" * 64),
            ("verifiedSchemes", [1]),
            ("apksignerSha256", "0" * 64),
        )
        for key, value in cases:
            with self.subTest(key=key):
                candidate = copy.deepcopy(payload)
                candidate[key] = value
                write_json(self.signing_receipt, candidate)
                with self.assertRaisesRegex(ValueError, "structural modern signature|logs do not bind"):
                    provenance.create_manifest(**self.create_arguments())
        unknown = copy.deepcopy(payload)
        unknown["googlePlayUploadAuthorized"] = True
        write_json(self.signing_receipt, unknown)
        with self.assertRaisesRegex(ValueError, "keys are not exact"):
            provenance.create_manifest(**self.create_arguments())
        self.signing_receipt.write_bytes(original_receipt)
        original_log = self.apksigner_log.read_bytes()
        self.apksigner_log.write_text("arbitrary success string\n", encoding="utf-8")
        try:
            with self.assertRaisesRegex(ValueError, "structural modern signature|logs do not bind"):
                provenance.create_manifest(**self.create_arguments())
        finally:
            self.apksigner_log.write_bytes(original_log)

    def test_script_owned_timeout_and_deadline_reject_consistent_multi_journal_rewrites(self) -> None:
        originals = {
            self.journal: self.journal.read_bytes(), self.raw_journal: self.raw_journal.read_bytes(),
            self.delegate_journal: self.delegate_journal.read_bytes(),
        }
        for target in originals:
            rows = [json.loads(line) for line in target.read_text(encoding="utf-8").splitlines()]
            for row in rows:
                if "timeoutSeconds" in row:
                    row["timeoutSeconds"] = 1799.0
            target.write_text("".join(json.dumps(row) + "\n" for row in rows), encoding="utf-8")
        try:
            with self.assertRaisesRegex(ValueError, "timeout/deadline"):
                provenance.create_manifest(**self.create_arguments())
        finally:
            for target, data in originals.items():
                target.write_bytes(data)
        for target in (self.journal, self.raw_journal):
            rows = [json.loads(line) for line in target.read_text(encoding="utf-8").splitlines()]
            for row in rows:
                row["totalDeadlineSeconds"] = 7201.0
                row["deadlineEpoch"] = row["invocationStartedEpoch"] + 7201.0
            target.write_text("".join(json.dumps(row) + "\n" for row in rows), encoding="utf-8")
        try:
            with self.assertRaisesRegex(ValueError, "timeout/deadline"):
                provenance.create_manifest(**self.create_arguments())
        finally:
            self.journal.write_bytes(originals[self.journal])
            self.raw_journal.write_bytes(originals[self.raw_journal])

    def test_raw_and_delegate_journal_unknown_duplicate_and_type_confusion_are_rejected(self) -> None:
        raw_original = self.raw_journal.read_bytes()
        delegate_original = self.delegate_journal.read_bytes()
        raw_rows = [json.loads(line) for line in self.raw_journal.read_text(encoding="utf-8").splitlines()]
        raw_rows[0]["unknownClaim"] = True
        self.raw_journal.write_text("".join(json.dumps(row) + "\n" for row in raw_rows), encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "keys are not exact"):
            provenance.create_manifest(**self.create_arguments())
        self.raw_journal.write_bytes(raw_original)
        delegate_lines = self.delegate_journal.read_text(encoding="utf-8").splitlines()
        delegate_lines[0] = delegate_lines[0].replace(
            '"phase": "toolchain-intake"',
            '"phase": "toolchain-intake", "phase": "forged"',
        )
        self.delegate_journal.write_text("\n".join(delegate_lines) + "\n", encoding="utf-8")
        try:
            with self.assertRaisesRegex(ValueError, "duplicate JSON key"):
                provenance.create_manifest(**self.create_arguments())
        finally:
            self.delegate_journal.write_bytes(delegate_original)

    def test_installed_sdk_package_jdk_and_command_path_drift_are_rejected(self) -> None:
        package_xml = self.platform_root / "package.xml"
        original_package = package_xml.read_bytes()
        package_xml.write_text(
            self.sdk_package_xml("platforms;android-36", 1, 0, 0), encoding="utf-8",
        )
        try:
            with self.assertRaisesRegex(ValueError, "package identity drifted"):
                provenance.create_manifest(**self.create_arguments())
        finally:
            package_xml.write_bytes(original_package)
        release = self.jdk_root / "release"
        original_release = release.read_bytes()
        release.write_text('JAVA_VERSION="17.0.14"\nIMPLEMENTOR="Unknown"\n', encoding="utf-8")
        try:
            with self.assertRaisesRegex(ValueError, "release-authorized: jdk_release"):
                provenance.create_manifest(**self.create_arguments())
        finally:
            release.write_bytes(original_release)
        wrong = self.root / "apksigner"
        shutil.copyfile(self.apksigner, wrong)
        wrong.chmod(0o755)
        arguments = self.create_arguments()
        arguments["apksigner_path"] = wrong
        with self.assertRaisesRegex(ValueError, "exact selected Android build-tools"):
            provenance.create_manifest(**arguments)

    def test_bounded_wrapper_rejects_non_script_owned_timeout_before_command(self) -> None:
        marker = self.root / "must-not-run"
        invocation = time.time()
        environment = {key: str(self.root) for key in provenance.ENVIRONMENT_ALLOWLIST}
        command = [
            sys.executable, str(REPO_ROOT / "scripts/materialize-api36-physical-build-provenance.py"),
            "run-bounded", "--journal", str(self.root / "bad-canonical.jsonl"),
            "--raw-journal", str(self.root / "bad-raw.jsonl"), "--delegate-journal",
            str(self.root / "bad-delegate.jsonl"), "--output", str(self.root / "bad.log"),
            "--phase", "toolchain-intake", "--timeout-seconds", "1799",
            "--deadline-epoch", str(invocation + 7200), "--invocation-started-epoch",
            str(invocation), "--working-directory", str(self.android),
        ]
        for key in sorted(environment):
            command.extend(("--environment", f"{key}={environment[key]}"))
        command.extend(("--", sys.executable, "-c", f"open({str(marker)!r}, 'w').write('ran')"))
        completed = subprocess.run(command, check=False, capture_output=True, text=True, timeout=30)
        self.assertNotEqual(0, completed.returncode)
        self.assertFalse(marker.exists())

    def test_bad_toolchain_is_blocked_before_any_restore_or_build_command(self) -> None:
        script = (REPO_ROOT / "scripts/build-api36-physical-candidate.sh").read_text(encoding="utf-8")
        hash_gate = script.index(
            'require_sha256 "$dotnet_command" "1c13be7f10008294dfd25f0fc0cd7c88e26d3dbaf8e16019af6c5bb53dd0259d"'
        )
        toolchain_phase = script.index("run_bounded toolchain-intake")
        authority_phase = script.index("run_bounded package-authority-intake")
        build_inputs_phase = script.index("run_bounded current-build-input-intake")
        restore_phase = script.index("run_bounded locked-full-restore")
        build_phase = script.index("run_bounded serialized-full-maui-build")
        self.assertLess(hash_gate, toolchain_phase)
        self.assertLess(toolchain_phase, authority_phase)
        self.assertLess(authority_phase, build_inputs_phase)
        self.assertLess(build_inputs_phase, restore_phase)
        self.assertLess(restore_phase, build_phase)
        pre_restore = script[:restore_phase]
        self.assertIn("capture-workloads", pre_restore)
        self.assertIn("--android-sdk-packages", pre_restore)
        self.assertIn("check-inputs", pre_restore)
        self.assertIn("verify_internal_phone_beta_package_authority.py", pre_restore)
        self.assertNotIn('"$dotnet_command" restore', pre_restore)
        self.assertNotIn('"$dotnet_command" build', pre_restore)

    def test_manifest_tamper_unknown_and_duplicate_keys_fail_closed(self) -> None:
        manifest = provenance.create_manifest(**self.create_arguments())
        provenance.write_manifest(self.manifest, manifest)
        payload = copy.deepcopy(manifest)
        payload["publicReleaseReady"] = True
        write_json(self.manifest, payload)
        with self.assertRaisesRegex(ValueError, "keys are not exact"):
            provenance.load_and_verify_manifest(self.manifest, **self.create_arguments())
        self.manifest.write_text('{"schema":"a","schema":"b"}', encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "duplicate JSON key"):
            provenance.load_and_verify_manifest(self.manifest, **self.create_arguments())

    def test_build_contract_is_locked_offline_bounded_and_has_no_device_or_publish_step(self) -> None:
        script = (REPO_ROOT / "scripts/build-api36-physical-candidate.sh").read_text(encoding="utf-8")
        required = (
            "--locked-mode", "--disable-parallel", "--no-http-cache",
            "ChummerUseLocalCompatibilityTree=false",
            "ChummerUseLockedOwnerContractPackages=true",
            "RestoreLockedMode=true", "RestorePackagesWithLockFile=true",
            "--source \"$CHUMMER_INTERNAL_PHONE_BETA_PACKAGE_FEED\"",
            "--source \"$CHUMMER_API36_OFFLINE_NUGET_FEED\"",
            "run-bounded", "capture-workloads", "verify_internal_phone_beta_package_authority.py",
            "verify_android_content_bundle.py", "check-inputs", "materialize",
            "--framework net10.0-android36.0", "--runtime android-arm64",
            "-p:AndroidPackageFormats=apk", "-m:1", "--warnaserror",
            "2c6b273ed9eb11db0c3820ebb7e8434ccea6471e7ac2db38763a0aa08db294d9",
            "presentation-revision-input-mismatch",
            "current-presentation-tree-mismatch",
            "current-presentation-lock-mismatch",
            "core-content-commit-mismatch",
            "core-runtime-revision-input-mismatch",
            "hub-revision-input-mismatch",
            'dotnet_command="/usr/lib/dotnet/dotnet"',
            'android_sdk_root="/home/tibor/.cache/chummer-android-toolchain/android-sdk"',
            'java_home="/home/tibor/.cache/chummer-android-toolchain/microsoft-jdk"',
            "verify-apk-signing", "--delegate-journal",
            "--timeout-seconds 1800", "+ 7200",
            "AndroidSdkBuildToolsVersion=$android_build_tools_version",
            '--ui-authority-receipt "$CHUMMER_CURRENT_UI_PACKAGE_AUTHORITY_RECEIPT"',
            '--package-authority "$repo_dir/eng/internal-phone-beta-package-authority.json"',
            '"CHUMMER_PRESENTATION_REVISION=$CHUMMER_PRESENTATION_REVISION"',
        )
        for fragment in required:
            with self.subTest(fragment=fragment):
                self.assertIn(fragment, script)
        self.assertNotIn("https://", script)
        self.assertNotIn("dotnet publish", script)
        self.assertNotIn(" adb ", script)
        self.assertNotIn("ChummerUseLocalCompatibilityTree=true", script)
        self.assertNotIn("ChummerUseLockedOwnerContractPackages=false", script)
        self.assertNotIn("CHUMMER_JAVA", script)
        self.assertNotIn("CHUMMER_DOTNET", script)
        self.assertNotIn("CHUMMER_RELEASE_WORKSPACE_ROOT", script)
        self.assertNotIn("verify_release_source_graph.py", script)
        bounded_environment = script[
            script.index("bounded_environment=("):script.index("bounded_environment_arguments=()")
        ]
        for variable in provenance.REVISION_ENVIRONMENT_VARIABLES:
            with self.subTest(revision_variable=variable):
                self.assertIn(variable, script[:script.index("for forbidden in")])
                self.assertEqual(
                    1,
                    bounded_environment.count(f'"{variable}=${variable}"'),
                )
        materializer = (REPO_ROOT / "scripts/materialize-api36-physical-build-provenance.py").read_text(encoding="utf-8")
        self.assertIn('"--verbose", "--print-certs", "--Werr"', materializer)

    def test_committed_full_project_lock_is_exact_arm64_v2_closure(self) -> None:
        lock_path = REPO_ROOT / "src/Chummer.Android/packages.lock.json"
        lock = provenance.validate_full_project_lock(lock_path)
        self.assertEqual(
            "2c6b273ed9eb11db0c3820ebb7e8434ccea6471e7ac2db38763a0aa08db294d9",
            provenance.file_sha256(lock_path),
        )
        self.assertEqual(70_376, lock_path.stat().st_size)
        self.assertEqual(142, len(lock["dependencies"][provenance.TARGET_FRAMEWORK]))


class PhysicalProducerConsumerAuthorityParityTests(unittest.TestCase):
    def test_consumer_trusted_toolchain_authority_is_exactly_the_producer_authority(self) -> None:
        self.assertEqual(
            str(provenance.ANDROID_SDK_ROOT_AUTHORITY),
            physical_contract.TRUSTED_ANDROID_SDK_ROOT,
        )
        self.assertEqual(
            provenance.TOOLCHAIN_SHA256_AUTHORITY,
            physical_contract.TRUSTED_TOOLCHAIN_SHA256,
        )


if __name__ == "__main__":
    unittest.main()
