using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;
using System.Globalization;

namespace Chummer.Android.Native;

/// <summary>SR6 pending choices only. Core owns all legality, budgets and writes.</summary>
internal sealed partial class Sr6CreationFoundationPage : NativePageBase
{
    private readonly VerticalStackLayout _body = new() { Padding = new Thickness(20, 18, 20, 40), Spacing = 14 };
    private readonly OwnerContextStamp? _owner;
    private readonly CharacterWorkspaceId? _workspace;
    private readonly bool _attributesMode;
    private readonly bool _skillsMode;
    private readonly bool _knowledgeMode;
    private readonly bool _talentMode;
    private readonly bool _formsMode;
    private readonly bool _spellsMode;
    private readonly bool _powersMode;
    private readonly bool _karmaMode;
    private readonly bool _qualitiesMode;
    private Sr6CreationFoundationState? _state;
    private Sr6CreationFoundationSelection? _selection;
    private Sr6CreationFoundationPreview? _preview;
    private Sr6CreationFoundationCommit? _commit;
    private IReadOnlyList<string> _blockers = [];
    private long _render;
    private bool _busy, _halted, _hasUnconfirmedChanges;
    private Label? _status;
    private Button? _confirm;
    private Button? _attributes;
    private Button? _skills;
    private Button? _knowledge;
    private Button? _talent;
    private Button? _forms;
    private Button? _spells;
    private Button? _powers;
    private Button? _karma;
    private Button? _qualities;
    private VerticalStackLayout? _review;

    internal Sr6CreationFoundationPage(RunnerSessionCoordinator coordinator, bool attributesMode = false,
        bool skillsMode = false, bool knowledgeMode = false, bool talentMode = false, bool formsMode = false, bool spellsMode = false,
        bool powersMode = false, bool karmaMode = false, bool qualitiesMode = false) : base(coordinator)
    {
        if (new[] { attributesMode, skillsMode, knowledgeMode, talentMode, formsMode, spellsMode, powersMode, karmaMode, qualitiesMode }.Count(mode => mode) > 1)
            throw new ArgumentException("Select one SR6 allocation page.");
        _attributesMode = attributesMode;
        _skillsMode = skillsMode;
        _knowledgeMode = knowledgeMode;
        _talentMode = talentMode;
        _formsMode = formsMode;
        _spellsMode = spellsMode;
        _powersMode = powersMode;
        _karmaMode = karmaMode;
        _qualitiesMode = qualitiesMode;
        _owner = coordinator.State.DisplayOwnerContext;
        _workspace = coordinator.State.WorkspaceId;
        Title = Sr6CreationCopy.Text(qualitiesMode ? "QualitiesTitle" : karmaMode ? "KarmaTitle" : powersMode ? "PowersTitle" : spellsMode ? "SpellsTitle" : formsMode ? "FormsTitle" : talentMode ? "TalentTitle" : knowledgeMode ? "KnowledgeTitle" : skillsMode ? "SkillTitle" : attributesMode ? "AttributeTitle" : "Title");
        AutomationId = qualitiesMode ? "sr6-qualities-page" : karmaMode ? "sr6-karma-page" : powersMode ? "sr6-powers-page" : spellsMode ? "sr6-spells-page" : formsMode ? "sr6-forms-page" : talentMode ? "sr6-talent-page" : knowledgeMode ? "sr6-knowledge-page" : skillsMode ? "sr6-skills-page" : attributesMode ? "sr6-attributes-page" : "sr6-foundation-page";
        Content = new ScrollView { Content = _body };
    }

    private bool FrameCurrent => Coordinator.State.WorkspaceId == _workspace
        && Coordinator.State.DisplayOwnerContext == _owner && Coordinator.State.Session.OwnerContext == _owner
        && Coordinator.CanOpenSr6Foundation();
    private bool Ready => !_halted && FrameCurrent && _state is { } state && Coordinator.IsSr6FoundationStateCurrent(state);

    protected override void OnAppearing()
    {
        _body.IsEnabled = false;
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
        bool Current() => FrameCurrent && IsCurrentAppearanceGeneration(appearance);
        if (_halted || !Current()) return;
        var previous = _state;
        var result = await Coordinator.LoadSr6FoundationAsync(cancellationToken, Current);
        if (!Current()) return;
        _preview = null;
        if (result.Value is not { } state)
        {
            _blockers = result.Blockers;
            _state = null;
            // Do not lose the editing baseline and later adopt a new revision
            // for the still-unconfirmed inputs after a transient load failure.
            if (_hasUnconfirmedChanges) _halted = true;
            return;
        }
        if (previous is not null && previous.Binding != state.Binding && _hasUnconfirmedChanges)
        { _halted = true; _blockers = [Sr6CreationFoundationBlockers.StaleBinding]; return; }
        _state = state;
        if (!_hasUnconfirmedChanges)
        {
            // A child may have saved a newer revision while this page was away.
            // Replace clean inputs from the newly issued Core state, never rebase
            // unsaved edits or carry a historical confirmation across appearances.
            _selection = state.Selection is { } saved
                ? saved.Selection with { Assignments = saved.Selection.Assignments.ToArray() } : null;
            _commit = null;
        }
        _blockers = [];
    }

