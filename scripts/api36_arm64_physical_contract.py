#!/usr/bin/env python3
"""Strict contracts for the six-journey API-36 ARM64 physical proof plane."""

from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime, timezone
import hashlib
import io
import json
import os
from pathlib import Path
import re
import stat
import subprocess
import tempfile
from typing import Callable, Mapping, Sequence
import zipfile
import xml.etree.ElementTree as ET


DEVICE_SCHEMA = "chummer.android.api36-arm64-physical-device/v1"
SEAL_SCHEMA = "chummer.android.api36-arm64-physical-journey-seal/v1"
AGGREGATE_SCHEMA = "chummer.android.api36-arm64-physical-six-journey/v1"
BUILD_PROVENANCE_SCHEMA = "chummer.android.api36-arm64-physical-build-provenance/v3"
SOURCE_GRAPH_SCHEMA = "chummer.android.release-source-graph/v2"
PACKAGE = "com.myexternalbrain.chummer"
TARGET_FRAMEWORK = "net10.0-android36.0"
RUNTIME_IDENTIFIER = "android-arm64"
ABI = "arm64-v8a"
ADB_TIMEOUT_HIERARCHY_MAX_BYTES = 1_000_000
JOURNEY_ORDER = (
    "priority", "career", "before-run", "after-run", "downtime", "playtime",
)
JOURNEY_CONTRACTS = {
    "priority": (
        "chummer.android.sr5-priority-create-physical-e2e/v1",
        "sr5-priority-create-physical",
    ),
    "career": (
        "chummer.android.sr5-career-active-skill-physical-e2e/v1",
        "sr5-career-active-skill-wizard-physical",
    ),
    "before-run": (
        "chummer.android.sr5-before-run-edge-physical-e2e/v1",
        "sr5-before-run-edge-physical",
    ),
    "after-run": (
        "chummer.android.sr5-after-run-settlement-physical-e2e/v1",
        "sr5-after-run-settlement-physical",
    ),
    "downtime": (
        "chummer.android.sr5-downtime-calendar-physical-e2e/v1",
        "sr5-downtime-calendar-physical",
    ),
    "playtime": (
        "chummer.android.sr5-playtime-weapon-physical-e2e/v1",
        "sr5-playtime-weapon-physical",
    ),
}
DOES_NOT_ASSERT = (
    "google_play_upload", "google_play_processing", "tester_distribution",
    "production_rollout", "tablet_journey", "publication_authority",
    "public_release_readiness",
)
SHA256 = re.compile(r"^[0-9a-f]{64}$")
PID = re.compile(r"^[1-9][0-9]*$")
SERIAL = re.compile(r"^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$")
COMPONENT = re.compile(r"^com\.myexternalbrain\.chummer/[A-Za-z0-9._$]+$")
ADB_FILE_HIERARCHY_REMOTE_PATH = "/sdcard/chummer-editing-window.xml"
ADB_FILE_HIERARCHY_REMOVE_ARGUMENTS = (
    "shell", "rm", "-f", ADB_FILE_HIERARCHY_REMOTE_PATH,
)
ADB_FILE_HIERARCHY_DUMP_ARGUMENTS = (
    "shell", "uiautomator", "dump", "--compressed",
    ADB_FILE_HIERARCHY_REMOTE_PATH,
)
ADB_FILE_HIERARCHY_DUMP_REDACTED_ARGUMENTS = (
    "shell", "uiautomator", "<3 redacted argument(s)>",
)
ADB_FILE_HIERARCHY_DUMP_ARGUMENTS_SHA256 = hashlib.sha256(
    "\0".join(ADB_FILE_HIERARCHY_DUMP_ARGUMENTS).encode("utf-8")
).hexdigest()
ADB_FILE_HIERARCHY_OBSERVATION_ARGUMENTS = (
    "exec-out", "cat", ADB_FILE_HIERARCHY_REMOTE_PATH,
)
ADB_FILE_HIERARCHY_STAT_ARGUMENTS = (
    "shell", "stat", "-c", "%d:%i:%s:%Y:%f", ADB_FILE_HIERARCHY_REMOTE_PATH,
)
ADB_FILE_HIERARCHY_OBSERVATION_READ_ATTEMPT_MAX_SECONDS = 1.0
ADB_FILE_HIERARCHY_OBSERVATION_MAX_SECONDS = 10.0
ADB_READ_ONLY_HIERARCHY_ARGUMENTS = (
    "exec-out", "uiautomator", "dump", "--compressed", "/dev/tty",
)
ADB_DIRECT_HIERARCHY_OBSERVATION_READ_ATTEMPT_MAX_SECONDS = 10.0
ADB_DIRECT_HIERARCHY_OBSERVATION_MAX_SECONDS = 48.0
ADB_HIERARCHY_OBSERVATION_MATCHING_AUTHORITY = (
    "exact-observation-bytes"
)
ADB_SWIPE_REDACTED_ARGUMENTS = (
    "shell", "input", "swipe", "<5 redacted argument(s)>",
)
ADB_CREATION_BOOTSTRAP_LOGCAT_ARGUMENTS = (
    "logcat", "-d", "-t", "50", "-s", "ChummerBootstrap:I", "*:S",
)
ADB_CREATION_DASHBOARD_READY_LOGCAT_ARGUMENTS = (
    "logcat", "-d", "-b", "main", "-v", "raw",
    "-s", "ChummerRoute:I", "*:S",
)
ADB_SAFE_READ_ONLY_REMOTE_PATH = re.compile(r"^/[A-Za-z0-9._/:-]{1,511}$")
ADB_SAFE_ANDROID_PROPERTY = re.compile(r"^[A-Za-z0-9._-]{1,255}$")
VIRTUAL_MARKERS = (
    "aosp_cf_", "cuttlefish", "emulator", "generic", "goldfish", "qemu",
    "ranchu", "sdk_gphone", "vbox", "virtualbox",
)
SOURCE_GRAPH_DOES_NOT_ASSERT = (
    "google_play_upload", "google_play_processing", "tester_installation",
    "production_rollout", "presentation_package_authority",
)
WP1_DOES_NOT_ASSERT = (
    "apk_install", "api36_device_execution", "physical_journey_pass",
    "google_play_upload", "google_play_processing", "tester_installation",
    "public_release_readiness", "publication_authority", "tablet_readiness",
)
WP1_COMMITTED_ADAPTER = "trusted-host-physical-build-provenance-v3"
WP1_SOURCE_HEAD_FIELDS = {
    "commit", "tree", "repository", "publicationAuthorized",
}
WP1_PRESENTATION_SOURCE_FIELDS = {
    "commit", "tree", "authorityClass", "productionSource",
    "publicationAuthorized", "packagePlaneLock", "producerLock", "remoteRef",
}
WP1_PACKAGE_AUTHORITY_FIELDS = {
    "sha256", "sizeBytes", "contractName", "authorityState", "sourceGraph",
    "uiReceipt", "cacheManifest", "intakeBinding", "postBuildBinding",
}
WP1_PACKAGE_SOURCE_GRAPH_FIELDS = {
    "corePackageRecipeCommit", "coreRuntimeSourceCommit", "hubProducerCommit",
    "registryCommit", "uiKitCommit",
}
WP1_CONTENT_FIELDS = {
    "sourceReceipt", "apkReceipt", "coreRevision", "bundleDigest",
    "manifestSha256", "canonicalFileCount", "canonicalByteCount",
    "sourceRepository",
}
WP1_ARTIFACT_FIELDS = {
    "basename", "sha256", "sizeBytes", "package", "abis", "apiLevel",
    "configuration", "runtimeIdentifier", "targetFramework", "fullMauiArtifact",
    "installed", "signing",
}
WP1_EXECUTION_EVIDENCE_FIELDS = {
    "toolchainLog", "packageAuthorityLog", "packageAuthorityBinding",
    "contentSourceLog", "buildInputsLog",
    "restoreLog", "buildLog", "signingPhaseLog", "apksignerLog",
    "jarsignerLog", "signingReceipt", "contentApkLog", "packageAuthoritySealLog",
    "packageAuthoritySeal",
    "commandJournal", "rawCommandJournal", "delegateCommandJournal",
    "boundedProcessGroups", "warnings", "errors",
}
WP1_TOOLCHAIN_FIELDS = {
    "dotnetSdkVersion", "dotnetRuntimeVersion", "workloadSetVersion", "dotnetHost",
    "dotnetWorkloads", "workloadManifests", "java", "javac", "jarsigner",
    "keytool", "jdkRelease", "androidSdk", "androidBuildToolsVersion",
    "androidPlatformLabel", "targetFramework", "targetSdkVersion",
    "runtimeIdentifier", "configuration", "serializedBuild",
}
TRUSTED_ANDROID_SDK_ROOT = "/home/tibor/.cache/chummer-android-toolchain/android-sdk"
TRUSTED_JDK_RELEASE_FIELDS = {
    "IMPLEMENTOR", "IMPLEMENTOR_VERSION", "JAVA_RUNTIME_VERSION", "JAVA_VERSION",
    "JAVA_VERSION_DATE", "LIBC", "MODULES", "OS_ARCH", "OS_NAME", "SOURCE",
}
TRUSTED_TOOLCHAIN_SHA256 = {
    "dotnet": "1c13be7f10008294dfd25f0fc0cd7c88e26d3dbaf8e16019af6c5bb53dd0259d",
    "jdk_release": "6bd25f1446259442ae9cfdd1d9d7b6094aa7e3cf05bcbddb842e2f2b5facac4c",
    "java": "2878f3c82270ae7f2bc0c94dbde65718a5a97387ed3ad4b1ce9047948f8b401e",
    "javac": "899fa6dab44db00429d59959cb2ca53169ad4393841dbbae14a0debcdb9fe2a8",
    "jarsigner": "07e52b7729ed7355c280f6766970b8d5dc9942e741ed5af0330cfc09699eb548",
    "keytool": "7bb11637313a640810ec568ffb7e12d90e423c8c81356fc0416d7547047fa144",
    "platform_package": "2110f8ec9c213a77e287e4e92d89e28dd770e4377c24350758cbddebb75de9f3",
    "android_jar": "d9eb9da824d9e247a352f570f01e1169e725b2954bca9e283a71786c59b59f9a",
    "build_tools_package": "a1d29ea87385aa2b8997c7f65968e0c52e8efb4f73ed4cf1df54df808acde6b8",
    "apksigner": "b47549e373b895ce6ca620d0c7887e674d9615ffa837a86ac601dcfd04adb0f0",
    "apksigner_jar": "3716d9311e55d2b0918a2fd9d54ba9e406c5f6abeea700b287f11259bc163dec",
    "aapt2": "1a6a396b9cd071f7040071fdd108718cb98c3c9f4960044f373b288993d19eb7",
    "zipalign": "c5f559e946de5a9e7d58792181db20383b228877812136bc469d97ae00a43b0a",
    "platform_tools_package": "b7253bc2352e6bd5fdc2aa5da4f452ee4c3b6bdc93f20a87d39ee680a91af97c",
    "adb": "372d800c04c3272729afade8a85d95a70fb1c7e74062d9ab17a92eb7b618096c",
    "android_workload_manifest": "e520a5f491b933774ed06c48e8adf3a6878ad8a6cd320180a3395080cf362644",
    "maui_workload_manifest": "e2506ea1897fca4cf528fa2e950d3267477e28e5253f1e7781520058742ced10",
}
TRUSTED_TOOLCHAIN_SIZE_BYTES = {
    "dotnet": 73016,
    "jdk_release": 1279,
    "java": 12368,
    "javac": 12416,
    "jarsigner": 12392,
    "keytool": 12392,
    "platform_package": 1655,
    "android_jar": 27768026,
    "build_tools_package": 17886,
    "apksigner": 2959,
    "apksigner_jar": 1100545,
    "aapt2": 5735384,
    "zipalign": 227696,
    "platform_tools_package": 17882,
    "adb": 8709272,
    "android_workload_manifest": 3608,
    "maui_workload_manifest": 4098,
}
TRUSTED_JAVA_VERSION_LINES = {
    "java": 'openjdk version "17.0.14" 2025-01-21 LTS',
    "javac": "javac 17.0.14",
}
TRUSTED_JDK_RELEASE_VALUES = {
    "IMPLEMENTOR": "Microsoft",
    "IMPLEMENTOR_VERSION": "Microsoft-10800290",
    "JAVA_RUNTIME_VERSION": "17.0.14+7-LTS",
    "JAVA_VERSION": "17.0.14",
    "JAVA_VERSION_DATE": "2025-01-21",
    "LIBC": "gnu",
    "MODULES": "java.base java.compiler java.datatransfer java.xml java.prefs java.desktop java.instrument java.logging java.management java.security.sasl java.naming java.rmi java.management.rmi java.net.http java.scripting java.security.jgss java.transaction.xa java.sql java.sql.rowset java.xml.crypto java.se java.smartcardio jdk.accessibility jdk.internal.jvmstat jdk.attach jdk.charsets jdk.compiler jdk.crypto.ec jdk.crypto.cryptoki jdk.dynalink jdk.internal.ed jdk.editpad jdk.hotspot.agent jdk.httpserver jdk.incubator.foreign jdk.incubator.vector jdk.internal.le jdk.internal.opt jdk.internal.vm.ci jdk.internal.vm.compiler jdk.internal.vm.compiler.management jdk.jartool jdk.javadoc jdk.jcmd jdk.management jdk.management.agent jdk.jconsole jdk.jdeps jdk.jdwp.agent jdk.jdi jdk.jfr jdk.jlink jdk.jpackage jdk.jshell jdk.jsobject jdk.jstatd jdk.localedata jdk.management.jfr jdk.naming.dns jdk.naming.rmi jdk.net jdk.nio.mapmode jdk.random jdk.sctp jdk.security.auth jdk.security.jgss jdk.unsupported jdk.unsupported.desktop jdk.xml.dom jdk.zipfs",
    "OS_ARCH": "x86_64",
    "OS_NAME": "Linux",
    "SOURCE": ".:git:261f4ed0a496",
}
TRUSTED_PRESENTATION_PRODUCER_LOCK = {
    "sha256": "b127bc2010e7ee33ffda3dd2dbcb7ade9b505200cba9753d71675719644bd161",
    "sizeBytes": 2019,
}
TRUSTED_FULL_PROJECT_LOCK = {
    "sha256": "66bbd296462b8db4838672af7af011a03ace6fa3c5a98bd7b5cc5c65a20464e6",
    "sizeBytes": 70375,
}
TRUSTED_CORE_CONTENT_TREE = "ee7696362ccfc18bddd49d42afa5fbf775be846d"
WP1_REFERENCE_EVIDENCE_FILES = {
    "executionEvidence.toolchainLog": "toolchain.log",
    "executionEvidence.packageAuthorityLog": "package-authority.log",
    "executionEvidence.packageAuthorityBinding": "package-authority-binding.json",
    "executionEvidence.contentSourceLog": "content-source.log",
    "executionEvidence.buildInputsLog": "build-inputs.log",
    "executionEvidence.restoreLog": "restore.log",
    "executionEvidence.buildLog": "build.log",
    "executionEvidence.signingPhaseLog": "signing-phase.log",
    "executionEvidence.apksignerLog": "apksigner.log",
    "executionEvidence.jarsignerLog": "jarsigner.log",
    "executionEvidence.signingReceipt": "signing-receipt.json",
    "executionEvidence.contentApkLog": "content-apk.log",
    "executionEvidence.packageAuthoritySealLog": "package-authority-seal.log",
    "executionEvidence.packageAuthoritySeal": "package-authority-seal.json",
    "executionEvidence.commandJournal": "command-journal.jsonl",
    "executionEvidence.rawCommandJournal": "raw-command-journal.jsonl",
    "executionEvidence.delegateCommandJournal": "delegate-command-journal.jsonl",
    "toolchain.dotnetWorkloads": "dotnet-workloads.json",
    "toolchain.androidSdk.selectedInventory": "selected-packages.xml",
}
PACKAGE_AUTHORITY_RELATIVE_PATH = Path("eng/internal-phone-beta-package-authority.json")
CONTENT_MANIFEST_RELATIVE_PATH = Path(
    "src/Chummer.Android/Content/chummer-content-manifest.json"
)
CONTENT_RECEIPT_FIELDS = {
    "status", "schema", "coreRevision", "bundleDigest", "manifestSha256",
    "apkSha256", "canonicalFileCount", "canonicalByteCount",
    "apkCanonicalFileCount", "apkVerified", "issues",
}
REPOSITORY_NAMES = (
    "chummer-android", "chummer6-ui", "chummer6-core", "chummer6-ui-kit",
    "chummer6-hub", "chummer6-hub-registry", "chummer6-media-factory",
    "chummer6-design",
)
REPOSITORY_ROLES = (
    "app", "runtime", "runtime", "runtime", "contracts_and_validation",
    "contracts", "contracts", "validation",
)
REPOSITORY_URLS = (
    "https://github.com/ArchonMegalon/chummer-android.git",
    "https://github.com/ArchonMegalon/chummer6-ui.git",
    "https://github.com/ArchonMegalon/chummer6-core.git",
    "https://github.com/ArchonMegalon/chummer6-ui-kit.git",
    "https://github.com/ArchonMegalon/chummer6-hub.git",
    "https://github.com/ArchonMegalon/chummer6-hub-registry.git",
    "https://github.com/ArchonMegalon/chummer6-media-factory.git",
    "https://github.com/ArchonMegalon/chummer6-design.git",
)
CORE_PACKAGE_IDS = (
    "Chummer.Application", "Chummer.Engine.Contracts", "Chummer.Infrastructure", "Chummer.Rulesets.Hosting",
    "Chummer.Rulesets.Sr4", "Chummer.Rulesets.Sr5", "Chummer.Rulesets.Sr6",
)
OWNER_PACKAGE_SPECS = (
    ("Chummer.Campaign.Contracts", "chummer6-hub"),
    ("Chummer.Play.Contracts", "chummer6-hub"),
    ("Chummer.Run.Contracts", "chummer6-hub"),
    ("Chummer.Hub.Registry.Contracts", "chummer6-hub-registry"),
    ("Chummer.Ui.Kit", "chummer6-ui-kit"),
)
INTEGRATION_BASE_COMMIT = "90e0c7377c85be135d79ff142c8d4657f545f10f"
INTEGRATION_BASE_TREE = "897c86b4f889a149118f2a21c0ecc5a93fb0dde0"
DRIVER_AUTHORITY_SCHEMA = "chummer.android.api36-arm64-physical-driver-authority/v1"
DRIVER_SPECS = {
    "priority": (
        "tests/run_api36_sr5_priority_legal_path_e2e.py",
        "2d14209d315b4779d95923bea67ce2d9932a1e01",
    ),
    "career": (
        "tests/run_api36_sr5_career_active_skill_wizard_e2e.py",
        "58fa882e91e837bb9ded9a1d60303ec9da43f97c",
    ),
    "before-run": (
        "tests/run_api36_sr5_before_run_edge_physical_e2e.py",
        "e5bb245ccb2f8248a6f944458b909719536681ab",
    ),
    "after-run": (
        "tests/run_api36_sr5_after_run_settlement_e2e.py",
        "84b36544f4cd5db8dc3954fbf809209bd2b91b20",
    ),
    "downtime": (
        "tests/run_api36_sr5_downtime_calendar_e2e.py",
        "be87da9aa497b26ef29936c680dbf317ff924c9e",
    ),
    "playtime": (
        "tests/run_api36_sr5_playtime_weapon_physical_e2e.py",
        "00c04c7f5748d98835d5a73b8c0607a2366b438f",
    ),
}


