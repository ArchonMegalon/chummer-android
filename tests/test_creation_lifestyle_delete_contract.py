from pathlib import Path
import unittest


ROOT = Path(__file__).resolve().parents[1]
COLLECTION_PAGE = ROOT / "src/Chummer.Android/Native/CollectionEditorPages.cs"
COORDINATOR = ROOT / "src/Chummer.Android/Native/RunnerSessionCoordinator.cs"
PROJECTOR = ROOT.parent / "chummer-presentation/Chummer.Presentation/Overview/CreationLifestyleDeleteRequest.cs"
MUTATION = ROOT.parent / "chummer-presentation/Chummer.Presentation/Overview/WorkspaceXmlMutationCatalog.cs"


class CreationLifestyleDeleteContractTests(unittest.TestCase):
    def test_selected_creation_lifestyle_has_typed_confirmable_phone_action(self) -> None:
        source = COLLECTION_PAGE.read_text(encoding="utf-8")
        for token in (
            "AddCreationLifestyleDeleteAction(item)",
            "WorkspaceCollectionKind.Lifestyle",
            "Coordinator.State.Profile?.Created != false",
            'Guid.TryParseExact(_target.ItemId, "D"',
            'creation-lifestyle-delete-{lifestyleId:N}',
            "PrepareCreationLifestyleDeleteAsync",
            "candidate.Identity.LifestyleId == lifestyleId",
            "Coordinator.ApplicationSettings.ConfirmDelete",
            '"Delete Lifestyle?"',
            '"Cancel"',
            "CreationLifestyleDeleteRequest",
        ):
            self.assertIn(token, source)

    def test_generic_lifestyle_delete_stays_fail_closed_and_dedicated_cas_is_used(self) -> None:
        generic = (ROOT.parent / "chummer-presentation/Chummer.Presentation/Overview/WorkspaceCollectionEditorProjector.cs").read_text(encoding="utf-8")
        self.assertIn("CanDelete: schema.Kind != WorkspaceCollectionKind.Lifestyle", generic)
        coordinator = COORDINATOR.read_text(encoding="utf-8")
        for token in (
            "ApplyCreationLifestyleDeleteAsync",
            "request.ExpectedContentRevision",
            "_presenter.ApplyCreationLifestyleDeleteAsync",
            "_presenter.SaveAsync",
        ):
            self.assertIn(token, coordinator)

    def test_projection_and_mutation_bind_quality_cascade_and_revalidate(self) -> None:
        projector = PROJECTOR.read_text(encoding="utf-8")
        mutation = MUTATION.read_text(encoding="utf-8")
        for token in (
            "CharacterCreationLifestyleDeleteIdentity",
            'FindSingleContainer(root, "lifestyles")',
            'FindSingleContainer(root, "improvements")',
            'FindSingleContainer(lifestyle, "lifestylequalities")',
            "Lifestyle Quality GUIDs must be unique",
            'sourceName.StartsWith(source + " "',
            "PersistedCascadeImprovementTypes",
            "ImprovementManager persisted-object cascade",
        ):
            self.assertIn(token, projector)
        for token in (
            "ApplyCreationLifestyleDelete",
            "CharacterCreationLifestyleDeleteRules.CanDelete",
            "foreach (XElement improvement in target.Improvements)",
            "improvement.Remove()",
            "target.Lifestyle.Remove()",
            "CreationLifestyleDeleteEditorProjector.ProjectElements(document.Root!)",
        ):
            self.assertIn(token, mutation)


if __name__ == "__main__":
    unittest.main()
