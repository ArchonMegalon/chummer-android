#!/usr/bin/env python3
"""Fail-closed contracts for the unsigned Preview.12 candidate lanes.

The producer consumes one exact two-green eligibility receipt and emits an
unsigned ARM64 AAB receipt.  A distinct workflow run independently rebuilds
the same tree and may emit signer-eligibility only when the normalized AAB
bytes and every release/toolchain authority agree.  None of these contracts
authorizes signing, Google Play upload, deployment, or publication.
"""

from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import stat
import subprocess
import sys
import tempfile
from typing import Any
import xml.etree.ElementTree as ET


SCRIPT_DIRECTORY = Path(__file__).resolve().parent
REPO_ROOT = SCRIPT_DIRECTORY.parent
POLICY_PATH = REPO_ROOT / "eng/preview12-unsigned-candidate-authority.json"
TWO_GREEN_PATH = SCRIPT_DIRECTORY / "materialize-api36-two-green-eligibility.py"
PROOF_VERIFIER_PATH = SCRIPT_DIRECTORY / "verify_release_aab_excludes_api36_proof.py"
CONTENT_VERIFIER_PATH = SCRIPT_DIRECTORY / "verify_android_content_bundle.py"
CONTENT_MANIFEST_PATH = "src/Chummer.Android/Content/chummer-content-manifest.json"
PROJECT_PATH = "src/Chummer.Android/Chummer.Android.csproj"
PRODUCER_WORKFLOW = ".github/workflows/preview12-unsigned-candidate-producer.yml"
VERIFIER_WORKFLOW = ".github/workflows/preview12-independent-candidate-verifier.yml"
PRODUCER_OUTPUT = "PREVIEW12_UNSIGNED_CANDIDATE.generated.json"
REBUILD_OUTPUT = "PREVIEW12_INDEPENDENT_REBUILD.generated.json"
ELIGIBILITY_OUTPUT = "PREVIEW12_SIGNER_ELIGIBILITY.generated.json"
POLICY_SCHEMA = "chummer.android.preview12-unsigned-candidate-policy/v1"
TOOLCHAIN_SCHEMA = "chummer.android.preview12-candidate-toolchain/v1"
PRODUCER_SCHEMA = "chummer.android.preview12-unsigned-candidate/v1"
REBUILD_SCHEMA = "chummer.android.preview12-independent-rebuild/v1"
ELIGIBILITY_SCHEMA = "chummer.android.preview12-signer-eligibility/v1"
PACKAGE_ID = "com.myexternalbrain.chummer"
VERSION_NAME = "0.1.0-preview.12"
VERSION_CODE = 12
COMPILE_SDK = 36
TARGET_SDK = 36
MINIMUM_SDK = 24
RUNTIME_IDENTIFIER = "android-arm64"
CONFIGURATION = "Release"
PACKAGE_FORMAT = "aab"
SOURCE_REPOSITORY = "https://github.com/ArchonMegalon/chummer-android.git"
MAIN_REF = "refs/heads/main"
MAX_JSON_BYTES = 16 * 1024 * 1024
MAX_AAB_BYTES = 512 * 1024 * 1024
SHA40 = re.compile(r"^[0-9a-f]{40}$")
SHA256 = re.compile(r"^[0-9a-f]{64}$")
ARTIFACT_DIGEST = re.compile(r"^sha256:[0-9a-f]{64}$")
ANDROID = "{http://schemas.android.com/apk/res/android}"
DOES_NOT_ASSERT = (
    "release_signing",
    "google_play_upload",
    "google_play_processing",
    "tester_distribution",
    "tester_installation",
    "public_release_readiness",
    "publication_authority",
)


def _load_module(path: Path, name: str) -> Any:
    specification = importlib.util.spec_from_file_location(name, path)
    if specification is None or specification.loader is None:
        raise ValueError(f"cannot load {name}")
    module = importlib.util.module_from_spec(specification)
    sys.modules[specification.name] = module
    specification.loader.exec_module(module)
    return module


TWO_GREEN = _load_module(TWO_GREEN_PATH, "preview12_two_green_contract")
PROOF = _load_module(PROOF_VERIFIER_PATH, "preview12_proof_exclusion")
CONTENT = _load_module(CONTENT_VERIFIER_PATH, "preview12_content_authority")


def canonical_bytes(value: object) -> bytes:
    return (json.dumps(value, sort_keys=True, separators=(",", ":")) + "\n").encode()


def pretty_bytes(value: object) -> bytes:
    return (json.dumps(value, indent=2, sort_keys=True) + "\n").encode()


def digest_object(value: object) -> str:
    return hashlib.sha256(canonical_bytes(value)).hexdigest()


def _duplicates(pairs: list[tuple[str, object]]) -> dict[str, object]:
    result: dict[str, object] = {}
    for key, value in pairs:
        if key in result:
            raise ValueError(f"duplicate JSON key: {key}")
        result[key] = value
    return result


class StableFile:
    def __init__(
        self, path: Path, label: str, limit: int, *, retain_data: bool = True
    ) -> None:
        if not path.is_absolute() or path.is_symlink() or path.resolve(strict=True) != path:
            raise ValueError(f"{label} must be an absolute canonical non-symlink file")
        descriptor = os.open(path, os.O_RDONLY | getattr(os, "O_NOFOLLOW", 0))
        try:
            before = os.fstat(descriptor)
            if not stat.S_ISREG(before.st_mode) or before.st_size <= 0 or before.st_size > limit:
                raise ValueError(f"{label} is not one bounded regular file")
            chunks: list[bytes] = []
            digest = hashlib.sha256()
            observed = 0
            while chunk := os.read(descriptor, min(1024 * 1024, limit + 1 - observed)):
                observed += len(chunk)
                if observed > limit:
                    raise ValueError(f"{label} exceeds its size bound")
                digest.update(chunk)
                if retain_data:
                    chunks.append(chunk)
            after = os.fstat(descriptor)
        finally:
            os.close(descriptor)
        self.path = path
        self.label = label
        self.data = b"".join(chunks) if retain_data else b""
        self.size = observed
        self.sha256 = digest.hexdigest()
        self._identity = _identity(before)
        if self._identity != _identity(after) or self.size != before.st_size:
            raise ValueError(f"{label} changed during capture")

    def recheck(self) -> None:
        if _identity(os.stat(self.path, follow_symlinks=False)) != self._identity:
            raise ValueError(f"{self.label} changed after capture")

    def json(self) -> dict[str, object]:
        if not self.data:
            raise ValueError(f"{self.label} was captured without retained bytes")
        try:
            value = json.loads(
                self.data.decode("utf-8", errors="strict"),
                object_pairs_hook=_duplicates,
                parse_constant=lambda token: (_ for _ in ()).throw(
                    ValueError(f"non-finite JSON value: {token}")
                ),
            )
        except (UnicodeDecodeError, json.JSONDecodeError) as error:
            raise ValueError(f"{self.label} is not strict UTF-8 JSON") from error
        if not isinstance(value, dict):
            raise ValueError(f"{self.label} must contain one JSON object")
        return value


def _identity(value: os.stat_result) -> tuple[int, ...]:
    return (
        value.st_dev,
        value.st_ino,
        value.st_mode,
        value.st_uid,
        value.st_gid,
        value.st_nlink,
        value.st_size,
        value.st_mtime_ns,
        value.st_ctime_ns,
    )


def _sha40(value: object, label: str) -> str:
    if not isinstance(value, str) or SHA40.fullmatch(value) is None:
        raise ValueError(f"{label} must be a lowercase SHA-40")
    return value


def _sha256(value: object, label: str) -> str:
    if not isinstance(value, str) or SHA256.fullmatch(value) is None:
        raise ValueError(f"{label} must be a lowercase SHA-256")
    return value


def _positive(value: object, label: str) -> int:
    if type(value) is not int or value <= 0:
        raise ValueError(f"{label} must be a positive integer")
    return value


def _binding(snapshot: StableFile, path: str) -> dict[str, object]:
    return {"path": path, "sha256": snapshot.sha256, "sizeBytes": snapshot.size}


