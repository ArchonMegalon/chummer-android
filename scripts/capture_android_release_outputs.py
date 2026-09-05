#!/usr/bin/env python3
"""Capture release outputs through stable file descriptors and promote by inode."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import stat


CONTRACT = "chummer.android.stable-release-output-capture/v1"
MAX_AAB_BYTES = 512 * 1024 * 1024
MAX_GRAPH_BYTES = 16 * 1024 * 1024
CHUNK = 1024 * 1024


def _canonical_json(value: object) -> bytes:
    return (json.dumps(value, indent=2, sort_keys=True) + "\n").encode("utf-8")


def _directory(path: Path, label: str, *, private: bool) -> Path:
    if not path.is_absolute() or path.is_symlink() or not path.is_dir():
        raise ValueError(f"{label} must be an absolute regular directory")
    resolved = path.resolve(strict=True)
    metadata = resolved.stat()
    if resolved != path or metadata.st_uid != os.getuid():
        raise ValueError(f"{label} must be canonical and owner-owned")
    if private and stat.S_IMODE(metadata.st_mode) & 0o077:
        raise ValueError(f"{label} must be owner-only")
    return resolved


def _identity(metadata: os.stat_result) -> dict[str, int]:
    return {
        "device": metadata.st_dev,
        "inode": metadata.st_ino,
        "sizeBytes": metadata.st_size,
        "mtimeNs": metadata.st_mtime_ns,
        "ctimeNs": metadata.st_ctime_ns,
    }


def _stable_capture(source: Path, target: Path, *, limit: int, mode: int) -> dict[str, object]:
    if not source.is_absolute() or source.is_symlink() or not source.is_file():
        raise ValueError("release capture source must be an absolute regular non-symlink file")
    source_parent = source.parent.resolve(strict=True)
    if source_parent != source.parent:
        raise ValueError("release capture source parent is not canonical")
    source_fd = os.open(source, os.O_RDONLY | getattr(os, "O_NOFOLLOW", 0))
    target_fd = -1
    try:
        before = os.fstat(source_fd)
        path_before = os.stat(source, follow_symlinks=False)
        if not stat.S_ISREG(before.st_mode) or _identity(before) != _identity(path_before):
            raise ValueError("release capture source path does not identify the opened file")
        if before.st_size <= 0 or before.st_size > limit:
            raise ValueError("release capture source size is outside its bound")
        target_fd = os.open(
            target,
            os.O_WRONLY | os.O_CREAT | os.O_EXCL | getattr(os, "O_NOFOLLOW", 0),
            mode,
        )
        digest = hashlib.sha256()
        copied = 0
        while True:
            chunk = os.read(source_fd, CHUNK)
            if not chunk:
                break
            copied += len(chunk)
            if copied > limit:
                raise ValueError("release capture source grew beyond its bound")
            digest.update(chunk)
            view = memoryview(chunk)
            while view:
                written = os.write(target_fd, view)
                if written <= 0:
                    raise OSError("release capture write made no progress")
                view = view[written:]
        os.fsync(target_fd)
        after = os.fstat(source_fd)
        path_after = os.stat(source, follow_symlinks=False)
        if (
            _identity(before) != _identity(after)
            or _identity(after) != _identity(path_after)
            or copied != before.st_size
        ):
            raise ValueError("release capture source changed while being captured")
        captured = os.fstat(target_fd)
        if not stat.S_ISREG(captured.st_mode) or captured.st_size != copied:
            raise ValueError("captured release output is not the exact copied file")
        return {
            "sourceIdentity": _identity(before),
            "capturedIdentity": _identity(captured),
            "sha256": digest.hexdigest(),
            "sizeBytes": copied,
        }
    except Exception:
        target.unlink(missing_ok=True)
        raise
    finally:
        if target_fd >= 0:
            os.close(target_fd)
        os.close(source_fd)


def _write_new(path: Path, raw: bytes, mode: int) -> None:
    descriptor = os.open(
        path,
        os.O_WRONLY | os.O_CREAT | os.O_EXCL | getattr(os, "O_NOFOLLOW", 0),
        mode,
    )
    try:
        with os.fdopen(descriptor, "wb") as stream:
            stream.write(raw)
            stream.flush()
            os.fsync(stream.fileno())
    except Exception:
        path.unlink(missing_ok=True)
        raise


def capture(
    aab: Path,
    graph: Path,
    capture_dir: Path,
    final_aab_name: str,
    final_graph_name: str,
) -> dict[str, object]:
    capture_dir = _directory(capture_dir, "release capture directory", private=True)
    if any(capture_dir.iterdir()):
        raise ValueError("release capture directory must be empty")
    if (
        Path(final_aab_name).name != final_aab_name
        or Path(final_graph_name).name != final_graph_name
        or not final_aab_name.endswith(".aab")
        or not final_graph_name.endswith(".json")
    ):
        raise ValueError("final release output names are not canonical")
    captured_aab = capture_dir / "captured.aab"
    captured_graph = capture_dir / "captured-source-graph.json"
    aab_claim = _stable_capture(aab, captured_aab, limit=MAX_AAB_BYTES, mode=0o444)
    graph_claim = _stable_capture(graph, captured_graph, limit=MAX_GRAPH_BYTES, mode=0o400)
    sidecar = capture_dir / "captured-output.sha256"
    sidecar_raw = (
        f"{aab_claim['sha256']}  artifacts/{final_aab_name}\n"
        f"{graph_claim['sha256']}  artifacts/{final_graph_name}\n"
    ).encode("ascii")
    _write_new(sidecar, sidecar_raw, 0o400)
    sidecar_metadata = sidecar.stat()
    receipt = {
        "contractName": CONTRACT,
        "publicationAuthorized": False,
        "outputs": {
            "aab": {
                "capturedName": captured_aab.name,
                "finalName": final_aab_name,
                **aab_claim,
            },
            "sourceGraph": {
                "capturedName": captured_graph.name,
                "finalName": final_graph_name,
                **graph_claim,
            },
            "buildSidecar": {
                "capturedName": sidecar.name,
                "finalName": f"{final_aab_name}.sha256",
                "capturedIdentity": _identity(sidecar_metadata),
                "sha256": hashlib.sha256(sidecar_raw).hexdigest(),
                "sizeBytes": len(sidecar_raw),
            },
        },
    }
    receipt_path = capture_dir / "capture-receipt.json"
    _write_new(receipt_path, _canonical_json(receipt), 0o400)
    return receipt


def _load_receipt(capture_dir: Path) -> dict[str, object]:
    raw = (capture_dir / "capture-receipt.json").read_bytes()
    try:
        value = json.loads(raw.decode("utf-8"))
    except (UnicodeDecodeError, json.JSONDecodeError) as error:
        raise ValueError("release capture receipt is invalid") from error
    if _canonical_json(value) != raw or value.get("contractName") != CONTRACT:
        raise ValueError("release capture receipt is not canonical")
    if value.get("publicationAuthorized") is not False:
        raise ValueError("release capture receipt escalates publication authority")
    return value


def _verify_captured(
    path: Path,
    claim: dict[str, object],
    *,
    allow_link_ctime_change: bool = False,
) -> os.stat_result:
    metadata = os.stat(path, follow_symlinks=False)
    expected = claim.get("capturedIdentity")
    actual = _identity(metadata)
    if allow_link_ctime_change and isinstance(expected, dict):
        identity_matches = all(
            actual.get(name) == expected.get(name)
            for name in ("device", "inode", "sizeBytes", "mtimeNs")
        ) and actual["ctimeNs"] >= expected.get("ctimeNs", actual["ctimeNs"] + 1)
    else:
        identity_matches = actual == expected
    if not stat.S_ISREG(metadata.st_mode) or not identity_matches:
        raise ValueError("captured release output identity changed")
    digest = hashlib.sha256()
    descriptor = os.open(path, os.O_RDONLY | getattr(os, "O_NOFOLLOW", 0))
    try:
        while chunk := os.read(descriptor, CHUNK):
            digest.update(chunk)
        if _identity(os.fstat(descriptor)) != _identity(metadata):
            raise ValueError("captured release output changed while verified")
    finally:
        os.close(descriptor)
    if digest.hexdigest() != claim.get("sha256") or metadata.st_size != claim.get("sizeBytes"):
        raise ValueError("captured release output bytes changed")
    return metadata


def promote(
    capture_dir: Path,
    output_aab: Path,
    output_graph: Path,
    output_sidecar: Path,
) -> None:
    capture_dir = _directory(capture_dir, "release capture directory", private=True)
    receipt = _load_receipt(capture_dir)
    outputs = receipt.get("outputs")
    if not isinstance(outputs, dict) or set(outputs) != {"aab", "sourceGraph", "buildSidecar"}:
        raise ValueError("release capture output inventory is invalid")
    destinations = {
        "aab": output_aab,
        "sourceGraph": output_graph,
        "buildSidecar": output_sidecar,
    }
    linked: list[tuple[Path, tuple[int, int]]] = []
    try:
        for name, destination in destinations.items():
            claim = outputs[name]
            if not isinstance(claim, dict):
                raise ValueError("release capture output claim is invalid")
            source = capture_dir / str(claim.get("capturedName"))
            if source.parent != capture_dir or destination.name != claim.get("finalName"):
                raise ValueError("release capture output naming changed")
            source_metadata = _verify_captured(source, claim)
            parent = _directory(destination.parent, "release artifact directory", private=False)
            if source_metadata.st_dev != parent.stat().st_dev:
                raise ValueError("release capture and artifact directory are not on one filesystem")
            if destination.exists() or destination.is_symlink():
                raise ValueError("release artifact output already exists")
            os.link(source, destination, follow_symlinks=False)
            target_metadata = os.stat(destination, follow_symlinks=False)
            if (target_metadata.st_dev, target_metadata.st_ino) != (
                source_metadata.st_dev,
                source_metadata.st_ino,
            ):
                raise ValueError("release artifact promotion did not retain the captured inode")
            linked.append((destination, (target_metadata.st_dev, target_metadata.st_ino)))
            _verify_captured(destination, claim, allow_link_ctime_change=True)
    except Exception:
        for path, identity in reversed(linked):
            try:
                metadata = os.stat(path, follow_symlinks=False)
                if (metadata.st_dev, metadata.st_ino) == identity:
                    path.unlink()
            except FileNotFoundError:
                pass
        raise


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    actions = parser.add_subparsers(dest="action", required=True)
    capture_parser = actions.add_parser("capture")
    capture_parser.add_argument("--aab", required=True, type=Path)
    capture_parser.add_argument("--source-graph", required=True, type=Path)
    capture_parser.add_argument("--capture-dir", required=True, type=Path)
    capture_parser.add_argument("--final-aab-name", required=True)
    capture_parser.add_argument("--final-graph-name", required=True)
    promote_parser = actions.add_parser("promote")
    promote_parser.add_argument("--capture-dir", required=True, type=Path)
    promote_parser.add_argument("--output-aab", required=True, type=Path)
    promote_parser.add_argument("--output-source-graph", required=True, type=Path)
    promote_parser.add_argument("--output-sidecar", required=True, type=Path)
    args = parser.parse_args()
    try:
        if args.action == "capture":
            result = capture(
                args.aab,
                args.source_graph,
                args.capture_dir,
                args.final_aab_name,
                args.final_graph_name,
            )
            print(json.dumps({"status": "pass", "publicationAuthorized": False, "outputs": result["outputs"]}, sort_keys=True))
        else:
            promote(
                args.capture_dir,
                args.output_aab,
                args.output_source_graph,
                args.output_sidecar,
            )
            print("android_release_output_promotion=passed publication_authorized=false")
    except (OSError, ValueError) as error:
        print(f"android_release_output_capture=failed error={error}")
        return 2
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
