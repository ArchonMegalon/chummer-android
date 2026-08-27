using System.Globalization;
using Chummer.Contracts.Characters;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

/// <summary>Dedicated phone list for the authoritative Contacts/Lifestyles creation stage.</summary>
public sealed class CreationContactsPage : NativePageBase
{
    private readonly VerticalStackLayout _body = new()
    {
        Padding = new Thickness(20, 18, 20, 40),
        Spacing = 14
    };

    public CreationContactsPage(RunnerSessionCoordinator coordinator) : base(coordinator)
    {
        Title = CreationFlowStrings.Get("Contacts.PageTitle", "Creation contacts");
        AutomationId = "creation-contacts-page";
        Content = new ScrollView { Content = _body };
    }

    protected override void Refresh()
    {
        _body.Clear();
        _body.Add(NativeTheme.Eyebrow(CreationFlowStrings.Get("Common.CharacterCreation", "Character creation")));
        _body.Add(NativeTheme.Title(CreationFlowStrings.Get("Contacts.Heading", "Contacts")));
        _body.Add(NativeTheme.Body(
            CreationFlowStrings.Get(
                "Contacts.Intro",
                "Choose an existing Contact and preview one rules-authoritative change. "
                + "Pets, Enemies, and Contact add/delete remain outside this surface."),
            NativeTheme.Muted));
        _body.Add(NativeTheme.NavigationRow(
            CreationFlowStrings.Get("Lifestyles.Heading", "Lifestyles"),
            CreationFlowStrings.Get(
                "Contacts.OpenLifestyles",
                "Open the Core catalog, configure a typed Lifestyle, and review exact nuyen/LP economics."),
            () => Navigation.PushAsync(new CreationLifestylesPage(Coordinator)),
            automationId: "creation-contacts-open-lifestyles"));
        var load = Coordinator.LoadCreationContacts();
        if (!string.Equals(load.Outcome, CharacterCreationContactOutcomes.Available, StringComparison.Ordinal)
            || load.State is not { } state)
        {
            AddBlockers(
                CreationFlowStrings.Get(
                    "Contacts.AuthorityUnavailable",
                    "Creation Contacts authority unavailable"),
                load.Blockers.Count > 0 ? load.Blockers : [load.Outcome],
                "creation-contacts-unavailable");
            return;
        }

        AddBinding(state);
        AddBudget(
            state.ContactBudget,
            CreationFlowStrings.Get("Contacts.ContactPoints", "Contact points"),
            "creation-contacts-budget");
        AddBudget(
            state.HighPlacesBudget,
            CreationFlowStrings.Get("Contacts.FriendsInHighPlaces", "Friends in High Places"),
            "creation-contacts-high-places-budget");
        if (!CreationContactsPhoneAuthority.IsReady(state, Coordinator.State))
        {
            AddBlockers(
                CreationFlowStrings.Get(
                    "Contacts.AuthorityBlocked",
                    "Creation Contacts authority blocked"),
                state.Blockers.DefaultIfEmpty(
                    CharacterCreationContactsBlockers.AuthorityUnavailable).ToArray(),
                "creation-contacts-blockers");
            return;
        }

        AddContacts(state);
        AddSourceAuthority(state);
    }

    private void AddBinding(CharacterCreationContactsInteractionState state)
    {
        Label binding = NativeTheme.Body(
            CreationFlowStrings.Format(
                "Common.Binding",
                "Revision {0} · saved {1} · snapshot {2} · source {3}",
                state.Binding.ContentRevision,
                state.Binding.SavedRevision,
                ShortDigest(state.SnapshotDigest),
                ShortDigest(state.Binding.SourceDigest)),
            NativeTheme.Muted);
        binding.AutomationId = "creation-contacts-binding";
        _body.Add(binding);
    }

