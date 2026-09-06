#!/usr/bin/env python3
"""Materialize the current phone capability boundary for Character Settings.

The Chummer5 settings contract is intentionally exhaustive. The Android phone
surface is narrower: a control is editable only when a current phone wizard reads the
saved value.  Everything else remains in the profile XML but is hidden on the phone.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import sys
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[1]
DEFAULT_COMPLETE_ROOT = Path(
    os.environ.get("CHUMMER_COMPLETE_ROOT", "/docker/chummercomplete")
).resolve()
DEFAULT_CONTRACT = (
    REPO_ROOT / "docs" / "CHUMMER5_CHARACTER_SETTINGS_CONTRACT.generated.json"
)
DEFAULT_JSON_OUTPUT = (
    REPO_ROOT
    / "docs"
    / "ANDROID_CHARACTER_SETTINGS_PHONE_CAPABILITIES.generated.json"
)
DEFAULT_RUNTIME_OUTPUT = (
    REPO_ROOT
    / "src"
    / "Chummer.Android"
    / "Native"
    / "AndroidCharacterSettingsPhoneCapabilities.Generated.cs"
)
DEFAULT_TEST_OUTPUT = (
    REPO_ROOT
    / "tests"
    / "Chummer.Android.Native.InteractionTests"
    / "CharacterSettingsPhoneCapabilityInventory.Generated.cs"
)

RUNTIME_FIELD_RE = re.compile(
    r'^\s*new\("(?P<control>[^"]+)",\s*"(?:[^"\\]|\\.)*",\s*'
    r'"(?P<section>[^"]+)",\s*"(?P<input>[^"]+)",\s*'
    r'(?P<multiline>true|false),'
)

# Each entry is an observed current phone behavior, not a future intent. Evidence
# names the current projector or coordinator path that reads the persisted value.
SUPPORTED: dict[str, dict[str, str]] = {
    "cboBuildMethod": {
        "behavior": "selects the preferred build method for the phone creation wizard",
        "evidence": "DialogCoordinator.ApplyCharacterSettings -> DesktopPreferenceState.CharacterPriority -> DesktopDialogFactory.BuildNewCharacterDialog",
        "labelKey": "CharacterSettingBuildMethod",
        "label": "Creation build method",
    },
    "chkDontUseCyberlimbCalculation": {
        "behavior": "changes cyberlimb participation in phone career attribute totals",
        "evidence": "CareerAttributeAdvanceEditorProjector.ProjectFacts -> HasApplicableCyberlimb",
        "labelKey": "CharacterSettingDontUseCyberlimbCalculation",
        "label": "Ignore cyberlimbs in augmented attribute totals",
    },
    "cboLimbCount": {
        "behavior": "changes which cyberlimb slots participate in phone career attribute totals",
        "evidence": "CareerAttributeAdvanceEditorProjector.HasApplicableCyberlimb -> settings/excludelimbslot",
        "labelKey": "CharacterSettingCyberlimbConfiguration",
        "label": "Cyberlimb slots used for attribute totals",
    },
    "nudKarmaNewActiveSkill": {
        "behavior": "sets the phone career cost for learning an active skill",
        "evidence": "CareerActiveSkillAdvanceEditorProjector.ProjectState",
        "labelKey": "CharacterSettingKarmaNewActiveSkill",
        "label": "Karma cost: new active skill",
    },
    "nudKarmaImproveActiveSkill": {
        "behavior": "sets the phone career cost multiplier for improving an active skill",
        "evidence": "CareerActiveSkillAdvanceEditorProjector.ProjectState",
        "labelKey": "CharacterSettingKarmaImproveActiveSkill",
        "label": "Karma multiplier: improve active skill",
    },
    "nudKarmaNewSkillGroup": {
        "behavior": "sets the phone career cost for learning a skill group",
        "evidence": "CareerActiveSkillAdvanceEditorProjector.ProjectState and CareerSkillGroupAdvanceEditorProjector.ProjectState",
        "labelKey": "CharacterSettingKarmaNewSkillGroup",
        "label": "Karma cost: new skill group",
    },
    "nudKarmaImproveSkillGroup": {
        "behavior": "sets the phone career cost multiplier for improving a skill group",
        "evidence": "CareerActiveSkillAdvanceEditorProjector.ProjectState and CareerSkillGroupAdvanceEditorProjector.ProjectState",
        "labelKey": "CharacterSettingKarmaImproveSkillGroup",
        "label": "Karma multiplier: improve skill group",
    },
    "chkCompensateSkillGroupKarmaDifference": {
        "behavior": "changes phone career active-skill cost when a linked group has a different rating",
        "evidence": "CareerActiveSkillAdvanceEditorProjector.ProjectState",
        "labelKey": "CharacterSettingCompensateSkillGroupKarmaDifference",
        "label": "Compensate skill-group Karma difference",
    },
    "nudKarmaAttribute": {
        "behavior": "sets the phone career cost multiplier for improving an attribute",
        "evidence": "CareerAttributeAdvanceEditorProjector.ProjectFacts",
        "labelKey": "CharacterSettingKarmaAttribute",
        "label": "Karma multiplier: improve attribute",
    },
    "chkAlternateMetatypeAttributeKarma": {
        "behavior": "changes metatype-adjusted phone career attribute costs",
        "evidence": "CareerAttributeAdvanceEditorProjector.ProjectFacts",
        "labelKey": "CharacterSettingAlternateMetatypeAttributeKarma",
        "label": "Use alternate metatype attribute Karma",
    },
    "chkUnclampAttributeMinimum": {
        "behavior": "changes minimum values used by the phone career attribute projector",
        "evidence": "CareerAttributeAdvanceEditorProjector.ProjectFacts -> ResolveTotalMinimum",
        "labelKey": "CharacterSettingUnclampAttributeMinimum",
        "label": "Allow attributes below the normal minimum",
    },
    "chkMysAdeptSecondMAGAttribute": {
        "behavior": "changes second-Magic handling in phone career attribute advancement",
        "evidence": "CareerAttributeAdvanceEditorProjector.ProjectFacts",
        "labelKey": "CharacterSettingMysticAdeptSecondMagicAttribute",
        "label": "Use a second Magic attribute for mystic adepts",
    },
    "nudKarmaNewKnowledgeSkill": {
        "behavior": "sets the phone career cost for learning a knowledge skill",
        "evidence": "CareerKnowledgeSkillAdvanceEditorProjector.ProjectState",
        "labelKey": "CharacterSettingKarmaNewKnowledgeSkill",
        "label": "Karma cost: new knowledge skill",
    },
    "nudKarmaImproveKnowledgeSkill": {
        "behavior": "sets the phone career cost multiplier for improving a knowledge skill",
        "evidence": "CareerKnowledgeSkillAdvanceEditorProjector.ProjectState",
        "labelKey": "CharacterSettingKarmaImproveKnowledgeSkill",
        "label": "Karma multiplier: improve knowledge skill",
    },
    "nudMaxSkillRating": {
        "behavior": "caps active-skill and skill-group ratings in phone career advancement",
        "evidence": "CareerActiveSkillAdvanceEditorProjector.ProjectState, CareerSkillGroupAdvanceEditorProjector.ProjectState, and CareerSkillSpecializationEditorProjector.ProjectState",
        "labelKey": "CharacterSettingMaxSkillRating",
        "label": "Maximum career active-skill rating",
    },
    "nudMaxKnowledgeSkillRating": {
        "behavior": "caps knowledge-skill ratings in phone career advancement",
        "evidence": "CareerKnowledgeSkillAdvanceEditorProjector.ProjectState",
        "labelKey": "CharacterSettingMaxKnowledgeSkillRating",
        "label": "Maximum career knowledge-skill rating",
    },
    "chkUsePointsOnBrokenGroups": {
        "behavior": "changes phone career active-skill, group, and specialization eligibility",
        "evidence": "CareerActiveSkillAdvanceEditorProjector.ProjectState, CareerSkillGroupAdvanceEditorProjector.ProjectState, and CareerSkillSpecializationEditorProjector.ProjectState",
        "labelKey": "CharacterSettingUsePointsOnBrokenGroups",
        "label": "Allow points on broken skill groups",
    },
}


def _sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def _cs(value: str) -> str:
    return json.dumps(value, ensure_ascii=False)


def _runtime_fields(path: Path) -> list[dict[str, object]]:
    rows: list[dict[str, object]] = []
    for line in path.read_text(encoding="utf-8").splitlines():
        match = RUNTIME_FIELD_RE.match(line)
        if match is None:
            continue
        rows.append(
            {
                "legacyControl": match.group("control"),
                "fieldId": "characterSettingsControl-" + match.group("control"),
                "sectionId": match.group("section"),
                "inputType": match.group("input"),
                "isMultiline": match.group("multiline") == "true",
            }
        )
    if not rows:
        raise ValueError(f"no generated Character Settings fields found in {path}")
    return rows


def _build(
    complete_root: Path,
    contract_path: Path,
) -> tuple[dict[str, object], str, str]:
    presentation_root = complete_root / "chummer-presentation"
    runtime_contract_path = (
        presentation_root
        / "Chummer.Presentation"
        / "Overview"
        / "Chummer5CharacterSettingsRuntimeContract.Generated.cs"
    )
    evidence_paths = [
        presentation_root / "Chummer.Presentation" / "Overview" / name
        for name in (
            "CareerActiveSkillAdvanceEditRequest.cs",
            "CareerAttributeAdvanceEditRequest.cs",
            "CareerKnowledgeSkillAdvanceEditRequest.cs",
            "CareerSkillGroupAdvanceEditRequest.cs",
            "CareerSkillSpecializationEditRequest.cs",
            "DesktopDialogFactory.cs",
            "DialogCoordinator.cs",
        )
    ]
    source_paths = [
        Path(__file__).resolve(),
        contract_path,
        runtime_contract_path,
        REPO_ROOT / "src" / "Chummer.Android" / "Native" / "NativeDialogPage.cs",
        *evidence_paths,
    ]
    for path in source_paths:
        if not path.is_file():
            raise FileNotFoundError(path)

    contract = json.loads(contract_path.read_text(encoding="utf-8"))
    contract_controls = {
        row["legacyControl"]: row for row in contract.get("controls", [])
    }
    runtime_fields = _runtime_fields(runtime_contract_path)
    runtime_controls = {row["legacyControl"] for row in runtime_fields}
    expected_runtime_controls = {
        row["legacyControl"]
        for row in contract.get("controls", [])
        if row["legacyControl"] != "cboSetting"
        and (
            row["operation"] == "set_value"
            or row["semanticOperation"]
            in {"edit_sourcebooks", "edit_custom_data_directories"}
        )
    }
    if runtime_controls != expected_runtime_controls:
        missing = sorted(expected_runtime_controls - runtime_controls)
        extra = sorted(runtime_controls - expected_runtime_controls)
        raise ValueError(
            f"runtime/contract control drift; missing={missing}, extra={extra}"
        )
    if set(SUPPORTED) - runtime_controls:
        raise ValueError(
            "supported phone controls missing from runtime contract: "
            + ", ".join(sorted(set(SUPPORTED) - runtime_controls))
        )

    controls: list[dict[str, object]] = []
    for field in runtime_fields:
        legacy_control = str(field["legacyControl"])
        capability = SUPPORTED.get(legacy_control)
        contract_row = contract_controls[legacy_control]
        row = {
            **field,
            "persistencePaths": contract_row["persistencePaths"],
            "phoneStatus": "visible_editable" if capability else "hidden_preserved",
            "androidBehavior": capability["behavior"] if capability else None,
            "behaviorEvidence": capability["evidence"] if capability else None,
            "labelResourceKey": capability["labelKey"] if capability else None,
            "englishLabel": capability["label"] if capability else None,
            "rationale": (
                "The persisted value is read by a current Android phone wizard."
                if capability
                else "No current Android phone wizard reads this catalog value; Android keeps the imported XML value unchanged."
            ),
        }
        controls.append(row)

    visible = [row for row in controls if row["phoneStatus"] == "visible_editable"]
    hidden = [row for row in controls if row["phoneStatus"] == "hidden_preserved"]
    payload: dict[str, object] = {
        "schema": "chummer.android.character-settings-phone-capabilities/v1",
        "scope": "current_phone_wizard_only",
        "policy": "A value control is visible only when current Android code reads it into a current creation or career phone wizard. Hidden controls remain in profile XML and are never cleared by projection.",
        "summary": {
            "valueControlCount": len(controls),
            "visibleEditableCount": len(visible),
            "hiddenPreservedCount": len(hidden),
            "visibleSectionCount": len({row["sectionId"] for row in visible}),
        },
        "sourceInputs": [
            {
                "path": (
                    path.relative_to(REPO_ROOT).as_posix()
                    if path.is_relative_to(REPO_ROOT)
                    else (
                        Path("chummer-presentation")
                        / path.relative_to(presentation_root)
                    ).as_posix()
                ),
                "sha256": _sha256(path),
            }
            for path in source_paths
        ],
        "controls": controls,
    }

    runtime_lines = [
        "// <auto-generated />",
        "// Generated by chummer-android/scripts/materialize_android_character_settings_phone_capabilities.py.",
        "#nullable enable",
        "using System.Globalization;",
        "",
        "namespace Chummer.Android.Native;",
        "",
        "internal sealed record AndroidCharacterSettingCapability(",
        "    string LegacyControl,",
        "    string FieldId,",
        "    string SectionId,",
        "    string InputType,",
        "    bool IsMultiline,",
        "    string AndroidBehavior,",
        "    string LabelResourceKey,",
        "    string EnglishLabel);",
        "",
        "internal static class AndroidCharacterSettingsPhoneCapabilities",
        "{",
        "    internal static IReadOnlyList<AndroidCharacterSettingCapability> Supported { get; } =",
        "    [",
    ]
    by_control = {row["legacyControl"]: row for row in runtime_fields}
    for legacy_control, capability in SUPPORTED.items():
        field = by_control[legacy_control]
        runtime_lines.append(
            "        new("
            + ", ".join(
                (
                    _cs(legacy_control),
                    _cs(str(field["fieldId"])),
                    _cs(str(field["sectionId"])),
                    _cs(str(field["inputType"])),
                    str(field["isMultiline"]).lower(),
                    _cs(capability["behavior"]),
                    _cs(capability["labelKey"]),
                    _cs(capability["label"]),
                )
            )
            + "),"
        )
    runtime_lines.extend(
        [
            "    ];",
            "",
            "    private static readonly IReadOnlyDictionary<string, AndroidCharacterSettingCapability> ByFieldId =",
            "        Supported.ToDictionary(capability => capability.FieldId, StringComparer.Ordinal);",
            "",
            "    internal static IReadOnlySet<string> SupportedSectionIds { get; } = Supported",
            "        .Select(capability => capability.SectionId)",
            "        .ToHashSet(StringComparer.Ordinal);",
            "",
            "    internal static bool TryGet(string fieldId, out AndroidCharacterSettingCapability capability)",
            "        => ByFieldId.TryGetValue(fieldId, out capability!);",
            "",
            "    internal static string LocalizeLabel(AndroidCharacterSettingCapability capability, CultureInfo? culture)",
            "        => PhoneStrings.Get(capability.LabelResourceKey, capability.EnglishLabel, culture);",
            "}",
            "",
        ]
    )

    test_lines = [
        "// <auto-generated />",
        "// Generated by chummer-android/scripts/materialize_android_character_settings_phone_capabilities.py.",
        "#nullable enable",
        "internal sealed record CharacterSettingsPhoneCapabilityInventoryEntry(",
        "    string LegacyControl,",
        "    string FieldId,",
        "    string SectionId,",
        "    string InputType,",
        "    bool IsMultiline,",
        "    bool IsVisible,",
        "    string? AndroidBehavior);",
        "",
        "internal static class CharacterSettingsPhoneCapabilityInventoryGenerated",
        "{",
        "    internal static IReadOnlyList<CharacterSettingsPhoneCapabilityInventoryEntry> Entries { get; } =",
        "    [",
    ]
    for row in controls:
        test_lines.append(
            "        new("
            + ", ".join(
                (
                    _cs(str(row["legacyControl"])),
                    _cs(str(row["fieldId"])),
                    _cs(str(row["sectionId"])),
                    _cs(str(row["inputType"])),
                    str(row["isMultiline"]).lower(),
                    str(row["phoneStatus"] == "visible_editable").lower(),
                    "null"
                    if row["androidBehavior"] is None
                    else _cs(str(row["androidBehavior"])),
                )
            )
            + "),"
        )
    test_lines.extend(["    ];", "}", ""])
    return payload, "\n".join(runtime_lines), "\n".join(test_lines)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--complete-root", type=Path, default=DEFAULT_COMPLETE_ROOT)
    parser.add_argument("--contract", type=Path, default=DEFAULT_CONTRACT)
    parser.add_argument("--json-output", type=Path, default=DEFAULT_JSON_OUTPUT)
    parser.add_argument("--runtime-output", type=Path, default=DEFAULT_RUNTIME_OUTPUT)
    parser.add_argument("--test-output", type=Path, default=DEFAULT_TEST_OUTPUT)
    parser.add_argument("--check", action="store_true")
    arguments = parser.parse_args()
    try:
        payload, runtime_source, test_source = _build(
            arguments.complete_root.resolve(), arguments.contract.resolve()
        )
    except (FileNotFoundError, ValueError, KeyError, json.JSONDecodeError, OSError) as error:
        print(f"Android Character Settings capability inventory failed: {error}", file=sys.stderr)
        return 1

    outputs = {
        arguments.json_output.resolve(): json.dumps(payload, indent=2) + "\n",
        arguments.runtime_output.resolve(): runtime_source,
        arguments.test_output.resolve(): test_source,
    }
    if arguments.check:
        stale = [
            path
            for path, rendered in outputs.items()
            if not path.is_file() or path.read_text(encoding="utf-8") != rendered
        ]
        if stale:
            for path in stale:
                print(f"Android Character Settings capability inventory is stale: {path}", file=sys.stderr)
            return 1
        print("Android Character Settings capability inventory is current")
        return 0

    for path, rendered in outputs.items():
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(rendered, encoding="utf-8")
    print(json.dumps(payload["summary"], indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
