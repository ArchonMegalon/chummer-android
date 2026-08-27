using System.Reflection;
using Chummer.Android.Native;
using Chummer.Presentation.Overview;

internal static class Program
{
    private static int Main()
    {
        ProjectionBindsExactWorkspaceAndTypedActions();
        ProjectionIsCanonicalAcrossProbeOrder();
        ClosedCatalogMatchesPresentationAndHasStableSelectors();
        NavigationCheckpointRoundTripsWithDurableReadBack();
        StaleWorkspaceRevisionSourceAndContentInvalidateRestart();
        MalformedOversizedAndNondurableCheckpointPayloadsFailClosed();
        NavigationModelsExposeNoMutationBoundary();
        Console.WriteLine("SR5 Career phone chooser tests passed: 7");
        return 0;
    }

    private static void ProjectionBindsExactWorkspaceAndTypedActions()
    {
        Sr5CareerWizardSnapshot snapshot = Sr5CareerWizardPhoneProjection.Project(
            Workspace(),
            [
                Available(Sr5CareerWizardActionIds.AdvanceAttribute, H('d')),
                Blocked(Sr5CareerWizardActionIds.AdvanceActiveSkill, H('e'))
            ]);

        Require(snapshot.Binding.WorkspaceId == "workspace-career-phone", "workspace binding");
        Require(snapshot.Binding.WorkspaceRevision == 41, "content revision binding");
        Require(snapshot.Binding.SavedRevision == 39, "saved revision binding");
        Require(snapshot.Binding.RulesetId == "sr5", "ruleset binding");
        Require(snapshot.Binding.RuntimeFingerprint == Sr5CareerWizardPhoneCatalog.RuntimeFingerprint,
            "runtime binding");
        Require(IsDigest(snapshot.Binding.SourceDigest), "source digest binding");
        Require(IsDigest(snapshot.Binding.ContentDigest), "content digest binding");

        Sr5CareerWizardActionState attribute = Action(snapshot, Sr5CareerWizardActionIds.AdvanceAttribute);
        Require(attribute.CanOpen && IsDigest(attribute.AuthorityDigest), "available typed action");
        Sr5CareerWizardActionState skill = Action(snapshot, Sr5CareerWizardActionIds.AdvanceActiveSkill);
        Require(!skill.CanOpen
                && skill.Blockers.SequenceEqual([Sr5CareerWizardPhoneBlockers.NoEligibleTarget]),
            "typed action without targets must stay blocked");
        Sr5CareerWizardActionState missing = Action(snapshot, Sr5CareerWizardActionIds.AdjustKarma);
        Require(!missing.CanOpen
                && missing.Blockers.SequenceEqual([Sr5CareerWizardBlockers.AuthorityUnavailable]),
            "an unprobed route must stay unavailable");
    }

    private static void ProjectionIsCanonicalAcrossProbeOrder()
    {
        Sr5CareerWizardPhoneAuthorityProbe first = Available(
            Sr5CareerWizardActionIds.AdvanceSkillGroup,
            H('a')) with { SourceAnchorIds = ["source.z", "source.a"] };
        Sr5CareerWizardPhoneAuthorityProbe second = Blocked(
            Sr5CareerWizardActionIds.EditNuyenExpense,
            H('b')) with { Blockers = ["z-blocker", "a-blocker"] };

        Sr5CareerWizardSnapshot left = Sr5CareerWizardPhoneProjection.Project(
            Workspace(),
            [first, second]);
        Sr5CareerWizardSnapshot right = Sr5CareerWizardPhoneProjection.Project(
            Workspace(),
            [second, first]);

        Require(left.SnapshotDigest == right.SnapshotDigest, "probe input order must be irrelevant");
        Require(left.Binding.SourceDigest == right.Binding.SourceDigest, "source digest order");
        Require(Action(right, first.ActionId).SourceAnchorIds.SequenceEqual(["source.a", "source.z"]),
            "source anchors must be canonical");
        Require(Action(right, second.ActionId).Blockers.SequenceEqual(["a-blocker", "z-blocker"]),
            "blockers must be canonical");
    }

    private static void ClosedCatalogMatchesPresentationAndHasStableSelectors()
    {
        Require(Sr5CareerWizardPhoneCatalog.Actions
                .Select(static action => action.ActionId)
                .SequenceEqual(Sr5CareerWizardProjector.KnownActionIds),
            "native action catalog must exactly match Presentation order");
        Require(Sr5CareerWizardPhoneCatalog.Actions.Select(static action => action.AutomationId)
                .Distinct(StringComparer.Ordinal).Count() == Sr5CareerWizardPhoneCatalog.Actions.Count,
            "action selectors must be unique");
        Require(Sr5CareerWizardPhoneCatalog.Families.Select(static family => family.RouteId)
                .SequenceEqual(["sr5-career/economy", "sr5-career/advancement", "sr5-career/calendar"]),
            "family routes must be stable and deterministic");
        Require(IsDigest(Sr5CareerWizardPhoneCatalog.RuntimeFingerprint), "runtime fingerprint");
        RequireThrows<InvalidOperationException>(
            () => Sr5CareerWizardPhoneCatalog.RequireAction("career.generic-edit"),
            "unknown actions must have no fallback route");
    }

