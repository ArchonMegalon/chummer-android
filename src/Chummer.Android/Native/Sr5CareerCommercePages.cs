using System.Globalization;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Presentation;
using static Chummer.Android.Native.Sr5CareerFlowStrings;

namespace Chummer.Android.Native;

/// <summary>
/// Phone-first entry point for the separate typed Career commerce lanes. It
/// does not provide a generic mutation fallback: every row opens either an
/// existing exact collection editor or Core's bounded purchase authority.
/// </summary>
public sealed class Sr5CareerCommerceHubPage : NativePageBase
{
    private readonly VerticalStackLayout _body = new()
    {
        Padding = new Thickness(20, 18, 20, 40),
        Spacing = 14
    };

    public Sr5CareerCommerceHubPage(RunnerSessionCoordinator coordinator) : base(coordinator)
    {
        Title = Text("Gear and implants");
        AutomationId = "sr5-career-commerce-hub";
        Content = new ScrollView { Content = _body };
    }

    protected override void Refresh()
    {
        _body.Clear();
        _body.Add(NativeTheme.Eyebrow(Text("SR5 Career · Typed commerce")));
        _body.Add(NativeTheme.Title(Text("Gear and implants")));
        _body.Add(NativeTheme.Body(
            Text("Purchase and manage exact Gear, Cyberware, and Bioware identities"),
            NativeTheme.Muted));
        _body.Add(NativeTheme.Body(
            Text("Choose a bounded commerce lane. Each destination reloads its own exact authority before review or persistence."),
            NativeTheme.Muted));

        if (Coordinator.State.Profile?.Created != true
            || !string.Equals(Coordinator.State.Rules?.GameEdition, "SR5", StringComparison.OrdinalIgnoreCase)
            || Coordinator.State.WorkspaceId is null)
        {
            AddUnavailable(Text("This commerce hub requires one loaded, created SR5 runner."));
            return;
        }

        _body.Add(NativeTheme.NavigationRow(
            Text("Purchase cyberware"),
            Text("Source-bound catalog → configuration → Core quote → durable receipt"),
            () => Navigation.PushAsync(new Sr5CareerCyberwarePurchasePage(Coordinator)),
            automationId: "sr5-career-purchase-cyberware"));
        _body.Add(NativeTheme.NavigationRow(
            Text("Installed gear"),
            Text("Open exact saved Gear identities for quantity purchases, reduction, split, merge, and safe edits"),
            () => OpenSectionAsync("tab-gear", Text("Gear")),
            automationId: "sr5-career-installed-gear"));
        _body.Add(NativeTheme.NavigationRow(
            Text("Installed cyberware and bioware"),
            Text("Open exact saved implant identities; eligible Cyberware exposes upgrade and sale quotes"),
            () => OpenSectionAsync("tab-cyberware", Text("Cyberware and bioware")),
            automationId: "sr5-career-installed-implants"));

        Label boundary = NativeTheme.Body(
            Text("New Gear and Bioware catalog purchases remain unavailable until Core exposes their exact source, capacity, availability, side-effect, and receipt authorities. Android never substitutes a generic XML add."),
            NativeTheme.Muted);
        boundary.AutomationId = "sr5-career-commerce-authority-boundary";
        _body.Add(NativeTheme.Card(boundary));
    }

    private async Task OpenSectionAsync(string tabId, string title)
    {
        NavigationTabDefinition? tab = Coordinator.Surface.NavigationTabs.SingleOrDefault(candidate =>
            string.Equals(candidate.Id, tabId, StringComparison.Ordinal));
        if (tab is null || !Coordinator.IsTabEnabled(tab))
        {
            await DisplayAlertAsync(
                Text("Commerce route unavailable"),
                Text("The exact saved-data section is not available for this runner and ruleset."),
                Text("OK"));
            return;
        }

        await Coordinator.SelectTabAsync(tab.Id);
        if (!string.Equals(Coordinator.State.ActiveSectionId, tab.SectionId, StringComparison.Ordinal)
            || Coordinator.State.Error is not null)
        {
            await DisplayAlertAsync(
                Text("Commerce route unavailable"),
                Text("The exact saved-data section could not be loaded. No fallback editor was opened."),
                Text("OK"));
            return;
        }
        await Navigation.PushAsync(new BuildSectionPage(Coordinator, tab.Id, title));
    }

