#!/usr/bin/env python3
"""Resolve one explicit, monotonic Android release identity."""

from __future__ import annotations

import argparse
import re
import xml.etree.ElementTree as ET
from pathlib import Path

from read_android_version import read_project_version


VERSION_NAME = re.compile(
    r"[0-9]+(?:\.[0-9]+){2}(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?"
)
VERSION_CODE = re.compile(r"[1-9][0-9]*")
HISTORICAL_VERSION_CODE_FLOOR = 11


def resolve_release_intent(
    project: Path,
    expected_version_name: str,
    expected_version_code: str,
    *,
    minimum_exclusive_code: int = HISTORICAL_VERSION_CODE_FLOOR,
) -> tuple[str, int]:
    if len(expected_version_name) > 128 or VERSION_NAME.fullmatch(expected_version_name) is None:
        raise ValueError("expected version name is missing or noncanonical")
    if VERSION_CODE.fullmatch(expected_version_code) is None:
        raise ValueError("expected version code is missing or noncanonical")
    version_code = int(expected_version_code)
    if version_code <= minimum_exclusive_code:
        raise ValueError(
            f"expected version code must be greater than {minimum_exclusive_code}"
        )

    project_version_name, project_version_code = read_project_version(project)
    if project_version_name != expected_version_name:
        raise ValueError("expected version name does not match the Android project")
    if project_version_code != expected_version_code:
        raise ValueError("expected version code does not match the Android project")
    return expected_version_name, version_code


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--project", required=True, type=Path)
    parser.add_argument("--expected-version-name", required=True)
    parser.add_argument("--expected-version-code", required=True)
    arguments = parser.parse_args()
    try:
        version_name, version_code = resolve_release_intent(
            arguments.project.resolve(),
            arguments.expected_version_name,
            arguments.expected_version_code,
        )
    except (OSError, ValueError, ET.ParseError) as error:
        raise SystemExit(f"Android release intent is invalid: {error}") from error
    print(f"{version_name}\t{version_code}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
