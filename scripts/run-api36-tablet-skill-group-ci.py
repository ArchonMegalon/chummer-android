#!/usr/bin/env python3
"""Opt-in hosted adapter for the supplemental native tablet x64 journey.

Consumes the existing build job's exact APK; never builds, downloads tools, or
starts an emulator. --preflight-only does not invoke any Android/Java tooling.
This receipt is neither Preview.12 seven-phone authority nor physical/release proof.
"""

from __future__ import annotations

import argparse
from dataclasses import dataclass
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import platform
import re
import stat
import subprocess
import sys
from typing import Mapping

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tests"))
import run_api36_tablet_skill_group_e2e as tablet
import api36_proof_environment_authority as authority

SCHEMA = "chummer.android.hosted-tablet-skill-group-x64/v1"
IMAGE = "system-images;android-36;google_apis;x86_64"
APK_NAME = "chummer-android-x64-debug.apk"
CORE_CONTENT = "1d8cf694d0412b3bd9f4a241fb95244fad341160"
require = tablet.require


def positive(value: str, label: str) -> int:
    require(re.fullmatch(r"[1-9][0-9]{0,19}", value) is not None, f"Invalid {label}")
    return int(value)


def canonical_directory(value: str, label: str) -> Path:
    path = Path(value)
    require(path.is_absolute() and path.resolve(strict=True) == path and path.is_dir(),
            f"{label} must be an existing canonical directory")
    return path


def path_identity(path: Path) -> list[dict]:
    """Observe only this named path chain, including any toolcache aliases."""
    result = []
    for component in (*reversed(path.parents), path):
        metadata = component.lstat()
        item = {"path": str(component), "device": metadata.st_dev, "inode": metadata.st_ino,
                "mode": metadata.st_mode, "ownerUid": metadata.st_uid, "ownerGid": metadata.st_gid}
        if stat.S_ISLNK(metadata.st_mode):
            item.update(linkTarget=os.readlink(component), changedNs=metadata.st_ctime_ns)
        result.append(item)
    return result


@dataclass
class JavaHomeBinding:
    environment: dict[str, str]
    observation: dict
    launcher: authority.StableFile
    release: authority.StableFile

    def recheck(self) -> None:
        current = bind_java_home(self.environment)
        require(current.observation == self.observation, "Governed Java path identity changed")
        self.launcher.recheck()
        self.release.recheck()


def bind_java_home(environment: Mapping[str, str]) -> JavaHomeBinding:
    """Resolve only setup-java's declared Temurin17 x64 toolcache entry.

    Hosted toolcache entries may alias preinstalled JDK directories. Preserve the
    advertised chain and canonical target identities; do not relax the ordinary
    canonical-directory verifier or discover Java through PATH.
    """
    selected = {key: environment.get(key, "") for key in ("JAVA_HOME", "JAVA_HOME_17_X64", "RUNNER_TOOL_CACHE")}
    advertised = Path(selected["JAVA_HOME"])
    cache = Path(selected["RUNNER_TOOL_CACHE"])
    require(advertised.is_absolute() and cache.is_absolute()
            and str(advertised) == selected["JAVA_HOME"] and str(cache) == selected["RUNNER_TOOL_CACHE"]
            and ".." not in advertised.parts and ".." not in cache.parts
            and selected["JAVA_HOME_17_X64"] == str(advertised),
            "Require setup-java's explicit Java17 x64 home and toolcache")
    relative = advertised.relative_to(cache)
    require(len(relative.parts) == 3 and relative.parts[0] == "Java_Temurin-Hotspot_jdk"
            and re.fullmatch(r"17\.[0-9][0-9A-Za-z.+_-]{0,63}", relative.parts[1]) is not None
            and relative.parts[2] == "x64", "Java home is not the approved Temurin17 x64 toolcache entry")
    before = path_identity(advertised)
    canonical_cache = canonical_directory(str(cache.resolve(strict=True)), "Governed Java toolcache target")
    canonical = canonical_directory(str(advertised.resolve(strict=True)), "Governed Java home target")
    target_identity = path_identity(canonical)
    launcher = authority.StableFile(canonical / "bin/java", "Governed Java launcher")
    release = authority.StableFile(canonical / "release", "Governed Java release metadata")
    require(launcher.size > 0 and os.access(launcher.path, os.X_OK) and release.size > 0,
            "Governed Java launcher or release metadata is unusable")
    require(path_identity(advertised) == before and path_identity(canonical) == target_identity
            and advertised.resolve(strict=True) == canonical,
            "Governed Java alias changed during capture")
    return JavaHomeBinding(selected, {
        "advertisedJavaHome": str(advertised), "canonicalJavaHome": str(canonical),
        "advertisedToolcache": str(cache), "canonicalToolcache": str(canonical_cache),
        "advertisedPathIdentity": before, "canonicalPathIdentity": target_identity,
        "launcherSha256": launcher.sha256, "releaseMetadataSha256": release.sha256,
    }, launcher, release)


