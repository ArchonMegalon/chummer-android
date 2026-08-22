#!/usr/bin/env python3
"""Read-only preflight for the pinned native Android compile toolchain."""

from __future__ import annotations

import argparse
import json
import os
import re
import shutil
import subprocess
from pathlib import Path
from typing import Any, Mapping


SCHEMA = "chummer.android.native-toolchain-preflight/v1"
READY = 0
INVALID_CONFIGURATION = 64
TOOLCHAIN_MISSING = 78


def _sdk_tuple(value: str) -> tuple[int, int, int] | None:
    match = re.fullmatch(r"(\d+)\.(\d+)\.(\d+)", value.strip())
    return tuple(int(part) for part in match.groups()) if match else None


def _feature_band(value: tuple[int, int, int]) -> tuple[int, int, int]:
    return value[0], value[1], value[2] // 100


def _resolve_executable(value: str) -> Path | None:
    candidate = Path(value)
    if candidate.is_absolute():
        return candidate.resolve() if candidate.is_file() and os.access(candidate, os.X_OK) else None
    discovered = shutil.which(value)
    return Path(discovered).resolve() if discovered else None


def _configured_path(
    explicit: Path | None,
    environment: Mapping[str, str],
    names: tuple[str, ...],
) -> Path | None:
    if explicit is not None:
        return explicit.resolve()
    for name in names:
        value = environment.get(name)
        if value:
            return Path(value).resolve()
    return None


def _android_workload_roots(dotnet: Path, environment: Mapping[str, str]) -> list[Path]:
    roots: list[Path] = []
    configured = environment.get("DOTNET_ROOT")
    if configured:
        roots.append(Path(configured).resolve())
    roots.append(dotnet.parent.resolve())
    home = environment.get("HOME")
    if home:
        roots.append((Path(home).resolve() / ".dotnet").resolve())
    return list(dict.fromkeys(roots))


