using System.Globalization;
using Chummer.Contracts.Characters;

namespace Chummer.Android.Native;

internal enum CreationKarmaStep { Overview, Metatype, Talent, Attributes, Skills, Skill, Group, Review }

/// <summary>Phone deep pages for the Core-owned pending Karma foundation.</summary>
internal sealed class CreationKarmaPage : NativePageBase
{
    private readonly CreationKarmaPhoneSession _session;
    private readonly CreationKarmaStep _step;
    private readonly string? _sourceId, _instanceId, _sourceKind;
    private readonly VerticalStackLayout _body = new() { Padding = new Thickness(20, 18, 20, 40), Spacing = 14 };
    private readonly ActivityIndicator _loading = new() { HeightRequest = 24 };
    private readonly Label _loadingText = NativeTheme.Body(CreationKarmaCopy.Loading, NativeTheme.Muted);
    private Label? _status;
    private long _render;
    private string _search = string.Empty;
    private int _page;
    private string _category = CharacterCreationSkillKinds.Active;
    private const int PageSize = 20;
    private CharacterCreationKarmaSkillAllocation? _editingSkill;
    private bool _skillInitialized;

    internal CreationKarmaPage(RunnerSessionCoordinator coordinator) : this(coordinator,
        new CreationKarmaPhoneSession(coordinator), CreationKarmaStep.Overview) { }

    private CreationKarmaPage(RunnerSessionCoordinator coordinator, CreationKarmaPhoneSession session,
        CreationKarmaStep step, string? sourceId = null, string? instanceId = null, string? sourceKind = null) : base(coordinator)
    {
        _session = session; _step = step; _sourceId = sourceId; _instanceId = instanceId; _sourceKind = sourceKind;
        Title = StepTitle(step);
        AutomationId = "creation-karma-" + step.ToString().ToLowerInvariant();
        _loadingText.AutomationId = "creation-karma-loading";
        Content = new ScrollView { Content = _body };
    }

    private static string StepTitle(CreationKarmaStep step) => step switch
    {
        CreationKarmaStep.Metatype => CreationKarmaCopy.Metatype,
        CreationKarmaStep.Talent => CreationKarmaCopy.Talent,
        CreationKarmaStep.Attributes => CreationKarmaCopy.Attributes,
        CreationKarmaStep.Skills or CreationKarmaStep.Skill => CreationKarmaCopy.Skills,
        CreationKarmaStep.Group => CreationKarmaCopy.Groups,
        CreationKarmaStep.Review => CreationKarmaCopy.Review,
        _ => CreationKarmaCopy.Title
    };

    protected override void OnAppearing()
    {
        _body.IsEnabled = false;
        if (!_body.Children.Contains(_loading)) _body.Children.Insert(0, _loading);
        if (!_body.Children.Contains(_loadingText)) _body.Children.Insert(1, _loadingText);
        _loading.IsRunning = true;
        base.OnAppearing();
    }

    protected override void OnDisappearing()
    {
        _body.IsEnabled = false;
        _render++;
        base.OnDisappearing();
    }

    protected override async Task PrepareForAppearanceRefreshAsync(CancellationToken cancellationToken)
    {
        long appearance = CaptureAppearanceGeneration();
        bool Current() => IsCurrentAppearanceGeneration(appearance) && _session.FrameCurrent;
        MarkVisitedStage();
        await _session.ReloadAsync(_step is CreationKarmaStep.Skills or CreationKarmaStep.Skill or CreationKarmaStep.Group,
            cancellationToken, Current);
        if (!Current() || !_session.Ready) return;
        MarkVisitedStage();
        if (!_session.QuoteCurrent) await _session.PreviewAsync(cancellationToken, Current);

        void MarkVisitedStage()
        {
            if (!Current() || !_session.Ready) return;
            if (_step == CreationKarmaStep.Attributes && _session.Selection is { TalentOptionId: not null, Attributes: null } attributes)
                _session.Change(attributes with { Attributes = [] });
            if (_step == CreationKarmaStep.Skills && _session.Selection is { Attributes: not null, Skills: null } skills)
                _session.Change(skills with { Skills = new([], []) });
        }
    }

