using Chummer.Android.Platform;
using Microsoft.Maui.ApplicationModel.DataTransfer;

namespace Chummer.Android.Native;

public sealed class CampaignPage : NativePageBase
{
    private readonly ToolbarItem _refreshToolbar;
    private readonly VerticalStackLayout _body = new()
    {
        Padding = new Thickness(20, 18, 20, 40),
        Spacing = 16
    };
    private string? _selectedChronicleId;

    public CampaignPage(RunnerSessionCoordinator coordinator) : base(coordinator)
    {
        Title = "Campaign";
        _refreshToolbar = new ToolbarItem
        {
            Text = "Refresh",
            Command = new Command(async () => await RunAsync(() => Coordinator.RefreshLinkedDataAsync()))
        };
        ToolbarItems.Add(_refreshToolbar);
        Content = new ScrollView { Content = _body };
    }

    protected override void Refresh()
    {
        _refreshToolbar.IsEnabled = Coordinator.Account.IsLinked;
        _body.Clear();
        _body.Add(NativeTheme.Eyebrow("Groups"));
        _body.Add(NativeTheme.Title("Campaign"));

        if (!Coordinator.Account.IsLinked)
        {
            _body.Add(NativeTheme.Body("Link your Chummer account to see or run a group.", NativeTheme.Muted));
            Button link = NativeTheme.PrimaryButton("Link account");
            link.IsEnabled = !Coordinator.Account.IsLoading;
            link.Clicked += async (_, _) => await RunAsync(() => Coordinator.BeginAccountLinkAsync());
            _body.Add(link);
            return;
        }

        Grid actions = new()
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star)
            },
            ColumnSpacing = 10
        };
        Button refresh = NativeTheme.SecondaryButton(Coordinator.Groups.Count == 0 ? "Load groups" : "Refresh");
        refresh.Clicked += async (_, _) => await RunAsync(() => Coordinator.RefreshLinkedDataAsync());
        Button create = NativeTheme.PrimaryButton("Create group");
        create.Clicked += async (_, _) => await Navigation.PushModalAsync(
            new NavigationPage(new GroupEditorPage(Coordinator)));
        actions.Add(refresh);
        actions.Add(create, 1);
        _body.Add(actions);

        if (Coordinator.Groups.Count == 0)
        {
            _body.Add(NativeTheme.Body("No groups loaded yet.", NativeTheme.Muted));
            return;
        }

        AddGroupPicker();
        if (Coordinator.SelectedGroup is { } group)
        {
            AddGroup(group);
            AddChronicles(group);
        }
    }

    private void AddGroupPicker()
    {
        string[] names = Coordinator.Groups.Select(static group => group.Name).ToArray();
        Picker picker = new()
        {
            Title = "Group",
            ItemsSource = names,
            SelectedIndex = Math.Max(0, Coordinator.Groups.ToList().FindIndex(group =>
                string.Equals(group.GroupId, Coordinator.SelectedGroup?.GroupId, StringComparison.Ordinal))),
            BackgroundColor = NativeTheme.Surface
        };
        picker.SelectedIndexChanged += async (_, _) =>
        {
            if (picker.SelectedIndex >= 0)
            {
                AndroidLinkedGroup group = Coordinator.Groups[picker.SelectedIndex];
                Coordinator.SelectGroup(group);
                _selectedChronicleId = null;
                await RunAsync(() => Coordinator.RefreshChroniclesAsync(group));
            }
        };
        _body.Add(picker);
    }

    private void AddGroup(AndroidLinkedGroup group)
    {
        VerticalStackLayout summary = new() { Spacing = 10 };
        summary.Add(NativeTheme.Eyebrow(group.CanManage ? "Game master" : group.Role));
        summary.Add(NativeTheme.Title(group.Name, 23));
        summary.Add(NativeTheme.Metric("Visibility", RunnerSessionCoordinator.HumanizeId(group.Visibility)));
        summary.Add(NativeTheme.Metric("Members", group.Members.Count.ToString()));

        if (group.CanManage)
        {
            Grid controls = new()
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(GridLength.Star)
                },
                ColumnSpacing = 10
            };
            Button edit = NativeTheme.SecondaryButton("Edit group");
            edit.Clicked += async (_, _) => await Navigation.PushModalAsync(
                new NavigationPage(new GroupEditorPage(Coordinator, group)));
            Button invite = NativeTheme.PrimaryButton("Invite players");
            invite.Clicked += async (_, _) => await CreateInviteAsync(group);
            controls.Add(edit);
            controls.Add(invite, 1);
            summary.Add(controls);
        }
        _body.Add(NativeTheme.Card(summary));

        VerticalStackLayout roster = new() { Spacing = 12 };
        roster.Add(NativeTheme.Eyebrow("Roster"));
        foreach (AndroidLinkedGroupMember member in group.Members)
        {
            roster.Add(NativeTheme.Metric(
                string.IsNullOrWhiteSpace(member.RunnerHandle) ? "Player" : member.RunnerHandle,
                RunnerSessionCoordinator.HumanizeId(member.Role)));
        }
        if (group.Members.Count == 0)
        {
            roster.Add(NativeTheme.Body("Share an invite to add players.", NativeTheme.Muted));
        }
        _body.Add(NativeTheme.Card(roster));
    }

    private void AddChronicles(AndroidLinkedGroup group)
    {
        VerticalStackLayout studio = new() { Spacing = 11 };
        studio.Add(NativeTheme.Eyebrow("Chronicle Studio"));
        studio.Add(NativeTheme.Title("Campaign books", 22));
        studio.Add(NativeTheme.Body(
            group.CanManage
                ? "Prepare the source here. AIWriteBook stays a separate, approved step."
                : "Finished books shared with this group.",
            NativeTheme.Muted));

        Grid controls = new()
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star)
            },
            ColumnSpacing = 10
        };
        Button refresh = NativeTheme.SecondaryButton("Refresh books");
        refresh.Clicked += async (_, _) => await RunAsync(() => Coordinator.RefreshChroniclesAsync(group));
        controls.Add(refresh);
        if (group.CanManage)
        {
            Button create = NativeTheme.PrimaryButton("New book");
            create.Clicked += async (_, _) => await Navigation.PushModalAsync(
                new NavigationPage(new ChronicleEditorPage(Coordinator, group)));
            controls.Add(create, 1);
        }
        studio.Add(controls);

        if (Coordinator.Chronicles.Count == 0)
        {
            studio.Add(NativeTheme.Body("No campaign books yet.", NativeTheme.Muted));
            _body.Add(NativeTheme.Card(studio));
            return;
        }

        AndroidChronicleProject selected = Coordinator.Chronicles.FirstOrDefault(project =>
            string.Equals(project.ChronicleProjectId, _selectedChronicleId, StringComparison.Ordinal))
            ?? Coordinator.Chronicles[0];
        _selectedChronicleId = selected.ChronicleProjectId;
        Picker projects = new()
        {
            Title = "Campaign book",
            ItemsSource = Coordinator.Chronicles.Select(static project => project.Title).ToArray(),
            SelectedIndex = Math.Max(0, Coordinator.Chronicles.ToList().FindIndex(project =>
                string.Equals(project.ChronicleProjectId, selected.ChronicleProjectId, StringComparison.Ordinal))),
            BackgroundColor = NativeTheme.Surface
        };
        projects.SelectedIndexChanged += (_, _) =>
        {
            if (projects.SelectedIndex >= 0)
            {
                _selectedChronicleId = Coordinator.Chronicles[projects.SelectedIndex].ChronicleProjectId;
                Refresh();
            }
        };
        studio.Add(projects);
        studio.Add(NativeTheme.Metric("Status", RunnerSessionCoordinator.HumanizeId(selected.Status)));
        studio.Add(NativeTheme.Metric("Book", RunnerSessionCoordinator.HumanizeId(selected.BookKind)));

        if (group.CanManage)
        {
            studio.Add(NativeTheme.Metric("Audience", RunnerSessionCoordinator.HumanizeId(selected.Audience)));
            studio.Add(NativeTheme.Metric("Estimated credits", selected.EstimatedCredits.ToString()));
            AddChronicleActions(studio, group, selected);
        }
        else if (!string.IsNullOrWhiteSpace(selected.ExportFormat))
        {
            studio.Add(NativeTheme.Metric("Format", selected.ExportFormat.ToUpperInvariant()));
        }
        if (!string.IsNullOrWhiteSpace(selected.ArtifactUrl))
        {
            Button copyArtifact = NativeTheme.SecondaryButton("Copy finished export link");
            copyArtifact.Clicked += async (_, _) =>
            {
                await Clipboard.Default.SetTextAsync(selected.ArtifactUrl);
                copyArtifact.Text = "Copied";
            };
            studio.Add(copyArtifact);
        }
        _body.Add(NativeTheme.Card(studio));
    }

    private void AddChronicleActions(
        VerticalStackLayout studio,
        AndroidLinkedGroup group,
        AndroidChronicleProject project)
    {
        if (string.Equals(project.Status, "draft", StringComparison.Ordinal))
        {
            Button edit = NativeTheme.SecondaryButton("Edit draft");
            edit.Clicked += async (_, _) => await Navigation.PushModalAsync(
                new NavigationPage(new ChronicleEditorPage(Coordinator, group, project)));
            Button approve = NativeTheme.PrimaryButton("Approve source");
            approve.Clicked += async (_, _) => await ConfirmAdvanceAsync(
                group,
                project,
                "approve_source",
                "Approve this source?",
                "This confirms the rights, consent, redaction, and spoiler checks in the draft.");
            studio.Add(edit);
            studio.Add(approve);
            return;
        }

        if (string.Equals(project.Status, "source_approved", StringComparison.Ordinal))
        {
            Button upload = NativeTheme.PrimaryButton("Approve source upload");
            upload.Clicked += async (_, _) => await ConfirmAdvanceAsync(
                group,
                project,
                "approve_upload",
                "Approve the source upload?",
                "This unlocks the reviewed packet for an operator. It does not upload or send it.");
            studio.Add(upload);
            return;
        }

        if (project.Status is "upload_approved" or "handoff_ready" or "generation_approved" or "outline_approved" or "artifact_ready" or "publication_approved" or "external_send_approved")
        {
            Button packet = NativeTheme.SecondaryButton("Save source packet");
            packet.Clicked += async (_, _) => await RunAsync(() => Coordinator.SaveChroniclePacketAsync(group, project));
            studio.Add(packet);
            Button handoff = NativeTheme.SecondaryButton("Save operator handoff");
            handoff.Clicked += async (_, _) => await RunAsync(() => Coordinator.SaveChronicleHandoffAsync(group, project));
            studio.Add(handoff);
        }

        if (project.Status is "upload_approved" or "handoff_ready")
        {
            Button provider = NativeTheme.PrimaryButton("Approve generation");
            provider.Clicked += async (_, _) => await Navigation.PushModalAsync(
                new NavigationPage(new ChronicleActionPage(Coordinator, group, project, ChronicleActionKind.ProviderProject)));
            studio.Add(provider);
        }
        else if (string.Equals(project.Status, "generation_approved", StringComparison.Ordinal))
        {
            Button outline = NativeTheme.PrimaryButton("Approve reviewed outline");
            outline.Clicked += async (_, _) => await ConfirmAdvanceAsync(
                group,
                project,
                "approve_outline",
                "Approve this outline?",
                "Confirm that you reviewed the provider outline. This does not start another generation.");
            studio.Add(outline);
        }
        else if (string.Equals(project.Status, "outline_approved", StringComparison.Ordinal))
        {
            Button artifact = NativeTheme.PrimaryButton("Add finished export");
            artifact.Clicked += async (_, _) => await Navigation.PushModalAsync(
                new NavigationPage(new ChronicleActionPage(Coordinator, group, project, ChronicleActionKind.Artifact)));
            studio.Add(artifact);
        }
        else if (string.Equals(project.Status, "artifact_ready", StringComparison.Ordinal))
        {
            Button publication = NativeTheme.PrimaryButton("Approve publication");
            publication.Clicked += async (_, _) => await ConfirmAdvanceAsync(
                group,
                project,
                "approve_publication",
                "Approve publication?",
                "This records approval in Chummer. It does not publish or send the book.");
            studio.Add(publication);
        }
        else if (string.Equals(project.Status, "publication_approved", StringComparison.Ordinal))
        {
            Button sharing = NativeTheme.PrimaryButton("Approve external sharing");
            sharing.Clicked += async (_, _) => await ConfirmAdvanceAsync(
                group,
                project,
                "approve_external_send",
                "Approve external sharing?",
                "This records permission only. Chummer does not send or publish the book.");
            studio.Add(sharing);
        }
    }

    private async Task ConfirmAdvanceAsync(
        AndroidLinkedGroup group,
        AndroidChronicleProject project,
        string action,
        string title,
        string message)
    {
        bool confirmed = await DisplayAlertAsync(title, message, "Continue", "Cancel");
        if (confirmed)
        {
            await RunAsync(() => Coordinator.AdvanceChronicleAsync(group, project, action));
        }
    }

    private async Task CreateInviteAsync(AndroidLinkedGroup group)
    {
        await RunAsync(async () =>
        {
            Uri invite = await Coordinator.CreateGroupInviteAsync(group);
            await Clipboard.Default.SetTextAsync(invite.ToString());
            await Navigation.PushModalAsync(new NavigationPage(new InvitePage(Coordinator, group.Name, invite)));
        });
    }
}

