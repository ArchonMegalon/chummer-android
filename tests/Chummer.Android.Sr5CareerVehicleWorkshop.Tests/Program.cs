using Chummer.Android.Native;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

internal static class Program
{
    private static readonly CharacterWorkspaceId WorkspaceId = new("vehicle-workshop-test");
    private static readonly CharacterVehicleChassisSourceId VehicleId = new(
        Guid.Parse("11111111-1111-4111-8111-111111111111"));
    private static readonly CharacterVehicleChassisSourceId DroneId = new(
        Guid.Parse("22222222-2222-4222-8222-222222222222"));
    private static readonly CharacterVehicleModificationSourceId ModId = new(
        Guid.Parse("33333333-3333-4333-8333-333333333333"));
    private static readonly CharacterVehicleWeaponMountComponentSourceId SizeId = new(
        Guid.Parse("44444444-4444-4444-8444-444444444444"));
    private static readonly CharacterVehicleWeaponMountComponentSourceId VisibilityId = new(
        Guid.Parse("55555555-5555-4555-8555-555555555555"));
    private static readonly CharacterVehicleWeaponMountComponentSourceId FlexibilityId = new(
        Guid.Parse("66666666-6666-4666-8666-666666666666"));
    private static readonly CharacterVehicleWeaponMountComponentSourceId ControlId = new(
        Guid.Parse("77777777-7777-4777-8777-777777777777"));
    private static readonly CharacterVehicleWeaponMountComponentSourceId ConflictControlId = new(
        Guid.Parse("88888888-8888-4888-8888-888888888888"));