    protected override void Refresh()
    {
        _render++;
        _loading.IsRunning = false;
        _body.Clear();
        _body.IsEnabled = _session.FrameCurrent;
        _body.Add(NativeTheme.Title(Title));
        if (!_session.FrameCurrent)
        { _body.Add(NativeTheme.Body(CreationKarmaCopy.Stale, NativeTheme.Danger)); return; }
        if (_step is CreationKarmaStep.Overview or CreationKarmaStep.Review)
            _body.Add(NativeTheme.Body(CreationKarmaCopy.Scope, NativeTheme.Muted));
        if (_session.Saved)
        {
            var saved = NativeTheme.Body(CreationKarmaCopy.Saved);
            saved.AutomationId = "creation-karma-saved";
            _body.Add(saved);
        }
        if (!_session.Ready)
        {
            _body.Add(NativeTheme.Body(CreationKarmaCopy.Stale, NativeTheme.Danger));
            AddBlockers();
            return;
        }
        var state = _session.Authority!;
        var binding = NativeTheme.Body(CreationKarmaCopy.Binding(state.Binding.ContentRevision, state.Binding.SavedRevision), NativeTheme.Muted);
        binding.AutomationId = "creation-karma-binding";
        _body.Add(binding);
        _status = NativeTheme.Body(_session.QuoteCurrent && _session.Quote is { } quote
            ? CreationKarmaCopy.Budget(quote.KarmaBudget.Used, quote.KarmaBudget.Total, quote.KarmaBudget.Remaining)
            : _session.Selection is null ? CreationKarmaCopy.Choose : CreationKarmaCopy.Pending, NativeTheme.Muted);
        _status.AutomationId = "creation-karma-budget";
        _body.Add(_status);
        switch (_step)
        {
            case CreationKarmaStep.Overview: AddOverview(); break;
            case CreationKarmaStep.Metatype: AddMetatypes(); break;
            case CreationKarmaStep.Talent: AddTalents(); break;
            case CreationKarmaStep.Attributes: AddAttributes(); break;
            case CreationKarmaStep.Skills: AddSkills(); break;
            case CreationKarmaStep.Skill: AddSkillEditor(); break;
            case CreationKarmaStep.Group: AddGroupEditor(); break;
            case CreationKarmaStep.Review: AddReview(); break;
        }
        AddBlockers();
    }

    private bool Current(long render, long appearance) => render == _render
        && IsCurrentAppearanceGeneration(appearance) && _session.Ready;

    private void AddButton(string text, string id, Func<Task> action, bool enabled = true)
    {
        long render = _render, appearance = CaptureAppearanceGeneration();
        var button = NativeTheme.SecondaryButton(text);
        button.AutomationId = id;
        button.IsEnabled = enabled;
        button.Clicked += async (_, _) => await RunAsync(async () =>
        {
            // Let the Android callback unwind before replacing any native control.
            await Task.Yield();
            if (Current(render, appearance) && enabled) await action();
        });
        _body.Add(button);
    }

    private Task Open(CreationKarmaStep step, string? sourceId = null, string? instanceId = null, string? sourceKind = null)
        => Navigation.PushAsync(new CreationKarmaPage(Coordinator, _session, step, sourceId, instanceId, sourceKind));

    private void Change(CreationKarmaPhoneSelection selection)
    {
        _session.Change(selection);
        if (_status is not null) _status.Text = CreationKarmaCopy.Pending;
    }

    private async Task Preview()
    {
        long appearance = CaptureAppearanceGeneration();
        _body.IsEnabled = false;
        await _session.PreviewAsync(default, () => IsCurrentAppearanceGeneration(appearance));
    }

    private void AddOverview()
    {
        var selection = _session.Selection;
        AddButton(CreationKarmaCopy.Metatype, "karma-open-metatype", () => Open(CreationKarmaStep.Metatype));
        AddButton(CreationKarmaCopy.Talent, "karma-open-talent", () => Open(CreationKarmaStep.Talent), selection is not null);
        AddButton(CreationKarmaCopy.Attributes, "karma-open-attributes", () => Open(CreationKarmaStep.Attributes), selection?.TalentOptionId is not null);
        AddButton(CreationKarmaCopy.Skills, "karma-open-skills", () => Open(CreationKarmaStep.Skills), selection?.Attributes is not null);
        AddButton(CreationKarmaCopy.Review, "karma-open-review", () => Open(CreationKarmaStep.Review), selection is not null);
        AddSelectionSummary();
    }