internal sealed class ChronicleEditorPage : ContentPage
{
    private static readonly string[] BookLabels =
        ["Campaign bible", "Season chronicle", "Player recap", "Adventure booklet", "World guide"];
    private static readonly string[] BookValues =
        ["campaign_bible", "season_chronicle", "player_recap", "adventure_booklet", "world_guide"];
    private static readonly string[] AudienceLabels = ["GM private", "Player safe"];
    private static readonly string[] AudienceValues = ["gm_private", "player_safe"];
    private static readonly string[] ModelLabels = ["Gemini", "Grok", "Claude"];
    private static readonly string[] ModelValues = ["gemini", "grok", "claude"];

    private readonly RunnerSessionCoordinator _coordinator;
    private readonly AndroidLinkedGroup _group;
    private readonly AndroidChronicleProject? _project;
    private readonly Entry _title;
    private readonly Editor _summary;
    private readonly Picker _book;
    private readonly Picker _audience;
    private readonly Picker _model;
    private readonly Stepper _chapters;
    private readonly Stepper _words;
    private readonly Switch _includeRoster;
    private readonly Switch _includeCover;
    private readonly Switch _includeTranslation;
    private readonly Switch _includeAudiobook;
    private readonly Switch _externalConsent;
    private readonly Switch _participantConsent;
    private readonly Switch _redactionReviewed;
    private readonly Switch _spoilerReview;
    private readonly Switch _sourceRights;

