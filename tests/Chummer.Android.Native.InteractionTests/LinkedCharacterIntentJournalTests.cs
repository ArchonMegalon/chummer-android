using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Chummer.Android.Native;
using Chummer.Android.Platform;
using Chummer.Contracts.Characters;
using Chummer.Presentation.Overview;

internal static class LinkedCharacterIntentJournalTests
{
    private const string Owner = "owner-a";
    private const string Workspace = "workspace-a";
    private static readonly string OriginalSha = new('a', 64);
    private static readonly string SuccessorSha = new('b', 64);
    private static readonly WorkspaceCollectionItemTarget Target = new(
        WorkspaceCollectionKind.Contact, "11111111-1111-4111-8111-111111111111");

    public static async Task RunAsync()
    {
        await Task.Run(() =>
        {
            ReopeningRetainsTheTypedRequestAndExactSuccessor();
            ExpectedSuccessorDigestIsRequiredAndExact();
            ExpectedSuccessorEnvelopeTamperingFailsClosed();
            UncertainBeginRetainsIntentAndBlocksAnotherInstance();
            UncertainObservationDoesNotReleasePendingIntent();
            KnownPredispatchAbandonmentIsDistinctAndAppendOnly();
            WrongOwnerWorkspaceAndTrustNeverDiscloseOrResolve();
            InvalidTypedIntentNeverPublishes();
            UnknownCorruptMissingAndDuplicateFieldsFailClosed();
            BoundedReadsRejectOversizedRecordsAndCardinality();
            SymbolicLinksNeverEnterTheJournal();
            PreRenameFailureRetainsUnknownBytesWithoutAutomaticCleanup();
        }).WaitAsync(TimeSpan.FromSeconds(20));
        await ConcurrentInstancesAdmitOnlyOneUnresolvedIntentAsync();
        Console.WriteLine("PASS linked-character host intent journal (real tempfile/fsync, append-only uncertainty and exact current-effect observation; no Core receipt/device/power-cut proof)");
    }

    private static void ReopeningRetainsTheTypedRequestAndExactSuccessor()
    {
        foreach (bool attach in new[] { false, true })
        {
            using var fixture = new Fixture();
            AndroidLinkedCharacterIntent intent = Intent(fixture, attach);
            fixture.Journal().Begin(intent);
            byte[] original = File.ReadAllBytes(fixture.IntentPath(intent.OperationId));
            AndroidLinkedCharacterIntentRecord pending = fixture.Journal().Read(Owner, false, Workspace).Single();
            Require(pending.Intent == intent && !pending.EffectObserved && !pending.NotDispatched,
                "A new instance lost or resolved the exact retained typed intent.");
            foreach ((long content, long saved, string sha) in new[]
            {
                (5L, 5L, SuccessorSha), (7L, 5L, SuccessorSha), (6L, 6L, SuccessorSha),
                (6L, 4L, SuccessorSha), (6L, 5L, OriginalSha), (6L, 5L, new string('e', 64)), (6L, 5L, "bad-hash")
            })
                Reject(() => fixture.Journal().Observe(intent.OperationId, Owner, false, Workspace, sha, content, saved));
            Require(!File.Exists(fixture.ObservationPath(intent.OperationId)), "Invalid successor published observation bytes.");
            fixture.Journal().Observe(intent.OperationId, Owner, false, Workspace, SuccessorSha, 6, 5);
            AndroidLinkedCharacterIntentRecord observed = fixture.Journal().Read(Owner, false, Workspace).Single();
            Require(observed.EffectObserved && !observed.NotDispatched && observed.Observation is
                { Outcome: "current-effect-observed", ContentRevision: 6, SavedRevision: 5 } value
                && value.DocumentAuthoritySha256 == SuccessorSha && value.OwnerScope == Owner
                && value.WorkspaceId == Workspace && value.OperationId == intent.OperationId && !value.TrustedLocalOwner,
                "Current-effect observation lost its owned document/revision tuple or claimed a checkpoint.");
            Require(File.ReadAllBytes(fixture.IntentPath(intent.OperationId)).SequenceEqual(original),
                "Observation overwrote immutable intent bytes.");
            byte[] terminal = File.ReadAllBytes(fixture.ObservationPath(intent.OperationId));
            Reject(() => fixture.Journal().Begin(intent));
            Reject(() => fixture.Journal().Observe(intent.OperationId, Owner, false, Workspace, SuccessorSha, 6, 5));
            Reject(() => fixture.Journal().AbandonBeforeDispatch(intent.OperationId, Owner, false, Workspace));
            Require(File.ReadAllBytes(fixture.ObservationPath(intent.OperationId)).SequenceEqual(terminal),
                "Duplicate/contradictory acknowledgement overwrote a terminal observation.");
            fixture.Journal().Begin(Intent(fixture, attach) with
            { ContentRevision = 6, DocumentAuthoritySha256 = SuccessorSha, ExpectedDocumentAuthoritySha256 = new string('d', 64) });
            Require(fixture.Journal().ReadAll(Owner, false).Count == 2, "Observed history was evicted for new intent.");
        }
    }