CAREER_SOURCE_FIELDS = {
    "sharedDriverSha256", "x86LeafDriverSha256", "buildPageSha256",
    "runnerCoordinatorSha256", "careerWizardModelSha256", "careerWizardPageSha256",
    "activeSkillWizardPageSha256", "activeSkillCoordinatorSha256",
    "checkpointStoreSha256", "careerActiveSkillRequestSha256",
    "careerActiveSkillMutationSha256", "presenterPersistenceSha256",
    "careerActiveSkillRulesSha256", "activeSkillSourceResolverSha256",
    "workspaceStoreSha256", "careerFixtureSha256", "driverSha256",
}
RAW_FIELDS = {
    "priority": {
        "schema", "status", "executionStatus", "releaseEvidenceStatus",
        "releaseAttested", "publicationAuthorized", "generatedAtUtc", "journey",
        "buildMethod", "profile", "serial", "apiLevel", "abi", "package", "apk",
        "apkSha256", "buildProvenance", "buildProvenanceFile",
        "buildProvenanceRecheckedAfterRun", "buildProvenanceFileRecheckedAfterRun",
        "disposableDeviceAuthorization", "deviceObservation", "adbTransport",
        "physicalDeviceProof", "installedArtifactBound", "draftStateFabricated",
        "identityContractStatus", "authorityProofStages",
    },
    "career": {
        "schema", "status", "executionStatus", "releaseEvidenceStatus",
        "buildProvenance", "adbTransport", "generatedAtUtc", "serial", "profile",
        "journey", "apiLevel", "abi", "deviceObservation", "package", "apk",
        "apkSha256", "expectedApkSha256", "apkAbis", "androidSourceRevision",
        "expectedAndroidSourceRevision", "presentationSourceRevision",
        "coreSourceRevision", "sourceGraphAuthority", "postRunSourceGraphAuthoritySha256",
        "sourceGraphRecheckedAfterRun", "verifiedRemoteCareerFixtureSha256",
        "remoteTemporaryFiles", "authorityProofStages", "journeys",
    } | CAREER_SOURCE_FIELDS,
    "before-run": {
        "schema", "status", "executionStatus", "releaseEvidenceStatus",
        "generatedAtUtc", "profile", "journey", "apiLevel", "abi",
        "deviceObservation", "buildProvenance", "sourceGraphAuthority",
        "sourceGraphRecheckedAfterRun", "apkSha256", "careerFixtureSha256",
        "verifiedRemoteCareerFixtureSha256", "remoteTemporaryFilesDeleted",
        "adbTransport", "authorityProofStages", "scope", "journeys",
    },
    "playtime": {
        "schema", "status", "executionStatus", "releaseEvidenceStatus",
        "generatedAtUtc", "profile", "journey", "apiLevel", "abi",
        "deviceObservation", "buildProvenance", "sourceGraphAuthority",
        "sourceGraphRecheckedAfterRun", "apkSha256", "careerFixtureSha256",
        "verifiedRemoteCareerFixtureSha256", "remoteTemporaryFilesDeleted",
        "adbTransport", "authorityProofStages", "scope", "journeys",
    },
    "after-run": {
        "schema", "status", "executionStatus", "releaseEvidenceStatus",
        "generatedAtUtc", "profile", "journey", "serial", "apiLevel", "abi",
        "deviceObservation", "buildProvenance", "sourceGraphAuthority",
        "postRunSourceGraphAuthoritySha256", "sourceGraphRecheckedAfterRun",
        "apkSha256", "apkAbis", "governedFixtureSha256", "materializedRunnerSha256",
        "verifiedRemoteRunnerSha256", "remoteTemporaryFiles", "authorityProofStages",
        "journeys",
    },
    "downtime": {
        "schema", "status", "executionStatus", "releaseEvidenceStatus",
        "generatedAtUtc", "profile", "journey", "serial", "apiLevel", "abi",
        "deviceObservation", "buildProvenance", "sourceGraphAuthority",
        "postRunSourceGraphAuthoritySha256", "sourceGraphRecheckedAfterRun",
        "apkSha256", "apkAbis", "governedFixtureSha256", "careerRunnerSha256",
        "verifiedRemoteRunnerSha256", "remoteTemporaryFiles", "authorityProofStages",
        "journeys",
    },
}
RAW_DEVICE_FIELDS = {
    "priority": {
        "classification", "evidenceNature", "serial", "apiLevel", "abi", "abiList",
        "qemu", "bootQemu", "manufacturer", "model", "hardware", "productDevice",
        "productName", "buildFingerprint", "buildId", "securityPatch", "verifiedBootState",
    },
    "shared": {
        "classification", "evidenceNature", "serial", "apiLevel", "abi", "abiList",
        "qemu", "manufacturer", "model", "hardware", "buildFingerprint", "buildId",
        "securityPatch", "verifiedBootState",
    },
}
SOURCE_AUTHORITY_FIELDS = {
    "expectedAndroidSourceRevision", "androidSourceRevision",
    "expectedPresentationSourceRevision", "presentationSourceRevision",
    "expectedCoreSourceRevision", "coreSourceRevision", "expectedApkSha256",
    "apkSha256", "apkAbis", "sourceFileSha256", "authoritySha256",
}
SOURCE_FILE_FIELDS = {
    "career": CAREER_SOURCE_FIELDS,
    "before-run": {
        "sharedPhysicalDriverSha256", "sharedProvenanceHelperSha256",
        "careerWizardPageSha256", "tableWizardPageSha256", "tableWizardModelSha256",
        "tableWizardTransactionSha256", "tableWizardAuthoritySha256",
        "runnerCoordinatorSha256", "workspaceStoreSha256", "fixtureSha256",
        "driverSha256", "careerEdgeRequestSha256", "careerEdgeRulesSha256",
        "presenterMutationSha256", "presenterPersistenceSha256",
    },
    "playtime": {
        "sharedPhysicalDriverSha256", "sharedProvenanceHelperSha256",
        "careerWizardPageSha256", "tableWizardPageSha256", "tableWizardModelSha256",
        "tableWizardTransactionSha256", "tableWizardAuthoritySha256",
        "runnerCoordinatorSha256", "workspaceStoreSha256", "fixtureSha256",
        "driverSha256", "careerWeaponRequestSha256", "careerWeaponRulesSha256",
        "presenterMutationSha256", "presenterPersistenceSha256",
        "weaponFixtureAuthorityHelperSha256",
    },
    "after-run": {
        "driverSha256", "fixtureSha256", "physicalHarnessSha256",
        "sharedDeviceHarnessSha256", "buildProvenanceVerifierSha256",
        "careerWizardPageSha256", "manualProposalPageSha256",
        "manualProposalSourceSha256", "workspaceSnapshotSha256",
        "checkpointStoreSha256", "settlementCoordinatorSha256",
        "settlementModelSha256", "settlementPageSha256", "runnerCoordinatorSha256",
    },
    "downtime": {
        "driverSha256", "fixtureSha256", "runnerFixtureSha256",
        "physicalHarnessSha256", "sharedDeviceHarnessSha256",
        "calendarImportHarnessSha256", "buildProvenanceVerifierSha256",
        "careerWizardPageSha256", "downtimePageSha256", "downtimeModelSha256",
        "downtimeAuthoritySha256", "runnerCoordinatorSha256",
    },
}
SUBJOURNEY_FIELDS = {
    "career": {
        "chooseExactTypedSkill", "reviewDurableCheckpoint",
        "reviewedCheckpointProcessRestartResume", "applyOnceAndFreshTypedReceipt",
        "appliedCheckpointProcessRestartRecovery", "acknowledgeAndDeleteAppliedCheckpoint",
        "acknowledgedDeletionFinalProcessRestart", "savedSuccessorRevisionAndPayloadDigest",
    },
    "before-run": {
        "importExactCareerFixture", "persistDurableReview", "restartAndResumeReview",
        "applyRepresentativeTypedActionOnce", "verifySavedRevisionPlusOne",
        "restartAndRecoverExactReceipt", "acknowledgeReceipt",
        "restartAndReopenSavedSuccessor",
    },
    "playtime": {
        "importExactCareerFixture", "persistDurableReview", "restartAndResumeReview",
        "applyRepresentativeTypedActionOnce", "verifySavedRevisionPlusOne",
        "restartAndRecoverExactReceipt", "acknowledgeReceipt",
        "restartAndReopenSavedSuccessor",
    },
    "after-run": {
        "exactProposalRunCharacterIds", "rewardHeatReputationAndContacts",
        "gmAndOwnerReviewDigests", "durableReviewRestartResume",
        "atomicCoreReceiptAndSuccessor", "receiptRestartRecovery",
        "acknowledgementAndFinalRestart",
    },
    "downtime": {
        "exactCalendarEdit", "durableReview", "reviewRestartAndReconfirm",
        "atomicApplyAndReceipt", "receiptRestartRecovery", "acknowledgeAndReopen",
        "finalRestartSuccessor",
    },
}
WORKSPACE_FIELDS = {
    "workspaceId", "contentRevision", "savedRevision", "payloadSha256", "documentSha256",
}
CAREER_PROOF_FIELDS = {
    "import", "restoredBeforeApply", "restoredAfterApply",
    "finalRestoredAfterAcknowledgement", "reviewedCheckpoint",
    "reviewedCheckpointSha256", "appliedCheckpoint", "appliedCheckpointSha256",
    "receiptProjection", "generatedExpenseGuid", "restartProcessIds",
}
CAREER_CHECKPOINT_FIELDS = {
    "SchemaVersion", "Version", "RouteId", "Kind", "WorkspaceId", "OwnerId",
    "ExpectedContentRevision", "SkillId", "SourceSkillId", "LogicalRevision",
    "SourceRevision", "RuleDigest", "SkillName", "SkillCategory", "BasePoints",
    "PreviousKarmaPoints", "RatingMaximum", "ActionId", "ExpenseDateLocal",
    "ExpenseAmount", "ExpenseReason", "ExpenseType", "ExpenseRefund",
    "ExpenseForceCareerVisible", "KarmaUndoType", "NuyenUndoType", "UndoObjectId",
    "UndoQuantity", "UndoExtra", "PreviousRating", "TargetRating", "SavedKarma",
    "IdempotencyKey", "Phase",
}
LANE_PROOF_FIELDS = {
    "scope", "import", "restoredBeforeApply", "savedSuccessor",
    "finalRestoredSuccessor", "actionAutomationId", "successorActionAutomationIds",
    "successorActionAuthority", "reviewedTransactionSha256",
    "appliedTransactionSha256", "receipt", "restartProcessIds",
}
LANE_JOURNAL_FIELDS = {
    "SchemaVersion", "Version", "Phase", "OwnerId", "TransactionId",
    "IdempotencyKey", "Review", "ExpectedPostconditionDigest", "Receipt", "JournalDigest",
}
LANE_REVIEW_FIELDS = {
    "Schema", "WorkspaceId", "WorkspaceRevision", "SnapshotDigest", "Lane", "SelectedAction",
}
LANE_RECEIPT_FIELDS = {
    "ContractName", "TransactionId", "IdempotencyKey", "WorkspaceId",
    "ExpectedWorkspaceRevision", "AppliedWorkspaceRevision", "ActionId", "ActionKind",
    "ActionDigest", "ExpectedPostconditionDigest", "ObservedPostconditionDigest",
    "ReceiptDigest",
}
AFTER_PROOF_FIELDS = {
    "import", "restoredBeforeApply", "savedSuccessor", "finalRestartSuccessor",
    "reviewedCheckpoint", "reviewedCheckpointSha256", "appliedCheckpoint",
    "appliedCheckpointSha256", "transactionAndReviewAuthority", "restartProcessIds",
}
AFTER_CHECKPOINT_FIELDS = {
    "SchemaVersion", "Version", "RouteId", "Phase", "Draft", "Receipt", "IdempotencyKey",
}
AFTER_DRAFT_FIELDS = {"OwnerId", "Candidate", "Plan", "Acknowledgements"}
AFTER_CANDIDATE_FIELDS = {"RewardContext", "Binding"}
AFTER_REWARD_CONTEXT_FIELDS = {
    "ContractName", "Identity", "RunTitle", "CompletedAt", "KarmaAward",
    "NuyenAward", "RewardReceiptDigest", "ContextDigest",
}
AFTER_BINDING_FIELDS = {
    "ContractName", "WorkspaceId", "WorkspaceRevision", "Identity", "Quote", "BindingDigest",
}
AFTER_IDENTITY_FIELDS = {"ProposalId", "RunId", "CharacterId"}
AFTER_QUOTE_FIELDS = {
    "Identity", "HeatBefore", "HeatDelta", "HeatAfter", "StreetCredBefore",
    "StreetCredDelta", "StreetCredAfter", "NotorietyBefore", "NotorietyDelta",
    "NotorietyAfter", "PublicAwarenessBefore", "RequestedPublicAwarenessDelta",
    "PublicAwarenessAfter", "KarmaBefore", "ContactKarmaCost", "KarmaAfter", "Contacts",
    "GmReviewDigest", "OwnerReviewDigest", "Prerequisites", "CanSettle", "Blocker",
    "SourceDigest", "CustomDataDigest", "GmPolicyDigest", "RuntimeDigest", "LogicalDigest",
}
AFTER_PLAN_FIELDS = {
    "Identity", "TransactionId", "TargetHeat", "TargetStreetCred", "TargetNotoriety",
    "TargetPublicAwareness", "TargetKarma", "ContactKarmaCost", "ContactsToAdd",
    "ExpenseId", "ExpenseAmount", "ExpenseReason", "GmReviewDigest", "OwnerReviewDigest",
    "ExpectedSourceDigest", "ExpectedCustomDataDigest", "ExpectedGmPolicyDigest",
    "ExpectedRuntimeDigest", "ExpectedLogicalDigest", "PlanDigest",
}
AFTER_ACK_FIELDS = {
    "RunContextReviewed", "RewardsReviewed", "ConsequencesReviewed", "ContactsReviewed",
    "GmApprovalReviewed", "OwnerApprovalReviewed",
}
AFTER_RECEIPT_FIELDS = {
    "TransactionId", "Identity", "HeatBefore", "HeatAfter", "StreetCredBefore",
    "StreetCredAfter", "NotorietyBefore", "NotorietyAfter", "PublicAwarenessBefore",
    "PublicAwarenessAfter", "KarmaBefore", "KarmaAfter", "ContactKarmaCost",
    "AddedContacts", "ExpenseId", "ExpenseAmount", "ExpenseReason", "GmReviewDigest",
    "OwnerReviewDigest", "SourceDigest", "CustomDataDigest", "GmPolicyDigest",
    "RuntimeDigest", "LogicalDigestBefore", "LogicalDigestAfter", "ReceiptDigest",
}
DOWNTIME_PROOF_FIELDS = {
    "import", "restoredBeforeApply", "savedSuccessor", "finalRestartSuccessor",
    "reviewedJournal", "reviewedJournalSha256", "appliedJournal", "appliedJournalSha256",
    "receiptAuthority", "restartProcessIds",
}
DOWNTIME_JOURNAL_FIELDS = {
    "SchemaVersion", "Version", "Phase", "OwnerId", "ActionId", "Review",
    "ExpectedPostconditionDigest", "Receipt", "JournalDigest",
}
DOWNTIME_REVIEW_FIELDS = {"Schema", "WorkspaceId", "WorkspaceRevision", "SnapshotDigest", "Preview"}
DOWNTIME_PREVIEW_FIELDS = {
    "Schema", "WeekId", "Year", "Week", "Notes", "NotesColor", "Operation",
    "ExpectedCalendarRevision", "ExpectedSourceRevision", "ExpectedLogicalRevision",
    "Summary", "PreviewDigest",
}
DOWNTIME_RECEIPT_FIELDS = {
    "ContractName", "WorkspaceId", "ExpectedWorkspaceRevision", "AppliedWorkspaceRevision",
    "ActionId", "Operation", "PreviewDigest", "ExpectedPostconditionDigest",
    "ObservedPostconditionDigest", "CalendarRevisionAfter", "SourceDigestAfter",
    "ContentDigestAfter", "ReceiptDigest",
}


@dataclass(frozen=True)
class BoundBytes:
    path: Path
    data: bytes
    sha256: str
    size_bytes: int

    def json(self) -> dict[str, object]:
        return {
            "basename": self.path.name,
            "sha256": self.sha256,
            "sizeBytes": self.size_bytes,
        }


@dataclass(frozen=True)
class BuildProvenanceReferences:
    package_authority: BoundBytes
    package_authority_intake: BoundBytes
    package_authority_post_build: BoundBytes
    content_manifest: BoundBytes
    content_source_receipt: BoundBytes
    content_apk_receipt: BoundBytes
    full_project_lock: BoundBytes
    project_assets: BoundBytes
    evidence: tuple[tuple[str, BoundBytes], ...]


def reject_duplicate_keys(pairs: list[tuple[str, object]]) -> dict[str, object]:
    result: dict[str, object] = {}
    for key, value in pairs:
        if key in result:
            raise ValueError(f"duplicate JSON key: {key}")
        result[key] = value
    return result


def strict_json_bytes(data: bytes, label: str) -> dict[str, object]:
    try:
        value = json.loads(
            data.decode("utf-8"), object_pairs_hook=reject_duplicate_keys,
            parse_constant=lambda token: (_ for _ in ()).throw(
                ValueError(f"{label} contains non-finite number {token}")
            ),
        )
    except (UnicodeError, json.JSONDecodeError) as error:
        raise ValueError(f"{label} is not strict UTF-8 JSON") from error
    if not isinstance(value, dict):
        raise ValueError(f"{label} must be one JSON object")
    return value


def require_exact_keys(value: object, expected: set[str], label: str) -> dict[str, object]:
    if not isinstance(value, dict):
        raise ValueError(f"{label} must be one object")
    if set(value) != expected:
        raise ValueError(
            f"{label} keys are not exact; missing={sorted(expected - set(value))}, "
            f"extra={sorted(set(value) - expected)}"
        )
    return value


def require_string(value: object, label: str, *, nonempty: bool = True) -> str:
    if not isinstance(value, str) or (nonempty and not value):
        raise ValueError(f"{label} must be one string")
    return value


def require_integer(value: object, label: str, *, minimum: int = 0) -> int:
    if not isinstance(value, int) or isinstance(value, bool) or value < minimum:
        raise ValueError(f"{label} must be one non-boolean integer >= {minimum}")
    return value


def require_hex(value: object, label: str, *, length: int = 64) -> str:
    text = require_string(value, label)
    if re.fullmatch(rf"[0-9a-f]{{{length}}}", text) is None:
        raise ValueError(f"{label} must be one canonical lowercase hex digest")
    return text


def require_string_list(value: object, label: str, *, nonempty: bool = False) -> list[str]:
    if not isinstance(value, list) or (nonempty and not value) or any(
        not isinstance(row, str) or not row for row in value
    ):
        raise ValueError(f"{label} must be a list of strings")
    return value


def require_field_types(
    value: Mapping[str, object], types: Mapping[str, type | tuple[type, ...]], label: str,
) -> None:
    for field, expected in types.items():
        actual = value.get(field)
        if type(actual) not in ((expected,) if isinstance(expected, type) else expected):
            raise ValueError(f"{label}.{field} has the wrong JSON type")


def canonical_sha256(value: object) -> str:
    encoded = json.dumps(
        value, allow_nan=False, ensure_ascii=False, separators=(",", ":"),
        sort_keys=True,
    ).encode("utf-8")
    return hashlib.sha256(encoded).hexdigest()


def require_utc_timestamp(value: object, label: str, *, canonical_z: bool = False) -> str:
    if not isinstance(value, str) or (canonical_z and not value.endswith("Z")):
        raise ValueError(f"{label} must be one UTC timestamp")
    try:
        parsed = datetime.fromisoformat(value.replace("Z", "+00:00"))
    except ValueError as error:
        raise ValueError(f"{label} must be one UTC timestamp") from error
    if parsed.tzinfo is None or parsed.utcoffset() != timezone.utc.utcoffset(parsed):
        raise ValueError(f"{label} must be one UTC timestamp")
    return value


def _reject_symlink_components(path: Path) -> None:
    current = Path(path.anchor)
    for part in path.parts[1:]:
        current /= part
        try:
            mode = os.lstat(current).st_mode
        except FileNotFoundError:
            continue
        if stat.S_ISLNK(mode):
            raise ValueError(f"path contains a symlink component: {current}")


def bind_regular(path: Path, label: str) -> BoundBytes:
    if not path.is_absolute() or path.resolve(strict=True) != path:
        raise ValueError(f"{label} path must be absolute and canonical")
    _reject_symlink_components(path)
    flags = os.O_RDONLY | getattr(os, "O_CLOEXEC", 0) | getattr(os, "O_NOFOLLOW", 0)
    descriptor = os.open(path, flags)
    try:
        before = os.fstat(descriptor)
        if not stat.S_ISREG(before.st_mode):
            raise ValueError(f"{label} must be a regular file")
        chunks: list[bytes] = []
        digest = hashlib.sha256()
        size = 0
        while True:
            chunk = os.read(descriptor, 1024 * 1024)
            if not chunk:
                break
            chunks.append(chunk)
            digest.update(chunk)
            size += len(chunk)
        after = os.fstat(descriptor)
    finally:
        os.close(descriptor)
    identity = (before.st_dev, before.st_ino, before.st_size, before.st_mtime_ns)
    if identity != (after.st_dev, after.st_ino, after.st_size, after.st_mtime_ns) or size != after.st_size:
        raise ValueError(f"{label} changed during immutable byte capture")
    return BoundBytes(path, b"".join(chunks), digest.hexdigest(), size)


def require_unchanged(bound: BoundBytes, label: str) -> None:
    if bind_regular(bound.path, label) != bound:
        raise ValueError(f"{label} bytes changed across the authentication boundary")


def capture_build_provenance_references(
    provenance: BoundBytes, repository_root: Path,
) -> BuildProvenanceReferences:
    if (
        not repository_root.is_absolute()
        or repository_root.resolve(strict=True) != repository_root
    ):
        raise ValueError("build-provenance repository root must be canonical")
    evidence_root = Path(f"{provenance.path}.evidence")
    if (
        not evidence_root.is_absolute()
        or evidence_root.resolve(strict=True) != evidence_root
        or not evidence_root.is_dir()
        or evidence_root.stat().st_mode & 0o077
    ):
        raise ValueError("WP1 evidence root must be a canonical owner-only directory")
    _reject_symlink_components(evidence_root)
    evidence = tuple(
        (
            field,
            bind_regular(evidence_root / filename, f"WP1 referenced evidence {field}"),
        )
        for field, filename in sorted(WP1_REFERENCE_EVIDENCE_FILES.items())
    )
    return BuildProvenanceReferences(
        package_authority=bind_regular(
            repository_root / PACKAGE_AUTHORITY_RELATIVE_PATH,
            "committed package authority",
        ),
        package_authority_intake=bind_regular(
            evidence_root / "package-authority-binding.json",
            "package authority intake",
        ),
        package_authority_post_build=bind_regular(
            evidence_root / "package-authority-seal.json",
            "package authority post-build seal",
        ),
        content_manifest=bind_regular(
            repository_root / CONTENT_MANIFEST_RELATIVE_PATH,
            "committed Android content manifest",
        ),
        content_source_receipt=bind_regular(
            evidence_root / "content-source-receipt.json",
            "Core content source receipt",
        ),
        content_apk_receipt=bind_regular(
            evidence_root / "content-apk-receipt.json",
            "APK content receipt",
        ),
        full_project_lock=bind_regular(
            repository_root / "src/Chummer.Android/packages.lock.json",
            "committed ARM64 full-project lock",
        ),
        project_assets=bind_regular(
            evidence_root / "project-assets.json",
            "ARM64 restore assets",
        ),
        evidence=evidence,
    )


def _evidence_reference_map(
    references: BuildProvenanceReferences,
) -> dict[str, BoundBytes]:
    result: dict[str, BoundBytes] = {}
    for field, bound in references.evidence:
        if field in result:
            raise ValueError(f"duplicate WP1 referenced evidence field: {field}")
        result[field] = bound
    if set(result) != set(WP1_REFERENCE_EVIDENCE_FILES):
        raise ValueError("WP1 referenced evidence field set is not exact")
    return result