def expected_policy() -> dict[str, object]:
    return {
        "schema": POLICY_SCHEMA,
        "activeAutomatically": False,
        "releaseIdentity": {
            "packageId": PACKAGE_ID,
            "versionName": VERSION_NAME,
            "versionCode": VERSION_CODE,
            "compileSdk": COMPILE_SDK,
            "targetSdk": TARGET_SDK,
            "minimumSdk": MINIMUM_SDK,
            "runtimeIdentifier": RUNTIME_IDENTIFIER,
            "configuration": CONFIGURATION,
            "packageFormat": PACKAGE_FORMAT,
        },
        "sourceAuthority": {
            "repository": SOURCE_REPOSITORY,
            "requiredRef": MAIN_REF,
            "requiresExactTwoGreenCommit": True,
            "requiresExactTwoGreenTree": True,
            "requiresCleanCheckout": True,
        },
        "dependencies": {
            "core-content": {
                "repository": "https://github.com/ArchonMegalon/chummer6-core.git",
                "commit": "c06f22c185c7b733637fdb76b3cf333f31716781",
            },
            "core-runtime": {
                "repository": "https://github.com/ArchonMegalon/chummer6-core.git",
                "commit": "60112dccb6a3faad330d32c3c98eef0aa81d97af",
            },
            "hub": {
                "repository": "https://github.com/ArchonMegalon/chummer6-hub.git",
                "commit": "4f335d6cebbd4101212fd2cc77265b50f252775c",
            },
            "media": {
                "repository": "https://github.com/ArchonMegalon/chummer6-media-factory.git",
                "commit": "415c8163d3d90b1211e4014fef332bdec6d75f73",
            },
            "presentation": {
                "repository": "https://github.com/ArchonMegalon/chummer6-ui.git",
                "commit": "a9e5bbd4fd44826177dd048b24417fad27397497",
            },
            "registry": {
                "repository": "https://github.com/ArchonMegalon/chummer6-hub-registry.git",
                "commit": "af9a7e19c3bf331e96411dfb8f9e7820a98cab29",
            },
            "ui-kit": {
                "repository": "https://github.com/ArchonMegalon/chummer6-ui-kit.git",
                "commit": "d51ecd99cf72098d4adc8db0192bff7bf9fd8e61",
            },
        },
        "toolchain": {
            "runner": "ubuntu-24.04",
            "dotnetSdk": "10.0.110",
            "javaMajor": 17,
            "androidNetSdk": "36.1.69",
            "mauiVersion": "10.0.20",
            "bundletoolVersion": "1.18.3",
            "bundletoolSha256": "a099cfa1543f55593bc2ed16a70a7c67fe54b1747bb7301f37fdfd6d91028e29",
        },
        "producer": {
            "workflow": PRODUCER_WORKFLOW,
            "receipt": PRODUCER_OUTPUT,
            "artifact": "chummer-android-preview12-unsigned-candidate",
        },
        "independentVerifier": {
            "workflow": VERIFIER_WORKFLOW,
            "receipt": REBUILD_OUTPUT,
            "signerEligibility": ELIGIBILITY_OUTPUT,
            "requiresDistinctWorkflowRun": True,
            "requiresExactAabSha256Agreement": True,
            "requiresExactToolchainCompatibility": True,
        },
        "proofExclusion": {
            "required": True,
            "verifier": "scripts/verify_release_aab_excludes_api36_proof.py",
        },
        "coreContent": {
            "required": True,
            "manifest": CONTENT_MANIFEST_PATH,
            "verifier": "scripts/verify_android_content_bundle.py",
            "aabPackagedRoot": "base/assets/chummer-content",
        },
        "signingAuthorized": False,
        "googlePlayUploadAuthorized": False,
        "publicationAuthorized": False,
        "doesNotAssert": list(DOES_NOT_ASSERT),
    }


def validate_policy(snapshot: StableFile) -> tuple[dict[str, object], dict[str, object]]:
    policy = snapshot.json()
    if policy != expected_policy():
        raise ValueError("Preview.12 candidate policy differs from the closed authority")
    return policy, _binding(snapshot, "eng/preview12-unsigned-candidate-authority.json")


def _run(command: list[str], *, timeout: int = 30) -> bytes:
    environment = {
        "PATH": "/usr/bin:/bin",
        "LANG": "C",
        "LC_ALL": "C",
        "GIT_NO_REPLACE_OBJECTS": "1",
    }
    completed = subprocess.run(
        command,
        check=False,
        capture_output=True,
        timeout=timeout,
        env=environment,
    )
    if completed.returncode != 0:
        detail = (completed.stderr or completed.stdout).decode(errors="replace")[:500]
        raise ValueError(f"command failed ({command[0]}): {detail}")
    return completed.stdout + completed.stderr


def source_identity(android_root: Path) -> dict[str, object]:
    if (
        not android_root.is_absolute()
        or android_root.is_symlink()
        or not android_root.is_dir()
        or android_root.resolve(strict=True) != android_root
    ):
        raise ValueError("Android root must be an absolute canonical non-symlink directory")
    prefix = ["/usr/bin/git", "-C", os.fspath(android_root)]
    if _run([*prefix, "status", "--porcelain=v1", "--untracked-files=all"]).strip():
        raise ValueError("Android source checkout contains tracked changes")
    commit = _run([*prefix, "rev-parse", "HEAD"]).decode().strip()
    tree = _run([*prefix, "rev-parse", "HEAD^{tree}"]).decode().strip()
    remote = _run([*prefix, "remote", "get-url", "origin"]).decode().strip()
    if remote not in {SOURCE_REPOSITORY, SOURCE_REPOSITORY.removesuffix(".git")}:
        raise ValueError("Android source repository authority differs")
    return {
        "repository": SOURCE_REPOSITORY,
        "commit": _sha40(commit, "Android source commit"),
        "tree": _sha40(tree, "Android source tree"),
    }


def project_identity(android_root: Path) -> dict[str, object]:
    project = StableFile(android_root / PROJECT_PATH, "Android project", MAX_JSON_BYTES)
    try:
        root = ET.fromstring(project.data)
    except ET.ParseError as error:
        raise ValueError("Android project is not well-formed XML") from error

    def one(name: str) -> str:
        values = [
            (item.text or "").strip()
            for item in root.iter()
            if item.tag.rsplit("}", 1)[-1] == name and (item.text or "").strip()
        ]
        if len(values) != 1:
            raise ValueError(f"Android project must declare exactly one {name}")
        return values[0]

    framework = one("TargetFramework")
    compile_match = re.fullmatch(r"net10\.0-android([0-9]+)\.0", framework)
    version_code = one("ApplicationVersion")
    compile_sdk = compile_match.group(1) if compile_match else ""
    target_sdk = one("TargetSdkVersion")
    minimum_sdk = one("AndroidMinSdkVersion")
    if (
        version_code != str(VERSION_CODE)
        or compile_sdk != str(COMPILE_SDK)
        or target_sdk != str(TARGET_SDK)
        or minimum_sdk != str(MINIMUM_SDK)
    ):
        raise ValueError("Android Preview.12 project numeric identity is noncanonical")
    identity: dict[str, object] = {
        "packageId": one("ApplicationId"),
        "versionName": one("ApplicationDisplayVersion"),
        "versionCode": int(version_code),
        "compileSdk": int(compile_sdk),
        "targetSdk": int(target_sdk),
        "minimumSdk": int(minimum_sdk),
        "runtimeIdentifier": RUNTIME_IDENTIFIER,
        "configuration": CONFIGURATION,
        "packageFormat": PACKAGE_FORMAT,
    }
    expected = expected_policy()["releaseIdentity"]
    assert isinstance(expected, dict)
    if identity != expected or one("MauiVersion") != "10.0.20":
        raise ValueError("Android Preview.12 project identity differs from policy")
    return identity


def validate_dependency_graph(
    graph: object, source: dict[str, object], policy: dict[str, object]
) -> dict[str, object]:
    if not isinstance(graph, dict) or set(graph) != {"mode", "sources", "sha256"}:
        raise ValueError("Preview.12 dependency graph is absent or noncanonical")
    if graph.get("mode") != {"localCompatibilityTree": True, "packageOnly": False}:
        raise ValueError("two-green dependency mode differs")
    sources = graph.get("sources")
    dependencies = policy["dependencies"]
    assert isinstance(dependencies, dict)
    expected_names = {"android", *dependencies}
    if not isinstance(sources, dict) or set(sources) != expected_names:
        raise ValueError("two-green dependency source set differs")
    android = sources.get("android")
    if android != {"repository": SOURCE_REPOSITORY, "tree": source["tree"]}:
        raise ValueError("two-green Android dependency source differs")
    for name, authority in dependencies.items():
        row = sources.get(name)
        assert isinstance(authority, dict)
        if (
            not isinstance(row, dict)
            or set(row) != {"commit", "repository", "tree"}
            or row.get("repository") != authority["repository"]
            or row.get("commit") != authority["commit"]
        ):
            raise ValueError(f"two-green dependency differs: {name}")
        _sha40(row.get("tree"), f"two-green {name} dependency tree")
    unsigned_graph = {"mode": graph["mode"], "sources": sources}
    if graph.get("sha256") != digest_object(unsigned_graph):
        raise ValueError("two-green dependency graph digest differs")
    return graph