    private void AddMetatypes()
    {
        foreach (var option in _session.Authority!.Options)
        {
            _body.Add(NativeTheme.Body(CreationKarmaCopy.Cost(option.Label, option.KarmaCost)));
            AddButton(option.Label, "karma-metatype-" + option.OptionId, async () =>
            {
                Change((_session.Selection ?? new(option.OptionId)) with { MetatypeOptionId = option.OptionId });
                await Navigation.PopAsync();
            }, option.IsEnabled && option.Blockers.Count == 0);
        }
    }

    private void AddTalents()
    {
        if (_session.Selection is not { } selection || _session.Authority!.Talents is not { } talents) return;
        foreach (var option in talents.Options)
        {
            _body.Add(NativeTheme.Body(CreationKarmaCopy.Cost(option.Name, option.KarmaCost)));
            AddButton(option.Name, "karma-talent-" + option.OptionId, async () =>
            {
                Change(selection with { TalentOptionId = option.OptionId });
                await Navigation.PopAsync();
            }, option.IsEnabled && option.Blockers.Count == 0);
        }
    }

    private void AddLevels(string name, string id, int value, int max, Action<int> changed, bool enabled = true)
    {
        long render = _render, appearance = CaptureAppearanceGeneration();
        var label = NativeTheme.Body(CreationKarmaCopy.Levels(name, value));
        label.AutomationId = id + "-value";
        var stepper = new Stepper { Minimum = 0, Maximum = Math.Max(1, Math.Max(value, max)), Increment = 1,
            Value = value, AutomationId = id, IsEnabled = enabled && (max > 0 || value > 0) };
        stepper.ValueChanged += (_, args) =>
        {
            if (!Current(render, appearance) || !stepper.IsEnabled) return;
            int next = checked((int)args.NewValue);
            changed(next);
            label.Text = CreationKarmaCopy.Levels(name, next);
        };
        _body.Add(NativeTheme.Card(new VerticalStackLayout { Children = { label, stepper } }));
    }

    private void AddAttributes()
    {
        if (_session.Selection?.Attributes is null || _session.Quote?.Attributes is not { } attributes) return;
        _body.Add(NativeTheme.Body(CreationKarmaCopy.LevelHelp, NativeTheme.Muted));
        foreach (var attribute in attributes.Attributes)
        {
            int current = _session.Selection.Attributes.SingleOrDefault(a => a.AttributeId == attribute.AttributeId)?.KarmaLevels ?? 0;
            AddLevels(CreationAllocationStrings.AttributeName(attribute.AttributeId), "karma-attribute-" + attribute.AttributeId,
                current, attribute.IsEnabled ? attribute.Maximum - attribute.Minimum : 0,
                next => Change(_session.Selection! with
                {
                    Attributes = _session.Selection!.Attributes!.Where(a => a.AttributeId != attribute.AttributeId)
                        .Concat(next > 0 ? [new CharacterCreationKarmaAttributeAllocation(attribute.AttributeId, next)] : []).ToArray()
                }));
        }
        AddButton(CreationKarmaCopy.Preview, "karma-preview-attributes", Preview);
    }

