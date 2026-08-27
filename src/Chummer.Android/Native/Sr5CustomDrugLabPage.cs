using System.Globalization;
using Chummer.Contracts.Characters;
using static Chummer.Android.Native.Sr5CareerFlowStrings;

namespace Chummer.Android.Native;

/// <summary>Native, source-bound SR5 custom-drug recipe lab for Creation and Career.</summary>
public sealed class Sr5CustomDrugLabPage : NativePageBase
{
    private readonly CharacterCustomDrugContext _context;
    private readonly VerticalStackLayout _body = new()
    {
        Padding = new Thickness(20, 18, 20, 40),
        Spacing = 14
    };
    private string? _operationNotice;

    public Sr5CustomDrugLabPage(
        RunnerSessionCoordinator coordinator,
        CharacterCustomDrugContext context) : base(coordinator)
    {
        _context = context;
        Title = RouteTitle(context);
        AutomationId = context == CharacterCustomDrugContext.Creation
            ? "creation-custom-drug-lab-page"
            : "career-custom-drug-lab-page";
        Content = new ScrollView { Content = _body };
    }

    public static string RouteTitle(CharacterCustomDrugContext context)
        => context == CharacterCustomDrugContext.Creation
            ? Text("Creation custom drug lab")
            : Text("Career custom drug lab");

    public static string RouteDetail(CharacterCustomDrugContext context)
        => context == CharacterCustomDrugContext.Creation
            ? Text("Design an exact recipe contribution for the atomic creation finalizer")
            : Text("Design, confirm, recover, and undo one exact saved recipe");

    protected override void Refresh()
    {
        _body.Clear();
        _body.Add(NativeTheme.Eyebrow(
            _context == CharacterCustomDrugContext.Creation
                ? Text("SR5 Creation · Typed finalizer draft")
                : Text("SR5 Career · Atomic recipe receipt")));
        _body.Add(NativeTheme.Title(Text("Custom drug lab")));
        _body.Add(NativeTheme.Body(
            _context == CharacterCustomDrugContext.Creation
                ? Text("Build a source-bound recipe here. Confirmation queues a typed contribution; it never writes creation XML directly.")
                : Text("Build and review a source-bound recipe, then confirm one idempotent Core commit against the clean saved runner."),
            NativeTheme.Muted));

        Sr5CustomDrugLabSnapshot snapshot = Coordinator.LoadCustomDrugLab(_context);
        if (!snapshot.IsReady || snapshot.Preparation is not { } preparation)
        {
            AddBlockers(snapshot.Blockers);
            return;
        }

        AddBinding(preparation);
        AddNotice(_operationNotice ?? snapshot.Notice);
        if (snapshot.Checkpoint?.Phase == Sr5CustomDrugCheckpointPhase.RecoveryUnknown)
        {
            AddRecoveryUnknown();
            return;
        }
        if (snapshot.IsQueuedForFinalization)
        {
            AddQueued(snapshot);
            return;
        }
        if (snapshot.HasAppliedReceipt)
        {
            AddReceipt(snapshot);
            return;
        }

        AddName(snapshot);
        AddGrade(snapshot);
        AddCategoryRoutes(snapshot);
        AddPreview(snapshot);
        AddActions(snapshot);
    }

