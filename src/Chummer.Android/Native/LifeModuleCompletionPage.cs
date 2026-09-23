using System.Globalization;
using Chummer.Contracts.Characters;
using Chummer.Contracts.LifeModules;

namespace Chummer.Android.Native;

internal enum LifeCompletionStep { Overview, Qualities, Talent, Attributes, Skills, Skill, Group, Resources, Gear, Lifestyles, Contacts, Contact, Magic, MagicCatalog, Review }

/// <summary>Life Modules input pages. All grants, costs, limits and the final write belong to Core.</summary>
internal sealed partial class LifeModuleCompletionPage : NativePageBase
{
    private readonly LifeModuleCompletionSession _session;
    private readonly LifeCompletionStep _step;
    private readonly string? _id, _kind;
    private readonly Func<Task>? _openBook;
    private readonly VerticalStackLayout _body = new() { Padding = new Thickness(20, 18, 20, 40), Spacing = 14 };
    private long _render;
    private string _search = "";
    private int _page;
    private Label? _pending;
    private const int PageSize = 20;
    private CharacterCreationFoundationFinalizationPreviewRequest Input => _session.Input!;
    private CharacterCreationFoundationFinalizationPreview? Quote => _session.Preview;

    internal LifeModuleCompletionPage(RunnerSessionCoordinator coordinator, Func<Task>? openBook = null,
        LifeModuleCompletionDraftStore? store = null)
        : this(coordinator, new LifeModuleCompletionSession(coordinator, store), LifeCompletionStep.Overview, openBook) { }

    private LifeModuleCompletionPage(RunnerSessionCoordinator coordinator, LifeModuleCompletionSession session,
        LifeCompletionStep step, Func<Task>? openBook, string? id = null, string? kind = null) : base(coordinator)
    {
        _session = session; _step = step; _openBook = openBook; _id = id; _kind = kind;
        Title = Caption(step); AutomationId = "life-completion-" + step.ToString().ToLowerInvariant();
        Content = new ScrollView { Content = _body };
    }

    private static string Caption(LifeCompletionStep step) => step switch
    {
        LifeCompletionStep.Qualities => CreationKarmaCopy.Qualities,
        LifeCompletionStep.Talent => CreationKarmaCopy.Talent,
        LifeCompletionStep.Attributes => CreationKarmaCopy.Attributes,
        LifeCompletionStep.Skills or LifeCompletionStep.Skill => CreationKarmaCopy.Skills,
        LifeCompletionStep.Group => CreationKarmaCopy.Groups,
        LifeCompletionStep.Resources => CreationKarmaCopy.Resources,
        LifeCompletionStep.Gear => CreationKarmaCopy.Gear,
        LifeCompletionStep.Lifestyles => CreationKarmaCopy.Lifestyles,
        LifeCompletionStep.Contacts or LifeCompletionStep.Contact => CreationKarmaCopy.Contacts,
        LifeCompletionStep.Magic or LifeCompletionStep.MagicCatalog => CreationKarmaCopy.Magic,
        LifeCompletionStep.Review => CreationKarmaCopy.Review,
        _ => LifeCopy("Title", "Complete Life Modules")
    };