def validate_two_green(
    value: dict[str, object], source: dict[str, object], policy: dict[str, object]
) -> dict[str, object]:
    TWO_GREEN.validate_authority(value)
    if (
        value.get("sourceCommit") != source["commit"]
        or value.get("sourceTree") != source["tree"]
        or value.get("releaseIdentity")
        != {
            "packageId": PACKAGE_ID,
            "versionName": VERSION_NAME,
            "versionCode": VERSION_CODE,
            "intentAuthority": "android_project_at_exact_main_tree",
        }
    ):
        raise ValueError("two-green receipt does not bind this exact Preview.12 main tree")
    common = value.get("commonAuthority")
    graph = common.get("dependencyGraph") if isinstance(common, dict) else None
    return validate_dependency_graph(graph, source, policy)


def _command_observation(command: list[str], label: str) -> dict[str, object]:
    raw = _run(command, timeout=60)
    if not raw or len(raw) > 8 * 1024 * 1024:
        raise ValueError(f"{label} observation is empty or unbounded")
    return {"sha256": hashlib.sha256(raw).hexdigest(), "sizeBytes": len(raw)}


def capture_toolchain(
    *,
    policy_path: Path,
    dotnet: Path,
    java_home: Path,
    android_sdk: Path,
    bundletool: Path,
    runner_image: str,
    runner_image_version: str,
) -> dict[str, object]:
    policy_snapshot = StableFile(policy_path, "Preview.12 policy", MAX_JSON_BYTES)
    policy, policy_binding = validate_policy(policy_snapshot)
    for path, label in (
        (dotnet, "dotnet"),
        (java_home, "Java home"),
        (android_sdk, "Android SDK"),
        (bundletool, "bundletool"),
    ):
        if not path.is_absolute() or path.is_symlink() or path.resolve(strict=True) != path:
            raise ValueError(f"{label} must be canonical and non-symlinked")
    java = java_home / "bin/java"
    sdkmanager = android_sdk / "cmdline-tools/latest/bin/sdkmanager"
    adb = android_sdk / "platform-tools/adb"
    for executable, label in ((dotnet, "dotnet"), (java, "java"), (sdkmanager, "sdkmanager"), (adb, "adb")):
        if not executable.is_file() or not os.access(executable, os.X_OK):
            raise ValueError(f"{label} executable is unavailable")
    bundle = StableFile(bundletool, "bundletool", 64 * 1024 * 1024)
    expected_toolchain = policy["toolchain"]
    assert isinstance(expected_toolchain, dict)
    if bundle.sha256 != expected_toolchain["bundletoolSha256"]:
        raise ValueError("bundletool digest differs from policy")
    dotnet_version = _run([os.fspath(dotnet), "--version"]).decode().strip()
    java_raw = _run([os.fspath(java), "-version"]).decode(errors="replace")
    java_match = re.search(r'version "([0-9]+)', java_raw)
    if dotnet_version != expected_toolchain["dotnetSdk"] or not java_match:
        raise ValueError("candidate build SDK identity differs from policy")
    java_major = int(java_match.group(1))
    if java_major != expected_toolchain["javaMajor"]:
        raise ValueError("candidate Java major differs from policy")
    android_net_sdk = dotnet.parent / "packs/Microsoft.Android.Sdk.Linux/36.1.69"
    if not android_net_sdk.is_dir():
        raise ValueError("pinned Microsoft Android SDK pack is unavailable")
    package_observation = _command_observation(
        [os.fspath(sdkmanager), "--list_installed"], "Android package inventory"
    )
    workload_observation = _command_observation(
        [os.fspath(dotnet), "workload", "list"], "dotnet workload inventory"
    )
    build_tools = sorted(
        item.name for item in (android_sdk / "build-tools").iterdir() if item.is_dir()
    )
    if not build_tools or not (android_sdk / "platforms/android-36/android.jar").is_file():
        raise ValueError("Android API 36 toolchain is incomplete")
    observed = {
        "runnerImage": runner_image,
        "runnerImageVersion": runner_image_version,
        "dotnetInfo": _command_observation([os.fspath(dotnet), "--info"], "dotnet info"),
        "javaVersion": {**_command_observation([os.fspath(java), "-version"], "Java version"), "major": java_major},
        "androidPackages": package_observation,
        "dotnetWorkloads": workload_observation,
        "adb": _command_observation([os.fspath(adb), "version"], "adb version"),
        "buildToolsVersions": build_tools,
    }
    compatibility = {
        "runner": expected_toolchain["runner"],
        "dotnetSdk": dotnet_version,
        "javaMajor": java_major,
        "androidNetSdk": expected_toolchain["androidNetSdk"],
        "mauiVersion": expected_toolchain["mauiVersion"],
        "bundletoolVersion": expected_toolchain["bundletoolVersion"],
        "bundletoolSha256": bundle.sha256,
        "androidPackagesSha256": package_observation["sha256"],
        "dotnetWorkloadsSha256": workload_observation["sha256"],
        "buildToolsVersions": build_tools,
    }
    unsigned = {
        "schema": TOOLCHAIN_SCHEMA,
        "status": "pass",
        "policyAuthority": policy_binding,
        "compatibility": compatibility,
        "compatibilitySha256": digest_object(compatibility),
        "observed": observed,
        "signingInputsPresent": False,
        "publicationAuthorized": False,
    }
    return {**unsigned, "observationSha256": digest_object(unsigned)}


def validate_toolchain(value: dict[str, object], policy_binding: dict[str, object]) -> dict[str, object]:
    fields = {
        "schema", "status", "policyAuthority", "compatibility", "compatibilitySha256",
        "observed", "signingInputsPresent", "publicationAuthorized", "observationSha256",
    }
    if set(value) != fields or value.get("schema") != TOOLCHAIN_SCHEMA or value.get("status") != "pass":
        raise ValueError("candidate toolchain receipt fields or status differ")
    compatibility = value.get("compatibility")
    observed = value.get("observed")
    expected = expected_policy()["toolchain"]
    assert isinstance(expected, dict)
    if (
        value.get("policyAuthority") != policy_binding
        or value.get("signingInputsPresent") is not False
        or value.get("publicationAuthorized") is not False
        or not isinstance(compatibility, dict)
        or set(compatibility)
        != {
            "runner", "dotnetSdk", "javaMajor", "androidNetSdk", "mauiVersion",
            "bundletoolVersion", "bundletoolSha256", "androidPackagesSha256",
            "dotnetWorkloadsSha256", "buildToolsVersions",
        }
        or {key: compatibility.get(key) for key in (
            "runner", "dotnetSdk", "javaMajor", "androidNetSdk", "mauiVersion",
            "bundletoolVersion", "bundletoolSha256",
        )} != expected
        or SHA256.fullmatch(str(compatibility.get("androidPackagesSha256"))) is None
        or SHA256.fullmatch(str(compatibility.get("dotnetWorkloadsSha256"))) is None
        or not isinstance(compatibility.get("buildToolsVersions"), list)
        or not compatibility["buildToolsVersions"]
        or not all(isinstance(item, str) and re.fullmatch(r"[0-9]+(?:\.[0-9]+)+", item) for item in compatibility["buildToolsVersions"])
        or not isinstance(observed, dict)
        or set(observed)
        != {
            "runnerImage", "runnerImageVersion", "dotnetInfo", "javaVersion",
            "androidPackages", "dotnetWorkloads", "adb", "buildToolsVersions",
        }
        or not isinstance(observed.get("runnerImage"), str)
        or not observed["runnerImage"]
        or not isinstance(observed.get("runnerImageVersion"), str)
        or not observed["runnerImageVersion"]
        or value.get("compatibilitySha256") != digest_object(compatibility)
    ):
        raise ValueError("candidate toolchain authority differs")
    for field in ("dotnetInfo", "androidPackages", "dotnetWorkloads", "adb"):
        binding = observed.get(field)
        if (
            not isinstance(binding, dict)
            or set(binding) != {"sha256", "sizeBytes"}
            or SHA256.fullmatch(str(binding.get("sha256"))) is None
            or type(binding.get("sizeBytes")) is not int
            or binding["sizeBytes"] <= 0
        ):
            raise ValueError(f"candidate toolchain {field} observation differs")
    java = observed.get("javaVersion")
    if (
        not isinstance(java, dict)
        or set(java) != {"sha256", "sizeBytes", "major"}
        or SHA256.fullmatch(str(java.get("sha256"))) is None
        or type(java.get("sizeBytes")) is not int
        or java["sizeBytes"] <= 0
        or java.get("major") != compatibility["javaMajor"]
        or observed.get("buildToolsVersions") != compatibility["buildToolsVersions"]
        or observed["androidPackages"]["sha256"] != compatibility["androidPackagesSha256"]
        or observed["dotnetWorkloads"]["sha256"] != compatibility["dotnetWorkloadsSha256"]
    ):
        raise ValueError("candidate Java or Android toolchain observation differs")
    unsigned = {key: member for key, member in value.items() if key != "observationSha256"}
    if value.get("observationSha256") != digest_object(unsigned):
        raise ValueError("candidate toolchain observation digest differs")
    return value


