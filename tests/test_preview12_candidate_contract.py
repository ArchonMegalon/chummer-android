from __future__ import annotations

import copy
import hashlib
import importlib.util
import json
import shutil
import struct
import subprocess
import sys
import tempfile
import unittest
import zipfile
from pathlib import Path


REPO = Path(__file__).resolve().parents[1]
CONTRACT_PATH = REPO / "scripts/preview12_candidate_contract.py"
NORMALIZER_PATH = REPO / "scripts/normalize_preview12_unsigned_aab.py"


def load(path: Path, name: str):
    spec = importlib.util.spec_from_file_location(name, path)
    assert spec is not None and spec.loader is not None
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


candidate = load(CONTRACT_PATH, "preview12_candidate_tests")
normalizer = load(NORMALIZER_PATH, "preview12_normalizer_tests")


def sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def content_fixture() -> tuple[bytes, dict[str, bytes]]:
    payloads = {
        "data/lifemodules.xml": b"<chummer />\n",
        "lang/en-us.xml": b"<chummer />\n",
    }
    files = [
        {"path": name, "size": len(data), "sha256": sha256(data)}
        for name, data in sorted(payloads.items())
    ]
    manifest = {
        "schema": candidate.CONTENT.SCHEMA,
        "coreRevision": candidate.CONTENT.CORE_REVISION,
        "bundleDigest": candidate.CONTENT._bundle_digest(files),
        "files": files,
    }
    return (json.dumps(manifest, indent=2) + "\n").encode(), payloads


def lz4_literals(data: bytes) -> bytes:
    literal_length = len(data)
    token_length = min(literal_length, 15)
    encoded = bytearray([token_length << 4])
    remainder = literal_length - token_length
    if token_length == 15:
        while remainder >= 255:
            encoded.append(255)
            remainder -= 255
        encoded.append(remainder)
    encoded.extend(data)
    return bytes(encoded)


def assembly_store_payload(image: bytes) -> bytes:
    name = b"Chummer.Android.dll"
    compressed = b"XALZ" + struct.pack("<II", 0, len(image)) + lz4_literals(image)
    header_size = 20
    index_size = 13
    descriptor_size = 28
    names_size = 4 + len(name)
    data_offset = header_size + index_size + descriptor_size + names_size
    return b"".join(
        (
            struct.pack("<IIIII", 0x41424158, 0x80010003, 1, 1, index_size),
            struct.pack("<QIB", 0x1234, 0, 0),
            struct.pack("<IIIIIII", 0, data_offset, len(compressed), 0, 0, 0, 0),
            struct.pack("<I", len(name)),
            name,
            compressed,
        )
    )


def elf(payload: bytes) -> bytes:
    strings = b"\x00.shstrtab\x00payload\x00"
    payload_offset = 128
    section_offset = (payload_offset + len(payload) + 7) & ~7
    image = bytearray(section_offset + 3 * 64)
    struct.pack_into(
        "<16sHHIQQQIHHHHHH",
        image,
        0,
        b"\x7fELF\x02\x01\x01" + b"\x00" * 9,
        3,
        183,
        1,
        0,
        0,
        section_offset,
        0,
        64,
        0,
        0,
        64,
        3,
        1,
    )
    image[64 : 64 + len(strings)] = strings
    image[payload_offset : payload_offset + len(payload)] = payload
    struct.pack_into("<IIQQQQIIQQ", image, section_offset + 64, 1, 3, 0, 0, 64, len(strings), 0, 0, 1, 0)
    struct.pack_into(
        "<IIQQQQIIQQ",
        image,
        section_offset + 128,
        11,
        1,
        0,
        0,
        payload_offset,
        len(payload),
        0,
        0,
        8,
        0,
    )
    return bytes(image)


def write_aab(path: Path, extra: bytes = b"") -> None:
    content_manifest, content_files = content_fixture()
    with zipfile.ZipFile(path, "w", compression=zipfile.ZIP_DEFLATED) as archive:
        archive.writestr(
            "base/lib/arm64-v8a/libassembly-store.so",
            elf(assembly_store_payload(b"MZ\x00ordinary-release-assembly")),
        )
        archive.writestr("base/assets/fixture.bin", b"fixture" + extra)
        archive.writestr("base/assets/chummer-content/manifest.json", content_manifest)
        for name, data in content_files.items():
            archive.writestr(f"base/assets/chummer-content/{name}", data)


