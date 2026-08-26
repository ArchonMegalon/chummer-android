#!/usr/bin/env python3
"""Prove the staged SR5 Career Active-Skill wizard on a physical API 36 phone."""

from __future__ import annotations

import argparse
from dataclasses import dataclass
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import re
import stat
import subprocess
import sys
import tempfile
import time
import uuid
import xml.etree.ElementTree as ET
import zipfile

import run_api36_career_active_skill_advance_e2e as leaf
import run_api36_editing_e2e as shared


CHECKPOINT_KEY = "sr5.career.active-skill.draft.v1"
CHOOSE_ROUTE = "sr5-career/advancement/active-skill/choose"
REVIEW_ROUTE = "sr5-career/advancement/active-skill/review"
RECEIPT_ROUTE = "sr5-career/advancement/active-skill/receipt"
CORE_REVISION = "8e2c53bf9c5ac85f675e738bf6e8ecd2ade4bb2a"
PRESENTATION_REVISION = "37b4f048fa50911db7cd493217e1b64005c37770"
LOWER_SHA256 = re.compile(r"^[0-9a-f]{64}$")
LOWER_GIT_REVISION = re.compile(r"^[0-9a-f]{40}$")
DOTNET_UNSPECIFIED_DATE = re.compile(
    r"^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}"
    r"(?:\.[0-9]{1,7})?$"
)
RECEIPT_BINDING = re.compile(
    r"^skill (?P<skill>[0-9a-f-]{36}) · source (?P<source>[0-9a-f-]{36}) · "
    r"source digest (?P<source_digest>[0-9a-f]{64}) · "
    r"reviewed rule (?P<reviewed_rule>[0-9a-f]{64}) · "
    r"loaded rule (?P<loaded_rule>[0-9a-f]{64}) · "
    r"loaded quote (?P<loaded_quote>[0-9a-f]{64}) · "
    r"owner (?P<owner>[0-9a-f-]{36}) · action (?P<action>[0-9a-f-]{36})$"
)
RECEIPT_SCHEMA = "chummer.android.sr5-career-active-skill-physical-e2e/v1"
DISPOSABLE_DEVICE_FLAG = "--allow-destructive-disposable-device"
UNVERIFIED_BUILD_FLAG = "--acknowledge-unverified-build-provenance"
SAFE_FIXTURE_BASENAME = re.compile(
    r"^[A-Za-z0-9][A-Za-z0-9._-]{0,126}\.chum5$"
)
SAFE_REMOTE_PATH = re.compile(
    r"^/sdcard/(?:Download/[A-Za-z0-9][A-Za-z0-9._-]{0,126}\.chum5"
    r"|chummer-editing-window\.xml)$"
)
SAFE_ADB_SERIAL = re.compile(r"^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$")
CHECKPOINT_FIELDS = {
    "SchemaVersion",
    "Version",
    "RouteId",
    "Kind",
    "WorkspaceId",
    "OwnerId",
    "ExpectedContentRevision",
    "SkillId",
    "SourceSkillId",
    "LogicalRevision",
    "SourceRevision",
    "RuleDigest",
    "SkillName",
    "SkillCategory",
    "BasePoints",
    "PreviousKarmaPoints",
    "RatingMaximum",
    "ActionId",
    "ExpenseDateLocal",
    "ExpenseAmount",
    "ExpenseReason",
    "ExpenseType",
    "ExpenseRefund",
    "ExpenseForceCareerVisible",
    "KarmaUndoType",
    "NuyenUndoType",
    "UndoObjectId",
    "UndoQuantity",
    "UndoExtra",
    "PreviousRating",
    "TargetRating",
    "SavedKarma",
    "IdempotencyKey",
    "Phase",
}
CHECKPOINT_EXACT_VALUES = {
    "SchemaVersion": 3,
    "RouteId": REVIEW_ROUTE,
    "Kind": 0,
    "SkillId": leaf.SKILL_ID,
    "SourceSkillId": leaf.SOURCE_SKILL_ID,
    "SkillName": "Pilot Ground Craft",
    "SkillCategory": "Vehicle Active",
    "BasePoints": 2,
    "PreviousKarmaPoints": 1,
    "RatingMaximum": 12,
    "ExpenseAmount": -8,
    "ExpenseReason": "Active Skill Pilot Ground Craft 3 -> 4",
    "ExpenseType": "Karma",
    "ExpenseRefund": False,
    "ExpenseForceCareerVisible": False,
    "KarmaUndoType": "ImproveSkill",
    "NuyenUndoType": "AddCyberware",
    "UndoObjectId": leaf.SKILL_ID,
    "UndoQuantity": 0,
    "UndoExtra": "",
    "PreviousRating": 3,
    "TargetRating": 4,
    "SavedKarma": 12,
}


@dataclass(frozen=True)
class CheckpointSnapshot:
    payload: dict[str, object]
    serialized_sha256: str


def object_without_duplicates(pairs: list[tuple[str, object]]) -> dict[str, object]:
    result: dict[str, object] = {}
    for key, value in pairs:
        if key in result:
            raise ValueError(f"duplicate JSON key: {key}")
        result[key] = value
    return result


def canonical_json_sha256(value: object) -> str:
    encoded = json.dumps(
        value,
        allow_nan=False,
        ensure_ascii=False,
        separators=(",", ":"),
        sort_keys=True,
    ).encode("utf-8")
    return hashlib.sha256(encoded).hexdigest()


def locate_explicit_receipt(argv: list[str]) -> Path:
    values: list[str] = []
    index = 0
    while index < len(argv):
        argument = argv[index]
        if argument == "--receipt":
            if index + 1 >= len(argv) or argv[index + 1].startswith("-"):
                raise RuntimeError("--receipt requires one explicit absolute path")
            values.append(argv[index + 1])
            index += 2
            continue
        if argument.startswith("--receipt="):
            values.append(argument.partition("=")[2])
        index += 1
    if len(values) != 1 or not values[0]:
        raise RuntimeError("Exactly one explicit --receipt path is required")
    raw_path = values[0]
    if "\x00" in raw_path:
        raise RuntimeError("--receipt contains a NUL byte")
    path = Path(raw_path)
    if (
        not path.is_absolute()
        or str(path) != raw_path
        or any(part in {".", ".."} for part in raw_path.split("/"))
    ):
        raise RuntimeError("--receipt must be an absolute normalized path")
    return path


def reject_symlink_components(path: Path, *, label: str) -> None:
    if not path.is_absolute():
        raise RuntimeError(f"{label} must be an absolute path")
    current = Path(path.anchor)
    for part in path.parts[1:]:
        current /= part
        try:
            mode = os.lstat(current).st_mode
        except FileNotFoundError:
            continue
        if stat.S_ISLNK(mode):
            raise RuntimeError(f"{label} contains a symlink component: {current}")


def prepare_receipt_target(path: Path) -> None:
    reject_symlink_components(path, label="Receipt path")
    path.parent.mkdir(parents=True, exist_ok=True)
    reject_symlink_components(path, label="Receipt path")
    try:
        mode = os.lstat(path).st_mode
    except FileNotFoundError:
        return
    if not stat.S_ISREG(mode):
        raise RuntimeError("Receipt target must be a regular non-symlink file")
    path.unlink()