def source_binding(root: Path, environment: Mapping[str, str]) -> dict:
    commit = authority._run(["git", "-C", str(root), "rev-parse", "HEAD"]).strip()
    tree = authority._run(["git", "-C", str(root), "rev-parse", "HEAD^{tree}"]).strip()
    require(re.fullmatch(r"[0-9a-f]{40}", commit) is not None
            and commit == environment.get("GITHUB_SHA")
            and re.fullmatch(r"[0-9a-f]{40}", tree) is not None,
            "Tablet checkout differs from the exact workflow source")
    # The generic environment command helper intentionally rejects empty output.
    # Clean git status is the opposite: empty stdout/stderr is required here.
    status = subprocess.run(["git", "-C", str(root), "status", "--porcelain", "--untracked-files=normal"],
                            check=True, capture_output=True, text=True, timeout=30)
    require(not status.stdout and not status.stderr,
            "Tablet proof requires a clean source tree")
    return {"sourceCommit": commit, "sourceTree": tree}


@dataclass
class Preflight:
    temporary: Path
    artifact: dict
    source: dict
    files: list[authority.StableFile]

    @property
    def apk(self) -> authority.StableFile:
        return self.files[0]

    def recheck(self) -> None:
        for snapshot in self.files:
            snapshot.recheck()


