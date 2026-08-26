using System.Text.Json;
using System.Xml.Linq;
using Chummer.Android.Native;
using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Android.Sr5CareerSkillGroupAtomicWorkspace.Tests;

internal static class Program
{
    private static readonly CharacterWorkspaceId WorkspaceId = new("runner-atomic-17");
    private static readonly CharacterCareerSkillGroupIdentity GroupIdentity = new(
        Guid.Parse("11111111-1111-1111-1111-111111111111"));
    private static readonly Guid SkillOneId =
        Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid SkillOneSourceId =
        Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid SkillTwoId =
        Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid SkillTwoSourceId =
        Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid TransactionId =
        Guid.Parse("66666666-6666-6666-6666-666666666666");
    private const long InitialRevision = 17;

    public static int Main()
    {
        Atomic_apply_persists_mutation_receipt_and_exact_digests();
        Restart_replays_before_stale_revision_and_collision_fails_closed();
        Unknown_store_outcome_recovers_persisted_receipt();
        Stale_revision_and_corrupt_ledger_never_mutate();
        Missing_settings_authority_fails_closed();
        Console.WriteLine("SR5 Career skill-group atomic workspace tests passed.");
        return 0;
    }

    private static void Atomic_apply_persists_mutation_receipt_and_exact_digests()
    {
        TestWorkspaceStore store = Store();
        CharacterCareerSkillGroupAdvanceService service = Service(store);
        CharacterCareerSkillGroupAdvanceCommand command = Command(service);

        CharacterCareerSkillGroupAdvanceResult result = service.Advance(command);

        Check(result.Outcome == CharacterCareerSkillGroupAdvanceServiceOutcome.Applied,
            "The first exact command must apply.");
        Check(result.CurrentWorkspaceRevision == InitialRevision + 1,
            "The atomic commit must advance exactly one workspace revision.");
        Check(store.AtomicReplaceCount == 1,
            "Exactly one atomic workspace replacement is allowed.");
        Check(store.Current.SavedRevision == store.Current.ContentRevision,
            "The committed runner must be checkpointed in the same store operation.");

        XDocument saved = XDocument.Parse(store.Current.Document.Content);
        XElement root = saved.Root!;
        Check(root.Element("karma")!.Value == result.Receipt!.CharacterKarmaAfter.ToString(),
            "Saved character Karma must match the Core receipt.");
        XElement group = root.Element("newskills")!.Element("groups")!.Element("group")!;
        Check(group.Element("karma")!.Value == result.Receipt.GroupKarmaAfter.ToString(),
            "Saved group Karma must match the Core receipt.");
        Check(root.Element("expenses")!.Elements("expense").Single()
                .Element("guid")!.Value == TransactionId.ToString("D"),
            "The claimed transaction must be the unique expense identity.");

        XElement entry = root.Element("androidcareerskillgroupadvanceledger")!
            .Elements("entry").Single();
        Check(entry.Element("commanddigest")!.Value == result.CommandDigest,
            "The exact command digest must be durable.");
        Check(CharacterCareerSkillGroupAdvanceServiceIntegrity.IsCanonicalDigest(
                entry.Element("bindingdigest")!.Value),
            "The exact binding digest must be durable.");
        Check(CharacterCareerSkillGroupAdvanceServiceIntegrity.IsCanonicalDigest(
                entry.Element("appliedresultdigest")!.Value),
            "The exact applied-result digest must be durable.");
        Check(CharacterCareerSkillGroupAdvanceRules.IsCoherent(result.Receipt),
            "The returned receipt must remain Core-coherent.");
    }