def _manifest_identity(manifest: StableFile) -> dict[str, object]:
    try:
        root = ET.fromstring(manifest.data)
    except ET.ParseError as error:
        raise ValueError("bundletool manifest is not XML") from error
    attr = lambda element, name: element.get(f"{ANDROID}{name}")  # noqa: E731
    uses_sdk = root.find("uses-sdk")
    if uses_sdk is None:
        raise ValueError("bundletool manifest does not contain uses-sdk")
    identity = {
        "packageId": root.get("package"),
        "versionName": attr(root, "versionName"),
        "versionCode": int(attr(root, "versionCode") or -1),
        "compileSdk": int(attr(root, "compileSdkVersion") or -1),
        "targetSdk": int(attr(uses_sdk, "targetSdkVersion") or -1),
        "minimumSdk": int(attr(uses_sdk, "minSdkVersion") or -1),
        "runtimeIdentifier": RUNTIME_IDENTIFIER,
        "configuration": CONFIGURATION,
        "packageFormat": PACKAGE_FORMAT,
    }
    expected = expected_policy()["releaseIdentity"]
    if identity != expected:
        raise ValueError("unsigned AAB manifest identity differs from Preview.12 policy")
    return identity


def artifact_authority(
    aab: StableFile, manifest: StableFile, android_root: Path
) -> tuple[dict[str, object], dict[str, object], dict[str, object]]:
    if aab.path.name != "chummer-android-0.1.0-preview.12-unsigned.aab":
        raise ValueError("unsigned Preview.12 AAB filename differs")
    manifest_identity = _manifest_identity(manifest)
    try:
        stores, assemblies, expanded = PROOF.verify(aab.path, android_root)
    except PROOF.VerificationError as error:
        raise ValueError(f"Preview.12 proof exclusion failed: {error}") from error
    verifier = StableFile(PROOF_VERIFIER_PATH, "proof-exclusion verifier", MAX_JSON_BYTES)
    content_verifier = StableFile(
        CONTENT_VERIFIER_PATH, "content-authority verifier", MAX_JSON_BYTES
    )
    content_manifest = StableFile(
        android_root / CONTENT_MANIFEST_PATH,
        "canonical Android content manifest",
        MAX_JSON_BYTES,
    )
    content_value = content_manifest.json()
    content_issues = CONTENT.validate_manifest(content_value)
    packaged_count, packaged_issues = CONTENT.verify_aab(
        aab.path, content_value, content_manifest.data
    )
    content_issues.extend(packaged_issues)
    if content_issues:
        raise ValueError(
            "Preview.12 Core-content authority failed: " + ";".join(sorted(content_issues))
        )
    artifact = {
        "fileName": aab.path.name,
        "sha256": aab.sha256,
        "sizeBytes": aab.size,
        "manifestSha256": manifest.sha256,
        "manifestSizeBytes": manifest.size,
        "manifest": manifest_identity,
    }
    proof = {
        "status": "pass",
        "verifier": _binding(verifier, "scripts/verify_release_aab_excludes_api36_proof.py"),
        "managedAssemblyStores": stores,
        "managedAssemblies": assemblies,
        "expandedManagedBytes": expanded,
        "aabSha256": aab.sha256,
    }
    files = content_value.get("files")
    assert isinstance(files, list)
    content = {
        "status": "pass",
        "verifier": _binding(
            content_verifier, "scripts/verify_android_content_bundle.py"
        ),
        "manifest": _binding(content_manifest, CONTENT_MANIFEST_PATH),
        "coreRevision": content_value["coreRevision"],
        "bundleDigest": content_value["bundleDigest"],
        "canonicalFileCount": len(files),
        "canonicalByteCount": sum(entry["size"] for entry in files),
        "aabCanonicalFileCount": packaged_count,
        "aabSha256": aab.sha256,
    }
    return artifact, proof, content


def validate_content_authority(
    value: object, *, aab_sha256: object
) -> dict[str, object]:
    if not isinstance(value, dict) or set(value) != {
        "status", "verifier", "manifest", "coreRevision", "bundleDigest",
        "canonicalFileCount", "canonicalByteCount", "aabCanonicalFileCount",
        "aabSha256",
    }:
        raise ValueError("Preview.12 Core-content authority differs")
    verifier_snapshot = StableFile(
        CONTENT_VERIFIER_PATH, "content-authority verifier", MAX_JSON_BYTES
    )
    manifest_snapshot = StableFile(
        REPO_ROOT / CONTENT_MANIFEST_PATH,
        "canonical Android content manifest",
        MAX_JSON_BYTES,
    )
    canonical_manifest = manifest_snapshot.json()
    manifest_issues = CONTENT.validate_manifest(canonical_manifest)
    files = canonical_manifest.get("files")
    if manifest_issues or not isinstance(files, list):
        raise ValueError("canonical Android content manifest is invalid")
    if (
        value.get("status") != "pass"
        or value.get("verifier")
        != _binding(verifier_snapshot, "scripts/verify_android_content_bundle.py")
        or value.get("manifest") != _binding(manifest_snapshot, CONTENT_MANIFEST_PATH)
        or value.get("coreRevision") != canonical_manifest.get("coreRevision")
        or value.get("coreRevision")
        != expected_policy()["dependencies"]["core-content"]["commit"]
        or value.get("bundleDigest") != canonical_manifest.get("bundleDigest")
        or type(value.get("canonicalFileCount")) is not int
        or value["canonicalFileCount"] != len(files)
        or value.get("aabCanonicalFileCount") != value.get("canonicalFileCount")
        or type(value.get("canonicalByteCount")) is not int
        or value["canonicalByteCount"]
        != sum(entry["size"] for entry in files)
        or value.get("aabSha256") != aab_sha256
    ):
        raise ValueError("Preview.12 Core-content authority differs")
    return value


def validate_artifact_and_proof(
    artifact: object, proof: object
) -> tuple[dict[str, object], dict[str, object]]:
    if (
        not isinstance(artifact, dict)
        or set(artifact)
        != {
            "fileName", "sha256", "sizeBytes", "manifestSha256",
            "manifestSizeBytes", "manifest",
        }
        or artifact.get("fileName")
        != "chummer-android-0.1.0-preview.12-unsigned.aab"
        or SHA256.fullmatch(str(artifact.get("sha256"))) is None
        or type(artifact.get("sizeBytes")) is not int
        or artifact["sizeBytes"] <= 0
        or artifact["sizeBytes"] > MAX_AAB_BYTES
        or SHA256.fullmatch(str(artifact.get("manifestSha256"))) is None
        or type(artifact.get("manifestSizeBytes")) is not int
        or artifact["manifestSizeBytes"] <= 0
        or artifact["manifestSizeBytes"] > MAX_JSON_BYTES
        or artifact.get("manifest") != expected_policy()["releaseIdentity"]
    ):
        raise ValueError("Preview.12 artifact authority differs")
    verifier = StableFile(
        PROOF_VERIFIER_PATH, "proof-exclusion verifier", MAX_JSON_BYTES
    )
    if (
        not isinstance(proof, dict)
        or set(proof)
        != {
            "status", "verifier", "managedAssemblyStores", "managedAssemblies",
            "expandedManagedBytes", "aabSha256",
        }
        or proof.get("status") != "pass"
        or proof.get("verifier")
        != _binding(verifier, "scripts/verify_release_aab_excludes_api36_proof.py")
        or type(proof.get("managedAssemblyStores")) is not int
        or proof["managedAssemblyStores"] <= 0
        or type(proof.get("managedAssemblies")) is not int
        or proof["managedAssemblies"] <= 0
        or type(proof.get("expandedManagedBytes")) is not int
        or proof["expandedManagedBytes"] <= 0
        or proof.get("aabSha256") != artifact["sha256"]
    ):
        raise ValueError("Preview.12 proof-exclusion authority differs")
    return artifact, proof