def _require_evidence_binding(
    binding: object, references: Mapping[str, BoundBytes], field: str,
) -> None:
    try:
        bound = references[field]
    except KeyError as error:
        raise ValueError(f"missing WP1 referenced evidence: {field}") from error
    if not isinstance(binding, dict):
        raise ValueError(f"WP1 referenced evidence {field} must be one object")
    projected = {
        "sha256": binding.get("sha256"),
        "sizeBytes": binding.get("sizeBytes"),
    }
    _require_bound_binding(projected, bound, f"WP1 referenced evidence {field}")


def _all_build_provenance_references(
    references: BuildProvenanceReferences,
) -> tuple[tuple[BoundBytes, str], ...]:
    direct = (
        (references.package_authority, "committed package authority"),
        (references.package_authority_intake, "package authority intake"),
        (references.package_authority_post_build, "package authority post-build seal"),
        (references.content_manifest, "committed Android content manifest"),
        (references.content_source_receipt, "Core content source receipt"),
        (references.content_apk_receipt, "APK content receipt"),
        (references.full_project_lock, "committed ARM64 full-project lock"),
        (references.project_assets, "ARM64 restore assets"),
    )
    evidence = tuple(
        (bound, f"WP1 referenced evidence {field}")
        for field, bound in references.evidence
    )
    return direct + evidence


def _validate_referenced_provenance_bytes(
    value: Mapping[str, object], apk: BoundBytes,
    references: BuildProvenanceReferences,
    evidence: Mapping[str, BoundBytes],
) -> None:
    presentation = require_exact_keys(
        value.get("presentationBuildSource"), WP1_PRESENTATION_SOURCE_FIELDS,
        "WP1 Presentation build source",
    )
    package_authority = require_exact_keys(
        value.get("packageAuthority"), WP1_PACKAGE_AUTHORITY_FIELDS,
        "WP1 package authority",
    )
    restore = require_exact_keys(value.get("restore"), {
        "lockedMode", "networkSourcesAllowed", "ownerSourceFallbackAllowed",
        "fullProjectLock", "projectAssets",
    }, "WP1 restore")
    execution = require_exact_keys(
        value.get("executionEvidence"), WP1_EXECUTION_EVIDENCE_FIELDS,
        "WP1 execution evidence",
    )
    toolchain = require_exact_keys(
        value.get("toolchain"), WP1_TOOLCHAIN_FIELDS, "WP1 toolchain",
    )
    artifact = require_exact_keys(
        value.get("artifact"), WP1_ARTIFACT_FIELDS, "WP1 artifact",
    )
    signing = require_exact_keys(
        artifact.get("signing"), {"certificateSha256", "verifiedSchemes", "receipt"},
        "WP1 artifact signing",
    )

    for field in WP1_EXECUTION_EVIDENCE_FIELDS - {
        "boundedProcessGroups", "warnings", "errors",
    }:
        _require_evidence_binding(
            execution.get(field), evidence, f"executionEvidence.{field}",
        )
    _require_bound_binding(
        restore.get("fullProjectLock"), references.full_project_lock,
        "WP1 restore full-project lock",
    )
    if restore.get("fullProjectLock") != TRUSTED_FULL_PROJECT_LOCK:
        raise ValueError("WP1 restore full-project lock is not exact")
    _require_bound_binding(
        restore.get("projectAssets"), references.project_assets,
        "WP1 restore project assets",
    )
    _require_evidence_binding(
        toolchain.get("dotnetWorkloads"), evidence, "toolchain.dotnetWorkloads",
    )
    android_sdk = require_exact_keys(toolchain.get("androidSdk"), {
        "root", "selectedInventory", "installedPackages", "androidJar", "aapt2",
        "zipalign", "adb", "apksigner", "apksignerJar",
    }, "WP1 Android SDK")
    _require_evidence_binding(
        android_sdk.get("selectedInventory"), evidence,
        "toolchain.androidSdk.selectedInventory",
    )
    if signing.get("receipt") != execution.get("signingReceipt"):
        raise ValueError("WP1 signing receipt bindings are not identical")
    signing_receipt_bound = evidence["executionEvidence.signingReceipt"]
    _require_bound_binding(
        signing.get("receipt"), signing_receipt_bound, "WP1 artifact signing receipt",
    )
    receipt = strict_json_bytes(signing_receipt_bound.data, "APK signing receipt")
    require_exact_keys(receipt, {
        "contractName", "status", "apkSha256", "certificateSha256",
        "verifiedSchemes", "apksignerSha256", "jarsignerSha256",
        "apksignerOutputSha256", "jarsignerOutputSha256", "warningsAsErrors",
        "publicationAuthorized",
    }, "APK signing receipt")
    if (
        receipt.get("contractName") != "chummer.android.apk-signing-verification/v1"
        or receipt.get("status") != "pass"
        or receipt.get("publicationAuthorized") is not False
        or receipt.get("warningsAsErrors") is not True
        or receipt.get("apkSha256") != apk.sha256
        or receipt.get("apksignerSha256") != TRUSTED_TOOLCHAIN_SHA256["apksigner"]
        or receipt.get("jarsignerSha256") != TRUSTED_TOOLCHAIN_SHA256["jarsigner"]
        or receipt.get("apksignerOutputSha256")
        != evidence["executionEvidence.apksignerLog"].sha256
        or receipt.get("jarsignerOutputSha256")
        != evidence["executionEvidence.jarsignerLog"].sha256
        or receipt.get("certificateSha256") != signing.get("certificateSha256")
        or receipt.get("verifiedSchemes") != signing.get("verifiedSchemes")
    ):
        raise ValueError("WP1 signing facts differ from exact signing receipt bytes")
    if execution.get("packageAuthorityBinding") != package_authority.get("intakeBinding"):
        raise ValueError("WP1 package-authority intake evidence bindings differ")
    if execution.get("packageAuthoritySeal") != package_authority.get("postBuildBinding"):
        raise ValueError("WP1 package-authority seal evidence bindings differ")


def _require_bound_binding(
    binding: object, bound: BoundBytes, label: str,
) -> None:
    value = _validate_wp1_binding(binding, label)
    if (value.get("sha256"), value.get("sizeBytes")) != (
        bound.sha256, bound.size_bytes,
    ):
        raise ValueError(f"{label} does not bind the exact referenced bytes")


def _validate_package_authority_references(
    package_authority: Mapping[str, object],
    presentation_source: Mapping[str, object],
    references: BuildProvenanceReferences,
    repository_root: Path,
    android_source: Mapping[str, object],
) -> None:
    if (
        references.package_authority.path
        != repository_root / PACKAGE_AUTHORITY_RELATIVE_PATH
        or references.content_manifest.path
        != repository_root / CONTENT_MANIFEST_RELATIVE_PATH
    ):
        raise ValueError("WP1 committed authority paths are outside the bound Android checkout")
    if (
        run_git(repository_root, ("rev-parse", "HEAD")).strip()
        != android_source.get("commit")
        or run_git(repository_root, ("rev-parse", "HEAD^{tree}")).strip()
        != android_source.get("tree")
        or run_git(
            repository_root,
            ("status", "--porcelain=v1", "--untracked-files=all"),
        )
    ):
        raise ValueError("WP1 Android checkout is not the exact clean source-graph authority")
    if (package_authority.get("sha256"), package_authority.get("sizeBytes")) != (
        references.package_authority.sha256, references.package_authority.size_bytes,
    ):
        raise ValueError("WP1 package authority does not bind the exact committed bytes")
    _require_bound_binding(
        package_authority.get("intakeBinding"), references.package_authority_intake,
        "WP1 package authority intake",
    )
    _require_bound_binding(
        package_authority.get("postBuildBinding"), references.package_authority_post_build,
        "WP1 package authority post-build seal",
    )
    if not (
        references.package_authority.data
        == references.package_authority_intake.data
        == references.package_authority_post_build.data
    ):
        raise ValueError("WP1 package authority bytes changed during the physical build")
    authority = strict_json_bytes(
        references.package_authority.data, "committed package authority",
    )
    if (
        authority.get("contractName") != package_authority.get("contractName")
        or authority.get("authorityState") != package_authority.get("authorityState")
        or authority.get("publicationAuthorized") is not False
        or authority.get("sourceGraph") != package_authority.get("sourceGraph")
    ):
        raise ValueError("WP1 package authority payload differs from exact referenced bytes")
    verification_receipt = require_exact_keys(
        authority.get("verificationReceipt"), {
            "contractName", "contractVersion", "sha256", "sizeBytes", "status",
        }, "committed package authority verification receipt",
    )
    artifact_cache = require_exact_keys(
        authority.get("artifactCache"), {
            "contractName", "cacheKey", "manifestFileName", "manifestSha256",
            "manifestSizeBytes", "packageCount",
        }, "committed package authority cache",
    )
    committed_package_lock = require_exact_keys(
        authority.get("packagePlaneLock"), {
            "path", "contractName", "contractVersion", "sha256", "sizeBytes", "gitBlob",
        }, "committed package authority package-plane lock",
    )
    expected_ui_receipt = {
        "sha256": verification_receipt.get("sha256"),
        "sizeBytes": verification_receipt.get("sizeBytes"),
    }
    expected_cache_manifest = {
        "sha256": artifact_cache.get("manifestSha256"),
        "sizeBytes": artifact_cache.get("manifestSizeBytes"),
    }
    expected_package_lock = {
        "sha256": committed_package_lock.get("sha256"),
        "sizeBytes": committed_package_lock.get("sizeBytes"),
    }
    if package_authority.get("uiReceipt") != expected_ui_receipt:
        raise ValueError("WP1 UI authority receipt differs from committed package authority")
    if package_authority.get("cacheManifest") != expected_cache_manifest:
        raise ValueError("WP1 package cache differs from committed package authority")
    if presentation_source.get("packagePlaneLock") != expected_package_lock:
        raise ValueError("WP1 Presentation package-plane lock differs from committed authority")
    if presentation_source.get("producerLock") != TRUSTED_PRESENTATION_PRODUCER_LOCK:
        raise ValueError("WP1 Presentation producer lock is not exact")
    authority_presentation = require_exact_keys(
        authority.get("presentationSource"), {"commit", "tree", "repository"},
        "committed package authority presentation source",
    )
    if (
        authority_presentation.get("commit") != presentation_source.get("commit")
        or authority_presentation.get("tree") != presentation_source.get("tree")
        or authority_presentation.get("repository") != REPOSITORY_URLS[1]
    ):
        raise ValueError("WP1 Presentation source differs from exact package authority bytes")


def _validate_content_references(
    content: Mapping[str, object], apk: BoundBytes,
    references: BuildProvenanceReferences,
) -> None:
    _require_bound_binding(
        content.get("sourceReceipt"), references.content_source_receipt,
        "WP1 content source receipt",
    )
    _require_bound_binding(
        content.get("apkReceipt"), references.content_apk_receipt,
        "WP1 content APK receipt",
    )
    manifest = strict_json_bytes(references.content_manifest.data, "Android content manifest")
    require_exact_keys(manifest, {"schema", "coreRevision", "bundleDigest", "files"}, "Android content manifest")
    files = manifest.get("files")
    if not isinstance(files, list) or not files:
        raise ValueError("Android content manifest files are not exact")
    manifest_count = 0
    manifest_bytes = 0
    for index, row in enumerate(files):
        entry = require_exact_keys(row, {"path", "size", "sha256"}, f"Android content manifest file {index}")
        require_string(entry.get("path"), f"Android content manifest file {index} path")
        manifest_bytes += require_integer(entry.get("size"), f"Android content manifest file {index} size")
        require_hex(entry.get("sha256"), f"Android content manifest file {index} sha256")
        manifest_count += 1
    if (
        manifest.get("schema") != "chummer.android.content-bundle/v1"
        or content.get("coreRevision") != manifest.get("coreRevision")
        or content.get("bundleDigest") != manifest.get("bundleDigest")
        or content.get("manifestSha256") != references.content_manifest.sha256
        or content.get("canonicalFileCount") != manifest_count
        or content.get("canonicalByteCount") != manifest_bytes
    ):
        raise ValueError("WP1 content payload differs from exact committed content manifest bytes")
    source = strict_json_bytes(references.content_source_receipt.data, "Core content source receipt")
    apk_receipt = strict_json_bytes(references.content_apk_receipt.data, "APK content receipt")
    for label, receipt, apk_verified in (
        ("Core content source receipt", source, False),
        ("APK content receipt", apk_receipt, True),
    ):
        require_exact_keys(receipt, CONTENT_RECEIPT_FIELDS, label)
        if (
            receipt.get("status") != "pass"
            or receipt.get("schema") != "chummer.android.content-bundle/v1"
            or receipt.get("coreRevision") != content.get("coreRevision")
            or receipt.get("bundleDigest") != content.get("bundleDigest")
            or receipt.get("manifestSha256") != content.get("manifestSha256")
            or receipt.get("canonicalFileCount") != content.get("canonicalFileCount")
            or receipt.get("canonicalByteCount") != content.get("canonicalByteCount")
            or receipt.get("apkVerified") is not apk_verified
            or receipt.get("issues") != []
        ):
            raise ValueError(f"{label} differs from authenticated WP1 content")
    if (
        source.get("apkSha256") is not None or source.get("apkCanonicalFileCount") != 0
        or apk_receipt.get("apkSha256") != apk.sha256
        or apk_receipt.get("apkCanonicalFileCount") != content.get("canonicalFileCount")
    ):
        raise ValueError("WP1 content receipts do not bind the exact APK/source posture")


def validate_external_output(path: Path, repository_root: Path) -> None:
    if not path.is_absolute() or any(part in {".", ".."} for part in path.parts):
        raise ValueError("output path must be absolute and normalized")
    if path.exists() or path.is_symlink():
        raise ValueError("output path must be absent")
    if path.parent.resolve(strict=True) != path.parent or path.parent.is_symlink():
        raise ValueError("output parent must be a canonical real directory")
    _reject_symlink_components(path.parent)
    try:
        path.relative_to(repository_root.resolve(strict=True))
    except ValueError:
        return
    raise ValueError("proof output must remain outside the source worktree")


def write_json_exclusive(path: Path, payload: Mapping[str, object], repository_root: Path) -> None:
    validate_external_output(path, repository_root)
    encoded = (json.dumps(payload, allow_nan=False, indent=2, sort_keys=True) + "\n").encode("utf-8")
    temporary: str | None = None
    try:
        with tempfile.NamedTemporaryFile("wb", dir=path.parent, prefix=f".{path.name}.", delete=False) as stream:
            temporary = stream.name
            os.fchmod(stream.fileno(), 0o600)
            stream.write(encoded)
            stream.flush()
            os.fsync(stream.fileno())
        os.link(temporary, path, follow_symlinks=False)
        directory = os.open(path.parent, os.O_RDONLY | getattr(os, "O_DIRECTORY", 0))
        try:
            os.fsync(directory)
        finally:
            os.close(directory)
    finally:
        if temporary is not None:
            Path(temporary).unlink(missing_ok=True)


def validate_source_graph(bound: BoundBytes) -> dict[str, object]:
    graph = strict_json_bytes(bound.data, "v2 release source graph")
    require_exact_keys(graph, {
        "contractName", "generatedAtUtc", "authorityState", "publicationAuthorized",
        "generator", "repositories", "packagePins", "ownerPackagePins",
        "dependencyClosure", "presentationSource", "doesNotAssert",
    }, "v2 release source graph")
    if (
        graph.get("contractName") != SOURCE_GRAPH_SCHEMA
        or graph.get("authorityState") != "local_review_required"
        or graph.get("publicationAuthorized") is not False
        or graph.get("doesNotAssert") != list(SOURCE_GRAPH_DOES_NOT_ASSERT)
    ):
        raise ValueError("source graph contract/publication posture is not exact")
    require_utc_timestamp(graph.get("generatedAtUtc"), "source graph generatedAtUtc", canonical_z=True)
    generator = require_exact_keys(
        graph.get("generator"), {"path", "sha256", "size_bytes"}, "source graph generator",
    )
    generator_path = Path(__file__).with_name("verify_release_source_graph.py")
    if generator_path.is_symlink() or not generator_path.is_file():
        raise ValueError("source graph generator is not one local regular file")
    generator_bytes = generator_path.read_bytes()
    if generator != {
        "path": "scripts/verify_release_source_graph.py",
        "sha256": hashlib.sha256(generator_bytes).hexdigest(),
        "size_bytes": len(generator_bytes),
    }:
        raise ValueError("source graph generator bytes are not exact")
    repositories = graph.get("repositories")
    if not isinstance(repositories, list) or len(repositories) != len(REPOSITORY_NAMES):
        raise ValueError("source graph must bind exactly eight repositories")
    repository_map: dict[str, dict[str, object]] = {}
    for expected_name, expected_role, expected_url, row in zip(
        REPOSITORY_NAMES, REPOSITORY_ROLES, REPOSITORY_URLS, repositories, strict=True,
    ):
        row = require_exact_keys(
            row, {"name", "role", "commit", "tree", "tree_sha256", "repository"},
            f"source graph repository {expected_name}",
        )
        if (
            row.get("name") != expected_name or row.get("role") != expected_role
            or row.get("repository") != expected_url
        ):
            raise ValueError("source graph repository order/identity is not exact")
        require_hex(row.get("commit"), f"{expected_name} commit", length=40)
        require_hex(row.get("tree"), f"{expected_name} tree", length=40)
        require_hex(row.get("tree_sha256"), f"{expected_name} tree sha256")
        repository_map[expected_name] = row
    package_pins = graph.get("packagePins")
    owner_pins = graph.get("ownerPackagePins")
    if (
        not isinstance(package_pins, list) or len(package_pins) != len(CORE_PACKAGE_IDS)
        or not isinstance(owner_pins, list) or len(owner_pins) != len(OWNER_PACKAGE_SPECS)
    ):
        raise ValueError("source graph must bind seven Core and five owner package pins")
    core_commit = repository_map["chummer6-core"]["commit"]
    for expected_id, row in zip(CORE_PACKAGE_IDS, package_pins, strict=True):
        row = require_exact_keys(
            row, {"package_id", "version", "sha256", "repository", "commit"},
            f"Core package pin {expected_id}",
        )
        if (
            row.get("package_id") != expected_id or row.get("repository") != "chummer6-core"
            or row.get("commit") != core_commit
        ):
            raise ValueError("source graph Core package pin authority/order is not exact")
        require_string(row.get("version"), f"Core package {expected_id} version")
        require_hex(row.get("sha256"), f"Core package {expected_id} sha256")
    owner_fields = {
        "package_id", "version", "sha256", "size_bytes", "owner_repository",
        "source_commit", "source_tree", "source_authority", "authority_receipt_sha256",
        "package_inventory_sha256", "package_plane_lock_sha256", "dependency_mode",
    }
    for (expected_id, expected_owner), row in zip(OWNER_PACKAGE_SPECS, owner_pins, strict=True):
        row = require_exact_keys(row, owner_fields, f"owner package pin {expected_id}")
        if (
            row.get("package_id") != expected_id
            or row.get("owner_repository") != expected_owner
            or row.get("dependency_mode") != "locked_package"
        ):
            raise ValueError("source graph owner package authority/order is not exact")
        source_commit = require_hex(
            row.get("source_commit"), f"owner package {expected_id} source commit", length=40
        )
        source_tree = require_hex(
            row.get("source_tree"), f"owner package {expected_id} source tree", length=40
        )
        owner_repository = repository_map[expected_owner]
        source_authority = require_exact_keys(
            row.get("source_authority"), {
                "owner_head_commit", "owner_head_tree", "relationship", "verification",
            }, f"owner package {expected_id} source authority",
        )
        if source_authority != {
            "owner_head_commit": owner_repository["commit"],
            "owner_head_tree": owner_repository["tree"],
            "relationship": "ancestor_or_equal",
            "verification": "git-merge-base-is-ancestor-without-replace-objects",
        }:
            raise ValueError(
                f"owner package {expected_id} source is not bound to its pinned repository head"
            )
        if source_commit == owner_repository["commit"] and source_tree != owner_repository["tree"]:
            raise ValueError(
                f"owner package {expected_id} current source tree differs from its repository tree"
            )
        require_string(row.get("version"), f"owner package {expected_id} version")
        require_integer(row.get("size_bytes"), f"owner package {expected_id} size", minimum=1)
        for field in (
            "sha256", "authority_receipt_sha256", "package_inventory_sha256",
            "package_plane_lock_sha256",
        ):
            require_hex(row.get(field), f"owner package {expected_id} {field}")
    closure = graph.get("dependencyClosure")
    if not isinstance(closure, list) or len(closure) != len(OWNER_PACKAGE_SPECS):
        raise ValueError("source graph dependency closure cardinality is not exact")
    for (expected_id, _owner), row in zip(OWNER_PACKAGE_SPECS, closure, strict=True):
        row = require_exact_keys(row, {"package_id", "dependencies"}, f"closure {expected_id}")
        dependencies = require_string_list(row.get("dependencies"), f"closure {expected_id} dependencies")
        if row.get("package_id") != expected_id or dependencies != sorted(set(dependencies)):
            raise ValueError("source graph dependency closure order/uniqueness is not exact")
        if expected_id == "Chummer.Run.Contracts" and "Chummer.Play.Contracts" not in dependencies:
            raise ValueError(
                "source graph Chummer.Run.Contracts closure is missing Chummer.Play.Contracts"
            )
    presentation = require_exact_keys(
        graph.get("presentationSource"), {
            "repository", "commit", "tree", "source_path", "authority_state",
            "publication_authorized", "dependency_mode",
        }, "source graph Presentation binding",
    )
    if (
        presentation.get("repository") != "chummer6-ui"
        or presentation.get("commit") != repository_map["chummer6-ui"]["commit"]
        or presentation.get("tree") != repository_map["chummer6-ui"]["tree"]
        or presentation.get("source_path") != "chummer-presentation"
        or presentation.get("authority_state") != "local_review_required"
        or presentation.get("publication_authorized") is not False
        or presentation.get("dependency_mode") != "source_compatibility"
    ):
        raise ValueError("source graph Presentation binding is not exact/non-publication")
    return graph