    protected override void Refresh()
    {
        long render = ++_render;
        long appearance = CaptureAppearanceGeneration();
        _body.Clear();
        _attributes = null;
        _skills = null;
        _knowledge = null;
        _talent = null;
        _forms = null;
        _spells = null;
        _powers = null;
        _karma = null;
        _qualities = null;
        _body.IsEnabled = !_busy;
        _body.Add(NativeTheme.Title(Title));
        _body.Add(NativeTheme.Body(Sr6CreationCopy.Text("Scope"), NativeTheme.Muted));
        _status = NativeTheme.Body(string.Join("\n", _blockers.Select(Sr6CreationCopy.Blocker)), NativeTheme.Danger);
        _status.AutomationId = "sr6-foundation-status";
        _body.Add(_status);
        if (_commit is { } committed && FrameCurrent && Coordinator.CanDisplaySr6FoundationCommit(committed))
            _body.Add(NativeTheme.Body(Sr6CreationCopy.Text(_state is null ? "SavedReopen" : "Saved")));
        if (!Ready || _state is not { } state)
        {
            _body.Add(NativeTheme.Body(Sr6CreationCopy.Text("Stale"), NativeTheme.Muted));
            return;
        }
        bool Current() => !_busy && render == _render && IsCurrentAppearanceGeneration(appearance) && Ready;
        _body.Add(NativeTheme.Body(state.BuildMethod + " · " + CreationKarmaCopy.Binding(
            state.Binding.ContentRevision, state.Binding.SavedRevision), NativeTheme.Muted));
        _selection ??= state.PointBuyLimits is not null
            ? new("", "", []) { PointBuy = new(0, 0, 0, 0) }
            : new("", "", CharacterCreationPriorityCategoryIds.Ordered.Select(id => new Sr6CreationPriorityChoice(id, "")).ToArray());

        if (_qualitiesMode)
        {
            if (state.Selection is null || state.QualityOptions is not { Count: > 0 } catalog)
            { _body.Add(NativeTheme.Body(Sr6CreationCopy.Text("FoundationRequired"))); return; }
            BuildQualityEditor(catalog, Current, Change);
        }
        else if (_karmaMode)
        {
            if (state.KarmaOptions is not { } options)
            { _body.Add(NativeTheme.Body(Sr6CreationCopy.Text("KarmaAllocationsRequired"))); return; }
            BuildKarmaEditor(options, Current, Change);
        }
        else if (_powersMode)
        {
            if (state.Selection?.TalentAllocation is not { } budget || state.AdeptPowerOptions is not { Count: > 0 } catalog)
            { _body.Add(NativeTheme.Body(Sr6CreationCopy.Text("PowersTalentRequired"))); return; }
            _body.Add(NativeTheme.Body(Sr6CreationCopy.Text("TalentSavedBudget"), NativeTheme.Muted));
            _body.Add(NativeTheme.Body(Sr6CreationCopy.TalentBudget(budget)));
            BuildPowerEditor(catalog, Current, Change);
        }
        else if (_spellsMode)
        {
            if (state.Selection?.TalentAllocation is not { } budget || state.SpellOptions is not { Count: > 0 } catalog)
            { _body.Add(NativeTheme.Body(Sr6CreationCopy.Text("SpellsTalentRequired"))); return; }
            _body.Add(NativeTheme.Body(Sr6CreationCopy.Text("TalentSavedBudget"), NativeTheme.Muted));
            _body.Add(NativeTheme.Body(Sr6CreationCopy.TalentBudget(budget)));
            if (state.Selection.PointBuy is { } savedPoints)
                _body.Add(NativeTheme.Body(Sr6CreationCopy.PointBuyBudget(savedPoints)));
            BuildSpellEditor(catalog, Current, Change);
        }
        else if (_formsMode)
        {
            if (state.Selection?.TalentAllocation is not { } budget || state.ComplexFormOptions is not { Count: > 0 } catalog)
            { _body.Add(NativeTheme.Body(Sr6CreationCopy.Text("FormsTalentRequired"))); return; }
            _body.Add(NativeTheme.Body(Sr6CreationCopy.TalentBudget(budget)));
            if (state.Selection.PointBuy is { } savedPoints)
                _body.Add(NativeTheme.Body(Sr6CreationCopy.PointBuyBudget(savedPoints)));
            BuildComplexFormEditor(catalog, Current, Change);
        }
        else if (_talentMode)
        {
            if (state.Selection is not { } savedFoundation || state.TalentOptions is not { } options)
            { _body.Add(NativeTheme.Body(Sr6CreationCopy.Text("TalentAttributesRequired"))); return; }
            _selection = _selection with { TalentAllocation = _selection.TalentAllocation ?? new(0) };
            _body.Add(NativeTheme.Body(Sr6CreationCopy.Budget(savedFoundation)));
            _body.Add(NativeTheme.Body(Sr6CreationCopy.Text("TalentHelp"), NativeTheme.Muted));
            _body.Add(NativeTheme.Body(Sr6CreationCopy.TalentOptions(options)));
            if (savedFoundation.TalentAllocation is { } savedTalent)
            {
                _body.Add(NativeTheme.Body(Sr6CreationCopy.Text("TalentSavedBudget"), NativeTheme.Muted));
                _body.Add(NativeTheme.Body(Sr6CreationCopy.TalentBudget(savedTalent)));
                if (savedFoundation.PointBuy is { } savedPoints)
                    _body.Add(NativeTheme.Body(Sr6CreationCopy.PointBuyBudget(savedPoints)));
            }
            if (options.RequiresAspect)
                AddPicker("talent-aspect", Sr6CreationCopy.Text("SkillAspect"), ["Sorcery", "Conjuring", "Enchanting"],
                    _selection.Skills?.AspectedSkillId ?? "", Sr6CreationCopy.Label,
                    value => Change(_selection with { Skills = (_selection.Skills ?? new([])) with { AspectedSkillId = value } }));
            if (options.MaximumSelectedPowerPoints > 0)
                AddPicker("talent-power-points", Sr6CreationCopy.Text("TalentPowerPoints"),
                    Enumerable.Range(0, options.MaximumSelectedPowerPoints + 1).Select(value => value.ToString(CultureInfo.InvariantCulture)).ToArray(),
                    _selection.TalentAllocation.SelectedPowerPoints.ToString(CultureInfo.InvariantCulture), value => value,
                    value => Change(_selection with { TalentAllocation = new(int.Parse(value, CultureInfo.InvariantCulture)) }));
        }
        else if (_knowledgeMode)
        {
            if (state.Selection?.Attributes is null || state.KnowledgePointBudget is not { } knowledgeBudget)
            { _body.Add(NativeTheme.Body(Sr6CreationCopy.Text("KnowledgeAttributesRequired"))); return; }
            BuildKnowledgeEditor(knowledgeBudget, Current, Change);
        }
        else if (_skillsMode)
        {
            if (state.Selection is not { } savedFoundation || state.SkillOptions is not { Count: 19 } options)
            { _body.Add(NativeTheme.Body(Sr6CreationCopy.Text("FoundationRequired"))); return; }
            _selection = _selection with { Skills = _selection.Skills ?? new([]) };
            _body.Add(NativeTheme.Body(Sr6CreationCopy.Budget(savedFoundation)));
            _body.Add(NativeTheme.Body(Sr6CreationCopy.Text("SkillHelp"), NativeTheme.Muted));
            if (_selection.TalentId == "aspected-magician")
                AddPicker("skill-aspect", Sr6CreationCopy.Text("SkillAspect"), ["Sorcery", "Conjuring", "Enchanting"],
                    _selection.Skills.AspectedSkillId ?? "", Sr6CreationCopy.Label,
                    value => Change(_selection with { Skills = _selection.Skills with { AspectedSkillId = value } }));
            foreach (var option in options)
            {
                string id = option.SkillId;
                bool selectableAspect = _selection.TalentId == "aspected-magician"
                    && id is "Sorcery" or "Conjuring" or "Enchanting";
                if (!option.Available && !selectableAspect)
                {
                    _body.Add(NativeTheme.Body(Sr6CreationCopy.Label(id) + " · "
                        + Sr6CreationCopy.Blocker(option.UnavailableReason!), NativeTheme.Muted));
                    continue;
                }
                var row = _selection.Skills.Allocations.SingleOrDefault(item => item.SkillId == id) ?? new(id, 0, []);
                AddPicker("skill-" + id, Sr6CreationCopy.Label(id),
                    Enumerable.Range(0, option.Maximum + 1).Select(value => value.ToString(CultureInfo.InvariantCulture)).ToArray(),
                    row.Rating.ToString(CultureInfo.InvariantCulture), value => value,
                    value => ChangeSkill(id, old => old with { Rating = int.Parse(value, CultureInfo.InvariantCulture) }));
                _body.Add(NativeTheme.Body(Sr6CreationCopy.Text(id == "ExoticWeapons" ? "SkillExoticSpecializations" : "SkillSpecialization")));
                var entry = new Entry { AutomationId = "sr6-foundation-specialization-" + id,
                    Text = string.Join("; ", row.Specializations), MaxLength = 972 };
                entry.TextChanged += (_, _) =>
                {
                    if (!Current()) return;
                    string[] names = (entry.Text ?? "").Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                    ChangeSkill(id, old => old with { Specializations = names });
                };
                _body.Add(entry);
            }
        }
        else if (_attributesMode)
        {
            if (state.Selection is not { } savedFoundation || state.AttributeOptions is not { Count: 11 } options)
            { _body.Add(NativeTheme.Body(Sr6CreationCopy.Text("FoundationRequired"))); return; }
            _body.Add(NativeTheme.Body(Sr6CreationCopy.Budget(savedFoundation)));
            _body.Add(NativeTheme.Body(Sr6CreationCopy.Text("AttributeHelp"), NativeTheme.Muted));
            _selection = _selection with { Attributes = _selection.Attributes ?? new(
                options.Select(option => new Sr6CreationAttributeSpend(option.AttributeId, 0, 0)).ToArray()) };
            foreach (var option in options.Where(option => option.Maximum > 0))
            {
                _body.Add(NativeTheme.Body(Sr6CreationCopy.AttributeRange(option)));
                if (option.AllowsAttributePoints) AddSpendPicker(option, false, savedFoundation.Budget.AttributePoints);
                if (option.AllowsAdjustmentPoints) AddSpendPicker(option, true, savedFoundation.Budget.MetatypeAdjustmentPoints);
            }
        }
        else
        {
            if (state.Selection is { } saved)
            {
                bool SavedCurrent() => Current() && Sr6CreationFoundationIntegrity.Digest(_selection)
                    == Sr6CreationFoundationIntegrity.Digest(saved.Selection);
                var qualities = NativeTheme.PrimaryButton(Sr6CreationCopy.Text("QualitiesTitle"));
                _qualities = qualities;
                qualities.AutomationId = "sr6-foundation-qualities";
                qualities.IsEnabled = SavedCurrent();
                qualities.Clicked += async (_, _) =>
                {
                    if (!SavedCurrent()) return;
                    await RunAsync(async () =>
                    {
                        if (SavedCurrent()) await Navigation.PushAsync(new Sr6CreationFoundationPage(Coordinator, qualitiesMode: true));
                    });
                };
                _body.Add(qualities);
                var attributes = NativeTheme.PrimaryButton(Sr6CreationCopy.Text("AttributeOpen"));
                _attributes = attributes;
                attributes.AutomationId = "sr6-foundation-attributes";
                attributes.IsEnabled = SavedCurrent();
                attributes.Clicked += async (_, _) =>
                {
                    if (!SavedCurrent()) return;
                    await RunAsync(async () =>
                    {
                        if (SavedCurrent()) await Navigation.PushAsync(new Sr6CreationFoundationPage(Coordinator, attributesMode: true));
                    });
                };
                _body.Add(attributes);
                var skills = NativeTheme.PrimaryButton(Sr6CreationCopy.Text("SkillOpen"));
                _skills = skills;
                skills.AutomationId = "sr6-foundation-skills";
                skills.IsEnabled = SavedCurrent();
                skills.Clicked += async (_, _) =>
                {
                    if (!SavedCurrent()) return;
                    await RunAsync(async () =>
                    {
                        if (SavedCurrent()) await Navigation.PushAsync(new Sr6CreationFoundationPage(Coordinator, skillsMode: true));
                    });
                };
                _body.Add(skills);
                var knowledge = NativeTheme.PrimaryButton(Sr6CreationCopy.Text("KnowledgeOpen"));
                _knowledge = knowledge;
                knowledge.AutomationId = "sr6-foundation-knowledge";
                knowledge.IsEnabled = SavedCurrent() && saved.Attributes is not null;
                knowledge.Clicked += async (_, _) =>
                {
                    if (!SavedCurrent() || saved.Attributes is null) return;
                    await RunAsync(async () =>
                    {
                        if (SavedCurrent()) await Navigation.PushAsync(new Sr6CreationFoundationPage(Coordinator, knowledgeMode: true));
                    });
                };
                _body.Add(knowledge);
                var talent = NativeTheme.PrimaryButton(Sr6CreationCopy.Text("TalentOpen"));
                _talent = talent;
                talent.AutomationId = "sr6-foundation-talent-budget";
                talent.IsEnabled = SavedCurrent() && saved.Attributes is not null;
                talent.Clicked += async (_, _) =>
                {
                    if (!SavedCurrent() || saved.Attributes is null) return;
                    await RunAsync(async () =>
                    {
                        if (SavedCurrent()) await Navigation.PushAsync(new Sr6CreationFoundationPage(Coordinator, talentMode: true));
                    });
                };
                _body.Add(talent);
                if (saved.Selection.TalentId == "technomancer")
                {
                    var forms = NativeTheme.PrimaryButton(Sr6CreationCopy.Text("FormsTitle"));
                    _forms = forms;
                    forms.AutomationId = "sr6-foundation-complex-forms";
                    forms.IsEnabled = SavedCurrent() && state.ComplexFormOptions is { Count: > 0 };
                    forms.Clicked += async (_, _) =>
                    {
                        if (!SavedCurrent() || state.ComplexFormOptions is not { Count: > 0 }) return;
                        await RunAsync(async () =>
                        {
                            if (SavedCurrent()) await Navigation.PushAsync(new Sr6CreationFoundationPage(Coordinator, formsMode: true));
                        });
                    };
                    _body.Add(forms);
                    if (state.ComplexFormOptions is null)
                        _body.Add(NativeTheme.Body(Sr6CreationCopy.Text("FormsTalentRequired"), NativeTheme.Muted));
                }
                if (saved.Selection.TalentId is "magician" or "mystic-adept" or "aspected-magician")
                {
                    var spells = NativeTheme.PrimaryButton(Sr6CreationCopy.Text("SpellsTitle"));
                    _spells = spells;
                    spells.AutomationId = "sr6-foundation-spells";
                    spells.IsEnabled = SavedCurrent() && state.SpellOptions is { Count: > 0 };
                    spells.Clicked += async (_, _) =>
                    {
                        if (!SavedCurrent() || state.SpellOptions is not { Count: > 0 }) return;
                        await RunAsync(async () =>
                        {
                            if (SavedCurrent()) await Navigation.PushAsync(new Sr6CreationFoundationPage(Coordinator, spellsMode: true));
                        });
                    };
                    _body.Add(spells);
                    if (state.SpellOptions is null)
                        _body.Add(NativeTheme.Body(Sr6CreationCopy.Text("SpellsTalentRequired"), NativeTheme.Muted));
                }
                if (saved.Selection.TalentId is "adept" or "mystic-adept")
                {
                    var powers = NativeTheme.PrimaryButton(Sr6CreationCopy.Text("PowersTitle"));
                    _powers = powers;
                    powers.AutomationId = "sr6-foundation-powers";
                    powers.IsEnabled = SavedCurrent() && state.AdeptPowerOptions is { Count: > 0 };
                    powers.Clicked += async (_, _) =>
                    {
                        if (!SavedCurrent() || state.AdeptPowerOptions is not { Count: > 0 }) return;
                        await RunAsync(async () =>
                        {
                            if (SavedCurrent()) await Navigation.PushAsync(new Sr6CreationFoundationPage(Coordinator, powersMode: true));
                        });
                    };
                    _body.Add(powers);
                    if (state.AdeptPowerOptions is null)
                        _body.Add(NativeTheme.Body(Sr6CreationCopy.Text("PowersTalentRequired"), NativeTheme.Muted));
                }
                var karma = NativeTheme.PrimaryButton(Sr6CreationCopy.Text("KarmaTitle"));
                _karma = karma;
                karma.AutomationId = "sr6-foundation-karma";
                karma.IsEnabled = SavedCurrent() && state.KarmaOptions is not null;
                karma.Clicked += async (_, _) =>
                {
                    if (!SavedCurrent() || state.KarmaOptions is null) return;
                    await RunAsync(async () =>
                    {
                        if (SavedCurrent()) await Navigation.PushAsync(new Sr6CreationFoundationPage(Coordinator, karmaMode: true));
                    });
                };
                _body.Add(karma);
                if (state.KarmaOptions is null)
                    _body.Add(NativeTheme.Body(Sr6CreationCopy.Text("KarmaAllocationsRequired"), NativeTheme.Muted));
                if (saved.Attributes is null)
                    _body.Add(NativeTheme.Body(Sr6CreationCopy.Text("KnowledgeAttributesRequired"), NativeTheme.Muted));
                _body.Add(NativeTheme.Body(Sr6CreationCopy.Text("AttributeResetWarning"), NativeTheme.Muted));
            }
            if (state.PointBuyLimits is { } limits)
            {
                _body.Add(NativeTheme.Body(Sr6CreationCopy.PointBuyLimits(limits)));
                _body.Add(NativeTheme.Body(Sr6CreationCopy.Text("PointBuyHelp"), NativeTheme.Muted));
                var purchase = _selection.PointBuy!;
                AddPoolPicker("attributes", "PointBuyAttributes", purchase.AdditionalAttributePoints,
                    limits.MaximumAdditionalAttributePoints, value => _selection.PointBuy! with { AdditionalAttributePoints = value });
                AddPoolPicker("skills", "PointBuySkills", purchase.AdditionalSkillPoints,
                    limits.MaximumAdditionalSkillPoints, value => _selection.PointBuy! with { AdditionalSkillPoints = value });
                AddPoolPicker("adjustment", "PointBuyAdjustment", purchase.AdditionalAdjustmentPoints,
                    limits.MaximumAdditionalAdjustmentPoints, value => _selection.PointBuy! with { AdditionalAdjustmentPoints = value });
                AddPoolPicker("resources", "PointBuyResources", purchase.ResourceUnits,
                    limits.MaximumResourceUnits, value => _selection.PointBuy! with { ResourceUnits = value });
            }
            else foreach (string category in CharacterCreationPriorityCategoryIds.Ordered)
            {
                string captured = category;
                AddPicker("rank-" + category, Sr6CreationCopy.Label(category), ["A", "B", "C", "D", "E"],
                    _selection.Assignments.Single(item => item.CategoryId == category).Rank,
                    id => id, value => Change(_selection with { Assignments = _selection.Assignments.Select(item =>
                        item.CategoryId == captured ? item with { Rank = value } : item).ToArray() }));
            }
            AddPicker("metatype", Sr6CreationCopy.Text("Metatype"), state.Metatypes.Select(item => item.Id).ToArray(),
                _selection.MetatypeId, Sr6CreationCopy.Label, value => Change(_selection with { MetatypeId = value }));
            AddPicker("talent", Sr6CreationCopy.Text("Talent"), state.Talents.Select(item => item.Id).ToArray(),
                _selection.TalentId, Sr6CreationCopy.Label, value => Change(_selection with { TalentId = value }));
        }

        var previewButton = NativeTheme.PrimaryButton(Sr6CreationCopy.Text("Preview"));
        previewButton.AutomationId = "sr6-foundation-preview";
        previewButton.Clicked += async (_, _) =>
        {
            if (!Current()) return;
            await RunAsync(async () =>
            {
                if (!Current()) return;
                _busy = true;
                _body.IsEnabled = false;
                try
                {
                    var result = await Coordinator.PreviewSr6FoundationAsync(state, _selection,
                        isCurrentPage: () => render == _render && IsCurrentAppearanceGeneration(appearance) && FrameCurrent);
                    if (render != _render || !IsCurrentAppearanceGeneration(appearance) || !FrameCurrent) return;
                    _preview = result.Value;
                    _blockers = result.Blockers;
                }
                finally { _busy = false; }
            });
        };
        _body.Add(previewButton);
        _review = new VerticalStackLayout { Spacing = 8, AutomationId = "sr6-foundation-review" };
        _body.Add(_review);
        if (_preview is { } quote && Coordinator.IsSr6FoundationPreviewCurrent(quote))
        {
            _review.Add(NativeTheme.Body(Sr6CreationCopy.Budget(quote)));
            if (quote.Qualities is { } qualities)
            {
                _review.Add(NativeTheme.Body(Sr6CreationCopy.QualityBudget(qualities)));
                foreach (var quality in qualities.Values)
                    _review.Add(NativeTheme.Body(Sr6CreationCopy.QualityValue(quality)));
            }
            if (quote.PointBuy is { } pointBuy)
            {
                _review.Add(NativeTheme.Body(Sr6CreationCopy.PointBuyBudget(pointBuy)));
                _review.Add(NativeTheme.Body(Sr6CreationCopy.Text("PointBuyHelp"), NativeTheme.Muted));
            }
            if (quote.TalentAllocation is { } talent)
            {
                _review.Add(NativeTheme.Body(Sr6CreationCopy.TalentBudget(talent)));
                if (_talentMode)
                    _review.Add(NativeTheme.Body(Sr6CreationCopy.Text("TalentHelp"), NativeTheme.Muted));
            }
            if (quote.AdeptPowers is { } powers)
            {
                _review.Add(NativeTheme.Body(Sr6CreationCopy.PowersBudget(powers)));
                foreach (var power in powers.Powers)
                    _review.Add(NativeTheme.Body(Sr6CreationCopy.PowerValue(power)));
                foreach (string warning in powers.WarningIds)
                    _review.Add(NativeTheme.Body(Sr6CreationCopy.Text("PowersWarning." + warning), NativeTheme.Muted));
            }
            if (quote.Spells is { } spells)
            {
                _review.Add(NativeTheme.Body(Sr6CreationCopy.SpellsBudget(spells)));
                _review.Add(NativeTheme.Body(Sr6CreationCopy.Text("SpellsUse." + spells.UseId), NativeTheme.Muted));
                foreach (var spell in spells.Spells)
                    _review.Add(NativeTheme.Body(Sr6CreationCopy.SpellName(spell) + " · " + spell.SourceAnchorId));
            }
            if (quote.ComplexForms is { } forms)
            {
                _review.Add(NativeTheme.Body(Sr6CreationCopy.FormsBudget(forms)));
                foreach (var form in forms.Forms)
                    _review.Add(NativeTheme.Body(form.SourceName + (form.Choice.Subject is null ? "" : " — " + form.Choice.Subject)
                        + " · " + form.SourceAnchorId));
                if (forms.Forms.Any(form => form.SubjectNeedsGmReview))
                    _review.Add(NativeTheme.Body(Sr6CreationCopy.Text("FormsSubjectReview"), NativeTheme.Muted));
            }
            if (quote.Attributes is { } allocation)
            {
                _review.Add(NativeTheme.Body(Sr6CreationCopy.AttributeBudget(allocation)));
                foreach (var value in allocation.Values.Where(value => value.Maximum > 0))
                    _review.Add(NativeTheme.Body(Sr6CreationCopy.AttributeValue(value)));
            }
            if (quote.Skills is { } skills)
            {
                _review.Add(NativeTheme.Body(Sr6CreationCopy.SkillBudget(skills)));
                foreach (var value in skills.Values.Where(value => value.Rating > 0))
                    _review.Add(NativeTheme.Body(Sr6CreationCopy.SkillValue(value)));
                if (skills.SpecializationsNeedGmReview)
                    _review.Add(NativeTheme.Body(Sr6CreationCopy.Text("SkillGmReview"), NativeTheme.Muted));
            }
            if (quote.Karma is { } karma)
            {
                _review.Add(NativeTheme.Body(Sr6CreationCopy.KarmaBudget(karma)));
                foreach (var value in karma.Attributes.Concat(karma.Skills))
                    _review.Add(NativeTheme.Body(Sr6CreationCopy.KarmaValue(value)));
                foreach (var value in karma.Specializations ?? [])
                    _review.Add(NativeTheme.Body(Sr6CreationCopy.KarmaSpecializationValue(value)));
                if (karma.Knowledge is { } purchases)
                {
                    foreach (var value in purchases.KnowledgeSkills)
                        _review.Add(NativeTheme.Body(Sr6CreationCopy.KarmaKnowledgeValue(value)));
                    foreach (var value in purchases.Languages)
                        _review.Add(NativeTheme.Body(Sr6CreationCopy.KarmaLanguageValue(value)));
                    if (purchases.KnowledgeSkills.Count > 0)
                        _review.Add(NativeTheme.Body(Sr6CreationCopy.Text("KnowledgeGmReview"), NativeTheme.Muted));
                }
                if (karma.Skills.Any(row => row.FirstExoticSpecialization is not null)
                    || karma.Specializations?.Any(row => row.NeedsGmReview) == true)
                    _review.Add(NativeTheme.Body(Sr6CreationCopy.Text("SkillGmReview"), NativeTheme.Muted));
            }
            if (quote.Knowledge is { } knowledge)
            {
                _review.Add(NativeTheme.Body(Sr6CreationCopy.KnowledgeBudget(knowledge)));
                _review.Add(NativeTheme.Body(Sr6CreationCopy.Text("KnowledgeNative") + ": " + knowledge.NativeLanguage));
                foreach (var topic in knowledge.KnowledgeSkills)
                    _review.Add(NativeTheme.Body(topic.Name));
                foreach (var language in knowledge.Languages)
                    _review.Add(NativeTheme.Body(Sr6CreationCopy.LanguageValue(language)));
                if (knowledge.TopicsNeedGmReview)
                    _review.Add(NativeTheme.Body(Sr6CreationCopy.Text("KnowledgeGmReview"), NativeTheme.Muted));
            }
            _review.Add(NativeTheme.Body(string.Join(" · ", quote.SourceAnchorIds), NativeTheme.Muted));
            _confirm = NativeTheme.PrimaryButton(Sr6CreationCopy.Text("Confirm"));
            _confirm.AutomationId = "sr6-foundation-confirm";
            _confirm.Clicked += async (_, _) =>
            {
                if (!Current() || !ReferenceEquals(_preview, quote)) return;
                await RunAsync(async () =>
                {
                    if (!Current() || !ReferenceEquals(_preview, quote)) return;
                    _busy = true;
                    _body.IsEnabled = false;
                    try
                    {
                        var result = await Coordinator.ConfirmSr6FoundationAsync(quote, true,
                            isCurrentPage: () => IsCurrentAppearanceGeneration(appearance) && FrameCurrent);
                        if (!IsCurrentAppearanceGeneration(appearance) || !FrameCurrent) return;
                        _preview = null;
                        _blockers = result.Blockers;
                        _commit = result.Commit;
                        _state = result.State;
                        if (result.Commit is not null && result.State?.Selection is { } saved)
                        {
                            _selection = saved.Selection with { Assignments = saved.Selection.Assignments.ToArray() };
                            _hasUnconfirmedChanges = false;
                        }
                        // Unknown outcomes are resolved by reopening, never a new automatic write.
                        _halted = result.State is null;
                    }
                    finally { _busy = false; }
                });
            };
            _review.Add(_confirm);
        }

        void Change(Sr6CreationFoundationSelection selection)
        {
            if (!Current()) return;
            bool foundationMode = !_attributesMode && !_skillsMode && !_knowledgeMode && !_talentMode && !_formsMode && !_spellsMode && !_powersMode && !_karmaMode && !_qualitiesMode;
            bool cleared = foundationMode && (_selection.Attributes is not null || _selection.Skills is not null || _selection.Knowledge is not null || _selection.TalentAllocation is not null || _selection.ComplexForms is not null || _selection.Spells is not null || _selection.AdeptPowers is not null || _selection.Karma is not null || _selection.Qualities is not null);
            if (foundationMode) selection = selection with { Attributes = null, Skills = null, Knowledge = null, TalentAllocation = null, ComplexForms = null, Spells = null, AdeptPowers = null, Karma = null, Qualities = null };
            _selection = selection;
            _hasUnconfirmedChanges = true;
            _preview = null;
            _commit = null;
            _blockers = [];
            if (_confirm is not null) _confirm.IsEnabled = false;
            if (_attributes is not null) _attributes.IsEnabled = false;
            if (_skills is not null) _skills.IsEnabled = false;
            if (_knowledge is not null) _knowledge.IsEnabled = false;
            if (_talent is not null) _talent.IsEnabled = false;
            if (_forms is not null) _forms.IsEnabled = false;
            if (_spells is not null) _spells.IsEnabled = false;
            if (_powers is not null) _powers.IsEnabled = false;
            if (_karma is not null) _karma.IsEnabled = false;
            if (_qualities is not null) _qualities.IsEnabled = false;
            _review?.Clear();
            _status.Text = Sr6CreationCopy.Text(cleared ? "AttributeResetWarning" : "Changed");
            // Do not rebuild the Picker visual tree inside SelectedIndexChanged.
        }

        void ChangeSkill(string id, Func<Sr6CreationSkillSpend, Sr6CreationSkillSpend> update)
        {
            if (!Current() || _selection.Skills is not { } skills) return;
            var old = skills.Allocations.SingleOrDefault(row => row.SkillId == id) ?? new(id, 0, []);
            Change(_selection with { Skills = skills with { Allocations =
                [.. skills.Allocations.Where(row => row.SkillId != id), update(old)] } });
        }

        void AddSpendPicker(Sr6CreationAttributeOption option, bool adjustment, int budget)
        {
            string id = option.AttributeId;
            var spend = _selection.Attributes!.Allocations.Single(row => row.AttributeId == id);
            int selected = adjustment ? spend.AdjustmentPoints : spend.AttributePoints;
            var options = Enumerable.Range(0, Math.Min(option.Maximum - option.BaseValue, budget) + 1)
                .Select(value => value.ToString(CultureInfo.InvariantCulture)).ToArray();
            AddPicker((adjustment ? "adjustment-" : "normal-") + id,
                Sr6CreationCopy.Text(adjustment ? "AdjustmentPoints" : "AttributePoints"), options,
                selected.ToString(CultureInfo.InvariantCulture), value => value, value =>
                {
                    int points = int.Parse(value, CultureInfo.InvariantCulture);
                    Change(_selection with { Attributes = new(_selection.Attributes!.Allocations.Select(row => row.AttributeId != id ? row
                        : adjustment ? row with { AdjustmentPoints = points } : row with { AttributePoints = points }).ToArray()) });
                });
        }

        void AddPoolPicker(string id, string title, int selected, int maximum, Func<int, Sr6CreationPointBuySelection> update)
            => AddPicker("point-buy-" + id, Sr6CreationCopy.Text(title),
                Enumerable.Range(0, maximum + 1).Select(value => value.ToString(CultureInfo.InvariantCulture)).ToArray(),
                selected.ToString(CultureInfo.InvariantCulture), value => value,
                value => Change(_selection with { PointBuy = update(int.Parse(value, CultureInfo.InvariantCulture)) }));

        void AddPicker(string id, string title, string[] options, string selected,
            Func<string, string> label, Action<string> change)
        {
            _body.Add(NativeTheme.Body(title));
            var picker = new Picker { Title = title, AutomationId = "sr6-foundation-" + id };
            foreach (string option in options) picker.Items.Add(label(option));
            picker.SelectedIndex = Array.IndexOf(options, selected);
            picker.SelectedIndexChanged += (_, _) =>
            {
                if (Current() && picker.SelectedIndex >= 0 && picker.SelectedIndex < options.Length)
                    change(options[picker.SelectedIndex]);
            };
            _body.Add(picker);
        }
    }