def _github_run(run_id: int, attempt: int, sha: str, ref: str, workflow: str) -> dict[str, object]:
    _positive(run_id, "GitHub run ID")
    _positive(attempt, "GitHub run attempt")
    if ref != MAIN_REF:
        raise ValueError("Preview.12 candidate lane must be dispatched on refs/heads/main")
    return {
        "id": run_id,
        "attempt": attempt,
        "event": "workflow_dispatch",
        "ref": ref,
        "sha": _sha40(sha, "GitHub run SHA"),
        "workflow": workflow,
    }


def create_producer(
    *,
    android_root: Path,
    policy_path: Path,
    two_green_path: Path,
    toolchain_path: Path,
    aab_path: Path,
    manifest_path: Path,
    workflow_path: Path,
    github_run_id: int,
    github_run_attempt: int,
    github_sha: str,
    github_ref: str,
    two_green_run_id: int,
    two_green_artifact_id: int,
    two_green_artifact_digest: str,
) -> dict[str, object]:
    if workflow_path != android_root / PRODUCER_WORKFLOW:
        raise ValueError("producer workflow must come from the governed Android checkout")
    snapshots = {
        "policy": StableFile(policy_path, "Preview.12 policy", MAX_JSON_BYTES),
        "twoGreen": StableFile(two_green_path, "two-green receipt", MAX_JSON_BYTES),
        "toolchain": StableFile(toolchain_path, "producer toolchain", MAX_JSON_BYTES),
        "aab": StableFile(
            aab_path, "producer unsigned AAB", MAX_AAB_BYTES, retain_data=False
        ),
        "manifest": StableFile(manifest_path, "producer bundletool manifest", MAX_JSON_BYTES),
        "workflow": StableFile(workflow_path, "producer workflow", MAX_JSON_BYTES),
    }
    policy, policy_binding = validate_policy(snapshots["policy"])
    source = source_identity(android_root)
    if github_sha != source["commit"]:
        raise ValueError("producer workflow SHA differs from checked-out source")
    project = project_identity(android_root)
    two_green = snapshots["twoGreen"].json()
    dependency_graph = validate_two_green(two_green, source, policy)
    toolchain = validate_toolchain(snapshots["toolchain"].json(), policy_binding)
    artifact, proof, content = artifact_authority(
        snapshots["aab"], snapshots["manifest"], android_root
    )
    if artifact["manifest"] != project:
        raise ValueError("producer AAB and project release identities differ")
    if not ARTIFACT_DIGEST.fullmatch(two_green_artifact_digest):
        raise ValueError("two-green artifact digest is not canonical")
    run = _github_run(
        github_run_id, github_run_attempt, github_sha, github_ref, PRODUCER_WORKFLOW
    )
    reviewed_inputs = {
        "workflow": _binding(snapshots["workflow"], PRODUCER_WORKFLOW),
        "policy": policy_binding,
    }
    two_green_binding = {
        "runId": _positive(two_green_run_id, "two-green workflow run ID"),
        "artifactId": _positive(two_green_artifact_id, "two-green artifact ID"),
        "artifactDigest": two_green_artifact_digest,
        "receiptSha256": snapshots["twoGreen"].sha256,
        "receiptSizeBytes": snapshots["twoGreen"].size,
        "eligibilitySha256": two_green["eligibilitySha256"],
    }
    unsigned = {
        "schema": PRODUCER_SCHEMA,
        "status": "pass",
        "candidateLane": "exact_main_unsigned_preview12_producer",
        "source": source,
        "releaseIdentity": project,
        "dependencyGraph": dependency_graph,
        "twoGreen": two_green_binding,
        "toolchain": toolchain,
        "artifact": artifact,
        "proofExclusion": proof,
        "contentAuthority": content,
        "reviewedInputs": reviewed_inputs,
        "githubRun": run,
        "signerEligible": False,
        "signingAuthorized": False,
        "googlePlayUploadAuthorized": False,
        "publicationAuthorized": False,
        "doesNotAssert": list(DOES_NOT_ASSERT),
    }
    result = {**unsigned, "candidateSha256": digest_object(unsigned)}
    for snapshot in snapshots.values():
        snapshot.recheck()
    validate_producer(result)
    return result


def validate_producer(value: dict[str, object]) -> dict[str, object]:
    required = {
        "schema", "status", "candidateLane", "source", "releaseIdentity",
        "dependencyGraph", "twoGreen", "toolchain", "artifact", "proofExclusion",
        "contentAuthority",
        "reviewedInputs", "githubRun", "signerEligible", "signingAuthorized",
        "googlePlayUploadAuthorized", "publicationAuthorized", "doesNotAssert",
        "candidateSha256",
    }
    if set(value) != required or value.get("schema") != PRODUCER_SCHEMA or value.get("status") != "pass":
        raise ValueError("producer receipt fields or status differ")
    policy_snapshot = StableFile(POLICY_PATH, "Preview.12 policy", MAX_JSON_BYTES)
    policy, policy_binding = validate_policy(policy_snapshot)
    if (
        value.get("candidateLane") != "exact_main_unsigned_preview12_producer"
        or value.get("releaseIdentity") != expected_policy()["releaseIdentity"]
        or value.get("signerEligible") is not False
        or value.get("signingAuthorized") is not False
        or value.get("googlePlayUploadAuthorized") is not False
        or value.get("publicationAuthorized") is not False
        or value.get("doesNotAssert") != list(DOES_NOT_ASSERT)
    ):
        raise ValueError("producer receipt authority posture differs")
    source = value.get("source")
    run = value.get("githubRun")
    if (
        not isinstance(source, dict)
        or set(source) != {"repository", "commit", "tree"}
        or source.get("repository") != SOURCE_REPOSITORY
        or SHA40.fullmatch(str(source.get("commit"))) is None
        or SHA40.fullmatch(str(source.get("tree"))) is None
        or not isinstance(run, dict)
        or set(run) != {"id", "attempt", "event", "ref", "sha", "workflow"}
        or type(run.get("id")) is not int
        or run["id"] <= 0
        or type(run.get("attempt")) is not int
        or run["attempt"] <= 0
        or run.get("sha") != source.get("commit")
        or run.get("ref") != MAIN_REF
        or run.get("event") != "workflow_dispatch"
        or run.get("workflow") != PRODUCER_WORKFLOW
    ):
        raise ValueError("producer exact-main source authority differs")
    assert isinstance(source, dict)
    validate_dependency_graph(value.get("dependencyGraph"), source, policy)
    toolchain = value.get("toolchain")
    if not isinstance(toolchain, dict):
        raise ValueError("producer toolchain is invalid")
    validate_toolchain(toolchain, policy_binding)
    reviewed = value.get("reviewedInputs")
    workflow_snapshot = StableFile(REPO_ROOT / PRODUCER_WORKFLOW, "producer workflow", MAX_JSON_BYTES)
    if (
        not isinstance(reviewed, dict)
        or reviewed != {
            "workflow": _binding(workflow_snapshot, PRODUCER_WORKFLOW),
            "policy": policy_binding,
        }
    ):
        raise ValueError("producer reviewed input authority differs")
    two_green = value.get("twoGreen")
    if (
        not isinstance(two_green, dict)
        or set(two_green)
        != {"runId", "artifactId", "artifactDigest", "receiptSha256", "receiptSizeBytes", "eligibilitySha256"}
        or type(two_green.get("runId")) is not int
        or two_green["runId"] <= 0
        or type(two_green.get("artifactId")) is not int
        or two_green["artifactId"] <= 0
        or ARTIFACT_DIGEST.fullmatch(str(two_green.get("artifactDigest"))) is None
        or SHA256.fullmatch(str(two_green.get("receiptSha256"))) is None
        or type(two_green.get("receiptSizeBytes")) is not int
        or two_green["receiptSizeBytes"] <= 0
        or two_green["receiptSizeBytes"] > MAX_JSON_BYTES
        or SHA256.fullmatch(str(two_green.get("eligibilitySha256"))) is None
    ):
        raise ValueError("producer Two-Green binding differs")
    artifact, _ = validate_artifact_and_proof(
        value.get("artifact"), value.get("proofExclusion")
    )
    content = value.get("contentAuthority")
    validate_content_authority(content, aab_sha256=artifact["sha256"])
    unsigned = {key: member for key, member in value.items() if key != "candidateSha256"}
    if value.get("candidateSha256") != digest_object(unsigned):
        raise ValueError("producer candidate digest differs")
    return value