GitRunner = Callable[[Path, Sequence[str]], str]


def run_git(repository_root: Path, arguments: Sequence[str]) -> str:
    result = subprocess.run(
        ["git", *arguments], cwd=repository_root, check=True, capture_output=True,
        text=True, timeout=30,
    )
    return result.stdout


def capture_driver_authority(
    *, repository_root: Path, driver_paths: Mapping[str, Path],
    source_graph: Mapping[str, object], git_runner: GitRunner = run_git,
) -> dict[str, object]:
    expected_root = Path(__file__).resolve().parents[1]
    if (
        not repository_root.is_absolute()
        or repository_root.resolve(strict=True) != repository_root
        or repository_root != expected_root
    ):
        raise ValueError("driver repository must be this exact canonical Package 5 checkout")
    if tuple(driver_paths) != JOURNEY_ORDER:
        raise ValueError("driver path cardinality/order is not exact")
    head = git_runner(repository_root, ("rev-parse", "HEAD")).strip()
    tree = git_runner(repository_root, ("rev-parse", "HEAD^{tree}")).strip()
    merge_base = git_runner(
        repository_root, ("merge-base", INTEGRATION_BASE_COMMIT, head),
    ).strip()
    if (
        re.fullmatch(r"[0-9a-f]{40}", head) is None
        or re.fullmatch(r"[0-9a-f]{40}", tree) is None
        or merge_base != INTEGRATION_BASE_COMMIT
    ):
        raise ValueError("driver repository is not a successor of the exact GREEN integration head")
    if git_runner(
        repository_root, ("status", "--porcelain=v1", "--untracked-files=all"),
    ):
        raise ValueError("driver repository must be clean, including untracked files")
    repositories = source_graph.get("repositories")
    if not isinstance(repositories, list):
        raise ValueError("source graph repository rows are unavailable for driver authority")
    android_rows = [row for row in repositories if isinstance(row, dict) and row.get("name") == "chummer-android"]
    if len(android_rows) != 1 or (
        android_rows[0].get("commit"), android_rows[0].get("tree")
    ) != (head, tree):
        raise ValueError("source graph does not bind the clean Package 5 Android commit/tree")

    rows: list[dict[str, object]] = []
    for journey_id, (relative, expected_blob) in DRIVER_SPECS.items():
        supplied = driver_paths[journey_id]
        expected_path = repository_root / relative
        if supplied != expected_path or supplied.resolve(strict=True) != expected_path:
            raise ValueError(f"{journey_id} driver path is not the exact integrated repository path")
        captured = bind_regular(supplied, f"{journey_id} integrated driver")
        line = git_runner(repository_root, ("ls-tree", head, "--", relative)).rstrip("\n")
        expected_line = f"100644 blob {expected_blob}\t{relative}"
        if line != expected_line:
            raise ValueError(f"{journey_id} driver Git blob/mode/path differs from GREEN integration")
        rows.append({
            "journeyId": journey_id, "repositoryRelativePath": relative,
            "gitBlobSha1": expected_blob, "sha256": captured.sha256,
            "sizeBytes": captured.size_bytes,
        })
    authority = {
        "schema": DRIVER_AUTHORITY_SCHEMA,
        "integrationBaseCommit": INTEGRATION_BASE_COMMIT,
        "integrationBaseTree": INTEGRATION_BASE_TREE,
        "repositoryCommit": head, "repositoryTree": tree,
        "publicationAuthorized": False, "drivers": rows,
    }
    return {**authority, "authoritySha256": canonical_sha256(authority)}


def parse_driver_paths(values: Sequence[str]) -> dict[str, Path]:
    result: dict[str, Path] = {}
    for value in values:
        journey, separator, raw_path = value.partition("=")
        if not separator or journey not in JOURNEY_ORDER or not raw_path or journey in result:
            raise ValueError("driver bindings must contain each exact journey once as id=/absolute/path")
        path = Path(raw_path)
        if not path.is_absolute():
            raise ValueError(f"driver path must be absolute: {journey}")
        result[journey] = path
    if tuple(result) != JOURNEY_ORDER:
        raise ValueError(f"driver cardinality/order must be {JOURNEY_ORDER!r}")
    return result


def _validate_wp1_binding(value: object, label: str) -> dict[str, object]:
    binding = require_exact_keys(value, {"sha256", "sizeBytes"}, label)
    require_hex(binding.get("sha256"), f"{label} sha256")
    require_integer(binding.get("sizeBytes"), f"{label} size", minimum=1)
    return binding


def _validate_wp1_successor_surfaces(
    value: Mapping[str, object],
) -> None:
    trusted_tool_names = {
        "dotnet", "jdk_release", "java", "javac", "jarsigner", "keytool",
        "platform_package", "android_jar", "build_tools_package", "apksigner",
        "apksigner_jar", "aapt2", "zipalign", "platform_tools_package", "adb",
        "android_workload_manifest", "maui_workload_manifest",
    }
    if (
        set(TRUSTED_TOOLCHAIN_SHA256) != trusted_tool_names
        or set(TRUSTED_TOOLCHAIN_SIZE_BYTES) != trusted_tool_names
        or any(type(size) is not int or size <= 0 for size in TRUSTED_TOOLCHAIN_SIZE_BYTES.values())
        or any(
        re.fullmatch(r"[0-9a-f]{64}", digest) is None
        for digest in TRUSTED_TOOLCHAIN_SHA256.values()
        )
    ):
        raise ValueError("trusted WP1 toolchain digest authority is not exact")

    def require_authorized_binding(
        binding: object, label: str, authority_name: str,
    ) -> dict[str, object]:
        entry = _validate_wp1_binding(binding, label)
        if (
            entry.get("sha256") != TRUSTED_TOOLCHAIN_SHA256[authority_name]
            or entry.get("sizeBytes") != TRUSTED_TOOLCHAIN_SIZE_BYTES[authority_name]
        ):
            raise ValueError(f"{label} bytes are not trusted-host authorized")
        return entry

    evidence = require_exact_keys(
        value.get("executionEvidence"), WP1_EXECUTION_EVIDENCE_FIELDS,
        f"{WP1_COMMITTED_ADAPTER} execution evidence",
    )
    for field in WP1_EXECUTION_EVIDENCE_FIELDS - {
        "boundedProcessGroups", "warnings", "errors",
    }:
        _validate_wp1_binding(evidence.get(field), f"WP1 execution evidence {field}")
    if (
        evidence.get("boundedProcessGroups") is not True
        or evidence.get("warnings") != 0 or type(evidence.get("warnings")) is not int
        or evidence.get("errors") != 0 or type(evidence.get("errors")) is not int
    ):
        raise ValueError("WP1 execution evidence outcome/bounds are not exact")

    toolchain = require_exact_keys(
        value.get("toolchain"), WP1_TOOLCHAIN_FIELDS,
        f"{WP1_COMMITTED_ADAPTER} toolchain",
    )
    if (
        toolchain.get("dotnetSdkVersion") != "10.0.111"
        or toolchain.get("dotnetRuntimeVersion") != "10.0.11"
        or toolchain.get("workloadSetVersion") != "10.0.110.1"
        or toolchain.get("androidBuildToolsVersion") != "36.0.0"
        or toolchain.get("androidPlatformLabel") != "Android 16"
        or toolchain.get("targetFramework") != TARGET_FRAMEWORK
        or toolchain.get("targetSdkVersion") != 36
        or type(toolchain.get("targetSdkVersion")) is not int
        or toolchain.get("runtimeIdentifier") != RUNTIME_IDENTIFIER
        or toolchain.get("configuration") != "Debug"
        or toolchain.get("serializedBuild") is not True
    ):
        raise ValueError("WP1 toolchain release selection is not exact")
    for field, authority_name in (
        ("dotnetHost", "dotnet"), ("jarsigner", "jarsigner"),
        ("keytool", "keytool"),
    ):
        require_authorized_binding(
            toolchain.get(field), f"WP1 toolchain {field}", authority_name,
        )
    for field, authority_name in (("java", "java"), ("javac", "javac")):
        entry = require_exact_keys(
            toolchain.get(field), {"sha256", "sizeBytes", "version", "versionLine"},
            f"WP1 toolchain {field}",
        )
        if (
            entry.get("sha256") != TRUSTED_TOOLCHAIN_SHA256[authority_name]
            or entry.get("sizeBytes") != TRUSTED_TOOLCHAIN_SIZE_BYTES[authority_name]
        ):
            raise ValueError(f"WP1 toolchain {field} bytes are not trusted-host authorized")
        require_integer(entry.get("sizeBytes"), f"WP1 toolchain {field} size", minimum=1)
        if (
            entry.get("version") != "17.0.14"
            or entry.get("versionLine") != TRUSTED_JAVA_VERSION_LINES[field]
        ):
            raise ValueError(f"WP1 toolchain {field} identity is not exact")
    manifests = require_exact_keys(
        toolchain.get("workloadManifests"), {"android", "maui"},
        "WP1 workload manifests",
    )
    for field, version, authority_name in (
        ("android", "36.1.69", "android_workload_manifest"),
        ("maui", "10.0.20", "maui_workload_manifest"),
    ):
        entry = require_exact_keys(
            manifests.get(field), {"sha256", "sizeBytes", "version"},
            f"WP1 workload manifest {field}",
        )
        if (
            entry.get("sha256") != TRUSTED_TOOLCHAIN_SHA256[authority_name]
            or entry.get("sizeBytes") != TRUSTED_TOOLCHAIN_SIZE_BYTES[authority_name]
        ):
            raise ValueError(f"WP1 workload manifest {field} bytes are not trusted-host authorized")
        require_integer(entry.get("sizeBytes"), f"WP1 workload manifest {field} size", minimum=1)
        if entry.get("version") != version:
            raise ValueError(f"WP1 workload manifest {field} version is not exact")
    workloads = require_exact_keys(
        toolchain.get("dotnetWorkloads"), {
            "sha256", "sizeBytes", "installed", "updateAvailable", "workloadSetVersion",
            "manifestVersions", "runtimeVersion",
        }, "WP1 .NET workloads",
    )
    require_hex(workloads.get("sha256"), "WP1 .NET workloads sha256")
    require_integer(workloads.get("sizeBytes"), "WP1 .NET workloads size", minimum=1)
    if workloads.get("installed") != ["maui-android"] or workloads.get("updateAvailable") != []:
        raise ValueError("WP1 .NET workload inventory is not exact")
    if workloads.get("workloadSetVersion") != "10.0.110.1" or workloads.get("runtimeVersion") != "10.0.11":
        raise ValueError("WP1 .NET workload versions are not exact")
    if workloads.get("manifestVersions") != {
        "maui-android": "10.0.20/10.0.100",
        "microsoft.net.sdk.android": "36.1.69",
    }:
        raise ValueError("WP1 .NET workload manifest versions are not exact")
    jdk_release = require_exact_keys(
        toolchain.get("jdkRelease"), {"sha256", "sizeBytes", "fields"},
        "WP1 JDK release identity",
    )
    if (
        jdk_release.get("sha256") != TRUSTED_TOOLCHAIN_SHA256["jdk_release"]
        or jdk_release.get("sizeBytes") != TRUSTED_TOOLCHAIN_SIZE_BYTES["jdk_release"]
    ):
        raise ValueError("WP1 JDK release bytes are not trusted-host authorized")
    require_integer(jdk_release.get("sizeBytes"), "WP1 JDK release size", minimum=1)
    release_fields = jdk_release.get("fields")
    if (
        not isinstance(release_fields, dict)
        or set(release_fields) != TRUSTED_JDK_RELEASE_FIELDS
        or release_fields != TRUSTED_JDK_RELEASE_VALUES
    ):
        raise ValueError("WP1 JDK release identity is not exact")
    android_sdk = require_exact_keys(toolchain.get("androidSdk"), {
        "root", "selectedInventory", "installedPackages", "androidJar", "aapt2",
        "zipalign", "adb", "apksigner", "apksignerJar",
    }, "WP1 Android SDK")
    android_sdk_root = android_sdk.get("root")
    if android_sdk_root != TRUSTED_ANDROID_SDK_ROOT:
        raise ValueError("WP1 Android SDK root is not exact")
    _validate_wp1_binding(android_sdk.get("selectedInventory"), "WP1 Android SDK selectedInventory")
    for field, authority_name in (
        ("androidJar", "android_jar"), ("aapt2", "aapt2"),
        ("zipalign", "zipalign"), ("adb", "adb"),
        ("apksigner", "apksigner"), ("apksignerJar", "apksigner_jar"),
    ):
        require_authorized_binding(
            android_sdk.get(field), f"WP1 Android SDK {field}", authority_name,
        )
    packages = require_exact_keys(android_sdk.get("installedPackages"), {
        "platforms;android-36", "build-tools;36.0.0", "platform-tools",
    }, "WP1 Android SDK installed packages")
    for package_id, revision, authority_name in (
        ("platforms;android-36", "2.0.0", "platform_package"),
        ("build-tools;36.0.0", "36.0.0", "build_tools_package"),
        ("platform-tools", "36.0.0", "platform_tools_package"),
    ):
        package = require_exact_keys(
            packages.get(package_id), {"sha256", "sizeBytes", "revision"},
            f"WP1 Android SDK package {package_id}",
        )
        if (
            package.get("sha256") != TRUSTED_TOOLCHAIN_SHA256[authority_name]
            or package.get("sizeBytes") != TRUSTED_TOOLCHAIN_SIZE_BYTES[authority_name]
        ):
            raise ValueError(f"WP1 Android SDK package {package_id} bytes are not trusted-host authorized")
        require_integer(package.get("sizeBytes"), f"WP1 Android SDK package {package_id} size", minimum=1)
        if package.get("revision") != revision:
            raise ValueError(f"WP1 Android SDK package {package_id} revision is not exact")


def validate_build_provenance(
    bound: BoundBytes, graph: BoundBytes, apk: BoundBytes,
    *, adapter: str = WP1_COMMITTED_ADAPTER,
    repository_root: Path | None = None,
    references: BuildProvenanceReferences | None = None,
) -> dict[str, object]:
    if adapter != WP1_COMMITTED_ADAPTER:
        raise ValueError(f"unsupported explicit WP1 adapter: {adapter!r}")
    value = strict_json_bytes(bound.data, "WP1 build provenance")
    require_exact_keys(value, {
        "schema", "status", "authorityClass", "publicationAuthorized", "proofScope",
        "dependencyMode", "sourceHead", "presentationBuildSource",
        "packageAuthority", "content", "restore", "executionEvidence", "toolchain",
        "artifact", "doesNotAssert", "authoritySha256", "generatedAtUtc",
    }, "WP1 build provenance")
    if (
        value.get("schema") != BUILD_PROVENANCE_SCHEMA or value.get("status") != "pass"
        or value.get("publicationAuthorized") is not False
        or value.get("proofScope") != "full_maui_arm64_apk_build_only"
        or value.get("authorityClass") != "internal_phone_beta_physical_candidate_only"
        or value.get("dependencyMode") != "locked_current_packages_no_owner_siblings"
    ):
        raise ValueError("WP1 build provenance pass/scope/publication posture is not exact")
    require_utc_timestamp(value.get("generatedAtUtc"), "WP1 generatedAtUtc", canonical_z=True)
    authority = dict(value)
    authority_sha = authority.pop("authoritySha256", None)
    authority.pop("generatedAtUtc", None)
    if not isinstance(authority_sha, str) or SHA256.fullmatch(authority_sha) is None:
        raise ValueError("WP1 authority digest is not canonical")
    if canonical_sha256(authority) != authority_sha:
        raise ValueError("WP1 authority digest does not authenticate its payload")

    graph_payload = validate_source_graph(graph)
    repository_rows = {
        row["name"]: row for row in graph_payload["repositories"]
        if isinstance(row, dict) and isinstance(row.get("name"), str)
    }
    if references is None:
        references = capture_build_provenance_references(
            bound, repository_root or Path(__file__).resolve().parents[1],
        )
    evidence = _evidence_reference_map(references)
    source_head = require_exact_keys(
        value.get("sourceHead"), WP1_SOURCE_HEAD_FIELDS,
        f"{WP1_COMMITTED_ADAPTER} source head",
    )
    android_row = repository_rows["chummer-android"]
    if (
        source_head.get("commit") != android_row["commit"]
        or source_head.get("tree") != android_row["tree"]
        or source_head.get("repository") != REPOSITORY_URLS[0]
        or source_head.get("publicationAuthorized") is not False
    ):
        raise ValueError("WP1 build provenance source head does not bind the supplied source graph")
    require_hex(source_head.get("commit"), "WP1 source head commit", length=40)
    require_hex(source_head.get("tree"), "WP1 source head tree", length=40)

    presentation_source = require_exact_keys(
        value.get("presentationBuildSource"), WP1_PRESENTATION_SOURCE_FIELDS,
        f"{WP1_COMMITTED_ADAPTER} Presentation build source",
    )
    presentation_row = repository_rows["chummer6-ui"]
    if (
        presentation_source.get("commit") != presentation_row["commit"]
        or presentation_source.get("tree") != presentation_row["tree"]
        or presentation_source.get("authorityClass") != "verified_current_ui_source"
        or presentation_source.get("productionSource") is not False
        or presentation_source.get("publicationAuthorized") is not False
        or presentation_source.get("remoteRef") != "refs/remotes/origin/main"
    ):
        raise ValueError("WP1 Presentation build source is not the exact current internal source")
    require_hex(presentation_source.get("commit"), "WP1 Presentation commit", length=40)
    require_hex(presentation_source.get("tree"), "WP1 Presentation tree", length=40)
    _validate_wp1_binding(presentation_source.get("packagePlaneLock"), "WP1 Presentation package-plane lock")
    _validate_wp1_binding(presentation_source.get("producerLock"), "WP1 Presentation producer lock")

    package_authority = require_exact_keys(
        value.get("packageAuthority"), WP1_PACKAGE_AUTHORITY_FIELDS,
        f"{WP1_COMMITTED_ADAPTER} package authority",
    )
    require_hex(package_authority.get("sha256"), "WP1 package authority sha256")
    require_integer(package_authority.get("sizeBytes"), "WP1 package authority size", minimum=1)
    if (
        package_authority.get("contractName")
        != "chummer.android.internal-phone-beta-package-authority/v2"
        or package_authority.get("authorityState") != "current_graph_verified"
    ):
        raise ValueError("WP1 package authority contract/state is not exact")
    package_source_graph = require_exact_keys(
        package_authority.get("sourceGraph"), WP1_PACKAGE_SOURCE_GRAPH_FIELDS,
        "WP1 package authority source graph",
    )
    expected_package_source_graph = {
        "coreRuntimeSourceCommit": repository_rows["chummer6-core"]["commit"],
        "hubProducerCommit": repository_rows["chummer6-hub"]["commit"],
        "registryCommit": repository_rows["chummer6-hub-registry"]["commit"],
        "uiKitCommit": repository_rows["chummer6-ui-kit"]["commit"],
    }
    for field, expected_value in expected_package_source_graph.items():
        if package_source_graph.get(field) != expected_value:
            raise ValueError(f"WP1 package authority source graph is not exact: {field}")
    require_hex(package_source_graph.get("corePackageRecipeCommit"), "WP1 Core package recipe commit", length=40)
    authority_bindings = [
        _validate_wp1_binding(package_authority.get(field), f"WP1 package authority {field}")
        for field in ("uiReceipt", "cacheManifest", "intakeBinding", "postBuildBinding")
    ]
    if authority_bindings[2] != authority_bindings[3] or (
        package_authority.get("sha256"), package_authority.get("sizeBytes")
    ) != (
        authority_bindings[2].get("sha256"), authority_bindings[2].get("sizeBytes")
    ):
        raise ValueError("WP1 package authority intake/post-build bindings are not identical")
    _validate_package_authority_references(
        package_authority, presentation_source, references,
        repository_root or Path(__file__).resolve().parents[1], android_row,
    )

    content = require_exact_keys(
        value.get("content"), WP1_CONTENT_FIELDS, f"{WP1_COMMITTED_ADAPTER} content",
    )
    for field in ("sourceReceipt", "apkReceipt"):
        _validate_wp1_binding(content.get(field), f"WP1 content {field}")
    for field in ("coreRevision",):
        require_hex(content.get(field), f"WP1 content {field}", length=40)
    for field in ("bundleDigest", "manifestSha256"):
        require_hex(content.get(field), f"WP1 content {field}")
    for field in ("canonicalFileCount", "canonicalByteCount"):
        require_integer(content.get(field), f"WP1 content {field}", minimum=1)
    content_source = require_exact_keys(
        content.get("sourceRepository"), {"commit", "tree"},
        "WP1 Core content source repository",
    )
    require_hex(content_source.get("commit"), "WP1 Core content source commit", length=40)
    require_hex(content_source.get("tree"), "WP1 Core content source tree", length=40)
    if (
        content.get("coreRevision") != content_source.get("commit")
        or content_source.get("commit") != package_source_graph.get("corePackageRecipeCommit")
        or content_source.get("tree") != TRUSTED_CORE_CONTENT_TREE
    ):
        raise ValueError("WP1 Core content source is not bound to current package authority")
    _validate_content_references(content, apk, references)

    artifact = require_exact_keys(value.get("artifact"), WP1_ARTIFACT_FIELDS, f"{WP1_COMMITTED_ADAPTER} artifact")
    expected = {
        "basename": apk.path.name, "sha256": apk.sha256, "sizeBytes": apk.size_bytes,
        "package": PACKAGE, "abis": [ABI], "apiLevel": 36, "configuration": "Debug",
        "runtimeIdentifier": RUNTIME_IDENTIFIER, "targetFramework": TARGET_FRAMEWORK,
        "fullMauiArtifact": True, "installed": False,
    }
    if {key: artifact[key] for key in expected} != expected:
        raise ValueError("WP1 artifact does not bind the exact supplied ARM64 APK")
    signing = require_exact_keys(
        artifact.get("signing"), {"certificateSha256", "verifiedSchemes", "receipt"},
        f"{WP1_COMMITTED_ADAPTER} artifact signing",
    )
    require_hex(signing.get("certificateSha256"), "WP1 artifact signing certificate")
    schemes = signing.get("verifiedSchemes")
    if (
        not isinstance(schemes, list) or schemes != sorted(set(schemes))
        or not all(type(scheme) is int and scheme in (1, 2, 3, 4) for scheme in schemes)
        or not set(schemes).intersection({2, 3, 4})
    ):
        raise ValueError("WP1 artifact signing schemes are not a canonical modern set")
    _validate_wp1_binding(signing.get("receipt"), "WP1 artifact signing receipt")
    _validate_wp1_successor_surfaces(value)
    _validate_referenced_provenance_bytes(value, apk, references, evidence)
    if value.get("doesNotAssert") != list(WP1_DOES_NOT_ASSERT):
        raise ValueError("WP1 non-claim boundary is not exact")
    restore = require_exact_keys(value.get("restore"), {
        "lockedMode", "networkSourcesAllowed", "ownerSourceFallbackAllowed",
        "fullProjectLock", "projectAssets",
    }, f"{WP1_COMMITTED_ADAPTER} restore")
    if (
        restore.get("lockedMode") is not True
        or restore.get("networkSourcesAllowed") is not False
        or restore.get("ownerSourceFallbackAllowed") is not False
    ):
        raise ValueError("WP1 restore posture is not locked and offline")
    _validate_wp1_binding(restore.get("fullProjectLock"), "WP1 full-project lock")
    _validate_wp1_binding(restore.get("projectAssets"), "WP1 project assets")
    for captured, label in _all_build_provenance_references(references):
        require_unchanged(captured, label)
    return value


