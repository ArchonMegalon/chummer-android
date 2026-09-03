from pathlib import Path


REPO = Path(__file__).resolve().parents[1]


def test_phone_table_exposes_only_typed_contextual_mutations() -> None:
    source = (REPO / "src/Chummer.Android/Native/PhoneShellPages.cs").read_text(
        encoding="utf-8"
    )

    assert 'automationId: "phone-table-downtime"' in source
    assert "new Sr5DowntimeCalendarWizardPage(Coordinator)" in source
    assert 'automationId: "phone-table-playtime"' in source
    assert "Sr5TableWizardLane.Playtime" in source
    assert "phone-table-playtime-unavailable" not in source

    for blocker in (
        "healing",
        "training",
        "acquisition/install/repair/crafting",
        "lifestyle/contact/project",
        "temporary modifiers",
        "initiative",
        "run-state",
    ):
        assert blocker in source
    assert "No generic mutation fallback is used." in source
    assert "Physical/Stun damage tracks" in source


def test_playtime_uses_one_restart_safe_typed_transaction_presenter() -> None:
    page = (REPO / "src/Chummer.Android/Native/Sr5TableWizardPage.cs").read_text(
        encoding="utf-8"
    )
    transaction = (
        REPO / "src/Chummer.Android/Native/Sr5TableWizardTypedTransaction.cs"
    ).read_text(encoding="utf-8")

    assert "Sr5TableWizardQuotePage" in page
    assert "Review exact diff" in page
    assert "TryBeginApplying(" in page
    assert "TryComplete(" in page
    assert "Acknowledge receipt" in page
    assert "ApplyCareerEdgeUseEditAsync(" in page
    assert "ApplyCareerWeaponFireAsync(" in page
    assert "RunWithConditionalRefreshAsync(() => QuoteAsync(action.Identity))" in page
    assert "private async Task<bool> QuoteAsync" in page
    assert "return false;" in page

    damage_page = (
        REPO / "src/Chummer.Android/Native/Sr5PlaytimeDamageWizardPage.cs"
    ).read_text(encoding="utf-8")
    damage_transaction = (
        REPO / "src/Chummer.Android/Native/Sr5PlaytimeDamageTransaction.cs"
    ).read_text(encoding="utf-8")
    assert 'automationId: $"sr5-table-playtime-damage-{token}"' in page
    assert "new Sr5PlaytimeDamageWizardPage(" in page
    assert "state.Snapshot.WorkspaceId" in page
    assert "Sr5PlaytimeDamageIntegrity.IsSupportedTrack(track.Track)" in page
    assert "!track.ActsAsAlternateTrack" in page
    assert "ApplyConditionMonitorEditAsync(" in damage_page
    assert "Sr5CareerMutationDomains.PlaytimeDamage" in damage_transaction
    assert "observed.WorkspaceRevision != journal.Quote.Original.WorkspaceRevision + 1" in damage_transaction

    for phase in ("Reviewed", "Applying", "Applied"):
        assert phase in transaction
        assert phase in damage_transaction
    assert "ExpectedPostconditionDigest" in transaction
    assert "IdempotencyKey" in transaction
    assert "TryReturnToReview(" in transaction
    assert "TryClearApplied(" in transaction