    private static int Main()
    {
        try
        {
            ReviewCommitRestartAndUndo();
            StableTypedIdentitiesSurviveDraftAndCatalogRebind();
            CoreOwnedLegalitySlotsAndProfilePriceRemainAuthoritative();
            CoreOwnedWeaponMountCompositionRejectsHostileDrafts();
            AppliedMountReceiptCanCloseAndReopenFreshWorkshop();
            UncertainOutcomeNeverReplays();
            Console.WriteLine("SR5 Career vehicle workshop Android tests passed (6 hostile scenarios).");
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
        CharacterVehicleWorkshopCatalog catalog = Catalog();
        FakeAuthority authority = new();
        FakeSourceData source = new(catalog);
        FakeWorkspaceStore workspaces = new(CharacterXml(), 7);
        FakeCheckpointStore checkpoints = new();
        Sr5CareerVehicleWorkshopService service = new(authority, source, workspaces, checkpoints);
        Sr5CareerVehicleWorkshopSnapshot initial = service.Load(WorkspaceId);
        CharacterVehicleWorkshopModificationSelection modification = new(
            ModId, new CharacterVehicleModificationInstanceId(Guid.NewGuid()), 2);
        CharacterVehicleWeaponMountSelection mount = CompleteMount();
        Sr5CareerVehicleWorkshopSnapshot editing = service.UpdateSelection(
            WorkspaceId,
            initial.Selection with
            {
                CustomName = "Road Ghost",
                Modifications = [modification],
                WeaponMounts = [mount]
            });
        Assert(editing.Quote is { Exact: true, TotalCost: 17000m, SlotsUsed: 4 }
            && editing.Quote.Lines.Count(line => line.Kind == "weapon-mount-component") == 4,
            "Core vehicle and four-component weapon-mount quote is preserved by Android");
        Sr5CareerVehicleWorkshopSnapshot reviewed = service.Review(WorkspaceId);
        Assert(reviewed.CanConfirm, "review is separate from mutation");
        Guid stableVehicle = reviewed.Selection.NewVehicleInstanceId.Value;
        Guid stableModification = reviewed.Selection.Modifications.Single().InstanceId.Value;
        Guid stableMount = reviewed.Selection.WeaponMounts.Single().InstanceId.Value;
        Guid[] stableComponents = reviewed.Selection.WeaponMounts.Single().Components
            .Select(component => component.InstanceId.Value).ToArray();
        Assert(stableVehicle != Guid.Empty && stableModification != Guid.Empty
            && stableMount != Guid.Empty && stableComponents.Length == 4
            && stableComponents.Distinct().Count() == 4
            && new[] { stableVehicle, stableModification, stableMount }
                .Concat(stableComponents).Distinct().Count() == 7,
            "vehicle, modification, mount, and component typed stable identities are distinct");
        Assert(reviewed.Checkpoint!.Command!.Selection.WeaponMounts.Single().InstanceId.Value
            == stableMount, "review command binds the exact mount identity");

        Sr5CareerVehicleWorkshopSnapshot applied = service.Confirm(WorkspaceId);
        Assert(applied.HasAppliedReceipt && workspaces.Revision == 8 && authority.CommitCalls == 1,
            "one commit and one saved revision");
        Sr5CareerVehicleWorkshopService restarted = new(authority, source, workspaces, checkpoints);
        Sr5CareerVehicleWorkshopSnapshot recovered = restarted.Load(WorkspaceId);
        Assert(recovered.HasAppliedReceipt && authority.RecoverCalls > 0,
            "restart uses Core recovery rather than replay");
        Sr5CareerVehicleWorkshopSnapshot undone = restarted.Undo(WorkspaceId);
        Assert(undone.Notice == Sr5CareerVehicleWorkshopNotices.UndoApplied
            && workspaces.Revision == 9 && !workspaces.Xml.Contains("workshop-purchase", StringComparison.Ordinal),
            "receipt-bound undo creates one saved revision");
    }

    private static void StableTypedIdentitiesSurviveDraftAndCatalogRebind()
    {
        CharacterVehicleWorkshopCatalog catalog = Catalog();
        FakeAuthority authority = new();
        FakeSourceData source = new(catalog);
        FakeWorkspaceStore workspaces = new(CharacterXml(), 10);
        FakeCheckpointStore checkpoints = new();
        Sr5CareerVehicleWorkshopService service = new(authority, source, workspaces, checkpoints);
        CharacterVehicleWorkshopSelection baseSelection = service.Load(WorkspaceId).Selection;
        CharacterVehicleModificationInstanceId instance = new(Guid.NewGuid());
        CharacterVehicleWeaponMountSelection mount = CompleteMount();
        Sr5CareerVehicleWorkshopSnapshot saved = service.UpdateSelection(WorkspaceId,
            baseSelection with
            {
                ChassisSourceId = DroneId,
                Modifications = [new(ModId, instance, 1)],
                WeaponMounts = [mount]
            });
        Assert(saved.Selection.NewVehicleInstanceId == baseSelection.NewVehicleInstanceId
            && saved.Selection.Modifications.Single().InstanceId == instance
            && saved.Selection.WeaponMounts.Single().InstanceId == mount.InstanceId
            && saved.Selection.WeaponMounts.Single().Components
                .Select(component => component.InstanceId)
                .SequenceEqual(mount.Components.Select(component => component.InstanceId)),
            "draft preserves vehicle, modification, mount, and component typed identities");

        source.Catalog = Rehashed(catalog with
        {
            Modifications = [],
            WeaponMountComponents = catalog.WeaponMountComponents
                .Where(component => component.SourceId != ControlId).ToArray()
        });
        workspaces.ExternalReplace(CharacterXml().Replace("100000", "99999", StringComparison.Ordinal));
        Sr5CareerVehicleWorkshopSnapshot rebound = service.Load(WorkspaceId);
        Assert(rebound.Selection.NewVehicleInstanceId == baseSelection.NewVehicleInstanceId
            && rebound.Selection.Modifications.Count == 0
            && rebound.Selection.WeaponMounts.Single().InstanceId == mount.InstanceId
            && rebound.Selection.WeaponMounts.Single().Components.Count == 3
            && !rebound.CanConfirm,
            "catalog drift removes unavailable modification/component sources and invalidates review without changing stable parent identities");
    }

    private static void CoreOwnedLegalitySlotsAndProfilePriceRemainAuthoritative()
    {
        CharacterVehicleWorkshopCatalog catalog = Catalog();
        FakeAuthority authority = new();
        Sr5CareerVehicleWorkshopService service = new(
            authority, new FakeSourceData(catalog),
            new FakeWorkspaceStore(CharacterXml(), 4), new FakeCheckpointStore());
        Sr5CareerVehicleWorkshopSnapshot initial = service.Load(WorkspaceId);
        Sr5CareerVehicleWorkshopSnapshot quote = service.UpdateSelection(
            WorkspaceId,
            initial.Selection with
            {
                Modifications =
                [
                    new(ModId, new CharacterVehicleModificationInstanceId(Guid.NewGuid()), 3),
                    new(ModId, new CharacterVehicleModificationInstanceId(Guid.NewGuid()), 3)
                ]
            });
        Assert(quote.Quote is { Exact: false }
            && quote.Quote.Blockers.Contains(CharacterVehicleWorkshopBlockers.SlotsExceeded),
            "Android cannot bypass Core slot validation");
        ExpectInvalid(() => service.Review(WorkspaceId), "blocked quote cannot review");
        Assert(authority.CommitCalls == 0, "invalid composition never commits");

        CharacterVehicleWorkshopSelection forbidden = initial.Selection with { ChassisSourceId = DroneId };
        Sr5CareerVehicleWorkshopSnapshot adjusted = service.UpdateSelection(WorkspaceId, forbidden);
        Assert(adjusted.Quote is { Exact: true, TotalCost: 24000m }
            && adjusted.Preparation!.Binding.MultiplyForbiddenCost
            && adjusted.Preparation.Binding.ForbiddenCostMultiplier == 1.2m,
            "Core's saved-profile forbidden multiplier produces the adjusted quote");
    }

    private static void UncertainOutcomeNeverReplays()
    {
        FakeAuthority authority = new();
        FakeWorkspaceStore workspaces = new(CharacterXml(), 20) { AmbiguousWrite = true };
        FakeCheckpointStore checkpoints = new();
        Sr5CareerVehicleWorkshopService service = new(
            authority, new FakeSourceData(Catalog()), workspaces, checkpoints);
        _ = service.Review(WorkspaceId);
        Sr5CareerVehicleWorkshopSnapshot result = service.Confirm(WorkspaceId);
        Assert(result.IsRecoveryUnknown && authority.CommitCalls == 1,
            "ambiguous CAS becomes recovery lock after one mutation");
        Sr5CareerVehicleWorkshopSnapshot restarted = new Sr5CareerVehicleWorkshopService(
            authority, new FakeSourceData(Catalog()), workspaces, checkpoints).Load(WorkspaceId);
        Assert(restarted.CanConfirm && authority.CommitCalls == 1,
            "unchanged pre-CAS bytes recover as reviewed without replay");
    }

    private static void CoreOwnedWeaponMountCompositionRejectsHostileDrafts()
    {
        CharacterVehicleWorkshopCatalog catalog = Catalog();
        FakeAuthority authority = new();
        Sr5CareerVehicleWorkshopService service = new(
            authority, new FakeSourceData(catalog),
            new FakeWorkspaceStore(CharacterXml(), 30), new FakeCheckpointStore());
        CharacterVehicleWorkshopSelection initial = service.Load(WorkspaceId).Selection;
        CharacterVehicleWeaponMountSelection complete = CompleteMount();

        Sr5CareerVehicleWorkshopSnapshot incomplete = service.UpdateSelection(
            WorkspaceId,
            initial with
            {
                WeaponMounts =
                [
                    complete with
                    {
                        Components = complete.Components
                            .Where(component => component.SourceId != ControlId).ToArray()
                    }
                ]
            });
        Assert(incomplete.Quote is { Exact: false }
            && incomplete.Quote.Blockers.Contains(
                "A weapon mount requires one exact Size, Visibility, Flexibility, and Control component."),
            "Core blocks an incomplete four-kind mount");
        ExpectInvalid(() => service.Review(WorkspaceId),
            "an incomplete weapon mount cannot cross review");

        CharacterVehicleWeaponMountComponentSelection visibility = complete.Components.Single(
            component => component.SourceId == VisibilityId);
        CharacterVehicleWeaponMountComponentSelection control = complete.Components.Single(
            component => component.SourceId == ControlId);
        Sr5CareerVehicleWorkshopSnapshot forbidden = service.UpdateSelection(
            WorkspaceId,
            initial with
            {
                WeaponMounts =
                [
                    complete with
                    {
                        Components = complete.Components
                            .Where(component => component.SourceId != ControlId)
                            .Append(new CharacterVehicleWeaponMountComponentSelection(
                                ConflictControlId, control.InstanceId))
                            .ToArray()
                    }
                ]
            });
        Assert(forbidden.Quote is { Exact: false }
            && forbidden.Quote.Blockers.Contains(
                "The weapon-mount composition contains a forbidden component."),
            "Core blocks a source-declared component conflict");

        Sr5CareerVehicleWorkshopSnapshot duplicateIdentity = service.UpdateSelection(
            WorkspaceId,
            initial with
            {
                WeaponMounts =
                [
                    complete with
                    {
                        Components = complete.Components.Select(component =>
                            component.SourceId == ControlId
                                ? component with { InstanceId = visibility.InstanceId }
                                : component).ToArray()
                    }
                ]
            });
        Assert(duplicateIdentity.Quote is { Exact: false }
            && duplicateIdentity.Quote.Blockers.Contains(CharacterVehicleWorkshopBlockers.IdentityInvalid),
            "Core blocks repeated component instance identity");
        Assert(authority.CommitCalls == 0,
            "no hostile mount draft reaches mutation");
    }

    private static void AppliedMountReceiptCanCloseAndReopenFreshWorkshop()
    {
        CharacterVehicleWorkshopCatalog catalog = Catalog();
        FakeAuthority authority = new();
        FakeWorkspaceStore workspaces = new(CharacterXml(), 40);
        FakeCheckpointStore checkpoints = new();
        Sr5CareerVehicleWorkshopService service = new(
            authority, new FakeSourceData(catalog), workspaces, checkpoints);
        CharacterVehicleWorkshopSelection initial = service.Load(WorkspaceId).Selection;
        CharacterVehicleWeaponMountSelection mount = CompleteMount();
        Sr5CareerVehicleWorkshopSnapshot editing = service.UpdateSelection(
            WorkspaceId, initial with { WeaponMounts = [mount] });
        CharacterVehicleInstanceId purchasedVehicle = editing.Selection.NewVehicleInstanceId;
        _ = service.Review(WorkspaceId);
        Sr5CareerVehicleWorkshopSnapshot applied = service.Confirm(WorkspaceId);
        Assert(applied.HasAppliedReceipt && applied.Checkpoint!.Receipt!.UndoReady,
            "mount purchase produces one verified undo-ready receipt");

        Sr5CareerVehicleWorkshopService restarted = new(
            authority, new FakeSourceData(catalog), workspaces, checkpoints);
        Sr5CareerVehicleWorkshopSnapshot reopened = restarted.Reopen(WorkspaceId);
        Assert(reopened.Notice == Sr5CareerVehicleWorkshopNotices.Reopened
            && reopened.Checkpoint is { Phase: Sr5CareerVehicleWorkshopPhase.Editing }
            && reopened.Selection.NewVehicleInstanceId != purchasedVehicle
            && reopened.Selection.WeaponMounts.Count == 0
            && workspaces.Revision == 41,
            "closing the recovered receipt starts a fresh workshop without another character mutation");
    }

    private static CharacterVehicleWorkshopCatalog Catalog()
    {
        CharacterVehicleWorkshopSourceBinding binding = new(
            "SR5", "standard", CharacterVehicleWorkshopRules.SemanticsVersion,
            Digest("profile"), Digest("vehicles"), Digest("weapons"), Digest("gear"), Digest("overlay"),
            true, 1.1m, true, 1.2m, true);
        CharacterVehicleWorkshopAvailability legal = new(6, CharacterVehicleWorkshopLegality.Legal, false);
        CharacterVehicleWorkshopAvailability forbidden = new(12, CharacterVehicleWorkshopLegality.Forbidden, false);
        CharacterVehicleWorkshopCatalog unsigned = new(
            binding,
            [
                Chassis(VehicleId, CharacterVehicleChassisKind.Vehicle, "Roadmaster", 10000m, 4, legal),
                Chassis(DroneId, CharacterVehicleChassisKind.Drone, "Steel Lynx", 20000m, 4, forbidden)
            ],
            [new CharacterVehicleWorkshopModificationEntry(
                ModId, "Armor", "Protection", 1, 3, 1000m, 2000m, 0, 1, 0, 1,
                legal, "R5", "123", [], CharacterVehicleWorkshopProjectionStatus.Exact, string.Empty)],
            [
                MountComponent(SizeId, CharacterVehicleWeaponMountComponentKind.Size,
                    "Standard mount", 1000m, 2, 1, legal),
                MountComponent(VisibilityId, CharacterVehicleWeaponMountComponentKind.Visibility,
                    "Internal", 200m, 0, 0, legal),
                MountComponent(FlexibilityId, CharacterVehicleWeaponMountComponentKind.Flexibility,
                    "Fixed", 500m, 0, 0, legal, required: [ControlId]),
                MountComponent(ControlId, CharacterVehicleWeaponMountComponentKind.Control,
                    "Manual", 300m, 0, 0, legal),
                MountComponent(ConflictControlId, CharacterVehicleWeaponMountComponentKind.Control,
                    "Remote only", 600m, 0, 0, legal, forbidden: [VisibilityId])
            ],
            string.Empty);
        return Rehashed(unsigned);
    }

    private static CharacterVehicleWorkshopCatalog Rehashed(CharacterVehicleWorkshopCatalog catalog)
        => catalog with { DeclaredCatalogDigest = CharacterVehicleWorkshopRules.ComputeCatalogDigest(catalog) };

    private static CharacterVehicleWorkshopChassisEntry Chassis(
        CharacterVehicleChassisSourceId id,
        CharacterVehicleChassisKind kind,
        string name,
        decimal cost,
        int slots,
        CharacterVehicleWorkshopAvailability availability)
        => new(id, kind, CharacterVehicleChassisPosture.Stock, name, "Groundcraft",
            4, 3, 2, 2, 100, 80, 3, 10, 4, 8, 3, slots, slots,
            cost, availability, "R5", "100", string.Empty,
            CharacterVehicleWorkshopProjectionStatus.Exact, string.Empty, [], []);

    private static CharacterVehicleWeaponMountComponentEntry MountComponent(
        CharacterVehicleWeaponMountComponentSourceId id,
        CharacterVehicleWeaponMountComponentKind kind,
        string name,
        decimal cost,
        int slots,
        int capacity,
        CharacterVehicleWorkshopAvailability availability,
        IReadOnlyList<CharacterVehicleWeaponMountComponentSourceId>? required = null,
        IReadOnlyList<CharacterVehicleWeaponMountComponentSourceId>? forbidden = null)
        => new(id, kind, name, cost, slots, capacity, availability, "R5", "162", [],
            required ?? [], forbidden ?? [], CharacterVehicleWorkshopProjectionStatus.Exact,
            string.Empty);

    private static CharacterVehicleWeaponMountSelection CompleteMount()
        => new(
            new CharacterVehicleWeaponMountInstanceId(Guid.NewGuid()),
            [
                new(SizeId, new CharacterVehicleWeaponMountComponentInstanceId(Guid.NewGuid())),
                new(VisibilityId, new CharacterVehicleWeaponMountComponentInstanceId(Guid.NewGuid())),
                new(FlexibilityId, new CharacterVehicleWeaponMountComponentInstanceId(Guid.NewGuid())),
                new(ControlId, new CharacterVehicleWeaponMountComponentInstanceId(Guid.NewGuid()))
            ]);

    private static string CharacterXml()
        => "<character><created>True</created><nuyen>100000</nuyen><vehicles></vehicles><expenses></expenses></character>";

    private static string Digest(string value)
        => CharacterVehicleWorkshopRules.ComputeCharacterDigest(value);

    private static void ExpectInvalid(Action action, string name)
    {
        try { action(); throw new InvalidOperationException($"Assertion failed: {name}"); }
        catch (InvalidOperationException exception) when (!exception.Message.StartsWith("Assertion failed", StringComparison.Ordinal)) { }
    }

    private static void Assert(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException($"Assertion failed: {name}");
    }

    private sealed class FakeSourceData(CharacterVehicleWorkshopCatalog catalog)
        : ICharacterSourceDataResolver, ICharacterSourceDataContext
    {
        public CharacterVehicleWorkshopCatalog Catalog { get; set; } = catalog;
        public ICharacterSourceDataContext? TryCreateContext(string characterXml) => this;
        public bool TryResolveVehicleWorkshopCatalog(out CharacterVehicleWorkshopCatalog catalog)
        {
            catalog = Catalog;
            return true;
        }
    }

    private sealed class FakeCheckpointStore : ISr5CareerVehicleWorkshopCheckpointStore
    {
        public Sr5CareerVehicleWorkshopCheckpoint? Current { get; private set; }
        public Sr5CareerVehicleWorkshopCheckpoint? Read(CharacterWorkspaceId workspaceId) => Current;
        public void Write(Sr5CareerVehicleWorkshopCheckpoint checkpoint) => Current = checkpoint;
        public void Clear(CharacterWorkspaceId workspaceId) => Current = null;
    }

    private sealed class FakeWorkspaceStore(string xml, long revision)
        : ISr5CareerVehicleWorkshopWorkspaceStore
    {
        public string Xml { get; private set; } = xml;
        public long Revision { get; private set; } = revision;
        public bool AmbiguousWrite { get; set; }

        public Sr5CareerVehicleWorkshopWorkspaceSnapshot? Read(CharacterWorkspaceId workspaceId)
            => new(workspaceId, Revision, Revision, new WorkspaceDocument(Xml));

        public Sr5CareerVehicleWorkshopWorkspaceWriteResult ReplaceAndCheckpoint(
            Sr5CareerVehicleWorkshopWorkspaceSnapshot expected,
            string characterXml)
        {
            if (AmbiguousWrite)
                return new(false, false, Revision, Revision, "ambiguous");
            Xml = characterXml;
            Revision++;
            return new(true, false, Revision, Revision, string.Empty);
        }

        public void ExternalReplace(string characterXml)
        {
            Xml = characterXml;
            Revision++;
        }
    }

    private sealed class FakeAuthority : ICharacterVehicleWorkshopAuthority
    {
        public int CommitCalls { get; private set; }
        public int RecoverCalls { get; private set; }

        public CharacterVehicleWorkshopPreparation Prepare(
            string characterXml, long contentRevision, CharacterVehicleWorkshopCatalog catalog)
            => new(true, [], contentRevision,
                CharacterVehicleWorkshopRules.ComputeCharacterDigest(characterXml),
                decimal.Parse(characterXml.Split("<nuyen>")[1].Split("</nuyen>")[0]),
                catalog.Binding, catalog.DeclaredCatalogDigest, catalog.Chassis,
                catalog.Modifications, catalog.WeaponMountComponents, []);

        public CharacterVehicleWorkshopQuote Quote(
            CharacterVehicleWorkshopPreparation preparation,
            CharacterVehicleWorkshopSelection selection)
            => CharacterVehicleWorkshopRules.Quote(preparation, selection);

        public CharacterVehicleWorkshopCommitResult Commit(
            string characterXml, long revision, CharacterVehicleWorkshopCatalog catalog,
            CharacterVehicleWorkshopCommitCommand command)
        {
            CommitCalls++;
            CharacterVehicleWorkshopPreparation preparation = Prepare(characterXml, revision, catalog);
            CharacterVehicleWorkshopQuote quote = Quote(preparation, command.Selection);
            if (!quote.Exact)
                return Blocked(characterXml, revision, command, quote.Blockers.First());
            string output = characterXml.Replace("</vehicles>",
                $"<workshop-purchase key=\"{CharacterVehicleWorkshopRules.ComputeIdempotencyKeyDigest(command.IdempotencyKey)}\" command=\"{CharacterVehicleWorkshopRules.ComputeCommandDigest(command)}\"/></vehicles>",
                StringComparison.Ordinal).Replace("<nuyen>100000</nuyen>",
                $"<nuyen>{100000m + quote.NuyenDelta}</nuyen>", StringComparison.Ordinal);
            string outputDigest = CharacterVehicleWorkshopRules.ComputeCharacterDigest(output);
            CharacterVehicleWorkshopCommitReceipt unsigned = new(
                revision + 1, outputDigest, revision, preparation.CharacterDigest, preparation.AvailableNuyen,
                preparation.CatalogDigest, quote.QuoteDigest,
                CharacterVehicleWorkshopRules.ComputeIdempotencyKeyDigest(command.IdempotencyKey),
                CharacterVehicleWorkshopRules.ComputeCommandDigest(command),
                command.Selection.NewVehicleInstanceId, command.NewExpenseId, quote.NuyenDelta,
                Digest("vehicle"), Digest("expense"), true, string.Empty);
            CharacterVehicleWorkshopCommitReceipt receipt = unsigned with
            {
                ReceiptDigest = CharacterVehicleWorkshopRules.ComputeReceiptDigest(unsigned)
            };
            return new(CharacterVehicleWorkshopCommitStatus.Committed, string.Empty,
                revision, revision + 1, preparation.CharacterDigest, outputDigest, output,
                command.Selection.NewVehicleInstanceId, command.NewExpenseId, quote.NuyenDelta, receipt);
        }

        public CharacterVehicleWorkshopCommitResult Recover(
            string characterXml, long revision, CharacterVehicleWorkshopCatalog catalog,
            CharacterVehicleWorkshopCommitCommand command)
        {
            RecoverCalls++;
            string key = CharacterVehicleWorkshopRules.ComputeIdempotencyKeyDigest(command.IdempotencyKey);
            if (!characterXml.Contains($"key=\"{key}\"", StringComparison.Ordinal))
                return Blocked(characterXml, revision, command, "not found");
            CharacterVehicleWorkshopPreparation current = Prepare(characterXml, revision, catalog);
            CharacterVehicleWorkshopQuote quote = Quote(current with
            {
                ContentRevision = command.ExpectedContentRevision,
                CharacterDigest = command.ExpectedCharacterDigest,
                AvailableNuyen = 100000m
            }, command.Selection);
            CharacterVehicleWorkshopCommitReceipt unsigned = new(
                revision, current.CharacterDigest, command.ExpectedContentRevision,
                command.ExpectedCharacterDigest, current.AvailableNuyen - quote.NuyenDelta,
                current.CatalogDigest, command.ExpectedQuoteDigest,
                key, CharacterVehicleWorkshopRules.ComputeCommandDigest(command),
                command.Selection.NewVehicleInstanceId, command.NewExpenseId, quote.NuyenDelta,
                Digest("vehicle"), Digest("expense"), true, string.Empty);
            CharacterVehicleWorkshopCommitReceipt receipt = unsigned with
            {
                ReceiptDigest = CharacterVehicleWorkshopRules.ComputeReceiptDigest(unsigned)
            };
            return new(CharacterVehicleWorkshopCommitStatus.Recovered, string.Empty,
                command.ExpectedContentRevision, revision, command.ExpectedCharacterDigest,
                current.CharacterDigest, characterXml, command.Selection.NewVehicleInstanceId,
                command.NewExpenseId, quote.NuyenDelta, receipt);
        }

        public CharacterVehicleWorkshopCommitResult Undo(
            string characterXml, long revision, CharacterVehicleWorkshopCatalog catalog,
            CharacterVehicleWorkshopUndoCommand command)
        {
            CharacterVehicleWorkshopCommitReceipt receipt = command.Receipt!;
            string output = characterXml.Replace(
                characterXml[characterXml.IndexOf("<workshop-purchase", StringComparison.Ordinal)..
                    (characterXml.IndexOf("/>", characterXml.IndexOf("<workshop-purchase", StringComparison.Ordinal), StringComparison.Ordinal) + 2)],
                string.Empty, StringComparison.Ordinal);
            return new(CharacterVehicleWorkshopCommitStatus.Undone, string.Empty,
                revision, revision + 1,
                CharacterVehicleWorkshopRules.ComputeCharacterDigest(characterXml),
                CharacterVehicleWorkshopRules.ComputeCharacterDigest(output), output,
                receipt.VehicleInstanceId, receipt.ExpenseId, -receipt.NuyenDelta, null);
        }

        private static CharacterVehicleWorkshopCommitResult Blocked(
            string xml, long revision, CharacterVehicleWorkshopCommitCommand command, string reason)
        {
            string digest = CharacterVehicleWorkshopRules.ComputeCharacterDigest(xml);
            return new(CharacterVehicleWorkshopCommitStatus.Blocked, reason, revision, revision,
                digest, digest, xml, command.Selection.NewVehicleInstanceId,
                command.NewExpenseId, 0m, null);
        }
    }
}