def validate_apk(bound: BoundBytes) -> None:
    try:
        with zipfile.ZipFile(io.BytesIO(bound.data)) as archive:
            abis = sorted({
                parts[1] for name in archive.namelist()
                if len(parts := name.split("/")) >= 3 and parts[0] == "lib" and parts[1]
            })
    except (OSError, zipfile.BadZipFile) as error:
        raise ValueError("APK is not a readable ZIP artifact") from error
    if abis != [ABI]:
        raise ValueError(f"APK ABI closure is not exactly {ABI}: {abis!r}")


def validate_device_observation(bound: BoundBytes) -> dict[str, object]:
    value = strict_json_bytes(bound.data, "physical device observation")
    return validate_device_payload(value)


def validate_device_payload(value: object) -> dict[str, object]:
    require_exact_keys(value, {
        "schema", "status", "classification", "publicationAuthorized", "serial",
        "serialSha256", "apiLevel", "abi", "abiList", "properties",
        "observationNature", "capturedAtUtc",
    }, "physical device observation")
    if (
        value.get("schema") != DEVICE_SCHEMA or value.get("status") != "pass"
        or value.get("classification") != "physical_api36_arm64_non_emulator"
        or value.get("publicationAuthorized") is not False
        or value.get("apiLevel") != 36 or value.get("abi") != ABI
    ):
        raise ValueError("physical API36 ARM64 device posture is not exact")
    serial = value.get("serial")
    if not isinstance(serial, str) or SERIAL.fullmatch(serial) is None:
        raise ValueError("physical device serial is invalid")
    if value.get("serialSha256") != hashlib.sha256(serial.encode("utf-8")).hexdigest():
        raise ValueError("physical device serial digest mismatch")
    require_utc_timestamp(value.get("capturedAtUtc"), "device capturedAtUtc", canonical_z=True)
    abi_list = value.get("abiList")
    if not isinstance(abi_list, list) or ABI not in abi_list or any(not isinstance(row, str) for row in abi_list):
        raise ValueError("physical device ABI list is invalid")
    properties = require_exact_keys(value.get("properties"), {
        "ro.boot.qemu", "ro.boot.verifiedbootstate", "ro.build.fingerprint",
        "ro.build.id", "ro.build.version.security_patch", "ro.build.version.sdk",
        "ro.hardware", "ro.kernel.qemu", "ro.product.cpu.abi",
        "ro.product.cpu.abilist", "ro.product.device", "ro.product.manufacturer",
        "ro.product.model", "ro.product.name",
    }, "physical device properties")
    if any(not isinstance(row, str) for row in properties.values()):
        raise ValueError("physical device properties must all be strings")
    if (
        properties["ro.build.version.sdk"] != "36"
        or properties["ro.product.cpu.abi"] != ABI
        or properties["ro.product.cpu.abilist"].split(",") != abi_list
        or not properties["ro.build.fingerprint"]
        or not properties["ro.product.manufacturer"]
        or not properties["ro.product.model"]
    ):
        raise ValueError("physical device properties contradict the API/ABI/device observation")
    virtual = "\n".join(str(properties[key]) for key in properties).lower()
    if (
        serial.lower().startswith(("emulator-", "localhost:", "127.0.0.1:", "::1:"))
        or properties["ro.kernel.qemu"] not in ("", "0")
        or properties["ro.boot.qemu"] not in ("", "0")
        or any(marker in virtual for marker in VIRTUAL_MARKERS)
    ):
        raise ValueError("device observation contains emulator evidence")
    return value


def parse_restart_evidence(bound: BoundBytes) -> dict[str, object]:
    try:
        lines = bound.data.decode("utf-8").splitlines()
    except UnicodeError as error:
        raise ValueError("restart evidence is not UTF-8") from error
    expected = (
        "pre_force_stop_process_ids", "pre_force_stop_resumed_component",
        "post_force_stop_process_ids", "restart_process_ids", "restart_resumed_component",
    )
    if len(lines) != len(expected):
        raise ValueError("restart evidence line count is not exact")
    values: dict[str, str] = {}
    for key, line in zip(expected, lines, strict=True):
        prefix = f"{key}="
        if not line.startswith(prefix):
            raise ValueError("restart evidence field order/set is not exact")
        values[key] = line[len(prefix):]
    before = values[expected[0]].split()
    after = values[expected[2]].split()
    restarted = values[expected[3]].split()
    if not before or not restarted or after:
        raise ValueError("restart evidence requires nonempty before/restarted and empty post-stop PIDs")
    if any(PID.fullmatch(row) is None for row in (*before, *restarted)):
        raise ValueError("restart evidence contains an invalid PID")
    if len(before) != len(set(before)) or len(restarted) != len(set(restarted)):
        raise ValueError("restart evidence contains duplicate PIDs")
    if set(before).intersection(restarted):
        raise ValueError("restart evidence reused a pre-force-stop PID")
    before_component = values[expected[1]]
    restarted_component = values[expected[4]]
    if (
        COMPONENT.fullmatch(before_component) is None
        or restarted_component != before_component
    ):
        raise ValueError("restart evidence resumed component is absent, foreign, or changed")
    authority = {
        "beforeProcessIds": before, "afterForceStopProcessIds": [],
        "restartedProcessIds": restarted, "beforeResumedComponent": before_component,
        "restartedResumedComponent": restarted_component,
        "newPidVerified": True,
    }
    return {**authority, "restartAuthoritySha256": canonical_sha256(authority)}


def _raw_device_matches(
    journey_id: str, raw: Mapping[str, object], device: Mapping[str, object],
) -> None:
    variant = "priority" if journey_id == "priority" else "shared"
    observation = require_exact_keys(
        raw.get("deviceObservation"), RAW_DEVICE_FIELDS[variant],
        f"{journey_id} device observation",
    )
    properties = device["properties"]
    expected = {
        "classification": "non-emulator-arm64-api36",
        "evidenceNature": "non-cryptographic getprop and adb serial observations",
        "serial": device["serial"], "apiLevel": 36, "abi": ABI,
        "abiList": properties["ro.product.cpu.abilist"],
        "qemu": properties["ro.kernel.qemu"],
        "manufacturer": properties["ro.product.manufacturer"],
        "model": properties["ro.product.model"], "hardware": properties["ro.hardware"],
        "buildFingerprint": properties["ro.build.fingerprint"],
        "buildId": properties["ro.build.id"],
        "securityPatch": properties["ro.build.version.security_patch"],
        "verifiedBootState": properties["ro.boot.verifiedbootstate"],
    }
    if variant == "priority":
        expected.update({
            "bootQemu": properties["ro.boot.qemu"],
            "productDevice": properties["ro.product.device"],
            "productName": properties["ro.product.name"],
        })
    if observation != expected:
        raise ValueError(f"{journey_id} nested device observation differs from the shared device bytes")


def read_only_adb_policy_reason(arguments: Sequence[str]) -> str | None:
    """Independently classify the driver's closed-world read-only ADB surface."""
    values = tuple(arguments)
    if values == ("get-state",):
        return "exact adb transport-state observation"
    if values == ("exec-out", "screencap", "-p"):
        return "exact framebuffer observation"
    if values == ADB_READ_ONLY_HIERARCHY_ARGUMENTS:
        return "exact accessibility-hierarchy observation without app mutation"
    if values == ADB_FILE_HIERARCHY_DUMP_ARGUMENTS:
        return "exact fenced file-backed accessibility-hierarchy observation"
    if (
        len(values) == 3
        and values[:2] == ("exec-out", "cat")
        and ADB_SAFE_READ_ONLY_REMOTE_PATH.fullmatch(values[2]) is not None
    ):
        return "exact remote-file byte observation"
    if values == ("logcat", "-d", "-t", "500"):
        return "bounded logcat dump observation"
    if values == ADB_CREATION_BOOTSTRAP_LOGCAT_ARGUMENTS:
        return "bounded exact-tag creation-bootstrap timing observation"
    if values == ADB_CREATION_DASHBOARD_READY_LOGCAT_ARGUMENTS:
        return "bounded exact-tag creation-dashboard route-ready snapshot observation"
    if values[:1] != ("shell",):
        return None

    shell_arguments = values[1:]
    if (
        len(shell_arguments) == 2
        and shell_arguments[0] == "getprop"
        and ADB_SAFE_ANDROID_PROPERTY.fullmatch(shell_arguments[1]) is not None
    ):
        return "exact Android property observation"
    if shell_arguments == ("wm", "size"):
        return "exact display-size observation"
    if shell_arguments == ("pidof", PACKAGE):
        return "exact package process-id observation"
    if (
        len(shell_arguments) == 2
        and shell_arguments[0] in {"cat", "sha256sum"}
        and ADB_SAFE_READ_ONLY_REMOTE_PATH.fullmatch(shell_arguments[1]) is not None
    ):
        return "exact remote-file observation"
    if (
        len(shell_arguments) == 4
        and shell_arguments[:3] == ("test", "!", "-e")
        and ADB_SAFE_READ_ONLY_REMOTE_PATH.fullmatch(shell_arguments[3]) is not None
    ):
        return "exact remote-path absence observation"
    if values == ADB_FILE_HIERARCHY_STAT_ARGUMENTS:
        return "exact hierarchy temporary-file identity observation"
    if shell_arguments in {
        ("dumpsys", "input_method"),
        ("dumpsys", "activity", "activities"),
        ("dumpsys", "activity", "lastanr"),
        ("dumpsys", "activity", "processes"),
        ("dumpsys", "activity", "exit-info", PACKAGE),
        ("dumpsys", "window", "windows"),
    }:
        return "exact dumpsys observation"
    if shell_arguments == ("ls", "-la", "/data/anr"):
        return "exact ANR-directory observation"
    if shell_arguments == (
        "logcat", "-d", "-b", "all", "-v", "threadtime", "-t", "4000",
    ):
        return "bounded logcat dump observation"
    return None


def validate_complete_timeout_hierarchy_output(output: object) -> tuple[str, int]:
    """Re-derive the exact hierarchy payload and node count from bound stdout."""
    if not isinstance(output, str):
        raise ValueError("ADB timeout hierarchy stdout must be UTF-8 text")
    try:
        output_bytes = output.encode("utf-8", errors="strict")
    except UnicodeEncodeError as error:
        raise ValueError("ADB timeout hierarchy stdout is not strict UTF-8") from error
    if len(output_bytes) > ADB_TIMEOUT_HIERARCHY_MAX_BYTES:
        raise ValueError("ADB timeout hierarchy stdout exceeds its byte bound")
    hierarchy_start = output.find("<hierarchy")
    hierarchy_close = "</hierarchy>"
    hierarchy_end = output.find(hierarchy_close, hierarchy_start + 1)
    if (
        hierarchy_start < 0
        or hierarchy_end < hierarchy_start
        or output.find("<hierarchy", hierarchy_start + 1) >= 0
        or output.find(hierarchy_close, hierarchy_end + len(hierarchy_close)) >= 0
    ):
        raise ValueError("ADB timeout hierarchy envelope is not single and complete")
    prefix = output[:hierarchy_start]
    suffix = output[hierarchy_end + len(hierarchy_close):]
    if re.fullmatch(r"\s*(?:<\?xml[^>]*\?>\s*)?", prefix) is None or re.fullmatch(
        r"\s*(?:UI (?:hierarchy|hierchary) dumped to:\s*/dev/tty\s*)?",
        suffix,
        flags=re.IGNORECASE,
    ) is None:
        raise ValueError("ADB timeout hierarchy has an unauthorized prefix or suffix")
    payload = output[hierarchy_start:hierarchy_end + len(hierarchy_close)]
    try:
        root = ET.fromstring(payload)
    except ET.ParseError as error:
        raise ValueError("ADB timeout hierarchy XML is malformed") from error
    if (
        root.tag != "hierarchy"
        or set(root.attrib) != {"rotation"}
        or root.attrib["rotation"] not in {"0", "1", "2", "3"}
    ):
        raise ValueError("ADB timeout hierarchy root authority is not exact")
    required_attributes = {
        "index", "text", "resource-id", "class", "package", "content-desc",
        "checkable", "checked", "clickable", "enabled", "focusable", "focused",
        "scrollable", "long-clickable", "password", "selected", "bounds",
    }
    boolean_attributes = {
        "checkable", "checked", "clickable", "enabled", "focusable", "focused",
        "scrollable", "long-clickable", "password", "selected",
    }
    bounds = re.compile(r"\[(\d+),(\d+)\]\[(\d+),(\d+)\]")
    nodes = list(root.iter("node"))
    if any(element.tag != "node" for element in root.iter() if element is not root):
        raise ValueError("ADB timeout hierarchy contains a foreign element")
    if not nodes or any(
        not required_attributes.issubset(node.attrib)
        or re.fullmatch(r"[0-9]+", node.attrib["index"]) is None
        or not node.attrib["class"]
        or node.attrib["package"] != PACKAGE
        or bounds.fullmatch(node.attrib["bounds"]) is None
        or any(node.attrib[name] not in {"true", "false"} for name in boolean_attributes)
        for node in nodes
    ):
        raise ValueError("ADB timeout hierarchy node authority is not exact")
    return payload, len(nodes)


