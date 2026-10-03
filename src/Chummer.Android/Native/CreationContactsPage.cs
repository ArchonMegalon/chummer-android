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
    private CharacterCreationContactsInteractionState? _authority;
    private CharacterCreationContactsInteractionLoadResult? _loaded;
    private bool _loading;
    private VerticalStackLayout _technicalDetails = new() { Spacing = 6 };

    public CreationContactsPage(
        RunnerSessionCoordinator coordinator,
        CharacterCreationContactsInteractionState? authority = null) : base(coordinator)
    {
        _authority = authority;
        Title = CreationFlowStrings.Get("Contacts.PageTitle", "Creation contacts");
        AutomationId = "creation-contacts-page";
        Content = new ScrollView { Content = _body };
    }

    protected override async Task PrepareForAppearanceRefreshAsync(CancellationToken cancellationToken)
    {
        if (_authority is { } current && CreationContactsPhoneAuthority.IsReady(current, Coordinator.State)) return;
        _loading = true;
        _loaded = null;
        Refresh();
        try
        {
            var loaded = await Coordinator.LoadCreationContactsForDisplayAsync(Coordinator.State, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            _loaded = loaded;
            _authority = _loaded.State;
        }
        finally { if (!cancellationToken.IsCancellationRequested) _loading = false; }
    }

    protected override void Refresh()
    {
        _body.Clear();
        _technicalDetails = new() { Spacing = 6 };
        _body.Add(NativeTheme.Eyebrow(CreationFlowStrings.Get("Common.CharacterCreation", "Character creation")));
        _body.Add(NativeTheme.Title(CreationFlowStrings.Get("Contacts.Heading", "Contacts")));
        _body.Add(NativeTheme.Body(
            CreationFlowStrings.Get(
                "Contacts.Intro",
                "Add someone your runner can turn to. Review their details and point cost before saving."),
            NativeTheme.Muted));
        _body.Add(NativeTheme.NavigationRow(
            CreationFlowStrings.Get("Lifestyles.Heading", "Lifestyles"),
            CreationFlowStrings.Get(
                "Contacts.OpenLifestyles",
                "Choose where your runner lives and review the cost."),
            () => Navigation.PushAsync(new CreationLifestylesPage(Coordinator)),
            automationId: "creation-contacts-open-lifestyles"));
        if (_loading)
        {
            _body.Add(new ActivityIndicator { IsRunning = true, AutomationId = "creation-contacts-loading" });
            return;
        }
        CharacterCreationContactsInteractionLoadResult? load = _loaded;
        CharacterCreationContactsInteractionState? state = CreationPageAuthorityCache.Resolve(
            _authority,
            candidate => CreationContactsPhoneAuthority.IsReady(candidate, Coordinator.State),
            () => null);
        _authority = state;
        if (state is null)
        {
            AddBlockers(
                CreationFlowStrings.Get(
                    "Contacts.AuthorityUnavailable",
                    "Creation Contacts authority unavailable"),
                load is { Blockers.Count: > 0 }
                    ? load.Blockers
                    : [CharacterCreationContactsBlockers.AuthorityUnavailable],
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

        if (state.NewContactTemplate is not null)
        {
            Button add = NativeTheme.PrimaryButton(CreationFlowStrings.Get("Contacts.Add", "Add contact"));
            add.AutomationId = "creation-contacts-add";
            add.Clicked += async (_, _) => await Navigation.PushAsync(
                new CreationContactEditPage(Coordinator, Guid.NewGuid(), adding: true));
            _body.Add(add);
        }
        AddContacts(state);
        AddSourceAuthority(state);
        CharacterOverviewState original = Coordinator.State;
        long appearance = CaptureAppearanceGeneration();
        _body.Add(NativeTheme.TechnicalDetails(_technicalDetails, "creation-contacts-details",
            () => IsCurrentAppearanceGeneration(appearance)
                  && Coordinator.IsCreationCatalogDisplayCurrent(original)
                  && CreationContactsPhoneAuthority.IsReady(state, Coordinator.State)));
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
        _technicalDetails.Add(binding);
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
                    "No contacts yet. Add someone your runner can turn to."),
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
                        : CreationFlowStrings.Format("Contacts.Cost", "Cost {0}", contact.ContactPointCost)
                }.Where(value => value is not null));
            _body.Add(NativeTheme.NavigationRow(
                name,
                detail,
                () => Navigation.PushAsync(new CreationContactEditPage(
                    Coordinator,
                    contact.ContactId)),
                automationId: $"creation-contact-item-{contact.ContactId:N}"));
            _technicalDetails.Add(NativeTheme.Metric(name, $"{contact.ContactId:D}\n{contact.ContactDigest}"));
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
        _technicalDetails.Add(border);
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
