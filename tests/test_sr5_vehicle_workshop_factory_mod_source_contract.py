from pathlib import Path


REPO = Path(__file__).resolve().parents[1]
NATIVE = REPO / "src" / "Chummer.Android" / "Native"
PAGE = NATIVE / "Sr5VehicleWorkshopPage.cs"
DRAFT = NATIVE / "Sr5VehicleWorkshopPhoneDraft.cs"
CONSUMER = NATIVE / "Sr5VehicleWorkshopFactoryModificationConsumer.cs"


def _text(path: Path) -> str:
    return path.read_text(encoding="utf-8")


def _between(source: str, start: str, end: str) -> str:
    return source[source.index(start) : source.index(end)]


def test_factory_step_is_phone_deep_read_only_and_fail_closed() -> None:
    page = _text(PAGE)
    factory_page = _between(
        page,
        "public sealed class Sr5VehicleWorkshopFactoryModificationsPage",
        "public sealed class Sr5VehicleWorkshopModificationsPage",
    )

    for marker in (
        'AutomationId = "sr5-vehicle-workshop-factory-modifications-page"',
        'T("Step 2 of 5")',
        "Sr5VehicleWorkshopFactoryModificationConsumer.Project(",
        "next.IsEnabled = projection.CanContinue",
        "if (projection.Blockers.Count != 0)",
        "RouteId = Sr5VehicleWorkshopRoutes.Modifications",
        "Navigation.PushAsync(new Sr5VehicleWorkshopModificationsPage(",
        "row.InstructionId.Value",
        "row.SourceId.Value",
        "row.InstanceId.Value",
    ):
        assert marker in factory_page

    assert "Remove" not in factory_page
    assert "Modifications =" not in factory_page


def test_factory_projection_keeps_typed_identity_out_of_editable_draft() -> None:
    consumer = _text(CONSUMER)
    for marker in (
        "CharacterVehicleFactoryModificationInstructionId InstructionId",
        "CharacterVehicleFactoryModificationSourceId SourceId",
        "CharacterVehicleFactoryModificationInstanceId InstanceId",
        "DeriveFactoryModificationInstructionId(",
        "DeriveFactoryModificationInstanceId(",
        "IsCanonicalDigest(",
        "Removable: false",
        "CharacterVehicleWorkshopProjectionStatus.Exact",
    ):
        assert marker in consumer

    assert "Sr5VehicleWorkshopDraft" not in consumer
    assert "CharacterVehicleWorkshopCommit" not in consumer


def test_route_checkpoint_and_review_preserve_reopen_receipt_semantics() -> None:
    draft = _text(DRAFT)
    page = _text(PAGE)
    review = page[page.index("public sealed class Sr5VehicleWorkshopReviewPage") :]

    assert (
        'FactoryModifications = "sr5-career/vehicle-workshop/factory-modifications"'
        in draft
    )
    assert "CurrentSchemaVersion = 2" in draft
    assert "routeId is Catalog or FactoryModifications or Modifications" in draft

    projection = review.index("Sr5VehicleWorkshopFactoryModificationProjection? factoryProjection")
    quote = review.index("_authority.Quote(_snapshot, _draft)")
    assert projection < quote
    for marker in (
        "Sr5VehicleWorkshopCheckpointStage.PendingCommit",
        "_authority.PersistPreparedAsync(",
        "Sr5VehicleWorkshopCheckpointStage.Receipt",
        "Sr5VehicleWorkshopRoutes.Recovery",
    ):
        assert marker in review