def preflight(environment: Mapping[str, str], *, root: Path = ROOT) -> Preflight:
    # Fail before resolving any SDK path or constructing Device, even on manual dispatch.
    require(environment.get("GITHUB_ACTIONS") == "true"
            and environment.get("GITHUB_EVENT_NAME") == "workflow_dispatch"
            and environment.get("CHUMMER_TABLET_SKILL_GROUP_OPT_IN") == "true"
            and environment.get("CHUMMER_E2E_PROFILE") == "tablet",
            "Hosted tablet execution requires explicit manual tablet opt-in")
    run_id = positive(environment.get("GITHUB_RUN_ID", ""), "workflow run ID")
    attempt = positive(environment.get("GITHUB_RUN_ATTEMPT", ""), "workflow attempt")
    producer = positive(environment.get("CHUMMER_E2E_APK_ARTIFACT_ATTEMPT", ""), "APK producer attempt")
    artifact_id = positive(environment.get("CHUMMER_E2E_APK_ARTIFACT_ID", ""), "APK artifact ID")
    require(producer <= attempt, "APK producer attempt is newer than this execution")
    artifact_digest = environment.get("CHUMMER_E2E_APK_ARTIFACT_DIGEST", "")
    require(re.fullmatch(r"(?:sha256:)?[0-9a-f]{64}", artifact_digest) is not None,
            "Invalid build-output APK artifact digest")
    apk_sha = tablet.digest(environment.get("CHUMMER_E2E_APK_SHA256"))
    name = f"chummer-android-api36-x64-debug-{run_id}-{producer}"
    require(environment.get("CHUMMER_E2E_APK_ARTIFACT_NAME") == name,
            "APK artifact name does not bind its producer attempt")
    temporary = canonical_directory(environment.get("RUNNER_TEMP", ""), "Runner temporary root")
    directory = temporary / "chummer-android-apk"
    apk = authority.StableFile(directory / APK_NAME, "shared APK")
    checksum = authority.StableFile(directory / (APK_NAME + ".sha256"), "APK checksum sidecar")
    require(apk.size > 0 and apk.sha256 == apk_sha
            and checksum.data == f"{apk_sha}  {APK_NAME}\n".encode(),
            "Downloaded APK bytes or exact checksum sidecar differ from build output")
    content = authority.StableFile(directory / "chummer-android-content-bundle-receipt.json", "APK content receipt")
    value = content.json()
    require(value.get("schema") == "chummer.android.content-bundle/v1"
            and value.get("status") == "pass" and value.get("apkVerified") is True
            and value.get("apkSha256") == apk_sha and value.get("coreRevision") == CORE_CONTENT
            and value.get("issues") == [], "Canonical content receipt does not bind this APK")
    source = source_binding(root, environment)
    files = [apk, checksum, content]
    for relative in (".github/workflows/api36-editing-e2e.yml", "scripts/build-debug.sh",
                     "eng/api36-sr5-wizard-gate-authority.json"):
        snapshot = authority.StableFile(root / relative, relative)
        files.append(snapshot)
        source[relative + "Sha256"] = snapshot.sha256
    return Preflight(temporary, {
        "workflowRunId": run_id, "executionAttempt": attempt, "producerAttempt": producer,
        "artifactId": artifact_id, "artifactName": name, "artifactDigest": artifact_digest.removeprefix("sha256:"),
        "artifactDigestAuthority": "needs.build upload-artifact output; download selected by immutable artifact ID",
        "archiveDigestIndependentlyRecomputed": False,
        "apkSha256": apk_sha, "apkSizeBytes": apk.size, "contentReceiptSha256": content.sha256,
        "proofBuildId": f"hosted-{run_id}-{producer}",
    }, source, files)


def avd_configuration(snapshot: authority.StableFile, sdk: Path) -> dict:
    entries: dict[str, list[str]] = {}
    for raw in snapshot.data.decode("utf-8").splitlines():
        line = raw.strip()
        if not line or line.startswith("#"):
            continue
        require("=" in line, "Malformed AVD configuration")
        key, value = (part.strip() for part in line.split("=", 1))
        require(key, "Empty AVD configuration key")
        entries.setdefault(key, []).append(value)
    # The pinned action appends hardware tuning entries. Preserve those entries
    # without guessing which the emulator consumed; identity keys must be singular.
    values = {}
    for key in ("image.sysdir.1", "hw.cpu.arch", "tag.id"):
        require(len(entries.get(key, [])) == 1, "Missing or duplicate AVD image identity key")
        values[key] = entries[key][0]
    image = Path(values["image.sysdir.1"])
    if not image.is_absolute():
        image = sdk / image
    require(image.resolve(strict=True) == sdk / "system-images/android-36/google_apis/x86_64"
            and values.get("hw.cpu.arch") == "x86_64" and values.get("tag.id") == "google_apis",
            "Running AVD does not bind the exact API36 google_apis x64 image")
    return {"configSha256": snapshot.sha256, "systemImageRelativePath": str(image.relative_to(sdk)),
            "cpuArch": values["hw.cpu.arch"], "tagId": values["tag.id"],
            "observedHardwareEntries": {key: entries[key] for key in (
                "hw.device.name", "hw.lcd.width", "hw.lcd.height", "hw.lcd.density",
                "hw.ramSize", "hw.cpu.ncore", "hw.keyboard") if key in entries}}


