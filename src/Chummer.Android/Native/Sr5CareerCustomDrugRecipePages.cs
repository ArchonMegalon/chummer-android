using System.Globalization;
using Chummer.Contracts.Characters;
using static Chummer.Android.Native.Sr5CareerFlowStrings;

namespace Chummer.Android.Native;

public sealed class Sr5CareerCustomDrugRecipePage : NativePageBase
{
    private readonly VerticalStackLayout _body = new()
    {
        Padding = new Thickness(20, 18, 20, 40),
        Spacing = 14
    };
    private string? _operationNotice;

    public Sr5CareerCustomDrugRecipePage(RunnerSessionCoordinator coordinator) : base(coordinator)
    {
        Title = Text("Custom drug recipe");
        AutomationId = "sr5-career-custom-drug-recipe-page";
        Content = new ScrollView { Content = _body };
    }

    protected override void Refresh()
    {
        _body.Clear();
        _body.Add(NativeTheme.Eyebrow(Text("SR5 Career · Free initial dose")));
        _body.Add(NativeTheme.Title(Text("Define custom drug")));
        _body.Add(NativeTheme.Body(
            Text("Name the recipe, choose one exact grade and exactly one Foundation, then add bounded Blocks or Enhancers. Core calculates every effect and saves one unstolen free initial dose."),
            NativeTheme.Muted));

        Sr5CareerCustomDrugRecipeSnapshot snapshot = Coordinator.LoadCareerCustomDrugRecipe();
        if (!snapshot.IsReady || snapshot.Preparation is not { } preparation)
        {
            AddBlockers(snapshot.Blockers);
            return;
        }

        AddBinding(preparation);
        AddNotice(_operationNotice ?? snapshot.Notice);
        if (snapshot.IsRecoveryUnknown)
        {
            AddRecoveryUnknown();
            return;
        }
        if (snapshot.HasAppliedReceipt)
        {
            AddReceipt(snapshot);
            return;
        }

        AddRecipe(snapshot);
        AddQuote(snapshot);
        AddActions(snapshot);
    }