def create_rebuild(
    *,
    android_root: Path,
    policy_path: Path,
    two_green_path: Path,
    producer_path: Path,
    producer_aab_path: Path,
    producer_manifest_path: Path,
    toolchain_path: Path,
    rebuilt_aab_path: Path,
    rebuilt_manifest_path: Path,
    workflow_path: Path,
    github_run_id: int,
    github_run_attempt: int,
    github_sha: str,
    github_ref: str,
    producer_run_id: int,
    producer_artifact_id: int,
    producer_artifact_digest: str,
) -> dict[str, object]:
    if workflow_path != android_root / VERIFIER_WORKFLOW:
        raise ValueError("verifier workflow must come from the governed Android checkout")
    snapshots = {
        "policy": StableFile(policy_path, "Preview.12 policy", MAX_JSON_BYTES),
        "twoGreen": StableFile(two_green_path, "two-green receipt", MAX_JSON_BYTES),
        "producer": StableFile(producer_path, "producer receipt", MAX_JSON_BYTES),
        "producerAab": StableFile(
            producer_aab_path, "producer AAB", MAX_AAB_BYTES, retain_data=False
        ),
        "producerManifest": StableFile(producer_manifest_path, "producer manifest", MAX_JSON_BYTES),
        "toolchain": StableFile(toolchain_path, "verifier toolchain", MAX_JSON_BYTES),
        "rebuiltAab": StableFile(
            rebuilt_aab_path,
            "independent rebuilt AAB",
            MAX_AAB_BYTES,
            retain_data=False,
        ),
        "rebuiltManifest": StableFile(rebuilt_manifest_path, "rebuilt manifest", MAX_JSON_BYTES),
        "workflow": StableFile(workflow_path, "verifier workflow", MAX_JSON_BYTES),
    }
    policy, policy_binding = validate_policy(snapshots["policy"])
    source = source_identity(android_root)
    if source["commit"] != github_sha:
        raise ValueError("verifier workflow SHA differs from checked-out source")
    project = project_identity(android_root)
    two_green = snapshots["twoGreen"].json()
    dependency_graph = validate_two_green(two_green, source, policy)
    producer = validate_producer(snapshots["producer"].json())
    if (
        producer["source"] != source
        or producer["releaseIdentity"] != project
        or producer["dependencyGraph"] != dependency_graph
        or producer["twoGreen"]["receiptSha256"] != snapshots["twoGreen"].sha256
        or producer["twoGreen"]["eligibilitySha256"] != two_green["eligibilitySha256"]
    ):
        raise ValueError("producer receipt does not bind the independently rebuilt source")
    if producer["githubRun"]["id"] != producer_run_id:
        raise ValueError("producer run ID differs from producer receipt")
    if github_run_id == producer_run_id:
        raise ValueError("independent verifier must use a distinct workflow run")
    if not ARTIFACT_DIGEST.fullmatch(producer_artifact_digest):
        raise ValueError("producer artifact digest is not canonical")
    producer_artifact, producer_proof, producer_content = artifact_authority(
        snapshots["producerAab"], snapshots["producerManifest"], android_root
    )
    if (
        producer_artifact != producer["artifact"]
        or producer_proof != producer["proofExclusion"]
        or producer_content != producer["contentAuthority"]
    ):
        raise ValueError("downloaded producer AAB differs from producer receipt")
    verifier_toolchain = validate_toolchain(snapshots["toolchain"].json(), policy_binding)
    rebuilt_artifact, rebuilt_proof, rebuilt_content = artifact_authority(
        snapshots["rebuiltAab"], snapshots["rebuiltManifest"], android_root
    )
    if rebuilt_artifact != producer_artifact:
        raise ValueError("independent rebuilt AAB bytes or manifest differ from producer")
    if rebuilt_proof != producer_proof:
        raise ValueError("independent proof-exclusion observation differs from producer")
    if rebuilt_content != producer_content:
        raise ValueError("independent Core-content observation differs from producer")
    if verifier_toolchain["compatibilitySha256"] != producer["toolchain"]["compatibilitySha256"]:
        raise ValueError("producer and verifier toolchain compatibility differs")
    run = _github_run(
        github_run_id, github_run_attempt, github_sha, github_ref, VERIFIER_WORKFLOW
    )
    agreement = {
        "sourceCommit": source["commit"],
        "sourceTree": source["tree"],
        "releaseIdentity": project,
        "dependencyGraphSha256": dependency_graph["sha256"],
        "twoGreenEligibilitySha256": two_green["eligibilitySha256"],
        "producerCandidateSha256": producer["candidateSha256"],
        "aabSha256": producer_artifact["sha256"],
        "aabSizeBytes": producer_artifact["sizeBytes"],
        "toolchainCompatibilitySha256": verifier_toolchain["compatibilitySha256"],
        "proofExclusion": "pass",
        "contentBundleDigest": producer_content["bundleDigest"],
    }
    unsigned = {
        "schema": REBUILD_SCHEMA,
        "status": "pass",
        "candidateLane": "independent_preview12_rebuild_verifier",
        "source": source,
        "producer": {
            "runId": _positive(producer_run_id, "producer run ID"),
            "artifactId": _positive(producer_artifact_id, "producer artifact ID"),
            "artifactDigest": producer_artifact_digest,
            "receiptSha256": snapshots["producer"].sha256,
            "candidateSha256": producer["candidateSha256"],
        },
        "verifierRun": run,
        "verifierToolchain": verifier_toolchain,
        "rebuiltArtifact": rebuilt_artifact,
        "proofExclusion": rebuilt_proof,
        "contentAuthority": rebuilt_content,
        "reviewedInputs": {
            "workflow": _binding(snapshots["workflow"], VERIFIER_WORKFLOW),
            "policy": policy_binding,
        },
        "agreement": agreement,
        "signerEligible": True,
        "signingAuthorized": False,
        "googlePlayUploadAuthorized": False,
        "publicationAuthorized": False,
        "doesNotAssert": list(DOES_NOT_ASSERT),
    }
    result = {**unsigned, "verificationSha256": digest_object(unsigned)}
    for snapshot in snapshots.values():
        snapshot.recheck()
    validate_rebuild(result)
    return result