    private void AddUnavailable(string reason)
    {
        Label blocker = NativeTheme.Body(reason, NativeTheme.Danger);
        blocker.AutomationId = "sr5-career-commerce-unavailable";
        _body.Add(NativeTheme.Card(blocker));
    }
}

/// <summary>
/// Native renderer for Core's bounded SR5 Career top-level Cyberware purchase
/// authority. The page owns navigation and explicit confirmation only.
/// </summary>
public sealed class Sr5CareerCyberwarePurchasePage : NativePageBase
{
    private readonly VerticalStackLayout _body = new()
    {
        Padding = new Thickness(20, 18, 20, 40),
        Spacing = 14
    };
    private string? _operationNotice;

    public Sr5CareerCyberwarePurchasePage(RunnerSessionCoordinator coordinator) : base(coordinator)
    {
        Title = Text("Purchase cyberware");
        AutomationId = "sr5-career-cyberware-purchase-page";
        Content = new ScrollView { Content = _body };
    }

    protected override void Refresh()
    {
        _body.Clear();
        _body.Add(NativeTheme.Eyebrow(Text("SR5 Career · Atomic purchase receipt")));
        _body.Add(NativeTheme.Title(Text("Purchase cyberware")));
        _body.Add(NativeTheme.Body(
            Text("Select one exact top-level Cyberware source and grade, review Core's current Nuyen and Essence quote, then confirm separately."),
            NativeTheme.Muted));

        Sr5CareerCyberwarePurchaseSnapshot snapshot = Coordinator.LoadCareerCyberwarePurchase();
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

        AddSelection(snapshot);
        AddConfiguration(snapshot);
        AddQuote(snapshot);
        AddActions(snapshot);
    }

