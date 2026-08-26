#!/usr/bin/env python3
"""Fail closed when the Android canonical content bundle is incomplete or miswired."""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import subprocess
import sys
import zipfile
from collections import Counter
from pathlib import Path, PurePosixPath
from typing import Any


SCHEMA = "chummer.android.content-bundle/v1"
CORE_REVISION = "2fb2ae9bb48e5a1a6b25a174ba88008ce995fcd5"
PACKAGED_ROOT = "assets/chummer-content"
MANIFEST_ENTRY = f"{PACKAGED_ROOT}/manifest.json"
CANONICAL_SEGMENTS = ("data", "lang")
SHA256_PATTERN = re.compile(r"[0-9a-f]{64}")


def _sha256_bytes(value: bytes) -> str:
    return hashlib.sha256(value).hexdigest()


def _sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def _bundle_digest(files: list[dict[str, Any]]) -> str:
    digest = hashlib.sha256()
    digest.update(f"{SCHEMA}\n{CORE_REVISION}\n".encode("utf-8"))
    for entry in files:
        digest.update(
            f"{entry['path']}\0{entry['size']}\0{entry['sha256']}\n".encode("utf-8")
        )
    return digest.hexdigest()


def _safe_manifest_path(value: Any) -> bool:
    if not isinstance(value, str) or not value or "\\" in value or ":" in value:
        return False
    path = PurePosixPath(value)
    return (
        not path.is_absolute()
        and all(segment not in {"", ".", ".."} for segment in path.parts)
        and len(path.parts) > 1
        and path.parts[0] in CANONICAL_SEGMENTS
        and path.as_posix() == value
    )


def collect_canonical_files(
    core_root: Path,
) -> tuple[dict[str, tuple[int, str]], list[str]]:
    issues: list[str] = []
    if core_root.is_symlink():
        return {}, [f"canonical-core-root-symlink:{core_root}"]
    if not core_root.is_dir():
        return {}, [f"canonical-core-root-unavailable:{core_root}"]

    try:
        head = subprocess.run(
            ["git", "-C", str(core_root), "rev-parse", "HEAD"],
            check=True,
            capture_output=True,
            text=True,
        ).stdout.strip()
        tracked_output = subprocess.run(
            [
                "git",
                "-C",
                str(core_root),
                "ls-files",
                "-z",
                "--",
                "Chummer/data",
                "Chummer/lang",
            ],
            check=True,
            capture_output=True,
        ).stdout
        dirty = subprocess.run(
            [
                "git",
                "-C",
                str(core_root),
                "status",
                "--porcelain=v1",
                "--untracked-files=all",
                "--",
                "Chummer/data",
                "Chummer/lang",
            ],
            check=True,
            capture_output=True,
            text=True,
        ).stdout.strip()
    except (OSError, subprocess.CalledProcessError) as error:
        return {}, [f"canonical-core-git-provenance-unavailable:{core_root}:{error}"]
    if head != CORE_REVISION:
        issues.append(f"canonical-core-revision-mismatch:{head}")
    if dirty:
        issues.append("canonical-core-content-tree-dirty")

    content_root = core_root / "Chummer"
    if content_root.is_symlink() or not content_root.is_dir():
        return {}, [f"canonical-content-root-unavailable:{content_root}"]
    files: dict[str, tuple[int, str]] = {}
    for segment in CANONICAL_SEGMENTS:
        source_root = content_root / segment
        if source_root.is_symlink() or not source_root.is_dir():
            issues.append(f"canonical-content-root-unavailable:{source_root}")
            continue
    tracked_paths = [
        raw.decode("utf-8")
        for raw in tracked_output.split(b"\0")
        if raw
    ]
    for tracked_path in tracked_paths:
        path = core_root / tracked_path
        if path.is_symlink():
            issues.append(f"canonical-content-symlink:{path}")
        elif not path.is_file():
            issues.append(f"canonical-content-file-unavailable:{path}")
        else:
            relative = path.relative_to(content_root).as_posix()
            if not _safe_manifest_path(relative):
                issues.append(f"canonical-content-path-unsafe:{relative}")
            else:
                files[relative] = (path.stat().st_size, _sha256_file(path))

    if "data/lifemodules.xml" not in files:
        issues.append("required-catalog-missing:data/lifemodules.xml")
    if not any(path.startswith("lang/") and path.endswith(".xml") for path in files):
        issues.append("required-language-catalogs-missing")
    return files, sorted(issues)


