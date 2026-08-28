using Chummer.Android.Native;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

internal static class Program
{
    private static readonly CharacterWorkspaceId WorkspaceId = new("workspace-career-cyberware");
    private static readonly CharacterCyberwareSourceId SourceId = new(
        Guid.Parse("11111111-1111-4111-8111-111111111111"));
    private static readonly CharacterCyberwareGradeId GradeId = new(
        Guid.Parse("22222222-2222-4222-8222-222222222222"));

    private static int Main()
    {
        try
        {
            ReviewCommitRestartAndUndo();
            UnknownOutcomeNeverReplays();
            StaleReviewIsDiscarded();
            Console.WriteLine("SR5 Career Cyberware purchase Android orchestration tests passed (3 scenarios).");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static void ReviewCommitRestartAndUndo()
    {
        FakeAuthority authority = new();
        FakeWorkspaceStore workspaces = new(CharacterXml(), revision: 7);
        FakeCheckpointStore checkpoints = new();
        Sr5CareerCyberwarePurchaseService first = new(authority, workspaces, checkpoints);

        Sr5CareerCyberwarePurchaseSnapshot loaded = first.Load(WorkspaceId);
        Assert(loaded.IsReady && loaded.Quote?.Exact == true, "initial exact quote");
        Sr5CareerCyberwarePurchaseSnapshot reviewed = first.Review(WorkspaceId);
        Assert(reviewed.CanConfirm, "separate reviewed confirmation");
        CharacterCyberwarePurchaseCommand command = reviewed.Checkpoint!.Command!;
        Assert(command.NewInstanceId.Value != Guid.Empty, "stable new instance identity");
        Assert(command.NewExpenseId != Guid.Empty, "stable new expense identity");

        Sr5CareerCyberwarePurchaseSnapshot applied = first.Confirm(WorkspaceId);
        Assert(applied.HasAppliedReceipt, "applied receipt");
        try
        {
            _ = first.Confirm(WorkspaceId);
            throw new InvalidOperationException("repeated confirmation must fail closed");
        }
        catch (InvalidOperationException exception)
            when (exception.Message.Contains("stale", StringComparison.OrdinalIgnoreCase))
        {
            // The applied checkpoint cannot become a fresh review again.
        }
        Assert(authority.CommitCalls == 1,
            "repeated confirmation must never replay the authority call");
        Assert(workspaces.Revision == 8 && workspaces.SavedRevision == 8, "atomic saved revision");
        Assert(workspaces.Xml.Contains(command.NewInstanceId.Value.ToString("D"), StringComparison.Ordinal),
            "saved exact instance");

        Sr5CareerCyberwarePurchaseService restarted = new(authority, workspaces, checkpoints);
        Sr5CareerCyberwarePurchaseSnapshot recovered = restarted.Load(WorkspaceId);
        Assert(recovered.HasAppliedReceipt, "restart receipt lookup");
        Sr5CareerCyberwarePurchaseSnapshot undone = restarted.Undo(WorkspaceId);
        Assert(undone.Notice == Sr5CareerCyberwarePurchaseNotices.UndoApplied, "receipt-bound undo");
        Assert(workspaces.Revision == 9 && !workspaces.Xml.Contains("<purchased", StringComparison.Ordinal),
            "undo exact persisted nodes");
    }

    private static void UnknownOutcomeNeverReplays()
    {
        FakeAuthority authority = new();
        FakeWorkspaceStore workspaces = new(CharacterXml(), revision: 3)
        {
            AmbiguousWrite = true
        };
        FakeCheckpointStore checkpoints = new();
        Sr5CareerCyberwarePurchaseService service = new(authority, workspaces, checkpoints);
        _ = service.Review(WorkspaceId);
        Sr5CareerCyberwarePurchaseSnapshot result = service.Confirm(WorkspaceId);
        Assert(result.IsRecoveryUnknown, "ambiguous CAS is locked");
        Assert(authority.CommitCalls == 1, "one commit invocation");

        Sr5CareerCyberwarePurchaseService restarted = new(authority, workspaces, checkpoints);
        Sr5CareerCyberwarePurchaseSnapshot recovered = restarted.Load(WorkspaceId);
        Assert(recovered.CanConfirm, "unchanged pre-commit revision resolves to not applied");
        Assert(recovered.Notice == Sr5CareerCyberwarePurchaseNotices.CommitNotApplied,
            "deterministic not-applied recovery");
        Assert(authority.CommitCalls == 1, "restart never replays commit");
    }

    private static void StaleReviewIsDiscarded()
    {
        FakeAuthority authority = new();
        FakeWorkspaceStore workspaces = new(CharacterXml(), revision: 11);
        FakeCheckpointStore checkpoints = new();
        Sr5CareerCyberwarePurchaseService service = new(authority, workspaces, checkpoints);
        Sr5CareerCyberwarePurchaseSnapshot reviewed = service.Review(WorkspaceId);
        Assert(reviewed.CanConfirm, "review is current");
        workspaces.ExternalReplace(CharacterXml().Replace("100000", "99999", StringComparison.Ordinal));

        Sr5CareerCyberwarePurchaseSnapshot rebound = service.Load(WorkspaceId);
        Assert(!rebound.CanConfirm, "stale review discarded");
        Assert(rebound.Checkpoint?.Phase == Sr5CareerCyberwarePurchasePhase.Editing,
            "rebound editing checkpoint");
        Assert(rebound.Notice == Sr5CareerCyberwarePurchaseNotices.ReviewStale,
            "stale review notice");
    }

    private static string CharacterXml()
        => "<character><created>True</created><nuyen>100000</nuyen><cyberwares/><expenses/></character>";

    private static void Assert(bool condition, string name)
    {
        if (!condition)
            throw new InvalidOperationException($"Assertion failed: {name}");
    }

    private sealed class FakeCheckpointStore : ISr5CareerCyberwarePurchaseCheckpointStore
    {
        private Sr5CareerCyberwarePurchaseCheckpoint? _checkpoint;
        public Sr5CareerCyberwarePurchaseCheckpoint? Read(CharacterWorkspaceId workspaceId)
            => _checkpoint?.WorkspaceId == workspaceId ? _checkpoint : null;
        public void Write(Sr5CareerCyberwarePurchaseCheckpoint checkpoint) => _checkpoint = checkpoint;
        public void Clear(CharacterWorkspaceId workspaceId) => _checkpoint = null;
    }

    private sealed class FakeWorkspaceStore(string xml, long revision) : ISr5CareerCyberwareWorkspaceStore
    {
        public string Xml { get; private set; } = xml;
        public long Revision { get; private set; } = revision;
        public long SavedRevision { get; private set; } = revision;
        public bool AmbiguousWrite { get; init; }

        public Sr5CareerCyberwareWorkspaceSnapshot? Read(CharacterWorkspaceId workspaceId)
            => workspaceId == WorkspaceId
                ? new(workspaceId, Revision, SavedRevision, Document(Xml))
                : null;

        public Sr5CareerCyberwareWorkspaceWriteResult ReplaceAndCheckpoint(
            Sr5CareerCyberwareWorkspaceSnapshot expected,
            string characterXml)
        {
            if (AmbiguousWrite)
                return new(false, false, Revision, SavedRevision, "write-outcome-unknown");
            if (expected.ContentRevision != Revision)
                return new(false, true, Revision, SavedRevision, "revision-conflict");
            Xml = characterXml;
            Revision++;
            SavedRevision = Revision;
            return new(true, false, Revision, SavedRevision, string.Empty);
        }

        public void ExternalReplace(string replacement)
        {
            Xml = replacement;
            Revision++;
            SavedRevision = Revision;
        }

        private static WorkspaceDocument Document(string content) => new(content);
    }

    private sealed class FakeAuthority : ICharacterCyberwarePurchaseAuthority
    {
        public int CommitCalls { get; private set; }

        public CharacterCyberwarePurchasePreparation Prepare(string characterXml, long contentRevision)
        {
            decimal nuyen = characterXml.Contains("99999", StringComparison.Ordinal) ? 99999m :
                characterXml.Contains("95000", StringComparison.Ordinal) ? 95000m : 100000m;
            return new(
                Exact: true,
                Blockers: [],
                contentRevision,
                CharacterCyberwarePurchaseRules.ComputeCharacterDigest(characterXml),
                CatalogDigest: new string('a', 64),
                SettingsProfileId: "sr5-test",
                CyberwareXmlDigest: new string('b', 64),
                AvailableNuyen: nuyen,
                ExCon: false,
                EssenceHoleRating: null,
                EssenceAntiHoleRating: null,
                new CharacterCyberwarePurchaseSettings(false, false, 1m, false, 1m, 2, false, []),
                Entries:
                [
                    new CharacterCyberwarePurchaseCatalogEntry(
                        SourceId,
                        "Simrig",
                        "Headware",
                        "0.1",
                        "[0]",
                        "6",
                        "5000",
                        "SR5",
                        "452",
                        true,
                        string.Empty,
                        [],
                        [new CharacterCyberwarePurchaseGrade(GradeId, "Standard", 1m, 1m, 0)])
                ],
                Exclusions: []);
        }

        public CharacterCyberwarePurchaseQuote Quote(
            CharacterCyberwarePurchasePreparation preparation,
            CharacterCyberwarePurchaseSelection selection)
            => CharacterCyberwarePurchaseRules.Quote(preparation, selection);

        public CharacterCyberwarePurchaseCommitResult Commit(
            string characterXml,
            long currentContentRevision,
            CharacterCyberwarePurchaseCommand command)
        {
            CommitCalls++;
            CharacterCyberwarePurchasePreparation preparation = Prepare(characterXml, currentContentRevision);
            CharacterCyberwarePurchaseQuote quote = Quote(preparation, command.Selection);
            if (!quote.Exact
                || command.ExpectedContentRevision != currentContentRevision
                || command.ExpectedCharacterDigest != preparation.CharacterDigest
                || command.ExpectedCatalogDigest != preparation.CatalogDigest
                || command.ExpectedQuoteDigest != quote.QuoteDigest)
            {
                return Blocked(characterXml, currentContentRevision, "stale");
            }

            string purchased = $"<purchased instance=\"{command.NewInstanceId.Value:D}\" expense=\"{command.NewExpenseId:D}\"/>";
            string output = characterXml.Replace("</character>", purchased + "</character>", StringComparison.Ordinal)
                .Replace("100000", "95000", StringComparison.Ordinal);
            string outputDigest = CharacterCyberwarePurchaseRules.ComputeCharacterDigest(output);
            var unsigned = new CharacterCyberwarePurchaseUndoReceipt(
                currentContentRevision + 1,
                outputDigest,
                currentContentRevision,
                preparation.CharacterDigest,
                preparation.AvailableNuyen,
                null,
                null,
                preparation.CatalogDigest,
                quote.QuoteDigest,
                command.Selection.SourceId,
                command.Selection.GradeId,
                command.Selection,
                command.NewInstanceId,
                command.NewExpenseId,
                command.ExpenseDate,
                quote.NuyenDelta,
                new string('c', 64),
                new string('d', 64),
                string.Empty);
            CharacterCyberwarePurchaseUndoReceipt receipt = unsigned with
            {
                ReceiptDigest = CharacterCyberwarePurchaseRules.ComputeUndoReceiptDigest(unsigned)
            };
            return new(
                true,
                string.Empty,
                currentContentRevision,
                currentContentRevision + 1,
                preparation.CharacterDigest,
                outputDigest,
                output,
                command.NewInstanceId,
                command.NewExpenseId,
                quote.NuyenDelta,
                0m,
                preparation.CatalogDigest,
                quote.QuoteDigest,
                receipt);
        }

        public CharacterCyberwarePurchaseCommitResult Undo(
            string characterXml,
            long currentContentRevision,
            CharacterCyberwarePurchaseUndoCommand command)
        {
            CharacterCyberwarePurchaseUndoReceipt? receipt = command.Receipt;
            if (receipt is null
                || receipt.ContentRevision != currentContentRevision
                || receipt.CharacterDigest != CharacterCyberwarePurchaseRules.ComputeCharacterDigest(characterXml)
                || receipt.ReceiptDigest != CharacterCyberwarePurchaseRules.ComputeUndoReceiptDigest(receipt))
            {
                return Blocked(characterXml, currentContentRevision, "stale-undo");
            }
            int start = characterXml.IndexOf("<purchased", StringComparison.Ordinal);
            int end = start < 0 ? -1 : characterXml.IndexOf("/>", start, StringComparison.Ordinal);
            if (start < 0 || end < 0)
                return Blocked(characterXml, currentContentRevision, "missing-receipt-nodes");
            string output = characterXml.Remove(start, end + 2 - start)
                .Replace("95000", "100000", StringComparison.Ordinal);
            return new(
                true,
                string.Empty,
                currentContentRevision,
                currentContentRevision + 1,
                receipt.CharacterDigest,
                CharacterCyberwarePurchaseRules.ComputeCharacterDigest(output),
                output,
                receipt.InstanceId,
                receipt.ExpenseId,
                -receipt.NuyenDelta,
                0m,
                receipt.CatalogDigest,
                receipt.QuoteDigest,
                null);
        }

        private static CharacterCyberwarePurchaseCommitResult Blocked(
            string xml,
            long revision,
            string reason)
        {
            string digest = CharacterCyberwarePurchaseRules.ComputeCharacterDigest(xml);
            return new(
                false,
                reason,
                revision,
                revision,
                digest,
                digest,
                xml,
                new CharacterCyberwareInstanceId(Guid.Empty),
                Guid.Empty,
                0m,
                0m,
                string.Empty,
                string.Empty,
                null);
        }
    }
}
