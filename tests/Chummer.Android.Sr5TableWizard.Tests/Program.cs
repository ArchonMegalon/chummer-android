using Chummer.Android.Native;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;
using Chummer.Presentation.Overview;

var backend = new MemoryBackend();
var store = new Sr5TableWizardCheckpointStore(backend);
Sr5TableWizardSnapshot snapshot = Sr5TableWizardProjector.Project(
    Sr5TableWizardLane.BeforeRun,
    new CareerEdgeUseEditorState(
        new CharacterWorkspaceId("workspace-before-run"),
        ContentRevision: 31,
        new CharacterCareerEdgeUseState(
            EdgeUsed: 1,
            TotalEdge: 4,
            CanSpend: true,
            CanRegain: true)));
var session = new Sr5TableWizardSession();
session.Bind(snapshot);
Sr5TableWizardActionState spend = snapshot.Actions.Single(action =>
    action.Identity.Kind == Sr5TableWizardActionKind.SpendEdge);
Assert(session.TrySelect(spend.Identity), "exact Before Run Edge identity must select");
Assert(store.TryWrite(session), "review checkpoint must be write/read-back verified");

Sr5TableWizardCheckpointRead read = store.Read();
Assert(read.Status == Sr5TableWizardCheckpointReadStatus.Ready && read.Checkpoint is not null,
    "verified checkpoint must be readable");
Sr5TableWizardState resumed = new Sr5TableWizardSession().Bind(snapshot, read.Checkpoint);
Assert(resumed.Resume.Restored && resumed.SelectedAction?.Identity == spend.Identity,
    "phone draft must resume the exact typed action");

backend.Payload = "not canonical base64";
Sr5TableWizardCheckpointRead invalid = store.Read();
Assert(invalid.Status == Sr5TableWizardCheckpointReadStatus.Invalid
       && backend.Payload.Length == 0,
    "tampered phone draft must be removed and fail closed");

var corruptingBackend = new MemoryBackend { CorruptReadBack = true };
var corruptingStore = new Sr5TableWizardCheckpointStore(corruptingBackend);
Assert(!corruptingStore.TryWrite(session),
    "navigation must not continue when durable read-back differs");

var unavailableStore = new Sr5TableWizardCheckpointStore(
    new MemoryBackend { ThrowOnRead = true });
Assert(unavailableStore.Read().Status == Sr5TableWizardCheckpointReadStatus.Unavailable,
    "unavailable durable storage must keep review closed");

backend.Payload = Convert.ToBase64String(new byte[40 * 1024]);
Assert(store.Read().Status == Sr5TableWizardCheckpointReadStatus.Invalid,
    "oversized checkpoint payload must be rejected and removed");

Console.WriteLine("SR5 Before Run / Playtime Android draft-store tests passed.");

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

internal sealed class MemoryBackend : ISr5TableWizardCheckpointBackend
{
    public string Payload { get; set; } = string.Empty;
    public bool CorruptReadBack { get; init; }
    public bool ThrowOnRead { get; init; }

    public string Read()
    {
        if (ThrowOnRead)
            throw new IOException("unavailable");
        return CorruptReadBack && Payload.Length > 0 ? Payload + "A" : Payload;
    }

    public void Write(string payload) => Payload = payload;
    public void Remove() => Payload = string.Empty;
}