    private static string LifeCopy(string key, string fallback) => CreationAllocationStrings.Get("LifeCompletion." + key, fallback);
    protected override void OnAppearing()
    {
        _body.IsEnabled = false;
        _body.Clear();
        _body.Add(new ActivityIndicator { IsRunning = true });
        _body.Add(NativeTheme.Body(CreationKarmaCopy.Loading));
        base.OnAppearing();
    }
    protected override void OnDisappearing() { _render++; _body.IsEnabled = false; base.OnDisappearing(); }
    protected override async Task PrepareForAppearanceRefreshAsync(CancellationToken ct)
    {
        long appearance = CaptureAppearanceGeneration();
        await _session.OpenAsync(ct, () => IsCurrentAppearanceGeneration(appearance));
    }
    private bool Current(long render, long appearance) => _render == render && IsCurrentAppearanceGeneration(appearance) && _session.Ready;
    private Task Open(LifeCompletionStep step, string? id = null, string? kind = null)
        => Navigation.PushAsync(new LifeModuleCompletionPage(Coordinator, _session, step, _openBook, id, kind));
    private void Change(CharacterCreationFoundationFinalizationPreviewRequest input)
    {
        _session.Change(input);
        if (_pending is not null) _pending.Text = LifeCopy("Pending", "Input changed. Save and review again; the runner has not changed.");
    }
    private async Task Review()
    {
        long appearance = CaptureAppearanceGeneration();
        _body.IsEnabled = false;
        await _session.ReviewAsync(default, () => IsCurrentAppearanceGeneration(appearance));
    }
    private async Task ChangeReview(CharacterCreationFoundationFinalizationPreviewRequest input) { Change(input); await Review(); }
    private void Body(string text, string? id = null)
    { var label = NativeTheme.Body(text); label.AutomationId = id; _body.Add(label); }
    private Button Button(string text, string id, Func<Task> action, bool enabled = true)
    {
        long render = _render, appearance = CaptureAppearanceGeneration();
        var button = NativeTheme.SecondaryButton(text); button.AutomationId = id; button.IsEnabled = enabled;
        button.Clicked += async (_, _) => await RunAsync(async () =>
        { await Task.Yield(); if (enabled && Current(render, appearance)) await action(); });
        _body.Add(button);
        return button;
    }
    private void Number(string title, string id, int value, int maximum, Action<int> changed)
    {
        long render = _render, appearance = CaptureAppearanceGeneration();
        var label = NativeTheme.Body(CreationKarmaCopy.Levels(title, value));
        var input = new Stepper { Minimum = 0, Maximum = Math.Max(1, Math.Max(value, maximum)),
            Increment = 1, Value = value, AutomationId = id, IsEnabled = maximum > 0 || value > 0 };
        input.ValueChanged += (_, e) =>
        { if (!Current(render, appearance) || !input.IsEnabled) return; changed(checked((int)e.NewValue)); label.Text = CreationKarmaCopy.Levels(title, (int)e.NewValue); };
        _body.Add(label); _body.Add(input);
    }
    private Entry Text(string title, string id, string value, Action<string>? changed = null, bool numeric = false)
    {
        Body(title);
        long render = _render, appearance = CaptureAppearanceGeneration();
        var input = new Entry { Text = value, AutomationId = id, Keyboard = numeric ? Keyboard.Numeric : Keyboard.Default };
        input.TextChanged += (_, e) => { if (Current(render, appearance)) changed?.Invoke(e.NewTextValue ?? ""); };
        _body.Add(input); return input;
    }
    private void Flag(string title, string id, bool value, Action<bool> changed)
    {
        Body(title);
        long render = _render, appearance = CaptureAppearanceGeneration();
        var input = new Switch { IsToggled = value, AutomationId = id };
        input.Toggled += (_, e) => { if (Current(render, appearance)) changed(e.Value); };
        _body.Add(input);
    }
    private void Search()
    {
        long render = _render, appearance = CaptureAppearanceGeneration();
        var input = new SearchBar { Text = _search, Placeholder = CreationKarmaCopy.Search, AutomationId = "life-search" };
        input.TextChanged += (_, e) => { if (Current(render, appearance)) _search = e.NewTextValue ?? ""; };
        _body.Add(input);
        Button(CreationKarmaCopy.Search, "life-search-go", () => { _page = 0; return Task.CompletedTask; });
    }
    private IEnumerable<T> Page<T>(IEnumerable<T> rows)
    {
        var all = rows.ToArray(); _page = Math.Min(_page, Math.Max(0, (all.Length - 1) / PageSize));
        Button(CreationKarmaCopy.Previous, "life-previous", () => { _page--; return Task.CompletedTask; }, _page > 0);
        Button(CreationKarmaCopy.Next, "life-next", () => { _page++; return Task.CompletedTask; }, (_page + 1) * PageSize < all.Length);
        return all.Skip(_page * PageSize).Take(PageSize);
    }