    private void AddBinding(CharacterCustomDrugPreparation preparation)
    {
        VerticalStackLayout card = new() { Spacing = 6 };
        card.Add(NativeTheme.Metric(
            Text("Saved revision"),
            preparation.ContentRevision.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric(Text("Available nuyen"), Nuyen(preparation.AvailableNuyen)));
        card.Add(NativeTheme.Metric(Text("Settings profile"), preparation.SettingsProfileId));
        card.Add(NativeTheme.Metric(Text("Catalog"), ShortDigest(preparation.CatalogDigest)));
        card.Add(NativeTheme.Metric(Text("Rules"), ShortDigest(preparation.RulesDigest)));
        card.Add(NativeTheme.Metric(
            Text("Maximum components"),
            preparation.Policy.MaximumComponents.ToString(CultureInfo.InvariantCulture)));
        Border border = NativeTheme.Card(card);
        border.AutomationId = "career-custom-drug-recipe-binding";
        _body.Add(border);
    }

    private void AddRecipe(Sr5CareerCustomDrugRecipeSnapshot snapshot)
    {
        bool canEdit = snapshot.Checkpoint?.Phase is null or Sr5CareerCustomDrugRecipePhase.Editing;
        CharacterCustomDrugSelection selection = snapshot.Selection;
        Entry name = NativeTheme.TextField(
            "career-custom-drug-recipe-name",
            selection.Name,
            Text("Enter a bounded recipe name"));
        name.MaxLength = CharacterCustomDrugRules.MaximumNameLength;
        name.IsEnabled = canEdit;
        VerticalStackLayout nameField = new() { Spacing = 5 };
        nameField.Add(NativeTheme.FieldLabel(Text("Recipe name")));
        nameField.Add(name);
        _body.Add(nameField);

        CharacterCustomDrugGrade? grade = snapshot.Preparation!.Grades.SingleOrDefault(candidate =>
            candidate.Id == selection.GradeId);
        _body.Add(NativeTheme.NavigationRow(
            Text("Grade"),
            grade?.Name ?? Text("Choose one exact grade identity"),
            () => Navigation.PushAsync(new Sr5CareerCustomDrugGradePage(Coordinator)),
            enabled: canEdit,
            automationId: "career-custom-drug-grade-route"));
        _body.Add(NativeTheme.NavigationRow(
            Text("Components"),
            selection.Components.Count == 0
                ? Text("Choose exactly one Foundation")
                : Format("{0} selected · choose Foundation, Blocks, or Enhancers", selection.Components.Count),
            () => Navigation.PushAsync(new Sr5CareerCustomDrugComponentPage(Coordinator)),
            enabled: canEdit,
            automationId: "career-custom-drug-components-route"));

        if (selection.Components.Count == 0)
        {
            _body.Add(NativeTheme.Body(Text("No components selected"), NativeTheme.Muted));
        }
        else
        {
            _body.Add(NativeTheme.Eyebrow(Text("Selected components")));
            for (int index = 0; index < selection.Components.Count; index++)
                AddSelectedComponent(snapshot, index, canEdit);
        }

        if (!canEdit)
            return;
        Button update = NativeTheme.SecondaryButton(Text("Update recipe quote"));
        update.AutomationId = "career-custom-drug-recipe-update";
        update.Clicked += async (_, _) => await RunAsync(() =>
        {
            Coordinator.UpdateCareerCustomDrugRecipeSelection(selection with
            {
                Name = name.Text ?? string.Empty
            });
            _operationNotice = null;
            return Task.CompletedTask;
        });
        _body.Add(update);
    }

    private void AddSelectedComponent(
        Sr5CareerCustomDrugRecipeSnapshot snapshot,
        int index,
        bool canEdit)
    {
        CharacterCustomDrugComponentSelection selected = snapshot.Selection.Components[index];
        CharacterCustomDrugComponentSource? source = snapshot.Preparation!.Components.SingleOrDefault(candidate =>
            candidate.Id == selected.ComponentId);
        if (source is null)
            return;
        VerticalStackLayout card = new() { Spacing = 5 };
        card.Add(NativeTheme.Title(source.Name, 18));
        card.Add(NativeTheme.Body(
            Format("{0} · level {1} · {2} {3}",
                Category(source.Category),
                selected.Level,
                source.SourceBook,
                source.Page),
            NativeTheme.Muted));
        Button remove = NativeTheme.SecondaryButton(Text("Remove component"));
        remove.AutomationId = $"career-custom-drug-remove-{index}";
        remove.IsEnabled = canEdit;
        remove.Clicked += async (_, _) => await RunAsync(() =>
        {
            List<CharacterCustomDrugComponentSelection> components = snapshot.Selection.Components.ToList();
            components.RemoveAt(index);
            Coordinator.UpdateCareerCustomDrugRecipeSelection(snapshot.Selection with
            {
                Components = components
            });
            return Task.CompletedTask;
        });
        card.Add(remove);
        _body.Add(NativeTheme.Card(card));
    }

    private void AddQuote(Sr5CareerCustomDrugRecipeSnapshot snapshot)
    {
        _body.Add(NativeTheme.Eyebrow(Text("Exact Core quote")));
        if (snapshot.Quote is not { Exact: true } quote)
        {
            Label blocker = NativeTheme.Body(
                snapshot.Quote?.BlockReason ?? CharacterCustomDrugBlockers.AuthorityUnavailable,
                NativeTheme.Danger);
            blocker.AutomationId = "career-custom-drug-quote-blocker";
            _body.Add(NativeTheme.Card(blocker));
            return;
        }

        VerticalStackLayout card = new() { Spacing = 6 };
        card.Add(NativeTheme.Metric(Text("Recipe"), quote.Name));
        card.Add(NativeTheme.Metric(Text("Grade"), quote.GradeName));
        card.Add(NativeTheme.Metric(Text("Component cost"), Nuyen(quote.ComponentCost)));
        card.Add(NativeTheme.Metric(Text("Unit cost"), Nuyen(quote.UnitCost)));
        card.Add(NativeTheme.Metric(Text("Charged now"), Nuyen(quote.ChargedCost)));
        card.Add(NativeTheme.Metric(
            Text("Availability"),
            Format("{0} · {1}", quote.Availability, Legality(quote.Legality))));
        card.Add(NativeTheme.Metric(
            Text("Addiction"),
            Format("rating {0} / threshold {1}", quote.AddictionRating, quote.AddictionThreshold)));
        card.Add(NativeTheme.Metric(
            Text("Effects"),
            Format(
                "attributes {0} · limits {1} · qualities {2} · information {3}",
                quote.Effects.Attributes.Count,
                quote.Effects.Limits.Count,
                quote.Effects.Qualities.Count,
                quote.Effects.Information.Count)));
        card.Add(NativeTheme.Metric(
            Text("Timing effects"),
            Format(
                "initiative {0} · dice {1} · crash {2} · speed {3} · duration {4}",
                quote.Effects.Initiative,
                quote.Effects.InitiativeDice,
                quote.Effects.CrashDamage,
                quote.Effects.Speed,
                quote.Effects.Duration)));
        card.Add(NativeTheme.Metric(Text("Quote"), ShortDigest(quote.QuoteDigest)));
        Border border = NativeTheme.Card(card);
        border.AutomationId = "career-custom-drug-quote";
        _body.Add(border);
    }

    private void AddActions(Sr5CareerCustomDrugRecipeSnapshot snapshot)
    {
        if (snapshot.CanReview)
        {
            Button review = NativeTheme.PrimaryButton(Text("Lock exact recipe review"));
            review.AutomationId = "career-custom-drug-review";
            review.Clicked += async (_, _) => await RunAsync(() =>
            {
                Sr5CareerCustomDrugRecipeSnapshot reviewed = Coordinator.ReviewCareerCustomDrugRecipe();
                _operationNotice = reviewed.Notice;
                return Task.CompletedTask;
            });
            _body.Add(review);
        }
        if (!snapshot.CanConfirm)
            return;

        AddReviewDiff(snapshot);
        Button confirm = NativeTheme.PrimaryButton(Text("Confirm and save recipe"));
        confirm.AutomationId = "career-custom-drug-confirm";
        confirm.Clicked += async (_, _) => await RunAsync(async () =>
        {
            bool accepted = await DisplayAlertAsync(
                Text("Confirm exact custom-drug recipe"),
                Text("Save this digest-bound recipe and its one unstolen free initial dose to the current clean Career revision?"),
                Text("Confirm"),
                Text("Keep reviewing"));
            if (!accepted)
                return;
            Sr5CareerCustomDrugRecipeSnapshot result =
                await Coordinator.ConfirmCareerCustomDrugRecipeAsync();
            _operationNotice = result.Notice;
        });
        _body.Add(confirm);
    }

    private void AddReviewDiff(Sr5CareerCustomDrugRecipeSnapshot snapshot)
    {
        CharacterCustomDrugCommitCommand command = snapshot.Checkpoint!.Command!;
        CharacterCustomDrugQuote quote = snapshot.Quote!;
        VerticalStackLayout card = new() { Spacing = 6 };
        card.Add(NativeTheme.Eyebrow(Text("Review exact diff")));
        card.Add(NativeTheme.Metric(Text("New recipe"), quote.Name));
        card.Add(NativeTheme.Metric(
            Text("Initial quantity"),
            command.Selection.Quantity.ToString("0.##", CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric(Text("Charged now"), Nuyen(quote.ChargedCost)));
        card.Add(NativeTheme.Metric(
            Text("New drug instance"),
            command.NewDrugInstanceId.Value.ToString("D")));
        card.Add(NativeTheme.Metric(
            Text("New component instances"),
            command.NewComponentInstanceIds.Count.ToString(CultureInfo.InvariantCulture)));
        Label sideEffects = NativeTheme.Body(
            Text("Confirm creates exactly one recipe with one unstolen free initial dose in one atomic saved revision. It creates no expense and performs no later quantity purchase."),
            NativeTheme.Muted);
        sideEffects.AutomationId = "career-custom-drug-review-side-effects";
        card.Add(sideEffects);
        Border border = NativeTheme.Card(card);
        border.AutomationId = "career-custom-drug-review-diff";
        _body.Add(border);
    }

    private void AddReceipt(Sr5CareerCustomDrugRecipeSnapshot snapshot)
    {
        CharacterCustomDrugCommitReceipt receipt = snapshot.Checkpoint!.Receipt!;
        VerticalStackLayout card = new() { Spacing = 7 };
        card.Add(NativeTheme.Eyebrow(Text("Verified custom-drug receipt")));
        card.Add(NativeTheme.Title(snapshot.Quote?.Name ?? Text("Custom drug recipe"), 21));
        card.Add(NativeTheme.Metric(
            Text("Saved revision"),
            receipt.ContentRevision.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric(
            Text("Drug instance"),
            receipt.DrugInstanceId.Value.ToString("D")));
        card.Add(NativeTheme.Metric(
            Text("Component instances"),
            receipt.ComponentInstanceIds.Count.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric(Text("Receipt"), ShortDigest(receipt.ReceiptDigest)));

        Button undo = NativeTheme.SecondaryButton(Text("Undo this recipe"));
        undo.AutomationId = "career-custom-drug-undo";
        undo.Clicked += async (_, _) => await RunAsync(async () =>
        {
            bool accepted = await DisplayAlertAsync(
                Text("Undo exact custom-drug recipe"),
                Text("Remove only the recipe and initial dose proven by this receipt in one new atomic saved revision?"),
                Text("Undo"),
                Text("Keep recipe"));
            if (!accepted)
                return;
            Sr5CareerCustomDrugRecipeSnapshot result =
                await Coordinator.UndoCareerCustomDrugRecipeAsync();
            _operationNotice = result.Notice;
        });
        card.Add(undo);

        Button next = NativeTheme.SecondaryButton(Text("Finish receipt and start another recipe"));
        next.AutomationId = "career-custom-drug-reopen";
        next.Clicked += async (_, _) => await RunAsync(async () =>
        {
            bool accepted = await DisplayAlertAsync(
                Text("Start another recipe"),
                Text("Close this phone undo receipt and prepare a new recipe from the current saved revision?"),
                Text("Continue"),
                Text("Keep receipt"));
            if (!accepted)
                return;
            Sr5CareerCustomDrugRecipeSnapshot result = Coordinator.ReopenCareerCustomDrugRecipe();
            _operationNotice = result.Notice;
        });
        card.Add(next);
        Border border = NativeTheme.Card(card);
        border.AutomationId = "career-custom-drug-receipt";
        _body.Add(border);
    }

    private void AddRecoveryUnknown()
    {
        Label blocker = NativeTheme.Body(
            Text("Recovery is locked because neither the exact saved receipt nor an unchanged pre-commit revision can be proven. This phone will not replay the recipe mutation."),
            NativeTheme.Danger);
        blocker.AutomationId = "career-custom-drug-recovery-unknown";
        _body.Add(NativeTheme.Card(blocker));
    }

    private void AddNotice(string? notice)
    {
        if (string.IsNullOrWhiteSpace(notice))
            return;
        string message = notice switch
        {
            Sr5CareerCustomDrugRecipeNotices.DraftRestored =>
                Text("The phone recipe draft was restored for this exact runner and catalog revision."),
            Sr5CareerCustomDrugRecipeNotices.ReviewStale =>
                Text("The runner, catalog, or rules changed. Invalid choices were removed and the old review was discarded."),
            Sr5CareerCustomDrugRecipeNotices.ReviewReady =>
                Text("The exact Core quote and new stable identities are durably reviewed. Confirm separately."),
            Sr5CareerCustomDrugRecipeNotices.CommitApplied =>
                Text("Core saved the recipe, its free initial dose, and the receipt atomically."),
            Sr5CareerCustomDrugRecipeNotices.CommitRecovered =>
                Text("Receipt lookup proved the interrupted recipe was already saved."),
            Sr5CareerCustomDrugRecipeNotices.CommitNotApplied =>
                Text("Core proved the recipe was not saved. Review the current quote before confirming again."),
            Sr5CareerCustomDrugRecipeNotices.UndoApplied =>
                Text("The receipt-bound custom-drug recipe was undone in one saved revision."),
            Sr5CareerCustomDrugRecipeNotices.Reopened =>
                Text("A new recipe draft is bound to the current saved runner revision."),
            _ => Text("The recipe outcome is not provable from the current receipt and runner revision.")
        };
        Label label = NativeTheme.Body(message, NativeTheme.Muted);
        label.AutomationId = "career-custom-drug-notice";
        _body.Add(NativeTheme.Card(label));
    }

    private void AddBlockers(IReadOnlyList<string> blockers)
    {
        VerticalStackLayout card = new() { Spacing = 6 };
        card.Add(NativeTheme.Eyebrow(Text("Custom-drug recipe authority unavailable")));
        foreach (string blocker in blockers.DefaultIfEmpty(CharacterCustomDrugBlockers.AuthorityUnavailable))
            card.Add(NativeTheme.Body(blocker, NativeTheme.Danger));
        Border border = NativeTheme.Card(card);
        border.AutomationId = "career-custom-drug-blockers";
        _body.Add(border);
    }

    private static string Nuyen(decimal value)
        => Format("{0} nuyen", value.ToString("0.##", CultureInfo.CurrentCulture));

    private static string ShortDigest(string value)
        => value.Length <= 19 ? value : value[..19] + "…";

    private static string Category(CharacterCustomDrugComponentCategory category)
        => category switch
        {
            CharacterCustomDrugComponentCategory.Foundation => Text("Foundation"),
            CharacterCustomDrugComponentCategory.Block => Text("Block"),
            CharacterCustomDrugComponentCategory.Enhancer => Text("Enhancer"),
            _ => Text("Component")
        };

    private static string Legality(CharacterCustomDrugLegality legality)
        => legality switch
        {
            CharacterCustomDrugLegality.Legal => Text("Legal"),
            CharacterCustomDrugLegality.Restricted => Text("Restricted"),
            CharacterCustomDrugLegality.Forbidden => Text("Forbidden"),
            _ => Text("Unknown")
        };
}

public sealed class Sr5CareerCustomDrugGradePage : NativePageBase
{
    private readonly VerticalStackLayout _body = new()
    {
        Padding = new Thickness(20, 18, 20, 40),
        Spacing = 12
    };

    public Sr5CareerCustomDrugGradePage(RunnerSessionCoordinator coordinator) : base(coordinator)
    {
        Title = Text("Choose custom-drug grade");
        AutomationId = "sr5-career-custom-drug-grade-page";
        Content = new ScrollView { Content = _body };
    }

    protected override void Refresh()
    {
        _body.Clear();
        _body.Add(NativeTheme.Eyebrow(Text("Source-authorized custom-drug grades")));
        _body.Add(NativeTheme.Title(Text("Choose custom-drug grade")));
        Sr5CareerCustomDrugRecipeSnapshot snapshot = Coordinator.LoadCareerCustomDrugRecipe();
        if (!snapshot.IsReady || snapshot.Preparation is not { } preparation)
        {
            AddUnavailable();
            return;
        }
        foreach (CharacterCustomDrugGrade grade in preparation.Grades)
        {
            _body.Add(NativeTheme.NavigationRow(
                grade.Name,
                Format("cost × {0} · addiction threshold {1} · {2}",
                    grade.CostMultiplier,
                    grade.AddictionThresholdModifier,
                    grade.SourceBook),
                () => SelectAsync(snapshot, grade),
                automationId: $"career-custom-drug-grade-{grade.Id.Value:N}"));
        }
    }

    private async Task SelectAsync(
        Sr5CareerCustomDrugRecipeSnapshot snapshot,
        CharacterCustomDrugGrade grade)
    {
        Coordinator.UpdateCareerCustomDrugRecipeSelection(snapshot.Selection with
        {
            GradeId = grade.Id
        });
        await Navigation.PopAsync();
    }

    private void AddUnavailable()
    {
        Label blocker = NativeTheme.Body(
            Text("The exact custom-drug catalog changed or became unavailable. Return to the recipe page."),
            NativeTheme.Danger);
        blocker.AutomationId = "career-custom-drug-grade-unavailable";
        _body.Add(NativeTheme.Card(blocker));
    }
}

public sealed class Sr5CareerCustomDrugComponentPage : NativePageBase
{
    private readonly VerticalStackLayout _body = new()
    {
        Padding = new Thickness(20, 18, 20, 40),
        Spacing = 12
    };

    public Sr5CareerCustomDrugComponentPage(RunnerSessionCoordinator coordinator) : base(coordinator)
    {
        Title = Text("Choose custom-drug components");
        AutomationId = "sr5-career-custom-drug-component-page";
        Content = new ScrollView { Content = _body };
    }

    protected override void Refresh()
    {
        _body.Clear();
        _body.Add(NativeTheme.Eyebrow(Text("Exact component identities and levels")));
        _body.Add(NativeTheme.Title(Text("Choose custom-drug components")));
        _body.Add(NativeTheme.Body(
            Text("Choosing a Foundation replaces the previous Foundation. Blocks and Enhancers may repeat only within their source limit and the Core maximum."),
            NativeTheme.Muted));
        Sr5CareerCustomDrugRecipeSnapshot snapshot = Coordinator.LoadCareerCustomDrugRecipe();
        if (!snapshot.IsReady || snapshot.Preparation is not { } preparation)
        {
            AddUnavailable();
            return;
        }

        foreach (IGrouping<CharacterCustomDrugComponentCategory, CharacterCustomDrugComponentSource> category in
                 preparation.Components
                     .GroupBy(static component => component.Category)
                     .OrderBy(static group => group.Key))
        {
            _body.Add(NativeTheme.Eyebrow(Category(category.Key)));
            foreach (CharacterCustomDrugComponentSource component in category
                         .OrderBy(static item => item.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                foreach (CharacterCustomDrugEffectLevel effect in component.Effects.OrderBy(static item => item.Level))
                {
                    int existing = snapshot.Selection.Components.Count(selected =>
                        selected.ComponentId == component.Id);
                    bool foundation = component.Category == CharacterCustomDrugComponentCategory.Foundation;
                    bool withinComponentLimit = foundation || component.Limit <= 0 || existing < component.Limit;
                    bool withinTotalLimit = foundation
                        || snapshot.Selection.Components.Count < preparation.Policy.MaximumComponents;
                    _body.Add(NativeTheme.NavigationRow(
                        component.Name,
                        Format("level {0} · cost {1} · availability {2} · {3} {4}",
                            effect.Level,
                            component.CostPerLevel,
                            component.AvailabilityModifier,
                            component.SourceBook,
                            component.Page),
                        () => SelectAsync(snapshot, component, effect),
                        enabled: withinComponentLimit && withinTotalLimit,
                        automationId: $"career-custom-drug-component-{component.Id.Value:N}-{effect.Level}"));
                }
            }
        }
    }

    private async Task SelectAsync(
        Sr5CareerCustomDrugRecipeSnapshot snapshot,
        CharacterCustomDrugComponentSource source,
        CharacterCustomDrugEffectLevel effect)
    {
        List<CharacterCustomDrugComponentSelection> components = snapshot.Selection.Components.ToList();
        if (source.Category == CharacterCustomDrugComponentCategory.Foundation)
        {
            HashSet<CharacterCustomDrugComponentId> foundations = snapshot.Preparation!.Components
                .Where(static component => component.Category == CharacterCustomDrugComponentCategory.Foundation)
                .Select(static component => component.Id)
                .ToHashSet();
            components.RemoveAll(selected => foundations.Contains(selected.ComponentId));
        }
        components.Add(new CharacterCustomDrugComponentSelection(source.Id, effect.Level));
        Coordinator.UpdateCareerCustomDrugRecipeSelection(snapshot.Selection with
        {
            Components = components
        });
        await Navigation.PopAsync();
    }

    private void AddUnavailable()
    {
        Label blocker = NativeTheme.Body(
            Text("The exact custom-drug catalog changed or became unavailable. Return to the recipe page."),
            NativeTheme.Danger);
        blocker.AutomationId = "career-custom-drug-component-unavailable";
        _body.Add(NativeTheme.Card(blocker));
    }

    private static string Category(CharacterCustomDrugComponentCategory category)
        => category switch
        {
            CharacterCustomDrugComponentCategory.Foundation => Text("Foundation"),
            CharacterCustomDrugComponentCategory.Block => Text("Block"),
            CharacterCustomDrugComponentCategory.Enhancer => Text("Enhancer"),
            _ => Text("Component")
        };

}