    public ChronicleEditorPage(
        RunnerSessionCoordinator coordinator,
        AndroidLinkedGroup group,
        AndroidChronicleProject? project = null)
    {
        _coordinator = coordinator;
        _group = group;
        _project = project;
        Title = project is null ? "New campaign book" : "Edit campaign book";
        BackgroundColor = NativeTheme.Paper;
        _title = new Entry
        {
            Text = project?.Title ?? string.Empty,
            Placeholder = "Book title",
            MaxLength = 160,
            BackgroundColor = NativeTheme.Surface,
            TextColor = NativeTheme.Text
        };
        _summary = new Editor
        {
            Text = project?.SourceSummary ?? string.Empty,
            Placeholder = "What should the book cover?",
            MaxLength = 4000,
            MinimumHeightRequest = 150,
            AutoSize = EditorAutoSizeOption.TextChanges,
            BackgroundColor = NativeTheme.Surface,
            TextColor = NativeTheme.Text
        };
        _book = Picker(BookLabels, IndexOf(BookValues, project?.BookKind, 0), "Book type");
        _audience = Picker(AudienceLabels, IndexOf(AudienceValues, project?.Audience, 0), "Audience");
        _model = Picker(ModelLabels, IndexOf(ModelValues, project?.ModelKey, 0), "Writing model");
        _chapters = new Stepper(1, 40, project?.TargetChapterCount ?? 8, 1);
        _words = new Stepper(100, 5000, project?.TargetWordsPerChapter ?? 1200, 100);
        _includeRoster = Toggle(project?.IncludeRunnerRoster ?? true);
        _includeCover = Toggle(project?.IncludeCover ?? false);
        _includeTranslation = Toggle(project?.IncludeTranslation ?? false);
        _includeAudiobook = Toggle(project?.IncludeAudiobook ?? false);
        _externalConsent = Toggle(project?.ExternalProcessingConsent ?? false);
        _participantConsent = Toggle(project?.ParticipantConsentConfirmed ?? false);
        _redactionReviewed = Toggle(project?.RedactionReviewed ?? false);
        _spoilerReview = Toggle(project?.SpoilerReviewConfirmed ?? false);
        _sourceRights = Toggle(project?.SourceRightsConfirmed ?? false);

        VerticalStackLayout body = new()
        {
            Padding = new Thickness(20, 18, 20, 40),
            Spacing = 16
        };
        body.Add(NativeTheme.Eyebrow("Chronicle Studio"));
        body.Add(NativeTheme.Title(Title, 25));
        body.Add(NativeTheme.Card(new VerticalStackLayout
        {
            Spacing = 11,
            Children = { _title, _book, _audience, _model, _summary }
        }));

        Label chapterValue = NativeTheme.Body($"Chapters  {(int)_chapters.Value}");
        chapterValue.FontAttributes = FontAttributes.Bold;
        _chapters.ValueChanged += (_, args) => chapterValue.Text = $"Chapters  {(int)args.NewValue}";
        Label wordValue = NativeTheme.Body($"Words per chapter  {(int)_words.Value}");
        wordValue.FontAttributes = FontAttributes.Bold;
        _words.ValueChanged += (_, args) => wordValue.Text = $"Words per chapter  {(int)args.NewValue}";
        body.Add(NativeTheme.Card(new VerticalStackLayout
        {
            Spacing = 10,
            Children = { chapterValue, _chapters, wordValue, _words }
        }));

        body.Add(NativeTheme.Card(new VerticalStackLayout
        {
            Spacing = 10,
            Children =
            {
                NativeTheme.Eyebrow("Extras"),
                ToggleRow("Include runner roster", _includeRoster),
                ToggleRow("Cover", _includeCover),
                ToggleRow("Translation", _includeTranslation),
                ToggleRow("Audiobook", _includeAudiobook)
            }
        }));
        body.Add(NativeTheme.Card(new VerticalStackLayout
        {
            Spacing = 10,
            Children =
            {
                NativeTheme.Eyebrow("Checks before approval"),
                ToggleRow("External processing is approved", _externalConsent),
                ToggleRow("Participants agreed", _participantConsent),
                ToggleRow("Private details and identifiers reviewed", _redactionReviewed),
                ToggleRow("Player-facing spoilers reviewed", _spoilerReview),
                ToggleRow("Source rights are confirmed", _sourceRights)
            }
        }));

        Button save = NativeTheme.PrimaryButton(project is null ? "Create draft" : "Save draft");
        save.Clicked += async (_, _) => await SaveAsync();
        Button cancel = NativeTheme.SecondaryButton("Cancel");
        cancel.Clicked += async (_, _) => await Navigation.PopModalAsync();
        body.Add(save);
        body.Add(cancel);
        Content = new ScrollView { Content = body };
    }

