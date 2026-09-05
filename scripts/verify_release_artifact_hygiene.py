#!/usr/bin/env python3
"""Fail closed when protected release-input identities leak into an AAB."""

from __future__ import annotations

import argparse
from pathlib import Path
import zipfile


ENV_MARKERS = (
    b"CHUMMER_ANDROID_TWO_GREEN_ELIGIBILITY_RECEIPT",
    b"CHUMMER_ANDROID_TWO_GREEN_RELEASE_APPROVAL",
)


def verify(aab: Path, forbidden_paths: list[Path]) -> None:
    markers = list(ENV_MARKERS)
    for path in forbidden_paths:
        markers.extend((str(path).encode(), path.name.encode()))
    with zipfile.ZipFile(aab) as archive:
        for info in archive.infolist():
            name = info.filename.encode()
            if any(marker and marker in name for marker in markers):
                raise ValueError("protected release input identity leaked into AAB")
            overlap = max(map(len, markers), default=1) - 1
            tail = b""
            with archive.open(info) as entry:
                while chunk := entry.read(1024 * 1024):
                    window = tail + chunk
                    if any(marker and marker in window for marker in markers):
                        raise ValueError("protected release input identity leaked into AAB")
                    tail = window[-overlap:] if overlap else b""


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--aab", required=True, type=Path)
    parser.add_argument("--forbidden-path", action="append", default=[], type=Path)
    arguments = parser.parse_args()
    try:
        verify(arguments.aab, arguments.forbidden_path)
    except (OSError, ValueError, zipfile.BadZipFile) as error:
        print(f"android_release_artifact_hygiene=failed error={error}")
        return 2
    print("android_release_artifact_hygiene=passed")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