def validate_adb_transport(value: object, *, serial: str, label: str) -> None:
    summary = require_exact_keys(value, {
        "schema", "status", "preflight", "eventCount", "terminalFailureCount", "events",
        "readOnlyMaximumAttempts", "readOnlyRetryDelaySeconds",
        "preflightObservationDelaySeconds", "explicitAdbReconnectCommandAllowed",
        "nonReplayableCommandMaximumAttempts",
    }, f"{label} ADB summary")
    events = summary.get("events")
    require_field_types(summary, {
        "schema": str, "status": str, "preflight": dict, "eventCount": int,
        "terminalFailureCount": int, "events": list, "readOnlyMaximumAttempts": int,
        "readOnlyRetryDelaySeconds": float, "preflightObservationDelaySeconds": float,
        "explicitAdbReconnectCommandAllowed": bool,
        "nonReplayableCommandMaximumAttempts": int,
    }, f"{label} ADB summary")
    if (
        summary.get("schema") != "chummer.android.adb-transport-summary/v1"
        or summary.get("status") != "pass"
        or not isinstance(events, list)
        or summary.get("eventCount") != len(events)
        or summary.get("terminalFailureCount") != 0
        or summary.get("readOnlyMaximumAttempts") != 3
        or summary.get("readOnlyRetryDelaySeconds") != 1.0
        or summary.get("preflightObservationDelaySeconds") != 1.0
        or summary.get("explicitAdbReconnectCommandAllowed") is not False
        or summary.get("nonReplayableCommandMaximumAttempts") != 1
    ):
        raise ValueError(f"{label} ADB summary pass/bounds are not exact")
    preflight = require_exact_keys(summary.get("preflight"), {
        "schema", "status", "serial", "expectedApiLevel", "requiredConsecutiveObservations",
        "maximumObservations", "observationDelaySeconds", "observationsPerformed",
        "consecutiveStableObservations", "mutationCommandsIssued", "recoveryPolicy",
        "recoveryMechanism", "observations",
    }, f"{label} ADB preflight")
    observations = preflight.get("observations")
    require_field_types(preflight, {
        "schema": str, "status": str, "serial": str, "expectedApiLevel": str,
        "requiredConsecutiveObservations": int, "maximumObservations": int,
        "observationDelaySeconds": float, "observationsPerformed": int,
        "consecutiveStableObservations": int, "mutationCommandsIssued": int,
        "recoveryPolicy": str, "recoveryMechanism": str, "observations": list,
    }, f"{label} ADB preflight")
    if (
        preflight.get("schema") != "chummer.android.adb-transport-preflight/v1"
        or preflight.get("status") != "pass" or preflight.get("serial") != serial
        or preflight.get("expectedApiLevel") != "36"
        or preflight.get("requiredConsecutiveObservations") != 3
        or preflight.get("maximumObservations") != 7
        or preflight.get("observationDelaySeconds") != 1.0
        or not isinstance(observations, list)
        or preflight.get("observationsPerformed") != len(observations)
        or preflight.get("consecutiveStableObservations") != 3
        or preflight.get("mutationCommandsIssued") != 0
        or preflight.get("recoveryPolicy") != "bounded-read-only-observation-retry"
        or preflight.get("recoveryMechanism") != "fresh-adb-invocation-no-reconnect-command"
    ):
        raise ValueError(f"{label} ADB preflight is not an exact stable pass")
    if not 3 <= len(observations) <= 7:
        raise ValueError(f"{label} ADB preflight observation count is outside its bound")
    for expected_index, observation in enumerate(observations, start=1):
        if not isinstance(observation, dict):
            raise ValueError(f"{label} ADB observation must be one object")
        if observation.get("status") == "stable":
            require_exact_keys(observation, {"index", "status", "getState", "apiLevel"}, f"{label} stable observation")
            require_field_types(observation, {
                "index": int, "status": str, "getState": str, "apiLevel": str,
            }, f"{label} stable observation")
            if observation != {
                "index": expected_index, "status": "stable", "getState": "device", "apiLevel": "36",
            }:
                raise ValueError(f"{label} stable ADB observation is not exact")
        else:
            require_exact_keys(observation, {
                "index", "status", "classification", "classificationAuthority",
                "retryableReadOnlyObservation", "failure",
            }, f"{label} retryable observation")
            failure = require_exact_keys(
                observation.get("failure"), {"type", "returnCode", "stdout", "stderr"},
                f"{label} retryable observation failure",
            )
            require_field_types(observation, {
                "index": int, "status": str, "classification": str,
                "classificationAuthority": str, "retryableReadOnlyObservation": bool,
                "failure": dict,
            }, f"{label} retryable observation")
            if (
                observation.get("index") != expected_index
                or observation.get("status") != "transport-failure"
                or observation.get("retryableReadOnlyObservation") is not True
                or any(not isinstance(failure[key], str) for key in ("type", "stdout", "stderr"))
                or (failure["returnCode"] is not None and (
                    not isinstance(failure["returnCode"], int) or isinstance(failure["returnCode"], bool)
                ))
            ):
                raise ValueError(f"{label} retryable ADB observation is malformed")
    if [row["status"] for row in observations[-3:]] != ["stable", "stable", "stable"]:
        raise ValueError(f"{label} ADB preflight lacks the final three stable observations")

    base_event_fields = {
        "schema", "status", "serial", "classification", "classificationAuthority",
        "retryableTransportClassification", "commandPolicy", "policyReason", "adbArguments",
        "adbArgumentsSha256", "attempt", "maximumAttempts", "commandInvocationPerformed",
        "outcomeMutationAuthority", "replay", "failure", "evidenceFile",
    }
    reconciliation_statuses = {
        "reconciled-unknown-swipe",
        "reconciled-unknown-hierarchy-dump",
    }
    allowed_statuses = {
        "fail", "retrying-read-only", "recovered-read-only",
        *reconciliation_statuses,
    }
    for index, event in enumerate(events, start=1):
        if not isinstance(event, dict):
            raise ValueError(f"{label} ADB event must be one object")
        status = event.get("status")
        fields = set(base_event_fields)
        if status in reconciliation_statuses:
            fields.update({"reconcilesEvidenceFile", "readOnlyObservation"})
        if (
            status == "recovered-read-only"
            and event.get("classification") == "timeout-output-complete"
        ):
            fields.add("timeoutOutput")
        require_exact_keys(event, fields, f"{label} ADB event")
        require_field_types(event, {
            "schema": str, "status": str, "serial": str, "classification": str,
            "classificationAuthority": str, "retryableTransportClassification": bool,
            "commandPolicy": str, "policyReason": str, "adbArguments": list,
            "adbArgumentsSha256": str, "attempt": int, "maximumAttempts": int,
            "commandInvocationPerformed": bool, "outcomeMutationAuthority": str,
            "replay": dict, "evidenceFile": str,
        }, f"{label} ADB event")
        if (
            event.get("schema") != "chummer.android.adb-transport-event/v1"
            or event.get("serial") != serial
            or status not in allowed_statuses
            or not isinstance(event.get("adbArguments"), list)
            or any(not isinstance(row, str) for row in event["adbArguments"])
            or SHA256.fullmatch(str(event.get("adbArgumentsSha256"))) is None
            or event.get("evidenceFile") != f"adb-transport-event-{index:04d}.json"
        ):
            raise ValueError(f"{label} ADB event identity/status is not exact")
        replay = require_exact_keys(
            event.get("replay"), {"eligible", "performed", "scheduled", "suppressed"},
            f"{label} ADB event replay",
        )
        if any(type(replay[key]) is not bool for key in replay):
            raise ValueError(f"{label} ADB replay fields must be booleans")
        failure = event.get("failure")
        if failure is not None:
            require_exact_keys(failure, {"type", "returnCode", "stdout", "stderr"}, f"{label} ADB event failure")
            require_field_types(failure, {
                "type": str, "returnCode": (int, type(None)),
                "stdout": str, "stderr": str,
            }, f"{label} ADB event failure")
        if status == "fail":
            if failure is None:
                raise ValueError(f"{label} ADB fail event has no failure")
            if event.get("commandPolicy") == "read-only-retryable":
                if (
                    index <= 1
                    or not isinstance(events[index - 2], dict)
                    or events[index - 2].get("status") != "retrying-read-only"
                ):
                    raise ValueError(
                        f"{label} ADB terminal read-only failure has no adjacent retry"
                    )
            elif index >= len(events):
                raise ValueError(
                    f"{label} ADB fail event is terminal or has no adjacent reconciliation"
                )
            else:
                following = events[index]
                if (
                    not isinstance(following, dict)
                    or following.get("status") not in reconciliation_statuses
                    or following.get("reconcilesEvidenceFile")
                    != event.get("evidenceFile")
                ):
                    raise ValueError(
                        f"{label} ADB fail event is not followed by its exact reconciliation"
                    )
        if status in reconciliation_statuses:
            if index <= 1:
                raise ValueError(f"{label} ADB reconciliation is orphaned")
            preceding = events[index - 2]
            if (
                not isinstance(preceding, dict)
                or preceding.get("status") != "fail"
                or event.get("reconcilesEvidenceFile")
                != preceding.get("evidenceFile")
                or event.get("serial") != preceding.get("serial")
                or event.get("adbArguments") != preceding.get("adbArguments")
                or event.get("adbArgumentsSha256")
                != preceding.get("adbArgumentsSha256")
            ):
                raise ValueError(
                    f"{label} ADB reconciliation does not exactly bind the adjacent fail event"
                )
        if status == "reconciled-unknown-swipe":
            preceding = events[index - 2]
            observation = require_exact_keys(event.get("readOnlyObservation"), {
                "arguments", "consecutiveMatching", "observationsPerformed", "hierarchySha256",
            }, f"{label} ADB reconciliation")
            require_hex(observation.get("hierarchySha256"), f"{label} ADB hierarchy sha256")
            expected_original = {
                "classification": "timeout-unknown-outcome",
                "classificationAuthority": "timeout-with-unknown-command-outcome",
                "retryableTransportClassification": True,
                "commandPolicy": "non-replayable",
                "policyReason": "shell mutation or ambiguous shell command is never replayed",
                "adbArguments": list(ADB_SWIPE_REDACTED_ARGUMENTS),
                "attempt": 1,
                "maximumAttempts": 1,
                "commandInvocationPerformed": True,
                "outcomeMutationAuthority": "unknown-fail-closed",
            }
            expected_reconciliation = {
                "classification": "timeout-unknown-outcome",
                "classificationAuthority": (
                    "bounded-consecutive-read-only-hierarchy-observations"
                ),
                "retryableTransportClassification": True,
                "commandPolicy": "non-replayable",
                "policyReason": (
                    "swipe was never replayed; current viewport became authority"
                ),
                "adbArguments": list(ADB_SWIPE_REDACTED_ARGUMENTS),
                "attempt": 1,
                "maximumAttempts": 1,
                "commandInvocationPerformed": False,
                "outcomeMutationAuthority": "current-viewport-observed-no-replay",
            }
            if (
                any(
                    preceding.get(key) != expected
                    for key, expected in expected_original.items()
                )
                or any(
                    event.get(key) != expected
                    for key, expected in expected_reconciliation.items()
                )
                or preceding.get("failure", {}).get("type") != "TimeoutExpired"
                or preceding.get("failure", {}).get("returnCode") is not None
                or preceding.get("replay") != {
                    "eligible": False, "performed": False,
                    "scheduled": False, "suppressed": True,
                }
                or event.get("failure") is not None
                or event.get("replay") != {
                    "eligible": False, "performed": False,
                    "scheduled": False, "suppressed": True,
                }
                or observation.get("arguments") != list(ADB_READ_ONLY_HIERARCHY_ARGUMENTS)
                or observation.get("consecutiveMatching") != 2
                or type(observation.get("observationsPerformed")) is not int
                or not 2 <= observation["observationsPerformed"] <= 3
            ):
                raise ValueError(f"{label} ADB swipe reconciliation is not exact")
        if status == "reconciled-unknown-hierarchy-dump":
            preceding = events[index - 2]
            observation = require_exact_keys(event.get("readOnlyObservation"), {
                "mode", "arguments", "freshnessBarrierArguments",
                "consecutiveMatching", "matchingAuthority",
                "observationsPerformed", "readAttemptMaximumSeconds",
                "maximumObservationSeconds",
                "hierarchySha256", "observationBytesSha256",
            }, f"{label} ADB hierarchy-dump reconciliation")
            require_field_types(observation, {
                "mode": str, "arguments": list, "freshnessBarrierArguments": list,
                "consecutiveMatching": int, "matchingAuthority": str,
                "observationsPerformed": int,
                "readAttemptMaximumSeconds": (int, float),
                "maximumObservationSeconds": (int, float),
                "hierarchySha256": str, "observationBytesSha256": str,
            }, f"{label} ADB hierarchy-dump reconciliation")
            require_hex(
                observation.get("hierarchySha256"),
                f"{label} ADB hierarchy-dump hierarchy sha256",
            )
            require_hex(
                observation.get("observationBytesSha256"),
                f"{label} ADB hierarchy-dump observation-bytes sha256",
            )
            expected_original = {
                "classification": "timeout-unknown-outcome",
                "classificationAuthority": "timeout-with-unknown-command-outcome",
                "retryableTransportClassification": True,
                "commandPolicy": "non-replayable",
                "policyReason": "shell mutation or ambiguous shell command is never replayed",
                "adbArguments": list(ADB_FILE_HIERARCHY_DUMP_REDACTED_ARGUMENTS),
                "adbArgumentsSha256": ADB_FILE_HIERARCHY_DUMP_ARGUMENTS_SHA256,
                "attempt": 1,
                "maximumAttempts": 1,
                "commandInvocationPerformed": True,
                "outcomeMutationAuthority": "unknown-fail-closed",
            }
            expected_reconciliation = {
                "classification": "timeout-unknown-outcome",
                "classificationAuthority": (
                    "bounded-consecutive-read-only-hierarchy-observations"
                ),
                "retryableTransportClassification": True,
                "commandPolicy": "non-replayable",
                "policyReason": (
                    "file-backed dump was never replayed; bounded stable current "
                    "hierarchy became observation authority"
                ),
                "adbArguments": list(ADB_FILE_HIERARCHY_DUMP_REDACTED_ARGUMENTS),
                "adbArgumentsSha256": ADB_FILE_HIERARCHY_DUMP_ARGUMENTS_SHA256,
                "attempt": 1,
                "maximumAttempts": 1,
                "commandInvocationPerformed": False,
                "outcomeMutationAuthority": (
                    "current-hierarchy-observed-no-dump-replay"
                ),
            }
            if any(preceding.get(key) != expected for key, expected in expected_original.items()):
                raise ValueError(
                    f"{label} ADB hierarchy-dump timeout event is not exact"
                )
            if any(event.get(key) != expected for key, expected in expected_reconciliation.items()):
                raise ValueError(
                    f"{label} ADB hierarchy-dump reconciliation metadata is not exact"
                )
            if (
                preceding.get("failure", {}).get("type") != "TimeoutExpired"
                or preceding.get("failure", {}).get("returnCode") is not None
                or preceding.get("replay") != {
                    "eligible": False, "performed": False,
                    "scheduled": False, "suppressed": True,
                }
                or event.get("failure") is not None
                or event.get("replay") != {
                    "eligible": False, "performed": False,
                    "scheduled": False, "suppressed": True,
                }
                or observation.get("freshnessBarrierArguments")
                != list(ADB_FILE_HIERARCHY_REMOVE_ARGUMENTS)
                or observation.get("consecutiveMatching") != 2
            ):
                raise ValueError(
                    f"{label} ADB hierarchy-dump reconciliation proof is not exact"
                )
            observation_mode = observation.get("mode")
            if observation_mode == "fresh-owned-file":
                observation_arguments = ADB_FILE_HIERARCHY_OBSERVATION_ARGUMENTS
                maximum_observations = 8
                read_attempt_maximum_seconds = (
                    ADB_FILE_HIERARCHY_OBSERVATION_READ_ATTEMPT_MAX_SECONDS
                )
                maximum_observation_seconds = (
                    ADB_FILE_HIERARCHY_OBSERVATION_MAX_SECONDS
                )
            elif observation_mode == "direct-current-hierarchy":
                observation_arguments = ADB_READ_ONLY_HIERARCHY_ARGUMENTS
                maximum_observations = 3
                read_attempt_maximum_seconds = (
                    ADB_DIRECT_HIERARCHY_OBSERVATION_READ_ATTEMPT_MAX_SECONDS
                )
                maximum_observation_seconds = (
                    ADB_DIRECT_HIERARCHY_OBSERVATION_MAX_SECONDS
                )
            else:
                raise ValueError(
                    f"{label} ADB hierarchy-dump observation mode is not exact"
                )
            if (
                observation.get("arguments") != list(observation_arguments)
                or observation.get("matchingAuthority")
                != ADB_HIERARCHY_OBSERVATION_MATCHING_AUTHORITY
                or observation.get("readAttemptMaximumSeconds")
                != read_attempt_maximum_seconds
                or observation.get("maximumObservationSeconds")
                != maximum_observation_seconds
                or not 2
                <= observation.get("observationsPerformed", 0)
                <= maximum_observations
            ):
                raise ValueError(
                    f"{label} ADB hierarchy-dump observation is not exact"
                )

    retryable_classification_authorities = {
        "timeout-unknown-outcome": "timeout-with-unknown-command-outcome",
        "observer-process-killed": "exact-file-hierarchy-observer-exit-137",
        "device-offline": "recognized-transient-transport-marker",
        "device-missing": "recognized-transient-transport-marker",
        "transport-closed": "recognized-transient-transport-marker",
        "daemon-unavailable": "recognized-transient-transport-marker",
    }
    terminal_classification_authorities = {
        **retryable_classification_authorities,
        "caller-deadline-exhausted-before-retry": (
            "caller-owned-deadline-before-command"
        ),
        "device-unauthorized": "recognized-nonretryable-transport-marker",
        "unclassified-adb-failure": "unclassified-fail-closed",
    }
    read_only_statuses = {"retrying-read-only", "recovered-read-only"}
    terminal_read_only_failure_seen = False

    def require_read_only_binding(
        earlier: Mapping[str, object],
        later: Mapping[str, object],
        chain_label: str,
    ) -> None:
        for field in (
            "serial", "commandPolicy", "policyReason", "adbArguments",
            "adbArgumentsSha256", "maximumAttempts",
        ):
            if later.get(field) != earlier.get(field):
                raise ValueError(
                    f"{label} ADB {chain_label} changes read-only {field}"
                )
        if later.get("attempt") != earlier.get("attempt", 0) + 1:
            raise ValueError(
                f"{label} ADB {chain_label} attempt progression is not exact"
            )

    for event_index, event in enumerate(events):
        status = event["status"]
        is_terminal_read_only = (
            status == "fail"
            and event.get("commandPolicy") == "read-only-retryable"
        )
        if status not in read_only_statuses and not is_terminal_read_only:
            continue

        arguments = event["adbArguments"]
        expected_arguments_sha256 = hashlib.sha256(
            "\0".join(arguments).encode("utf-8")
        ).hexdigest()
        expected_policy_reason = read_only_adb_policy_reason(arguments)
        attempt = event["attempt"]
        maximum_attempts = event["maximumAttempts"]
        deadline_before_retry = (
            event.get("classification")
            == "caller-deadline-exhausted-before-retry"
        )
        if (
            event.get("commandPolicy") != "read-only-retryable"
            or expected_policy_reason is None
            or event.get("policyReason") != expected_policy_reason
            or event.get("serial") != serial
            or event.get("adbArgumentsSha256") != expected_arguments_sha256
            or type(attempt) is not int
            or type(maximum_attempts) is not int
            or maximum_attempts != summary["readOnlyMaximumAttempts"]
            or not 1 <= attempt <= maximum_attempts
            or event.get("commandInvocationPerformed")
            is not (not deadline_before_retry)
            or event.get("outcomeMutationAuthority") != "none-read-only-command"
        ):
            raise ValueError(f"{label} ADB read-only event identity/bounds are not exact")

        if status == "recovered-read-only":
            if event.get("classification") == "transport-recovered":
                if (
                    event.get("classificationAuthority")
                    != "fresh-read-only-command-succeeded"
                    or event.get("retryableTransportClassification") is not True
                    or event.get("failure") is not None
                    or event.get("replay") != {
                        "eligible": True, "performed": True,
                        "scheduled": False, "suppressed": False,
                    }
                    or attempt <= 1
                    or event_index == 0
                    or events[event_index - 1].get("status")
                    != "retrying-read-only"
                ):
                    raise ValueError(f"{label} ADB read-only recovery is not exact")
                require_read_only_binding(
                    events[event_index - 1], event, "retry recovery",
                )
            elif event.get("classification") == "timeout-output-complete":
                timeout_output = require_exact_keys(
                    event.get("timeoutOutput"),
                    {
                        "validation", "stdout", "stdoutSha256",
                        "stdoutBytes", "hierarchySha256", "hierarchyBytes",
                        "hierarchyNodeCount",
                    },
                    f"{label} ADB timeout hierarchy output",
                )
                require_field_types(timeout_output, {
                    "validation": str, "stdout": str,
                    "stdoutSha256": str, "stdoutBytes": int,
                    "hierarchySha256": str, "hierarchyBytes": int,
                    "hierarchyNodeCount": int,
                }, f"{label} ADB timeout hierarchy output")
                try:
                    hierarchy_payload, hierarchy_node_count = (
                        validate_complete_timeout_hierarchy_output(
                            timeout_output.get("stdout")
                        )
                    )
                except ValueError as error:
                    raise ValueError(
                        f"{label} ADB timeout hierarchy recovery is not exact"
                    ) from error
                stdout_bytes = timeout_output["stdout"].encode("utf-8")
                hierarchy_bytes = hierarchy_payload.encode("utf-8")
                if (
                    event.get("classificationAuthority")
                    != "complete-well-formed-read-only-timeout-stdout"
                    or event.get("retryableTransportClassification") is not True
                    or not isinstance(failure, dict)
                    or failure.get("type") != "TimeoutExpired"
                    or failure.get("returnCode") is not None
                    or event.get("replay") != {
                        "eligible": True, "performed": attempt > 1,
                        "scheduled": False, "suppressed": True,
                    }
                    or event.get("adbArguments")
                    != list(ADB_READ_ONLY_HIERARCHY_ARGUMENTS)
                    or timeout_output.get("validation")
                    != "complete-well-formed-single-hierarchy"
                    or timeout_output.get("stdoutSha256")
                    != hashlib.sha256(stdout_bytes).hexdigest()
                    or timeout_output.get("stdoutBytes") != len(stdout_bytes)
                    or timeout_output.get("hierarchySha256")
                    != hashlib.sha256(hierarchy_bytes).hexdigest()
                    or timeout_output.get("hierarchyBytes") != len(hierarchy_bytes)
                    or timeout_output.get("hierarchyNodeCount")
                    != hierarchy_node_count
                    or failure.get("stdout")
                    != timeout_output["stdout"][:4000]
                    or failure.get("stderr") != ""
                ):
                    raise ValueError(
                        f"{label} ADB timeout hierarchy recovery is not exact"
                    )
                if attempt > 1:
                    if (
                        event_index == 0
                        or events[event_index - 1].get("status")
                        != "retrying-read-only"
                    ):
                        raise ValueError(
                            f"{label} ADB timeout hierarchy recovery is orphaned"
                        )
                    require_read_only_binding(
                        events[event_index - 1], event,
                        "timeout hierarchy recovery",
                    )
            else:
                raise ValueError(f"{label} ADB read-only recovery is not exact")
            continue

        classification = event.get("classification")
        expected_authority = (
            retryable_classification_authorities.get(classification)
            if status == "retrying-read-only"
            else terminal_classification_authorities.get(classification)
        )
        retryable_classification = (
            classification in retryable_classification_authorities
        )
        observer_process_killed = classification == "observer-process-killed"
        expected_replay = {
            "eligible": retryable_classification,
            "performed": attempt > 1 and not deadline_before_retry,
            "scheduled": status == "retrying-read-only",
            "suppressed": status == "fail",
        }
        if (
            expected_authority is None
            or event.get("classificationAuthority") != expected_authority
            or event.get("retryableTransportClassification")
            is not retryable_classification
            or event.get("failure") is None
            or event.get("replay") != expected_replay
            or (
                observer_process_killed
                and (
                    tuple(arguments) != ADB_FILE_HIERARCHY_DUMP_ARGUMENTS
                    or event["failure"].get("type") != "CalledProcessError"
                    or event["failure"].get("returnCode") != 137
                )
            )
            or (
                deadline_before_retry
                and (
                    event["failure"].get("type")
                    != "AdbOperationDeadlineExceeded"
                    or event["failure"].get("returnCode") is not None
                )
            )
        ):
            raise ValueError(f"{label} ADB read-only failure/replay is not exact")

        if attempt > 1:
            if (
                event_index == 0
                or events[event_index - 1].get("status") != "retrying-read-only"
            ):
                raise ValueError(f"{label} ADB read-only retry is orphaned")
            require_read_only_binding(
                events[event_index - 1], event, "retry attempt",
            )

        if status == "retrying-read-only":
            if attempt >= maximum_attempts or event_index + 1 >= len(events):
                raise ValueError(f"{label} ADB read-only retry is dangling")
            following = events[event_index + 1]
            if (
                not isinstance(following, dict)
                or following.get("status")
                not in {"retrying-read-only", "recovered-read-only", "fail"}
            ):
                raise ValueError(
                    f"{label} ADB read-only retry has no adjacent completion"
                )
            require_read_only_binding(event, following, "retry completion")
        else:
            terminal_read_only_failure_seen = True

    if terminal_read_only_failure_seen:
        raise ValueError(f"{label} ADB pass summary contains a terminal read-only failure")


def validate_workspace(value: object, label: str) -> None:
    workspace = require_exact_keys(value, WORKSPACE_FIELDS, label)
    require_string(workspace.get("workspaceId"), f"{label} workspaceId")
    require_integer(workspace.get("contentRevision"), f"{label} contentRevision", minimum=1)
    require_integer(workspace.get("savedRevision"), f"{label} savedRevision", minimum=1)
    require_hex(workspace.get("payloadSha256"), f"{label} payloadSha256")
    require_hex(workspace.get("documentSha256"), f"{label} documentSha256")


def validate_source_authority(journey_id: str, value: object, apk: BoundBytes) -> None:
    authority = require_exact_keys(value, SOURCE_AUTHORITY_FIELDS, f"{journey_id} source graph authority")
    for field in (
        "expectedAndroidSourceRevision", "androidSourceRevision",
        "expectedPresentationSourceRevision", "presentationSourceRevision",
        "expectedCoreSourceRevision", "coreSourceRevision",
    ):
        require_hex(authority.get(field), f"{journey_id} {field}", length=40)
    if (
        authority["expectedAndroidSourceRevision"] != authority["androidSourceRevision"]
        or authority["expectedPresentationSourceRevision"] != authority["presentationSourceRevision"]
        or authority["expectedCoreSourceRevision"] != authority["coreSourceRevision"]
        or authority.get("expectedApkSha256") != apk.sha256
        or authority.get("apkSha256") != apk.sha256
        or authority.get("apkAbis") != [ABI]
    ):
        raise ValueError(f"{journey_id} source graph authority revisions/APK are not cross-bound")
    source_files = require_exact_keys(
        authority.get("sourceFileSha256"), SOURCE_FILE_FIELDS[journey_id],
        f"{journey_id} source file authority",
    )
    for field, digest in source_files.items():
        require_hex(digest, f"{journey_id} source file {field}")
    unsigned = dict(authority)
    digest = unsigned.pop("authoritySha256")
    if digest != canonical_sha256(unsigned):
        raise ValueError(f"{journey_id} source graph authority digest is not canonical")


def _pid_list(value: object, label: str) -> list[str]:
    if not isinstance(value, list) or not value or any(
        not isinstance(row, str) or PID.fullmatch(row) is None for row in value
    ):
        raise ValueError(f"{label} must contain canonical PID strings")
    if len(value) != len(set(value)):
        raise ValueError(f"{label} contains duplicate PIDs")
    return value