    private void AddSkills()
    {
        if (_session.Selection?.Skills is not { } skills || _session.Authority!.SkillsCatalog is not { } catalog) return;
        if (_session.Access is { RequiredUnlockChoices.Count: > 0 } access)
            foreach (string choice in access.RequiredUnlockChoices)
                AddButton(choice, "karma-unlock-" + choice, async () =>
                { Change(_session.Selection! with { Skills = skills with { TalentUnlock = choice } }); await Preview(); });
        if (_session.QuoteCurrent && _session.Quote?.Skills is { } quote)
            _body.Add(NativeTheme.Body(CreationKarmaCopy.KnowledgeBudget(quote.KnowledgePointsUsed, quote.KnowledgePointsTotal)));
        foreach (var allocation in skills.Skills)
        {
            var source = catalog.ActiveSkills.Concat(catalog.KnowledgeSkills).Single(s => s.SourceSkillId == allocation.SourceSkillId && s.Kind == allocation.Kind);
            string name = source.Name;
            if (allocation.SpecializationOptionId is { } spec)
                name += " · " + source.Specializations.Single(s => s.OptionId == spec).Name;
            AddButton(name, "karma-selected-skill-" + source.Kind + "-" + source.SourceSkillId + "-" + allocation.SpecializationOptionId,
                () => Open(CreationKarmaStep.Skill, source.SourceSkillId, allocation.SpecializationOptionId, source.Kind));
        }
        foreach (var group in skills.Groups)
        {
            var source = catalog.SkillGroups.Single(g => g.GroupId == group.GroupId);
            AddButton(source.Name, "karma-selected-group-" + source.GroupId, () => Open(CreationKarmaStep.Group, source.GroupId));
        }
        AddButton(CreationKarmaCopy.Active, "karma-filter-active", () => Filter(CharacterCreationSkillKinds.Active));
        AddButton(CreationKarmaCopy.KnowledgeSkills, "karma-filter-knowledge", () => Filter(CharacterCreationSkillKinds.Knowledge));
        AddButton(CreationKarmaCopy.Groups, "karma-filter-groups", () => Filter("groups"));
        var search = new SearchBar { Placeholder = CreationKarmaCopy.Search, Text = _search, AutomationId = "karma-skill-search" };
        long render = _render, appearance = CaptureAppearanceGeneration();
        search.TextChanged += (_, args) => { if (Current(render, appearance)) _search = args.NewTextValue ?? string.Empty; };
        _body.Add(search);
        AddButton(CreationKarmaCopy.Search, "karma-search", () => { _page = 0; return Task.CompletedTask; });
        bool Match(string name) => name.Contains(_search, StringComparison.CurrentCultureIgnoreCase);
        var rows = _category == "groups"
            ? catalog.SkillGroups.Where(s => Match(s.Name)).Select(s => (Id: s.GroupId, s.Name, Kind: "groups",
                Allowed: _session.Access?.AllowedSkillGroupIds.Contains(s.GroupId) == true)).ToArray()
            : (_category == CharacterCreationSkillKinds.Active ? catalog.ActiveSkills : catalog.KnowledgeSkills)
                .Where(s => Match(s.Name)).Select(s => (Id: s.SourceSkillId, s.Name, s.Kind,
                    Allowed: _session.Access?.IsReady == true && (s.Kind == CharacterCreationSkillKinds.Knowledge
                        || _session.Access.AllowedActiveSkillSourceIds.Contains(s.SourceSkillId)))).ToArray();
        _page = Math.Min(_page, Math.Max(0, (rows.Length - 1) / PageSize));
        foreach (var row in rows.Skip(_page * PageSize).Take(PageSize))
            AddButton(row.Name, "karma-catalog-" + row.Kind + "-" + row.Id,
                () => Open(row.Kind == "groups" ? CreationKarmaStep.Group : CreationKarmaStep.Skill, row.Id, sourceKind: row.Kind), row.Allowed);
        AddButton(CreationKarmaCopy.Previous, "karma-list-previous", () => { _page--; return Task.CompletedTask; }, _page > 0);
        AddButton(CreationKarmaCopy.Next, "karma-list-next", () => { _page++; return Task.CompletedTask; }, (_page + 1) * PageSize < rows.Length);
        AddButton(CreationKarmaCopy.Preview, "karma-preview-skills", Preview);
        Task Filter(string category) { _category = category; _page = 0; return Task.CompletedTask; }
    }

