#!/usr/bin/env python3
"""Generate the exact Chummer5 EditCharacterSettings persistence contract.

The generated contract is an implementation input, not parity evidence. It keeps the
phone editor fail-closed by requiring every legacy editable control to resolve to a
profile operation or one or more paths in the Chummer5 settings XML document.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path
from typing import Iterable


REPO_ROOT = Path(__file__).resolve().parents[1]
DEFAULT_CHUMMER5_ROOT = Path(
    os.environ.get("CHUMMER5A_ROOT", "/docker/chummer5a")
).resolve()
DEFAULT_INVENTORY = (
    REPO_ROOT / "docs" / "ANDROID_CHUMMER5_EDITABILITY_INVENTORY.generated.json"
)
DEFAULT_OUTPUT = (
    REPO_ROOT / "docs" / "CHUMMER5_CHARACTER_SETTINGS_CONTRACT.generated.json"
)

REGISTER_CALL_RE = re.compile(
    r"\b(?P<control>[A-Za-z_][A-Za-z0-9_]*)\s*\.\s*"
    r"(?P<method>Register[A-Za-z0-9_]*DataBinding[A-Za-z0-9_]*)\s*\("
)
WRITER_CALL_RE = re.compile(
    r"\bobjWriter\.(?P<method>WriteStartElement|WriteEndElement|WriteElementString)\s*\("
)
PROPERTY_NAME_RE = re.compile(
    r"nameof\(CharacterSettings\s*\.\s*(?P<property>[A-Za-z_][A-Za-z0-9_]*)\)"
)
SETTER_RE = re.compile(r"\.Set(?P<property>[A-Za-z_][A-Za-z0-9_]*)Async\s*\(")
DIRECT_ASSIGNMENT_RE = re.compile(
    r"_objCharacterSettings\.(?P<property>[A-Za-z_][A-Za-z0-9_]*)\s*="
)
FIELD_RE = re.compile(r"(?<![A-Za-z0-9_])_[A-Za-z][A-Za-z0-9_]*")
STRING_LITERAL_RE = re.compile(r'^\s*"(?P<value>(?:\\.|[^"\\])*)"')


MANUAL_CONTROL_PROPERTIES: dict[str, tuple[str, ...]] = {
    "cboLimbCount": ("LimbCount", "ExcludeLimbSlot"),
    "cboPriorityTable": ("PriorityTable",),
    "chkEncumbrancePenaltyWoundModifier": ("DoEncumbrancePenaltyWoundModifier",),
    "chkGrade": ("BannedWareGrades",),
    "chkUnclampAttributeMinimum": ("UnclampAttributeMinimum",),
    "treCustomDataDirectories": ("CustomDataDirectoryKeys",),
    "treSourcebook": ("Books",),
    "txtNuyenExpression": ("ChargenKarmaToNuyenExpression",),
}

MANUAL_PROPERTY_PATHS: dict[str, tuple[str, ...]] = {
    "BannedWareGrades": ("settings/bannedwaregrades/grade",),
    "Books": ("settings/books/book",),
    "CustomDataDirectoryKeys": (
        "settings/customdatadirectorynames/customdatadirectoryname",
    ),
    "CyberlimbAttributeBonusCapOverride": (
        "settings/cyberlimbattributebonuscapoverride",
    ),
    "DoEncumbrancePenaltyReaction": ("settings/doencumbrancepenaltyreaction",),
    "DroneArmorMultiplierEnabled": ("settings/dronearmormultiplierenabled",),
    "EssenceDecimals": ("settings/essenceformat",),
    "ExceedNegativeQualities": ("settings/exceednegativequalities",),
    "ExceedPositiveQualities": ("settings/exceedpositivequalities",),
    "MaxNuyenDecimals": ("settings/nuyenformat",),
    "MinNuyenDecimals": ("settings/nuyenformat",),
    "MaxKnowledgeSkillRating": ("settings/maxknowledgeskillrating",),
    "MaxSkillRating": ("settings/maxskillrating",),
    "MysAdeptAllowPpCareer": ("settings/mysaddppcareer",),
    "MysAdeptSecondMAGAttribute": ("settings/mysadeptsecondmagattribute",),
    "PrioritySpellsAsAdeptPowers": ("settings/priorityspellsasadeptpowers",),
    "RedlinerExcludesArms": ("settings/redlinerexclusion/limb",),
    "RedlinerExcludesLegs": ("settings/redlinerexclusion/limb",),
    "RedlinerExcludesSkull": ("settings/redlinerexclusion/limb",),
    "RedlinerExcludesTorso": ("settings/redlinerexclusion/limb",),
    "UnclampAttributeMinimum": ("settings/unclampattributeminimum",),
    "WeightDecimals": ("settings/weightformat",),
}

PROFILE_OPERATIONS: dict[str, tuple[str, tuple[str, ...]]] = {
    "cboSetting": ("select_profile", ("settings",)),
    "cmdDelete": ("delete_profile", ("settings",)),
    "cmdEnableSourcebooks": ("enable_sourcebooks", ("settings/books/book",)),
    "cmdRename": ("rename_profile", ("settings/name",)),
    "cmdRestoreDefaults": ("restore_profile_defaults", ("settings",)),
    "cmdSave": ("save_profile", ("settings",)),
    "cmdSaveAs": ("save_profile_as", ("settings",)),
    "cmdOK": ("save_profile_and_close", ("settings",)),
    "cmdDecreaseCustomDirectoryLoadOrder": (
        "move_custom_data_directory_down",
        ("settings/customdatadirectorynames/customdatadirectoryname",),
    ),
    "cmdIncreaseCustomDirectoryLoadOrder": (
        "move_custom_data_directory_up",
        ("settings/customdatadirectorynames/customdatadirectoryname",),
    ),
    "cmdToBottomCustomDirectoryLoadOrder": (
        "move_custom_data_directory_to_bottom",
        ("settings/customdatadirectorynames/customdatadirectoryname",),
    ),
    "cmdToTopCustomDirectoryLoadOrder": (
        "move_custom_data_directory_to_top",
        ("settings/customdatadirectorynames/customdatadirectoryname",),
    ),
    "treCustomDataDirectories": (
        "edit_custom_data_directories",
        ("settings/customdatadirectorynames/customdatadirectoryname",),
    ),
    "treSourcebook": ("edit_sourcebooks", ("settings/books/book",)),
}


def _sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def _balanced_end(text: str, opening: int, opener: str, closer: str) -> int:
    depth = 0
    in_string = False
    verbatim = False
    escaped = False
    index = opening
    while index < len(text):
        character = text[index]
        if in_string:
            if verbatim:
                if character == '"' and index + 1 < len(text) and text[index + 1] == '"':
                    index += 2
                    continue
                if character == '"':
                    in_string = False
            else:
                if escaped:
                    escaped = False
                elif character == "\\":
                    escaped = True
                elif character == '"':
                    in_string = False
            index += 1
            continue
        if character == '@' and index + 1 < len(text) and text[index + 1] == '"':
            in_string = True
            verbatim = True
            index += 2
            continue
        if character == '"':
            in_string = True
            verbatim = False
            index += 1
            continue
        if character == opener:
            depth += 1
        elif character == closer:
            depth -= 1
            if depth == 0:
                return index
        index += 1
    raise ValueError(f"unbalanced {opener}{closer} starting at offset {opening}")


def _call_arguments(text: str, opening: int) -> tuple[str, int]:
    closing = _balanced_end(text, opening, "(", ")")
    return text[opening + 1 : closing], closing + 1


def _definition_body(text: str, name: str) -> str:
    pattern = re.compile(rf"\b{re.escape(name)}\s*\(")
    for match in pattern.finditer(text):
        opening = match.end() - 1
        try:
            signature_end = _balanced_end(text, opening, "(", ")")
        except ValueError:
            continue
        cursor = signature_end + 1
        while cursor < len(text) and text[cursor].isspace():
            cursor += 1
        if cursor < len(text) and text[cursor] == "{":
            try:
                closing = _balanced_end(text, cursor, "{", "}")
            except ValueError:
                continue
            return text[cursor + 1 : closing]
    return ""


def _save_body(character_settings_source: str) -> str:
    signature = "Save(Stream objStream"
    start = character_settings_source.find(signature)
    if start < 0:
        raise ValueError("CharacterSettings.Save(Stream) was not found")
    opening = character_settings_source.find("(", start)
    signature_end = _balanced_end(character_settings_source, opening, "(", ")")
    body_opening = character_settings_source.find("{", signature_end)
    body_closing = _balanced_end(character_settings_source, body_opening, "{", "}")
    return character_settings_source[body_opening + 1 : body_closing]


def _saved_field_paths(character_settings_source: str) -> dict[str, set[str]]:
    body = _save_body(character_settings_source)
    stack: list[str] = []
    result: dict[str, set[str]] = {}
    cursor = 0
    while True:
        match = WRITER_CALL_RE.search(body, cursor)
        if match is None:
            break
        arguments, cursor = _call_arguments(body, match.end() - 1)
        method = match.group("method")
        if method == "WriteEndElement":
            if stack:
                stack.pop()
            continue
        literal = STRING_LITERAL_RE.match(arguments)
        if literal is None:
            continue
        name = bytes(literal.group("value"), "utf-8").decode("unicode_escape")
        if method == "WriteStartElement":
            stack.append(name)
            continue
        path = "/".join([*stack, name])
        for field in FIELD_RE.findall(arguments):
            result.setdefault(field, set()).add(path)
    return result


def _property_fields(
    character_settings_source: str,
    property_name: str,
    saved_fields: set[str],
) -> set[str]:
    chunks = [
        _definition_body(character_settings_source, f"Get{property_name}Async"),
        _definition_body(character_settings_source, f"Set{property_name}Async"),
    ]
    property_pattern = re.compile(
        rf"\bpublic\s+[^;{{}}]+\s+{re.escape(property_name)}\s*\{{"
    )
    property_match = property_pattern.search(character_settings_source)
    if property_match is not None:
        opening = property_match.end() - 1
        try:
            closing = _balanced_end(character_settings_source, opening, "{", "}")
            chunks.append(character_settings_source[opening + 1 : closing])
        except ValueError:
            pass
    return {
        field
        for chunk in chunks
        for field in FIELD_RE.findall(chunk)
        if field in saved_fields
    }


def _mutable_binding_properties(form_source: str) -> dict[str, set[str]]:
    result: dict[str, set[str]] = {}
    cursor = 0
    while True:
        match = REGISTER_CALL_RE.search(form_source, cursor)
        if match is None:
            break
        arguments, cursor = _call_arguments(form_source, match.end() - 1)
        if "_objCharacterSettings" not in arguments or "OneWay" in match.group("method"):
            continue
        properties = {item.group("property") for item in PROPERTY_NAME_RE.finditer(arguments)}
        setters = {item.group("property") for item in SETTER_RE.finditer(arguments)}
        selected = properties & setters if properties & setters else properties
        if selected:
            result.setdefault(match.group("control"), set()).update(selected)
    return result


def _event_properties(form_source: str, handlers: Iterable[str]) -> set[str]:
    result: set[str] = set()
    for handler in handlers:
        body = _definition_body(form_source, handler)
        result.update(item.group("property") for item in SETTER_RE.finditer(body))
        result.update(item.group("property") for item in DIRECT_ASSIGNMENT_RE.finditer(body))
    return result


def _default_values(settings_xml: Path) -> dict[str, list[str]]:
    root = ET.parse(settings_xml).getroot()
    setting = root.find("./settings/setting")
    if setting is None:
        raise ValueError("Chummer/data/settings.xml contains no built-in setting")
    values: dict[str, list[str]] = {}

    def visit(element: ET.Element, parents: list[str]) -> None:
        path = "/".join([*parents, element.tag])
        if len(element) == 0:
            values.setdefault(path, []).append(element.text or "")
            return
        for child in element:
            visit(child, [*parents, element.tag])

    visit(setting, [])
    return {
        path.replace("setting/", "settings/", 1): entries
        for path, entries in values.items()
    }


def build_contract(chummer5_root: Path, inventory_path: Path) -> dict[str, object]:
    form_path = chummer5_root / "Chummer" / "Forms" / "EditCharacterSettings.cs"
    character_settings_path = (
        chummer5_root
        / "Chummer"
        / "Backend"
        / "Character Settings"
        / "CharacterSettings.cs"
    )
    settings_xml_path = chummer5_root / "Chummer" / "data" / "settings.xml"
    for path in (form_path, character_settings_path, settings_xml_path, inventory_path):
        if not path.is_file():
            raise FileNotFoundError(path)

    inventory = json.loads(inventory_path.read_text(encoding="utf-8"))
    rows = [
        row
        for row in inventory["rows"]
        if row["legacy"]["formOrControl"] == "EditCharacterSettings"
        and row["editParityRequired"]
    ]
    form_source = form_path.read_text(encoding="utf-8-sig")
    character_settings_source = character_settings_path.read_text(encoding="utf-8-sig")
    field_paths = _saved_field_paths(character_settings_source)
    all_saved_fields = set(field_paths)
    bindings = _mutable_binding_properties(form_source)
    defaults = _default_values(settings_xml_path)
    property_path_cache: dict[str, tuple[str, ...]] = {}

    def property_paths(property_name: str) -> tuple[str, ...]:
        if property_name in property_path_cache:
            return property_path_cache[property_name]
        manual_paths = MANUAL_PROPERTY_PATHS.get(property_name)
        if manual_paths is not None:
            resolved = tuple(sorted(manual_paths))
            property_path_cache[property_name] = resolved
            return resolved
        paths: set[str] = set()
        for field in _property_fields(
            character_settings_source,
            property_name,
            all_saved_fields,
        ):
            paths.update(field_paths[field])
        resolved = tuple(sorted(paths))
        property_path_cache[property_name] = resolved
        return resolved

    controls: list[dict[str, object]] = []
    unresolved: list[str] = []
    for row in sorted(rows, key=lambda item: (item["legacy"]["line"], item["id"])):
        legacy = row["legacy"]
        control = legacy["controlName"]
        operation = row["operation"]
        semantic_operation = operation
        properties = set(bindings.get(control, set()))
        properties.update(MANUAL_CONTROL_PROPERTIES.get(control, ()))
        handlers = [event["handler"] for event in legacy.get("events", [])]
        properties.update(_event_properties(form_source, handlers))

        if control in PROFILE_OPERATIONS:
            semantic_operation, explicit_paths = PROFILE_OPERATIONS[control]
            paths = set(explicit_paths)
        else:
            paths = {
                path
                for property_name in properties
                for path in property_paths(property_name)
            }

        if not paths:
            unresolved.append(control)
        controls.append(
            {
                "legacyControl": control,
                "legacyType": legacy["controlType"],
                "legacyLine": legacy["line"],
                "inventoryRowId": row["id"],
                "operation": operation,
                "semanticOperation": semantic_operation,
                "characterSettingsProperties": sorted(properties),
                "persistencePaths": sorted(paths),
                "builtInStandardValues": {
                    path: defaults.get(path, []) for path in sorted(paths)
                },
                "events": legacy.get("events", []),
            }
        )

    return {
        "schema": "chummer.android.character-settings-contract/v1",
        "status": "complete" if not unresolved else "unresolved_fail_closed",
        "implementationEvidence": False,
        "summary": {
            "controlCount": len(controls),
            "resolvedControlCount": len(controls) - len(unresolved),
            "unresolvedControlCount": len(unresolved),
            "setValueCount": sum(row["operation"] == "set_value" for row in rows),
            "profileOrCollectionActionCount": sum(
                row["operation"] != "set_value" for row in rows
            ),
        },
        "unresolvedControls": sorted(unresolved),
        "sourceInputs": [
            {"path": str(path), "sha256": _sha256(path)}
            for path in (form_path, character_settings_path, settings_xml_path, inventory_path)
        ],
        "controls": controls,
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--chummer5-root", type=Path, default=DEFAULT_CHUMMER5_ROOT)
    parser.add_argument("--inventory", type=Path, default=DEFAULT_INVENTORY)
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
    parser.add_argument("--check", action="store_true")
    arguments = parser.parse_args()

    try:
        payload = build_contract(
            arguments.chummer5_root.resolve(), arguments.inventory.resolve()
        )
    except (FileNotFoundError, ET.ParseError, ValueError, json.JSONDecodeError, OSError) as error:
        print(f"character settings contract failed: {error}", file=sys.stderr)
        return 1

    rendered = json.dumps(payload, indent=2) + "\n"
    output = arguments.output.resolve()
    if arguments.check:
        if not output.is_file() or output.read_text(encoding="utf-8") != rendered:
            print(f"character settings contract is stale: {output}", file=sys.stderr)
            return 1
        print(f"character settings contract is current: {output}")
        return 0

    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(rendered, encoding="utf-8")
    print(json.dumps(payload["summary"], indent=2))
    if payload["unresolvedControls"]:
        print(
            "unresolved controls: " + ", ".join(payload["unresolvedControls"]),
            file=sys.stderr,
        )
        return 1
    print(f"wrote {output}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