    protected override void Refresh()
    {
        _render++; _body.Clear(); _body.IsEnabled = _session.FrameCurrent;
        _body.Add(NativeTheme.Title(Title));
        if (!_session.FrameCurrent) { Body(CreationKarmaCopy.Stale); return; }
        if (_session.Receipt is { } receipt)
        {
            Body(Coordinator.IsLifeModuleCompletionReceiptCurrent(receipt) ? CreationKarmaCopy.CareerReady : CreationKarmaCopy.CareerReopen,
                "life-completion-saved");
            long render = _render, appearance = CaptureAppearanceGeneration();
            var done = NativeTheme.PrimaryButton(CreationKarmaCopy.OpenCareer);
            done.AutomationId = "life-completion-open-career";
            done.IsEnabled = Coordinator.IsLifeModuleCompletionReceiptCurrent(receipt);
            done.Clicked += async (_, _) => await RunAsync(async () =>
            {
                if (render == _render && IsCurrentAppearanceGeneration(appearance)
                    && Coordinator.IsLifeModuleCompletionReceiptCurrent(receipt)) await Navigation.PopToRootAsync();
            });
            _body.Add(done);
            return;
        }
        if (!_session.Ready)
        {
            Body(CreationKarmaCopy.Stale); foreach (string reason in _session.Blockers) Body(reason);
            if (_session.CanDiscardInputDraft)
            {
                long render = _render, appearance = CaptureAppearanceGeneration();
                var reset = NativeTheme.SecondaryButton(LifeCopy("DiscardInputs", "Discard these input choices and start again. Confirmed modules and chapters are kept."));
                reset.AutomationId = "life-discard-inputs";
                reset.Clicked += async (_, _) => await RunAsync(async () =>
                {
                    if (render == _render && IsCurrentAppearanceGeneration(appearance))
                        await _session.DiscardInputDraftAsync(default, () => IsCurrentAppearanceGeneration(appearance));
                });
                _body.Add(reset);
            }
            return;
        }
        Body(CreationKarmaCopy.Binding(_session.State!.Binding.ContentRevision, _session.State.Binding.SavedRevision), "life-completion-binding");
        _pending = NativeTheme.Body(_session.Saved ? LifeCopy("Saved", "Input draft saved. The runner changes only after final confirmation.")
            : LifeCopy("Pending", "Input changed. Save and review again; the runner has not changed."));
        _pending.AutomationId = "life-completion-input-status"; _body.Add(_pending);
        switch (_step)
        {
            case LifeCompletionStep.Overview: Overview(); break;
            case LifeCompletionStep.Qualities: Qualities(); break;
            case LifeCompletionStep.Talent: Talents(); break;
            case LifeCompletionStep.Attributes: Attributes(); break;
            case LifeCompletionStep.Skills: Skills(); break;
            case LifeCompletionStep.Skill: Skill(); break;
            case LifeCompletionStep.Group: Group(); break;
            case LifeCompletionStep.Resources: ResourceInputs(); break;
            case LifeCompletionStep.Gear: Gear(); break;
            case LifeCompletionStep.Lifestyles: Lifestyles(); break;
            case LifeCompletionStep.Contacts: Contacts(); break;
            case LifeCompletionStep.Contact: Contact(); break;
            case LifeCompletionStep.Magic: Magic(); break;
            case LifeCompletionStep.MagicCatalog: MagicCatalog(); break;
            case LifeCompletionStep.Review: CompletionReview(); break;
        }
        foreach (string reason in _session.Blockers.Distinct()) Body(CreationKarmaCopy.Blocker(reason), "life-blocker-" + reason);
    }

    private void Overview()
    {
        Body(LifeCopy("Help", "Your chapters are retained. Review module grants and buy any additional values. Only Core decides costs and whether the complete runner is ready for Career."));
        if (_openBook is not null) Button(LifeCopy("Book", "Read your book"), "life-open-book", _openBook);
        foreach (var (step, available) in new[]
        {
            (LifeCompletionStep.Qualities, Quote?.ModuleSequence is not null),
            (LifeCompletionStep.Talent, Quote?.TalentCatalog is not null),
            (LifeCompletionStep.Attributes, Input.TalentSelection is not null),
            (LifeCompletionStep.Skills, Quote?.SkillsCatalog is not null),
            (LifeCompletionStep.Resources, Quote?.ResourcesPolicy is not null),
            (LifeCompletionStep.Gear, Quote?.GearAuthority is not null),
            (LifeCompletionStep.Lifestyles, Quote?.LifestylesAuthority is not null),
            (LifeCompletionStep.Contacts, Quote?.ContactsPolicy is not null),
            (LifeCompletionStep.Magic, Quote?.MagicCatalog is not null),
            (LifeCompletionStep.Review, true)
        }) Button(Caption(step), "life-open-" + step.ToString().ToLowerInvariant(), () => Open(step), available);
    }