def capture_environment(device, checked: Preflight, environment: Mapping[str, str], sdk: Path,
                        evidence: Path, *, kvm_path: Path = Path("/dev/kvm")) -> tuple[dict, list[authority.StableFile | JavaHomeBinding]]:
    require(environment.get("RUNNER_OS") == "Linux" and environment.get("RUNNER_ARCH") == "X64"
            and environment.get("ImageOS") == "ubuntu24" and environment.get("ImageVersion"),
            "Requires the declared hosted Ubuntu24 x64 image")
    require(stat.S_ISCHR(kvm_path.stat().st_mode) and os.access(kvm_path, os.R_OK | os.W_OK),
            "Hosted tablet emulator requires usable KVM")
    manager = authority.sdk_executable(sdk, "cmdline-tools/latest/bin/sdkmanager", "sdkmanager", required=True)
    inventory_text = authority._run([str(manager), "--list_installed"], timeout=120)
    inventory = authority.parse_sdkmanager_inventory(inventory_text)
    versions = {item["package"]: item["version"] for item in inventory}
    required = ("emulator", "platform-tools", "platforms;android-36", IMAGE)
    require(all(name in versions for name in required), "Required installed SDK package observation is incomplete")
    (evidence / "sdkmanager-installed.txt").write_text(inventory_text)
    snapshots = [authority.StableFile(sdk / relative / "package.xml", relative + " package metadata")
                 for relative in ("emulator", "platform-tools", "platforms/android-36",
                                  "system-images/android-36/google_apis/x86_64")]
    avd_home = canonical_directory(environment.get("ANDROID_AVD_HOME", ""), "Hosted AVD home")
    # The pinned action exports this exact home-owned path during SDK setup;
    # a job-level ANDROID_AVD_HOME override would be silently replaced.
    runner_home = canonical_directory(environment.get("HOME", ""), "Hosted runner home")
    require(avd_home == runner_home / ".android/avd", "Unexpected action-owned AVD home")
    require(device.run("emu", "avd", "name").stdout.splitlines() == ["test", "OK"],
            "Live emulator does not own the configured tablet AVD")
    avd = authority.StableFile(avd_home / "test.avd/config.ini", "live AVD configuration")
    snapshots.append(avd)
    avd_observation = avd_configuration(avd, sdk)
    prefix, identity = authority.capture_stable_growing_log_prefix(checked.temporary / "chummer-api36-emulator-live.log")
    live = authority.parse_emulator_version_prefix(prefix)
    require(authority.emulator_versions_match(versions["emulator"], live["version"]),
            "Live emulator version differs from installed SDK package")
    (evidence / "emulator-startup-prefix.log").write_bytes(prefix)
    java_binding = bind_java_home(environment)
    java_binding.recheck()
    java = authority._run([str(java_binding.launcher.path), "-version"])
    require(re.search(r'(?:openjdk|java) version "17\.', java) is not None, "Requires governed Java17")
    java_binding.recheck()
    (evidence / "java-version.txt").write_text(java)
    (evidence / "java-release.txt").write_bytes(java_binding.release.data)
    (evidence / "avd-config.ini").write_bytes(avd.data)
    observed = tablet.tablet_environment(device)  # Actual sw>=600dp; profile name is not proof.
    observed["buildFingerprint"] = device.shell("getprop", "ro.build.fingerprint")
    require(observed["buildFingerprint"], "Missing live Android build fingerprint")
    return {"schema": "chummer.android.hosted-tablet-environment/v1", "runnerOs": "Linux", "runnerArch": "X64",
            "imageOS": environment["ImageOS"], "imageVersion": environment["ImageVersion"],
            "hostKernel": platform.release(), "kvmUsable": True, "javaVersionOutput": java,
            "javaHomeBinding": java_binding.observation,
            "installedPackages": inventory, "sdkPackageMetadata": {
                str(item.path.relative_to(sdk)): item.sha256 for item in snapshots[:-1]},
            "liveEmulator": {**live, "startupLogIdentity": identity,
                "startupPrefixSha256": hashlib.sha256(prefix).hexdigest()},
            "avd": avd_observation, "android": observed}, [*snapshots, java_binding]