def write_receipt_atomically(path: Path, receipt: dict[str, object]) -> None:
    reject_symlink_components(path, label="Receipt path")
    encoded = (json.dumps(receipt, indent=2, sort_keys=True) + "\n").encode("utf-8")
    temporary_name: str | None = None
    try:
        with tempfile.NamedTemporaryFile(
            mode="wb",
            dir=path.parent,
            prefix=f".{path.name}.",
            delete=False,
        ) as stream:
            temporary_name = stream.name
            os.fchmod(stream.fileno(), 0o600)
            stream.write(encoded)
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temporary_name, path)
        reject_symlink_components(path, label="Receipt path")
    finally:
        if temporary_name is not None:
            Path(temporary_name).unlink(missing_ok=True)


def safe_fixture_basename(path: Path) -> str:
    name = path.name
    if SAFE_FIXTURE_BASENAME.fullmatch(name) is None:
        raise RuntimeError(
            "Career fixture basename must match the safe ASCII *.chum5 grammar"
        )
    return name


def path_is_within(path: Path, root: Path) -> bool:
    try:
        return os.path.commonpath((os.fspath(path), os.fspath(root))) == os.fspath(root)
    except ValueError:
        return False


def git_toplevel(repository: Path) -> Path:
    output = subprocess.run(
        ["git", "rev-parse", "--show-toplevel"],
        cwd=repository,
        check=True,
        capture_output=True,
        text=True,
        timeout=30,
    ).stdout.strip()
    root = Path(output)
    if not root.is_absolute():
        raise RuntimeError(f"Git toplevel is not absolute: {repository}")
    return root


def validate_external_output_path(
    path: Path,
    *,
    label: str,
    repository_roots: tuple[Path, ...],
    expect_directory: bool,
) -> None:
    if not path.is_absolute() or any(
        part in {".", ".."} for part in os.fspath(path).split("/")
    ):
        raise RuntimeError(f"{label} must be an absolute normalized path")
    reject_symlink_components(path, label=label)
    for root in repository_roots:
        if path_is_within(path, root):
            raise RuntimeError(f"{label} must be outside every source worktree")
    try:
        mode = os.lstat(path).st_mode
    except FileNotFoundError:
        return
    expected = stat.S_ISDIR(mode) if expect_directory else stat.S_ISREG(mode)
    if not expected:
        kind = "directory" if expect_directory else "regular file"
        raise RuntimeError(f"{label} must be a non-symlink {kind}")


def validate_output_layout(*, receipt: Path, evidence: Path) -> None:
    """Require separate output trees so evidence activity cannot collide with receipt."""
    if (
        receipt == evidence
        or path_is_within(receipt, evidence)
        or path_is_within(evidence, receipt)
    ):
        raise RuntimeError(
            "Receipt file and evidence directory must be separate non-overlapping "
            "paths; sibling outputs are allowed"
        )


def source_repository_roots(
    *,
    android_root: Path,
    workspace_root: Path,
) -> tuple[Path, ...]:
    return (
        git_toplevel(android_root),
        git_toplevel(workspace_root / "chummer-core-engine"),
        git_toplevel(workspace_root / "chummer-presentation"),
    )


def unverified_build_provenance(
    *,
    expected_android_head: str | None,
    expected_apk_sha256: str | None,
    source_graph_authority_sha256: str | None = None,
) -> dict[str, object]:
    """Describe caller-bound bytes without claiming an authenticated build."""
    return {
        "status": "unverified",
        "releaseEvidenceEligible": False,
        "externalBuildAuthorityManifest": None,
        "reason": (
            "No external build-authority manifest authenticates the exact Android "
            "HEAD to APK SHA-256 binding"
        ),
        "callerExpectedAndroidHead": expected_android_head,
        "callerExpectedApkSha256": expected_apk_sha256,
        "sourceGraphAuthoritySha256": source_graph_authority_sha256,
    }


def canonical_guid(value: object, label: str) -> str:
    if not isinstance(value, str):
        raise RuntimeError(f"{label} is not a typed GUID string")
    try:
        parsed = uuid.UUID(value)
    except (AttributeError, ValueError) as error:
        raise RuntimeError(f"{label} is not a GUID") from error
    canonical = str(parsed)
    if parsed.int == 0 or value != canonical:
        raise RuntimeError(f"{label} is not one canonical nonempty GUID")
    return canonical


def dotnet_roundtrip_unspecified(value: str) -> str:
    if DOTNET_UNSPECIFIED_DATE.fullmatch(value) is None:
        raise RuntimeError("Checkpoint expense date is not an unspecified local timestamp")
    parsed = datetime.fromisoformat(value)
    if parsed.tzinfo is not None:
        raise RuntimeError("Checkpoint expense date unexpectedly contains an offset")
    return parsed.strftime("%Y-%m-%dT%H:%M:%S.%f") + "0"


def expected_idempotency_key(checkpoint: dict[str, object]) -> str:
    expense_date = dotnet_roundtrip_unspecified(str(checkpoint["ExpenseDateLocal"]))
    values = (
        checkpoint["RouteId"],
        checkpoint["OwnerId"],
        checkpoint["WorkspaceId"],
        checkpoint["ExpectedContentRevision"],
        checkpoint["ActionId"],
        f'{checkpoint["SkillId"]}:{checkpoint["SourceSkillId"]}',
        checkpoint["LogicalRevision"],
        checkpoint["SourceRevision"],
        checkpoint["RuleDigest"],
        checkpoint["SkillName"],
        checkpoint["SkillCategory"],
        checkpoint["BasePoints"],
        checkpoint["PreviousKarmaPoints"],
        checkpoint["RatingMaximum"],
        expense_date,
        checkpoint["ExpenseAmount"],
        checkpoint["ExpenseReason"],
        checkpoint["ExpenseType"],
        str(checkpoint["ExpenseRefund"]),
        str(checkpoint["ExpenseForceCareerVisible"]),
        checkpoint["KarmaUndoType"],
        checkpoint["NuyenUndoType"],
        checkpoint["UndoObjectId"],
        checkpoint["UndoQuantity"],
        checkpoint["UndoExtra"],
        checkpoint["PreviousRating"],
        checkpoint["TargetRating"],
        checkpoint["SavedKarma"],
    )
    return hashlib.sha256(
        "\n".join(str(value) for value in values).encode("utf-8")
    ).hexdigest()


def validate_checkpoint(
    checkpoint: dict[str, object],
    *,
    workspace_id: str,
    expected_content_revision: int,
    phase: int,
    version: int,
) -> None:
    if set(checkpoint) != CHECKPOINT_FIELDS:
        raise RuntimeError(
            "Career checkpoint fields are not exact: "
            f"expected={sorted(CHECKPOINT_FIELDS)!r}, actual={sorted(checkpoint)!r}"
        )
    expected = {
        **CHECKPOINT_EXACT_VALUES,
        "Version": version,
        "WorkspaceId": workspace_id,
        "ExpectedContentRevision": expected_content_revision,
        "Phase": phase,
    }
    for field, value in expected.items():
        if checkpoint[field] != value or type(checkpoint[field]) is not type(value):
            raise RuntimeError(
                f"Career checkpoint {field} is not exact: "
                f"expected {value!r}, got {checkpoint[field]!r}"
            )

    canonical_guid(checkpoint["OwnerId"], "Checkpoint owner identity")
    canonical_guid(checkpoint["ActionId"], "Checkpoint action/expense identity")
    for field in ("LogicalRevision", "SourceRevision", "RuleDigest"):
        value = checkpoint[field]
        if not isinstance(value, str) or LOWER_SHA256.fullmatch(value) is None:
            raise RuntimeError(f"Career checkpoint {field} is not canonical SHA-256")
    if checkpoint["IdempotencyKey"] != expected_idempotency_key(checkpoint):
        raise RuntimeError("Career checkpoint idempotency digest is not exact")


