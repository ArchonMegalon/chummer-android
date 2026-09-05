from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
COORDINATOR = ROOT / "src/Chummer.Android/Native/RunnerSessionCoordinator.cs"


def test_import_activation_relies_on_exact_workspace_authority_not_a_new_id() -> None:
    source = COORDINATOR.read_text(encoding="utf-8")

    assert "ActivatedNewWorkspace" not in source
    assert source.count("if (State.WorkspaceId is { } importedWorkspaceId)") == 2
    assert source.count(
        "TryRefreshWorkspaceAuthorityAsync(\n"
        "                    importedWorkspaceId,\n"
        "                    expectedPayloadSha256,"
    ) == 2
    assert source.count("verifiedAuthority?.Matches(State) == true") == 2
    assert source.count("WorkspaceIsActive(State, stableWorkspaceId)") == 2


def test_local_import_still_fails_closed_when_no_workspace_is_active() -> None:
    source = COORDINATOR.read_text(encoding="utf-8")

    assert 'TryRecordDocumentImportFailure(\n                    "workspace-not-activated")' in source
    assert "NativeWorkspaceActivationKind.LocalFile" in source
    assert "expectedPayloadSha256" in source