    private static void Restart_replays_before_stale_revision_and_collision_fails_closed()
    {
        TestWorkspaceStore store = Store();
        CharacterCareerSkillGroupAdvanceService firstProcess = Service(store);
        CharacterCareerSkillGroupAdvanceCommand command = Command(firstProcess);
        CharacterCareerSkillGroupAdvanceResult applied = firstProcess.Advance(command);
        Check(applied.Outcome == CharacterCareerSkillGroupAdvanceServiceOutcome.Applied,
            "Precondition: first process applies.");

        CharacterCareerSkillGroupAdvanceService restarted = Service(store);
        CharacterCareerSkillGroupAdvanceResult replay = restarted.Advance(command);
        Check(replay.Outcome == CharacterCareerSkillGroupAdvanceServiceOutcome.Replayed,
            "A restarted process must recover before checking the stale expected revision.");
        Check(replay.Receipt == applied.Receipt,
            "Restart replay must return the exact durable receipt.");
        Check(store.AtomicReplaceCount == 1,
            "Replay must not perform a second XML mutation.");

        CharacterCareerSkillGroupAdvanceResult collision = restarted.Advance(command with
        {
            ExpenseDateLocal = command.ExpenseDateLocal.AddMinutes(1)
        });
        Check(collision.Outcome
                == CharacterCareerSkillGroupAdvanceServiceOutcome.IdempotencyConflict,
            "The same transaction id with a different command must fail closed.");
        Check(store.AtomicReplaceCount == 1,
            "An idempotency collision must not mutate the runner.");
    }

    private static void Unknown_store_outcome_recovers_persisted_receipt()
    {
        TestWorkspaceStore store = Store();
        store.ReturnUnavailableAfterNextDurableCommit = true;
        CharacterCareerSkillGroupAdvanceService service = Service(store);
        CharacterCareerSkillGroupAdvanceCommand command = Command(service);

        CharacterCareerSkillGroupAdvanceResult result = service.Advance(command);

        Check(result.Outcome == CharacterCareerSkillGroupAdvanceServiceOutcome.Replayed,
            "A durable write with an unavailable response must recover by receipt lookup.");
        Check(CharacterCareerSkillGroupAdvanceRules.IsCoherent(result.Receipt),
            "Unknown-outcome recovery must return the durable Core receipt.");
        Check(store.AtomicReplaceCount == 1,
            "Unknown-outcome recovery must never issue a second mutation.");
    }

    private static void Stale_revision_and_corrupt_ledger_never_mutate()
    {
        TestWorkspaceStore staleStore = Store();
        CharacterCareerSkillGroupAdvanceService staleService = Service(staleStore);
        CharacterCareerSkillGroupAdvanceCommand staleCommand = Command(staleService);
        staleStore.ReplaceExternally(staleStore.Current.Document);
        CharacterCareerSkillGroupAdvanceResult stale = staleService.Advance(staleCommand);
        Check(stale.Outcome == CharacterCareerSkillGroupAdvanceServiceOutcome.Conflict,
            "A stale expected content revision must conflict.");
        Check(staleStore.AtomicReplaceCount == 0,
            "A stale command must not enter the atomic mutation seam.");

        TestWorkspaceStore corruptStore = Store();
        CharacterCareerSkillGroupAdvanceService corruptService = Service(corruptStore);
        CharacterCareerSkillGroupAdvanceCommand command = Command(corruptService);
        Check(corruptService.Advance(command).Outcome
                == CharacterCareerSkillGroupAdvanceServiceOutcome.Applied,
            "Precondition: receipt exists before corruption.");
        XDocument corrupt = XDocument.Parse(corruptStore.Current.Document.Content);
        corrupt.Root!.Element("androidcareerskillgroupadvanceledger")!
            .Element("entry")!.Element("bindingdigest")!.Value = new string('0', 64);
        corruptStore.ReplaceExternally(new WorkspaceDocument(
            corruptStore.Current.Document.State with
            {
                Payload = corrupt.ToString(SaveOptions.DisableFormatting)
            },
            corruptStore.Current.Document.Format));

        CharacterCareerSkillGroupAdvanceResult rejected = corruptService.Advance(command);
        Check(rejected.Outcome == CharacterCareerSkillGroupAdvanceServiceOutcome.Corrupt,
            "A forged durable binding digest must reject the entire replay.");
        Check(corruptStore.AtomicReplaceCount == 1,
            "Corrupt replay state must not trigger another mutation.");
    }