def inspect_toolchain(
    repo_root: Path,
    dotnet_value: str,
    android_sdk: Path | None,
    java_sdk: Path | None,
    environment: Mapping[str, str] | None = None,
) -> dict[str, Any]:
    if environment is None:
        environment = os.environ
    repo_root = repo_root.resolve()
    issues: list[dict[str, str]] = []
    global_json_path = repo_root / "global.json"
    project_path = repo_root / "src/Chummer.Android/Chummer.Android.csproj"
    if not global_json_path.is_file() or not project_path.is_file():
        return {
            "schema": SCHEMA,
            "status": "invalid_configuration",
            "issues": [{"code": "repo_contract_missing", "detail": str(repo_root)}],
        }

    try:
        global_json = json.loads(global_json_path.read_text(encoding="utf-8"))
    except (OSError, ValueError, json.JSONDecodeError) as error:
        return {
            "schema": SCHEMA,
            "status": "invalid_configuration",
            "exitCode": INVALID_CONFIGURATION,
            "issues": [{"code": "pinned_dotnet_policy_invalid", "detail": str(error)}],
        }
    sdk_policy = global_json.get("sdk", {})
    expected_version = str(sdk_policy.get("version", ""))
    expected = _sdk_tuple(expected_version)
    dotnet = _resolve_executable(dotnet_value)
    actual_version = ""
    if expected is None:
        issues.append({"code": "pinned_dotnet_sdk_invalid", "detail": expected_version})
    if sdk_policy.get("rollForward") != "latestPatch" or sdk_policy.get("allowPrerelease") is not False:
        issues.append(
            {
                "code": "pinned_dotnet_policy_invalid",
                "detail": "rollForward must be latestPatch and allowPrerelease must be false",
            }
        )
    if dotnet is None:
        issues.append({"code": "dotnet_sdk_missing", "detail": dotnet_value})
    else:
        try:
            completed = subprocess.run(
                [str(dotnet), "--version"],
                check=False,
                capture_output=True,
                text=True,
                timeout=10,
                env=dict(environment),
            )
            actual_version = completed.stdout.strip()
            if completed.returncode != 0:
                issues.append(
                    {
                        "code": "dotnet_sdk_unusable",
                        "detail": f"dotnet --version exited {completed.returncode}",
                    }
                )
        except (OSError, subprocess.TimeoutExpired) as error:
            issues.append({"code": "dotnet_sdk_unusable", "detail": str(error)})
        actual = _sdk_tuple(actual_version)
        if actual is None:
            issues.append({"code": "dotnet_sdk_version_invalid", "detail": actual_version})
        elif expected is not None and (
            _feature_band(actual) != _feature_band(expected) or actual < expected
        ):
            issues.append(
                {
                    "code": "dotnet_sdk_pin_mismatch",
                    "detail": f"expected {expected_version} latestPatch; found {actual_version}",
                }
            )

    project_text = project_path.read_text(encoding="utf-8")
    target_match = re.search(r"<TargetSdkVersion>(\d+)</TargetSdkVersion>", project_text)
    framework_match = re.search(r"<TargetFramework>net\d+\.\d+-android(\d+)\.0</TargetFramework>", project_text)
    if target_match is None or framework_match is None or target_match.group(1) != framework_match.group(1):
        issues.append({"code": "android_api_contract_invalid", "detail": str(project_path)})
        api_level = ""
    else:
        api_level = target_match.group(1)

    android_sdk = _configured_path(
        android_sdk,
        environment,
        ("AndroidSdkDirectory", "ANDROID_SDK_ROOT", "ANDROID_HOME"),
    )
    if android_sdk is None:
        issues.append({"code": "android_sdk_unconfigured", "detail": "AndroidSdkDirectory"})
    elif not android_sdk.is_dir():
        issues.append({"code": "android_sdk_missing", "detail": str(android_sdk)})
    elif api_level:
        android_jar = android_sdk / "platforms" / f"android-{api_level}" / "android.jar"
        build_tools = android_sdk / "build-tools"
        aapt2 = sorted(build_tools.glob("*/aapt2")) if build_tools.is_dir() else []
        if not android_jar.is_file():
            issues.append({"code": "android_platform_missing", "detail": str(android_jar)})
        if not any(path.is_file() and os.access(path, os.X_OK) for path in aapt2):
            issues.append({"code": "android_build_tools_missing", "detail": str(build_tools)})

    java_sdk = _configured_path(java_sdk, environment, ("JavaSdkDirectory", "JAVA_HOME"))
    if java_sdk is None:
        issues.append({"code": "java_sdk_unconfigured", "detail": "JavaSdkDirectory"})
    elif not java_sdk.is_dir():
        issues.append({"code": "java_sdk_missing", "detail": str(java_sdk)})
    else:
        for executable in ("java", "javac"):
            path = java_sdk / "bin" / executable
            if not path.is_file() or not os.access(path, os.X_OK):
                issues.append({"code": f"java_{executable}_missing", "detail": str(path)})

    workload_packs: list[Path] = []
    if dotnet is not None:
        for root in _android_workload_roots(dotnet, environment):
            packs = root / "packs" / "Microsoft.Android.Sdk.Linux"
            if packs.is_dir():
                workload_packs.extend(path for path in packs.iterdir() if path.is_dir())
        if not workload_packs:
            issues.append({"code": "android_workload_pack_missing", "detail": "Microsoft.Android.Sdk.Linux"})

    invalid_codes = {
        "repo_contract_missing",
        "pinned_dotnet_sdk_invalid",
        "pinned_dotnet_policy_invalid",
        "android_api_contract_invalid",
    }
    status = "ready"
    exit_code = READY
    if issues:
        status = "invalid_configuration" if any(item["code"] in invalid_codes for item in issues) else "toolchain_missing"
        exit_code = INVALID_CONFIGURATION if status == "invalid_configuration" else TOOLCHAIN_MISSING
    return {
        "schema": SCHEMA,
        "status": status,
        "exitCode": exit_code,
        "repoRoot": str(repo_root),
        "dotnet": str(dotnet) if dotnet else None,
        "expectedDotnetSdk": expected_version,
        "actualDotnetSdk": actual_version or None,
        "androidApiLevel": int(api_level) if api_level else None,
        "androidSdkDirectory": str(android_sdk) if android_sdk else None,
        "javaSdkDirectory": str(java_sdk) if java_sdk else None,
        "androidWorkloadPacks": sorted(str(path) for path in workload_packs),
        "issues": issues,
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo-root", type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument("--dotnet", default=os.environ.get("CHUMMER_DOTNET", "dotnet"))
    parser.add_argument("--android-sdk", type=Path)
    parser.add_argument("--java-sdk", type=Path)
    args = parser.parse_args()
    payload = inspect_toolchain(
        args.repo_root,
        args.dotnet,
        args.android_sdk,
        args.java_sdk,
    )
    print(json.dumps(payload, indent=2, sort_keys=True))
    return int(payload.get("exitCode", INVALID_CONFIGURATION))


if __name__ == "__main__":
    raise SystemExit(main())
