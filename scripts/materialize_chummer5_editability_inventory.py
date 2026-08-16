#!/usr/bin/env python3
"""Materialize a fail-closed Android editability inventory from Chummer5 UI sources.

The inventory deliberately includes ambiguous interactive controls. A row is never treated as
covered merely because a broad feature family exists in Android: phone and tablet coverage must
name a concrete route/surface, durable mutation, persistence assertion, and E2E proof.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import subprocess
import sys
from collections import Counter, defaultdict
from pathlib import Path
from typing import Any, Iterable


REPO_ROOT = Path(__file__).resolve().parents[1]


def _resolve_workspace_root() -> Path:
    configured = os.environ.get("CHUMMER_COMPLETE_ROOT")
    if configured:
        return Path(configured).resolve()

    for candidate in REPO_ROOT.parents:
        if (
            (candidate / "chummer-presentation").is_dir()
            and (candidate / "chummer-design").is_dir()
        ):
            return candidate.resolve()
    return REPO_ROOT.parent.resolve()


WORKSPACE_ROOT = _resolve_workspace_root()
DEFAULT_CHUMMER5_ROOT = Path(
    os.environ.get("CHUMMER5A_ROOT", "/docker/chummer5a")
).resolve()
DEFAULT_REGISTRY = Path(
    os.environ.get(
        "CHUMMER_ANDROID_PARITY_REGISTRY",
        WORKSPACE_ROOT / "chummer-design" / "products" / "chummer" / "ANDROID_WINDOWS_FEATURE_PARITY.yaml",
    )
).resolve()
DEFAULT_OUTPUT = REPO_ROOT / "docs" / "ANDROID_CHUMMER5_EDITABILITY_INVENTORY.generated.json"
CONDITION_E2E_RECEIPTS = {
    "phone": REPO_ROOT
    / "docs"
    / "editability-evidence"
    / "api36-phone-condition-monitor"
    / "receipt.json",
    "tablet": REPO_ROOT
    / "docs"
    / "editability-evidence"
    / "api36-tablet-condition-monitor"
    / "receipt.json",
}
CONDITION_E2E_JOURNEYS = (
    "careerRunnerImport",
    "physicalConditionDamageEditPersisted",
    "stunConditionDamageEditPersisted",
    "processRestartConditionDamagePersistence",
)
CONTACT_PET_E2E_RECEIPTS = {
    "phone": REPO_ROOT
    / "docs"
    / "editability-evidence"
    / "api36-phone-contact-pet"
    / "receipt.json",
    "tablet": REPO_ROOT
    / "docs"
    / "editability-evidence"
    / "api36-tablet-contact-pet"
    / "receipt.json",
}
CONTACT_PET_E2E_JOURNEYS = (
    "creationRunnerImport",
    "contactInvalidBoundsRejected",
    "contactEditPersisted",
    "contactDeletePersisted",
    "processRestartContactPersistence",
    "petInvalidNameRejected",
    "petEditPersisted",
    "petDeletePersisted",
    "processRestartPetPersistence",
)
ATTRIBUTE_PHONE_E2E_RECEIPT = (
    REPO_ROOT
    / "docs"
    / "editability-evidence"
    / "api36-phone-attributes"
    / "receipt.json"
)
ATTRIBUTE_E2E_JOURNEYS = (
    "newRunner",
    "attributeBaseEditPersisted",
    "attributeKarmaEditPersisted",
    "processRestartAttributePersistence",
)
ATTRIBUTE_CAREER_PHONE_E2E_RECEIPT = (
    REPO_ROOT
    / "docs"
    / "editability-evidence"
    / "api36-phone-career-attributes"
    / "receipt.json"
)
ATTRIBUTE_CAREER_E2E_JOURNEYS = (
    "careerRunnerImport",
    "attributeImprovePersisted",
    "attributeBurnEdgePersisted",
    "processRestartCareerAttributePersistence",
)
NEW_CHARACTER_SETTINGS_PHONE_E2E_RECEIPT = (
    REPO_ROOT
    / "docs"
    / "editability-evidence"
    / "api36-phone-new-character-build-settings"
    / "receipt.json"
)
NEW_CHARACTER_SETTINGS_E2E_JOURNEYS = (
    "characterSettingEdited",
    "ignoreCreationRulesEnabled",
    "creationCommitCompleted",
    "characterSettingUiReadback",
    "workspaceBuildSettingsPersisted",
    "processRestartBuildSettingsPersistence",
)
NEW_CHARACTER_PRIORITY_PHONE_E2E_RECEIPT = (
    REPO_ROOT
    / "docs"
    / "editability-evidence"
    / "api36-phone-new-character-priority"
    / "receipt.json"
)
NEW_CHARACTER_KARMA_PHONE_E2E_RECEIPT = (
    REPO_ROOT
    / "docs"
    / "editability-evidence"
    / "api36-phone-new-character-karma"
    / "receipt.json"
)
NEW_CHARACTER_KARMA_E2E_JOURNEYS = (
    "buildMethodKarmaSelected",
    "metatypeSearchEdited",
    "metatypeSearchFiltered",
    "metatypeCategoryEdited",
    "metatypeEdited",
    "metavariantEdited",
    "forceEdited",
    "possessionBasedEnabled",
    "possessionMethodEdited",
    "creationCommitCompleted",
    "metatypeUiReadback",
    "metavariantUiReadback",
    "workspaceKarmaPersisted",
    "processRestartKarmaPersistence",
    "spiritUiReadback",
    "workspaceSpiritPossessionPersisted",
    "processRestartSpiritPossessionPersistence",
)
NEW_CHARACTER_PRIORITY_E2E_JOURNEYS = (
    "metatypeCategoryEdited",
    "metatypeEdited",
    "metavariantEdited",
    "heritagePriorityEdited",
    "attributesPriorityEdited",
    "talentPriorityEdited",
    "skillsPriorityEdited",
    "resourcesPriorityEdited",
    "talentChoiceEdited",
    "prioritySkillChoice1Edited",
    "prioritySkillChoice2Edited",
    "prioritySkillChoice3Edited",
    "forceEdited",
    "possessionBasedEnabled",
    "possessionMethodEdited",
    "creationCommitCompleted",
    "metatypeUiReadback",
    "metavariantUiReadback",
    "workspacePriorityPersisted",
    "processRestartPriorityPersistence",
    "spiritUiReadback",
    "workspaceSpiritPossessionPersisted",
    "processRestartSpiritPossessionPersistence",
)
CHARACTER_SETTINGS_PHONE_E2E_RECEIPT = (
    REPO_ROOT
    / "docs"
    / "editability-evidence"
    / "api36-phone-character-settings"
    / "receipt.json"
)
CHARACTER_SETTINGS_E2E_JOURNEYS = (
    "actionSearchRoute",
    "allEightPhoneSectionsReachable",
    "checkboxEdited",
    "textEdited",
    "numberEdited",
    "pickerEdited",
    "sourcebookCollectionEdited",
    "customDataCollectionEdited",
    "profileSavedWithoutClosing",
    "profileSavedAndClosed",
    "catalogXmlPersisted",
    "processRestartCatalogPersistence",
    "processRestartUiReadback",
    "allValueControlsEdited",
    "allValueControlsCatalogPersisted",
    "allValueControlsRestartUiReadback",
)
CHARACTER_SETTINGS_CONTROL_E2E_PROOF_KEYS = (
    "mutated",
    "catalogPersisted",
    "processRestartUiReadback",
)
CHARACTER_SETTINGS_ACTIONS_PHONE_E2E_RECEIPT = (
    REPO_ROOT
    / "docs"
    / "editability-evidence"
    / "api36-phone-character-settings-actions"
    / "receipt.json"
)
CHARACTER_SETTINGS_ACTIONS_E2E_JOURNEYS = (
    "profileSavedAs",
    "profileRenamed",
    "profileSelected",
    "sourcebooksEnabled",
    "customDataMovedDown",
    "customDataMovedUp",
    "customDataMovedToBottom",
    "customDataMovedToTop",
    "defaultsRestored",
    "profileDeleted",
    "processRestartCatalogPersistence",
    "processRestartUiReadback",
)
CHARACTER_SETTINGS_ACTION_CONTROL_E2E_PROOF_KEYS = (
    "mutated",
    "catalogPersisted",
    "processRestartReadback",
)
ORIGIN_DOSSIER_PHONE_E2E_RECEIPT = (
    REPO_ROOT
    / "docs"
    / "editability-evidence"
    / "api36-phone-origin-dossier"
    / "receipt.json"
)
ORIGIN_DOSSIER_E2E_JOURNEYS = (
    "creationRunnerCreated",
    "allCreationOriginFieldsEdited",
    "creationWorkspaceXmlPersisted",
    "creationProcessRestartUiReadback",
    "careerRunnerImported",
    "allCareerOriginFieldsEdited",
    "careerWorkspaceXmlPersisted",
    "careerProcessRestartUiReadback",
)
ORIGIN_DOSSIER_CONTROL_E2E_PROOF_KEYS = (
    "mutated",
    "workspacePersisted",
    "processRestartUiReadback",
)
LINKED_RUNNER_PHONE_E2E_RECEIPT = (
    REPO_ROOT
    / "docs"
    / "editability-evidence"
    / "api36-phone-linked-runner"
    / "receipt.json"
)
LINKED_RUNNER_E2E_JOURNEYS = (
    "creationRunnerImported",
    "invalidLinkedRunnerRejected",
    "contactLinkedRunnerAttachPersisted",
    "petLinkedRunnerAttachPersisted",
    "processRestartAttachPersistence",
    "contactLinkedRunnerRemovePersisted",
    "petLinkedRunnerRemovePersisted",
    "processRestartRemovePersistence",
)
LINKED_RUNNER_CONTROL_E2E_PROOF_KEYS = (
    "mutated",
    "workspacePersisted",
    "processRestartUiReadback",
)
CHARACTER_SETTINGS_EXACT_API36_ACTIONS = frozenset({"cmdSave", "cmdOK"})
CHARACTER_SETTINGS_ACTION_AUTOMATION_IDS = {
    "cboSetting": "dialog-field-charactersettingsprofile",
    "cmdEnableSourcebooks": "dialog-field-charactersettingscontrol-tresourcebook",
    "cmdDecreaseCustomDirectoryLoadOrder": "dialog-field-charactersettingscontrol-trecustomdatadirectories",
    "cmdIncreaseCustomDirectoryLoadOrder": "dialog-field-charactersettingscontrol-trecustomdatadirectories",
    "cmdSave": "dialog-action-save",
    "cmdOK": "dialog-action-save-and-close",
    "cmdSaveAs": "dialog-action-save-as",
    "cmdRestoreDefaults": "dialog-action-restore-defaults",
    "cmdDelete": "dialog-action-delete",
    "cmdRename": "dialog-action-rename",
    "cmdToBottomCustomDirectoryLoadOrder": "dialog-field-charactersettingscontrol-trecustomdatadirectories",
    "cmdToTopCustomDirectoryLoadOrder": "dialog-field-charactersettingscontrol-trecustomdatadirectories",
}
CHARACTER_SETTINGS_ACTION_E2E_CONTROLS = (
    frozenset(CHARACTER_SETTINGS_ACTION_AUTOMATION_IDS)
    - CHARACTER_SETTINGS_EXACT_API36_ACTIONS
)

INERT_LEGACY_DESIGNER_FIELDS = {
    ("SelectBuildMethod", "cboBuildMethod"):
        "legacy designer remnant is never added to the SelectBuildMethod control tree and has no event wiring",
    ("SelectBuildMethod", "nudMaxAvail"):
        "legacy designer remnant is never added to the SelectBuildMethod control tree and has no event wiring",
    ("SelectBuildMethod", "cboGamePlay"):
        "legacy designer remnant is never added to the SelectBuildMethod control tree and has no event wiring",
}

SCHEMA = "chummer.android.chummer5-editability-inventory/v1"
REQUIRED_SOURCE_ROOTS = (Path("Chummer/Forms"), Path("Chummer/Controls"))
OPTIONAL_PRODUCT_UI_ROOTS = (
    Path("Plugins/ChummerHub.Client/UI"),
    Path("Translator"),
    Path("CrashHandler"),
    Path("ChummerDataViewer"),
)
REQUIRED_ROW_FIELDS = (
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
)

FIELD_RE = re.compile(
    r"^\s*(?:(?:private|protected|public|internal|static|readonly|volatile|new)\s+)+"
    r"(?P<type>[A-Za-z_][A-Za-z0-9_.]*(?:<[^;=]+>)?\??)\s+"
    r"(?P<name>[A-Za-z_][A-Za-z0-9_]*)\s*(?:=|;)",
    re.MULTILINE,
)
NAMESPACE_RE = re.compile(r"\bnamespace\s+([A-Za-z_][A-Za-z0-9_.]*)")
CLASS_RE = re.compile(r"\b(?:partial\s+)?class\s+([A-Za-z_][A-Za-z0-9_]*)")
EVENT_RE = re.compile(
    r"(?<![A-Za-z0-9_.])(?:this\.)?(?P<control>[A-Za-z_][A-Za-z0-9_]*)\."
    r"(?P<event>[A-Za-z_][A-Za-z0-9_]*)\s*\+=\s*(?P<expression>[^;\n]+)",
    re.MULTILINE,
)
PROPERTY_RE = re.compile(
    r"(?<![A-Za-z0-9_.])(?:this\.)?(?P<control>[A-Za-z_][A-Za-z0-9_]*)\."
    r"(?P<property>Text|Tag|ReadOnly|Enabled|Visible)\s*=\s*"
    r"(?P<value>true|false|\"(?:\\.|[^\"])*\")\s*;",
    re.IGNORECASE | re.MULTILINE,
)
NEW_CONTROL_RE = re.compile(
    r"\bnew\s+(?P<type>[A-Za-z_][A-Za-z0-9_.]*(?:<[^;={}()]+>)?)\s*(?:\(|\{)",
    re.MULTILINE,
)
TARGET_TYPED_NEW_RE = re.compile(
    r"(?P<type>[A-Za-z_][A-Za-z0-9_.]*(?:<[^;={}()]+>)?)\s+"
    r"(?P<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*new\s*(?:\(|\{)",
    re.MULTILINE,
)

DIRECT_EDITOR_TYPES = {
    "CheckBox",
    "CheckBoxTableCell",
    "CheckedListBox",
    "ColorableCheckBox",
    "ComboBox",
    "DataGridViewCheckBoxColumn",
    "DataGridViewComboBoxColumn",
    "DateTimePicker",
    "DpiFriendlyCheckBoxDisguisedAsButton",
    "ElasticComboBox",
    "MaskedTextBox",
    "NumericUpDown",
    "NumericUpDownEx",
    "RadioButton",
    "RichTextBox",
    "RtfEditor",
    "SpinnerTableCell",
    "TextBox",
    "TextTableCell",
    "TrackBar",
}
ACTION_TYPES = {
    "Button",
    "ButtonWithToolTip",
    "DpiFriendlyImagedButton",
    "DpiFriendlyToolStripButton",
    "DpiFriendlyToolStripMenuItem",
    "LinkLabel",
    "SplitButton",
    "ToolStripButton",
    "ToolStripMenuItem",
}
COLLECTION_EDITOR_TYPES = {
    "BindingListDisplay",
    "DataGridView",
    "ListBox",
    "ListView",
    "ObservableCollectionDisplay",
    "TableView",
    "TreeView",
}
MUTATING_EVENT_NAMES = {
    "AfterCheck",
    "AfterLabelEdit",
    "CellEndEdit",
    "CheckedChanged",
    "Click",
    "DragDrop",
    "DropDownClosed",
    "ItemCheck",
    "KeyDown",
    "SelectedIndexChanged",
    "SelectedValueChanged",
    "TextChanged",
    "Validated",
    "ValueChanged",
}

ORIGIN_FIELDS = {
    "txtCharacterName": ("name", "origin-name"),
    "txtAlias": ("alias", "origin-alias"),
    "txtPlayerName": ("playername", "origin-player-name"),
    "txtGender": ("sex", "origin-sex"),
    "txtAge": ("age", "origin-age"),
    "txtHeight": ("height", "origin-height"),
    "txtWeight": ("weight", "origin-weight"),
    "txtHair": ("hair", "origin-hair"),
    "txtEyes": ("eyes", "origin-eyes"),
    "txtSkin": ("skin", "origin-skin"),
    "rtfConcept": ("concept", "origin-concept"),
    "rtfDescription": ("description", "origin-description"),
    "rtfBackground": ("background", "origin-background"),
}
ATTRIBUTE_FIELDS = {
    "nudBase": ("base", "attribute-base-{attribute}"),
    "nudKarma": ("karma", "attribute-karma-{attribute}"),
    "cmdImproveATT": ("improve", "attribute-improve-{attribute}"),
    "cmdBurnEdge": ("burn", "attribute-burn-edge"),
}
CONTACT_TEXT_FIELDS = {
    "txtContactName": ("Name", "name", "name"),
    "cboContactRole": ("Role", "role", "role"),
    "txtContactLocation": ("Location", "location", "location"),
    "cboMetatype": ("Metatype", "metatype", "metatype"),
    "cboGender": ("Gender", "gender", "gender"),
    "cboAge": ("Age", "age", "age"),
    "cboType": ("ContactType", "contacttype", "contacttype"),
    "cboPreferredPayment": ("PreferredPayment", "preferredpayment", "preferredpayment"),
    "cboHobbiesVice": ("HobbiesVice", "hobbiesvice", "hobbiesvice"),
    "cboPersonalLife": ("PersonalLife", "personallife", "personallife"),
    "cmdNotes": ("Notes", "notes", "notes"),
}
CONTACT_TOGGLE_FIELDS = {
    "chkGroup": ("Group", "group", "group"),
    "chkFree": ("Free", "free", "free"),
    "chkFamily": ("Family", "family", "family"),
    "chkBlackmail": ("Blackmail", "blackmail", "blackmail"),
}
CONTACT_RATING_FIELDS = {
    "nudConnection": ("ContactConnection", "connection", "connection"),
    "nudLoyalty": ("ContactLoyalty", "loyalty", "loyalty"),
}
PET_TEXT_FIELDS = {
    "txtContactName": ("Name", "name", "name"),
    "cboMetatype": ("Metatype", "metatype", "metatype"),
    "cmdNotes": ("Notes", "notes", "notes"),
}
LEGACY_CAREER_COLLECTION_DELETE_CONTROLS = {
    "cmdDeleteGear": ("Gear", "Gear"),
    "cmdDeleteWeapon": ("Weapon", "Weapons"),
    "cmdDeleteArmor": ("Armor", "Armor"),
    "cmdDeleteVehicle": ("Vehicle", "Vehicles"),
    "cmdDeleteCyberware": ("Cyberware", "Cyberware"),
    "cmdDeleteSpell": ("Spell", "Spells"),
    "cmdDeleteComplexForm": ("ComplexForm", "Complex forms"),
    "cmdDeleteAIProgram": ("MatrixProgram", "AI programs"),
    "cmdDeleteCritterPower": ("CritterPower", "Critter powers"),
    "cmdDeleteQuality": ("Quality", "Qualities"),
}

MATRIX_CONDITION_CONTROL_RE = re.compile(
    r"^chk(?P<kind>Cyberware|Gear|Armor|Weapon|Vehicle)MatrixCM(?P<box>[1-9]|1[0-9]|2[0-4])$"
)
MATRIX_CONDITION_HANDLERS = {
    "Cyberware": "chkCyberwareCM_CheckedChanged",
    "Gear": "chkGearCM_CheckedChanged",
    "Armor": "chkArmorMatrixCM_CheckedChanged",
    "Weapon": "chkWeaponCM_CheckedChanged",
    "Vehicle": "chkVehicleCM_CheckedChanged",
}
MATRIX_CONDITION_COVERAGE_LIMITS = {
    "Cyberware": (
        "Exact saved-data and authoritatively matched GUID-settings/profile subset only; legacy "
        "settings replacement, unsupported custom-data operations, and Device Rating expressions "
        "beyond integer Rating/braced Rating/saved character-total tokens remain read-only."
    ),
    "Gear": (
        "Exact saved-data subset includes integer Rating/braced Rating/saved character-total tokens; "
        "unsupported selected Living Persona, Device Rating, or Matrix-condition expressions remain "
        "read-only."
    ),
    "Armor": (
        "Exact saved-data subset only; unsupported Device Rating or selected child Living Persona "
        "expressions remain read-only."
    ),
    "Weapon": (
        "Exact saved-data and unambiguous saved-parent subset only; ambiguous or cyclic parent "
        "graphs and Device Rating expressions beyond integer Rating/braced Rating/saved "
        "character-total tokens remain read-only."
    ),
    "Vehicle": (
        "Exact saved-data and authoritatively matched GUID-settings/profile source-only mod subset "
        "only; legacy settings replacement, unsupported custom-data operations, and unsupported "
        "base Device Rating expressions remain read-only."
    ),
}
CHARACTER_CONDITION_CONTROL_RE = re.compile(
    r"^chk(?P<track>Physical|Stun)CM(?P<box>[1-9]|1[0-9]|2[0-4])$"
)
CHARACTER_CONDITION_HANDLERS = {
    "Physical": "chkPhysicalCM_CheckedChanged",
    "Stun": "chkStunCM_CheckedChanged",
}
DASHBOARD_CONDITION_CONTROLS = {
    "_btnPhysical": ("Physical", "_btnPhysical_Click"),
    "_nudPhysical": ("Physical", "_btnPhysical_Click"),
    "_btnApplyStun": ("Stun", "_btnApplyStun_Click"),
    "nudStun": ("Stun", "_btnApplyStun_Click"),
}
VEHICLE_PHYSICAL_CONDITION_CONTROL_RE = re.compile(
    r"^chkVehiclePhysicalCM(?P<box>[1-9]|1[0-9]|2[0-4])$"
)
VEHICLE_PHYSICAL_CONDITION_COVERAGE_LIMIT = (
    "Exact saved-data and authoritatively matched GUID-settings/profile source-only mod subset "
    "only; legacy settings replacement, unsupported custom-data operations, and unsupported body "
    "expressions remain read-only."
)

SELECTION_FORM_FAMILIES = {
    "selectaiprogram": "matrix_programs",
    "selectarmor": "armor",
    "selectarmormod": "armor_mods",
    "selectart": "metamagics_and_echoes",
    "selectattribute": "attributes",
    "selectcalendarstart": "calendar",
    "selectcomplexform": "complex_forms",
    "selectcontactconnection": "contacts",
    "selectcritterpower": "critter_powers",
    "selectcyberware": "cyberware",
    "selectcyberwaresuite": "cyberware",
    "selectdrug": "drugs",
    "selectexoticskill": "skills",
    "selectgear": "gear",
    "selectlifestyle": "lifestyles",
    "selectlifestylequality": "lifestyles",
    "selectmartialart": "qualities",
    "selectmartialarttechnique": "qualities",
    "selectmentorspirit": "qualities",
    "selectmetamagic": "metamagics_and_echoes",
    "selectoptionalpower": "critter_powers",
    "selectpower": "adept_powers",
    "selectprogramoption": "matrix_programs",
    "selectquality": "qualities",
    "selectsetting": "sourcebook_selection",
    "selectskill": "skills",
    "selectskillcategory": "skills",
    "selectskillgroup": "skill_groups",
    "selectskillspec": "specializations",
    "selectspell": "spells",
    "selectspellcategory": "spells",
    "selectvehicle": "vehicles_and_drones",
    "selectvehiclemod": "vehicle_mods_and_mounts",
    "selectweapon": "weapons",
    "selectweaponaccessory": "weapon_accessories",
    "selectweaponcategory": "weapons",
}

FORM_FAMILIES = {
    "About": "application_file_workflows",
    "AddToken": "dice_pools_and_tokens",
    "BindingListDisplay": "generic_selection_and_collection_controls",
    "CharacterRoster": "character_roster",
    "CharacterSheetViewer": "import_export_printing",
    "CheckBoxTableCell": "generic_selection_and_collection_controls",
    "ChummerUpdater": "application_file_workflows",
    "ConditionMonitorUserControl": "condition_monitors_and_damage",
    "CreateImprovement": "improvements",
    "CreatePACKSKit": "packs_and_templates",
    "DataExporter": "import_export_printing",
    "DicePoolControl": "dice_pools_and_tokens",
    "DiceRoller": "dice_pools_and_tokens",
    "EditXmlData": "xml_and_custom_data",
    "ExportCharacter": "import_export_printing",
    "GameMasterDashboard": "initiative_and_turn_tracking",
    "HeroLabImporter": "import_export_printing",
    "InitiativeRoller": "initiative_and_turn_tracking",
    "InitiativeTracker": "initiative_and_turn_tracking",
    "InitiativeUserControl": "initiative_and_turn_tracking",
    "LimitTabUserControl": "limits_and_modifiers",
    "MasterIndex": "application_file_workflows",
    "NumericUpDownEx": "generic_selection_and_collection_controls",
    "ObservableCollectionDisplay": "generic_selection_and_collection_controls",
    "PlayerDashboard": "initiative_and_turn_tracking",
    "PrintMultipleCharacters": "import_export_printing",
    "ScrollableMessageBox": "application_file_workflows",
    "SelectDiceHits": "dice_pools_and_tokens",
    "SelectItem": "generic_selection_and_collection_controls",
    "SelectLimit": "limits_and_modifiers",
    "SelectLimitModifier": "limits_and_modifiers",
    "SelectNumber": "generic_selection_and_collection_controls",
    "SelectPACKSKit": "packs_and_templates",
    "SelectSide": "generic_selection_and_collection_controls",
    "SelectText": "generic_selection_and_collection_controls",
    "SellItem": "generic_selection_and_collection_controls",
    "SpinnerTableCell": "generic_selection_and_collection_controls",
    "SustainedObjectControl": "spells",
    "TableCell": "generic_selection_and_collection_controls",
    "TableView": "generic_selection_and_collection_controls",
    "TestDataEntries": "xml_and_custom_data",
    "TextTableCell": "generic_selection_and_collection_controls",
    "VersionHistory": "application_file_workflows",
}

FAMILY_KEYWORDS = (
    ("condition_monitors_and_damage", ("stuncm", "physicalcm", "conditionmonitor", "damage")),
    ("karma_nuyen_and_reputation", ("streetcred", "notoriety", "publicaware", "reputation", "karma", "edgegained", "edgespent", "burnstreetcred")),
    ("magic_tradition_and_resonance", ("tradition", "drain", "stream", "joingroup", "groupname")),
    ("improvements", ("improvement",)),
    ("limits_and_modifiers", ("limitmodifier", "liftcarryhits", "limitcontrol")),
    ("mugshots", ("mugshot", "portrait")),
    ("calendar", ("calendar", "addweek", "editweek", "deleteweek", "startweek")),
    ("sourcebook_selection", ("sourcebook", "bookenabled", "customdata")),
    ("vehicle_mods_and_mounts", ("weaponmount", "vehiclemod", "vehiclecyberware", "vehiclesensor")),
    ("vehicles_and_drones", ("vehicle", "drone")),
    ("weapon_accessories", ("weaponaccessory", "underbarrel", "accessory")),
    ("weapons", ("weapon", "ammo", "reload")),
    ("armor_mods", ("armormod", "armorgear")),
    ("armor", ("armor",)),
    ("nested_plugins", ("plugin", "addgear")),
    ("cyberware", ("cyberware", "cyberlimb")),
    ("bioware", ("bioware",)),
    ("drugs", ("drug",)),
    ("initiation_and_submersion", ("initiation", "submersion", "grade")),
    ("metamagics_and_echoes", ("metamagic", "echo")),
    ("spirits_and_sprites", ("spirit", "sprite")),
    ("complex_forms", ("complexform",)),
    ("matrix_programs", ("aiprogram", "advancedprogram", "program")),
    ("rituals", ("ritual", "preparation", "enchant")),
    ("spells", ("spell",)),
    ("critter_powers", ("critterpower", "weakness")),
    ("adept_powers", ("adeptpower", "power")),
    ("foci", ("focus", "foci")),
    ("gear", ("gear", "commlink", "homenode", "wireless")),
    ("specializations", ("specialization", "skillspec", "spec")),
    ("skill_groups", ("skillgroup",)),
    ("knowledge_and_languages", ("knowledge", "language")),
    ("skills", ("skill",)),
    ("qualities", ("quality", "qualit", "martialart", "mentor", "paragon")),
    ("contacts", ("contact", "enemy", "enemie", "pet")),
    ("identities", ("identity",)),
    ("lifestyles", ("lifestyle",)),
    ("expenses", ("expense", "nuyen")),
    ("calendar", ("calendar",)),
    ("notes", ("note", "rtfeditor")),
    ("spells", ("sustained",)),
    ("locations_and_containers", ("location", "container")),
    ("equipped_quantity_rating_and_custom_names", ("equipped", "quantity", "rating", "customname")),
    ("attributes", ("attribute",)),
)


def _read_text(path: Path) -> str:
    return path.read_text(encoding="utf-8-sig", errors="replace")


def _sha256_bytes(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def _sha256_file(path: Path) -> str:
    return _sha256_bytes(path.read_bytes())


def _validated_condition_e2e_receipts() -> dict[str, dict[str, Any]]:
    driver = REPO_ROOT / "tests" / "run_api36_editing_e2e.py"
    fixture = REPO_ROOT / "tests" / "fixtures" / "career-condition-monitor-e2e.chum5"
    if not driver.is_file() or not fixture.is_file():
        return {}

    expected_driver_sha = _sha256_file(driver)
    expected_fixture_sha = _sha256_file(fixture)
    validated: dict[str, dict[str, Any]] = {}
    for profile, receipt_path in CONDITION_E2E_RECEIPTS.items():
        try:
            receipt = json.loads(_read_text(receipt_path))
        except (FileNotFoundError, json.JSONDecodeError, OSError):
            continue
        journeys = receipt.get("journeys")
        apk_sha = str(receipt.get("apkSha256") or "")
        if not (
            receipt.get("schema") == "chummer.android.editing-e2e/v1"
            and receipt.get("status") == "pass"
            and receipt.get("profile") == profile
            and receipt.get("journey") == "condition-monitor"
            and receipt.get("apiLevel") == 36
            and receipt.get("driverSha256") == expected_driver_sha
            and receipt.get("inputFixtureSha256") == expected_fixture_sha
            and isinstance(journeys, dict)
            and all(journeys.get(journey) == "pass" for journey in CONDITION_E2E_JOURNEYS)
            and re.fullmatch(r"[0-9a-f]{64}", apk_sha)
        ):
            continue
        validated[profile] = {
            "status": "executed_api36",
            "ref": receipt_path.relative_to(REPO_ROOT).as_posix(),
            "receiptSha256": _sha256_file(receipt_path),
            "apkSha256": apk_sha,
        }

    if set(validated) != set(CONDITION_E2E_RECEIPTS):
        return {}
    if validated["phone"]["apkSha256"] != validated["tablet"]["apkSha256"]:
        return {}
    return validated


def _validated_contact_pet_e2e_receipts() -> dict[str, dict[str, Any]]:
    driver = REPO_ROOT / "tests" / "run_api36_editing_e2e.py"
    fixture = REPO_ROOT / "tests" / "fixtures" / "creation-contact-pet-e2e.chum5"
    if not driver.is_file() or not fixture.is_file():
        return {}

    expected_driver_sha = _sha256_file(driver)
    expected_fixture_sha = _sha256_file(fixture)
    validated: dict[str, dict[str, Any]] = {}
    for profile, receipt_path in CONTACT_PET_E2E_RECEIPTS.items():
        try:
            receipt = json.loads(_read_text(receipt_path))
        except (FileNotFoundError, json.JSONDecodeError, OSError):
            continue
        journeys = receipt.get("journeys")
        apk_sha = str(receipt.get("apkSha256") or "")
        if not (
            receipt.get("schema") == "chummer.android.editing-e2e/v1"
            and receipt.get("status") == "pass"
            and receipt.get("profile") == profile
            and receipt.get("journey") == "contact-pet"
            and receipt.get("apiLevel") == 36
            and receipt.get("driverSha256") == expected_driver_sha
            and receipt.get("inputFixtureSha256") == expected_fixture_sha
            and isinstance(journeys, dict)
            and all(journeys.get(journey) == "pass" for journey in CONTACT_PET_E2E_JOURNEYS)
            and re.fullmatch(r"[0-9a-f]{64}", apk_sha)
        ):
            continue
        validated[profile] = {
            "status": "executed_api36",
            "ref": receipt_path.relative_to(REPO_ROOT).as_posix(),
            "receiptSha256": _sha256_file(receipt_path),
            "apkSha256": apk_sha,
        }

    if set(validated) != set(CONTACT_PET_E2E_RECEIPTS):
        return {}
    if validated["phone"]["apkSha256"] != validated["tablet"]["apkSha256"]:
        return {}
    return validated


def _validated_attribute_phone_e2e_receipt() -> dict[str, Any] | None:
    driver = REPO_ROOT / "tests" / "run_api36_attribute_e2e.py"
    shared_driver = REPO_ROOT / "tests" / "run_api36_editing_e2e.py"
    if not driver.is_file() or not shared_driver.is_file():
        return None

    expected_driver_sha = _sha256_file(driver)
    expected_shared_driver_sha = _sha256_file(shared_driver)
    try:
        receipt = json.loads(_read_text(ATTRIBUTE_PHONE_E2E_RECEIPT))
    except (FileNotFoundError, json.JSONDecodeError, OSError):
        return None
    journeys = receipt.get("journeys")
    apk_sha = str(receipt.get("apkSha256") or "")
    if not (
        receipt.get("schema") == "chummer.android.editing-e2e/v1"
        and receipt.get("status") == "pass"
        and receipt.get("profile") == "phone"
        and receipt.get("journey") == "attributes"
        and receipt.get("apiLevel") == 36
        and receipt.get("driverSha256") == expected_driver_sha
        and receipt.get("sharedDriverSha256") == expected_shared_driver_sha
        and isinstance(journeys, dict)
        and all(journeys.get(journey) == "pass" for journey in ATTRIBUTE_E2E_JOURNEYS)
        and re.fullmatch(r"[0-9a-f]{64}", apk_sha)
    ):
        return None
    return {
        "status": "executed_api36",
        "ref": ATTRIBUTE_PHONE_E2E_RECEIPT.relative_to(REPO_ROOT).as_posix(),
        "receiptSha256": _sha256_file(ATTRIBUTE_PHONE_E2E_RECEIPT),
        "apkSha256": apk_sha,
    }


def _validated_attribute_career_phone_e2e_receipt() -> dict[str, Any] | None:
    driver = REPO_ROOT / "tests" / "run_api36_career_attribute_e2e.py"
    creation_driver = REPO_ROOT / "tests" / "run_api36_attribute_e2e.py"
    shared_driver = REPO_ROOT / "tests" / "run_api36_editing_e2e.py"
    fixture = REPO_ROOT / "tests" / "fixtures" / "career-attribute-e2e.chum5"
    if not all(path.is_file() for path in (driver, creation_driver, shared_driver, fixture)):
        return None

    try:
        receipt = json.loads(_read_text(ATTRIBUTE_CAREER_PHONE_E2E_RECEIPT))
    except (FileNotFoundError, json.JSONDecodeError, OSError):
        return None
    journeys = receipt.get("journeys")
    apk_sha = str(receipt.get("apkSha256") or "")
    if not (
        receipt.get("schema") == "chummer.android.editing-e2e/v1"
        and receipt.get("status") == "pass"
        and receipt.get("profile") == "phone"
        and receipt.get("journey") == "career-attributes"
        and receipt.get("apiLevel") == 36
        and receipt.get("driverSha256") == _sha256_file(driver)
        and receipt.get("creationAttributeDriverSha256") == _sha256_file(creation_driver)
        and receipt.get("sharedDriverSha256") == _sha256_file(shared_driver)
        and receipt.get("inputFixtureSha256") == _sha256_file(fixture)
        and isinstance(journeys, dict)
        and all(
            journeys.get(journey) == "pass"
            for journey in ATTRIBUTE_CAREER_E2E_JOURNEYS
        )
        and re.fullmatch(r"[0-9a-f]{64}", apk_sha)
    ):
        return None
    return {
        "status": "executed_api36",
        "ref": ATTRIBUTE_CAREER_PHONE_E2E_RECEIPT.relative_to(REPO_ROOT).as_posix(),
        "receiptSha256": _sha256_file(ATTRIBUTE_CAREER_PHONE_E2E_RECEIPT),
        "apkSha256": apk_sha,
    }


def _validated_new_character_settings_phone_e2e_receipt() -> dict[str, Any] | None:
    driver = REPO_ROOT / "tests" / "run_api36_new_character_settings_e2e.py"
    shared_driver = REPO_ROOT / "tests" / "run_api36_editing_e2e.py"
    native_dialog = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "NativeDialogPage.cs"
    build_page = native_dialog.with_name("BuildPage.cs")
    presentation_root = WORKSPACE_ROOT / "chummer-presentation" / "Chummer.Presentation" / "Overview"
    dialog_factory = presentation_root / "DesktopDialogFactory.cs"
    dialog_coordinator = presentation_root / "DialogCoordinator.cs"
    sources = (driver, shared_driver, native_dialog, build_page, dialog_factory, dialog_coordinator)
    if not all(path.is_file() for path in sources):
        return None

    try:
        receipt = json.loads(_read_text(NEW_CHARACTER_SETTINGS_PHONE_E2E_RECEIPT))
    except (FileNotFoundError, json.JSONDecodeError, OSError):
        return None
    journeys = receipt.get("journeys")
    apk_sha = str(receipt.get("apkSha256") or "")
    if not (
        receipt.get("schema") == "chummer.android.editing-e2e/v1"
        and receipt.get("status") == "pass"
        and receipt.get("profile") == "phone"
        and receipt.get("journey") == "new-character-build-settings"
        and receipt.get("apiLevel") == 36
        and receipt.get("driverSha256") == _sha256_file(driver)
        and receipt.get("sharedDriverSha256") == _sha256_file(shared_driver)
        and receipt.get("nativeDialogPageSha256") == _sha256_file(native_dialog)
        and receipt.get("buildPageSha256") == _sha256_file(build_page)
        and receipt.get("dialogFactorySha256") == _sha256_file(dialog_factory)
        and receipt.get("dialogCoordinatorSha256") == _sha256_file(dialog_coordinator)
        and isinstance(journeys, dict)
        and all(
            journeys.get(journey) == "pass"
            for journey in NEW_CHARACTER_SETTINGS_E2E_JOURNEYS
        )
        and re.fullmatch(r"[0-9a-f]{64}", apk_sha)
    ):
        return None
    return {
        "status": "executed_api36",
        "ref": NEW_CHARACTER_SETTINGS_PHONE_E2E_RECEIPT.relative_to(REPO_ROOT).as_posix(),
        "receiptSha256": _sha256_file(NEW_CHARACTER_SETTINGS_PHONE_E2E_RECEIPT),
        "apkSha256": apk_sha,
    }


def _validated_character_settings_phone_e2e_receipt() -> dict[str, Any] | None:
    driver = REPO_ROOT / "tests" / "run_api36_character_settings_e2e.py"
    shared_driver = REPO_ROOT / "tests" / "run_api36_editing_e2e.py"
    native_root = REPO_ROOT / "src" / "Chummer.Android" / "Native"
    native_command = native_root / "NativeCommandPage.cs"
    native_dialog = native_root / "NativeDialogPage.cs"
    coordinator = native_root / "RunnerSessionCoordinator.cs"
    presentation_root = WORKSPACE_ROOT / "chummer-presentation" / "Chummer.Presentation" / "Overview"
    dialog_factory = presentation_root / "DesktopDialogFactory.cs"
    character_settings_dialog = presentation_root / "DesktopDialogFactory.CharacterSettings.cs"
    profiles = presentation_root / "Chummer5CharacterSettingsProfiles.cs"
    contract = presentation_root / "Chummer5CharacterSettingsRuntimeContract.Generated.cs"
    dialog_coordinator = presentation_root / "DialogCoordinator.cs"
    sources = (
        driver,
        shared_driver,
        native_command,
        native_dialog,
        coordinator,
        dialog_factory,
        character_settings_dialog,
        profiles,
        contract,
        dialog_coordinator,
    )
    if not all(path.is_file() for path in sources):
        return None

    try:
        receipt = json.loads(_read_text(CHARACTER_SETTINGS_PHONE_E2E_RECEIPT))
    except (FileNotFoundError, json.JSONDecodeError, OSError):
        return None
    journeys = receipt.get("journeys")
    control_proofs = receipt.get("controls")
    apk_sha = str(receipt.get("apkSha256") or "")
    contract_text = _read_text(contract)
    fields_marker = "internal static IReadOnlyList<Chummer5CharacterSettingsFieldDefinition> Fields"
    values_marker = "internal static IReadOnlyDictionary<string, IReadOnlyList<string>> BuiltInStandardValues"
    if fields_marker not in contract_text or values_marker not in contract_text:
        return None
    fields_block = contract_text.split(fields_marker, 1)[1].split(values_marker, 1)[0]
    expected_control_proofs = set(re.findall(r'new\("([^"]+)"', fields_block))
    expected_hashes = {
        "driverSha256": driver,
        "sharedDriverSha256": shared_driver,
        "nativeCommandPageSha256": native_command,
        "nativeDialogPageSha256": native_dialog,
        "runnerSessionCoordinatorSha256": coordinator,
        "dialogFactorySha256": dialog_factory,
        "characterSettingsDialogSha256": character_settings_dialog,
        "characterSettingsProfilesSha256": profiles,
        "characterSettingsContractSha256": contract,
        "dialogCoordinatorSha256": dialog_coordinator,
    }
    if not (
        receipt.get("schema") == "chummer.android.editing-e2e/v1"
        and receipt.get("status") == "pass"
        and receipt.get("profile") == "phone"
        and receipt.get("journey") == "character-settings"
        and receipt.get("apiLevel") == 36
        and all(receipt.get(key) == _sha256_file(path) for key, path in expected_hashes.items())
        and isinstance(journeys, dict)
        and all(journeys.get(journey) == "pass" for journey in CHARACTER_SETTINGS_E2E_JOURNEYS)
        and isinstance(control_proofs, dict)
        and receipt.get("valueControlCount") == len(expected_control_proofs) == 150
        and set(control_proofs) == expected_control_proofs
        and all(
            isinstance(proof, dict)
            and all(proof.get(key) == "pass" for key in CHARACTER_SETTINGS_CONTROL_E2E_PROOF_KEYS)
            for proof in control_proofs.values()
        )
        and re.fullmatch(r"[0-9a-f]{64}", apk_sha)
    ):
        return None
    return {
        "status": "executed_api36",
        "ref": CHARACTER_SETTINGS_PHONE_E2E_RECEIPT.relative_to(REPO_ROOT).as_posix(),
        "receiptSha256": _sha256_file(CHARACTER_SETTINGS_PHONE_E2E_RECEIPT),
        "apkSha256": apk_sha,
        "controlProofs": control_proofs,
    }


def _validated_character_settings_actions_phone_e2e_receipt() -> dict[str, Any] | None:
    driver = REPO_ROOT / "tests" / "run_api36_character_settings_actions_e2e.py"
    character_settings_driver = REPO_ROOT / "tests" / "run_api36_character_settings_e2e.py"
    shared_driver = REPO_ROOT / "tests" / "run_api36_editing_e2e.py"
    native_root = REPO_ROOT / "src" / "Chummer.Android" / "Native"
    overview = WORKSPACE_ROOT / "chummer-presentation" / "Chummer.Presentation" / "Overview"
    expected_hashes = {
        "driverSha256": driver,
        "characterSettingsDriverSha256": character_settings_driver,
        "sharedDriverSha256": shared_driver,
        "nativeCommandPageSha256": native_root / "NativeCommandPage.cs",
        "nativeDialogPageSha256": native_root / "NativeDialogPage.cs",
        "runnerSessionCoordinatorSha256": native_root / "RunnerSessionCoordinator.cs",
        "dialogFactorySha256": overview / "DesktopDialogFactory.cs",
        "characterSettingsDialogSha256": overview / "DesktopDialogFactory.CharacterSettings.cs",
        "characterSettingsProfilesSha256": overview / "Chummer5CharacterSettingsProfiles.cs",
        "characterSettingsContractSha256": overview / "Chummer5CharacterSettingsRuntimeContract.Generated.cs",
        "dialogCoordinatorSha256": overview / "DialogCoordinator.cs",
    }
    if not all(path.is_file() for path in expected_hashes.values()):
        return None

    try:
        receipt = json.loads(_read_text(CHARACTER_SETTINGS_ACTIONS_PHONE_E2E_RECEIPT))
    except (FileNotFoundError, json.JSONDecodeError, OSError):
        return None
    journeys = receipt.get("journeys")
    control_proofs = receipt.get("controls")
    apk_sha = str(receipt.get("apkSha256") or "")
    if not (
        receipt.get("schema") == "chummer.android.editing-e2e/v1"
        and receipt.get("status") == "pass"
        and receipt.get("profile") == "phone"
        and receipt.get("journey") == "character-settings-actions"
        and receipt.get("apiLevel") == 36
        and all(receipt.get(key) == _sha256_file(path) for key, path in expected_hashes.items())
        and isinstance(journeys, dict)
        and all(
            journeys.get(journey) == "pass"
            for journey in CHARACTER_SETTINGS_ACTIONS_E2E_JOURNEYS
        )
        and isinstance(control_proofs, dict)
        and receipt.get("controlCount") == len(CHARACTER_SETTINGS_ACTION_E2E_CONTROLS) == 10
        and set(control_proofs) == CHARACTER_SETTINGS_ACTION_E2E_CONTROLS
        and all(
            isinstance(proof, dict)
            and all(
                proof.get(key) == "pass"
                for key in CHARACTER_SETTINGS_ACTION_CONTROL_E2E_PROOF_KEYS
            )
            for proof in control_proofs.values()
        )
        and re.fullmatch(r"[0-9a-f]{64}", apk_sha)
    ):
        return None
    return {
        "status": "executed_api36",
        "ref": CHARACTER_SETTINGS_ACTIONS_PHONE_E2E_RECEIPT.relative_to(REPO_ROOT).as_posix(),
        "receiptSha256": _sha256_file(CHARACTER_SETTINGS_ACTIONS_PHONE_E2E_RECEIPT),
        "apkSha256": apk_sha,
        "controlProofs": control_proofs,
    }


def _validated_origin_dossier_phone_e2e_receipt() -> dict[str, Any] | None:
    driver = REPO_ROOT / "tests" / "run_api36_origin_dossier_e2e.py"
    shared_driver = REPO_ROOT / "tests" / "run_api36_editing_e2e.py"
    native_root = REPO_ROOT / "src" / "Chummer.Android" / "Native"
    page = native_root / "OriginDossierPage.cs"
    coordinator = native_root / "RunnerSessionCoordinator.cs"
    fixture = REPO_ROOT / "tests" / "fixtures" / "career-condition-monitor-e2e.chum5"
    workspace_mutations = (
        WORKSPACE_ROOT
        / "chummer-presentation"
        / "Chummer.Presentation"
        / "Overview"
        / "CharacterOverviewPresenter.WorkspaceMutations.cs"
    )
    expected_hashes = {
        "driverSha256": driver,
        "sharedDriverSha256": shared_driver,
        "originDossierPageSha256": page,
        "runnerSessionCoordinatorSha256": coordinator,
        "workspaceMutationsSha256": workspace_mutations,
        "careerFixtureSha256": fixture,
    }
    if not all(path.is_file() for path in expected_hashes.values()):
        return None

    try:
        receipt = json.loads(_read_text(ORIGIN_DOSSIER_PHONE_E2E_RECEIPT))
    except (FileNotFoundError, json.JSONDecodeError, OSError):
        return None
    journeys = receipt.get("journeys")
    control_proofs = receipt.get("controls")
    apk_sha = str(receipt.get("apkSha256") or "")
    expected_controls = {
        f"{form_name}.{control}"
        for form_name in ("CharacterCreate", "CharacterCareer")
        for control in ORIGIN_FIELDS
    }
    if not (
        receipt.get("schema") == "chummer.android.editing-e2e/v1"
        and receipt.get("status") == "pass"
        and receipt.get("profile") == "phone"
        and receipt.get("journey") == "origin-dossier"
        and receipt.get("apiLevel") == 36
        and all(receipt.get(key) == _sha256_file(path) for key, path in expected_hashes.items())
        and isinstance(journeys, dict)
        and all(journeys.get(journey) == "pass" for journey in ORIGIN_DOSSIER_E2E_JOURNEYS)
        and isinstance(control_proofs, dict)
        and receipt.get("controlCount") == len(expected_controls) == 26
        and set(control_proofs) == expected_controls
        and all(
            isinstance(proof, dict)
            and all(proof.get(key) == "pass" for key in ORIGIN_DOSSIER_CONTROL_E2E_PROOF_KEYS)
            for proof in control_proofs.values()
        )
        and re.fullmatch(r"[0-9a-f]{64}", apk_sha)
    ):
        return None
    return {
        "status": "executed_api36",
        "ref": ORIGIN_DOSSIER_PHONE_E2E_RECEIPT.relative_to(REPO_ROOT).as_posix(),
        "receiptSha256": _sha256_file(ORIGIN_DOSSIER_PHONE_E2E_RECEIPT),
        "apkSha256": apk_sha,
        "controlProofs": control_proofs,
    }


def _validated_linked_runner_phone_e2e_receipt() -> dict[str, Any] | None:
    driver = REPO_ROOT / "tests" / "run_api36_linked_runner_e2e.py"
    shared_driver = REPO_ROOT / "tests" / "run_api36_editing_e2e.py"
    native_root = REPO_ROOT / "src" / "Chummer.Android"
    overview = WORKSPACE_ROOT / "chummer-presentation" / "Chummer.Presentation" / "Overview"
    fixture_root = REPO_ROOT / "tests" / "fixtures"
    expected_hashes = {
        "driverSha256": driver,
        "sharedDriverSha256": shared_driver,
        "collectionEditorPagesSha256": native_root / "Native" / "CollectionEditorPages.cs",
        "runnerSessionCoordinatorSha256": native_root / "Native" / "RunnerSessionCoordinator.cs",
        "linkedCharacterFileServiceSha256": native_root / "Platform" / "IAndroidLinkedCharacterFileService.cs",
        "linkedDocumentCodecSha256": WORKSPACE_ROOT / "chummer-core-engine" / "Chummer.Infrastructure" / "Xml" / "Chummer5LinkedDocumentCodec.cs",
        "workspaceCollectionEditorProjectorSha256": overview / "WorkspaceCollectionEditorProjector.cs",
        "workspaceCollectionEditorStateSha256": overview / "WorkspaceCollectionEditorState.cs",
        "workspaceCollectionMutationRequestSha256": overview / "WorkspaceCollectionMutationRequest.cs",
        "workspaceXmlMutationCatalogSha256": overview / "WorkspaceXmlMutationCatalog.cs",
        "workspaceMutationsSha256": overview / "CharacterOverviewPresenter.WorkspaceMutations.cs",
        "inputFixtureSha256": fixture_root / "creation-contact-pet-e2e.chum5",
        "linkedFixtureSha256": fixture_root / "linked-runner-e2e.chum5",
        "invalidLinkedFixtureSha256": fixture_root / "invalid-linked-runner-e2e.chum5",
    }
    if not all(path.is_file() for path in expected_hashes.values()):
        return None

    try:
        receipt = json.loads(_read_text(LINKED_RUNNER_PHONE_E2E_RECEIPT))
    except (FileNotFoundError, json.JSONDecodeError, OSError):
        return None
    journeys = receipt.get("journeys")
    control_proofs = receipt.get("controls")
    apk_sha = str(receipt.get("apkSha256") or "")
    expected_controls = {
        f"{class_name}.{control}"
        for class_name in ("ContactControl", "PetControl")
        for control in ("tsAttachCharacter", "tsRemoveCharacter")
    }
    if not (
        receipt.get("schema") == "chummer.android.editing-e2e/v1"
        and receipt.get("status") == "pass"
        and receipt.get("profile") == "phone"
        and receipt.get("journey") == "linked-runner"
        and receipt.get("apiLevel") == 36
        and all(receipt.get(key) == _sha256_file(path) for key, path in expected_hashes.items())
        and isinstance(journeys, dict)
        and all(journeys.get(journey) == "pass" for journey in LINKED_RUNNER_E2E_JOURNEYS)
        and isinstance(control_proofs, dict)
        and receipt.get("controlCount") == len(expected_controls) == 4
        and set(control_proofs) == expected_controls
        and all(
            isinstance(proof, dict)
            and all(proof.get(key) == "pass" for key in LINKED_RUNNER_CONTROL_E2E_PROOF_KEYS)
            for proof in control_proofs.values()
        )
        and re.fullmatch(r"[0-9a-f]{64}", apk_sha)
    ):
        return None
    return {
        "status": "executed_api36",
        "ref": LINKED_RUNNER_PHONE_E2E_RECEIPT.relative_to(REPO_ROOT).as_posix(),
        "receiptSha256": _sha256_file(LINKED_RUNNER_PHONE_E2E_RECEIPT),
        "apkSha256": apk_sha,
        "controlProofs": control_proofs,
    }


def _validated_new_character_karma_phone_e2e_receipt() -> dict[str, Any] | None:
    driver = REPO_ROOT / "tests" / "run_api36_new_character_karma_e2e.py"
    shared_driver = REPO_ROOT / "tests" / "run_api36_editing_e2e.py"
    helper_driver = REPO_ROOT / "tests" / "run_api36_new_character_priority_e2e.py"
    native_dialog = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "NativeDialogPage.cs"
    build_page = native_dialog.with_name("BuildPage.cs")
    presentation_root = WORKSPACE_ROOT / "chummer-presentation" / "Chummer.Presentation" / "Overview"
    dialog_factory = presentation_root / "DesktopDialogFactory.cs"
    dialog_coordinator = presentation_root / "DialogCoordinator.cs"
    sources = (
        driver,
        shared_driver,
        helper_driver,
        native_dialog,
        build_page,
        dialog_factory,
        dialog_coordinator,
    )
    if not all(path.is_file() for path in sources):
        return None

    try:
        receipt = json.loads(_read_text(NEW_CHARACTER_KARMA_PHONE_E2E_RECEIPT))
    except (FileNotFoundError, json.JSONDecodeError, OSError):
        return None
    journeys = receipt.get("journeys")
    apk_sha = str(receipt.get("apkSha256") or "")
    if not (
        receipt.get("schema") == "chummer.android.editing-e2e/v1"
        and receipt.get("status") == "pass"
        and receipt.get("profile") == "phone"
        and receipt.get("journey") == "new-character-metatype-karma"
        and receipt.get("apiLevel") == 36
        and receipt.get("driverSha256") == _sha256_file(driver)
        and receipt.get("sharedDriverSha256") == _sha256_file(shared_driver)
        and receipt.get("helperDriverSha256") == _sha256_file(helper_driver)
        and receipt.get("nativeDialogPageSha256") == _sha256_file(native_dialog)
        and receipt.get("buildPageSha256") == _sha256_file(build_page)
        and receipt.get("dialogFactorySha256") == _sha256_file(dialog_factory)
        and receipt.get("dialogCoordinatorSha256") == _sha256_file(dialog_coordinator)
        and isinstance(journeys, dict)
        and all(journeys.get(journey) == "pass" for journey in NEW_CHARACTER_KARMA_E2E_JOURNEYS)
        and re.fullmatch(r"[0-9a-f]{64}", apk_sha)
    ):
        return None
    return {
        "status": "executed_api36",
        "ref": NEW_CHARACTER_KARMA_PHONE_E2E_RECEIPT.relative_to(REPO_ROOT).as_posix(),
        "receiptSha256": _sha256_file(NEW_CHARACTER_KARMA_PHONE_E2E_RECEIPT),
        "apkSha256": apk_sha,
    }


def _validated_new_character_priority_phone_e2e_receipt() -> dict[str, Any] | None:
    driver = REPO_ROOT / "tests" / "run_api36_new_character_priority_e2e.py"
    shared_driver = REPO_ROOT / "tests" / "run_api36_editing_e2e.py"
    native_dialog = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "NativeDialogPage.cs"
    build_page = native_dialog.with_name("BuildPage.cs")
    presentation_root = WORKSPACE_ROOT / "chummer-presentation" / "Chummer.Presentation" / "Overview"
    dialog_factory = presentation_root / "DesktopDialogFactory.cs"
    dialog_coordinator = presentation_root / "DialogCoordinator.cs"
    sources = (driver, shared_driver, native_dialog, build_page, dialog_factory, dialog_coordinator)
    if not all(path.is_file() for path in sources):
        return None

    try:
        receipt = json.loads(_read_text(NEW_CHARACTER_PRIORITY_PHONE_E2E_RECEIPT))
    except (FileNotFoundError, json.JSONDecodeError, OSError):
        return None
    journeys = receipt.get("journeys")
    apk_sha = str(receipt.get("apkSha256") or "")
    if not (
        receipt.get("schema") == "chummer.android.editing-e2e/v1"
        and receipt.get("status") == "pass"
        and receipt.get("profile") == "phone"
        and receipt.get("journey") == "new-character-metatype-priority"
        and receipt.get("apiLevel") == 36
        and receipt.get("driverSha256") == _sha256_file(driver)
        and receipt.get("sharedDriverSha256") == _sha256_file(shared_driver)
        and receipt.get("nativeDialogPageSha256") == _sha256_file(native_dialog)
        and receipt.get("buildPageSha256") == _sha256_file(build_page)
        and receipt.get("dialogFactorySha256") == _sha256_file(dialog_factory)
        and receipt.get("dialogCoordinatorSha256") == _sha256_file(dialog_coordinator)
        and isinstance(journeys, dict)
        and all(
            journeys.get(journey) == "pass"
            for journey in NEW_CHARACTER_PRIORITY_E2E_JOURNEYS
        )
        and re.fullmatch(r"[0-9a-f]{64}", apk_sha)
    ):
        return None
    return {
        "status": "executed_api36",
        "ref": NEW_CHARACTER_PRIORITY_PHONE_E2E_RECEIPT.relative_to(REPO_ROOT).as_posix(),
        "receiptSha256": _sha256_file(NEW_CHARACTER_PRIORITY_PHONE_E2E_RECEIPT),
        "apkSha256": apk_sha,
    }


def _git_value(root: Path, *arguments: str) -> str:
    try:
        return subprocess.run(
            ["git", "-C", str(root), *arguments],
            check=True,
            capture_output=True,
            text=True,
            timeout=30,
        ).stdout.strip()
    except (FileNotFoundError, subprocess.CalledProcessError, subprocess.TimeoutExpired):
        return "unavailable"


def _class_identity(text: str, path: Path) -> tuple[str, str] | None:
    namespace = NAMESPACE_RE.search(text)
    class_name = CLASS_RE.search(text)
    if class_name is None:
        return None
    return (namespace.group(1) if namespace else path.parent.name, class_name.group(1))


def _simple_type(type_name: str) -> str:
    value = type_name.rstrip("?").split("<", 1)[0]
    return value.rsplit(".", 1)[-1]


def _control_kind(type_name: str) -> str | None:
    simple = _simple_type(type_name)
    if simple in DIRECT_EDITOR_TYPES or simple.startswith("DataGridView") and "Column" in simple:
        return "direct_value_editor"
    if simple in ACTION_TYPES or simple.endswith("Button") or simple.endswith("MenuItem"):
        return "action"
    if simple in COLLECTION_EDITOR_TYPES:
        return "collection_editor"
    if simple.endswith("Control") or simple in {
        "ContactControl",
        "DicePoolControl",
        "ExpenseChart",
        "KnowledgeSkillControl",
        "PetControl",
        "SkillControl",
        "SkillGroupControl",
        "SpiritControl",
        "SustainedObjectControl",
    }:
        return "composite_editor"
    return None


def _event_map(texts: Iterable[str]) -> dict[str, list[dict[str, str]]]:
    result: dict[str, list[dict[str, str]]] = defaultdict(list)
    seen: set[tuple[str, str, str]] = set()
    for text in texts:
        for match in EVENT_RE.finditer(text):
            control = match.group("control")
            event = match.group("event")
            expression = match.group("expression").strip()
            tokens = re.findall(r"[A-Za-z_][A-Za-z0-9_]*", expression)
            handler = tokens[-1] if tokens else "lambda_or_unknown"
            key = (control, event, handler)
            if key in seen:
                continue
            seen.add(key)
            result[control].append({"event": event, "handler": handler})
    for handlers in result.values():
        handlers.sort(key=lambda row: (row["event"], row["handler"]))
    return result


def _property_map(texts: Iterable[str]) -> dict[str, dict[str, str | bool]]:
    result: dict[str, dict[str, str | bool]] = defaultdict(dict)
    canonical_names = {
        "text": "Text",
        "tag": "Tag",
        "readonly": "ReadOnly",
        "enabled": "Enabled",
        "visible": "Visible",
    }
    for text in texts:
        for match in PROPERTY_RE.finditer(text):
            value: str | bool = match.group("value")
            if value.lower() in {"true", "false"}:
                value = value.lower() == "true"
            else:
                value = value[1:-1]
            result[match.group("control")][canonical_names[match.group("property").lower()]] = value
    return result


def _operation(control: str, kind: str, handlers: list[dict[str, str]]) -> tuple[str, str]:
    token = " ".join([control, *(row["handler"] for row in handlers)]).lower()
    if kind == "direct_value_editor":
        return "set_value", "definite"
    if kind == "collection_editor":
        if any(
            marker in token
            for marker in ("dragdrop", "aftercheck", "itemcheck", "cellendedit", "cellvaluechanged", "delete", "remove")
        ):
            return "manage_collection", "definite"
        if handlers and all(
            any(
                marker in row["handler"].lower()
                for marker in ("afterselect", "selectedindex", "columnclick", "selectionchanged", "mousewheel")
            )
            for row in handlers
        ):
            return "select_or_sort_collection", "non_mutating"
        if not handlers:
            return "collection_container", "non_mutating"
        return "manage_collection_review", "review_required"
    if kind == "composite_editor":
        return "composite_container", "non_mutating"

    action_rules = (
        ("delete", ("delete", "remove", "sell", "clear")),
        ("add", ("add", "create", "new", "paste", "import")),
        ("edit", ("edit", "rename", "name", "note", "change")),
        ("reorder", ("moveup", "movedown", "reorder", "sort")),
        ("toggle_equipped", ("equip", "unequip")),
        ("adjust", ("improve", "upgrade", "increase", "decrease", "burn")),
        ("reload", ("reload",)),
        ("commit", ("save", "apply", "accept", "select", "ok")),
        ("toggle", ("enable", "disable", "toggle", "check")),
    )
    for operation, keywords in action_rules:
        if any(keyword in token.replace("_", "") for keyword in keywords):
            return operation, "definite"
    mutating_action_markers = (
        "attach",
        "backcolor",
        "buyammo",
        "fireweapon",
        "font",
        "forecolor",
        "join",
        "karma",
        "mergeqty",
        "move",
        "nuyen",
        "populate",
        "post",
        "preparemodel",
        "reduceqty",
        "restoredefaults",
        "splitqty",
        "subscript",
        "superscript",
        "swap",
        "unload",
        "unorderedlist",
        "upload",
        "visibility",
    )
    compact_token = token.replace("_", "")
    if any(marker in compact_token for marker in mutating_action_markers):
        return "mutating_action", "definite"
    non_mutating_action_markers = (
        "about",
        "cancel",
        "close",
        "discord",
        "expand",
        "export",
        "help",
        "linkclicked",
        "mru",
        "openfile",
        "preview",
        "print",
        "roll",
        "search",
        "verify",
        "wiki",
    )
    if any(marker in compact_token for marker in non_mutating_action_markers):
        return "invoke_non_mutating_action", "non_mutating"
    if not handlers:
        return "action_container", "non_mutating"
    if any(row["event"] in MUTATING_EVENT_NAMES for row in handlers):
        return "invoke_action_review", "review_required"
    return "interaction_review", "review_required"


def _resolve_reviewed_interaction(
    class_name: str,
    control: str,
    kind: str,
    handlers: list[dict[str, str]],
    display_text: str | None = None,
) -> tuple[str, str] | None:
    """Resolve only interaction shapes whose mutation disposition is explicit in their wiring."""
    handler_token = " ".join(row["handler"] for row in handlers).lower().replace("_", "")
    control_token = control.lower().replace("_", "")
    text_token = (display_text or "").lower()

    if kind == "collection_editor":
        if "childpropertychanged" in handler_token:
            return "child_editor_container", "non_mutating"
        if any(
            marker in handler_token
            for marker in ("cmdok", "acceptform", "doubleclick", "keydown", "dragdrop", "edit", "import")
        ):
            return "select_or_mutate_collection", "definite"
        return "collection_selector_or_display", "non_mutating"

    if kind == "event_wired_control":
        if any(marker in handler_token for marker in ("opensource", "openpdf", "sourcelabel", "lblsource")):
            return "open_reference", "non_mutating"
        if "dragdrop" in handler_token or class_name == "TextTableCell":
            return "mutate_from_event_surface", "definite"
        if control.lower() == "panenemies":
            return "event_navigation_or_selection", "non_mutating"
        return None

    if kind != "action":
        return None

    always_non_mutating_classes = {
        "CharacterRoster",
        "ChummerMainForm",
        "ChummerUpdater",
        "CrashReporter",
        "CrashReportView",
        "GameMasterDashboard",
        "MasterIndex",
        "ScrollableMessageBox",
        "SetupForm",
        "TestDataEntries",
    }
    if class_name in always_non_mutating_classes:
        return "invoke_non_mutating_action", "non_mutating"

    if class_name in {
        "ConditionMonitorUserControl",
        "InitiativeUserControl",
        "SkillControl",
        "SkillsTabUserControl",
        "TranslatorMain",
    }:
        return "mutating_action", "definite"

    if class_name in {"CharacterCreate", "CharacterCareer"}:
        return "mutating_action", "definite"

    if class_name in {"ContactControl", "PetControl", "SpiritControl"}:
        if "contactopen" in handler_token:
            return "open_reference", "non_mutating"
        if class_name in {"ContactControl", "PetControl"} and control_token == "cmdlink":
            return "open_link_context_menu", "non_mutating"
        return "mutating_action", "definite"

    if class_name == "EditCharacterSettings":
        if "globaloptionscustomdata" in handler_token:
            return "open_settings_surface", "non_mutating"
        return "mutating_action", "definite"

    if class_name == "EditGlobalSettings":
        if any(marker in handler_token for marker in ("pdftest", "characterroster")):
            return "invoke_non_mutating_action", "non_mutating"
        return "mutating_action", "definite"

    if class_name == "EditXmlData":
        return "load_editor_value", "definite"

    if class_name.startswith("ucSINner"):
        if any(marker in handler_token for marker in ("viewgroup", "loadincharacterroster", "login")):
            return "invoke_non_mutating_action", "non_mutating"
        if text_token == "ok" or text_token.startswith("select "):
            return "select_value", "definite"
        if any(marker in handler_token for marker in ("parentgroup", "temppath")):
            return "set_value", "definite"

    return None


def _disposition_evidence(
    kind: str,
    confidence: str,
    handlers: list[dict[str, str]],
    read_only: str | bool | None,
) -> str:
    handler_names = ", ".join(row["handler"] for row in handlers) or "no event handler"
    if read_only is True and confidence == "non_mutating":
        return f"{kind} is ReadOnly at design time; mutation triggers are inventoried separately"
    if confidence == "non_mutating":
        return f"{kind} source wiring is navigation/display/container behavior ({handler_names})"
    if confidence == "definite":
        return f"{kind} accepts a value or invokes mutation-capable source wiring ({handler_names})"
    return f"source wiring requires manual disposition ({handler_names})"


def _family(path: str, class_name: str, control: str, handlers: list[dict[str, str]]) -> str:
    if path.startswith("Plugins/ChummerHub.Client/UI/"):
        return "online_account_groups_and_sharing"
    if path.startswith("Translator/"):
        return "translation_and_localization"
    if path.startswith("CrashHandler/"):
        return "crash_report_and_diagnostics"
    if path.startswith("ChummerDataViewer/"):
        return "data_tools"
    class_key = class_name.lower()
    if class_name in {"CharacterCreate", "CharacterCareer"} and control in ORIGIN_FIELDS:
        return "origin_dossier"
    if class_name == "AttributeControl" and control in ATTRIBUTE_FIELDS:
        return "attributes"
    if class_name in {"CharacterCreate", "CharacterCareer"}:
        token = control.lower()
        if token.startswith("tab") or token.startswith("lbl") and "source" in token:
            return "origin_dossier" if token == "tablongtexts" else "navigation_and_reference_actions"
        if token.startswith(("mnufile", "mnucreate", "tsbsave", "tsbprint", "tsbcopy", "tsbpaste")):
            return "application_file_workflows"
        if token in {"cboprimaryarm", "chkcharactercreated", "cmdlifemodule"}:
            return "character_settings"
        if token == "btncreatebackstory":
            return "origin_dossier"
        if "mysticadept" in token:
            return "attributes"
        if "dpifriendlycheckboxdisguisedasbutton" in token:
            return "condition_monitors_and_damage"
        if token in {"lmtcontrol"}:
            return "limits_and_modifiers"
        if "psycheactive" in token:
            return "magic_tradition_and_resonance"
        if token.startswith("mnuedit"):
            return "application_file_workflows"
        if token == "tsaddfromfile":
            return "gear"
        if token == "tsmsellitem":
            return "generic_selection_and_collection_controls"
        if token == "cb":
            return "condition_monitors_and_damage"
    if class_name == "CharacterShared" and control == "objControl":
        return "spells"
    if class_name == "EditGlobalSettings":
        return "application_settings"
    if class_name == "EditCharacterSettings":
        if any(token in control.lower() for token in ("source", "book", "customdata")):
            return "sourcebook_selection"
        return "character_settings"
    if class_name == "ChummerMainForm":
        token = control.lower()
        if "globalsettings" in token:
            return "application_settings"
        if "charactersettings" in token:
            return "character_settings"
        if "translator" in token:
            return "translation_and_localization"
        if "xml" in token:
            return "xml_and_custom_data"
        if any(value in token for value in ("export", "import", "print")):
            return "import_export_printing"
        if "dice" in token:
            return "dice_pools_and_tokens"
        if "roster" in token:
            return "character_roster"
        return "application_file_workflows"
    if class_name in FORM_FAMILIES:
        return FORM_FAMILIES[class_name]
    if class_key in SELECTION_FORM_FAMILIES:
        return SELECTION_FORM_FAMILIES[class_key]
    if class_name in {"SelectBuildMethod", "SelectMetatypeKarma", "SelectMetatypePriority", "SelectLifeModule"}:
        return "character_settings"
    if class_name == "ContactControl" or class_name == "PetControl":
        return "contacts"
    if class_name == "SpiritControl":
        return "spirits_and_sprites"
    if class_name in {"KnowledgeSkillControl"}:
        return "knowledge_and_languages"
    if class_name == "SkillGroupControl":
        return "skill_groups"
    if class_name in {"SkillControl", "SkillsTabUserControl"}:
        return "skills"
    if class_name == "PowersTabUserControl":
        return "adept_powers"
    if class_name in {"CreateExpense", "ExpenseChart"}:
        return "expenses"
    if class_name == "CreateCustomDrug":
        return "drugs"
    if class_name == "CreateNaturalWeapon":
        return "weapons"
    if class_name == "CreateSpell":
        return "spells"
    if class_name == "CreateWeaponMount":
        return "vehicle_mods_and_mounts"
    if class_name in {"ReloadWeapon", "SellItem"}:
        return "weapons" if class_name == "ReloadWeapon" else "unclassified_legacy_controls"
    normalized = re.sub(
        r"[^a-z0-9]",
        "",
        " ".join([path, class_name, control, *(row["handler"] for row in handlers)]).lower(),
    )
    for family, keywords in FAMILY_KEYWORDS:
        if any(keyword in normalized for keyword in keywords):
            return family
    return "unclassified_legacy_controls"


def _source_files(chummer5_root: Path) -> tuple[list[Path], list[str]]:
    files: list[Path] = []
    source_roots: list[str] = []
    for relative_root in REQUIRED_SOURCE_ROOTS:
        root = chummer5_root / relative_root
        if not root.is_dir():
            raise FileNotFoundError(f"Missing Chummer5 UI source directory: {root}")
        source_roots.append(relative_root.as_posix())
        files.extend(path for path in root.rglob("*.cs") if path.is_file())
    for relative_root in OPTIONAL_PRODUCT_UI_ROOTS:
        root = chummer5_root / relative_root
        if not root.is_dir():
            continue
        source_roots.append(relative_root.as_posix())
        files.extend(
            path
            for path in root.rglob("*.cs")
            if path.is_file()
            and not any(part in {"Properties", "Migrations", "bin", "obj"} for part in path.parts)
        )
    return (
        sorted(set(files), key=lambda path: path.relative_to(chummer5_root).as_posix()),
        source_roots,
    )


def extract_legacy_rows(chummer5_root: Path) -> tuple[list[dict[str, Any]], dict[str, Any]]:
    files, source_roots = _source_files(chummer5_root)
    units: list[tuple[Path, str, tuple[str, str]]] = []
    class_texts: dict[tuple[str, str], list[str]] = defaultdict(list)
    source_fingerprint = hashlib.sha256()
    for path in files:
        relative = path.relative_to(chummer5_root).as_posix()
        data = path.read_bytes()
        source_fingerprint.update(relative.encode("utf-8"))
        source_fingerprint.update(b"\0")
        source_fingerprint.update(data)
        source_fingerprint.update(b"\0")
        text = data.decode("utf-8-sig", errors="replace")
        identity = _class_identity(text, path)
        if identity is None:
            continue
        units.append((path, text, identity))
        class_texts[identity].append(text)

    events_by_class = {
        identity: _event_map(texts)
        for identity, texts in class_texts.items()
    }
    properties_by_class = {
        identity: _property_map(texts)
        for identity, texts in class_texts.items()
    }
    field_names_by_class: dict[tuple[str, str], set[str]] = defaultdict(set)
    for _, text, identity in units:
        field_names_by_class[identity].update(match.group("name") for match in FIELD_RE.finditer(text))
    rows: list[dict[str, Any]] = []
    seen_ids: set[str] = set()
    for path, text, identity in units:
        namespace, class_name = identity
        relative = path.relative_to(chummer5_root).as_posix()
        texts = class_texts[identity]
        events = events_by_class[identity]
        properties = properties_by_class[identity]
        for match in FIELD_RE.finditer(text):
            type_name = match.group("type")
            control = match.group("name")
            handlers = events.get(control, [])
            kind = _control_kind(type_name)
            if kind is None and not any(row["event"] in MUTATING_EVENT_NAMES for row in handlers):
                continue
            if kind is None:
                kind = "event_wired_control"
            line = text.count("\n", 0, match.start()) + 1
            row_id = f"{relative}::{namespace}.{class_name}::{control}"
            if row_id in seen_ids:
                continue
            seen_ids.add(row_id)
            operation, confidence = _operation(control, kind, handlers)
            control_properties = properties.get(control, {})
            inert_evidence = INERT_LEGACY_DESIGNER_FIELDS.get((class_name, control))
            if inert_evidence is not None:
                operation = "unreachable_designer_field"
                confidence = "non_mutating"
            if confidence == "review_required":
                reviewed = _resolve_reviewed_interaction(
                    class_name,
                    control,
                    kind,
                    handlers,
                    control_properties.get("Text") if isinstance(control_properties.get("Text"), str) else None,
                )
                if reviewed is not None:
                    operation, confidence = reviewed
            read_only = control_properties.get("ReadOnly")
            if read_only is True and confidence == "definite":
                operation = "read_only_display"
                confidence = "non_mutating"
            rows.append(
                {
                    "id": row_id,
                    "legacy": {
                        "sourcePath": relative,
                        "line": line,
                        "namespace": namespace,
                        "formOrControl": class_name,
                        "controlName": control,
                        "controlType": type_name,
                        "candidateKind": kind,
                        "mutationConfidence": confidence,
                        "mutationDisposition": (
                            "mutating" if confidence == "definite" else confidence
                        ),
                        "dispositionEvidence": inert_evidence or _disposition_evidence(
                            kind, confidence, handlers, read_only
                        ),
                        "text": control_properties.get("Text"),
                        "tag": control_properties.get("Tag"),
                        "readOnlyAtDesignTime": read_only,
                        "enabledAtDesignTime": control_properties.get("Enabled"),
                        "visibleAtDesignTime": control_properties.get("Visible"),
                        "events": handlers,
                    },
                    "mutationFamily": _family(relative, class_name, control, handlers),
                    "operation": operation,
                }
            )

        field_names = field_names_by_class[identity]
        dynamic_matches: list[tuple[int, str, str | None]] = []
        for match in NEW_CONTROL_RE.finditer(text):
            prefix = text[max(0, match.start() - 300):match.start()]
            assignment = re.search(r"(?:this\.)?([A-Za-z_][A-Za-z0-9_]*)\s*=\s*$", prefix)
            dynamic_matches.append(
                (match.start(), match.group("type"), assignment.group(1) if assignment else None)
            )
        for match in TARGET_TYPED_NEW_RE.finditer(text):
            dynamic_matches.append((match.start(), match.group("type"), match.group("name")))

        seen_dynamic: set[tuple[int, str, str | None]] = set()
        for start, type_name, assigned_name in sorted(dynamic_matches):
            dynamic_key = (start, type_name, assigned_name)
            if dynamic_key in seen_dynamic:
                continue
            seen_dynamic.add(dynamic_key)
            kind = _control_kind(type_name)
            if kind is None or assigned_name in field_names:
                continue
            line = text.count("\n", 0, start) + 1
            snippet = text[start:start + 600]
            declared_name = assigned_name
            if declared_name is None:
                name_match = re.search(r"\bName\s*=\s*\"([^\"]+)\"", snippet)
                declared_name = name_match.group(1) if name_match else None
            control = declared_name or f"runtime_{_simple_type(type_name)}_{line}"
            handlers = events.get(control, [])
            control_properties = properties.get(control, {})
            text_match = re.search(r"\bText\s*=\s*\"([^\"]*)\"", snippet)
            tag_match = re.search(r"\bTag\s*=\s*\"([^\"]*)\"", snippet)
            operation, confidence = _operation(control, kind, handlers)
            display_text = control_properties.get("Text") or (text_match.group(1) if text_match else None)
            if confidence == "review_required":
                reviewed = _resolve_reviewed_interaction(
                    class_name,
                    control,
                    kind,
                    handlers,
                    display_text if isinstance(display_text, str) else None,
                )
                if reviewed is not None:
                    operation, confidence = reviewed
            row_id = f"{relative}::{namespace}.{class_name}::runtime::{control}@{line}"
            if row_id in seen_ids:
                continue
            seen_ids.add(row_id)
            rows.append(
                {
                    "id": row_id,
                    "legacy": {
                        "sourcePath": relative,
                        "line": line,
                        "namespace": namespace,
                        "formOrControl": class_name,
                        "controlName": control,
                        "controlType": type_name,
                        "candidateKind": f"runtime_{kind}",
                        "mutationConfidence": confidence,
                        "mutationDisposition": (
                            "mutating" if confidence == "definite" else confidence
                        ),
                        "dispositionEvidence": _disposition_evidence(
                            f"runtime_{kind}", confidence, handlers, control_properties.get("ReadOnly")
                        ),
                        "text": display_text,
                        "tag": control_properties.get("Tag") or (tag_match.group(1) if tag_match else None),
                        "readOnlyAtDesignTime": control_properties.get("ReadOnly"),
                        "enabledAtDesignTime": control_properties.get("Enabled"),
                        "visibleAtDesignTime": control_properties.get("Visible"),
                        "events": handlers,
                    },
                    "mutationFamily": _family(relative, class_name, control, handlers),
                    "operation": operation,
                }
            )
    rows.sort(key=lambda row: (row["legacy"]["sourcePath"], row["legacy"]["line"], row["id"]))
    return rows, {
        "sourceFileCount": len(files),
        "designerFileCount": sum(path.name.endswith(".Designer.cs") for path in files),
        "sourceRoots": source_roots,
        "sourceFingerprintSha256": source_fingerprint.hexdigest(),
    }


def _contains(path: Path, *markers: str) -> bool:
    if not path.is_file():
        return False
    text = _read_text(path)
    return all(marker in text for marker in markers)


def _android_token(value: str) -> str:
    return "".join(character if character.isalnum() else "-" for character in value.strip().lower())


def _known_phone_mapping(
    row: dict[str, Any],
    presentation_root: Path,
    condition_e2e_receipts: dict[str, dict[str, Any]],
    contact_pet_e2e_receipts: dict[str, dict[str, Any]],
    attribute_phone_e2e_receipt: dict[str, Any] | None,
    attribute_career_phone_e2e_receipt: dict[str, Any] | None,
    character_settings_phone_e2e_receipt: dict[str, Any] | None,
    character_settings_actions_phone_e2e_receipt: dict[str, Any] | None,
    origin_dossier_phone_e2e_receipt: dict[str, Any] | None,
    linked_runner_phone_e2e_receipt: dict[str, Any] | None,
    new_character_settings_phone_e2e_receipt: dict[str, Any] | None,
    new_character_karma_phone_e2e_receipt: dict[str, Any] | None,
    new_character_priority_phone_e2e_receipt: dict[str, Any] | None,
) -> dict[str, Any] | None:
    legacy = row["legacy"]
    class_name = legacy["formOrControl"]
    control = legacy["controlName"]
    if class_name == "EditCharacterSettings":
        native_root = REPO_ROOT / "src" / "Chummer.Android" / "Native"
        native_command = native_root / "NativeCommandPage.cs"
        native_dialog = native_root / "NativeDialogPage.cs"
        coordinator = native_root / "RunnerSessionCoordinator.cs"
        e2e_driver = REPO_ROOT / "tests" / "run_api36_character_settings_e2e.py"
        overview = presentation_root / "Chummer.Presentation" / "Overview"
        dialog_factory = overview / "DesktopDialogFactory.cs"
        character_settings_dialog = overview / "DesktopDialogFactory.CharacterSettings.cs"
        profiles = overview / "Chummer5CharacterSettingsProfiles.cs"
        runtime_contract = overview / "Chummer5CharacterSettingsRuntimeContract.Generated.cs"
        dialog_coordinator = overview / "DialogCoordinator.cs"
        profile_tests = presentation_root / "Chummer.Tests" / "Presentation" / "Chummer5CharacterSettingsProfilesTests.cs"

        action_automation_id = CHARACTER_SETTINGS_ACTION_AUTOMATION_IDS.get(control)
        value_field = _contains(runtime_contract, f'new("{control}",')
        if not value_field and action_automation_id is None:
            return None

        implementation_complete = (
            _contains(
                native_command,
                'AutomationId = $"command-action-{Token(command.Id)}"',
                "new NativeDialogPage(_coordinator, dialog)",
            )
            and _contains(
                native_dialog,
                'AutomationId = $"dialog-field-{Token(field.Id)}"',
                'AutomationId = $"dialog-action-{Token(action.Id)}"',
                "RequiresStructuralRerender",
            )
            and _contains(
                coordinator,
                "CharacterSettingsCatalogPreferenceKey",
                "Preferences.Default.Set",
                "DesktopPreferenceStateRuntime.SetCurrent",
            )
            and _contains(dialog_factory, '"character_settings" => BuildCharacterSettingsDialog(preferences)')
            and _contains(
                character_settings_dialog,
                "BuildCharacterSettingsDialog",
                "Chummer5CharacterSettingsRuntimeContractGenerated.Fields",
                "SaveAndCloseActionId",
            )
            and _contains(
                profiles,
                "TryApplyVisibleFields",
                "SaveAs(",
                "RestoreDefaults(",
                "SerializeCatalog",
            )
            and _contains(
                dialog_coordinator,
                "ApplyCharacterSettings",
                "SaveAndCloseActionId",
                "CharacterSettingsCatalogJson",
            )
            and _contains(
                profile_tests,
                "Assert.AreEqual(150, definitions.Count)",
                "Every_legacy_value_control_round_trips_through_chummer5_settings_xml",
                "Profile_actions_preserve_ids_names_xml_and_a_deterministic_fallback",
            )
        )
        e2e_scripted = _contains(
            e2e_driver,
            "command-action-character-settings",
            "dialog-field-charactersettingscontrol-chkdontusecyberlimbcalculation",
            "dialog-field-charactersettingscontrol-tresourcebook",
            "dialog-field-charactersettingscontrol-chkenforcecapacity",
            "dialog-field-charactersettingscontrol-nudnuyendecimalsminimum",
            "dialog-field-charactersettingscontrol-nudkarmamysticadeptpowerpoint",
            "dialog-field-charactersettingscontrol-trecustomdatadirectories",
            "dialog-field-charactersettingscontrol-chknoarmorencumbrance",
            "dialog-field-charactersettingscontrol-cbobuildmethod",
            "dialog-action-save-and-close",
            '"controls": control_proofs',
            '"allValueControlsEdited": "pass"',
            '"allValueControlsCatalogPersisted": "pass"',
            '"allValueControlsRestartUiReadback": "pass"',
            '"processRestartUiReadback": "pass"',
        )
        phone_e2e = (
            character_settings_phone_e2e_receipt
            if implementation_complete and e2e_scripted
            else None
        )
        value_control_proofs = phone_e2e.get("controlProofs") if phone_e2e is not None else None
        value_control_proof = (
            value_control_proofs.get(control)
            if isinstance(value_control_proofs, dict)
            else None
        )
        value_exact_api36 = phone_e2e is not None and (
            isinstance(value_control_proof, dict)
            or control in CHARACTER_SETTINGS_EXACT_API36_ACTIONS
        )
        action_control_proofs = (
            character_settings_actions_phone_e2e_receipt.get("controlProofs")
            if character_settings_actions_phone_e2e_receipt is not None
            else None
        )
        action_control_proof = (
            action_control_proofs.get(control)
            if isinstance(action_control_proofs, dict)
            else None
        )
        action_exact_api36 = isinstance(action_control_proof, dict)
        exact_api36 = value_exact_api36 or action_exact_api36
        exact_receipt = (
            character_settings_actions_phone_e2e_receipt
            if action_exact_api36
            else phone_e2e
            if value_exact_api36
            else None
        )
        exact_control_proof = (
            action_control_proof
            if action_exact_api36
            else value_control_proof
        )
        base_receipt_e2e = (
            {key: value for key, value in phone_e2e.items() if key != "controlProofs"}
            if phone_e2e is not None
            else None
        )
        receipt_e2e = (
            {key: value for key, value in exact_receipt.items() if key != "controlProofs"}
            if exact_receipt is not None
            else None
        )
        automation_id = action_automation_id or (
            f"dialog-field-charactersettingscontrol-{_android_token(control)}"
        )
        source_refs = [
            "src/Chummer.Android/Native/NativeCommandPage.cs",
            "src/Chummer.Android/Native/NativeDialogPage.cs",
            "src/Chummer.Android/Native/RunnerSessionCoordinator.cs",
            "chummer-presentation/Chummer.Presentation/Overview/DesktopDialogFactory.CharacterSettings.cs",
            "chummer-presentation/Chummer.Presentation/Overview/Chummer5CharacterSettingsProfiles.cs",
            "chummer-presentation/Chummer.Presentation/Overview/Chummer5CharacterSettingsRuntimeContract.Generated.cs",
            "chummer-presentation/Chummer.Presentation/Overview/DialogCoordinator.cs",
            "chummer-presentation/Chummer.Tests/Presentation/Chummer5CharacterSettingsProfilesTests.cs",
            "tests/run_api36_character_settings_e2e.py",
            "tests/run_api36_character_settings_actions_e2e.py",
        ]
        representative_e2e = None
        if base_receipt_e2e is not None:
            representative_e2e = {
                **base_receipt_e2e,
                "status": "section_representative_api36",
            }
        exact_e2e = None
        if receipt_e2e is not None and exact_api36:
            exact_e2e = {
                **receipt_e2e,
                **(
                    {"controlProof": exact_control_proof}
                    if isinstance(exact_control_proof, dict)
                    else {}
                ),
            }
        return {
            "status": (
                "implemented_verified_api36"
                if exact_api36
                else "implemented_pending_emulator"
                if implementation_complete
                else "missing"
            ),
            "route": "Build > Actions > Character Settings",
            "surface": "NativeDialogPage",
            "automationId": automation_id,
            "coverageLimit": (
                "this exact legacy control was mutated and read back after process restart on API 36"
                if exact_api36
                else "all 150 value controls round-trip mechanically and every section/control kind is device-proven, but this exact legacy control still needs an individual API 36 mutation"
            ),
            "sourceRefs": source_refs,
            "presenterMutation": "Chummer5CharacterSettingsProfiles / DialogCoordinator.ApplyCharacterSettings",
            "persistenceAssertion": (
                f"the active settings profile retains the {control} value or equivalent collection/profile operation after save, reopen, and process restart"
            ),
            "e2e": exact_e2e if exact_api36 else representative_e2e or {
                "status": "scripted_not_executed" if e2e_scripted else "missing",
                "ref": e2e_driver.relative_to(REPO_ROOT).as_posix() if e2e_scripted else None,
            },
        }
    if class_name == "SelectBuildMethod" and control in {
        "cboCharacterSetting",
        "chkIgnoreRules",
        "cmdOK",
    }:
        native_dialog = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "NativeDialogPage.cs"
        build_page = native_dialog.with_name("BuildPage.cs")
        e2e_driver = REPO_ROOT / "tests" / "run_api36_new_character_settings_e2e.py"
        dialog_factory = presentation_root / "Chummer.Presentation" / "Overview" / "DesktopDialogFactory.cs"
        dialog_coordinator = presentation_root / "Chummer.Presentation" / "Overview" / "DialogCoordinator.cs"
        implementation_complete = (
            _contains(native_dialog, 'AutomationId = $"dialog-field-{Token(field.Id)}"', 'AutomationId = $"dialog-action-{Token(action.Id)}"')
            and _contains(build_page, 'NativeTheme.Metric("Character Setting"', "Coordinator.State.Rules?.Settings")
            and _contains(dialog_factory, '"newCharacterSetting"', '"newCharacterIgnoreRules"', '"newCharacterWorkflowSetting"', '"newCharacterWorkflowIgnoreRules"')
            and _contains(dialog_coordinator, '"newCharacterSetting"', '"newCharacterIgnoreRules"', '"settings"', '"ignorerules"', "CompleteNewCharacterWorkflowAsync")
        )
        e2e_scripted = _contains(
            e2e_driver,
            "dialog-field-newcharactersetting",
            "dialog-field-newcharacterignorerules",
            "dialog-action-create-character",
            '"workspaceBuildSettingsPersisted": "pass"',
            '"processRestartBuildSettingsPersistence": "pass"',
        )
        phone_e2e = (
            new_character_settings_phone_e2e_receipt
            if implementation_complete and e2e_scripted
            else None
        )
        automation_ids = {
            "cboCharacterSetting": "dialog-field-newcharactersetting",
            "chkIgnoreRules": "dialog-field-newcharacterignorerules",
            "cmdOK": "dialog-action-create-character",
        }
        assertions = {
            "cboCharacterSetting": "character/settings equals the submitted setting after workspace reopen and process restart",
            "chkIgnoreRules": "character/ignorerules remains True after workspace reopen and process restart",
            "cmdOK": "the committed creation workflow produces a durable workspace with the selected build settings",
        }
        source_refs = [
            "src/Chummer.Android/Native/NativeDialogPage.cs",
            "src/Chummer.Android/Native/BuildPage.cs",
            "chummer-presentation/Chummer.Presentation/Overview/DesktopDialogFactory.cs",
            "chummer-presentation/Chummer.Presentation/Overview/DialogCoordinator.cs",
            "tests/run_api36_new_character_settings_e2e.py",
        ]
        return {
            "status": (
                "implemented_verified_api36"
                if phone_e2e is not None
                else "implemented_pending_emulator"
                if implementation_complete
                else "missing"
            ),
            "route": "Home > New runner > Select Build Method",
            "surface": "NativeDialogPage",
            "automationId": automation_ids[control],
            "sourceRefs": source_refs,
            "presenterMutation": "DialogCoordinator.CreateCharacterFromDialogAsync / CompleteNewCharacterWorkflowAsync",
            "persistenceAssertion": assertions[control],
            "e2e": phone_e2e or {
                "status": "scripted_not_executed" if e2e_scripted else "missing",
                "ref": e2e_driver.relative_to(REPO_ROOT).as_posix() if e2e_scripted else None,
            },
        }
    if class_name == "SelectMetatypeKarma" and control in {
        "txtSearch",
        "cboCategory",
        "lstMetatypes",
        "cboMetavariant",
        "chkPossessionBased",
        "cboPossessionMethod",
        "nudForce",
        "cmdOK",
    }:
        native_dialog = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "NativeDialogPage.cs"
        build_page = native_dialog.with_name("BuildPage.cs")
        e2e_driver = REPO_ROOT / "tests" / "run_api36_new_character_karma_e2e.py"
        helper_driver = REPO_ROOT / "tests" / "run_api36_new_character_priority_e2e.py"
        dialog_factory = presentation_root / "Chummer.Presentation" / "Overview" / "DesktopDialogFactory.cs"
        dialog_coordinator = presentation_root / "Chummer.Presentation" / "Overview" / "DialogCoordinator.cs"
        implementation_complete = (
            _contains(
                native_dialog,
                'AutomationId = $"dialog-field-{Token(field.Id)}"',
                'AutomationId = $"dialog-action-{Token(action.Id)}"',
                "UpdateFieldAsync",
                "RequiresStructuralRerender",
                "Render(next)",
            )
            and _contains(
                build_page,
                'NativeTheme.Metric("Metatype"',
                "Coordinator.State.Profile?.Metatype",
                'NativeTheme.Metric("Metavariant"',
                "Coordinator.State.Profile?.Metavariant",
            )
            and _contains(
                dialog_factory,
                'NewCharacterKarmaWorkflowDialogId = "dialog.new_character.karma_workflow"',
                'NewCharacterKarmaMetatypeSearchFieldId = "newCharacterMetatypeSearch"',
                '"newCharacterMetatypeCategory"',
                '"newCharacterMetatype"',
                "NewCharacterMetavariantFieldId",
                "NewCharacterForceFieldId",
                "NewCharacterPossessionBasedFieldId",
                "NewCharacterPossessionMethodFieldId",
                '"complete_new_character_workflow"',
                "BuildNewCharacterKarmaWorkflowDialog",
                "FilterKarmaMetatypeOptions",
                "RebuildNewCharacterKarmaWorkflowDialog",
            )
            and _contains(
                dialog_coordinator,
                '"dialog.new_character.karma_workflow"',
                '"metatypecategory"',
                '"metatype"',
                '"metavariant"',
                '"force"',
                '"possessionmethod"',
                '"critterpowers"',
                "TryValidateNewCharacterSpiritSelection",
                "ApplySpiritSelection",
                "CompleteNewCharacterWorkflowAsync",
            )
        )
        e2e_scripted = _contains(
            e2e_driver,
            "dialog-field-newcharactermetatypesearch",
            "dialog-field-newcharactermetatypecategory",
            "dialog-field-newcharactermetatype",
            "dialog-field-newcharactermetavariant",
            "dialog-field-newcharacterforce",
            "dialog-field-newcharacterpossessionbased",
            "dialog-field-newcharacterpossessionmethod",
            "dialog-action-complete-new-character-workflow",
            '"metatypeSearchFiltered": "pass"',
            '"workspaceKarmaPersisted": "pass"',
            '"processRestartKarmaPersistence": "pass"',
            '"workspaceSpiritPossessionPersisted": "pass"',
            '"processRestartSpiritPossessionPersistence": "pass"',
        ) and _contains(helper_driver, "def select_option", "def workspace_payloads")
        phone_e2e = (
            new_character_karma_phone_e2e_receipt
            if implementation_complete and e2e_scripted
            else None
        )
        automation_ids = {
            "txtSearch": "dialog-field-newcharactermetatypesearch",
            "cboCategory": "dialog-field-newcharactermetatypecategory",
            "lstMetatypes": "dialog-field-newcharactermetatype",
            "cboMetavariant": "dialog-field-newcharactermetavariant",
            "chkPossessionBased": "dialog-field-newcharacterpossessionbased",
            "cboPossessionMethod": "dialog-field-newcharacterpossessionmethod",
            "nudForce": "dialog-field-newcharacterforce",
            "cmdOK": "dialog-action-complete-new-character-workflow",
        }
        assertions = {
            "txtSearch": "the entered search narrows the rendered metatype picker to Elf before the selected result is committed",
            "cboCategory": "character/metatypecategory equals the submitted Karma category after workspace reopen and process restart",
            "lstMetatypes": "character/metatype equals the submitted Karma metatype after workspace reopen, UI readback, and process restart",
            "cboMetavariant": "character/metavariant equals the submitted Karma metavariant after workspace reopen, UI readback, and process restart",
            "chkPossessionBased": "the enabled Karma possess-based tradition writes the selected possession method and matching critter power after workspace reopen and process restart",
            "cboPossessionMethod": "character/possessionmethod and the matching Chummer5 critterpower retain the submitted Karma possession method after process restart",
            "nudForce": "character/force equals the submitted Karma force after workspace reopen, UI readback, and process restart",
            "cmdOK": "the committed metatype-Karma workflow produces a durable workspace with the selected metatype, metavariant, Force, and possession state",
        }
        source_refs = [
            "src/Chummer.Android/Native/NativeDialogPage.cs",
            "src/Chummer.Android/Native/BuildPage.cs",
            "chummer-presentation/Chummer.Presentation/Overview/DesktopDialogFactory.cs",
            "chummer-presentation/Chummer.Presentation/Overview/DialogCoordinator.cs",
            "tests/run_api36_new_character_karma_e2e.py",
            "tests/run_api36_new_character_priority_e2e.py",
        ]
        return {
            "status": (
                "implemented_verified_api36"
                if phone_e2e is not None
                else "implemented_pending_emulator"
                if implementation_complete
                else "missing"
            ),
            "route": "Home > New runner > Select Build Method > Karma > Select Metatype",
            "surface": "NativeDialogPage",
            "automationId": automation_ids[control],
            "sourceRefs": source_refs,
            "presenterMutation": "DialogCoordinator.CompleteNewCharacterWorkflowAsync",
            "persistenceAssertion": assertions[control],
            "e2e": phone_e2e or {
                "status": "scripted_not_executed" if e2e_scripted else "missing",
                "ref": e2e_driver.relative_to(REPO_ROOT).as_posix() if e2e_scripted else None,
            },
        }
    if class_name == "SelectMetatypePriority" and control in {
        "cboCategory",
        "lstMetatypes",
        "cboMetavariant",
        "cboHeritage",
        "cboAttributes",
        "cboTalent",
        "cboSkills",
        "cboResources",
        "cboTalents",
        "cboSkill1",
        "cboSkill2",
        "cboSkill3",
        "chkPossessionBased",
        "cboPossessionMethod",
        "nudForce",
        "cmdOK",
    }:
        native_dialog = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "NativeDialogPage.cs"
        build_page = native_dialog.with_name("BuildPage.cs")
        e2e_driver = REPO_ROOT / "tests" / "run_api36_new_character_priority_e2e.py"
        dialog_factory = presentation_root / "Chummer.Presentation" / "Overview" / "DesktopDialogFactory.cs"
        dialog_coordinator = presentation_root / "Chummer.Presentation" / "Overview" / "DialogCoordinator.cs"
        implementation_complete = (
            _contains(native_dialog, 'AutomationId = $"dialog-field-{Token(field.Id)}"', 'AutomationId = $"dialog-action-{Token(action.Id)}"')
            and _contains(
                build_page,
                'NativeTheme.Metric("Metatype"',
                "Coordinator.State.Profile?.Metatype",
                'NativeTheme.Metric("Metavariant"',
                "Coordinator.State.Profile?.Metavariant",
            )
            and _contains(
                dialog_factory,
                '"newCharacterMetatypeCategory"',
                '"newCharacterMetatype"',
                '"newCharacterMetavariant"',
                '"newCharacterPriorityHeritage"',
                '"newCharacterPriorityAttributes"',
                '"newCharacterPriorityTalent"',
                '"newCharacterPrioritySkills"',
                '"newCharacterPriorityResources"',
                '"newCharacterPriorityTalentChoice"',
                '"newCharacterPrioritySkillChoice1"',
                '"newCharacterPrioritySkillChoice2"',
                '"newCharacterPrioritySkillChoice3"',
                '"newCharacterForce"',
                '"newCharacterPossessionBased"',
                '"newCharacterPossessionMethod"',
                '"complete_new_character_workflow"',
            )
            and _contains(
                dialog_coordinator,
                '"metatypecategory"',
                '"metatype"',
                '"metavariant"',
                '"prioritymetatype"',
                '"priorityattributes"',
                '"priorityspecial"',
                '"priorityskills"',
                '"priorityresources"',
                '"prioritytalent"',
                '"newCharacterPrioritySkillChoice1"',
                '"newCharacterPrioritySkillChoice2"',
                '"newCharacterPrioritySkillChoice3"',
                '"force"',
                '"possessionmethod"',
                '"critterpowers"',
                'new XElement("priorityskill", skill)',
                "CompleteNewCharacterWorkflowAsync",
            )
        )
        e2e_scripted = _contains(
            e2e_driver,
            "dialog-field-newcharactermetatypecategory",
            "dialog-field-newcharactermetatype",
            "dialog-field-newcharactermetavariant",
            "dialog-field-newcharacterpriorityheritage",
            "dialog-field-newcharacterpriorityattributes",
            "dialog-field-newcharacterprioritytalent",
            "dialog-field-newcharacterpriorityskills",
            "dialog-field-newcharacterpriorityresources",
            "dialog-field-newcharacterprioritytalentchoice",
            "dialog-field-newcharacterpriorityskillchoice1",
            "dialog-field-newcharacterpriorityskillchoice2",
            "dialog-field-newcharacterpriorityskillchoice3",
            "dialog-field-newcharacterforce",
            "dialog-field-newcharacterpossessionbased",
            "dialog-field-newcharacterpossessionmethod",
            "dialog-action-complete-new-character-workflow",
            '"metavariantEdited": "pass"',
            '"prioritySkillChoice1Edited": "pass"',
            '"prioritySkillChoice2Edited": "pass"',
            '"prioritySkillChoice3Edited": "pass"',
            '"forceEdited": "pass"',
            '"possessionBasedEnabled": "pass"',
            '"possessionMethodEdited": "pass"',
            '"metavariantUiReadback": "pass"',
            '"workspacePriorityPersisted": "pass"',
            '"processRestartPriorityPersistence": "pass"',
            '"workspaceSpiritPossessionPersisted": "pass"',
            '"processRestartSpiritPossessionPersistence": "pass"',
        )
        phone_e2e = (
            new_character_priority_phone_e2e_receipt
            if implementation_complete and e2e_scripted
            else None
        )
        automation_ids = {
            "cboCategory": "dialog-field-newcharactermetatypecategory",
            "lstMetatypes": "dialog-field-newcharactermetatype",
            "cboMetavariant": "dialog-field-newcharactermetavariant",
            "cboHeritage": "dialog-field-newcharacterpriorityheritage",
            "cboAttributes": "dialog-field-newcharacterpriorityattributes",
            "cboTalent": "dialog-field-newcharacterprioritytalent",
            "cboSkills": "dialog-field-newcharacterpriorityskills",
            "cboResources": "dialog-field-newcharacterpriorityresources",
            "cboTalents": "dialog-field-newcharacterprioritytalentchoice",
            "cboSkill1": "dialog-field-newcharacterpriorityskillchoice1",
            "cboSkill2": "dialog-field-newcharacterpriorityskillchoice2",
            "cboSkill3": "dialog-field-newcharacterpriorityskillchoice3",
            "chkPossessionBased": "dialog-field-newcharacterpossessionbased",
            "cboPossessionMethod": "dialog-field-newcharacterpossessionmethod",
            "nudForce": "dialog-field-newcharacterforce",
            "cmdOK": "dialog-action-complete-new-character-workflow",
        }
        assertions = {
            "cboCategory": "character/metatypecategory equals the submitted category after workspace reopen and process restart",
            "lstMetatypes": "character/metatype equals the submitted metatype after workspace reopen and process restart",
            "cboMetavariant": "character/metavariant equals the submitted metavariant after workspace reopen, UI readback, and process restart",
            "cboHeritage": "character/prioritymetatype equals the submitted priority after workspace reopen and process restart",
            "cboAttributes": "character/priorityattributes equals the submitted priority after workspace reopen and process restart",
            "cboTalent": "character/priorityspecial equals the submitted priority after workspace reopen and process restart",
            "cboSkills": "character/priorityskills equals the submitted priority after workspace reopen and process restart",
            "cboResources": "character/priorityresources equals the submitted priority after workspace reopen and process restart",
            "cboTalents": "character/prioritytalent equals the submitted talent after workspace reopen and process restart",
            "cboSkill1": "the nested character/priorityskills/priorityskill sequence retains the submitted first free skill after workspace reopen and process restart",
            "cboSkill2": "the nested character/priorityskills/priorityskill sequence retains the submitted second free skill in order after workspace reopen and process restart",
            "cboSkill3": "the nested character/priorityskills/priorityskill sequence retains the submitted third free skill in order after workspace reopen and process restart",
            "chkPossessionBased": "the enabled possess-based tradition writes the selected possession method and matching critter power after workspace reopen and process restart",
            "cboPossessionMethod": "character/possessionmethod and the matching Chummer5 critterpower retain the submitted Possession or Inhabitation method after workspace reopen and process restart",
            "nudForce": "character/force equals the submitted force after workspace reopen, UI readback, and process restart",
            "cmdOK": "the committed metatype-priority workflow produces a durable workspace with every selected priority value, metavariant, and ordered free-skill choice",
        }
        source_refs = [
            "src/Chummer.Android/Native/NativeDialogPage.cs",
            "src/Chummer.Android/Native/BuildPage.cs",
            "chummer-presentation/Chummer.Presentation/Overview/DesktopDialogFactory.cs",
            "chummer-presentation/Chummer.Presentation/Overview/DialogCoordinator.cs",
            "tests/run_api36_new_character_priority_e2e.py",
        ]
        return {
            "status": (
                "implemented_verified_api36"
                if phone_e2e is not None
                else "implemented_pending_emulator"
                if implementation_complete
                else "missing"
            ),
            "route": "Home > New runner > Select Build Method > Select Metatype Priority",
            "surface": "NativeDialogPage",
            "automationId": automation_ids[control],
            "sourceRefs": source_refs,
            "presenterMutation": "DialogCoordinator.CompleteNewCharacterWorkflowAsync",
            "persistenceAssertion": assertions[control],
            "e2e": phone_e2e or {
                "status": "scripted_not_executed" if e2e_scripted else "missing",
                "ref": e2e_driver.relative_to(REPO_ROOT).as_posix() if e2e_scripted else None,
            },
        }
    if class_name in {"ContactControl", "PetControl"} and control in {
        "tsAttachCharacter",
        "tsRemoveCharacter",
    }:
        phone_page = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "CollectionEditorPages.cs"
        tablet_page = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "TabletBuildPage.cs"
        coordinator = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "RunnerSessionCoordinator.cs"
        staging = REPO_ROOT / "src" / "Chummer.Android" / "Platform" / "IAndroidLinkedCharacterFileService.cs"
        e2e_driver = REPO_ROOT / "tests" / "run_api36_linked_runner_e2e.py"
        tablet_e2e_driver = REPO_ROOT / "tests" / "run_api36_editing_e2e.py"
        codec = (
            WORKSPACE_ROOT
            / "chummer-core-engine"
            / "Chummer.Infrastructure"
            / "Xml"
            / "Chummer5LinkedDocumentCodec.cs"
        )
        state = presentation_root / "Chummer.Presentation" / "Overview" / "WorkspaceCollectionEditorState.cs"
        request = presentation_root / "Chummer.Presentation" / "Overview" / "WorkspaceCollectionMutationRequest.cs"
        mutation = presentation_root / "Chummer.Presentation" / "Overview" / "WorkspaceXmlMutationCatalog.cs"
        projector = presentation_root / "Chummer.Presentation" / "Overview" / "WorkspaceCollectionEditorProjector.cs"
        presenter = presentation_root / "Chummer.Presentation" / "Overview" / "CharacterOverviewPresenter.WorkspaceMutations.cs"
        attach = control == "tsAttachCharacter"
        kind = "Contact" if class_name == "ContactControl" else "Pet"
        operation = "Attach" if attach else "Remove"
        phone_token = "attach" if attach else "remove"
        shared = (
            _contains(codec, '".chum5"', '".chum5lz"', "LzmaStream.Create")
            and _contains(staging, "ICharacterLinkedDocumentCodec", 'DirectoryName = "linked-characters"', "File.Move")
            and _contains(state, "WorkspaceLinkedCharacterState", "LinkedCharacter")
            and _contains(
                request,
                "WorkspaceSetLinkedCharacterRequest",
                "WorkspaceRemoveLinkedCharacterRequest",
            )
            and _contains(
                mutation,
                "ApplyLinkedCharacterMutation",
                "ApplyRemoveLinkedCharacterMutation",
                "ResolveLinkedCharacterTarget",
            )
            and _contains(projector, "ProjectLinkedCharacter", "WorkspaceCollectionKind.Pet")
            and _contains(presenter, "ApplyCollectionMutationAsync", "ApplyWorkspaceXmlMutationAsync")
            and _contains(
                coordinator,
                "AttachLinkedCharacterAsync",
                "RemoveLinkedCharacterAsync",
                "_linkedCharacters.DeleteOwnedAsync",
            )
        )
        phone_implemented = shared and _contains(
            phone_page,
            f"collection-linked-{phone_token}-",
            f"Coordinator.{operation}LinkedCharacterAsync",
        )
        tablet_implemented = shared and _contains(
            tablet_page,
            f'"tablet-linked-{phone_token}"',
            f"Coordinator.{operation}LinkedCharacterAsync",
        )
        e2e_marker = (
            f'"{kind.lower()}LinkedRunnerAttachPersisted": "pass"'
            if attach
            else f'"{kind.lower()}LinkedRunnerRemoveRestoredIdentity": "pass"'
        )
        e2e_scripted = _contains(
            e2e_driver,
            '"journey": "linked-runner"',
            "assert_link_persisted_then_remove",
            "assert_unlinked_after_restart",
            '"processRestartAttachPersistence": "pass"',
            '"processRestartRemovePersistence": "pass"',
        )
        tablet_e2e_scripted = _contains(
            tablet_e2e_driver,
            "assert_linked_identity",
            "assert_link_persisted_then_remove",
            e2e_marker,
        )
        phone_e2e = linked_runner_phone_e2e_receipt if phone_implemented and e2e_scripted else None
        control_proofs = phone_e2e.get("controlProofs") if phone_e2e is not None else None
        control_proof = (
            control_proofs.get(f"{class_name}.{control}")
            if isinstance(control_proofs, dict)
            else None
        )
        exact_api36 = isinstance(control_proof, dict)
        receipt_e2e = (
            {key: value for key, value in phone_e2e.items() if key != "controlProofs"}
            if phone_e2e is not None
            else None
        )
        source_refs = [
            "src/Chummer.Android/Native/CollectionEditorPages.cs",
            "src/Chummer.Android/Native/TabletBuildPage.cs",
            "src/Chummer.Android/Native/RunnerSessionCoordinator.cs",
            "src/Chummer.Android/Platform/IAndroidLinkedCharacterFileService.cs",
            "chummer-core-engine/Chummer.Infrastructure/Xml/Chummer5LinkedDocumentCodec.cs",
            "chummer-presentation/Chummer.Presentation/Overview/WorkspaceCollectionEditorProjector.cs",
            "chummer-presentation/Chummer.Presentation/Overview/WorkspaceCollectionEditorState.cs",
            "chummer-presentation/Chummer.Presentation/Overview/WorkspaceCollectionMutationRequest.cs",
            "chummer-presentation/Chummer.Presentation/Overview/WorkspaceXmlMutationCatalog.cs",
        ]
        presenter_mutation = (
            "ICharacterOverviewPresenter.ApplyCollectionMutationAsync / "
            f"Workspace{operation}LinkedCharacterRequest on WorkspaceCollectionKind.{kind}"
        )
        return {
            "status": (
                "implemented_verified_api36"
                if exact_api36
                else "implemented_pending_emulator"
                if phone_implemented
                else "missing"
            ),
            "route": f"Build > Relationships > {kind}s > selected {kind.lower()} > Linked runner",
            "surface": "CollectionItemEditorPage",
            "automationId": f"collection-linked-{phone_token}-{{stable-target}}",
            "sourceRefs": source_refs,
            "presenterMutation": presenter_mutation,
            "persistenceAssertion": (
                f"selected stable {kind} guid retains governed file/relative link state and "
                "linked identity after reopen/process restart, or restores its saved identity after unlink"
            ),
            "e2e": {
                **receipt_e2e,
                "controlProof": control_proof,
            } if exact_api36 and receipt_e2e is not None else {
                "status": "scripted_not_executed" if e2e_scripted else "missing",
                "ref": e2e_driver.relative_to(REPO_ROOT).as_posix() if e2e_scripted else None,
            },
            "tablet": {
                "status": "implemented_pending_emulator" if tablet_implemented else "missing",
                "surface": f"TabletBuildPage persistent {kind.lower()} inspector",
                "automationId": f"tablet-linked-{phone_token}",
                "sourceRefs": source_refs,
            },
            "tabletE2e": {
                "status": "scripted_not_executed" if tablet_e2e_scripted else "missing",
                "ref": tablet_e2e_driver.relative_to(REPO_ROOT).as_posix() if tablet_e2e_scripted else None,
            },
        }
    if (
        class_name == "CharacterCareer"
        and control in LEGACY_CAREER_COLLECTION_DELETE_CONTROLS
    ):
        kind, section_label = LEGACY_CAREER_COLLECTION_DELETE_CONTROLS[control]
        expected_handler = f"{control}_Click"
        if not any(event.get("handler") == expected_handler for event in legacy.get("events", [])):
            return None

        phone_page = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "CollectionEditorPages.cs"
        phone_route = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "BuildFlowPages.cs"
        coordinator = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "RunnerSessionCoordinator.cs"
        request = presentation_root / "Chummer.Presentation" / "Overview" / "WorkspaceCollectionMutationRequest.cs"
        mutation = presentation_root / "Chummer.Presentation" / "Overview" / "WorkspaceXmlMutationCatalog.cs"
        projector = presentation_root / "Chummer.Presentation" / "Overview" / "WorkspaceCollectionEditorProjector.cs"
        presenter = presentation_root / "Chummer.Presentation" / "Overview" / "CharacterOverviewPresenter.WorkspaceMutations.cs"
        shared = (
            _contains(request, "WorkspaceCollectionItemTarget", "WorkspaceDeleteCollectionItemRequest")
            and _contains(projector, f"WorkspaceCollectionKind.{kind}")
            and _contains(mutation, "ApplyDeleteMutation", f"WorkspaceCollectionKind.{kind}")
            and _contains(coordinator, "ApplyCollectionMutationAsync")
            and _contains(presenter, "ApplyCollectionMutationAsync", "ApplyWorkspaceXmlMutationAsync")
        )
        phone_implemented = shared and _contains(
            phone_route,
            "AddCollectionRows",
            "CollectionItemEditorPage",
        ) and _contains(
            phone_page,
            "collection-delete-",
            "WorkspaceDeleteCollectionItemRequest",
            "item.CanDelete",
        )
        return {
            "status": "implemented_pending_emulator" if phone_implemented else "missing",
            "route": f"Build > {section_label} > selected item > Delete item",
            "surface": "CollectionItemEditorPage",
            "automationId": "collection-delete-{stable-target}",
            "sourceRefs": [
                "src/Chummer.Android/Native/BuildFlowPages.cs",
                "src/Chummer.Android/Native/CollectionEditorPages.cs",
                "src/Chummer.Android/Native/RunnerSessionCoordinator.cs",
                "chummer-presentation/Chummer.Presentation/Overview/WorkspaceCollectionEditorProjector.cs",
                "chummer-presentation/Chummer.Presentation/Overview/WorkspaceCollectionMutationRequest.cs",
                "chummer-presentation/Chummer.Presentation/Overview/WorkspaceXmlMutationCatalog.cs",
            ],
            "presenterMutation": (
                "ICharacterOverviewPresenter.ApplyCollectionMutationAsync / "
                f"WorkspaceDeleteCollectionItemRequest on WorkspaceCollectionKind.{kind}"
            ),
            "persistenceAssertion": (
                f"selected stable {kind} guid is absent from the saved runner after reopen and process restart"
            ),
            "e2e": {"status": "missing", "ref": None},
            "tablet": {
                "status": "missing",
                "surface": None,
                "automationId": None,
                "sourceRefs": [],
            },
            "tabletE2e": {"status": "missing", "ref": None},
        }
    if class_name == "PetControl" and (control in PET_TEXT_FIELDS or control == "cmdDelete"):
        phone_page = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "CollectionEditorPages.cs"
        tablet_page = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "TabletBuildPage.cs"
        coordinator = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "RunnerSessionCoordinator.cs"
        e2e_driver = REPO_ROOT / "tests" / "run_api36_editing_e2e.py"
        pet_semantics = (
            WORKSPACE_ROOT
            / "chummer-core-engine"
            / "Chummer.Contracts"
            / "Characters"
            / "CharacterPetEditSemantics.cs"
        )
        request = presentation_root / "Chummer.Presentation" / "Overview" / "WorkspaceCollectionMutationRequest.cs"
        mutation = presentation_root / "Chummer.Presentation" / "Overview" / "WorkspaceXmlMutationCatalog.cs"
        projector = presentation_root / "Chummer.Presentation" / "Overview" / "WorkspaceCollectionEditorProjector.cs"
        presenter = presentation_root / "Chummer.Presentation" / "Overview" / "CharacterOverviewPresenter.WorkspaceMutations.cs"
        dialog = presentation_root / "Chummer.Presentation" / "Overview" / "DialogCoordinator.cs"

        common_shared = (
            _contains(pet_semantics, "CharacterPetEditSemanticsResolver", "IdentityEditable", "CanDelete")
            and _contains(request, "public enum WorkspaceCollectionKind", "    Pet,")
            and _contains(projector, '"pets"', "WorkspaceCollectionKind.Pet")
            and _contains(
                mutation,
                "ResolvePetSemantics",
                "IsExpectedContactRecordType",
                "WorkspaceCollectionKind.Pet",
                "AddPet",
            )
            and _contains(dialog, "WorkspaceQuickAddKinds.Pet", "IsPetSection")
            and _contains(coordinator, "ApplyCollectionMutationAsync")
            and _contains(presenter, "ApplyCollectionMutationAsync", "ApplyWorkspaceXmlMutationAsync")
        )
        e2e_scripted = _contains(
            e2e_driver,
            "build-action-tab-relationships-pets",
            "tablet-build-action-tab-relationships-pets",
            '"petInvalidNameRejected": "pass"',
            '"petEditPersisted": "pass"',
            '"petDeletePersisted": "pass"',
            '"processRestartPetPersistence": "pass"',
        )
        source_refs = [
            "src/Chummer.Android/Native/CollectionEditorPages.cs",
            "src/Chummer.Android/Native/TabletBuildPage.cs",
            "src/Chummer.Android/Native/RunnerSessionCoordinator.cs",
            "chummer-core-engine/Chummer.Contracts/Characters/CharacterPetEditSemantics.cs",
            "chummer-presentation/Chummer.Presentation/Overview/DialogCoordinator.cs",
            "chummer-presentation/Chummer.Presentation/Overview/WorkspaceCollectionEditorProjector.cs",
            "chummer-presentation/Chummer.Presentation/Overview/WorkspaceCollectionMutationRequest.cs",
            "chummer-presentation/Chummer.Presentation/Overview/WorkspaceXmlMutationCatalog.cs",
        ]

        if control in PET_TEXT_FIELDS:
            field, xml_element, token = PET_TEXT_FIELDS[control]
            shared = common_shared and _contains(
                request,
                "public enum WorkspaceCollectionTextField",
                f"    {field}",
            ) and _contains(
                mutation,
                f"WorkspaceCollectionTextField.{field}",
                f'"{xml_element}"',
            ) and _contains(
                projector,
                f"WorkspaceCollectionTextField.{field}",
            )
            phone_implemented = shared and _contains(
                phone_page,
                "collection-field-",
                "original.IsEnabled",
                "WorkspacePatchCollectionItemRequest",
            )
            tablet_implemented = shared and _contains(
                tablet_page,
                "tablet-field-",
                "original.IsEnabled",
                "WorkspacePatchCollectionItemRequest",
            )
            presenter_mutation = (
                "ICharacterOverviewPresenter.ApplyCollectionMutationAsync / "
                f"WorkspaceCollectionTextField.{field} on WorkspaceCollectionKind.Pet"
            )
            phone_automation_id = f"collection-field-{token}-{{stable-target}}"
            tablet_automation_id = f"tablet-field-{token}"
        else:
            xml_element = "contact[type=Pet]"
            shared = common_shared and _contains(mutation, "ApplyDeleteMutation", "ResolvePetSemantics")
            phone_implemented = shared and _contains(phone_page, "collection-delete-", "item.CanDelete")
            tablet_implemented = shared and _contains(tablet_page, '"tablet-inspector-delete"', "item.CanDelete")
            presenter_mutation = (
                "ICharacterOverviewPresenter.ApplyCollectionMutationAsync / "
                "WorkspaceDeleteCollectionItemRequest on WorkspaceCollectionKind.Pet"
            )
            phone_automation_id = "collection-delete-{stable-target}"
            tablet_automation_id = "tablet-inspector-delete"

        phone_e2e = contact_pet_e2e_receipts.get("phone") if e2e_scripted else None
        tablet_e2e = contact_pet_e2e_receipts.get("tablet") if e2e_scripted else None
        contact_pet_e2e_complete = bool(
            phone_implemented and tablet_implemented and phone_e2e and tablet_e2e
        )
        return {
            "status": (
                "implemented_verified_api36"
                if contact_pet_e2e_complete
                else "implemented_pending_emulator" if phone_implemented else "missing"
            ),
            "route": "Build > Relationships > Pets > selected pet",
            "surface": "CollectionItemEditorPage",
            "automationId": phone_automation_id,
            "sourceRefs": source_refs,
            "presenterMutation": presenter_mutation,
            "persistenceAssertion": (
                f"selected stable Pet guid retains {xml_element} after reopen and process restart"
            ),
            "e2e": phone_e2e or {
                "status": "scripted_not_executed" if e2e_scripted else "missing",
                "ref": "tests/run_api36_editing_e2e.py" if e2e_scripted else None,
            },
            "tablet": {
                "status": (
                    "implemented_verified_api36"
                    if contact_pet_e2e_complete
                    else "implemented_pending_emulator" if tablet_implemented else "missing"
                ),
                "surface": "TabletBuildPage persistent pet inspector",
                "automationId": tablet_automation_id,
                "sourceRefs": source_refs,
            },
            "tabletE2e": tablet_e2e or {
                "status": "scripted_not_executed" if e2e_scripted else "missing",
                "ref": "tests/run_api36_editing_e2e.py" if e2e_scripted else None,
            },
            "completionProven": contact_pet_e2e_complete,
        }
    if class_name == "ContactControl" and (
        control in CONTACT_TEXT_FIELDS
        or control in CONTACT_TOGGLE_FIELDS
        or control in CONTACT_RATING_FIELDS
        or control == "cmdDelete"
    ):
        phone_page = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "CollectionEditorPages.cs"
        tablet_page = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "TabletBuildPage.cs"
        coordinator = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "RunnerSessionCoordinator.cs"
        e2e_driver = REPO_ROOT / "tests" / "run_api36_editing_e2e.py"
        contact_semantics = (
            WORKSPACE_ROOT
            / "chummer-core-engine"
            / "Chummer.Contracts"
            / "Characters"
            / "CharacterContactEditSemantics.cs"
        )
        request = presentation_root / "Chummer.Presentation" / "Overview" / "WorkspaceCollectionMutationRequest.cs"
        mutation = presentation_root / "Chummer.Presentation" / "Overview" / "WorkspaceXmlMutationCatalog.cs"
        projector = presentation_root / "Chummer.Presentation" / "Overview" / "WorkspaceCollectionEditorProjector.cs"
        presenter = presentation_root / "Chummer.Presentation" / "Overview" / "CharacterOverviewPresenter.WorkspaceMutations.cs"

        common_shared = (
            _contains(contact_semantics, "CharacterContactEditSemanticsResolver", "ConnectionMaximum", "CanDelete")
            and _contains(request, "WorkspacePatchCollectionItemRequest", "public enum WorkspaceCollectionKind", "    Contact,")
            and _contains(projector, "WorkspaceContactEditorState", "WorkspaceCollectionKind.Contact")
            and _contains(mutation, "ResolveContactSemantics", "WorkspaceCollectionKind.Contact")
            and _contains(coordinator, "ApplyCollectionMutationAsync")
            and _contains(presenter, "ApplyCollectionMutationAsync", "ApplyWorkspaceXmlMutationAsync")
        )
        e2e_scripted = _contains(
            e2e_driver,
            '"contactEditPersisted": "pass"',
            '"contactInvalidBoundsRejected": "pass"',
            '"contactDeletePersisted": "pass"',
            "collection-contact-connection-",
            "tablet-contact-connection",
            "processRestartContactPersistence",
        )
        source_refs = [
            "src/Chummer.Android/Native/CollectionEditorPages.cs",
            "src/Chummer.Android/Native/TabletBuildPage.cs",
            "src/Chummer.Android/Native/RunnerSessionCoordinator.cs",
            "chummer-core-engine/Chummer.Contracts/Characters/CharacterContactEditSemantics.cs",
            "chummer-presentation/Chummer.Presentation/Overview/WorkspaceCollectionEditorProjector.cs",
            "chummer-presentation/Chummer.Presentation/Overview/WorkspaceCollectionMutationRequest.cs",
            "chummer-presentation/Chummer.Presentation/Overview/WorkspaceXmlMutationCatalog.cs",
        ]

        if control in CONTACT_TEXT_FIELDS:
            field, xml_element, token = CONTACT_TEXT_FIELDS[control]
            shared = common_shared and _contains(
                request,
                "public enum WorkspaceCollectionTextField",
                f"    {field}",
            ) and _contains(
                mutation,
                f"WorkspaceCollectionTextField.{field}",
                f'"{xml_element}"',
            ) and _contains(
                projector,
                f"WorkspaceCollectionTextField.{field}",
            )
            phone_implemented = shared and _contains(
                phone_page,
                "collection-field-",
                "original.IsEnabled",
                "WorkspacePatchCollectionItemRequest",
            )
            tablet_implemented = shared and _contains(
                tablet_page,
                "tablet-field-",
                "original.IsEnabled",
                "WorkspacePatchCollectionItemRequest",
            )
            presenter_mutation = (
                "ICharacterOverviewPresenter.ApplyCollectionMutationAsync / "
                f"WorkspaceCollectionTextField.{field}"
            )
            phone_automation_id = f"collection-field-{token}-{{stable-target}}"
            tablet_automation_id = f"tablet-field-{token}"
        elif control in CONTACT_TOGGLE_FIELDS:
            field, xml_element, token = CONTACT_TOGGLE_FIELDS[control]
            shared = common_shared and _contains(
                request,
                "public enum WorkspaceCollectionToggleField",
                f"    {field}",
            ) and _contains(
                mutation,
                f"WorkspaceCollectionToggleField.{field}",
                f'"{xml_element}"',
            ) and _contains(
                projector,
                f"WorkspaceCollectionToggleField.{field}",
            )
            phone_implemented = shared and _contains(
                phone_page,
                "collection-toggle-",
                "original.IsEnabled",
                "WorkspacePatchCollectionItemRequest",
            )
            tablet_implemented = shared and _contains(
                tablet_page,
                "tablet-toggle-",
                "original.IsEnabled",
                "WorkspacePatchCollectionItemRequest",
            )
            presenter_mutation = (
                "ICharacterOverviewPresenter.ApplyCollectionMutationAsync / "
                f"WorkspaceCollectionToggleField.{field}"
            )
            phone_automation_id = f"collection-toggle-{token}-{{stable-target}}"
            tablet_automation_id = f"tablet-toggle-{token}"
        elif control in CONTACT_RATING_FIELDS:
            field, xml_element, token = CONTACT_RATING_FIELDS[control]
            shared = common_shared and _contains(request, field) and _contains(
                mutation,
                f"Apply{field}Mutation",
                f'"{xml_element}"',
            ) and _contains(projector, "ProjectContact", "WorkspaceContactEditorState")
            phone_implemented = shared and _contains(
                phone_page,
                f"collection-contact-{token}-",
                field,
                "WorkspacePatchCollectionItemRequest",
            )
            tablet_implemented = shared and _contains(
                tablet_page,
                f'"tablet-contact-{token}"',
                field,
                "WorkspacePatchCollectionItemRequest",
            )
            presenter_mutation = (
                "ICharacterOverviewPresenter.ApplyCollectionMutationAsync / "
                f"WorkspacePatchCollectionItemRequest.{field}"
            )
            phone_automation_id = f"collection-contact-{token}-{{stable-target}}"
            tablet_automation_id = f"tablet-contact-{token}"
        else:
            xml_element = "contact"
            shared = common_shared and _contains(mutation, "ApplyDeleteMutation", "CanDelete")
            phone_implemented = shared and _contains(phone_page, "collection-delete-", "item.CanDelete")
            tablet_implemented = shared and _contains(tablet_page, '"tablet-inspector-delete"', "item.CanDelete")
            presenter_mutation = (
                "ICharacterOverviewPresenter.ApplyCollectionMutationAsync / "
                "WorkspaceDeleteCollectionItemRequest"
            )
            phone_automation_id = "collection-delete-{stable-target}"
            tablet_automation_id = "tablet-inspector-delete"

        scripted_for_control = e2e_scripted
        phone_e2e = contact_pet_e2e_receipts.get("phone") if scripted_for_control else None
        tablet_e2e = contact_pet_e2e_receipts.get("tablet") if scripted_for_control else None
        contact_pet_e2e_complete = bool(
            phone_implemented and tablet_implemented and phone_e2e and tablet_e2e
        )
        return {
            "status": (
                "implemented_verified_api36"
                if contact_pet_e2e_complete
                else "implemented_pending_emulator" if phone_implemented else "missing"
            ),
            "route": "Build > Relationships > Contacts > selected contact",
            "surface": "CollectionItemEditorPage",
            "automationId": phone_automation_id,
            "sourceRefs": source_refs,
            "presenterMutation": presenter_mutation,
            "persistenceAssertion": (
                f"selected stable Contact guid retains {xml_element} after reopen and process restart"
            ),
            "e2e": phone_e2e or {
                "status": "scripted_not_executed" if scripted_for_control else "missing",
                "ref": "tests/run_api36_editing_e2e.py" if scripted_for_control else None,
            },
            "tablet": {
                "status": (
                    "implemented_verified_api36"
                    if contact_pet_e2e_complete
                    else "implemented_pending_emulator" if tablet_implemented else "missing"
                ),
                "surface": "TabletBuildPage persistent contact inspector",
                "automationId": tablet_automation_id,
                "sourceRefs": source_refs,
            },
            "tabletE2e": tablet_e2e or {
                "status": "scripted_not_executed" if scripted_for_control else "missing",
                "ref": "tests/run_api36_editing_e2e.py" if scripted_for_control else None,
            },
            "completionProven": contact_pet_e2e_complete,
        }
    if class_name in {"CharacterCreate", "CharacterCareer"} and control in ORIGIN_FIELDS:
        xml_element, automation_id = ORIGIN_FIELDS[control]
        field_id = automation_id.removeprefix("origin-")
        android_page = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "OriginDossierPage.cs"
        coordinator = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "RunnerSessionCoordinator.cs"
        presenter = presentation_root / "Chummer.Presentation" / "Overview" / "CharacterOverviewPresenter.WorkspaceMutations.cs"
        implemented = (
            _contains(android_page, f'"{field_id}"', '$"origin-{id}"', "OriginDossierEditRequest")
            and _contains(coordinator, "ApplyOriginDossierEditAsync")
            and _contains(presenter, "ApplyOriginDossierEditAsync", "ApplyWorkspaceXmlMutationAsync")
        )
        e2e_driver = REPO_ROOT / "tests" / "run_api36_origin_dossier_e2e.py"
        e2e_scripted = _contains(
            e2e_driver,
            '"journey": "origin-dossier"',
            '"controls": control_proofs',
            '"creationWorkspaceXmlPersisted": "pass"',
            '"careerWorkspaceXmlPersisted": "pass"',
            '"creationProcessRestartUiReadback": "pass"',
            '"careerProcessRestartUiReadback": "pass"',
        )
        phone_e2e = (
            origin_dossier_phone_e2e_receipt
            if implemented and e2e_scripted
            else None
        )
        receipt_e2e = (
            {key: value for key, value in phone_e2e.items() if key != "controlProofs"}
            if phone_e2e is not None
            else None
        )
        control_proofs = phone_e2e.get("controlProofs") if phone_e2e is not None else None
        control_proof = (
            control_proofs.get(f"{class_name}.{control}")
            if isinstance(control_proofs, dict)
            else None
        )
        exact_api36 = isinstance(control_proof, dict)
        return {
            "status": (
                "implemented_verified_api36"
                if exact_api36
                else "implemented_pending_emulator"
                if implemented
                else "missing"
            ),
            "route": "Build > Origin dossier",
            "surface": "OriginDossierPage",
            "automationId": automation_id,
            "sourceRefs": [
                "src/Chummer.Android/Native/OriginDossierPage.cs",
                "src/Chummer.Android/Native/RunnerSessionCoordinator.cs",
                "chummer-presentation/Chummer.Presentation/Overview/CharacterOverviewPresenter.WorkspaceMutations.cs",
            ],
            "presenterMutation": "ICharacterOverviewPresenter.ApplyOriginDossierEditAsync",
            "persistenceAssertion": f"character/{xml_element} equals the submitted value after reopen and process restart",
            "e2e": {
                **receipt_e2e,
                "controlProof": control_proof,
            } if exact_api36 and receipt_e2e is not None else {
                "status": "scripted_not_executed" if e2e_scripted else "missing",
                "ref": e2e_driver.relative_to(REPO_ROOT).as_posix() if e2e_scripted else None,
            },
        }
    if class_name == "AttributeControl" and control in ATTRIBUTE_FIELDS:
        operation, automation_id = ATTRIBUTE_FIELDS[control]
        phone_page = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "AttributeEditPage.cs"
        coordinator = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "RunnerSessionCoordinator.cs"
        presenter = presentation_root / "Chummer.Presentation" / "Overview" / "CharacterOverviewPresenter.WorkspaceMutations.cs"
        phone_implemented = (
            _contains(phone_page, f'"{operation}"', "ApplyAttributeEditAsync")
            and _contains(coordinator, "ApplyAttributeEditAsync")
            and _contains(presenter, "ApplyAttributeEditAsync", "ApplyWorkspaceXmlMutationAsync")
        )
        if operation in {"base", "karma"}:
            e2e_driver = REPO_ROOT / "tests" / "run_api36_attribute_e2e.py"
            e2e_marker = {
                "base": '"attributeBaseEditPersisted": "pass"',
                "karma": '"attributeKarmaEditPersisted": "pass"',
            }[operation]
            e2e_scripted = _contains(
                e2e_driver,
                e2e_marker,
                '"processRestartAttributePersistence": "pass"',
                "assert_body_values",
            )
            phone_e2e = attribute_phone_e2e_receipt if e2e_scripted else None
        else:
            e2e_driver = REPO_ROOT / "tests" / "run_api36_career_attribute_e2e.py"
            e2e_marker = {
                "improve": '"attributeImprovePersisted": "pass"',
                "burn": '"attributeBurnEdgePersisted": "pass"',
            }.get(operation)
            e2e_scripted = e2e_marker is not None and _contains(
                e2e_driver,
                e2e_marker,
                '"processRestartCareerAttributePersistence": "pass"',
                "assert_attribute_total",
            )
            phone_e2e = attribute_career_phone_e2e_receipt if e2e_scripted else None
        phone_verified = bool(phone_implemented and phone_e2e is not None)
        source_refs = [
            "src/Chummer.Android/Native/AttributeEditPage.cs",
            "src/Chummer.Android/Native/RunnerSessionCoordinator.cs",
            "chummer-presentation/Chummer.Presentation/Overview/CharacterOverviewPresenter.WorkspaceMutations.cs",
            "chummer-presentation/Chummer.Presentation/Overview/WorkspaceXmlMutationCatalog.cs",
        ]
        return {
            "status": (
                "implemented_verified_api36"
                if phone_verified
                else "implemented_pending_emulator"
                if phone_implemented
                else "missing"
            ),
            "route": "Build > Attributes > selected attribute",
            "surface": "AttributeEditPage",
            "automationId": automation_id,
            "sourceRefs": source_refs,
            "presenterMutation": "ICharacterOverviewPresenter.ApplyAttributeEditAsync",
            "persistenceAssertion": f"selected attribute {operation} mutation is durable after reopen and process restart",
            "e2e": phone_e2e or {
                "status": "scripted_not_executed" if e2e_scripted else "missing",
                "ref": e2e_driver.relative_to(REPO_ROOT).as_posix() if e2e_scripted else None,
            },
        }
    character_condition_match = CHARACTER_CONDITION_CONTROL_RE.fullmatch(control)
    dashboard_condition = (
        DASHBOARD_CONDITION_CONTROLS.get(control)
        if class_name == "ConditionMonitorUserControl"
        else None
    )
    if (
        class_name == "CharacterCareer" and character_condition_match is not None
    ) or dashboard_condition is not None:
        if dashboard_condition is not None:
            track, expected_handler = dashboard_condition
            legacy_dashboard = (
                presentation_root
                / "Chummer"
                / "Controls"
                / "Dashboards"
                / "ConditionMonitorUserControl.cs"
            )
            button_control = control in {"_btnPhysical", "_btnApplyStun"}
            if button_control and not any(
                event.get("handler") == expected_handler for event in legacy.get("events", [])
            ):
                return None
            expected_counter = "_nudPhysical" if track == "Physical" else "nudStun"
            if not _contains(legacy_dashboard, expected_handler, expected_counter):
                return None
        else:
            track = character_condition_match.group("track")
            expected_handler = CHARACTER_CONDITION_HANDLERS[track]
            if not any(event.get("handler") == expected_handler for event in legacy.get("events", [])):
                return None

        token = track.lower()
        xml_element = f"{token}cmfilled"
        phone_page = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "ConditionMonitorEditPage.cs"
        phone_route = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "BuildFlowPages.cs"
        tablet_page = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "TabletBuildPage.cs"
        coordinator = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "RunnerSessionCoordinator.cs"
        request = presentation_root / "Chummer.Presentation" / "Overview" / "ConditionMonitorEditRequest.cs"
        state = presentation_root / "Chummer.Presentation" / "Overview" / "ConditionMonitorEditorState.cs"
        mutation = presentation_root / "Chummer.Presentation" / "Overview" / "WorkspaceXmlMutationCatalog.cs"
        presenter = presentation_root / "Chummer.Presentation" / "Overview" / "CharacterOverviewPresenter.WorkspaceMutations.cs"
        e2e_driver = REPO_ROOT / "tests" / "run_api36_editing_e2e.py"
        shared = (
            _contains(request, "ConditionMonitorEditRequest", track)
            and _contains(state, "ConditionMonitorEditorProjector", track)
            and _contains(mutation, "ApplyConditionMonitorEdit", xml_element)
            and _contains(coordinator, "ApplyConditionMonitorEditAsync")
            and _contains(presenter, "ApplyConditionMonitorEditAsync", "ApplyWorkspaceXmlMutationAsync")
        )
        phone_implemented = shared and _contains(
            phone_page,
            "condition-monitor-filled-",
            "ApplyConditionMonitorEditAsync",
        ) and _contains(phone_route, "condition-monitor-", "ConditionMonitorEditPage")
        tablet_implemented = shared and _contains(
            tablet_page,
            "tablet-condition-filled-",
            "ApplyConditionMonitorEditAsync",
        )
        e2e_scripted = _contains(
            e2e_driver,
            "edit_condition_damage",
            "assert_condition_damage",
            f'"{token}ConditionDamageEditPersisted": "pass"',
            '"processRestartConditionDamagePersistence": "pass"',
        )
        phone_e2e = condition_e2e_receipts.get("phone") if e2e_scripted else None
        tablet_e2e = condition_e2e_receipts.get("tablet") if e2e_scripted else None
        condition_e2e_complete = bool(
            phone_implemented and tablet_implemented and phone_e2e and tablet_e2e
        )
        return {
            "status": (
                "implemented_verified_api36"
                if phone_implemented and condition_e2e_complete
                else "implemented_pending_emulator" if phone_implemented else "missing"
            ),
            "route": f"Build > Combat > Damage tracks > {track}",
            "surface": "ConditionMonitorEditPage",
            "automationId": f"condition-monitor-filled-{token}",
            "sourceRefs": [
                "src/Chummer.Android/Native/BuildFlowPages.cs",
                "src/Chummer.Android/Native/ConditionMonitorEditPage.cs",
                "src/Chummer.Android/Native/RunnerSessionCoordinator.cs",
                "chummer-presentation/Chummer.Presentation/Overview/ConditionMonitorEditRequest.cs",
                "chummer-presentation/Chummer.Presentation/Overview/ConditionMonitorEditorState.cs",
                "chummer-presentation/Chummer.Presentation/Overview/WorkspaceXmlMutationCatalog.cs",
            ],
            "presenterMutation": "ICharacterOverviewPresenter.ApplyConditionMonitorEditAsync",
            "persistenceAssertion": (
                f"character/{xml_element} equals the chosen box count after reopen and process restart"
            ),
            "e2e": dict(phone_e2e) if phone_e2e is not None else {
                "status": "scripted_not_executed" if e2e_scripted else "missing",
                "ref": "tests/run_api36_editing_e2e.py" if e2e_scripted else None,
            },
            "tablet": {
                "status": (
                    "implemented_verified_api36"
                    if tablet_implemented and condition_e2e_complete
                    else "implemented_pending_emulator" if tablet_implemented else "missing"
                ),
                "surface": "TabletBuildPage persistent damage inspector",
                "automationId": f"tablet-condition-filled-{token}",
                "sourceRefs": [
                    "src/Chummer.Android/Native/TabletBuildPage.cs",
                    "src/Chummer.Android/Native/RunnerSessionCoordinator.cs",
                    "chummer-presentation/Chummer.Presentation/Overview/ConditionMonitorEditRequest.cs",
                    "chummer-presentation/Chummer.Presentation/Overview/ConditionMonitorEditorState.cs",
                    "chummer-presentation/Chummer.Presentation/Overview/WorkspaceXmlMutationCatalog.cs",
                ],
            },
            "tabletE2e": dict(tablet_e2e) if tablet_e2e is not None else {
                "status": "scripted_not_executed" if e2e_scripted else "missing",
                "ref": "tests/run_api36_editing_e2e.py" if e2e_scripted else None,
            },
            "completionProven": condition_e2e_complete,
        }
    vehicle_physical_match = VEHICLE_PHYSICAL_CONDITION_CONTROL_RE.fullmatch(control)
    if class_name == "CharacterCareer" and vehicle_physical_match is not None:
        if not any(
            event.get("handler") == "chkVehicleCM_CheckedChanged"
            for event in legacy.get("events", [])
        ):
            return None

        phone_page = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "CollectionEditorPages.cs"
        tablet_page = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "TabletBuildPage.cs"
        coordinator = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "RunnerSessionCoordinator.cs"
        request = presentation_root / "Chummer.Presentation" / "Overview" / "WorkspaceCollectionMutationRequest.cs"
        mutation = presentation_root / "Chummer.Presentation" / "Overview" / "WorkspaceXmlMutationCatalog.cs"
        projector = presentation_root / "Chummer.Presentation" / "Overview" / "WorkspaceCollectionEditorProjector.cs"
        presenter = presentation_root / "Chummer.Presentation" / "Overview" / "CharacterOverviewPresenter.WorkspaceMutations.cs"
        shared = (
            _contains(request, "VehiclePhysicalDamage", "WorkspacePatchCollectionItemRequest")
            and _contains(mutation, "ApplyVehiclePhysicalDamageMutation", "physicalcmfilled")
            and _contains(projector, "ProjectVehiclePhysicalConditionMonitor", "WorkspaceItemConditionMonitorState")
            and _contains(coordinator, "ApplyCollectionMutationAsync")
            and _contains(presenter, "ApplyCollectionMutationAsync", "ApplyWorkspaceXmlMutationAsync")
        )
        phone_implemented = shared and _contains(
            phone_page,
            "collection-vehicle-physical-damage-",
            "VehiclePhysicalDamage",
            "WorkspacePatchCollectionItemRequest",
        )
        tablet_implemented = shared and _contains(
            tablet_page,
            '"tablet-vehicle-physical-damage"',
            "VehiclePhysicalDamage",
            "WorkspacePatchCollectionItemRequest",
        )
        return {
            "status": "partial_exact_saved_data" if phone_implemented else "missing",
            "route": "Build > Vehicles > selected item",
            "surface": "CollectionItemEditorPage",
            "automationId": "collection-vehicle-physical-damage-{stable-target}",
            "coverageLimit": VEHICLE_PHYSICAL_CONDITION_COVERAGE_LIMIT,
            "sourceRefs": [
                "src/Chummer.Android/Native/CollectionEditorPages.cs",
                "src/Chummer.Android/Native/RunnerSessionCoordinator.cs",
                "chummer-presentation/Chummer.Presentation/Overview/WorkspaceCollectionEditorProjector.cs",
                "chummer-presentation/Chummer.Presentation/Overview/WorkspaceCollectionMutationRequest.cs",
                "chummer-presentation/Chummer.Presentation/Overview/WorkspaceXmlMutationCatalog.cs",
            ],
            "presenterMutation": (
                "ICharacterOverviewPresenter.ApplyCollectionMutationAsync / "
                "WorkspacePatchCollectionItemRequest.VehiclePhysicalDamage"
            ),
            "persistenceAssertion": (
                "selected stable Vehicle guid retains physicalcmfilled equal to the chosen box count "
                "after reopen and process restart"
            ),
            "e2e": {"status": "missing", "ref": None},
            "tablet": {
                "status": "partial_exact_saved_data" if tablet_implemented else "missing",
                "surface": "TabletBuildPage persistent inspector",
                "automationId": "tablet-vehicle-physical-damage",
                "coverageLimit": VEHICLE_PHYSICAL_CONDITION_COVERAGE_LIMIT,
                "sourceRefs": [
                    "src/Chummer.Android/Native/TabletBuildPage.cs",
                    "src/Chummer.Android/Native/RunnerSessionCoordinator.cs",
                    "chummer-presentation/Chummer.Presentation/Overview/WorkspaceCollectionEditorProjector.cs",
                    "chummer-presentation/Chummer.Presentation/Overview/WorkspaceCollectionMutationRequest.cs",
                    "chummer-presentation/Chummer.Presentation/Overview/WorkspaceXmlMutationCatalog.cs",
                ],
            },
            "tabletE2e": {"status": "missing", "ref": None},
        }
    matrix_match = MATRIX_CONDITION_CONTROL_RE.fullmatch(control)
    if class_name == "CharacterCareer" and matrix_match is not None:
        kind = matrix_match.group("kind")
        expected_handler = MATRIX_CONDITION_HANDLERS[kind]
        if not any(event.get("handler") == expected_handler for event in legacy.get("events", [])):
            return None

        token = kind.lower()
        damage_property = f"{kind}MatrixDamage"
        phone_page = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "CollectionEditorPages.cs"
        tablet_page = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "TabletBuildPage.cs"
        coordinator = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "RunnerSessionCoordinator.cs"
        request = presentation_root / "Chummer.Presentation" / "Overview" / "WorkspaceCollectionMutationRequest.cs"
        mutation = presentation_root / "Chummer.Presentation" / "Overview" / "WorkspaceXmlMutationCatalog.cs"
        projector = presentation_root / "Chummer.Presentation" / "Overview" / "WorkspaceCollectionEditorProjector.cs"
        presenter = presentation_root / "Chummer.Presentation" / "Overview" / "CharacterOverviewPresenter.WorkspaceMutations.cs"
        shared = (
            _contains(request, damage_property, "WorkspacePatchCollectionItemRequest")
            and _contains(mutation, f"Apply{kind}MatrixDamageMutation", "matrixcmfilled")
            and _contains(projector, "ProjectMatrixConditionMonitor", "WorkspaceItemConditionMonitorState")
            and _contains(coordinator, "ApplyCollectionMutationAsync")
            and _contains(presenter, "ApplyCollectionMutationAsync", "ApplyWorkspaceXmlMutationAsync")
        )
        phone_implemented = shared and _contains(
            phone_page,
            f"collection-{token}-matrix-damage-",
            damage_property,
            "WorkspacePatchCollectionItemRequest",
        )
        tablet_implemented = shared and _contains(
            tablet_page,
            f'"tablet-{token}-matrix-damage"',
            damage_property,
            "WorkspacePatchCollectionItemRequest",
        )
        section = "Vehicles" if kind == "Vehicle" else kind
        coverage_limit = MATRIX_CONDITION_COVERAGE_LIMITS[kind]
        return {
            "status": "partial_exact_saved_data" if phone_implemented else "missing",
            "route": f"Build > {section} > selected item",
            "surface": "CollectionItemEditorPage",
            "automationId": f"collection-{token}-matrix-damage-{{stable-target}}",
            "coverageLimit": coverage_limit,
            "sourceRefs": [
                "src/Chummer.Android/Native/CollectionEditorPages.cs",
                "src/Chummer.Android/Native/RunnerSessionCoordinator.cs",
                "chummer-presentation/Chummer.Presentation/Overview/WorkspaceCollectionEditorProjector.cs",
                "chummer-presentation/Chummer.Presentation/Overview/WorkspaceCollectionMutationRequest.cs",
                "chummer-presentation/Chummer.Presentation/Overview/WorkspaceXmlMutationCatalog.cs",
            ],
            "presenterMutation": (
                "ICharacterOverviewPresenter.ApplyCollectionMutationAsync / "
                f"WorkspacePatchCollectionItemRequest.{damage_property}"
            ),
            "persistenceAssertion": (
                f"selected stable {kind} guid retains matrixcmfilled equal to the chosen box count "
                "after reopen and process restart"
            ),
            "e2e": {"status": "missing", "ref": None},
            "tablet": {
                "status": "partial_exact_saved_data" if tablet_implemented else "missing",
                "surface": "TabletBuildPage persistent inspector",
                "automationId": f"tablet-{token}-matrix-damage",
                "coverageLimit": coverage_limit,
                "sourceRefs": [
                    "src/Chummer.Android/Native/TabletBuildPage.cs",
                    "src/Chummer.Android/Native/RunnerSessionCoordinator.cs",
                    "chummer-presentation/Chummer.Presentation/Overview/WorkspaceCollectionEditorProjector.cs",
                    "chummer-presentation/Chummer.Presentation/Overview/WorkspaceCollectionMutationRequest.cs",
                    "chummer-presentation/Chummer.Presentation/Overview/WorkspaceXmlMutationCatalog.cs",
                ],
            },
            "tabletE2e": {"status": "missing", "ref": None},
        }
    return None


def enrich_rows(
    rows: list[dict[str, Any]],
    registry: dict[str, Any],
    presentation_root: Path,
) -> list[dict[str, Any]]:
    surfaces = registry.get("editing_parity", {}).get("surfaces", {})
    condition_e2e_receipts = _validated_condition_e2e_receipts()
    contact_pet_e2e_receipts = _validated_contact_pet_e2e_receipts()
    attribute_phone_e2e_receipt = _validated_attribute_phone_e2e_receipt()
    attribute_career_phone_e2e_receipt = _validated_attribute_career_phone_e2e_receipt()
    character_settings_phone_e2e_receipt = _validated_character_settings_phone_e2e_receipt()
    character_settings_actions_phone_e2e_receipt = _validated_character_settings_actions_phone_e2e_receipt()
    origin_dossier_phone_e2e_receipt = _validated_origin_dossier_phone_e2e_receipt()
    linked_runner_phone_e2e_receipt = _validated_linked_runner_phone_e2e_receipt()
    new_character_settings_phone_e2e_receipt = _validated_new_character_settings_phone_e2e_receipt()
    new_character_karma_phone_e2e_receipt = _validated_new_character_karma_phone_e2e_receipt()
    new_character_priority_phone_e2e_receipt = _validated_new_character_priority_phone_e2e_receipt()
    for row in rows:
        family = row["mutationFamily"]
        family_contract = surfaces.get(family, {})
        non_mutating = row["legacy"]["mutationDisposition"] == "non_mutating"
        if non_mutating:
            row["phone"] = {
                "status": "not_applicable_non_mutating",
                "route": None,
                "surface": None,
                "automationId": None,
                "sourceRefs": [],
            }
            row["tablet"] = {
                "status": "not_applicable_non_mutating",
                "surface": None,
                "automationId": None,
                "sourceRefs": [],
            }
            row["presenterMutation"] = None
            row["persistenceAssertion"] = None
            row["e2e"] = {
                "phone": {"status": "not_applicable_non_mutating", "ref": None},
                "tablet": {"status": "not_applicable_non_mutating", "ref": None},
            }
            row["editParityRequired"] = False
            row["legacyReviewComplete"] = True
            row["overallStatus"] = "not_applicable_non_mutating"
            row["completionProven"] = True
            continue
        known = _known_phone_mapping(
            row,
            presentation_root,
            condition_e2e_receipts,
            contact_pet_e2e_receipts,
            attribute_phone_e2e_receipt,
            attribute_career_phone_e2e_receipt,
            character_settings_phone_e2e_receipt,
            character_settings_actions_phone_e2e_receipt,
            origin_dossier_phone_e2e_receipt,
            linked_runner_phone_e2e_receipt,
            new_character_settings_phone_e2e_receipt,
            new_character_karma_phone_e2e_receipt,
            new_character_priority_phone_e2e_receipt,
        )
        if known is not None:
            phone = {
                key: known[key]
                for key in ("status", "route", "surface", "automationId", "sourceRefs")
            }
            if "coverageLimit" in known:
                phone["coverageLimit"] = known["coverageLimit"]
            presenter_mutation = known["presenterMutation"]
            persistence_assertion = known["persistenceAssertion"]
            phone_e2e = known["e2e"]
        else:
            family_phone_status = str(family_contract.get("phone_status") or "missing")
            operation = row["operation"]
            partial_add = family_phone_status == "partial_create_only" and operation == "add"
            phone = {
                "status": "partial_create_only" if partial_add else (
                    "read_only" if family_phone_status == "read_only" else "missing"
                ),
                "route": family_contract.get("phone_route"),
                "surface": None,
                "automationId": None,
                "sourceRefs": [],
            }
            presenter_mutation = "workspace quick-add mutation" if partial_add else None
            persistence_assertion = None
            phone_e2e = {"status": "missing", "ref": None}

        if known is not None and isinstance(known.get("tablet"), dict):
            tablet = known["tablet"]
            tablet_e2e = known.get("tabletE2e", {"status": "missing", "ref": None})
        else:
            tablet = {
                "status": str(family_contract.get("tablet_status") or "missing"),
                "surface": None if family_contract.get("tablet_surface") in {None, "missing"} else family_contract.get("tablet_surface"),
                "automationId": None,
                "sourceRefs": [],
            }
            tablet_e2e = {"status": "missing", "ref": None}
        legacy_review = row["legacy"]["mutationConfidence"] == "review_required"
        row["phone"] = phone
        row["tablet"] = tablet
        row["presenterMutation"] = presenter_mutation
        row["persistenceAssertion"] = persistence_assertion
        row["e2e"] = {"phone": phone_e2e, "tablet": tablet_e2e}
        row["editParityRequired"] = True
        row["legacyReviewComplete"] = not legacy_review
        completion_proven = bool(known and known.get("completionProven")) and not legacy_review
        row["overallStatus"] = (
            "complete" if completion_proven else "review_required" if legacy_review else "missing"
        )
        row["completionProven"] = completion_proven
    return rows


def _status_counts(rows: list[dict[str, Any]], key: str) -> dict[str, int]:
    return dict(sorted(Counter(str(row[key]["status"]) for row in rows).items()))


def build_inventory(
    chummer5_root: Path,
    registry_path: Path,
    presentation_root: Path,
) -> dict[str, Any]:
    if not registry_path.is_file():
        raise FileNotFoundError(f"Missing Android parity registry: {registry_path}")
    registry = json.loads(_read_text(registry_path))
    rows, source_summary = extract_legacy_rows(chummer5_root)
    enrich_rows(rows, registry, presentation_root)

    family_counts: dict[str, dict[str, int]] = {}
    for family in sorted({row["mutationFamily"] for row in rows}):
        family_rows = [row for row in rows if row["mutationFamily"] == family]
        family_counts[family] = {
            "rowCount": len(family_rows),
            "reviewRequiredCount": sum(row["overallStatus"] == "review_required" for row in family_rows),
            "phoneMissingCount": sum(row["phone"]["status"] == "missing" for row in family_rows),
            "tabletMissingCount": sum(row["tablet"]["status"] == "missing" for row in family_rows),
            "completionProvenCount": sum(bool(row["completionProven"]) for row in family_rows),
        }

    android_inputs = [
        REPO_ROOT / "src" / "Chummer.Android" / "Native" / "AttributeEditPage.cs",
        REPO_ROOT / "src" / "Chummer.Android" / "Native" / "BuildFlowPages.cs",
        REPO_ROOT / "src" / "Chummer.Android" / "Native" / "BuildPage.cs",
        REPO_ROOT / "src" / "Chummer.Android" / "Native" / "CollectionEditorPages.cs",
        REPO_ROOT / "src" / "Chummer.Android" / "Native" / "ConditionMonitorEditPage.cs",
        REPO_ROOT / "src" / "Chummer.Android" / "Native" / "OriginDossierPage.cs",
        REPO_ROOT / "src" / "Chummer.Android" / "Native" / "NativeDialogPage.cs",
        REPO_ROOT / "src" / "Chummer.Android" / "Native" / "NativeCommandPage.cs",
        REPO_ROOT / "src" / "Chummer.Android" / "Native" / "RunnerSessionCoordinator.cs",
        REPO_ROOT / "src" / "Chummer.Android" / "Native" / "TabletBuildPage.cs",
        REPO_ROOT / "src" / "Chummer.Android" / "Platform" / "IAndroidLinkedCharacterFileService.cs",
        REPO_ROOT / "tests" / "run_api36_editing_e2e.py",
        REPO_ROOT / "tests" / "run_api36_attribute_e2e.py",
        REPO_ROOT / "tests" / "run_api36_career_attribute_e2e.py",
        REPO_ROOT / "tests" / "run_api36_character_settings_e2e.py",
        REPO_ROOT / "tests" / "run_api36_character_settings_actions_e2e.py",
        REPO_ROOT / "tests" / "run_api36_origin_dossier_e2e.py",
        REPO_ROOT / "tests" / "run_api36_linked_runner_e2e.py",
        REPO_ROOT / "tests" / "run_api36_new_character_settings_e2e.py",
        REPO_ROOT / "tests" / "run_api36_new_character_karma_e2e.py",
        REPO_ROOT / "tests" / "run_api36_new_character_priority_e2e.py",
        REPO_ROOT / "tests" / "fixtures" / "career-condition-monitor-e2e.chum5",
        REPO_ROOT / "tests" / "fixtures" / "creation-contact-pet-e2e.chum5",
        REPO_ROOT / "tests" / "fixtures" / "career-attribute-e2e.chum5",
        *CONDITION_E2E_RECEIPTS.values(),
        *CONTACT_PET_E2E_RECEIPTS.values(),
        ATTRIBUTE_PHONE_E2E_RECEIPT,
        ATTRIBUTE_CAREER_PHONE_E2E_RECEIPT,
        NEW_CHARACTER_SETTINGS_PHONE_E2E_RECEIPT,
        NEW_CHARACTER_KARMA_PHONE_E2E_RECEIPT,
        NEW_CHARACTER_PRIORITY_PHONE_E2E_RECEIPT,
        CHARACTER_SETTINGS_PHONE_E2E_RECEIPT,
        CHARACTER_SETTINGS_ACTIONS_PHONE_E2E_RECEIPT,
        ORIGIN_DOSSIER_PHONE_E2E_RECEIPT,
        LINKED_RUNNER_PHONE_E2E_RECEIPT,
        WORKSPACE_ROOT / "chummer-core-engine" / "Chummer.Contracts" / "Characters" / "CharacterContactEditSemantics.cs",
        WORKSPACE_ROOT / "chummer-core-engine" / "Chummer.Contracts" / "Characters" / "CharacterPetEditSemantics.cs",
        WORKSPACE_ROOT / "chummer-core-engine" / "Chummer.Infrastructure" / "Xml" / "Chummer5LinkedDocumentCodec.cs",
        presentation_root / "Chummer.Presentation" / "Overview" / "CharacterOverviewPresenter.WorkspaceMutations.cs",
        presentation_root / "Chummer.Presentation" / "Overview" / "ConditionMonitorEditRequest.cs",
        presentation_root / "Chummer.Presentation" / "Overview" / "ConditionMonitorEditorState.cs",
        presentation_root / "Chummer.Presentation" / "Overview" / "DialogCoordinator.cs",
        presentation_root / "Chummer.Presentation" / "Overview" / "DesktopDialogFactory.cs",
        presentation_root / "Chummer.Presentation" / "Overview" / "DesktopDialogFactory.CharacterSettings.cs",
        presentation_root / "Chummer.Presentation" / "Overview" / "Chummer5CharacterSettingsProfiles.cs",
        presentation_root / "Chummer.Presentation" / "Overview" / "Chummer5CharacterSettingsRuntimeContract.Generated.cs",
        presentation_root / "Chummer.Presentation" / "Overview" / "WorkspaceCollectionEditorProjector.cs",
        presentation_root / "Chummer.Presentation" / "Overview" / "WorkspaceCollectionEditorState.cs",
        presentation_root / "Chummer.Presentation" / "Overview" / "WorkspaceCollectionMutationRequest.cs",
        presentation_root / "Chummer.Presentation" / "Overview" / "WorkspaceXmlMutationCatalog.cs",
        presentation_root / "Chummer.Tests" / "Presentation" / "Chummer5CharacterSettingsProfilesTests.cs",
    ]
    return {
        "schema": SCHEMA,
        "status": "incomplete_fail_closed",
        "completionProven": False,
        "summary": {
            "rowCount": len(rows),
            "sourceFileCount": source_summary["sourceFileCount"],
            "designerFileCount": source_summary["designerFileCount"],
            "definiteMutationCandidateCount": sum(
                row["legacy"]["mutationConfidence"] == "definite" for row in rows
            ),
            "reviewRequiredCount": sum(row["overallStatus"] == "review_required" for row in rows),
            "legacyReviewCompleteCount": sum(bool(row["legacyReviewComplete"]) for row in rows),
            "reviewedNonMutatingCount": sum(
                row["overallStatus"] == "not_applicable_non_mutating" for row in rows
            ),
            "editParityRequiredCount": sum(bool(row["editParityRequired"]) for row in rows),
            "unclassifiedCount": sum(
                row["mutationFamily"] == "unclassified_legacy_controls" for row in rows
            ),
            "completionProvenCount": sum(bool(row["completionProven"]) for row in rows),
            "phoneStatusCounts": _status_counts(rows, "phone"),
            "tabletStatusCounts": _status_counts(rows, "tablet"),
        },
        "completionRule": (
            "Every row must have a reviewed legacy mutation classification, a shared durable presenter/core mutation, "
            "reachable phone and purpose-designed tablet surfaces, persistence assertions, and executed API 36 E2E "
            "receipts covering edit, navigation, reopen, and process restart."
        ),
        "requiredRowFields": list(REQUIRED_ROW_FIELDS),
        "generationInputs": {
            "chummer5": {
                "revision": _git_value(chummer5_root, "rev-parse", "HEAD"),
                "commitTimestamp": _git_value(chummer5_root, "show", "-s", "--format=%cI", "HEAD"),
                "trackedTreeDirty": bool(_git_value(chummer5_root, "status", "--porcelain", "--untracked-files=no")),
                "sourceRoots": source_summary["sourceRoots"],
                "sourceFingerprintSha256": source_summary["sourceFingerprintSha256"],
            },
            "androidParityRegistry": {
                "path": "chummer-design/products/chummer/ANDROID_WINDOWS_FEATURE_PARITY.yaml",
                "sha256": _sha256_file(registry_path),
            },
            "androidAndPresenterSources": [
                {
                    "path": (
                        path.relative_to(REPO_ROOT).as_posix()
                        if path.is_relative_to(REPO_ROOT)
                        else path.relative_to(WORKSPACE_ROOT).as_posix()
                        if path.is_relative_to(WORKSPACE_ROOT)
                        else str(path)
                    ),
                    "exists": path.is_file(),
                    "sha256": _sha256_file(path) if path.is_file() else None,
                }
                for path in android_inputs
            ],
            "generator": {
                "path": "scripts/materialize_chummer5_editability_inventory.py",
                "sha256": _sha256_file(Path(__file__).resolve()),
            },
        },
        "coverageByFamily": family_counts,
        "rows": rows,
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--chummer5-root", type=Path, default=DEFAULT_CHUMMER5_ROOT)
    parser.add_argument("--registry", type=Path, default=DEFAULT_REGISTRY)
    parser.add_argument(
        "--presentation-root",
        type=Path,
        default=WORKSPACE_ROOT / "chummer-presentation",
    )
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
    parser.add_argument("--check", action="store_true")
    arguments = parser.parse_args()

    try:
        payload = build_inventory(
            arguments.chummer5_root.resolve(),
            arguments.registry.resolve(),
            arguments.presentation_root.resolve(),
        )
    except (FileNotFoundError, json.JSONDecodeError, OSError) as error:
        print(f"editability inventory failed: {error}", file=sys.stderr)
        return 1

    output = arguments.output.resolve()
    rendered = json.dumps(payload, indent=2, sort_keys=False) + "\n"
    if arguments.check:
        if not output.is_file() or output.read_text(encoding="utf-8") != rendered:
            print(f"editability inventory is stale: {output}", file=sys.stderr)
            return 1
        print(f"editability inventory is current: {output}")
        return 0

    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(rendered, encoding="utf-8")
    print(json.dumps(payload["summary"], indent=2))
    print(f"wrote {output}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
