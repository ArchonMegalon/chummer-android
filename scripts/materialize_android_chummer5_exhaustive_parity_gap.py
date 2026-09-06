#!/usr/bin/env python3
"""Materialize an honest, row-complete Android/Chummer5 parity gap receipt.

This audit is deliberately separate from the seven-journey phone-beta gate.  A
green wizard journey proves only that bounded journey; it does not promote a
legacy control row to exhaustive edit parity unless the exhaustive inventory
itself binds an executed phone and tablet proof for that row.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import subprocess
from collections import Counter, defaultdict
from pathlib import Path
from typing import Any


REPO_ROOT = Path(__file__).resolve().parents[1]
DEFAULT_INVENTORY = REPO_ROOT / "docs" / "ANDROID_CHUMMER5_EDITABILITY_INVENTORY.generated.json"
DEFAULT_SETTINGS = REPO_ROOT / "docs" / "ANDROID_CHARACTER_SETTINGS_PHONE_CAPABILITIES.generated.json"
DEFAULT_WIZARD_GATE = REPO_ROOT / "eng" / "api36-sr5-wizard-gate-authority.json"
DEFAULT_OUTPUT = REPO_ROOT / "docs" / "ANDROID_CHUMMER5_EXHAUSTIVE_PARITY_GAP.generated.json"
ALLOWED_SURFACE_STATUSES = {
    "implemented_pending_emulator",
    "missing",
    "not_applicable_non_mutating",
    "partial_create_only",
    "partial_exact_saved_data",
}
ALLOWED_E2E_STATUSES = {
    "missing",
    "not_applicable_non_mutating",
    "not_applicable_phone_scope",
    "scripted_not_executed",
    "pass",
    "executed_pass",
}

QUALIFIED_FEATURES = (
    ("creation-prerequisite", "creation", "Only the prerequisite slice; not a completed Standard Priority character."),
    ("career-active-skill-advance", "career", "One active-skill quote/review/commit/reopen path."),
    ("career-weapon-fire", "playtime", "One direct weapon-fire/ammunition path."),
    ("before-run-edge", "before_run", "Bounded Edge spend/regain preparation."),
    ("playtime-short-burst", "playtime", "Bounded Edge/direct-fire short-burst path."),
    ("downtime-calendar", "downtime", "One calendar add/edit/delete persistence path."),
    ("after-run-settlement", "after_run", "One atomic settlement path."),
)

SOURCE_MARKERS: dict[str, tuple[str, ...]] = {
    "src/Chummer.Android/Native/CurrentPhoneWizardScope.cs": (
        "CharacterCreationBuildMethods.Priority",
        "CoversCreationStage",
        "MarkExperimental",
    ),
    "src/Chummer.Android/Native/CreationPrerequisitePhoneDraft.cs": (
        "CharacterCreationBuildMethods.SumToTen",
        "CharacterCreationBuildMethods.Priority",
    ),
    "src/Chummer.Android/Native/RunnerSessionCoordinator.cs": (
        "CharacterCreationBuildMethods.LifeModules",
        "ConfirmSr5LifeModuleOriginAsync",
    ),
    "src/Chummer.Android/Native/Sr5CareerWizardPhoneModel.cs": (
        "Sr5CareerWizardActionIds.AdvanceAttribute",
        "Sr5CareerWizardActionIds.AdvanceActiveSkill",
        "Sr5CareerWizardActionIds.AdvanceKnowledgeSkill",
        "Sr5CareerWizardActionIds.AdvanceSkillGroup",
        "Sr5CareerWizardActionIds.LearnSpecialization",
        "Sr5CareerWizardActionIds.ChangeQuality",
    ),
    "src/Chummer.Android/Native/Sr5CareerCyberwarePurchaseService.cs": (
        "Restart-safe Android orchestration",
        "modular cyberlimbs deliberately receive no mutation",
    ),
    "src/Chummer.Android/Native/Sr5CareerVehicleWorkshopService.cs": (
        "typed SR5 vehicle/drone workshop",
        "Core owns the catalog",
    ),
    "src/Chummer.Android/Native/Sr5CareerCustomDrugRecipeService.cs": (
        "Career-only custom-drug recipe",
        "quantity purchases and Creation finalization deliberately have no seam",
    ),
    "src/Chummer.Android/Native/TabletBuildPage.cs": (
        "tablet-build-navigation-pane",
        "tablet-build-collection-pane",
        "tablet-build-inspector-pane",
    ),
}


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def load_json(path: Path) -> dict[str, Any]:
    value = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(value, dict):
        raise ValueError(f"{path} is not a JSON object")
    return value


def git_value(*args: str) -> str:
    return subprocess.check_output(["git", "-C", str(REPO_ROOT), *args], text=True).strip()


def require_qualified_bytes(commit: str, paths: list[Path]) -> None:
    for path in paths:
        relative = path.resolve().relative_to(REPO_ROOT).as_posix()
        qualified = subprocess.check_output(
            ["git", "-C", str(REPO_ROOT), "show", f"{commit}:{relative}"]
        )
        if qualified != path.read_bytes():
            raise ValueError(f"{relative} differs from the audited qualified commit")


def verify_source_markers() -> list[dict[str, Any]]:
    bindings: list[dict[str, Any]] = []
    for relative, markers in SOURCE_MARKERS.items():
        path = REPO_ROOT / relative
        source = path.read_text(encoding="utf-8")
        missing = [marker for marker in markers if marker not in source]
        if missing:
            raise ValueError(f"{relative} is missing audited markers: {missing}")
        bindings.append({
            "path": relative,
            "sha256": sha256(path),
            "sizeBytes": path.stat().st_size,
        })
    return bindings


def classify_row(row: dict[str, Any], hidden_setting_controls: set[str]) -> str:
    legacy = row.get("legacy") or {}
    phone = row.get("phone") or {}
    tablet = row.get("tablet") or {}
    if legacy.get("mutationDisposition") != "mutating" or row.get("editParityRequired") is not True:
        return "not_applicable_non_mutating"
    if (
        legacy.get("formOrControl") == "EditCharacterSettings"
        and legacy.get("controlName") in hidden_setting_controls
    ):
        return "hidden_or_deferred"
    if row.get("completionProven") is True:
        return "typed_and_api36_proven"
    if phone.get("status") != "missing" or tablet.get("status") != "missing":
        return "implemented_unproven"
    return "missing"


def validate_row(row: dict[str, Any]) -> None:
    required = {
        "id",
        "legacy",
        "mutationFamily",
        "operation",
        "phone",
        "tablet",
        "presenterMutation",
        "persistenceAssertion",
        "e2e",
        "editParityRequired",
        "legacyReviewComplete",
        "overallStatus",
    }
    missing = sorted(required - row.keys())
    if missing:
        raise ValueError(f"inventory row is missing fields {missing}")
    disposition = row["legacy"].get("mutationDisposition")
    if disposition not in {"mutating", "non_mutating"}:
        raise ValueError(f"{row['id']} has unknown legacy mutation disposition {disposition!r}")
    if row.get("legacyReviewComplete") is not True:
        raise ValueError(f"{row['id']} is not legacy-review complete")
    if row.get("editParityRequired") is not (disposition == "mutating"):
        raise ValueError(f"{row['id']} edit-parity requirement contradicts legacy mutation disposition")
    for lane in ("phone", "tablet"):
        status = row[lane].get("status")
        if status not in ALLOWED_SURFACE_STATUSES:
            raise ValueError(f"{row['id']} has unknown {lane} status {status!r}")
        e2e_status = row["e2e"][lane].get("status")
        if e2e_status not in ALLOWED_E2E_STATUSES:
            raise ValueError(f"{row['id']} has unknown {lane} E2E status {e2e_status!r}")
    if row.get("completionProven") is True and row.get("editParityRequired") is True:
        mutation = row.get("presenterMutation")
        persistence = row.get("persistenceAssertion")
        phone = row["phone"]
        tablet = row["tablet"]
        if (
            row.get("overallStatus") != "complete"
            or row.get("legacyReviewComplete") is not True
            or not isinstance(mutation, str)
            or not mutation.strip()
            or not isinstance(persistence, str)
            or not persistence.strip()
            or phone.get("status") in {"missing", "not_applicable_non_mutating"}
            or tablet.get("status") in {"missing", "not_applicable_non_mutating"}
            or any(
                not isinstance(phone.get(key), str) or not phone[key].strip()
                for key in ("route", "surface", "automationId")
            )
            or any(
                not isinstance(tablet.get(key), str) or not tablet[key].strip()
                for key in ("surface", "automationId")
            )
            or not phone.get("sourceRefs")
            or not tablet.get("sourceRefs")
            or any(
                row["e2e"][lane].get("status") not in {"pass", "executed_pass"}
                or not isinstance(row["e2e"][lane].get("ref"), str)
                or not row["e2e"][lane]["ref"].strip()
                for lane in ("phone", "tablet")
            )
        ):
            raise ValueError(f"{row['id']} claims mutating completion without two-lane executed proof")


def validate_two_green(
    receipt: dict[str, Any],
    commit: str,
    tree: str,
    expected_journeys: list[str],
) -> None:
    if receipt.get("schema") != "chummer.android.api36-ordered-review-main-green-eligibility/v2":
        raise ValueError("unexpected two-green schema")
    if (
        receipt.get("status") != "pass"
        or receipt.get("eligible") is not True
        or receipt.get("internalTestingEligible") is not True
    ):
        raise ValueError("two-green evidence is not eligible")
    if receipt.get("publicationAuthorized") is not False or receipt.get("googlePlayUploadAuthorized") is not False:
        raise ValueError("two-green receipt must not authorize publication or Play upload")
    if receipt.get("sourceCommit") != commit or receipt.get("sourceTree") != tree:
        raise ValueError("two-green evidence does not bind the audited qualified commit/tree")
    if receipt.get("commonAuthority", {}).get("requiredJourneys") != expected_journeys:
        raise ValueError("two-green journey set is not exact")
    for lane in ("reviewRun", "mainRun"):
        evidence = receipt.get(lane)
        if not isinstance(evidence, dict) or evidence.get("aggregateStatus") != "pass":
            raise ValueError(f"two-green {lane} aggregate is not pass")
        run = evidence.get("run")
        if (
            not isinstance(run, dict)
            or run.get("status") != "completed"
            or run.get("conclusion") != "success"
            or not isinstance(run.get("id"), int)
        ):
            raise ValueError(f"two-green {lane} workflow is not terminal success")
        jobs = evidence.get("jobs")
        if not isinstance(jobs, dict) or not jobs:
            raise ValueError(f"two-green {lane} jobs are missing")
        if any(
            not isinstance(job, dict)
            or job.get("status") != "completed"
            or job.get("conclusion") != "success"
            for job in jobs.values()
        ):
            raise ValueError(f"two-green {lane} contains a non-success job")
    review_run = receipt["reviewRun"]["run"]
    main_run = receipt["mainRun"]["run"]
    if review_run["id"] == main_run["id"]:
        raise ValueError("two-green review and main run IDs are not distinct")
    if review_run.get("event") != "pull_request":
        raise ValueError("two-green review run is not a pull-request run")
    if (
        main_run.get("event") != "push"
        or main_run.get("ref") != "refs/heads/main"
        or main_run.get("headSha") != commit
    ):
        raise ValueError("two-green main run is not the exact qualified main push")


def validate_inventory_summary(inventory: dict[str, Any]) -> None:
    source_rows = inventory.get("rows")
    summary = inventory.get("summary")
    if not isinstance(source_rows, list) or not isinstance(summary, dict):
        raise ValueError("editability inventory rows or summary are missing")
    mutating = sum(
        1 for row in source_rows
        if row.get("legacy", {}).get("mutationDisposition") == "mutating"
    )
    non_mutating = sum(
        1 for row in source_rows
        if row.get("legacy", {}).get("mutationDisposition") == "non_mutating"
    )
    if len(source_rows) != int(summary.get("rowCount", -1)):
        raise ValueError("editability inventory row count is inconsistent")
    if mutating != int(summary.get("editParityRequiredCount", -1)):
        raise ValueError("editability inventory mutating count is inconsistent")
    if non_mutating != int(summary.get("reviewedNonMutatingCount", -1)):
        raise ValueError("editability inventory non-mutating count is inconsistent")
    if mutating + non_mutating != len(source_rows):
        raise ValueError("editability inventory contains an unknown mutation disposition")


def feature_slices(settings: dict[str, Any]) -> list[dict[str, Any]]:
    visible = int(settings["summary"]["visibleEditableCount"])
    hidden = int(settings["summary"]["hiddenPreservedCount"])
    return [
        {
            "id": "creation.standard_priority",
            "classification": "implemented_unproven",
            "detail": "Priority is the only current Preview creation method, but the API-36 gate proves only its prerequisite slice, not an empty-workspace-to-Career finalization.",
        },
        {
            "id": "creation.sum_to_ten",
            "classification": "hidden_or_deferred",
            "detail": "Typed prerequisite handling exists, but CurrentPhoneWizardScope excludes it and no complete lifecycle proof is bound.",
        },
        {
            "id": "creation.karma",
            "classification": "missing",
            "detail": "No current native creation-method route/authority was found in the audited phone scope.",
        },
        {
            "id": "creation.life_modules_origin_dossier",
            "classification": "hidden_or_deferred",
            "detail": "Life-Module/Origin runtime code exists but is outside CurrentPhoneWizardScope and the seven-journey gate.",
        },
        {
            "id": "career.active_skill",
            "classification": "typed_and_api36_proven",
            "journey": "career-active-skill-advance",
        },
        {
            "id": "career.weapon_fire",
            "classification": "typed_and_api36_proven",
            "journey": "career-weapon-fire",
        },
        {
            "id": "career.before_run_edge",
            "classification": "typed_and_api36_proven",
            "journey": "before-run-edge",
        },
        {
            "id": "career.playtime_short_burst",
            "classification": "typed_and_api36_proven",
            "journey": "playtime-short-burst",
        },
        {
            "id": "career.downtime_calendar",
            "classification": "typed_and_api36_proven",
            "journey": "downtime-calendar",
        },
        {
            "id": "career.after_run_settlement",
            "classification": "typed_and_api36_proven",
            "journey": "after-run-settlement",
        },
        {
            "id": "career.attributes_skill_groups_knowledge_qualities_specializations_economy",
            "classification": "implemented_unproven",
            "detail": "Typed phone routes exist, but they are not members of the current exact API-36 gate.",
        },
        {
            "id": "gear.general_and_nested",
            "classification": "implemented_unproven",
            "detail": "Many generic gear/nested mutations are surfaced, but exhaustive coverage and API-36 row binding are absent; purchase breadth remains incomplete.",
        },
        {
            "id": "cyberware.root_purchase",
            "classification": "implemented_unproven",
            "detail": "A bounded restart-safe root purchase flow exists outside the current API-36 gate.",
        },
        {
            "id": "cyberware.modular_limbs_descendants_bioware",
            "classification": "missing",
            "detail": "The current bounded Cyberware service explicitly excludes descendants, prompts, mounts, modular cyberlimbs, Gear purchase, and Bioware.",
        },
        {
            "id": "vehicles_drones.root_workshop",
            "classification": "implemented_unproven",
            "detail": "Typed vehicle/drone workshop code exists, but is outside the seven-journey API-36 gate.",
        },
        {
            "id": "vehicles_drones.saved_item_edits",
            "classification": "implemented_unproven",
            "detail": "Some exact saved Vehicle/Weapon/Matrix/location edits exist, but exhaustive row-level proof is absent.",
        },
        {
            "id": "vehicle_mods_and_mounts.full_customization",
            "classification": "missing",
            "detail": "All 48 reviewed data-changing legacy controls in this family remain missing in the exhaustive inventory.",
        },
        {
            "id": "drugs.custom_recipe",
            "classification": "implemented_unproven",
            "detail": "One Career-only recipe plus free initial dose is implemented outside the current API-36 gate.",
        },
        {
            "id": "drugs.quantity_purchase_and_creation",
            "classification": "missing",
            "detail": "The bounded custom-drug service explicitly excludes later quantity purchases and Creation finalization.",
        },
        {
            "id": "character_settings.visible_wizard_inputs",
            "classification": "implemented_unproven",
            "controlCount": visible,
            "detail": "Visible only where a current wizard consumes the value; no exhaustive settings journey is in the gate.",
        },
        {
            "id": "character_settings.hidden_preserved",
            "classification": "hidden_or_deferred",
            "controlCount": hidden,
            "detail": "Saved profile values are preserved but intentionally not editable on the current phone surface.",
        },
        {
            "id": "account_link_and_application_state",
            "classification": "implemented_unproven",
            "detail": "Native account/file/settings surfaces exist, but the seven-journey receipt does not prove them; account-key/token/response-bound hardening remains independent work.",
        },
        {
            "id": "tablet.master_detail_shell",
            "classification": "implemented_unproven",
            "detail": "A purpose-composed navigation/collection/inspector shell exists, but no tablet API-36 authority is bound.",
        },
        {
            "id": "tablet.exhaustive_data_changing_parity",
            "classification": "missing",
            "detail": "The exhaustive inventory still reports most required tablet controls missing and none completed.",
        },
    ]


def build(
    inventory_path: Path,
    settings_path: Path,
    wizard_gate_path: Path,
    two_green_path: Path,
    qualified_commit: str,
) -> dict[str, Any]:
    inventory = load_json(inventory_path)
    settings = load_json(settings_path)
    gate = load_json(wizard_gate_path)
    two_green = load_json(two_green_path)

    if inventory.get("schema") != "chummer.android.chummer5-editability-inventory/v1":
        raise ValueError("unexpected editability inventory schema")
    if inventory.get("completionProven") is not False:
        raise ValueError("exhaustive inventory unexpectedly claims completion")
    validate_inventory_summary(inventory)
    if settings.get("schema") != "chummer.android.character-settings-phone-capabilities/v1":
        raise ValueError("unexpected phone settings capability schema")
    if gate.get("schema") != "chummer.android.api36-sr5-wizard-gate-authority/v1":
        raise ValueError("unexpected wizard gate schema")
    commit = git_value("rev-parse", f"{qualified_commit}^{{commit}}")
    tree = git_value("show", "-s", "--format=%T", commit)

    expected_journeys = [row[0] for row in QUALIFIED_FEATURES]
    gate_journeys = [row["matrixJourney"] for row in gate.get("requiredJourneys", [])]
    if gate_journeys != expected_journeys:
        raise ValueError("qualified wizard journey set is not exact")
    validate_two_green(two_green, commit, tree, expected_journeys)

    audited_paths = [
        inventory_path,
        settings_path,
        wizard_gate_path,
        *(REPO_ROOT / relative for relative in SOURCE_MARKERS),
    ]
    require_qualified_bytes(commit, audited_paths)

    hidden_settings = {
        row["legacyControl"]
        for row in settings.get("controls", [])
        if row.get("phoneStatus") == "hidden_preserved"
    }
    if len(hidden_settings) != int(settings["summary"]["hiddenPreservedCount"]):
        raise ValueError("hidden Character Settings capability count is inconsistent")
    visible_settings = {
        row["legacyControl"]
        for row in settings.get("controls", [])
        if row.get("phoneStatus") == "visible_editable"
    }
    if len(visible_settings) != int(settings["summary"]["visibleEditableCount"]):
        raise ValueError("visible Character Settings capability count is inconsistent")
    if hidden_settings & visible_settings:
        raise ValueError("Character Settings controls cannot be both visible and hidden")
    unknown_settings = {
        row.get("phoneStatus") for row in settings.get("controls", [])
    } - {"hidden_preserved", "visible_editable"}
    if unknown_settings:
        raise ValueError(f"unknown Character Settings phone statuses: {sorted(unknown_settings)}")
    rows: list[dict[str, Any]] = []
    counts: Counter[str] = Counter()
    family_counts: dict[str, Counter[str]] = defaultdict(Counter)
    non_mutating = 0
    seen_row_ids: set[str] = set()
    for source_row in inventory.get("rows", []):
        validate_row(source_row)
        row_id = source_row["id"]
        if row_id in seen_row_ids:
            raise ValueError(f"duplicate exhaustive inventory row ID: {row_id}")
        seen_row_ids.add(row_id)
        classification = classify_row(source_row, hidden_settings)
        if classification == "not_applicable_non_mutating":
            non_mutating += 1
            continue
        legacy = source_row["legacy"]
        phone = source_row["phone"]
        tablet = source_row["tablet"]
        e2e = source_row["e2e"]
        row = {
            "id": row_id,
            "family": source_row["mutationFamily"],
            "operation": source_row["operation"],
            "classification": classification,
            "legacyControl": legacy["controlName"],
            "phoneStatus": phone["status"],
            "phoneRoute": phone.get("route"),
            "phoneSurface": phone.get("surface"),
            "phoneE2eStatus": e2e["phone"]["status"],
            "tabletStatus": tablet["status"],
            "tabletSurface": tablet.get("surface"),
            "tabletE2eStatus": e2e["tablet"]["status"],
            "presenterMutation": source_row.get("presenterMutation"),
            "completionProven": source_row.get("completionProven") is True,
        }
        rows.append(row)
        counts[classification] += 1
        family_counts[row["family"]][classification] += 1

    expected_mutating = int(inventory["summary"]["editParityRequiredCount"])
    if len(rows) != expected_mutating:
        raise ValueError(f"classified {len(rows)} mutating rows, expected {expected_mutating}")

    source_bindings = [
        {"path": str(path.relative_to(REPO_ROOT)), "sha256": sha256(path), "sizeBytes": path.stat().st_size}
        for path in (inventory_path, settings_path, wizard_gate_path)
    ]
    source_bindings.extend(verify_source_markers())

    next_slices = [
        {
            "order": 1,
            "id": "sr5-standard-priority-full-lifecycle",
            "scope": "New empty workspace through every required Priority step, atomic finalization, Career projection, save/reopen/process restart on the exact APK.",
        },
        {
            "order": 2,
            "id": "sr5-career-remaining-typed-wizards",
            "scope": "Attributes, skill groups, Knowledge/Language, qualities, specializations, manual Karma/Nuyen and expense edits as isolated typed API-36 journeys.",
        },
        {
            "order": 3,
            "id": "gear-cyberware-bioware-modular-limbs",
            "scope": "Complete purchase/removal/descendant/capacity/Essence/grade/modular-limb flows with atomic economic receipts and recovery.",
        },
        {
            "order": 4,
            "id": "vehicles-drones-mods-mounts",
            "scope": "Complete chassis, drone, mod, mount, weapon/accessory and capacity legality with exact identity and restart proof.",
        },
        {
            "order": 5,
            "id": "custom-drug-complete-lifecycle",
            "scope": "Recipe plus subsequent dose purchases, quantity/economics and Creation integration.",
        },
        {
            "order": 6,
            "id": "settings-account-application-state",
            "scope": "Only meaningful phone settings, profile preservation, file/account operations, secure key/token/response handling and API-36 persistence proofs.",
        },
        {
            "order": 7,
            "id": "tablet-purpose-composed-exhaustive-parity",
            "scope": "Master/list/detail composition per mutation family with tablet-specific API-36 edit/save/reopen/process-restart receipts.",
        },
    ]

    family_summary = {
        family: {key: counter.get(key, 0) for key in (
            "typed_and_api36_proven",
            "implemented_unproven",
            "hidden_or_deferred",
            "missing",
        )}
        for family, counter in sorted(family_counts.items())
    }
    return {
        "schema": "chummer.android.chummer5-exhaustive-parity-gap/v1",
        "status": "incomplete_fail_closed",
        "auditScope": "every reviewed Chummer5 UI control that can change runner or application data",
        "qualifiedSource": {
            "commit": commit,
            "tree": tree,
            "releaseIdentity": two_green["releaseIdentity"],
        },
        "currentReleaseBoundary": {
            "authorityClass": gate["authorityClass"],
            "proofScope": gate["proofScope"],
            "requiredJourneys": expected_journeys,
            "twoGreenReceiptSha256": sha256(two_green_path),
            "twoGreenStatus": two_green["status"],
            "reviewRunId": two_green["reviewRun"]["run"]["id"],
            "reviewAggregateStatus": two_green["reviewRun"]["aggregateStatus"],
            "mainRunId": two_green["mainRun"]["run"]["id"],
            "mainAggregateStatus": two_green["mainRun"]["aggregateStatus"],
            "publicationAuthorized": False,
            "doesNotAssert": gate["doesNotAssert"],
        },
        "classificationPolicy": {
            "typed_and_api36_proven": "The exhaustive row itself binds typed mutation plus executed phone and tablet edit/save/reopen/process-restart proof. Wizard-only feature slices are listed separately and never promote a legacy row implicitly.",
            "implemented_unproven": "A concrete phone/tablet implementation or partial saved-data seam exists, but exhaustive executed proof is absent.",
            "hidden_or_deferred": "The legacy mutation is intentionally preserved but hidden/deferred by the current phone capability policy.",
            "missing": "Neither phone nor tablet has a concrete implementation entry for the required mutation.",
        },
        "summary": {
            "legacyControlCount": int(inventory["summary"]["rowCount"]),
            "dataChangingControlCount": len(rows),
            "reviewedNonMutatingControlCount": non_mutating,
            "typedAndApi36ProvenRowCount": counts["typed_and_api36_proven"],
            "implementedUnprovenRowCount": counts["implemented_unproven"],
            "hiddenOrDeferredRowCount": counts["hidden_or_deferred"],
            "missingRowCount": counts["missing"],
            "exhaustiveParityComplete": False,
            "phoneCompletionProven": False,
            "tabletCompletionProven": False,
        },
        "qualifiedWizardFeatureSlices": [
            {"journey": journey, "family": family, "classification": "typed_and_api36_proven", "limit": limit}
            for journey, family, limit in QUALIFIED_FEATURES
        ],
        "featureSlices": feature_slices(settings),
        "familySummary": family_summary,
        "nextImplementationSlices": next_slices,
        "sourceBindings": source_bindings,
        "rows": rows,
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--inventory", type=Path, default=DEFAULT_INVENTORY)
    parser.add_argument("--settings", type=Path, default=DEFAULT_SETTINGS)
    parser.add_argument("--wizard-gate", type=Path, default=DEFAULT_WIZARD_GATE)
    parser.add_argument("--two-green-receipt", type=Path, required=True)
    parser.add_argument("--qualified-commit", default="refs/remotes/origin/main")
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()

    payload = build(
        args.inventory.resolve(),
        args.settings.resolve(),
        args.wizard_gate.resolve(),
        args.two_green_receipt.resolve(),
        args.qualified_commit,
    )
    rendered = json.dumps(payload, indent=2, ensure_ascii=False, sort_keys=True) + "\n"
    if args.check:
        if not args.output.is_file() or args.output.read_text(encoding="utf-8") != rendered:
            raise SystemExit("exhaustive parity gap receipt is stale")
        return 0
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(rendered, encoding="utf-8")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