def build_manifest(core_root: Path) -> tuple[dict[str, Any], list[str]]:
    canonical_files, issues = collect_canonical_files(core_root)
    entries = [
        {"path": path, "size": metadata[0], "sha256": metadata[1]}
        for path, metadata in sorted(canonical_files.items())
    ]
    return {
        "schema": SCHEMA,
        "coreRevision": CORE_REVISION,
        "bundleDigest": _bundle_digest(entries),
        "files": entries,
    }, issues


def validate_manifest(manifest: Any) -> list[str]:
    if not isinstance(manifest, dict):
        return ["content-manifest-not-an-object"]
    issues: list[str] = []
    if manifest.get("schema") != SCHEMA:
        issues.append("content-manifest-schema-invalid")
    if manifest.get("coreRevision") != CORE_REVISION:
        issues.append("content-manifest-core-revision-invalid")
    if not SHA256_PATTERN.fullmatch(str(manifest.get("bundleDigest", ""))):
        issues.append("content-manifest-bundle-digest-invalid")

    files = manifest.get("files")
    if not isinstance(files, list) or not files:
        return sorted(issues + ["content-manifest-files-invalid"])

    paths: list[str] = []
    for index, entry in enumerate(files):
        if not isinstance(entry, dict):
            issues.append(f"content-manifest-file-not-an-object:{index}")
            continue
        path = entry.get("path")
        if not _safe_manifest_path(path):
            issues.append(f"content-manifest-path-unsafe:{path}")
            continue
        paths.append(path)
        size = entry.get("size")
        if not isinstance(size, int) or isinstance(size, bool) or size < 0:
            issues.append(f"content-manifest-size-invalid:{path}")
        if not SHA256_PATTERN.fullmatch(str(entry.get("sha256", ""))):
            issues.append(f"content-manifest-sha256-invalid:{path}")

    if paths != sorted(paths) or len(paths) != len(set(paths)):
        issues.append("content-manifest-paths-not-unique-and-sorted")
    if "data/lifemodules.xml" not in paths:
        issues.append("content-manifest-required-catalog-missing")
    if not any(path.startswith("lang/") and path.endswith(".xml") for path in paths):
        issues.append("content-manifest-language-catalogs-missing")
    if not issues and manifest["bundleDigest"] != _bundle_digest(files):
        issues.append("content-manifest-bundle-digest-mismatch")
    return sorted(issues)


def verify_manifest_against_source(
    manifest: dict[str, Any],
    core_root: Path,
) -> list[str]:
    expected, issues = build_manifest(core_root)
    issues.extend(validate_manifest(manifest))
    if not issues and manifest != expected:
        issues.append("content-manifest-does-not-match-canonical-source")
    return sorted(issues)