    private async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(_title.Text) || string.IsNullOrWhiteSpace(_summary.Text))
        {
            await DisplayAlertAsync("Campaign book", "Add a title and a short source brief.", "OK");
            return;
        }

        AndroidChronicleDraft draft = new(
            _title.Text.Trim(),
            BookValues[Math.Max(0, _book.SelectedIndex)],
            AudienceValues[Math.Max(0, _audience.SelectedIndex)],
            _summary.Text.Trim(),
            ModelValues[Math.Max(0, _model.SelectedIndex)],
            (int)_chapters.Value,
            (int)_words.Value,
            _includeRoster.IsToggled,
            _includeCover.IsToggled,
            _includeTranslation.IsToggled,
            _includeAudiobook.IsToggled,
            _externalConsent.IsToggled,
            _participantConsent.IsToggled,
            _redactionReviewed.IsToggled,
            _sourceRights.IsToggled,
            _spoilerReview.IsToggled);
        try
        {
            if (_project is null)
            {
                await _coordinator.CreateChronicleAsync(_group, draft);
            }
            else
            {
                await _coordinator.ReviseChronicleAsync(_group, _project, draft);
            }
            await Navigation.PopModalAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Campaign book", ex.Message, "OK");
        }
    }

    private static Picker Picker(string[] labels, int selectedIndex, string title)
        => new()
        {
            Title = title,
            ItemsSource = labels,
            SelectedIndex = selectedIndex,
            BackgroundColor = NativeTheme.Surface,
            TextColor = NativeTheme.Text
        };

    private static Switch Toggle(bool value) => new() { IsToggled = value, OnColor = NativeTheme.Signal };

    private static Grid ToggleRow(string label, Switch toggle)
    {
        Grid row = new()
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            ColumnSpacing = 12
        };
        row.Add(NativeTheme.Body(label));
        row.Add(toggle, 1);
        return row;
    }

    private static int IndexOf(string[] values, string? value, int fallback)
    {
        int index = Array.FindIndex(values, item => string.Equals(item, value, StringComparison.OrdinalIgnoreCase));
        return index >= 0 ? index : fallback;
    }
}