    private static void UncertainBeginRetainsIntentAndBlocksAnotherInstance()
    {
        using var fixture = new Fixture();
        AndroidLinkedCharacterIntent intent = Intent(fixture);
        bool faultReached = false;
        var journal = fixture.Journal(directory =>
        {
            AndroidPrivateFileDurability.SyncDirectory(directory);
            if (File.Exists(fixture.IntentPath(intent.OperationId)))
            { faultReached = true; throw new IOException("Injected post-rename directory acknowledgement failure."); }
        });
        Reject(() => journal.Begin(intent));
        Require(faultReached && File.Exists(fixture.IntentPath(intent.OperationId)), "Test missed the real post-rename boundary.");
        Require(fixture.Journal().Read(Owner, false, Workspace) is [var pending] && !pending.EffectObserved,
            "An ambiguous Begin was lost or promoted on reopen.");
        byte[] original = File.ReadAllBytes(fixture.IntentPath(intent.OperationId));
        Reject(() => fixture.Journal().Begin(Intent(fixture)));
        Reject(() => fixture.Journal().Begin(intent));
        Require(File.ReadAllBytes(fixture.IntentPath(intent.OperationId)).SequenceEqual(original)
            && Directory.GetFiles(fixture.Directory).Length == 1, "Ambiguous Begin was overwritten, retried or deleted.");
    }

    private static void ExpectedSuccessorDigestIsRequiredAndExact()
    {
        foreach (bool attach in new[] { false, true })
        {
            using var fixture = new Fixture();
            AndroidLinkedCharacterIntent intent = Intent(fixture, attach);
            foreach (string? invalid in new string?[]
            { null, "", "not-a-digest", new string('b', 63), new string('B', 64), OriginalSha })
                Reject(() => fixture.Journal().Begin(intent with { ExpectedDocumentAuthoritySha256 = invalid! }));
            Require(!Directory.Exists(fixture.Directory), "Missing, malformed or baseline successor digest created journal storage.");
            fixture.Journal().Begin(intent);
            byte[] before = File.ReadAllBytes(fixture.IntentPath(intent.OperationId));
            string alternate = new('d', 64); // Valid SHA shape and different from both bound document identities.
            Reject(() => fixture.Journal().Observe(intent.OperationId, Owner, false, Workspace, alternate, 6, 5));
            AndroidLinkedCharacterIntentRecord pending = fixture.Journal().Read(Owner, false, Workspace).Single();
            Require(!pending.EffectObserved && !pending.NotDispatched && pending.Observation is null
                && pending.Intent.ExpectedDocumentAuthoritySha256 == SuccessorSha
                && !File.Exists(fixture.ObservationPath(intent.OperationId))
                && File.ReadAllBytes(fixture.IntentPath(intent.OperationId)).SequenceEqual(before),
                "A different well-formed successor digest resolved intent or changed its durable binding.");
            fixture.Journal().Observe(intent.OperationId, Owner, false, Workspace, SuccessorSha, 6, 5);
            Require(fixture.Journal().ReadAll(Owner, false).Single().EffectObserved,
                "The exact expected successor was rejected after an alternate observation failed.");
        }
    }

