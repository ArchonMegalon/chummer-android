import json
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
MORE_PAGE = ROOT / "src/Chummer.Android/Native/MorePage.cs"
PHONE_SHELL_PAGES = ROOT / "src/Chummer.Android/Native/PhoneShellPages.cs"
APPLICATION_SETTINGS = ROOT / "src/Chummer.Android/Native/ApplicationSettingsPage.cs"
NATIVE_DIALOG = ROOT / "src/Chummer.Android/Native/NativeDialogPage.cs"
PHONE_CAPABILITIES = (
    ROOT / "docs/ANDROID_CHARACTER_SETTINGS_PHONE_CAPABILITIES.generated.json"
)
RUNTIME_CAPABILITIES = (
    ROOT
    / "src/Chummer.Android/Native/AndroidCharacterSettingsPhoneCapabilities.Generated.cs"
)


def test_phone_more_exposes_only_the_phone_owned_settings_surface() -> None:
    more_source = MORE_PAGE.read_text(encoding="utf-8")
    phone_source = PHONE_SHELL_PAGES.read_text(encoding="utf-8")

    assert "new ApplicationSettingsPage(Coordinator)" in more_source
    assert 'automationId: "more-application-settings"' in more_source
    assert 'AutomationId = "more-updates-play-managed"' in more_source
    assert 'AutomationId = "more-check-play-updates"' in more_source
    assert "Coordinator.CheckForUpdatesAsync()" in more_source
    assert "showUnrestrictedActions: false" in phone_source


def test_phone_settings_do_not_render_the_legacy_character_settings_catalog() -> None:
    settings_source = APPLICATION_SETTINGS.read_text(encoding="utf-8")

    assert 'AutomationId = "application-settings-page"' in settings_source
    assert 'AutomationId = "settings-confirm-delete"' in settings_source
    assert 'AutomationId = "settings-language-device-managed"' in settings_source
    assert 'AutomationId = "settings-updates-play-managed"' in settings_source
    assert "NativeDialogPage" not in settings_source
    assert "ActiveDialog" not in settings_source
    assert "CharacterSettings" not in settings_source


def test_character_settings_scope_preserves_profile_identity_and_fails_closed() -> None:
    dialog_source = NATIVE_DIALOG.read_text(encoding="utf-8")

    assert 'CharacterSettingsDialogId = "dialog.character_settings"' in dialog_source
    assert (
        'CustomDataFieldId = "characterSettingsControl-treCustomDataDirectories"'
        in dialog_source
    )
    assert 'ControlFieldPrefix = "characterSettingsControl-"' in dialog_source
    assert 'AutomationId = "dialog-settings-scope"' in dialog_source
    assert "AndroidCharacterSettingsPhoneCapabilities.TryGet" in dialog_source
    assert "AndroidCharacterSettingsPhoneCapabilities.SupportedSectionIds" in dialog_source
    assert "IsExpectedCapabilityField(sectionId, field, capability)" in dialog_source
    assert "matches.Length == 1 && IsExpectedSectionField(matches[0])" in dialog_source
    assert "return new NativeDialogScopedField(false" in dialog_source
    assert "Every other dialog passes through unchanged" in dialog_source


def test_visible_native_dialog_inputs_have_one_localized_accessible_label() -> None:
    dialog_source = NATIVE_DIALOG.read_text(encoding="utf-8")

    assert dialog_source.count(
        "NativeDialogAccessibility.BindFieldLabel(label,"
    ) == 4
    for control in ("picker", "toggle", "editor", "entry"):
        assert f"BindFieldLabel(label, {control}, scopedField.Label)" in dialog_source
    assert "SemanticProperties.SetDescription(input, accessibleLabel)" in dialog_source
    assert "AutomationProperties.SetLabeledBy(input, decorativeLabel)" in dialog_source
    assert (
        "AutomationProperties.SetIsInAccessibleTree(decorativeLabel, false)"
        in dialog_source
    )
    assert "if (scopedField.IsVisible)" in dialog_source
    assert "body.Add(CreateField(dialog.Id, _renderGeneration, field, scopedField))" in dialog_source
    assert dialog_source.count(
        "_coordinator.UpdateDialogFieldAsync(binding.FieldId, value)"
    ) == 2


def test_character_settings_title_and_actions_use_stable_localized_ids() -> None:
    dialog_source = NATIVE_DIALOG.read_text(encoding="utf-8")

    assert "AndroidDialogSettingsScope.Title(dialog)" in dialog_source
    assert "AndroidDialogSettingsScope.ActionLabel(dialog, action)" in dialog_source
    expected = {
        "save": "CharacterSettingsActionSave",
        "save_and_close": "CharacterSettingsActionSaveAndClose",
        "save_as": "CharacterSettingsActionSaveAs",
        "rename": "CharacterSettingsActionRename",
        "delete": "CharacterSettingsActionDelete",
        "restore_defaults": "CharacterSettingsActionRestoreDefaults",
        "cancel": "CharacterSettingsActionCancel",
    }
    for action_id, resource_key in expected.items():
        assert f'["{action_id}"] = ("{resource_key}"' in dialog_source
    assert 'PhoneStrings.Get("CharacterSettingsTitle", "Character Settings", culture)' in dialog_source


def test_character_settings_phone_capability_inventory_is_exhaustive_and_fail_closed() -> None:
    inventory = json.loads(PHONE_CAPABILITIES.read_text(encoding="utf-8"))
    controls = inventory["controls"]
    visible = [row for row in controls if row["phoneStatus"] == "visible_editable"]
    hidden = [row for row in controls if row["phoneStatus"] == "hidden_preserved"]

    assert inventory["scope"] == "current_phone_wizard_only"
    assert inventory["summary"] == {
        "valueControlCount": 150,
        "visibleEditableCount": 17,
        "hiddenPreservedCount": 133,
        "visibleSectionCount": 5,
    }
    assert len({row["fieldId"] for row in controls}) == 150
    assert {row["sectionId"] for row in visible} == {
        "ware",
        "rules",
        "karma",
        "limits",
        "build",
    }
    assert all(row["androidBehavior"] and row["behaviorEvidence"] for row in visible)
    assert all(row["labelResourceKey"] and row["englishLabel"] for row in visible)
    assert all(row["androidBehavior"] is None for row in hidden)
    assert all("unchanged" in row["rationale"] for row in hidden)

    by_control = {row["legacyControl"]: row for row in controls}
    for desktop_or_unwired in (
        "treSourcebook",
        "treCustomDataDirectories",
        "chkIgnoreArt",
        "nudKarmaSpecialization",
        "txtNuyenExpression",
        "nudMaxSkillRatingCreate",
        "nudStartingKarma",
        "nudMaxAvail",
    ):
        assert by_control[desktop_or_unwired]["phoneStatus"] == "hidden_preserved"

    runtime_source = RUNTIME_CAPABILITIES.read_text(encoding="utf-8")
    for row in visible:
        assert f'new("{row["legacyControl"]}", "{row["fieldId"]}"' in runtime_source
    for row in hidden:
        assert f'new("{row["legacyControl"]}", "{row["fieldId"]}"' not in runtime_source
