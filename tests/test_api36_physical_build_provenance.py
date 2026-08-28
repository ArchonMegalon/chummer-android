from __future__ import annotations

import copy
import hashlib
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import time
import unittest
from unittest import mock
import zipfile

from tests import api36_physical_build_provenance as provenance


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
        (self.android / "scripts/verify_release_source_graph.py").write_text(
            "# fixture source graph verifier\n", encoding="utf-8",
        )
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
        subprocess.run(["git", "commit", "-qm", "W5 source"], cwd=self.android, check=True)
        self.w5_commit = self.git("rev-parse", "HEAD")
        self.w5_tree = self.git("rev-parse", "HEAD^{tree}")
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
        (self.presentation / "source.txt").write_text("W4.1 source\n", encoding="utf-8")
        (self.presentation / "Chummer.Presentation").mkdir()
        (self.presentation / "Chummer.Desktop.Runtime").mkdir()
        self.presentation_lock = self.presentation / "Chummer.Presentation/packages.lock.json"
        self.desktop_lock = self.presentation / "Chummer.Desktop.Runtime/packages.lock.json"
        self.presentation_lock.write_text('{"version":1}\n', encoding="utf-8")
        self.desktop_lock.write_text('{"version":1}\n', encoding="utf-8")
        subprocess.run(["git", "add", "."], cwd=self.presentation, check=True)
        subprocess.run(["git", "commit", "-qm", "W4.1 source"], cwd=self.presentation, check=True)
        self.presentation_commit = subprocess.run(
            ["git", "rev-parse", "HEAD"], cwd=self.presentation, check=True,
            capture_output=True, text=True,
        ).stdout.strip()
        self.presentation_tree = subprocess.run(
            ["git", "rev-parse", "HEAD^{tree}"], cwd=self.presentation, check=True,
            capture_output=True, text=True,
        ).stdout.strip()

        self.w5_receipt = self.root / "w5.json"
        self.w5_evidence = self.root / "w5.json.evidence"
        self.w5_evidence.mkdir()
        (self.w5_evidence / "authority-binding.json").write_text("authority\n", encoding="utf-8")
        (self.w5_evidence / "command-journal.jsonl").write_text("journal\n", encoding="utf-8")
        w5_payload = self.w5_payload()
        w5_payload["evidence"] = [
            {
                "path": path.name, "sha256": provenance.file_sha256(path),
                "sizeBytes": path.stat().st_size,
            }
            for path in sorted(self.w5_evidence.iterdir())
        ]
        write_json(self.w5_receipt, w5_payload)
        self.source_graph = self.root / "source-graph.json"
        write_json(self.source_graph, self.source_graph_payload())
        self.release_authority_v2 = self.root / "release-package-authority-v2.json"
        write_json(self.release_authority_v2, {"contractName": "fixture-v2-package-authority"})
        self.release_workspace = self.root / "release-workspace"
        self.package_feed = self.root / "w5-feed"
        self.offline_feed = self.root / "offline-feed"
        self.nuget_packages = self.root / "nuget-packages"
        for directory in (
            self.release_workspace, self.package_feed, self.offline_feed, self.nuget_packages,
        ):
            directory.mkdir()
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
        self.sdk_packages = self.root / "packages.xml"
        self.sdk_packages.write_text(
            '<sdk:sdk-repository xmlns:sdk="urn:android">'
            '<localPackage path="platforms;android-36" />'
            '<localPackage path="build-tools;36.0.0" />'
            '<localPackage path="platform-tools" />'
            '</sdk:sdk-repository>\n', encoding="utf-8",
        )
        self.workloads = self.root / "dotnet-workloads.json"
        write_json(self.workloads, {"installed": ["maui-android"], "updateAvailable": []})
        self.bin = self.root / "bin"
        self.bin.mkdir()
        self.java = self.bin / "java"
        self.javac = self.bin / "javac"
        self.dotnet = self.bin / "dotnet"
        self.python = self.bin / "python3"
        self.java.write_text('#!/bin/sh\nprintf \'openjdk version "21.0.8" fixture\\n\' >&2\n', encoding="utf-8")
        self.javac.write_text("#!/bin/sh\nprintf 'javac 21.0.8\\n'\n", encoding="utf-8")
        self.dotnet.write_text("#!/bin/sh\nprintf '10.0.111\\n'\n", encoding="utf-8")
        self.python.write_text("#!/bin/sh\nexit 0\n", encoding="utf-8")
        for executable in (self.java, self.javac, self.dotnet, self.python):
            executable.chmod(0o755)
        self.toolchain_log = self.root / "toolchain.log"
        self.toolchain_log.write_text("dotnet_workload_inventory=pass\n", encoding="utf-8")
        self.restore_log = self.root / "restore.log"
        self.restore_log.write_text("All projects are up-to-date for restore.\n", encoding="utf-8")
        self.build_log = self.root / "build.log"
        self.build_log.write_text(
            "Build succeeded.\n    0 Warning(s)\n    0 Error(s)\n", encoding="utf-8",
        )
        self.source_graph_log = self.root / "source-graph.log"
        self.source_graph_log.write_text("source graph exact\n", encoding="utf-8")
        self.content_source_log = self.root / "content-source.log"
        self.content_source_log.write_text("content source exact\n", encoding="utf-8")
        self.build_inputs_log = self.root / "build-inputs.log"
        self.build_inputs_log.write_text("build inputs exact\n", encoding="utf-8")
        self.content_apk_log = self.root / "content-apk.log"
        self.content_apk_log.write_text("APK content exact\n", encoding="utf-8")
        self.source_graph_seal_log = self.root / "source-graph-seal.log"
        self.source_graph_seal_log.write_text("source graph remained exact\n", encoding="utf-8")
        self.journal = self.root / "journal.jsonl"
        self.raw_journal = self.root / "raw-journal.jsonl"
        self.write_journal()
        self.manifest = self.root / "provenance.json"

        self.patches = [
            mock.patch.object(provenance, "W5_ANDROID_COMMIT", self.w5_commit),
            mock.patch.object(provenance, "W5_ANDROID_TREE", self.w5_tree),
            mock.patch.object(provenance, "W5_RECEIPT_SHA256", provenance.file_sha256(self.w5_receipt)),
            mock.patch.object(provenance, "W5_AUTHORITY_BINDING_SHA256", provenance.file_sha256(self.package_authority)),
            mock.patch.object(provenance, "W5_PRESENTATION_COMMIT", self.presentation_commit),
            mock.patch.object(provenance, "W5_PRESENTATION_TREE", self.presentation_tree),
            mock.patch.object(provenance, "W41_PRESENTATION_LOCK_SHA256", provenance.file_sha256(self.presentation_lock)),
            mock.patch.object(provenance, "W41_DESKTOP_LOCK_SHA256", provenance.file_sha256(self.desktop_lock)),
            mock.patch.object(provenance, "FULL_PROJECT_LOCK_SHA256", provenance.file_sha256(self.lock)),
            mock.patch.object(provenance, "FULL_PROJECT_LOCK_SIZE", self.lock.stat().st_size),
            mock.patch.object(provenance, "CORE_CONTENT_REVISION", self.core_commit),
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
    def verified_w5(_receipt: Path, _evidence: Path) -> dict[str, object]:
        return {"status": "pass", "verifiedReceiptStatus": "pass"}

    @staticmethod
    def verified_content(_android: Path, _core: Path) -> list[str]:
        return []

    def package_authority_payload(self) -> dict[str, object]:
        package_pins = [
            {"package_id": package_id, "version": "1.0.0", "sha256": "1" * 64, "size_bytes": 1}
            for package_id in provenance.CORE_PACKAGE_IDS
        ]
        owner_pins = [
            {"package_id": package_id, "owner": owner, "version": "1.0.0", "sha256": "2" * 64, "size_bytes": 1}
            for package_id, owner in provenance.OWNER_PACKAGE_SPECS
        ]
        return {
            "contractName": provenance.PACKAGE_AUTHORITY_CONTRACT,
            "authorityClass": "internal_phone_beta_only",
            "authorityState": "independently_audited",
            "publicationAuthorized": False,
            "presentationSource": {},
            "authority": {},
            "verificationReceipt": {},
            "dependencyMode": {
                "packageOnly": True, "restoreLockedMode": True,
                "sourceCheckoutsPresent": False, "siblingsAllowed": False,
            },
            "sdkAuthority": {},
            "headlessRuntimeBinding": {},
            "packagePins": package_pins,
            "ownerPackagePins": owner_pins,
            "lockFiles": [],
            "androidConsumerLock": {},
            "doesNotAssert": [],
        }

    def w5_payload(self) -> dict[str, object]:
        return {
            "contractName": provenance.W5_CONTRACT,
            "status": "pass",
            "authorityClass": "internal_phone_beta_only",
            "publicationAuthorized": False,
            "dependencyMode": "locked_package_no_siblings",
            "packageOnly": True,
            "restoreLockedMode": True,
            "sourceCheckoutsPresent": False,
            "siblingsAllowed": False,
            "serializedBuild": True,
            "sdkVersion": provenance.DOTNET_SDK_VERSION,
            "androidCommit": self.w5_commit,
            "androidTree": self.w5_tree,
            "presentationCommit": self.presentation_commit,
            "presentationTree": self.presentation_tree,
            "authorityBindingSha256": provenance.file_sha256(self.package_authority),
            "lockSha256": provenance.W5_LOCK_SHA256,
            "proofScope": "Native.CompileCheck_dependency_only",
            "fullMauiBuild": False,
            "coreDataLangContentVerified": False,
        }

    def source_graph_payload(self) -> dict[str, object]:
        repositories = []
        roles = (
            "app", "runtime", "runtime", "runtime", "contracts_and_validation",
            "contracts", "contracts", "validation",
        )
        for index, (name, role) in enumerate(zip(provenance.REPOSITORY_NAMES, roles, strict=True), start=1):
            commit = f"{index:x}" * 40
            tree = f"{index + 1:x}" * 40
            if name == "chummer-android":
                commit, tree = self.android_commit, self.android_tree
            elif name == "chummer6-ui":
                commit, tree = provenance.PRODUCTION_PRESENTATION_COMMIT, provenance.PRODUCTION_PRESENTATION_TREE
            repositories.append({
                "name": name, "role": role, "commit": commit, "tree": tree,
                "tree_sha256": f"{index:x}" * 64,
                "repository": f"https://github.com/ArchonMegalon/{name}.git",
            })
        by_name = {row["name"]: row for row in repositories}
        authority = self.package_authority_payload()
        package_pins = [
            {
                "package_id": package_id, "version": authority["packagePins"][index - 1]["version"],
                "sha256": authority["packagePins"][index - 1]["sha256"],
                "repository": "chummer6-core", "commit": by_name["chummer6-core"]["commit"],
            }
            for index, package_id in enumerate(provenance.CORE_PACKAGE_IDS, start=1)
        ]
        owner_pins = []
        for index, (package_id, owner) in enumerate(provenance.OWNER_PACKAGE_SPECS, start=7):
            source = by_name[owner]
            owner_pins.append({
                "package_id": package_id,
                "version": authority["ownerPackagePins"][index - 7]["version"],
                "sha256": authority["ownerPackagePins"][index - 7]["sha256"],
                "size_bytes": authority["ownerPackagePins"][index - 7]["size_bytes"],
                "owner_repository": owner,
                "source_commit": source["commit"], "source_tree": source["tree"],
                "authority_receipt_sha256": f"{index:x}" * 64,
                "package_inventory_sha256": f"{index + 1:x}" * 64,
                "package_plane_lock_sha256": f"{index + 2:x}" * 64,
                "dependency_mode": "locked_package",
            })
        closure = [
            {
                "package_id": package_id,
                "dependencies": ["Chummer.Play.Contracts"] if package_id == "Chummer.Run.Contracts" else [],
            }
            for package_id in provenance.OWNER_PACKAGE_IDS
        ]
        return {
            "contractName": provenance.SOURCE_GRAPH_CONTRACT,
            "generatedAtUtc": "2026-08-28T00:00:00Z",
            "authorityState": "local_review_required",
            "publicationAuthorized": False,
            "generator": {
                "path": "scripts/verify_release_source_graph.py",
                "sha256": provenance.file_sha256(self.android / "scripts/verify_release_source_graph.py"),
                "size_bytes": (self.android / "scripts/verify_release_source_graph.py").stat().st_size,
            },
            "repositories": repositories,
            "packagePins": package_pins,
            "ownerPackagePins": owner_pins,
            "dependencyClosure": closure,
            "presentationSource": {
                "repository": "chummer6-ui",
                "commit": provenance.PRODUCTION_PRESENTATION_COMMIT,
                "tree": provenance.PRODUCTION_PRESENTATION_TREE,
                "source_path": "chummer-presentation",
                "authority_state": "local_review_required",
                "publication_authorized": False,
                "dependency_mode": "source_compatibility",
            },
            "doesNotAssert": list(provenance.SOURCE_GRAPH_DOES_NOT_ASSERT),
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
            package_id: {"type": "Transitive", "resolved": "1.0.0", "contentHash": "sha512-fixture"}
            for package_id in (*provenance.CORE_PACKAGE_IDS, *provenance.OWNER_PACKAGE_IDS)
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
        authority = self.package_authority_payload()
        libraries = {
            "Chummer.Desktop.Runtime/1.0.0": {"type": "project"},
            "Chummer.Presentation/1.0.0": {"type": "project"},
        }
        for group in (authority["packagePins"], authority["ownerPackagePins"]):
            for row in group:
                libraries[f"{row['package_id']}/{row['version']}"] = {"type": "package"}
        return {"version": 3, "libraries": libraries}

    def write_journal(self) -> None:
        phases = (
            ("toolchain-intake", self.toolchain_log),
            ("source-graph-intake", self.source_graph_log),
            ("core-content-intake", self.content_source_log),
            ("w5-build-input-intake", self.build_inputs_log),
            ("locked-full-restore", self.restore_log),
            ("serialized-full-maui-build", self.build_log),
            ("apk-content-verification", self.content_apk_log),
            ("post-build-source-graph-seal", self.source_graph_seal_log),
        )
        project = str(self.android / "src/Chummer.Android/Chummer.Android.csproj")
        materializer = str(self.android / "scripts/materialize-api36-physical-build-provenance.py")
        graph_verifier = str(self.android / "scripts/verify_release_source_graph.py")
        content_verifier = str(self.android / "scripts/verify_android_content_bundle.py")
        content_manifest = str(self.content_manifest)
        package_args = [
            f"-p:ChummerPresentationRoot={self.presentation}",
            f"-p:ChummerCoreEngineRoot={self.core}",
            "-p:ChummerDesktopRuntimeIdentifiers=",
            "-p:ChummerUseLocalCompatibilityTree=false",
            "-p:ChummerUseLockedOwnerContractPackages=true",
            "-p:RestoreLockedMode=true", "-p:RestorePackagesWithLockFile=true",
            "-p:NuGetAudit=false", "-p:AndroidSdkBuildToolsVersion=36.0.0",
            "-p:ChummerContractsPackageVersion=0.1.0-packageplane.breaking.shb04ff26f6d538.auth91a48eed5b819",
            "-p:ChummerCoreRuntimePackageVersion=0.1.0-packageplane.breaking.shb04ff26f6d538.auth91a48eed5b819",
            "-p:ChummerCampaignContractsPackageVersion=0.1.0-packageplane.android.sh1215f9389779e",
            "-p:ChummerRunContractsPackageVersion=0.1.0-packageplane.android.sh1215f9389779e",
            "-p:ChummerRunHubContractsPackageVersion=0.1.0-packageplane.android.sh1215f9389779e",
            "-p:ChummerRunHubPackageVersion=0.1.0-packageplane.android.sh1215f9389779e",
            "-p:ChummerHubRegistryContractsPackageVersion=0.1.0-packageplane.candidate.sh66c418a5004f",
            "-p:ChummerUiKitPackageVersion=0.1.0-packageplane.android.shd51ecd99cf720",
        ]
        commands = {
            "toolchain-intake": [
                str(self.python), materializer, "capture-workloads", "--dotnet",
                str(self.dotnet), "--output", str(self.workloads),
            ],
            "source-graph-intake": [
                str(self.python), graph_verifier, "--android-root", str(self.android),
                "--presentation-root", str(self.presentation), "--core-content-root", str(self.core),
                "--workspace-root", str(self.release_workspace), "--package-authority",
                str(self.release_authority_v2), "--verify-existing", str(self.source_graph),
            ],
            "core-content-intake": [
                str(self.python), content_verifier, "--repo-root", str(self.android),
                "--core-root", str(self.core), "--manifest", content_manifest,
                "--receipt", str(self.content_source), "--check",
            ],
            "w5-build-input-intake": [
                str(self.python), materializer, "check-inputs", "--android-root", str(self.android),
                "--presentation-root", str(self.presentation), "--core-content-root", str(self.core),
                "--w5-receipt", str(self.w5_receipt), "--w5-evidence-directory", str(self.w5_evidence),
                "--source-graph", str(self.source_graph), "--package-authority", str(self.package_authority),
                "--release-package-authority-v2", str(self.release_authority_v2),
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
            "apk-content-verification": [
                str(self.python), content_verifier, "--repo-root", str(self.android),
                "--core-root", str(self.core), "--manifest", content_manifest,
                "--apk", str(self.apk), "--receipt", str(self.content_apk), "--check",
            ],
            "post-build-source-graph-seal": [
                str(self.python), graph_verifier, "--android-root", str(self.android),
                "--workspace-root", str(self.release_workspace), "--package-authority",
                str(self.release_authority_v2), "--verify-existing", str(self.source_graph),
            ],
        }
        environment = {key: str(self.root) for key in provenance.ENVIRONMENT_ALLOWLIST}
        environment.update({
            "ANDROID_HOME": str(self.sdk_packages.parent),
            "JAVA_HOME": str(self.java.parent.parent), "NUGET_PACKAGES": str(self.nuget_packages),
            "DOTNET_CLI_TELEMETRY_OPTOUT": "1", "DOTNET_CLI_USE_MSBUILD_SERVER": "0",
            "MSBUILDDISABLENODEREUSE": "1", "LANG": "C.UTF-8", "LC_ALL": "C.UTF-8",
        })
        rows = []
        raw_rows = []
        for phase, output in phases:
            common = {
                "contractName": provenance.COMMAND_JOURNAL_CONTRACT,
                "phase": phase, "argv": commands[phase],
                "workingDirectory": str(self.android), "environment": environment,
                "timeoutSeconds": 100.0, "deadlineEpoch": 2000.0,
                "startedEpoch": 1000.0, "outputPath": str(output),
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
                    "timeoutSeconds": 100.0, "processGroupTermination": True,
                    "publicationAuthorized": False,
                },
                {
                    "contractName": provenance.RAW_COMMAND_JOURNAL_CONTRACT,
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

    def create_arguments(self) -> dict[str, object]:
        return {
            "android_root": self.android,
            "presentation_root": self.presentation,
            "core_content_root": self.core,
            "apk": self.apk,
            "w5_receipt_path": self.w5_receipt,
            "w5_evidence_directory": self.w5_evidence,
            "source_graph_path": self.source_graph,
            "package_authority_path": self.package_authority,
            "release_package_authority_v2_path": self.release_authority_v2,
            "content_source_receipt_path": self.content_source,
            "content_apk_receipt_path": self.content_apk,
            "full_project_lock_path": self.lock,
            "assets_path": self.assets,
            "toolchain_log_path": self.toolchain_log,
            "source_graph_log_path": self.source_graph_log,
            "content_source_log_path": self.content_source_log,
            "build_inputs_log_path": self.build_inputs_log,
            "restore_log_path": self.restore_log,
            "build_log_path": self.build_log,
            "content_apk_log_path": self.content_apk_log,
            "source_graph_seal_log_path": self.source_graph_seal_log,
            "command_journal_path": self.journal,
            "raw_command_journal_path": self.raw_journal,
            "android_sdk_packages_path": self.sdk_packages,
            "dotnet_workloads_path": self.workloads,
            "java_path": self.java, "javac_path": self.javac,
            "dotnet_path": self.dotnet, "python_path": self.python,
            "release_workspace_root": self.release_workspace,
            "package_feed_path": self.package_feed,
            "offline_feed_path": self.offline_feed,
            "nuget_packages_path": self.nuget_packages,
            "android_build_tools_version": "36.0.0",
            "dotnet_version": provenance.DOTNET_SDK_VERSION,
            "w5_verifier": self.verified_w5,
            "content_verifier": self.verified_content,
        }

    def test_full_v2_provenance_round_trip_binds_inputs_without_device_claims(self) -> None:
        manifest = provenance.create_manifest(**self.create_arguments())
        provenance.write_manifest(self.manifest, manifest)
        self.assertEqual(
            manifest,
            provenance.load_and_verify_manifest(self.manifest, **self.create_arguments()),
        )
        self.assertEqual(8, len(manifest["sourceGraph"]["repositories"]))
        self.assertEqual(7, len(manifest["packageAuthority"]["ownerPackagePins"]))
        self.assertTrue(manifest["artifact"]["fullMauiArtifact"])
        self.assertFalse(manifest["artifact"]["installed"])
        self.assertFalse(manifest["publicationAuthorized"])
        self.assertIn("api36_device_execution", manifest["doesNotAssert"])

    def test_w5_receipt_digest_status_and_verifier_are_fail_closed(self) -> None:
        original = self.w5_receipt.read_text(encoding="utf-8")
        payload = json.loads(original)
        payload["status"] = "blocked"
        write_json(self.w5_receipt, payload)
        with self.assertRaisesRegex(ValueError, "digest"):
            provenance.create_manifest(**self.create_arguments())
        self.w5_receipt.write_text(original, encoding="utf-8")
        with mock.patch.object(provenance, "W5_RECEIPT_SHA256", provenance.file_sha256(self.w5_receipt)):
            arguments = self.create_arguments()
            arguments["w5_verifier"] = lambda *_: {"status": "blocked"}
            with self.assertRaisesRegex(ValueError, "committed verifier"):
                provenance.create_manifest(**arguments)

    def test_source_graph_rejects_missing_reordered_misowned_and_stale_inputs(self) -> None:
        cases = []
        missing_repo = self.source_graph_payload()
        missing_repo["repositories"].pop()
        cases.append((missing_repo, "exactly eight"))
        reordered = self.source_graph_payload()
        reordered["repositories"][0], reordered["repositories"][1] = reordered["repositories"][1], reordered["repositories"][0]
        cases.append((reordered, "order/set"))
        misowned = self.source_graph_payload()
        misowned["ownerPackagePins"][0]["owner_repository"] = "chummer6-ui-kit"
        cases.append((misowned, "owner package authority"))
        missing_play = self.source_graph_payload()
        run = next(row for row in missing_play["dependencyClosure"] if row["package_id"] == "Chummer.Run.Contracts")
        run["dependencies"] = []
        cases.append((missing_play, "transitive Chummer.Play"))
        stale = self.source_graph_payload()
        stale["contractName"] = "chummer.android.release-source-graph/v1"
        cases.append((stale, "authority posture"))
        for payload, error in cases:
            with self.subTest(error=error):
                write_json(self.source_graph, payload)
                with self.assertRaisesRegex(ValueError, error):
                    provenance.create_manifest(**self.create_arguments())

    def test_package_authority_tamper_fails_closed(self) -> None:
        payload = json.loads(self.package_authority.read_text(encoding="utf-8"))
        payload["ownerPackagePins"][0]["package_id"] = "Forged"
        write_json(self.package_authority, payload)
        subprocess.run(["git", "add", "."], cwd=self.android, check=True)
        subprocess.run(["git", "commit", "-qm", "tamper"], cwd=self.android, check=True)
        with self.assertRaisesRegex(ValueError, "product source changed|W5-bound"):
            provenance.create_manifest(**self.create_arguments())

    def test_graph_package_authority_and_generator_are_cross_bound(self) -> None:
        graph = self.source_graph_payload()
        graph["ownerPackagePins"][0]["sha256"] = "3" * 64
        write_json(self.source_graph, graph)
        with self.assertRaisesRegex(ValueError, "owner package authority cross-binding"):
            provenance.create_manifest(**self.create_arguments())
        graph = self.source_graph_payload()
        graph["generator"]["sha256"] = "f" * 64
        write_json(self.source_graph, graph)
        with self.assertRaisesRegex(ValueError, "generator bytes"):
            provenance.create_manifest(**self.create_arguments())

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
        del assets["libraries"]["Chummer.Ui.Kit/1.0.0"]
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
            (lambda rows: rows[0]["argv"].append("--forged"), "argv is not exact"),
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
        output = self.root / "wrapper-output.log"
        environment = {key: str(self.root) for key in provenance.ENVIRONMENT_ALLOWLIST}
        environment.update({
            "ANDROID_HOME": str(self.root), "JAVA_HOME": str(self.root),
            "NUGET_PACKAGES": str(self.nuget_packages),
            "DOTNET_CLI_TELEMETRY_OPTOUT": "1", "DOTNET_CLI_USE_MSBUILD_SERVER": "0",
            "MSBUILDDISABLENODEREUSE": "1", "LANG": "C.UTF-8", "LC_ALL": "C.UTF-8",
        })
        command = [
            sys.executable, str(REPO_ROOT / "scripts/materialize-api36-physical-build-provenance.py"),
            "run-bounded", "--journal", str(canonical), "--raw-journal", str(raw),
            "--output", str(output), "--phase", "toolchain-intake",
            "--timeout-seconds", "20", "--deadline-epoch", str(time.time() + 60),
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
        self.assertEqual(["started", "finished"], [row["event"] for row in canonical_rows])
        self.assertEqual(["started", "finished"], [row["event"] for row in raw_rows])
        self.assertEqual(canonical_rows[0]["argv"], raw_rows[0]["command"])
        self.assertEqual(provenance.file_sha256(output), canonical_rows[1]["outputSha256"])
        self.assertEqual(provenance.COMMAND_JOURNAL_CONTRACT, canonical_rows[0]["contractName"])
        self.assertEqual(provenance.RAW_COMMAND_JOURNAL_CONTRACT, raw_rows[0]["contractName"])

    def test_six_descriptor_snapshot_swap_races_are_rejected(self) -> None:
        targets = (
            ("w5 receipt", self.w5_receipt),
            ("v2 source graph", self.source_graph),
            ("v2 package authority", self.release_authority_v2),
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

    def test_remaining_authority_lock_asset_log_and_w5_evidence_swaps_are_rejected(self) -> None:
        targets = (
            self.w5_evidence / "authority-binding.json",
            self.package_authority,
            self.presentation_lock,
            self.desktop_lock,
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
            (self.presentation / "source.txt", "W5 Presentation build source is dirty"),
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
            (self.sdk_packages, b"not xml", "well-formed XML"),
            (
                self.sdk_packages,
                b'<sdk:sdk-repository xmlns:sdk="urn:android"><localPackage path="platform-tools" /></sdk:sdk-repository>',
                "selected API36 toolchain",
            ),
            (self.java, b"#!/bin/sh\nprintf 'arbitrary java\\n'\n", "not canonical"),
            (self.javac, b"#!/bin/sh\nprintf 'javac 22.0.1\\n'\n", "do not match"),
            (self.workloads, b'{"installed":[],"updateAvailable":[]}\n', "maui-android"),
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
            "run-bounded", "capture-workloads", "verify_release_source_graph.py",
            "verify_android_content_bundle.py", "check-inputs", "materialize",
            "--framework net10.0-android36.0", "--runtime android-arm64",
            "-p:AndroidPackageFormats=apk", "-m:1", "--warnaserror",
            "9037d4afc11dd8661dfbcccbc67a9f814d110fb17cf985cf215268e12ae3583e",
            "568fd2c602494329d19fbe8d9a2c83a4c2e82754b50e31141b192c1af7ccf964",
            "202a29a35b4768c3306349ee40a34d8f23ada97c0b0ef11e104763b5ff9cc60e",
            'java_command="${CHUMMER_JAVA:-java}"',
            'javac_command="${CHUMMER_JAVAC:-javac}"',
            "AndroidSdkBuildToolsVersion=$android_build_tools_version",
        )
        for fragment in required:
            with self.subTest(fragment=fragment):
                self.assertIn(fragment, script)
        self.assertNotIn("https://", script)
        self.assertNotIn("dotnet publish", script)
        self.assertNotIn(" adb ", script)
        self.assertNotIn("ChummerUseLocalCompatibilityTree=true", script)
        self.assertNotIn("ChummerUseLockedOwnerContractPackages=false", script)

    def test_committed_full_project_lock_is_exact_arm64_w5_closure(self) -> None:
        lock_path = REPO_ROOT / "src/Chummer.Android/packages.lock.json"
        lock = provenance.validate_full_project_lock(lock_path)
        self.assertEqual(
            "9037d4afc11dd8661dfbcccbc67a9f814d110fb17cf985cf215268e12ae3583e",
            provenance.file_sha256(lock_path),
        )
        self.assertEqual(72_165, lock_path.stat().st_size)
        self.assertEqual(144, len(lock["dependencies"][provenance.TARGET_FRAMEWORK]))


if __name__ == "__main__":
    unittest.main()