    private void Qualities()
    {
        foreach (var row in Quote?.ModuleSequence?.QualityLevels ?? [])
        {
            Body(row.Group + " · " + row.Level.ToString(CultureInfo.CurrentCulture));
            if (row.InstancePrompt is not { } prompt) continue;
            QualityInput(prompt, row.InstanceValue);
        }
        foreach (var row in Quote?.ModuleSequence?.DependentQualityInstances ?? [])
            QualityInput(row.InstancePrompt, row.InstanceValue);
        Button(LifeCopy("SaveReview", "Save inputs and review"), "life-review-qualities", Review);

        void QualityInput(LifeModuleFollowUpPromptDto prompt, string? currentValue)
        {
            string selected = Input.QualityInstanceValues?.GetValueOrDefault(prompt.PromptId) ?? currentValue ?? "";
            if (prompt.Options.Count == 0)
                Text(prompt.Label, "life-quality-" + prompt.PromptId, selected, value => Set(value));
            else foreach (var option in prompt.Options)
                Button(option.Label, "life-quality-option-" + option.OptionId,
                    () => { Set(option.SourceValue); return Review(); }, option.IsEnabled);
            void Set(string value)
            {
                var answers = (Input.QualityInstanceValues ?? new Dictionary<string, string>()).ToDictionary(p => p.Key, p => p.Value);
                answers[prompt.PromptId] = value; Change(Input with { QualityInstanceValues = answers });
            }
        }
    }

    private void Talents()
    {
        foreach (var talent in Quote?.TalentCatalog?.Options ?? [])
        {
            Body(CreationKarmaCopy.Cost(talent.Name, talent.KarmaCost));
            foreach (string reason in talent.Blockers) Body(reason);
            Button(talent.Name, "life-talent-" + talent.OptionId,
                () => ChangeReview(Input with { TalentSelection = new(talent.OptionId) }), talent.IsEnabled && talent.Blockers.Count == 0);
        }
        if (Input.TalentSelection is { } selected && Quote?.TalentCatalog?.SkillUnlockChoices.TryGetValue(selected.OptionId, out var unlocks) == true)
            foreach (string unlock in unlocks)
                Button(unlock, "life-talent-unlock-" + unlock, () => ChangeReview(Input with { TalentSelection = selected with { SkillUnlock = unlock } }));
    }

    private void Attributes()
    {
        if (Input.AttributePurchases is null)
        {
            Button(LifeCopy("ReviewGrants", "Review granted values before adding purchases"), "life-begin-attributes",
                () => ChangeReview(Input with { AttributePurchases = [] })); return;
        }
        foreach (var row in Quote?.AttributeQuote?.Attributes ?? [])
        {
            Body(CreationAllocationStrings.Format("LifeCompletion.AttributeValue", "{0}: {1} · module levels {2} · purchase cost {3} Karma",
                CreationAllocationStrings.AttributeName(row.AttributeId), row.Current, row.AppliedModuleLevels, row.KarmaCost));
            int selected = Input.AttributePurchases.SingleOrDefault(x => x.AttributeId == row.AttributeId)?.KarmaLevels ?? 0;
            Number(CreationKarmaCopy.KarmaLevels, "life-attribute-" + row.AttributeId, selected,
                Math.Max(0, row.Maximum - row.Minimum - row.AppliedModuleLevels), value => Change(Input with
                { AttributePurchases = Input.AttributePurchases.Where(x => x.AttributeId != row.AttributeId)
                    .Concat(value > 0 ? [new CharacterCreationLifeModuleAttributePurchase(row.AttributeId, value)] : []).ToArray() }));
        }
        Button(LifeCopy("SaveReview", "Save inputs and review"), "life-review-attributes", Review);
    }