def verify_project_contract(repo_root: Path) -> list[str]:
    project_path = repo_root / "src/Chummer.Android/Chummer.Android.csproj"
    maui_program_path = repo_root / "src/Chummer.Android/MauiProgram.cs"
    materializer_path = (
        repo_root
        / "src/Chummer.Android/Platforms/Android/AndroidBundledContentMaterializer.cs"
    )
    issues: list[str] = []
    try:
        project = project_path.read_text(encoding="utf-8")
        maui_program = maui_program_path.read_text(encoding="utf-8")
        materializer = materializer_path.read_text(encoding="utf-8")
    except OSError as error:
        return [f"android-content-contract-unavailable:{error}"]

    required_project_fragments = (
        "<ChummerCoreEngineRoot Condition=",
        "$(ChummerPresentationRoot)/../chummer-core-engine",
        '<AndroidAsset Include="$(ChummerCoreEngineRoot)/Chummer/data/**"',
        'LogicalName="chummer-content/data/%(RecursiveDir)%(Filename)%(Extension)"',
        '<AndroidAsset Include="$(ChummerCoreEngineRoot)/Chummer/lang/**"',
        'LogicalName="chummer-content/lang/%(RecursiveDir)%(Filename)%(Extension)"',
        '<AndroidAsset Include="Content/chummer-content-manifest.json"',
        'LogicalName="chummer-content/manifest.json"',
    )
    for fragment in required_project_fragments:
        if project.count(fragment) != 1:
            issues.append(f"android-asset-declaration-invalid:{fragment}")

    materialize_call = "string contentPath = AndroidBundledContentMaterializer.Materialize();"
    require_bundle_call = (
        'Environment.SetEnvironmentVariable("CHUMMER_REQUIRE_CONTENT_BUNDLE", "true");'
    )
    runtime_call = "builder.Services.AddChummerLocalRuntimeClient("
    for fragment in (materialize_call, require_bundle_call, runtime_call):
        if fragment not in maui_program:
            issues.append(f"content-runtime-registration-missing:{fragment}")
    if all(fragment in maui_program for fragment in (materialize_call, require_bundle_call, runtime_call)):
        if not (
            maui_program.index(materialize_call)
            < maui_program.index(require_bundle_call)
            < maui_program.index(runtime_call)
        ):
            issues.append("content-materialization-or-validation-occurs-after-di")
    if "builder.Services.AddChummerLocalRuntimeClient(\n            contentPath,\n            contentPath," not in maui_program:
        issues.append("runtime-client-not-bound-to-materialized-content-root")

    required_materializer_fragments = (
        'ManifestAssetPath = $"{PackagedContentRoot}/manifest.json"',
        f'CanonicalCoreRevision = "{CORE_REVISION}"',
        "manifest.CoreRevision,",
        "LoadAndValidateManifest(assets)",
        "Path.Combine(contentContainer, manifest.BundleDigest)",
        '$".staging-{manifest.BundleDigest}-{Guid.NewGuid():N}"',
        "VerifyMaterializedContent(stagingRoot, manifest);",
        "Directory.Move(stagingRoot, destinationRoot);",
        "QuarantineInvalidGeneration(contentContainer, destinationRoot, manifest.BundleDigest)",
        "EnumerateRegularFiles(contentRoot, contentRoot)",
        "EnsureRegularDirectory(contentContainer);",
        "CleanupInterruptedStaging(contentContainer);",
        "RejectReparsePoint(directoryPath);",
        "destination.Flush(flushToDisk: true);",
        '$"{ManifestSchema}\\n{CanonicalCoreRevision}\\n"',
        "SHA256.HashData(stream)",
        "ResolveDestinationPath(stagingRoot, relativePath)",
    )
    for fragment in required_materializer_fragments:
        if fragment not in materializer:
            issues.append(f"runtime-materializer-contract-invalid:{fragment}")
    if "Directory.Delete(destinationRoot" in materializer:
        issues.append("runtime-materializer-deletes-published-generation")
    if (
        "VerifyMaterializedContent(stagingRoot, manifest);" in materializer
        and "Directory.Move(stagingRoot, destinationRoot);" in materializer
        and materializer.index("VerifyMaterializedContent(stagingRoot, manifest);")
        > materializer.index("Directory.Move(stagingRoot, destinationRoot);")
    ):
        issues.append("materialized-content-validation-occurs-after-publish")
    return sorted(issues)