    private static void ExpectedSuccessorEnvelopeTamperingFailsClosed()
    {
        foreach (string mutation in new[] { "missing", "null", "malformed", "baseline", "changed-without-seal" })
        {
            using var fixture = new Fixture();
            AndroidLinkedCharacterIntent intent = Intent(fixture);
            fixture.Journal().Begin(intent);
            string path = fixture.IntentPath(intent.OperationId);
            JsonNode envelope = JsonNode.Parse(File.ReadAllText(path))!;
            ResealTestEnvelope(envelope);
            File.WriteAllText(path, envelope.ToJsonString());
            Require(fixture.Journal().ReadAll(Owner, false).Single().Intent == intent,
                "Test resealing did not preserve a valid real intent envelope.");
            JsonObject payload = envelope["Payload"]!.AsObject();
            switch (mutation)
            {
                case "missing":
                    Require(payload.Remove("ExpectedDocumentAuthoritySha256"), "Expected digest field was absent from the real envelope.");
                    break;
                case "null": payload["ExpectedDocumentAuthoritySha256"] = null; break;
                case "malformed": payload["ExpectedDocumentAuthoritySha256"] = new string('B', 64); break;
                case "baseline": payload["ExpectedDocumentAuthoritySha256"] = OriginalSha; break;
                case "changed-without-seal": payload["ExpectedDocumentAuthoritySha256"] = new string('d', 64); break;
            }
            // Recompute integrity for malformed schema/value cases: rejection must
            // come from the actual schema/authority validation, not a stale seal.
            if (mutation != "changed-without-seal") ResealTestEnvelope(envelope);
            File.WriteAllText(path, envelope.ToJsonString());
            byte[] hostile = File.ReadAllBytes(path);
            Reject(() => fixture.Journal().ReadAll(Owner, false));
            Reject(() => fixture.Journal().Begin(Intent(fixture)));
            Require(File.ReadAllBytes(path).SequenceEqual(hostile)
                && !File.Exists(fixture.ObservationPath(intent.OperationId)),
                "Rejecting expected-digest tampering rewrote or resolved retained evidence.");
        }
        using var alternate = new Fixture();
        AndroidLinkedCharacterIntent original = Intent(alternate);
        alternate.Journal().Begin(original);
        alternate.Journal().Observe(original.OperationId, Owner, false, Workspace, SuccessorSha, 6, 5);
        string observationPath = alternate.ObservationPath(original.OperationId);
        JsonNode observation = JsonNode.Parse(File.ReadAllText(observationPath))!;
        ResealTestEnvelope(observation);
        File.WriteAllText(observationPath, observation.ToJsonString());
        Require(alternate.Journal().ReadAll(Owner, false).Single().EffectObserved,
            "Test resealing did not preserve a valid real observation envelope.");
        observation["Payload"]!["DocumentAuthoritySha256"] = new string('d', 64);
        ResealTestEnvelope(observation);
        File.WriteAllText(observationPath, observation.ToJsonString());
        Reject(() => alternate.Journal().Read(Owner, false, Workspace));
        Reject(() => alternate.Journal().Begin(Intent(alternate)));
    }

    private static void ResealTestEnvelope(JsonNode envelope)
    {
        // Test-only host journal integrity; never a Core or publication receipt.
        envelope["IntegritySha256"] = Convert.ToHexStringLower(SHA256.HashData(
            JsonSerializer.SerializeToUtf8Bytes(new
            { Schema = envelope["Schema"]!.GetValue<string>(), Payload = envelope["Payload"] })));
    }

    private static void UncertainObservationDoesNotReleasePendingIntent()
    {
        foreach (bool abandon in new[] { false, true })
        {
            using var fixture = new Fixture();
            AndroidLinkedCharacterIntent intent = Intent(fixture);
            fixture.Journal().Begin(intent);
            bool faultReached = false;
            var journal = fixture.Journal(directory =>
            {
                AndroidPrivateFileDurability.SyncDirectory(directory);
                if (File.Exists(fixture.ObservationPath(intent.OperationId)))
                { faultReached = true; throw new IOException("Injected observation acknowledgement failure."); }
            });
            Reject(() =>
            {
                if (abandon) journal.AbandonBeforeDispatch(intent.OperationId, Owner, false, Workspace);
                else journal.Observe(intent.OperationId, Owner, false, Workspace, SuccessorSha, 6, 5);
            });
            AndroidLinkedCharacterIntentRecord pending = fixture.Journal().Read(Owner, false, Workspace).Single();
            Require(faultReached && pending.Observation is not null && !pending.ObservationAcknowledged
                && !pending.EffectObserved && !pending.NotDispatched, "Failed observation acknowledgement released custody.");
            Reject(() => fixture.Journal().Begin(Intent(fixture)));
            Reject(() => fixture.Journal().AbandonBeforeDispatch(intent.OperationId, Owner, false, Workspace));
            Reject(() => fixture.Journal().Observe(intent.OperationId, Owner, false, Workspace, SuccessorSha, 6, 5));
            Require(Directory.GetFiles(fixture.Directory).Length == 2, "Failed observation triggered rewriting or cleanup.");
        }
    }