internal enum ChronicleActionKind
{
    ProviderProject,
    Artifact
}

internal sealed class ChronicleActionPage : ContentPage
{
    private readonly RunnerSessionCoordinator _coordinator;
    private readonly AndroidLinkedGroup _group;
    private readonly AndroidChronicleProject _project;
    private readonly ChronicleActionKind _kind;
    private readonly Entry _primary;
    private readonly Entry? _sha256;
    private readonly Picker? _format;

    public ChronicleActionPage(
        RunnerSessionCoordinator coordinator,
        AndroidLinkedGroup group,
        AndroidChronicleProject project,
        ChronicleActionKind kind)
    {
        _coordinator = coordinator;
        _group = group;
        _project = project;
        _kind = kind;
        bool artifact = kind == ChronicleActionKind.Artifact;
        Title = artifact ? "Add finished export" : "Approve generation";
        BackgroundColor = NativeTheme.Paper;
        _primary = new Entry
        {
            Placeholder = artifact ? "HTTPS or Chummer artifact URL" : "AIWriteBook project reference",
            Text = artifact ? project.ArtifactUrl : project.ExternalProjectRef,
            MaxLength = artifact ? 2048 : 256,
            BackgroundColor = NativeTheme.Surface,
            TextColor = NativeTheme.Text
        };
        _sha256 = artifact
            ? new Entry
            {
                Placeholder = "SHA-256",
                Text = project.ArtifactSha256,
                MaxLength = 64,
                BackgroundColor = NativeTheme.Surface,
                TextColor = NativeTheme.Text
            }
            : null;
        _format = artifact
            ? new Picker
            {
                Title = "Export format",
                ItemsSource = new[] { "PDF", "EPUB", "DOCX" },
                SelectedIndex = FormatIndex(project.ExportFormat),
                BackgroundColor = NativeTheme.Surface
            }
            : null;

        VerticalStackLayout fields = new()
        {
            Padding = new Thickness(20),
            Spacing = 16,
            Children =
            {
                NativeTheme.Eyebrow(project.Title),
                NativeTheme.Title(Title, 25),
                NativeTheme.Body(
                    artifact
                        ? "Record the verified export. Chummer does not publish it from this step."
                        : "Record the AIWriteBook project reference, then explicitly approve this generation and its credit spend.",
                    NativeTheme.Muted),
                _primary
            }
        };
        if (_sha256 is not null && _format is not null)
        {
            fields.Add(_sha256);
            fields.Add(_format);
        }
        Button save = NativeTheme.PrimaryButton(artifact ? "Add export" : "Approve generation");
        save.Clicked += async (_, _) => await SaveAsync();
        Button cancel = NativeTheme.SecondaryButton("Cancel");
        cancel.Clicked += async (_, _) => await Navigation.PopModalAsync();
        fields.Add(save);
        fields.Add(cancel);
        Content = new ScrollView { Content = fields };
    }