    private void Skills()
    {
        if (Input.SkillSelection is null)
        { Button(LifeCopy("ReviewGrants", "Review granted values before adding purchases"), "life-begin-skills", () => ChangeReview(Input with { SkillSelection = new([], []) })); return; }
        if (Quote?.SkillsQuote is not { } quote || Quote.SkillsCatalog is not { } catalog) return;
        Body(CreationKarmaCopy.KnowledgeBudget(quote.KnowledgePointsUsed, quote.KnowledgePointsTotal));
        foreach (var selected in Input.SkillSelection.Skills)
        {
            var source = catalog.ActiveSkills.Concat(catalog.KnowledgeSkills).SingleOrDefault(x => x.SourceSkillId == selected.SourceSkillId && x.Kind == selected.Kind);
            Button(source?.Name ?? selected.SourceSkillId, "life-selected-skill-" + selected.SourceSkillId,
                () => Open(LifeCompletionStep.Skill, selected.SourceSkillId, selected.Kind));
        }
        Search();
        foreach (var row in Page(catalog.ActiveSkills.Concat(catalog.KnowledgeSkills)
                     .Where(x => x.Name.Contains(_search, StringComparison.CurrentCultureIgnoreCase))))
            Button(row.Name, "life-skill-" + row.SourceSkillId, () => Open(LifeCompletionStep.Skill, row.SourceSkillId, row.Kind),
                row.Kind == CharacterCreationSkillKinds.Knowledge || quote.AllowedActiveSkillSourceIds.Contains(row.SourceSkillId));
        foreach (var group in catalog.SkillGroups.Where(x => x.Name.Contains(_search, StringComparison.CurrentCultureIgnoreCase)))
            Button(group.Name, "life-group-" + group.GroupId, () => Open(LifeCompletionStep.Group, group.GroupId),
                quote.AllowedSkillGroupIds.Contains(group.GroupId));
    }

    private void Skill()
    {
        if (Input.SkillSelection is not { } skills || Quote?.SkillsCatalog is not { } catalog || Quote.SkillsQuote is not { } quote) return;
        var source = catalog.ActiveSkills.Concat(catalog.KnowledgeSkills).SingleOrDefault(x => x.SourceSkillId == _id && x.Kind == _kind);
        if (source is null) return;
        // One editable instance per ordinary skill; exotic instances retain their exact specialization identity.
        var original = skills.Skills.FirstOrDefault(x => x.SourceSkillId == _id && x.Kind == _kind);
        var edit = original ?? new CharacterCreationKarmaSkillAllocation(source.SourceSkillId, source.Kind, 0);
        Body(source.Name);
        int maximum = source.Kind == CharacterCreationSkillKinds.Active ? quote.Policy.MaxActiveSkillRatingCreate : quote.Policy.MaxKnowledgeSkillRatingCreate;
        Number(CreationKarmaCopy.KarmaLevels, "life-skill-levels", edit.KarmaLevels, maximum, value => edit = edit with { KarmaLevels = value });
        if (source.Kind == CharacterCreationSkillKinds.Knowledge)
            Number(CreationKarmaCopy.KnowledgeLevels, "life-knowledge-levels", edit.KnowledgePointLevels, maximum, value => edit = edit with { KnowledgePointLevels = value });
        if (source.CanBeNativeLanguage)
            Flag(CreationKarmaCopy.NativeLanguage, "life-native-language", edit.IsNativeLanguage, value => edit = edit with { IsNativeLanguage = value });
        foreach (var option in source.Specializations)
            Button(option.Name, "life-specialization-" + option.OptionId, () =>
            {
                edit = edit with { SpecializationOptionId = option.OptionId,
                    SpecializationPayment = source.IsExotic ? CharacterCreationKarmaSpecializationPayments.ExoticIdentity : CharacterCreationKarmaSpecializationPayments.Karma };
                Change(Input with { SkillSelection = skills with { Skills = skills.Skills.Where(x => x != original).Append(edit).ToArray() } });
                return Review();
            });
        if (!source.IsExotic && edit.SpecializationOptionId is not null)
        {
            Flag(CreationKarmaCopy.KnowledgeLevels, "life-specialization-knowledge", edit.SpecializationPayment == CharacterCreationKarmaSpecializationPayments.KnowledgePoint,
                value => edit = edit with { SpecializationPayment = value ? CharacterCreationKarmaSpecializationPayments.KnowledgePoint : CharacterCreationKarmaSpecializationPayments.Karma });
            Button(CreationKarmaCopy.Remove, "life-remove-specialization", () => ChangeReview(Input with
            { SkillSelection = skills with { Skills = skills.Skills.Where(x => x != original).Append(edit with { SpecializationOptionId = null, SpecializationPayment = null }).ToArray() } }));
        }
        Button(CreationKarmaCopy.UseSelection, "life-use-skill", async () =>
        {
            Change(Input with { SkillSelection = skills with { Skills = skills.Skills.Where(x => x != original).Append(edit).ToArray() } });
            await Review(); if (_session.Ready) await Navigation.PopAsync();
        });
        Button(CreationKarmaCopy.Remove, "life-remove-skill", async () =>
        { Change(Input with { SkillSelection = skills with { Skills = skills.Skills.Where(x => x != original).ToArray() } }); await Review(); if (_session.Ready) await Navigation.PopAsync(); }, original is not null);
    }

