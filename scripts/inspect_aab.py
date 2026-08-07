#!/usr/bin/env python3
"""Fail-closed structural inspection for a bundletool-extracted Android manifest."""

from __future__ import annotations

import sys
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


def inspect(aab_path: Path, manifest_path: Path) -> None:
    root = ET.parse(manifest_path).getroot()
    require(root.get("package") == PACKAGE_ID, "unexpected package id")
    require(attr(root, "compileSdkVersion") == "36", "compile SDK must be 36")
    require(attr(root, "versionCode") == "1", "preview version code must be 1")
    require(attr(root, "versionName") == "0.1.0-preview.1", "unexpected preview version name")

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

    verified_link = False
    for intent_filter in launcher.findall("intent-filter"):
        if attr(intent_filter, "autoVerify") != "true":
            continue
        data = intent_filter.findall("data")
        values = {(attr(item, "scheme"), attr(item, "host"), attr(item, "pathPrefix")) for item in data}
        schemes = {item[0] for item in values}
        hosts = {item[1] for item in values}
        prefixes = {item[2] for item in values}
        if "https" in schemes and "chummer.run" in hosts and "/app" in prefixes:
            verified_link = True
    require(verified_link, "verified https://chummer.run/app intent filter is missing")

    with zipfile.ZipFile(aab_path) as bundle:
        names = bundle.namelist()
    native_abis = {
        name.split("/", 3)[2]
        for name in names
        if name.startswith("base/lib/") and name.count("/") >= 3
    }
    require(native_abis == {"arm64-v8a"}, f"unexpected native ABI set: {sorted(native_abis)}")

    print(
        "AAB inspection passed: package, version, SDK bounds, permissions, privacy flags, "
        "back navigation, app link, and arm64 payload are valid."
    )


def main() -> None:
    require(len(sys.argv) == 3, "usage: inspect_aab.py AAB MANIFEST_XML")
    aab_path = Path(sys.argv[1]).resolve()
    manifest_path = Path(sys.argv[2]).resolve()
    require(aab_path.is_file(), f"bundle not found: {aab_path}")
    require(manifest_path.is_file(), f"manifest not found: {manifest_path}")
    inspect(aab_path, manifest_path)


if __name__ == "__main__":
    main()