def validate_rebuild(value: dict[str, object]) -> dict[str, object]:
    required = {
        "schema", "status", "candidateLane", "source", "producer", "verifierRun",
        "verifierToolchain", "rebuiltArtifact", "proofExclusion", "contentAuthority", "reviewedInputs",
        "agreement", "signerEligible", "signingAuthorized", "googlePlayUploadAuthorized",
        "publicationAuthorized", "doesNotAssert", "verificationSha256",
    }
    if set(value) != required or value.get("schema") != REBUILD_SCHEMA or value.get("status") != "pass":
        raise ValueError("independent rebuild receipt fields or status differ")
    policy_snapshot = StableFile(POLICY_PATH, "Preview.12 policy", MAX_JSON_BYTES)
    _, policy_binding = validate_policy(policy_snapshot)
    if (
        value.get("candidateLane") != "independent_preview12_rebuild_verifier"
        or value.get("signerEligible") is not True
        or value.get("signingAuthorized") is not False
        or value.get("googlePlayUploadAuthorized") is not False
        or value.get("publicationAuthorized") is not False
        or value.get("doesNotAssert") != list(DOES_NOT_ASSERT)
    ):
        raise ValueError("independent rebuild authority posture differs")
    producer = value.get("producer")
    run = value.get("verifierRun")
    source = value.get("source")
    artifact, _ = validate_artifact_and_proof(
        value.get("rebuiltArtifact"), value.get("proofExclusion")
    )
    content = value.get("contentAuthority")
    agreement = value.get("agreement")
    expected_agreement_fields = {
        "sourceCommit", "sourceTree", "releaseIdentity", "dependencyGraphSha256",
        "twoGreenEligibilitySha256", "producerCandidateSha256", "aabSha256",
        "aabSizeBytes", "toolchainCompatibilitySha256", "proofExclusion",
        "contentBundleDigest",
    }
    if (
        not isinstance(producer, dict)
        or set(producer)
        != {"runId", "artifactId", "artifactDigest", "receiptSha256", "candidateSha256"}
        or type(producer.get("runId")) is not int
        or producer["runId"] <= 0
        or type(producer.get("artifactId")) is not int
        or producer["artifactId"] <= 0
        or ARTIFACT_DIGEST.fullmatch(str(producer.get("artifactDigest"))) is None
        or SHA256.fullmatch(str(producer.get("receiptSha256"))) is None
        or SHA256.fullmatch(str(producer.get("candidateSha256"))) is None
        or not isinstance(run, dict)
        or set(run) != {"id", "attempt", "event", "ref", "sha", "workflow"}
        or type(run.get("id")) is not int
        or run["id"] <= 0
        or type(run.get("attempt")) is not int
        or run["attempt"] <= 0
        or run.get("event") != "workflow_dispatch"
        or run.get("workflow") != VERIFIER_WORKFLOW
        or run.get("ref") != MAIN_REF
        or run.get("id") == producer.get("runId")
        or not isinstance(source, dict)
        or set(source) != {"repository", "commit", "tree"}
        or source.get("repository") != SOURCE_REPOSITORY
        or SHA40.fullmatch(str(source.get("commit"))) is None
        or SHA40.fullmatch(str(source.get("tree"))) is None
        or run.get("sha") != source.get("commit")
        or not isinstance(agreement, dict)
        or set(agreement) != expected_agreement_fields
        or agreement.get("aabSha256") != artifact.get("sha256")
        or agreement.get("aabSizeBytes") != artifact.get("sizeBytes")
        or agreement.get("proofExclusion") != "pass"
        or not isinstance(content, dict)
        or SHA256.fullmatch(str(agreement.get("contentBundleDigest"))) is None
        or agreement.get("contentBundleDigest") != content.get("bundleDigest")
        or content.get("status") != "pass"
        or content.get("aabSha256") != artifact.get("sha256")
        or content.get("aabCanonicalFileCount") != content.get("canonicalFileCount")
    ):
        raise ValueError("independent rebuild agreement is invalid")
    verifier_toolchain = value.get("verifierToolchain")
    if not isinstance(verifier_toolchain, dict):
        raise ValueError("verifier toolchain is invalid")
    validate_toolchain(verifier_toolchain, policy_binding)
    validate_content_authority(content, aab_sha256=artifact["sha256"])
    workflow_snapshot = StableFile(REPO_ROOT / VERIFIER_WORKFLOW, "verifier workflow", MAX_JSON_BYTES)
    if value.get("reviewedInputs") != {
        "workflow": _binding(workflow_snapshot, VERIFIER_WORKFLOW),
        "policy": policy_binding,
    }:
        raise ValueError("verifier reviewed input authority differs")
    if (
        agreement.get("sourceCommit") != source.get("commit")
        or agreement.get("sourceTree") != source.get("tree")
        or agreement.get("releaseIdentity") != expected_policy()["releaseIdentity"]
        or agreement.get("toolchainCompatibilitySha256")
        != value["verifierToolchain"]["compatibilitySha256"]
        or agreement.get("producerCandidateSha256") != producer.get("candidateSha256")
    ):
        raise ValueError("independent rebuild semantic agreement differs")
    unsigned = {key: member for key, member in value.items() if key != "verificationSha256"}
    if value.get("verificationSha256") != digest_object(unsigned):
        raise ValueError("independent rebuild verification digest differs")
    return value


def create_signer_eligibility(producer: dict[str, object], rebuild: dict[str, object]) -> dict[str, object]:
    validate_producer(producer)
    validate_rebuild(rebuild)
    if (
        rebuild["source"] != producer["source"]
        or rebuild["producer"]["candidateSha256"] != producer["candidateSha256"]
        or rebuild["agreement"]["aabSha256"] != producer["artifact"]["sha256"]
        or rebuild["agreement"]["dependencyGraphSha256"] != producer["dependencyGraph"]["sha256"]
        or rebuild["agreement"]["twoGreenEligibilitySha256"] != producer["twoGreen"]["eligibilitySha256"]
        or rebuild["agreement"]["toolchainCompatibilitySha256"]
        != producer["toolchain"]["compatibilitySha256"]
        or rebuild["agreement"]["contentBundleDigest"]
        != producer["contentAuthority"]["bundleDigest"]
    ):
        raise ValueError("producer and independent verifier do not agree")
    agreement = rebuild["agreement"]
    unsigned = {
        "schema": ELIGIBILITY_SCHEMA,
        "status": "pass",
        "eligibilityScope": "external_signer_input_only",
        "signerEligible": True,
        "source": producer["source"],
        "releaseIdentity": producer["releaseIdentity"],
        "dependencyGraphSha256": agreement["dependencyGraphSha256"],
        "twoGreenEligibilitySha256": agreement["twoGreenEligibilitySha256"],
        "producerCandidateSha256": producer["candidateSha256"],
        "independentVerificationSha256": rebuild["verificationSha256"],
        "unsignedAab": {
            "fileName": producer["artifact"]["fileName"],
            "sha256": agreement["aabSha256"],
            "sizeBytes": agreement["aabSizeBytes"],
        },
        "toolchainCompatibilitySha256": agreement["toolchainCompatibilitySha256"],
        "proofExclusion": "pass",
        "contentBundleDigest": agreement["contentBundleDigest"],
        "signingAuthorized": False,
        "googlePlayUploadAuthorized": False,
        "publicationAuthorized": False,
        "doesNotAssert": list(DOES_NOT_ASSERT),
    }
    return {**unsigned, "eligibilitySha256": digest_object(unsigned)}


def validate_signer_eligibility(value: dict[str, object]) -> dict[str, object]:
    required = {
        "schema", "status", "eligibilityScope", "signerEligible", "source",
        "releaseIdentity", "dependencyGraphSha256", "twoGreenEligibilitySha256",
        "producerCandidateSha256", "independentVerificationSha256", "unsignedAab",
        "toolchainCompatibilitySha256", "proofExclusion", "signingAuthorized",
        "contentBundleDigest",
        "googlePlayUploadAuthorized", "publicationAuthorized", "doesNotAssert",
        "eligibilitySha256",
    }
    if set(value) != required or value.get("schema") != ELIGIBILITY_SCHEMA or value.get("status") != "pass":
        raise ValueError("signer-eligibility fields or status differ")
    if (
        value.get("eligibilityScope") != "external_signer_input_only"
        or value.get("signerEligible") is not True
        or value.get("proofExclusion") != "pass"
        or value.get("signingAuthorized") is not False
        or value.get("googlePlayUploadAuthorized") is not False
        or value.get("publicationAuthorized") is not False
        or value.get("doesNotAssert") != list(DOES_NOT_ASSERT)
        or SHA256.fullmatch(str(value.get("contentBundleDigest"))) is None
    ):
        raise ValueError("signer-eligibility authority posture differs")
    for field in (
        "dependencyGraphSha256", "twoGreenEligibilitySha256", "producerCandidateSha256",
        "independentVerificationSha256", "toolchainCompatibilitySha256",
    ):
        _sha256(value.get(field), field)
    source = value.get("source")
    artifact = value.get("unsignedAab")
    if (
        not isinstance(source, dict)
        or set(source) != {"repository", "commit", "tree"}
        or source.get("repository") != SOURCE_REPOSITORY
        or SHA40.fullmatch(str(source.get("commit"))) is None
        or SHA40.fullmatch(str(source.get("tree"))) is None
        or value.get("releaseIdentity") != expected_policy()["releaseIdentity"]
        or not isinstance(artifact, dict)
        or set(artifact) != {"fileName", "sha256", "sizeBytes"}
        or artifact.get("fileName") != "chummer-android-0.1.0-preview.12-unsigned.aab"
        or SHA256.fullmatch(str(artifact.get("sha256"))) is None
        or type(artifact.get("sizeBytes")) is not int
        or artifact["sizeBytes"] <= 0
        or artifact["sizeBytes"] > MAX_AAB_BYTES
    ):
        raise ValueError("signer-eligibility source or artifact identity differs")
    unsigned = {key: member for key, member in value.items() if key != "eligibilitySha256"}
    if value.get("eligibilitySha256") != digest_object(unsigned):
        raise ValueError("signer-eligibility digest differs")
    return value