def read_checkpoint(device: shared.Device, *, required: bool = True) -> CheckpointSnapshot | None:
    listing = device.shell(
        "run-as",
        shared.PACKAGE,
        "find",
        "shared_prefs",
        "-type",
        "f",
        "-name",
        "*.xml",
    )
    matches: list[str] = []
    for path in (line.strip() for line in listing.splitlines() if line.strip()):
        raw = device.run(
            "exec-out",
            "run-as",
            shared.PACKAGE,
            "cat",
            path,
        ).stdout
        try:
            root = ET.fromstring(raw)
        except ET.ParseError as error:
            raise RuntimeError(f"Shared-preference file is malformed: {path}") from error
        matches.extend(
            element.text or ""
            for element in root.findall("string")
            if element.get("name") == CHECKPOINT_KEY
        )
    if not matches:
        if required:
            raise RuntimeError("The durable SR5 Career checkpoint is missing")
        return None
    if len(matches) != 1 or not matches[0]:
        raise RuntimeError(
            f"Expected one durable SR5 Career checkpoint, got {len(matches)}"
        )
    serialized = matches[0]
    try:
        parsed = json.loads(serialized, object_pairs_hook=object_without_duplicates)
    except (json.JSONDecodeError, ValueError) as error:
        raise RuntimeError("The durable SR5 Career checkpoint is not strict JSON") from error
    if not isinstance(parsed, dict):
        raise RuntimeError("The durable SR5 Career checkpoint is not an object")
    return CheckpointSnapshot(
        parsed,
        hashlib.sha256(serialized.encode("utf-8")).hexdigest(),
    )


def require_same_action(
    reviewed: dict[str, object],
    applied: dict[str, object],
) -> None:
    changing = {"Version", "Phase"}
    reviewed_action = {
        key: value for key, value in reviewed.items() if key not in changing
    }
    applied_action = {
        key: value for key, value in applied.items() if key not in changing
    }
    if applied_action != reviewed_action:
        raise RuntimeError("Applied checkpoint does not bind the exact reviewed action")


def wait_exact_route(
    device: shared.Device,
    route: str,
    *,
    timeout: int = 90,
) -> shared.UiNode:
    return device.wait_for_single_exact_accessibility_value(
        route,
        timeout=timeout,
        evidence_prefix="sr5-career-route",
        surface_name="SR5 Career route",
    )


def tap_exact_route(device: shared.Device, route: str, *, timeout: int = 90) -> None:
    node = wait_exact_route(device, route, timeout=timeout)
    if not device.node_has_tappable_bounds(node):
        device.capture("sr5-career-route-not-tappable")
        raise RuntimeError(f"Exact SR5 Career route is not tappable: {route}")
    x, y = node.center
    device.shell("input", "tap", str(x), str(y))


def open_choose(device: shared.Device) -> None:
    shared.open_build(device, "phone")
    shared.reset_scroll_to_top(device, swipes=16)
    device.tap_bidirectional(
        "build-sr5-career-wizard",
        timeout=120,
        backward_scrolls=16,
        forward_scrolls=48,
        scroll_distance_ratio=0.18,
        exact_resource_id=True,
    )
    wait_exact_route(device, "sr5-career", timeout=60)
    advancement_route = "sr5-career/advancement"
    tap_exact_route(device, advancement_route, timeout=60)
    wait_exact_route(device, advancement_route, timeout=60)
    device.tap_single_exact_resource_id(
        "sr5-career-action-active-skill",
        timeout=90,
        evidence_prefix="sr5-career-active-skill-action",
        surface_name="SR5 Career Active-Skill action",
    )
    wait_exact_route(device, CHOOSE_ROUTE, timeout=120)


def assert_initial_quote(device: shared.Device) -> None:
    picker = device.wait_for_single_exact_resource_id(
        "sr5-career-active-skill-picker",
        timeout=60,
        evidence_prefix="sr5-career-active-skill-picker",
        surface_name="SR5 Career Active-Skill picker",
    )
    if "Pilot Ground Craft" not in (picker.attributes.get("text") or ""):
        raise RuntimeError("The staged wizard did not choose the exact saved active skill")
    rating = device.wait_for_single_exact_resource_id(
        "sr5-career-active-skill-rating",
        timeout=30,
        evidence_prefix="sr5-career-active-skill-rating",
        surface_name="SR5 Career Active-Skill rating quote",
    )
    cost = device.wait_for_single_exact_resource_id(
        "sr5-career-active-skill-cost",
        timeout=30,
        evidence_prefix="sr5-career-active-skill-cost",
        surface_name="SR5 Career Active-Skill Karma quote",
    )
    if (rating.attributes.get("text") or "") != "Current 3 · after 4 · maximum 12":
        raise RuntimeError("The staged wizard rendered a non-exact initial rating quote")
    if (cost.attributes.get("text") or "") != "Cost 8 Karma · available 20 · after 12":
        raise RuntimeError("The staged wizard rendered a non-exact initial Karma quote")


def assert_successor_quote(device: shared.Device) -> None:
    rating = device.wait_for_single_exact_resource_id(
        "sr5-career-active-skill-rating",
        timeout=30,
        evidence_prefix="sr5-career-active-skill-successor-rating",
        surface_name="SR5 Career successor rating quote",
    )
    cost = device.wait_for_single_exact_resource_id(
        "sr5-career-active-skill-cost",
        timeout=30,
        evidence_prefix="sr5-career-active-skill-successor-cost",
        surface_name="SR5 Career successor Karma quote",
    )
    if (rating.attributes.get("text") or "") != "Current 4 · after 5 · maximum 12":
        raise RuntimeError("The staged wizard did not reload the exact saved rating")
    if (cost.attributes.get("text") or "") != "Cost 10 Karma · available 12 · after 2":
        raise RuntimeError("The staged wizard did not reload the next exact Karma quote")