    private void AddBinding(CharacterCyberwarePurchasePreparation preparation)
    {
        VerticalStackLayout card = new() { Spacing = 6 };
        card.Add(NativeTheme.Metric(
            Text("Saved revision"),
            preparation.ContentRevision.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric(Text("Available nuyen"), Nuyen(preparation.AvailableNuyen)));
        card.Add(NativeTheme.Metric(Text("Settings profile"), preparation.SettingsProfileId));
        card.Add(NativeTheme.Metric(Text("Catalog"), ShortDigest(preparation.CatalogDigest)));
        card.Add(NativeTheme.Metric(Text("Source bytes"), ShortDigest(preparation.CyberwareXmlDigest)));
        card.Add(NativeTheme.Metric(
            Text("Excluded unsupported catalog rows"),
            preparation.Exclusions.Count.ToString(CultureInfo.InvariantCulture)));
        Border border = NativeTheme.Card(card);
        border.AutomationId = "career-cyberware-purchase-binding";
        _body.Add(border);
    }

    private void AddSelection(Sr5CareerCyberwarePurchaseSnapshot snapshot)
    {
        CharacterCyberwarePurchaseCatalogEntry? source = snapshot.Preparation!.Entries.SingleOrDefault(candidate =>
            candidate.SourceId == snapshot.Selection.SourceId);
        CharacterCyberwarePurchaseGrade? grade = source?.Grades.SingleOrDefault(candidate =>
            candidate.Id == snapshot.Selection.GradeId);
        _body.Add(NativeTheme.NavigationRow(
            Text("Cyberware"),
            source is null
                ? Text("Choose one exact source identity")
                : Format("{0} · {1} · {2} {3}", source.Name, source.Category, source.SourceBook, source.Page),
            () => Navigation.PushAsync(new Sr5CareerCyberwareCatalogPage(Coordinator)),
            automationId: "career-cyberware-purchase-source-route"));
        _body.Add(NativeTheme.NavigationRow(
            Text("Grade"),
            grade is null ? Text("Choose one exact grade identity") : grade.Name,
            () => Navigation.PushAsync(new Sr5CareerCyberwareGradePage(Coordinator)),
            enabled: source is not null,
            automationId: "career-cyberware-purchase-grade-route"));
    }

    private void AddConfiguration(Sr5CareerCyberwarePurchaseSnapshot snapshot)
    {
        CharacterCyberwarePurchaseSelection original = snapshot.Selection;
        Entry essenceDiscount = NativeTheme.TextField(
            "career-cyberware-purchase-essence-discount",
            original.EssenceDiscountPercent.ToString(CultureInfo.InvariantCulture),
            Text("Integer percentage"));
        // Both exact domains admit negative values; Android's numeric keyboard
        // is not required to expose a minus sign on every device/locale.
        essenceDiscount.Keyboard = Keyboard.Default;
        essenceDiscount.IsEnabled = snapshot.Preparation!.Settings.AllowEssenceDiscounts;
        AddEntry(Text("Essence discount percent"), essenceDiscount);

        Entry markup = NativeTheme.TextField(
            "career-cyberware-purchase-markup",
            original.MarkupPercent.ToString("0.##", CultureInfo.InvariantCulture),
            Text("-99.00 through 1000.00"));
        markup.Keyboard = Keyboard.Default;
        AddEntry(Text("Markup percent"), markup);

        Switch blackMarket = AddSwitch(
            Text("Black-market discount"),
            original.BlackMarketDiscount,
            "career-cyberware-purchase-black-market");
        CharacterCyberwarePurchaseCatalogEntry? source = snapshot.Preparation.Entries.SingleOrDefault(candidate =>
            candidate.SourceId == original.SourceId);
        blackMarket.IsEnabled = source?.BlackMarketEligible == true;
        Switch freeCost = AddSwitch(
            Text("Free cost"),
            original.FreeCost,
            "career-cyberware-purchase-free-cost");

        Button apply = NativeTheme.SecondaryButton(Text("Update exact quote"));
        apply.AutomationId = "career-cyberware-purchase-update-quote";
        apply.Clicked += async (_, _) => await RunAsync(() =>
        {
            if (!int.TryParse(
                    essenceDiscount.Text,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int discount)
                || !decimal.TryParse(
                    markup.Text,
                    NumberStyles.Number,
                    CultureInfo.InvariantCulture,
                    out decimal markupPercent))
            {
                throw new InvalidOperationException(Text("Discount and markup must be exact invariant numbers."));
            }
            Coordinator.UpdateCareerCyberwarePurchaseSelection(original with
            {
                EssenceDiscountPercent = discount,
                MarkupPercent = markupPercent,
                BlackMarketDiscount = blackMarket.IsToggled,
                FreeCost = freeCost.IsToggled
            });
            _operationNotice = null;
            return Task.CompletedTask;
        });
        _body.Add(apply);
    }

    private void AddQuote(Sr5CareerCyberwarePurchaseSnapshot snapshot)
    {
        _body.Add(NativeTheme.Eyebrow(Text("Exact Core quote")));
        if (snapshot.Quote is not { Exact: true } quote)
        {
            Label blocker = NativeTheme.Body(
                snapshot.Quote?.BlockReason ?? CharacterCyberwarePurchaseBlockers.SourceAuthorityUnavailable,
                NativeTheme.Danger);
            blocker.AutomationId = "career-cyberware-purchase-quote-blocker";
            _body.Add(NativeTheme.Card(blocker));
            return;
        }

        CharacterCyberwarePurchaseCatalogEntry source = snapshot.Preparation!.Entries.Single(candidate =>
            candidate.SourceId == quote.SourceId);
        CharacterCyberwarePurchaseGrade grade = source.Grades.Single(candidate =>
            candidate.Id == quote.GradeId);
        VerticalStackLayout card = new() { Spacing = 6 };
        card.Add(NativeTheme.Metric(Text("Item"), quote.Name));
        card.Add(NativeTheme.Metric(Text("Grade"), quote.GradeName));
        card.Add(NativeTheme.Metric(Text("Base cost"), Nuyen(quote.BaseCost)));
        card.Add(NativeTheme.Metric(Text("Charged now"), Nuyen(quote.ChargedCost)));
        card.Add(NativeTheme.Metric(
            Text("Nuyen after purchase"),
            Nuyen(snapshot.Preparation.AvailableNuyen + quote.NuyenDelta)));
        card.Add(NativeTheme.Metric(Text("Source availability"), source.AvailabilityExpression));
        card.Add(NativeTheme.Metric(
            Text("Grade availability modifier"),
            grade.AvailabilityModifier.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric(
            Text("Final availability"),
            Text("Unavailable · Core carries the source expression and grade modifier separately")));
        card.Add(NativeTheme.Metric(
            Text("Karma mutation"),
            Text("None · this bounded purchase command contains no Karma field")));
        card.Add(NativeTheme.Metric(
            Text("Installed Essence"),
            quote.InstalledEssence.ToString("0.####", CultureInfo.CurrentCulture)));
        card.Add(NativeTheme.Metric(
            Text("Essence hole before / after"),
            Format(
                "{0} / {1}",
                StoredRating(snapshot.Preparation.EssenceHoleRating),
                StoredRating(quote.NewEssenceHoleRating))));
        card.Add(NativeTheme.Metric(
            Text("Elapsed time mutation"),
            Text("Unavailable · this bounded command carries no calendar or elapsed-time mutation")));
        card.Add(NativeTheme.Metric(
            Text("Bounded command prerequisites"),
            Text("Satisfied for this exact Core quote; final availability and elapsed time remain unresolved")));
        card.Add(NativeTheme.Metric(Text("Quote"), ShortDigest(quote.QuoteDigest)));
        Border border = NativeTheme.Card(card);
        border.AutomationId = "career-cyberware-purchase-quote";
        _body.Add(border);
    }

    private void AddActions(Sr5CareerCyberwarePurchaseSnapshot snapshot)
    {
        if (snapshot.CanReview)
        {
            Button review = NativeTheme.PrimaryButton(Text("Lock exact review"));
            review.AutomationId = "career-cyberware-purchase-review";
            review.Clicked += async (_, _) => await RunAsync(() =>
            {
                Sr5CareerCyberwarePurchaseSnapshot reviewed = Coordinator.ReviewCareerCyberwarePurchase();
                _operationNotice = reviewed.Notice;
                return Task.CompletedTask;
            });
            _body.Add(review);
        }
        if (!snapshot.CanConfirm)
            return;

        AddReviewDiff(snapshot);

        Button confirm = NativeTheme.PrimaryButton(Text("Confirm and save purchase"));
        confirm.AutomationId = "career-cyberware-purchase-confirm";
        confirm.Clicked += async (_, _) => await RunAsync(async () =>
        {
            bool accepted = await DisplayAlertAsync(
                Text("Confirm exact Cyberware purchase"),
                Text("Apply this digest-bound Core quote to the current clean saved Career revision?"),
                Text("Confirm"),
                Text("Keep reviewing"));
            if (!accepted)
                return;
            Sr5CareerCyberwarePurchaseSnapshot result =
                await Coordinator.ConfirmCareerCyberwarePurchaseAsync();
            _operationNotice = result.Notice;
        });
        _body.Add(confirm);
    }

    private void AddReviewDiff(Sr5CareerCyberwarePurchaseSnapshot snapshot)
    {
        CharacterCyberwarePurchasePreparation preparation = snapshot.Preparation!;
        CharacterCyberwarePurchaseQuote quote = snapshot.Quote!;
        CharacterCyberwarePurchaseCommand command = snapshot.Checkpoint!.Command!;
        VerticalStackLayout card = new() { Spacing = 6 };
        card.Add(NativeTheme.Eyebrow(Text("Review exact diff")));
        card.Add(NativeTheme.Metric(
            Text("Nuyen before / after"),
            Format(
                "{0} / {1}",
                Nuyen(preparation.AvailableNuyen),
                Nuyen(preparation.AvailableNuyen + quote.NuyenDelta))));
        card.Add(NativeTheme.Metric(
            Text("Essence hole before / after"),
            Format(
                "{0} / {1}",
                StoredRating(preparation.EssenceHoleRating),
                StoredRating(quote.NewEssenceHoleRating))));
        card.Add(NativeTheme.Metric(
            Text("New Cyberware instance"),
            command.NewInstanceId.Value.ToString("D")));
        card.Add(NativeTheme.Metric(Text("New Nuyen expense"), command.NewExpenseId.ToString("D")));
        card.Add(NativeTheme.Metric(
            Text("Expense time"),
            command.ExpenseDate.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)));
        Label sideEffects = NativeTheme.Body(
            Text("Confirm creates exactly one Cyberware instance and one Nuyen expense in one atomic saved revision. It does not create a calendar entry or apply elapsed time."),
            NativeTheme.Muted);
        sideEffects.AutomationId = "career-cyberware-purchase-review-side-effects";
        card.Add(sideEffects);
        Border border = NativeTheme.Card(card);
        border.AutomationId = "career-cyberware-purchase-review-diff";
        _body.Add(border);
    }

    private void AddReceipt(Sr5CareerCyberwarePurchaseSnapshot snapshot)
    {
        CharacterCyberwarePurchaseUndoReceipt receipt = snapshot.Checkpoint!.Receipt!;
        VerticalStackLayout card = new() { Spacing = 7 };
        card.Add(NativeTheme.Eyebrow(Text("Verified Career receipt")));
        card.Add(NativeTheme.Title(snapshot.Quote?.Name ?? Text("Cyberware purchase"), 21));
        card.Add(NativeTheme.Metric(
            Text("Saved revision"),
            receipt.ContentRevision.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric(Text("Instance"), receipt.InstanceId.Value.ToString("D")));
        card.Add(NativeTheme.Metric(Text("Expense"), receipt.ExpenseId.ToString("D")));
        card.Add(NativeTheme.Metric(Text("Receipt"), ShortDigest(receipt.ReceiptDigest)));

        Button undo = NativeTheme.SecondaryButton(Text("Undo this purchase"));
        undo.AutomationId = "career-cyberware-purchase-undo";
        undo.Clicked += async (_, _) => await RunAsync(async () =>
        {
            bool accepted = await DisplayAlertAsync(
                Text("Undo exact Cyberware purchase"),
                Text("Remove only the Cyberware and expense proven by this receipt in one new atomic saved revision?"),
                Text("Undo"),
                Text("Keep purchase"));
            if (!accepted)
                return;
            Sr5CareerCyberwarePurchaseSnapshot result =
                await Coordinator.UndoCareerCyberwarePurchaseAsync();
            _operationNotice = result.Notice;
        });
        card.Add(undo);

        Button next = NativeTheme.SecondaryButton(Text("Finish receipt and start another purchase"));
        next.AutomationId = "career-cyberware-purchase-reopen";
        next.Clicked += async (_, _) => await RunAsync(async () =>
        {
            bool accepted = await DisplayAlertAsync(
                Text("Start another purchase"),
                Text("Close this phone undo receipt and prepare a new purchase from the current saved revision?"),
                Text("Continue"),
                Text("Keep receipt"));
            if (!accepted)
                return;
            Sr5CareerCyberwarePurchaseSnapshot result = Coordinator.ReopenCareerCyberwarePurchase();
            _operationNotice = result.Notice;
        });
        card.Add(next);

        Border border = NativeTheme.Card(card);
        border.AutomationId = "career-cyberware-purchase-receipt";
        _body.Add(border);
    }

    private void AddRecoveryUnknown()
    {
        Label blocker = NativeTheme.Body(
            Text("Recovery is locked because neither the exact saved receipt nor an unchanged pre-commit revision can be proven. This phone will not replay the purchase."),
            NativeTheme.Danger);
        blocker.AutomationId = "career-cyberware-purchase-recovery-unknown";
        _body.Add(NativeTheme.Card(blocker));
    }

    private void AddNotice(string? notice)
    {
        if (string.IsNullOrWhiteSpace(notice))
            return;
        string message = notice switch
        {
            Sr5CareerCyberwarePurchaseNotices.DraftRestored =>
                Text("The phone draft was restored for this exact runner and catalog revision."),
            Sr5CareerCyberwarePurchaseNotices.ReviewStale =>
                Text("The runner or catalog changed. The selection was rebound and the old review was discarded."),
            Sr5CareerCyberwarePurchaseNotices.ReviewReady =>
                Text("The exact quote and new stable identities are durably reviewed. Confirm separately."),
            Sr5CareerCyberwarePurchaseNotices.CommitApplied =>
                Text("Core saved the Cyberware, Nuyen expense, Essence bookkeeping, and receipt atomically."),
            Sr5CareerCyberwarePurchaseNotices.CommitRecovered =>
                Text("Receipt lookup proved the interrupted purchase was already saved."),
            Sr5CareerCyberwarePurchaseNotices.CommitNotApplied =>
                Text("Core proved the purchase was not saved. Review the current quote before confirming again."),
            Sr5CareerCyberwarePurchaseNotices.UndoApplied =>
                Text("The receipt-bound Cyberware purchase was undone in one saved revision."),
            Sr5CareerCyberwarePurchaseNotices.Reopened =>
                Text("A new purchase draft is bound to the current saved runner revision."),
            _ => Text("The purchase outcome is not provable from the current receipt and runner revision.")
        };
        Label label = NativeTheme.Body(message, NativeTheme.Muted);
        label.AutomationId = "career-cyberware-purchase-notice";
        _body.Add(NativeTheme.Card(label));
    }

    private void AddBlockers(IReadOnlyList<string> blockers)
    {
        VerticalStackLayout card = new() { Spacing = 6 };
        card.Add(NativeTheme.Eyebrow(Text("Cyberware purchase authority unavailable")));
        foreach (string blocker in blockers.DefaultIfEmpty(
                     CharacterCyberwarePurchaseBlockers.SourceAuthorityUnavailable))
        {
            card.Add(NativeTheme.Body(blocker, NativeTheme.Danger));
        }
        Border border = NativeTheme.Card(card);
        border.AutomationId = "career-cyberware-purchase-blockers";
        _body.Add(border);
    }

    private void AddEntry(string label, Entry entry)
    {
        VerticalStackLayout field = new() { Spacing = 5 };
        field.Add(NativeTheme.FieldLabel(label));
        field.Add(entry);
        _body.Add(field);
    }

    private Switch AddSwitch(string label, bool value, string automationId)
    {
        Switch toggle = new() { IsToggled = value, AutomationId = automationId };
        Grid row = new()
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            ColumnSpacing = 12
        };
        row.Add(NativeTheme.Body(label), 0, 0);
        row.Add(toggle, 1, 0);
        _body.Add(NativeTheme.Card(row));
        return toggle;
    }

    internal static string Nuyen(decimal value)
        => Format("{0} nuyen", value.ToString("0.##", CultureInfo.CurrentCulture));

    internal static string StoredRating(int? value)
        => value?.ToString(CultureInfo.InvariantCulture) ?? Text("not present");

    internal static string ShortDigest(string value)
        => value.Length <= 19 ? value : value[..19] + "…";
}

public sealed class Sr5CareerCyberwareCatalogPage : NativePageBase
{
    private readonly VerticalStackLayout _body = new()
    {
        Padding = new Thickness(20, 18, 20, 40),
        Spacing = 12
    };