def verify_apk(
    apk_path: Path,
    manifest: dict[str, Any],
    manifest_bytes: bytes,
) -> tuple[int, list[str]]:
    issues: list[str] = []
    files = manifest.get("files", [])
    expected = {
        f"{PACKAGED_ROOT}/{entry['path']}": (entry["size"], entry["sha256"])
        for entry in files
    }
    expected_names = set(expected) | {MANIFEST_ENTRY}
    actual_canonical_count = 0
    try:
        with zipfile.ZipFile(apk_path) as archive:
            names = archive.namelist()
            duplicate_names = sorted(
                name for name, count in Counter(names).items() if count > 1
            )
            if duplicate_names:
                issues.append(f"signed-apk-duplicate-members:{duplicate_names}")
            actual_names = {
                name
                for name in names
                if name.startswith(f"{PACKAGED_ROOT}/") and not name.endswith("/")
            }
            actual_canonical_count = len(set(expected) & actual_names)
            missing = sorted(expected_names - actual_names)
            unexpected = sorted(actual_names - expected_names)
            if missing:
                issues.append(f"signed-apk-canonical-content-missing:{missing}")
            if unexpected:
                issues.append(f"signed-apk-canonical-content-unexpected:{unexpected}")
            if MANIFEST_ENTRY in actual_names:
                if archive.read(MANIFEST_ENTRY) != manifest_bytes:
                    issues.append("signed-apk-content-manifest-bytes-mismatch")
            for name, (expected_size, expected_digest) in sorted(expected.items()):
                if name not in actual_names:
                    continue
                value = archive.read(name)
                if len(value) != expected_size:
                    issues.append(f"signed-apk-content-size-mismatch:{name}")
                if _sha256_bytes(value) != expected_digest:
                    issues.append(f"signed-apk-content-sha256-mismatch:{name}")
    except (OSError, zipfile.BadZipFile) as error:
        return 0, [f"signed-apk-unreadable:{apk_path}:{error}"]
    return actual_canonical_count, sorted(issues)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--repo-root",
        type=Path,
        default=Path(__file__).resolve().parents[1],
    )
    parser.add_argument("--core-root", type=Path)
    parser.add_argument("--manifest", type=Path)
    parser.add_argument("--apk", type=Path)
    parser.add_argument("--receipt", type=Path)
    action = parser.add_mutually_exclusive_group(required=True)
    action.add_argument("--check", action="store_true")
    action.add_argument("--write-manifest", action="store_true")
    args = parser.parse_args()

    repo_root = args.repo_root.resolve()
    core_root = args.core_root or repo_root.parent / "chummer-core-engine"
    manifest_path = args.manifest or (
        repo_root / "src/Chummer.Android/Content/chummer-content-manifest.json"
    )
    issues = verify_project_contract(repo_root)
    if args.write_manifest:
        generated, generation_issues = build_manifest(core_root)
        issues.extend(generation_issues)
        if not generation_issues:
            manifest_path.parent.mkdir(parents=True, exist_ok=True)
            temporary_path = manifest_path.with_suffix(manifest_path.suffix + ".tmp")
            temporary_path.write_text(
                json.dumps(generated, indent=2, sort_keys=False) + "\n",
                encoding="utf-8",
            )
            temporary_path.replace(manifest_path)
    manifest: dict[str, Any] = {}
    manifest_bytes = b""
    try:
        if manifest_path.is_symlink():
            raise OSError("manifest path must not be a symlink")
        manifest_bytes = manifest_path.read_bytes()
        loaded = json.loads(manifest_bytes)
        if isinstance(loaded, dict):
            manifest = loaded
        else:
            issues.append("content-manifest-not-an-object")
    except (OSError, json.JSONDecodeError) as error:
        issues.append(f"content-manifest-unreadable:{manifest_path}:{error}")

    if manifest:
        issues.extend(verify_manifest_against_source(manifest, core_root))
    apk_file_count = 0
    if args.apk is not None and manifest:
        apk_file_count, apk_issues = verify_apk(
            args.apk.resolve(),
            manifest,
            manifest_bytes,
        )
        issues.extend(apk_issues)

    payload = {
        "status": "pass" if not issues else "blocked",
        "schema": SCHEMA,
        "coreRevision": CORE_REVISION,
        "bundleDigest": manifest.get("bundleDigest"),
        "manifestSha256": _sha256_bytes(manifest_bytes) if manifest_bytes else None,
        "apkSha256": (
            _sha256_file(args.apk.resolve())
            if args.apk is not None and args.apk.is_file()
            else None
        ),
        "canonicalFileCount": len(manifest.get("files", [])),
        "canonicalByteCount": sum(
            entry.get("size", 0)
            for entry in manifest.get("files", [])
            if isinstance(entry, dict) and isinstance(entry.get("size"), int)
        ),
        "apkCanonicalFileCount": apk_file_count,
        "apkVerified": args.apk is not None,
        "issues": sorted(set(issues)),
    }
    receipt = json.dumps(payload, indent=2, sort_keys=True) + "\n"
    print(receipt, end="")
    if args.receipt is not None:
        receipt_path = args.receipt.resolve()
        receipt_path.parent.mkdir(parents=True, exist_ok=True)
        temporary_receipt = receipt_path.with_suffix(receipt_path.suffix + ".tmp")
        temporary_receipt.write_text(receipt, encoding="utf-8")
        temporary_receipt.replace(receipt_path)
    return 0 if not issues else 2


if __name__ == "__main__":
    raise SystemExit(main())