    private static void NavigationCheckpointRoundTripsWithDurableReadBack()
    {
        Sr5CareerWizardSnapshot snapshot = Sr5CareerWizardPhoneProjection.Project(
            Workspace(),
            [
                Available(Sr5CareerWizardActionIds.AdvanceAttribute, H('a')),
                Available(Sr5CareerWizardActionIds.AdvanceActiveSkill, H('b'))
            ]);
        var session = new Sr5CareerWizardDesktopSession();
        session.Bind(snapshot);
        Require(session.TrySelectAction(Sr5CareerWizardActionIds.AdvanceActiveSkill), "select action");

        MemoryBackend backend = new();
        var store = new Sr5CareerWizardPhoneCheckpointStore(backend);
        Require(store.TryWrite(session), "checkpoint durable write/read-back");
        Sr5CareerWizardPhoneCheckpointRead read = store.Read();
        Require(read.Status == Sr5CareerWizardPhoneCheckpointReadStatus.Ready
                && read.Checkpoint is not null,
            "checkpoint read");
        Sr5CareerWizardDesktopState restored = new Sr5CareerWizardDesktopSession()
            .Bind(snapshot, read.Checkpoint);
        Require(restored.Resume.Restored
                && restored.SelectedActionId == Sr5CareerWizardActionIds.AdvanceActiveSkill,
            "exact restart restore");
        Require(!backend.Payload.Contains("quote", StringComparison.OrdinalIgnoreCase)
                && !backend.Payload.Contains("confirm", StringComparison.OrdinalIgnoreCase)
                && !backend.Payload.Contains("receipt", StringComparison.OrdinalIgnoreCase),
            "checkpoint must remain navigation-only");
    }

    private static void StaleWorkspaceRevisionSourceAndContentInvalidateRestart()
    {
        Sr5CareerWizardPhoneAuthorityProbe probe = Available(
            Sr5CareerWizardActionIds.AdvanceAttribute,
            H('a'));
        Sr5CareerWizardSnapshot original = Sr5CareerWizardPhoneProjection.Project(Workspace(), [probe]);
        var originalSession = new Sr5CareerWizardDesktopSession();
        originalSession.Bind(original);
        Sr5CareerWizardCheckpoint checkpoint = originalSession.CreateCheckpoint();

        Sr5CareerWizardSnapshot foreignWorkspace = Sr5CareerWizardPhoneProjection.Project(
            Workspace() with { WorkspaceId = "foreign-workspace" },
            [probe]);
        Require(Resume(foreignWorkspace, checkpoint).Resume.InvalidationReason
                == Sr5CareerWizardCheckpointInvalidationReasons.WorkspaceChanged,
            "foreign workspace invalidation");

        Sr5CareerWizardSnapshot revised = Sr5CareerWizardPhoneProjection.Project(
            Workspace() with { WorkspaceRevision = 42 },
            [probe]);
        Require(Resume(revised, checkpoint).Resume.InvalidationReason
                == Sr5CareerWizardCheckpointInvalidationReasons.WorkspaceRevisionChanged,
            "revision invalidation");

        Sr5CareerWizardSnapshot resaved = Sr5CareerWizardPhoneProjection.Project(
            Workspace() with { SavedRevision = 40 },
            [probe]);
        Require(Resume(resaved, checkpoint).Resume.InvalidationReason
                == Sr5CareerWizardCheckpointInvalidationReasons.SnapshotChanged,
            "saved revision invalidation");

        Sr5CareerWizardSnapshot changedSource = Sr5CareerWizardPhoneProjection.Project(
            Workspace(),
            [probe with { ProjectionDigest = H('c') }]);
        Require(Resume(changedSource, checkpoint).Resume.InvalidationReason
                == Sr5CareerWizardCheckpointInvalidationReasons.SnapshotChanged,
            "typed source projection invalidation");

        Sr5CareerWizardSnapshot changedContent = Sr5CareerWizardPhoneProjection.Project(
            Workspace() with { DocumentSha256 = new string('f', 64) },
            [probe]);
        Require(Resume(changedContent, checkpoint).Resume.InvalidationReason
                == Sr5CareerWizardCheckpointInvalidationReasons.SnapshotChanged,
            "document content invalidation");

        Sr5CareerWizardSnapshot changedPayload = Sr5CareerWizardPhoneProjection.Project(
            Workspace() with { PayloadSha256 = new string('e', 64) },
            [probe]);
        Require(Resume(changedPayload, checkpoint).Resume.InvalidationReason
                == Sr5CareerWizardCheckpointInvalidationReasons.SnapshotChanged,
            "payload byte invalidation");

        RequireThrows<InvalidOperationException>(
            () => Sr5CareerWizardPhoneProjection.Project(Workspace() with { RulesetId = "sr6" }, [probe]),
            "cross-ruleset authority must fail closed");
    }