    public Sr5CareerCyberwareCatalogPage(RunnerSessionCoordinator coordinator) : base(coordinator)
    {
        Title = Text("Choose cyberware");
        AutomationId = "sr5-career-cyberware-catalog-page";
        Content = new ScrollView { Content = _body };
    }

    protected override void Refresh()
    {
        _body.Clear();
        _body.Add(NativeTheme.Eyebrow(Text("Pinned effective cyberware catalog")));
        _body.Add(NativeTheme.Title(Text("Choose cyberware")));
        Sr5CareerCyberwarePurchaseSnapshot snapshot = Coordinator.LoadCareerCyberwarePurchase();
        if (!snapshot.IsReady || snapshot.Preparation is not { } preparation)
        {
            AddUnavailable();
            return;
        }

        foreach (IGrouping<string, CharacterCyberwarePurchaseCatalogEntry> category in
                 preparation.Entries.GroupBy(static entry => entry.Category, StringComparer.Ordinal)
                     .OrderBy(static group => group.Key, StringComparer.CurrentCultureIgnoreCase))
        {
            _body.Add(NativeTheme.Eyebrow(category.Key));
            foreach (CharacterCyberwarePurchaseCatalogEntry entry in category
                         .OrderBy(static item => item.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                _body.Add(NativeTheme.NavigationRow(
                    entry.Name,
                    Format(
                        "cost {0} · Essence {1} · availability {2} · {3} {4}",
                        entry.CostExpression,
                        entry.EssenceExpression,
                        entry.AvailabilityExpression,
                        entry.SourceBook,
                        entry.Page),
                    () => SelectAsync(snapshot, entry),
                    automationId: $"career-cyberware-source-{entry.SourceId.Value:N}"));
            }
        }
    }

    private async Task SelectAsync(
        Sr5CareerCyberwarePurchaseSnapshot snapshot,
        CharacterCyberwarePurchaseCatalogEntry entry)
    {
        CharacterCyberwarePurchaseGrade grade = entry.Grades.First();
        Coordinator.UpdateCareerCyberwarePurchaseSelection(snapshot.Selection with
        {
            SourceId = entry.SourceId,
            GradeId = grade.Id,
            Rating = 0,
            EssenceDiscountPercent = 0,
            BlackMarketDiscount = false,
            MarkupPercent = 0m,
            FreeCost = false
        });
        await Navigation.PopAsync();
    }

    private void AddUnavailable()
    {
        Label blocker = NativeTheme.Body(
            Text("The exact Cyberware catalog changed or became unavailable. Return to the purchase page."),
            NativeTheme.Danger);
        blocker.AutomationId = "career-cyberware-catalog-unavailable";
        _body.Add(NativeTheme.Card(blocker));
    }

}

public sealed class Sr5CareerCyberwareGradePage : NativePageBase
{
    private readonly VerticalStackLayout _body = new()
    {
        Padding = new Thickness(20, 18, 20, 40),
        Spacing = 12
    };