    private void AddSkillEditor()
    {
        if (_session.Selection?.Skills is not { } selection || _session.Authority!.SkillsCatalog is not { } catalog
            || _session.Authority.SkillsPolicy is not { } policy) return;
        var source = catalog.ActiveSkills.Concat(catalog.KnowledgeSkills).SingleOrDefault(s => s.SourceSkillId == _sourceId && s.Kind == _sourceKind);
        if (source is null) return;
        var original = selection.Skills.SingleOrDefault(s => s.SourceSkillId == _sourceId && s.Kind == _sourceKind
            && (!source.IsExotic || s.SpecializationOptionId == _instanceId));
        if (!_skillInitialized)
        { _editingSkill = original ?? new(source.SourceSkillId, source.Kind, 0); _skillInitialized = true; }
        var edit = _editingSkill!;
        _body.Add(NativeTheme.Title(source.Name));
        _body.Add(NativeTheme.Body(CreationKarmaCopy.SkillHelp, NativeTheme.Muted));
        int maximum = source.Kind == CharacterCreationSkillKinds.Active ? policy.MaxActiveSkillRatingCreate : policy.MaxKnowledgeSkillRatingCreate;
        bool allowed = _session.Access?.IsReady == true && (source.Kind == CharacterCreationSkillKinds.Knowledge
            || _session.Access.AllowedActiveSkillSourceIds.Contains(source.SourceSkillId));
        AddLevels(CreationKarmaCopy.KarmaLevels, "karma-skill-levels", edit.KarmaLevels, maximum,
            value => _editingSkill = _editingSkill! with { KarmaLevels = value });
        if (source.Kind == CharacterCreationSkillKinds.Knowledge)
            AddLevels(CreationKarmaCopy.KnowledgeLevels, "karma-knowledge-levels", edit.KnowledgePointLevels, maximum,
                value => _editingSkill = _editingSkill! with { KnowledgePointLevels = value });
        long render = _render, appearance = CaptureAppearanceGeneration();
        if (source.CanBeNativeLanguage)
        {
            var native = new Switch { IsToggled = edit.IsNativeLanguage, AutomationId = "karma-native-language" };
            native.Toggled += (_, args) => { if (Current(render, appearance)) _editingSkill = _editingSkill! with { IsNativeLanguage = args.Value }; };
            _body.Add(NativeTheme.Body(CreationKarmaCopy.NativeLanguage));
            _body.Add(native);
        }
        var specs = source.Specializations.ToArray();
        var picker = new Picker { Title = CreationKarmaCopy.Specialization,
            ItemsSource = new[] { CreationKarmaCopy.None }.Concat(specs.Select(s => s.Name)).ToArray(),
            SelectedIndex = Array.FindIndex(specs, s => s.OptionId == edit.SpecializationOptionId) + 1,
            AutomationId = "karma-specialization" };
        picker.SelectedIndexChanged += (_, _) =>
        {
            if (!Current(render, appearance)) return;
            string? id = picker.SelectedIndex > 0 ? specs[picker.SelectedIndex - 1].OptionId : null;
            _editingSkill = _editingSkill! with { SpecializationOptionId = id,
                SpecializationPayment = source.IsExotic && id is not null ? CharacterCreationKarmaSpecializationPayments.ExoticIdentity
                    : id is null ? null : _editingSkill.SpecializationPayment };
        };
        _body.Add(picker);
        if (!source.IsExotic)
        {
            string?[] payments = source.Kind == CharacterCreationSkillKinds.Knowledge
                ? [null, CharacterCreationKarmaSpecializationPayments.Karma, CharacterCreationKarmaSpecializationPayments.KnowledgePoint]
                : [null, CharacterCreationKarmaSpecializationPayments.Karma];
            var payment = new Picker { Title = CreationKarmaCopy.Payment,
                ItemsSource = payments.Select(p => p is null ? CreationKarmaCopy.Choose
                    : p == CharacterCreationKarmaSpecializationPayments.Karma ? CreationKarmaCopy.Karma : CreationKarmaCopy.Knowledge).ToArray(),
                SelectedIndex = Array.IndexOf(payments, edit.SpecializationPayment), AutomationId = "karma-spec-payment" };
            payment.SelectedIndexChanged += (_, _) =>
            { if (Current(render, appearance) && payment.SelectedIndex >= 0) _editingSkill = _editingSkill! with { SpecializationPayment = payments[payment.SelectedIndex] }; };
            _body.Add(payment);
        }
        AddButton(CreationKarmaCopy.UseSelection, "karma-use-skill", async () =>
        {
            Change(_session.Selection! with { Skills = selection with
                { Skills = selection.Skills.Where(s => s != original).Append(_editingSkill!).ToArray() } });
            await Navigation.PopAsync();
        }, allowed);
        AddButton(CreationKarmaCopy.Remove, "karma-remove-skill", async () =>
        {
            Change(_session.Selection! with { Skills = selection with { Skills = selection.Skills.Where(s => s != original).ToArray() } });
            await Navigation.PopAsync();
        }, original is not null);
    }