    private void BuildQualityEditor(IReadOnlyList<Sr6CreationQualityOption> catalog, Func<bool> current,
        Action<Sr6CreationFoundationSelection> change)
    {
        _body.Add(NativeTheme.Body(Sr6CreationCopy.Text("QualitiesHelp"), NativeTheme.Muted));
        if (_state?.Selection?.Qualities is { } saved)
        {
            _body.Add(NativeTheme.Body(Sr6CreationCopy.Text("TalentSavedBudget"), NativeTheme.Muted));
            _body.Add(NativeTheme.Body(Sr6CreationCopy.QualityBudget(saved)));
        }
        var selected = _selection!.Qualities?.OptionIds ?? [];
        foreach (string id in selected)
        {
            var option = catalog.Single(row => row.Id == id);
            _body.Add(NativeTheme.Body(Sr6CreationCopy.QualityValue(option)));
            var remove = NativeTheme.PrimaryButton(Sr6CreationCopy.Text("QualitiesRemove"));
            remove.AutomationId = "sr6-foundation-quality-remove-" + id;
            remove.Clicked += (_, _) =>
            {
                if (!current()) return;
                change(_selection with { Qualities = new(selected.Where(value => value != id).ToArray()) });
                Refresh();
            };
            _body.Add(remove);
        }
        if (selected.Count >= 6) return;
        var available = catalog.Where(row => !selected.Contains(row.Id, StringComparer.Ordinal)).ToArray();
        var picker = new Picker { Title = Sr6CreationCopy.Text("QualitiesChoose"), AutomationId = "sr6-foundation-quality-catalog" };
        foreach (var option in available) picker.Items.Add(Sr6CreationCopy.QualityName(option));
        var details = NativeTheme.Body(""); details.AutomationId = "sr6-foundation-quality-details";
        var add = NativeTheme.PrimaryButton(Sr6CreationCopy.Text("QualitiesAdd"));
        add.AutomationId = "sr6-foundation-quality-add";
        add.IsEnabled = false;
        picker.SelectedIndexChanged += (_, _) =>
        {
            if (!current()) return;
            bool valid = picker.SelectedIndex >= 0 && picker.SelectedIndex < available.Length;
            add.IsEnabled = valid && available[picker.SelectedIndex].Available;
            details.Text = valid ? Sr6CreationCopy.QualityValue(available[picker.SelectedIndex]) : "";
        };
        add.Clicked += (_, _) =>
        {
            if (!current() || picker.SelectedIndex < 0 || picker.SelectedIndex >= available.Length
                || !available[picker.SelectedIndex].Available) return;
            change(_selection with { Qualities = new([.. selected, available[picker.SelectedIndex].Id]) });
            Refresh();
        };
        _body.Add(picker);
        _body.Add(details);
        _body.Add(add);
    }

