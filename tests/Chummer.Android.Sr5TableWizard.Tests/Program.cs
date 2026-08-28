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

AssertCapabilityBoundaries();

Console.WriteLine("SR5 Before Run / Playtime Android draft-store tests passed.");

static void AssertCapabilityBoundaries()
{
    Assert(
        Sr5CareerRunCapabilityCatalog.BeforeRun.Single(capability =>
            capability.Id == "before-run-edge").Status
        == Sr5CareerRunCapabilityStatus.Available,
        "Before Run must expose only the existing typed Edge preparation lane");
    foreach (string blocked in new[]
             {
                 "before-run-loadout",
                 "before-run-preparation",
                 "before-run-contacts",
                 "before-run-commitments"
             })
    {
        Assert(
            Sr5CareerRunCapabilityCatalog.BeforeRun.Single(capability =>
                capability.Id == blocked).Status
            == Sr5CareerRunCapabilityStatus.Unavailable,
            $"{blocked} must stay fail-closed without typed authority");
    }

    foreach (string readOnly in new[] { "after-run-karma", "after-run-nuyen" })
    {
        Assert(
            Sr5CareerRunCapabilityCatalog.AfterRun.Single(capability =>
                capability.Id == readOnly).Status
            == Sr5CareerRunCapabilityStatus.ReadOnly,
            $"{readOnly} must remain signed proposal context and never be re-awarded");
    }
    foreach (string available in new[]
             {
                 "after-run-heat",
                 "after-run-street-cred",
                 "after-run-notoriety",
                 "after-run-public-awareness",
                 "after-run-contacts"
             })
    {
        Assert(
            Sr5CareerRunCapabilityCatalog.AfterRun.Single(capability =>
                capability.Id == available).Status
            == Sr5CareerRunCapabilityStatus.Available,
            $"{available} must map to the typed atomic settlement");
    }
    foreach (string blocked in new[]
             {
                 "after-run-injuries",
                 "after-run-ammo",
                 "after-run-loot",
                 "after-run-expenses",
                 "after-run-log"
             })
    {
        Assert(
            Sr5CareerRunCapabilityCatalog.AfterRun.Single(capability =>
                capability.Id == blocked).Status
            == Sr5CareerRunCapabilityStatus.Unavailable,
            $"{blocked} must stay fail-closed without typed authority");
    }
}

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
