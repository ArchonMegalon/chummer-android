#!/usr/bin/env python3
"""Extract one signing-certificate SHA-256 from versioned apksigner output."""

from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path


_DIGEST = r"(?P<digest>[0-9A-Fa-f]{64})"
_ACCEPTED_LABELS = (
    (
        "numbered",
        re.compile(
            rf"^Signer #[1-9][0-9]* certificate SHA-256 digest: {_DIGEST}$"
        ),
    ),
    (
        "sdk-range",
        re.compile(
            rf"^Signer \(minSdkVersion=[0-9]+, maxSdkVersion=[0-9]+\) "
            rf"certificate SHA-256 digest: {_DIGEST}$"
        ),
    ),
    (
        "sdk-range-dev-release",
        re.compile(
            rf"^Signer \(minSdkVersion=[0-9]+ \(dev release=true\), "
            rf"maxSdkVersion=[0-9]+\) certificate SHA-256 digest: {_DIGEST}$"
        ),
    ),
)


class CertificateDigestError(ValueError):
    """Raised when apksigner output is not a single accepted certificate binding."""


def extract_certificate_sha256(output: str) -> tuple[str, str]:
    matches: list[tuple[str, str]] = []
    certificate_digest_line_count = 0
    for line in output.splitlines():
        if "certificate SHA-256 digest:" in line:
            certificate_digest_line_count += 1
        for label, pattern in _ACCEPTED_LABELS:
            match = pattern.fullmatch(line)
            if match is not None:
                matches.append((label, match.group("digest").lower()))
                break

    if certificate_digest_line_count != 1 or len(matches) != 1:
        raise CertificateDigestError(
            "expected exactly one certificate SHA-256 line and one accepted signer line; "
            f"accepted={len(matches)}, certificate_digest_lines="
            f"{certificate_digest_line_count}"
        )

    return matches[0]


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("apksigner_output", type=Path)
    args = parser.parse_args()

    try:
        label, digest = extract_certificate_sha256(
            args.apksigner_output.read_text(encoding="utf-8")
        )
    except (CertificateDigestError, OSError, UnicodeError) as error:
        print(f"certificate digest extraction failed: {error}", file=sys.stderr)
        return 1

    print(
        f"accepted signer certificate label={label} digest_length={len(digest)}",
        file=sys.stderr,
    )
    print(digest)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
