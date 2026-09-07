"""Page composition/resource guards, not Android runtime qualification."""
import re
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
NATIVE = ROOT / "src/Chummer.Android/Native"
RESOURCES = ROOT / "src/Chummer.Android/Resources/Localization"


def test_ordinary_reward_page_has_no_manual_authority_intake():
    page = (NATIVE / "Sr5AfterRunRewardWizardPage.cs").read_text()
    view = (NATIVE / "Sr5AfterRunRewardView.cs").read_text()
    assert "CreateAfterRunRewardModel" in page
    assert "PrepareForAppearanceRefreshAsync" in page
    assert "_model.InitializeAsync(cancellationToken)" in page
    assert "_lifetime?.Cancel()" in page
    assert "_view.SetLifetime(_lifetime.Token)" in page
    assert "new(_model, RunAsync" in page
    for forbidden in ("Guid.NewGuid", "Guid.Parse", "SHA256", "QuickEdit", "PublishManualAfterRunProposal", "ReplaceWorkspaceDocument"):
        assert forbidden not in page + view
    for action in ("ConfirmAsync", "RecoverAsync", "RetryAsync", "ResumeRecordedRewardAsync"):
        assert action in view


def test_appearance_does_not_replay_and_controls_do_not_rebuild_on_refresh():
    view = (NATIVE / "Sr5AfterRunRewardView.cs").read_text()
    refresh = view.split("public void Refresh()", 1)[1].split("private void BindHistory", 1)[0]
    assert "Clear()" not in refresh
    assert "new Entry" not in refresh
    assert "_model.ConfirmAsync" not in refresh
    assert "_model.RetryAsync" not in refresh
    assert "HistoryPageSize = 25" in view
    assert "CancellationToken token = _lifetime" in view
    assert "token.IsCancellationRequested" in view
    assert "_model.CanContinue" in refresh


def test_saved_reward_planning_reuses_calendar_and_revalidates_destination_entry():
    page = (NATIVE / "Sr5AfterRunRewardWizardPage.cs").read_text()
    view = (NATIVE / "Sr5AfterRunRewardView.cs").read_text()
    calendar = (NATIVE / "Sr5DowntimeCalendarWizardPage.cs").read_text()
    route = page.split("internal async Task OpenDowntimeAsync", 1)[1].split("internal async Task OpenConsequencesAsync", 1)[0]
    assert "new Sr5DowntimeCalendarWizardPage" in route
    assert "Sr5DowntimeCalendarJournalStore.CreateDefault()" in route
    assert route.count("RequireCurrentReward(saved, cancellationToken)") == 2
    assert "entryStillCurrent:" in route
    assert "ReferenceEquals(saved, _model.Handoff)" in route
    assert "sr5-reward-downtime" in view
    assert "_downtime.IsEnabled = !busy && _downtime.IsVisible" in view
    load = calendar.split("private async Task LoadAndRecoverAsync", 1)[1].split("private void RequireCurrentEntry", 1)[0]
    assert load.count("RequireCurrentEntry(cancellationToken)") == 2
    assert load.index("var load = await _authority.LoadAsync") < load.rindex("RequireCurrentEntry(cancellationToken)") < load.index("_load = load")
    assert "entryStillCurrent?.Invoke() == false" in calendar
    for mutation in ("ApplyAsync", "SettleAfterRun", "PublishManual", "TryConfirm", "TryWriteReview"):
        assert mutation not in route


def test_every_reward_resource_has_nonempty_exact_locale_and_placeholder_parity():
    catalogs = []
    for suffix in ("", ".de", ".es"):
        nodes = ET.parse(RESOURCES / f"PhoneStrings{suffix}.resx").getroot().findall("data")
        keys = [node.attrib["name"] for node in nodes]
        assert len(keys) == len(set(keys)), f"duplicate keys: {suffix}"
        catalogs.append({node.attrib["name"]: node.findtext("value") for node in nodes})
    source = "\n".join((NATIVE / name).read_text() for name in ("Sr5AfterRunRewardView.cs", "Sr5AfterRunRewardWizardPage.cs"))
    keys = set(re.findall(r'"(AfterRunReward\w+)"', source))
    assert len(keys) >= 35
    for key in keys:
        placeholders = set(re.findall(r"\{\d+\}", catalogs[0][key]))
        for catalog in catalogs:
            assert catalog[key].strip()
            assert set(re.findall(r"\{\d+\}", catalog[key])) == placeholders, key
    assert "Experimenteller" in catalogs[1]["AfterRunRewardExperimental"]
    assert "experimental" in catalogs[2]["AfterRunRewardExperimental"]