    private void Group()
    {
        if (Input.SkillSelection is not { } skills || Quote?.SkillsQuote is not { } quote
            || Quote.SkillsCatalog?.SkillGroups.SingleOrDefault(x => x.GroupId == _id) is not { } source) return;
        int levels = skills.Groups.SingleOrDefault(x => x.GroupId == source.GroupId)?.KarmaLevels ?? 0;
        Number(source.Name, "life-group-levels", levels, quote.Policy.MaxSkillGroupRatingCreate, value => Change(Input with
        { SkillSelection = Input.SkillSelection! with { Groups = Input.SkillSelection!.Groups.Where(x => x.GroupId != source.GroupId)
            .Concat(value > 0 ? [new CharacterCreationKarmaSkillGroupAllocation(source.GroupId, value)] : []).ToArray() } }));
        Button(LifeCopy("SaveReview", "Save inputs and review"), "life-review-group", Review);
    }

    private void ResourceInputs()
    {
        if (Quote?.ResourcesPolicy is not { } policy) return;
        Body(CreationKarmaCopy.ResourceLimit(policy.MaximumKarmaInvestment));
        Body(policy.FundingExpression);
        var amount = Text(CreationKarmaCopy.ResourceInvestment, "life-resource-investment", Input.KarmaResourceInvestment?.ToString(CultureInfo.CurrentCulture) ?? "", numeric: true);
        Button(LifeCopy("SaveReview", "Save inputs and review"), "life-use-resources", async () =>
        {
            if (decimal.TryParse(amount.Text, NumberStyles.AllowDecimalPoint, CultureInfo.CurrentCulture, out decimal value) && value >= 0 && value <= policy.MaximumKarmaInvestment)
                await ChangeReview(Input with { KarmaResourceInvestment = value });
            else await DisplayAlertAsync(Title, CreationKarmaCopy.InvalidNumber, "OK");
        });
    }

