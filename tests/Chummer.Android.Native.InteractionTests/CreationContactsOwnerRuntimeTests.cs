using System.Text.Json;
using System.Reflection;
using Microsoft.Maui.Controls;
using Chummer.Android.Native;
using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Workspaces;
using Chummer.Presentation;
using Chummer.Presentation.Overview;

internal static partial class AfterRunAuthorityHarness
{
    private static readonly OwnerScope ContactsOwnerA = new("contacts-native-a");
    private static readonly OwnerScope ContactsOwnerB = new("contacts-native-b");
    private static readonly Guid CreationContactId = Guid.Parse("b7ff1656-c972-47f5-b6f1-2f3c31320b06");
    private const string ContactsCreationXml = """
        <character><name>Contacts owner fixture</name><gameedition>SR5</gameedition>
        <settings>default.xml</settings><metatype>Human</metatype><buildmethod>Priority</buildmethod>
        <createdversion>5.225.0</createdversion><appversion>5.225.0</appversion><karma>0</karma><nuyen>0</nuyen>
        <created>False</created><contactpoints>15</contactpoints><improvements/>
        <contacts><contact><guid>b7ff1656-c972-47f5-b6f1-2f3c31320b06</guid>
        <name>Fixer</name><role>Broker</role><connection>3</connection><loyalty>2</loyalty>
        <group>False</group><free>False</free><family>False</family><blackmail>False</blackmail>
        <type>Contact</type></contact></contacts><notes>Preserve this</notes></character>
        """;