def label_bound_value(
    device: shared.Device,
    label: str,
    *,
    swipes: int = 30,
) -> str:
    """Read one metric value horizontally bound to one exact visible label."""
    shared.reset_scroll_to_top(device, swipes=swipes)
    for _ in range(swipes + 1):
        nodes = device.hierarchy()
        labels = [node for node in nodes if node.attributes.get("text") == label]
        if len(labels) > 1:
            device.capture("sr5-career-metric-label-cardinality")
            raise RuntimeError(f"Receipt metric label {label!r} is ambiguous")
        if len(labels) == 1:
            label_node = labels[0]
            _, label_top, label_right, label_bottom = label_node.bounds
            values: list[shared.UiNode] = []
            for candidate in nodes:
                text = candidate.attributes.get("text", "")
                if not text or candidate is label_node:
                    continue
                left, top, right, bottom = candidate.bounds
                vertically_overlaps = top < label_bottom and bottom > label_top
                if vertically_overlaps and left >= label_right and right > left:
                    values.append(candidate)
            if len(values) != 1:
                device.capture("sr5-career-metric-value-cardinality")
                raise RuntimeError(
                    f"Receipt metric {label!r} has {len(values)} bound values; expected one"
                )
            return values[0].attributes["text"]
        device.swipe_up(distance_ratio=0.18)
        time.sleep(0.35)
    device.capture("sr5-career-metric-label-missing")
    raise RuntimeError(f"Receipt metric label {label!r} was not rendered")


def require_exact_page_text(
    device: shared.Device,
    expected: str,
    *,
    swipes: int = 30,
) -> None:
    shared.reset_scroll_to_top(device, swipes=swipes)
    for _ in range(swipes + 1):
        matches = [
            node
            for node in device.hierarchy()
            if node.attributes.get("text") == expected
        ]
        if len(matches) > 1:
            device.capture("sr5-career-page-text-cardinality")
            raise RuntimeError(f"Page text {expected!r} is ambiguous")
        if len(matches) == 1:
            return
        device.swipe_up(distance_ratio=0.18)
        time.sleep(0.35)
    device.capture("sr5-career-page-text-missing")
    raise RuntimeError(f"Page text {expected!r} was not rendered")


def read_exact_receipt_binding(
    device: shared.Device,
    *,
    swipes: int = 30,
) -> dict[str, str]:
    shared.reset_scroll_to_top(device, swipes=swipes)
    for _ in range(swipes + 1):
        matches = [
            match
            for node in device.hierarchy()
            if (match := RECEIPT_BINDING.fullmatch(
                node.attributes.get("text", "")
            )) is not None
        ]
        if len(matches) > 1:
            device.capture("sr5-career-receipt-binding-cardinality")
            raise RuntimeError("Receipt digest/identity binding is ambiguous")
        if len(matches) == 1:
            return matches[0].groupdict()
        device.swipe_up(distance_ratio=0.18)
        time.sleep(0.35)
    device.capture("sr5-career-receipt-binding-missing")
    raise RuntimeError("Receipt digest/identity binding was not rendered")


def require_review_text(
    device: shared.Device,
    checkpoint: dict[str, object],
) -> None:
    wait_exact_route(device, REVIEW_ROUTE)
    require_exact_page_text(device, "Review exact diff", swipes=18)
    expected_metrics = {
        "Skill": "Pilot Ground Craft",
        "Rating": "3 → 4",
        "Karma": "20 → 12",
        "Expense": "Active Skill Pilot Ground Craft 3 -> 4",
        "Date": dotnet_roundtrip_unspecified(str(checkpoint["ExpenseDateLocal"])),
        "Undo type": "ImproveSkill",
        "Skill identity": str(checkpoint["SkillId"]),
        "Source identity": str(checkpoint["SourceSkillId"]),
        "Expense identity": str(checkpoint["ActionId"]),
    }
    for label, expected in expected_metrics.items():
        actual = label_bound_value(device, label, swipes=18)
        if actual != expected:
            raise RuntimeError(
                f"Review metric {label!r} differs: expected {expected!r}, got {actual!r}"
            )
    device.wait_exact_resource_id_bidirectional(
        "sr5-career-active-skill-apply",
        timeout=90,
        backward_scrolls=18,
        forward_scrolls=18,
        scroll_distance_ratio=0.18,
        evidence_prefix="sr5-career-active-skill-apply",
        surface_name="SR5 Career Active-Skill apply control",
    )


def require_receipt_text(
    device: shared.Device,
    checkpoint: dict[str, object],
) -> dict[str, str]:
    wait_exact_route(device, RECEIPT_ROUTE)
    require_exact_page_text(device, "Verified saved advancement")
    expected_metrics = {
        "Skill": "Pilot Ground Craft",
        "Rating": "3 → 4",
        "Karma spent": "8",
        "Saved Karma": "12",
        "Saved revision": str(int(checkpoint["ExpectedContentRevision"]) + 1),
        "Expense identity": str(checkpoint["ActionId"]),
        "Expense date": dotnet_roundtrip_unspecified(
            str(checkpoint["ExpenseDateLocal"])
        ),
        "Expense reason": "Active Skill Pilot Ground Craft 3 -> 4",
        "Expense type": "Karma",
        "Refund": "False",
        "Career visible": "False",
        "Karma undo type": "ImproveSkill",
        "Nuyen undo type": "AddCyberware",
        "Undo object": leaf.SKILL_ID,
        "Undo quantity": "0",
        "Undo extra": "—",
    }
    for label, expected in expected_metrics.items():
        actual = label_bound_value(device, label)
        if actual != expected:
            raise RuntimeError(
                f"Receipt metric {label!r} differs: expected {expected!r}, got {actual!r}"
            )
    require_exact_page_text(
        device,
        "Receipt values came from fresh typed skill and expense projections for this clean saved revision.",
    )
    device.wait_exact_resource_id_bidirectional(
        "sr5-career-active-skill-receipt-acknowledge",
        timeout=90,
        backward_scrolls=30,
        forward_scrolls=30,
        scroll_distance_ratio=0.18,
        evidence_prefix="sr5-career-receipt-acknowledge",
        surface_name="SR5 Career receipt acknowledgement control",
    )
    binding = read_exact_receipt_binding(device)
    expected_binding = {
        "skill": str(checkpoint["SkillId"]),
        "source": str(checkpoint["SourceSkillId"]),
        "source_digest": str(checkpoint["SourceRevision"]),
        "reviewed_rule": str(checkpoint["RuleDigest"]),
        "loaded_rule": str(checkpoint["RuleDigest"]),
        "owner": str(checkpoint["OwnerId"]),
        "action": str(checkpoint["ActionId"]),
    }
    for field, expected in expected_binding.items():
        if binding[field] != expected:
            raise RuntimeError(
                f"Receipt typed binding {field} differs: "
                f"expected {expected!r}, got {binding[field]!r}"
            )
    if binding["loaded_quote"] == checkpoint["LogicalRevision"]:
        raise RuntimeError("Fresh saved receipt reused the reviewed logical revision")
    return binding


def git_revision(repository: Path, *, expected: str | None = None) -> str:
    completed = subprocess.run(
        ["git", "rev-parse", "HEAD"],
        cwd=repository,
        check=True,
        capture_output=True,
        text=True,
        timeout=30,
    )
    revision = completed.stdout.strip()
    if LOWER_GIT_REVISION.fullmatch(revision) is None:
        raise RuntimeError(f"Source revision is not one exact Git commit: {repository}")
    if expected is not None and revision != expected:
        raise RuntimeError(
            f"Source revision differs for {repository}: expected {expected}, got {revision}"
        )
    status = subprocess.run(
        ["git", "status", "--porcelain=v1", "--untracked-files=all"],
        cwd=repository,
        check=True,
        capture_output=True,
        text=True,
        timeout=30,
    ).stdout
    if status:
        raise RuntimeError(f"Source worktree is dirty: {repository}")
    return revision