    private void BuildKarmaEditor(Sr6CreationKarmaOptions options, Func<bool> current,
        Action<Sr6CreationFoundationSelection> change)
    {
        _selection = _selection! with { Karma = _selection.Karma ?? new([], [], 0) };
        _body.Add(NativeTheme.Body(Sr6CreationCopy.Text("KarmaHelp"), NativeTheme.Muted));
        _body.Add(NativeTheme.Body(Sr6CreationCopy.KarmaOptions(options)));
        if (_state?.Selection?.Karma is { } saved)
        {
            _body.Add(NativeTheme.Body(Sr6CreationCopy.Text("TalentSavedBudget"), NativeTheme.Muted));
            _body.Add(NativeTheme.Body(Sr6CreationCopy.KarmaBudget(saved)));
        }
        AddRows(_selection.Karma.Attributes, false);
        AddRows(_selection.Karma.Skills, true);
        var catalog = options.Attributes.Select(row => (Option: row, Skill: false))
            .Concat(options.Skills.Select(row => (Option: row, Skill: true))).ToArray();
        var picker = new Picker { Title = Sr6CreationCopy.Text("KarmaChoose"), AutomationId = "sr6-foundation-karma-catalog" };
        foreach (var row in catalog)
            picker.Items.Add(Sr6CreationCopy.Text(row.Skill ? "KarmaSkill" : "KarmaAttribute") + " · " + Sr6CreationCopy.Label(row.Option.Id));
        var details = NativeTheme.Body(""); details.AutomationId = "sr6-foundation-karma-details";
        var increase = new Picker { Title = Sr6CreationCopy.Text("KarmaIncrease"), AutomationId = "sr6-foundation-karma-increase" };
        var subject = new Entry { Placeholder = Sr6CreationCopy.Text("SkillExoticSpecializations"), MaxLength = 80,
            AutomationId = "sr6-foundation-karma-exotic", IsVisible = false };
        var add = NativeTheme.PrimaryButton(Sr6CreationCopy.Text("KarmaAdd"));
        add.AutomationId = "sr6-foundation-karma-add"; add.IsEnabled = false;
        picker.SelectedIndexChanged += (_, _) =>
        {
            if (!current() || picker.SelectedIndex < 0 || picker.SelectedIndex >= catalog.Length) return;
            var selected = catalog[picker.SelectedIndex];
            increase.Items.Clear(); increase.SelectedIndex = -1;
            if (selected.Option.Available)
                for (int value = 1; value <= selected.Option.MaximumIncrease; value++)
                    increase.Items.Add(value.ToString(CultureInfo.InvariantCulture));
            details.Text = Sr6CreationCopy.KarmaOption(selected.Option);
            subject.IsVisible = selected.Skill && selected.Option.Id == "ExoticWeapons" && selected.Option.BaseRating == 0;
            subject.Text = "";
            add.IsEnabled = false;
        };
        increase.SelectedIndexChanged += (_, _) =>
        { if (current()) add.IsEnabled = increase.SelectedIndex >= 0 && increase.SelectedIndex < increase.Items.Count; };
        add.Clicked += (_, _) =>
        {
            if (!current() || picker.SelectedIndex < 0 || picker.SelectedIndex >= catalog.Length
                || increase.SelectedIndex < 0 || increase.SelectedIndex >= increase.Items.Count) return;
            var selected = catalog[picker.SelectedIndex];
            var row = new Sr6CreationKarmaIncrease(selected.Option.Id, int.Parse(increase.Items[increase.SelectedIndex], CultureInfo.InvariantCulture),
                subject.IsVisible ? subject.Text?.Trim() : null);
            var selection = _selection.Karma!;
            change(_selection with { Karma = selected.Skill
                ? selection with { Skills = [.. selection.Skills, row] }
                : selection with { Attributes = [.. selection.Attributes, row] } });
            Refresh();
        };
        _body.Add(picker); _body.Add(details); _body.Add(increase); _body.Add(subject); _body.Add(add);
        _body.Add(NativeTheme.Body(Sr6CreationCopy.Text("KarmaNuyen")));
        var cash = new Picker { Title = Sr6CreationCopy.Text("KarmaNuyen"), AutomationId = "sr6-foundation-karma-nuyen" };
        for (int value = 0; value <= options.MaximumKarmaForNuyen; value++) cash.Items.Add(value.ToString(CultureInfo.InvariantCulture));
        cash.SelectedIndex = _selection.Karma.KarmaForNuyen;
        cash.SelectedIndexChanged += (_, _) =>
        {
            if (current() && cash.SelectedIndex >= 0 && cash.SelectedIndex < cash.Items.Count)
                change(_selection with { Karma = _selection.Karma! with { KarmaForNuyen = cash.SelectedIndex } });
        };
        _body.Add(cash);
        if (_state?.KarmaSpecializationOptions is { } specialtyOptions)
            BuildKarmaSpecializations(specialtyOptions, current, change);
        if (_state?.KarmaKnowledgeOptions is { } knowledgeOptions)
            BuildKarmaKnowledge(knowledgeOptions, current, change);
        else
            _body.Add(NativeTheme.Body(Sr6CreationCopy.Text("KarmaKnowledgeRequired"), NativeTheme.Muted));

        void AddRows(IReadOnlyList<Sr6CreationKarmaIncrease> rows, bool skills)
        {
            for (int index = 0; index < rows.Count; index++)
            {
                int captured = index;
                var row = rows[index];
                _body.Add(NativeTheme.Body(Sr6CreationCopy.KarmaChoice(row)));
                var remove = NativeTheme.PrimaryButton(Sr6CreationCopy.Text("KarmaRemove"));
                remove.AutomationId = "sr6-foundation-karma-remove-" + (skills ? "skill-" : "attribute-") + index.ToString(CultureInfo.InvariantCulture);
                remove.Clicked += (_, _) =>
                {
                    if (!current()) return;
                    var selection = _selection.Karma!;
                    change(_selection with { Karma = skills
                        ? selection with { Skills = selection.Skills.Where((_, i) => i != captured).ToArray() }
                        : selection with { Attributes = selection.Attributes.Where((_, i) => i != captured).ToArray() } });
                    Refresh();
                };
                _body.Add(remove);
            }
        }
    }