    private void AddBudget(
        CharacterCreationContactBudget budget,
        string label,
        string automationId)
    {
        VerticalStackLayout card = new() { Spacing = 7 };
        card.Add(NativeTheme.Eyebrow(label));
        card.Add(NativeTheme.Metric(
            CreationFlowStrings.Get("Common.Total", "Total"),
            budget.Total.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric(
            CreationFlowStrings.Get("Common.Used", "Used"),
            budget.Used.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric(
            CreationFlowStrings.Get("Common.Remaining", "Remaining"),
            budget.Remaining.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Body(
            budget.IsExact
                ? CreationFlowStrings.Get("Common.ExactCoreBudget", "Exact Core budget")
                : CreationFlowStrings.Get("Common.BudgetInexact", "Budget authority is not exact"),
            budget.IsExact ? NativeTheme.Muted : NativeTheme.Danger));
        foreach (string blocker in budget.Blockers)
            card.Add(NativeTheme.Body(blocker, NativeTheme.Danger));
        Border border = NativeTheme.Card(card);
        border.AutomationId = automationId;
        SemanticProperties.SetDescription(
            border,
            CreationFlowStrings.Format(
                "Common.BudgetSemantic",
                "{0}. Total {1}. Used {2}. Remaining {3}.",
                label,
                budget.Total,
                budget.Used,
                budget.Remaining));
        _body.Add(border);
    }

    private void AddContacts(CharacterCreationContactsInteractionState state)
    {
        _body.Add(NativeTheme.Eyebrow(CreationFlowStrings.Get("Contacts.Existing", "Existing Contacts")));
        if (state.Contacts.Count == 0)
        {
            Label empty = NativeTheme.Body(
                CreationFlowStrings.Get(
                    "Contacts.Empty",
                    "No existing Contact is projected. Adding Contacts is not yet available here."),
                NativeTheme.Muted);
            empty.AutomationId = "creation-contacts-empty";
            _body.Add(NativeTheme.Card(empty));
            return;
        }

        foreach (CharacterCreationContactProjection contact in state.Contacts)
        {
            string name = string.IsNullOrWhiteSpace(contact.Identity.Name)
                ? CreationFlowStrings.Get("Contacts.Unnamed", "Unnamed Contact")
                : contact.Identity.Name;
            string detail = string.Join(
                " · ",
                new[]
                {
                    string.IsNullOrWhiteSpace(contact.Identity.Role) ? null : contact.Identity.Role,
                    CreationFlowStrings.Format("Contacts.Connection", "Connection {0}", contact.Connection),
                    CreationFlowStrings.Format("Contacts.Loyalty", "Loyalty {0}", contact.Loyalty),
                    contact.Free
                        ? CreationFlowStrings.Get("Contacts.Free", "Free")
                        : CreationFlowStrings.Format("Contacts.Cost", "Cost {0}", contact.ContactPointCost),
                    CreationFlowStrings.Format(
                        "Common.AuthorityInline",
                        "authority {0}",
                        ShortDigest(contact.ContactDigest))
                }.Where(value => value is not null));
            _body.Add(NativeTheme.NavigationRow(
                name,
                detail,
                () => Navigation.PushAsync(new CreationContactEditPage(
                    Coordinator,
                    contact.ContactId)),
                automationId: $"creation-contact-item-{contact.ContactId:N}"));
        }
    }

    private void AddSourceAuthority(CharacterCreationContactsInteractionState state)
    {
        VerticalStackLayout card = new() { Spacing = 6 };
        card.Add(NativeTheme.Eyebrow(CreationFlowStrings.Get("Common.AuthorityBinding", "Authority binding")));
        card.Add(NativeTheme.Metric(CreationFlowStrings.Get("Common.Content", "Content"), state.Binding.ContentDigest));
        card.Add(NativeTheme.Metric(CreationFlowStrings.Get("Common.AuxiliaryState", "Auxiliary state"), state.Binding.AuxiliaryStateDigest));
        card.Add(NativeTheme.Metric(CreationFlowStrings.Get("Common.Source", "Source"), state.Binding.SourceDigest));
        card.Add(NativeTheme.Metric(CreationFlowStrings.Get("Common.Rules", "Rules"), state.Binding.RulesDigest));
        card.Add(NativeTheme.Metric(CreationFlowStrings.Get("Common.Runtime", "Runtime"), state.Binding.RuntimeDigest));
        Border border = NativeTheme.Card(card);
        border.AutomationId = "creation-contacts-authority";
        _body.Add(border);
    }

    private void AddBlockers(string title, IReadOnlyList<string> blockers, string automationId)
    {
        VerticalStackLayout card = new() { Spacing = 6 };
        card.Add(NativeTheme.Eyebrow(title));
        foreach (string blocker in blockers.Distinct(StringComparer.Ordinal))
            card.Add(NativeTheme.Body(blocker, NativeTheme.Danger));
        Border border = NativeTheme.Card(card);
        border.AutomationId = automationId;
        _body.Add(border);
    }

    private static string ShortDigest(string value)
        => string.IsNullOrWhiteSpace(value)
            ? CreationFlowStrings.Get("Common.Unavailable", "unavailable")
            : value[..Math.Min(19, value.Length)];
}