def source_graph_snapshot(
    *,
    android_root: Path,
    core_root: Path,
    presentation_root: Path,
    apk: Path,
    expected_apk_sha256: str,
    expected_android_revision: str,
    source_paths: dict[str, Path],
) -> dict[str, object]:
    if LOWER_SHA256.fullmatch(expected_apk_sha256) is None:
        raise RuntimeError("Expected APK SHA-256 is not canonical")
    if LOWER_GIT_REVISION.fullmatch(expected_android_revision) is None:
        raise RuntimeError("Expected Android HEAD is not one exact commit")
    actual_apk_sha256 = shared.sha256(apk)
    if actual_apk_sha256 != expected_apk_sha256:
        raise RuntimeError(
            "APK SHA-256 differs from the caller-supplied expected artifact"
        )
    file_sha256 = {key: shared.sha256(path) for key, path in source_paths.items()}
    authority: dict[str, object] = {
        "expectedAndroidSourceRevision": expected_android_revision,
        "androidSourceRevision": git_revision(
            android_root,
            expected=expected_android_revision,
        ),
        "expectedPresentationSourceRevision": PRESENTATION_REVISION,
        "presentationSourceRevision": git_revision(
            presentation_root,
            expected=PRESENTATION_REVISION,
        ),
        "expectedCoreSourceRevision": CORE_REVISION,
        "coreSourceRevision": git_revision(core_root, expected=CORE_REVISION),
        "expectedApkSha256": expected_apk_sha256,
        "apkSha256": actual_apk_sha256,
        "apkAbis": apk_abis(apk),
        "sourceFileSha256": file_sha256,
    }
    return {
        **authority,
        "authoritySha256": canonical_json_sha256(authority),
    }


def apk_abis(apk: Path) -> list[str]:
    if apk.is_symlink() or not apk.is_file():
        raise RuntimeError("Physical proof APK is not one regular file")
    try:
        with zipfile.ZipFile(apk) as archive:
            abis = {
                parts[1]
                for name in archive.namelist()
                if len(parts := name.split("/")) >= 3
                and parts[0] == "lib"
                and parts[1]
            }
    except (OSError, zipfile.BadZipFile) as error:
        raise RuntimeError("Physical proof APK is not a readable Android package") from error
    if "arm64-v8a" not in abis:
        raise RuntimeError(
            f"Physical proof APK has no ARM64 native payload: {sorted(abis)!r}"
        )
    return sorted(abis)


def android_device_observation(device: shared.Device) -> dict[str, object]:
    if device.run("get-state").stdout.strip() != "device":
        raise RuntimeError("The requested physical Android transport is not ready")
    api = device.shell("getprop", "ro.build.version.sdk")
    if api != "36":
        raise RuntimeError(f"Physical SR5 Career proof requires API 36, got {api!r}")
    abi = device.shell("getprop", "ro.product.cpu.abi")
    if abi != "arm64-v8a":
        raise RuntimeError(
            f"Physical SR5 Career proof requires arm64-v8a, got {abi!r}"
        )
    abi_list = device.shell("getprop", "ro.product.cpu.abilist")
    if "arm64-v8a" not in abi_list.split(","):
        raise RuntimeError("Physical device ABI list does not contain arm64-v8a")
    qemu = device.shell("getprop", "ro.kernel.qemu")
    hardware = device.shell("getprop", "ro.hardware")
    if (
        device.serial.startswith("emulator-")
        or qemu == "1"
        or any(token in hardware.lower() for token in ("goldfish", "ranchu", "cuttlefish"))
    ):
        raise RuntimeError("The requested transport is an emulator, not a physical phone")
    return {
        "classification": "non-emulator-arm64-api36",
        "evidenceNature": "non-cryptographic getprop and adb serial observations",
        "serial": device.serial,
        "apiLevel": int(api),
        "abi": abi,
        "abiList": abi_list,
        "qemu": qemu,
        "manufacturer": device.shell("getprop", "ro.product.manufacturer"),
        "model": device.shell("getprop", "ro.product.model"),
        "hardware": hardware,
        "buildFingerprint": device.shell("getprop", "ro.build.fingerprint"),
        "buildId": device.shell("getprop", "ro.build.id"),
        "securityPatch": device.shell("getprop", "ro.build.version.security_patch"),
        "verifiedBootState": device.shell("getprop", "ro.boot.verifiedbootstate"),
    }


def remove_remote_temporary_file(device: shared.Device, remote_path: str) -> None:
    if SAFE_REMOTE_PATH.fullmatch(remote_path) is None:
        raise RuntimeError(
            "Remote temporary path does not match the explicit safe ASCII grammar"
        )
    device.shell("rm", "-f", remote_path)
    device.run("shell", "test", "!", "-e", remote_path)