    private void BuildKarmaSpecializations(Sr6CreationKarmaSpecializationOptions options, Func<bool> current,
        Action<Sr6CreationFoundationSelection> change)
    {
        _body.Add(NativeTheme.Body(Sr6CreationCopy.KarmaSpecializationHelp(options), NativeTheme.Muted));
        var selected = _selection!.Karma!.Specializations ?? [];
        for (int index = 0; index < selected.Count; index++)
        {
            int captured = index;
            var row = selected[index];
            _body.Add(NativeTheme.Body(Sr6CreationCopy.Label(row.SkillId) + " · " + row.Subject));
            var remove = NativeTheme.PrimaryButton(Sr6CreationCopy.Text("KarmaSpecializationRemove"));
            remove.AutomationId = "sr6-foundation-karma-specialization-remove-" + index.ToString(CultureInfo.InvariantCulture);
            remove.Clicked += (_, _) =>
            {
                if (!current()) return;
                change(_selection with { Karma = _selection.Karma! with
                    { Specializations = selected.Where((_, i) => i != captured).ToArray() } });
                Refresh();
            };
            _body.Add(remove);
        }
        var picker = new Picker { Title = Sr6CreationCopy.Text("KarmaSpecializationSkill"), AutomationId = "sr6-foundation-karma-specialization-skill" };
        foreach (var row in options.Skills) picker.Items.Add(Sr6CreationCopy.Label(row.SkillId));
        var details = NativeTheme.Body(""); details.AutomationId = "sr6-foundation-karma-specialization-details";
        var subject = new Entry { Placeholder = Sr6CreationCopy.Text("KarmaSpecializationSubject"), MaxLength = 80,
            AutomationId = "sr6-foundation-karma-specialization-subject" };
        var add = NativeTheme.PrimaryButton(Sr6CreationCopy.Text("KarmaSpecializationAdd"));
        add.AutomationId = "sr6-foundation-karma-specialization-add"; add.IsEnabled = false;
        bool CanAdd() => current() && picker.SelectedIndex >= 0 && picker.SelectedIndex < options.Skills.Count
            && options.Skills[picker.SelectedIndex].Available && !string.IsNullOrWhiteSpace(subject.Text);
        picker.SelectedIndexChanged += (_, _) =>
        {
            if (!current()) return;
            details.Text = picker.SelectedIndex >= 0 && picker.SelectedIndex < options.Skills.Count
                ? Sr6CreationCopy.KarmaSpecializationOption(options.Skills[picker.SelectedIndex]) : "";
            add.IsEnabled = CanAdd();
        };
        subject.TextChanged += (_, _) => { if (current()) add.IsEnabled = CanAdd(); };
        add.Clicked += (_, _) =>
        {
            if (!CanAdd()) return;
            var row = new Sr6CreationKarmaSpecialization(options.Skills[picker.SelectedIndex].SkillId, subject.Text!.Trim());
            change(_selection with { Karma = _selection.Karma! with { Specializations = [.. selected, row] } });
            Refresh();
        };
        _body.Add(picker); _body.Add(details); _body.Add(subject); _body.Add(add);
    }

