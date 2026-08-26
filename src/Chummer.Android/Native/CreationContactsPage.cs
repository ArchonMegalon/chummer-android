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
        Title = "Creation contacts";
        AutomationId = "creation-contacts-page";
        Content = new ScrollView { Content = _body };
    }

    protected override void Refresh()
    {
        _body.Clear();
        _body.Add(NativeTheme.Eyebrow("Character creation"));
        _body.Add(NativeTheme.Title("Contacts"));
        _body.Add(NativeTheme.Body(
            "Choose an existing Contact and preview one rules-authoritative change. "
            + "Pets, Enemies, and Contact add/delete remain outside this surface.",
            NativeTheme.Muted));
        _body.Add(NativeTheme.NavigationRow(
            "Lifestyles",
            "Open the Core catalog, configure a typed Lifestyle, and review exact nuyen/LP economics.",
            () => Navigation.PushAsync(new CreationLifestylesPage(Coordinator)),
            automationId: "creation-contacts-open-lifestyles"));
        var load = Coordinator.LoadCreationContacts();
        if (!string.Equals(load.Outcome, CharacterCreationContactOutcomes.Available, StringComparison.Ordinal)
            || load.State is not { } state)
        {
            AddBlockers(
                "Creation Contacts authority unavailable",
                load.Blockers.Count > 0 ? load.Blockers : [load.Outcome],
                "creation-contacts-unavailable");
            return;
        }

        AddBinding(state);
        AddBudget(state.ContactBudget, "Contact points", "creation-contacts-budget");
        AddBudget(
            state.HighPlacesBudget,
            "Friends in High Places",
            "creation-contacts-high-places-budget");
        if (!CreationContactsPhoneAuthority.IsReady(state, Coordinator.State))
        {
            AddBlockers(
                "Creation Contacts authority blocked",
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
            $"Revision {state.Binding.ContentRevision} · saved {state.Binding.SavedRevision} · "
            + $"snapshot {ShortDigest(state.SnapshotDigest)} · source {ShortDigest(state.Binding.SourceDigest)}",
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
            "Total",
            budget.Total.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric(
            "Used",
            budget.Used.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric(
            "Remaining",
            budget.Remaining.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Body(
            budget.IsExact ? "Exact Core budget" : "Budget authority is not exact",
            budget.IsExact ? NativeTheme.Muted : NativeTheme.Danger));
        foreach (string blocker in budget.Blockers)
            card.Add(NativeTheme.Body(blocker, NativeTheme.Danger));
        Border border = NativeTheme.Card(card);
        border.AutomationId = automationId;
        SemanticProperties.SetDescription(
            border,
            $"{label}. Total {budget.Total}. Used {budget.Used}. Remaining {budget.Remaining}.");
        _body.Add(border);
    }

    private void AddContacts(CharacterCreationContactsInteractionState state)
    {
        _body.Add(NativeTheme.Eyebrow("Existing Contacts"));
        if (state.Contacts.Count == 0)
        {
            Label empty = NativeTheme.Body(
                "No existing Contact is projected. Adding Contacts is not yet available here.",
                NativeTheme.Muted);
            empty.AutomationId = "creation-contacts-empty";
            _body.Add(NativeTheme.Card(empty));
            return;
        }

        foreach (CharacterCreationContactProjection contact in state.Contacts)
        {
            string name = string.IsNullOrWhiteSpace(contact.Identity.Name)
                ? "Unnamed Contact"
                : contact.Identity.Name;
            string detail = string.Join(
                " · ",
                new[]
                {
                    string.IsNullOrWhiteSpace(contact.Identity.Role) ? null : contact.Identity.Role,
                    $"Connection {contact.Connection}",
                    $"Loyalty {contact.Loyalty}",
                    contact.Free ? "Free" : $"Cost {contact.ContactPointCost}",
                    $"authority {ShortDigest(contact.ContactDigest)}"
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
        card.Add(NativeTheme.Eyebrow("Authority binding"));
        card.Add(NativeTheme.Metric("Content", state.Binding.ContentDigest));
        card.Add(NativeTheme.Metric("Auxiliary state", state.Binding.AuxiliaryStateDigest));
        card.Add(NativeTheme.Metric("Source", state.Binding.SourceDigest));
        card.Add(NativeTheme.Metric("Rules", state.Binding.RulesDigest));
        card.Add(NativeTheme.Metric("Runtime", state.Binding.RuntimeDigest));
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
        => string.IsNullOrWhiteSpace(value) ? "unavailable" : value[..Math.Min(19, value.Length)];
}