    public Sr5CareerCyberwareGradePage(RunnerSessionCoordinator coordinator) : base(coordinator)
    {
        Title = Text("Choose grade");
        AutomationId = "sr5-career-cyberware-grade-page";
        Content = new ScrollView { Content = _body };
    }

    protected override void Refresh()
    {
        _body.Clear();
        _body.Add(NativeTheme.Eyebrow(Text("Source-authorized grades")));
        _body.Add(NativeTheme.Title(Text("Choose grade")));
        Sr5CareerCyberwarePurchaseSnapshot snapshot = Coordinator.LoadCareerCyberwarePurchase();
        CharacterCyberwarePurchaseCatalogEntry? source = snapshot.Preparation?.Entries.SingleOrDefault(candidate =>
            candidate.SourceId == snapshot.Selection.SourceId);
        if (!snapshot.IsReady || source is null)
        {
            Label blocker = NativeTheme.Body(
                Text("Choose an exact Cyberware source before selecting its grade."),
                NativeTheme.Danger);
            blocker.AutomationId = "career-cyberware-grade-unavailable";
            _body.Add(NativeTheme.Card(blocker));
            return;
        }

        foreach (CharacterCyberwarePurchaseGrade grade in source.Grades)
        {
            _body.Add(NativeTheme.NavigationRow(
                grade.Name,
                Format(
                    "Cost × {0} · Essence × {1} · availability {2}",
                    grade.CostMultiplier,
                    grade.EssenceMultiplier,
                    grade.AvailabilityModifier),
                () => SelectAsync(snapshot, grade),
                automationId: $"career-cyberware-grade-{grade.Id.Value:N}"));
        }
    }

    private async Task SelectAsync(
        Sr5CareerCyberwarePurchaseSnapshot snapshot,
        CharacterCyberwarePurchaseGrade grade)
    {
        Coordinator.UpdateCareerCyberwarePurchaseSelection(snapshot.Selection with
        {
            GradeId = grade.Id
        });
        await Navigation.PopAsync();
    }
}