    private static void KnownPredispatchAbandonmentIsDistinctAndAppendOnly()
    {
        using var fixture = new Fixture();
        AndroidLinkedCharacterIntent intent = Intent(fixture, true);
        fixture.Journal().Begin(intent);
        fixture.Journal().AbandonBeforeDispatch(intent.OperationId, Owner, false, Workspace);
        AndroidLinkedCharacterIntentRecord abandoned = fixture.Journal().ReadAll(Owner, false).Single();
        Require(abandoned.NotDispatched && !abandoned.EffectObserved && abandoned.Observation is
            { Outcome: "not-dispatched", ContentRevision: 5, SavedRevision: 5 } value
            && value.DocumentAuthoritySha256 == OriginalSha, "Predispatch abandonment invented a successor or commit receipt.");
        Reject(() => fixture.Journal().Begin(intent));
        fixture.Journal().Begin(Intent(fixture, true));
        Require(fixture.Journal().ReadAll(Owner, false).Count == 2
            && File.Exists(intent.StagedFileName), "Known abandonment erased history or reclaimed staged bytes itself.");
    }

    private static void WrongOwnerWorkspaceAndTrustNeverDiscloseOrResolve()
    {
        using var fixture = new Fixture();
        AndroidLinkedCharacterIntent intent = Intent(fixture);
        fixture.Journal().Begin(intent);
        foreach ((string owner, bool trusted, string workspace) in new[]
        { ("owner-b", false, Workspace), (Owner, true, Workspace), (Owner, false, "workspace-b") })
        {
            Require(fixture.Journal().Read(owner, trusted, workspace).Count == 0, "Read disclosed another custody scope.");
            Reject(() => fixture.Journal().Observe(intent.OperationId, owner, trusted, workspace, SuccessorSha, 6, 5));
            Reject(() => fixture.Journal().AbandonBeforeDispatch(intent.OperationId, owner, trusted, workspace));
            // The same operation identity can never be republished under another owner.
            Reject(() => fixture.Journal().Begin(intent with { OwnerScope = owner, TrustedLocalOwner = trusted, WorkspaceId = workspace }));
        }
        fixture.Journal().Begin(Intent(fixture) with { WorkspaceId = "workspace-b" });
        fixture.Journal().Begin(Intent(fixture) with { OwnerScope = "owner-b" });
        Require(fixture.Journal().ReadAll(Owner, false).Count == 2
            && fixture.Journal().ReadAll("owner-b", false).Count == 1
            && fixture.Journal().ReadAll(Owner, true).Count == 0, "Owner-wide history exposed or lost workspace-scoped intents.");
    }

    private static void InvalidTypedIntentNeverPublishes()
    {
        using var fixture = new Fixture();
        AndroidLinkedCharacterIntent valid = Intent(fixture);
        foreach (AndroidLinkedCharacterIntent bad in new[]
        {
            valid with { OperationId = Guid.Empty }, valid with { OwnerScope = "" }, valid with { WorkspaceId = "\n" },
            valid with { ContentRevision = -1 }, valid with { ContentRevision = long.MaxValue },
            valid with { SavedRevision = -1 }, valid with { SavedRevision = 6 },
            valid with { DocumentAuthoritySha256 = new string('A', 64) }, valid with { EditorAuthoritySha256 = "bad" },
            valid with { RequestSha256 = new string('f', 64) }, valid with { SectionId = "pets" },
            valid with { Target = Target with { Kind = WorkspaceCollectionKind.Gear } },
            valid with { Target = Target with { ItemId = "item-a" } },
            valid with { Target = Target with { NestedItemId = Target.ItemId } },
            valid with { Attach = true }, valid with { StagedFileName = "/private/unrequested.chum5" }
        }) Reject(() => fixture.Journal().Begin(bad));
        AndroidLinkedCharacterIntent attached = Intent(fixture, true);
        foreach (AndroidLinkedCharacterIntent bad in new[]
        {
            attached with { Attachment = null }, attached with { StagedFileSha256 = null },
            attached with { StagedFileName = "relative.chum5" },
            attached with { Attachment = attached.Attachment! with { FileName = "/different.chum5" } },
            attached with { Attachment = attached.Attachment! with { DisplayName = "Altered request.chum5" } },
            attached with { Attachment = attached.Attachment! with { Target = Target with { ItemId = Guid.NewGuid().ToString("D") } } }
        }) Reject(() => fixture.Journal().Begin(bad));
        Require(!Directory.Exists(fixture.Directory), "Invalid intent created journal storage.");
        WorkspaceCollectionItemTarget pet = Target with { Kind = WorkspaceCollectionKind.Pet };
        WorkspaceCollectionMutationRequest request = new WorkspaceRemoveLinkedCharacterRequest(pet);
        fixture.Journal().Begin(valid with { Target = pet, SectionId = "pets", RequestSha256 = RequestHash(request) });
    }

