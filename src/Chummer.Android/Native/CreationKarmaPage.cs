using System.Globalization;
using Chummer.Contracts.Characters;

namespace Chummer.Android.Native;

internal enum CreationKarmaStep { Overview, Metatype, Talent, Attributes, Qualities, Skills, Skill, Group, Resources, Gear, Contacts, Contact, Review }

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
    private string? _resourceInput;
    private CharacterCreationKarmaContactSelection? _editingContact;

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
        CreationKarmaStep.Qualities => CreationKarmaCopy.Qualities,
        CreationKarmaStep.Skills or CreationKarmaStep.Skill => CreationKarmaCopy.Skills,
        CreationKarmaStep.Group => CreationKarmaCopy.Groups,
        CreationKarmaStep.Resources => CreationKarmaCopy.Resources,
        CreationKarmaStep.Gear => CreationKarmaCopy.Gear,
        CreationKarmaStep.Contacts or CreationKarmaStep.Contact => CreationKarmaCopy.Contacts,
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
            cancellationToken, Current, includeQualities: _step == CreationKarmaStep.Qualities,
            includeGear: _step == CreationKarmaStep.Gear);
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
            if (_step == CreationKarmaStep.Qualities && _session.Authority?.QualitiesCatalog is not null
                && _session.Selection is { Attributes: not null, QualityOptionIds: null } qualities)
                _session.Change(qualities with { QualityOptionIds = [] });
            if (_step == CreationKarmaStep.Gear && _session.Authority?.GearAuthority is not null
                && _session.Selection is { ResourceKarmaInvestment: not null, GearSelections: null } gear)
                _session.Change(gear with { GearSelections = [] });
            if (_step == CreationKarmaStep.Contacts && _session.Authority?.ContactsPolicy is not null
                && _session.Selection is { Attributes: not null, Skills: not null, QualityOptionIds: not null,
                    ContactSelections: null } contacts)
                _session.Change(contacts with { ContactSelections = [] });
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
        _status = NativeTheme.Body(_session.QuoteCurrent && _session.Quote is { KarmaBudget.IsExact: true } quote
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
            case CreationKarmaStep.Qualities: AddQualities(); break;
            case CreationKarmaStep.Skills: AddSkills(); break;
            case CreationKarmaStep.Skill: AddSkillEditor(); break;
            case CreationKarmaStep.Group: AddGroupEditor(); break;
            case CreationKarmaStep.Resources: AddResources(); break;
            case CreationKarmaStep.Gear: AddGear(); break;
            case CreationKarmaStep.Contacts: AddContacts(); break;
            case CreationKarmaStep.Contact: AddContactEditor(); break;
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
        AddButton(CreationKarmaCopy.Qualities, "karma-open-qualities", () => Open(CreationKarmaStep.Qualities), selection?.Attributes is not null);
        AddButton(CreationKarmaCopy.Skills, "karma-open-skills", () => Open(CreationKarmaStep.Skills), selection?.Attributes is not null);
        AddButton(CreationKarmaCopy.Resources, "karma-open-resources", () => Open(CreationKarmaStep.Resources),
            selection?.Attributes is not null && _session.Authority?.ResourcesPolicy is not null);
        AddButton(CreationKarmaCopy.Gear, "karma-open-gear", () => Open(CreationKarmaStep.Gear),
            selection?.ResourceKarmaInvestment is not null);
        AddButton(CreationKarmaCopy.Contacts, "karma-open-contacts", () => Open(CreationKarmaStep.Contacts),
            selection is { Attributes: not null, Skills: not null, QualityOptionIds: not null }
                && _session.Authority?.ContactsPolicy is not null);
        AddButton(CreationKarmaCopy.Review, "karma-open-review", () => Open(CreationKarmaStep.Review), selection is not null);
        AddCompletionButton();
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

    private void AddQualities()
    {
        if (_session.Selection is not { Attributes: not null, QualityOptionIds: { } selected }
            || _session.Authority?.QualitiesCatalog is not { } catalog) return;
        _body.Add(NativeTheme.Body(CreationKarmaCopy.QualityHelp, NativeTheme.Muted));
        AddQualityTotals();
        // Selected rows stay available for removal even when another choice has
        // made the combined draft invalid. No UI-side eligibility calculation.
        foreach (string id in selected)
        {
            var option = catalog.Options.Single(item => item.OptionId == id);
            _body.Add(NativeTheme.Body(CreationKarmaCopy.QualitySourceCost(option.Name, option.Rating, option.KarmaCost)));
            AddButton(CreationKarmaCopy.Remove, "karma-remove-quality-" + id, async () =>
            {
                Change(_session.Selection! with { QualityOptionIds = _session.Selection!.QualityOptionIds!.Where(item => item != id).ToArray() });
                await Preview();
            });
        }
        var search = new SearchBar { Placeholder = CreationKarmaCopy.Search, Text = _search, AutomationId = "karma-quality-search" };
        long render = _render, appearance = CaptureAppearanceGeneration();
        search.TextChanged += (_, args) => { if (Current(render, appearance)) _search = args.NewTextValue ?? string.Empty; };
        _body.Add(search);
        AddButton(CreationKarmaCopy.Search, "karma-quality-search-go", () => { _page = 0; return Task.CompletedTask; });
        var rows = catalog.Options.Where(item => item.Name.Contains(_search, StringComparison.CurrentCultureIgnoreCase)).ToArray();
        _page = Math.Min(_page, Math.Max(0, (rows.Length - 1) / PageSize));
        foreach (var option in rows.Skip(_page * PageSize).Take(PageSize))
        {
            _body.Add(NativeTheme.Body(CreationKarmaCopy.QualitySourceCost(option.Name, option.Rating, option.KarmaCost)));
            if (!option.IsSelectable)
                _body.Add(NativeTheme.Body(CreationKarmaCopy.QualityUnavailable + " · " + option.DisableReasonKey, NativeTheme.Muted));
            foreach (string anchor in option.SourceAnchorIds) _body.Add(NativeTheme.Body(anchor, NativeTheme.Muted));
            bool alreadySelected = selected.Contains(option.OptionId, StringComparer.Ordinal);
            AddButton(alreadySelected ? CreationKarmaCopy.Selected : CreationKarmaCopy.UseSelection,
                "karma-add-quality-" + option.OptionId, async () =>
                {
                    // Changing a rating replaces that source choice, never buys
                    // a second instance or accumulates an earlier cost/credit.
                    var retained = _session.Selection!.QualityOptionIds!.Where(id =>
                        catalog.Options.Single(item => item.OptionId == id).SelectionKey != option.SelectionKey);
                    Change(_session.Selection with { QualityOptionIds = retained.Append(option.OptionId).ToArray() });
                    await Preview();
                }, option.IsSelectable && !alreadySelected);
        }
        AddButton(CreationKarmaCopy.Previous, "karma-quality-previous", () => { _page--; return Task.CompletedTask; }, _page > 0);
        AddButton(CreationKarmaCopy.Next, "karma-quality-next", () => { _page++; return Task.CompletedTask; }, (_page + 1) * PageSize < rows.Length);
    }

    private void AddQualityTotals()
    {
        if (!_session.QuoteCurrent || _session.Quote?.Qualities is not { } qualities) return;
        var totals = NativeTheme.Body(CreationKarmaCopy.QualityTotals(qualities.Costs.PositiveKarmaSpent,
            qualities.Costs.NegativeKarmaGranted, qualities.Costs.NetKarmaSpent));
        totals.AutomationId = "karma-quality-totals";
        _body.Add(totals);
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
        AddQualityTotals();
        foreach (var quality in quote.Qualities?.Selections ?? [])
            _body.Add(NativeTheme.Body(CreationKarmaCopy.QualitySourceCost(quality.Name, quality.Rating, quality.KarmaCost)));
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
        if (quote.Resources is { } resources)
        {
            var funding = NativeTheme.Body(CreationKarmaCopy.ResourceFunding(resources.KarmaInvestment, resources.NuyenFromKarma));
            funding.AutomationId = "karma-resource-funding";
            _body.Add(funding);
        }
        AddGearTotals();
        foreach (var line in quote.Gear?.Lines ?? [])
            _body.Add(NativeTheme.Body(CreationKarmaCopy.GearLine(line.Name, line.Quantity, line.TotalCost)));
        AddContactTotals();
        foreach (var line in quote.Contacts?.Lines ?? [])
            _body.Add(NativeTheme.Body(CreationKarmaCopy.ContactLine(line.Selection.Identity.Name,
                line.Selection.Connection, line.Selection.Loyalty, line.PointCost)));
    }

    private void AddContactTotals()
    {
        if (!_session.QuoteCurrent || _session.Quote?.Contacts is not { } contacts) return;
        var totals = NativeTheme.Body(CreationKarmaCopy.ContactTotals(contacts.ContactPointsUsed, contacts.ContactPoints,
            contacts.HighPlacesPointsUsed, contacts.HighPlacesPoints, contacts.KarmaUsed));
        totals.AutomationId = "karma-contact-totals";
        _body.Add(totals);
        if (contacts.GroupContactKarma > 0)
            _body.Add(NativeTheme.Body(CreationKarmaCopy.GroupContactCost(contacts.GroupContactKarma,
                contacts.CombinedQualityCosts.PositiveLimitKarma)));
    }

    private void AddContacts()
    {
        if (_session.Selection?.ContactSelections is not { } contacts) return;
        _body.Add(NativeTheme.Body(CreationKarmaCopy.ContactHelp, NativeTheme.Muted));
        AddContactTotals();
        foreach (var contact in contacts)
            AddButton(contact.Identity.Name.Length == 0 ? CreationKarmaCopy.UnnamedContact : contact.Identity.Name,
                "karma-contact-" + contact.ContactId.ToString("D"),
                () => Open(CreationKarmaStep.Contact, contact.ContactId.ToString("D")));
        AddButton(CreationKarmaCopy.AddContact, "karma-add-contact", () => Open(CreationKarmaStep.Contact),
            contacts.Count < Chummer.Application.Characters.CharacterCreationKarmaContactsRules.MaximumSelections);
        AddButton(CreationKarmaCopy.Preview, "karma-preview-contacts", Preview);
    }

    private void AddContactEditor()
    {
        if (_session.Selection?.ContactSelections is not { } contacts) return;
        var original = contacts.SingleOrDefault(item => item.ContactId.ToString("D") == _sourceId);
        // A missing retained identity must not become an accidental new contact.
        if (_sourceId is not null && original is null) return;
        _editingContact ??= original ?? new(Guid.NewGuid(),
            new CharacterCreationContactIdentity("", "", "", "", "", "", "", "", "", "", "", "", ""), 1, 1);
        long render = _render, appearance = CaptureAppearanceGeneration();
        AddText(CreationKarmaCopy.ContactName, "name", _editingContact.Identity.Name,
            value => _editingContact = _editingContact! with { Identity = _editingContact.Identity with { Name = value } });
        AddText(CreationKarmaCopy.ContactRole, "role", _editingContact.Identity.Role,
            value => _editingContact = _editingContact! with { Identity = _editingContact.Identity with { Role = value } });
        AddText(CreationKarmaCopy.ContactLocation, "location", _editingContact.Identity.Location,
            value => _editingContact = _editingContact! with { Identity = _editingContact.Identity with { Location = value } });
        AddText(CreationKarmaCopy.ContactNotes, "notes", _editingContact.Identity.Notes,
            value => _editingContact = _editingContact! with { Identity = _editingContact.Identity with { Notes = value } });
        AddRating(CreationKarmaCopy.Connection, "connection", _editingContact.Connection, 12,
            value => _editingContact = _editingContact! with { Connection = value });
        AddRating(CreationKarmaCopy.Loyalty, "loyalty", _editingContact.Loyalty, 6,
            value => _editingContact = _editingContact! with { Loyalty = value });
        AddFlag(CreationKarmaCopy.GroupContact, "group", _editingContact.IsGroup,
            value => _editingContact = _editingContact! with { IsGroup = value });
        AddFlag(CreationKarmaCopy.FamilyContact, "family", _editingContact.Family,
            value => _editingContact = _editingContact! with { Family = value });
        AddFlag(CreationKarmaCopy.BlackmailContact, "blackmail", _editingContact.Blackmail,
            value => _editingContact = _editingContact! with { Blackmail = value });
        _body.Add(NativeTheme.Body(CreationKarmaCopy.ContactHelp, NativeTheme.Muted));
        AddButton(CreationKarmaCopy.UseSelection, "karma-use-contact", async () =>
        {
            Change(_session.Selection! with { ContactSelections = contacts.Where(item => item != original)
                .Append(_editingContact!).ToArray() });
            await Navigation.PopAsync();
        });
        AddButton(CreationKarmaCopy.Remove, "karma-remove-contact", async () =>
        {
            Change(_session.Selection! with { ContactSelections = contacts.Where(item => item != original).ToArray() });
            await Navigation.PopAsync();
        }, original is not null);

        void AddText(string label, string id, string value, Action<string> changed)
        {
            _body.Add(NativeTheme.Body(label));
            var entry = new Entry { Text = value, MaxLength = 32767, AutomationId = "karma-contact-" + id };
            entry.TextChanged += (_, args) =>
            { if (Current(render, appearance)) changed((args.NewTextValue ?? string.Empty).Trim()); };
            _body.Add(entry);
        }
        void AddRating(string label, string id, int value, int maximum, Action<int> changed)
        {
            var text = NativeTheme.Body(CreationKarmaCopy.Levels(label, value));
            var stepper = new Stepper { Minimum = 1, Maximum = maximum, Increment = 1, Value = value,
                AutomationId = "karma-contact-" + id };
            stepper.ValueChanged += (_, args) =>
            {
                if (!Current(render, appearance)) return;
                changed((int)args.NewValue);
                text.Text = CreationKarmaCopy.Levels(label, (int)args.NewValue);
            };
            _body.Add(text); _body.Add(stepper);
        }
        void AddFlag(string label, string id, bool value, Action<bool> changed)
        {
            var toggle = new Switch { IsToggled = value, AutomationId = "karma-contact-" + id };
            toggle.Toggled += (_, args) => { if (Current(render, appearance)) changed(args.Value); };
            _body.Add(NativeTheme.Body(label)); _body.Add(toggle);
        }
    }

    private void AddGearTotals()
    {
        if (!_session.QuoteCurrent || _session.Quote?.Gear?.Budget is not { } budget) return;
        var totals = NativeTheme.Body(CreationKarmaCopy.GearTotals(budget.TotalStartingNuyen, budget.BasketCost,
            budget.RemainingNuyen, budget.Overspend));
        totals.AutomationId = "karma-gear-totals";
        _body.Add(totals);
    }

    private void AddGear()
    {
        if (_session.Authority?.GearAuthority is not { } authority)
        { _body.Add(NativeTheme.Body(CreationKarmaCopy.GearUnavailable, NativeTheme.Danger)); return; }
        if (_session.Selection is not { GearSelections: { } selections }) return;
        _body.Add(NativeTheme.Body(CreationKarmaCopy.GearHelp, NativeTheme.Muted));
        AddGearTotals();
        foreach (var selected in selections)
        {
            var option = authority.Options.Single(item => item.OptionId == selected.OptionId);
            AddLevels(option.Name, "karma-gear-quantity-" + option.OptionId, selected.Quantity,
                authority.MaximumQuantityPerLine, quantity => Change(_session.Selection! with
                {
                    GearSelections = _session.Selection!.GearSelections!.Where(item => item.OptionId != option.OptionId)
                        .Concat(quantity > 0 ? [new CharacterCreationGearSelection(option.OptionId, quantity)] : []).ToArray()
                }));
            AddButton(CreationKarmaCopy.Remove, "karma-remove-gear-" + option.OptionId, async () =>
            {
                Change(_session.Selection! with { GearSelections = _session.Selection!.GearSelections!
                    .Where(item => item.OptionId != option.OptionId).ToArray() });
                await Preview();
            });
        }
        AddButton(CreationKarmaCopy.Preview, "karma-preview-gear", Preview);
        var search = new SearchBar { Placeholder = CreationKarmaCopy.Search, Text = _search, AutomationId = "karma-gear-search" };
        long render = _render, appearance = CaptureAppearanceGeneration();
        search.TextChanged += (_, args) => { if (Current(render, appearance)) _search = args.NewTextValue ?? string.Empty; };
        _body.Add(search);
        AddButton(CreationKarmaCopy.Search, "karma-gear-search-go", () => { _page = 0; return Task.CompletedTask; });
        var rows = authority.Options.Where(item => item.Name.Contains(_search, StringComparison.CurrentCultureIgnoreCase)).ToArray();
        _page = Math.Min(_page, Math.Max(0, (rows.Length - 1) / PageSize));
        foreach (var option in rows.Skip(_page * PageSize).Take(PageSize))
        {
            _body.Add(NativeTheme.Body(CreationKarmaCopy.GearLine(option.Name, option.PackageQuantity, option.PackageCost)));
            _body.Add(NativeTheme.Body(option.SourceBook + " · " + option.Page, NativeTheme.Muted));
            if (!option.IsSelectable)
                _body.Add(NativeTheme.Body(CreationKarmaCopy.GearUnavailable + " · " + string.Join(", ", option.Blockers), NativeTheme.Muted));
            bool selected = selections.Any(item => item.OptionId == option.OptionId);
            AddButton(selected ? CreationKarmaCopy.Selected : CreationKarmaCopy.UseSelection,
                "karma-add-gear-" + option.OptionId, async () =>
                {
                    Change(_session.Selection! with { GearSelections = _session.Selection!.GearSelections!
                        .Append(new CharacterCreationGearSelection(option.OptionId, option.PackageQuantity)).ToArray() });
                    await Preview();
                }, option.IsSelectable && !selected);
        }
        AddButton(CreationKarmaCopy.Previous, "karma-gear-previous", () => { _page--; return Task.CompletedTask; }, _page > 0);
        AddButton(CreationKarmaCopy.Next, "karma-gear-next", () => { _page++; return Task.CompletedTask; }, (_page + 1) * PageSize < rows.Length);
    }

    private void AddResources()
    {
        if (_session.Selection is not { Attributes: not null } selection
            || _session.Authority?.ResourcesPolicy is not { } policy) return;
        _body.Add(NativeTheme.Body(CreationKarmaCopy.ResourceHelp, NativeTheme.Muted));
        _body.Add(NativeTheme.Body(CreationKarmaCopy.ResourceLimit(policy.MaximumKarmaInvestment)));
        _body.Add(NativeTheme.Body(policy.FundingExpression, NativeTheme.Muted));
        _body.Add(NativeTheme.Body(CreationKarmaCopy.ResourceInvestment));
        long render = _render, appearance = CaptureAppearanceGeneration();
        _resourceInput ??= (selection.ResourceKarmaInvestment ?? 0m).ToString(CultureInfo.CurrentCulture);
        var input = new Entry { Text = _resourceInput, Keyboard = Keyboard.Numeric,
            AutomationId = "karma-resource-investment" };
        var invalid = NativeTheme.Body(CreationKarmaCopy.InvalidNumber, NativeTheme.Danger);
        invalid.AutomationId = "karma-resource-invalid";
        var use = NativeTheme.SecondaryButton(CreationKarmaCopy.UseSelection);
        use.AutomationId = "karma-use-resources";
        void Validate()
        {
            bool valid = TryInvestment(input.Text, out _);
            use.IsEnabled = valid;
            invalid.IsVisible = !valid;
        }
        input.TextChanged += (_, _) =>
        {
            if (!Current(render, appearance)) return;
            _resourceInput = input.Text;
            Validate();
        };
        use.Clicked += async (_, _) => await RunAsync(async () =>
        {
            await Task.Yield();
            if (!Current(render, appearance) || !TryInvestment(input.Text, out decimal investment)) return;
            Change(selection with { ResourceKarmaInvestment = investment });
            await Navigation.PopAsync();
        });
        Validate();
        _body.Add(input);
        _body.Add(invalid);
        _body.Add(use);
        if (_session.QuoteCurrent && _session.Quote?.Resources is { } resources)
            _body.Add(NativeTheme.Body(CreationKarmaCopy.ResourceFunding(resources.KarmaInvestment, resources.NuyenFromKarma)));

        static bool TryInvestment(string? text, out decimal value) => decimal.TryParse(text,
            NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite | NumberStyles.AllowDecimalPoint,
            CultureInfo.CurrentCulture, out value);
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
                if (_session.Saved && _session.Ready && IsCurrentAppearanceGeneration(appearance))
                    await _session.PreviewAsync(default, () => IsCurrentAppearanceGeneration(appearance));
            }, quote.CanSelect && !_session.Saved);
        }
        AddCompletionButton();
    }

    private void AddCompletionButton()
        => AddButton(CreationKarmaCopy.Finish, "karma-open-completion",
            () => Navigation.PushAsync(new CreationKarmaCompletionPage(Coordinator, _session.Quote!)), _session.CanFinalize);

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