    private void AddBinding(CharacterCustomDrugPreparation preparation)
    {
        VerticalStackLayout card = new() { Spacing = 6 };
        card.Add(NativeTheme.Metric(
            Text("Saved revision"),
            preparation.ContentRevision.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric(Text("Settings profile"), preparation.SettingsProfileId));
        card.Add(NativeTheme.Metric(Text("Catalog"), ShortDigest(preparation.CatalogDigest)));
        card.Add(NativeTheme.Metric(Text("Rules"), ShortDigest(preparation.RulesDigest)));
        card.Add(NativeTheme.Metric(
            Text("Maximum components"),
            preparation.Policy.MaximumComponents.ToString(CultureInfo.InvariantCulture)));
        Border border = NativeTheme.Card(card);
        border.AutomationId = "custom-drug-authority-binding";
        _body.Add(border);
    }

    private void AddNotice(string? notice)
    {
        if (string.IsNullOrWhiteSpace(notice))
            return;
        string message = notice switch
        {
            Sr5CustomDrugLabNotices.DraftRestored =>
                Text("The saved phone draft was restored for this exact runner revision."),
            Sr5CustomDrugLabNotices.ReviewStale =>
                Text("The runner, catalog, or rules changed. The selection was preserved, but the old review was discarded."),
            Sr5CustomDrugLabNotices.ReviewReady =>
                Text("The exact preview is durable. Confirm it separately before anything is queued or saved."),
            Sr5CustomDrugLabNotices.QueuedForFinalization =>
                Text("The typed recipe contribution is queued for the atomic whole-character finalizer."),
            Sr5CustomDrugLabNotices.CommitRecovered =>
                Text("Receipt lookup proved the interrupted Career commit was already saved."),
            Sr5CustomDrugLabNotices.CommitApplied =>
                Text("Core saved the recipe and receipt on one exact runner revision."),
            Sr5CustomDrugLabNotices.CommitNotApplied =>
                Text("Core proved no recipe was saved. Review and confirm again."),
            Sr5CustomDrugLabNotices.UndoApplied =>
                Text("The receipt-bound recipe was removed in one atomic saved revision."),
            _ => Text("The outcome cannot be proven from the current receipt and runner revision.")
        };
        Label label = NativeTheme.Body(message, NativeTheme.Muted);
        label.AutomationId = "custom-drug-resume-notice";
        _body.Add(NativeTheme.Card(label));
    }

    private void AddName(Sr5CustomDrugLabSnapshot snapshot)
    {
        _body.Add(NativeTheme.FieldLabel(Text("Recipe name")));
        Entry name = NativeTheme.TextField(
            "custom-drug-name",
            snapshot.Selection.Name,
            Text("Enter a unique recipe name"));
        name.MaxLength = CharacterCustomDrugRules.MaximumNameLength;
        name.Unfocused += async (_, _) =>
        {
            string requested = name.Text?.Trim() ?? string.Empty;
            if (string.Equals(requested, snapshot.Selection.Name, StringComparison.Ordinal))
                return;
            await RunAsync(() =>
            {
                Coordinator.UpdateCustomDrugSelection(
                    _context,
                    snapshot.Selection with { Name = requested });
                _operationNotice = null;
                return Task.CompletedTask;
            });
        };
        _body.Add(name);
    }

    private void AddGrade(Sr5CustomDrugLabSnapshot snapshot)
    {
        CharacterCustomDrugGrade? grade = snapshot.Preparation!.Grades
            .SingleOrDefault(candidate => candidate.Id == snapshot.Selection.GradeId);
        _body.Add(NativeTheme.NavigationRow(
            Text("Grade"),
            grade is null
                ? Text("Choose one exact grade")
                : Format("{0} · source {1}", grade.Name, grade.SourceBook),
            () => Navigation.PushAsync(new Sr5CustomDrugGradePage(Coordinator, _context)),
            automationId: "custom-drug-grade-route"));
    }

    private void AddCategoryRoutes(Sr5CustomDrugLabSnapshot snapshot)
    {
        _body.Add(NativeTheme.Eyebrow(Text("Recipe components")));
        AddCategoryRoute(snapshot, CharacterCustomDrugComponentCategory.Foundation);
        AddCategoryRoute(snapshot, CharacterCustomDrugComponentCategory.Block);
        AddCategoryRoute(snapshot, CharacterCustomDrugComponentCategory.Enhancer);
    }

    private void AddCategoryRoute(
        Sr5CustomDrugLabSnapshot snapshot,
        CharacterCustomDrugComponentCategory category)
    {
        HashSet<CharacterCustomDrugComponentId> identities = snapshot.Preparation!.Components
            .Where(component => component.Category == category)
            .Select(component => component.Id)
            .ToHashSet();
        int selected = snapshot.Selection.Components.Count(item => identities.Contains(item.ComponentId));
        string detail = category == CharacterCustomDrugComponentCategory.Foundation
            ? Format("{0} selected · exactly one required", selected)
            : Format("{0} selected · source limits apply", selected);
        _body.Add(NativeTheme.NavigationRow(
            CategoryLabel(category),
            detail,
            () => Navigation.PushAsync(new Sr5CustomDrugCatalogPage(
                Coordinator,
                _context,
                category)),
            automationId: $"custom-drug-category-{CategoryToken(category)}"));
    }

    private void AddPreview(Sr5CustomDrugLabSnapshot snapshot)
    {
        _body.Add(NativeTheme.Eyebrow(Text("Exact effect and cost preview")));
        if (snapshot.Quote is not { Exact: true } quote)
        {
            Label blocker = NativeTheme.Body(
                snapshot.Quote?.BlockReason ?? CharacterCustomDrugBlockers.AuthorityUnavailable,
                NativeTheme.Danger);
            blocker.AutomationId = "custom-drug-preview-blocker";
            _body.Add(NativeTheme.Card(blocker));
            return;
        }

        VerticalStackLayout totals = new() { Spacing = 6 };
        totals.Add(NativeTheme.Metric(Text("Grade"), quote.GradeName));
        totals.Add(NativeTheme.Metric(Text("Unit cost"), Nuyen(quote.UnitCost)));
        totals.Add(NativeTheme.Metric(Text("Charged now"), Nuyen(quote.ChargedCost)));
        totals.Add(NativeTheme.Metric(
            Text("Availability"),
            Format("{0} · {1}", quote.Availability, LegalityLabel(quote.Legality))));
        totals.Add(NativeTheme.Metric(
            Text("Addiction"),
            Format("rating {0} · threshold {1}", quote.AddictionRating, quote.AddictionThreshold)));
        totals.Add(NativeTheme.Body(
            Text("Recipe definition always queues one free, unstolen initial dose with no markup or expense."),
            NativeTheme.Muted));
        Border totalsCard = NativeTheme.Card(totals);
        totalsCard.AutomationId = "custom-drug-preview-totals";
        _body.Add(totalsCard);

        AddEffects(quote.Effects);
        foreach (CharacterCustomDrugSelectedComponent component in quote.Components)
        {
            VerticalStackLayout item = new() { Spacing = 5 };
            item.Add(NativeTheme.Title(component.Name, 19));
            item.Add(NativeTheme.Body(
                Format(
                    "{0} · level {1} · {2} · availability {3}",
                    CategoryLabel(component.Category),
                    DisplayLevel(component.Level),
                    Nuyen(component.CostContribution),
                    component.AvailabilityContribution),
                NativeTheme.Muted));
            item.Add(NativeTheme.Body(
                Format("Source {0} {1}", component.SourceBook, component.Page),
                NativeTheme.Muted));
            Border card = NativeTheme.Card(item);
            card.AutomationId = $"custom-drug-preview-component-{component.ComponentId.Value:N}-{component.Level}";
            _body.Add(card);
        }
    }

    private void AddEffects(CharacterCustomDrugAggregateEffects effects)
    {
        VerticalStackLayout card = new() { Spacing = 5 };
        foreach (CharacterCustomDrugAttributeEffect effect in effects.Attributes)
            card.Add(NativeTheme.Metric(effect.Attribute, Signed(effect.Value)));
        foreach (CharacterCustomDrugLimitEffect effect in effects.Limits)
            card.Add(NativeTheme.Metric(effect.Limit, Signed(effect.Value)));
        foreach (CharacterCustomDrugQualityEffect effect in effects.Qualities)
            card.Add(NativeTheme.Metric(effect.Name, effect.Rating.ToString(CultureInfo.InvariantCulture)));
        foreach (string information in effects.Information)
            card.Add(NativeTheme.Body(information, NativeTheme.Muted));
        card.Add(NativeTheme.Metric(Text("Initiative"), Signed(effects.Initiative)));
        card.Add(NativeTheme.Metric(Text("Initiative dice"), Signed(effects.InitiativeDice)));
        card.Add(NativeTheme.Metric(Text("Crash damage"), Signed(effects.CrashDamage)));
        card.Add(NativeTheme.Metric(Text("Speed"), Signed(effects.Speed)));
        card.Add(NativeTheme.Metric(Text("Duration"), Signed(effects.Duration)));
        Border border = NativeTheme.Card(card);
        border.AutomationId = "custom-drug-preview-effects";
        _body.Add(border);
    }

    private void AddActions(Sr5CustomDrugLabSnapshot snapshot)
    {
        if (snapshot.CanReview)
        {
            Button review = NativeTheme.PrimaryButton(Text("Lock exact review"));
            review.AutomationId = "custom-drug-review";
            review.Clicked += async (_, _) => await RunAsync(() =>
            {
                Sr5CustomDrugLabSnapshot reviewed = Coordinator.ReviewCustomDrug(_context);
                _operationNotice = reviewed.Notice;
                return Task.CompletedTask;
            });
            _body.Add(review);
        }
        if (!snapshot.CanConfirm)
            return;

        Button confirm = NativeTheme.PrimaryButton(
            _context == CharacterCustomDrugContext.Creation
                ? Text("Confirm finalizer contribution")
                : Text("Confirm and save recipe"));
        confirm.AutomationId = "custom-drug-confirm";
        confirm.Clicked += async (_, _) => await RunAsync(async () =>
        {
            bool accepted = await DisplayAlertAsync(
                Text("Confirm exact recipe"),
                _context == CharacterCustomDrugContext.Creation
                    ? Text("Queue this reviewed recipe for the atomic whole-character finalizer? No character XML is written now.")
                    : Text("Save this reviewed recipe as one free initial dose on the exact current Career revision?"),
                Text("Confirm"),
                Text("Keep reviewing"));
            if (!accepted)
                return;
            Sr5CustomDrugLabSnapshot result = _context == CharacterCustomDrugContext.Creation
                ? Coordinator.ConfirmCreationCustomDrug()
                : await Coordinator.ConfirmCareerCustomDrugAsync();
            _operationNotice = result.Notice;
        });
        _body.Add(confirm);
    }

    private void AddQueued(Sr5CustomDrugLabSnapshot snapshot)
    {
        VerticalStackLayout card = new() { Spacing = 7 };
        card.Add(NativeTheme.Eyebrow(Text("Queued creation contribution")));
        card.Add(NativeTheme.Title(snapshot.Selection.Name, 21));
        card.Add(NativeTheme.Body(
            Text("The atomic finalizer must re-prepare and re-quote this typed selection before its single whole-character write."),
            NativeTheme.Muted));
        if (snapshot.Checkpoint?.CreationContribution is { } contribution)
            card.Add(NativeTheme.Metric(Text("Contribution"), ShortDigest(contribution.ContributionDigest)));
        Button edit = NativeTheme.SecondaryButton(Text("Edit queued recipe"));
        edit.AutomationId = "custom-drug-edit-queued";
        edit.Clicked += async (_, _) => await RunAsync(() =>
        {
            Coordinator.StartEditingCustomDrug(_context);
            _operationNotice = null;
            return Task.CompletedTask;
        });
        card.Add(edit);
        Border border = NativeTheme.Card(card);
        border.AutomationId = "custom-drug-queued-card";
        _body.Add(border);
    }

    private void AddReceipt(Sr5CustomDrugLabSnapshot snapshot)
    {
        CharacterCustomDrugCommitReceipt receipt = snapshot.Checkpoint!.Receipt!;
        VerticalStackLayout card = new() { Spacing = 7 };
        card.Add(NativeTheme.Eyebrow(Text("Verified Career receipt")));
        card.Add(NativeTheme.Title(snapshot.Selection.Name, 21));
        card.Add(NativeTheme.Metric(
            Text("Saved revision"),
            receipt.ContentRevision.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric(Text("Receipt"), ShortDigest(receipt.ReceiptDigest)));
        Button undo = NativeTheme.SecondaryButton(Text("Undo saved recipe"));
        undo.AutomationId = "custom-drug-undo";
        undo.Clicked += async (_, _) => await RunAsync(async () =>
        {
            bool accepted = await DisplayAlertAsync(
                Text("Undo exact recipe"),
                Text("Remove only the recipe proven by this receipt in one new atomic saved revision?"),
                Text("Undo"),
                Text("Keep recipe"));
            if (!accepted)
                return;
            Sr5CustomDrugLabSnapshot result = await Coordinator.UndoCareerCustomDrugAsync();
            _operationNotice = result.Notice;
        });
        card.Add(undo);
        Border border = NativeTheme.Card(card);
        border.AutomationId = "custom-drug-receipt-card";
        _body.Add(border);
    }

    private void AddRecoveryUnknown()
    {
        Label blocker = NativeTheme.Body(
            Text("Recovery is locked because neither the exact saved receipt nor an unchanged pre-commit revision can be proven. Reopen the runner or use support recovery; this phone will not retry the mutation."),
            NativeTheme.Danger);
        blocker.AutomationId = "custom-drug-recovery-unknown";
        _body.Add(NativeTheme.Card(blocker));
    }

    private void AddBlockers(IReadOnlyList<string> blockers)
    {
        VerticalStackLayout card = new() { Spacing = 6 };
        card.Add(NativeTheme.Eyebrow(Text("Custom drug authority unavailable")));
        foreach (string blocker in blockers.DefaultIfEmpty(CharacterCustomDrugBlockers.AuthorityUnavailable))
            card.Add(NativeTheme.Body(blocker, NativeTheme.Danger));
        Border border = NativeTheme.Card(card);
        border.AutomationId = "custom-drug-authority-blockers";
        _body.Add(border);
    }

    internal static string CategoryLabel(CharacterCustomDrugComponentCategory category)
        => category switch
        {
            CharacterCustomDrugComponentCategory.Foundation => Text("Foundation"),
            CharacterCustomDrugComponentCategory.Block => Text("Block"),
            CharacterCustomDrugComponentCategory.Enhancer => Text("Enhancer"),
            _ => throw new ArgumentOutOfRangeException(nameof(category))
        };

    internal static string CategoryToken(CharacterCustomDrugComponentCategory category)
        => category.ToString().ToLowerInvariant();

    internal static string DisplayLevel(int zeroBasedLevel)
        => (zeroBasedLevel + 1).ToString(CultureInfo.InvariantCulture);

    private static string LegalityLabel(CharacterCustomDrugLegality legality)
        => legality switch
        {
            CharacterCustomDrugLegality.Legal => Text("legal"),
            CharacterCustomDrugLegality.Restricted => Text("restricted"),
            CharacterCustomDrugLegality.Forbidden => Text("forbidden"),
            _ => throw new ArgumentOutOfRangeException(nameof(legality))
        };

    private static string Nuyen(decimal value)
        => Format("{0} nuyen", value.ToString("0.##", CultureInfo.CurrentCulture));

    private static string Signed(decimal value)
        => value > 0m ? $"+{value.ToString(CultureInfo.CurrentCulture)}" : value.ToString(CultureInfo.CurrentCulture);

    private static string Signed(int value)
        => value > 0 ? $"+{value.ToString(CultureInfo.CurrentCulture)}" : value.ToString(CultureInfo.CurrentCulture);

    private static string ShortDigest(string value)
        => string.IsNullOrWhiteSpace(value) ? "—" : value[..Math.Min(16, value.Length)] + "…";
}

public sealed class Sr5CustomDrugGradePage : NativePageBase
{
    private readonly CharacterCustomDrugContext _context;
    private readonly VerticalStackLayout _body = new()
    {
        Padding = new Thickness(20, 18, 20, 40),
        Spacing = 14
    };

    public Sr5CustomDrugGradePage(
        RunnerSessionCoordinator coordinator,
        CharacterCustomDrugContext context) : base(coordinator)
    {
        _context = context;
        Title = Text("Choose grade");
        AutomationId = "custom-drug-grade-page";
        Content = new ScrollView { Content = _body };
    }

    protected override void Refresh()
    {
        _body.Clear();
        _body.Add(NativeTheme.Eyebrow(Text("Exact source grades")));
        _body.Add(NativeTheme.Title(Text("Choose grade")));
        Sr5CustomDrugLabSnapshot snapshot = Coordinator.LoadCustomDrugLab(_context);
        if (!snapshot.IsReady || snapshot.Preparation is null)
            return;
        foreach (CharacterCustomDrugGrade grade in snapshot.Preparation.Grades
                     .OrderBy(item => item.Name, StringComparer.CurrentCulture))
        {
            string selected = grade.Id == snapshot.Selection.GradeId ? Text("Selected · ") : string.Empty;
            _body.Add(NativeTheme.NavigationRow(
                grade.Name,
                Format(
                    "{0}source {1} · cost ×{2} · threshold {3}",
                    selected,
                    grade.SourceBook,
                    grade.CostMultiplier,
                    grade.AddictionThresholdModifier),
                async () =>
                {
                    Coordinator.UpdateCustomDrugSelection(
                        _context,
                        snapshot.Selection with { GradeId = grade.Id });
                    await Navigation.PopAsync();
                },
                automationId: $"custom-drug-grade-{grade.Id.Value:N}"));
        }
    }
}

public sealed class Sr5CustomDrugCatalogPage : NativePageBase
{
    private readonly CharacterCustomDrugContext _context;
    private readonly CharacterCustomDrugComponentCategory _category;
    private readonly VerticalStackLayout _body = new()
    {
        Padding = new Thickness(20, 18, 20, 40),
        Spacing = 14
    };

    public Sr5CustomDrugCatalogPage(
        RunnerSessionCoordinator coordinator,
        CharacterCustomDrugContext context,
        CharacterCustomDrugComponentCategory category) : base(coordinator)
    {
        _context = context;
        _category = category;
        Title = Sr5CustomDrugLabPage.CategoryLabel(category);
        AutomationId = $"custom-drug-category-{Sr5CustomDrugLabPage.CategoryToken(category)}-page";
        Content = new ScrollView { Content = _body };
    }

    protected override void Refresh()
    {
        _body.Clear();
        _body.Add(NativeTheme.Eyebrow(Text("Exact component catalog")));
        _body.Add(NativeTheme.Title(Sr5CustomDrugLabPage.CategoryLabel(_category)));
        _body.Add(NativeTheme.Body(
            _category == CharacterCustomDrugComponentCategory.Foundation
                ? Text("Choose exactly one Foundation. Choosing another Foundation replaces the current one.")
                : Text("Add effect levels only within each component's source limit and the recipe-wide maximum."),
            NativeTheme.Muted));
        Sr5CustomDrugLabSnapshot snapshot = Coordinator.LoadCustomDrugLab(_context);
        if (!snapshot.IsReady || snapshot.Preparation is null)
            return;
        foreach (CharacterCustomDrugComponentSource component in snapshot.Preparation.Components
                     .Where(item => item.Category == _category)
                     .OrderBy(item => item.Name, StringComparer.CurrentCulture))
        {
            int count = snapshot.Selection.Components.Count(item => item.ComponentId == component.Id);
            _body.Add(NativeTheme.NavigationRow(
                component.Name,
                Format(
                    "{0} selected · limit {1} · availability {2} · {3} {4}",
                    count,
                    component.Limit,
                    component.AvailabilityModifier,
                    component.SourceBook,
                    component.Page),
                () => Navigation.PushAsync(new Sr5CustomDrugComponentPage(
                    Coordinator,
                    _context,
                    component.Id)),
                automationId: $"custom-drug-component-{component.Id.Value:N}"));
        }
    }
}

public sealed class Sr5CustomDrugComponentPage : NativePageBase
{
    private readonly CharacterCustomDrugContext _context;
    private readonly CharacterCustomDrugComponentId _componentId;
    private readonly VerticalStackLayout _body = new()
    {
        Padding = new Thickness(20, 18, 20, 40),
        Spacing = 14
    };

    public Sr5CustomDrugComponentPage(
        RunnerSessionCoordinator coordinator,
        CharacterCustomDrugContext context,
        CharacterCustomDrugComponentId componentId) : base(coordinator)
    {
        _context = context;
        _componentId = componentId;
        Title = Text("Component levels");
        AutomationId = $"custom-drug-component-{componentId.Value:N}-page";
        Content = new ScrollView { Content = _body };
    }

    protected override void Refresh()
    {
        _body.Clear();
        Sr5CustomDrugLabSnapshot snapshot = Coordinator.LoadCustomDrugLab(_context);
        CharacterCustomDrugComponentSource? component = snapshot.Preparation?.Components
            .SingleOrDefault(item => item.Id == _componentId);
        if (!snapshot.IsReady || snapshot.Preparation is null || component is null)
            return;

        _body.Add(NativeTheme.Eyebrow(Sr5CustomDrugLabPage.CategoryLabel(component.Category)));
        _body.Add(NativeTheme.Title(component.Name));
        _body.Add(NativeTheme.Body(
            Format(
                "Source {0} {1} · source limit {2} · {3} per selected level",
                component.SourceBook,
                component.Page,
                component.Limit,
                Format("{0} nuyen", component.CostPerLevel)),
            NativeTheme.Muted));

        CharacterCustomDrugComponentSelection[] selected = snapshot.Selection.Components
            .Where(item => item.ComponentId == component.Id)
            .ToArray();
        if (selected.Length > 0)
        {
            _body.Add(NativeTheme.Eyebrow(Text("Selected levels")));
            for (int index = 0; index < selected.Length; index++)
            {
                CharacterCustomDrugComponentSelection item = selected[index];
                Button remove = NativeTheme.SecondaryButton(
                    Format("Remove level {0}", Sr5CustomDrugLabPage.DisplayLevel(item.Level)));
                remove.AutomationId = $"custom-drug-remove-{component.Id.Value:N}-{index}";
                remove.Clicked += async (_, _) => await RunAsync(() =>
                {
                    List<CharacterCustomDrugComponentSelection> updated = snapshot.Selection.Components.ToList();
                    int absolute = updated.FindIndex(candidate => candidate.ComponentId == component.Id
                        && candidate.Level == item.Level);
                    if (absolute >= 0)
                        updated.RemoveAt(absolute);
                    Coordinator.UpdateCustomDrugSelection(
                        _context,
                        snapshot.Selection with { Components = updated.ToArray() });
                    return Task.CompletedTask;
                });
                _body.Add(remove);
            }
        }

        _body.Add(NativeTheme.Eyebrow(Text("Available effect levels")));
        foreach (CharacterCustomDrugEffectLevel effect in component.Effects.OrderBy(item => item.Level))
        {
            bool replacesFoundation = component.Category == CharacterCustomDrugComponentCategory.Foundation
                && snapshot.Selection.Components.Any(item => snapshot.Preparation.Components.Any(source =>
                    source.Category == CharacterCustomDrugComponentCategory.Foundation
                    && source.Id == item.ComponentId));
            bool totalRoom = replacesFoundation
                || snapshot.Selection.Components.Count < snapshot.Preparation.Policy.MaximumComponents;
            bool componentRoom = component.Category == CharacterCustomDrugComponentCategory.Foundation
                || component.Limit <= 0
                || selected.Length < component.Limit;
            Button add = NativeTheme.PrimaryButton(
                Format("Add level {0}", Sr5CustomDrugLabPage.DisplayLevel(effect.Level)));
            add.AutomationId = $"custom-drug-add-{component.Id.Value:N}-{effect.Level}";
            add.IsEnabled = totalRoom && componentRoom;
            add.Clicked += async (_, _) => await RunAsync(() =>
            {
                List<CharacterCustomDrugComponentSelection> updated = snapshot.Selection.Components.ToList();
                if (component.Category == CharacterCustomDrugComponentCategory.Foundation)
                {
                    HashSet<CharacterCustomDrugComponentId> foundations = snapshot.Preparation.Components
                        .Where(item => item.Category == CharacterCustomDrugComponentCategory.Foundation)
                        .Select(item => item.Id)
                        .ToHashSet();
                    updated.RemoveAll(item => foundations.Contains(item.ComponentId));
                }
                updated.Add(new CharacterCustomDrugComponentSelection(component.Id, effect.Level));
                Coordinator.UpdateCustomDrugSelection(
                    _context,
                    snapshot.Selection with { Components = updated.ToArray() });
                return Task.CompletedTask;
            });
            VerticalStackLayout card = new() { Spacing = 6 };
            card.Add(EffectSummary(effect));
            card.Add(add);
            Border border = NativeTheme.Card(card);
            border.AutomationId = $"custom-drug-effect-{component.Id.Value:N}-{effect.Level}";
            _body.Add(border);
        }
    }

    private static View EffectSummary(CharacterCustomDrugEffectLevel effect)
    {
        VerticalStackLayout summary = new() { Spacing = 4 };
        summary.Add(NativeTheme.Title(
            Format("Level {0}", Sr5CustomDrugLabPage.DisplayLevel(effect.Level)),
            19));
        foreach (CharacterCustomDrugAttributeEffect value in effect.Attributes)
            summary.Add(NativeTheme.Metric(value.Attribute, value.Value.ToString(CultureInfo.CurrentCulture)));
        foreach (CharacterCustomDrugLimitEffect value in effect.Limits)
            summary.Add(NativeTheme.Metric(value.Limit, value.Value.ToString(CultureInfo.CurrentCulture)));
        foreach (CharacterCustomDrugQualityEffect value in effect.Qualities)
            summary.Add(NativeTheme.Metric(value.Name, value.Rating.ToString(CultureInfo.CurrentCulture)));
        foreach (string value in effect.Information)
            summary.Add(NativeTheme.Body(value, NativeTheme.Muted));
        summary.Add(NativeTheme.Body(
            Format(
                "Initiative {0} · dice {1} · crash {2} · speed {3} · duration {4}",
                effect.Initiative,
                effect.InitiativeDice,
                effect.CrashDamage,
                effect.Speed,
                effect.Duration),
            NativeTheme.Muted));
        return summary;
    }
}