def _raw_restart_pids(
    journey_id: str, proof: Mapping[str, object], restart: Mapping[str, object],
) -> list[str]:
    if journey_id == "priority":
        raw_restart = require_exact_keys(
            proof.get("processRestart"), {
                "beforeProcessIds", "afterForceStopProcessIds", "restartedProcessIds",
                "newPidVerified",
            }, "Priority raw processRestart",
        )
        before = _pid_list(raw_restart.get("beforeProcessIds"), "Priority before PIDs")
        after = raw_restart.get("afterForceStopProcessIds")
        restarted = _pid_list(raw_restart.get("restartedProcessIds"), "Priority restarted PIDs")
        if not isinstance(after, list) or after or raw_restart.get("newPidVerified") is not True:
            raise ValueError("Priority raw restart semantics are invalid")
        if set(before).intersection(restarted):
            raise ValueError("Priority raw restart reused a PID")
        if (
            before != restart["beforeProcessIds"]
            or after != restart["afterForceStopProcessIds"]
            or restarted != restart["restartedProcessIds"]
        ):
            raise ValueError("Priority raw restart does not fully cross-bind the durable restart file")
        return restarted
    rows = proof.get("restartProcessIds")
    if not isinstance(rows, list) or len(rows) != 3:
        raise ValueError(f"{journey_id} raw receipt must contain exactly three restarted PID sets")
    normalized = [_pid_list(row, f"{journey_id} restarted PID set") for row in rows]
    flattened = [pid for row in normalized for pid in row]
    if len(flattened) != len(set(flattened)):
        raise ValueError(f"{journey_id} raw receipt reused a restarted PID")
    if normalized[-1] != restart["restartedProcessIds"]:
        raise ValueError(f"{journey_id} raw final restart does not bind the durable restart file")
    return normalized[-1]


def _validate_workspace_group(proof: Mapping[str, object], fields: Sequence[str], label: str) -> None:
    for field in fields:
        validate_workspace(proof.get(field), f"{label}.{field}")


def _validate_priority_proof(proof: Mapping[str, object]) -> None:
    require_exact_keys(proof, {
        "stages", "identityGap", "draftStateAuthority", "finalization",
        "savedCareerWorkspace", "restoredCareerWorkspace",
        "persistedCreationReceiptDigest", "restoredCreationReceiptDigest", "processRestart",
    }, "Priority authorityProofStages")
    stages = proof.get("stages")
    expected_steps = (
        "basics", "method", "foundation", "attributes", "qualities", "skills",
        "magic-resonance", "resources", "contacts-lifestyles", "identity-story",
    )
    if not isinstance(stages, list) or len(stages) != len(expected_steps):
        raise ValueError("Priority stage cardinality is not exact")
    for expected_step, stage in zip(expected_steps, stages, strict=True):
        common = {
            "stepId", "routeId", "requiredByCurrentFinalizer", "routeStatus",
            "authorityVisible", "draftFabricated",
        }
        if expected_step == "identity-story":
            fields = common | {"blocker"}
        else:
            fields = common | {"pageId", "authorityId"}
            extras = {
                "basics": "sourcebookMutation", "method": "buildMethod",
                "resources": "gearDraft", "contacts-lifestyles": "lifestylesAuthority",
            }
            if expected_step in extras:
                fields.add(extras[expected_step])
        stage = require_exact_keys(stage, fields, f"Priority stage {expected_step}")
        require_field_types(stage, {
            "stepId": str, "routeId": str, "requiredByCurrentFinalizer": bool,
            "routeStatus": str, "authorityVisible": bool, "draftFabricated": bool,
        }, f"Priority stage {expected_step}")
        for field in fields - common:
            if type(stage.get(field)) is not str:
                raise ValueError(f"Priority stage {expected_step}.{field} has the wrong JSON type")
        if stage.get("stepId") != expected_step or stage.get("draftFabricated") is not False:
            raise ValueError(f"Priority stage {expected_step} identity/fabrication posture is invalid")
        if expected_step == "identity-story":
            if (
                stage.get("routeStatus") != "typed-contract-unavailable"
                or stage.get("authorityVisible") is not False
                or stage.get("blocker") != "creation-identity-draft-contract-unavailable"
            ):
                raise ValueError("Priority Identity gap is not exact")
        elif stage.get("routeStatus") != "typed-authority-visible" or stage.get("authorityVisible") is not True:
            raise ValueError(f"Priority stage {expected_step} did not expose typed authority")
    if proof.get("identityGap") != stages[-1]:
        raise ValueError("Priority Identity gap does not cross-bind its ordered stage")
    if proof.get("draftStateAuthority") != "typed-phone-pages-preexisting-no-seed-or-fabrication":
        raise ValueError("Priority draft-state authority is not exact")
    finalization = require_exact_keys(proof.get("finalization"), {
        "review", "visibleReviewEvidence", "sealedPlanAuthority", "receiptAuthority",
        "confirmation", "receipt", "careerReopen",
    }, "Priority finalization")
    require_field_types(finalization, {
        "review": str, "visibleReviewEvidence": dict, "sealedPlanAuthority": dict,
        "receiptAuthority": dict, "confirmation": str, "receipt": str,
        "careerReopen": str,
    }, "Priority finalization")
    visible = require_exact_keys(finalization.get("visibleReviewEvidence"), {
        "creation-finalization-binding", "creation-finalization-costs",
        "creation-finalization-atomic-boundary",
    }, "Priority visible finalization review")
    if any(not isinstance(value, str) or not value for value in visible.values()):
        raise ValueError("Priority visible finalization review is malformed")
    plan = require_exact_keys(
        finalization.get("sealedPlanAuthority"), {"contentRevision", "planDigest", "previewDigest"},
        "Priority sealed plan",
    )
    receipt = require_exact_keys(finalization.get("receiptAuthority"), {
        "previousContentRevision", "contentRevision", "savedRevision", "buildMethod",
        "planDigest", "previewDigest", "receiptDigest",
    }, "Priority receipt authority")
    require_field_types(plan, {"contentRevision": int, "planDigest": str, "previewDigest": str}, "Priority sealed plan")
    require_field_types(receipt, {
        "previousContentRevision": int, "contentRevision": int, "savedRevision": int,
        "buildMethod": str, "planDigest": str, "previewDigest": str,
        "receiptDigest": str,
    }, "Priority receipt authority")
    for field in ("contentRevision",):
        require_integer(plan.get(field), f"Priority plan {field}", minimum=1)
    for field in ("previousContentRevision", "contentRevision", "savedRevision"):
        require_integer(receipt.get(field), f"Priority receipt {field}", minimum=1)
    for field in ("planDigest", "previewDigest"):
        require_hex(plan.get(field), f"Priority plan {field}")
        if receipt.get(field) != plan[field]:
            raise ValueError(f"Priority receipt {field} does not bind the sealed plan")
    require_hex(receipt.get("receiptDigest"), "Priority receipt digest")
    if receipt.get("buildMethod") != "Priority":
        raise ValueError("Priority finalization receipt BuildMethod is not exact")
    _validate_workspace_group(proof, ("savedCareerWorkspace", "restoredCareerWorkspace"), "Priority")
    if proof["savedCareerWorkspace"] != proof["restoredCareerWorkspace"]:
        raise ValueError("Priority restored workspace differs from the saved authority")
    for field in ("persistedCreationReceiptDigest", "restoredCreationReceiptDigest"):
        require_hex(proof.get(field), f"Priority {field}")
    if (
        proof["persistedCreationReceiptDigest"] != proof["restoredCreationReceiptDigest"]
        or proof["persistedCreationReceiptDigest"] != receipt["receiptDigest"]
    ):
        raise ValueError("Priority durable receipt digest cross-binding failed")


def _validate_career_checkpoint(value: object, label: str) -> None:
    checkpoint = require_exact_keys(value, CAREER_CHECKPOINT_FIELDS, label)
    integer_fields = {
        "SchemaVersion", "Version", "Kind", "ExpectedContentRevision", "BasePoints",
        "PreviousKarmaPoints", "RatingMaximum", "ExpenseAmount", "UndoQuantity",
        "PreviousRating", "TargetRating", "SavedKarma", "Phase",
    }
    boolean_fields = {"ExpenseRefund", "ExpenseForceCareerVisible"}
    require_field_types(checkpoint, {
        **{field: int for field in integer_fields},
        **{field: bool for field in boolean_fields},
        **{
            field: str
            for field in CAREER_CHECKPOINT_FIELDS - integer_fields - boolean_fields
        },
    }, label)


def _validate_career_proof(proof: Mapping[str, object]) -> None:
    require_exact_keys(proof, CAREER_PROOF_FIELDS, "Career authorityProofStages")
    _validate_workspace_group(proof, (
        "import", "restoredBeforeApply", "restoredAfterApply",
        "finalRestoredAfterAcknowledgement",
    ), "Career")
    _validate_career_checkpoint(proof.get("reviewedCheckpoint"), "Career reviewed checkpoint")
    _validate_career_checkpoint(proof.get("appliedCheckpoint"), "Career applied checkpoint")
    for field in ("reviewedCheckpointSha256", "appliedCheckpointSha256"):
        require_hex(proof.get(field), f"Career {field}")
    projection = require_exact_keys(proof.get("receiptProjection"), {
        "skill", "source", "source_digest", "reviewed_rule", "loaded_rule",
        "loaded_quote", "owner", "action",
    }, "Career receipt projection")
    if any(not isinstance(value, str) or not value for value in projection.values()):
        raise ValueError("Career receipt projection values must be strings")
    require_string(proof.get("generatedExpenseGuid"), "Career generated expense GUID")


def _validate_lane_proof(journey_id: str, proof: Mapping[str, object]) -> None:
    require_exact_keys(proof, LANE_PROOF_FIELDS, f"{journey_id} authorityProofStages")
    scope = require_exact_keys(proof.get("scope"), {"representativeAction", "excluded", "claim"}, f"{journey_id} scope")
    if (
        not isinstance(scope.get("representativeAction"), str)
        or not isinstance(scope.get("excluded"), list)
        or any(not isinstance(row, str) for row in scope["excluded"])
        or scope.get("claim") != "one representative typed action only"
    ):
        raise ValueError(f"{journey_id} scope is not exact/typed")
    _validate_workspace_group(proof, (
        "import", "restoredBeforeApply", "savedSuccessor", "finalRestoredSuccessor",
    ), journey_id)
    require_string(proof.get("actionAutomationId"), f"{journey_id} actionAutomationId")
    automation = require_string_list(
        proof.get("successorActionAutomationIds"), f"{journey_id} successor automation IDs", nonempty=True,
    )
    contracts = proof.get("successorActionAuthority")
    expected_ids = (
        {"before-run.edge.spend", "before-run.edge.regain"}
        if journey_id == "before-run" else {"playtime.weapon.fire"}
    )
    contracts = require_exact_keys(contracts, expected_ids, f"{journey_id} successor action authority")
    if len(automation) != len(expected_ids) or len(automation) != len(set(automation)):
        raise ValueError(f"{journey_id} successor automation authority is not unique/exact")
    for action_id, row in contracts.items():
        fields = (
            {"actionId", "kind", "edgeUsedBefore", "edgeUsedAfter", "totalEdge", "targetRevision", "actionDigest", "automationId"}
            if journey_id == "before-run" else
            {"actionId", "kind", "weaponId", "ammoSlot", "ammoGearId", "fireMode", "roundsConsumed", "ammoBefore", "ammoAfter", "targetRevision", "actionDigest", "automationId"}
        )
        row = require_exact_keys(row, fields, f"{journey_id} successor {action_id}")
        integer_fields = (
            {"edgeUsedBefore", "edgeUsedAfter", "totalEdge"}
            if journey_id == "before-run" else
            {"ammoSlot", "roundsConsumed", "ammoBefore", "ammoAfter"}
        )
        require_field_types(row, {
            **{field: int for field in integer_fields},
            **{field: str for field in fields - integer_fields},
        }, f"{journey_id} successor {action_id}")
        if row.get("actionId") != action_id or row.get("automationId") not in automation:
            raise ValueError(f"{journey_id} successor action identity is not cross-bound")
        require_hex(row.get("targetRevision"), f"{journey_id} successor target revision")
        require_hex(row.get("actionDigest"), f"{journey_id} successor action digest")
    for field in ("reviewedTransactionSha256", "appliedTransactionSha256"):
        require_hex(proof.get(field), f"{journey_id} {field}")
    receipt = require_exact_keys(proof.get("receipt"), LANE_RECEIPT_FIELDS, f"{journey_id} transaction receipt")
    require_field_types(receipt, {
        **{field: int for field in {"ExpectedWorkspaceRevision", "AppliedWorkspaceRevision", "ActionKind"}},
        **{
            field: str
            for field in LANE_RECEIPT_FIELDS
            - {"ExpectedWorkspaceRevision", "AppliedWorkspaceRevision", "ActionKind"}
        },
    }, f"{journey_id} transaction receipt")
    for field in ("ActionDigest", "ExpectedPostconditionDigest", "ObservedPostconditionDigest", "ReceiptDigest"):
        require_hex(str(receipt.get(field)).removeprefix("sha256:"), f"{journey_id} receipt {field}")


def _validate_after_checkpoint(value: object, label: str) -> None:
    checkpoint = require_exact_keys(value, AFTER_CHECKPOINT_FIELDS, label)
    require_field_types(checkpoint, {
        "SchemaVersion": int, "Version": int, "RouteId": str, "Phase": int,
        "Draft": dict, "Receipt": (dict, type(None)), "IdempotencyKey": str,
    }, label)
    draft = require_exact_keys(checkpoint.get("Draft"), AFTER_DRAFT_FIELDS, f"{label}.Draft")
    require_field_types(draft, {
        "OwnerId": str, "Candidate": dict, "Plan": dict, "Acknowledgements": dict,
    }, f"{label}.Draft")
    candidate = require_exact_keys(draft.get("Candidate"), AFTER_CANDIDATE_FIELDS, f"{label}.Candidate")
    require_field_types(candidate, {"RewardContext": dict, "Binding": dict}, f"{label}.Candidate")
    reward = require_exact_keys(candidate.get("RewardContext"), AFTER_REWARD_CONTEXT_FIELDS, f"{label}.RewardContext")
    require_field_types(reward, {
        "ContractName": str, "Identity": dict, "RunTitle": str, "CompletedAt": str,
        "KarmaAward": int, "NuyenAward": int, "RewardReceiptDigest": str,
        "ContextDigest": str,
    }, f"{label}.RewardContext")
    binding = require_exact_keys(candidate.get("Binding"), AFTER_BINDING_FIELDS, f"{label}.Binding")
    require_field_types(binding, {
        "ContractName": str, "WorkspaceId": dict, "WorkspaceRevision": int,
        "Identity": dict, "Quote": dict, "BindingDigest": str,
    }, f"{label}.Binding")
    workspace_id = require_exact_keys(binding.get("WorkspaceId"), {"Value"}, f"{label}.Binding.WorkspaceId")
    require_field_types(workspace_id, {"Value": str}, f"{label}.Binding.WorkspaceId")
    for identity in (reward.get("Identity"), binding.get("Identity")):
        identity = require_exact_keys(identity, AFTER_IDENTITY_FIELDS, f"{label}.Identity")
        require_field_types(identity, {field: str for field in AFTER_IDENTITY_FIELDS}, f"{label}.Identity")
    quote = require_exact_keys(binding.get("Quote"), AFTER_QUOTE_FIELDS, f"{label}.Quote")
    numeric_quote = {
        "HeatBefore", "HeatDelta", "HeatAfter", "StreetCredBefore", "StreetCredDelta",
        "StreetCredAfter", "NotorietyBefore", "NotorietyDelta", "NotorietyAfter",
        "PublicAwarenessBefore", "RequestedPublicAwarenessDelta", "PublicAwarenessAfter",
        "KarmaBefore", "ContactKarmaCost", "KarmaAfter", "Blocker",
    }
    require_field_types(quote, {
        **{field: int for field in numeric_quote},
        **{field: str for field in {
            "GmReviewDigest", "OwnerReviewDigest", "SourceDigest", "CustomDataDigest",
            "GmPolicyDigest", "RuntimeDigest", "LogicalDigest",
        }},
        "Identity": dict, "Contacts": list, "Prerequisites": list, "CanSettle": bool,
    }, f"{label}.Quote")
    quote_identity = require_exact_keys(quote.get("Identity"), AFTER_IDENTITY_FIELDS, f"{label}.Quote.Identity")
    require_field_types(quote_identity, {field: str for field in AFTER_IDENTITY_FIELDS}, f"{label}.Quote.Identity")
    contacts = quote.get("Contacts")
    if not isinstance(contacts, list):
        raise ValueError(f"{label}.Quote.Contacts must be a list")
    contact_fields = {"ContactId", "Name", "Role", "Location", "Connection", "Loyalty", "Kind", "KarmaCost"}
    for contact in contacts:
        contact = require_exact_keys(contact, contact_fields, f"{label}.Quote.Contact")
        require_field_types(contact, {
            **{field: str for field in {"ContactId", "Name", "Role", "Location"}},
            **{field: int for field in {"Connection", "Loyalty", "Kind", "KarmaCost"}},
        }, f"{label}.Quote.Contact")
    prerequisites = quote.get("Prerequisites")
    if not isinstance(prerequisites, list):
        raise ValueError(f"{label}.Quote.Prerequisites must be a list")
    for prerequisite in prerequisites:
        prerequisite = require_exact_keys(prerequisite, {"Prerequisite", "Satisfied", "Authority"}, f"{label}.Prerequisite")
        require_field_types(prerequisite, {
            "Prerequisite": int, "Satisfied": bool, "Authority": str,
        }, f"{label}.Prerequisite")
    plan = require_exact_keys(draft.get("Plan"), AFTER_PLAN_FIELDS, f"{label}.Plan")
    numeric_plan = {
        "TargetHeat", "TargetStreetCred", "TargetNotoriety", "TargetPublicAwareness",
        "TargetKarma", "ContactKarmaCost", "ExpenseAmount",
    }
    require_field_types(plan, {
        **{field: int for field in numeric_plan},
        **{field: str for field in AFTER_PLAN_FIELDS - numeric_plan - {"Identity", "ContactsToAdd"}},
        "Identity": dict, "ContactsToAdd": list,
    }, f"{label}.Plan")
    plan_identity = require_exact_keys(plan.get("Identity"), AFTER_IDENTITY_FIELDS, f"{label}.Plan.Identity")
    require_field_types(plan_identity, {field: str for field in AFTER_IDENTITY_FIELDS}, f"{label}.Plan.Identity")
    planned_contacts = plan.get("ContactsToAdd")
    if not isinstance(planned_contacts, list):
        raise ValueError(f"{label}.Plan.ContactsToAdd must be a list")
    for contact in planned_contacts:
        contact = require_exact_keys(contact, contact_fields, f"{label}.Plan.Contact")
        require_field_types(contact, {
            **{field: str for field in {"ContactId", "Name", "Role", "Location"}},
            **{field: int for field in {"Connection", "Loyalty", "Kind", "KarmaCost"}},
        }, f"{label}.Plan.Contact")
    acknowledgements = require_exact_keys(draft.get("Acknowledgements"), AFTER_ACK_FIELDS, f"{label}.Acknowledgements")
    if any(type(value) is not bool for value in acknowledgements.values()):
        raise ValueError(f"{label}.Acknowledgements must be booleans")
    receipt = checkpoint.get("Receipt")
    if receipt is not None:
        receipt = require_exact_keys(receipt, AFTER_RECEIPT_FIELDS, f"{label}.Receipt")
        numeric_receipt = {
            "HeatBefore", "HeatAfter", "StreetCredBefore", "StreetCredAfter",
            "NotorietyBefore", "NotorietyAfter", "PublicAwarenessBefore",
            "PublicAwarenessAfter", "KarmaBefore", "KarmaAfter", "ContactKarmaCost",
            "ExpenseAmount",
        }
        require_field_types(receipt, {
            **{field: int for field in numeric_receipt},
            **{field: str for field in AFTER_RECEIPT_FIELDS - numeric_receipt - {"Identity", "AddedContacts"}},
            "Identity": dict, "AddedContacts": list,
        }, f"{label}.Receipt")
        receipt_identity = require_exact_keys(receipt.get("Identity"), AFTER_IDENTITY_FIELDS, f"{label}.Receipt.Identity")
        require_field_types(receipt_identity, {field: str for field in AFTER_IDENTITY_FIELDS}, f"{label}.Receipt.Identity")
        added = receipt.get("AddedContacts")
        if not isinstance(added, list):
            raise ValueError(f"{label}.Receipt.AddedContacts must be a list")
        for contact in added:
            contact = require_exact_keys(contact, contact_fields, f"{label}.Receipt.Contact")
            require_field_types(contact, {
                **{field: str for field in {"ContactId", "Name", "Role", "Location"}},
                **{field: int for field in {"Connection", "Loyalty", "Kind", "KarmaCost"}},
            }, f"{label}.Receipt.Contact")


def _validate_after_proof(proof: Mapping[str, object]) -> None:
    require_exact_keys(proof, AFTER_PROOF_FIELDS, "After Run authorityProofStages")
    _validate_workspace_group(proof, (
        "import", "restoredBeforeApply", "savedSuccessor", "finalRestartSuccessor",
    ), "After Run")
    _validate_after_checkpoint(proof.get("reviewedCheckpoint"), "After Run reviewed checkpoint")
    _validate_after_checkpoint(proof.get("appliedCheckpoint"), "After Run applied checkpoint")
    for field in ("reviewedCheckpointSha256", "appliedCheckpointSha256"):
        require_hex(proof.get(field), f"After Run {field}")
    projection = require_exact_keys(proof.get("transactionAndReviewAuthority"), {
        "transactionId", "gmReviewDigest", "ownerReviewDigest", "receiptDigest",
    }, "After Run transaction authority")
    for field in ("gmReviewDigest", "ownerReviewDigest", "receiptDigest"):
        require_hex(projection.get(field), f"After Run {field}")