    private void CompletionReview()
    {
        Body(CreationKarmaCopy.CompletionHelp);
        if (Quote?.StartingCashSource is { } cash)
            Body(CreationKarmaCopy.StartingCash(cash.Name, cash.Dice, cash.Multiplier));
        var dice = Text(CreationKarmaCopy.DiceTotal, "life-starting-dice", Input.StartingNuyenDiceTotal?.ToString(CultureInfo.CurrentCulture) ?? "",
            value => Change(Input with { StartingNuyenDiceTotal = int.TryParse(value, NumberStyles.None, CultureInfo.CurrentCulture, out int total) ? total : null }), numeric: true);
        Button(CreationKarmaCopy.PreviewCompletion, "life-review-completion", async () =>
        {
            if (int.TryParse(dice.Text, NumberStyles.None, CultureInfo.CurrentCulture, out int value))
                await ChangeReview(Input with { StartingNuyenDiceTotal = value });
            else await DisplayAlertAsync(Title, CreationKarmaCopy.InvalidNumber, "OK");
        });
        if (!_session.Reviewed || Quote is null) return;
        if (Quote.FinalizationBudget is { } budget)
            Body(CreationAllocationStrings.Format("LifeCompletion.Carryover", "Career: {0} Karma, {1:N2} ¥. Discarded: {2} Karma, {3:N2} ¥.",
                budget.KarmaCarried, budget.CareerNuyen, budget.KarmaDiscarded, budget.NuyenDiscarded));
        foreach (var delta in Quote.FinalizationPlan?.OrderedDeltas ?? [])
        {
            Body(delta.Kind + " · " + delta.TargetId + ": " + delta.BeforeValue + " → " + delta.AfterValue);
            Body(CreationKarmaCopy.DeltaCost(delta.KarmaCost, delta.NuyenCost));
            Body(string.Join(" · ", delta.SourceAnchorIds));
        }
        bool confirmed = false, saving = false;
        var acknowledgement = new Switch { AutomationId = "life-completion-confirmed" };
        Body(LifeCopy("Confirm", "Apply this reviewed runner once and enter Career? Your confirmed chapters remain attached."));
        _body.Add(acknowledgement);
        var progress = new ActivityIndicator
        {
            AutomationId = "life-completion-saving", IsVisible = false,
            WidthRequest = 24, HeightRequest = 24,
            HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center
        };
        var confirmButton = NativeTheme.PrimaryButton(CreationKarmaCopy.ConfirmCompletion);
        confirmButton.AutomationId = "life-confirm-completion";
        confirmButton.IsEnabled = false;
        // Reserve the indicator beside the button. Showing it during a save
        // must not insert a new row and push the tapped action below the viewport.
        var confirmationRow = new Grid
        {
            HeightRequest = confirmButton.HeightRequest,
            ColumnSpacing = 10,
            ColumnDefinitions = { new(GridLength.Star), new(new GridLength(32)) }
        };
        confirmationRow.Add(confirmButton, 0);
        confirmationRow.Add(progress, 1);
        _body.Add(confirmationRow);
        long render = _render, currentAppearance = CaptureAppearanceGeneration();
        confirmButton.Clicked += async (_, _) => await RunAsync(async () =>
        {
            if (saving || !confirmed || !confirmButton.IsEnabled || !Current(render, currentAppearance) || !_session.CanConfirm) return;
            // Update the actual tapped controls before yielding to the Core write.
            // RunAsync already prevents overlapping actions, but that alone does
            // not show Android users that a lengthy save is in progress.
            saving = true;
            _body.IsEnabled = false;
            confirmButton.IsEnabled = false;
            acknowledgement.IsEnabled = false;
            dice.IsEnabled = false;
            confirmButton.Text = LifeCopy("Saving", "Saving runner…");
            progress.IsVisible = true;
            progress.IsRunning = true;
            try
            {
                await Task.Yield();
                if (Current(render, currentAppearance))
                    await _session.ConfirmAsync(default, () => IsCurrentAppearanceGeneration(currentAppearance));
            }
            finally
            {
                progress.IsRunning = false;
                progress.IsVisible = false;
                saving = false;
                // Never rearm a consumed confirmation or restore a departed page.
                // The normal refresh shows the receipt, blockers or a fresh review.
                if (_render == render && IsCurrentAppearanceGeneration(currentAppearance))
                {
                    _body.IsEnabled = _session.FrameCurrent;
                    dice.IsEnabled = _session.Ready;
                    acknowledgement.IsEnabled = false;
                    confirmButton.Text = CreationKarmaCopy.ConfirmCompletion;
                }
            }
        });
        acknowledgement.Toggled += (_, args) =>
        { if (!saving && Current(render, currentAppearance)) { confirmed = args.Value; confirmButton.IsEnabled = confirmed && _session.CanConfirm; } };
    }
}