    private async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(_primary.Text))
        {
            await DisplayAlertAsync(Title, "Fill in the required field.", "OK");
            return;
        }

        try
        {
            if (_kind == ChronicleActionKind.ProviderProject)
            {
                await _coordinator.AdvanceChronicleAsync(
                    _group,
                    _project,
                    "approve_generation",
                    externalProjectRef: _primary.Text.Trim());
            }
            else
            {
                string format = _format!.SelectedIndex switch
                {
                    1 => "epub",
                    2 => "docx",
                    _ => "pdf"
                };
                await _coordinator.AdvanceChronicleAsync(
                    _group,
                    _project,
                    "import_artifact",
                    artifactUrl: _primary.Text.Trim(),
                    artifactSha256: _sha256?.Text?.Trim(),
                    exportFormat: format);
            }
            await Navigation.PopModalAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync(Title, ex.Message, "OK");
        }
    }

    private static int FormatIndex(string? format)
        => string.Equals(format, "epub", StringComparison.OrdinalIgnoreCase)
            ? 1
            : string.Equals(format, "docx", StringComparison.OrdinalIgnoreCase) ? 2 : 0;
}

internal sealed class GroupEditorPage : ContentPage
{
    private readonly RunnerSessionCoordinator _coordinator;
    private readonly AndroidLinkedGroup? _group;
    private readonly Entry _name;
    private readonly Picker _visibility;