def _validate_downtime_journal(value: object, label: str) -> None:
    journal = require_exact_keys(value, DOWNTIME_JOURNAL_FIELDS, label)
    require_field_types(journal, {
        "SchemaVersion": int, "Version": int, "Phase": int, "OwnerId": str,
        "ActionId": str, "Review": dict, "ExpectedPostconditionDigest": str,
        "Receipt": (dict, type(None)), "JournalDigest": str,
    }, label)
    review = require_exact_keys(journal.get("Review"), DOWNTIME_REVIEW_FIELDS, f"{label}.Review")
    require_field_types(review, {
        "Schema": str, "WorkspaceId": str, "WorkspaceRevision": int,
        "SnapshotDigest": str, "Preview": dict,
    }, f"{label}.Review")
    preview = require_exact_keys(review.get("Preview"), DOWNTIME_PREVIEW_FIELDS, f"{label}.Preview")
    require_field_types(preview, {
        **{field: int for field in {"Year", "Week", "Operation"}},
        **{field: str for field in DOWNTIME_PREVIEW_FIELDS - {"Year", "Week", "Operation"}},
    }, f"{label}.Preview")
    receipt = journal.get("Receipt")
    if receipt is not None:
        receipt = require_exact_keys(receipt, DOWNTIME_RECEIPT_FIELDS, f"{label}.Receipt")
        require_field_types(receipt, {
            **{field: int for field in {"ExpectedWorkspaceRevision", "AppliedWorkspaceRevision", "Operation"}},
            **{
                field: str
                for field in DOWNTIME_RECEIPT_FIELDS
                - {"ExpectedWorkspaceRevision", "AppliedWorkspaceRevision", "Operation"}
            },
        }, f"{label}.Receipt")


def _validate_downtime_proof(proof: Mapping[str, object]) -> None:
    require_exact_keys(proof, DOWNTIME_PROOF_FIELDS, "Downtime authorityProofStages")
    _validate_workspace_group(proof, (
        "import", "restoredBeforeApply", "savedSuccessor", "finalRestartSuccessor",
    ), "Downtime")
    _validate_downtime_journal(proof.get("reviewedJournal"), "Downtime reviewed journal")
    _validate_downtime_journal(proof.get("appliedJournal"), "Downtime applied journal")
    for field in ("reviewedJournalSha256", "appliedJournalSha256"):
        require_hex(proof.get(field), f"Downtime {field}")
    projection = require_exact_keys(proof.get("receiptAuthority"), {
        "actionId", "previewDigest", "expectedPostconditionDigest", "receiptDigest",
    }, "Downtime receipt authority")
    for field in ("previewDigest", "expectedPostconditionDigest", "receiptDigest"):
        require_hex(projection.get(field), f"Downtime {field}")


def validate_proof_stages(journey_id: str, proof: Mapping[str, object]) -> None:
    if journey_id == "priority":
        _validate_priority_proof(proof)
    elif journey_id == "career":
        _validate_career_proof(proof)
    elif journey_id in {"before-run", "playtime"}:
        _validate_lane_proof(journey_id, proof)
    elif journey_id == "after-run":
        _validate_after_proof(proof)
    else:
        _validate_downtime_proof(proof)


def validate_remote_cleanup(journey_id: str, raw: Mapping[str, object]) -> None:
    if journey_id in {"career", "after-run", "downtime"}:
        rows = raw.get("remoteTemporaryFiles")
        if not isinstance(rows, list) or len(rows) != 2:
            raise ValueError(f"{journey_id} remote temporary file cardinality is not exact")
        expected_fields = {
            "path", "purpose", "precleanAttempted", "precleaned", "cleanupAttempted",
            "cleanupReplaySuppressed", "deletedAndVerified",
        }
        paths: list[str] = []
        for row in rows:
            row = require_exact_keys(row, expected_fields, f"{journey_id} remote temporary file")
            path = require_string(row.get("path"), f"{journey_id} remote path")
            require_string(row.get("purpose"), f"{journey_id} remote purpose")
            if (
                row.get("precleanAttempted") is not True or row.get("precleaned") is not True
                or row.get("cleanupAttempted") is not True
                or row.get("cleanupReplaySuppressed") is not False
                or row.get("deletedAndVerified") is not True
            ):
                raise ValueError(f"{journey_id} remote cleanup booleans are not an exact success")
            paths.append(path)
    elif journey_id in {"before-run", "playtime"}:
        cleanup = raw.get("remoteTemporaryFilesDeleted")
        if not isinstance(cleanup, dict) or len(cleanup) != 2 or any(value is not True for value in cleanup.values()):
            raise ValueError(f"{journey_id} remote cleanup map is not exact")
        paths = list(cleanup)
    else:
        return
    if (
        len(paths) != len(set(paths))
        or "/sdcard/chummer-editing-window.xml" not in paths
        or len([path for path in paths if re.fullmatch(r"/sdcard/Download/[A-Za-z0-9][A-Za-z0-9._-]{0,126}\.chum5", path)]) != 1
    ):
        raise ValueError(f"{journey_id} remote cleanup paths are not the exact fixture/hierarchy set")


def validate_raw_receipt(
    journey_id: str, bound: BoundBytes, *, build_provenance: Mapping[str, object],
    build_provenance_binding: BoundBytes, apk: BoundBytes,
    device: Mapping[str, object], restart: Mapping[str, object],
) -> dict[str, object]:
    if journey_id not in JOURNEY_CONTRACTS:
        raise ValueError(f"unknown journey id: {journey_id}")
    value = strict_json_bytes(bound.data, f"{journey_id} raw receipt")
    require_exact_keys(value, RAW_FIELDS[journey_id], f"{journey_id} raw receipt")
    schema, raw_journey = JOURNEY_CONTRACTS[journey_id]
    if (
        value.get("schema") != schema or value.get("journey") != raw_journey
        or value.get("status") != "device-pass-source-bound"
        or value.get("executionStatus") != "pass" or value.get("profile") != "phone"
        or value.get("apiLevel") != 36 or value.get("abi") != ABI
        or value.get("releaseEvidenceStatus") != "source-and-apk-bound-local-build-not-release-attested"
    ):
        raise ValueError(f"{journey_id} raw receipt pass/schema/phone posture is not exact")
    require_utc_timestamp(value.get("generatedAtUtc"), f"{journey_id} generatedAtUtc")
    if value.get("buildProvenance") != build_provenance:
        raise ValueError(f"{journey_id} raw receipt does not bind the exact WP1 provenance")
    if value.get("apkSha256") != apk.sha256:
        raise ValueError(f"{journey_id} raw receipt APK digest mismatch")
    if value.get("package", PACKAGE) != PACKAGE:
        raise ValueError(f"{journey_id} raw receipt package identity mismatch")
    if value.get("apkAbis", [ABI]) != [ABI]:
        raise ValueError(f"{journey_id} raw receipt ABI closure mismatch")
    if value.get("expectedApkSha256", apk.sha256) != apk.sha256:
        raise ValueError(f"{journey_id} raw receipt expected APK digest mismatch")
    if value.get("serial", device["serial"]) != device["serial"]:
        raise ValueError(f"{journey_id} raw receipt serial mismatch")
    provenance_file = value.get("buildProvenanceFile")
    if provenance_file is not None and provenance_file != {
        "sha256": build_provenance_binding.sha256, "size": build_provenance_binding.size_bytes,
    }:
        raise ValueError(f"{journey_id} raw receipt WP1 provenance byte binding mismatch")
    if journey_id == "priority":
        disposable = value.get("disposableDeviceAuthorization")
        if disposable != {
            "authorized": True,
            "flag": "--allow-destructive-disposable-device",
            "serial": device["serial"],
            "scope": "install-apk-and-atomically-finalize-one-pending-runner",
        }:
            raise ValueError("Priority raw receipt lacks exact disposable-device authorization")
        if (
            value.get("releaseAttested") is not False
            or value.get("buildMethod") != "Priority"
            or value.get("apk") != str(apk.path)
            or value.get("buildProvenanceRecheckedAfterRun") is not True
            or value.get("buildProvenanceFileRecheckedAfterRun") is not True
            or value.get("physicalDeviceProof") is not True
            or value.get("installedArtifactBound") is not True
            or value.get("draftStateFabricated") is not False
            or value.get("identityContractStatus") != "typed-contract-unavailable"
        ):
            raise ValueError("Priority raw receipt physical/provenance/non-release posture is not exact")
    if "publicationAuthorized" in value and value["publicationAuthorized"] is not False:
        raise ValueError(f"{journey_id} raw receipt contains a publication claim")
    forbidden_claims = {
        "publicationauthorized", "releaseattested", "productionrollout",
        "googleplayupload", "googleplayprocessing", "testerdistribution",
        "tabletjourney", "publicreleasereadiness",
    }

    def reject_positive_claims(candidate: object) -> None:
        if isinstance(candidate, dict):
            for key, nested in candidate.items():
                normalized = re.sub(r"[^a-z0-9]", "", key.lower())
                if normalized in forbidden_claims and nested not in (
                    False, None, "false", "unclaimed", "not-asserted",
                ):
                    raise ValueError(f"{journey_id} raw receipt contains forbidden claim {key}")
                reject_positive_claims(nested)
        elif isinstance(candidate, list):
            for nested in candidate:
                reject_positive_claims(nested)

    reject_positive_claims(value)
    if value.get("sourceGraphRecheckedAfterRun", True) is not True:
        raise ValueError(f"{journey_id} raw receipt did not recheck its source graph")
    transport = value.get("adbTransport")
    if "adbTransport" in RAW_FIELDS[journey_id] and (
        not isinstance(transport, dict)
    ):
        raise ValueError(f"{journey_id} raw receipt ADB transport did not pass")
    if "adbTransport" in RAW_FIELDS[journey_id]:
        validate_adb_transport(transport, serial=str(device["serial"]), label=journey_id)
    journeys = value.get("journeys")
    if journey_id != "priority":
        journeys = require_exact_keys(
            journeys, SUBJOURNEY_FIELDS[journey_id], f"{journey_id} subjourneys",
        )
        if any(result != "pass" for result in journeys.values()):
            raise ValueError(f"{journey_id} raw subjourney claims are not all pass")
        source_authority = value.get("sourceGraphAuthority")
        validate_source_authority(journey_id, source_authority, apk)
        if value.get("postRunSourceGraphAuthoritySha256", source_authority["authoritySha256"]) != source_authority["authoritySha256"]:
            raise ValueError(f"{journey_id} post-run source graph digest is not cross-bound")
        if journey_id == "career":
            source_files = source_authority["sourceFileSha256"]
            if any(value.get(field) != source_files[field] for field in CAREER_SOURCE_FIELDS):
                raise ValueError("Career flattened source digests differ from sourceGraphAuthority")
    validate_remote_cleanup(journey_id, value)
    proof = value.get("authorityProofStages")
    if not isinstance(proof, dict):
        raise ValueError(f"{journey_id} raw receipt omitted authorityProofStages")
    validate_proof_stages(journey_id, proof)
    if journey_id in {"before-run", "playtime"} and value.get("scope") != proof.get("scope"):
        raise ValueError(f"{journey_id} top-level scope differs from authorityProofStages")
    _raw_device_matches(journey_id, value, device)
    final_raw_pids = _raw_restart_pids(journey_id, proof, restart)
    if final_raw_pids != restart["restartedProcessIds"]:
        raise ValueError(f"{journey_id} raw final restarted PIDs do not match restart evidence")
    return value


def common_authority(
    *, apk: BoundBytes, graph: BoundBytes, provenance: BoundBytes,
    provenance_payload: Mapping[str, object], device: BoundBytes,
    device_payload: Mapping[str, object], driver_authority: Mapping[str, object],
) -> dict[str, object]:
    return {
        "artifact": {
            **apk.json(), "package": PACKAGE, "targetFramework": TARGET_FRAMEWORK,
            "runtimeIdentifier": RUNTIME_IDENTIFIER, "abi": ABI,
        },
        "sourceGraph": {**graph.json(), "contractName": SOURCE_GRAPH_SCHEMA},
        "buildProvenance": {
            **provenance.json(), "schema": BUILD_PROVENANCE_SCHEMA,
            "adapter": WP1_COMMITTED_ADAPTER,
            "authoritySha256": provenance_payload["authoritySha256"],
        },
        "deviceObservation": {
            **device.json(), "schema": DEVICE_SCHEMA,
            "serial": device_payload["serial"],
            "serialSha256": device_payload["serialSha256"],
        },
        "driverAuthority": dict(driver_authority),
    }


def capture_authority_inputs(
    *, apk_path: Path, source_graph_path: Path, build_provenance_path: Path,
    device_observation_path: Path, repository_root: Path,
    driver_paths: Mapping[str, Path],
) -> tuple[
    BoundBytes, BoundBytes, BoundBytes, dict[str, object], BoundBytes,
    dict[str, object], dict[str, object],
]:
    apk, graph, provenance, provenance_payload = capture_build_inputs(
        apk_path=apk_path, source_graph_path=source_graph_path,
        build_provenance_path=build_provenance_path,
        repository_root=repository_root,
    )
    device = bind_regular(device_observation_path, "physical device observation")
    device_payload = validate_device_observation(device)
    driver_authority = capture_driver_authority(
        repository_root=repository_root, driver_paths=driver_paths,
        source_graph=validate_source_graph(graph),
    )
    return apk, graph, provenance, provenance_payload, device, device_payload, driver_authority


def capture_build_inputs(
    *, apk_path: Path, source_graph_path: Path, build_provenance_path: Path,
    repository_root: Path | None = None,
    references: BuildProvenanceReferences | None = None,
) -> tuple[BoundBytes, BoundBytes, BoundBytes, dict[str, object]]:
    apk = bind_regular(apk_path, "ARM64 APK")
    validate_apk(apk)
    graph = bind_regular(source_graph_path, "v2 source graph")
    validate_source_graph(graph)
    provenance = bind_regular(build_provenance_path, "WP1 build provenance")
    provenance_payload = validate_build_provenance(
        provenance, graph, apk,
        repository_root=repository_root,
        references=references,
    )
    return apk, graph, provenance, provenance_payload


def create_journey_seal(
    *, journey_id: str, raw_receipt_path: Path, restart_evidence_path: Path,
    apk_path: Path, source_graph_path: Path, build_provenance_path: Path,
    device_observation_path: Path, repository_root: Path | None = None,
    driver_paths: Mapping[str, Path] | None = None,
    generated_at_utc: str | None = None,
) -> dict[str, object]:
    repository_root = repository_root or Path(__file__).resolve().parents[1]
    driver_paths = driver_paths or {
        journey: repository_root / relative
        for journey, (relative, _blob) in DRIVER_SPECS.items()
    }
    (
        apk, graph, provenance, provenance_payload, device, device_payload,
        driver_authority,
    ) = capture_authority_inputs(
        apk_path=apk_path, source_graph_path=source_graph_path,
        build_provenance_path=build_provenance_path,
        device_observation_path=device_observation_path,
        repository_root=repository_root, driver_paths=driver_paths,
    )
    raw = bind_regular(raw_receipt_path, f"{journey_id} raw receipt")
    restart_file = bind_regular(restart_evidence_path, f"{journey_id} restart evidence")
    restart = parse_restart_evidence(restart_file)
    raw_payload = validate_raw_receipt(
        journey_id, raw, build_provenance=provenance_payload,
        build_provenance_binding=provenance, apk=apk,
        device=device_payload, restart=restart,
    )
    authority = {
        "schema": SEAL_SCHEMA, "status": "pass",
        "authorityClass": "local_physical_api36_arm64_journey_only",
        "publicationAuthorized": False, "profile": "phone",
        "journeyId": journey_id, "journeyOrder": JOURNEY_ORDER.index(journey_id),
        "rawReceipt": {
            **raw.json(), "schema": raw_payload["schema"],
            "journey": raw_payload["journey"], "status": raw_payload["status"],
        },
        "restartEvidence": {**restart_file.json(), **restart},
        **common_authority(
            apk=apk, graph=graph, provenance=provenance,
            provenance_payload=provenance_payload, device=device,
            device_payload=device_payload, driver_authority=driver_authority,
        ),
        "doesNotAssert": list(DOES_NOT_ASSERT),
    }
    seal = {
        **authority, "sealAuthoritySha256": canonical_sha256(authority),
        "generatedAtUtc": generated_at_utc
        or datetime.now(timezone.utc).isoformat().replace("+00:00", "Z"),
    }
    for captured, label in (
        (apk, "ARM64 APK"), (graph, "v2 source graph"),
        (provenance, "WP1 build provenance"), (device, "physical device observation"),
        (raw, f"{journey_id} raw receipt"),
        (restart_file, f"{journey_id} restart evidence"),
    ):
        require_unchanged(captured, label)
    return seal


def load_and_verify_journey_seal(path: Path, **arguments: object) -> tuple[dict[str, object], BoundBytes]:
    bound = bind_regular(path, "journey seal")
    value = strict_json_bytes(bound.data, "journey seal")
    require_exact_keys(value, {
        "schema", "status", "authorityClass", "publicationAuthorized", "profile",
        "journeyId", "journeyOrder", "rawReceipt", "restartEvidence", "artifact",
        "sourceGraph", "buildProvenance", "deviceObservation", "driverAuthority", "doesNotAssert",
        "sealAuthoritySha256", "generatedAtUtc",
    }, "journey seal")
    generated = require_utc_timestamp(
        value.get("generatedAtUtc"), "journey seal generatedAtUtc", canonical_z=True,
    )
    expected = create_journey_seal(generated_at_utc=generated, **arguments)
    if value != expected:
        raise ValueError("journey seal differs from authenticated current inputs")
    require_unchanged(bound, "journey seal")
    return value, bound


def create_aggregate(
    *, journey_inputs: Sequence[tuple[str, Path, Path, Path]], apk_path: Path,
    source_graph_path: Path, build_provenance_path: Path,
    device_observation_path: Path, repository_root: Path | None = None,
    driver_paths: Mapping[str, Path] | None = None,
    generated_at_utc: str | None = None,
) -> dict[str, object]:
    if tuple(row[0] for row in journey_inputs) != JOURNEY_ORDER or len(journey_inputs) != 6:
        raise ValueError("aggregate journey cardinality/order is not exact")
    repository_root = repository_root or Path(__file__).resolve().parents[1]
    driver_paths = driver_paths or {
        journey: repository_root / relative
        for journey, (relative, _blob) in DRIVER_SPECS.items()
    }
    (
        apk, graph, provenance, provenance_payload, device, device_payload,
        driver_authority,
    ) = capture_authority_inputs(
        apk_path=apk_path, source_graph_path=source_graph_path,
        build_provenance_path=build_provenance_path,
        device_observation_path=device_observation_path,
        repository_root=repository_root, driver_paths=driver_paths,
    )
    rows: list[dict[str, object]] = []
    captured_journey_files: list[tuple[BoundBytes, str]] = []
    observed_pids: set[str] = set()
    for journey_id, raw_path, restart_path, seal_path in journey_inputs:
        seal, seal_bound = load_and_verify_journey_seal(
            seal_path, journey_id=journey_id, raw_receipt_path=raw_path,
            restart_evidence_path=restart_path, apk_path=apk_path,
            source_graph_path=source_graph_path,
            build_provenance_path=build_provenance_path,
            device_observation_path=device_observation_path,
            repository_root=repository_root, driver_paths=driver_paths,
        )
        raw_bound = bind_regular(raw_path, f"{journey_id} raw receipt")
        restart_bound = bind_regular(restart_path, f"{journey_id} restart evidence")
        if raw_bound.json() != {
            key: seal["rawReceipt"][key] for key in ("basename", "sha256", "sizeBytes")
        }:
            raise ValueError(f"{journey_id} aggregate raw receipt binding diverged")
        if restart_bound.json() != {
            key: seal["restartEvidence"][key] for key in ("basename", "sha256", "sizeBytes")
        }:
            raise ValueError(f"{journey_id} aggregate restart binding diverged")
        captured_journey_files.extend((
            (raw_bound, f"{journey_id} raw receipt"),
            (restart_bound, f"{journey_id} restart evidence"),
            (seal_bound, f"{journey_id} journey seal"),
        ))
        journey_pids = [
            *seal["restartEvidence"]["beforeProcessIds"],
            *seal["restartEvidence"]["restartedProcessIds"],
        ]
        if observed_pids.intersection(journey_pids):
            raise ValueError("aggregate reused a before/restarted PID across physical journeys")
        observed_pids.update(journey_pids)
        rows.append({
            "journeyId": journey_id, "journeyOrder": JOURNEY_ORDER.index(journey_id),
            "rawReceipt": seal["rawReceipt"], "restartEvidence": seal["restartEvidence"],
            "receiptSeal": {**seal_bound.json(), "sealAuthoritySha256": seal["sealAuthoritySha256"]},
        })
    authority = {
        "schema": AGGREGATE_SCHEMA, "status": "pass",
        "authorityClass": "local_physical_api36_arm64_six_journey_only",
        "publicationAuthorized": False, "profile": "phone",
        "journeyOrder": list(JOURNEY_ORDER), "journeys": rows,
        **common_authority(
            apk=apk, graph=graph, provenance=provenance,
            provenance_payload=provenance_payload, device=device,
            device_payload=device_payload, driver_authority=driver_authority,
        ),
        "doesNotAssert": list(DOES_NOT_ASSERT),
    }
    aggregate = {
        **authority, "authoritySha256": canonical_sha256(authority),
        "generatedAtUtc": generated_at_utc
        or datetime.now(timezone.utc).isoformat().replace("+00:00", "Z"),
    }
    for captured, label in (
        (apk, "ARM64 APK"), (graph, "v2 source graph"),
        (provenance, "WP1 build provenance"), (device, "physical device observation"),
        *captured_journey_files,
    ):
        require_unchanged(captured, label)
    return aggregate


def load_and_verify_aggregate(path: Path, **arguments: object) -> dict[str, object]:
    bound = bind_regular(path, "six-journey aggregate")
    value = strict_json_bytes(bound.data, "six-journey aggregate")
    require_exact_keys(value, {
        "schema", "status", "authorityClass", "publicationAuthorized", "profile",
        "journeyOrder", "journeys", "artifact", "sourceGraph", "buildProvenance",
        "deviceObservation", "driverAuthority", "doesNotAssert", "authoritySha256", "generatedAtUtc",
    }, "six-journey aggregate")
    generated = require_utc_timestamp(
        value.get("generatedAtUtc"), "aggregate generatedAtUtc", canonical_z=True,
    )
    expected = create_aggregate(generated_at_utc=generated, **arguments)
    if value != expected:
        raise ValueError("six-journey aggregate differs from authenticated current inputs")
    require_unchanged(bound, "six-journey aggregate")
    return value
