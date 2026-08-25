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


def _digest_character_class(value: str) -> str:
    if re.fullmatch(r"[0-9A-Fa-f]+", value):
        return "hex"
    if re.fullmatch(r"(?:[0-9A-Fa-f]{2}:)+[0-9A-Fa-f]{2}", value):
        return "colon-delimited-hex-bytes"
    return "other"


def extract_certificate_sha256(output: str) -> tuple[str, str]:
    matches: list[tuple[str, str]] = []
    certificate_digest_line_count = 0
    observed_labels: list[str] = []
    for line in output.splitlines():
        delimiter = " certificate SHA-256 digest:"
        if delimiter in line:
            certificate_digest_line_count += 1
            raw_label, _, raw_digest = line.partition(delimiter)
            safe_label = re.sub(r"[^A-Za-z0-9 #().,=+_-]", "?", raw_label)[:160]
            digest_value = raw_digest.strip()
            observed_labels.append(
                f"{safe_label} (digest_length={len(digest_value)}, "
                f"digest_class={_digest_character_class(digest_value)})"
            )
        for label, pattern in _ACCEPTED_LABELS:
            match = pattern.fullmatch(line)
            if match is not None:
                matches.append((label, match.group("digest").lower()))
                break

    if certificate_digest_line_count != 1 or len(matches) != 1:
        raise CertificateDigestError(
            "expected exactly one certificate SHA-256 line and one accepted signer line; "
            f"accepted={len(matches)}, certificate_digest_lines="
            f"{certificate_digest_line_count}, observed_labels={observed_labels!r}"
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