    private static void Missing_settings_authority_fails_closed()
    {
        TestWorkspaceStore store = Store();
        var adapter = new AndroidCharacterCareerSkillGroupAdvanceWorkspace(
            store,
            new SkillSourceResolver(),
            new SettingsCatalog(string.Empty));
        var service = new CharacterCareerSkillGroupAdvanceService(adapter);

        CharacterCareerSkillGroupQuoteResult result = service.Quote(
            new CharacterCareerSkillGroupQuoteRequest(WorkspaceId, GroupIdentity));

        Check(result.Outcome == CharacterCareerSkillGroupAdvanceServiceOutcome.Unavailable,
            "Missing exact settings authority must remain unavailable.");
        Check(store.AtomicReplaceCount == 0,
            "Missing authority must never fall back to XML mutation.");
    }

    private static CharacterCareerSkillGroupAdvanceService Service(TestWorkspaceStore store)
        => new(new AndroidCharacterCareerSkillGroupAdvanceWorkspace(
            store,
            new SkillSourceResolver(),
            new SettingsCatalog(SettingsCatalogJson())));

    private static CharacterCareerSkillGroupAdvanceCommand Command(
        CharacterCareerSkillGroupAdvanceService service)
    {
        CharacterCareerSkillGroupQuoteResult quoted = service.Quote(
            new CharacterCareerSkillGroupQuoteRequest(WorkspaceId, GroupIdentity));
        Check(quoted.Outcome == CharacterCareerSkillGroupAdvanceServiceOutcome.Available,
            "The exact fixture must quote as available.");
        CharacterCareerSkillGroupQuoteBinding binding = quoted.Binding!;
        return new CharacterCareerSkillGroupAdvanceCommand(
            CharacterCareerSkillGroupAdvanceServiceSchemas.CommandV1,
            WorkspaceId,
            binding.WorkspaceRevision,
            GroupIdentity,
            binding.Quote.LogicalRevision,
            binding.Quote.SourceRevision,
            binding.Quote.RuleDigest,
            binding.BindingDigest,
            TransactionId,
            new DateTime(2081, 5, 12, 14, 30, 0, DateTimeKind.Unspecified),
            ExplicitlyConfirmed: true);
    }

    private static TestWorkspaceStore Store()
        => new(new WorkspaceStoredDocument(
            WorkspaceId,
            new WorkspaceDocument(CharacterXml(), CharacterCareerSkillGroupAdvanceRules.RulesetId),
            InitialRevision,
            InitialRevision,
            DateTimeOffset.UtcNow));

    private static string CharacterXml()
        => $"""
           <character>
             <created>True</created>
             <settings>profile-sr5</settings>
             <karma>40</karma>
             <newskills>
               <skills>
                 <skill>
                   <guid>{SkillOneId:D}</guid>
                   <suid>{SkillOneSourceId:D}</suid>
                   <isknowledge>False</isknowledge>
                   <skillcategory>Combat Active</skillcategory>
                   <base>0</base>
                   <karma>0</karma>
                 </skill>
                 <skill>
                   <guid>{SkillTwoId:D}</guid>
                   <suid>{SkillTwoSourceId:D}</suid>
                   <isknowledge>False</isknowledge>
                   <skillcategory>Combat Active</skillcategory>
                   <base>0</base>
                   <karma>0</karma>
                 </skill>
               </skills>
               <groups>
                 <group>
                   <id>{GroupIdentity.InternalId:D}</id>
                   <name>Firearms</name>
                   <base>0</base>
                   <karma>1</karma>
                   <isbroken>False</isbroken>
                 </group>
               </groups>
             </newskills>
             <improvements />
           </character>
           """;