    private static void UnknownCorruptMissingAndDuplicateFieldsFailClosed()
    {
        foreach (string corruption in new[]
        { "hash", "unknown-field", "duplicate-field", "missing-field", "readonly-identity", "orphan", "truncated", "unknown-entry" })
        {
            using var fixture = new Fixture();
            AndroidLinkedCharacterIntent intent = Intent(fixture, corruption == "readonly-identity");
            fixture.Journal().Begin(intent);
            fixture.Journal().Observe(intent.OperationId, Owner, false, Workspace, SuccessorSha, 6, 5);
            string path = fixture.ObservationPath(intent.OperationId);
            switch (corruption)
            {
                case "hash":
                    JsonNode json = JsonNode.Parse(File.ReadAllText(path))!;
                    json["IntegritySha256"] = new string('0', 64);
                    File.WriteAllText(path, json.ToJsonString()); break;
                case "unknown-field":
                    JsonNode unknown = JsonNode.Parse(File.ReadAllText(path))!;
                    unknown["AuthorityClaim"] = "not-permitted";
                    File.WriteAllText(path, unknown.ToJsonString()); break;
                case "duplicate-field":
                    File.WriteAllText(path, File.ReadAllText(path).Replace("\"Schema\":", "\"Schema\":\"duplicate\",\"Schema\":", StringComparison.Ordinal)); break;
                case "missing-field":
                    path = fixture.IntentPath(intent.OperationId);
                    JsonNode missingField = JsonNode.Parse(File.ReadAllText(path))!;
                    missingField["Payload"]!.AsObject().Remove("Attachment");
                    File.WriteAllText(path, missingField.ToJsonString()); break;
                case "readonly-identity":
                    path = fixture.IntentPath(intent.OperationId);
                    JsonNode changedIdentity = JsonNode.Parse(File.ReadAllText(path))!;
                    changedIdentity["Payload"]!["Attachment"]!["Identity"]!["DisplayMetatype"] = "forged display";
                    File.WriteAllText(path, changedIdentity.ToJsonString()); break;
                case "orphan": File.Delete(fixture.IntentPath(intent.OperationId)); break;
                case "truncated": File.WriteAllText(path, "{"); break;
                case "unknown-entry": File.WriteAllText(Path.Combine(fixture.Directory, "unknown.tmp"), "uncertain"); break;
            }
            Reject(() => fixture.Journal().Read(Owner, false, Workspace));
            Reject(() => fixture.Journal().Begin(Intent(fixture)));
        }
        using var missing = new Fixture();
        AndroidLinkedCharacterIntent original = Intent(missing);
        missing.Journal().Begin(original);
        missing.Journal().Observe(original.OperationId, Owner, false, Workspace, SuccessorSha, 6, 5);
        File.Delete(missing.ObservationPath(original.OperationId)); // Test loss, never product cleanup.
        Require(!missing.Journal().ReadAll(Owner, false).Single().EffectObserved, "Missing observation was inferred from intent.");
        Reject(() => missing.Journal().Begin(Intent(missing)));
    }

    private static void BoundedReadsRejectOversizedRecordsAndCardinality()
    {
        using var oversized = new Fixture();
        Directory.CreateDirectory(oversized.Directory);
        File.WriteAllBytes(oversized.IntentPath(Guid.NewGuid()), new byte[AndroidLinkedCharacterIntentJournal.MaximumRecordBytes + 1]);
        Require(Reject(() => oversized.Journal().ReadAll(Owner, false)).Message.Contains("size limit", StringComparison.Ordinal),
            "Oversized record reached JSON parsing before the byte bound.");
        using var excessive = new Fixture();
        Directory.CreateDirectory(excessive.Directory);
        for (int i = 0; i <= AndroidLinkedCharacterIntentJournal.MaximumRecords; i++)
            File.WriteAllText(excessive.IntentPath(Guid.NewGuid()), "not-json");
        Require(Reject(() => excessive.Journal().ReadAll(Owner, false)).Message.Contains("record limit", StringComparison.Ordinal),
            "Excessive record cardinality reached JSON parsing before its bound.");
    }

