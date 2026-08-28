using System.Globalization;
using Chummer.Contracts.Characters;

namespace Chummer.Android.Native;

/// <summary>
/// Read-only phone surface for the creation bootstrap that already selected the
/// ruleset and settings profile.  The current typed graph does not expose a
/// creation-time sourcebook mutation contract, so this page deliberately shows
/// that dependency instead of editing settings.xml or fabricating book toggles.
/// </summary>
public sealed class CreationBasicsPage : NativePageBase
{
    private readonly VerticalStackLayout _body = new()
    {
        Padding = new Thickness(20, 18, 20, 40),
        Spacing = 14
    };

    public CreationBasicsPage(RunnerSessionCoordinator coordinator) : base(coordinator)
    {
        Title = "Creation setup";
        AutomationId = "creation-basics-page";
        Content = new ScrollView { Content = _body };
    }

    protected override void Refresh()
    {
        _body.Clear();
        _body.Add(NativeTheme.Eyebrow("Character creation"));
        _body.Add(NativeTheme.Title("Profile & source books"));

        CharacterProfileSection? profile = Coordinator.State.Profile;
        CharacterRulesSection? rules = Coordinator.State.Rules;
        CharacterCreationWizardSnapshot? snapshot = Coordinator.State.CreationWizard;
        if (profile is null
            || profile.Created
            || rules is null
            || snapshot is null
            || Coordinator.State.WorkspaceId is not { } workspaceId
            || !string.Equals(snapshot.WorkspaceId, workspaceId.Value, StringComparison.Ordinal)
            || snapshot.WorkspaceRevision != Coordinator.State.ContentRevision)
        {
            Label unavailable = NativeTheme.Body(
                "The revision-bound creation setup is unavailable. No settings or sourcebook data was inferred.",
                NativeTheme.Danger);
            unavailable.AutomationId = "creation-basics-authority-unavailable";
            _body.Add(NativeTheme.Card(unavailable));
            return;
        }

        VerticalStackLayout binding = new() { Spacing = 6 };
        binding.Add(NativeTheme.Eyebrow("Bound bootstrap"));
        binding.Add(NativeTheme.Metric("Runner", FirstNonBlank(profile.Alias, profile.Name, "Not set")));
        binding.Add(NativeTheme.Metric("Edition", rules.GameEdition));
        binding.Add(NativeTheme.Metric("Settings profile", rules.Settings));
        binding.Add(NativeTheme.Metric("Build method", snapshot.BuildMethod));
        binding.Add(NativeTheme.Metric(
            "Workspace revision",
            snapshot.WorkspaceRevision.ToString(CultureInfo.InvariantCulture)));
        binding.Add(NativeTheme.Metric("Source authority", ShortDigest(snapshot.SourceDigest)));
        Border authority = NativeTheme.Card(binding);
        authority.AutomationId = "creation-basics-authority";
        _body.Add(authority);

        VerticalStackLayout books = new() { Spacing = 6 };
        books.Add(NativeTheme.Eyebrow("Source books"));
        Label blocker = NativeTheme.Body(
            "This dependency graph has no typed creation settings-profile/sourcebook selection contract. "
            + "The selected profile remains frozen; Chummer will not edit settings.xml or guess enabled books.",
            NativeTheme.Danger);
        blocker.AutomationId = "creation-basics-sourcebooks-contract-unavailable";
        books.Add(blocker);
        _body.Add(NativeTheme.Card(books));
    }

    private static string FirstNonBlank(params string?[] values)
        => values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value))?.Trim()
           ?? string.Empty;

    private static string ShortDigest(string value)
        => string.IsNullOrWhiteSpace(value)
            ? "bootstrap pending"
            : value[..Math.Min(19, value.Length)];
}
