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
        Title = CreationAllocationStrings.Get("Basics.PageTitle", "Rules & sources");
        AutomationId = "creation-basics-page";
        Content = new ScrollView { Content = _body };
    }

    protected override void Refresh()
    {
        _body.Clear();
        _body.Add(NativeTheme.Title(CreationAllocationStrings.Get("Basics.Heading", "Your runner")));

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
                CreationAllocationStrings.Get("Basics.Unavailable",
                    "Creation settings could not be loaded. Go back and reopen this runner."),
                NativeTheme.Danger);
            unavailable.AutomationId = "creation-basics-authority-unavailable";
            _body.Add(NativeTheme.Card(unavailable));
            return;
        }

        VerticalStackLayout binding = new() { Spacing = 6 };
        // Profile IDs, revisions and digests stay in the original bound state;
        // they are not useful display names on the player's setup screen.
        binding.Add(NativeTheme.Metric(CreationAllocationStrings.Get("Basics.Runner", "Runner"),
            FirstNonBlank(profile.Alias, profile.Name, CreationAllocationStrings.Get("Basics.NotSet", "Not set"))));
        binding.Add(NativeTheme.Metric(CreationAllocationStrings.Get("Basics.Edition", "Edition"), rules.GameEdition));
        if (!string.IsNullOrWhiteSpace(rules.Settings) && !Guid.TryParse(rules.Settings, out _))
            binding.Add(NativeTheme.Metric(CreationAllocationStrings.Get("Basics.Profile", "Rules profile"), rules.Settings));
        binding.Add(NativeTheme.Metric(CreationAllocationStrings.Get("Basics.BuildMethod", "Build method"),
            snapshot.BuildMethod is CharacterCreationBuildMethods.Priority or CharacterCreationBuildMethods.SumToTen
                or CharacterCreationBuildMethods.Karma or CharacterCreationBuildMethods.LifeModules
                ? BuildPageUiProjection.MethodLabel(snapshot.BuildMethod)
                : CreationAllocationStrings.Get("Basics.UnknownMethod", "Unavailable")));
        Border authority = NativeTheme.Card(binding);
        authority.AutomationId = "creation-basics-authority";
        _body.Add(authority);

        VerticalStackLayout books = new() { Spacing = 6 };
        books.Add(NativeTheme.Eyebrow(CreationAllocationStrings.Get("Basics.SourceBooks", "Source books")));
        // There is no typed creation settings-profile/sourcebook selection contract.
        // Do not infer an enabled-book list or expose an ineffective editor.
        Label blocker = NativeTheme.Body(
            CreationAllocationStrings.Get("Basics.SourceBooksReadOnly",
                "Source books follow this runner's saved profile. You cannot change them on this screen yet."),
            NativeTheme.Muted);
        blocker.AutomationId = "creation-basics-sourcebooks-contract-unavailable";
        books.Add(blocker);
        _body.Add(NativeTheme.Card(books));
    }

    private static string FirstNonBlank(params string?[] values)
        => values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value))?.Trim()
           ?? string.Empty;
}
