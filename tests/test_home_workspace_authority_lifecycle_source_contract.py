import pathlib
import unittest


REPO = pathlib.Path(__file__).resolve().parents[1]
NATIVE = REPO / "src" / "Chummer.Android" / "Native"


class HomeWorkspaceAuthorityLifecycleSourceContractTests(unittest.TestCase):
    def test_appearance_refreshes_authority_before_rendering_runners(self) -> None:
        page_base = (NATIVE / "NativePageBase.cs").read_text(encoding="utf-8")
        appearing = page_base.split(
            "protected override async void OnAppearing()", maxsplit=1
        )[1].split("protected override void OnDisappearing()", maxsplit=1)[0]

        initialized = appearing.index("await Coordinator.InitializeAsync()")
        prepared = appearing.index(
            "await PrepareForAppearanceRefreshAsync(appearanceToken)"
        )
        rendered = appearing.index("Refresh()", prepared)
        self.assertLess(initialized, prepared)
        self.assertLess(prepared, rendered)

        home = (NATIVE / "HomePage.cs").read_text(encoding="utf-8")
        debug_hook = home.split("#if DEBUG", maxsplit=1)[1].split(
            "#endif", maxsplit=1
        )[0]
        self.assertIn(
            "PrepareForAppearanceRefreshAsync", debug_hook
        )
        self.assertIn(
            "RefreshDebugWorkspaceAuthorityForPageAppearanceAsync",
            debug_hook,
        )

    def test_refresh_is_debug_opt_in_and_uses_existing_exact_double_read(self) -> None:
        coordinator = (NATIVE / "RunnerSessionCoordinator.cs").read_text(
            encoding="utf-8"
        )
        debug_surface = coordinator.split("#if DEBUG", maxsplit=1)[1].split(
            "#endif", maxsplit=1
        )[0]
        refresh = debug_surface.split(
            "RefreshDebugWorkspaceAuthorityForPageAppearanceAsync", maxsplit=1
        )[1]
        self.assertIn("AndroidE2EAuthority.Enabled", refresh)
        self.assertIn("CharacterOverviewState state = State", refresh)
        self.assertIn("expectedWorkspaceId: state.WorkspaceId", refresh)
        self.assertIn("TryRefreshWorkspaceAuthorityAsync", refresh)
        self.assertNotIn("Task.Delay", refresh)
        self.assertNotIn("while (", refresh)

        exact_read = coordinator.split(
            "private async Task<NativeWorkspaceAuthoritySnapshot?> "
            "TryRefreshWorkspaceAuthorityAsync",
            maxsplit=1,
        )[1].split("private void ClearWorkspaceAuthority()", maxsplit=1)[0]
        self.assertEqual(1, exact_read.count("ReadWorkspaceAuthorityAsync("))
        read = coordinator.split(
            "private async Task<NativeWorkspaceAuthoritySnapshot> "
            "ReadWorkspaceAuthorityAsync",
            maxsplit=1,
        )[1].split("private static WorkspaceDocumentSnapshot", maxsplit=1)[0]
        self.assertEqual(2, read.count("await _client.GetWorkspaceAsync("))
        self.assertIn("AuthoritySnapshotsMatch(first, verified)", read)


if __name__ == "__main__":
    unittest.main()