    private void BuildKarmaKnowledge(Sr6CreationKarmaKnowledgeOptions options, Func<bool> current,
        Action<Sr6CreationFoundationSelection> change)
    {
        _body.Add(NativeTheme.Body(Sr6CreationCopy.KarmaKnowledgeHelp(options), NativeTheme.Muted));
        var selected = _selection!.Karma!.Knowledge ?? new([], []);
        foreach (var row in selected.KnowledgeSkills)
        {
            _body.Add(NativeTheme.Body(row.Name));
            AddRemove("topic", row.Id, () => selected with
                { KnowledgeSkills = selected.KnowledgeSkills.Where(value => value.Id != row.Id).ToArray() });
        }
        var topic = new Entry { Placeholder = Sr6CreationCopy.Text("KarmaKnowledgeTopic"), MaxLength = 80,
            AutomationId = "sr6-foundation-karma-knowledge-topic" };
        var addTopic = NativeTheme.PrimaryButton(Sr6CreationCopy.Text("KnowledgeAddTopic"));
        addTopic.AutomationId = "sr6-foundation-karma-knowledge-add-topic"; addTopic.IsEnabled = false;
        topic.TextChanged += (_, _) => { if (current()) addTopic.IsEnabled = !string.IsNullOrWhiteSpace(topic.Text); };
        addTopic.Clicked += (_, _) =>
        {
            if (!current() || string.IsNullOrWhiteSpace(topic.Text)) return;
            Change(selected with { KnowledgeSkills = [.. selected.KnowledgeSkills, new(Guid.NewGuid(), topic.Text.Trim())] });
        };
        _body.Add(topic); _body.Add(addTopic);
        foreach (var row in selected.Languages)
        {
            _body.Add(NativeTheme.Body(row.Name + " · " + Sr6CreationCopy.KarmaLanguageLevel(row.Level)));
            AddRemove("language", row.Id, () => selected with
                { Languages = selected.Languages.Where(value => value.Id != row.Id).ToArray() });
        }
        var language = new Picker { Title = Sr6CreationCopy.Text("KarmaLanguageChoose"),
            AutomationId = "sr6-foundation-karma-knowledge-language" };
        language.Items.Add(Sr6CreationCopy.Text("KarmaLanguageNew"));
        foreach (var row in options.PoolLanguages)
            language.Items.Add(row.Name + " · " + Sr6CreationCopy.KarmaLanguageLevel(row.Level));
        language.SelectedIndex = 0;
        var name = new Entry { Placeholder = Sr6CreationCopy.Text("KnowledgeLanguage"), MaxLength = 80,
            AutomationId = "sr6-foundation-karma-knowledge-name" };
        var level = new Picker { Title = Sr6CreationCopy.Text("KarmaLanguageTarget"),
            AutomationId = "sr6-foundation-karma-knowledge-level" };
        foreach (string value in options.LanguageLevels) level.Items.Add(Sr6CreationCopy.KarmaLanguageLevel(value));
        var addLanguage = NativeTheme.PrimaryButton(Sr6CreationCopy.Text("KarmaLanguageAdd"));
        addLanguage.AutomationId = "sr6-foundation-karma-knowledge-add-language"; addLanguage.IsEnabled = false;
        bool CanAdd() => current() && language.SelectedIndex >= 0 && language.SelectedIndex <= options.PoolLanguages.Count
            && level.SelectedIndex >= 0 && level.SelectedIndex < options.LanguageLevels.Count
            && (language.SelectedIndex > 0 || !string.IsNullOrWhiteSpace(name.Text));
        language.SelectedIndexChanged += (_, _) =>
        {
            if (!current()) return;
            name.IsVisible = language.SelectedIndex == 0;
            addLanguage.IsEnabled = CanAdd();
        };
        name.TextChanged += (_, _) => { if (current()) addLanguage.IsEnabled = CanAdd(); };
        level.SelectedIndexChanged += (_, _) => { if (current()) addLanguage.IsEnabled = CanAdd(); };
        addLanguage.Clicked += (_, _) =>
        {
            if (!CanAdd()) return;
            var baseline = language.SelectedIndex == 0 ? null : options.PoolLanguages[language.SelectedIndex - 1];
            var row = new Sr6CreationLanguageEntry(baseline?.Id ?? Guid.NewGuid(), baseline?.Name ?? name.Text!.Trim(),
                options.LanguageLevels[level.SelectedIndex]);
            Change(selected with { Languages = [.. selected.Languages.Where(value => value.Id != row.Id), row] });
        };
        _body.Add(language); _body.Add(name); _body.Add(level); _body.Add(addLanguage);

        void Change(Sr6CreationKarmaKnowledgeSelection value)
        {
            if (!current()) return;
            change(_selection with { Karma = _selection.Karma! with { Knowledge = value } });
            Refresh();
        }
        void AddRemove(string kind, Guid id, Func<Sr6CreationKarmaKnowledgeSelection> removeSelection)
        {
            var remove = NativeTheme.PrimaryButton(Sr6CreationCopy.Text("KarmaKnowledgeRemove"));
            remove.AutomationId = "sr6-foundation-karma-knowledge-remove-" + kind + "-" + id.ToString("N");
            remove.Clicked += (_, _) => { if (current()) Change(removeSelection()); };
            _body.Add(remove);
        }
    }