def write_exclusive(path: Path, expected_name: str, value: dict[str, object]) -> None:
    if path.name != expected_name or not path.is_absolute() or path.exists() or path.is_symlink():
        raise ValueError(f"output must be an absent absolute {expected_name}")
    if not path.parent.is_dir() or path.parent.is_symlink() or path.parent.resolve(strict=True) != path.parent:
        raise ValueError("output parent must be canonical and non-symlinked")
    descriptor = os.open(
        path,
        os.O_WRONLY | os.O_CREAT | os.O_EXCL | getattr(os, "O_NOFOLLOW", 0),
        0o600,
    )
    with os.fdopen(descriptor, "wb") as stream:
        stream.write(pretty_bytes(value))
        stream.flush()
        os.fsync(stream.fileno())


def _producer_arguments(parser: argparse.ArgumentParser) -> None:
    parser.add_argument("--android-root", type=Path, required=True)
    parser.add_argument("--policy", dest="policy_path", type=Path, required=True)
    parser.add_argument("--two-green", dest="two_green_path", type=Path, required=True)
    parser.add_argument("--toolchain", dest="toolchain_path", type=Path, required=True)
    parser.add_argument("--aab", dest="aab_path", type=Path, required=True)
    parser.add_argument("--manifest", dest="manifest_path", type=Path, required=True)
    parser.add_argument("--workflow", dest="workflow_path", type=Path, required=True)
    parser.add_argument("--github-run-id", type=int, required=True)
    parser.add_argument("--github-run-attempt", type=int, required=True)
    parser.add_argument("--github-sha", required=True)
    parser.add_argument("--github-ref", required=True)
    parser.add_argument("--two-green-run-id", type=int, required=True)
    parser.add_argument("--two-green-artifact-id", type=int, required=True)
    parser.add_argument("--two-green-artifact-digest", required=True)


def _rebuild_arguments(parser: argparse.ArgumentParser) -> None:
    parser.add_argument("--android-root", type=Path, required=True)
    parser.add_argument("--policy", dest="policy_path", type=Path, required=True)
    parser.add_argument("--two-green", dest="two_green_path", type=Path, required=True)
    parser.add_argument("--producer", dest="producer_path", type=Path, required=True)
    parser.add_argument("--producer-aab", dest="producer_aab_path", type=Path, required=True)
    parser.add_argument("--producer-manifest", dest="producer_manifest_path", type=Path, required=True)
    parser.add_argument("--toolchain", dest="toolchain_path", type=Path, required=True)
    parser.add_argument("--rebuilt-aab", dest="rebuilt_aab_path", type=Path, required=True)
    parser.add_argument("--rebuilt-manifest", dest="rebuilt_manifest_path", type=Path, required=True)
    parser.add_argument("--workflow", dest="workflow_path", type=Path, required=True)
    parser.add_argument("--github-run-id", type=int, required=True)
    parser.add_argument("--github-run-attempt", type=int, required=True)
    parser.add_argument("--github-sha", required=True)
    parser.add_argument("--github-ref", required=True)
    parser.add_argument("--producer-run-id", type=int, required=True)
    parser.add_argument("--producer-artifact-id", type=int, required=True)
    parser.add_argument("--producer-artifact-digest", required=True)


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    toolchain = commands.add_parser("capture-toolchain")
    toolchain.add_argument("--policy", dest="policy_path", type=Path, required=True)
    toolchain.add_argument("--dotnet", type=Path, required=True)
    toolchain.add_argument("--java-home", type=Path, required=True)
    toolchain.add_argument("--android-sdk", type=Path, required=True)
    toolchain.add_argument("--bundletool", type=Path, required=True)
    toolchain.add_argument("--runner-image", required=True)
    toolchain.add_argument("--runner-image-version", required=True)
    toolchain.add_argument("--output", type=Path, required=True)
    producer = commands.add_parser("producer")
    _producer_arguments(producer)
    producer.add_argument("--output", type=Path, required=True)
    verify_producer = commands.add_parser("verify-producer")
    _producer_arguments(verify_producer)
    verify_producer.add_argument("--receipt", type=Path, required=True)
    rebuild = commands.add_parser("rebuild")
    _rebuild_arguments(rebuild)
    rebuild.add_argument("--output", type=Path, required=True)
    verify_rebuild = commands.add_parser("verify-rebuild")
    _rebuild_arguments(verify_rebuild)
    verify_rebuild.add_argument("--receipt", type=Path, required=True)
    eligibility = commands.add_parser("signer-eligibility")
    eligibility.add_argument("--producer", type=Path, required=True)
    eligibility.add_argument("--rebuild", type=Path, required=True)
    eligibility.add_argument("--output", type=Path, required=True)
    verify_eligibility = commands.add_parser("verify-signer-eligibility")
    verify_eligibility.add_argument("--producer", type=Path, required=True)
    verify_eligibility.add_argument("--rebuild", type=Path, required=True)
    verify_eligibility.add_argument("--receipt", type=Path, required=True)
    args = parser.parse_args(argv)
    if args.command == "capture-toolchain":
        value = capture_toolchain(
            policy_path=args.policy_path,
            dotnet=args.dotnet,
            java_home=args.java_home,
            android_sdk=args.android_sdk,
            bundletool=args.bundletool,
            runner_image=args.runner_image,
            runner_image_version=args.runner_image_version,
        )
        write_exclusive(args.output, "PREVIEW12_TOOLCHAIN.generated.json", value)
    elif args.command in {"producer", "verify-producer"}:
        inputs = {key: value for key, value in vars(args).items() if key not in {"command", "output", "receipt"}}
        value = create_producer(**inputs)
        if args.command == "producer":
            write_exclusive(args.output, PRODUCER_OUTPUT, value)
        else:
            observed = StableFile(args.receipt, "producer receipt", MAX_JSON_BYTES).json()
            if validate_producer(observed) != value:
                raise ValueError("producer receipt does not replay from exact inputs")
    elif args.command in {"rebuild", "verify-rebuild"}:
        inputs = {key: value for key, value in vars(args).items() if key not in {"command", "output", "receipt"}}
        value = create_rebuild(**inputs)
        if args.command == "rebuild":
            write_exclusive(args.output, REBUILD_OUTPUT, value)
        else:
            observed = StableFile(args.receipt, "rebuild receipt", MAX_JSON_BYTES).json()
            if validate_rebuild(observed) != value:
                raise ValueError("independent rebuild receipt does not replay from exact inputs")
    elif args.command == "signer-eligibility":
        producer_value = StableFile(args.producer, "producer receipt", MAX_JSON_BYTES).json()
        rebuild_value = StableFile(args.rebuild, "rebuild receipt", MAX_JSON_BYTES).json()
        write_exclusive(
            args.output,
            ELIGIBILITY_OUTPUT,
            create_signer_eligibility(producer_value, rebuild_value),
        )
    else:
        producer_value = StableFile(args.producer, "producer receipt", MAX_JSON_BYTES).json()
        rebuild_value = StableFile(args.rebuild, "rebuild receipt", MAX_JSON_BYTES).json()
        observed = StableFile(args.receipt, "signer eligibility", MAX_JSON_BYTES).json()
        expected = create_signer_eligibility(producer_value, rebuild_value)
        if validate_signer_eligibility(observed) != expected:
            raise ValueError("signer eligibility does not replay from exact receipts")
    print(
        f"preview12_candidate_contract={args.command}=pass "
        "signing_authorized=false google_play_upload_authorized=false publication_authorized=false"
    )
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (OSError, ValueError, subprocess.TimeoutExpired) as error:
        print(f"preview12_candidate_contract=blocked reason={error}", file=sys.stderr)
        raise SystemExit(2) from error
