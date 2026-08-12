#!/usr/bin/env python3
"""Read the canonical Android version pair from the app project."""

from __future__ import annotations

import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path


VERSION_NAME_RE = re.compile(r"[0-9]+(?:\.[0-9]+){2}(?:-[0-9A-Za-z][0-9A-Za-z.-]*)?")
VERSION_CODE_RE = re.compile(r"[1-9][0-9]*")


def read_project_version(project_path: Path) -> tuple[str, str]:
    root = ET.parse(project_path).getroot()

    def values(name: str) -> list[str]:
        return [
            (element.text or "").strip()
            for element in root.iter()
            if element.tag.rsplit("}", 1)[-1] == name and (element.text or "").strip()
        ]

    version_names = values("ApplicationDisplayVersion")
    version_codes = values("ApplicationVersion")
    if len(version_names) != 1 or VERSION_NAME_RE.fullmatch(version_names[0]) is None:
        raise SystemExit("Android project must declare one canonical ApplicationDisplayVersion")
    if len(version_codes) != 1 or VERSION_CODE_RE.fullmatch(version_codes[0]) is None:
        raise SystemExit("Android project must declare one positive integer ApplicationVersion")
    return version_names[0], version_codes[0]


def main() -> None:
    if len(sys.argv) != 2:
        raise SystemExit("usage: read_android_version.py PROJECT.csproj")
    version_name, version_code = read_project_version(Path(sys.argv[1]).resolve())
    print(f"{version_name}\t{version_code}")


if __name__ == "__main__":
    main()
