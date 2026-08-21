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
CHARACTER_NOTES_PHONE_E2E_RECEIPT = (
    REPO_ROOT
    / "docs"
    / "editability-evidence"
    / "api36-phone-character-notes"
    / "receipt.json"
)
CHARACTER_NOTES_E2E_JOURNEYS = (
    "newRunner",
    "characterNotesEditPersisted",
    "allCreationNotesEdited",
    "creationWorkspaceXmlPersisted",
    "creationProcessRestartUiReadback",
    "careerRunnerImported",
    "allCareerNotesEdited",
    "careerWorkspaceXmlPersisted",
    "careerProcessRestartUiReadback",
)
CHARACTER_NOTES_CONTROL_E2E_PROOF_KEYS = (
    "mutated",
    "workspacePersisted",
    "processRestartUiReadback",
)
CAREER_REPUTATION_PHONE_E2E_RECEIPT = (
    REPO_ROOT
    / "docs"
    / "editability-evidence"
    / "api36-phone-career-reputation"
    / "receipt.json"
)
CAREER_REPUTATION_E2E_JOURNEYS = (
    "coreOnlySourceVisibilityEnforced",
    "fullSourceProfileImported",
    "allCareerReputationEdited",
    "streetCredBurnConfirmed",
    "burntStreetCredIncrementedByTwo",
    "careerWorkspaceXmlPersisted",
    "careerUiReopenReadback",
    "careerProcessRestartUiReadback",
)
CAREER_REPUTATION_CONTROL_E2E_PROOF_KEYS = (
    "mutated",
    "workspacePersisted",
    "processRestartUiReadback",
)
SITUATIONAL_MODIFIERS_PHONE_E2E_RECEIPT = (
    REPO_ROOT
    / "docs"
    / "editability-evidence"
    / "api36-phone-situational-modifiers"
    / "receipt.json"
)
SITUATIONAL_MODIFIERS_E2E_JOURNEYS = (
    "creationRunnerImported",
    "allCreationSituationalModifiersEdited",
    "creationWorkspaceXmlPersisted",
    "creationUiReopenReadback",
    "creationProcessRestartUiReadback",
    "careerRunnerImported",
    "allCareerSituationalModifiersEdited",
    "careerWorkspaceXmlPersisted",
    "careerUiReopenReadback",
    "careerProcessRestartUiReadback",
)
SITUATIONAL_MODIFIERS_CONTROL_E2E_PROOF_KEYS = (
    "mutated",
    "workspacePersisted",
    "processRestartUiReadback",
)
PRIMARY_ARM_PHONE_E2E_RECEIPT = (
    REPO_ROOT
    / "docs"
    / "editability-evidence"
    / "api36-phone-primary-arm"
    / "receipt.json"
)
PRIMARY_ARM_E2E_JOURNEYS = (
    "creationRunnerImported",
    "creationPrimaryArmEdited",
    "creationWorkspaceXmlPersisted",
    "creationUiReopenReadback",
    "creationProcessRestartUiReadback",
    "careerRunnerImported",
    "careerPrimaryArmEdited",
    "careerWorkspaceXmlPersisted",
    "careerUiReopenReadback",
    "careerProcessRestartUiReadback",
    "ambidextrousReadOnlyGateEnforced",
)
PRIMARY_ARM_CONTROL_E2E_PROOF_KEYS = (
    "mutated",
    "workspacePersisted",
    "processRestartUiReadback",
)
GEAR_LOCATION_PHONE_E2E_RECEIPT = (
    REPO_ROOT
    / "docs"
    / "editability-evidence"
    / "api36-phone-gear-location-add"
    / "receipt.json"
)
GEAR_LOCATION_E2E_JOURNEYS = (
    "creationRunnerImported",
    "creationGearLocationAdded",
    "creationWorkspaceXmlPersisted",
    "creationSurfaceReopened",
    "creationProcessRestartPersistence",
    "careerRunnerImported",
    "careerGearLocationAdded",
    "careerWorkspaceXmlPersisted",
    "careerSurfaceReopened",
    "careerProcessRestartPersistence",
)
GEAR_LOCATION_CONTROL_E2E_PROOF_KEYS = (
    "mutated",
    "workspacePersisted",
    "surfaceReopened",
    "processRestartWorkspacePersisted",
)
WEAPON_LOCATION_PHONE_E2E_RECEIPT = (
    REPO_ROOT
    / "docs"
    / "editability-evidence"
    / "api36-phone-weapon-location-add"
    / "receipt.json"
)
WEAPON_LOCATION_E2E_JOURNEYS = (
    "creationRunnerImported",
    "creationWeaponLocationAdded",
    "creationWorkspaceXmlPersisted",
    "creationSurfaceReopened",
    "creationProcessRestartPersistence",
    "careerRunnerImported",
    "careerWeaponLocationAdded",
    "careerWorkspaceXmlPersisted",
    "careerSurfaceReopened",
    "careerProcessRestartPersistence",
)
WEAPON_LOCATION_CONTROL_E2E_PROOF_KEYS = GEAR_LOCATION_CONTROL_E2E_PROOF_KEYS
VEHICLE_LOCATION_PHONE_E2E_RECEIPT = (
    REPO_ROOT
    / "docs"
    / "editability-evidence"
    / "api36-phone-vehicle-location-add"
    / "receipt.json"
)
VEHICLE_LOCATION_E2E_JOURNEYS = (
    "creationRunnerImported",
    "creationGlobalVehicleLocationAdded",
    "creationSelectedVehicleLocationAdded",
    "creationBothBranchesWorkspaceXmlPersisted",
    "creationBothSurfacesReopened",
    "creationProcessRestartPersistence",
    "careerRunnerImported",
    "careerGlobalVehicleLocationAdded",
    "careerSelectedVehicleLocationAdded",
    "careerBothBranchesWorkspaceXmlPersisted",
    "careerBothSurfacesReopened",
    "careerProcessRestartPersistence",
)
VEHICLE_LOCATION_CONTROL_E2E_PROOF_KEYS = (
    "globalBranchMutated",
    "selectedVehicleBranchMutated",
    "workspacePersisted",
    "bothSurfacesReopened",
    "processRestartWorkspacePersisted",
)
VEHICLE_HOME_NODE_PHONE_E2E_RECEIPT = (
    REPO_ROOT
    / "docs"
    / "editability-evidence"
    / "api36-phone-vehicle-home-node"
    / "receipt.json"
)
VEHICLE_HOME_NODE_E2E_JOURNEYS = (
    "creationRunnerImported",
    "creationVehicleEnabledExclusive",
    "creationVehicleEnabledReopened",
    "creationVehicleEnabledProcessRestart",
    "creationVehicleDisabled",
    "creationVehicleDisabledReopened",
    "creationVehicleDisabledProcessRestart",
    "careerRunnerImported",
    "careerVehicleEnabledExclusive",
    "careerVehicleEnabledReopened",
    "careerVehicleEnabledProcessRestart",
    "careerVehicleDisabled",
    "careerVehicleDisabledReopened",
    "careerVehicleDisabledProcessRestart",
)
VEHICLE_HOME_NODE_CONTROL_E2E_PROOF_KEYS = (
    "enabledAsExclusiveHomeNode",
    "disabledFromHomeNode",
    "workspacePersisted",
    "surfaceReopened",
    "processRestartWorkspacePersisted",
    "processRestartUiReadback",
)
ARMOR_HOME_NODE_PHONE_E2E_RECEIPT = (
    REPO_ROOT
    / "docs"
    / "editability-evidence"
    / "api36-phone-armor-home-node"
    / "receipt.json"
)
ARMOR_HOME_NODE_E2E_JOURNEYS = (
    "creationRunnerImported",
    "creationArmorEnabledExclusive",
    "creationArmorEnabledReopened",
    "creationArmorEnabledProcessRestart",
    "creationArmorDisabled",
    "creationArmorDisabledReopened",
    "creationArmorDisabledProcessRestart",
    "careerRunnerImported",
    "careerArmorEnabledExclusive",
    "careerArmorEnabledReopened",
    "careerArmorEnabledProcessRestart",
    "careerArmorDisabled",
    "careerArmorDisabledReopened",
    "careerArmorDisabledProcessRestart",
)
ARMOR_HOME_NODE_CONTROL_E2E_PROOF_KEYS = VEHICLE_HOME_NODE_CONTROL_E2E_PROOF_KEYS
WEAPON_HOME_NODE_PHONE_E2E_RECEIPT = (
    REPO_ROOT
    / "docs"
    / "editability-evidence"
    / "api36-phone-weapon-home-node"
    / "receipt.json"
)
WEAPON_HOME_NODE_E2E_JOURNEYS = (
    "creationRunnerImported",
    "creationAiEligibilityReadback",
    "creationWeaponEnabledExclusive",
    "creationWeaponEnabledReopened",
    "creationWeaponEnabledProcessRestart",
    "creationWeaponDisabled",
    "creationWeaponDisabledReopened",
    "creationWeaponDisabledProcessRestart",
    "careerRunnerImported",
    "careerAiEligibilityReadback",
    "careerWeaponEnabledExclusive",
    "careerWeaponEnabledReopened",
    "careerWeaponEnabledProcessRestart",
    "careerWeaponDisabled",
    "careerWeaponDisabledReopened",
    "careerWeaponDisabledProcessRestart",
)
WEAPON_HOME_NODE_CONTROL_E2E_PROOF_KEYS = (
    "aiEligibilityReadback",
    *VEHICLE_HOME_NODE_CONTROL_E2E_PROOF_KEYS,
)
WEAPON_ACTIVE_COMMLINK_PHONE_E2E_RECEIPT = (
    REPO_ROOT
    / "docs"
    / "editability-evidence"
    / "api36-phone-weapon-active-commlink"
    / "receipt.json"
)
WEAPON_ACTIVE_COMMLINK_E2E_JOURNEYS = (
    "creationRunnerImported",
    "creationMatrixOwnerReadback",
    "creationWeaponEnabledExclusive",
    "creationWeaponEnabledReopened",
    "creationWeaponEnabledProcessRestart",
    "creationWeaponDisabled",
    "creationWeaponDisabledReopened",
    "creationWeaponDisabledProcessRestart",
    "careerRunnerImported",
    "careerMatrixOwnerReadback",
    "careerWeaponEnabledExclusive",
    "careerWeaponEnabledReopened",
    "careerWeaponEnabledProcessRestart",
    "careerWeaponDisabled",
    "careerWeaponDisabledReopened",
    "careerWeaponDisabledProcessRestart",
)
WEAPON_ACTIVE_COMMLINK_CONTROL_E2E_PROOF_KEYS = (
    "matrixOwnerReadback",
    "enabledAsExclusiveActiveCommlink",
    "disabledFromActiveCommlink",
    "workspacePersisted",
    "surfaceReopened",
    "processRestartWorkspacePersisted",
    "processRestartUiReadback",
)
ARMOR_ACTIVE_COMMLINK_PHONE_E2E_RECEIPT = (
    REPO_ROOT
    / "docs"
    / "editability-evidence"
    / "api36-phone-armor-active-commlink"
    / "receipt.json"
)
ARMOR_ACTIVE_COMMLINK_E2E_JOURNEYS = (
    "creationRunnerImported",
    "creationArmorEnabledExclusive",
    "creationArmorEnabledReopened",
    "creationArmorEnabledProcessRestart",
    "creationArmorDisabled",
    "creationArmorDisabledReopened",
    "creationArmorDisabledProcessRestart",
    "careerRunnerImported",
    "careerArmorEnabledExclusive",
    "careerArmorEnabledReopened",
    "careerArmorEnabledProcessRestart",
    "careerArmorDisabled",
    "careerArmorDisabledReopened",
    "careerArmorDisabledProcessRestart",
)
ARMOR_ACTIVE_COMMLINK_CONTROL_E2E_PROOF_KEYS = (
    "enabledAsExclusiveActiveCommlink",
    "disabledFromActiveCommlink",
    "legacyPersonaEligibility",
    "workspacePersisted",
    "surfaceReopened",
    "processRestartWorkspacePersisted",
    "processRestartUiReadback",
)
ARMOR_DAMAGE_PHONE_E2E_RECEIPT = (
    REPO_ROOT
    / "docs"
    / "editability-evidence"
    / "api36-phone-armor-damage"
    / "receipt.json"
)
ARMOR_DAMAGE_E2E_JOURNEYS = (
    "careerRunnerImported",
    "careerDegradeEnabledAtZero",
    "careerRepairDisabledAtZero",
    "careerDegradedToMaximum",
    "careerDegradeDisabledAtMaximum",
    "careerDegradedReopened",
    "careerDegradedProcessRestart",
    "careerRepairedToZero",
    "careerRepairDisabledAfterRepair",
    "careerRepairedReopened",
    "careerRepairedProcessRestart",
)
ARMOR_DAMAGE_CONTROL_E2E_PROOF_KEYS = (
    "stableArmorIdentity",
    "exactLegacyDirection",
    "exactBoundaryEnablement",
    "workspacePersisted",
    "unrelatedXmlPreserved",
    "surfaceReopened",
    "processRestartWorkspacePersisted",
    "processRestartUiReadback",
)
ARMOR_EQUIPMENT_PHONE_E2E_RECEIPT = (
    REPO_ROOT / "docs" / "editability-evidence" / "api36-phone-armor-equipment" / "receipt.json"
)
ARMOR_EQUIPMENT_E2E_JOURNEYS = tuple(
    f"{prefix}{journey}"
    for prefix in ("creation", "career")
    for journey in (
        "RunnerImported",
        "SelectedEquipped",
        "SelectedReopened",
        "AllUnequipped",
        "AllUnequippedReopened",
        "AllUnequippedProcessRestart",
        "AllEquipped",
        "AllEquippedReopened",
        "AllEquippedProcessRestart",
    )
)
ARMOR_EQUIPMENT_CONTROL_E2E_PROOF_KEYS = (
    "stableArmorIdentity",
    "exactSelectedState",
    "exactBulkAllState",
    "exactEligibility",
    "nestedEquipmentFlagsPreserved",
    "workspacePersisted",
    "unrelatedXmlPreserved",
    "surfaceReopened",
    "processRestartWorkspacePersisted",
    "processRestartUiReadback",
)
WEAPON_ACCESSORY_INCLUDED_PHONE_E2E_RECEIPT = (
    REPO_ROOT
    / "docs"
    / "editability-evidence"
    / "api36-phone-weapon-accessory-included"
    / "receipt.json"
)
WEAPON_ACCESSORY_INCLUDED_E2E_JOURNEYS = (
    "creationRunnerImported",
    "creationAccessoryEnabled",
    "creationAccessoryEnabledReopened",
    "creationAccessoryEnabledProcessRestart",
    "creationAccessoryDisabled",
    "creationAccessoryDisabledReopened",
    "creationAccessoryDisabledProcessRestart",
    "careerRunnerImported",
    "careerAccessoryEnabled",
    "careerAccessoryEnabledReopened",
    "careerAccessoryEnabledProcessRestart",
    "careerAccessoryDisabled",
    "careerAccessoryDisabledReopened",
    "careerAccessoryDisabledProcessRestart",
)
WEAPON_ACCESSORY_INCLUDED_CONTROL_E2E_PROOF_KEYS = (
    "enabledForSelectedAccessory",
    "disabledForSelectedAccessory",
    "stableParentAndAccessoryIdentity",
    "workspacePersisted",
    "unrelatedXmlPreserved",
    "surfaceReopened",
    "processRestartWorkspacePersisted",
    "processRestartUiReadback",
)
CRITTER_POWER_COUNT_PHONE_E2E_RECEIPT = (
    REPO_ROOT
    / "docs"
    / "editability-evidence"
    / "api36-phone-critter-power-count"
    / "receipt.json"
)
CRITTER_POWER_COUNT_E2E_JOURNEYS = (
    "creationRunnerImported",
    "creationLegacyDefaultReadback",
    "creationExcludedPersistedReopenedRestarted",
    "creationIncludedPersistedReopenedRestarted",
    "careerRunnerImported",
    "careerSavedTrueReadback",
    "careerExcludedPersistedReopenedRestarted",
    "careerIncludedPersistedReopenedRestarted",
)
CRITTER_POWER_COUNT_CONTROL_E2E_PROOF_KEYS = (
    "selectedIdentityStable",
    "legacyDefaultTrue",
    "excludedAndIncluded",
    "workspacePersisted",
    "unrelatedXmlPreserved",
    "expectedRevisionAtomicSave",
    "surfaceReopened",
    "processRestartWorkspacePersisted",
    "processRestartUiReadback",
)
SUSTAINED_EFFECTS_PHONE_E2E_RECEIPT = (
    REPO_ROOT
    / "docs"
    / "editability-evidence"
    / "api36-phone-sustained-effects"
    / "receipt.json"
)
SUSTAINED_EFFECTS_E2E_JOURNEYS = (
    "creationRunnerImported",
    "creationDuplicateEditedReopenedRestarted",
    "creationCritterDeletedRestarted",
    "careerRunnerImported",
    "careerDuplicateEditedReopenedRestarted",
    "careerCritterDeletedRestarted",
)
SUSTAINED_EFFECTS_CONTROL_E2E_PROOF_KEYS = (
    "sharedCreateCareerReachability",
    "linkedTypeGuidOccurrenceIdentity",
    "duplicateCastIsolation",
    "forceAndNetHitsBounds",
    "critterPowerSelfSustainedHidden",
    "explicitDeleteConfirmation",
    "workspacePersisted",
    "unrelatedXmlPreserved",
    "expectedRevisionAtomicSave",
    "surfaceReopened",
    "processRestartWorkspacePersisted",
    "processRestartUiReadback",
)
LOCATION_RENAME_PHONE_E2E_RECEIPT = (
    REPO_ROOT
    / "docs"
    / "editability-evidence"
    / "api36-phone-location-rename"
    / "receipt.json"
)
LOCATION_RENAME_E2E_JOURNEYS = (
    "creationRunnerImported",
    "creationAllLocationsRenamed",
    "creationWorkspaceXmlPersisted",
    "creationAllSurfacesReopened",
    "creationProcessRestartPersistence",
    "careerRunnerImported",
    "careerAllLocationsRenamed",
    "careerWorkspaceXmlPersisted",
    "careerAllSurfacesReopened",
    "careerProcessRestartPersistence",
)
LOCATION_RENAME_CONTROL_E2E_PROOF_KEYS = (
    "mutated",
    "workspacePersisted",
    "surfaceReopened",
    "processRestartWorkspacePersisted",
)
EXPLICIT_SAVE_PHONE_E2E_RECEIPT = (
    REPO_ROOT
    / "docs"
    / "editability-evidence"
    / "api36-phone-explicit-save"
    / "receipt.json"
)
EXPLICIT_SAVE_E2E_JOURNEYS = (
    "creationRunnerImported",
    "creationBuildToolbarSaveInvoked",
    "creationMorePageSaveInvoked",
    "creationWorkspaceRevisionSaved",
    "creationProcessRestartReadback",
    "careerRunnerImported",
    "careerBuildToolbarSaveInvoked",
    "careerMorePageSaveInvoked",
    "careerWorkspaceRevisionSaved",
    "careerProcessRestartReadback",
    "selectedRunnerSaveEquivalent",
)
EXPLICIT_SAVE_CONTROL_E2E_PROOF_KEYS = (
    "invoked",
    "workspacePersisted",
    "processRestartReadback",
)
NESTED_COLLECTION_NOTES_PHONE_E2E_RECEIPT = (
    REPO_ROOT
    / "docs"
    / "editability-evidence"
    / "api36-phone-nested-collection-notes"
    / "receipt.json"
)
NESTED_COLLECTION_NOTES_E2E_JOURNEYS = (
    "creationRunnerImported",
    "allCreationNestedNotesEdited",
    "creationWorkspaceXmlPersisted",
    "creationProcessRestartUiReadback",
    "careerRunnerImported",
    "allCareerNestedNotesEdited",
    "careerWorkspaceXmlPersisted",
    "careerProcessRestartUiReadback",
)
NESTED_COLLECTION_NOTES_CONTROL_E2E_PROOF_KEYS = (
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
UNREACHABLE_LEGACY_FORMS = {
    "SelectSetting": (
        "orphaned legacy form has no reference outside its own partial class and designer sources, "
        "so its controls cannot be reached in the product UI"
    ),
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
CAREER_REPUTATION_CONTROLS = {
    "nudStreetCred": ("streetcred", "career-reputation-street-cred", "StreetCred"),
    "nudNotoriety": ("notoriety", "career-reputation-notoriety", "Notoriety"),
    "nudPublicAware": ("publicawareness", "career-reputation-public-awareness", "PublicAwareness"),
    "nudAstralReputation": ("baseastralreputation", "career-reputation-astral", "AstralReputation"),
    "nudWildReputation": ("basewildreputation", "career-reputation-wild", "WildReputation"),
}
BURN_STREET_CRED_CONTROL = "cmdBurnStreetCred"
SITUATIONAL_MODIFIER_CONTROLS = {
    "nudCounterspellingDice": (
        "currentcounterspellingdice",
        "situational-counterspelling-dice",
        "CounterspellingDice",
    ),
    "nudLiftCarryHits": (
        "currentliftcarryhits",
        "situational-lift-carry-hits",
        "LiftCarryHits",
    ),
}
PRIMARY_ARM_CONTROLS = {
    "cboPrimaryArm": ("primaryarm", "primary-arm-choice", "PrimaryArm"),
}
GROUP_MEMBERSHIP_CONTROL = "chkJoinGroup"
GROUP_NAME_CONTROL = "txtGroupName"
TRADITION_NAME_CONTROL = "txtTraditionName"
CAREER_EDGE_USE_CONTROLS = {
    "cmdEdgeSpent": (
        "cmdEdgeSpent_Click",
        "spend",
        "career-edge-use-spend",
    ),
    "cmdEdgeGained": (
        "cmdEdgeGained_Click",
        "regain",
        "career-edge-use-regain",
    ),
}
CAREER_MANUAL_KARMA_CONTROLS = {
    "cmdKarmaGained": (
        "cmdKarmaGained_Click",
        "gain",
        "career-manual-karma-gain",
    ),
    "cmdKarmaSpent": (
        "cmdKarmaSpent_Click",
        "spend",
        "career-manual-karma-spend",
    ),
}
GEAR_LOCATION_ADD_CONTROL = "cmdAddLocation"
WEAPON_LOCATION_ADD_CONTROL = "cmdAddWeaponLocation"
VEHICLE_LOCATION_ADD_CONTROL = "cmdAddVehicleLocation"
VEHICLE_HOME_NODE_CONTROL = "chkVehicleHomeNode"
ARMOR_HOME_NODE_CONTROL = "chkArmorHomeNode"
WEAPON_HOME_NODE_CONTROL = "chkWeaponHomeNode"
WEAPON_ACTIVE_COMMLINK_CONTROL = "chkWeaponActiveCommlink"
ARMOR_ACTIVE_COMMLINK_CONTROL = "chkArmorActiveCommlink"
WEAPON_ACCESSORY_INCLUDED_CONTROL = "chkIncludedInWeapon"
QUALITY_LEVEL_CONTROL = "nudQualityLevel"
CRITTER_POWER_COUNT_CONTROL = "chkCritterPowerCount"
SUSTAINED_EFFECTS_CONTROLS = {
    "nudForce": "sustained-effect-force-{linked-type-guid-occurrence}",
    "nudNetHits": "sustained-effect-net-hits-{linked-type-guid-occurrence}",
    "chkSelfSustained": "sustained-effect-self-{linked-type-guid-occurrence}",
    "cmdDelete": "sustained-effect-delete-{linked-type-guid-occurrence}",
}
ARMOR_DAMAGE_CONTROLS = {
    "cmdArmorIncrease": (
        "cmdArmorIncrease_Click",
        "repair",
        "armor-damage-repair-{stable-armor-guid}",
    ),
    "cmdArmorDecrease": (
        "cmdArmorDecrease_Click",
        "degrade",
        "armor-damage-degrade-{stable-armor-guid}",
    ),
}
ARMOR_EQUIPMENT_CONTROLS = {
    "chkArmorEquipped": (
        "chkArmorEquipped_CheckedChanged",
        "selected",
        "armor-equipment-toggle-{stable-armor-guid}",
    ),
    "cmdArmorEquipAll": (
        "cmdArmorEquipAll_Click",
        "equip-all",
        "armor-equipment-equip-all-{stable-armor-guid}",
    ),
    "cmdArmorUnEquipAll": (
        "cmdArmorUnEquipAll_Click",
        "unequip-all",
        "armor-equipment-unequip-all-{stable-armor-guid}",
    ),
}
CYBERWARE_COMMERCE_CONTROLS = {
    "tsCyberwareUpgrade": ("upgrade", "cyberware-commerce-upgrade-{stable-cyberware-guid}"),
    "tsCyberwareSell": ("sell", "cyberware-commerce-sell-{stable-cyberware-guid}"),
}
GEAR_QUANTITY_CONTROLS = {
    "cmdGearIncreaseQty": ("increase", "gear-quantity-increase-{stable-gear-guid}"),
    "cmdGearReduceQty": ("reduce", "gear-quantity-reduce-{stable-gear-guid}"),
    "cmdGearSplitQty": ("split", "gear-quantity-split-{stable-gear-guid}"),
    "cmdGearMergeQty": ("merge", "gear-quantity-merge-{stable-gear-guid}"),
}
LOCATION_RENAME_CONTROLS = {
    "tsGearRenameLocation": ("Gear", "gearlocations"),
    "tsWeaponRenameLocation": ("Weapon", "weaponlocations"),
    "tsArmorRenameLocation": ("Armor", "armorlocations"),
    "tsVehicleRenameLocation": ("Vehicle", "vehiclelocations"),
}
EXPLICIT_SAVE_CONTROLS = {
    ("CharacterCreate", "tsbSave"): (
        "Build > toolbar Save",
        "BuildPage toolbar",
        "build-save-runner",
    ),
    ("CharacterCreate", "mnuFileSave"): (
        "More > Save",
        "MorePage",
        "more-save-runner",
    ),
    ("CharacterCareer", "tsbSave"): (
        "Build > toolbar Save",
        "BuildPage toolbar",
        "build-save-runner",
    ),
    ("CharacterCareer", "mnuFileSave"): (
        "More > Save",
        "MorePage",
        "more-save-runner",
    ),
    ("ChummerMainForm", "tsSave"): (
        "Build > toolbar Save",
        "BuildPage toolbar",
        "build-save-runner",
    ),
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
LEGACY_CHARACTER_COLLECTION_DELETE_CONTROLS = {
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
LEGACY_CHARACTER_COLLECTION_NOTES_CONTROLS = {
    "tsGearNotes": ("Gear", "Gear", "tsGearNotes_Click"),
    "tsWeaponNotes": ("Weapon", "Weapons", "tsWeaponNotes_Click"),
    "tsArmorNotes": ("Armor", "Armor", "tsArmorNotes_Click"),
    "tsVehicleNotes": ("Vehicle", "Vehicles", "tsVehicleNotes_Click"),
    "tsCyberwareNotes": ("Cyberware", "Cyberware", "tsCyberwareNotes_Click"),
    "tsBiowareNotes": ("Cyberware", "Cyberware", "tsCyberwareNotes_Click"),
    "tsSpellNotes": ("Spell", "Spells", "tsSpellNotes_Click"),
    "tsComplexFormNotes": ("ComplexForm", "Complex forms", "tsComplexFormNotes_Click"),
    "tsAIProgramNotes": ("MatrixProgram", "AI programs", "tsAIProgramNotes_Click"),
    "tsCritterPowersNotes": ("CritterPower", "Critter powers", "tsCritterPowersNotes_Click"),
    "tsQualityNotes": ("Quality", "Qualities", "tsQualityNotes_Click"),
    "tsInitiationNotes": ("InitiationGrade", "Initiation grades", "tsInitiationNotes_Click"),
}
LEGACY_NESTED_COLLECTION_NOTES_CONTROLS = {
    "tsWeaponAccessoryNotes": (
        "Weapon",
        "WeaponAccessory",
        "Gear > Weapon Accessories",
        "weaponaccessories",
        "tsWeaponNotes_Click",
        {"CharacterCreate", "CharacterCareer"},
    ),
    "tsArmorModNotes": (
        "Armor",
        "ArmorMod",
        "Gear > Armor Mods",
        "armormods",
        "tsArmorNotes_Click",
        {"CharacterCreate", "CharacterCareer"},
    ),
    "tsGearPluginNotes": (
        "Gear",
        "Gear",
        "Gear",
        "gear",
        "tsGearNotes_Click",
        {"CharacterCareer"},
    ),
}
LEGACY_CHARACTER_COLLECTION_TEXT_CONTROLS = {
    "tsWeaponName": (
        "Weapon", "Weapons", "CustomName", "extra", "tsWeaponName_Click",
        {"CharacterCreate", "CharacterCareer"},
    ),
    "tsArmorName": (
        "Armor", "Armor", "CustomName", "extra", "tsArmorName_Click",
        {"CharacterCreate", "CharacterCareer"},
    ),
    "tsVehicleName": (
        "Vehicle", "Vehicles", "CustomName", "extra", "tsVehicleName_Click",
        {"CharacterCreate", "CharacterCareer"},
    ),
    "tsGearAllowRenameNotes": (
        "Gear", "Gear", "Notes", "notes", "tsGearNotes_Click", {"CharacterCreate"},
    ),
}
LEGACY_CHARACTER_COLLECTION_TOGGLE_CONTROLS = {
    "chkGearEquipped": ("Gear", "Gear", "Equipped", "equipped", {"CharacterCreate", "CharacterCareer"}),
    "chkGearWireless": ("Gear", "Gear", "WirelessEnabled", "wirelesson", {"CharacterCareer"}),
    "chkGearHomeNode": ("Gear", "Gear", "HomeNode", "homenode", {"CharacterCreate", "CharacterCareer"}),
    "chkCyberwareWireless": ("Cyberware", "Cyberware", "WirelessEnabled", "wirelesson", {"CharacterCareer"}),
    "chkCyberwareHomeNode": ("Cyberware", "Cyberware", "HomeNode", "homenode", {"CharacterCreate", "CharacterCareer"}),
    "chkArmorEquipped": ("Armor", "Armor", "Equipped", "equipped", {"CharacterCreate", "CharacterCareer"}),
    "chkArmorWireless": ("Armor", "Armor", "WirelessEnabled", "wirelesson", {"CharacterCareer"}),
    "chkWeaponEquipped": ("Weapon", "Weapons", "Equipped", "equipped", {"CharacterCreate", "CharacterCareer"}),
    "chkWeaponWireless": ("Weapon", "Weapons", "WirelessEnabled", "wirelesson", {"CharacterCareer"}),
    "chkInitiationGroup": ("InitiationGrade", "Initiation grades", "Group", "group", {"CharacterCreate", "CharacterCareer"}),
    "chkInitiationOrdeal": ("InitiationGrade", "Initiation grades", "Ordeal", "ordeal", {"CharacterCreate", "CharacterCareer"}),
    "chkInitiationSchooling": ("InitiationGrade", "Initiation grades", "Schooling", "schooling", {"CharacterCreate", "CharacterCareer"}),
}
LEGACY_CREATION_COLLECTION_NUMERIC_CONTROLS = {
    "nudGearRating": ("Gear", "Gear", "Rating", "rating"),
    "nudGearQty": ("Gear", "Gear", "Quantity", "qty"),
    "nudArmorRating": ("Armor", "Armor", "Rating", "rating and armor"),
    "nudCyberwareRating": ("Cyberware", "Cyberware", "Rating", "rating"),
    "nudDrugQty": ("Drug", "Drugs", "Quantity", "qty"),
}
SPIRIT_GENERIC_EDITOR_CONTROLS = {
    "cmdNotes": ("text", "Notes", "notes"),
    "txtCritterName": ("critter", "CritterName", "crittername"),
    "nudForce": ("force", "Force", "force"),
    "nudServices": ("integer", "Services", "services"),
    "chkBound": ("toggle", "Bound", "bound"),
    "cmdDelete": ("delete", None, "spirit"),
}
SPIRIT_LINKED_RUNNER_CONTROLS = {
    "cmdLink": "manage",
    "tsAttachCharacter": "attach",
    "tsRemoveCharacter": "remove",
}
NON_MUTATING_LEGACY_INTERACTIONS = {
    ("CharacterCreate", "chkIncludedInArmor"): (
        "display_included_in_armor_state",
        "the checkbox is disabled in the designer, has no mutation event, and selection refresh only copies "
        "ArmorMod.IncludedInArmor or Gear.IncludedInParent into Checked; it writes no runner or persisted "
        "application state",
    ),
    ("CharacterCareer", "chkIncludedInArmor"): (
        "display_included_in_armor_state",
        "the checkbox is disabled in the designer, has no mutation event, and selection refresh only copies "
        "ArmorMod.IncludedInArmor or Gear.IncludedInParent into Checked; it writes no runner or persisted "
        "application state",
    ),
    ("CharacterCreate", "chkVehicleIncludedInWeapon"): (
        "display_included_in_weapon_state",
        "the checkbox is disabled in the designer, has no mutation event, and vehicle-tree selection refresh "
        "only copies Weapon.IncludedInWeapon or WeaponAccessory.IncludedInWeapon into Checked; it writes no "
        "runner or persisted application state",
    ),
    ("CharacterCareer", "chkVehicleIncludedInWeapon"): (
        "display_included_in_weapon_state",
        "the checkbox is disabled in the designer, has no mutation event, and vehicle-tree selection refresh "
        "only copies Weapon.IncludedInWeapon or WeaponAccessory.IncludedInWeapon into Checked; it writes no "
        "runner or persisted application state",
    ),
    ("CharacterCreate", "chkCommlinks"): (
        "filter_gear_view",
        "CheckedChanged only rebuilds the Gear tree with the commlinks-only filter; it writes no runner or "
        "persisted application state",
    ),
    ("CharacterCareer", "chkCommlinks"): (
        "filter_gear_view",
        "CheckedChanged only rebuilds the Gear tree with the commlinks-only filter; it writes no runner or "
        "persisted application state",
    ),
    ("CharacterCareer", "chkHideLoadedAmmo"): (
        "filter_gear_view",
        "CheckedChanged only rebuilds the Gear tree with the hide-loaded-ammo filter; it writes no runner or "
        "persisted application state",
    ),
    ("CharacterCareer", "chkShowFreeKarma"): (
        "filter_view",
        "CheckedChanged only repopulates the Karma expense list and chart with or without zero-value entries; "
        "it writes no runner or persisted application state",
    ),
    ("CharacterCareer", "chkShowKarmaChart"): (
        "toggle_view",
        "CheckedChanged only assigns chtKarma.Visible from the checkbox; it writes no runner or persisted "
        "application state",
    ),
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
DYNAMIC_CHARACTER_CONDITION_CONTROL = "cb"
DYNAMIC_CHARACTER_CONDITION_HANDLER = "evtButtonClickEvent"
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


def _validated_character_notes_phone_e2e_receipt(
    presentation_root: Path,
    core_root: Path,
) -> dict[str, Any] | None:
    driver = REPO_ROOT / "tests" / "run_api36_character_notes_e2e.py"
    shared_driver = REPO_ROOT / "tests" / "run_api36_editing_e2e.py"
    fixture = REPO_ROOT / "tests" / "fixtures" / "career-condition-monitor-e2e.chum5"
    source_digests = {
        "notesPageSha256": REPO_ROOT / "src" / "Chummer.Android" / "Native" / "CharacterNotesPage.cs",
        "coordinatorSha256": REPO_ROOT / "src" / "Chummer.Android" / "Native" / "RunnerSessionCoordinator.cs",
        "updateWorkspaceMetadataContractSha256": core_root / "Chummer.Contracts" / "Workspaces" / "CharacterWorkspaceModels.cs",
        "profileContractSha256": core_root / "Chummer.Contracts" / "Characters" / "CharacterSectionModels.cs",
        "characterFileServiceSha256": core_root / "Chummer.Infrastructure" / "Xml" / "CharacterFileService.cs",
        "characterSectionServiceSha256": core_root / "Chummer.Infrastructure" / "Xml" / "CharacterSectionService.cs",
        "presentationPersistenceSha256": presentation_root / "Chummer.Presentation" / "Overview" / "CharacterOverviewPresenter.Persistence.cs",
    }
    if (
        not driver.is_file()
        or not shared_driver.is_file()
        or not fixture.is_file()
        or not all(path.is_file() for path in source_digests.values())
    ):
        return None

    try:
        receipt = json.loads(_read_text(CHARACTER_NOTES_PHONE_E2E_RECEIPT))
    except (FileNotFoundError, json.JSONDecodeError, OSError):
        return None
    journeys = receipt.get("journeys")
    controls = receipt.get("controls")
    apk_sha = str(receipt.get("apkSha256") or "")
    expected_controls = {
        "CharacterCreate.rtfNotes",
        "CharacterCreate.txtGroupNotes",
        "CharacterCareer.rtfNotes",
        "CharacterCareer.rtfGameNotes",
        "CharacterCareer.txtGroupNotes",
    }
    if not (
        receipt.get("schema") == "chummer.android.editing-e2e/v1"
        and receipt.get("status") == "pass"
        and receipt.get("profile") == "phone"
        and receipt.get("journey") == "character-notes"
        and receipt.get("apiLevel") == 36
        and receipt.get("driverSha256") == _sha256_file(driver)
        and receipt.get("sharedDriverSha256") == _sha256_file(shared_driver)
        and receipt.get("careerFixtureSha256") == _sha256_file(fixture)
        and all(receipt.get(key) == _sha256_file(path) for key, path in source_digests.items())
        and isinstance(journeys, dict)
        and all(journeys.get(journey) == "pass" for journey in CHARACTER_NOTES_E2E_JOURNEYS)
        and isinstance(controls, dict)
        and set(controls) == expected_controls
        and receipt.get("controlCount") == len(expected_controls)
        and all(
            isinstance(controls.get(control), dict)
            and all(
                controls[control].get(proof_key) == "pass"
                for proof_key in CHARACTER_NOTES_CONTROL_E2E_PROOF_KEYS
            )
            for control in expected_controls
        )
        and re.fullmatch(r"[0-9a-f]{64}", apk_sha)
    ):
        return None
    return {
        "status": "executed_api36",
        "ref": CHARACTER_NOTES_PHONE_E2E_RECEIPT.relative_to(REPO_ROOT).as_posix(),
        "receiptSha256": _sha256_file(CHARACTER_NOTES_PHONE_E2E_RECEIPT),
        "apkSha256": apk_sha,
    }


def _validated_career_reputation_phone_e2e_receipt(
    presentation_root: Path,
    core_root: Path,
) -> dict[str, Any] | None:
    driver = REPO_ROOT / "tests" / "run_api36_career_reputation_e2e.py"
    shared_driver = REPO_ROOT / "tests" / "run_api36_editing_e2e.py"
    core_fixture = REPO_ROOT / "tests" / "fixtures" / "career-reputation-core-only-e2e.chum5"
    full_fixture = REPO_ROOT / "tests" / "fixtures" / "career-reputation-full-e2e.chum5"
    source_digests = {
        "reputationPageSha256": REPO_ROOT / "src" / "Chummer.Android" / "Native" / "CareerReputationPage.cs",
        "buildPageSha256": REPO_ROOT / "src" / "Chummer.Android" / "Native" / "BuildPage.cs",
        "coordinatorSha256": REPO_ROOT / "src" / "Chummer.Android" / "Native" / "RunnerSessionCoordinator.cs",
        "reputationContractSha256": presentation_root / "Chummer.Presentation" / "Overview" / "CareerReputationEditRequest.cs",
        "mutationCatalogSha256": presentation_root / "Chummer.Presentation" / "Overview" / "WorkspaceXmlMutationCatalog.cs",
        "presenterMutationSha256": presentation_root / "Chummer.Presentation" / "Overview" / "CharacterOverviewPresenter.WorkspaceMutations.cs",
        "progressContractSha256": core_root / "Chummer.Contracts" / "Characters" / "CharacterSectionModels.cs",
        "sectionServiceSha256": core_root / "Chummer.Infrastructure" / "Xml" / "CharacterSectionService.cs",
        "sourceContractSha256": core_root / "Chummer.Application" / "Characters" / "ICharacterSourceDataResolver.cs",
        "sourceResolverSha256": core_root / "Chummer.Infrastructure" / "Xml" / "FileSystemCharacterSourceDataResolver.cs",
    }
    if not all(
        path.is_file()
        for path in (driver, shared_driver, core_fixture, full_fixture, *source_digests.values())
    ):
        return None

    try:
        receipt = json.loads(_read_text(CAREER_REPUTATION_PHONE_E2E_RECEIPT))
    except (FileNotFoundError, json.JSONDecodeError, OSError):
        return None
    journeys = receipt.get("journeys")
    controls = receipt.get("controls")
    apk_sha = str(receipt.get("apkSha256") or "")
    expected_controls = {
        f"CharacterCareer.{control}" for control in CAREER_REPUTATION_CONTROLS
    } | {f"CharacterCareer.{BURN_STREET_CRED_CONTROL}"}
    if not (
        receipt.get("schema") == "chummer.android.editing-e2e/v1"
        and receipt.get("status") == "pass"
        and receipt.get("profile") == "phone"
        and receipt.get("journey") == "career-reputation"
        and receipt.get("apiLevel") == 36
        and receipt.get("driverSha256") == _sha256_file(driver)
        and receipt.get("sharedDriverSha256") == _sha256_file(shared_driver)
        and receipt.get("coreFixtureSha256") == _sha256_file(core_fixture)
        and receipt.get("fullFixtureSha256") == _sha256_file(full_fixture)
        and all(receipt.get(key) == _sha256_file(path) for key, path in source_digests.items())
        and isinstance(journeys, dict)
        and all(journeys.get(journey) == "pass" for journey in CAREER_REPUTATION_E2E_JOURNEYS)
        and isinstance(controls, dict)
        and set(controls) == expected_controls
        and receipt.get("controlCount") == len(expected_controls)
        and all(
            isinstance(controls.get(control), dict)
            and all(
                controls[control].get(proof_key) == "pass"
                for proof_key in CAREER_REPUTATION_CONTROL_E2E_PROOF_KEYS
            )
            for control in expected_controls
        )
        and re.fullmatch(r"[0-9a-f]{64}", apk_sha)
    ):
        return None
    return {
        "status": "executed_api36",
        "ref": CAREER_REPUTATION_PHONE_E2E_RECEIPT.relative_to(REPO_ROOT).as_posix(),
        "receiptSha256": _sha256_file(CAREER_REPUTATION_PHONE_E2E_RECEIPT),
        "apkSha256": apk_sha,
    }


def _validated_situational_modifiers_phone_e2e_receipt(
    presentation_root: Path,
    core_root: Path,
) -> dict[str, Any] | None:
    driver = REPO_ROOT / "tests" / "run_api36_situational_modifiers_e2e.py"
    shared_driver = REPO_ROOT / "tests" / "run_api36_editing_e2e.py"
    creation_fixture = REPO_ROOT / "tests" / "fixtures" / "creation-situational-modifiers-e2e.chum5"
    career_fixture = REPO_ROOT / "tests" / "fixtures" / "career-situational-modifiers-e2e.chum5"
    source_digests = {
        "modifiersPageSha256": REPO_ROOT / "src" / "Chummer.Android" / "Native" / "SituationalModifiersPage.cs",
        "buildPageSha256": REPO_ROOT / "src" / "Chummer.Android" / "Native" / "BuildPage.cs",
        "coordinatorSha256": REPO_ROOT / "src" / "Chummer.Android" / "Native" / "RunnerSessionCoordinator.cs",
        "modifiersContractSha256": presentation_root / "Chummer.Presentation" / "Overview" / "SituationalModifiersEditRequest.cs",
        "mutationCatalogSha256": presentation_root / "Chummer.Presentation" / "Overview" / "WorkspaceXmlMutationCatalog.cs",
        "presenterMutationSha256": presentation_root / "Chummer.Presentation" / "Overview" / "CharacterOverviewPresenter.WorkspaceMutations.cs",
        "presenterInterfaceSha256": presentation_root / "Chummer.Presentation" / "Overview" / "ICharacterOverviewPresenter.cs",
        "progressContractSha256": core_root / "Chummer.Contracts" / "Characters" / "CharacterSectionModels.cs",
        "sectionServiceSha256": core_root / "Chummer.Infrastructure" / "Xml" / "CharacterSectionService.cs",
    }
    if not all(
        path.is_file()
        for path in (driver, shared_driver, creation_fixture, career_fixture, *source_digests.values())
    ):
        return None

    try:
        receipt = json.loads(_read_text(SITUATIONAL_MODIFIERS_PHONE_E2E_RECEIPT))
    except (FileNotFoundError, json.JSONDecodeError, OSError):
        return None
    journeys = receipt.get("journeys")
    controls = receipt.get("controls")
    apk_sha = str(receipt.get("apkSha256") or "")
    expected_controls = {
        f"{form}.{control}"
        for form in ("CharacterCreate", "CharacterCareer")
        for control in SITUATIONAL_MODIFIER_CONTROLS
    }
    if not (
        receipt.get("schema") == "chummer.android.editing-e2e/v1"
        and receipt.get("status") == "pass"
        and receipt.get("profile") == "phone"
        and receipt.get("journey") == "situational-modifiers"
        and receipt.get("apiLevel") == 36
        and receipt.get("driverSha256") == _sha256_file(driver)
        and receipt.get("sharedDriverSha256") == _sha256_file(shared_driver)
        and receipt.get("creationFixtureSha256") == _sha256_file(creation_fixture)
        and receipt.get("careerFixtureSha256") == _sha256_file(career_fixture)
        and all(receipt.get(key) == _sha256_file(path) for key, path in source_digests.items())
        and isinstance(journeys, dict)
        and all(journeys.get(journey) == "pass" for journey in SITUATIONAL_MODIFIERS_E2E_JOURNEYS)
        and isinstance(controls, dict)
        and set(controls) == expected_controls
        and receipt.get("controlCount") == len(expected_controls)
        and all(
            isinstance(controls.get(control), dict)
            and all(
                controls[control].get(proof_key) == "pass"
                for proof_key in SITUATIONAL_MODIFIERS_CONTROL_E2E_PROOF_KEYS
            )
            for control in expected_controls
        )
        and re.fullmatch(r"[0-9a-f]{64}", apk_sha)
    ):
        return None
    return {
        "status": "executed_api36",
        "ref": SITUATIONAL_MODIFIERS_PHONE_E2E_RECEIPT.relative_to(REPO_ROOT).as_posix(),
        "receiptSha256": _sha256_file(SITUATIONAL_MODIFIERS_PHONE_E2E_RECEIPT),
        "apkSha256": apk_sha,
    }


def _validated_primary_arm_phone_e2e_receipt(
    presentation_root: Path,
    core_root: Path,
) -> dict[str, Any] | None:
    driver = REPO_ROOT / "tests" / "run_api36_primary_arm_e2e.py"
    shared_driver = REPO_ROOT / "tests" / "run_api36_editing_e2e.py"
    creation_fixture = REPO_ROOT / "tests" / "fixtures" / "creation-primary-arm-e2e.chum5"
    career_fixture = REPO_ROOT / "tests" / "fixtures" / "career-primary-arm-e2e.chum5"
    ambidextrous_fixture = REPO_ROOT / "tests" / "fixtures" / "ambidextrous-primary-arm-e2e.chum5"
    source_digests = {
        "primaryArmPageSha256": REPO_ROOT / "src" / "Chummer.Android" / "Native" / "PrimaryArmPage.cs",
        "buildPageSha256": REPO_ROOT / "src" / "Chummer.Android" / "Native" / "BuildPage.cs",
        "coordinatorSha256": REPO_ROOT / "src" / "Chummer.Android" / "Native" / "RunnerSessionCoordinator.cs",
        "primaryArmContractSha256": presentation_root / "Chummer.Presentation" / "Overview" / "PrimaryArmEditRequest.cs",
        "mutationCatalogSha256": presentation_root / "Chummer.Presentation" / "Overview" / "WorkspaceXmlMutationCatalog.cs",
        "presenterMutationSha256": presentation_root / "Chummer.Presentation" / "Overview" / "CharacterOverviewPresenter.WorkspaceMutations.cs",
        "presenterInterfaceSha256": presentation_root / "Chummer.Presentation" / "Overview" / "ICharacterOverviewPresenter.cs",
        "profileContractSha256": core_root / "Chummer.Contracts" / "Characters" / "CharacterSectionModels.cs",
        "sectionServiceSha256": core_root / "Chummer.Infrastructure" / "Xml" / "CharacterSectionService.cs",
    }
    if not all(
        path.is_file()
        for path in (
            driver,
            shared_driver,
            creation_fixture,
            career_fixture,
            ambidextrous_fixture,
            *source_digests.values(),
        )
    ):
        return None

    try:
        receipt = json.loads(_read_text(PRIMARY_ARM_PHONE_E2E_RECEIPT))
    except (FileNotFoundError, json.JSONDecodeError, OSError):
        return None
    journeys = receipt.get("journeys")
    controls = receipt.get("controls")
    apk_sha = str(receipt.get("apkSha256") or "")
    expected_controls = {
        f"{form}.cboPrimaryArm" for form in ("CharacterCreate", "CharacterCareer")
    }
    if not (
        receipt.get("schema") == "chummer.android.editing-e2e/v1"
        and receipt.get("status") == "pass"
        and receipt.get("profile") == "phone"
        and receipt.get("journey") == "primary-arm"
        and receipt.get("apiLevel") == 36
        and receipt.get("driverSha256") == _sha256_file(driver)
        and receipt.get("sharedDriverSha256") == _sha256_file(shared_driver)
        and receipt.get("creationFixtureSha256") == _sha256_file(creation_fixture)
        and receipt.get("careerFixtureSha256") == _sha256_file(career_fixture)
        and receipt.get("ambidextrousFixtureSha256") == _sha256_file(ambidextrous_fixture)
        and all(receipt.get(key) == _sha256_file(path) for key, path in source_digests.items())
        and isinstance(journeys, dict)
        and all(journeys.get(journey) == "pass" for journey in PRIMARY_ARM_E2E_JOURNEYS)
        and isinstance(controls, dict)
        and set(controls) == expected_controls
        and receipt.get("controlCount") == len(expected_controls)
        and all(
            isinstance(controls.get(control), dict)
            and all(
                controls[control].get(proof_key) == "pass"
                for proof_key in PRIMARY_ARM_CONTROL_E2E_PROOF_KEYS
            )
            for control in expected_controls
        )
        and re.fullmatch(r"[0-9a-f]{64}", apk_sha)
    ):
        return None
    return {
        "status": "executed_api36",
        "ref": PRIMARY_ARM_PHONE_E2E_RECEIPT.relative_to(REPO_ROOT).as_posix(),
        "receiptSha256": _sha256_file(PRIMARY_ARM_PHONE_E2E_RECEIPT),
        "apkSha256": apk_sha,
    }


def _validated_gear_location_phone_e2e_receipt(
    presentation_root: Path,
) -> dict[str, Any] | None:
    driver = REPO_ROOT / "tests" / "run_api36_gear_location_e2e.py"
    shared_driver = REPO_ROOT / "tests" / "run_api36_editing_e2e.py"
    creation_fixture = REPO_ROOT / "tests" / "fixtures" / "creation-gear-location-e2e.chum5"
    career_fixture = REPO_ROOT / "tests" / "fixtures" / "career-gear-location-e2e.chum5"
    source_digests = {
        "gearLocationPageSha256": REPO_ROOT / "src" / "Chummer.Android" / "Native" / "GearLocationAddPage.cs",
        "buildFlowPagesSha256": REPO_ROOT / "src" / "Chummer.Android" / "Native" / "BuildFlowPages.cs",
        "coordinatorSha256": REPO_ROOT / "src" / "Chummer.Android" / "Native" / "RunnerSessionCoordinator.cs",
        "gearLocationContractSha256": presentation_root / "Chummer.Presentation" / "Overview" / "GearLocationAddRequest.cs",
        "mutationCatalogSha256": presentation_root / "Chummer.Presentation" / "Overview" / "WorkspaceXmlMutationCatalog.cs",
        "presenterMutationSha256": presentation_root / "Chummer.Presentation" / "Overview" / "CharacterOverviewPresenter.WorkspaceMutations.cs",
        "presenterInterfaceSha256": presentation_root / "Chummer.Presentation" / "Overview" / "ICharacterOverviewPresenter.cs",
    }
    if not all(
        path.is_file()
        for path in (
            driver,
            shared_driver,
            creation_fixture,
            career_fixture,
            *source_digests.values(),
        )
    ):
        return None

    try:
        receipt = json.loads(_read_text(GEAR_LOCATION_PHONE_E2E_RECEIPT))
    except (FileNotFoundError, json.JSONDecodeError, OSError):
        return None
    journeys = receipt.get("journeys")
    controls = receipt.get("controls")
    apk_sha = str(receipt.get("apkSha256") or "")
    expected_controls = {
        f"{form}.{GEAR_LOCATION_ADD_CONTROL}"
        for form in ("CharacterCreate", "CharacterCareer")
    }
    if not (
        receipt.get("schema") == "chummer.android.editing-e2e/v1"
        and receipt.get("status") == "pass"
        and receipt.get("profile") == "phone"
        and receipt.get("journey") == "gear-location-add"
        and receipt.get("apiLevel") == 36
        and receipt.get("driverSha256") == _sha256_file(driver)
        and receipt.get("sharedDriverSha256") == _sha256_file(shared_driver)
        and receipt.get("creationFixtureSha256") == _sha256_file(creation_fixture)
        and receipt.get("careerFixtureSha256") == _sha256_file(career_fixture)
        and all(receipt.get(key) == _sha256_file(path) for key, path in source_digests.items())
        and isinstance(journeys, dict)
        and all(journeys.get(journey) == "pass" for journey in GEAR_LOCATION_E2E_JOURNEYS)
        and isinstance(controls, dict)
        and set(controls) == expected_controls
        and receipt.get("controlCount") == len(expected_controls)
        and all(
            isinstance(controls.get(control), dict)
            and all(
                controls[control].get(proof_key) == "pass"
                for proof_key in GEAR_LOCATION_CONTROL_E2E_PROOF_KEYS
            )
            for control in expected_controls
        )
        and re.fullmatch(r"[0-9a-f]{64}", apk_sha)
    ):
        return None
    return {
        "status": "executed_api36",
        "ref": GEAR_LOCATION_PHONE_E2E_RECEIPT.relative_to(REPO_ROOT).as_posix(),
        "receiptSha256": _sha256_file(GEAR_LOCATION_PHONE_E2E_RECEIPT),
        "apkSha256": apk_sha,
    }


def _validated_weapon_location_phone_e2e_receipt(
    presentation_root: Path,
) -> dict[str, Any] | None:
    driver = REPO_ROOT / "tests" / "run_api36_weapon_location_e2e.py"
    shared_driver = REPO_ROOT / "tests" / "run_api36_editing_e2e.py"
    creation_fixture = REPO_ROOT / "tests" / "fixtures" / "creation-weapon-location-e2e.chum5"
    career_fixture = REPO_ROOT / "tests" / "fixtures" / "career-weapon-location-e2e.chum5"
    overview = presentation_root / "Chummer.Presentation" / "Overview"
    source_digests = {
        "weaponLocationPageSha256": REPO_ROOT / "src" / "Chummer.Android" / "Native" / "WeaponLocationAddPage.cs",
        "buildFlowPagesSha256": REPO_ROOT / "src" / "Chummer.Android" / "Native" / "BuildFlowPages.cs",
        "coordinatorSha256": REPO_ROOT / "src" / "Chummer.Android" / "Native" / "RunnerSessionCoordinator.cs",
        "weaponLocationContractSha256": overview / "WeaponLocationAddRequest.cs",
        "mutationCatalogSha256": overview / "WorkspaceXmlMutationCatalog.cs",
        "presenterMutationSha256": overview / "CharacterOverviewPresenter.WorkspaceMutations.cs",
        "presenterInterfaceSha256": overview / "ICharacterOverviewPresenter.cs",
    }
    if not all(
        path.is_file()
        for path in (
            driver,
            shared_driver,
            creation_fixture,
            career_fixture,
            *source_digests.values(),
        )
    ):
        return None

    try:
        receipt = json.loads(_read_text(WEAPON_LOCATION_PHONE_E2E_RECEIPT))
    except (FileNotFoundError, json.JSONDecodeError, OSError):
        return None
    journeys = receipt.get("journeys")
    controls = receipt.get("controls")
    apk_sha = str(receipt.get("apkSha256") or "")
    expected_controls = {
        f"{form}.{WEAPON_LOCATION_ADD_CONTROL}"
        for form in ("CharacterCreate", "CharacterCareer")
    }
    if not (
        receipt.get("schema") == "chummer.android.editing-e2e/v1"
        and receipt.get("status") == "pass"
        and receipt.get("profile") == "phone"
        and receipt.get("journey") == "weapon-location-add"
        and receipt.get("apiLevel") == 36
        and receipt.get("driverSha256") == _sha256_file(driver)
        and receipt.get("sharedDriverSha256") == _sha256_file(shared_driver)
        and receipt.get("creationFixtureSha256") == _sha256_file(creation_fixture)
        and receipt.get("careerFixtureSha256") == _sha256_file(career_fixture)
        and all(receipt.get(key) == _sha256_file(path) for key, path in source_digests.items())
        and isinstance(journeys, dict)
        and all(journeys.get(journey) == "pass" for journey in WEAPON_LOCATION_E2E_JOURNEYS)
        and isinstance(controls, dict)
        and set(controls) == expected_controls
        and receipt.get("controlCount") == len(expected_controls)
        and all(
            isinstance(controls.get(control), dict)
            and all(
                controls[control].get(proof_key) == "pass"
                for proof_key in WEAPON_LOCATION_CONTROL_E2E_PROOF_KEYS
            )
            for control in expected_controls
        )
        and re.fullmatch(r"[0-9a-f]{64}", apk_sha)
    ):
        return None
    return {
        "status": "executed_api36",
        "ref": WEAPON_LOCATION_PHONE_E2E_RECEIPT.relative_to(REPO_ROOT).as_posix(),
        "receiptSha256": _sha256_file(WEAPON_LOCATION_PHONE_E2E_RECEIPT),
        "apkSha256": apk_sha,
    }


def _validated_vehicle_location_phone_e2e_receipt(
    presentation_root: Path,
    core_engine_root: Path,
) -> dict[str, Any] | None:
    driver = REPO_ROOT / "tests" / "run_api36_vehicle_location_e2e.py"
    shared_driver = REPO_ROOT / "tests" / "run_api36_editing_e2e.py"
    creation_fixture = REPO_ROOT / "tests" / "fixtures" / "creation-vehicle-location-e2e.chum5"
    career_fixture = REPO_ROOT / "tests" / "fixtures" / "career-vehicle-location-e2e.chum5"
    overview = presentation_root / "Chummer.Presentation" / "Overview"
    source_digests = {
        "vehicleLocationPageSha256": REPO_ROOT / "src" / "Chummer.Android" / "Native" / "VehicleLocationAddPage.cs",
        "buildFlowPagesSha256": REPO_ROOT / "src" / "Chummer.Android" / "Native" / "BuildFlowPages.cs",
        "collectionEditorPagesSha256": REPO_ROOT / "src" / "Chummer.Android" / "Native" / "CollectionEditorPages.cs",
        "coordinatorSha256": REPO_ROOT / "src" / "Chummer.Android" / "Native" / "RunnerSessionCoordinator.cs",
        "vehicleLocationContractSha256": overview / "VehicleLocationAddRequest.cs",
        "collectionEditorStateSha256": overview / "WorkspaceCollectionEditorState.cs",
        "collectionEditorProjectorSha256": overview / "WorkspaceCollectionEditorProjector.cs",
        "mutationCatalogSha256": overview / "WorkspaceXmlMutationCatalog.cs",
        "presenterMutationSha256": overview / "CharacterOverviewPresenter.WorkspaceMutations.cs",
        "presenterInterfaceSha256": overview / "ICharacterOverviewPresenter.cs",
        "characterSectionModelsSha256": core_engine_root / "Chummer.Contracts" / "Characters" / "CharacterSectionModels.cs",
        "characterSectionServiceSha256": core_engine_root / "Chummer.Infrastructure" / "Xml" / "CharacterSectionService.cs",
    }
    if not all(
        path.is_file()
        for path in (
            driver,
            shared_driver,
            creation_fixture,
            career_fixture,
            *source_digests.values(),
        )
    ):
        return None

    try:
        receipt = json.loads(_read_text(VEHICLE_LOCATION_PHONE_E2E_RECEIPT))
    except (FileNotFoundError, json.JSONDecodeError, OSError):
        return None
    journeys = receipt.get("journeys")
    controls = receipt.get("controls")
    apk_sha = str(receipt.get("apkSha256") or "")
    expected_controls = {
        f"{form}.{VEHICLE_LOCATION_ADD_CONTROL}"
        for form in ("CharacterCreate", "CharacterCareer")
    }
    if not (
        receipt.get("schema") == "chummer.android.editing-e2e/v1"
        and receipt.get("status") == "pass"
        and receipt.get("profile") == "phone"
        and receipt.get("journey") == "vehicle-location-add"
        and receipt.get("apiLevel") == 36
        and receipt.get("driverSha256") == _sha256_file(driver)
        and receipt.get("sharedDriverSha256") == _sha256_file(shared_driver)
        and receipt.get("creationFixtureSha256") == _sha256_file(creation_fixture)
        and receipt.get("careerFixtureSha256") == _sha256_file(career_fixture)
        and all(receipt.get(key) == _sha256_file(path) for key, path in source_digests.items())
        and isinstance(journeys, dict)
        and all(journeys.get(journey) == "pass" for journey in VEHICLE_LOCATION_E2E_JOURNEYS)
        and isinstance(controls, dict)
        and set(controls) == expected_controls
        and receipt.get("controlCount") == len(expected_controls)
        and all(
            isinstance(controls.get(control), dict)
            and all(
                controls[control].get(proof_key) == "pass"
                for proof_key in VEHICLE_LOCATION_CONTROL_E2E_PROOF_KEYS
            )
            for control in expected_controls
        )
        and re.fullmatch(r"[0-9a-f]{64}", apk_sha)
    ):
        return None
    return {
        "status": "executed_api36",
        "ref": VEHICLE_LOCATION_PHONE_E2E_RECEIPT.relative_to(REPO_ROOT).as_posix(),
        "receiptSha256": _sha256_file(VEHICLE_LOCATION_PHONE_E2E_RECEIPT),
        "apkSha256": apk_sha,
    }


def _validated_vehicle_home_node_phone_e2e_receipt(
    presentation_root: Path,
    core_engine_root: Path,
) -> dict[str, Any] | None:
    driver = REPO_ROOT / "tests" / "run_api36_vehicle_home_node_e2e.py"
    shared_driver = REPO_ROOT / "tests" / "run_api36_editing_e2e.py"
    creation_fixture = REPO_ROOT / "tests" / "fixtures" / "creation-vehicle-home-node-e2e.chum5"
    career_fixture = REPO_ROOT / "tests" / "fixtures" / "career-vehicle-home-node-e2e.chum5"
    overview = presentation_root / "Chummer.Presentation" / "Overview"
    source_digests = {
        "vehicleHomeNodePageSha256": REPO_ROOT / "src" / "Chummer.Android" / "Native" / "VehicleHomeNodePage.cs",
        "collectionEditorPagesSha256": REPO_ROOT / "src" / "Chummer.Android" / "Native" / "CollectionEditorPages.cs",
        "coordinatorSha256": REPO_ROOT / "src" / "Chummer.Android" / "Native" / "RunnerSessionCoordinator.cs",
        "vehicleHomeNodeContractSha256": overview / "VehicleHomeNodeEditRequest.cs",
        "collectionEditorStateSha256": overview / "WorkspaceCollectionEditorState.cs",
        "collectionEditorProjectorSha256": overview / "WorkspaceCollectionEditorProjector.cs",
        "mutationCatalogSha256": overview / "WorkspaceXmlMutationCatalog.cs",
        "presenterMutationSha256": overview / "CharacterOverviewPresenter.WorkspaceMutations.cs",
        "presenterInterfaceSha256": overview / "ICharacterOverviewPresenter.cs",
        "characterSectionModelsSha256": core_engine_root / "Chummer.Contracts" / "Characters" / "CharacterSectionModels.cs",
        "characterSectionServiceSha256": core_engine_root / "Chummer.Infrastructure" / "Xml" / "CharacterSectionService.cs",
    }
    if not all(
        path.is_file()
        for path in (driver, shared_driver, creation_fixture, career_fixture, *source_digests.values())
    ):
        return None

    try:
        receipt = json.loads(_read_text(VEHICLE_HOME_NODE_PHONE_E2E_RECEIPT))
    except (FileNotFoundError, json.JSONDecodeError, OSError):
        return None
    journeys = receipt.get("journeys")
    controls = receipt.get("controls")
    apk_sha = str(receipt.get("apkSha256") or "")
    expected_controls = {
        f"{form}.{VEHICLE_HOME_NODE_CONTROL}"
        for form in ("CharacterCreate", "CharacterCareer")
    }
    if not (
        receipt.get("schema") == "chummer.android.editing-e2e/v1"
        and receipt.get("status") == "pass"
        and receipt.get("profile") == "phone"
        and receipt.get("journey") == "vehicle-home-node"
        and receipt.get("apiLevel") == 36
        and receipt.get("driverSha256") == _sha256_file(driver)
        and receipt.get("sharedDriverSha256") == _sha256_file(shared_driver)
        and receipt.get("creationFixtureSha256") == _sha256_file(creation_fixture)
        and receipt.get("careerFixtureSha256") == _sha256_file(career_fixture)
        and all(receipt.get(key) == _sha256_file(path) for key, path in source_digests.items())
        and isinstance(journeys, dict)
        and all(journeys.get(journey) == "pass" for journey in VEHICLE_HOME_NODE_E2E_JOURNEYS)
        and isinstance(controls, dict)
        and set(controls) == expected_controls
        and receipt.get("controlCount") == len(expected_controls)
        and all(
            isinstance(controls.get(control), dict)
            and all(
                controls[control].get(proof_key) == "pass"
                for proof_key in VEHICLE_HOME_NODE_CONTROL_E2E_PROOF_KEYS
            )
            for control in expected_controls
        )
        and re.fullmatch(r"[0-9a-f]{64}", apk_sha)
    ):
        return None
    return {
        "status": "executed_api36",
        "ref": VEHICLE_HOME_NODE_PHONE_E2E_RECEIPT.relative_to(REPO_ROOT).as_posix(),
        "receiptSha256": _sha256_file(VEHICLE_HOME_NODE_PHONE_E2E_RECEIPT),
        "apkSha256": apk_sha,
    }


def _validated_armor_home_node_phone_e2e_receipt(
    presentation_root: Path,
    core_engine_root: Path,
) -> dict[str, Any] | None:
    driver = REPO_ROOT / "tests" / "run_api36_armor_home_node_e2e.py"
    shared_driver = REPO_ROOT / "tests" / "run_api36_editing_e2e.py"
    creation_fixture = REPO_ROOT / "tests" / "fixtures" / "creation-armor-home-node-e2e.chum5"
    career_fixture = REPO_ROOT / "tests" / "fixtures" / "career-armor-home-node-e2e.chum5"
    overview = presentation_root / "Chummer.Presentation" / "Overview"
    source_digests = {
        "armorHomeNodePageSha256": REPO_ROOT / "src" / "Chummer.Android" / "Native" / "ArmorHomeNodePage.cs",
        "collectionEditorPagesSha256": REPO_ROOT / "src" / "Chummer.Android" / "Native" / "CollectionEditorPages.cs",
        "coordinatorSha256": REPO_ROOT / "src" / "Chummer.Android" / "Native" / "RunnerSessionCoordinator.cs",
        "armorHomeNodeContractSha256": overview / "ArmorHomeNodeEditRequest.cs",
        "collectionEditorStateSha256": overview / "WorkspaceCollectionEditorState.cs",
        "collectionEditorProjectorSha256": overview / "WorkspaceCollectionEditorProjector.cs",
        "mutationCatalogSha256": overview / "WorkspaceXmlMutationCatalog.cs",
        "presenterMutationSha256": overview / "CharacterOverviewPresenter.WorkspaceMutations.cs",
        "presenterInterfaceSha256": overview / "ICharacterOverviewPresenter.cs",
        "characterSectionModelsSha256": core_engine_root / "Chummer.Contracts" / "Characters" / "CharacterSectionModels.cs",
        "characterSectionServiceSha256": core_engine_root / "Chummer.Infrastructure" / "Xml" / "CharacterSectionService.cs",
    }
    if not all(
        path.is_file()
        for path in (driver, shared_driver, creation_fixture, career_fixture, *source_digests.values())
    ):
        return None

    try:
        receipt = json.loads(_read_text(ARMOR_HOME_NODE_PHONE_E2E_RECEIPT))
    except (FileNotFoundError, json.JSONDecodeError, OSError):
        return None
    journeys = receipt.get("journeys")
    controls = receipt.get("controls")
    apk_sha = str(receipt.get("apkSha256") or "")
    expected_controls = {
        f"{form}.{ARMOR_HOME_NODE_CONTROL}"
        for form in ("CharacterCreate", "CharacterCareer")
    }
    if not (
        receipt.get("schema") == "chummer.android.editing-e2e/v1"
        and receipt.get("status") == "pass"
        and receipt.get("profile") == "phone"
        and receipt.get("journey") == "armor-home-node"
        and receipt.get("apiLevel") == 36
        and receipt.get("driverSha256") == _sha256_file(driver)
        and receipt.get("sharedDriverSha256") == _sha256_file(shared_driver)
        and receipt.get("creationFixtureSha256") == _sha256_file(creation_fixture)
        and receipt.get("careerFixtureSha256") == _sha256_file(career_fixture)
        and all(receipt.get(key) == _sha256_file(path) for key, path in source_digests.items())
        and isinstance(journeys, dict)
        and all(journeys.get(journey) == "pass" for journey in ARMOR_HOME_NODE_E2E_JOURNEYS)
        and isinstance(controls, dict)
        and set(controls) == expected_controls
        and receipt.get("controlCount") == len(expected_controls)
        and all(
            isinstance(controls.get(control), dict)
            and all(
                controls[control].get(proof_key) == "pass"
                for proof_key in ARMOR_HOME_NODE_CONTROL_E2E_PROOF_KEYS
            )
            for control in expected_controls
        )
        and re.fullmatch(r"[0-9a-f]{64}", apk_sha)
    ):
        return None
    return {
        "status": "executed_api36",
        "ref": ARMOR_HOME_NODE_PHONE_E2E_RECEIPT.relative_to(REPO_ROOT).as_posix(),
        "receiptSha256": _sha256_file(ARMOR_HOME_NODE_PHONE_E2E_RECEIPT),
        "apkSha256": apk_sha,
    }


def _validated_weapon_home_node_phone_e2e_receipt(
    presentation_root: Path,
    core_engine_root: Path,
) -> dict[str, Any] | None:
    driver = REPO_ROOT / "tests" / "run_api36_weapon_home_node_e2e.py"
    shared_driver = REPO_ROOT / "tests" / "run_api36_editing_e2e.py"
    creation_fixture = REPO_ROOT / "tests" / "fixtures" / "creation-weapon-home-node-e2e.chum5"
    career_fixture = REPO_ROOT / "tests" / "fixtures" / "career-weapon-home-node-e2e.chum5"
    overview = presentation_root / "Chummer.Presentation" / "Overview"
    source_digests = {
        "weaponHomeNodePageSha256": REPO_ROOT / "src" / "Chummer.Android" / "Native" / "WeaponHomeNodePage.cs",
        "collectionEditorPagesSha256": REPO_ROOT / "src" / "Chummer.Android" / "Native" / "CollectionEditorPages.cs",
        "coordinatorSha256": REPO_ROOT / "src" / "Chummer.Android" / "Native" / "RunnerSessionCoordinator.cs",
        "weaponHomeNodeContractSha256": overview / "WeaponHomeNodeEditRequest.cs",
        "collectionEditorStateSha256": overview / "WorkspaceCollectionEditorState.cs",
        "collectionEditorProjectorSha256": overview / "WorkspaceCollectionEditorProjector.cs",
        "mutationCatalogSha256": overview / "WorkspaceXmlMutationCatalog.cs",
        "presenterMutationSha256": overview / "CharacterOverviewPresenter.WorkspaceMutations.cs",
        "presenterInterfaceSha256": overview / "ICharacterOverviewPresenter.cs",
        "weaponHomeNodeRulesSha256": core_engine_root / "Chummer.Contracts" / "Characters" / "CharacterWeaponHomeNodeRules.cs",
        "weaponParentResolverSha256": core_engine_root / "Chummer.Contracts" / "Characters" / "CharacterWeaponMatrixParentResolver.cs",
        "characterSectionModelsSha256": core_engine_root / "Chummer.Contracts" / "Characters" / "CharacterSectionModels.cs",
        "characterSectionServiceSha256": core_engine_root / "Chummer.Infrastructure" / "Xml" / "CharacterSectionService.cs",
    }
    if not all(
        path.is_file()
        for path in (driver, shared_driver, creation_fixture, career_fixture, *source_digests.values())
    ):
        return None

    try:
        receipt = json.loads(_read_text(WEAPON_HOME_NODE_PHONE_E2E_RECEIPT))
    except (FileNotFoundError, json.JSONDecodeError, OSError):
        return None
    journeys = receipt.get("journeys")
    controls = receipt.get("controls")
    apk_sha = str(receipt.get("apkSha256") or "")
    expected_controls = {
        f"{form}.{WEAPON_HOME_NODE_CONTROL}"
        for form in ("CharacterCreate", "CharacterCareer")
    }
    if not (
        receipt.get("schema") == "chummer.android.editing-e2e/v1"
        and receipt.get("status") == "pass"
        and receipt.get("profile") == "phone"
        and receipt.get("journey") == "weapon-home-node"
        and receipt.get("apiLevel") == 36
        and receipt.get("driverSha256") == _sha256_file(driver)
        and receipt.get("sharedDriverSha256") == _sha256_file(shared_driver)
        and receipt.get("creationFixtureSha256") == _sha256_file(creation_fixture)
        and receipt.get("careerFixtureSha256") == _sha256_file(career_fixture)
        and all(receipt.get(key) == _sha256_file(path) for key, path in source_digests.items())
        and isinstance(journeys, dict)
        and all(journeys.get(journey) == "pass" for journey in WEAPON_HOME_NODE_E2E_JOURNEYS)
        and isinstance(controls, dict)
        and set(controls) == expected_controls
        and receipt.get("controlCount") == len(expected_controls)
        and all(
            isinstance(controls.get(control), dict)
            and all(
                controls[control].get(proof_key) == "pass"
                for proof_key in WEAPON_HOME_NODE_CONTROL_E2E_PROOF_KEYS
            )
            for control in expected_controls
        )
        and re.fullmatch(r"[0-9a-f]{64}", apk_sha)
    ):
        return None
    return {
        "status": "executed_api36",
        "ref": WEAPON_HOME_NODE_PHONE_E2E_RECEIPT.relative_to(REPO_ROOT).as_posix(),
        "receiptSha256": _sha256_file(WEAPON_HOME_NODE_PHONE_E2E_RECEIPT),
        "apkSha256": apk_sha,
    }


def _validated_armor_active_commlink_phone_e2e_receipt(
    presentation_root: Path,
    core_engine_root: Path,
) -> dict[str, Any] | None:
    driver = REPO_ROOT / "tests" / "run_api36_armor_active_commlink_e2e.py"
    shared_driver = REPO_ROOT / "tests" / "run_api36_editing_e2e.py"
    creation_fixture = REPO_ROOT / "tests" / "fixtures" / "creation-armor-active-commlink-e2e.chum5"
    career_fixture = REPO_ROOT / "tests" / "fixtures" / "career-armor-active-commlink-e2e.chum5"
    overview = presentation_root / "Chummer.Presentation" / "Overview"
    source_digests = {
        "armorActiveCommlinkPageSha256": REPO_ROOT / "src" / "Chummer.Android" / "Native" / "ArmorActiveCommlinkPage.cs",
        "collectionEditorPagesSha256": REPO_ROOT / "src" / "Chummer.Android" / "Native" / "CollectionEditorPages.cs",
        "coordinatorSha256": REPO_ROOT / "src" / "Chummer.Android" / "Native" / "RunnerSessionCoordinator.cs",
        "armorActiveCommlinkContractSha256": overview / "ArmorActiveCommlinkEditRequest.cs",
        "collectionEditorStateSha256": overview / "WorkspaceCollectionEditorState.cs",
        "collectionEditorProjectorSha256": overview / "WorkspaceCollectionEditorProjector.cs",
        "mutationCatalogSha256": overview / "WorkspaceXmlMutationCatalog.cs",
        "presenterMutationSha256": overview / "CharacterOverviewPresenter.WorkspaceMutations.cs",
        "presenterInterfaceSha256": overview / "ICharacterOverviewPresenter.cs",
        "characterSectionModelsSha256": core_engine_root / "Chummer.Contracts" / "Characters" / "CharacterSectionModels.cs",
        "characterSectionServiceSha256": core_engine_root / "Chummer.Infrastructure" / "Xml" / "CharacterSectionService.cs",
    }
    if not all(
        path.is_file()
        for path in (driver, shared_driver, creation_fixture, career_fixture, *source_digests.values())
    ):
        return None

    try:
        receipt = json.loads(_read_text(ARMOR_ACTIVE_COMMLINK_PHONE_E2E_RECEIPT))
    except (FileNotFoundError, json.JSONDecodeError, OSError):
        return None
    journeys = receipt.get("journeys")
    controls = receipt.get("controls")
    apk_sha = str(receipt.get("apkSha256") or "")
    expected_controls = {
        f"{form}.{ARMOR_ACTIVE_COMMLINK_CONTROL}"
        for form in ("CharacterCreate", "CharacterCareer")
    }
    if not (
        receipt.get("schema") == "chummer.android.editing-e2e/v1"
        and receipt.get("status") == "pass"
        and receipt.get("profile") == "phone"
        and receipt.get("journey") == "armor-active-commlink"
        and receipt.get("apiLevel") == 36
        and receipt.get("driverSha256") == _sha256_file(driver)
        and receipt.get("sharedDriverSha256") == _sha256_file(shared_driver)
        and receipt.get("creationFixtureSha256") == _sha256_file(creation_fixture)
        and receipt.get("careerFixtureSha256") == _sha256_file(career_fixture)
        and all(receipt.get(key) == _sha256_file(path) for key, path in source_digests.items())
        and isinstance(journeys, dict)
        and all(journeys.get(journey) == "pass" for journey in ARMOR_ACTIVE_COMMLINK_E2E_JOURNEYS)
        and isinstance(controls, dict)
        and set(controls) == expected_controls
        and receipt.get("controlCount") == len(expected_controls)
        and all(
            isinstance(controls.get(control), dict)
            and all(
                controls[control].get(proof_key) == "pass"
                for proof_key in ARMOR_ACTIVE_COMMLINK_CONTROL_E2E_PROOF_KEYS
            )
            for control in expected_controls
        )
        and re.fullmatch(r"[0-9a-f]{64}", apk_sha)
    ):
        return None
    return {
        "status": "executed_api36",
        "ref": ARMOR_ACTIVE_COMMLINK_PHONE_E2E_RECEIPT.relative_to(REPO_ROOT).as_posix(),
        "receiptSha256": _sha256_file(ARMOR_ACTIVE_COMMLINK_PHONE_E2E_RECEIPT),
        "apkSha256": apk_sha,
    }


def _validated_weapon_active_commlink_phone_e2e_receipt(
    presentation_root: Path,
    core_engine_root: Path,
) -> dict[str, Any] | None:
    driver = REPO_ROOT / "tests" / "run_api36_weapon_active_commlink_e2e.py"
    shared_driver = REPO_ROOT / "tests" / "run_api36_editing_e2e.py"
    creation_fixture = REPO_ROOT / "tests" / "fixtures" / "creation-weapon-active-commlink-e2e.chum5"
    career_fixture = REPO_ROOT / "tests" / "fixtures" / "career-weapon-active-commlink-e2e.chum5"
    overview = presentation_root / "Chummer.Presentation" / "Overview"
    source_digests = {
        "weaponActiveCommlinkPageSha256": REPO_ROOT / "src" / "Chummer.Android" / "Native" / "WeaponActiveCommlinkPage.cs",
        "collectionEditorPagesSha256": REPO_ROOT / "src" / "Chummer.Android" / "Native" / "CollectionEditorPages.cs",
        "coordinatorSha256": REPO_ROOT / "src" / "Chummer.Android" / "Native" / "RunnerSessionCoordinator.cs",
        "weaponActiveCommlinkContractSha256": overview / "WeaponActiveCommlinkEditRequest.cs",
        "collectionEditorStateSha256": overview / "WorkspaceCollectionEditorState.cs",
        "collectionEditorProjectorSha256": overview / "WorkspaceCollectionEditorProjector.cs",
        "mutationCatalogSha256": overview / "WorkspaceXmlMutationCatalog.cs",
        "presenterMutationSha256": overview / "CharacterOverviewPresenter.WorkspaceMutations.cs",
        "presenterInterfaceSha256": overview / "ICharacterOverviewPresenter.cs",
        "weaponActiveCommlinkRulesSha256": core_engine_root / "Chummer.Contracts" / "Characters" / "CharacterWeaponActiveCommlinkRules.cs",
        "weaponHomeNodeRulesSha256": core_engine_root / "Chummer.Contracts" / "Characters" / "CharacterWeaponHomeNodeRules.cs",
        "weaponParentResolverSha256": core_engine_root / "Chummer.Contracts" / "Characters" / "CharacterWeaponMatrixParentResolver.cs",
        "characterSectionModelsSha256": core_engine_root / "Chummer.Contracts" / "Characters" / "CharacterSectionModels.cs",
        "characterSectionServiceSha256": core_engine_root / "Chummer.Infrastructure" / "Xml" / "CharacterSectionService.cs",
    }
    if not all(
        path.is_file()
        for path in (driver, shared_driver, creation_fixture, career_fixture, *source_digests.values())
    ):
        return None

    try:
        receipt = json.loads(_read_text(WEAPON_ACTIVE_COMMLINK_PHONE_E2E_RECEIPT))
    except (FileNotFoundError, json.JSONDecodeError, OSError):
        return None
    journeys = receipt.get("journeys")
    controls = receipt.get("controls")
    apk_sha = str(receipt.get("apkSha256") or "")
    expected_controls = {
        f"{form}.{WEAPON_ACTIVE_COMMLINK_CONTROL}"
        for form in ("CharacterCreate", "CharacterCareer")
    }
    if not (
        receipt.get("schema") == "chummer.android.editing-e2e/v1"
        and receipt.get("status") == "pass"
        and receipt.get("profile") == "phone"
        and receipt.get("journey") == "weapon-active-commlink"
        and receipt.get("apiLevel") == 36
        and receipt.get("driverSha256") == _sha256_file(driver)
        and receipt.get("sharedDriverSha256") == _sha256_file(shared_driver)
        and receipt.get("creationFixtureSha256") == _sha256_file(creation_fixture)
        and receipt.get("careerFixtureSha256") == _sha256_file(career_fixture)
        and all(receipt.get(key) == _sha256_file(path) for key, path in source_digests.items())
        and isinstance(journeys, dict)
        and all(journeys.get(journey) == "pass" for journey in WEAPON_ACTIVE_COMMLINK_E2E_JOURNEYS)
        and isinstance(controls, dict)
        and set(controls) == expected_controls
        and receipt.get("controlCount") == len(expected_controls)
        and all(
            isinstance(controls.get(control), dict)
            and all(
                controls[control].get(proof_key) == "pass"
                for proof_key in WEAPON_ACTIVE_COMMLINK_CONTROL_E2E_PROOF_KEYS
            )
            for control in expected_controls
        )
        and re.fullmatch(r"[0-9a-f]{64}", apk_sha)
    ):
        return None
    return {
        "status": "executed_api36",
        "ref": WEAPON_ACTIVE_COMMLINK_PHONE_E2E_RECEIPT.relative_to(REPO_ROOT).as_posix(),
        "receiptSha256": _sha256_file(WEAPON_ACTIVE_COMMLINK_PHONE_E2E_RECEIPT),
        "apkSha256": apk_sha,
    }


def _validated_armor_damage_phone_e2e_receipt(
    presentation_root: Path,
    core_engine_root: Path,
) -> dict[str, Any] | None:
    driver = REPO_ROOT / "tests" / "run_api36_armor_damage_e2e.py"
    shared_driver = REPO_ROOT / "tests" / "run_api36_editing_e2e.py"
    career_fixture = REPO_ROOT / "tests" / "fixtures" / "career-armor-damage-e2e.chum5"
    overview = presentation_root / "Chummer.Presentation" / "Overview"
    source_digests = {
        "armorDamagePageSha256": REPO_ROOT / "src" / "Chummer.Android" / "Native" / "ArmorDamagePage.cs",
        "collectionEditorPagesSha256": REPO_ROOT / "src" / "Chummer.Android" / "Native" / "CollectionEditorPages.cs",
        "coordinatorSha256": REPO_ROOT / "src" / "Chummer.Android" / "Native" / "RunnerSessionCoordinator.cs",
        "armorDamageContractSha256": overview / "ArmorDamageAdjustmentRequest.cs",
        "collectionEditorStateSha256": overview / "WorkspaceCollectionEditorState.cs",
        "collectionEditorProjectorSha256": overview / "WorkspaceCollectionEditorProjector.cs",
        "mutationCatalogSha256": overview / "WorkspaceXmlMutationCatalog.cs",
        "presenterMutationSha256": overview / "CharacterOverviewPresenter.WorkspaceMutations.cs",
        "presenterInterfaceSha256": overview / "ICharacterOverviewPresenter.cs",
        "armorDamageRulesSha256": core_engine_root / "Chummer.Contracts" / "Characters" / "CharacterArmorDamageRules.cs",
        "characterSectionModelsSha256": core_engine_root / "Chummer.Contracts" / "Characters" / "CharacterSectionModels.cs",
        "characterSectionServiceSha256": core_engine_root / "Chummer.Infrastructure" / "Xml" / "CharacterSectionService.cs",
    }
    if not all(
        path.is_file()
        for path in (driver, shared_driver, career_fixture, *source_digests.values())
    ):
        return None

    try:
        receipt = json.loads(_read_text(ARMOR_DAMAGE_PHONE_E2E_RECEIPT))
    except (FileNotFoundError, json.JSONDecodeError, OSError):
        return None
    journeys = receipt.get("journeys")
    controls = receipt.get("controls")
    apk_sha = str(receipt.get("apkSha256") or "")
    expected_controls = {
        f"CharacterCareer.{control}"
        for control in ARMOR_DAMAGE_CONTROLS
    }
    if not (
        receipt.get("schema") == "chummer.android.editing-e2e/v1"
        and receipt.get("status") == "pass"
        and receipt.get("profile") == "phone"
        and receipt.get("journey") == "armor-damage"
        and receipt.get("apiLevel") == 36
        and receipt.get("driverSha256") == _sha256_file(driver)
        and receipt.get("sharedDriverSha256") == _sha256_file(shared_driver)
        and receipt.get("careerFixtureSha256") == _sha256_file(career_fixture)
        and all(receipt.get(key) == _sha256_file(path) for key, path in source_digests.items())
        and isinstance(journeys, dict)
        and all(journeys.get(journey) == "pass" for journey in ARMOR_DAMAGE_E2E_JOURNEYS)
        and isinstance(controls, dict)
        and set(controls) == expected_controls
        and receipt.get("controlCount") == len(expected_controls)
        and all(
            isinstance(controls.get(control), dict)
            and all(
                controls[control].get(proof_key) == "pass"
                for proof_key in ARMOR_DAMAGE_CONTROL_E2E_PROOF_KEYS
            )
            for control in expected_controls
        )
        and re.fullmatch(r"[0-9a-f]{64}", apk_sha)
    ):
        return None
    return {
        "status": "executed_api36",
        "ref": ARMOR_DAMAGE_PHONE_E2E_RECEIPT.relative_to(REPO_ROOT).as_posix(),
        "receiptSha256": _sha256_file(ARMOR_DAMAGE_PHONE_E2E_RECEIPT),
        "apkSha256": apk_sha,
    }


def _validated_armor_equipment_phone_e2e_receipt(
    presentation_root: Path,
    core_engine_root: Path,
) -> dict[str, Any] | None:
    driver = REPO_ROOT / "tests" / "run_api36_armor_equipment_e2e.py"
    shared_driver = REPO_ROOT / "tests" / "run_api36_editing_e2e.py"
    creation_fixture = REPO_ROOT / "tests" / "fixtures" / "creation-armor-equipment-e2e.chum5"
    career_fixture = REPO_ROOT / "tests" / "fixtures" / "career-armor-equipment-e2e.chum5"
    overview = presentation_root / "Chummer.Presentation" / "Overview"
    contracts = core_engine_root / "Chummer.Contracts" / "Characters"
    source_digests = {
        "armorEquipmentPageSha256": REPO_ROOT / "src" / "Chummer.Android" / "Native" / "ArmorEquipmentPage.cs",
        "collectionEditorPagesSha256": REPO_ROOT / "src" / "Chummer.Android" / "Native" / "CollectionEditorPages.cs",
        "coordinatorSha256": REPO_ROOT / "src" / "Chummer.Android" / "Native" / "RunnerSessionCoordinator.cs",
        "armorEquipmentContractSha256": overview / "ArmorEquipmentEditRequest.cs",
        "collectionEditorStateSha256": overview / "WorkspaceCollectionEditorState.cs",
        "collectionEditorProjectorSha256": overview / "WorkspaceCollectionEditorProjector.cs",
        "mutationCatalogSha256": overview / "WorkspaceXmlMutationCatalog.cs",
        "presenterMutationSha256": overview / "CharacterOverviewPresenter.WorkspaceMutations.cs",
        "presenterInterfaceSha256": overview / "ICharacterOverviewPresenter.cs",
        "armorEquipmentRulesSha256": contracts / "CharacterArmorEquipmentRules.cs",
        "characterSectionModelsSha256": contracts / "CharacterSectionModels.cs",
        "characterSectionServiceSha256": core_engine_root / "Chummer.Infrastructure" / "Xml" / "CharacterSectionService.cs",
    }
    if not all(
        path.is_file()
        for path in (driver, shared_driver, creation_fixture, career_fixture, *source_digests.values())
    ):
        return None
    try:
        receipt = json.loads(_read_text(ARMOR_EQUIPMENT_PHONE_E2E_RECEIPT))
    except (FileNotFoundError, json.JSONDecodeError, OSError):
        return None
    journeys = receipt.get("journeys")
    controls = receipt.get("controls")
    apk_sha = str(receipt.get("apkSha256") or "")
    expected_controls = {
        f"{form}.{control}"
        for form in ("CharacterCreate", "CharacterCareer")
        for control in ARMOR_EQUIPMENT_CONTROLS
    }
    if not (
        receipt.get("schema") == "chummer.android.editing-e2e/v1"
        and receipt.get("status") == "pass"
        and receipt.get("profile") == "phone"
        and receipt.get("journey") == "armor-equipment"
        and receipt.get("apiLevel") == 36
        and receipt.get("driverSha256") == _sha256_file(driver)
        and receipt.get("sharedDriverSha256") == _sha256_file(shared_driver)
        and receipt.get("creationFixtureSha256") == _sha256_file(creation_fixture)
        and receipt.get("careerFixtureSha256") == _sha256_file(career_fixture)
        and all(receipt.get(key) == _sha256_file(path) for key, path in source_digests.items())
        and isinstance(journeys, dict)
        and all(journeys.get(journey) == "pass" for journey in ARMOR_EQUIPMENT_E2E_JOURNEYS)
        and isinstance(controls, dict)
        and set(controls) == expected_controls
        and receipt.get("controlCount") == len(expected_controls)
        and all(
            isinstance(controls.get(control), dict)
            and all(
                controls[control].get(proof_key) == "pass"
                for proof_key in ARMOR_EQUIPMENT_CONTROL_E2E_PROOF_KEYS
            )
            for control in expected_controls
        )
        and re.fullmatch(r"[0-9a-f]{64}", apk_sha)
    ):
        return None
    return {
        "status": "executed_api36",
        "ref": ARMOR_EQUIPMENT_PHONE_E2E_RECEIPT.relative_to(REPO_ROOT).as_posix(),
        "receiptSha256": _sha256_file(ARMOR_EQUIPMENT_PHONE_E2E_RECEIPT),
        "apkSha256": apk_sha,
    }


def _validated_weapon_accessory_included_phone_e2e_receipt(
    presentation_root: Path,
    core_engine_root: Path,
) -> dict[str, Any] | None:
    driver = REPO_ROOT / "tests" / "run_api36_weapon_accessory_included_e2e.py"
    shared_driver = REPO_ROOT / "tests" / "run_api36_editing_e2e.py"
    creation_fixture = REPO_ROOT / "tests" / "fixtures" / "creation-weapon-accessory-included-e2e.chum5"
    career_fixture = REPO_ROOT / "tests" / "fixtures" / "career-weapon-accessory-included-e2e.chum5"
    overview = presentation_root / "Chummer.Presentation" / "Overview"
    source_digests = {
        "weaponAccessoryIncludedPageSha256": REPO_ROOT / "src" / "Chummer.Android" / "Native" / "WeaponAccessoryIncludedPage.cs",
        "collectionEditorPagesSha256": REPO_ROOT / "src" / "Chummer.Android" / "Native" / "CollectionEditorPages.cs",
        "coordinatorSha256": REPO_ROOT / "src" / "Chummer.Android" / "Native" / "RunnerSessionCoordinator.cs",
        "weaponAccessoryIncludedContractSha256": overview / "WeaponAccessoryIncludedEditRequest.cs",
        "collectionEditorStateSha256": overview / "WorkspaceCollectionEditorState.cs",
        "collectionEditorProjectorSha256": overview / "WorkspaceCollectionEditorProjector.cs",
        "mutationCatalogSha256": overview / "WorkspaceXmlMutationCatalog.cs",
        "presenterMutationSha256": overview / "CharacterOverviewPresenter.WorkspaceMutations.cs",
        "presenterInterfaceSha256": overview / "ICharacterOverviewPresenter.cs",
        "characterSectionModelsSha256": core_engine_root / "Chummer.Contracts" / "Characters" / "CharacterSectionModels.cs",
        "characterSectionServiceSha256": core_engine_root / "Chummer.Infrastructure" / "Xml" / "CharacterSectionService.cs",
    }
    if not all(
        path.is_file()
        for path in (driver, shared_driver, creation_fixture, career_fixture, *source_digests.values())
    ):
        return None

    try:
        receipt = json.loads(_read_text(WEAPON_ACCESSORY_INCLUDED_PHONE_E2E_RECEIPT))
    except (FileNotFoundError, json.JSONDecodeError, OSError):
        return None
    journeys = receipt.get("journeys")
    controls = receipt.get("controls")
    apk_sha = str(receipt.get("apkSha256") or "")
    expected_controls = {
        f"{form}.{WEAPON_ACCESSORY_INCLUDED_CONTROL}"
        for form in ("CharacterCreate", "CharacterCareer")
    }
    if not (
        receipt.get("schema") == "chummer.android.editing-e2e/v1"
        and receipt.get("status") == "pass"
        and receipt.get("profile") == "phone"
        and receipt.get("journey") == "weapon-accessory-included"
        and receipt.get("apiLevel") == 36
        and receipt.get("driverSha256") == _sha256_file(driver)
        and receipt.get("sharedDriverSha256") == _sha256_file(shared_driver)
        and receipt.get("creationFixtureSha256") == _sha256_file(creation_fixture)
        and receipt.get("careerFixtureSha256") == _sha256_file(career_fixture)
        and all(receipt.get(key) == _sha256_file(path) for key, path in source_digests.items())
        and isinstance(journeys, dict)
        and all(journeys.get(journey) == "pass" for journey in WEAPON_ACCESSORY_INCLUDED_E2E_JOURNEYS)
        and isinstance(controls, dict)
        and set(controls) == expected_controls
        and receipt.get("controlCount") == len(expected_controls)
        and all(
            isinstance(controls.get(control), dict)
            and all(
                controls[control].get(proof_key) == "pass"
                for proof_key in WEAPON_ACCESSORY_INCLUDED_CONTROL_E2E_PROOF_KEYS
            )
            for control in expected_controls
        )
        and re.fullmatch(r"[0-9a-f]{64}", apk_sha)
    ):
        return None
    return {
        "status": "executed_api36",
        "ref": WEAPON_ACCESSORY_INCLUDED_PHONE_E2E_RECEIPT.relative_to(REPO_ROOT).as_posix(),
        "receiptSha256": _sha256_file(WEAPON_ACCESSORY_INCLUDED_PHONE_E2E_RECEIPT),
        "apkSha256": apk_sha,
    }


def _validated_critter_power_count_phone_e2e_receipt(
    presentation_root: Path,
    core_engine_root: Path,
) -> dict[str, Any] | None:
    driver = REPO_ROOT / "tests" / "run_api36_critter_power_count_e2e.py"
    shared_driver = REPO_ROOT / "tests" / "run_api36_editing_e2e.py"
    creation_fixture = REPO_ROOT / "tests" / "fixtures" / "creation-critter-power-count-e2e.chum5"
    career_fixture = REPO_ROOT / "tests" / "fixtures" / "career-critter-power-count-e2e.chum5"
    overview = presentation_root / "Chummer.Presentation" / "Overview"
    source_digests = {
        "critterPowerCountPageSha256": REPO_ROOT / "src" / "Chummer.Android" / "Native" / "CritterPowerCountPage.cs",
        "collectionEditorPagesSha256": REPO_ROOT / "src" / "Chummer.Android" / "Native" / "CollectionEditorPages.cs",
        "coordinatorSha256": REPO_ROOT / "src" / "Chummer.Android" / "Native" / "RunnerSessionCoordinator.cs",
        "critterPowerCountContractSha256": overview / "CritterPowerCountEditRequest.cs",
        "collectionEditorStateSha256": overview / "WorkspaceCollectionEditorState.cs",
        "collectionEditorProjectorSha256": overview / "WorkspaceCollectionEditorProjector.cs",
        "mutationCatalogSha256": overview / "WorkspaceXmlMutationCatalog.cs",
        "presenterMutationSha256": overview / "CharacterOverviewPresenter.WorkspaceMutations.cs",
        "presenterPersistenceSha256": overview / "CharacterOverviewPresenter.Persistence.cs",
        "presenterInterfaceSha256": overview / "ICharacterOverviewPresenter.cs",
        "critterPowerCountRulesSha256": core_engine_root / "Chummer.Contracts" / "Characters" / "CharacterCritterPowerCountRules.cs",
        "characterSectionModelsSha256": core_engine_root / "Chummer.Contracts" / "Characters" / "CharacterSectionModels.cs",
        "characterSectionServiceSha256": core_engine_root / "Chummer.Infrastructure" / "Xml" / "CharacterSectionService.cs",
        "workspaceStoreSha256": core_engine_root / "Chummer.Infrastructure" / "Workspaces" / "FileWorkspaceStore.cs",
    }
    if not all(
        path.is_file()
        for path in (driver, shared_driver, creation_fixture, career_fixture, *source_digests.values())
    ):
        return None

    try:
        receipt = json.loads(_read_text(CRITTER_POWER_COUNT_PHONE_E2E_RECEIPT))
    except (FileNotFoundError, json.JSONDecodeError, OSError):
        return None
    journeys = receipt.get("journeys")
    controls = receipt.get("controls")
    apk_sha = str(receipt.get("apkSha256") or "")
    expected_controls = {
        f"{form}.{CRITTER_POWER_COUNT_CONTROL}"
        for form in ("CharacterCreate", "CharacterCareer")
    }
    if not (
        receipt.get("schema") == "chummer.android.editing-e2e/v1"
        and receipt.get("status") == "pass"
        and receipt.get("profile") == "phone"
        and receipt.get("journey") == "critter-power-count"
        and receipt.get("apiLevel") == 36
        and receipt.get("driverSha256") == _sha256_file(driver)
        and receipt.get("sharedDriverSha256") == _sha256_file(shared_driver)
        and receipt.get("creationFixtureSha256") == _sha256_file(creation_fixture)
        and receipt.get("careerFixtureSha256") == _sha256_file(career_fixture)
        and all(receipt.get(key) == _sha256_file(path) for key, path in source_digests.items())
        and isinstance(journeys, dict)
        and all(journeys.get(journey) == "pass" for journey in CRITTER_POWER_COUNT_E2E_JOURNEYS)
        and isinstance(controls, dict)
        and set(controls) == expected_controls
        and receipt.get("controlCount") == len(expected_controls)
        and all(
            isinstance(controls.get(control), dict)
            and all(
                controls[control].get(proof_key) == "pass"
                for proof_key in CRITTER_POWER_COUNT_CONTROL_E2E_PROOF_KEYS
            )
            for control in expected_controls
        )
        and re.fullmatch(r"[0-9a-f]{64}", apk_sha)
    ):
        return None
    return {
        "status": "executed_api36",
        "ref": CRITTER_POWER_COUNT_PHONE_E2E_RECEIPT.relative_to(REPO_ROOT).as_posix(),
        "receiptSha256": _sha256_file(CRITTER_POWER_COUNT_PHONE_E2E_RECEIPT),
        "apkSha256": apk_sha,
    }


def _validated_sustained_effects_phone_e2e_receipt(
    presentation_root: Path,
    core_engine_root: Path,
) -> dict[str, Any] | None:
    driver = REPO_ROOT / "tests" / "run_api36_sustained_effects_e2e.py"
    shared_driver = REPO_ROOT / "tests" / "run_api36_editing_e2e.py"
    creation_fixture = REPO_ROOT / "tests" / "fixtures" / "creation-sustained-effects-e2e.chum5"
    career_fixture = REPO_ROOT / "tests" / "fixtures" / "career-sustained-effects-e2e.chum5"
    overview = presentation_root / "Chummer.Presentation" / "Overview"
    source_digests = {
        "buildPageSha256": REPO_ROOT / "src" / "Chummer.Android" / "Native" / "BuildPage.cs",
        "sustainedEffectsPageSha256": REPO_ROOT / "src" / "Chummer.Android" / "Native" / "SustainedObjectsPage.cs",
        "coordinatorSha256": REPO_ROOT / "src" / "Chummer.Android" / "Native" / "RunnerSessionCoordinator.cs",
        "sustainedEffectsContractSha256": overview / "SustainedObjectEditRequest.cs",
        "mutationCatalogSha256": overview / "WorkspaceXmlMutationCatalog.cs",
        "presenterMutationSha256": overview / "CharacterOverviewPresenter.WorkspaceMutations.cs",
        "presenterPersistenceSha256": overview / "CharacterOverviewPresenter.Persistence.cs",
        "presenterInterfaceSha256": overview / "ICharacterOverviewPresenter.cs",
        "sustainedEffectsRulesSha256": core_engine_root / "Chummer.Contracts" / "Characters" / "CharacterSustainedObjectRules.cs",
        "workspaceStoreSha256": core_engine_root / "Chummer.Infrastructure" / "Workspaces" / "FileWorkspaceStore.cs",
    }
    if not all(
        path.is_file()
        for path in (driver, shared_driver, creation_fixture, career_fixture, *source_digests.values())
    ):
        return None

    try:
        receipt = json.loads(_read_text(SUSTAINED_EFFECTS_PHONE_E2E_RECEIPT))
    except (FileNotFoundError, json.JSONDecodeError, OSError):
        return None
    journeys = receipt.get("journeys")
    controls = receipt.get("controls")
    apk_sha = str(receipt.get("apkSha256") or "")
    expected_controls = {
        f"SustainedObjectControl.{control}"
        for control in SUSTAINED_EFFECTS_CONTROLS
    }
    if not (
        receipt.get("schema") == "chummer.android.editing-e2e/v1"
        and receipt.get("status") == "pass"
        and receipt.get("profile") == "phone"
        and receipt.get("journey") == "sustained-effects"
        and receipt.get("apiLevel") == 36
        and receipt.get("driverSha256") == _sha256_file(driver)
        and receipt.get("sharedDriverSha256") == _sha256_file(shared_driver)
        and receipt.get("creationFixtureSha256") == _sha256_file(creation_fixture)
        and receipt.get("careerFixtureSha256") == _sha256_file(career_fixture)
        and all(receipt.get(key) == _sha256_file(path) for key, path in source_digests.items())
        and isinstance(journeys, dict)
        and all(journeys.get(journey) == "pass" for journey in SUSTAINED_EFFECTS_E2E_JOURNEYS)
        and isinstance(controls, dict)
        and set(controls) == expected_controls
        and receipt.get("controlCount") == len(expected_controls)
        and all(
            isinstance(controls.get(control), dict)
            and all(
                controls[control].get(proof_key) == "pass"
                for proof_key in SUSTAINED_EFFECTS_CONTROL_E2E_PROOF_KEYS
            )
            for control in expected_controls
        )
        and re.fullmatch(r"[0-9a-f]{64}", apk_sha)
    ):
        return None
    return {
        "status": "executed_api36",
        "ref": SUSTAINED_EFFECTS_PHONE_E2E_RECEIPT.relative_to(REPO_ROOT).as_posix(),
        "receiptSha256": _sha256_file(SUSTAINED_EFFECTS_PHONE_E2E_RECEIPT),
        "apkSha256": apk_sha,
    }


def _validated_location_rename_phone_e2e_receipt(
    presentation_root: Path,
) -> dict[str, Any] | None:
    driver = REPO_ROOT / "tests" / "run_api36_location_rename_e2e.py"
    shared_driver = REPO_ROOT / "tests" / "run_api36_editing_e2e.py"
    creation_fixture = REPO_ROOT / "tests" / "fixtures" / "creation-location-rename-e2e.chum5"
    career_fixture = REPO_ROOT / "tests" / "fixtures" / "career-location-rename-e2e.chum5"
    overview = presentation_root / "Chummer.Presentation" / "Overview"
    source_digests = {
        "locationRenamePageSha256": REPO_ROOT / "src" / "Chummer.Android" / "Native" / "LocationRenamePage.cs",
        "buildFlowPagesSha256": REPO_ROOT / "src" / "Chummer.Android" / "Native" / "BuildFlowPages.cs",
        "coordinatorSha256": REPO_ROOT / "src" / "Chummer.Android" / "Native" / "RunnerSessionCoordinator.cs",
        "locationStateSha256": overview / "WorkspaceLocationEditorState.cs",
        "locationRenameContractSha256": overview / "LocationRenameRequest.cs",
        "sectionRendererSha256": overview / "WorkspaceSectionRenderer.cs",
        "overviewStateSha256": overview / "CharacterOverviewState.cs",
        "mutationCatalogSha256": overview / "WorkspaceXmlMutationCatalog.cs",
        "presenterMutationSha256": overview / "CharacterOverviewPresenter.WorkspaceMutations.cs",
        "presenterInterfaceSha256": overview / "ICharacterOverviewPresenter.cs",
    }
    if not all(
        path.is_file()
        for path in (
            driver,
            shared_driver,
            creation_fixture,
            career_fixture,
            *source_digests.values(),
        )
    ):
        return None

    try:
        receipt = json.loads(_read_text(LOCATION_RENAME_PHONE_E2E_RECEIPT))
    except (FileNotFoundError, json.JSONDecodeError, OSError):
        return None
    journeys = receipt.get("journeys")
    controls = receipt.get("controls")
    apk_sha = str(receipt.get("apkSha256") or "")
    expected_controls = {
        f"{form}.{control}"
        for form in ("CharacterCreate", "CharacterCareer")
        for control in LOCATION_RENAME_CONTROLS
    }
    if not (
        receipt.get("schema") == "chummer.android.editing-e2e/v1"
        and receipt.get("status") == "pass"
        and receipt.get("profile") == "phone"
        and receipt.get("journey") == "location-rename"
        and receipt.get("apiLevel") == 36
        and receipt.get("driverSha256") == _sha256_file(driver)
        and receipt.get("sharedDriverSha256") == _sha256_file(shared_driver)
        and receipt.get("creationFixtureSha256") == _sha256_file(creation_fixture)
        and receipt.get("careerFixtureSha256") == _sha256_file(career_fixture)
        and all(receipt.get(key) == _sha256_file(path) for key, path in source_digests.items())
        and isinstance(journeys, dict)
        and all(journeys.get(journey) == "pass" for journey in LOCATION_RENAME_E2E_JOURNEYS)
        and isinstance(controls, dict)
        and set(controls) == expected_controls
        and receipt.get("controlCount") == len(expected_controls)
        and all(
            isinstance(controls.get(control), dict)
            and all(
                controls[control].get(proof_key) == "pass"
                for proof_key in LOCATION_RENAME_CONTROL_E2E_PROOF_KEYS
            )
            for control in expected_controls
        )
        and re.fullmatch(r"[0-9a-f]{64}", apk_sha)
    ):
        return None
    return {
        "status": "executed_api36",
        "ref": LOCATION_RENAME_PHONE_E2E_RECEIPT.relative_to(REPO_ROOT).as_posix(),
        "receiptSha256": _sha256_file(LOCATION_RENAME_PHONE_E2E_RECEIPT),
        "apkSha256": apk_sha,
    }


def _validated_explicit_save_phone_e2e_receipt(
    presentation_root: Path,
) -> dict[str, Any] | None:
    driver = REPO_ROOT / "tests" / "run_api36_explicit_save_e2e.py"
    shared_driver = REPO_ROOT / "tests" / "run_api36_editing_e2e.py"
    creation_fixture = REPO_ROOT / "tests" / "fixtures" / "creation-explicit-save-e2e.chum5"
    career_fixture = REPO_ROOT / "tests" / "fixtures" / "career-explicit-save-e2e.chum5"
    source_digests = {
        "buildPageSha256": REPO_ROOT / "src" / "Chummer.Android" / "Native" / "BuildPage.cs",
        "morePageSha256": REPO_ROOT / "src" / "Chummer.Android" / "Native" / "MorePage.cs",
        "coordinatorSha256": REPO_ROOT / "src" / "Chummer.Android" / "Native" / "RunnerSessionCoordinator.cs",
        "presenterInterfaceSha256": presentation_root / "Chummer.Presentation" / "Overview" / "ICharacterOverviewPresenter.cs",
        "presenterPersistenceSha256": presentation_root / "Chummer.Presentation" / "Overview" / "CharacterOverviewPresenter.Persistence.cs",
    }
    if not all(
        path.is_file()
        for path in (driver, shared_driver, creation_fixture, career_fixture, *source_digests.values())
    ):
        return None

    try:
        receipt = json.loads(_read_text(EXPLICIT_SAVE_PHONE_E2E_RECEIPT))
    except (FileNotFoundError, json.JSONDecodeError, OSError):
        return None
    journeys = receipt.get("journeys")
    controls = receipt.get("controls")
    apk_sha = str(receipt.get("apkSha256") or "")
    expected_controls = {
        f"{form}.{control}" for form, control in EXPLICIT_SAVE_CONTROLS
    }
    if not (
        receipt.get("schema") == "chummer.android.editing-e2e/v1"
        and receipt.get("status") == "pass"
        and receipt.get("profile") == "phone"
        and receipt.get("journey") == "explicit-save"
        and receipt.get("apiLevel") == 36
        and receipt.get("driverSha256") == _sha256_file(driver)
        and receipt.get("sharedDriverSha256") == _sha256_file(shared_driver)
        and receipt.get("creationFixtureSha256") == _sha256_file(creation_fixture)
        and receipt.get("careerFixtureSha256") == _sha256_file(career_fixture)
        and all(receipt.get(key) == _sha256_file(path) for key, path in source_digests.items())
        and isinstance(journeys, dict)
        and all(journeys.get(journey) == "pass" for journey in EXPLICIT_SAVE_E2E_JOURNEYS)
        and isinstance(controls, dict)
        and set(controls) == expected_controls
        and receipt.get("controlCount") == len(expected_controls)
        and all(
            isinstance(controls.get(control), dict)
            and all(
                controls[control].get(proof_key) == "pass"
                for proof_key in EXPLICIT_SAVE_CONTROL_E2E_PROOF_KEYS
            )
            for control in expected_controls
        )
        and re.fullmatch(r"[0-9a-f]{64}", apk_sha)
    ):
        return None
    return {
        "status": "executed_api36",
        "ref": EXPLICIT_SAVE_PHONE_E2E_RECEIPT.relative_to(REPO_ROOT).as_posix(),
        "receiptSha256": _sha256_file(EXPLICIT_SAVE_PHONE_E2E_RECEIPT),
        "apkSha256": apk_sha,
    }


def _validated_nested_collection_notes_phone_e2e_receipt(
    presentation_root: Path,
    core_root: Path,
) -> dict[str, Any] | None:
    driver = REPO_ROOT / "tests" / "run_api36_nested_collection_notes_e2e.py"
    shared_driver = REPO_ROOT / "tests" / "run_api36_editing_e2e.py"
    creation_fixture = REPO_ROOT / "tests" / "fixtures" / "creation-nested-notes-e2e.chum5"
    career_fixture = REPO_ROOT / "tests" / "fixtures" / "career-nested-notes-e2e.chum5"
    source_digests = {
        "collectionPageSha256": REPO_ROOT / "src" / "Chummer.Android" / "Native" / "CollectionEditorPages.cs",
        "coordinatorSha256": REPO_ROOT / "src" / "Chummer.Android" / "Native" / "RunnerSessionCoordinator.cs",
        "sectionServiceSha256": core_root / "Chummer.Infrastructure" / "Xml" / "CharacterSectionService.cs",
        "shellCatalogSha256": core_root / "Chummer.Rulesets.Hosting" / "Presentation" / "WorkspaceSurfaceActionCatalog.cs",
        "projectorSha256": presentation_root / "Chummer.Presentation" / "Overview" / "WorkspaceCollectionEditorProjector.cs",
        "collectionMutationRequestSha256": presentation_root / "Chummer.Presentation" / "Overview" / "WorkspaceCollectionMutationRequest.cs",
        "mutationCatalogSha256": presentation_root / "Chummer.Presentation" / "Overview" / "WorkspaceXmlMutationCatalog.cs",
        "presenterMutationSha256": presentation_root / "Chummer.Presentation" / "Overview" / "CharacterOverviewPresenter.WorkspaceMutations.cs",
    }
    if not all(
        path.is_file()
        for path in (driver, shared_driver, creation_fixture, career_fixture, *source_digests.values())
    ):
        return None

    try:
        receipt = json.loads(_read_text(NESTED_COLLECTION_NOTES_PHONE_E2E_RECEIPT))
    except (FileNotFoundError, json.JSONDecodeError, OSError):
        return None
    journeys = receipt.get("journeys")
    controls = receipt.get("controls")
    apk_sha = str(receipt.get("apkSha256") or "")
    expected_controls = {
        "CharacterCreate.tsWeaponAccessoryNotes",
        "CharacterCreate.tsArmorModNotes",
        "CharacterCareer.tsWeaponAccessoryNotes",
        "CharacterCareer.tsArmorModNotes",
        "CharacterCareer.tsGearPluginNotes",
    }
    if not (
        receipt.get("schema") == "chummer.android.editing-e2e/v1"
        and receipt.get("status") == "pass"
        and receipt.get("profile") == "phone"
        and receipt.get("journey") == "nested-collection-notes"
        and receipt.get("apiLevel") == 36
        and receipt.get("driverSha256") == _sha256_file(driver)
        and receipt.get("sharedDriverSha256") == _sha256_file(shared_driver)
        and receipt.get("creationFixtureSha256") == _sha256_file(creation_fixture)
        and receipt.get("careerFixtureSha256") == _sha256_file(career_fixture)
        and all(receipt.get(key) == _sha256_file(path) for key, path in source_digests.items())
        and isinstance(journeys, dict)
        and all(journeys.get(journey) == "pass" for journey in NESTED_COLLECTION_NOTES_E2E_JOURNEYS)
        and isinstance(controls, dict)
        and set(controls) == expected_controls
        and receipt.get("controlCount") == len(expected_controls)
        and all(
            isinstance(controls.get(control), dict)
            and all(
                controls[control].get(proof_key) == "pass"
                for proof_key in NESTED_COLLECTION_NOTES_CONTROL_E2E_PROOF_KEYS
            )
            for control in expected_controls
        )
        and re.fullmatch(r"[0-9a-f]{64}", apk_sha)
    ):
        return None
    return {
        "status": "executed_api36",
        "ref": NESTED_COLLECTION_NOTES_PHONE_E2E_RECEIPT.relative_to(REPO_ROOT).as_posix(),
        "receiptSha256": _sha256_file(NESTED_COLLECTION_NOTES_PHONE_E2E_RECEIPT),
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
    if class_name in {"CharacterCreate", "CharacterCareer"} and control == "rtfNotes":
        return "notes"
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
    externally_referenced_legacy_forms = {
        form_name
        for form_name in UNREACHABLE_LEGACY_FORMS
        if any(
            identity[1] != form_name
            and re.search(rf"\b{re.escape(form_name)}\b", text) is not None
            for _, text, identity in units
        )
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
            reviewed_non_mutating = NON_MUTATING_LEGACY_INTERACTIONS.get((class_name, control))
            if reviewed_non_mutating is not None:
                operation, inert_evidence = reviewed_non_mutating
                confidence = "non_mutating"
            elif inert_evidence is not None:
                operation = "unreachable_designer_field"
                confidence = "non_mutating"
            elif (
                class_name in UNREACHABLE_LEGACY_FORMS
                and class_name not in externally_referenced_legacy_forms
            ):
                inert_evidence = UNREACHABLE_LEGACY_FORMS[class_name]
                operation = "unreachable_legacy_form"
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
    chummer5_root: Path,
    presentation_root: Path,
    character_notes_core_root: Path,
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
    character_notes_phone_e2e_receipt: dict[str, Any] | None,
    career_reputation_phone_e2e_receipt: dict[str, Any] | None,
    situational_modifiers_phone_e2e_receipt: dict[str, Any] | None,
    primary_arm_phone_e2e_receipt: dict[str, Any] | None,
    gear_location_phone_e2e_receipt: dict[str, Any] | None,
    weapon_location_phone_e2e_receipt: dict[str, Any] | None,
    vehicle_location_phone_e2e_receipt: dict[str, Any] | None,
    vehicle_home_node_phone_e2e_receipt: dict[str, Any] | None,
    armor_home_node_phone_e2e_receipt: dict[str, Any] | None,
    weapon_home_node_phone_e2e_receipt: dict[str, Any] | None,
    weapon_active_commlink_phone_e2e_receipt: dict[str, Any] | None,
    armor_active_commlink_phone_e2e_receipt: dict[str, Any] | None,
    armor_damage_phone_e2e_receipt: dict[str, Any] | None,
    armor_equipment_phone_e2e_receipt: dict[str, Any] | None,
    weapon_accessory_included_phone_e2e_receipt: dict[str, Any] | None,
    critter_power_count_phone_e2e_receipt: dict[str, Any] | None,
    location_rename_phone_e2e_receipt: dict[str, Any] | None,
    explicit_save_phone_e2e_receipt: dict[str, Any] | None,
    nested_collection_notes_phone_e2e_receipt: dict[str, Any] | None,
) -> dict[str, Any] | None:
    legacy = row["legacy"]
    class_name = legacy["formOrControl"]
    control = legacy["controlName"]
    save_mapping = EXPLICIT_SAVE_CONTROLS.get((class_name, control))
    if save_mapping is not None:
        route, surface, automation_id = save_mapping
        native_root = REPO_ROOT / "src" / "Chummer.Android" / "Native"
        build_page = native_root / "BuildPage.cs"
        more_page = native_root / "MorePage.cs"
        coordinator = native_root / "RunnerSessionCoordinator.cs"
        overview = presentation_root / "Chummer.Presentation" / "Overview"
        presenter_interface = overview / "ICharacterOverviewPresenter.cs"
        presenter_persistence = overview / "CharacterOverviewPresenter.Persistence.cs"
        e2e_driver = REPO_ROOT / "tests" / "run_api36_explicit_save_e2e.py"
        implemented = (
            _contains(
                build_page,
                'AutomationId = "build-save-runner"',
                "Coordinator.SaveAsync",
            )
            and _contains(
                more_page,
                'save.AutomationId = "more-save-runner"',
                "Coordinator.SaveAsync",
            )
            and _contains(
                coordinator,
                "public async Task SaveAsync",
                "_presenter.SaveAsync",
                '_notice = State.Error is null ? "Saved." : null',
            )
            and _contains(presenter_interface, "Task SaveAsync(CancellationToken ct)")
            and _contains(
                presenter_persistence,
                "public async Task SaveAsync",
                "expectedContentRevision",
                "SavedRevision",
                "TryCaptureRecoveryPayloadAsync",
            )
        )
        e2e_scripted = _contains(
            e2e_driver,
            '"journey": "explicit-save"',
            '"creationBuildToolbarSaveInvoked": "pass"',
            '"creationMorePageSaveInvoked": "pass"',
            '"creationWorkspaceRevisionSaved": "pass"',
            '"creationProcessRestartReadback": "pass"',
            '"careerBuildToolbarSaveInvoked": "pass"',
            '"careerMorePageSaveInvoked": "pass"',
            '"careerWorkspaceRevisionSaved": "pass"',
            '"careerProcessRestartReadback": "pass"',
            '"selectedRunnerSaveEquivalent": "pass"',
            '"controls": controls',
        )
        phone_e2e = explicit_save_phone_e2e_receipt if implemented and e2e_scripted else None
        return {
            "status": "implemented_verified_api36" if phone_e2e else "implemented_pending_emulator" if implemented else "missing",
            "route": route,
            "surface": surface,
            "automationId": automation_id,
            "sourceRefs": [
                "src/Chummer.Android/Native/BuildPage.cs",
                "src/Chummer.Android/Native/MorePage.cs",
                "src/Chummer.Android/Native/RunnerSessionCoordinator.cs",
                "chummer-presentation/Chummer.Presentation/Overview/ICharacterOverviewPresenter.cs",
                "chummer-presentation/Chummer.Presentation/Overview/CharacterOverviewPresenter.Persistence.cs",
            ],
            "presenterMutation": "ICharacterOverviewPresenter.SaveAsync",
            "persistenceAssertion": (
                "workspace SavedRevision equals ContentRevision after explicit save and the exact "
                "runner payload survives process restart"
            ),
            "e2e": phone_e2e or {
                "status": "scripted_not_executed" if e2e_scripted else "missing",
                "ref": "tests/run_api36_explicit_save_e2e.py" if e2e_scripted else None,
            },
        }
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
                'NewCharacterMetatypeSearchFieldId = "newCharacterMetatypeSearch"',
                '"newCharacterMetatypeCategory"',
                '"newCharacterMetatype"',
                "NewCharacterMetavariantFieldId",
                "NewCharacterForceFieldId",
                "NewCharacterPossessionBasedFieldId",
                "NewCharacterPossessionMethodFieldId",
                '"complete_new_character_workflow"',
                "BuildNewCharacterKarmaWorkflowDialog",
                "ResolveKarmaWorkflowResolution",
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
                "ApplyPrioritySpiritSelection",
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
    if class_name == "SpiritControl" and control in SPIRIT_LINKED_RUNNER_CONTROLS:
        phone_page = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "CollectionEditorPages.cs"
        coordinator = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "RunnerSessionCoordinator.cs"
        staging = REPO_ROOT / "src" / "Chummer.Android" / "Platform" / "IAndroidLinkedCharacterFileService.cs"
        state = presentation_root / "Chummer.Presentation" / "Overview" / "WorkspaceCollectionEditorState.cs"
        request = presentation_root / "Chummer.Presentation" / "Overview" / "WorkspaceCollectionMutationRequest.cs"
        mutation = presentation_root / "Chummer.Presentation" / "Overview" / "WorkspaceXmlMutationCatalog.cs"
        projector = presentation_root / "Chummer.Presentation" / "Overview" / "WorkspaceCollectionEditorProjector.cs"
        presenter = presentation_root / "Chummer.Presentation" / "Overview" / "CharacterOverviewPresenter.WorkspaceMutations.cs"
        core_models = WORKSPACE_ROOT / "chummer-core-engine" / "Chummer.Contracts" / "Characters" / "CharacterSectionModels.cs"
        core_parser = WORKSPACE_ROOT / "chummer-core-engine" / "Chummer.Infrastructure" / "Xml" / "CharacterSectionService.cs"
        action = SPIRIT_LINKED_RUNNER_CONTROLS[control]
        shared = (
            _contains(staging, "ICharacterLinkedDocumentCodec", 'DirectoryName = "linked-characters"', "File.Move")
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
                "WorkspaceCollectionKind.Spirit",
            )
            and _contains(
                projector,
                "WorkspaceCollectionKind.Spirit",
                "ProjectLinkedCharacter(item)",
            )
            and _contains(core_models, "CharacterLinkedAssociationSummary? LinkedCharacter")
            and _contains(
                core_parser,
                'ReadValue(spirit, "file")',
                "CharacterLinkedAssociationSummary(",
            )
            and _contains(presenter, "ApplyCollectionMutationAsync", "ApplyWorkspaceXmlMutationAsync")
            and _contains(
                coordinator,
                "AttachLinkedCharacterAsync",
                "RemoveLinkedCharacterAsync",
                "_linkedCharacters.DeleteOwnedAsync",
            )
        )
        attach_available = _contains(
            phone_page,
            "collection-linked-attach-",
            "Coordinator.AttachLinkedCharacterAsync",
        )
        remove_available = _contains(
            phone_page,
            "collection-linked-remove-",
            "Coordinator.RemoveLinkedCharacterAsync",
        )
        phone_implemented = shared and (
            attach_available
            if action == "attach"
            else remove_available
            if action == "remove"
            else attach_available and remove_available
        )
        automation_id = {
            "attach": "collection-linked-attach-{stable-target}",
            "remove": "collection-linked-remove-{stable-target}",
            "manage": "collection-linked-attach-{stable-target}|collection-linked-remove-{stable-target}",
        }[action]
        operation = {
            "attach": "WorkspaceSetLinkedCharacterRequest",
            "remove": "WorkspaceRemoveLinkedCharacterRequest",
            "manage": "WorkspaceSetLinkedCharacterRequest / WorkspaceRemoveLinkedCharacterRequest",
        }[action]
        return {
            "status": "implemented_pending_emulator" if phone_implemented else "missing",
            "route": "Build > Magic and Resonance > Spirits and sprites > selected spirit or sprite > Linked runner",
            "surface": "CollectionItemEditorPage",
            "automationId": automation_id,
            "sourceRefs": [
                "src/Chummer.Android/Native/CollectionEditorPages.cs",
                "src/Chummer.Android/Native/RunnerSessionCoordinator.cs",
                "src/Chummer.Android/Platform/IAndroidLinkedCharacterFileService.cs",
                "chummer-core-engine/Chummer.Contracts/Characters/CharacterSectionModels.cs",
                "chummer-core-engine/Chummer.Infrastructure/Xml/CharacterSectionService.cs",
                "chummer-presentation/Chummer.Presentation/Overview/WorkspaceCollectionEditorProjector.cs",
                "chummer-presentation/Chummer.Presentation/Overview/WorkspaceCollectionEditorState.cs",
                "chummer-presentation/Chummer.Presentation/Overview/WorkspaceCollectionMutationRequest.cs",
                "chummer-presentation/Chummer.Presentation/Overview/WorkspaceXmlMutationCatalog.cs",
            ],
            "presenterMutation": (
                "ICharacterOverviewPresenter.ApplyCollectionMutationAsync / "
                f"{operation} on WorkspaceCollectionKind.Spirit"
            ),
            "persistenceAssertion": (
                "selected stable Spirit or Sprite guid retains governed file/relative link state and "
                "linked identity after reopen and process restart, or restores its saved spirit identity after unlink"
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
    if class_name == "CharacterCreate" and control in LEGACY_CREATION_COLLECTION_NUMERIC_CONTROLS:
        kind, section_label, numeric_kind, xml_element = LEGACY_CREATION_COLLECTION_NUMERIC_CONTROLS[control]
        expected_handler = f"{control}_ValueChanged"
        if not any(event.get("handler") == expected_handler for event in legacy.get("events", [])):
            return None

        phone_page = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "CollectionEditorPages.cs"
        phone_route = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "BuildFlowPages.cs"
        coordinator = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "RunnerSessionCoordinator.cs"
        request = presentation_root / "Chummer.Presentation" / "Overview" / "WorkspaceCollectionMutationRequest.cs"
        mutation = presentation_root / "Chummer.Presentation" / "Overview" / "WorkspaceXmlMutationCatalog.cs"
        projector = presentation_root / "Chummer.Presentation" / "Overview" / "WorkspaceCollectionEditorProjector.cs"
        presenter = presentation_root / "Chummer.Presentation" / "Overview" / "CharacterOverviewPresenter.WorkspaceMutations.cs"
        request_type = f"WorkspaceSetCollection{numeric_kind}Request"
        support_method = f"Supports{numeric_kind}"
        mutation_method = f"Apply{numeric_kind}Mutation"
        shared = (
            _contains(request, "WorkspaceCollectionItemTarget", request_type, "WorkspacePatchCollectionItemRequest")
            and _contains(projector, support_method, f"WorkspaceCollectionKind.{kind}")
            and _contains(mutation, mutation_method, f"WorkspaceCollectionKind.{kind}")
            and _contains(coordinator, "ApplyCollectionMutationAsync")
            and _contains(presenter, "ApplyCollectionMutationAsync", "ApplyWorkspaceXmlMutationAsync")
        )
        automation_token = numeric_kind.lower()
        validation_markers = (
            "rating.Minimum",
            "rating.Maximum",
        ) if numeric_kind == "Rating" else (
            "quantity.MinimumExclusive",
            "quantity.Maximum",
        )
        phone_implemented = shared and _contains(
            phone_route,
            "AddCollectionRows",
            "CollectionItemEditorPage",
        ) and _contains(
            phone_page,
            f'"collection-{automation_token}-',
            "WorkspacePatchCollectionItemRequest",
            *validation_markers,
        )
        return {
            "status": "implemented_pending_emulator" if phone_implemented else "missing",
            "route": f"Build > {section_label} > selected item > {numeric_kind}",
            "surface": "CollectionItemEditorPage",
            "automationId": f"collection-{automation_token}-{{stable-target}}",
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
                f"{request_type} on WorkspaceCollectionKind.{kind}"
            ),
            "persistenceAssertion": (
                f"selected stable {kind} guid retains {xml_element} after save, reopen, and process restart"
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
    if class_name == "CharacterCareer" and control in CAREER_MANUAL_KARMA_CONTROLS:
        handler, action, automation_id = CAREER_MANUAL_KARMA_CONTROLS[control]
        page = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "CareerManualKarmaPage.cs"
        build_page = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "BuildPage.cs"
        coordinator = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "RunnerSessionCoordinator.cs"
        driver = REPO_ROOT / "tests" / "run_api36_career_manual_karma_e2e.py"
        fixture = REPO_ROOT / "tests" / "fixtures" / "career-manual-karma-e2e.chum5"
        overview = presentation_root / "Chummer.Presentation" / "Overview"
        request = overview / "CareerManualKarmaEditRequest.cs"
        mutation = overview / "WorkspaceXmlMutationCatalog.cs"
        presenter = overview / "CharacterOverviewPresenter.WorkspaceMutations.cs"
        presenter_interface = overview / "ICharacterOverviewPresenter.cs"
        presenter_persistence = overview / "CharacterOverviewPresenter.Persistence.cs"
        core_rules = character_notes_core_root / "Chummer.Contracts" / "Characters" / "CharacterCareerManualKarmaRules.cs"
        source_resolver_contract = character_notes_core_root / "Chummer.Application" / "Characters" / "ICharacterSourceDataResolver.cs"
        source_resolver = character_notes_core_root / "Chummer.Infrastructure" / "Xml" / "FileSystemCharacterSourceDataResolver.cs"
        workspace_store = character_notes_core_root / "Chummer.Infrastructure" / "Workspaces" / "FileWorkspaceStore.cs"
        legacy_handler_exact = any(
            event.get("handler") == handler
            for event in legacy.get("events", [])
            if isinstance(event, dict)
        )
        action_marker = (
            "CharacterCareerManualKarmaAction.Gain"
            if action == "gain"
            else "CharacterCareerManualKarmaAction.Spend"
        )
        implemented = (
            legacy_handler_exact
            and _contains(
                page,
                "class CareerManualKarmaPage",
                'AutomationId = "career-manual-karma-page"',
                f'AutomationId = "{automation_id}"',
                'AutomationId = "career-manual-karma-amount"',
                '"career-manual-karma-reason"',
                '"career-manual-karma-exchange"',
                '"career-manual-karma-force-career-visible"',
                action_marker,
                "_editor.ContentRevision",
            )
            and _contains(build_page, '"build-career-manual-karma"', "new CareerManualKarmaPage")
            and _contains(
                coordinator,
                "PrepareCareerManualKarmaEditAsync",
                "ApplyCareerManualKarmaEditAsync",
                "ExpectedContentRevision",
                "_presenter.SaveAsync",
            )
            and _contains(
                request,
                "CareerManualKarmaEditorState",
                "CareerManualKarmaEditRequest",
                "CharacterCareerManualKarmaState ExpectedState",
                'ReadRequiredBool(root, "created")',
                'ReadOptionalInt(root, "karma")',
                'ReadOptionalDecimal(root, "nuyen")',
                "TryResolveKarmaNuyenExchangeRates",
            )
            and _contains(
                mutation,
                "ApplyCareerManualKarmaEdit",
                "CharacterCareerManualKarmaRules.TryQuote",
                'EnsureElement(root, "karma")',
                'EnsureElement(root, "nuyen")',
                "InsertManualKarmaExpenseSorted",
                'new XElement("forcecareervisible"',
                'nuyenType: "ManualSubtract"',
            )
            and _contains(
                presenter,
                "PrepareCareerManualKarmaEditAsync",
                "ApplyCareerManualKarmaEditAsync",
                "ApplyWorkspaceXmlMutationAsync",
            )
            and _contains(
                presenter_interface,
                "PrepareCareerManualKarmaEditAsync",
                "ApplyCareerManualKarmaEditAsync",
            )
            and _contains(
                presenter_persistence,
                "SaveAsync",
                "expectedContentRevision",
                "TryBeginCaptureIntent",
                "_workspacePersistenceService.SaveAsync",
            )
            and _contains(
                core_rules,
                "CharacterCareerManualKarmaState",
                "CharacterCareerManualKarmaQuote",
                "NuyenPerKarmaWorkingForPeople",
                "NuyenPerKarmaWorkingForMan",
                "nuyenExpenseAmount = checked(-amount * state.NuyenPerKarmaWorkingForPeople)",
                "nuyenBalanceDelta = checked(-amount * state.NuyenPerKarmaWorkingForMan)",
            )
            and _contains(
                source_resolver_contract,
                "TryResolveKarmaNuyenExchangeRates",
                "workingForPeopleRate",
                "workingForManRate",
            )
            and _contains(
                source_resolver,
                '"nuyenperbpwftp"',
                '"nuyenperbpwftm"',
                "TryReadPositiveDecimal",
            )
            and _contains(
                workspace_store,
                "expectedContentRevision",
                "Flush(flushToDisk: true)",
                "File.Replace",
                "File.Move",
            )
        )
        e2e_scripted = (
            _contains(
                driver,
                'CONTROLS = ("cmdKarmaGained", "cmdKarmaSpent")',
                'api != "36"',
                '"profile": "phone"',
                '"journey": "career-manual-karma"',
                '"careerManualKarmaRulesSha256"',
                '"sourceResolverSha256"',
                '"presenterPersistenceSha256"',
                '"workspaceStoreSha256"',
                '"careerFixtureSha256"',
            )
            and fixture.is_file()
        )
        return {
            "status": "implemented_pending_emulator" if implemented else "missing",
            "route": "Build > Runner > Manual Karma",
            "surface": "CareerManualKarmaPage",
            "automationId": automation_id,
            "sourceRefs": [
                "src/Chummer.Android/Native/CareerManualKarmaPage.cs",
                "src/Chummer.Android/Native/BuildPage.cs",
                "src/Chummer.Android/Native/RunnerSessionCoordinator.cs",
                "chummer-presentation/Chummer.Presentation/Overview/CareerManualKarmaEditRequest.cs",
                "chummer-presentation/Chummer.Presentation/Overview/WorkspaceXmlMutationCatalog.cs",
                "chummer-presentation/Chummer.Presentation/Overview/CharacterOverviewPresenter.WorkspaceMutations.cs",
                "chummer-presentation/Chummer.Presentation/Overview/CharacterOverviewPresenter.Persistence.cs",
                "chummer-presentation/Chummer.Presentation/Overview/ICharacterOverviewPresenter.cs",
                "chummer-core-engine/Chummer.Contracts/Characters/CharacterCareerManualKarmaRules.cs",
                "chummer-core-engine/Chummer.Application/Characters/ICharacterSourceDataResolver.cs",
                "chummer-core-engine/Chummer.Infrastructure/Xml/FileSystemCharacterSourceDataResolver.cs",
                "chummer-core-engine/Chummer.Infrastructure/Workspaces/FileWorkspaceStore.cs",
            ],
            "presenterMutation": (
                "ICharacterOverviewPresenter.ApplyCareerManualKarmaEditAsync / "
                f"CareerManualKarmaEditRequest({action}) on character/karma, character/nuyen, and character/expenses"
            ),
            "persistenceAssertion": (
                "character/karma, optional exchange-driven character/nuyen, exact sorted Karma/Nuyen expense and "
                "legacy undo metadata persist after revision-bound atomic save, same-session reopen, and two "
                "process restarts; unrelated nested Karma XML survives"
            ),
            "coverageLimit": (
                "Career only, matching Chummer5; exact saved settings must prove both NuyenPerBPWftP and "
                "NuyenPerBPWftM, including the gained-Karma People-expense/Man-balance asymmetry; the API 36 "
                f"phone driver is {'present but not yet executed' if e2e_scripted else 'missing'}"
            ),
            "e2e": {
                "status": "scripted_not_executed" if e2e_scripted else "missing",
                "ref": "tests/run_api36_career_manual_karma_e2e.py" if e2e_scripted else None,
            },
            "tablet": {
                "status": "missing",
                "surface": None,
                "automationId": None,
                "sourceRefs": [],
            },
            "tabletE2e": {"status": "missing", "ref": None},
        }
    if (
        class_name in {"CharacterCreate", "CharacterCareer"}
        and control == VEHICLE_HOME_NODE_CONTROL
    ):
        expected_handler = "chkVehicleHomeNode_CheckedChanged"
        legacy_source = (
            presentation_root / "Chummer" / "Forms" / "Character Forms" / f"{class_name}.cs"
        )
        page = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "VehicleHomeNodePage.cs"
        editor = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "CollectionEditorPages.cs"
        coordinator = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "RunnerSessionCoordinator.cs"
        e2e_driver = REPO_ROOT / "tests" / "run_api36_vehicle_home_node_e2e.py"
        overview = presentation_root / "Chummer.Presentation" / "Overview"
        request = overview / "VehicleHomeNodeEditRequest.cs"
        state = overview / "WorkspaceCollectionEditorState.cs"
        projector = overview / "WorkspaceCollectionEditorProjector.cs"
        mutation = overview / "WorkspaceXmlMutationCatalog.cs"
        presenter = overview / "CharacterOverviewPresenter.WorkspaceMutations.cs"
        presenter_interface = overview / "ICharacterOverviewPresenter.cs"
        core_models = character_notes_core_root / "Chummer.Contracts" / "Characters" / "CharacterSectionModels.cs"
        core_service = character_notes_core_root / "Chummer.Infrastructure" / "Xml" / "CharacterSectionService.cs"
        legacy_exact = (
            any(event.get("handler") == expected_handler for event in legacy.get("events", []))
            and _contains(
                legacy_source,
                expected_handler,
                "IHasMatrixAttributes objCommlink",
                "objCommlink.SetHomeNodeAsync(CharacterObject",
                "chkVehicleHomeNode.DoThreadSafeFuncAsync(x => x.Checked",
            )
        )
        implemented = (
            legacy_exact
            and _contains(
                page,
                "VehicleHomeNodeEditRequest",
                'AutomationId = $"vehicle-home-node-page-{targetToken}"',
                'AutomationId = $"vehicle-home-node-toggle-{targetToken}"',
                'AutomationId = $"vehicle-home-node-save-{targetToken}"',
                "_contentRevision",
                "Coordinator.ApplyVehicleHomeNodeEditAsync",
            )
            and _contains(
                editor,
                "item.VehicleHomeNode is not { } homeNode",
                'Guid.TryParseExact(_target.ItemId, "D"',
                'automationId: $"vehicle-home-node-open-{vehicleId:N}"',
                "new VehicleHomeNodePage",
            )
            and _contains(
                coordinator,
                "ApplyVehicleHomeNodeEditAsync",
                "ExpectedContentRevision",
                "_presenter.ApplyVehicleHomeNodeEditAsync",
                "_presenter.SaveAsync",
            )
            and _contains(
                request,
                "VehicleHomeNodeEditRequest",
                "ExpectedContentRevision",
                "Guid VehicleId",
                "bool HomeNode",
            )
            and _contains(state, "bool? VehicleHomeNode")
            and _contains(
                projector,
                "TryReadStrictBool",
                'TryReadStrictBool(item, "homeNode"',
                "VehicleHomeNode = vehicleHomeNode",
            )
            and _contains(core_models, "CharacterVehicleSummary", "bool HomeNode = false")
            and _contains(core_service, 'HomeNode: ParseBool(ReadValue(item, "homenode"))')
            and _contains(
                mutation,
                "ApplyVehicleHomeNodeEdit",
                'root.Descendants("homenode")',
                'homeNode.Value = "False"',
                'target.Value = "True"',
                "FindUniqueItemById",
            )
            and _contains(
                presenter,
                "ApplyVehicleHomeNodeEditAsync",
                "ApplyWorkspaceXmlMutationAsync",
                "ExpectedContentRevision",
            )
            and _contains(
                presenter_interface,
                "ApplyVehicleHomeNodeEditAsync",
                "VehicleHomeNodeEditRequest",
            )
        )
        e2e_scripted = _contains(
            e2e_driver,
            '"journey": "vehicle-home-node"',
            '"creationVehicleEnabledExclusive": "pass"',
            '"creationVehicleDisabledProcessRestart": "pass"',
            '"careerVehicleEnabledExclusive": "pass"',
            '"careerVehicleDisabledProcessRestart": "pass"',
            '"controls": controls',
        )
        phone_e2e = vehicle_home_node_phone_e2e_receipt if implemented and e2e_scripted else None
        return {
            "status": (
                "implemented_verified_api36"
                if phone_e2e
                else "implemented_pending_emulator" if implemented else "missing"
            ),
            "route": "Build > Gear > Vehicles > selected stable vehicle > Vehicle Home Node",
            "surface": "VehicleHomeNodePage",
            "automationId": "vehicle-home-node-toggle-{stable-vehicle-guid}",
            "sourceRefs": [
                "src/Chummer.Android/Native/VehicleHomeNodePage.cs",
                "src/Chummer.Android/Native/CollectionEditorPages.cs",
                "src/Chummer.Android/Native/RunnerSessionCoordinator.cs",
                "chummer-presentation/Chummer.Presentation/Overview/VehicleHomeNodeEditRequest.cs",
                "chummer-presentation/Chummer.Presentation/Overview/WorkspaceCollectionEditorState.cs",
                "chummer-presentation/Chummer.Presentation/Overview/WorkspaceCollectionEditorProjector.cs",
                "chummer-presentation/Chummer.Presentation/Overview/WorkspaceXmlMutationCatalog.cs",
                "chummer-presentation/Chummer.Presentation/Overview/CharacterOverviewPresenter.WorkspaceMutations.cs",
                "chummer-presentation/Chummer.Presentation/Overview/ICharacterOverviewPresenter.cs",
                "chummer-core-engine/Chummer.Contracts/Characters/CharacterSectionModels.cs",
                "chummer-core-engine/Chummer.Infrastructure/Xml/CharacterSectionService.cs",
            ],
            "presenterMutation": (
                "ICharacterOverviewPresenter.ApplyVehicleHomeNodeEditAsync("
                "VehicleHomeNodeEditRequest) with stable vehicle Guid and expected content revision"
            ),
            "persistenceAssertion": (
                "enabling selected character/vehicles/vehicle[stable Guid]/homenode sets it True and "
                "normalizes every other saved homenode False; disabling sets only the selected vehicle "
                "False; unrelated XML remains exact after save, same-session reopen, and process restart"
            ),
            "e2e": phone_e2e or {
                "status": "scripted_not_executed" if e2e_scripted else "missing",
                "ref": "tests/run_api36_vehicle_home_node_e2e.py" if e2e_scripted else None,
            },
            "coverageLimit": (
                "Exact Chummer5 selected top-level vehicle Home Node checkbox only; other device kinds "
                "and Active Commlink remain separately inventoried."
            ),
            "tablet": {
                "status": "missing",
                "surface": None,
                "automationId": None,
                "sourceRefs": [],
            },
            "tabletE2e": {"status": "missing", "ref": None},
        }
    if (
        class_name in {"CharacterCreate", "CharacterCareer"}
        and control == ARMOR_HOME_NODE_CONTROL
    ):
        expected_handler = "chkArmorHomeNode_CheckedChanged"
        legacy_source = (
            presentation_root / "Chummer" / "Forms" / "Character Forms" / f"{class_name}.cs"
        )
        page = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "ArmorHomeNodePage.cs"
        editor = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "CollectionEditorPages.cs"
        coordinator = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "RunnerSessionCoordinator.cs"
        e2e_driver = REPO_ROOT / "tests" / "run_api36_armor_home_node_e2e.py"
        overview = presentation_root / "Chummer.Presentation" / "Overview"
        request = overview / "ArmorHomeNodeEditRequest.cs"
        state = overview / "WorkspaceCollectionEditorState.cs"
        projector = overview / "WorkspaceCollectionEditorProjector.cs"
        mutation = overview / "WorkspaceXmlMutationCatalog.cs"
        presenter = overview / "CharacterOverviewPresenter.WorkspaceMutations.cs"
        presenter_interface = overview / "ICharacterOverviewPresenter.cs"
        core_models = character_notes_core_root / "Chummer.Contracts" / "Characters" / "CharacterSectionModels.cs"
        core_service = character_notes_core_root / "Chummer.Infrastructure" / "Xml" / "CharacterSectionService.cs"
        legacy_exact = (
            any(event.get("handler") == expected_handler for event in legacy.get("events", []))
            and _contains(
                legacy_source,
                expected_handler,
                "IHasMatrixAttributes objCommlink",
                "objCommlink.SetHomeNodeAsync(CharacterObject",
                "chkArmorHomeNode.DoThreadSafeFuncAsync(x => x.Checked",
            )
        )
        implemented = (
            legacy_exact
            and _contains(
                page,
                "ArmorHomeNodeEditRequest",
                'AutomationId = $"armor-home-node-page-{targetToken}"',
                'AutomationId = $"armor-home-node-toggle-{targetToken}"',
                'AutomationId = $"armor-home-node-save-{targetToken}"',
                "_contentRevision",
                "Coordinator.ApplyArmorHomeNodeEditAsync",
            )
            and _contains(
                editor,
                "item.ArmorHomeNode is not { } homeNode",
                'Guid.TryParseExact(_target.ItemId, "D"',
                'automationId: $"armor-home-node-open-{armorId:N}"',
                "new ArmorHomeNodePage",
            )
            and _contains(
                coordinator,
                "ApplyArmorHomeNodeEditAsync",
                "ExpectedContentRevision",
                "_presenter.ApplyArmorHomeNodeEditAsync",
                "_presenter.SaveAsync",
            )
            and _contains(
                request,
                "ArmorHomeNodeEditRequest",
                "ExpectedContentRevision",
                "Guid ArmorId",
                "bool HomeNode",
            )
            and _contains(state, "bool? ArmorHomeNode")
            and _contains(
                projector,
                "TryReadStrictBool",
                'TryReadStrictBool(item, "homeNode"',
                "ArmorHomeNode = armorHomeNode",
            )
            and _contains(core_models, "CharacterArmorSummary", "bool HomeNode = false")
            and _contains(core_service, 'HomeNode: ParseBool(ReadValue(item, "homenode"))')
            and _contains(
                mutation,
                "ApplyArmorHomeNodeEdit",
                'root.Descendants("homenode")',
                'homeNode.Value = "False"',
                'target.Value = "True"',
                "FindUniqueItemById",
            )
            and _contains(
                presenter,
                "ApplyArmorHomeNodeEditAsync",
                "ApplyWorkspaceXmlMutationAsync",
                "ExpectedContentRevision",
            )
            and _contains(
                presenter_interface,
                "ApplyArmorHomeNodeEditAsync",
                "ArmorHomeNodeEditRequest",
            )
        )
        e2e_scripted = _contains(
            e2e_driver,
            '"journey": "armor-home-node"',
            '"creationArmorEnabledExclusive": "pass"',
            '"creationArmorDisabledProcessRestart": "pass"',
            '"careerArmorEnabledExclusive": "pass"',
            '"careerArmorDisabledProcessRestart": "pass"',
            '"controls": controls',
        )
        phone_e2e = armor_home_node_phone_e2e_receipt if implemented and e2e_scripted else None
        return {
            "status": (
                "implemented_verified_api36"
                if phone_e2e
                else "implemented_pending_emulator" if implemented else "missing"
            ),
            "route": "Build > Gear > Armor > selected stable armor > Armor Home Node",
            "surface": "ArmorHomeNodePage",
            "automationId": "armor-home-node-toggle-{stable-armor-guid}",
            "sourceRefs": [
                "src/Chummer.Android/Native/ArmorHomeNodePage.cs",
                "src/Chummer.Android/Native/CollectionEditorPages.cs",
                "src/Chummer.Android/Native/RunnerSessionCoordinator.cs",
                "chummer-presentation/Chummer.Presentation/Overview/ArmorHomeNodeEditRequest.cs",
                "chummer-presentation/Chummer.Presentation/Overview/WorkspaceCollectionEditorState.cs",
                "chummer-presentation/Chummer.Presentation/Overview/WorkspaceCollectionEditorProjector.cs",
                "chummer-presentation/Chummer.Presentation/Overview/WorkspaceXmlMutationCatalog.cs",
                "chummer-presentation/Chummer.Presentation/Overview/CharacterOverviewPresenter.WorkspaceMutations.cs",
                "chummer-presentation/Chummer.Presentation/Overview/ICharacterOverviewPresenter.cs",
                "chummer-core-engine/Chummer.Contracts/Characters/CharacterSectionModels.cs",
                "chummer-core-engine/Chummer.Infrastructure/Xml/CharacterSectionService.cs",
            ],
            "presenterMutation": (
                "ICharacterOverviewPresenter.ApplyArmorHomeNodeEditAsync("
                "ArmorHomeNodeEditRequest) with stable armor Guid and expected content revision"
            ),
            "persistenceAssertion": (
                "enabling selected character/armors/armor[stable Guid]/homenode sets it True and "
                "normalizes every other saved homenode False; disabling sets only the selected armor "
                "False; unrelated XML remains exact after atomic save, same-session reopen, and process restart"
            ),
            "e2e": phone_e2e or {
                "status": "scripted_not_executed" if e2e_scripted else "missing",
                "ref": "tests/run_api36_armor_home_node_e2e.py" if e2e_scripted else None,
            },
            "coverageLimit": (
                "Exact Chummer5 selected top-level armor Home Node checkbox only; other device kinds "
                "and Active Commlink remain separately inventoried."
            ),
            "tablet": {
                "status": "missing",
                "surface": None,
                "automationId": None,
                "sourceRefs": [],
            },
            "tabletE2e": {"status": "missing", "ref": None},
        }
    if (
        class_name in {"CharacterCreate", "CharacterCareer"}
        and control == CRITTER_POWER_COUNT_CONTROL
    ):
        expected_handler = "chkCritterPowerCount_CheckedChanged"
        legacy_source = (
            presentation_root / "Chummer" / "Forms" / "Character Forms" / f"{class_name}.cs"
        )
        page = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "CritterPowerCountPage.cs"
        editor = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "CollectionEditorPages.cs"
        coordinator = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "RunnerSessionCoordinator.cs"
        e2e_driver = REPO_ROOT / "tests" / "run_api36_critter_power_count_e2e.py"
        overview = presentation_root / "Chummer.Presentation" / "Overview"
        request = overview / "CritterPowerCountEditRequest.cs"
        state = overview / "WorkspaceCollectionEditorState.cs"
        projector = overview / "WorkspaceCollectionEditorProjector.cs"
        mutation = overview / "WorkspaceXmlMutationCatalog.cs"
        presenter = overview / "CharacterOverviewPresenter.WorkspaceMutations.cs"
        presenter_persistence = overview / "CharacterOverviewPresenter.Persistence.cs"
        presenter_interface = overview / "ICharacterOverviewPresenter.cs"
        core_rules = character_notes_core_root / "Chummer.Contracts" / "Characters" / "CharacterCritterPowerCountRules.cs"
        core_models = character_notes_core_root / "Chummer.Contracts" / "Characters" / "CharacterSectionModels.cs"
        core_service = character_notes_core_root / "Chummer.Infrastructure" / "Xml" / "CharacterSectionService.cs"
        workspace_store = character_notes_core_root / "Chummer.Infrastructure" / "Workspaces" / "FileWorkspaceStore.cs"
        legacy_exact = (
            any(event.get("handler") == expected_handler for event in legacy.get("events", []))
            and _contains(
                legacy_source,
                expected_handler,
                "is",
                "CritterPower objPower",
                "objPower.CountTowardsLimit",
                "chkCritterPowerCount",
                "DoThreadSafeFuncAsync(x => x.Checked",
                "MakeDirtyWithCharacterUpdate",
            )
        )
        implemented = (
            legacy_exact
            and _contains(
                page,
                "CritterPowerCountEditRequest",
                'AutomationId = $"critter-power-count-page-{targetToken}"',
                'AutomationId = $"critter-power-count-toggle-{targetToken}"',
                'AutomationId = $"critter-power-count-save-{targetToken}"',
                "_contentRevision",
                "_state.CritterPowerId",
                "Coordinator.ApplyCritterPowerCountEditAsync",
            )
            and _contains(
                editor,
                "item.CritterPowerCount is not { } countState",
                'Guid.TryParseExact(_target.ItemId, "D"',
                'automationId: $"critter-power-count-open-{critterPowerId:N}"',
                "new CritterPowerCountPage",
            )
            and _contains(
                coordinator,
                "ApplyCritterPowerCountEditAsync",
                "ExpectedContentRevision",
                "_presenter.ApplyCritterPowerCountEditAsync",
                "_presenter.SaveAsync",
            )
            and _contains(
                request,
                "CritterPowerCountEditRequest",
                "ExpectedContentRevision",
                "Guid CritterPowerId",
                "bool CountsTowardsLimit",
            )
            and _contains(state, "CharacterCritterPowerCountState? CritterPowerCount")
            and _contains(
                projector,
                'TryGetPropertyValueIgnoreCase(item, "countTowardsLimitSemantics"',
                'TryReadStrictBool(semantics, "countsTowardsLimit"',
                "CritterPowerCount = critterPowerCount",
            )
            and _contains(
                core_rules,
                "CharacterCritterPowerCountState",
                "LegacyDefault = true",
                "savedIdentities.Count != 1",
                "savedValues.Count > 1",
            )
            and _contains(
                core_models,
                "CharacterCritterPowerSummary",
                "CountTowardsLimitSemantics",
            )
            and _contains(
                core_service,
                'power.Elements("counttowardslimit")',
                "CharacterCritterPowerCountRules.TryProject",
            )
            and _contains(
                mutation,
                "ApplyCritterPowerCountEdit",
                "WorkspaceCollectionKind.CritterPower",
                'new XElement("counttowardslimit")',
                'target.Value = request.CountsTowardsLimit ? "True" : "False"',
                "ResolveCollectionItem",
            )
            and _contains(
                presenter,
                "ApplyCritterPowerCountEditAsync",
                "ApplyWorkspaceXmlMutationAsync",
                "ExpectedContentRevision",
            )
            and _contains(
                presenter_persistence,
                "public async Task SaveAsync",
                "TryCaptureRecoveryPayloadAsync",
                "postcommit save recovery",
            )
            and _contains(
                workspace_store,
                "WriteRecordAtomically",
                "Flush(true)",
                "File.Replace",
            )
            and _contains(
                presenter_interface,
                "ApplyCritterPowerCountEditAsync",
                "CritterPowerCountEditRequest",
            )
        )
        e2e_scripted = _contains(
            e2e_driver,
            '"journey": "critter-power-count"',
            '"creationLegacyDefaultReadback": "pass"',
            '"creationExcludedPersistedReopenedRestarted": "pass"',
            '"careerIncludedPersistedReopenedRestarted": "pass"',
            '"controls": controls',
        )
        phone_e2e = critter_power_count_phone_e2e_receipt if implemented and e2e_scripted else None
        return {
            "status": (
                "implemented_verified_api36"
                if phone_e2e
                else "implemented_pending_emulator" if implemented else "missing"
            ),
            "route": "Build > Critter > Critter Powers > selected stable power > Counts towards limit",
            "surface": "CritterPowerCountPage",
            "automationId": "critter-power-count-toggle-{stable-critter-power-guid}",
            "sourceRefs": [
                "src/Chummer.Android/Native/CritterPowerCountPage.cs",
                "src/Chummer.Android/Native/CollectionEditorPages.cs",
                "src/Chummer.Android/Native/RunnerSessionCoordinator.cs",
                "chummer-presentation/Chummer.Presentation/Overview/CritterPowerCountEditRequest.cs",
                "chummer-presentation/Chummer.Presentation/Overview/WorkspaceCollectionEditorState.cs",
                "chummer-presentation/Chummer.Presentation/Overview/WorkspaceCollectionEditorProjector.cs",
                "chummer-presentation/Chummer.Presentation/Overview/WorkspaceXmlMutationCatalog.cs",
                "chummer-presentation/Chummer.Presentation/Overview/CharacterOverviewPresenter.WorkspaceMutations.cs",
                "chummer-presentation/Chummer.Presentation/Overview/CharacterOverviewPresenter.Persistence.cs",
                "chummer-core-engine/Chummer.Contracts/Characters/CharacterCritterPowerCountRules.cs",
                "chummer-core-engine/Chummer.Contracts/Characters/CharacterSectionModels.cs",
                "chummer-core-engine/Chummer.Infrastructure/Xml/CharacterSectionService.cs",
                "chummer-core-engine/Chummer.Infrastructure/Workspaces/FileWorkspaceStore.cs",
            ],
            "presenterMutation": (
                "ICharacterOverviewPresenter.ApplyCritterPowerCountEditAsync(CritterPowerCountEditRequest) "
                "with stable CritterPower Guid, exact legacy default-true Boolean, and expected content revision"
            ),
            "persistenceAssertion": (
                "selected character/critterpowers/critterpower[stable Guid]/counttowardslimit changes in both "
                "directions while unrelated power and custom XML remain exact after atomic save, same-session "
                "reopen, and process restart"
            ),
            "e2e": phone_e2e or {
                "status": "scripted_not_executed" if e2e_scripted else "missing",
                "ref": "tests/run_api36_critter_power_count_e2e.py" if e2e_scripted else None,
            },
            "coverageLimit": (
                "Exact Chummer5 chkCritterPowerCount in CharacterCreate and CharacterCareer only; other "
                "Critter Power actions and tablet remain separately inventoried."
            ),
            "tablet": {
                "status": "missing",
                "surface": None,
                "automationId": None,
                "sourceRefs": [],
            },
            "tabletE2e": {"status": "missing", "ref": None},
        }
    if (
        class_name in {"CharacterCreate", "CharacterCareer"}
        and control == WEAPON_ACCESSORY_INCLUDED_CONTROL
    ):
        expected_handler = "chkIncludedInWeapon_CheckedChanged"
        legacy_source = (
            presentation_root / "Chummer" / "Forms" / "Character Forms" / f"{class_name}.cs"
        )
        page = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "WeaponAccessoryIncludedPage.cs"
        editor = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "CollectionEditorPages.cs"
        coordinator = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "RunnerSessionCoordinator.cs"
        e2e_driver = REPO_ROOT / "tests" / "run_api36_weapon_accessory_included_e2e.py"
        overview = presentation_root / "Chummer.Presentation" / "Overview"
        request = overview / "WeaponAccessoryIncludedEditRequest.cs"
        state = overview / "WorkspaceCollectionEditorState.cs"
        projector = overview / "WorkspaceCollectionEditorProjector.cs"
        mutation = overview / "WorkspaceXmlMutationCatalog.cs"
        presenter = overview / "CharacterOverviewPresenter.WorkspaceMutations.cs"
        presenter_interface = overview / "ICharacterOverviewPresenter.cs"
        core_models = character_notes_core_root / "Chummer.Contracts" / "Characters" / "CharacterSectionModels.cs"
        core_service = character_notes_core_root / "Chummer.Infrastructure" / "Xml" / "CharacterSectionService.cs"
        legacy_exact = (
            any(event.get("handler") == expected_handler for event in legacy.get("events", []))
            and _contains(
                legacy_source,
                expected_handler,
                "is WeaponAccessory",
                "objAccessory.IncludedInWeapon",
                "chkIncludedInWeapon",
                "DoThreadSafeFuncAsync(x => x.Checked",
                "AllowEditPartOfBaseWeapon",
            )
        )
        implemented = (
            legacy_exact
            and _contains(
                page,
                "WeaponAccessoryIncludedEditRequest",
                'AutomationId = $"weapon-accessory-included-page-{targetToken}"',
                'AutomationId = $"weapon-accessory-included-toggle-{targetToken}"',
                'AutomationId = $"weapon-accessory-included-save-{targetToken}"',
                "_contentRevision",
                "_weaponId",
                "_accessoryId",
                "Coordinator.ApplyWeaponAccessoryIncludedEditAsync",
            )
            and _contains(
                editor,
                "item.WeaponAccessoryIncludedInWeapon is not { } includedInWeapon",
                'Guid.TryParseExact(_target.ItemId, "D"',
                'Guid.TryParseExact(_target.NestedItemId, "D"',
                'automationId: $"weapon-accessory-included-open-{accessoryId:N}"',
                "new WeaponAccessoryIncludedPage",
            )
            and _contains(
                coordinator,
                "ApplyWeaponAccessoryIncludedEditAsync",
                "ExpectedContentRevision",
                "_presenter.ApplyWeaponAccessoryIncludedEditAsync",
                "_presenter.SaveAsync",
            )
            and _contains(
                request,
                "WeaponAccessoryIncludedEditRequest",
                "ExpectedContentRevision",
                "Guid WeaponId",
                "Guid AccessoryId",
                "bool IncludedInWeapon",
            )
            and _contains(state, "bool? WeaponAccessoryIncludedInWeapon")
            and _contains(
                projector,
                'TryReadStrictBool(item, "includedInWeapon"',
                'Guid.TryParseExact(target.ItemId, "D"',
                'Guid.TryParseExact(target.NestedItemId, "D"',
                "WeaponAccessoryIncludedInWeapon = weaponAccessoryIncludedInWeapon",
            )
            and _contains(
                core_models,
                "CharacterWeaponAccessorySummary",
                "bool IncludedInWeapon = false",
            )
            and _contains(
                core_service,
                'IncludedInWeapon: ParseBool(ReadValue(accessory, "included"))',
            )
            and _contains(
                mutation,
                "ApplyWeaponAccessoryIncludedEdit",
                "WorkspaceCollectionKind.Weapon",
                "WorkspaceNestedCollectionKind.WeaponAccessory",
                'target.Value = request.IncludedInWeapon ? "True" : "False"',
                "ResolveCollectionItem",
            )
            and _contains(
                presenter,
                "ApplyWeaponAccessoryIncludedEditAsync",
                "ApplyWorkspaceXmlMutationAsync",
                "ExpectedContentRevision",
            )
            and _contains(
                presenter_interface,
                "ApplyWeaponAccessoryIncludedEditAsync",
                "WeaponAccessoryIncludedEditRequest",
            )
        )
        e2e_scripted = _contains(
            e2e_driver,
            '"journey": "weapon-accessory-included"',
            '"creationAccessoryEnabled": "pass"',
            '"creationAccessoryDisabledProcessRestart": "pass"',
            '"careerAccessoryEnabled": "pass"',
            '"careerAccessoryDisabledProcessRestart": "pass"',
            '"controls": controls',
        )
        phone_e2e = weapon_accessory_included_phone_e2e_receipt if implemented and e2e_scripted else None
        return {
            "status": (
                "implemented_verified_api36"
                if phone_e2e
                else "implemented_pending_emulator" if implemented else "missing"
            ),
            "route": (
                "Build > Gear > Weapon Accessories > selected stable accessory > Included in Weapon"
            ),
            "surface": "WeaponAccessoryIncludedPage",
            "automationId": "weapon-accessory-included-toggle-{stable-accessory-guid}",
            "sourceRefs": [
                "src/Chummer.Android/Native/WeaponAccessoryIncludedPage.cs",
                "src/Chummer.Android/Native/CollectionEditorPages.cs",
                "src/Chummer.Android/Native/RunnerSessionCoordinator.cs",
                "chummer-presentation/Chummer.Presentation/Overview/WeaponAccessoryIncludedEditRequest.cs",
                "chummer-presentation/Chummer.Presentation/Overview/WorkspaceCollectionEditorState.cs",
                "chummer-presentation/Chummer.Presentation/Overview/WorkspaceCollectionEditorProjector.cs",
                "chummer-presentation/Chummer.Presentation/Overview/WorkspaceXmlMutationCatalog.cs",
                "chummer-presentation/Chummer.Presentation/Overview/CharacterOverviewPresenter.WorkspaceMutations.cs",
                "chummer-presentation/Chummer.Presentation/Overview/ICharacterOverviewPresenter.cs",
                "chummer-core-engine/Chummer.Contracts/Characters/CharacterSectionModels.cs",
                "chummer-core-engine/Chummer.Infrastructure/Xml/CharacterSectionService.cs",
            ],
            "presenterMutation": (
                "ICharacterOverviewPresenter.ApplyWeaponAccessoryIncludedEditAsync("
                "WeaponAccessoryIncludedEditRequest) with stable parent weapon Guid, stable accessory Guid, "
                "and expected content revision"
            ),
            "persistenceAssertion": (
                "selected character/weapons/weapon[stable parent Guid]/accessories/accessory[stable Guid]/included "
                "is set to exact Chummer5 True or False while sibling accessories and unrelated XML remain exact "
                "after revision-checked atomic save, same-session reopen, and process restart"
            ),
            "e2e": phone_e2e or {
                "status": "scripted_not_executed" if e2e_scripted else "missing",
                "ref": "tests/run_api36_weapon_accessory_included_e2e.py" if e2e_scripted else None,
            },
            "coverageLimit": (
                "Exact Chummer5 selected saved weapon-accessory Included in Weapon Boolean; legacy desktop "
                "visibility remains governed by AllowEditPartOfBaseWeapon, while tablet is deferred."
            ),
            "tablet": {
                "status": "missing",
                "surface": None,
                "automationId": None,
                "sourceRefs": [],
            },
            "tabletE2e": {"status": "missing", "ref": None},
        }
    if (
        class_name in {"CharacterCreate", "CharacterCareer"}
        and control == WEAPON_HOME_NODE_CONTROL
    ):
        expected_handler = "chkWeaponHomeNode_CheckedChanged"
        legacy_source = (
            presentation_root / "Chummer" / "Forms" / "Character Forms" / f"{class_name}.cs"
        )
        page = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "WeaponHomeNodePage.cs"
        editor = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "CollectionEditorPages.cs"
        coordinator = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "RunnerSessionCoordinator.cs"
        e2e_driver = REPO_ROOT / "tests" / "run_api36_weapon_home_node_e2e.py"
        overview = presentation_root / "Chummer.Presentation" / "Overview"
        request = overview / "WeaponHomeNodeEditRequest.cs"
        state = overview / "WorkspaceCollectionEditorState.cs"
        projector = overview / "WorkspaceCollectionEditorProjector.cs"
        mutation = overview / "WorkspaceXmlMutationCatalog.cs"
        presenter = overview / "CharacterOverviewPresenter.WorkspaceMutations.cs"
        presenter_interface = overview / "ICharacterOverviewPresenter.cs"
        core_contracts = character_notes_core_root / "Chummer.Contracts" / "Characters"
        core_rules = core_contracts / "CharacterWeaponHomeNodeRules.cs"
        core_resolver = core_contracts / "CharacterWeaponMatrixParentResolver.cs"
        core_models = core_contracts / "CharacterSectionModels.cs"
        core_service = character_notes_core_root / "Chummer.Infrastructure" / "Xml" / "CharacterSectionService.cs"
        legacy_exact = (
            any(event.get("handler") == expected_handler for event in legacy.get("events", []))
            and _contains(
                legacy_source,
                expected_handler,
                "IHasMatrixAttributes objCommlink",
                "objCommlink.SetHomeNodeAsync(CharacterObject",
                "chkWeaponHomeNode.DoThreadSafeFuncAsync(x => x.Checked",
                "CharacterObject.GetIsAIAsync",
                'GetTotalMatrixAttributeAsync("Device Rating"',
                '"Program Limit", token',
                'CharacterObject.GetAttributeAsync("DEP"',
                "blnIsCommlink &&",
            )
        )
        implemented = (
            legacy_exact
            and _contains(
                page,
                "CharacterWeaponHomeNodeSemantics",
                "WeaponHomeNodeEditRequest",
                'AutomationId = $"weapon-home-node-page-{targetToken}"',
                'AutomationId = $"weapon-home-node-toggle-{targetToken}"',
                'AutomationId = $"weapon-home-node-save-{targetToken}"',
                "semantics.Enabled",
                "Coordinator.ApplyWeaponHomeNodeEditAsync",
            )
            and _contains(
                editor,
                "item.WeaponHomeNode is not { Visible: true } semantics",
                'Guid.TryParseExact(_target.ItemId, "D"',
                'automationId: $"weapon-home-node-open-{weaponId:N}"',
                "new WeaponHomeNodePage",
            )
            and _contains(
                coordinator,
                "ApplyWeaponHomeNodeEditAsync",
                "ExpectedContentRevision",
                "_presenter.ApplyWeaponHomeNodeEditAsync",
                "_presenter.SaveAsync",
            )
            and _contains(
                request,
                "WeaponHomeNodeEditRequest",
                "ExpectedContentRevision",
                "Guid WeaponId",
                "bool HomeNode",
                "CharacterWeaponHomeNodeSemantics ExpectedSemantics",
            )
            and _contains(state, "CharacterWeaponHomeNodeSemantics? WeaponHomeNode")
            and _contains(
                projector,
                "ProjectWeaponHomeNode",
                '"homeNodeSemantics"',
                "WeaponHomeNode = weaponHomeNode",
                "deviceRating",
                "programLimit",
                "depTotal",
            )
            and _contains(
                core_rules,
                "CharacterWeaponHomeNodeSemantics",
                "CharacterWeaponMatrixParentResolver.TryResolveOwner",
                "TryReadIsAi",
                "TryReadAttributeTotal",
                "requiredProgramLimit = depTotal > deviceRating ? 2 : 1",
                "programLimit >= requiredProgramLimit",
                "EnumerateSavedHomeNodes",
            )
            and _contains(
                core_resolver,
                "CharacterWeaponMatrixParentResolver",
                "TryResolveOwner",
            )
            and _contains(
                core_models,
                "CharacterWeaponSummary",
                "CharacterWeaponHomeNodeSemantics? HomeNodeSemantics",
            )
            and _contains(
                core_service,
                "CharacterWeaponHomeNodeRules.TryProject",
                "HomeNodeSemantics = homeNodeSemantics",
            )
            and _contains(
                mutation,
                "ApplyWeaponHomeNodeEdit",
                "CharacterWeaponHomeNodeRules.TryProject",
                "current != request.ExpectedSemantics",
                "!current.Visible || !current.Enabled",
                "CharacterWeaponHomeNodeRules.EnumerateSavedHomeNodes",
                'homeNode.Value = "False"',
                'target.Value = "True"',
                "FindUniqueItemById",
            )
            and _contains(
                presenter,
                "ApplyWeaponHomeNodeEditAsync",
                "ApplyWorkspaceXmlMutationAsync",
                "ExpectedContentRevision",
            )
            and _contains(
                presenter_interface,
                "ApplyWeaponHomeNodeEditAsync",
                "WeaponHomeNodeEditRequest",
            )
        )
        e2e_scripted = _contains(
            e2e_driver,
            '"journey": "weapon-home-node"',
            '"creationAiEligibilityReadback": "pass"',
            '"creationWeaponEnabledExclusive": "pass"',
            '"creationWeaponDisabledProcessRestart": "pass"',
            '"careerAiEligibilityReadback": "pass"',
            '"careerWeaponEnabledExclusive": "pass"',
            '"careerWeaponDisabledProcessRestart": "pass"',
            '"controls": controls',
        )
        phone_e2e = weapon_home_node_phone_e2e_receipt if implemented and e2e_scripted else None
        return {
            "status": (
                "implemented_verified_api36"
                if phone_e2e
                else "implemented_pending_emulator" if implemented else "missing"
            ),
            "route": "Build > Gear > Weapons > selected stable AI weapon > Weapon Home Node",
            "surface": "WeaponHomeNodePage",
            "automationId": "weapon-home-node-toggle-{stable-weapon-guid}",
            "sourceRefs": [
                "src/Chummer.Android/Native/WeaponHomeNodePage.cs",
                "src/Chummer.Android/Native/CollectionEditorPages.cs",
                "src/Chummer.Android/Native/RunnerSessionCoordinator.cs",
                "chummer-presentation/Chummer.Presentation/Overview/WeaponHomeNodeEditRequest.cs",
                "chummer-presentation/Chummer.Presentation/Overview/WorkspaceCollectionEditorState.cs",
                "chummer-presentation/Chummer.Presentation/Overview/WorkspaceCollectionEditorProjector.cs",
                "chummer-presentation/Chummer.Presentation/Overview/WorkspaceXmlMutationCatalog.cs",
                "chummer-presentation/Chummer.Presentation/Overview/CharacterOverviewPresenter.WorkspaceMutations.cs",
                "chummer-presentation/Chummer.Presentation/Overview/ICharacterOverviewPresenter.cs",
                "chummer-core-engine/Chummer.Contracts/Characters/CharacterWeaponHomeNodeRules.cs",
                "chummer-core-engine/Chummer.Contracts/Characters/CharacterWeaponMatrixParentResolver.cs",
                "chummer-core-engine/Chummer.Contracts/Characters/CharacterSectionModels.cs",
                "chummer-core-engine/Chummer.Infrastructure/Xml/CharacterSectionService.cs",
            ],
            "presenterMutation": (
                "ICharacterOverviewPresenter.ApplyWeaponHomeNodeEditAsync(WeaponHomeNodeEditRequest) "
                "with stable weapon/Matrix-owner Guids, exact AI/Device Rating/Program Limit/DEP semantics, "
                "and expected content revision"
            ),
            "persistenceAssertion": (
                "enabling selected character/weapons/weapon[stable Guid]/homenode revalidates the exact "
                "Chummer5 AI commlink Program Limit >= (DEP > Device Rating ? 2 : 1) rule, sets it True, "
                "and normalizes every other recognized saved homenode False; disabling sets the selected "
                "weapon False; unrelated XML remains exact after revision-checked atomic save, same-session "
                "reopen, and process restart"
            ),
            "e2e": phone_e2e or {
                "status": "scripted_not_executed" if e2e_scripted else "missing",
                "ref": "tests/run_api36_weapon_home_node_e2e.py" if e2e_scripted else None,
            },
            "coverageLimit": (
                "Exact top-level stable weapon with a uniquely resolved saved Gear/Armor/Cyberware/Vehicle "
                "Matrix owner and locally provable Matrix expressions; unsupported source-only vehicle mods, "
                "Living Persona fragments, or unresolved expressions fail closed. Tablet is deferred."
            ),
            "tablet": {
                "status": "missing",
                "surface": None,
                "automationId": None,
                "sourceRefs": [],
            },
            "tabletE2e": {"status": "missing", "ref": None},
        }
    if (
        class_name in {"CharacterCreate", "CharacterCareer"}
        and control == WEAPON_ACTIVE_COMMLINK_CONTROL
    ):
        expected_handler = "chkWeaponActiveCommlink_CheckedChanged"
        legacy_source = (
            presentation_root / "Chummer" / "Forms" / "Character Forms" / f"{class_name}.cs"
        )
        page = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "WeaponActiveCommlinkPage.cs"
        editor = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "CollectionEditorPages.cs"
        coordinator = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "RunnerSessionCoordinator.cs"
        e2e_driver = REPO_ROOT / "tests" / "run_api36_weapon_active_commlink_e2e.py"
        overview = presentation_root / "Chummer.Presentation" / "Overview"
        request = overview / "WeaponActiveCommlinkEditRequest.cs"
        state = overview / "WorkspaceCollectionEditorState.cs"
        projector = overview / "WorkspaceCollectionEditorProjector.cs"
        mutation = overview / "WorkspaceXmlMutationCatalog.cs"
        presenter = overview / "CharacterOverviewPresenter.WorkspaceMutations.cs"
        presenter_interface = overview / "ICharacterOverviewPresenter.cs"
        core_contracts = character_notes_core_root / "Chummer.Contracts" / "Characters"
        core_rules = core_contracts / "CharacterWeaponActiveCommlinkRules.cs"
        core_home_rules = core_contracts / "CharacterWeaponHomeNodeRules.cs"
        core_resolver = core_contracts / "CharacterWeaponMatrixParentResolver.cs"
        core_models = core_contracts / "CharacterSectionModels.cs"
        core_service = character_notes_core_root / "Chummer.Infrastructure" / "Xml" / "CharacterSectionService.cs"
        legacy_exact = (
            any(event.get("handler") == expected_handler for event in legacy.get("events", []))
            and _contains(
                legacy_source,
                expected_handler,
                "IHasMatrixAttributes objSelectedCommlink",
                "objSelectedCommlink.SetActiveCommlinkAsync(CharacterObject",
                "chkWeaponActiveCommlink.DoThreadSafeFuncAsync(x => x.Checked",
                "x.Checked = blnIsActiveCommlink",
                "x.Visible = blnIsCommlink",
            )
        )
        implemented = (
            legacy_exact
            and _contains(
                page,
                "CharacterWeaponActiveCommlinkSemantics",
                "WeaponActiveCommlinkEditRequest",
                'AutomationId = $"weapon-active-commlink-page-{targetToken}"',
                'AutomationId = $"weapon-active-commlink-toggle-{targetToken}"',
                'AutomationId = $"weapon-active-commlink-save-{targetToken}"',
                "semantics.IsCommlink",
                "Coordinator.ApplyWeaponActiveCommlinkEditAsync",
            )
            and _contains(
                editor,
                "item.WeaponActiveCommlink is not { IsCommlink: true } semantics",
                'Guid.TryParseExact(_target.ItemId, "D"',
                'automationId: $"weapon-active-commlink-open-{weaponId:N}"',
                "new WeaponActiveCommlinkPage",
            )
            and _contains(
                coordinator,
                "ApplyWeaponActiveCommlinkEditAsync",
                "ExpectedContentRevision",
                "_presenter.ApplyWeaponActiveCommlinkEditAsync",
                "_presenter.SaveAsync",
            )
            and _contains(
                request,
                "WeaponActiveCommlinkEditRequest",
                "ExpectedContentRevision",
                "Guid WeaponId",
                "bool ActiveCommlink",
                "CharacterWeaponActiveCommlinkSemantics ExpectedSemantics",
            )
            and _contains(
                state,
                "CharacterWeaponActiveCommlinkSemantics? WeaponActiveCommlink",
            )
            and _contains(
                projector,
                "ProjectWeaponActiveCommlink",
                '"activeCommlinkSemantics"',
                "WeaponActiveCommlink = weaponActiveCommlink",
                "matrixOwnerId",
                "matrixOwnerKind",
                "isCommlink",
            )
            and _contains(
                core_rules,
                "CharacterWeaponActiveCommlinkSemantics",
                "CharacterWeaponMatrixParentResolver.TryResolveOwner",
                "TryEvaluateOwnerIsCommlink",
                "EnumerateSavedActiveCommlinks",
            )
            and _contains(
                core_home_rules,
                "TryEvaluateOwnerIsCommlink",
                "CharacterMatrixOwnerKind.Gear",
                "CharacterMatrixOwnerKind.Armor",
                "CharacterMatrixOwnerKind.Cyberware",
                "CharacterMatrixOwnerKind.Vehicle",
            )
            and _contains(
                core_resolver,
                "CharacterWeaponMatrixParentResolver",
                "TryResolveOwner",
            )
            and _contains(
                core_models,
                "CharacterWeaponSummary",
                "CharacterWeaponActiveCommlinkSemantics? ActiveCommlinkSemantics",
            )
            and _contains(
                core_service,
                "CharacterWeaponActiveCommlinkRules.TryProject",
                "ActiveCommlinkSemantics = activeCommlinkSemantics",
            )
            and _contains(
                mutation,
                "ApplyWeaponActiveCommlinkEdit",
                "CharacterWeaponActiveCommlinkRules.TryProject",
                "current != request.ExpectedSemantics",
                "!current.IsCommlink",
                "EnumerateSavedActiveCommlinks(root)",
                'active.Value = "False"',
                'target.Value = "True"',
                "FindUniqueItemById",
            )
            and _contains(
                presenter,
                "ApplyWeaponActiveCommlinkEditAsync",
                "ApplyWorkspaceXmlMutationAsync",
                "ExpectedContentRevision",
            )
            and _contains(
                presenter_interface,
                "ApplyWeaponActiveCommlinkEditAsync",
                "WeaponActiveCommlinkEditRequest",
            )
        )
        e2e_scripted = _contains(
            e2e_driver,
            '"journey": "weapon-active-commlink"',
            '"creationMatrixOwnerReadback": "pass"',
            '"creationWeaponEnabledExclusive": "pass"',
            '"creationWeaponDisabledProcessRestart": "pass"',
            '"careerMatrixOwnerReadback": "pass"',
            '"careerWeaponEnabledExclusive": "pass"',
            '"careerWeaponDisabledProcessRestart": "pass"',
            '"controls": controls',
        )
        phone_e2e = weapon_active_commlink_phone_e2e_receipt if implemented and e2e_scripted else None
        return {
            "status": (
                "implemented_verified_api36"
                if phone_e2e
                else "implemented_pending_emulator" if implemented else "missing"
            ),
            "route": "Build > Gear > Weapons > selected stable commlink weapon > Weapon Active Commlink",
            "surface": "WeaponActiveCommlinkPage",
            "automationId": "weapon-active-commlink-toggle-{stable-weapon-guid}",
            "sourceRefs": [
                "src/Chummer.Android/Native/WeaponActiveCommlinkPage.cs",
                "src/Chummer.Android/Native/CollectionEditorPages.cs",
                "src/Chummer.Android/Native/RunnerSessionCoordinator.cs",
                "chummer-presentation/Chummer.Presentation/Overview/WeaponActiveCommlinkEditRequest.cs",
                "chummer-presentation/Chummer.Presentation/Overview/WorkspaceCollectionEditorState.cs",
                "chummer-presentation/Chummer.Presentation/Overview/WorkspaceCollectionEditorProjector.cs",
                "chummer-presentation/Chummer.Presentation/Overview/WorkspaceXmlMutationCatalog.cs",
                "chummer-presentation/Chummer.Presentation/Overview/CharacterOverviewPresenter.WorkspaceMutations.cs",
                "chummer-presentation/Chummer.Presentation/Overview/ICharacterOverviewPresenter.cs",
                "chummer-core-engine/Chummer.Contracts/Characters/CharacterWeaponActiveCommlinkRules.cs",
                "chummer-core-engine/Chummer.Contracts/Characters/CharacterWeaponHomeNodeRules.cs",
                "chummer-core-engine/Chummer.Contracts/Characters/CharacterWeaponMatrixParentResolver.cs",
                "chummer-core-engine/Chummer.Contracts/Characters/CharacterSectionModels.cs",
                "chummer-core-engine/Chummer.Infrastructure/Xml/CharacterSectionService.cs",
            ],
            "presenterMutation": (
                "ICharacterOverviewPresenter.ApplyWeaponActiveCommlinkEditAsync("
                "WeaponActiveCommlinkEditRequest) with stable weapon/Matrix-owner Guids, exact persona "
                "eligibility, full expected semantics, and expected content revision"
            ),
            "persistenceAssertion": (
                "enabling selected character/weapons/weapon[stable Guid]/active revalidates its exact "
                "saved Matrix owner and commlink eligibility, sets it True, and normalizes every other "
                "recognized saved matrix-device active False; disabling sets only the selected weapon "
                "False; unrelated active XML and other fields remain exact after revision-checked atomic "
                "save, same-session reopen, and process restart"
            ),
            "e2e": phone_e2e or {
                "status": "scripted_not_executed" if e2e_scripted else "missing",
                "ref": "tests/run_api36_weapon_active_commlink_e2e.py" if e2e_scripted else None,
            },
            "coverageLimit": (
                "Exact selected stable top-level Weapon whose saved Gear/Armor/Cyberware/Vehicle Matrix "
                "owner is uniquely resolved and locally provable; source-only vehicle-mod contributions "
                "fail closed. Legacy WeaponAccessory/Gear tree selections remain separately inventoried. "
                "Tablet is deferred."
            ),
            "tablet": {
                "status": "missing",
                "surface": None,
                "automationId": None,
                "sourceRefs": [],
            },
            "tabletE2e": {"status": "missing", "ref": None},
        }
    if (
        class_name in {"CharacterCreate", "CharacterCareer"}
        and control == ARMOR_ACTIVE_COMMLINK_CONTROL
    ):
        expected_handler = "chkArmorActiveCommlink_CheckedChanged"
        legacy_source = (
            presentation_root / "Chummer" / "Forms" / "Character Forms" / f"{class_name}.cs"
        )
        page = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "ArmorActiveCommlinkPage.cs"
        editor = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "CollectionEditorPages.cs"
        coordinator = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "RunnerSessionCoordinator.cs"
        e2e_driver = REPO_ROOT / "tests" / "run_api36_armor_active_commlink_e2e.py"
        overview = presentation_root / "Chummer.Presentation" / "Overview"
        request = overview / "ArmorActiveCommlinkEditRequest.cs"
        state = overview / "WorkspaceCollectionEditorState.cs"
        projector = overview / "WorkspaceCollectionEditorProjector.cs"
        mutation = overview / "WorkspaceXmlMutationCatalog.cs"
        presenter = overview / "CharacterOverviewPresenter.WorkspaceMutations.cs"
        presenter_interface = overview / "ICharacterOverviewPresenter.cs"
        core_models = character_notes_core_root / "Chummer.Contracts" / "Characters" / "CharacterSectionModels.cs"
        core_service = character_notes_core_root / "Chummer.Infrastructure" / "Xml" / "CharacterSectionService.cs"
        legacy_exact = (
            any(event.get("handler") == expected_handler for event in legacy.get("events", []))
            and _contains(
                legacy_source,
                expected_handler,
                "IHasMatrixAttributes objSelectedCommlink",
                "objSelectedCommlink.SetActiveCommlinkAsync(CharacterObject",
                "chkArmorActiveCommlink.DoThreadSafeFuncAsync(x => x.Checked",
                "x.Visible = blnIsCommlink",
            )
        )
        implemented = (
            legacy_exact
            and _contains(
                page,
                "ArmorActiveCommlinkEditRequest",
                'AutomationId = $"armor-active-commlink-page-{targetToken}"',
                'AutomationId = $"armor-active-commlink-toggle-{targetToken}"',
                'AutomationId = $"armor-active-commlink-save-{targetToken}"',
                "_contentRevision",
                "Coordinator.ApplyArmorActiveCommlinkEditAsync",
            )
            and _contains(
                editor,
                "item.ArmorActiveCommlink is not { } activeCommlink",
                'Guid.TryParseExact(_target.ItemId, "D"',
                'automationId: $"armor-active-commlink-open-{armorId:N}"',
                "new ArmorActiveCommlinkPage",
            )
            and _contains(
                coordinator,
                "ApplyArmorActiveCommlinkEditAsync",
                "ExpectedContentRevision",
                "_presenter.ApplyArmorActiveCommlinkEditAsync",
                "_presenter.SaveAsync",
            )
            and _contains(
                request,
                "ArmorActiveCommlinkEditRequest",
                "ExpectedContentRevision",
                "Guid ArmorId",
                "bool ActiveCommlink",
            )
            and _contains(state, "bool? ArmorActiveCommlink")
            and _contains(
                projector,
                "TryReadStrictBool",
                'TryReadStrictBool(item, "isCommlink"',
                'TryReadStrictBool(item, "activeCommlink"',
                "ArmorActiveCommlink = armorActiveCommlink",
            )
            and _contains(
                core_models,
                "CharacterArmorSummary",
                "bool ActiveCommlink = false",
                "bool IsCommlink = false",
            )
            and _contains(
                core_service,
                'ActiveCommlink: ParseBool(ReadValue(item, "active"))',
                "IsCommlink: IsArmorCommlink(item)",
                'Contains("Self", StringComparison.Ordinal)',
                'Contains("Parent", StringComparison.Ordinal)',
            )
            and _contains(
                mutation,
                "ApplyArmorActiveCommlinkEdit",
                'node.Name.LocalName is "armor" or "gear" or "weapon" or "cyberware" or "vehicle"',
                'ReadDirectValue(armor, "canformpersona").Contains("Self", StringComparison.Ordinal)',
                'active.Value = "False"',
                'target.Value = "True"',
                "FindUniqueItemById",
            )
            and _contains(
                presenter,
                "ApplyArmorActiveCommlinkEditAsync",
                "ApplyWorkspaceXmlMutationAsync",
                "ExpectedContentRevision",
            )
            and _contains(
                presenter_interface,
                "ApplyArmorActiveCommlinkEditAsync",
                "ArmorActiveCommlinkEditRequest",
            )
        )
        e2e_scripted = _contains(
            e2e_driver,
            '"journey": "armor-active-commlink"',
            '"creationArmorEnabledExclusive": "pass"',
            '"creationArmorDisabledProcessRestart": "pass"',
            '"careerArmorEnabledExclusive": "pass"',
            '"careerArmorDisabledProcessRestart": "pass"',
            '"controls": controls',
        )
        phone_e2e = armor_active_commlink_phone_e2e_receipt if implemented and e2e_scripted else None
        return {
            "status": (
                "implemented_verified_api36"
                if phone_e2e
                else "implemented_pending_emulator" if implemented else "missing"
            ),
            "route": "Build > Gear > Armor > selected stable persona-capable armor > Armor Active Commlink",
            "surface": "ArmorActiveCommlinkPage",
            "automationId": "armor-active-commlink-toggle-{stable-armor-guid}",
            "sourceRefs": [
                "src/Chummer.Android/Native/ArmorActiveCommlinkPage.cs",
                "src/Chummer.Android/Native/CollectionEditorPages.cs",
                "src/Chummer.Android/Native/RunnerSessionCoordinator.cs",
                "chummer-presentation/Chummer.Presentation/Overview/ArmorActiveCommlinkEditRequest.cs",
                "chummer-presentation/Chummer.Presentation/Overview/WorkspaceCollectionEditorState.cs",
                "chummer-presentation/Chummer.Presentation/Overview/WorkspaceCollectionEditorProjector.cs",
                "chummer-presentation/Chummer.Presentation/Overview/WorkspaceXmlMutationCatalog.cs",
                "chummer-presentation/Chummer.Presentation/Overview/CharacterOverviewPresenter.WorkspaceMutations.cs",
                "chummer-presentation/Chummer.Presentation/Overview/ICharacterOverviewPresenter.cs",
                "chummer-core-engine/Chummer.Contracts/Characters/CharacterSectionModels.cs",
                "chummer-core-engine/Chummer.Infrastructure/Xml/CharacterSectionService.cs",
            ],
            "presenterMutation": (
                "ICharacterOverviewPresenter.ApplyArmorActiveCommlinkEditAsync("
                "ArmorActiveCommlinkEditRequest) with stable armor Guid, exact persona eligibility, "
                "and expected content revision"
            ),
            "persistenceAssertion": (
                "enabling selected persona-capable character/armors/armor[stable Guid]/active sets it True and "
                "normalizes every other saved matrix-device active False; disabling sets only the selected "
                "armor False; unrelated active XML and other fields remain exact after atomic save, "
                "same-session reopen, and process restart"
            ),
            "e2e": phone_e2e or {
                "status": "scripted_not_executed" if e2e_scripted else "missing",
                "ref": "tests/run_api36_armor_active_commlink_e2e.py" if e2e_scripted else None,
            },
            "coverageLimit": (
                "Exact Chummer5 selected top-level persona-capable armor Active Commlink checkbox only; "
                "other device kinds remain separately inventoried."
            ),
            "tablet": {
                "status": "missing",
                "surface": None,
                "automationId": None,
                "sourceRefs": [],
            },
            "tabletE2e": {"status": "missing", "ref": None},
        }
    if class_name in {"CharacterCreate", "CharacterCareer"} and control == QUALITY_LEVEL_CONTROL:
        legacy_source = (
            chummer5_root / "Chummer" / "Forms" / "Character Forms" / f"{class_name}.cs"
        )
        page = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "QualityLevelPage.cs"
        editor = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "CollectionEditorPages.cs"
        coordinator = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "RunnerSessionCoordinator.cs"
        e2e_driver = REPO_ROOT / "tests" / "run_api36_quality_level_e2e.py"
        creation_fixture = REPO_ROOT / "tests" / "fixtures" / "creation-quality-level-e2e.chum5"
        career_fixture = REPO_ROOT / "tests" / "fixtures" / "career-quality-level-e2e.chum5"
        overview = presentation_root / "Chummer.Presentation" / "Overview"
        request = overview / "QualityLevelEditRequest.cs"
        state = overview / "WorkspaceCollectionEditorState.cs"
        projector = overview / "WorkspaceCollectionEditorProjector.cs"
        mutation = overview / "WorkspaceXmlMutationCatalog.cs"
        presenter = overview / "CharacterOverviewPresenter.WorkspaceMutations.cs"
        presenter_interface = overview / "ICharacterOverviewPresenter.cs"
        source_contract = character_notes_core_root / "Chummer.Application" / "Characters" / "ICharacterSourceDataResolver.cs"
        core_models = character_notes_core_root / "Chummer.Contracts" / "Characters" / "CharacterSectionModels.cs"
        core_service = character_notes_core_root / "Chummer.Infrastructure" / "Xml" / "CharacterSectionService.cs"
        file_resolver = character_notes_core_root / "Chummer.Infrastructure" / "Xml" / "FileSystemCharacterSourceDataResolver.cs"
        legacy_exact = (
            any(event.get("handler") == "nudQualityLevel_ValueChanged" for event in legacy.get("events", []))
            and _contains(
                legacy_source,
                "nudQualityLevel_ValueChanged",
                "GetLevelsAsync",
                "intSelectedLevels",
                "SourceID",
                "GetExtraAsync",
                "GetSourceNameAsync",
            )
        )
        implemented = (
            legacy_exact
            and _contains(
                page,
                "QualityLevelEditRequest",
                'AutomationId = $"quality-level-page-{token}"',
                '$"quality-level-value-{token}"',
                '$"quality-level-save-{token}"',
                '"Confirm Quality Level increase"',
                "Coordinator.ApplyQualityLevelEditAsync",
            )
            and _contains(
                editor,
                "item.QualityLevel is not { } qualityLevel",
                'Guid.TryParseExact(_target.ItemId, "D"',
                'automationId: $"quality-level-open-{qualityId:N}"',
                "new QualityLevelPage",
            )
            and _contains(
                coordinator,
                "ApplyQualityLevelEditAsync",
                "ExpectedContentRevision",
                "_presenter.ApplyQualityLevelEditAsync",
                "_presenter.SaveAsync",
            )
            and _contains(
                request,
                "QualityLevelEditRequest",
                "ExpectedContentRevision",
                "Guid QualityId",
                "ExpectedLevel",
                "MaximumLevel",
                "NewLevel",
            )
            and _contains(
                state,
                "WorkspaceQualityLevelState",
                "Guid QualityId",
                "int Level",
                "int MaximumLevel",
                "bool CareerMode",
            )
            and _contains(
                projector,
                'TryGetPropertyValueIgnoreCase(item, "levelSemantics"',
                "ProjectQualityLevel",
                "QualityLevel = qualityLevel",
            )
            and _contains(
                mutation,
                "ApplyQualityLevelEdit",
                "CharacterSectionService(sourceDataResolver)",
                "FindUniqueItemById",
                "AppendFreeCareerQualityExpense",
                "AppendFreeCareerNegativeQualityRemovalExpense",
                'new XElement("karmatype", "AddQuality")',
                'new XElement("karmatype", "RemoveQuality")',
            )
            and _contains(
                presenter,
                "ApplyQualityLevelEditAsync",
                "ApplyWorkspaceXmlMutationAsync",
                "ExpectedContentRevision",
            )
            and _contains(
                presenter_interface,
                "ApplyQualityLevelEditAsync",
                "QualityLevelEditRequest",
            )
            and _contains(
                source_contract,
                "CharacterQualityLevelSource",
                "TryResolveQualityLevelSource",
                "UsesUnsupportedSemantics",
            )
            and _contains(
                core_models,
                "CharacterQualityLevelSemantics",
                "AnchorQualityId",
                "LevelSemantics",
            )
            and _contains(
                core_service,
                "TryBuildQualityLevelSemantics",
                'ReadValue(item, "sourceid")',
                'ReadValue(item, "extra")',
                'ReadValue(item, "sourcename")',
                'ReadValue(item, "qualitytype")',
                "HasUnsafeSavedQualityLevelSemantics",
            )
            and _contains(
                file_resolver,
                "TryResolveQualityLevelSource",
                '"qualities.xml"',
                "safeFields",
                "!safeFields.Contains(element.Name.LocalName)",
            )
        )
        e2e_scripted = (
            creation_fixture.is_file()
            and career_fixture.is_file()
            and _contains(
                e2e_driver,
                '"journey": "quality-level"',
                '"creationIncrease": "pass"',
                '"creationDecrease": "pass"',
                '"careerIncreaseConfirmed": "pass"',
                '"careerDecrease": "pass"',
                '"processRestart": "pass"',
                '"qualityLevelContractSha256"',
                '"sourceResolverContractSha256"',
                '"fileSourceResolverSha256"',
                '"controls": controls',
            )
        )
        return {
            "status": "implemented_pending_emulator" if implemented else "missing",
            "route": "Build > Qualities > selected stable exact Quality > Quality Level",
            "surface": "QualityLevelPage",
            "automationId": "quality-level-value-{stable-quality-guid}",
            "sourceRefs": [
                "src/Chummer.Android/Native/QualityLevelPage.cs",
                "src/Chummer.Android/Native/CollectionEditorPages.cs",
                "src/Chummer.Android/Native/RunnerSessionCoordinator.cs",
                "chummer-presentation/Chummer.Presentation/Overview/QualityLevelEditRequest.cs",
                "chummer-presentation/Chummer.Presentation/Overview/WorkspaceCollectionEditorState.cs",
                "chummer-presentation/Chummer.Presentation/Overview/WorkspaceCollectionEditorProjector.cs",
                "chummer-presentation/Chummer.Presentation/Overview/WorkspaceXmlMutationCatalog.cs",
                "chummer-presentation/Chummer.Presentation/Overview/CharacterOverviewPresenter.WorkspaceMutations.cs",
                "chummer-presentation/Chummer.Presentation/Overview/ICharacterOverviewPresenter.cs",
                "chummer-core-engine/Chummer.Application/Characters/ICharacterSourceDataResolver.cs",
                "chummer-core-engine/Chummer.Contracts/Characters/CharacterSectionModels.cs",
                "chummer-core-engine/Chummer.Infrastructure/Xml/CharacterSectionService.cs",
                "chummer-core-engine/Chummer.Infrastructure/Xml/FileSystemCharacterSourceDataResolver.cs",
            ],
            "presenterMutation": (
                "ICharacterOverviewPresenter.ApplyQualityLevelEditAsync(QualityLevelEditRequest) with stable "
                "anchor Quality Guid, exact SourceID+Extra+SourceName+Type duplicate identity, source-backed "
                "maximum, expected level, and expected content revision"
            ),
            "persistenceAssertion": (
                "selected side-effect-free free quality level adds fresh-Guid duplicate saved qualities or removes "
                "only non-anchor matching levels; Career changes append exact zero-Karma AddQuality or negative "
                "RemoveQuality undo expenses; "
                "sibling qualities and unrelated XML remain exact after revision-checked atomic save, same-session "
                "reopen, and process restart"
            ),
            "e2e": {
                "status": "scripted_not_executed" if e2e_scripted else "missing",
                "ref": "tests/run_api36_quality_level_e2e.py" if e2e_scripted else None,
            },
            "coverageLimit": (
                "Exact Chummer5 Create/Career nudQualityLevel only for Selected, free (saved BP 0), stable-Guid "
                "quality groups whose active qualities.xml profile proves an integer limit and no requirements, "
                "bonus, first-level bonus, natural-weapon, weapon, or selection side effects. Customized notes, "
                "ambiguous identities, paid Career levels, unsupported source semantics, and tablet fail closed."
            ),
            "tablet": {
                "status": "missing",
                "surface": None,
                "automationId": None,
                "sourceRefs": [],
            },
            "tabletE2e": {"status": "missing", "ref": None},
        }
    if class_name == "CharacterCareer" and control in ARMOR_DAMAGE_CONTROLS:
        expected_handler, action, automation_id = ARMOR_DAMAGE_CONTROLS[control]
        legacy_source = (
            presentation_root / "Chummer" / "Forms" / "Character Forms" / "CharacterCareer.cs"
        )
        page = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "ArmorDamagePage.cs"
        editor = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "CollectionEditorPages.cs"
        coordinator = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "RunnerSessionCoordinator.cs"
        e2e_driver = REPO_ROOT / "tests" / "run_api36_armor_damage_e2e.py"
        overview = presentation_root / "Chummer.Presentation" / "Overview"
        request = overview / "ArmorDamageAdjustmentRequest.cs"
        state = overview / "WorkspaceCollectionEditorState.cs"
        projector = overview / "WorkspaceCollectionEditorProjector.cs"
        mutation = overview / "WorkspaceXmlMutationCatalog.cs"
        presenter = overview / "CharacterOverviewPresenter.WorkspaceMutations.cs"
        presenter_interface = overview / "ICharacterOverviewPresenter.cs"
        core_rules = character_notes_core_root / "Chummer.Contracts" / "Characters" / "CharacterArmorDamageRules.cs"
        core_models = character_notes_core_root / "Chummer.Contracts" / "Characters" / "CharacterSectionModels.cs"
        core_service = character_notes_core_root / "Chummer.Infrastructure" / "Xml" / "CharacterSectionService.cs"
        direction_marker = "--objArmor.ArmorDamage" if action == "repair" else "++objArmor.ArmorDamage"
        legacy_exact = (
            any(event.get("handler") == expected_handler for event in legacy.get("events", []))
            and _contains(
                legacy_source,
                expected_handler,
                "is Armor objArmor",
                direction_marker,
                "objArmor.ArmorDamage > 0",
                "objArmor.ArmorDamage < await objArmor.GetTotalArmorAsync",
                "objArmor.GetTotalOverrideArmorAsync",
                "GetArmorDegradationAsync",
            )
        )
        implemented = (
            legacy_exact
            and _contains(
                page,
                "ArmorDamageAdjustmentRequest",
                'AutomationId = $"armor-damage-page-{token}"',
                'AutomationId = $"armor-damage-repair-{token}"',
                'AutomationId = $"armor-damage-degrade-{token}"',
                "_contentRevision",
                "Coordinator.ApplyArmorDamageAdjustmentAsync",
            )
            and _contains(
                editor,
                "item.ArmorDamageAdjustment is not { } armorDamage",
                'Guid.TryParseExact(_target.ItemId, "D"',
                'automationId: $"armor-damage-open-{armorId:N}"',
                "new ArmorDamagePage",
            )
            and _contains(
                coordinator,
                "ApplyArmorDamageAdjustmentAsync",
                "ExpectedContentRevision",
                "_presenter.ApplyArmorDamageAdjustmentAsync",
                "_presenter.SaveAsync",
            )
            and _contains(
                request,
                "ArmorDamageAdjustmentRequest",
                "ExpectedContentRevision",
                "Guid ArmorId",
                "ExpectedArmorDamage",
                "ArmorDamageMaximum",
                "CharacterArmorDamageAdjustment Adjustment",
            )
            and _contains(
                state,
                "WorkspaceArmorDamageAdjustmentState",
                "Guid ArmorId",
                "bool CanRepair",
                "bool CanDegrade",
            )
            and _contains(
                projector,
                'TryReadStrictBool(item, "armorDamageMaximumExact"',
                "CharacterArmorDamageRules.CanRepair",
                "CharacterArmorDamageRules.CanDegrade",
                "ArmorDamageAdjustment = armorDamageAdjustment",
            )
            and _contains(
                mutation,
                "ApplyArmorDamageAdjustment",
                "FindUniqueItemById",
                "TryCalculateArmorDamageMaximum",
                "TryApplyAdjustment",
                "target.Value = updatedDamage.ToString(CultureInfo.InvariantCulture)",
            )
            and _contains(
                presenter,
                "ApplyArmorDamageAdjustmentAsync",
                "ApplyWorkspaceXmlMutationAsync",
                "ExpectedContentRevision",
            )
            and _contains(
                presenter_interface,
                "ApplyArmorDamageAdjustmentAsync",
                "ArmorDamageAdjustmentRequest",
            )
            and _contains(
                core_rules,
                "CharacterArmorDamageAdjustment",
                "TryCalculateMaximum",
                "CanRepair",
                "CanDegrade",
                "TryApplyAdjustment",
            )
            and _contains(
                core_models,
                "CharacterArmorSummary",
                "int ArmorDamage = 0",
                "int ArmorDamageMaximum = 0",
                "bool ArmorDamageMaximumExact = false",
            )
            and _contains(
                core_service,
                "BuildArmorSummary",
                "CharacterArmorDamageRules.TryCalculateMaximum",
                'ReadValue(item, "damage")',
            )
        )
        e2e_scripted = _contains(
            e2e_driver,
            '"journey": "armor-damage"',
            '"careerDegradeEnabledAtZero": "pass"',
            '"careerDegradedProcessRestart": "pass"',
            '"careerRepairedToZero": "pass"',
            '"careerRepairedProcessRestart": "pass"',
            '"controls": controls',
        )
        phone_e2e = armor_damage_phone_e2e_receipt if implemented and e2e_scripted else None
        return {
            "status": (
                "implemented_verified_api36"
                if phone_e2e
                else "implemented_pending_emulator" if implemented else "missing"
            ),
            "route": "Build > Gear > Armor > selected stable Career armor > Armor Condition",
            "surface": "ArmorDamagePage",
            "automationId": automation_id,
            "sourceRefs": [
                "src/Chummer.Android/Native/ArmorDamagePage.cs",
                "src/Chummer.Android/Native/CollectionEditorPages.cs",
                "src/Chummer.Android/Native/RunnerSessionCoordinator.cs",
                "chummer-presentation/Chummer.Presentation/Overview/ArmorDamageAdjustmentRequest.cs",
                "chummer-presentation/Chummer.Presentation/Overview/WorkspaceCollectionEditorState.cs",
                "chummer-presentation/Chummer.Presentation/Overview/WorkspaceCollectionEditorProjector.cs",
                "chummer-presentation/Chummer.Presentation/Overview/WorkspaceXmlMutationCatalog.cs",
                "chummer-presentation/Chummer.Presentation/Overview/CharacterOverviewPresenter.WorkspaceMutations.cs",
                "chummer-presentation/Chummer.Presentation/Overview/ICharacterOverviewPresenter.cs",
                "chummer-core-engine/Chummer.Contracts/Characters/CharacterArmorDamageRules.cs",
                "chummer-core-engine/Chummer.Contracts/Characters/CharacterSectionModels.cs",
                "chummer-core-engine/Chummer.Infrastructure/Xml/CharacterSectionService.cs",
            ],
            "presenterMutation": (
                "ICharacterOverviewPresenter.ApplyArmorDamageAdjustmentAsync(ArmorDamageAdjustmentRequest) "
                f"with CharacterArmorDamageAdjustment.{action.title()}, stable top-level armor Guid, exact "
                "expected damage/maximum, and expected content revision"
            ),
            "persistenceAssertion": (
                f"selected character/armors/armor[stable Guid]/damage changes by exactly one legacy {action} "
                "step inside the exact primary/override half-armor bound; sibling armor and unrelated XML remain "
                "exact after revision-checked atomic save, same-session reopen, and process restart"
            ),
            "e2e": phone_e2e or {
                "status": "scripted_not_executed" if e2e_scripted else "missing",
                "ref": "tests/run_api36_armor_damage_e2e.py" if e2e_scripted else None,
            },
            "coverageLimit": (
                "Exact Chummer5 Career top-level saved armor repair/degrade buttons. Legacy visibility remains "
                "governed by ArmorDegradation; unresolved armor/rating/modifier expressions fail closed, and "
                "tablet is deferred. No matching CharacterCreate controls exist."
            ),
            "tablet": {
                "status": "missing",
                "surface": None,
                "automationId": None,
                "sourceRefs": [],
            },
            "tabletE2e": {"status": "missing", "ref": None},
        }
    if class_name == "CharacterCareer" and control in CYBERWARE_COMMERCE_CONTROLS:
        action, automation_id = CYBERWARE_COMMERCE_CONTROLS[control]
        expected_handler = {
            "tsCyberwareUpgrade": "tsCyberwareUpgrade_Click",
            "tsCyberwareSell": "tsCyberwareSell_Click",
        }[control]
        page = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "CyberwareCommercePage.cs"
        editor = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "CollectionEditorPages.cs"
        coordinator = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "RunnerSessionCoordinator.cs"
        e2e_driver = REPO_ROOT / "tests" / "run_api36_cyberware_commerce_e2e.py"
        overview = presentation_root / "Chummer.Presentation" / "Overview"
        request = overview / "CyberwareCommerceRequest.cs"
        state = overview / "WorkspaceCollectionEditorState.cs"
        projector = overview / "WorkspaceCollectionEditorProjector.cs"
        mutation = overview / "WorkspaceXmlMutationCatalog.cs"
        presenter = overview / "CharacterOverviewPresenter.WorkspaceMutations.cs"
        presenter_interface = overview / "ICharacterOverviewPresenter.cs"
        core_contracts = character_notes_core_root / "Chummer.Contracts" / "Characters"
        core_rules = core_contracts / "CharacterCyberwareCommerceRules.cs"
        core_models = core_contracts / "CharacterSectionModels.cs"
        core_service = character_notes_core_root / "Chummer.Infrastructure" / "Xml" / "CharacterSectionService.cs"
        source_resolver = character_notes_core_root / "Chummer.Application" / "Characters" / "ICharacterSourceDataResolver.cs"
        file_source_resolver = character_notes_core_root / "Chummer.Infrastructure" / "Xml" / "FileSystemCharacterSourceDataResolver.cs"
        implemented = (
            any(event.get("handler") == expected_handler for event in legacy.get("events", []))
            and _contains(
                page,
                "CyberwareCommerceRequest",
                'AutomationId = $"cyberware-commerce-page-{token}"',
                'AutomationId = $"cyberware-commerce-grade-{token}"',
                'AutomationId = $"cyberware-commerce-rating-{token}"',
                'AutomationId = $"cyberware-commerce-refund-percent-{token}"',
                'AutomationId = $"cyberware-commerce-free-cost-{token}"',
                'AutomationId = $"cyberware-commerce-upgrade-{token}"',
                'AutomationId = $"cyberware-commerce-sell-{token}"',
                "CharacterCyberwareCommerceRules.QuoteUpgrade",
                "CharacterCyberwareCommerceRules.QuoteSale",
                '"Confirm Cyberware upgrade"',
                '"Confirm Cyberware sale"',
                "Confirmed: true",
                "QuoteDigest: quote.QuoteDigest",
            )
            and _contains(
                editor,
                "CyberwareCommerceRequired",
                "PrepareCyberwareCommerceEditAsync",
                'automationId: $"cyberware-commerce-open-{cyberwareId:N}"',
                "new CyberwareCommercePage",
            )
            and _contains(
                coordinator,
                "PrepareCyberwareCommerceEditAsync",
                "ApplyCyberwareCommerceEditAsync",
                "ExpectedContentRevision",
                "_presenter.SaveAsync",
            )
            and _contains(
                request,
                "CyberwareCommerceRequest",
                "ExpectedContentRevision",
                "Guid CyberwareId",
                "CharacterCyberwareCommerceAction Action",
                "decimal RefundPercentage",
                "bool Confirmed",
                "string QuoteDigest",
            )
            and _contains(state, "CyberwareCommerceRequired")
            and _contains(projector, "CyberwareCommerceRequired", "careerEditable")
            and _contains(
                mutation,
                "ApplyCyberwareCommerceEdit",
                "CharacterCyberwareCommerceRules.QuoteUpgrade",
                "CharacterCyberwareCommerceRules.QuoteSale",
                "ApplyEssenceBookkeeping",
                "AppendCyberwareExpense",
                'new XElement("nuyentype", "AddGear")',
                "explicit confirmation",
                "quote changed",
            )
            and _contains(
                presenter,
                "PrepareCyberwareCommerceEditAsync",
                "ApplyCyberwareCommerceEditAsync",
                "ApplyWorkspaceXmlMutationAsync",
                "ExpectedContentRevision",
            )
            and _contains(
                presenter_interface,
                "PrepareCyberwareCommerceEditAsync",
                "ApplyCyberwareCommerceEditAsync",
                "CyberwareCommerceRequest",
            )
            and _contains(
                core_rules,
                "TryNormalizeRefundPercentage",
                "TryPlanEssenceHole",
                "QuoteUpgrade",
                "QuoteSale",
                "SHA256.HashData",
            )
            and _contains(core_models, "CharacterCyberwareCommerceSemantics", "CommerceSemantics")
            and _contains(
                core_service,
                "BuildCyberwareCommerceSemantics",
                "Linked Capacity=[*] child Cyberware",
                "SourceEntryUsesGeneratedOrImprovementSemantics",
                "HasExternalSavedReference",
            )
            and _contains(source_resolver, "TryResolveCyberwareCommerceSource")
            and _contains(
                file_source_resolver,
                "TryResolveCyberwareCommerceSource",
                'TryLoadEffectiveDocument(_catalog, "cyberware.xml"',
                'ReadValue(settings, "essenceformat")',
            )
        )
        e2e_scripted = _contains(
            e2e_driver,
            '"journey": "cyberware-commerce"',
            '"tsCyberwareUpgrade"',
            '"tsCyberwareSell"',
            '"controls": controls',
            '"upgradeRatingGradeEconomicsEssenceHole": "pass"',
            '"upgradeLegacyAddGearUndo": "pass"',
            '"saleCancellationZeroMutation": "pass"',
            '"saleConfirmedDeletionCascade": "pass"',
            '"linkedCapacityGuard": "pass"',
            '"processRestart": "pass"',
        )
        return {
            "status": "implemented_pending_emulator" if implemented else "missing",
            "route": "Build > Gear > Cyberwares > selected stable Career Cyberware > Upgrade or Sell",
            "surface": "CyberwareCommercePage",
            "automationId": automation_id,
            "sourceRefs": [
                "src/Chummer.Android/Native/CyberwareCommercePage.cs",
                "src/Chummer.Android/Native/CollectionEditorPages.cs",
                "src/Chummer.Android/Native/RunnerSessionCoordinator.cs",
                "chummer-presentation/Chummer.Presentation/Overview/CyberwareCommerceRequest.cs",
                "chummer-presentation/Chummer.Presentation/Overview/WorkspaceCollectionEditorState.cs",
                "chummer-presentation/Chummer.Presentation/Overview/WorkspaceCollectionEditorProjector.cs",
                "chummer-presentation/Chummer.Presentation/Overview/WorkspaceXmlMutationCatalog.cs",
                "chummer-presentation/Chummer.Presentation/Overview/CharacterOverviewPresenter.WorkspaceMutations.cs",
                "chummer-presentation/Chummer.Presentation/Overview/ICharacterOverviewPresenter.cs",
                "chummer-core-engine/Chummer.Contracts/Characters/CharacterCyberwareCommerceRules.cs",
                "chummer-core-engine/Chummer.Contracts/Characters/CharacterSectionModels.cs",
                "chummer-core-engine/Chummer.Infrastructure/Xml/CharacterSectionService.cs",
                "chummer-core-engine/Chummer.Application/Characters/ICharacterSourceDataResolver.cs",
                "chummer-core-engine/Chummer.Infrastructure/Xml/FileSystemCharacterSourceDataResolver.cs",
            ],
            "presenterMutation": (
                f"ICharacterOverviewPresenter.ApplyCyberwareCommerceEditAsync(CyberwareCommerceRequest.{action}) "
                "with stable Cyberware Guid, Core-owned exact source-backed quote digest, explicit confirmation, "
                "and expected content revision"
            ),
            "persistenceAssertion": (
                "upgrade replays exact rating/grade cost and Essence multipliers, adjusts existing Essence Hole "
                "bookkeeping, applies affordability and the legacy AddGear undo quirk; confirmed sale applies the "
                "exact refund and deletes only the selected complete bounded subtree; cancellation is zero mutation "
                "and unrelated XML remains exact after atomic save, same-session reopen, and process restart"
            ),
            "e2e": {
                "status": "scripted_not_executed" if e2e_scripted else "missing",
                "ref": "tests/run_api36_cyberware_commerce_e2e.py" if e2e_scripted else None,
            },
            "coverageLimit": (
                "Exact simple source-backed CharacterCareer Cyberware Upgrade and Sell only; linked Capacity=[*] "
                "children and unresolved custom/generated/vehicle/modular/improvement/capacity or deletion-cascade "
                "semantics fail closed; no CharacterCreate counterparts exist."
            ),
            "tablet": {
                "status": "missing",
                "surface": None,
                "automationId": None,
                "sourceRefs": [],
            },
            "tabletE2e": {"status": "missing", "ref": None},
        }
    if class_name == "CharacterCareer" and control in GEAR_QUANTITY_CONTROLS:
        action, automation_id = GEAR_QUANTITY_CONTROLS[control]
        expected_handler = {
            "cmdGearIncreaseQty": "cmdGearIncreaseQty_Click",
            "cmdGearReduceQty": "cmdGearReduceQty_Click",
            "cmdGearSplitQty": "cmdGearSplitQty_Click",
            "cmdGearMergeQty": "cmdGearMergeQty_Click",
        }[control]
        page = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "GearQuantityPage.cs"
        editor = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "CollectionEditorPages.cs"
        coordinator = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "RunnerSessionCoordinator.cs"
        e2e_driver = REPO_ROOT / "tests" / "run_api36_gear_quantity_e2e.py"
        overview = presentation_root / "Chummer.Presentation" / "Overview"
        request = overview / "GearQuantityEditRequest.cs"
        state = overview / "WorkspaceCollectionEditorState.cs"
        projector = overview / "WorkspaceCollectionEditorProjector.cs"
        mutation = overview / "WorkspaceXmlMutationCatalog.cs"
        presenter = overview / "CharacterOverviewPresenter.WorkspaceMutations.cs"
        presenter_interface = overview / "ICharacterOverviewPresenter.cs"
        core_contracts = character_notes_core_root / "Chummer.Contracts" / "Characters"
        core_rules = core_contracts / "CharacterGearQuantityRules.cs"
        core_models = core_contracts / "CharacterSectionModels.cs"
        core_service = character_notes_core_root / "Chummer.Infrastructure" / "Xml" / "CharacterSectionService.cs"
        source_resolver = character_notes_core_root / "Chummer.Application" / "Characters" / "ICharacterSourceDataResolver.cs"
        file_source_resolver = character_notes_core_root / "Chummer.Infrastructure" / "Xml" / "FileSystemCharacterSourceDataResolver.cs"
        implemented = (
            any(event.get("handler") == expected_handler for event in legacy.get("events", []))
            and _contains(
                page,
                "GearQuantityEditRequest",
                'AutomationId = $"gear-quantity-page-{targetToken}"',
                'AutomationId = $"gear-quantity-amount-{targetToken}"',
                'AutomationId = $"gear-quantity-merge-target-{targetToken}"',
                'ActionButton("Increase quantity", $"gear-quantity-increase-{targetToken}"',
                'ActionButton("Reduce quantity", $"gear-quantity-reduce-{targetToken}"',
                'ActionButton("Split stack", $"gear-quantity-split-{targetToken}"',
                'ActionButton("Merge stacks", $"gear-quantity-merge-{targetToken}"',
                "CharacterGearQuantityRules.IsValidAmount",
                "DisplayAlertAsync",
                "reductionConfirmed",
            )
            and _contains(
                editor,
                "GearQuantityLifecycleRequired",
                "GearQuantityLifecycle",
                'automationId: $"gear-quantity-open-{lifecycle.GearId:N}"',
                "new GearQuantityPage",
            )
            and _contains(
                coordinator,
                "ApplyGearQuantityEditAsync",
                "ExpectedContentRevision",
                "_presenter.ApplyGearQuantityEditAsync",
                "_presenter.SaveAsync",
            )
            and _contains(
                request,
                "GearQuantityEditRequest",
                "ExpectedContentRevision",
                "Guid GearId",
                "GearQuantityAction Action",
                "decimal Amount",
                "Guid? MergeTargetGearId",
                "bool ReductionConfirmed",
            )
            and _contains(
                state,
                "WorkspaceGearQuantityLifecycleState",
                "WorkspaceGearMergeCandidateState",
                "GearQuantityLifecycleRequired",
            )
            and _contains(
                projector,
                "ProjectGearQuantityLifecycle",
                '"quantitySemantics"',
                "MinimumIncrement",
                "MergeCandidates",
            )
            and _contains(
                mutation,
                "ApplyGearQuantityEdit",
                "ApplyGearQuantityIncrease",
                "ApplyGearQuantityReduction",
                "ApplyGearQuantitySplit",
                "ApplyGearQuantityMerge",
                "CharacterGearQuantityRules.AreIdenticalForMerge",
                "EnsureGearCloneOrRemovalIsIsolated",
                'new XElement("type", "Nuyen")',
                'new XElement("nuyentype", "AddGear")',
                "Guid.NewGuid()",
            )
            and _contains(
                presenter,
                "ApplyGearQuantityEditAsync",
                "ApplyWorkspaceXmlMutationAsync",
                "ExpectedContentRevision",
            )
            and _contains(
                presenter_interface,
                "ApplyGearQuantityEditAsync",
                "GearQuantityEditRequest",
            )
            and _contains(
                core_rules,
                "TryResolvePrecision",
                "AreIdenticalForMerge",
                "TryCalculatePurchaseUnitCost",
            )
            and _contains(core_models, "CharacterGearQuantitySemantics", "MergeCandidateGuids")
            and _contains(
                core_service,
                "BuildGearQuantitySemantics",
                "CharacterGearQuantityRules.AreIdenticalForMerge",
                "TryBuildGearCostSnapshot",
            )
            and _contains(source_resolver, "TryResolveMaxNuyenDecimals")
            and _contains(file_source_resolver, "TryResolveMaxNuyenDecimals", 'ReadValue(settings, "nuyenformat")')
        )
        e2e_scripted = _contains(
            e2e_driver,
            '"journey": "gear-quantity-lifecycle"',
            '"cmdGearIncreaseQty"',
            '"cmdGearReduceQty"',
            '"cmdGearSplitQty"',
            '"cmdGearMergeQty"',
            '"controls": controls',
            '"increasePurchaseExpense": "pass"',
            '"reduceConfirmed": "pass"',
            '"splitClonePreserved": "pass"',
            '"mergeIdentityExact": "pass"',
            '"processRestart": "pass"',
        )
        return {
            "status": "implemented_pending_emulator" if implemented else "missing",
            "route": "Build > Gear > Gear > selected stable Career Gear > Gear Quantity",
            "surface": "GearQuantityPage",
            "automationId": automation_id,
            "sourceRefs": [
                "src/Chummer.Android/Native/GearQuantityPage.cs",
                "src/Chummer.Android/Native/CollectionEditorPages.cs",
                "src/Chummer.Android/Native/RunnerSessionCoordinator.cs",
                "chummer-presentation/Chummer.Presentation/Overview/GearQuantityEditRequest.cs",
                "chummer-presentation/Chummer.Presentation/Overview/WorkspaceCollectionEditorState.cs",
                "chummer-presentation/Chummer.Presentation/Overview/WorkspaceCollectionEditorProjector.cs",
                "chummer-presentation/Chummer.Presentation/Overview/WorkspaceXmlMutationCatalog.cs",
                "chummer-presentation/Chummer.Presentation/Overview/CharacterOverviewPresenter.WorkspaceMutations.cs",
                "chummer-presentation/Chummer.Presentation/Overview/ICharacterOverviewPresenter.cs",
                "chummer-core-engine/Chummer.Contracts/Characters/CharacterGearQuantityRules.cs",
                "chummer-core-engine/Chummer.Contracts/Characters/CharacterSectionModels.cs",
                "chummer-core-engine/Chummer.Infrastructure/Xml/CharacterSectionService.cs",
                "chummer-core-engine/Chummer.Application/Characters/ICharacterSourceDataResolver.cs",
                "chummer-core-engine/Chummer.Infrastructure/Xml/FileSystemCharacterSourceDataResolver.cs",
            ],
            "presenterMutation": (
                f"ICharacterOverviewPresenter.ApplyGearQuantityEditAsync(GearQuantityEditRequest.{action}) "
                "with stable top-level gear Guid, exact Core-projected precision/merge/cost authority, "
                "and expected content revision"
            ),
            "persistenceAssertion": (
                "exact decimal quantity is increased, confirmed-reduced/deleted, split into a deep clone with "
                "fresh recursive Gear GUIDs, or merged only under exact IsIdenticalToOtherGear-compatible identity; "
                "career increase subtracts exact Nuyen and appends its expense/undo; unrelated saved XML remains "
                "exact after atomic save, same-session reopen, and process restart"
            ),
            "e2e": {
                "status": "scripted_not_executed" if e2e_scripted else "missing",
                "ref": "tests/run_api36_gear_quantity_e2e.py" if e2e_scripted else None,
            },
            "coverageLimit": (
                "Exact Chummer5 top-level CharacterCareer Gear stack quantity lifecycle only; no CharacterCreate "
                "counterparts exist; nested gear, linked/generated-weapon Gear, external saved-data references, "
                "or unsupported source/settings data fail closed."
            ),
            "tablet": {
                "status": "missing",
                "surface": None,
                "automationId": None,
                "sourceRefs": [],
            },
            "tabletE2e": {"status": "missing", "ref": None},
        }
    if (
        class_name in {"CharacterCreate", "CharacterCareer"}
        and control in ARMOR_EQUIPMENT_CONTROLS
    ):
        expected_handler, action, automation_id = ARMOR_EQUIPMENT_CONTROLS[control]
        legacy_source = (
            presentation_root / "Chummer" / "Forms" / "Character Forms" / f"{class_name}.cs"
        )
        legacy_designer = (
            presentation_root / "Chummer" / "Forms" / "Character Forms" / f"{class_name}.Designer.cs"
        )
        page = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "ArmorEquipmentPage.cs"
        editor = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "CollectionEditorPages.cs"
        coordinator = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "RunnerSessionCoordinator.cs"
        e2e_driver = REPO_ROOT / "tests" / "run_api36_armor_equipment_e2e.py"
        overview = presentation_root / "Chummer.Presentation" / "Overview"
        request = overview / "ArmorEquipmentEditRequest.cs"
        state = overview / "WorkspaceCollectionEditorState.cs"
        projector = overview / "WorkspaceCollectionEditorProjector.cs"
        mutation = overview / "WorkspaceXmlMutationCatalog.cs"
        presenter = overview / "CharacterOverviewPresenter.WorkspaceMutations.cs"
        presenter_interface = overview / "ICharacterOverviewPresenter.cs"
        contracts = character_notes_core_root / "Chummer.Contracts" / "Characters"
        core_rules = contracts / "CharacterArmorEquipmentRules.cs"
        core_models = contracts / "CharacterSectionModels.cs"
        core_service = character_notes_core_root / "Chummer.Infrastructure" / "Xml" / "CharacterSectionService.cs"
        handler_markers = (
            ("case Armor objArmor:", "objArmor.SetEquippedAsync(blnChecked")
            if control == "chkArmorEquipped"
            else (
                "CharacterObject.Armor.ForEachWithSideEffectsAsync",
                "objArmor.SetEquippedAsync(true" if control == "cmdArmorEquipAll" else "objArmor.SetEquippedAsync(false",
            )
        )
        legacy_exact = (
            any(event.get("handler") == expected_handler for event in legacy.get("events", []))
            and _contains(legacy_source, expected_handler, *handler_markers)
            and _contains(legacy_designer, f"this.{control}")
        )
        implemented = (
            legacy_exact
            and _contains(
                page,
                "ArmorEquipmentEditRequest",
                'AutomationId = $"armor-equipment-page-{token}"',
                'AutomationId = $"armor-equipment-toggle-{token}"',
                'AutomationId = $"armor-equipment-equip-all-{token}"',
                'AutomationId = $"armor-equipment-unequip-all-{token}"',
                "_contentRevision",
                "Coordinator.ApplyArmorEquipmentEditAsync",
            )
            and _contains(
                editor,
                "item.ArmorEquipment is not { } equipment",
                'Guid.TryParseExact(_target.ItemId, "D"',
                'automationId: $"armor-equipment-open-{armorId:N}"',
                "new ArmorEquipmentPage",
            )
            and _contains(
                coordinator,
                "ApplyArmorEquipmentEditAsync",
                "ExpectedContentRevision",
                "_presenter.ApplyArmorEquipmentEditAsync",
                "_presenter.SaveAsync",
            )
            and _contains(
                request,
                "ArmorEquipmentEditRequest",
                "ExpectedContentRevision",
                "Guid ArmorId",
                "ExpectedArmorCount",
                "ExpectedEquippedCount",
                "CharacterArmorEquipmentAction Action",
            )
            and _contains(state, "CharacterArmorEquipmentState? ArmorEquipment")
            and _contains(
                projector,
                "ProjectArmorEquipment",
                'TryReadStrictBool(armor, "equippedExact"',
                "CharacterArmorEquipmentRules.TryProject",
                "WorkspaceCollectionKind.Armor => [WorkspaceCollectionToggleField.WirelessEnabled]",
            )
            and _contains(
                mutation,
                "ApplyArmorEquipmentEdit",
                "CharacterArmorEquipmentRules.TryProject",
                "CharacterArmorEquipmentRules.CanApply",
                "CharacterArmorEquipmentRules.ResolveEquipped",
                'equipped.Value = updated ? "True" : "False"',
            )
            and _contains(
                presenter,
                "ApplyArmorEquipmentEditAsync",
                "ApplyWorkspaceXmlMutationAsync",
                "ExpectedContentRevision",
            )
            and _contains(
                presenter_interface,
                "ApplyArmorEquipmentEditAsync",
                "ArmorEquipmentEditRequest",
            )
            and _contains(
                core_rules,
                "CharacterArmorEquipmentAction",
                "CharacterArmorEquipmentBasis",
                "CharacterArmorEquipmentState",
                "TryProject",
                "CanApply",
                "ResolveEquipped",
            )
            and _contains(core_models, "CharacterArmorSummary", "bool EquippedExact = false")
            and _contains(core_service, "bool equippedExact = bool.TryParse", "EquippedExact: equippedExact")
        )
        e2e_scripted = _contains(
            e2e_driver,
            '"journey": "armor-equipment"',
            '"chkArmorEquipped"',
            '"cmdArmorEquipAll"',
            '"cmdArmorUnEquipAll"',
            '"AllUnequippedProcessRestart"',
            '"AllEquippedProcessRestart"',
            '"controls": controls',
        )
        phone_e2e = armor_equipment_phone_e2e_receipt if implemented and e2e_scripted else None
        return {
            "status": (
                "implemented_verified_api36"
                if phone_e2e
                else "implemented_pending_emulator" if implemented else "missing"
            ),
            "route": "Build > Gear > Armor > selected stable armor > Armor Equipment",
            "surface": "ArmorEquipmentPage",
            "automationId": automation_id,
            "sourceRefs": [
                "src/Chummer.Android/Native/ArmorEquipmentPage.cs",
                "src/Chummer.Android/Native/CollectionEditorPages.cs",
                "src/Chummer.Android/Native/RunnerSessionCoordinator.cs",
                "chummer-presentation/Chummer.Presentation/Overview/ArmorEquipmentEditRequest.cs",
                "chummer-presentation/Chummer.Presentation/Overview/WorkspaceCollectionEditorState.cs",
                "chummer-presentation/Chummer.Presentation/Overview/WorkspaceCollectionEditorProjector.cs",
                "chummer-presentation/Chummer.Presentation/Overview/WorkspaceXmlMutationCatalog.cs",
                "chummer-presentation/Chummer.Presentation/Overview/CharacterOverviewPresenter.WorkspaceMutations.cs",
                "chummer-presentation/Chummer.Presentation/Overview/ICharacterOverviewPresenter.cs",
                "chummer-core-engine/Chummer.Contracts/Characters/CharacterArmorEquipmentRules.cs",
                "chummer-core-engine/Chummer.Contracts/Characters/CharacterSectionModels.cs",
                "chummer-core-engine/Chummer.Infrastructure/Xml/CharacterSectionService.cs",
            ],
            "presenterMutation": (
                f"ICharacterOverviewPresenter.ApplyArmorEquipmentEditAsync({action}) with stable top-level "
                "armor Guid, exact expected selected/count aggregate, and expected content revision"
            ),
            "persistenceAssertion": (
                "selected or all top-level character/armors/armor/equipped values change exactly while nested "
                "armor-mod/gear equipped flags, sibling fields, and unrelated XML remain exact after "
                "revision-checked atomic save, reopen, and process restart"
            ),
            "e2e": phone_e2e or {
                "status": "scripted_not_executed" if e2e_scripted else "missing",
                "ref": "tests/run_api36_armor_equipment_e2e.py" if e2e_scripted else None,
            },
            "coverageLimit": (
                "Exact top-level Armor equipped checkbox and the equip-all/unequip-all handler's all-armors "
                "branch reached from selected Armor in both modes. Legacy Create bulk controls are Designer-hidden; "
                "legacy location/root selection scopes and ArmorMod/Gear branches remain separately inventoried."
            ),
            "tablet": {"status": "missing", "surface": None, "automationId": None, "sourceRefs": []},
            "tabletE2e": {"status": "missing", "ref": None},
        }
    if control in LEGACY_CHARACTER_COLLECTION_TOGGLE_CONTROLS:
        kind, section_label, field, xml_element, supported_forms = (
            LEGACY_CHARACTER_COLLECTION_TOGGLE_CONTROLS[control]
        )
        if class_name not in supported_forms:
            return None

        phone_page = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "CollectionEditorPages.cs"
        phone_route = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "BuildFlowPages.cs"
        coordinator = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "RunnerSessionCoordinator.cs"
        request = presentation_root / "Chummer.Presentation" / "Overview" / "WorkspaceCollectionMutationRequest.cs"
        mutation = presentation_root / "Chummer.Presentation" / "Overview" / "WorkspaceXmlMutationCatalog.cs"
        projector = presentation_root / "Chummer.Presentation" / "Overview" / "WorkspaceCollectionEditorProjector.cs"
        presenter = presentation_root / "Chummer.Presentation" / "Overview" / "CharacterOverviewPresenter.WorkspaceMutations.cs"
        shared = (
            _contains(
                request,
                "WorkspaceCollectionItemTarget",
                "WorkspacePatchCollectionItemRequest",
                "WorkspaceCollectionToggleField",
                f"    {field},",
            )
            and _contains(projector, f"WorkspaceCollectionKind.{kind}", f"WorkspaceCollectionToggleField.{field}")
            and _contains(
                mutation,
                "ResolveToggleElementName",
                f"WorkspaceCollectionToggleField.{field}",
                f'=> "{xml_element}"',
            )
            and _contains(coordinator, "ApplyCollectionMutationAsync")
            and _contains(presenter, "ApplyCollectionMutationAsync", "ApplyWorkspaceXmlMutationAsync")
        )
        phone_implemented = shared and _contains(
            phone_route,
            "AddCollectionRows",
            "CollectionItemEditorPage",
        ) and _contains(
            phone_page,
            "AddToggle",
            "collection-toggle-",
            "value.IsEnabled",
            "WorkspacePatchCollectionItemRequest",
        )
        return {
            "status": "implemented_pending_emulator" if phone_implemented else "missing",
            "route": f"Build > {section_label} > selected item > {field}",
            "surface": "CollectionItemEditorPage",
            "automationId": f"collection-toggle-{field.lower()}-{{stable-target}}",
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
                f"WorkspaceCollectionToggleField.{field} on WorkspaceCollectionKind.{kind}"
            ),
            "persistenceAssertion": (
                f"selected stable {kind} guid retains {xml_element} after save, reopen, and process restart"
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
    if control in LEGACY_CHARACTER_COLLECTION_TEXT_CONTROLS:
        kind, section_label, field, xml_element, expected_handler, supported_forms = (
            LEGACY_CHARACTER_COLLECTION_TEXT_CONTROLS[control]
        )
        if class_name not in supported_forms:
            return None
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
            _contains(
                request,
                "WorkspaceCollectionItemTarget",
                "WorkspacePatchCollectionItemRequest",
                "WorkspaceCollectionTextField",
                f"    {field}",
            )
            and _contains(projector, f"WorkspaceCollectionKind.{kind}", f"WorkspaceCollectionTextField.{field}")
            and _contains(
                mutation,
                "ApplyTextMutation",
                f"WorkspaceCollectionTextField.{field}",
                xml_element,
            )
            and _contains(coordinator, "ApplyCollectionMutationAsync")
            and _contains(presenter, "ApplyCollectionMutationAsync", "ApplyWorkspaceXmlMutationAsync")
        )
        phone_implemented = shared and _contains(
            phone_route,
            "AddCollectionRows",
            "CollectionItemEditorPage",
        ) and _contains(
            phone_page,
            "AddTextField",
            "collection-field-",
            "value.MaximumLength",
            "original.IsEnabled",
            "WorkspacePatchCollectionItemRequest",
        )
        return {
            "status": "implemented_pending_emulator" if phone_implemented else "missing",
            "route": f"Build > {section_label} > selected item > {field}",
            "surface": "CollectionItemEditorPage",
            "automationId": f"collection-field-{field.lower()}-{{stable-target}}",
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
                f"WorkspaceCollectionTextField.{field} on WorkspaceCollectionKind.{kind}"
            ),
            "persistenceAssertion": (
                f"selected stable {kind} guid retains {xml_element} after save, reopen, and process restart"
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
    if (
        class_name in {"CharacterCreate", "CharacterCareer"}
        and control in LEGACY_NESTED_COLLECTION_NOTES_CONTROLS
    ):
        (
            kind,
            nested_kind,
            section_label,
            section_id,
            expected_handler,
            form_names,
        ) = LEGACY_NESTED_COLLECTION_NOTES_CONTROLS[control]
        if class_name not in form_names or not any(
            event.get("handler") == expected_handler for event in legacy.get("events", [])
        ):
            return None

        phone_page = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "CollectionEditorPages.cs"
        phone_route = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "BuildFlowPages.cs"
        coordinator = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "RunnerSessionCoordinator.cs"
        e2e_driver = REPO_ROOT / "tests" / "run_api36_nested_collection_notes_e2e.py"
        request = presentation_root / "Chummer.Presentation" / "Overview" / "WorkspaceCollectionMutationRequest.cs"
        mutation = presentation_root / "Chummer.Presentation" / "Overview" / "WorkspaceXmlMutationCatalog.cs"
        projector = presentation_root / "Chummer.Presentation" / "Overview" / "WorkspaceCollectionEditorProjector.cs"
        presenter = presentation_root / "Chummer.Presentation" / "Overview" / "CharacterOverviewPresenter.WorkspaceMutations.cs"
        core_sections = character_notes_core_root / "Chummer.Infrastructure" / "Xml" / "CharacterSectionService.cs"
        core_actions = (
            character_notes_core_root
            / "Chummer.Rulesets.Hosting"
            / "Presentation"
            / "WorkspaceSurfaceActionCatalog.cs"
        )
        projector_target = (
            _contains(
                projector,
                "schema.Kind == WorkspaceCollectionKind.Gear",
                "WorkspaceNestedCollectionKind.Gear",
                'ReadText(item, "parentGuid")',
            )
            if nested_kind == "Gear"
            else _contains(
                projector,
                f'"{section_id}" => new(',
                f"WorkspaceCollectionKind.{kind}",
                f"WorkspaceNestedCollectionKind.{nested_kind}",
            )
        )
        core_projection = {
            "Gear": _contains(
                core_sections,
                "CharacterGearSection ParseGear",
                "FlattenGearSummary",
                "ParentGuid: parentGuid",
                "Notes: ReadValue(item, \"notes\")",
            ),
            "WeaponAccessory": _contains(
                core_sections,
                "CharacterWeaponAccessoriesSection ParseWeaponAccessories",
                "AccessoryGuid: ReadValue(accessory, \"guid\")",
                "Notes: ReadValue(accessory, \"notes\")",
            ),
            "ArmorMod": _contains(
                core_sections,
                "CharacterArmorModsSection ParseArmorMods",
                "ModGuid: ReadValue(mod, \"guid\")",
                "Notes: ReadValue(mod, \"notes\")",
            ),
        }.get(nested_kind, False)
        shared = (
            _contains(
                request,
                "WorkspaceCollectionItemTarget",
                "WorkspacePatchCollectionItemRequest",
                "WorkspaceNestedCollectionKind",
                f"    {nested_kind}",
                "WorkspaceCollectionTextField",
                "    Notes,",
            )
            and projector_target
            and _contains(
                projector,
                "if (schema.NestedKind is not null)",
                "WorkspaceCollectionTextField.Notes",
            )
            and _contains(
                mutation,
                f"(WorkspaceCollectionKind.{kind}, WorkspaceNestedCollectionKind.{nested_kind})",
                'WorkspaceCollectionTextField.Notes => "notes"',
                "FindUniqueItemById",
            )
            and core_projection
            and _contains(
                core_actions,
                '"tab-gear"',
                f'"{section_id}"',
                "WorkspaceSurfaceActionKind.Section",
            )
            and _contains(coordinator, "ApplyCollectionMutationAsync")
            and _contains(presenter, "ApplyCollectionMutationAsync", "ApplyWorkspaceXmlMutationAsync")
        )
        phone_implemented = shared and _contains(
            phone_route,
            "AddCollectionRows",
            "CollectionItemEditorPage",
        ) and _contains(
            phone_page,
            "collection-field-",
            "WorkspaceCollectionTextField.Notes",
            "NativeTheme.TextArea",
            "value.MaximumLength",
            "WorkspacePatchCollectionItemRequest",
        )
        e2e_scripted = _contains(
            e2e_driver,
            '"journey": "nested-collection-notes"',
            '"allCreationNestedNotesEdited": "pass"',
            '"creationWorkspaceXmlPersisted": "pass"',
            '"creationProcessRestartUiReadback": "pass"',
            '"allCareerNestedNotesEdited": "pass"',
            '"careerWorkspaceXmlPersisted": "pass"',
            '"careerProcessRestartUiReadback": "pass"',
            '"controls": control_proofs',
        )
        phone_e2e = nested_collection_notes_phone_e2e_receipt if phone_implemented and e2e_scripted else None
        return {
            "status": "implemented_verified_api36" if phone_e2e else "implemented_pending_emulator" if phone_implemented else "missing",
            "route": f"Build > {section_label} > selected nested item > Notes",
            "surface": "CollectionItemEditorPage",
            "automationId": "collection-field-notes-{stable-nested-target}",
            "sourceRefs": [
                "src/Chummer.Android/Native/BuildFlowPages.cs",
                "src/Chummer.Android/Native/CollectionEditorPages.cs",
                "src/Chummer.Android/Native/RunnerSessionCoordinator.cs",
                "chummer-core-engine/Chummer.Infrastructure/Xml/CharacterSectionService.cs",
                "chummer-core-engine/Chummer.Rulesets.Hosting/Presentation/WorkspaceSurfaceActionCatalog.cs",
                "chummer-presentation/Chummer.Presentation/Overview/WorkspaceCollectionEditorProjector.cs",
                "chummer-presentation/Chummer.Presentation/Overview/WorkspaceCollectionMutationRequest.cs",
                "chummer-presentation/Chummer.Presentation/Overview/WorkspaceXmlMutationCatalog.cs",
            ],
            "presenterMutation": (
                "ICharacterOverviewPresenter.ApplyCollectionMutationAsync / "
                f"WorkspaceCollectionTextField.Notes on WorkspaceCollectionKind.{kind} / "
                f"WorkspaceNestedCollectionKind.{nested_kind}"
            ),
            "persistenceAssertion": (
                f"selected stable {kind}/{nested_kind} parent+child guid pair retains notes "
                "after save, reopen, and process restart"
            ),
            "e2e": phone_e2e or {
                "status": "scripted_not_executed" if e2e_scripted else "missing",
                "ref": "tests/run_api36_nested_collection_notes_e2e.py" if e2e_scripted else None,
            },
            "tablet": {
                "status": "missing",
                "surface": None,
                "automationId": None,
                "sourceRefs": [],
            },
            "tabletE2e": {"status": "missing", "ref": None},
        }
    if (
        class_name in {"CharacterCreate", "CharacterCareer"}
        and control in LEGACY_CHARACTER_COLLECTION_NOTES_CONTROLS
    ):
        kind, section_label, expected_handler = LEGACY_CHARACTER_COLLECTION_NOTES_CONTROLS[control]
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
            _contains(
                request,
                "WorkspaceCollectionItemTarget",
                "WorkspacePatchCollectionItemRequest",
                "WorkspaceCollectionTextField",
                "    Notes,",
            )
            and _contains(projector, f"WorkspaceCollectionKind.{kind}", "WorkspaceCollectionTextField.Notes")
            and _contains(mutation, "ApplyTextMutation", 'WorkspaceCollectionTextField.Notes => "notes"')
            and _contains(coordinator, "ApplyCollectionMutationAsync")
            and _contains(presenter, "ApplyCollectionMutationAsync", "ApplyWorkspaceXmlMutationAsync")
        )
        phone_implemented = shared and _contains(
            phone_route,
            "AddCollectionRows",
            "CollectionItemEditorPage",
        ) and _contains(
            phone_page,
            "collection-field-",
            "WorkspaceCollectionTextField.Notes",
            "NativeTheme.TextArea",
            "value.MaximumLength",
            "WorkspacePatchCollectionItemRequest",
        )
        return {
            "status": "implemented_pending_emulator" if phone_implemented else "missing",
            "route": f"Build > {section_label} > selected item > Notes",
            "surface": "CollectionItemEditorPage",
            "automationId": "collection-field-notes-{stable-target}",
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
                f"WorkspaceCollectionTextField.Notes on WorkspaceCollectionKind.{kind}"
            ),
            "persistenceAssertion": (
                f"selected stable {kind} guid retains notes after save, reopen, and process restart"
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
    if (
        class_name in {"CharacterCreate", "CharacterCareer"}
        and control in LEGACY_CHARACTER_COLLECTION_DELETE_CONTROLS
    ):
        kind, section_label = LEGACY_CHARACTER_COLLECTION_DELETE_CONTROLS[control]
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
        core_models = character_notes_core_root / "Chummer.Contracts" / "Characters" / "CharacterSectionModels.cs"
        core_parser = character_notes_core_root / "Chummer.Infrastructure" / "Xml" / "CharacterSectionService.cs"
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
    if class_name == "SustainedObjectControl" and control in SUSTAINED_EFFECTS_CONTROLS:
        native_root = REPO_ROOT / "src" / "Chummer.Android" / "Native"
        phone_page = native_root / "SustainedObjectsPage.cs"
        build_page = native_root / "BuildPage.cs"
        coordinator = native_root / "RunnerSessionCoordinator.cs"
        driver = REPO_ROOT / "tests" / "run_api36_sustained_effects_e2e.py"
        creation_fixture = REPO_ROOT / "tests" / "fixtures" / "creation-sustained-effects-e2e.chum5"
        career_fixture = REPO_ROOT / "tests" / "fixtures" / "career-sustained-effects-e2e.chum5"
        legacy_control = chummer5_root / "Chummer" / "Controls" / "Shared" / "SustainedObjectControl.cs"
        legacy_designer = chummer5_root / "Chummer" / "Controls" / "Shared" / "SustainedObjectControl.Designer.cs"
        legacy_shared = chummer5_root / "Chummer" / "Forms" / "Character Forms" / "CharacterShared.cs"
        overview = presentation_root / "Chummer.Presentation" / "Overview"
        request = overview / "SustainedObjectEditRequest.cs"
        mutation = overview / "WorkspaceXmlMutationCatalog.cs"
        presenter = overview / "CharacterOverviewPresenter.WorkspaceMutations.cs"
        presenter_interface = overview / "ICharacterOverviewPresenter.cs"
        presenter_persistence = overview / "CharacterOverviewPresenter.Persistence.cs"
        core_rules = character_notes_core_root / "Chummer.Contracts" / "Characters" / "CharacterSustainedObjectRules.cs"
        workspace_store = character_notes_core_root / "Chummer.Infrastructure" / "Workspaces" / "FileWorkspaceStore.cs"
        legacy_exact = (
            _contains(
                legacy_control,
                "RegisterAsyncDataBindingAsync",
                "nameof(SustainedObject.Force)",
                "nameof(SustainedObject.NetHits)",
                "nameof(SustainedObject.SelfSustained)",
                "Improvement.ImprovementSource.CritterPower",
                "UnsustainObject.Invoke",
            )
            and _contains(
                legacy_designer,
                "this.nudForce.Maximum",
                "this.nudNetHits.Maximum",
                "this.cmdDelete.Click",
                "this.chkSelfSustained.CheckedChanged",
            )
            and _contains(
                legacy_shared,
                "DeleteSustainedObject",
                "ConfirmDeleteAsync",
                "CharacterObject.SustainedCollection.RemoveAsync",
            )
        )
        implemented = (
            legacy_exact
            and _contains(
                phone_page,
                "class SustainedObjectsPage",
                "class SustainedObjectEditPage",
                "CharacterSustainedObjectIdentity",
                "sustained-effect-force-",
                "sustained-effect-net-hits-",
                "sustained-effect-self-",
                "sustained-effect-delete-",
                "CharacterSustainedObjectAction.Update",
                "CharacterSustainedObjectAction.Delete",
                "Confirmed: true",
                "_contentRevision",
            )
            and _contains(
                build_page,
                'automationId: "build-sustained-effects"',
                "PrepareSustainedObjectsEditAsync",
                "new SustainedObjectsPage",
            )
            and _contains(
                coordinator,
                "PrepareSustainedObjectsEditAsync",
                "ApplySustainedObjectEditAsync",
                "ExpectedContentRevision",
                "_presenter.SaveAsync",
            )
            and _contains(
                request,
                "CharacterSustainedObjectIdentity",
                "SustainedObjectsEditorState",
                "SustainedObjectEditRequest",
                "TryProjectAll",
                'Elements("sustainedobjects")',
                'Elements("sustainedobject")',
                "Occurrence",
            )
            and _contains(
                mutation,
                "ApplySustainedObjectEdit",
                "CharacterSustainedObjectRules.CanUpdate",
                "CharacterSustainedObjectRules.CanDelete",
                'SetElementValue(target.Element, "force"',
                'SetElementValue(target.Element, "nethits"',
                'SetElementValue(target.Element, "self"',
                "target.Element.Remove()",
            )
            and _contains(
                presenter,
                "PrepareSustainedObjectsEditAsync",
                "ApplySustainedObjectEditAsync",
                "ApplyWorkspaceXmlMutationAsync",
            )
            and _contains(
                presenter_interface,
                "PrepareSustainedObjectsEditAsync",
                "ApplySustainedObjectEditAsync",
            )
            and _contains(
                presenter_persistence,
                "SaveAsync",
                "expectedContentRevision",
                "TryBeginCaptureIntent",
                "_workspacePersistenceService.SaveAsync",
            )
            and _contains(
                core_rules,
                "CharacterSustainedObjectIdentity",
                "MinimumForce = 0",
                "MaximumForce = 100",
                "MinimumNetHits = 0",
                "MaximumNetHits = 100",
                "CritterPower",
            )
            and _contains(
                workspace_store,
                "expectedContentRevision",
                "Flush(flushToDisk: true)",
                "File.Replace",
                "File.Move",
            )
        )
        e2e_scripted = (
            _contains(
                driver,
                '"journey": "sustained-effects"',
                'api != "36"',
                '"profile": "phone"',
                '"linkedTypeGuidOccurrenceIdentity"',
                '"critterPowerSelfSustainedHidden"',
                '"sustainedEffectsRulesSha256"',
                '"presenterPersistenceSha256"',
                '"workspaceStoreSha256"',
                '"creationFixtureSha256"',
                '"careerFixtureSha256"',
            )
            and creation_fixture.is_file()
            and career_fixture.is_file()
        )
        phone_e2e = (
            _validated_sustained_effects_phone_e2e_receipt(
                presentation_root,
                character_notes_core_root,
            )
            if implemented and e2e_scripted
            else None
        )
        return {
            "status": (
                "implemented_verified_api36"
                if phone_e2e
                else "implemented_pending_emulator" if implemented else "missing"
            ),
            "route": "Build > Runner > Sustained effects > selected saved occurrence",
            "surface": "SustainedObjectEditPage",
            "automationId": SUSTAINED_EFFECTS_CONTROLS[control],
            "sourceRefs": [
                "src/Chummer.Android/Native/SustainedObjectsPage.cs",
                "src/Chummer.Android/Native/BuildPage.cs",
                "src/Chummer.Android/Native/RunnerSessionCoordinator.cs",
                "chummer-presentation/Chummer.Presentation/Overview/SustainedObjectEditRequest.cs",
                "chummer-presentation/Chummer.Presentation/Overview/WorkspaceXmlMutationCatalog.cs",
                "chummer-presentation/Chummer.Presentation/Overview/CharacterOverviewPresenter.WorkspaceMutations.cs",
                "chummer-presentation/Chummer.Presentation/Overview/CharacterOverviewPresenter.Persistence.cs",
                "chummer-presentation/Chummer.Presentation/Overview/ICharacterOverviewPresenter.cs",
                "chummer-core-engine/Chummer.Contracts/Characters/CharacterSustainedObjectRules.cs",
                "chummer-core-engine/Chummer.Infrastructure/Workspaces/FileWorkspaceStore.cs",
                "tests/run_api36_sustained_effects_e2e.py",
                "tests/fixtures/creation-sustained-effects-e2e.chum5",
                "tests/fixtures/career-sustained-effects-e2e.chum5",
            ],
            "presenterMutation": (
                "ICharacterOverviewPresenter.ApplySustainedObjectEditAsync / SustainedObjectEditRequest "
                "with linked type + linked GUID + saved occurrence and expected content revision"
            ),
            "persistenceAssertion": (
                "the exact duplicate occurrence retains 0-100 Force and Net Hits plus conditional Self-Sustained; "
                "confirmed delete removes only the selected occurrence; unrelated XML remains exact after atomic "
                "save, same-session reopen, recovery, and process restart"
            ),
            "coverageLimit": (
                "One shared Chummer5 SustainedObjectControl row reaches Create and Career. Self-Sustained is "
                "intentionally absent for Critter Powers; identity is the persisted linked type/GUID/occurrence "
                "because Chummer5 does not save the control's runtime GUID."
            ),
            "e2e": phone_e2e or {
                "status": "scripted_not_executed" if e2e_scripted else "missing",
                "ref": "tests/run_api36_sustained_effects_e2e.py" if e2e_scripted else None,
            },
            "tablet": {
                "status": "missing",
                "surface": None,
                "automationId": None,
                "sourceRefs": [],
            },
            "tabletE2e": {"status": "missing", "ref": None},
        }
    if class_name == "SpiritControl" and control == "chkFettered":
        native_root = REPO_ROOT / "src" / "Chummer.Android" / "Native"
        phone_page = native_root / "SpiritFetteredPage.cs"
        collection_page = native_root / "CollectionEditorPages.cs"
        coordinator = native_root / "RunnerSessionCoordinator.cs"
        driver = REPO_ROOT / "tests" / "run_api36_spirit_fettered_e2e.py"
        creation_fixture = REPO_ROOT / "tests" / "fixtures" / "creation-spirit-fettered-e2e.chum5"
        career_fixture = REPO_ROOT / "tests" / "fixtures" / "career-spirit-fettered-e2e.chum5"
        overview = presentation_root / "Chummer.Presentation" / "Overview"
        request = overview / "SpiritFetteredEditRequest.cs"
        state = overview / "WorkspaceCollectionEditorState.cs"
        projector = overview / "WorkspaceCollectionEditorProjector.cs"
        mutation = overview / "WorkspaceXmlMutationCatalog.cs"
        presenter = overview / "CharacterOverviewPresenter.WorkspaceMutations.cs"
        presenter_interface = overview / "ICharacterOverviewPresenter.cs"
        presenter_persistence = overview / "CharacterOverviewPresenter.Persistence.cs"
        core_contracts = character_notes_core_root / "Chummer.Contracts" / "Characters"
        core_rules = core_contracts / "CharacterSpiritFetteringRules.cs"
        core_models = core_contracts / "CharacterSectionModels.cs"
        core_parser = character_notes_core_root / "Chummer.Infrastructure" / "Xml" / "CharacterSectionService.cs"
        workspace_store = character_notes_core_root / "Chummer.Infrastructure" / "Workspaces" / "FileWorkspaceStore.cs"
        phone_implemented = (
            _contains(
                phone_page,
                "class SpiritFetteredPage",
                "CharacterSpiritFetteringState",
                "spirit-fettered-toggle-",
                "spirit-fettered-save-",
                "_contentRevision",
            )
            and _contains(
                collection_page,
                "AddSpiritFetteredAction",
                "spirit-fettered-open-",
                "WorkspaceCollectionKind.Spirit",
            )
            and _contains(
                coordinator,
                "ApplySpiritFetteredEditAsync",
                "ExpectedContentRevision",
                "_presenter.SaveAsync",
            )
            and _contains(
                request,
                "SpiritFetteredEditRequest",
                "CharacterSpiritFetteringState ExpectedState",
            )
            and _contains(
                state,
                "CharacterSpiritFetteringState? SpiritFettering",
            )
            and _contains(
                projector,
                "ProjectSpiritFettering",
                '"fetteringSemantics"',
                '"spiritId"',
            )
            and _contains(
                mutation,
                "ApplySpiritFetteredEdit",
                "CharacterSpiritFetteringRules.CanSet",
                "CreateSpiritFetteringImprovement",
                "AppendSpiritFetteringExpense",
                'new XElement("karmatype", "SpiritFettering")',
            )
            and _contains(
                presenter,
                "ApplySpiritFetteredEditAsync",
                "ApplyWorkspaceXmlMutationAsync",
            )
            and _contains(presenter_interface, "ApplySpiritFetteredEditAsync")
            and _contains(
                presenter_persistence,
                "SaveAsync",
                "expectedContentRevision",
                "TryBeginCaptureIntent",
                "_workspacePersistenceService.SaveAsync",
            )
            and _contains(
                core_rules,
                "CharacterSpiritFetteringState",
                "allowSpriteFettering",
                "karmaSpiritFettering",
                "violatesCareerUnboundLimit",
            )
            and _contains(core_models, "FetteringSemantics")
            and _contains(
                core_parser,
                "ProjectSpiritFetteringSemantics",
                '"AllowSpriteFettering"',
                '"SpiritFettering"',
            )
            and _contains(
                workspace_store,
                "expectedContentRevision",
                "Flush(flushToDisk: true)",
                "File.Replace",
                "File.Move",
            )
        )
        e2e_scripted = (
            _contains(
                driver,
                'CONTROL = "SpiritControl.chkFettered"',
                'api != "36"',
                '"profile": "phone"',
                '"controlCount": 1',
                '"spiritFetteringRulesSha256"',
                '"presenterPersistenceSha256"',
                '"workspaceStoreSha256"',
                '"creationFixtureSha256"',
                '"careerFixtureSha256"',
            )
            and creation_fixture.is_file()
            and career_fixture.is_file()
        )
        return {
            "status": "partial_exact_saved_data" if phone_implemented else "missing",
            "route": "Build > Magic and Resonance > Spirits and sprites > selected spirit or sprite > Fettered Spirit / Sprite Pet",
            "surface": "SpiritFetteredPage",
            "automationId": "spirit-fettered-toggle-{stable-target}",
            "sourceRefs": [
                "src/Chummer.Android/Native/SpiritFetteredPage.cs",
                "src/Chummer.Android/Native/BuildPage.cs",
                "src/Chummer.Android/Native/BuildFlowPages.cs",
                "src/Chummer.Android/Native/CollectionEditorPages.cs",
                "src/Chummer.Android/Native/RunnerSessionCoordinator.cs",
                "chummer-presentation/Chummer.Presentation/Overview/SpiritFetteredEditRequest.cs",
                "chummer-presentation/Chummer.Presentation/Overview/WorkspaceCollectionEditorState.cs",
                "chummer-presentation/Chummer.Presentation/Overview/WorkspaceCollectionEditorProjector.cs",
                "chummer-presentation/Chummer.Presentation/Overview/WorkspaceXmlMutationCatalog.cs",
                "chummer-presentation/Chummer.Presentation/Overview/CharacterOverviewPresenter.WorkspaceMutations.cs",
                "chummer-presentation/Chummer.Presentation/Overview/CharacterOverviewPresenter.Persistence.cs",
                "chummer-presentation/Chummer.Presentation/Overview/ICharacterOverviewPresenter.cs",
                "chummer-core-engine/Chummer.Contracts/Characters/CharacterSpiritFetteringRules.cs",
                "chummer-core-engine/Chummer.Contracts/Characters/CharacterSectionModels.cs",
                "chummer-core-engine/Chummer.Infrastructure/Xml/CharacterSectionService.cs",
                "chummer-core-engine/Chummer.Infrastructure/Workspaces/FileWorkspaceStore.cs",
                "chummer-core-engine/Chummer.Rulesets.Sr5/Sr5ShellCatalogs.cs",
            ],
            "presenterMutation": (
                "ICharacterOverviewPresenter.ApplySpiritFetteredEditAsync / "
                "SpiritFetteredEditRequest on one stable Spirit or Sprite guid"
            ),
            "persistenceAssertion": (
                "one shared SpiritControl row reaches Create and Career; the selected stable Spirit or Sprite guid "
                "retains fettered plus exact MAG improvement and Career Karma/SpiritFettering undo side effects "
                "after atomic save, reopen, recovery, and process restart"
            ),
            "coverageLimit": (
                "Activation is enabled only when saved improvements prove Sprite Pet eligibility and, for a Career "
                "Spirit, the active KarmaSpiritFettering value is persisted with the runner; the API 36 driver is "
                f"{'present but not yet executed' if e2e_scripted else 'missing'}"
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
    if class_name == "SpiritControl" and control in SPIRIT_GENERIC_EDITOR_CONTROLS:
        editor_kind, field, xml_element = SPIRIT_GENERIC_EDITOR_CONTROLS[control]
        phone_page = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "CollectionEditorPages.cs"
        phone_route = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "BuildFlowPages.cs"
        coordinator = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "RunnerSessionCoordinator.cs"
        request = presentation_root / "Chummer.Presentation" / "Overview" / "WorkspaceCollectionMutationRequest.cs"
        mutation = presentation_root / "Chummer.Presentation" / "Overview" / "WorkspaceXmlMutationCatalog.cs"
        projector = presentation_root / "Chummer.Presentation" / "Overview" / "WorkspaceCollectionEditorProjector.cs"
        presenter = presentation_root / "Chummer.Presentation" / "Overview" / "CharacterOverviewPresenter.WorkspaceMutations.cs"
        core_models = character_notes_core_root / "Chummer.Contracts" / "Characters" / "CharacterSectionModels.cs"
        core_parser = character_notes_core_root / "Chummer.Infrastructure" / "Xml" / "CharacterSectionService.cs"
        common_shared = (
            _contains(request, "WorkspaceCollectionKind", "    Spirit,")
            and _contains(projector, '"spirits"', "WorkspaceCollectionKind.Spirit")
            and _contains(mutation, "WorkspaceCollectionKind.Spirit", 'new(["spirits"], "spirit")')
            and _contains(coordinator, "ApplyCollectionMutationAsync")
            and _contains(presenter, "ApplyCollectionMutationAsync", "ApplyWorkspaceXmlMutationAsync")
        )
        source_refs = [
            "src/Chummer.Android/Native/BuildFlowPages.cs",
            "src/Chummer.Android/Native/CollectionEditorPages.cs",
            "src/Chummer.Android/Native/RunnerSessionCoordinator.cs",
            "chummer-presentation/Chummer.Presentation/Overview/WorkspaceCollectionEditorProjector.cs",
            "chummer-presentation/Chummer.Presentation/Overview/WorkspaceCollectionMutationRequest.cs",
            "chummer-presentation/Chummer.Presentation/Overview/WorkspaceXmlMutationCatalog.cs",
            "chummer-core-engine/Chummer.Contracts/Characters/CharacterSectionModels.cs",
            "chummer-core-engine/Chummer.Infrastructure/Xml/CharacterSectionService.cs",
        ]
        if editor_kind == "text":
            assert field is not None
            shared = common_shared and _contains(
                request,
                "WorkspacePatchCollectionItemRequest",
                f"    {field},",
            ) and _contains(
                projector,
                f"WorkspaceCollectionTextField.{field}",
                "case WorkspaceCollectionKind.Spirit:",
            ) and _contains(
                mutation,
                "ApplyTextMutation",
                f'"{xml_element}"',
            )
            phone_implemented = shared and _contains(
                phone_route,
                "AddCollectionRows",
                "CollectionItemEditorPage",
            ) and _contains(
                phone_page,
                "collection-field-",
                "WorkspacePatchCollectionItemRequest",
                "original.IsEnabled",
            )
            presenter_mutation = (
                "ICharacterOverviewPresenter.ApplyCollectionMutationAsync / "
                f"WorkspaceCollectionTextField.{field} on WorkspaceCollectionKind.Spirit"
            )
            phone_automation_id = f"collection-field-{xml_element}-{{stable-target}}"
            persistence_assertion = (
                f"selected stable Spirit guid retains {xml_element} after reopen and process restart"
            )
        elif editor_kind == "toggle":
            assert field is not None
            shared = common_shared and _contains(
                request,
                "WorkspacePatchCollectionItemRequest",
                f"    {field},",
            ) and _contains(
                projector,
                "WorkspaceCollectionKind.Spirit => [WorkspaceCollectionToggleField.Bound]",
            ) and _contains(
                mutation,
                "WorkspaceCollectionToggleField.Bound",
                f'"{xml_element}"',
                "Spirit Bound/Registered",
            ) and _contains(
                projector,
                "WorkspaceCollectionKind.Spirit",
                'ReadBool(section, "created")',
            )
            phone_implemented = shared and _contains(
                phone_route,
                "AddCollectionRows",
                "CollectionItemEditorPage",
            ) and _contains(
                phone_page,
                "collection-toggle-",
                "WorkspacePatchCollectionItemRequest",
                "original.IsEnabled",
            )
            presenter_mutation = (
                "ICharacterOverviewPresenter.ApplyCollectionMutationAsync / "
                f"WorkspaceCollectionToggleField.{field} on WorkspaceCollectionKind.Spirit"
            )
            phone_automation_id = f"collection-toggle-{xml_element}-{{stable-target}}"
            persistence_assertion = (
                f"selected stable Spirit guid retains {xml_element} after reopen and process restart"
            )
        elif editor_kind == "integer":
            assert field is not None
            shared = common_shared and _contains(
                request,
                "WorkspaceSetCollectionIntegerRequest",
                f"    {field}"
            ) and _contains(
                projector,
                f"WorkspaceCollectionIntegerField.{field}",
                "ResolveIntegerFields",
            ) and _contains(
                mutation,
                "ApplyIntegerMutation",
                f"WorkspaceCollectionIntegerField.{field}",
                f'"{xml_element}"',
            )
            phone_implemented = shared and _contains(
                phone_route,
                "AddCollectionRows",
                "CollectionItemEditorPage",
            ) and _contains(
                phone_page,
                "collection-integer-",
                "IntegerValues",
            )
            presenter_mutation = (
                "ICharacterOverviewPresenter.ApplyCollectionMutationAsync / "
                f"WorkspaceCollectionIntegerField.{field} on WorkspaceCollectionKind.Spirit"
            )
            phone_automation_id = f"collection-integer-{xml_element}-{{stable-target}}"
            persistence_assertion = (
                f"selected stable Spirit guid retains {xml_element} after reopen and process restart"
            )
        elif editor_kind == "force":
            assert field is not None
            shared = common_shared and _contains(
                request,
                "WorkspaceSetCollectionIntegerRequest",
                "    Force",
            ) and _contains(
                projector,
                "WorkspaceCollectionIntegerField.Force",
                "forceMaximumExact",
                "forceEditable",
            ) and _contains(
                mutation,
                "WorkspaceCollectionIntegerField.Force",
                "TryCalculateSpiritForceMaximum",
                "Force/Rating is read-only",
            ) and _contains(
                core_models,
                "ForceMaximumExact",
                "ForceEditable",
                "EntityType",
            ) and _contains(
                core_parser,
                "TryCalculateSpiritForceMaximum",
                "spiritforcebasedontotalmag",
            )
            phone_implemented = shared and _contains(
                phone_route,
                "AddCollectionRows",
                "CollectionItemEditorPage",
            ) and _contains(
                phone_page,
                "collection-integer-",
                "WorkspaceCollectionIntegerField.Force",
                "IntegerValues",
            )
            presenter_mutation = (
                "ICharacterOverviewPresenter.ApplyCollectionMutationAsync / "
                "WorkspaceCollectionIntegerField.Force on WorkspaceCollectionKind.Spirit "
                "when the Chummer5 ceiling is exact"
            )
            phone_automation_id = "collection-integer-force-{stable-target}"
            persistence_assertion = (
                "selected stable Spirit guid or stable Sprite guid retains force after reopen and process restart "
                "when the saved runner determines the exact Chummer5 ceiling"
            )
        elif editor_kind == "critter":
            assert field is not None
            shared = common_shared and _contains(
                request,
                "public enum WorkspaceCollectionTextField",
                "CritterName",
            ) and _contains(
                projector,
                "WorkspaceCollectionTextField.CritterName",
                "critterNameEditableExact",
            ) and _contains(
                mutation,
                "WorkspaceCollectionTextField.CritterName",
                "Spirit Critter Name is read-only",
                '"crittername"',
            ) and _contains(
                core_models,
                "CritterName",
                "CritterNameEditableExact",
            ) and _contains(
                core_parser,
                'ReadValue(spirit, "crittername")',
                'ReadValue(spirit, "file")',
                'ReadValue(spirit, "relative")',
            )
            phone_implemented = shared and _contains(
                phone_route,
                "AddCollectionRows",
                "CollectionItemEditorPage",
            ) and _contains(
                phone_page,
                "collection-field-",
                "original.IsEnabled",
            )
            presenter_mutation = (
                "ICharacterOverviewPresenter.ApplyCollectionMutationAsync / "
                "WorkspaceCollectionTextField.CritterName on WorkspaceCollectionKind.Spirit "
                "when saved data proves no linked-character path"
            )
            phone_automation_id = "collection-field-crittername-{stable-target}"
            persistence_assertion = (
                "selected stable Spirit guid retains crittername after reopen and process restart "
                "when both saved linked-character paths are blank"
            )
        else:
            shared = common_shared and _contains(
                request,
                "WorkspaceDeleteCollectionItemRequest",
            ) and _contains(
                mutation,
                "ApplyDeleteMutation",
                "WorkspaceCollectionKind.Spirit",
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
            presenter_mutation = (
                "ICharacterOverviewPresenter.ApplyCollectionMutationAsync / "
                "WorkspaceDeleteCollectionItemRequest on WorkspaceCollectionKind.Spirit"
            )
            phone_automation_id = "collection-delete-{stable-target}"
            persistence_assertion = (
                "selected stable Spirit guid is absent from the saved runner after reopen and process restart"
            )

        return {
            "status": (
                "partial_exact_saved_data"
                if editor_kind in {"force", "critter"} and phone_implemented
                else "implemented_pending_emulator" if phone_implemented else "missing"
            ),
            "route": "Build > Magic and Resonance > Spirits and sprites > selected spirit or sprite",
            "surface": "CollectionItemEditorPage",
            "automationId": phone_automation_id,
            "sourceRefs": source_refs,
            "presenterMutation": presenter_mutation,
            "persistenceAssertion": persistence_assertion,
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
    character_note_controls = {
        "rtfNotes": ("notes", "character-notes-editor", "Notes", "CharacterNotes"),
        "rtfGameNotes": ("gamenotes", "character-game-notes-editor", "GameNotes", "GameNotes"),
        "txtGroupNotes": ("groupnotes", "character-group-notes-editor", "GroupNotes", "GroupNotes"),
    }
    if (
        class_name in {"CharacterCreate", "CharacterCareer"}
        and control in character_note_controls
        and not (class_name == "CharacterCreate" and control == "rtfGameNotes")
    ):
        xml_element, automation_id, command_property, profile_property = character_note_controls[control]
        coordinator_command_marker = (
            "request.CharacterNotes"
            if command_property == "Notes"
            else f"{command_property} = request.{command_property}"
        )
        phone_page = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "CharacterNotesPage.cs"
        build_page = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "BuildPage.cs"
        coordinator = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "RunnerSessionCoordinator.cs"
        presenter = presentation_root / "Chummer.Presentation" / "Overview" / "CharacterOverviewPresenter.Persistence.cs"
        core_root = character_notes_core_root
        workspace_contract = core_root / "Chummer.Contracts" / "Workspaces" / "CharacterWorkspaceModels.cs"
        profile_contract = core_root / "Chummer.Contracts" / "Characters" / "CharacterSectionModels.cs"
        file_service = core_root / "Chummer.Infrastructure" / "Xml" / "CharacterFileService.cs"
        section_service = core_root / "Chummer.Infrastructure" / "Xml" / "CharacterSectionService.cs"
        implemented = (
            _contains(phone_page, f'"{automation_id}"', '"character-notes-save"', "CharacterNotesEditRequest")
            and _contains(build_page, '"build-character-notes"', "new CharacterNotesPage")
            and _contains(
                coordinator,
                "ApplyCharacterNotesEditAsync",
                "ExpectedContentRevision",
                "UpdateMetadataAsync",
                "UpdateWorkspaceMetadata",
                "SaveAsync",
                coordinator_command_marker,
            )
            and _contains(presenter, "UpdateMetadataAsync", "expectedContentRevision")
            and _contains(workspace_contract, f"string? {command_property}")
            and _contains(profile_contract, f"public string {profile_property}")
            and _contains(file_service, f'UpdateNode(character, "{xml_element}"')
            and _contains(section_service, f'ReadValue(character, "{xml_element}")')
            and (control != "rtfGameNotes" or _contains(phone_page, "if (profile.Created)"))
        )
        e2e_driver = REPO_ROOT / "tests" / "run_api36_character_notes_e2e.py"
        e2e_scripted = _contains(
            e2e_driver,
            '"journey": "character-notes"',
            '"characterNotesEditPersisted": "pass"',
            '"allCreationNotesEdited": "pass"',
            '"creationWorkspaceXmlPersisted": "pass"',
            '"creationProcessRestartUiReadback": "pass"',
            '"allCareerNotesEdited": "pass"',
            '"careerWorkspaceXmlPersisted": "pass"',
            '"careerProcessRestartUiReadback": "pass"',
            '"controls": control_proofs',
        )
        phone_e2e = character_notes_phone_e2e_receipt if implemented and e2e_scripted else None
        return {
            "status": "implemented_verified_api36" if phone_e2e else "implemented_pending_emulator" if implemented else "missing",
            "route": "Build > Notes",
            "surface": "CharacterNotesPage",
            "automationId": automation_id,
            "sourceRefs": [
                "src/Chummer.Android/Native/CharacterNotesPage.cs",
                "src/Chummer.Android/Native/BuildPage.cs",
                "src/Chummer.Android/Native/RunnerSessionCoordinator.cs",
                "chummer-core-engine/Chummer.Contracts/Workspaces/CharacterWorkspaceModels.cs",
                "chummer-core-engine/Chummer.Contracts/Characters/CharacterSectionModels.cs",
                "chummer-core-engine/Chummer.Infrastructure/Xml/CharacterFileService.cs",
                "chummer-core-engine/Chummer.Infrastructure/Xml/CharacterSectionService.cs",
                "chummer-presentation/Chummer.Presentation/Overview/CharacterOverviewPresenter.Persistence.cs",
            ],
            "presenterMutation": (
                "ICharacterOverviewPresenter.UpdateMetadataAsync(UpdateWorkspaceMetadata."
                f"{command_property})"
            ),
            "persistenceAssertion": (
                f"character/{xml_element} equals the submitted value after save, reopen, and process restart"
            ),
            "e2e": phone_e2e or {
                "status": "scripted_not_executed" if e2e_scripted else "missing",
                "ref": "tests/run_api36_character_notes_e2e.py" if e2e_scripted else None,
            },
        }
    if class_name in {"CharacterCreate", "CharacterCareer"} and control in PRIMARY_ARM_CONTROLS:
        xml_element, automation_id, property_name = PRIMARY_ARM_CONTROLS[control]
        page = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "PrimaryArmPage.cs"
        build_page = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "BuildPage.cs"
        coordinator = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "RunnerSessionCoordinator.cs"
        e2e_driver = REPO_ROOT / "tests" / "run_api36_primary_arm_e2e.py"
        presentation_overview = presentation_root / "Chummer.Presentation" / "Overview"
        request = presentation_overview / "PrimaryArmEditRequest.cs"
        mutation = presentation_overview / "WorkspaceXmlMutationCatalog.cs"
        presenter = presentation_overview / "CharacterOverviewPresenter.WorkspaceMutations.cs"
        core_contract = character_notes_core_root / "Chummer.Contracts" / "Characters" / "CharacterSectionModels.cs"
        core_section = character_notes_core_root / "Chummer.Infrastructure" / "Xml" / "CharacterSectionService.cs"
        implemented = (
            _contains(page, f'"{automation_id}"', '"primary-arm-save"', "PrimaryArmEditRequest", "Ambidextrous")
            and _contains(build_page, '"build-primary-arm"', "new PrimaryArmPage")
            and _contains(
                coordinator,
                "PreparePrimaryArmEditAsync",
                "ApplyPrimaryArmEditAsync",
                "ExpectedContentRevision",
                "SaveAsync",
            )
            and _contains(
                request,
                "PrimaryArmEditorState",
                "PrimaryArmEditRequest",
                "ExpectedContentRevision",
                "IsAmbidextrous",
                '"Left"',
                '"Right"',
            )
            and _contains(
                mutation,
                "ApplyPrimaryArmEdit",
                "PrimaryArmEditorProjector.IsAmbidextrous",
                f'"{xml_element}"',
            )
            and _contains(
                presenter,
                "PreparePrimaryArmEditAsync",
                "ApplyPrimaryArmEditAsync",
                "ApplyWorkspaceXmlMutationAsync",
            )
            and _contains(core_contract, f"public string {property_name}", "public bool Ambidextrous")
            and _contains(
                core_section,
                'ReadValue(character, "primaryarm")',
                '"Ambidextrous"',
                "ReadLegacyImprovementIntegerFlag",
            )
        )
        e2e_scripted = _contains(
            e2e_driver,
            '"journey": "primary-arm"',
            '"creationPrimaryArmEdited": "pass"',
            '"creationWorkspaceXmlPersisted": "pass"',
            '"creationProcessRestartUiReadback": "pass"',
            '"careerPrimaryArmEdited": "pass"',
            '"careerWorkspaceXmlPersisted": "pass"',
            '"careerProcessRestartUiReadback": "pass"',
            '"ambidextrousReadOnlyGateEnforced": "pass"',
            '"controls": controls',
        )
        phone_e2e = primary_arm_phone_e2e_receipt if implemented and e2e_scripted else None
        return {
            "status": "implemented_verified_api36" if phone_e2e else "implemented_pending_emulator" if implemented else "missing",
            "route": "Build > Primary arm",
            "surface": "PrimaryArmPage",
            "automationId": automation_id,
            "sourceRefs": [
                "src/Chummer.Android/Native/PrimaryArmPage.cs",
                "src/Chummer.Android/Native/BuildPage.cs",
                "src/Chummer.Android/Native/RunnerSessionCoordinator.cs",
                "chummer-presentation/Chummer.Presentation/Overview/PrimaryArmEditRequest.cs",
                "chummer-presentation/Chummer.Presentation/Overview/WorkspaceXmlMutationCatalog.cs",
                "chummer-presentation/Chummer.Presentation/Overview/CharacterOverviewPresenter.WorkspaceMutations.cs",
                "chummer-core-engine/Chummer.Contracts/Characters/CharacterSectionModels.cs",
                "chummer-core-engine/Chummer.Infrastructure/Xml/CharacterSectionService.cs",
            ],
            "presenterMutation": (
                "ICharacterOverviewPresenter.ApplyPrimaryArmEditAsync(PrimaryArmEditRequest)"
            ),
            "persistenceAssertion": (
                "character/primaryarm equals exact Left or Right after save, same-session reopen, "
                "and process restart; enabled Ambidextrous improvement remains read-only"
            ),
            "e2e": phone_e2e or {
                "status": "scripted_not_executed" if e2e_scripted else "missing",
                "ref": "tests/run_api36_primary_arm_e2e.py" if e2e_scripted else None,
            },
        }
    if (
        class_name in {"CharacterCreate", "CharacterCareer"}
        and control == GROUP_MEMBERSHIP_CONTROL
    ):
        page = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "GroupMembershipPage.cs"
        build_page = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "BuildPage.cs"
        coordinator = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "RunnerSessionCoordinator.cs"
        driver = REPO_ROOT / "tests" / "run_api36_group_membership_e2e.py"
        creation_fixture = REPO_ROOT / "tests" / "fixtures" / "creation-group-membership-e2e.chum5"
        career_fixture = REPO_ROOT / "tests" / "fixtures" / "career-group-membership-e2e.chum5"
        overview = presentation_root / "Chummer.Presentation" / "Overview"
        request = overview / "GroupMembershipEditRequest.cs"
        mutation = overview / "WorkspaceXmlMutationCatalog.cs"
        presenter = overview / "CharacterOverviewPresenter.WorkspaceMutations.cs"
        presenter_interface = overview / "ICharacterOverviewPresenter.cs"
        presenter_persistence = overview / "CharacterOverviewPresenter.Persistence.cs"
        core_rules = character_notes_core_root / "Chummer.Contracts" / "Characters" / "CharacterGroupMembershipRules.cs"
        resolver_contract = character_notes_core_root / "Chummer.Application" / "Characters" / "ICharacterSourceDataResolver.cs"
        resolver = character_notes_core_root / "Chummer.Infrastructure" / "Xml" / "FileSystemCharacterSourceDataResolver.cs"
        workspace_store = character_notes_core_root / "Chummer.Infrastructure" / "Workspaces" / "FileWorkspaceStore.cs"
        implemented = (
            _contains(
                page,
                "class GroupMembershipPage",
                '"group-membership-page"',
                '"group-membership-toggle"',
                '"group-membership-save"',
                "CharacterGroupMembershipState",
                "_editor.ContentRevision",
                "GroupMembershipEditRequest",
            )
            and _contains(build_page, '"build-group-membership"', "new GroupMembershipPage")
            and _contains(
                coordinator,
                "PrepareGroupMembershipEditAsync",
                "ApplyGroupMembershipEditAsync",
                "ExpectedContentRevision",
                "_presenter.SaveAsync",
            )
            and _contains(
                request,
                "GroupMembershipEditorState",
                "GroupMembershipEditRequest",
                "CharacterGroupMembershipState ExpectedState",
                "TryResolveGroupMembershipKarmaCosts",
            )
            and _contains(
                mutation,
                "ApplyGroupMembershipEdit",
                "CharacterGroupMembershipRules.CanSet",
                "AppendGroupMembershipExpense",
                'new XElement("karmatype", joining ? "JoinGroup" : "LeaveGroup")',
            )
            and _contains(
                presenter,
                "PrepareGroupMembershipEditAsync",
                "ApplyGroupMembershipEditAsync",
                "ApplyWorkspaceXmlMutationAsync",
            )
            and _contains(
                presenter_interface,
                "PrepareGroupMembershipEditAsync",
                "ApplyGroupMembershipEditAsync",
            )
            and _contains(
                presenter_persistence,
                "SaveAsync",
                "expectedContentRevision",
                "TryBeginCaptureIntent",
                "_workspacePersistenceService.SaveAsync",
            )
            and _contains(
                core_rules,
                "CharacterGroupMembershipState",
                "RequiresConfirmation",
                "transitionCost <= availableKarma",
            )
            and _contains(
                resolver_contract,
                "TryResolveGroupMembershipKarmaCosts",
                "joinCost",
                "leaveCost",
            )
            and _contains(
                resolver,
                '"karmajoingroup"',
                '"karmaleavegroup"',
                "TryResolveGroupMembershipKarmaCosts",
            )
            and _contains(
                workspace_store,
                "expectedContentRevision",
                "Flush(flushToDisk: true)",
                "File.Replace",
                "File.Move",
            )
        )
        e2e_scripted = (
            _contains(
                driver,
                'CONTROL = "chkJoinGroup"',
                'api != "36"',
                '"profile": "phone"',
                '"journey": "group-membership"',
                '"groupMembershipRulesSha256"',
                '"sourceResolverSha256"',
                '"presenterPersistenceSha256"',
                '"workspaceStoreSha256"',
                '"creationFixtureSha256"',
                '"careerFixtureSha256"',
            )
            and creation_fixture.is_file()
            and career_fixture.is_file()
        )
        return {
            "status": "implemented_pending_emulator" if implemented else "missing",
            "route": "Build > Runner > Group membership",
            "surface": "GroupMembershipPage",
            "automationId": "group-membership-toggle",
            "sourceRefs": [
                "src/Chummer.Android/Native/GroupMembershipPage.cs",
                "src/Chummer.Android/Native/BuildPage.cs",
                "src/Chummer.Android/Native/RunnerSessionCoordinator.cs",
                "chummer-presentation/Chummer.Presentation/Overview/GroupMembershipEditRequest.cs",
                "chummer-presentation/Chummer.Presentation/Overview/WorkspaceXmlMutationCatalog.cs",
                "chummer-presentation/Chummer.Presentation/Overview/CharacterOverviewPresenter.WorkspaceMutations.cs",
                "chummer-presentation/Chummer.Presentation/Overview/CharacterOverviewPresenter.Persistence.cs",
                "chummer-presentation/Chummer.Presentation/Overview/ICharacterOverviewPresenter.cs",
                "chummer-core-engine/Chummer.Contracts/Characters/CharacterGroupMembershipRules.cs",
                "chummer-core-engine/Chummer.Application/Characters/ICharacterSourceDataResolver.cs",
                "chummer-core-engine/Chummer.Infrastructure/Xml/FileSystemCharacterSourceDataResolver.cs",
                "chummer-core-engine/Chummer.Infrastructure/Workspaces/FileWorkspaceStore.cs",
            ],
            "presenterMutation": (
                "ICharacterOverviewPresenter.ApplyGroupMembershipEditAsync / "
                "GroupMembershipEditRequest on character/groupmember"
            ),
            "persistenceAssertion": (
                "character/groupmember matches the submitted Create/Career value after revision-bound atomic save, "
                "same-session reopen, and process restart; Career MAG changes preserve exact profile Karma expense "
                "and JoinGroup/LeaveGroup undo while non-MAG network changes stay cost-free"
            ),
            "coverageLimit": (
                "Create toggles directly like the legacy binding; Career MAG editing fails closed unless the saved "
                "settings profile proves exact KarmaJoinGroup/KarmaLeaveGroup costs; the API 36 driver is "
                f"{'present but not yet executed' if e2e_scripted else 'missing'}"
            ),
            "e2e": {
                "status": "scripted_not_executed" if e2e_scripted else "missing",
                "ref": "tests/run_api36_group_membership_e2e.py" if e2e_scripted else None,
            },
            "tablet": {
                "status": "missing",
                "surface": None,
                "automationId": None,
                "sourceRefs": [],
            },
            "tabletE2e": {"status": "missing", "ref": None},
        }
    if (
        class_name in {"CharacterCreate", "CharacterCareer"}
        and control == GROUP_NAME_CONTROL
    ):
        page = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "GroupNamePage.cs"
        build_page = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "BuildPage.cs"
        coordinator = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "RunnerSessionCoordinator.cs"
        driver = REPO_ROOT / "tests" / "run_api36_group_name_e2e.py"
        creation_fixture = REPO_ROOT / "tests" / "fixtures" / "creation-group-name-e2e.chum5"
        career_fixture = REPO_ROOT / "tests" / "fixtures" / "career-group-name-e2e.chum5"
        overview = presentation_root / "Chummer.Presentation" / "Overview"
        request = overview / "GroupNameEditRequest.cs"
        mutation = overview / "WorkspaceXmlMutationCatalog.cs"
        presenter = overview / "CharacterOverviewPresenter.WorkspaceMutations.cs"
        presenter_interface = overview / "ICharacterOverviewPresenter.cs"
        presenter_persistence = overview / "CharacterOverviewPresenter.Persistence.cs"
        core_rules = character_notes_core_root / "Chummer.Contracts" / "Characters" / "CharacterGroupNameRules.cs"
        workspace_store = character_notes_core_root / "Chummer.Infrastructure" / "Workspaces" / "FileWorkspaceStore.cs"
        implemented = (
            _contains(
                page,
                "class GroupNamePage",
                'AutomationId = "group-name-page"',
                'AutomationId = "group-name-value"',
                'AutomationId = "group-name-save"',
                "MaxLength = CharacterGroupNameRules.MaximumLength",
                "GroupNameEditRequest",
            )
            and _contains(build_page, 'automationId: "build-group-name"', "new GroupNamePage")
            and _contains(
                coordinator,
                "PrepareGroupNameEditAsync",
                "ApplyGroupNameEditAsync",
                "ExpectedContentRevision",
                "_presenter.SaveAsync",
            )
            and _contains(
                request,
                "GroupNameEditorState",
                "GroupNameEditRequest",
                "ExpectedGroupName",
                'root.Elements("groupname").Take(2)',
            )
            and _contains(
                mutation,
                "ApplyGroupNameEdit",
                "CharacterGroupNameRules.TryValidate",
                'root.Elements("groupname").SingleOrDefault()',
            )
            and _contains(
                presenter,
                "PrepareGroupNameEditAsync",
                "ApplyGroupNameEditAsync",
                "ApplyWorkspaceXmlMutationAsync",
            )
            and _contains(
                presenter_interface,
                "PrepareGroupNameEditAsync",
                "ApplyGroupNameEditAsync",
            )
            and _contains(
                presenter_persistence,
                "SaveAsync",
                "expectedContentRevision",
                "TryBeginCaptureIntent",
                "_workspacePersistenceService.SaveAsync",
            )
            and _contains(
                core_rules,
                "CharacterGroupNameRules",
                "MaximumLength = 32_767",
                "IndexOfAny",
            )
            and _contains(
                workspace_store,
                "expectedContentRevision",
                "Flush(flushToDisk: true)",
                "File.Replace",
                "File.Move",
            )
        )
        e2e_scripted = (
            _contains(
                driver,
                '"CharacterCreate.txtGroupName"',
                '"CharacterCareer.txtGroupName"',
                'api != "36"',
                '"profile": "phone"',
                '"journey": "group-name"',
                '"groupNameRulesSha256"',
                '"presenterPersistenceSha256"',
                '"workspaceStoreSha256"',
                '"creationFixtureSha256"',
                '"careerFixtureSha256"',
                '"contactGroupNameNotCrossWired": "pass"',
            )
            and creation_fixture.is_file()
            and career_fixture.is_file()
        )
        return {
            "status": "implemented_pending_emulator" if implemented else "missing",
            "route": "Build > Runner > Group name",
            "surface": "GroupNamePage",
            "automationId": "group-name-value",
            "sourceRefs": [
                "src/Chummer.Android/Native/GroupNamePage.cs",
                "src/Chummer.Android/Native/BuildPage.cs",
                "src/Chummer.Android/Native/RunnerSessionCoordinator.cs",
                "chummer-presentation/Chummer.Presentation/Overview/GroupNameEditRequest.cs",
                "chummer-presentation/Chummer.Presentation/Overview/WorkspaceXmlMutationCatalog.cs",
                "chummer-presentation/Chummer.Presentation/Overview/CharacterOverviewPresenter.WorkspaceMutations.cs",
                "chummer-presentation/Chummer.Presentation/Overview/CharacterOverviewPresenter.Persistence.cs",
                "chummer-presentation/Chummer.Presentation/Overview/ICharacterOverviewPresenter.cs",
                "chummer-core-engine/Chummer.Contracts/Characters/CharacterGroupNameRules.cs",
                "chummer-core-engine/Chummer.Infrastructure/Workspaces/FileWorkspaceStore.cs",
            ],
            "presenterMutation": (
                "ICharacterOverviewPresenter.ApplyGroupNameEditAsync(GroupNameEditRequest)"
            ),
            "persistenceAssertion": (
                "character/groupname equals the exact submitted single-line Create/Career text after "
                "revision-bound atomic save, same-session reopen, and process restart; nested contact-like "
                "groupname nodes remain untouched"
            ),
            "coverageLimit": (
                "Covers only CharacterCreate/CharacterCareer initiation-group txtGroupName; the distinct "
                "SelectContactConnection.txtGroupName remains a separate missing control; the API 36 driver is "
                f"{'present but not yet executed' if e2e_scripted else 'missing'}"
            ),
            "e2e": {
                "status": "scripted_not_executed" if e2e_scripted else "missing",
                "ref": "tests/run_api36_group_name_e2e.py" if e2e_scripted else None,
            },
            "tablet": {
                "status": "missing",
                "surface": None,
                "automationId": None,
                "sourceRefs": [],
            },
            "tabletE2e": {"status": "missing", "ref": None},
        }
    if (
        class_name in {"CharacterCreate", "CharacterCareer"}
        and control == TRADITION_NAME_CONTROL
    ):
        page = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "TraditionNamePage.cs"
        build_page = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "BuildPage.cs"
        coordinator = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "RunnerSessionCoordinator.cs"
        driver = REPO_ROOT / "tests" / "run_api36_tradition_name_e2e.py"
        creation_fixture = REPO_ROOT / "tests" / "fixtures" / "creation-tradition-name-e2e.chum5"
        career_fixture = REPO_ROOT / "tests" / "fixtures" / "career-tradition-name-e2e.chum5"
        overview = presentation_root / "Chummer.Presentation" / "Overview"
        request = overview / "TraditionNameEditRequest.cs"
        mutation = overview / "WorkspaceXmlMutationCatalog.cs"
        presenter = overview / "CharacterOverviewPresenter.WorkspaceMutations.cs"
        presenter_interface = overview / "ICharacterOverviewPresenter.cs"
        presenter_persistence = overview / "CharacterOverviewPresenter.Persistence.cs"
        core_rules = character_notes_core_root / "Chummer.Contracts" / "Characters" / "CharacterTraditionNameRules.cs"
        workspace_store = character_notes_core_root / "Chummer.Infrastructure" / "Workspaces" / "FileWorkspaceStore.cs"
        implemented = (
            _contains(
                page,
                "class TraditionNamePage",
                'AutomationId = "tradition-name-page"',
                'AutomationId = "tradition-name-value"',
                'AutomationId = "tradition-name-save"',
                "MaxLength = CharacterTraditionNameRules.MaximumLength",
                "TraditionNameEditRequest",
            )
            and _contains(build_page, 'automationId: "build-tradition-name"', "new TraditionNamePage")
            and _contains(
                coordinator,
                "PrepareTraditionNameEditAsync",
                "ApplyTraditionNameEditAsync",
                "ExpectedContentRevision",
                "_presenter.SaveAsync",
            )
            and _contains(
                request,
                "TraditionNameEditorState",
                "TraditionNameEditRequest",
                "ExpectedTraditionName",
                'root.Elements("tradition").Take(2)',
                'tradition.Elements("name").Take(2)',
                "CharacterTraditionNameRules.CustomMagicalTraditionSourceId",
            )
            and _contains(
                mutation,
                "ApplyTraditionNameEdit",
                "CharacterTraditionNameRules.TryValidate",
                'root.Elements("tradition").Single()',
            )
            and _contains(
                presenter,
                "PrepareTraditionNameEditAsync",
                "ApplyTraditionNameEditAsync",
                "ApplyWorkspaceXmlMutationAsync",
            )
            and _contains(
                presenter_interface,
                "PrepareTraditionNameEditAsync",
                "ApplyTraditionNameEditAsync",
            )
            and _contains(
                presenter_persistence,
                "SaveAsync",
                "expectedContentRevision",
                "TryBeginCaptureIntent",
                "_workspacePersistenceService.SaveAsync",
            )
            and _contains(
                core_rules,
                "CharacterTraditionNameRules",
                "616ba093-306c-45fc-8f41-0b98c8cccb46",
                "MaximumLength = 32_767",
                "IndexOfAny",
            )
            and _contains(
                workspace_store,
                "expectedContentRevision",
                "Flush(flushToDisk: true)",
                "File.Replace",
                "File.Move",
            )
        )
        e2e_scripted = (
            _contains(
                driver,
                '"CharacterCreate.txtTraditionName"',
                '"CharacterCareer.txtTraditionName"',
                'api != "36"',
                '"profile": "phone"',
                '"journey": "tradition-name"',
                '"traditionNameRulesSha256"',
                '"presenterPersistenceSha256"',
                '"workspaceStoreSha256"',
                '"creationFixtureSha256"',
                '"careerFixtureSha256"',
                '"nonCustomTraditionRejectedBySourceContract": "pass"',
            )
            and creation_fixture.is_file()
            and career_fixture.is_file()
        )
        return {
            "status": "implemented_pending_emulator" if implemented else "missing",
            "route": "Build > Magic > Custom tradition name",
            "surface": "TraditionNamePage",
            "automationId": "tradition-name-value",
            "sourceRefs": [
                "src/Chummer.Android/Native/TraditionNamePage.cs",
                "src/Chummer.Android/Native/BuildPage.cs",
                "src/Chummer.Android/Native/RunnerSessionCoordinator.cs",
                "chummer-presentation/Chummer.Presentation/Overview/TraditionNameEditRequest.cs",
                "chummer-presentation/Chummer.Presentation/Overview/WorkspaceXmlMutationCatalog.cs",
                "chummer-presentation/Chummer.Presentation/Overview/CharacterOverviewPresenter.WorkspaceMutations.cs",
                "chummer-presentation/Chummer.Presentation/Overview/CharacterOverviewPresenter.Persistence.cs",
                "chummer-presentation/Chummer.Presentation/Overview/ICharacterOverviewPresenter.cs",
                "chummer-core-engine/Chummer.Contracts/Characters/CharacterTraditionNameRules.cs",
                "chummer-core-engine/Chummer.Infrastructure/Workspaces/FileWorkspaceStore.cs",
            ],
            "presenterMutation": (
                "ICharacterOverviewPresenter.ApplyTraditionNameEditAsync(TraditionNameEditRequest)"
            ),
            "persistenceAssertion": (
                "character/tradition/name equals the exact submitted Create/Career single-line text after "
                "stable-GUID, exact-Custom-source, revision-bound atomic save, same-session reopen, and process "
                "restart; nested name nodes and all other tradition fields remain untouched"
            ),
            "coverageLimit": (
                "Covers only CharacterCreate/CharacterCareer txtTraditionName when the saved tradition has the "
                "exact Chummer5 Custom magical source ID 616ba093-306c-45fc-8f41-0b98c8cccb46; published, "
                "missing, duplicate, RES, or identity-less traditions fail closed; the API 36 driver is "
                f"{'present but not yet executed' if e2e_scripted else 'missing'}"
            ),
            "e2e": {
                "status": "scripted_not_executed" if e2e_scripted else "missing",
                "ref": "tests/run_api36_tradition_name_e2e.py" if e2e_scripted else None,
            },
            "tablet": {
                "status": "missing",
                "surface": None,
                "automationId": None,
                "sourceRefs": [],
            },
            "tabletE2e": {"status": "missing", "ref": None},
        }
    if class_name == "CharacterCareer" and control in CAREER_EDGE_USE_CONTROLS:
        handler, action, automation_id = CAREER_EDGE_USE_CONTROLS[control]
        page = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "CareerEdgeUsePage.cs"
        build_page = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "BuildPage.cs"
        coordinator = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "RunnerSessionCoordinator.cs"
        driver = REPO_ROOT / "tests" / "run_api36_career_edge_use_e2e.py"
        fixture = REPO_ROOT / "tests" / "fixtures" / "career-edge-use-e2e.chum5"
        overview = presentation_root / "Chummer.Presentation" / "Overview"
        request = overview / "CareerEdgeUseEditRequest.cs"
        mutation = overview / "WorkspaceXmlMutationCatalog.cs"
        presenter = overview / "CharacterOverviewPresenter.WorkspaceMutations.cs"
        presenter_interface = overview / "ICharacterOverviewPresenter.cs"
        presenter_persistence = overview / "CharacterOverviewPresenter.Persistence.cs"
        core_rules = character_notes_core_root / "Chummer.Contracts" / "Characters" / "CharacterCareerEdgeUseRules.cs"
        workspace_store = character_notes_core_root / "Chummer.Infrastructure" / "Workspaces" / "FileWorkspaceStore.cs"
        legacy_handler_exact = any(
            event.get("handler") == handler
            for event in legacy.get("events", [])
            if isinstance(event, dict)
        )
        action_marker = (
            "CharacterCareerEdgeUseAction.Spend"
            if action == "spend"
            else "CharacterCareerEdgeUseAction.Regain"
        )
        implemented = (
            legacy_handler_exact
            and _contains(
                page,
                "class CareerEdgeUsePage",
                'AutomationId = "career-edge-use-page"',
                f'AutomationId = "{automation_id}"',
                action_marker,
                "_editor.ContentRevision",
            )
            and _contains(build_page, '"build-career-edge-use"', "new CareerEdgeUsePage")
            and _contains(
                coordinator,
                "PrepareCareerEdgeUseEditAsync",
                "ApplyCareerEdgeUseEditAsync",
                "ExpectedContentRevision",
                "_presenter.SaveAsync",
            )
            and _contains(
                request,
                "CareerEdgeUseEditorState",
                "CareerEdgeUseEditRequest",
                "CharacterCareerEdgeUseState ExpectedState",
                'ReadRequiredBool(root, "created")',
                'ReadOptionalNonNegativeInt(root, "edgeused")',
                '"EDG"',
            )
            and _contains(
                mutation,
                "ApplyCareerEdgeUseEdit",
                "CharacterCareerEdgeUseRules.Apply",
                'root.Elements("edgeused").SingleOrDefault()',
            )
            and _contains(
                presenter,
                "PrepareCareerEdgeUseEditAsync",
                "ApplyCareerEdgeUseEditAsync",
                "ApplyWorkspaceXmlMutationAsync",
            )
            and _contains(
                presenter_interface,
                "PrepareCareerEdgeUseEditAsync",
                "ApplyCareerEdgeUseEditAsync",
            )
            and _contains(
                presenter_persistence,
                "SaveAsync",
                "expectedContentRevision",
                "TryBeginCaptureIntent",
                "_workspacePersistenceService.SaveAsync",
            )
            and _contains(
                core_rules,
                "CharacterCareerEdgeUseState",
                "edgeUsed < totalEdge",
                "edgeUsed > 0",
                "checked(state.EdgeUsed + 1)",
                "checked(state.EdgeUsed - 1)",
            )
            and _contains(
                workspace_store,
                "expectedContentRevision",
                "Flush(flushToDisk: true)",
                "File.Replace",
                "File.Move",
            )
        )
        e2e_scripted = (
            _contains(
                driver,
                'CONTROLS = ("cmdEdgeSpent", "cmdEdgeGained")',
                'api != "36"',
                '"profile": "phone"',
                '"journey": "career-edge-use"',
                '"careerEdgeUseRulesSha256"',
                '"presenterPersistenceSha256"',
                '"workspaceStoreSha256"',
                '"careerFixtureSha256"',
            )
            and fixture.is_file()
        )
        return {
            "status": "implemented_pending_emulator" if implemented else "missing",
            "route": "Build > Runner > Edge use",
            "surface": "CareerEdgeUsePage",
            "automationId": automation_id,
            "sourceRefs": [
                "src/Chummer.Android/Native/CareerEdgeUsePage.cs",
                "src/Chummer.Android/Native/BuildPage.cs",
                "src/Chummer.Android/Native/RunnerSessionCoordinator.cs",
                "chummer-presentation/Chummer.Presentation/Overview/CareerEdgeUseEditRequest.cs",
                "chummer-presentation/Chummer.Presentation/Overview/WorkspaceXmlMutationCatalog.cs",
                "chummer-presentation/Chummer.Presentation/Overview/CharacterOverviewPresenter.WorkspaceMutations.cs",
                "chummer-presentation/Chummer.Presentation/Overview/CharacterOverviewPresenter.Persistence.cs",
                "chummer-presentation/Chummer.Presentation/Overview/ICharacterOverviewPresenter.cs",
                "chummer-core-engine/Chummer.Contracts/Characters/CharacterCareerEdgeUseRules.cs",
                "chummer-core-engine/Chummer.Infrastructure/Workspaces/FileWorkspaceStore.cs",
            ],
            "presenterMutation": (
                "ICharacterOverviewPresenter.ApplyCareerEdgeUseEditAsync / "
                f"CareerEdgeUseEditRequest({action}) on character/edgeused"
            ),
            "persistenceAssertion": (
                "character/edgeused changes by exactly one within the exact saved EDG total bound after "
                "revision-bound atomic save, same-session reopen, and two process restarts; unrelated nested "
                "edgeused XML survives"
            ),
            "coverageLimit": (
                "Career only, matching Chummer5; the API 36 phone driver is "
                f"{'present but not yet executed' if e2e_scripted else 'missing'}"
            ),
            "e2e": {
                "status": "scripted_not_executed" if e2e_scripted else "missing",
                "ref": "tests/run_api36_career_edge_use_e2e.py" if e2e_scripted else None,
            },
            "tablet": {
                "status": "missing",
                "surface": None,
                "automationId": None,
                "sourceRefs": [],
            },
            "tabletE2e": {"status": "missing", "ref": None},
        }
    if (
        class_name in {"CharacterCreate", "CharacterCareer"}
        and control == GEAR_LOCATION_ADD_CONTROL
    ):
        page = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "GearLocationAddPage.cs"
        flow = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "BuildFlowPages.cs"
        coordinator = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "RunnerSessionCoordinator.cs"
        e2e_driver = REPO_ROOT / "tests" / "run_api36_gear_location_e2e.py"
        presentation_overview = presentation_root / "Chummer.Presentation" / "Overview"
        request = presentation_overview / "GearLocationAddRequest.cs"
        mutation = presentation_overview / "WorkspaceXmlMutationCatalog.cs"
        presenter = presentation_overview / "CharacterOverviewPresenter.WorkspaceMutations.cs"
        presenter_interface = presentation_overview / "ICharacterOverviewPresenter.cs"
        legacy_source = (
            presentation_root
            / "Chummer"
            / "Forms"
            / "Character Forms"
            / f"{class_name}.cs"
        )
        expected_handler = "cmdAddLocation_Click"
        legacy_exact = (
            any(event.get("handler") == expected_handler for event in legacy.get("events", []))
            and _contains(
                legacy_source,
                expected_handler,
                "new Location(CharacterObject, CharacterObject.GearLocations",
                "CharacterObject.GearLocations.AddAsync",
            )
        )
        implemented = (
            legacy_exact
            and _contains(
                page,
                'AutomationId = "gear-location-name"',
                'AutomationId = "gear-location-add"',
                "GearLocationAddRequest.MaximumNameLength",
                "Coordinator.ApplyGearLocationAddAsync",
            )
            and _contains(
                flow,
                '"gearlocations"',
                'automationId: "gear-location-open-add"',
                "new GearLocationAddPage",
            )
            and _contains(
                coordinator,
                "ApplyGearLocationAddAsync",
                "ExpectedContentRevision",
                "_presenter.ApplyGearLocationAddAsync",
                "_presenter.SaveAsync",
            )
            and _contains(
                request,
                "GearLocationAddRequest",
                "ExpectedContentRevision",
                "MaximumNameLength = 32767",
                "ValidateName",
            )
            and _contains(
                mutation,
                "ApplyGearLocationAdd",
                '"gearlocations"',
                "Guid.NewGuid()",
                'new XElement("notes", string.Empty)',
            )
            and _contains(
                presenter,
                "ApplyGearLocationAddAsync",
                "ApplyWorkspaceXmlMutationAsync",
                "ExpectedContentRevision",
            )
            and _contains(presenter_interface, "ApplyGearLocationAddAsync", "GearLocationAddRequest")
        )
        e2e_scripted = _contains(
            e2e_driver,
            '"journey": "gear-location-add"',
            '"creationGearLocationAdded": "pass"',
            '"creationWorkspaceXmlPersisted": "pass"',
            '"creationProcessRestartPersistence": "pass"',
            '"careerGearLocationAdded": "pass"',
            '"careerWorkspaceXmlPersisted": "pass"',
            '"careerProcessRestartPersistence": "pass"',
            '"controls": controls',
        )
        phone_e2e = gear_location_phone_e2e_receipt if implemented and e2e_scripted else None
        return {
            "status": (
                "implemented_verified_api36"
                if phone_e2e
                else "implemented_pending_emulator" if implemented else "missing"
            ),
            "route": "Build > Gear > Gear Locations > Add gear location",
            "surface": "GearLocationAddPage",
            "automationId": "gear-location-add",
            "sourceRefs": [
                "src/Chummer.Android/Native/GearLocationAddPage.cs",
                "src/Chummer.Android/Native/BuildFlowPages.cs",
                "src/Chummer.Android/Native/RunnerSessionCoordinator.cs",
                "chummer-presentation/Chummer.Presentation/Overview/GearLocationAddRequest.cs",
                "chummer-presentation/Chummer.Presentation/Overview/WorkspaceXmlMutationCatalog.cs",
                "chummer-presentation/Chummer.Presentation/Overview/CharacterOverviewPresenter.WorkspaceMutations.cs",
                "chummer-presentation/Chummer.Presentation/Overview/ICharacterOverviewPresenter.cs",
            ],
            "presenterMutation": (
                "ICharacterOverviewPresenter.ApplyGearLocationAddAsync(GearLocationAddRequest)"
            ),
            "persistenceAssertion": (
                "one new stable-guid character/gearlocations/location preserves the exact nonempty "
                "name and empty notes after save, same-session reopen, and process restart"
            ),
            "e2e": phone_e2e or {
                "status": "scripted_not_executed" if e2e_scripted else "missing",
                "ref": "tests/run_api36_gear_location_e2e.py" if e2e_scripted else None,
            },
            "coverageLimit": (
                "Exact Chummer5 GearLocations add only; weapon, armor, and vehicle location "
                "collections remain separately inventoried and are not claimed by this control."
            ),
        }
    if (
        class_name in {"CharacterCreate", "CharacterCareer"}
        and control == WEAPON_LOCATION_ADD_CONTROL
    ):
        page = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "WeaponLocationAddPage.cs"
        flow = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "BuildFlowPages.cs"
        coordinator = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "RunnerSessionCoordinator.cs"
        e2e_driver = REPO_ROOT / "tests" / "run_api36_weapon_location_e2e.py"
        overview = presentation_root / "Chummer.Presentation" / "Overview"
        request = overview / "WeaponLocationAddRequest.cs"
        mutation = overview / "WorkspaceXmlMutationCatalog.cs"
        presenter = overview / "CharacterOverviewPresenter.WorkspaceMutations.cs"
        presenter_interface = overview / "ICharacterOverviewPresenter.cs"
        legacy_source = (
            presentation_root
            / "Chummer"
            / "Forms"
            / "Character Forms"
            / f"{class_name}.cs"
        )
        expected_handler = "cmdAddWeaponLocation_Click"
        legacy_exact = (
            any(event.get("handler") == expected_handler for event in legacy.get("events", []))
            and _contains(
                legacy_source,
                expected_handler,
                "new Location(CharacterObject, CharacterObject.WeaponLocations",
                "CharacterObject.WeaponLocations.AddAsync",
                "string.IsNullOrEmpty(frmPickText.MyForm.SelectedValue)",
            )
        )
        implemented = (
            legacy_exact
            and _contains(
                page,
                'AutomationId = "weapon-location-name"',
                'AutomationId = "weapon-location-add"',
                "WeaponLocationAddRequest.MaximumNameLength",
                "Coordinator.ApplyWeaponLocationAddAsync",
            )
            and _contains(
                flow,
                'case "weaponlocations"',
                'automationId: "weapon-location-open-add"',
                "new WeaponLocationAddPage",
            )
            and _contains(
                coordinator,
                "ApplyWeaponLocationAddAsync",
                "ExpectedContentRevision",
                "_presenter.ApplyWeaponLocationAddAsync",
                "_presenter.SaveAsync",
            )
            and _contains(
                request,
                "WeaponLocationAddRequest",
                "ExpectedContentRevision",
                "MaximumNameLength = 32767",
                "ValidateName",
            )
            and _contains(
                mutation,
                "ApplyWeaponLocationAdd",
                'root.Element("weaponlocations")',
                'new XElement("weaponlocations")',
                "Guid.NewGuid()",
                'new XElement("notes", string.Empty)',
            )
            and _contains(
                presenter,
                "ApplyWeaponLocationAddAsync",
                "ApplyWorkspaceXmlMutationAsync",
                "ExpectedContentRevision",
            )
            and _contains(
                presenter_interface,
                "ApplyWeaponLocationAddAsync",
                "WeaponLocationAddRequest",
            )
        )
        e2e_scripted = _contains(
            e2e_driver,
            '"journey": "weapon-location-add"',
            '"creationWeaponLocationAdded": "pass"',
            '"creationWorkspaceXmlPersisted": "pass"',
            '"creationProcessRestartPersistence": "pass"',
            '"careerWeaponLocationAdded": "pass"',
            '"careerWorkspaceXmlPersisted": "pass"',
            '"careerProcessRestartPersistence": "pass"',
            '"controls": controls',
        )
        phone_e2e = weapon_location_phone_e2e_receipt if implemented and e2e_scripted else None
        return {
            "status": (
                "implemented_verified_api36"
                if phone_e2e
                else "implemented_pending_emulator" if implemented else "partial_create_only"
            ),
            "route": "Build > Gear > Weapon Locations > Add weapon location",
            "surface": "WeaponLocationAddPage",
            "automationId": "weapon-location-add",
            "sourceRefs": [
                "src/Chummer.Android/Native/WeaponLocationAddPage.cs",
                "src/Chummer.Android/Native/BuildFlowPages.cs",
                "src/Chummer.Android/Native/RunnerSessionCoordinator.cs",
                "chummer-presentation/Chummer.Presentation/Overview/WeaponLocationAddRequest.cs",
                "chummer-presentation/Chummer.Presentation/Overview/WorkspaceXmlMutationCatalog.cs",
                "chummer-presentation/Chummer.Presentation/Overview/CharacterOverviewPresenter.WorkspaceMutations.cs",
                "chummer-presentation/Chummer.Presentation/Overview/ICharacterOverviewPresenter.cs",
            ],
            "presenterMutation": (
                "ICharacterOverviewPresenter.ApplyWeaponLocationAddAsync(WeaponLocationAddRequest)"
            ),
            "persistenceAssertion": (
                "one new stable-guid character/weaponlocations/location preserves the exact "
                "nonempty name and empty notes while existing locations remain unchanged after "
                "save, same-session reopen, and process restart"
            ),
            "e2e": phone_e2e or {
                "status": "scripted_not_executed" if e2e_scripted else "missing",
                "ref": "tests/run_api36_weapon_location_e2e.py" if e2e_scripted else None,
            },
            "coverageLimit": (
                "Exact Chummer5 top-level WeaponLocations add only; selected-vehicle nested "
                "locations and item-into-location actions remain separately inventoried."
            ),
        }
    if (
        class_name in {"CharacterCreate", "CharacterCareer"}
        and control == VEHICLE_LOCATION_ADD_CONTROL
    ):
        page = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "VehicleLocationAddPage.cs"
        flow = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "BuildFlowPages.cs"
        editor = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "CollectionEditorPages.cs"
        coordinator = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "RunnerSessionCoordinator.cs"
        e2e_driver = REPO_ROOT / "tests" / "run_api36_vehicle_location_e2e.py"
        overview = presentation_root / "Chummer.Presentation" / "Overview"
        request = overview / "VehicleLocationAddRequest.cs"
        state = overview / "WorkspaceCollectionEditorState.cs"
        projector = overview / "WorkspaceCollectionEditorProjector.cs"
        mutation = overview / "WorkspaceXmlMutationCatalog.cs"
        presenter = overview / "CharacterOverviewPresenter.WorkspaceMutations.cs"
        presenter_interface = overview / "ICharacterOverviewPresenter.cs"
        core_models = character_notes_core_root / "Chummer.Contracts" / "Characters" / "CharacterSectionModels.cs"
        core_service = character_notes_core_root / "Chummer.Infrastructure" / "Xml" / "CharacterSectionService.cs"
        legacy_source = (
            presentation_root
            / "Chummer"
            / "Forms"
            / "Character Forms"
            / f"{class_name}.cs"
        )
        expected_handler = "cmdAddVehicleLocation_Click"
        legacy_exact = (
            any(event.get("handler") == expected_handler for event in legacy.get("events", []))
            and _contains(
                legacy_source,
                expected_handler,
                "if (objSelected is Vehicle objVehicle)",
                "destCollection = objVehicle.Locations;",
                'objSelected.ToString() == "Node_SelectedVehicles"',
                "destCollection = CharacterObject.VehicleLocations;",
                "new Location(CharacterObject, destCollection, frmPickText.MyForm.SelectedValue)",
                "destCollection.AddAsync(objLocation",
                "string.IsNullOrEmpty(frmPickText.MyForm.SelectedValue)",
            )
        )
        implemented = (
            legacy_exact
            and _contains(
                page,
                'vehicleId?.ToString("N") ?? "global"',
                'AutomationId = $"vehicle-location-add-page-{targetToken}"',
                'AutomationId = $"vehicle-location-name-{targetToken}"',
                'AutomationId = $"vehicle-location-add-{targetToken}"',
                "VehicleLocationAddRequest.MaximumNameLength",
                "Coordinator.ApplyVehicleLocationAddAsync",
            )
            and _contains(
                flow,
                'case "vehiclelocations"',
                'automationId: "vehicle-location-open-add-global"',
                "new VehicleLocationAddPage",
                "vehicleId: null",
            )
            and _contains(
                editor,
                "item.VehicleLocations is null",
                'Guid.TryParseExact(_target.ItemId, "D"',
                'automationId: $"vehicle-location-open-add-{vehicleId:N}"',
                "new VehicleLocationAddPage",
            )
            and _contains(
                coordinator,
                "ApplyVehicleLocationAddAsync",
                "ExpectedContentRevision",
                "_presenter.ApplyVehicleLocationAddAsync",
                "_presenter.SaveAsync",
            )
            and _contains(
                request,
                "VehicleLocationAddRequest",
                "Guid? VehicleId",
                "ExpectedContentRevision",
                "MaximumNameLength = 32767",
                "ValidateName",
            )
            and _contains(state, "VehicleLocations", "WorkspaceLocationItemState")
            and _contains(
                projector,
                '"locationCount"',
                '"locations"',
                "TryProjectVehicleLocations",
                'Guid.TryParseExact(target.ItemId, "D"',
                "identities.Add(id)",
            )
            and _contains(
                core_models,
                "CharacterVehicleSummary",
                "int LocationCount = 0",
                "IReadOnlyList<CharacterLocationSummary>? Locations = null",
            )
            and _contains(
                core_service,
                'item.Element("locations")',
                "CharacterLocationSummary[] locations",
                "LocationCount: locations.Length",
                "Locations: locations",
            )
            and _contains(
                mutation,
                "ApplyVehicleLocationAdd",
                'string containerName = "vehiclelocations"',
                "request.VehicleId is { } vehicleId",
                'FindUniqueItemById(vehicles, "vehicle"',
                'containerName = "locations"',
                "Guid.NewGuid()",
                'new XElement("notes", string.Empty)',
            )
            and _contains(
                presenter,
                "ApplyVehicleLocationAddAsync",
                "ApplyWorkspaceXmlMutationAsync",
                "ExpectedContentRevision",
            )
            and _contains(
                presenter_interface,
                "ApplyVehicleLocationAddAsync",
                "VehicleLocationAddRequest",
            )
        )
        e2e_scripted = _contains(
            e2e_driver,
            '"journey": "vehicle-location-add"',
            '"creationGlobalVehicleLocationAdded": "pass"',
            '"creationSelectedVehicleLocationAdded": "pass"',
            '"creationBothBranchesWorkspaceXmlPersisted": "pass"',
            '"creationProcessRestartPersistence": "pass"',
            '"careerGlobalVehicleLocationAdded": "pass"',
            '"careerSelectedVehicleLocationAdded": "pass"',
            '"careerBothBranchesWorkspaceXmlPersisted": "pass"',
            '"careerProcessRestartPersistence": "pass"',
            '"controls": controls',
        )
        phone_e2e = vehicle_location_phone_e2e_receipt if implemented and e2e_scripted else None
        return {
            "status": (
                "implemented_verified_api36"
                if phone_e2e
                else "implemented_pending_emulator" if implemented else "partial_create_only"
            ),
            "route": (
                "Build > Gear > Vehicle Locations > Add vehicle location OR "
                "Build > Gear > Vehicles > selected stable vehicle > Add location to vehicle"
            ),
            "surface": "VehicleLocationAddPage",
            "automationId": "vehicle-location-add-{global|stable-vehicle-guid}",
            "sourceRefs": [
                "src/Chummer.Android/Native/VehicleLocationAddPage.cs",
                "src/Chummer.Android/Native/BuildFlowPages.cs",
                "src/Chummer.Android/Native/CollectionEditorPages.cs",
                "src/Chummer.Android/Native/RunnerSessionCoordinator.cs",
                "chummer-presentation/Chummer.Presentation/Overview/VehicleLocationAddRequest.cs",
                "chummer-presentation/Chummer.Presentation/Overview/WorkspaceCollectionEditorState.cs",
                "chummer-presentation/Chummer.Presentation/Overview/WorkspaceCollectionEditorProjector.cs",
                "chummer-presentation/Chummer.Presentation/Overview/WorkspaceXmlMutationCatalog.cs",
                "chummer-presentation/Chummer.Presentation/Overview/CharacterOverviewPresenter.WorkspaceMutations.cs",
                "chummer-presentation/Chummer.Presentation/Overview/ICharacterOverviewPresenter.cs",
                "chummer-core-engine/Chummer.Contracts/Characters/CharacterSectionModels.cs",
                "chummer-core-engine/Chummer.Infrastructure/Xml/CharacterSectionService.cs",
            ],
            "presenterMutation": (
                "ICharacterOverviewPresenter.ApplyVehicleLocationAddAsync(VehicleLocationAddRequest) "
                "with null for global or stable vehicle Guid for selected-vehicle ownership"
            ),
            "persistenceAssertion": (
                "one new stable-guid character/vehiclelocations/location and one new stable-guid "
                "character/vehicles/vehicle[stable Guid]/locations/location preserve exact nonempty "
                "names and empty notes; existing global, target-vehicle, untouched-vehicle, and "
                "unrelated data remain unchanged after save, both same-session surface reopens, "
                "and process restart"
            ),
            "e2e": phone_e2e or {
                "status": "scripted_not_executed" if e2e_scripted else "missing",
                "ref": "tests/run_api36_vehicle_location_e2e.py" if e2e_scripted else None,
            },
            "coverageLimit": (
                "Exact Chummer5 global and selected-vehicle location-add branches; moving vehicles "
                "into locations and rename/delete actions remain separately inventoried."
            ),
        }
    if (
        class_name in {"CharacterCreate", "CharacterCareer"}
        and control in LOCATION_RENAME_CONTROLS
    ):
        kind, section_id = LOCATION_RENAME_CONTROLS[control]
        page = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "LocationRenamePage.cs"
        flow = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "BuildFlowPages.cs"
        coordinator = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "RunnerSessionCoordinator.cs"
        e2e_driver = REPO_ROOT / "tests" / "run_api36_location_rename_e2e.py"
        overview = presentation_root / "Chummer.Presentation" / "Overview"
        state = overview / "WorkspaceLocationEditorState.cs"
        request = overview / "LocationRenameRequest.cs"
        mutation = overview / "WorkspaceXmlMutationCatalog.cs"
        presenter = overview / "CharacterOverviewPresenter.WorkspaceMutations.cs"
        presenter_interface = overview / "ICharacterOverviewPresenter.cs"
        renderer = overview / "WorkspaceSectionRenderer.cs"
        overview_state = overview / "CharacterOverviewState.cs"
        legacy_source = (
            presentation_root
            / "Chummer"
            / "Forms"
            / "Character Forms"
            / f"{class_name}.cs"
        )
        expected_handler = f"{control}_Click"
        legacy_exact = (
            any(event.get("handler") == expected_handler for event in legacy.get("events", []))
            and _contains(
                legacy_source,
                expected_handler,
                "objSelectedNode?.Tag is Location objLocation",
                "DefaultString = objLocation.Name",
                "objLocation.Name = frmPickText.MyForm.SelectedValue",
                "SetDirty(true)",
            )
        )
        implemented = (
            legacy_exact
            and _contains(
                page,
                'AutomationId = "location-rename-page"',
                'AutomationId = "location-rename-name"',
                'AutomationId = "location-rename-save"',
                "LocationRenameRequest.MaximumNameLength",
                "Coordinator.ApplyLocationRenameAsync",
            )
            and _contains(
                flow,
                "ActiveLocationEditor",
                "WorkspaceLocationItemState",
                "new LocationRenamePage",
                'automationId: $"location-rename-open-',
            )
            and _contains(
                coordinator,
                "ApplyLocationRenameAsync",
                "ExpectedContentRevision",
                "_presenter.ApplyLocationRenameAsync",
                "_presenter.SaveAsync",
            )
            and _contains(
                state,
                f"WorkspaceLocationKind.{kind} => \"{section_id}\"",
                "Guid Id",
                "Guid.TryParseExact",
                "declaredCount != locations.Count",
            )
            and _contains(
                request,
                "LocationRenameRequest",
                "ExpectedContentRevision",
                "Guid LocationId",
                "MaximumNameLength = 32767",
                "ValidateName",
            )
            and _contains(
                mutation,
                "ApplyLocationRename",
                "WorkspaceLocationEditorProjector.SectionId",
                "FindUniqueItemById",
                'SetElementValue(location, "name", name)',
            )
            and _contains(
                presenter,
                "ApplyLocationRenameAsync",
                "ApplyWorkspaceXmlMutationAsync",
                "ExpectedContentRevision",
            )
            and _contains(
                presenter_interface,
                "ApplyLocationRenameAsync",
                "LocationRenameRequest",
            )
            and _contains(
                renderer,
                "WorkspaceLocationEditorProjector.TryProject",
                "ActiveLocationEditor: locationEditor",
            )
            and _contains(overview_state, "WorkspaceLocationEditorState? ActiveLocationEditor")
        )
        e2e_scripted = _contains(
            e2e_driver,
            '"journey": "location-rename"',
            '"creationAllLocationsRenamed": "pass"',
            '"creationWorkspaceXmlPersisted": "pass"',
            '"creationProcessRestartPersistence": "pass"',
            '"careerAllLocationsRenamed": "pass"',
            '"careerWorkspaceXmlPersisted": "pass"',
            '"careerProcessRestartPersistence": "pass"',
            '"controlCount": len(controls)',
        )
        phone_e2e = location_rename_phone_e2e_receipt if implemented and e2e_scripted else None
        return {
            "status": (
                "implemented_verified_api36"
                if phone_e2e
                else "implemented_pending_emulator" if implemented else "missing"
            ),
            "route": f"Build > Gear > {kind} Locations > selected stable location > Rename",
            "surface": "LocationRenamePage",
            "automationId": "location-rename-save",
            "sourceRefs": [
                "src/Chummer.Android/Native/LocationRenamePage.cs",
                "src/Chummer.Android/Native/BuildFlowPages.cs",
                "src/Chummer.Android/Native/RunnerSessionCoordinator.cs",
                "chummer-presentation/Chummer.Presentation/Overview/WorkspaceLocationEditorState.cs",
                "chummer-presentation/Chummer.Presentation/Overview/LocationRenameRequest.cs",
                "chummer-presentation/Chummer.Presentation/Overview/WorkspaceSectionRenderer.cs",
                "chummer-presentation/Chummer.Presentation/Overview/WorkspaceXmlMutationCatalog.cs",
                "chummer-presentation/Chummer.Presentation/Overview/CharacterOverviewPresenter.WorkspaceMutations.cs",
                "chummer-presentation/Chummer.Presentation/Overview/ICharacterOverviewPresenter.cs",
            ],
            "presenterMutation": (
                "ICharacterOverviewPresenter.ApplyLocationRenameAsync(LocationRenameRequest) "
                f"with WorkspaceLocationKind.{kind} and stable Guid identity"
            ),
            "persistenceAssertion": (
                f"the stable-guid character/{section_id}/location keeps its identity and notes while "
                "its exact nonempty name persists after save, same-session reopen, and process restart"
            ),
            "e2e": phone_e2e or {
                "status": "scripted_not_executed" if e2e_scripted else "missing",
                "ref": "tests/run_api36_location_rename_e2e.py" if e2e_scripted else None,
            },
            "coverageLimit": (
                "Exact Chummer5 top-level Gear/Weapon/Armor/Vehicle Location rename only; "
                "improvement-location and tablet actions remain separately inventoried."
            ),
        }
    if class_name in {"CharacterCreate", "CharacterCareer"} and control in SITUATIONAL_MODIFIER_CONTROLS:
        xml_element, automation_id, property_name = SITUATIONAL_MODIFIER_CONTROLS[control]
        page = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "SituationalModifiersPage.cs"
        build_page = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "BuildPage.cs"
        coordinator = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "RunnerSessionCoordinator.cs"
        e2e_driver = REPO_ROOT / "tests" / "run_api36_situational_modifiers_e2e.py"
        presentation_overview = presentation_root / "Chummer.Presentation" / "Overview"
        request = presentation_overview / "SituationalModifiersEditRequest.cs"
        mutation = presentation_overview / "WorkspaceXmlMutationCatalog.cs"
        presenter = presentation_overview / "CharacterOverviewPresenter.WorkspaceMutations.cs"
        core_contract = character_notes_core_root / "Chummer.Contracts" / "Characters" / "CharacterSectionModels.cs"
        core_section = character_notes_core_root / "Chummer.Infrastructure" / "Xml" / "CharacterSectionService.cs"
        core_proof = (
            _contains(core_contract, "int CurrentCounterspellingDice,")
            and _contains(core_section, 'ReadValue(character, "currentcounterspellingdice")')
            if control == "nudCounterspellingDice"
            else _contains(core_contract, "public int CurrentLiftCarryHits")
            and _contains(core_section, 'ReadValue(character, "currentliftcarryhits")')
        )
        implemented = (
            _contains(page, f'"{automation_id}"', '"situational-modifiers-save"', "SituationalModifiersEditRequest")
            and _contains(build_page, '"build-situational-modifiers"', "new SituationalModifiersPage")
            and _contains(
                coordinator,
                "PrepareSituationalModifiersEditAsync",
                "ApplySituationalModifiersEditAsync",
                "ExpectedContentRevision",
                "SaveAsync",
            )
            and _contains(
                request,
                "SituationalModifiersEditorState",
                "SituationalModifiersEditRequest",
                "ExpectedContentRevision",
                f"int {property_name}",
            )
            and _contains(
                mutation,
                "ApplySituationalModifiersEdit",
                "MaximumSituationalModifier = 100",
                f'"{xml_element}"',
            )
            and _contains(
                presenter,
                "PrepareSituationalModifiersEditAsync",
                "ApplySituationalModifiersEditAsync",
                "ApplyWorkspaceXmlMutationAsync",
            )
            and core_proof
        )
        e2e_scripted = _contains(
            e2e_driver,
            '"journey": "situational-modifiers"',
            '"allCreationSituationalModifiersEdited": "pass"',
            '"creationWorkspaceXmlPersisted": "pass"',
            '"creationProcessRestartUiReadback": "pass"',
            '"allCareerSituationalModifiersEdited": "pass"',
            '"careerWorkspaceXmlPersisted": "pass"',
            '"careerProcessRestartUiReadback": "pass"',
            '"controls": controls',
        )
        phone_e2e = situational_modifiers_phone_e2e_receipt if implemented and e2e_scripted else None
        return {
            "status": "implemented_verified_api36" if phone_e2e else "implemented_pending_emulator" if implemented else "missing",
            "route": "Build > Situational modifiers",
            "surface": "SituationalModifiersPage",
            "automationId": automation_id,
            "sourceRefs": [
                "src/Chummer.Android/Native/SituationalModifiersPage.cs",
                "src/Chummer.Android/Native/BuildPage.cs",
                "src/Chummer.Android/Native/RunnerSessionCoordinator.cs",
                "chummer-presentation/Chummer.Presentation/Overview/SituationalModifiersEditRequest.cs",
                "chummer-presentation/Chummer.Presentation/Overview/WorkspaceXmlMutationCatalog.cs",
                "chummer-presentation/Chummer.Presentation/Overview/CharacterOverviewPresenter.WorkspaceMutations.cs",
                "chummer-core-engine/Chummer.Contracts/Characters/CharacterSectionModels.cs",
                "chummer-core-engine/Chummer.Infrastructure/Xml/CharacterSectionService.cs",
            ],
            "presenterMutation": (
                "ICharacterOverviewPresenter.ApplySituationalModifiersEditAsync"
                "(SituationalModifiersEditRequest)"
            ),
            "persistenceAssertion": (
                f"character/{xml_element} equals the submitted inclusive 0-100 value after save, "
                "same-session reopen, and process restart"
            ),
            "e2e": phone_e2e or {
                "status": "scripted_not_executed" if e2e_scripted else "missing",
                "ref": "tests/run_api36_situational_modifiers_e2e.py" if e2e_scripted else None,
            },
        }
    if class_name == "CharacterCareer" and control == BURN_STREET_CRED_CONTROL:
        page = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "CareerReputationPage.cs"
        build_page = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "BuildPage.cs"
        coordinator = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "RunnerSessionCoordinator.cs"
        e2e_driver = REPO_ROOT / "tests" / "run_api36_career_reputation_e2e.py"
        presentation_overview = presentation_root / "Chummer.Presentation" / "Overview"
        request = presentation_overview / "CareerReputationEditRequest.cs"
        mutation = presentation_overview / "WorkspaceXmlMutationCatalog.cs"
        presenter = presentation_overview / "CharacterOverviewPresenter.WorkspaceMutations.cs"
        implemented = (
            _contains(
                page,
                '"career-reputation-burn-street-cred"',
                'DisplayAlertAsync(',
                '"Burn"',
                "BurnStreetCredRequest",
            )
            and _contains(build_page, '"build-career-reputation"', "new CareerReputationPage")
            and _contains(
                coordinator,
                "ApplyBurnStreetCredAsync",
                "ExpectedContentRevision",
                "SaveAsync",
            )
            and _contains(
                request,
                "BurnStreetCredRequest",
                "CareerStreetCredRules",
                "CanBurnStreetCred",
                "TotalStreetCred",
                "ExpectedContentRevision",
            )
            and _contains(
                mutation,
                "ApplyBurnStreetCred",
                "CareerStreetCredRules.Project(root)",
                'SetElementValue(root, "burntstreetcred"',
            )
            and _contains(
                presenter,
                "ApplyBurnStreetCredAsync",
                "ApplyWorkspaceXmlMutationAsync",
            )
        )
        e2e_scripted = _contains(
            e2e_driver,
            '"journey": "career-reputation"',
            'BURN_CONTROL = "cmdBurnStreetCred"',
            '"streetCredBurnConfirmed": "pass"',
            '"burntStreetCredIncrementedByTwo": "pass"',
            '"burntstreetcred": "2"',
        )
        phone_e2e = career_reputation_phone_e2e_receipt if implemented and e2e_scripted else None
        return {
            "status": "implemented_verified_api36" if phone_e2e else "implemented_pending_emulator" if implemented else "missing",
            "route": "Build > Reputation > Burn 2 Street Cred",
            "surface": "CareerReputationPage",
            "automationId": "career-reputation-burn-street-cred",
            "sourceRefs": [
                "src/Chummer.Android/Native/CareerReputationPage.cs",
                "src/Chummer.Android/Native/BuildPage.cs",
                "src/Chummer.Android/Native/RunnerSessionCoordinator.cs",
                "chummer-presentation/Chummer.Presentation/Overview/CareerReputationEditRequest.cs",
                "chummer-presentation/Chummer.Presentation/Overview/WorkspaceXmlMutationCatalog.cs",
                "chummer-presentation/Chummer.Presentation/Overview/CharacterOverviewPresenter.WorkspaceMutations.cs",
            ],
            "presenterMutation": "ICharacterOverviewPresenter.ApplyBurnStreetCredAsync(BurnStreetCredRequest)",
            "persistenceAssertion": (
                "career character/burntstreetcred increases by exactly 2 after confirmation, "
                "save, same-session reopen, and process restart"
            ),
            "e2e": phone_e2e or {
                "status": "scripted_not_executed" if e2e_scripted else "missing",
                "ref": e2e_driver.relative_to(REPO_ROOT).as_posix() if e2e_scripted else None,
            },
            "tablet": {
                "status": "missing",
                "surface": None,
                "automationId": None,
                "sourceRefs": [],
            },
            "tabletE2e": {"status": "missing", "ref": None},
        }
    if class_name == "CharacterCareer" and control in CAREER_REPUTATION_CONTROLS:
        xml_element, automation_id, property_name = CAREER_REPUTATION_CONTROLS[control]
        page = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "CareerReputationPage.cs"
        build_page = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "BuildPage.cs"
        coordinator = REPO_ROOT / "src" / "Chummer.Android" / "Native" / "RunnerSessionCoordinator.cs"
        e2e_driver = REPO_ROOT / "tests" / "run_api36_career_reputation_e2e.py"
        presentation_overview = presentation_root / "Chummer.Presentation" / "Overview"
        request = presentation_overview / "CareerReputationEditRequest.cs"
        mutation = presentation_overview / "WorkspaceXmlMutationCatalog.cs"
        presenter = presentation_overview / "CharacterOverviewPresenter.WorkspaceMutations.cs"
        core_contract = character_notes_core_root / "Chummer.Contracts" / "Characters" / "CharacterSectionModels.cs"
        core_section = character_notes_core_root / "Chummer.Infrastructure" / "Xml" / "CharacterSectionService.cs"
        source_contract = character_notes_core_root / "Chummer.Application" / "Characters" / "ICharacterSourceDataResolver.cs"
        source_resolver = character_notes_core_root / "Chummer.Infrastructure" / "Xml" / "FileSystemCharacterSourceDataResolver.cs"
        core_property_marker = (
            f"public int {property_name}"
            if control in {"nudAstralReputation", "nudWildReputation"}
            else f"int {property_name},"
        )
        request_property_marker = (
            f"int? {property_name}"
            if control in {"nudAstralReputation", "nudWildReputation"}
            else f"int {property_name}"
        )
        source_book_rule = (
            True
            if control not in {"nudAstralReputation", "nudWildReputation"}
            else _contains(
                request,
                'IsBookEnabled(sourceData, "FA")',
                'IsBookEnabled(sourceData, "SG")' if control == "nudAstralReputation" else 'WildReputationVisible: forbiddenArcana',
            )
            and _contains(source_contract, "TryIsBookEnabled")
            and _contains(source_resolver, 'Element("books")', "_enabledSourcebooks")
        )
        implemented = (
            _contains(page, f'"{automation_id}"', '"career-reputation-save"', "CareerReputationEditRequest")
            and _contains(build_page, '"build-career-reputation"', "new CareerReputationPage")
            and _contains(
                coordinator,
                "PrepareCareerReputationEditAsync",
                "ApplyCareerReputationEditAsync",
                "ExpectedContentRevision",
                "SaveAsync",
            )
            and _contains(
                request,
                "CareerReputationEditorState",
                "CareerReputationEditRequest",
                "ExpectedContentRevision",
                request_property_marker,
            )
            and _contains(
                mutation,
                "ApplyCareerReputationEdit",
                'ParseBool(root.Element("created")?.Value)',
                "MaximumCareerReputation = 100",
                f'SetElementValue(root, "{xml_element}"',
            )
            and _contains(
                presenter,
                "PrepareCareerReputationEditAsync",
                "ApplyCareerReputationEditAsync",
                "ApplyWorkspaceXmlMutationAsync",
            )
            and _contains(core_contract, core_property_marker)
            and _contains(core_section, f'ReadValue(character, "{xml_element}")')
            and source_book_rule
        )
        e2e_scripted = _contains(
            e2e_driver,
            '"journey": "career-reputation"',
            '"coreOnlySourceVisibilityEnforced": "pass"',
            '"allCareerReputationEdited": "pass"',
            '"careerWorkspaceXmlPersisted": "pass"',
            '"careerUiReopenReadback": "pass"',
            '"careerProcessRestartUiReadback": "pass"',
            '"controls": controls',
        )
        phone_e2e = career_reputation_phone_e2e_receipt if implemented and e2e_scripted else None
        return {
            "status": "implemented_verified_api36" if phone_e2e else "implemented_pending_emulator" if implemented else "missing",
            "route": "Build > Reputation",
            "surface": "CareerReputationPage",
            "automationId": automation_id,
            "sourceRefs": [
                "src/Chummer.Android/Native/CareerReputationPage.cs",
                "src/Chummer.Android/Native/BuildPage.cs",
                "src/Chummer.Android/Native/RunnerSessionCoordinator.cs",
                "chummer-presentation/Chummer.Presentation/Overview/CareerReputationEditRequest.cs",
                "chummer-presentation/Chummer.Presentation/Overview/WorkspaceXmlMutationCatalog.cs",
                "chummer-presentation/Chummer.Presentation/Overview/CharacterOverviewPresenter.WorkspaceMutations.cs",
                "chummer-core-engine/Chummer.Contracts/Characters/CharacterSectionModels.cs",
                "chummer-core-engine/Chummer.Infrastructure/Xml/CharacterSectionService.cs",
                "chummer-core-engine/Chummer.Application/Characters/ICharacterSourceDataResolver.cs",
                "chummer-core-engine/Chummer.Infrastructure/Xml/FileSystemCharacterSourceDataResolver.cs",
            ],
            "presenterMutation": "ICharacterOverviewPresenter.ApplyCareerReputationEditAsync(CareerReputationEditRequest)",
            "persistenceAssertion": (
                f"career character/{xml_element} equals the submitted 0..100 value after save, reopen, and process restart"
            ),
            "e2e": phone_e2e or {
                "status": "scripted_not_executed" if e2e_scripted else "missing",
                "ref": e2e_driver.relative_to(REPO_ROOT).as_posix() if e2e_scripted else None,
            },
            "tablet": {
                "status": "missing",
                "surface": None,
                "automationId": None,
                "sourceRefs": [],
            },
            "tabletE2e": {"status": "missing", "ref": None},
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
    dynamic_character_condition = (
        class_name == "CharacterCareer"
        and control == DYNAMIC_CHARACTER_CONDITION_CONTROL
    )
    dashboard_condition = (
        DASHBOARD_CONDITION_CONTROLS.get(control)
        if class_name == "ConditionMonitorUserControl"
        else None
    )
    if (
        class_name == "CharacterCareer"
        and (character_condition_match is not None or dynamic_character_condition)
    ) or dashboard_condition is not None:
        if dashboard_condition is not None:
            track, expected_handler = dashboard_condition
            tracks = (track,)
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
        elif dynamic_character_condition:
            if not any(
                event.get("event") == "Click"
                and event.get("handler") == DYNAMIC_CHARACTER_CONDITION_HANDLER
                for event in legacy.get("events", [])
            ):
                return None
            legacy_character = (
                presentation_root
                / "Chummer"
                / "Forms"
                / "Character Forms"
                / "CharacterCareer.cs"
            )
            if not _contains(
                legacy_character,
                "ProcessCharacterConditionMonitorBoxDisplays(",
                "DpiFriendlyCheckBoxDisguisedAsButton cb",
                "cb.Click += evtButtonClickEvent;",
                "chkPhysicalCM_CheckedChanged",
                "chkStunCM_CheckedChanged",
                "SetPhysicalCMFilledAsync",
                "SetStunCMFilledAsync",
            ):
                return None
            tracks = ("Physical", "Stun")
        else:
            track = character_condition_match.group("track")
            expected_handler = CHARACTER_CONDITION_HANDLERS[track]
            if not any(event.get("handler") == expected_handler for event in legacy.get("events", [])):
                return None
            tracks = (track,)

        tokens = tuple(track_name.lower() for track_name in tracks)
        xml_elements = tuple(f"{token}cmfilled" for token in tokens)
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
            all(
                _contains(request, "ConditionMonitorEditRequest", track_name)
                and _contains(state, "ConditionMonitorEditorProjector", track_name)
                and _contains(mutation, "ApplyConditionMonitorEdit", xml_element)
                for track_name, xml_element in zip(tracks, xml_elements, strict=True)
            )
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
            *(f'"{token}ConditionDamageEditPersisted": "pass"' for token in tokens),
            '"processRestartConditionDamagePersistence": "pass"',
        )
        phone_e2e = condition_e2e_receipts.get("phone") if e2e_scripted else None
        tablet_e2e = condition_e2e_receipts.get("tablet") if e2e_scripted else None
        condition_e2e_complete = bool(
            phone_implemented and tablet_implemented and phone_e2e and tablet_e2e
        )
        track_label = " / ".join(tracks)
        phone_automation_id = (
            "condition-monitor-filled-{physical|stun}"
            if dynamic_character_condition
            else f"condition-monitor-filled-{tokens[0]}"
        )
        tablet_automation_id = (
            "tablet-condition-filled-{physical|stun}"
            if dynamic_character_condition
            else f"tablet-condition-filled-{tokens[0]}"
        )
        persistence_assertion = (
            "character/physicalcmfilled and character/stuncmfilled each equal the corresponding "
            "chosen box count after reopen and process restart"
            if dynamic_character_condition
            else f"character/{xml_elements[0]} equals the chosen box count after reopen and process restart"
        )
        mapping = {
            "status": (
                "implemented_verified_api36"
                if phone_implemented and condition_e2e_complete
                else "implemented_pending_emulator" if phone_implemented else "missing"
            ),
            "route": f"Build > Combat > Damage tracks > {track_label}",
            "surface": "ConditionMonitorEditPage",
            "automationId": phone_automation_id,
            "sourceRefs": [
                "src/Chummer.Android/Native/BuildFlowPages.cs",
                "src/Chummer.Android/Native/ConditionMonitorEditPage.cs",
                "src/Chummer.Android/Native/RunnerSessionCoordinator.cs",
                "chummer-presentation/Chummer.Presentation/Overview/ConditionMonitorEditRequest.cs",
                "chummer-presentation/Chummer.Presentation/Overview/ConditionMonitorEditorState.cs",
                "chummer-presentation/Chummer.Presentation/Overview/WorkspaceXmlMutationCatalog.cs",
            ],
            "presenterMutation": "ICharacterOverviewPresenter.ApplyConditionMonitorEditAsync",
            "persistenceAssertion": persistence_assertion,
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
                "automationId": tablet_automation_id,
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
        if dynamic_character_condition:
            mapping["coverageLimit"] = (
                "The single synthetic Chummer5 runtime row represents every additional Physical "
                "and Stun checkbox created beyond the designer controls; parity is the exact "
                "filled-count effect for both tracks, not a one-widget-per-generated-box layout."
            )
        return mapping
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
    chummer5_root: Path,
    presentation_root: Path,
    core_engine_root: Path,
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
    character_notes_phone_e2e_receipt = _validated_character_notes_phone_e2e_receipt(
        presentation_root,
        core_engine_root,
    )
    career_reputation_phone_e2e_receipt = _validated_career_reputation_phone_e2e_receipt(
        presentation_root,
        core_engine_root,
    )
    situational_modifiers_phone_e2e_receipt = _validated_situational_modifiers_phone_e2e_receipt(
        presentation_root,
        core_engine_root,
    )
    primary_arm_phone_e2e_receipt = _validated_primary_arm_phone_e2e_receipt(
        presentation_root,
        core_engine_root,
    )
    gear_location_phone_e2e_receipt = _validated_gear_location_phone_e2e_receipt(
        presentation_root,
    )
    weapon_location_phone_e2e_receipt = _validated_weapon_location_phone_e2e_receipt(
        presentation_root,
    )
    vehicle_location_phone_e2e_receipt = _validated_vehicle_location_phone_e2e_receipt(
        presentation_root,
        core_engine_root,
    )
    vehicle_home_node_phone_e2e_receipt = _validated_vehicle_home_node_phone_e2e_receipt(
        presentation_root,
        core_engine_root,
    )
    armor_home_node_phone_e2e_receipt = _validated_armor_home_node_phone_e2e_receipt(
        presentation_root,
        core_engine_root,
    )
    weapon_home_node_phone_e2e_receipt = _validated_weapon_home_node_phone_e2e_receipt(
        presentation_root,
        core_engine_root,
    )
    weapon_active_commlink_phone_e2e_receipt = _validated_weapon_active_commlink_phone_e2e_receipt(
        presentation_root,
        core_engine_root,
    )
    armor_active_commlink_phone_e2e_receipt = _validated_armor_active_commlink_phone_e2e_receipt(
        presentation_root,
        core_engine_root,
    )
    armor_damage_phone_e2e_receipt = _validated_armor_damage_phone_e2e_receipt(
        presentation_root,
        core_engine_root,
    )
    armor_equipment_phone_e2e_receipt = _validated_armor_equipment_phone_e2e_receipt(
        presentation_root,
        core_engine_root,
    )
    weapon_accessory_included_phone_e2e_receipt = _validated_weapon_accessory_included_phone_e2e_receipt(
        presentation_root,
        core_engine_root,
    )
    critter_power_count_phone_e2e_receipt = _validated_critter_power_count_phone_e2e_receipt(
        presentation_root,
        core_engine_root,
    )
    location_rename_phone_e2e_receipt = _validated_location_rename_phone_e2e_receipt(
        presentation_root,
    )
    explicit_save_phone_e2e_receipt = _validated_explicit_save_phone_e2e_receipt(
        presentation_root,
    )
    nested_collection_notes_phone_e2e_receipt = _validated_nested_collection_notes_phone_e2e_receipt(
        presentation_root,
        core_engine_root,
    )
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
            chummer5_root,
            presentation_root,
            core_engine_root,
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
            character_notes_phone_e2e_receipt,
            career_reputation_phone_e2e_receipt,
            situational_modifiers_phone_e2e_receipt,
            primary_arm_phone_e2e_receipt,
            gear_location_phone_e2e_receipt,
            weapon_location_phone_e2e_receipt,
            vehicle_location_phone_e2e_receipt,
            vehicle_home_node_phone_e2e_receipt,
            armor_home_node_phone_e2e_receipt,
            weapon_home_node_phone_e2e_receipt,
            weapon_active_commlink_phone_e2e_receipt,
            armor_active_commlink_phone_e2e_receipt,
            armor_damage_phone_e2e_receipt,
            armor_equipment_phone_e2e_receipt,
            weapon_accessory_included_phone_e2e_receipt,
            critter_power_count_phone_e2e_receipt,
            location_rename_phone_e2e_receipt,
            explicit_save_phone_e2e_receipt,
            nested_collection_notes_phone_e2e_receipt,
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
    core_engine_root: Path,
) -> dict[str, Any]:
    if not registry_path.is_file():
        raise FileNotFoundError(f"Missing Android parity registry: {registry_path}")
    registry = json.loads(_read_text(registry_path))
    rows, source_summary = extract_legacy_rows(chummer5_root)
    enrich_rows(rows, registry, chummer5_root, presentation_root, core_engine_root)

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
        REPO_ROOT / "src" / "Chummer.Android" / "Native" / "MorePage.cs",
        REPO_ROOT / "src" / "Chummer.Android" / "Native" / "CareerReputationPage.cs",
        REPO_ROOT / "src" / "Chummer.Android" / "Native" / "SituationalModifiersPage.cs",
        REPO_ROOT / "src" / "Chummer.Android" / "Native" / "PrimaryArmPage.cs",
        REPO_ROOT / "src" / "Chummer.Android" / "Native" / "GroupMembershipPage.cs",
        REPO_ROOT / "src" / "Chummer.Android" / "Native" / "GroupNamePage.cs",
        REPO_ROOT / "src" / "Chummer.Android" / "Native" / "TraditionNamePage.cs",
        REPO_ROOT / "src" / "Chummer.Android" / "Native" / "CareerEdgeUsePage.cs",
        REPO_ROOT / "src" / "Chummer.Android" / "Native" / "CareerManualKarmaPage.cs",
        REPO_ROOT / "src" / "Chummer.Android" / "Native" / "GearLocationAddPage.cs",
        REPO_ROOT / "src" / "Chummer.Android" / "Native" / "WeaponLocationAddPage.cs",
        REPO_ROOT / "src" / "Chummer.Android" / "Native" / "VehicleLocationAddPage.cs",
        REPO_ROOT / "src" / "Chummer.Android" / "Native" / "VehicleHomeNodePage.cs",
        REPO_ROOT / "src" / "Chummer.Android" / "Native" / "ArmorHomeNodePage.cs",
        REPO_ROOT / "src" / "Chummer.Android" / "Native" / "WeaponHomeNodePage.cs",
        REPO_ROOT / "src" / "Chummer.Android" / "Native" / "WeaponActiveCommlinkPage.cs",
        REPO_ROOT / "src" / "Chummer.Android" / "Native" / "ArmorActiveCommlinkPage.cs",
        REPO_ROOT / "src" / "Chummer.Android" / "Native" / "ArmorDamagePage.cs",
        REPO_ROOT / "src" / "Chummer.Android" / "Native" / "ArmorEquipmentPage.cs",
        REPO_ROOT / "src" / "Chummer.Android" / "Native" / "WeaponAccessoryIncludedPage.cs",
        REPO_ROOT / "src" / "Chummer.Android" / "Native" / "CritterPowerCountPage.cs",
        REPO_ROOT / "src" / "Chummer.Android" / "Native" / "SpiritFetteredPage.cs",
        REPO_ROOT / "src" / "Chummer.Android" / "Native" / "SustainedObjectsPage.cs",
        REPO_ROOT / "src" / "Chummer.Android" / "Native" / "GearQuantityPage.cs",
        REPO_ROOT / "src" / "Chummer.Android" / "Native" / "QualityLevelPage.cs",
        REPO_ROOT / "src" / "Chummer.Android" / "Native" / "CyberwareCommercePage.cs",
        REPO_ROOT / "src" / "Chummer.Android" / "Native" / "LocationRenamePage.cs",
        REPO_ROOT / "src" / "Chummer.Android" / "Native" / "CharacterNotesPage.cs",
        REPO_ROOT / "src" / "Chummer.Android" / "Native" / "CollectionEditorPages.cs",
        REPO_ROOT / "src" / "Chummer.Android" / "Native" / "ConditionMonitorEditPage.cs",
        REPO_ROOT / "src" / "Chummer.Android" / "Native" / "OriginDossierPage.cs",
        REPO_ROOT / "src" / "Chummer.Android" / "Native" / "NativeDialogPage.cs",
        REPO_ROOT / "src" / "Chummer.Android" / "Native" / "NativeCommandPage.cs",
        REPO_ROOT / "src" / "Chummer.Android" / "Native" / "RunnerSessionCoordinator.cs",
        REPO_ROOT / "src" / "Chummer.Android" / "Native" / "TabletBuildPage.cs",
        REPO_ROOT / "src" / "Chummer.Android" / "Platform" / "IAndroidLinkedCharacterFileService.cs",
        REPO_ROOT / "tests" / "run_api36_editing_e2e.py",
        REPO_ROOT / "tests" / "run_api36_character_notes_e2e.py",
        REPO_ROOT / "tests" / "run_api36_career_reputation_e2e.py",
        REPO_ROOT / "tests" / "run_api36_situational_modifiers_e2e.py",
        REPO_ROOT / "tests" / "run_api36_primary_arm_e2e.py",
        REPO_ROOT / "tests" / "run_api36_group_membership_e2e.py",
        REPO_ROOT / "tests" / "run_api36_group_name_e2e.py",
        REPO_ROOT / "tests" / "run_api36_tradition_name_e2e.py",
        REPO_ROOT / "tests" / "run_api36_career_edge_use_e2e.py",
        REPO_ROOT / "tests" / "run_api36_career_manual_karma_e2e.py",
        REPO_ROOT / "tests" / "run_api36_gear_location_e2e.py",
        REPO_ROOT / "tests" / "run_api36_weapon_location_e2e.py",
        REPO_ROOT / "tests" / "run_api36_vehicle_location_e2e.py",
        REPO_ROOT / "tests" / "run_api36_vehicle_home_node_e2e.py",
        REPO_ROOT / "tests" / "run_api36_armor_home_node_e2e.py",
        REPO_ROOT / "tests" / "run_api36_weapon_home_node_e2e.py",
        REPO_ROOT / "tests" / "run_api36_weapon_active_commlink_e2e.py",
        REPO_ROOT / "tests" / "run_api36_armor_active_commlink_e2e.py",
        REPO_ROOT / "tests" / "run_api36_armor_damage_e2e.py",
        REPO_ROOT / "tests" / "run_api36_armor_equipment_e2e.py",
        REPO_ROOT / "tests" / "run_api36_weapon_accessory_included_e2e.py",
        REPO_ROOT / "tests" / "run_api36_critter_power_count_e2e.py",
        REPO_ROOT / "tests" / "run_api36_spirit_fettered_e2e.py",
        REPO_ROOT / "tests" / "run_api36_sustained_effects_e2e.py",
        REPO_ROOT / "tests" / "run_api36_gear_quantity_e2e.py",
        REPO_ROOT / "tests" / "run_api36_quality_level_e2e.py",
        REPO_ROOT / "tests" / "run_api36_cyberware_commerce_e2e.py",
        REPO_ROOT / "tests" / "run_api36_location_rename_e2e.py",
        REPO_ROOT / "tests" / "run_api36_explicit_save_e2e.py",
        REPO_ROOT / "tests" / "run_api36_nested_collection_notes_e2e.py",
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
        REPO_ROOT / "tests" / "fixtures" / "career-reputation-core-only-e2e.chum5",
        REPO_ROOT / "tests" / "fixtures" / "career-reputation-full-e2e.chum5",
        REPO_ROOT / "tests" / "fixtures" / "creation-situational-modifiers-e2e.chum5",
        REPO_ROOT / "tests" / "fixtures" / "career-situational-modifiers-e2e.chum5",
        REPO_ROOT / "tests" / "fixtures" / "creation-primary-arm-e2e.chum5",
        REPO_ROOT / "tests" / "fixtures" / "career-primary-arm-e2e.chum5",
        REPO_ROOT / "tests" / "fixtures" / "ambidextrous-primary-arm-e2e.chum5",
        REPO_ROOT / "tests" / "fixtures" / "creation-group-membership-e2e.chum5",
        REPO_ROOT / "tests" / "fixtures" / "career-group-membership-e2e.chum5",
        REPO_ROOT / "tests" / "fixtures" / "creation-group-name-e2e.chum5",
        REPO_ROOT / "tests" / "fixtures" / "career-group-name-e2e.chum5",
        REPO_ROOT / "tests" / "fixtures" / "creation-tradition-name-e2e.chum5",
        REPO_ROOT / "tests" / "fixtures" / "career-tradition-name-e2e.chum5",
        REPO_ROOT / "tests" / "fixtures" / "career-edge-use-e2e.chum5",
        REPO_ROOT / "tests" / "fixtures" / "career-manual-karma-e2e.chum5",
        REPO_ROOT / "tests" / "fixtures" / "creation-gear-location-e2e.chum5",
        REPO_ROOT / "tests" / "fixtures" / "career-gear-location-e2e.chum5",
        REPO_ROOT / "tests" / "fixtures" / "creation-weapon-location-e2e.chum5",
        REPO_ROOT / "tests" / "fixtures" / "career-weapon-location-e2e.chum5",
        REPO_ROOT / "tests" / "fixtures" / "creation-vehicle-location-e2e.chum5",
        REPO_ROOT / "tests" / "fixtures" / "career-vehicle-location-e2e.chum5",
        REPO_ROOT / "tests" / "fixtures" / "creation-vehicle-home-node-e2e.chum5",
        REPO_ROOT / "tests" / "fixtures" / "career-vehicle-home-node-e2e.chum5",
        REPO_ROOT / "tests" / "fixtures" / "creation-armor-home-node-e2e.chum5",
        REPO_ROOT / "tests" / "fixtures" / "career-armor-home-node-e2e.chum5",
        REPO_ROOT / "tests" / "fixtures" / "creation-weapon-home-node-e2e.chum5",
        REPO_ROOT / "tests" / "fixtures" / "career-weapon-home-node-e2e.chum5",
        REPO_ROOT / "tests" / "fixtures" / "creation-weapon-active-commlink-e2e.chum5",
        REPO_ROOT / "tests" / "fixtures" / "career-weapon-active-commlink-e2e.chum5",
        REPO_ROOT / "tests" / "fixtures" / "creation-armor-active-commlink-e2e.chum5",
        REPO_ROOT / "tests" / "fixtures" / "career-armor-active-commlink-e2e.chum5",
        REPO_ROOT / "tests" / "fixtures" / "career-armor-damage-e2e.chum5",
        REPO_ROOT / "tests" / "fixtures" / "creation-armor-equipment-e2e.chum5",
        REPO_ROOT / "tests" / "fixtures" / "career-armor-equipment-e2e.chum5",
        REPO_ROOT / "tests" / "fixtures" / "creation-weapon-accessory-included-e2e.chum5",
        REPO_ROOT / "tests" / "fixtures" / "career-weapon-accessory-included-e2e.chum5",
        REPO_ROOT / "tests" / "fixtures" / "creation-critter-power-count-e2e.chum5",
        REPO_ROOT / "tests" / "fixtures" / "career-critter-power-count-e2e.chum5",
        REPO_ROOT / "tests" / "fixtures" / "creation-spirit-fettered-e2e.chum5",
        REPO_ROOT / "tests" / "fixtures" / "career-spirit-fettered-e2e.chum5",
        REPO_ROOT / "tests" / "fixtures" / "creation-sustained-effects-e2e.chum5",
        REPO_ROOT / "tests" / "fixtures" / "career-sustained-effects-e2e.chum5",
        REPO_ROOT / "tests" / "fixtures" / "career-gear-quantity-e2e.chum5",
        REPO_ROOT / "tests" / "fixtures" / "creation-quality-level-e2e.chum5",
        REPO_ROOT / "tests" / "fixtures" / "career-quality-level-e2e.chum5",
        REPO_ROOT / "tests" / "fixtures" / "career-cyberware-commerce-e2e.chum5",
        REPO_ROOT / "tests" / "fixtures" / "creation-location-rename-e2e.chum5",
        REPO_ROOT / "tests" / "fixtures" / "career-location-rename-e2e.chum5",
        REPO_ROOT / "tests" / "fixtures" / "creation-explicit-save-e2e.chum5",
        REPO_ROOT / "tests" / "fixtures" / "career-explicit-save-e2e.chum5",
        REPO_ROOT / "tests" / "fixtures" / "creation-nested-notes-e2e.chum5",
        REPO_ROOT / "tests" / "fixtures" / "career-nested-notes-e2e.chum5",
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
        CHARACTER_NOTES_PHONE_E2E_RECEIPT,
        CAREER_REPUTATION_PHONE_E2E_RECEIPT,
        SITUATIONAL_MODIFIERS_PHONE_E2E_RECEIPT,
        PRIMARY_ARM_PHONE_E2E_RECEIPT,
        GEAR_LOCATION_PHONE_E2E_RECEIPT,
        WEAPON_LOCATION_PHONE_E2E_RECEIPT,
        VEHICLE_LOCATION_PHONE_E2E_RECEIPT,
        VEHICLE_HOME_NODE_PHONE_E2E_RECEIPT,
        ARMOR_HOME_NODE_PHONE_E2E_RECEIPT,
        WEAPON_HOME_NODE_PHONE_E2E_RECEIPT,
        WEAPON_ACTIVE_COMMLINK_PHONE_E2E_RECEIPT,
        ARMOR_ACTIVE_COMMLINK_PHONE_E2E_RECEIPT,
        ARMOR_DAMAGE_PHONE_E2E_RECEIPT,
        ARMOR_EQUIPMENT_PHONE_E2E_RECEIPT,
        WEAPON_ACCESSORY_INCLUDED_PHONE_E2E_RECEIPT,
        CRITTER_POWER_COUNT_PHONE_E2E_RECEIPT,
        SUSTAINED_EFFECTS_PHONE_E2E_RECEIPT,
        LOCATION_RENAME_PHONE_E2E_RECEIPT,
        EXPLICIT_SAVE_PHONE_E2E_RECEIPT,
        NESTED_COLLECTION_NOTES_PHONE_E2E_RECEIPT,
        LINKED_RUNNER_PHONE_E2E_RECEIPT,
        core_engine_root / "Chummer.Contracts" / "Characters" / "CharacterContactEditSemantics.cs",
        core_engine_root / "Chummer.Contracts" / "Characters" / "CharacterPetEditSemantics.cs",
        core_engine_root / "Chummer.Contracts" / "Characters" / "CharacterSectionModels.cs",
        core_engine_root / "Chummer.Contracts" / "Characters" / "CharacterWeaponHomeNodeRules.cs",
        core_engine_root / "Chummer.Contracts" / "Characters" / "CharacterWeaponActiveCommlinkRules.cs",
        core_engine_root / "Chummer.Contracts" / "Characters" / "CharacterWeaponMatrixParentResolver.cs",
        core_engine_root / "Chummer.Contracts" / "Characters" / "CharacterCyberwareCommerceRules.cs",
        core_engine_root / "Chummer.Contracts" / "Characters" / "CharacterArmorDamageRules.cs",
        core_engine_root / "Chummer.Contracts" / "Characters" / "CharacterArmorEquipmentRules.cs",
        core_engine_root / "Chummer.Contracts" / "Characters" / "CharacterCritterPowerCountRules.cs",
        core_engine_root / "Chummer.Contracts" / "Characters" / "CharacterSpiritFetteringRules.cs",
        core_engine_root / "Chummer.Contracts" / "Characters" / "CharacterSustainedObjectRules.cs",
        core_engine_root / "Chummer.Contracts" / "Characters" / "CharacterGroupMembershipRules.cs",
        core_engine_root / "Chummer.Contracts" / "Characters" / "CharacterGroupNameRules.cs",
        core_engine_root / "Chummer.Contracts" / "Characters" / "CharacterTraditionNameRules.cs",
        core_engine_root / "Chummer.Contracts" / "Characters" / "CharacterCareerEdgeUseRules.cs",
        core_engine_root / "Chummer.Contracts" / "Characters" / "CharacterCareerManualKarmaRules.cs",
        core_engine_root / "Chummer.Contracts" / "Workspaces" / "CharacterWorkspaceModels.cs",
        core_engine_root / "Chummer.Application" / "Characters" / "ICharacterSourceDataResolver.cs",
        core_engine_root / "Chummer.Infrastructure" / "Xml" / "CharacterFileService.cs",
        core_engine_root / "Chummer.Infrastructure" / "Xml" / "CharacterSectionService.cs",
        core_engine_root / "Chummer.Infrastructure" / "Workspaces" / "FileWorkspaceStore.cs",
        core_engine_root / "Chummer.Infrastructure" / "Xml" / "FileSystemCharacterSourceDataResolver.cs",
        core_engine_root / "Chummer.Infrastructure" / "Xml" / "Chummer5LinkedDocumentCodec.cs",
        core_engine_root / "Chummer.Rulesets.Hosting" / "Presentation" / "WorkspaceSurfaceActionCatalog.cs",
        core_engine_root / "Chummer.Rulesets.Sr5" / "Sr5ShellCatalogs.cs",
        presentation_root / "Chummer.Presentation" / "Overview" / "CharacterOverviewPresenter.WorkspaceMutations.cs",
        presentation_root / "Chummer.Presentation" / "Overview" / "CareerReputationEditRequest.cs",
        presentation_root / "Chummer.Presentation" / "Overview" / "SituationalModifiersEditRequest.cs",
        presentation_root / "Chummer.Presentation" / "Overview" / "PrimaryArmEditRequest.cs",
        presentation_root / "Chummer.Presentation" / "Overview" / "GroupMembershipEditRequest.cs",
        presentation_root / "Chummer.Presentation" / "Overview" / "GroupNameEditRequest.cs",
        presentation_root / "Chummer.Presentation" / "Overview" / "TraditionNameEditRequest.cs",
        presentation_root / "Chummer.Presentation" / "Overview" / "CareerEdgeUseEditRequest.cs",
        presentation_root / "Chummer.Presentation" / "Overview" / "CareerManualKarmaEditRequest.cs",
        presentation_root / "Chummer.Presentation" / "Overview" / "GearLocationAddRequest.cs",
        presentation_root / "Chummer.Presentation" / "Overview" / "WeaponLocationAddRequest.cs",
        presentation_root / "Chummer.Presentation" / "Overview" / "VehicleLocationAddRequest.cs",
        presentation_root / "Chummer.Presentation" / "Overview" / "VehicleHomeNodeEditRequest.cs",
        presentation_root / "Chummer.Presentation" / "Overview" / "ArmorHomeNodeEditRequest.cs",
        presentation_root / "Chummer.Presentation" / "Overview" / "WeaponHomeNodeEditRequest.cs",
        presentation_root / "Chummer.Presentation" / "Overview" / "ArmorActiveCommlinkEditRequest.cs",
        presentation_root / "Chummer.Presentation" / "Overview" / "ArmorDamageAdjustmentRequest.cs",
        presentation_root / "Chummer.Presentation" / "Overview" / "ArmorEquipmentEditRequest.cs",
        presentation_root / "Chummer.Presentation" / "Overview" / "WeaponAccessoryIncludedEditRequest.cs",
        presentation_root / "Chummer.Presentation" / "Overview" / "CritterPowerCountEditRequest.cs",
        presentation_root / "Chummer.Presentation" / "Overview" / "SpiritFetteredEditRequest.cs",
        presentation_root / "Chummer.Presentation" / "Overview" / "SustainedObjectEditRequest.cs",
        presentation_root / "Chummer.Presentation" / "Overview" / "CyberwareCommerceRequest.cs",
        presentation_root / "Chummer.Presentation" / "Overview" / "WorkspaceLocationEditorState.cs",
        presentation_root / "Chummer.Presentation" / "Overview" / "LocationRenameRequest.cs",
        presentation_root / "Chummer.Presentation" / "Overview" / "WorkspaceSectionRenderer.cs",
        presentation_root / "Chummer.Presentation" / "Overview" / "CharacterOverviewState.cs",
        presentation_root / "Chummer.Presentation" / "Overview" / "CharacterOverviewPresenter.Persistence.cs",
        presentation_root / "Chummer.Presentation" / "Overview" / "ICharacterOverviewPresenter.cs",
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
                        else (Path("chummer-presentation") / path.relative_to(presentation_root)).as_posix()
                        if path.is_relative_to(presentation_root)
                        else (Path("chummer-core-engine") / path.relative_to(core_engine_root)).as_posix()
                        if path.is_relative_to(core_engine_root)
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
    parser.add_argument(
        "--core-root",
        type=Path,
        default=WORKSPACE_ROOT / "chummer-core-engine",
    )
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
    parser.add_argument("--check", action="store_true")
    arguments = parser.parse_args()

    try:
        payload = build_inventory(
            arguments.chummer5_root.resolve(),
            arguments.registry.resolve(),
            arguments.presentation_root.resolve(),
            arguments.core_root.resolve(),
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