    public GroupEditorPage(RunnerSessionCoordinator coordinator, AndroidLinkedGroup? group = null)
    {
        _coordinator = coordinator;
        _group = group;
        Title = group is null ? "Create group" : "Edit group";
        BackgroundColor = NativeTheme.Paper;
        _name = new Entry
        {
            Text = group?.Name ?? string.Empty,
            Placeholder = "Group name",
            BackgroundColor = NativeTheme.Surface,
            TextColor = NativeTheme.Text,
            MaxLength = 120
        };
        _visibility = new Picker
        {
            Title = "Visibility",
            ItemsSource = new[] { "Private", "Group members", "Unlisted" },
            SelectedIndex = VisibilityIndex(group?.Visibility),
            BackgroundColor = NativeTheme.Surface
        };

        Button save = NativeTheme.PrimaryButton(group is null ? "Create group" : "Save changes");
        save.Clicked += async (_, _) => await SaveAsync();
        Button cancel = NativeTheme.SecondaryButton("Cancel");
        cancel.Clicked += async (_, _) => await Navigation.PopModalAsync();
        VerticalStackLayout fields = new()
        {
            Padding = new Thickness(20),
            Spacing = 16,
            Children =
            {
                NativeTheme.Eyebrow("Campaign"),
                NativeTheme.Title(Title, 25),
                NativeTheme.Body("Players join through a browser link and appear in the native roster.", NativeTheme.Muted),
                _name,
                _visibility,
                save,
                cancel
            }
        };
        Content = new ScrollView { Content = fields };
    }

    private async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(_name.Text))
        {
            await DisplayAlertAsync("Group name", "Enter a name for the group.", "OK");
            return;
        }

        try
        {
            string visibility = _visibility.SelectedIndex switch
            {
                2 => "unlisted",
                1 => "group",
                _ => "private"
            };
            if (_group is null)
            {
                await _coordinator.CreateGroupAsync(_name.Text, visibility);
            }
            else
            {
                await _coordinator.UpdateGroupAsync(_group, _name.Text, visibility);
            }
            await Navigation.PopModalAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Campaign", ex.Message, "OK");
        }
    }

    private static int VisibilityIndex(string? visibility)
        => string.Equals(visibility, "unlisted", StringComparison.OrdinalIgnoreCase)
            ? 2
            : string.Equals(visibility, "group", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
}

internal sealed class InvitePage : ContentPage
{
    public InvitePage(RunnerSessionCoordinator coordinator, string groupName, Uri invite)
    {
        Title = "Invite players";
        BackgroundColor = NativeTheme.Paper;
        Label link = NativeTheme.Body(invite.ToString());
        link.LineBreakMode = LineBreakMode.CharacterWrap;
        Button copy = NativeTheme.PrimaryButton("Copy link");
        copy.Clicked += async (_, _) =>
        {
            await Clipboard.Default.SetTextAsync(invite.ToString());
            copy.Text = "Copied";
        };
        Button share = NativeTheme.SecondaryButton("Share");
        share.Clicked += async (_, _) => await coordinator.ShareTextAsync(invite.ToString());
        Button done = NativeTheme.SecondaryButton("Done");
        done.Clicked += async (_, _) => await Navigation.PopModalAsync();
        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = new Thickness(20),
                Spacing = 16,
                Children =
                {
                    NativeTheme.Eyebrow(groupName),
                    NativeTheme.Title("Invite link", 25),
                    NativeTheme.Body("Anyone who opens this link while signed in can join the group.", NativeTheme.Muted),
                    NativeTheme.Card(link),
                    copy,
                    share,
                    done
                }
            }
        };
    }
}