def execute(environment: Mapping[str, str]) -> dict:
    checked = preflight(environment)
    evidence = checked.temporary / "chummer-api36-tablet-skill-group-evidence"
    evidence.mkdir(mode=0o700)  # Fresh job output only; never overwrite another run.
    receipt = {"schema": SCHEMA, "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
               "status": "fail", "executionStatus": "not-run", "profile": "tablet",
               "physicalDeviceProof": False, "releaseEvidenceEligible": False,
               "preview12PhoneAuthority": False, "managedOwnerGenerationRaceProof": False,
               "CoreDispatchReplayAttested": False, "apkArtifact": checked.artifact, "source": checked.source}
    try:
        sdk = authority._canonical_sdk_root(Path(environment.get("ANDROID_HOME", "")))
        if environment.get("ANDROID_SDK_ROOT"):
            require(authority._canonical_sdk_root(Path(environment["ANDROID_SDK_ROOT"])) == sdk,
                    "SDK root differs from the emulator action's ANDROID_HOME")
        adb = authority.sdk_executable(sdk, "platform-tools/adb", "adb", required=True)
        device = tablet.TabletDevice(adb, "emulator-5554", evidence / "provisioning")
        device.require_transport_stability()
        observed, snapshots = capture_environment(device, checked, environment, sdk, evidence)
        receipt["environment"] = observed
        checked.recheck()
        for snapshot in snapshots:
            snapshot.recheck()
        receipt["executionStatus"] = "attempted"
        device.install_verified(checked.apk.path, checked.apk.sha256, "--no-streaming", "-r")
        result_path = evidence / "tablet-journey-receipt.json"
        result = tablet.execute(argparse.Namespace(
            adb=adb, serial="emulator-5554", evidence=evidence / "journey", receipt=result_path,
            gate_contract=ROOT / "eng/api36-sr5-wizard-gate-authority.json",
            proof_build_id=checked.artifact["proofBuildId"], allow_mutate_disposable_runner=True))
        require(result.get("status") == "device-pass" and result.get("executionStatus") == "executed",
                "Native tablet runner did not execute successfully")
        require(result.get("environment") == {key: value for key, value in observed["android"].items()
                if key != "buildFingerprint"}, "Tablet configuration changed between environment and journey")
        checked.recheck()
        for snapshot in snapshots:
            snapshot.recheck()
        require(source_binding(ROOT, environment) == {key: checked.source[key] for key in ("sourceCommit", "sourceTree")},
                "Source graph changed during the tablet journey")
        result_file = authority.StableFile(result_path, "native tablet journey receipt")
        require(result_file.json() == result, "Native journey output differs from its written receipt")
        receipt.update(status="device-pass", executionStatus="executed",
                       journeyReceipt={"path": result_path.name, "sha256": result_file.sha256})
    except Exception as error:
        receipt["error"] = {"type": type(error).__name__, "message": str(error)}
        raise
    finally:
        path = evidence / "hosted-tablet-receipt.json"
        tablet.common.prepare_receipt_target(path)
        tablet.common.write_receipt_atomically(path, receipt)
    return receipt


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--preflight-only", action="store_true")
    args = parser.parse_args(argv)
    try:
        if args.preflight_only:
            checked = preflight(os.environ)
            checked.recheck()
            print(json.dumps({"status": "preflight-pass", "executionStatus": "not-run", "apkArtifact": checked.artifact}))
        else:
            print(json.dumps(execute(os.environ), indent=2))
        return 0
    except Exception as error:
        print(f"Hosted tablet proof failed: {type(error).__name__}: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