def prove_staged_wizard(
    device: shared.Device,
    fixture: Path,
    fixture_sha256: str,
) -> dict[str, object]:
    device.shell("pm", "clear", shared.PACKAGE)
    initial_launch, imported = leaf.prepare_runner(
        device,
        fixture.name,
        fixture_sha256,
    )
    leaf.assert_before(leaf.root_for_authority(device, imported))

    open_choose(device)
    assert_initial_quote(device)
    device.capture("sr5-career-active-skill-choose")
    device.tap_single_exact_resource_id(
        "sr5-career-active-skill-review",
        timeout=60,
        evidence_prefix="sr5-career-active-skill-review",
        surface_name="SR5 Career Active-Skill review control",
    )
    wait_exact_route(device, REVIEW_ROUTE, timeout=90)
    reviewed = read_checkpoint(device)
    if reviewed is None:
        raise RuntimeError("Reviewed checkpoint unexpectedly disappeared")
    validate_checkpoint(
        reviewed.payload,
        workspace_id=imported.workspace_id,
        expected_content_revision=imported.content_revision,
        phase=0,
        version=1,
    )
    require_review_text(device, reviewed.payload)
    device.capture("sr5-career-active-skill-reviewed-checkpoint")

    reviewed_restart = shared.force_stop_and_launch_new_process(device, initial_launch)
    shared.wait_for_phone_runner_route(device, created=True, timeout=120)
    shared.tap_phone_destination(device, "phone-destination-runners")
    shared.wait_for_phone_runners(device, timeout=120)
    restored_before_apply = shared.read_phone_workspace_authority(device)
    shared.require_restored_authority(imported, restored_before_apply)
    leaf.assert_before(leaf.root_for_authority(device, restored_before_apply))
    resumed_checkpoint = read_checkpoint(device)
    if resumed_checkpoint is None:
        raise RuntimeError("Reviewed checkpoint unexpectedly disappeared after restart")
    if resumed_checkpoint != reviewed:
        raise RuntimeError("Reviewed checkpoint bytes changed across process restart")

    open_choose(device)
    device.wait_exact_resource_id_bidirectional(
        "sr5-career-active-skill-resume",
        timeout=90,
        backward_scrolls=20,
        forward_scrolls=20,
        scroll_distance_ratio=0.18,
        evidence_prefix="sr5-career-active-skill-resume",
        surface_name="SR5 Career reviewed-checkpoint resume control",
    )
    recovery = device.wait_for_single_exact_resource_id(
        "sr5-career-active-skill-recovery",
        timeout=30,
        scroll=True,
        max_scrolls=20,
        scroll_distance_ratio=0.18,
        evidence_prefix="sr5-career-active-skill-recovery",
        surface_name="SR5 Career checkpoint recovery status",
    )
    if (recovery.attributes.get("text") or "") != (
        "A durable reviewed advancement can be resumed with the same owner and action identity."
    ):
        raise RuntimeError("The reviewed checkpoint did not authenticate after restart")
    device.tap_bidirectional(
        "sr5-career-active-skill-resume",
        timeout=90,
        backward_scrolls=20,
        forward_scrolls=20,
        scroll_distance_ratio=0.18,
        exact_resource_id=True,
    )
    wait_exact_route(device, REVIEW_ROUTE, timeout=90)
    require_review_text(device, reviewed.payload)
    device.capture("sr5-career-active-skill-resumed-review")
    device.tap_bidirectional(
        "sr5-career-active-skill-apply",
        timeout=120,
        backward_scrolls=18,
        forward_scrolls=18,
        scroll_distance_ratio=0.22,
        exact_resource_id=True,
    )
    wait_exact_route(device, RECEIPT_ROUTE, timeout=180)
    applied = read_checkpoint(device)
    if applied is None:
        raise RuntimeError("Applied checkpoint unexpectedly disappeared")
    validate_checkpoint(
        applied.payload,
        workspace_id=imported.workspace_id,
        expected_content_revision=imported.content_revision,
        phase=2,
        version=3,
    )
    require_same_action(reviewed.payload, applied.payload)
    receipt_projection = require_receipt_text(device, applied.payload)
    device.capture("sr5-career-active-skill-typed-receipt")

    applied_restart = shared.force_stop_and_launch_new_process(
        device,
        reviewed_restart.restarted,
    )
    shared.wait_for_phone_runner_route(device, created=True, timeout=120)
    open_choose(device)
    wait_exact_route(device, RECEIPT_ROUTE, timeout=180)
    recovered_applied = read_checkpoint(device)
    if recovered_applied is None:
        raise RuntimeError("Applied checkpoint unexpectedly disappeared after restart")
    if recovered_applied != applied:
        raise RuntimeError("Applied checkpoint bytes changed during receipt recovery")
    recovered_receipt_projection = require_receipt_text(
        device,
        recovered_applied.payload,
    )
    if recovered_receipt_projection != receipt_projection:
        raise RuntimeError("Recovered typed receipt projection differs after restart")
    device.capture("sr5-career-active-skill-recovered-receipt")
    device.tap_bidirectional(
        "sr5-career-active-skill-receipt-acknowledge",
        timeout=120,
        backward_scrolls=30,
        forward_scrolls=30,
        scroll_distance_ratio=0.22,
        exact_resource_id=True,
    )
    time.sleep(1)
    if read_checkpoint(device, required=False) is not None:
        raise RuntimeError("Acknowledged applied checkpoint was not removed")

    shared.tap_phone_destination(device, "phone-destination-runners")
    shared.wait_for_phone_runners(device, timeout=120)
    restored_after_apply = shared.read_phone_workspace_authority(device)
    shared.require_saved_authority(restored_after_apply)
    if restored_after_apply.workspace_id != imported.workspace_id:
        raise RuntimeError("Staged Active-Skill apply changed workspace identity")
    if restored_after_apply.content_revision != imported.content_revision + 1:
        raise RuntimeError("Staged Active-Skill apply did not save one exact successor revision")
    if restored_after_apply.payload_sha256 == imported.payload_sha256:
        raise RuntimeError("Staged Active-Skill apply did not change the payload digest")
    expense_id = leaf.assert_after(
        leaf.root_for_authority(device, restored_after_apply)
    )
    if expense_id != applied.payload["ActionId"]:
        raise RuntimeError("Saved expense identity differs from the typed receipt action")

    acknowledged_restart = shared.force_stop_and_launch_new_process(
        device,
        applied_restart.restarted,
    )
    shared.wait_for_phone_runner_route(device, created=True, timeout=120)
    shared.tap_phone_destination(device, "phone-destination-runners")
    shared.wait_for_phone_runners(device, timeout=120)
    final_restored = shared.read_phone_workspace_authority(device)
    shared.require_restored_authority(restored_after_apply, final_restored)
    if read_checkpoint(device, required=False) is not None:
        raise RuntimeError(
            "Acknowledged checkpoint deletion did not survive process restart"
        )
    final_expense_id = leaf.assert_after(
        leaf.root_for_authority(device, final_restored)
    )
    if final_expense_id != expense_id:
        raise RuntimeError("Saved expense identity changed after final process restart")
    open_choose(device)
    assert_successor_quote(device)
    device.capture("sr5-career-active-skill-final-restart-successor-quote")
    return {
        "import": shared.workspace_authority_json(imported),
        "restoredBeforeApply": shared.workspace_authority_json(restored_before_apply),
        "restoredAfterApply": shared.workspace_authority_json(restored_after_apply),
        "finalRestoredAfterAcknowledgement": shared.workspace_authority_json(
            final_restored
        ),
        "reviewedCheckpoint": reviewed.payload,
        "reviewedCheckpointSha256": reviewed.serialized_sha256,
        "appliedCheckpoint": applied.payload,
        "appliedCheckpointSha256": applied.serialized_sha256,
        "receiptProjection": receipt_projection,
        "generatedExpenseGuid": expense_id,
        "restartProcessIds": [
            list(reviewed_restart.restarted.process_ids),
            list(applied_restart.restarted.process_ids),
            list(acknowledged_restart.restarted.process_ids),
        ],
    }