    private void AddGroupEditor()
    {
        if (_session.Selection?.Skills is not { } selection || _session.Authority!.SkillsCatalog is not { } catalog) return;
        var source = catalog.SkillGroups.SingleOrDefault(g => g.GroupId == _sourceId);
        if (source is null) return;
        _body.Add(NativeTheme.Title(source.Name));
        foreach (string member in source.MemberSkillSourceIds)
            _body.Add(NativeTheme.Body(catalog.ActiveSkills.Single(s => s.SourceSkillId == member).Name));
        int levels = selection.Groups.SingleOrDefault(g => g.GroupId == source.GroupId)?.KarmaLevels ?? 0;
        AddLevels(CreationKarmaCopy.KarmaLevels, "karma-group-levels", levels, _session.Authority.SkillsPolicy!.MaxSkillGroupRatingCreate,
            next => Change(_session.Selection! with { Skills = _session.Selection!.Skills! with
                { Groups = _session.Selection.Skills.Groups.Where(g => g.GroupId != source.GroupId)
                    .Concat(next > 0 ? [new CharacterCreationKarmaSkillGroupAllocation(source.GroupId, next)] : []).ToArray() } }),
            _session.Access?.AllowedSkillGroupIds.Contains(source.GroupId) == true || levels > 0);
        AddButton(CreationKarmaCopy.Preview, "karma-preview-group", Preview);
    }

    private void AddSelectionSummary()
    {
        if (!_session.QuoteCurrent || _session.Quote is not { } quote) return;
        _body.Add(NativeTheme.Body(CreationKarmaCopy.Cost(quote.Metatype.Label, quote.Metatype.KarmaCost)));
        if (quote.Talent is { } talent) _body.Add(NativeTheme.Body(CreationKarmaCopy.Cost(talent.Name, talent.KarmaCost)));
        foreach (var attribute in quote.Attributes?.Attributes ?? [])
            _body.Add(NativeTheme.Body(CreationKarmaCopy.ValueCost(CreationAllocationStrings.AttributeName(attribute.AttributeId),
                attribute.Current, attribute.KarmaCost)));
        if (quote.Skills is { } skills)
        {
            _body.Add(NativeTheme.Body(CreationKarmaCopy.KnowledgeBudget(skills.KnowledgePointsUsed, skills.KnowledgePointsTotal)));
            foreach (var skill in skills.Skills)
            {
                string name = skill.Name;
                if (skill.Allocation.SpecializationOptionId is { } spec)
                {
                    var source = skills.Basis.Catalog.ActiveSkills.Concat(skills.Basis.Catalog.KnowledgeSkills)
                        .Single(s => s.SourceSkillId == skill.Allocation.SourceSkillId && s.Kind == skill.Allocation.Kind);
                    name += " · " + source.Specializations.Single(s => s.OptionId == spec).Name;
                }
                _body.Add(NativeTheme.Body(CreationKarmaCopy.ValueCost(name,
                    skill.Rating?.ToString(CultureInfo.CurrentCulture) ?? CreationKarmaCopy.NativeLanguage, skill.KarmaCost)));
                if (skill.KnowledgePointCost > 0)
                    _body.Add(NativeTheme.Body(CreationKarmaCopy.KnowledgeBudget(skill.KnowledgePointCost, skills.KnowledgePointsTotal)));
            }
        }
        foreach (var group in quote.Skills?.Groups ?? [])
            _body.Add(NativeTheme.Body(CreationKarmaCopy.ValueCost(group.Name, group.Allocation.KarmaLevels, group.KarmaCost)));
    }

    private void AddReview()
    {
        AddSelectionSummary();
        if (_session.QuoteCurrent && _session.Quote is { } quote)
        {
            foreach (string anchor in quote.SourceAnchorIds) _body.Add(NativeTheme.Body(anchor, NativeTheme.Muted));
            AddButton(CreationKarmaCopy.Confirm, "karma-confirm", async () =>
            {
                long appearance = CaptureAppearanceGeneration();
                _body.IsEnabled = false;
                await _session.ConfirmAsync(default, () => IsCurrentAppearanceGeneration(appearance));
            }, quote.CanSelect && !_session.Saved);
        }
    }

    private void AddBlockers()
    {
        foreach (string blocker in _session.Blockers)
        {
            var label = NativeTheme.Body(blocker, NativeTheme.Danger);
            label.AutomationId = "karma-blocker-" + blocker;
            _body.Add(label);
        }
    }
}