    private static string SettingsCatalogJson()
        => JsonSerializer.Serialize(new
        {
            ActiveProfileId = "profile-sr5",
            Profiles = new[]
            {
                new
                {
                    Id = "profile-sr5",
                    Name = "SR5 exact",
                    Xml = "<settings><karmacost><karmanewskillgroup>5</karmanewskillgroup><karmaimproveskillgroup>5</karmaimproveskillgroup></karmacost><maxskillrating>12</maxskillrating><usepointsonbrokengroups>False</usepointsonbrokengroups></settings>"
                }
            }
        });

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class SettingsCatalog(string json) :
        IAndroidCareerSkillGroupSettingsCatalog
    {
        public string ReadCatalogJson() => json;
    }

    private sealed class SkillSourceResolver :
        ICharacterSourceDataResolver,
        ICharacterSourceDataContext
    {
        public ICharacterSourceDataContext? TryCreateContext(string characterXml)
            => string.IsNullOrWhiteSpace(characterXml) ? null : this;

        public bool TryResolveActiveSkillSource(
            string sourceSkillId,
            out CharacterActiveSkillSource source)
        {
            if (!Guid.TryParse(sourceSkillId, out Guid id)
                || id != SkillOneSourceId && id != SkillTwoSourceId)
            {
                source = CharacterActiveSkillSource.Unavailable;
                return false;
            }
            string name = id == SkillOneSourceId ? "Automatics" : "Longarms";
            source = new CharacterActiveSkillSource(
                id.ToString("D"),
                name,
                "Combat Active",
                "Firearms",
                "AGI",
                IsExotic: false,
                RequiresGroundMovement: false,
                RequiresSwimMovement: false,
                RequiresFlyMovement: false,
                $"<skill><id>{id:D}</id><name>{name}</name><skillgroup>Firearms</skillgroup></skill>");
            return true;
        }

    }

    private sealed class TestWorkspaceStore(WorkspaceStoredDocument initial) : IWorkspaceStore
    {
        private WorkspaceStoredDocument _current = initial;

        public WorkspaceStoredDocument Current => _current;
        public int AtomicReplaceCount { get; private set; }
        public bool ReturnUnavailableAfterNextDurableCommit { get; set; }

        public WorkspaceStoreReadResult Get(CharacterWorkspaceId id)
            => id == _current.Id
                ? new WorkspaceStoreReadResult(WorkspaceOperationOutcome.Success, _current)
                : new WorkspaceStoreReadResult(WorkspaceOperationOutcome.Missing);

        public WorkspaceStoreMutationResult ReplaceWorkspaceDocumentAndCheckpoint(
            CharacterWorkspaceId id,
            long expectedContentRevision,
            WorkspaceDocument document)
        {
            if (id != _current.Id)
            {
                return new(WorkspaceOperationOutcome.Missing);
            }
            if (_current.ContentRevision != expectedContentRevision)
            {
                return new(
                    WorkspaceOperationOutcome.Conflict,
                    Entry(),
                    "stale");
            }
            AtomicReplaceCount++;
            long revision = checked(_current.ContentRevision + 1);
            _current = new WorkspaceStoredDocument(
                id,
                document,
                revision,
                revision,
                DateTimeOffset.UtcNow);
            if (ReturnUnavailableAfterNextDurableCommit)
            {
                ReturnUnavailableAfterNextDurableCommit = false;
                return new(
                    WorkspaceOperationOutcome.Unavailable,
                    Error: "simulated_unknown_outcome");
            }
            return new(WorkspaceOperationOutcome.Success, Entry());
        }

        public void ReplaceExternally(WorkspaceDocument document)
        {
            long revision = checked(_current.ContentRevision + 1);
            _current = new WorkspaceStoredDocument(
                _current.Id,
                document,
                revision,
                revision,
                DateTimeOffset.UtcNow);
        }

        private WorkspaceStoreEntry Entry()
            => new(
                _current.Id,
                _current.LastUpdatedUtc,
                _current.ContentRevision,
                _current.SavedRevision);

    }
}
