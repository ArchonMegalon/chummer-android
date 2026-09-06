using Chummer.Application.Workspaces;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Workspaces;

namespace Chummer.Android.Sr5AfterRunReward.Tests;

/// <summary>
/// Real Core persistence fixture. Cold reopen constructs a fresh file store and
/// reward service over the existing directory without recreating saved state.
/// </summary>
internal sealed class RealAfterRunRewardFixture : IDisposable
{
    private bool _disposed;

    public RealAfterRunRewardFixture(
        WorkspaceDocument? document = null,
        CharacterWorkspaceId? workspaceId = null)
    {
        WorkspaceId = workspaceId ?? new CharacterWorkspaceId("android-after-run-reward-tests");
        DirectoryPath = Directory.CreateTempSubdirectory("chummer-android-after-run-reward-tests-").FullName;
        try
        {
            Store = new FileWorkspaceStore(DirectoryPath);
            var created = Store.CreateWorkspaceDocument(WorkspaceId, document ?? CreateDocument());
            if (!created.Success)
                throw new InvalidOperationException($"Could not create reward fixture: {created.Error}");
            var saved = Store.SaveCheckpoint(WorkspaceId, 1);
            if (!saved.Success)
                throw new InvalidOperationException($"Could not save reward fixture revision 1: {saved.Error}");
            Service = new WorkspaceCharacterAfterRunRewardService(Store);
        }
        catch
        {
            Directory.Delete(DirectoryPath, recursive: true);
            throw;
        }
    }

    public string DirectoryPath { get; }
    public CharacterWorkspaceId WorkspaceId { get; }
    public FileWorkspaceStore Store { get; }
    public WorkspaceCharacterAfterRunRewardService Service { get; }

    public static WorkspaceDocument CreateDocument()
        => new(
            """
            <character>
              <created>True</created><karma>30</karma><nuyen>1000</nuyen>
              <streetcred>10</streetcred><notoriety>4</notoriety><publicawareness>6</publicawareness>
              <contacts /><expenses /><notes>Keep unrelated runner state</notes>
            </character>
            """, "sr5");

    public FileWorkspaceStore ColdReopenStore()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new FileWorkspaceStore(DirectoryPath);
    }

    public WorkspaceCharacterAfterRunRewardService ColdReopenService()
        => new(ColdReopenStore());

    public void Dispose()
    {
        if (_disposed)
            return;
        Directory.Delete(DirectoryPath, recursive: true);
        _disposed = true;
    }
}