def parse_args(argv: list[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--adb", type=Path, required=True)
    parser.add_argument("--apk", type=Path, required=True)
    parser.add_argument("--expected-apk-sha256", required=True)
    parser.add_argument("--expected-android-head", required=True)
    parser.add_argument("--serial", required=True)
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--receipt", type=Path, required=True)
    parser.add_argument("--workspace-root", type=Path, required=True)
    parser.add_argument(DISPOSABLE_DEVICE_FLAG, action="store_true")
    parser.add_argument(UNVERIFIED_BUILD_FLAG, action="store_true")
    parser.add_argument(
        "--career-runner",
        type=Path,
        default=Path(__file__).resolve().parent
        / "fixtures/career-active-skill-advance-e2e.chum5",
    )
    return parser.parse_args(argv)


def execute(
    args: argparse.Namespace,
    context: dict[str, object],
) -> dict[str, object]:
    if not args.allow_destructive_disposable_device:
        raise RuntimeError(
            f"{DISPOSABLE_DEVICE_FLAG} is required because this journey installs the APK, "
            "clears Chummer app data, imports a runner, and mutates that disposable runner"
        )
    if not args.acknowledge_unverified_build_provenance:
        raise RuntimeError(
            f"{UNVERIFIED_BUILD_FLAG} is required because no external build-authority "
            "manifest authenticates the Android HEAD to APK SHA-256 binding"
        )
    if SAFE_ADB_SERIAL.fullmatch(args.serial) is None:
        raise RuntimeError("ADB serial does not match the explicit safe ASCII grammar")

    driver = Path(__file__).resolve()
    android_root = driver.parents[1]
    workspace_root = args.workspace_root.resolve()
    core_root = workspace_root / "chummer-core-engine"
    presentation_root = workspace_root / "chummer-presentation"
    repository_roots = source_repository_roots(
        android_root=android_root,
        workspace_root=workspace_root,
    )
    validate_external_output_path(
        args.receipt,
        label="Receipt path",
        repository_roots=repository_roots,
        expect_directory=False,
    )
    validate_external_output_path(
        args.evidence,
        label="Evidence path",
        repository_roots=repository_roots,
        expect_directory=True,
    )
    validate_output_layout(receipt=args.receipt, evidence=args.evidence)
    context["releaseEvidenceStatus"] = "ineligible-unverified-build-provenance"
    context["buildProvenance"] = unverified_build_provenance(
        expected_android_head=args.expected_android_head,
        expected_apk_sha256=args.expected_apk_sha256,
    )

    fixture = args.career_runner.resolve()
    fixture_basename = safe_fixture_basename(fixture)
    source_paths = {
        "sharedDriverSha256": Path(shared.__file__).resolve(),
        "x86LeafDriverSha256": Path(leaf.__file__).resolve(),
        "buildPageSha256": android_root / "src/Chummer.Android/Native/BuildPage.cs",
        "runnerCoordinatorSha256": android_root
        / "src/Chummer.Android/Native/RunnerSessionCoordinator.cs",
        "careerWizardModelSha256": android_root
        / "src/Chummer.Android/Native/Sr5CareerWizardModel.cs",
        "careerWizardPageSha256": android_root
        / "src/Chummer.Android/Native/Sr5CareerWizardPage.cs",
        "activeSkillWizardPageSha256": android_root
        / "src/Chummer.Android/Native/Sr5CareerActiveSkillWizardPage.cs",
        "activeSkillCoordinatorSha256": android_root
        / "src/Chummer.Android/Native/Sr5CareerActiveSkillCoordinator.cs",
        "checkpointStoreSha256": android_root
        / "src/Chummer.Android/Native/Sr5CareerDraftCheckpointStore.cs",
        "careerActiveSkillRequestSha256": presentation_root
        / "Chummer.Presentation/Overview/CareerActiveSkillAdvanceEditRequest.cs",
        "careerActiveSkillMutationSha256": presentation_root
        / "Chummer.Presentation/Overview/CareerActiveSkillAdvanceMutation.cs",
        "presenterPersistenceSha256": presentation_root
        / "Chummer.Presentation/Overview/CharacterOverviewPresenter.Persistence.cs",
        "careerActiveSkillRulesSha256": core_root
        / "Chummer.Contracts/Characters/CharacterCareerActiveSkillAdvanceRules.cs",
        "activeSkillSourceResolverSha256": core_root
        / "Chummer.Infrastructure/Xml/FileSystemCharacterSourceDataResolver.cs",
        "workspaceStoreSha256": core_root
        / "Chummer.Infrastructure/Workspaces/FileWorkspaceStore.cs",
        "careerFixtureSha256": fixture,
        "driverSha256": driver,
    }
    missing = [str(path) for path in source_paths.values() if not path.is_file()]
    if missing:
        raise RuntimeError(f"Staged SR5 Career source graph is incomplete: {missing!r}")

    apk = args.apk.resolve()
    source_before = source_graph_snapshot(
        android_root=android_root,
        core_root=core_root,
        presentation_root=presentation_root,
        apk=apk,
        expected_apk_sha256=args.expected_apk_sha256,
        expected_android_revision=args.expected_android_head,
        source_paths=source_paths,
    )
    context["sourceGraphAuthority"] = source_before
    context["buildProvenance"] = unverified_build_provenance(
        expected_android_head=args.expected_android_head,
        expected_apk_sha256=args.expected_apk_sha256,
        source_graph_authority_sha256=str(source_before["authoritySha256"]),
    )
    leaf.require_canonical_import_fixture(ET.parse(fixture).getroot())
    source_file_sha256 = source_before["sourceFileSha256"]
    if not isinstance(source_file_sha256, dict):
        raise RuntimeError("Source graph file digest authority is malformed")
    fixture_sha256 = str(source_file_sha256["careerFixtureSha256"])
    device = shared.Device(args.adb.resolve(), args.serial, args.evidence)
    remote_fixture = f"/sdcard/Download/{fixture_basename}"
    remote_temporary_files: list[dict[str, object]] = [
        {
            "path": remote_fixture,
            "purpose": "temporary canonical Career runner import",
            "precleaned": False,
            "deletedAndVerified": False,
        },
        {
            "path": "/sdcard/chummer-editing-window.xml",
            "purpose": "temporary UIAutomator hierarchy dump",
            "precleaned": False,
            "deletedAndVerified": False,
        },
    ]
    context["remoteTemporaryFiles"] = remote_temporary_files
    errors: list[str] = []
    journey: dict[str, object] | None = None
    device_observation: dict[str, object] | None = None
    device_validated = False
    verified_remote_fixture_sha256 = ""
    try:
        device_observation = android_device_observation(device)
        context["deviceObservation"] = device_observation
        device_validated = True
        for remote in remote_temporary_files:
            remove_remote_temporary_file(device, str(remote["path"]))
            remote["precleaned"] = True
        subprocess.run(
            [
                str(args.adb.resolve()),
                "-s",
                args.serial,
                "install",
                "--no-streaming",
                "-r",
                str(apk),
            ],
            check=True,
            timeout=300,
        )
        verified_remote_fixture_sha256 = device.push_verified(
            fixture,
            remote_fixture,
            fixture_sha256,
        )
        journey = prove_staged_wizard(device, fixture, fixture_sha256)
    except Exception as error:  # noqa: BLE001 - receipt must record every runtime failure
        errors.append(f"journey failed: {type(error).__name__}: {error}")
    finally:
        if device_validated:
            for remote in remote_temporary_files:
                try:
                    remove_remote_temporary_file(device, str(remote["path"]))
                    remote["deletedAndVerified"] = True
                except Exception as error:  # noqa: BLE001 - cleanup is proof
                    errors.append(
                        "remote temporary-file cleanup failed for "
                        f"{remote['path']}: {type(error).__name__}: {error}"
                    )
        try:
            source_after = source_graph_snapshot(
                android_root=android_root,
                core_root=core_root,
                presentation_root=presentation_root,
                apk=apk,
                expected_apk_sha256=args.expected_apk_sha256,
                expected_android_revision=args.expected_android_head,
                source_paths=source_paths,
            )
            context["postRunSourceGraphAuthority"] = source_after
            if source_after != source_before:
                errors.append("source/APK authority changed during device execution")
        except Exception as error:  # noqa: BLE001 - TOCTOU recheck must fail closed
            context["postRunSourceGraphAuthorityError"] = (
                f"{type(error).__name__}: {error}"
            )
            errors.append(f"source/APK authority recheck failed: {type(error).__name__}: {error}")
    if errors:
        raise RuntimeError("; ".join(errors))
    if (
        journey is None
        or device_observation is None
        or not all(
            remote["deletedAndVerified"] for remote in remote_temporary_files
        )
    ):
        raise RuntimeError("Device journey, observation, or remote cleanup proof is incomplete")

    return {
        "schema": RECEIPT_SCHEMA,
        "status": "device-pass-non-release",
        "executionStatus": "pass",
        "releaseEvidenceStatus": "ineligible-unverified-build-provenance",
        "buildProvenance": context["buildProvenance"],
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "serial": args.serial,
        "profile": "phone",
        "journey": "sr5-career-active-skill-wizard-physical",
        "apiLevel": device_observation["apiLevel"],
        "abi": device_observation["abi"],
        "deviceObservation": device_observation,
        "package": shared.PACKAGE,
        "apk": str(apk),
        "apkSha256": source_before["apkSha256"],
        "expectedApkSha256": source_before["expectedApkSha256"],
        "apkAbis": source_before["apkAbis"],
        "androidSourceRevision": source_before["androidSourceRevision"],
        "expectedAndroidSourceRevision": source_before[
            "expectedAndroidSourceRevision"
        ],
        "presentationSourceRevision": source_before[
            "presentationSourceRevision"
        ],
        "coreSourceRevision": source_before["coreSourceRevision"],
        "sourceGraphAuthority": source_before,
        "postRunSourceGraphAuthoritySha256": source_before["authoritySha256"],
        "sourceGraphRecheckedAfterRun": True,
        **source_file_sha256,
        "careerFixtureSha256": fixture_sha256,
        "verifiedRemoteCareerFixtureSha256": verified_remote_fixture_sha256,
        "remoteTemporaryFiles": remote_temporary_files,
        "authorityProofStages": journey,
        "journeys": {
            "chooseExactTypedSkill": "pass",
            "reviewDurableCheckpoint": "pass",
            "reviewedCheckpointProcessRestartResume": "pass",
            "applyOnceAndFreshTypedReceipt": "pass",
            "appliedCheckpointProcessRestartRecovery": "pass",
            "acknowledgeAndDeleteAppliedCheckpoint": "pass",
            "acknowledgedDeletionFinalProcessRestart": "pass",
            "savedSuccessorRevisionAndPayloadDigest": "pass",
        },
    }


def failure_receipt(
    args: argparse.Namespace,
    error: Exception,
    context: dict[str, object],
) -> dict[str, object]:
    return {
        "schema": RECEIPT_SCHEMA,
        "status": "fail",
        "executionStatus": "fail",
        "releaseEvidenceStatus": "ineligible-unverified-build-provenance",
        "buildProvenance": unverified_build_provenance(
            expected_android_head=args.expected_android_head,
            expected_apk_sha256=args.expected_apk_sha256,
        ),
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "profile": "phone",
        "journey": "sr5-career-active-skill-wizard-physical",
        "serial": args.serial,
        "expectedAndroidSourceRevision": args.expected_android_head,
        "expectedApkSha256": args.expected_apk_sha256,
        "failure": {
            "type": type(error).__name__,
            "message": str(error)[:4000],
        },
        **context,
    }


def argument_failure_receipt(exit_code: int) -> dict[str, object]:
    return {
        "schema": RECEIPT_SCHEMA,
        "status": "fail",
        "executionStatus": "fail",
        "releaseEvidenceStatus": "ineligible-unverified-build-provenance",
        "buildProvenance": unverified_build_provenance(
            expected_android_head=None,
            expected_apk_sha256=None,
        ),
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "profile": "phone",
        "journey": "sr5-career-active-skill-wizard-physical",
        "failure": {
            "type": "ArgumentParseError",
            "message": f"Full command-line validation exited with status {exit_code}",
        },
    }


def preparse_repository_roots(argv: list[str]) -> tuple[Path, ...]:
    """Find only roots safely knowable before full argparse validation."""
    driver = Path(__file__).resolve()
    roots = [git_toplevel(driver.parents[1])]
    values: list[str] = []
    index = 0
    while index < len(argv):
        argument = argv[index]
        if argument == "--workspace-root":
            if index + 1 < len(argv) and not argv[index + 1].startswith("-"):
                values.append(argv[index + 1])
                index += 2
                continue
        elif argument.startswith("--workspace-root="):
            values.append(argument.partition("=")[2])
        index += 1
    if len(values) != 1 or not values[0] or "\x00" in values[0]:
        return tuple(roots)
    workspace_root = Path(values[0])
    if not workspace_root.is_absolute() or str(workspace_root) != values[0]:
        return tuple(roots)
    for repository in (
        workspace_root / "chummer-core-engine",
        workspace_root / "chummer-presentation",
    ):
        try:
            roots.append(git_toplevel(repository))
        except (OSError, RuntimeError, subprocess.SubprocessError):
            # Full validation will report a malformed/missing workspace. The receipt
            # still must be invalidated if it is external to every knowable root.
            continue
    return tuple(roots)


def main(argv: list[str] | None = None) -> int:
    raw_args = list(sys.argv[1:] if argv is None else argv)
    if any(argument in {"-h", "--help"} for argument in raw_args):
        try:
            parse_args(["--help"])
        except SystemExit as error:
            return int(error.code or 0)
        return 0

    try:
        receipt_path = locate_explicit_receipt(raw_args)
        validate_external_output_path(
            receipt_path,
            label="Receipt path",
            repository_roots=preparse_repository_roots(raw_args),
            expect_directory=False,
        )
        prepare_receipt_target(receipt_path)
    except Exception as error:  # noqa: BLE001 - never follow or unlink unsafe target
        print(f"Cannot prepare explicit receipt target: {error}", file=sys.stderr)
        return 2

    try:
        args = parse_args(raw_args)
    except SystemExit as error:
        exit_code = int(error.code or 0)
        if exit_code != 0:
            write_receipt_atomically(
                receipt_path,
                argument_failure_receipt(exit_code),
            )
        return exit_code

    if args.receipt != receipt_path:
        write_receipt_atomically(receipt_path, argument_failure_receipt(2))
        print("Parsed receipt path differs from the pre-parsed target", file=sys.stderr)
        return 2

    context: dict[str, object] = {}
    driver = Path(__file__).resolve()
    try:
        repository_roots = source_repository_roots(
            android_root=driver.parents[1],
            workspace_root=args.workspace_root.resolve(),
        )
        validate_external_output_path(
            receipt_path,
            label="Receipt path",
            repository_roots=repository_roots,
            expect_directory=False,
        )
    except Exception as error:  # noqa: BLE001 - forbidden receipt gets no output
        print(f"Receipt path validation failed: {error}", file=sys.stderr)
        return 2

    try:
        validate_external_output_path(
            args.evidence,
            label="Evidence path",
            repository_roots=repository_roots,
            expect_directory=True,
        )
        validate_output_layout(receipt=receipt_path, evidence=args.evidence)
        receipt = execute(args, context)
    except Exception as error:  # noqa: BLE001 - stale pass must never survive
        receipt = failure_receipt(args, error, context)
        write_receipt_atomically(receipt_path, receipt)
        print(
            f"Physical SR5 Career Active-Skill E2E failed: {error}",
            file=sys.stderr,
        )
        return 1
    write_receipt_atomically(receipt_path, receipt)
    print(json.dumps(receipt, indent=2, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