    public static async Task RunCreationContactsOwnerCasesAsync(string contentRoot)
    {
        foreach (string scenario in new[]
        {
            "local", "linked", "stale-display", "aba", "foreign", "missing",
            "during-confirm", "ambiguous-receipt", "committed-owner-change", "committed-cancellation", "refresh-error", "lookup-error"
        })
        {
            var owners = new ControlledLinkedOwner();
            owners.Set(scenario == "local" ? OwnerScope.LocalSingleUser : ContactsOwnerA);
            ContactsBoundaryProbe? probe = null;
            await using var runtime = new NativeRewardRuntime(contentRoot, linkedOwners: owners,
                creationContacts: true, contactsDecorator: service => probe = new(service));
            var store = new FileWorkspaceStore(runtime.StateDirectory);
            var imported = await runtime.Client.ImportAsync(new WorkspaceImportDocument(ContactsCreationXml, "sr5"), default);
            runtime.Id = imported.Id;
            var originalOwner = owners.Current;
            var document = (originalOwner.IsLocalSingleUser
                ? store.Get(runtime.Id) : store.Get(originalOwner, runtime.Id)).Value!.Document;
            var validation = await runtime.Client.ValidateAsync(runtime.Id, default);
            Require(validation.IsValid, "Canonical fixture import rejected: " + JsonSerializer.Serialize(validation.Issues));
            foreach (OwnerScope owner in new[] { OwnerScope.LocalSingleUser, ContactsOwnerA, ContactsOwnerB })
            {
                if (owner == originalOwner) continue;
                Require((owner.IsLocalSingleUser
                    ? store.CreateWorkspaceDocument(runtime.Id, document)
                    : store.CreateWorkspaceDocument(owner, runtime.Id, document)).Success,
                    "Real owner-partition fixture creation failed.");
            }
            await runtime.Presenter.LoadAsync(runtime.Id, default);
            CharacterOverviewState original = runtime.Coordinator.State;
            var state = runtime.Coordinator.LoadCreationContacts().State;
            Require(state is not null && CreationContactsPhoneAuthority.IsReady(state, original),
                $"Actual native Contacts is not ready for {scenario}: {original.Error}; "
                + string.Join(",", runtime.Coordinator.LoadCreationContacts().Blockers));
            OwnerContextStamp stamp = owners.Capture();
            Require(state!.DisplayOwnerContext == stamp && original.DisplayOwnerContext == stamp,
                "Core read and native display lost original owner provenance.");
            var contact = CreationContactsPhoneAuthority.ResolveUniqueContact(state, CreationContactId)!;
            var draft = new CreationContactsPhoneDraft();
            draft.Bind(state, contact);
            Require(draft.TrySetInteger(state, contact, CharacterCreationContactFieldIds.Loyalty, 3),
                "The exact native contact draft rejected a legal loyalty selection.");
            var input = draft.ToInput(state, contact);
            Require(input is not null && input.DisplayOwnerContext == stamp, "Native input lost its original stamp.");
            var prepare = runtime.Coordinator.PrepareCreationContact(input!);
            var prepared = prepare.PreparedPreview;
            Require(prepared is not null && prepared.DisplayOwnerContext == stamp,
                "Real native/Core preview failed: " + string.Join(",", prepare.Blockers));
            var missingCompanion = new CharacterCreationContactsInteractionPresenter(new CharacterCreationContactsService(store));
            Require(missingCompanion.Load(original).State is null,
                "A stamped native display fell back to the trusted-local service when the companion was absent.");
            Require(!JsonSerializer.Serialize(prepared).Contains(stamp.AuthorityInstanceId, StringComparison.Ordinal)
                && !JsonSerializer.Serialize(input).Contains("DisplayOwnerContext", StringComparison.Ordinal),
                "Transient owner provenance leaked into serialized payloads.");
            var before = SnapshotContactsPartitions(store, runtime.Id);
            using var cancel = new CancellationTokenSource();

            if (scenario == "stale-display")
            {
                owners.Set(ContactsOwnerB);
                await runtime.Presenter.LoadAsync(runtime.Id, default);
                var b = runtime.Coordinator.LoadCreationContacts().State!;
                Require(!draft.Matches(b, b.Contacts[0]), "A draft was retained for identical B bytes.");
                Require(runtime.Coordinator.PrepareCreationContact(input!).PreparedPreview is null,
                    "A's old gesture was accepted against B's identical display.");
            }
            else if (scenario == "aba")
            {
                owners.Set(ContactsOwnerB);
                owners.Set(ContactsOwnerA);
            }
            else if (scenario == "foreign")
            {
                var foreignOwners = new ControlledLinkedOwner();
                foreignOwners.Set(stamp.Owner);
                prepared = prepared! with { DisplayOwnerContext = foreignOwners.Capture() };
            }
            else if (scenario == "missing")
                prepared = prepared! with { DisplayOwnerContext = null };
            else if (scenario == "during-confirm")
                probe!.BeforeConfirm = () => { owners.Set(ContactsOwnerB); owners.Set(ContactsOwnerA); };
            else if (scenario == "ambiguous-receipt")
                probe!.AfterConfirm = () => throw new IOException("Synthetic post-commit observation failure");
            else if (scenario == "committed-owner-change")
                probe!.AfterConfirm = () => owners.Set(ContactsOwnerB);
            else if (scenario == "committed-cancellation")
                probe!.AfterConfirm = cancel.Cancel;
            else if (scenario == "refresh-error")
                probe!.AfterConfirm = () => probe.ThrowLoads = true;
            else if (scenario == "lookup-error")
                probe!.AfterConfirm = () => { probe.ThrowLoads = true; probe.ThrowLookup = true; };

            var confirmed = await runtime.Coordinator.ConfirmCreationContactAsync(prepared!, cancel.Token);
            Console.WriteLine("CONTACTS_DIAGNOSTIC " + JsonSerializer.Serialize(new
            {
                scenario, confirmed.Outcome, confirmed.Blockers,
                receipt = confirmed.Receipt?.ContentRevision,
                refreshed = confirmed.RefreshedState?.Binding.ContentRevision,
                overviewRevision = runtime.Coordinator.State.ContentRevision,
                ownerMatches = runtime.Coordinator.State.DisplayOwnerContext == stamp,
                runtime.Coordinator.State.Error, runtime.Coordinator.State.IsBusy
            }));
            bool committed = scenario is "local" or "linked" or "ambiguous-receipt"
                or "committed-owner-change" or "committed-cancellation" or "refresh-error" or "lookup-error";
            var after = SnapshotContactsPartitions(new FileWorkspaceStore(runtime.StateDirectory), runtime.Id);
            foreach (var entry in before)
            {
                bool changed = committed && entry.Key == stamp.Owner;
                Require(after[entry.Key].ContentRevision == entry.Value.ContentRevision + (changed ? 1 : 0),
                    $"{scenario}: wrong account revision changed ({entry.Key.Value}).");
                if (!changed) RequireSameRewardDocument(entry.Value, after[entry.Key]);
                else Require(after[entry.Key].SavedRevision == after[entry.Key].ContentRevision
                    && after[entry.Key].Document.Content.Contains("<loyalty>3</loyalty>", StringComparison.Ordinal),
                    "The real contact mutation was not atomically saved.");
            }
            if (committed)
            {
                Require(confirmed.Receipt is not null
                    && CreationContactsPhoneAuthority.ReceiptMatches(prepared!, confirmed.Receipt),
                    scenario + ": durable commit receipt was lost during refresh/recovery.");
                var page = new CreationContactPreviewPage(runtime.Coordinator, prepared!);
                typeof(CreationContactPreviewPage).GetField("_confirmation", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .SetValue(page, confirmed);
                typeof(CreationContactPreviewPage).GetMethod("Refresh", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(page, null);
                var body = (VerticalStackLayout)((ScrollView)page.Content!).Content!;
                Require(body.Children.OfType<Border>().Any(card => card.AutomationId == "creation-contact-confirm-receipt")
                    && !body.Children.OfType<Button>().Any(button => button.AutomationId == "creation-contact-confirm"),
                    "Actual native preview hid a durable receipt or offered to submit it again.");
                if (confirmed.RefreshedState is null)
                    Require(body.Children.OfType<Border>().Any(card => card.Content is Label
                        { AutomationId: "creation-contact-committed-reload-required" }),
                        "Actual native page did not explain committed-but-reload-required state.");
                if (scenario is "local" or "linked" or "ambiguous-receipt")
                    Require(confirmed.RefreshedState is not null
                        && runtime.Coordinator.State.DisplayOwnerContext == stamp
                        && runtime.Coordinator.State.ContentRevision == 2,
                        scenario + ": successful owner-bound refresh did not preserve functional navigation.");
                else Require(confirmed.RefreshedState is null, "Stale/cancelled refresh was presented as current.");
                if (scenario == "ambiguous-receipt")
                    Require(confirmed.RecoveredByReceiptLookup && probe!.ConfirmCalls == 1,
                        "Ambiguous recovery must read the retained key, never replay the mutation.");
                // Actual reconstructed FileWorkspaceStore + fresh issuer can recover a historical
                // receipt with NEW current authority. The old transient issuer is not durable truth.
                var restartedOwners = new ControlledLinkedOwner();
                restartedOwners.Set(stamp.Owner);
                var recovered = new OwnerBoundCharacterCreationContactsService(
                    new CharacterCreationContactsService(new FileWorkspaceStore(runtime.StateDirectory)), restartedOwners)
                    .LookupReceipt(restartedOwners.Capture(), new(runtime.Id, prepared!.IdempotencyKey));
                Require(recovered.Value?.ReceiptDigest == confirmed.Receipt!.ReceiptDigest,
                    "Fresh process composition lost exact historical receipt recovery.");
            }
            else Require(confirmed.Receipt is null && confirmed.RefreshedState is null,
                scenario + ": a rejected old gesture acquired another account's receipt.");
            Require(owners.ActiveLeases == 0 && probe!.MutationStamps.All(value => value == stamp),
                "Native pipeline replaced original provenance or leaked an owner lease.");
            Console.WriteLine("PASS real native Contacts owner boundary: " + scenario);
        }
    }

    private static Dictionary<OwnerScope, WorkspaceStoredDocument> SnapshotContactsPartitions(
        FileWorkspaceStore store, CharacterWorkspaceId id)
        => new[] { OwnerScope.LocalSingleUser, ContactsOwnerA, ContactsOwnerB }.ToDictionary(owner => owner,
            owner => (owner.IsLocalSingleUser ? store.Get(id) : store.Get(owner, id)).Value
                ?? throw new InvalidOperationException("Contacts fixture partition is missing."));

    private sealed class ContactsBoundaryProbe(IOwnerBoundCharacterCreationContactsService inner)
        : IOwnerBoundCharacterCreationContactsService
    {
        public readonly List<OwnerContextStamp> Stamps = [];
        public readonly List<OwnerContextStamp> MutationStamps = [];
        public Action? BeforeConfirm;
        public Action? AfterConfirm;
        public int ConfirmCalls;
        public bool ThrowLoads;
        public bool ThrowLookup;
        public CharacterCreationContactResult<CharacterCreationContactsState> Load(
            OwnerContextStamp owner, CharacterCreationContactsLoadRequest request)
        {
            Stamps.Add(owner);
            if (ThrowLoads) throw new IOException("Synthetic post-commit refresh outage");
            return inner.Load(owner, request);
        }
        public CharacterCreationContactResult<CharacterCreationContactPreview> Preview(
            OwnerContextStamp owner, CharacterCreationContactPreviewRequest request)
        { Stamps.Add(owner); return inner.Preview(owner, request); }
        public CharacterCreationContactResult<CharacterCreationContactReceipt> Confirm(
            OwnerContextStamp owner, CharacterCreationContactConfirmRequest request)
        {
            Stamps.Add(owner); MutationStamps.Add(owner); ConfirmCalls++; BeforeConfirm?.Invoke();
            var result = inner.Confirm(owner, request);
            if (result.Value is not null) AfterConfirm?.Invoke();
            return result;
        }
        public CharacterCreationContactResult<CharacterCreationContactReceipt> LookupReceipt(
            OwnerContextStamp owner, CharacterCreationContactReceiptLookupRequest request)
        {
            Stamps.Add(owner); MutationStamps.Add(owner);
            if (ThrowLookup) throw new IOException("Synthetic receipt observation outage");
            return inner.LookupReceipt(owner, request);
        }
    }
}
