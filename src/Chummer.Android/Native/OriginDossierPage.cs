using Chummer.Contracts.Characters;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

public sealed class OriginDossierPage : NativePageBase
{
    private readonly VerticalStackLayout _body = new()
    {
        Padding = new Thickness(20, 18, 20, 40),
        Spacing = 14
    };

    public OriginDossierPage(RunnerSessionCoordinator coordinator) : base(coordinator)
    {
        Title = "Origin dossier";
        AutomationId = "origin-dossier";
        Content = new ScrollView { Content = _body };
    }

    protected override void Refresh()
    {
        CharacterProfileSection? profile = Coordinator.State.Profile;
        _body.Clear();
        _body.Add(NativeTheme.Eyebrow("Runner"));
        _body.Add(NativeTheme.Title("Origin dossier"));
        if (profile is null)
        {
            _body.Add(NativeTheme.Body("Open a runner first.", NativeTheme.Muted));
            return;
        }

        string identity = FirstNonBlank(profile.Alias, profile.Name, "Not set");
        _body.Add(NativeTheme.NavigationRow(
            "Identity",
            identity,
            () => Navigation.PushAsync(new OriginDossierEditorPage(Coordinator, OriginDossierSection.Identity)),
            automationId: "origin-dossier-identity"));
        _body.Add(NativeTheme.NavigationRow(
            "Appearance",
            FirstNonBlank(profile.Sex, profile.Age, "Not set"),
            () => Navigation.PushAsync(new OriginDossierEditorPage(Coordinator, OriginDossierSection.Appearance)),
            automationId: "origin-dossier-appearance"));
        _body.Add(NativeTheme.NavigationRow(
            "Story",
            FirstNonBlank(profile.Concept, "Not set"),
            () => Navigation.PushAsync(new OriginDossierEditorPage(Coordinator, OriginDossierSection.Story)),
            automationId: "origin-dossier-story"));

        if (!string.IsNullOrWhiteSpace(Coordinator.Notice))
        {
            _body.Add(NativeTheme.Body(Coordinator.Notice!, NativeTheme.Muted));
        }
    }

    private static string FirstNonBlank(params string?[] values)
        => values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
}

internal enum OriginDossierSection
{
    Identity,
    Appearance,
    Story
}

internal sealed class OriginDossierEditorPage : NativePageBase
{
    private readonly OriginDossierSection _section;
    private readonly VerticalStackLayout _body = new()
    {
        Padding = new Thickness(20, 18, 20, 40),
        Spacing = 14
    };
    private readonly Dictionary<string, InputView> _fields = new(StringComparer.Ordinal);

    public OriginDossierEditorPage(RunnerSessionCoordinator coordinator, OriginDossierSection section) : base(coordinator)
    {
        _section = section;
        Title = section.ToString();
        AutomationId = $"origin-dossier-{section.ToString().ToLowerInvariant()}-editor";
        Content = new ScrollView { Content = _body };
    }

    protected override void Refresh()
    {
        CharacterProfileSection? profile = Coordinator.State.Profile;
        if (profile is null)
        {
            _body.Clear();
            _body.Add(NativeTheme.Body("Open a runner first.", NativeTheme.Muted));
            return;
        }

        _body.Clear();
        _fields.Clear();
        _body.Add(NativeTheme.Eyebrow("Origin dossier"));
        _body.Add(NativeTheme.Title(Title));

        switch (_section)
        {
            case OriginDossierSection.Identity:
                AddTextField("name", "Name", profile.Name);
                AddTextField("alias", "Alias", profile.Alias);
                AddTextField("player-name", "Player", profile.PlayerName);
                break;
            case OriginDossierSection.Appearance:
                AddTextField("sex", "Sex", profile.Sex);
                AddTextField("age", "Age", profile.Age);
                AddTextField("height", "Height", profile.Height);
                AddTextField("weight", "Weight", profile.Weight);
                AddTextField("hair", "Hair", profile.Hair);
                AddTextField("eyes", "Eyes", profile.Eyes);
                AddTextField("skin", "Skin", profile.Skin);
                break;
            case OriginDossierSection.Story:
                AddTextArea("concept", "Concept", profile.Concept);
                AddTextArea("description", "Description", profile.Description);
                AddTextArea("background", "Background", profile.Background);
                break;
        }

        Button save = NativeTheme.PrimaryButton("Save changes");
        save.AutomationId = $"origin-dossier-{_section.ToString().ToLowerInvariant()}-save";
        save.Clicked += async (_, _) => await RunAsync(() => SaveAsync(profile));
        _body.Add(save);

        if (!string.IsNullOrWhiteSpace(Coordinator.State.Error))
        {
            _body.Add(NativeTheme.Body(Coordinator.State.Error!, NativeTheme.Danger));
        }
    }

    private void AddTextField(string id, string label, string? value)
    {
        VerticalStackLayout field = new() { Spacing = 5 };
        field.Add(NativeTheme.FieldLabel(label));
        Entry entry = NativeTheme.TextField($"origin-{id}", value);
        _fields.Add(id, entry);
        field.Add(entry);
        _body.Add(field);
    }

    private void AddTextArea(string id, string label, string? value)
    {
        VerticalStackLayout field = new() { Spacing = 5 };
        field.Add(NativeTheme.FieldLabel(label));
        Editor editor = NativeTheme.TextArea($"origin-{id}", value);
        _fields.Add(id, editor);
        field.Add(NativeTheme.Card(editor, new Thickness(12, 6)));
        _body.Add(field);
    }

    private Task SaveAsync(CharacterProfileSection profile)
    {
        string Value(string id, string fallback)
            => _fields.TryGetValue(id, out InputView? input) ? input.Text ?? string.Empty : fallback;

        return Coordinator.ApplyOriginDossierEditAsync(new OriginDossierEditRequest(
            Name: Value("name", profile.Name),
            Alias: Value("alias", profile.Alias),
            PlayerName: Value("player-name", profile.PlayerName),
            Sex: Value("sex", profile.Sex),
            Age: Value("age", profile.Age),
            Height: Value("height", profile.Height),
            Weight: Value("weight", profile.Weight),
            Hair: Value("hair", profile.Hair),
            Eyes: Value("eyes", profile.Eyes),
            Skin: Value("skin", profile.Skin),
            Concept: Value("concept", profile.Concept),
            Description: Value("description", profile.Description),
            Background: Value("background", profile.Background)));
    }
}