def write_manifest(path: Path) -> None:
    path.write_text(
        '<manifest xmlns:android="http://schemas.android.com/apk/res/android" '
        'package="com.myexternalbrain.chummer" android:compileSdkVersion="36" '
        'android:versionCode="12" android:versionName="0.1.0-preview.12">'
        '<uses-sdk android:minSdkVersion="24" android:targetSdkVersion="36" />'
        '</manifest>\n',
        encoding="utf-8",
    )


class Preview12CandidateContractTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name).resolve()
        self.android = self.root / "android"
        self.android.mkdir()
        self.original_candidate_repo_root = candidate.REPO_ROOT
        candidate.REPO_ROOT = self.android
        subprocess.run(["git", "init", "-q", str(self.android)], check=True)
        subprocess.run(
            ["git", "-C", str(self.android), "remote", "add", "origin", candidate.SOURCE_REPOSITORY],
            check=True,
        )
        project = self.android / candidate.PROJECT_PATH
        project.parent.mkdir(parents=True)
        project.write_text(
            "<Project><PropertyGroup>"
            "<TargetFramework>net10.0-android36.0</TargetFramework>"
            "<MauiVersion>10.0.20</MauiVersion>"
            "<ApplicationId>com.myexternalbrain.chummer</ApplicationId>"
            "<ApplicationDisplayVersion>0.1.0-preview.12</ApplicationDisplayVersion>"
            "<ApplicationVersion>12</ApplicationVersion>"
            "<TargetSdkVersion>36</TargetSdkVersion>"
            "<AndroidMinSdkVersion>24</AndroidMinSdkVersion>"
            "</PropertyGroup></Project>\n",
            encoding="utf-8",
        )
        for relative in (candidate.PRODUCER_WORKFLOW, candidate.VERIFIER_WORKFLOW):
            target = self.android / relative
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(REPO / relative, target)
        proof_root = self.android / "src/Chummer.Android/Proof"
        proof_root.mkdir(parents=True)
        for source in (REPO / "src/Chummer.Android/Proof").glob("*.cs"):
            shutil.copyfile(source, proof_root / source.name)
        subprocess.run(["git", "-C", str(self.android), "add", "."], check=True)
        subprocess.run(
            [
                "git", "-C", str(self.android), "-c", "user.name=Candidate Test",
                "-c", "user.email=test@example.invalid", "commit", "-qm", "fixture",
            ],
            check=True,
        )
        self.commit = self.git("rev-parse", "HEAD")
        self.tree = self.git("rev-parse", "HEAD^{tree}")
        self.policy = self.root / "policy.json"
        self.policy.write_bytes((REPO / "eng/preview12-unsigned-candidate-authority.json").read_bytes())
        self.policy_binding = candidate._binding(
            candidate.StableFile(self.policy, "policy", candidate.MAX_JSON_BYTES),
            "eng/preview12-unsigned-candidate-authority.json",
        )
        content_manifest, _ = content_fixture()
        content_path = self.android / candidate.CONTENT_MANIFEST_PATH
        content_path.parent.mkdir(parents=True, exist_ok=True)
        content_path.write_bytes(content_manifest)
        subprocess.run(["git", "-C", str(self.android), "add", "."], check=True)
        subprocess.run(
            [
                "git", "-C", str(self.android), "-c", "user.name=Candidate Test",
                "-c", "user.email=test@example.invalid", "commit", "-qm", "content fixture",
            ],
            check=True,
        )
        self.commit = self.git("rev-parse", "HEAD")
        self.tree = self.git("rev-parse", "HEAD^{tree}")
        self.two_green = self.root / "two-green.json"
        self.two_green.write_bytes(candidate.pretty_bytes(self.two_green_value()))
        self.producer_toolchain = self.root / "producer-toolchain.json"
        self.producer_toolchain.write_bytes(candidate.pretty_bytes(self.toolchain_value("producer")))
        self.verifier_toolchain = self.root / "verifier-toolchain.json"
        self.verifier_toolchain.write_bytes(candidate.pretty_bytes(self.toolchain_value("verifier")))
        self.producer_dir = self.root / "producer"
        self.producer_dir.mkdir()
        self.producer_aab = self.producer_dir / normalizer.EXPECTED_OUTPUT
        write_aab(self.producer_aab)
        self.producer_manifest = self.producer_dir / "AndroidManifest.xml"
        write_manifest(self.producer_manifest)

    def tearDown(self) -> None:
        candidate.REPO_ROOT = self.original_candidate_repo_root
        self.temporary.cleanup()

    def git(self, *args: str) -> str:
        return subprocess.run(
            ["git", "-C", str(self.android), *args],
            check=True,
            capture_output=True,
            text=True,
        ).stdout.strip()

    def two_green_value(self) -> dict[str, object]:
        sources: dict[str, object] = {
            "android": {"repository": candidate.SOURCE_REPOSITORY, "tree": self.tree}
        }
        dependencies = candidate.expected_policy()["dependencies"]
        assert isinstance(dependencies, dict)
        for index, (name, authority) in enumerate(sorted(dependencies.items()), start=1):
            assert isinstance(authority, dict)
            sources[name] = {
                "repository": authority["repository"],
                "commit": authority["commit"],
                "tree": f"{index:x}" * 40,
            }
        graph = {
            "mode": {"localCompatibilityTree": True, "packageOnly": False},
            "sources": sources,
        }
        graph["sha256"] = candidate.digest_object(graph)
        unsigned = {
            "schema": candidate.TWO_GREEN.CONTRACT,
            "status": "pass",
            "eligibilityScope": candidate.TWO_GREEN.ELIGIBILITY_SCOPE,
            "eligible": True,
            "internalTestingEligible": True,
            "publicationAuthorized": False,
            "googlePlayUploadAuthorized": False,
            "policyAuthority": {},
            "sourceCommit": self.commit,
            "sourceTree": self.tree,
            "releaseIdentity": {
                "packageId": candidate.PACKAGE_ID,
                "versionName": candidate.VERSION_NAME,
                "versionCode": candidate.VERSION_CODE,
                "intentAuthority": "android_project_at_exact_main_tree",
            },
            "commonAuthority": {
                "androidTree": self.tree,
                "dependencyGraph": graph,
                "environmentCompatibilityStatus": "pass",
            },
            "reviewRun": {},
            "mainRun": {
                "run": {"headSha": self.commit},
                "p0EventSha": self.commit,
                "aggregateStatus": "pass",
            },
            "decisionTimeUtc": "2026-09-06T10:00:00Z",
            "reviewPullRequest": {},
            "doesNotAssert": list(candidate.TWO_GREEN.DOES_NOT_ASSERT),
        }
        return {**unsigned, "eligibilitySha256": candidate.digest_object(unsigned)}

    def toolchain_value(self, observation: str) -> dict[str, object]:
        required = candidate.expected_policy()["toolchain"]
        assert isinstance(required, dict)
        compatibility = {
            **required,
            "androidPackagesSha256": "a" * 64,
            "dotnetWorkloadsSha256": "b" * 64,
            "buildToolsVersions": ["36.0.0"],
        }
        observed = {
            "runnerImage": "ubuntu24",
            "runnerImageVersion": observation,
            "dotnetInfo": {"sha256": "c" * 64, "sizeBytes": 100},
            "javaVersion": {"sha256": "d" * 64, "sizeBytes": 100, "major": 17},
            "androidPackages": {"sha256": "a" * 64, "sizeBytes": 100},
            "dotnetWorkloads": {"sha256": "b" * 64, "sizeBytes": 100},
            "adb": {"sha256": "e" * 64, "sizeBytes": 100},
            "buildToolsVersions": ["36.0.0"],
        }
        unsigned = {
            "schema": candidate.TOOLCHAIN_SCHEMA,
            "status": "pass",
            "policyAuthority": self.policy_binding,
            "compatibility": compatibility,
            "compatibilitySha256": candidate.digest_object(compatibility),
            "observed": observed,
            "signingInputsPresent": False,
            "publicationAuthorized": False,
        }
        return {**unsigned, "observationSha256": candidate.digest_object(unsigned)}

    def producer_inputs(self) -> dict[str, object]:
        return {
            "android_root": self.android,
            "policy_path": self.policy,
            "two_green_path": self.two_green,
            "toolchain_path": self.producer_toolchain,
            "aab_path": self.producer_aab,
            "manifest_path": self.producer_manifest,
            "workflow_path": self.android / candidate.PRODUCER_WORKFLOW,
            "github_run_id": 100,
            "github_run_attempt": 1,
            "github_sha": self.commit,
            "github_ref": candidate.MAIN_REF,
            "two_green_run_id": 90,
            "two_green_artifact_id": 91,
            "two_green_artifact_digest": "sha256:" + "c" * 64,
        }

    def rebuild_inputs(self, producer_receipt: Path, rebuilt_aab: Path, rebuilt_manifest: Path) -> dict[str, object]:
        return {
            "android_root": self.android,
            "policy_path": self.policy,
            "two_green_path": self.two_green,
            "producer_path": producer_receipt,
            "producer_aab_path": self.producer_aab,
            "producer_manifest_path": self.producer_manifest,
            "toolchain_path": self.verifier_toolchain,
            "rebuilt_aab_path": rebuilt_aab,
            "rebuilt_manifest_path": rebuilt_manifest,
            "workflow_path": self.android / candidate.VERIFIER_WORKFLOW,
            "github_run_id": 200,
            "github_run_attempt": 1,
            "github_sha": self.commit,
            "github_ref": candidate.MAIN_REF,
            "producer_run_id": 100,
            "producer_artifact_id": 101,
            "producer_artifact_digest": "sha256:" + "d" * 64,
        }

    def materialize_producer(self) -> tuple[dict[str, object], Path]:
        receipt = candidate.create_producer(**self.producer_inputs())
        path = self.root / candidate.PRODUCER_OUTPUT
        path.write_bytes(candidate.pretty_bytes(receipt))
        return receipt, path

    def test_producer_rebuild_and_signer_eligibility_require_exact_agreement(self) -> None:
        producer, producer_path = self.materialize_producer()
        rebuilt_dir = self.root / "rebuilt"
        rebuilt_dir.mkdir()
        rebuilt_aab = rebuilt_dir / normalizer.EXPECTED_OUTPUT
        shutil.copyfile(self.producer_aab, rebuilt_aab)
        rebuilt_manifest = rebuilt_dir / "AndroidManifest.xml"
        shutil.copyfile(self.producer_manifest, rebuilt_manifest)
        rebuild = candidate.create_rebuild(
            **self.rebuild_inputs(producer_path, rebuilt_aab, rebuilt_manifest)
        )
        eligibility = candidate.create_signer_eligibility(producer, rebuild)

        self.assertTrue(eligibility["signerEligible"])
        self.assertFalse(eligibility["signingAuthorized"])
        self.assertFalse(eligibility["googlePlayUploadAuthorized"])
        self.assertFalse(eligibility["publicationAuthorized"])
        self.assertEqual(producer["artifact"]["sha256"], eligibility["unsignedAab"]["sha256"])
        candidate.validate_signer_eligibility(eligibility)

    def test_rebuild_rejects_one_byte_artifact_difference(self) -> None:
        _, producer_path = self.materialize_producer()
        rebuilt_dir = self.root / "different"
        rebuilt_dir.mkdir()
        rebuilt_aab = rebuilt_dir / normalizer.EXPECTED_OUTPUT
        write_aab(rebuilt_aab, b"different")
        rebuilt_manifest = rebuilt_dir / "AndroidManifest.xml"
        write_manifest(rebuilt_manifest)
        with self.assertRaisesRegex(ValueError, "rebuilt AAB bytes"):
            candidate.create_rebuild(
                **self.rebuild_inputs(producer_path, rebuilt_aab, rebuilt_manifest)
            )

    def test_rebuild_rejects_toolchain_compatibility_drift(self) -> None:
        _, producer_path = self.materialize_producer()
        value = self.toolchain_value("verifier")
        value["compatibility"]["buildToolsVersions"] = ["37.0.0"]
        value["observed"]["buildToolsVersions"] = ["37.0.0"]
        value["compatibilitySha256"] = candidate.digest_object(value["compatibility"])
        unsigned = {key: member for key, member in value.items() if key != "observationSha256"}
        value["observationSha256"] = candidate.digest_object(unsigned)
        self.verifier_toolchain.write_bytes(candidate.pretty_bytes(value))
        rebuilt_dir = self.root / "rebuilt-toolchain"
        rebuilt_dir.mkdir()
        rebuilt_aab = rebuilt_dir / normalizer.EXPECTED_OUTPUT
        shutil.copyfile(self.producer_aab, rebuilt_aab)
        rebuilt_manifest = rebuilt_dir / "AndroidManifest.xml"
        shutil.copyfile(self.producer_manifest, rebuilt_manifest)
        with self.assertRaisesRegex(ValueError, "toolchain compatibility differs"):
            candidate.create_rebuild(
                **self.rebuild_inputs(producer_path, rebuilt_aab, rebuilt_manifest)
            )

    def test_producer_rejects_stale_two_green_tree(self) -> None:
        value = self.two_green_value()
        value["sourceTree"] = "f" * 40
        unsigned = {key: member for key, member in value.items() if key != "eligibilitySha256"}
        value["eligibilitySha256"] = candidate.digest_object(unsigned)
        self.two_green.write_bytes(candidate.pretty_bytes(value))
        with self.assertRaisesRegex(ValueError, "common environment authority|exact Preview.12 main tree"):
            candidate.create_producer(**self.producer_inputs())

    def test_receipt_digest_tampering_fails_closed(self) -> None:
        producer, _ = self.materialize_producer()
        producer["artifact"]["sha256"] = "e" * 64
        with self.assertRaisesRegex(
            ValueError, "artifact authority differs|proof-exclusion authority differs|candidate digest differs"
        ):
            candidate.validate_producer(producer)

    def test_producer_rejects_rehashed_content_authority_tampering(self) -> None:
        producer, _ = self.materialize_producer()
        producer["contentAuthority"]["verifier"]["sha256"] = "e" * 64
        unsigned = {
            key: value
            for key, value in producer.items()
            if key != "candidateSha256"
        }
        producer["candidateSha256"] = candidate.digest_object(unsigned)
        with self.assertRaisesRegex(ValueError, "Core-content authority differs"):
            candidate.validate_producer(producer)

    def test_producer_rejects_aab_without_canonical_core_content(self) -> None:
        with zipfile.ZipFile(self.producer_aab, "w", compression=zipfile.ZIP_DEFLATED) as archive:
            archive.writestr(
                "base/lib/arm64-v8a/libassembly-store.so",
                elf(assembly_store_payload(b"MZ\x00ordinary-release-assembly")),
            )
        with self.assertRaisesRegex(ValueError, "Core-content authority failed"):
            candidate.create_producer(**self.producer_inputs())

    def test_signer_eligibility_cannot_escalate_signing_or_publication(self) -> None:
        producer, producer_path = self.materialize_producer()
        rebuilt_dir = self.root / "authorization-rebuild"
        rebuilt_dir.mkdir()
        rebuilt_aab = rebuilt_dir / normalizer.EXPECTED_OUTPUT
        shutil.copyfile(self.producer_aab, rebuilt_aab)
        rebuilt_manifest = rebuilt_dir / "AndroidManifest.xml"
        shutil.copyfile(self.producer_manifest, rebuilt_manifest)
        rebuild = candidate.create_rebuild(
            **self.rebuild_inputs(producer_path, rebuilt_aab, rebuilt_manifest)
        )
        eligibility = candidate.create_signer_eligibility(producer, rebuild)
        for field in (
            "signingAuthorized",
            "googlePlayUploadAuthorized",
            "publicationAuthorized",
        ):
            with self.subTest(field=field):
                tampered = copy.deepcopy(eligibility)
                tampered[field] = True
                unsigned = {
                    key: value
                    for key, value in tampered.items()
                    if key != "eligibilitySha256"
                }
                tampered["eligibilitySha256"] = candidate.digest_object(unsigned)
                with self.assertRaisesRegex(ValueError, "authority posture differs"):
                    candidate.validate_signer_eligibility(tampered)