    private void BuildPowerEditor(IReadOnlyList<Sr6CreationAdeptPowerOption> catalog,
        Func<bool> current, Action<Sr6CreationFoundationSelection> change)
    {
        _selection = _selection! with { AdeptPowers = _selection.AdeptPowers ?? new([]) };
        _body.Add(NativeTheme.Body(Sr6CreationCopy.Text("PowersHelp"), NativeTheme.Muted));
        if (_state?.Selection?.AdeptPowers is { } saved)
            _body.Add(NativeTheme.Body(Sr6CreationCopy.PowersBudget(saved)));
        for (int index = 0; index < _selection.AdeptPowers.Choices.Count; index++)
        {
            int captured = index;
            var choice = _selection.AdeptPowers.Choices[index];
            var option = catalog.Single(row => row.Id == choice.CatalogId);
            _body.Add(NativeTheme.Body(Sr6CreationCopy.PowerName(option) + " · " +
                Sr6CreationCopy.Text("PowersRating") + " " + choice.Rating + " · " + option.SourceAnchorId));
            var remove = NativeTheme.PrimaryButton(Sr6CreationCopy.Text("PowersRemove"));
            remove.AutomationId = "sr6-foundation-power-remove-" + index;
            remove.Clicked += (_, _) =>
            {
                if (!current()) return;
                change(_selection with { AdeptPowers = new(_selection.AdeptPowers.Choices.Where((_, i) => i != captured).ToArray()) });
                Refresh();
            };
            _body.Add(remove);
        }
        if (_selection.AdeptPowers.Choices.Count >= 24) return;
        var picker = new Picker { Title = Sr6CreationCopy.Text("PowersChoose"), AutomationId = "sr6-foundation-power-catalog" };
        foreach (var option in catalog) picker.Items.Add(Sr6CreationCopy.PowerName(option));
        var rating = new Picker { Title = Sr6CreationCopy.Text("PowersRating"), AutomationId = "sr6-foundation-power-rating" };
        var details = NativeTheme.Body("", NativeTheme.Muted);
        details.AutomationId = "sr6-foundation-power-details";
        var add = NativeTheme.PrimaryButton(Sr6CreationCopy.Text("PowersAdd"));
        add.AutomationId = "sr6-foundation-power-add";
        add.IsEnabled = false;
        picker.SelectedIndexChanged += (_, _) =>
        {
            if (!current()) return;
            add.IsEnabled = false;
            rating.Items.Clear();
            if (picker.SelectedIndex < 0 || picker.SelectedIndex >= catalog.Count) return;
            var option = catalog[picker.SelectedIndex];
            details.Text = Sr6CreationCopy.PowerOption(option);
            foreach (int value in Enumerable.Range(1, option.MaximumRating)) rating.Items.Add(value.ToString(CultureInfo.InvariantCulture));
            rating.SelectedIndex = option.MaximumRating > 0 ? 0 : -1;
        };
        rating.SelectedIndexChanged += (_, _) =>
        {
            if (current()) add.IsEnabled = picker.SelectedIndex >= 0 && picker.SelectedIndex < catalog.Count
                && rating.SelectedIndex >= 0 && rating.SelectedIndex < catalog[picker.SelectedIndex].MaximumRating;
        };
        add.Clicked += (_, _) =>
        {
            if (!current() || picker.SelectedIndex < 0 || picker.SelectedIndex >= catalog.Count
                || rating.SelectedIndex < 0 || rating.SelectedIndex >= catalog[picker.SelectedIndex].MaximumRating) return;
            change(_selection with { AdeptPowers = new([.. _selection.AdeptPowers.Choices,
                new(catalog[picker.SelectedIndex].Id, rating.SelectedIndex + 1)]) });
            Refresh();
        };
        _body.Add(picker);
        _body.Add(details);
        _body.Add(rating);
        _body.Add(add);
    }

