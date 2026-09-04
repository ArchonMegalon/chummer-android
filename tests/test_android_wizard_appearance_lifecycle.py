from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
NATIVE = ROOT / "src/Chummer.Android/Native"


def source(name: str) -> str:
    return (NATIVE / name).read_text(encoding="utf-8")


def test_native_wizard_appearance_loads_use_the_awaited_base_hook() -> None:
    base = source("NativePageBase.cs")
    assert "await PrepareForAppearanceRefreshAsync(appearanceToken);" in base

    for name in (
        "Sr5CareerWizardPage.cs",
        "Sr5PlaytimeDamageWizardPage.cs",
        "Sr5DowntimeCalendarWizardPage.cs",
        "Sr5TableWizardPage.cs",
        "Sr5CareerActiveSkillWizardPage.cs",
        "Sr5CareerSkillGroupWizardPage.cs",
        "Sr5CareerSpecializationWizardPage.cs",
        "Sr5CareerQualityWizardPage.cs",
        "Sr5CareerKnowledgeSkillWizardPage.cs",
        "Sr5CareerAttributeWizardPage.cs",
        "Sr5AfterRunSettlementWizardPage.cs",
    ):
        page = source(name)
        assert "protected override async void OnAppearing()" not in page
        assert "PrepareForAppearanceRefreshAsync(" in page


def test_table_wizard_does_not_double_initialize_during_appearance() -> None:
    page = source("Sr5TableWizardPage.cs")
    appearance = page.split("PrepareForAppearanceRefreshAsync(", 1)[1].split(
        "protected override void OnDisappearing()", 1
    )[0]

    assert "CreateLinkedTokenSource(cancellationToken)" in appearance
    assert "initializeCoordinator: false" in appearance
    assert "cancellationToken.ThrowIfCancellationRequested();" in appearance
    assert "initializeCoordinator: true" in page


def test_career_projection_is_linked_to_the_page_lifetime_without_double_init() -> None:
    page = source("Sr5CareerWizardPage.cs")
    appearance = page.split("PrepareForAppearanceRefreshAsync(", 1)[1].split(
        "protected override void OnDisappearing()", 1
    )[0]
    load = page.split(
        "private async Task LoadLatestAsync(CancellationToken cancellationToken)", 1
    )[1]

    assert "CreateLinkedTokenSource(cancellationToken)" in appearance
    assert "await LoadLatestAsync(_loadLifetime.Token);" in appearance
    assert "cancellationToken.ThrowIfCancellationRequested();" in appearance
    assert "await Coordinator.InitializeAsync();" not in load


def test_downtime_projection_propagates_appearance_cancellation_to_authority() -> None:
    page = source("Sr5DowntimeCalendarWizardPage.cs")
    assert "await LoadAndRecoverAsync(cancellationToken);" in page
    assert "private async Task LoadAndRecoverAsync(CancellationToken cancellationToken)" in page
    assert "await _authority.LoadAsync(cancellationToken);" in page
    assert "catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)" in page
    assert "_loaded = false;\n            throw;" in page


def test_playtime_damage_recovery_does_not_start_a_second_coordinator_init() -> None:
    page = source("Sr5PlaytimeDamageWizardPage.cs")
    appearance = page.split("PrepareForAppearanceRefreshAsync(", 1)[1].split(
        "protected override void Refresh()", 1
    )[0]
    load = page.split("private void LoadLatest(CancellationToken cancellationToken)", 1)[1]

    assert "LoadLatest(cancellationToken);" in appearance
    assert "Coordinator.InitializeAsync()" not in load
    assert "cancellationToken.ThrowIfCancellationRequested();" in load
    assert "if (!cancellationToken.IsCancellationRequested)" in load
