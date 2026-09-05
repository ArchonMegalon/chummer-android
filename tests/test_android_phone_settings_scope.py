from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
MORE_PAGE = ROOT / "src/Chummer.Android/Native/MorePage.cs"
PHONE_SHELL_PAGES = ROOT / "src/Chummer.Android/Native/PhoneShellPages.cs"
APPLICATION_SETTINGS = ROOT / "src/Chummer.Android/Native/ApplicationSettingsPage.cs"


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