    private static void SymbolicLinksNeverEnterTheJournal()
    {
        using var fixture = new Fixture();
        AndroidLinkedCharacterIntent intent = Intent(fixture);
        fixture.Journal().Begin(intent);
        string path = fixture.IntentPath(intent.OperationId);
        string elsewhere = Path.Combine(fixture.Root, "retained-intent.json");
        File.Move(path, elsewhere);
        File.CreateSymbolicLink(path, elsewhere);
        Reject(() => fixture.Journal().ReadAll(Owner, false));
        Require(File.Exists(elsewhere), "Rejecting a symbolic link deleted its target.");
        using var parent = new Fixture();
        string alias = Path.Combine(parent.Root, "alias");
        Directory.CreateSymbolicLink(alias, fixture.Root);
        Reject(() => new AndroidLinkedCharacterIntentJournal(alias).ReadAll(Owner, false));
    }

    private static void PreRenameFailureRetainsUnknownBytesWithoutAutomaticCleanup()
    {
        using var fixture = new Fixture();
        var journal = new AndroidLinkedCharacterIntentJournal(fixture.Root,
            AndroidPrivateFileDurability.SyncDirectory, stream => throw new IOException("Injected file flush failure."));
        Reject(() => journal.Begin(Intent(fixture)));
        Require(Directory.GetFiles(fixture.Directory) is [var temporary] && temporary.EndsWith(".tmp", StringComparison.Ordinal),
            "Interrupted temporary intent bytes were automatically removed or published.");
        Reject(() => fixture.Journal().ReadAll(Owner, false));
        Reject(() => fixture.Journal().Begin(Intent(fixture)));
    }

    private static async Task ConcurrentInstancesAdmitOnlyOneUnresolvedIntentAsync()
    {
        using var fixture = new Fixture();
        AndroidLinkedCharacterIntent first = Intent(fixture);
        AndroidLinkedCharacterIntent second = Intent(fixture);
        async Task<bool> Begin(AndroidLinkedCharacterIntent intent) => await Task.Run(() =>
        {
            try { fixture.Journal().Begin(intent); return true; }
            catch (IOException) { return false; }
        });
        bool[] results = await Task.WhenAll(Begin(first), Begin(second)).WaitAsync(TimeSpan.FromSeconds(10));
        Require(results.Count(result => result) == 1 && fixture.Journal().ReadAll(Owner, false).Count == 1,
            "Separate journal instances admitted concurrent unresolved intent for one owner/workspace.");
    }

    private static AndroidLinkedCharacterIntent Intent(Fixture fixture, bool attach = false)
    {
        string staged = Path.Combine(fixture.Root, "staged.chum5");
        WorkspaceSetLinkedCharacterRequest? attachment = null;
        if (attach)
        {
            File.WriteAllText(staged, "test-only staged bytes; not a Core document");
            attachment = new(Target, staged, "linked-characters/staged.chum5", "Runner.chum5",
                new CharacterLinkedDocument("Runner", "Runner", "Alias", "Human", "", "", ""));
        }
        WorkspaceCollectionMutationRequest request = attachment is not null
            ? attachment : new WorkspaceRemoveLinkedCharacterRequest(Target);
        return new(Guid.NewGuid(), Owner, false, Workspace, 5, 5, OriginalSha, SuccessorSha, new string('c', 64),
            "contacts", Target, attach, RequestHash(request), attach ? staged : null,
            attach ? Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(staged))) : null, attachment);
    }

    private static string RequestHash(WorkspaceCollectionMutationRequest request)
        => Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(request, request.GetType())));
    private static IOException Reject(Action action)
    {
        try { action(); }
        catch (IOException error) { return error; }
        throw new InvalidOperationException("Expected fail-closed journal rejection.");
    }
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = System.IO.Directory.CreateTempSubdirectory("chummer-link-intent-").FullName;
        public string Directory => Path.Combine(Root, "linked-character-intents-v1");
        public string IntentPath(Guid id) => Path.Combine(Directory, $"{id:N}.intent.json");
        public string ObservationPath(Guid id) => Path.Combine(Directory, $"{id:N}.observation.json");
        public AndroidLinkedCharacterIntentJournal Journal(Action<string>? sync = null)
            => sync is null ? new(Root) : new(Root, sync);
        public void Dispose() => System.IO.Directory.Delete(Root, recursive: true);
    }
}
