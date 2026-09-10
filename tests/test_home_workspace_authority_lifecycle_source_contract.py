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
        self.assertIn(
            "ReadWorkspaceAuthorityAsync(workspaceId, token, expectedOwner)",
            exact_read,
        )
        self.assertIn("_workspaceOperationCoordinator.RunCurrentAsync(", exact_read)
        for guard in (
            "!execution.CanPublish",
            "!execution.HasValue",
            "execution.Value is not { } authority",
            "bound.CaptureOwnerContext() != retained",
            "authorityEpoch != _workspaceAuthorityEpoch",
            "expectedOwner is { } owner && State.DisplayOwnerContext != owner",
            "!authority.Matches(State)",
        ):
            self.assertIn(guard, exact_read)
            self.assertLess(exact_read.index(guard), exact_read.index("_workspaceAuthority = authority;"))
        self.assertIn(
            "if (AndroidE2EAuthority.Enabled\n"
            "                    && optInGeneration == AndroidE2EAuthority.Generation)\n"
            "                {\n"
            "                    _workspaceAuthority = authority;",
            exact_read,
        )
        self.assertIn("_workspaceAuthorityOptInGeneration = optInGeneration;", exact_read)
        read = coordinator.split(
            "private async Task<NativeWorkspaceAuthoritySnapshot> "
            "ReadWorkspaceAuthorityAsync",
            maxsplit=1,
        )[1].split("private static WorkspaceDocumentSnapshot", maxsplit=1)[0]
        # Both snapshots use the same captured owner parameter. Only the
        # explicit no-owner branch retains the legacy local-client read path.
        self.assertEqual(2, read.count("await ReadAsync()"))
        self.assertEqual(2, read.count("RequireWorkspaceSnapshot("))
        self.assertIn(
            "if (expectedOwner is not null && _client is not IOwnerBoundWorkspaceMutationClient)\n"
            '            throw new InvalidOperationException("Owner-bound Android proof capture is unavailable.");',
            read,
        )
        self.assertIn(
            "=> expectedOwner is { } owner\n"
            "                ? ((IOwnerBoundWorkspaceMutationClient)_client).GetWorkspaceAsync(owner, workspaceId, cancellationToken)\n"
            "                : _client.GetWorkspaceAsync(workspaceId, cancellationToken);",
            read,
        )
        self.assertIn("AuthoritySnapshotsMatch(first, verified)", read)
        self.assertLess(read.rindex("await ReadAsync()"), read.index("if (!AuthoritySnapshotsMatch(first, verified))"))
        self.assertLess(read.index("if (!AuthoritySnapshotsMatch(first, verified))"), read.index("return new NativeWorkspaceAuthoritySnapshot("))
        comparison = coordinator.split(
            "private static bool AuthoritySnapshotsMatch(", maxsplit=1
        )[1].split("internal static string ComputeDocumentAuthoritySha256", maxsplit=1)[0]
        for binding in (
            "first.Id.Value, verified.Id.Value, StringComparison.Ordinal",
            "first.LastUpdatedUtc == verified.LastUpdatedUtc",
            "first.ContentRevision == verified.ContentRevision",
            "first.SavedRevision == verified.SavedRevision",
            "first.Document.Content, verified.Document.Content, StringComparison.Ordinal",
            "first.Document.AuxiliaryStateDigest,\n               verified.Document.AuxiliaryStateDigest,",
        ):
            self.assertIn(binding, comparison)


if __name__ == "__main__":
    unittest.main()