    private static void MalformedOversizedAndNondurableCheckpointPayloadsFailClosed()
    {
        MemoryBackend malformed = new() { Payload = "not-base64" };
        Sr5CareerWizardPhoneCheckpointRead invalid =
            new Sr5CareerWizardPhoneCheckpointStore(malformed).Read();
        Require(invalid.Status == Sr5CareerWizardPhoneCheckpointReadStatus.Invalid
                && malformed.Payload.Length == 0,
            "malformed checkpoint must be removed");

        MemoryBackend oversized = new() { Payload = new string('a', 8193) };
        invalid = new Sr5CareerWizardPhoneCheckpointStore(oversized).Read();
        Require(invalid.Status == Sr5CareerWizardPhoneCheckpointReadStatus.Invalid
                && oversized.Payload.Length == 0,
            "oversized checkpoint must be removed");

        Sr5CareerWizardSnapshot snapshot = Sr5CareerWizardPhoneProjection.Project(
            Workspace(),
            [Available(Sr5CareerWizardActionIds.AdjustKarma, H('a'))]);
        var session = new Sr5CareerWizardDesktopSession();
        session.Bind(snapshot);
        MemoryBackend nondurable = new() { DropWrites = true };
        Require(!new Sr5CareerWizardPhoneCheckpointStore(nondurable).TryWrite(session),
            "a dropped write must block navigation");

        MemoryBackend unavailable = new() { ThrowOnRead = true };
        Require(new Sr5CareerWizardPhoneCheckpointStore(unavailable).Read().Status
                == Sr5CareerWizardPhoneCheckpointReadStatus.Unavailable,
            "unavailable storage must fail closed without accepting a checkpoint");
    }

    private static void NavigationModelsExposeNoMutationBoundary()
    {
        string[] forbidden = ["Apply", "Confirm", "Commit", "Mutate"];
        string[] methods = typeof(Sr5CareerWizardDesktopSession)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Concat(typeof(Sr5CareerWizardPhoneProjection)
                .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly))
            .Select(static method => method.Name)
            .Where(name => forbidden.Any(token => name.Contains(token, StringComparison.Ordinal)))
            .ToArray();
        Require(methods.Length == 0, "chooser/session must expose no mutation boundary");
    }

    private static Sr5CareerWizardDesktopState Resume(
        Sr5CareerWizardSnapshot snapshot,
        Sr5CareerWizardCheckpoint checkpoint)
        => new Sr5CareerWizardDesktopSession().Bind(snapshot, checkpoint);

    private static Sr5CareerWizardPhoneWorkspaceAuthority Workspace()
        => new(
            "workspace-career-phone",
            WorkspaceRevision: 41,
            SavedRevision: 39,
            RulesetId: "sr5",
            PayloadSha256: new string('1', 64),
            DocumentSha256: new string('2', 64));

    private static Sr5CareerWizardPhoneAuthorityProbe Available(string actionId, string digest)
        => new(actionId, true, [], [], digest);

    private static Sr5CareerWizardPhoneAuthorityProbe Blocked(string actionId, string digest)
        => new(actionId, false, [Sr5CareerWizardPhoneBlockers.NoEligibleTarget], [], digest);

    private static Sr5CareerWizardActionState Action(Sr5CareerWizardSnapshot snapshot, string actionId)
        => snapshot.Families.SelectMany(static family => family.Actions)
            .Single(action => action.ActionId == actionId);

    private static bool IsDigest(string value)
        => value.Length == 71 && value.StartsWith("sha256:", StringComparison.Ordinal);

    private static string H(char value) => "sha256:" + new string(value, 64);

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void RequireThrows<TException>(Action action, string message)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }
        throw new InvalidOperationException(message);
    }

    private sealed class MemoryBackend : ISr5CareerWizardPhoneCheckpointBackend
    {
        public string Payload { get; set; } = string.Empty;
        public bool DropWrites { get; init; }
        public bool ThrowOnRead { get; init; }

        public string Read()
            => ThrowOnRead ? throw new InvalidOperationException("unavailable") : Payload;

        public void Write(string payload)
        {
            if (!DropWrites)
                Payload = payload;
        }

        public void Remove() => Payload = string.Empty;
    }
}
