#!/usr/bin/env python3
"""Fail-closed structural inspection for a bundletool-extracted Android manifest."""

from __future__ import annotations

import sys
import os
import xml.etree.ElementTree as ET
import zipfile
from pathlib import Path

ANDROID = "{http://schemas.android.com/apk/res/android}"
PACKAGE_ID = "com.myexternalbrain.chummer"
ALLOWED_PERMISSIONS = {
    "android.permission.ACCESS_NETWORK_STATE",
    "android.permission.INTERNET",
    f"{PACKAGE_ID}.DYNAMIC_RECEIVER_NOT_EXPORTED_PERMISSION",
}


def require(condition: bool, message: str) -> None:
    if not condition:
        raise SystemExit(f"AAB inspection failed: {message}")


def attr(element: ET.Element, name: str) -> str | None:
    return element.get(f"{ANDROID}{name}")


def inspect(aab_path: Path, manifest_path: Path, *, require_unsigned: bool = False) -> None:
    expected_version_name = os.environ.get("CHUMMER_EXPECTED_VERSION_NAME")
    expected_version_code = os.environ.get("CHUMMER_EXPECTED_VERSION_CODE")
    require(bool(expected_version_name), "protected expected version name is missing")
    require(bool(expected_version_code), "protected expected version code is missing")
    root = ET.parse(manifest_path).getroot()
    require(root.get("package") == PACKAGE_ID, "unexpected package id")
    require(attr(root, "compileSdkVersion") == "36", "compile SDK must be 36")
    require(attr(root, "versionCode") == expected_version_code, "unexpected preview version code")
    require(attr(root, "versionName") == expected_version_name, "unexpected preview version name")

    uses_sdk = root.find("uses-sdk")
    require(uses_sdk is not None, "uses-sdk is missing")
    require(attr(uses_sdk, "minSdkVersion") == "24", "minimum SDK must be 24")
    require(attr(uses_sdk, "targetSdkVersion") == "36", "target SDK must be 36")

    permissions = {attr(item, "name") for item in root.findall("uses-permission")}
    require(permissions == ALLOWED_PERMISSIONS, f"unexpected permission set: {sorted(permissions)}")

    application = root.find("application")
    require(application is not None, "application element is missing")
    require(attr(application, "allowBackup") == "false", "Android backup must be disabled")
    require(attr(application, "usesCleartextTraffic") == "false", "cleartext traffic must be disabled")

    launcher = None
    for activity in application.findall("activity"):
        for intent_filter in activity.findall("intent-filter"):
            actions = {attr(item, "name") for item in intent_filter.findall("action")}
            if "android.intent.action.MAIN" in actions:
                launcher = activity
                break
    require(launcher is not None, "launcher activity is missing")
    require(attr(launcher, "exported") == "true", "launcher activity must be exported")
    require(
        attr(launcher, "enableOnBackInvokedCallback") == "true",
        "predictive-back callback integration must be enabled",
    )

    auto_verify_declared_link = False
    for intent_filter in launcher.findall("intent-filter"):
        if attr(intent_filter, "autoVerify") != "true":
            continue
        data = intent_filter.findall("data")
        values = {(attr(item, "scheme"), attr(item, "host"), attr(item, "path")) for item in data}
        schemes = {item[0] for item in values}
        hosts = {item[1] for item in values}
        paths = {item[2] for item in values}
        if "https" in schemes and "chummer.run" in hosts and "/app/install-link" in paths:
            auto_verify_declared_link = True
    require(
        auto_verify_declared_link,
        "autoVerify-declared https://chummer.run/app/install-link intent filter is missing",
    )

    with zipfile.ZipFile(aab_path) as bundle:
        names = bundle.namelist()
        if require_unsigned:
            # A JAR manifest alone can be unsigned. Signature instruction files,
            # algorithm blocks and reserved SIG-* files cannot enter this lane,
            # regardless of the bundle filename or whether the signature is valid.
            for entry in bundle.infolist():
                name = entry.orig_filename
                require(name == entry.filename and "\\" not in name and not name.startswith("/"),
                        "noncanonical ZIP name in unsigned bundle")
                parts = name.rstrip("/").split("/")
                require(all(part not in ("", ".", "..") for part in parts),
                        "noncanonical ZIP name in unsigned bundle")
                if len(parts) == 2 and parts[0].upper() == "META-INF":
                    leaf = parts[1].upper()
                    require(not (leaf.endswith((".SF", ".RSA", ".DSA", ".EC")) or leaf.startswith("SIG-")),
                            "JAR signature metadata is forbidden in an unsigned bundle")
            print("AAB unsigned inspection passed: no JAR signature metadata.")
    native_abis = {
        name.split("/", 3)[2]
        for name in names
        if name.startswith("base/lib/") and name.count("/") >= 3
    }
    require(native_abis == {"arm64-v8a"}, f"unexpected native ABI set: {sorted(native_abis)}")

    print(
        "AAB inspection passed: package, version, SDK bounds, permissions, privacy flags, "
        "back navigation, autoVerify-declared app link, and arm64 payload are valid."
    )


def main() -> None:
    require(len(sys.argv) == 3 or (len(sys.argv) == 4 and sys.argv[3] == "--require-unsigned"),
            "usage: inspect_aab.py AAB MANIFEST_XML [--require-unsigned]")
    # /proc/self/fd paths are intentionally not resolved: the protected
    # attester supplies immutable inherited descriptors rather than names that
    # a same-UID filesystem attacker can swap.
    aab_path = Path(sys.argv[1])
    manifest_path = Path(sys.argv[2])
    require(aab_path.is_file(), f"bundle not found: {aab_path}")
    require(manifest_path.is_file(), f"manifest not found: {manifest_path}")
    inspect(aab_path, manifest_path, require_unsigned=len(sys.argv) == 4)


if __name__ == "__main__":
    main()