class Preview12NormalizerAndWorkflowTests(unittest.TestCase):
    def test_normalizer_is_deterministic_across_order_and_timestamps(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary).resolve()
            sources = []
            for index, order in enumerate((("b", "a"), ("a", "b")), start=1):
                source = root / f"source-{index}.aab"
                with zipfile.ZipFile(source, "w") as archive:
                    for name in order:
                        info = zipfile.ZipInfo(f"base/assets/{name}", (2020 + index, 1, 2, 3, 4, 6))
                        archive.writestr(info, name.encode())
                sources.append(source)
            outputs = []
            for index, source in enumerate(sources, start=1):
                directory = root / f"output-{index}"
                directory.mkdir()
                output = directory / normalizer.EXPECTED_OUTPUT
                normalizer.normalize(source, output)
                outputs.append(output.read_bytes())
            self.assertEqual(outputs[0], outputs[1])

    def test_normalizer_rejects_preexisting_signature(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary).resolve()
            source = root / "signed.aab"
            with zipfile.ZipFile(source, "w") as archive:
                archive.writestr("META-INF/UPLOAD.RSA", b"signature")
            output = root / normalizer.EXPECTED_OUTPUT
            with self.assertRaisesRegex(ValueError, "already contains a JAR signature"):
                normalizer.normalize(source, output)

    def test_normalizer_rejects_noncanonical_and_special_members(self) -> None:
        for member in ("base//assets/value", "base/./assets/value"):
            with self.subTest(member=member), tempfile.TemporaryDirectory() as temporary:
                root = Path(temporary).resolve()
                source = root / "unsafe.aab"
                with zipfile.ZipFile(source, "w") as archive:
                    archive.writestr(member, b"value")
                with self.assertRaisesRegex(ValueError, "unsafe AAB member"):
                    normalizer.normalize(source, root / normalizer.EXPECTED_OUTPUT)

        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary).resolve()
            source = root / "type-mismatch.aab"
            with zipfile.ZipFile(source, "w") as archive:
                member = zipfile.ZipInfo("base/assets/value/")
                member.create_system = 3
                member.external_attr = 0o100644 << 16
                archive.writestr(member, b"")
            with self.assertRaisesRegex(ValueError, "type/name mismatch"):
                normalizer.normalize(source, root / normalizer.EXPECTED_OUTPUT)

    def test_both_workflows_are_manual_read_only_and_non_promoting(self) -> None:
        producer = (REPO / candidate.PRODUCER_WORKFLOW).read_text(encoding="utf-8")
        verifier = (REPO / candidate.VERIFIER_WORKFLOW).read_text(encoding="utf-8")
        for workflow in (producer, verifier):
            self.assertIn("workflow_dispatch:", workflow)
            self.assertNotIn("\n  push:", workflow)
            self.assertNotIn("\n  pull_request:", workflow)
            self.assertNotIn("\n  workflow_run:", workflow)
            self.assertNotIn("environment:", workflow)
            self.assertNotIn("secrets.", workflow)
            self.assertNotIn("id-token: write", workflow)
            self.assertIn("actions: read", workflow)
            self.assertIn("contents: read", workflow)
            self.assertNotIn("google-github-actions", workflow)
            self.assertIn("verify_android_content_bundle.py", workflow)
            self.assertIn("CHUMMER_CORE_RUNTIME_ROOT:", workflow)
            self.assertIn("CHUMMER_CORE_CONTENT_ROOT:", workflow)
            policy = json.loads(candidate.POLICY_PATH.read_text(encoding="utf-8"))
            for dependency in policy["dependencies"].values():
                self.assertIn(f"ref: {dependency['commit']}", workflow)
        self.assertNotIn("signer-eligibility", producer)
        self.assertIn("signer-eligibility", verifier)
        self.assertNotIn("play.google.com", verifier)
        self.assertIn(".size_in_bytes", producer)
        self.assertIn("artifact_digest#sha256:", producer)
        self.assertIn(".size_in_bytes", verifier)
        self.assertIn("PRODUCER_ARTIFACT_DIGEST#sha256:", verifier)
        self.assertIn("os.O_EXCL", verifier)
        self.assertIn("cmp --silent", verifier)
        self.assertNotIn("target.write_bytes(archive.read(row))", verifier)

    def test_build_script_explicitly_disables_signing(self) -> None:
        build = (REPO / "scripts/build-preview12-unsigned-candidate.sh").read_text(encoding="utf-8")
        self.assertIn("-p:AndroidKeyStore=false", build)
        self.assertIn("signing-or-publication-input-present", build)
        self.assertNotIn("jarsigner", build)
        self.assertNotIn("PlayPublisher", build)
        self.assertIn("CHUMMER_CORE_RUNTIME_ROOT", build)
        self.assertIn("CHUMMER_CORE_CONTENT_ROOT", build)
        self.assertIn("-p:ChummerCoreEngineRoot=$core_content_root", build)
        self.assertIn(
            "-p:ChummerLocalContractsProject=$core_runtime_root/Chummer.Contracts/Chummer.Contracts.csproj",
            build,
        )


if __name__ == "__main__":
    unittest.main()