    private void BuildSpellEditor(IReadOnlyList<Sr6CreationSpellOption> catalog,
        Func<bool> current, Action<Sr6CreationFoundationSelection> change)
    {
        _selection = _selection! with { Spells = _selection.Spells ?? new([]) };
        _body.Add(NativeTheme.Body(Sr6CreationCopy.Text("SpellsHelp"), NativeTheme.Muted));
        if (_state?.Selection?.Spells is { } saved)
        {
            _body.Add(NativeTheme.Body(Sr6CreationCopy.SpellsBudget(saved)));
            _body.Add(NativeTheme.Body(Sr6CreationCopy.Text("SpellsUse." + saved.UseId), NativeTheme.Muted));
        }
        for (int index = 0; index < _selection.Spells.CatalogIds.Count; index++)
        {
            int captured = index;
            var option = catalog.Single(row => row.Id == _selection.Spells.CatalogIds[index]);
            _body.Add(NativeTheme.Body(Sr6CreationCopy.SpellName(option) + " · " + option.SourceAnchorId));
            var remove = NativeTheme.PrimaryButton(Sr6CreationCopy.Text("SpellsRemove"));
            remove.AutomationId = "sr6-foundation-spell-remove-" + index;
            remove.Clicked += (_, _) =>
            {
                if (!current()) return;
                change(_selection with { Spells = new(_selection.Spells.CatalogIds.Where((_, i) => i != captured).ToArray()) });
                Refresh();
            };
            _body.Add(remove);
        }
        if (_selection.Spells.CatalogIds.Count >= 12) return;
        var picker = new Picker { Title = Sr6CreationCopy.Text("SpellsChoose"), AutomationId = "sr6-foundation-spell-catalog" };
        foreach (var option in catalog) picker.Items.Add(Sr6CreationCopy.SpellName(option));
        var add = NativeTheme.PrimaryButton(Sr6CreationCopy.Text("SpellsAdd"));
        add.AutomationId = "sr6-foundation-spell-add";
        add.IsEnabled = false;
        picker.SelectedIndexChanged += (_, _) =>
        {
            if (current()) add.IsEnabled = picker.SelectedIndex >= 0 && picker.SelectedIndex < catalog.Count;
        };
        add.Clicked += (_, _) =>
        {
            if (!current() || picker.SelectedIndex < 0 || picker.SelectedIndex >= catalog.Count) return;
            change(_selection with { Spells = new([.. _selection.Spells.CatalogIds, catalog[picker.SelectedIndex].Id]) });
            Refresh();
        };
        _body.Add(picker);
        _body.Add(add);
    }

    private void BuildComplexFormEditor(IReadOnlyList<Sr6CreationComplexFormOption> catalog,
        Func<bool> current, Action<Sr6CreationFoundationSelection> change)
    {
        _selection = _selection! with { ComplexForms = _selection.ComplexForms ?? new([]) };
        _body.Add(NativeTheme.Body(Sr6CreationCopy.Text("FormsHelp"), NativeTheme.Muted));
        if (_state?.Selection?.ComplexForms is { } saved)
        {
            _body.Add(NativeTheme.Body(Sr6CreationCopy.Text("TalentSavedBudget"), NativeTheme.Muted));
            _body.Add(NativeTheme.Body(Sr6CreationCopy.FormsBudget(saved)));
        }
        for (int index = 0; index < _selection.ComplexForms.Choices.Count; index++)
        {
            int captured = index;
            var choice = _selection.ComplexForms.Choices[index];
            var option = catalog.Single(row => row.Id == choice.CatalogId);
            _body.Add(NativeTheme.Body(option.SourceName + " · " + option.SourceAnchorId));
            if (option.SubjectKind is { } kind)
            {
                _body.Add(NativeTheme.Body(Sr6CreationCopy.Text("FormsSubject." + kind)));
                var entry = new Entry { AutomationId = "sr6-foundation-form-subject-" + index,
                    Text = choice.Subject ?? "", MaxLength = 80 };
                entry.TextChanged += (_, _) =>
                {
                    if (!current()) return;
                    string name = (entry.Text ?? "").Trim();
                    try { name = name.Normalize(System.Text.NormalizationForm.FormC); }
                    catch (ArgumentException) { /* Core rejects malformed input at review. */ }
                    Edit(rows => rows.Select((row, i) => i == captured ? row with { Subject = name } : row).ToArray());
                };
                _body.Add(entry);
                _body.Add(NativeTheme.Body(Sr6CreationCopy.Text("FormsSubjectReview"), NativeTheme.Muted));
            }
            var remove = NativeTheme.PrimaryButton(Sr6CreationCopy.Text("FormsRemove"));
            remove.AutomationId = "sr6-foundation-form-remove-" + index;
            remove.Clicked += (_, _) =>
            {
                if (!current()) return;
                Edit(rows => rows.Where((_, i) => i != captured).ToArray());
                Refresh();
            };
            _body.Add(remove);
        }
        if (_selection.ComplexForms.Choices.Count >= 12) return;
        var picker = new Picker { Title = Sr6CreationCopy.Text("FormsChoose"), AutomationId = "sr6-foundation-form-catalog" };
        foreach (var option in catalog) picker.Items.Add(option.SourceName);
        var add = NativeTheme.PrimaryButton(Sr6CreationCopy.Text("FormsAdd"));
        add.AutomationId = "sr6-foundation-form-add";
        add.IsEnabled = false;
        picker.SelectedIndexChanged += (_, _) =>
        {
            if (current()) add.IsEnabled = picker.SelectedIndex >= 0 && picker.SelectedIndex < catalog.Count;
        };
        add.Clicked += (_, _) =>
        {
            if (!current() || picker.SelectedIndex < 0 || picker.SelectedIndex >= catalog.Count) return;
            string id = catalog[picker.SelectedIndex].Id;
            Edit(rows => [.. rows, new(id)]);
            Refresh(); // Never replace the picker tree inside SelectedIndexChanged.
        };
        _body.Add(picker);
        _body.Add(add);

        void Edit(Func<IReadOnlyList<Sr6CreationComplexFormChoice>, IReadOnlyList<Sr6CreationComplexFormChoice>> update)
        {
            if (current() && _selection.ComplexForms is { } forms)
                change(_selection with { ComplexForms = new(update(forms.Choices)) });
        }
    }
}
