using System.Globalization;
using Chummer.Contracts.Characters;
using static Chummer.Android.Native.Sr5CareerFlowStrings;

namespace Chummer.Android.Native;

public sealed class Sr5CareerVehicleWorkshopPage : NativePageBase
{
    private readonly VerticalStackLayout _body = new()
    {
        Padding = new Thickness(20, 18, 20, 40),
        Spacing = 14
    };
    private string? _operationNotice;

    public Sr5CareerVehicleWorkshopPage(RunnerSessionCoordinator coordinator) : base(coordinator)
    {
        Title = Text("Vehicle and drone workshop");
        AutomationId = "sr5-career-vehicle-workshop-page";
        Content = new ScrollView { Content = _body };
    }

    protected override void Refresh()
    {
        _body.Clear();
        _body.Add(NativeTheme.Eyebrow(Text("SR5 Career · Vehicle workshop")));
        _body.Add(NativeTheme.Title(Text("Build a vehicle or drone")));
        _body.Add(NativeTheme.Body(
            Text("Choose one exact source chassis, add compatible modifications, review Core's cost and legality, then confirm one atomic saved purchase."),
            NativeTheme.Muted));

        Sr5CareerVehicleWorkshopSnapshot snapshot = Coordinator.LoadCareerVehicleWorkshop();
        if (!snapshot.IsReady || snapshot.Preparation is not { } preparation)
        {
            AddBlockers(snapshot.Blockers);
            return;
        }
        AddBinding(preparation);
        AddNotice(_operationNotice ?? snapshot.Notice);
        if (snapshot.IsRecoveryUnknown)
        {
            Label locked = NativeTheme.Body(
                Text("The purchase outcome is uncertain. This draft is locked so the mutation cannot be replayed."),
                NativeTheme.Danger);
            locked.AutomationId = "career-vehicle-workshop-recovery-unknown";
            _body.Add(NativeTheme.Card(locked));
            return;
        }
        if (snapshot.HasAppliedReceipt)
        {
            AddReceipt(snapshot);
            return;
        }
        AddComposition(snapshot);
        AddQuote(snapshot);
        AddActions(snapshot);
    }

    private void AddBinding(CharacterVehicleWorkshopPreparation preparation)
    {
        VerticalStackLayout card = new() { Spacing = 6 };
        card.Add(NativeTheme.Metric(Text("Saved revision"),
            preparation.ContentRevision.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric(Text("Available nuyen"), Nuyen(preparation.AvailableNuyen)));
        card.Add(NativeTheme.Metric(Text("Settings profile"), preparation.Binding.ProfileId));
        card.Add(NativeTheme.Metric(Text("Catalog"), ShortDigest(preparation.CatalogDigest)));
        card.Add(NativeTheme.Metric(Text("Restricted price multiplier"),
            preparation.Binding.MultiplyRestrictedCost
                ? preparation.Binding.RestrictedCostMultiplier.ToString("0.##×", CultureInfo.CurrentCulture)
                : Text("Not enabled")));
        card.Add(NativeTheme.Metric(Text("Forbidden price multiplier"),
            preparation.Binding.MultiplyForbiddenCost
                ? preparation.Binding.ForbiddenCostMultiplier.ToString("0.##×", CultureInfo.CurrentCulture)
                : Text("Not enabled")));
        card.Add(NativeTheme.Body(
            Text("Core applies only the saved rules-profile price multipliers. Manual markup or discount is not available in this wizard."),
            NativeTheme.Muted));
        Border border = NativeTheme.Card(card);
        border.AutomationId = "career-vehicle-workshop-binding";
        _body.Add(border);
    }

    private void AddComposition(Sr5CareerVehicleWorkshopSnapshot snapshot)
    {
        bool canEdit = snapshot.Checkpoint?.Phase is null or Sr5CareerVehicleWorkshopPhase.Editing;
        CharacterVehicleWorkshopChassisEntry? chassis = snapshot.Preparation!.Chassis.SingleOrDefault(
            candidate => candidate.SourceId == snapshot.Selection.ChassisSourceId);
        string kind = chassis?.Kind == CharacterVehicleChassisKind.Drone
            ? Text("Drone")
            : Text("Vehicle");
        _body.Add(NativeTheme.NavigationRow(
            Text("Chassis"),
            chassis is null ? Text("Choose one exact vehicle or drone")
                : Format("{0} · {1} · {2}", kind, chassis.Name, Nuyen(chassis.Cost)),
            () => Navigation.PushAsync(new Sr5CareerVehicleChassisPage(Coordinator)),
            canEdit,
            "career-vehicle-workshop-chassis-route"));

        Entry customName = NativeTheme.TextField(
            "career-vehicle-workshop-name",
            snapshot.Selection.CustomName,
            Text("Optional runner name for this vehicle"));
        customName.MaxLength = CharacterVehicleWorkshopRules.MaximumCustomNameLength;
        customName.IsEnabled = canEdit;
        VerticalStackLayout field = new() { Spacing = 5 };
        field.Add(NativeTheme.FieldLabel(Text("Custom vehicle name")));
        field.Add(customName);
        _body.Add(field);

        _body.Add(NativeTheme.NavigationRow(
            Text("Modifications"),
            snapshot.Selection.Modifications.Count == 0
                ? Text("Add compatible modifications")
                : Format("{0} modifications selected", snapshot.Selection.Modifications.Count),
            () => Navigation.PushAsync(new Sr5CareerVehicleModificationPage(Coordinator)),
            canEdit && chassis is not null,
            "career-vehicle-workshop-modifications-route"));
        _body.Add(NativeTheme.Body(
            Text("Weapon-mount composition is not part of this first phone slice; Android never guesses its required four component identities."),
            NativeTheme.Muted));

        if (!canEdit)
            return;
        Button update = NativeTheme.SecondaryButton(Text("Update workshop quote"));
        update.AutomationId = "career-vehicle-workshop-update";
        update.Clicked += async (_, _) => await RunAsync(() =>
        {
            Coordinator.UpdateCareerVehicleWorkshopSelection(snapshot.Selection with
            {
                CustomName = customName.Text ?? string.Empty
            });
            _operationNotice = null;
            return Task.CompletedTask;
        });
        _body.Add(update);
    }

    private void AddQuote(Sr5CareerVehicleWorkshopSnapshot snapshot)
    {
        _body.Add(NativeTheme.Eyebrow(Text("Exact Core quote")));
        CharacterVehicleWorkshopQuote? quote = snapshot.Quote;
        if (quote is not { Exact: true })
        {
            string reason = quote?.Blockers.FirstOrDefault()
                ?? CharacterVehicleWorkshopBlockers.SourceAuthorityUnavailable;
            Label blocker = NativeTheme.Body(reason, NativeTheme.Danger);
            blocker.AutomationId = "career-vehicle-workshop-quote-blocker";
            _body.Add(NativeTheme.Card(blocker));
            return;
        }
        VerticalStackLayout card = new() { Spacing = 6 };
        card.Add(NativeTheme.Metric(Text("Vehicle or drone"), quote.DisplayName));
        card.Add(NativeTheme.Metric(Text("Adjusted total"), Nuyen(quote.TotalCost)));
        card.Add(NativeTheme.Metric(Text("Charged now"), Nuyen(-quote.NuyenDelta)));
        card.Add(NativeTheme.Metric(Text("Availability"),
            Format("{0} · {1}", quote.Availability.Value, Legality(quote.Availability.Legality))));
        card.Add(NativeTheme.Metric(Text("Modification slots"),
            Format("{0} used · {1} remaining", quote.SlotsUsed, quote.SlotsRemaining)));
        card.Add(NativeTheme.Metric(Text("Modification capacity"),
            Format("{0} used · {1} remaining", quote.CapacityUsed, quote.CapacityRemaining)));
        card.Add(NativeTheme.Metric(Text("Quote"), ShortDigest(quote.QuoteDigest)));
        Border border = NativeTheme.Card(card);
        border.AutomationId = "career-vehicle-workshop-quote";
        _body.Add(border);
    }

    private void AddActions(Sr5CareerVehicleWorkshopSnapshot snapshot)
    {
        if (snapshot.CanReview)
        {
            Button review = NativeTheme.PrimaryButton(Text("Lock exact workshop review"));
            review.AutomationId = "career-vehicle-workshop-review";
            review.Clicked += async (_, _) => await RunAsync(() =>
            {
                _operationNotice = Coordinator.ReviewCareerVehicleWorkshop().Notice;
                return Task.CompletedTask;
            });
            _body.Add(review);
        }
        if (!snapshot.CanConfirm)
            return;
        CharacterVehicleWorkshopCommitCommand command = snapshot.Checkpoint!.Command!;
        VerticalStackLayout diff = new() { Spacing = 6 };
        diff.Add(NativeTheme.Eyebrow(Text("Review exact diff")));
        diff.Add(NativeTheme.Metric(Text("New instance"),
            command.Selection.NewVehicleInstanceId.Value.ToString("D")));
        diff.Add(NativeTheme.Metric(Text("Expense receipt"), command.NewExpenseId.ToString("D")));
        diff.Add(NativeTheme.Metric(Text("Nuyen change"), Nuyen(snapshot.Quote!.NuyenDelta)));
        diff.Add(NativeTheme.Metric(Text("Quote"), ShortDigest(command.ExpectedQuoteDigest)));
        _body.Add(NativeTheme.Card(diff));

        Button confirm = NativeTheme.PrimaryButton(Text("Confirm and save purchase"));
        confirm.AutomationId = "career-vehicle-workshop-confirm";
        confirm.Clicked += async (_, _) => await RunAsync(async () =>
        {
            bool accepted = await DisplayAlertAsync(
                Text("Confirm exact vehicle purchase"),
                Text("Save this digest-bound vehicle or drone, modifications, nuyen change, and expense receipt to the current clean Career revision?"),
                Text("Confirm"), Text("Keep reviewing"));
            if (!accepted)
                return;
            _operationNotice = (await Coordinator.ConfirmCareerVehicleWorkshopAsync()).Notice;
        });
        _body.Add(confirm);
    }

    private void AddReceipt(Sr5CareerVehicleWorkshopSnapshot snapshot)
    {
        CharacterVehicleWorkshopCommitReceipt receipt = snapshot.Checkpoint!.Receipt!;
        VerticalStackLayout card = new() { Spacing = 6 };
        card.Add(NativeTheme.Eyebrow(Text("Saved purchase receipt")));
        card.Add(NativeTheme.Metric(Text("Vehicle or drone"),
            receipt.VehicleInstanceId.Value.ToString("D")));
        card.Add(NativeTheme.Metric(Text("Saved revision"),
            receipt.ContentRevision.ToString(CultureInfo.InvariantCulture)));
        card.Add(NativeTheme.Metric(Text("Nuyen change"), Nuyen(receipt.NuyenDelta)));
        card.Add(NativeTheme.Metric(Text("Receipt"), ShortDigest(receipt.ReceiptDigest)));
        Border border = NativeTheme.Card(card);
        border.AutomationId = "career-vehicle-workshop-receipt";
        _body.Add(border);

        Button undo = NativeTheme.SecondaryButton(Text("Undo this purchase"));
        undo.AutomationId = "career-vehicle-workshop-undo";
        undo.IsEnabled = receipt.UndoReady;
        undo.Clicked += async (_, _) => await RunAsync(async () =>
            _operationNotice = (await Coordinator.UndoCareerVehicleWorkshopAsync()).Notice);
        _body.Add(undo);
        Button next = NativeTheme.PrimaryButton(Text("Start another vehicle or drone"));
        next.AutomationId = "career-vehicle-workshop-reopen";
        next.Clicked += async (_, _) => await RunAsync(() =>
        {
            _operationNotice = Coordinator.ReopenCareerVehicleWorkshop().Notice;
            return Task.CompletedTask;
        });
        _body.Add(next);
    }

    private void AddNotice(string? notice)
    {
        if (string.IsNullOrWhiteSpace(notice))
            return;
        string message = notice switch
        {
            Sr5CareerVehicleWorkshopNotices.DraftRestored =>
                Text("The phone workshop draft was restored for this exact runner and catalog revision."),
            Sr5CareerVehicleWorkshopNotices.ReviewStale =>
                Text("The runner or vehicle catalog changed. Invalid choices were removed and the old review was discarded."),
            Sr5CareerVehicleWorkshopNotices.ReviewReady =>
                Text("The exact Core workshop quote and stable identities are durably reviewed. Confirm separately."),
            Sr5CareerVehicleWorkshopNotices.CommitApplied =>
                Text("Core saved the vehicle or drone, modifications, nuyen change, and expense receipt atomically."),
            Sr5CareerVehicleWorkshopNotices.CommitRecovered =>
                Text("Core recovery proved the interrupted vehicle purchase was already saved."),
            Sr5CareerVehicleWorkshopNotices.CommitNotApplied =>
                Text("Core proved the vehicle purchase was not saved. Review the current quote before confirming again."),
            Sr5CareerVehicleWorkshopNotices.UndoApplied =>
                Text("The receipt-bound vehicle purchase was undone in one saved revision."),
            Sr5CareerVehicleWorkshopNotices.Reopened =>
                Text("A new vehicle workshop draft is bound to the current saved runner revision."),
            _ => Text("The vehicle purchase outcome is not provable from the current receipt and runner revision.")
        };
        Label label = NativeTheme.Body(message, NativeTheme.Muted);
        label.AutomationId = "career-vehicle-workshop-notice";
        _body.Add(label);
    }

    private void AddBlockers(IEnumerable<string> blockers)
    {
        foreach (string blocker in blockers.Distinct(StringComparer.Ordinal))
            _body.Add(NativeTheme.Body(blocker, NativeTheme.Danger));
    }

    private static string ShortDigest(string value)
        => string.IsNullOrWhiteSpace(value) ? "—" : value[..Math.Min(12, value.Length)];

    private static string Nuyen(decimal value)
        => string.Format(CultureInfo.CurrentCulture, "{0:N0} ¥", value);

    private static string Legality(CharacterVehicleWorkshopLegality legality)
        => legality switch
        {
            CharacterVehicleWorkshopLegality.Restricted => Text("Restricted"),
            CharacterVehicleWorkshopLegality.Forbidden => Text("Forbidden"),
            _ => Text("Legal")
        };
}

public sealed class Sr5CareerVehicleChassisPage : NativePageBase
{
    private readonly VerticalStackLayout _body = new()
    {
        Padding = new Thickness(20, 18, 20, 40),
        Spacing = 12
    };

    public Sr5CareerVehicleChassisPage(RunnerSessionCoordinator coordinator) : base(coordinator)
    {
        Title = Text("Choose chassis");
        AutomationId = "sr5-career-vehicle-chassis-page";
        Content = new ScrollView { Content = _body };
    }

    protected override void Refresh()
    {
        _body.Clear();
        _body.Add(NativeTheme.Title(Text("Choose vehicle or drone chassis")));
        Sr5CareerVehicleWorkshopSnapshot snapshot = Coordinator.LoadCareerVehicleWorkshop();
        if (!snapshot.IsReady || snapshot.Preparation is null)
            return;
        foreach (CharacterVehicleWorkshopChassisEntry chassis in snapshot.Preparation.Chassis
                     .OrderBy(candidate => candidate.Kind)
                     .ThenBy(candidate => candidate.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            bool exact = chassis.ProjectionStatus == CharacterVehicleWorkshopProjectionStatus.Exact;
            string kind = chassis.Kind == CharacterVehicleChassisKind.Drone ? Text("Drone") : Text("Vehicle");
            _body.Add(NativeTheme.NavigationRow(
                chassis.Name,
                exact
                    ? Format("{0} · {1} · slots {2} · {3} {4}", kind,
                        string.Format(CultureInfo.CurrentCulture, "{0:N0} ¥", chassis.Cost),
                        chassis.ModificationSlots, chassis.SourceBook, chassis.Page)
                    : chassis.UnsupportedReason,
                async () =>
                {
                    Coordinator.UpdateCareerVehicleWorkshopSelection(snapshot.Selection with
                    {
                        ChassisSourceId = chassis.SourceId,
                        CustomName = snapshot.Selection.CustomName,
                        GmAuthorityDigest = chassis.Posture == CharacterVehicleChassisPosture.GmApprovedCustom
                            ? chassis.GmAuthorityDigest
                            : string.Empty,
                        Modifications = [],
                        WeaponMounts = []
                    });
                    await Navigation.PopAsync();
                },
                exact,
                $"career-vehicle-chassis-{chassis.SourceId.Value:N}"));
        }
    }
}

public sealed class Sr5CareerVehicleModificationPage : NativePageBase
{
    private readonly VerticalStackLayout _body = new()
    {
        Padding = new Thickness(20, 18, 20, 40),
        Spacing = 12
    };

    public Sr5CareerVehicleModificationPage(RunnerSessionCoordinator coordinator) : base(coordinator)
    {
        Title = Text("Vehicle modifications");
        AutomationId = "sr5-career-vehicle-modifications-page";
        Content = new ScrollView { Content = _body };
    }

    protected override void Refresh()
    {
        _body.Clear();
        _body.Add(NativeTheme.Title(Text("Choose compatible modifications")));
        Sr5CareerVehicleWorkshopSnapshot snapshot = Coordinator.LoadCareerVehicleWorkshop();
        if (!snapshot.IsReady || snapshot.Preparation is null)
            return;
        CharacterVehicleChassisSourceId chassisId = snapshot.Selection.ChassisSourceId;
        foreach (CharacterVehicleWorkshopModificationEntry source in snapshot.Preparation.Modifications
                     .OrderBy(candidate => candidate.Category, StringComparer.CurrentCultureIgnoreCase)
                     .ThenBy(candidate => candidate.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            bool compatible = source.ProjectionStatus == CharacterVehicleWorkshopProjectionStatus.Exact
                && (source.AllowedChassis.Count == 0 || source.AllowedChassis.Contains(chassisId));
            CharacterVehicleWorkshopModificationSelection? selected = snapshot.Selection.Modifications
                .SingleOrDefault(candidate => candidate.SourceId == source.SourceId);
            VerticalStackLayout card = new() { Spacing = 6 };
            card.Add(NativeTheme.Title(source.Name, 18));
            card.Add(NativeTheme.Body(compatible
                ? Format("{0} · rating {1}–{2} · {3} {4}", source.Category,
                    source.MinimumRating, source.MaximumRating, source.SourceBook, source.Page)
                : source.UnsupportedReason.Length == 0
                    ? Text("Not compatible with the selected chassis")
                    : source.UnsupportedReason,
                NativeTheme.Muted));
            if (selected is null)
            {
                Button add = NativeTheme.SecondaryButton(Text("Add modification"));
                add.AutomationId = $"career-vehicle-mod-add-{source.SourceId.Value:N}";
                add.IsEnabled = compatible;
                add.Clicked += async (_, _) => await RunAsync(() =>
                {
                    Coordinator.UpdateCareerVehicleWorkshopSelection(snapshot.Selection with
                    {
                        Modifications = snapshot.Selection.Modifications.Append(
                            new CharacterVehicleWorkshopModificationSelection(
                                source.SourceId,
                                new CharacterVehicleModificationInstanceId(Guid.NewGuid()),
                                source.MinimumRating)).ToArray()
                    });
                    return Task.CompletedTask;
                });
                card.Add(add);
            }
            else
            {
                card.Add(NativeTheme.Metric(Text("Selected rating"),
                    selected.Rating.ToString(CultureInfo.InvariantCulture)));
                HorizontalStackLayout actions = new() { Spacing = 8 };
                Button lower = NativeTheme.SecondaryButton(Text("Lower rating"));
                lower.IsEnabled = selected.Rating > source.MinimumRating;
                lower.Clicked += async (_, _) => await RunAsync(() => ChangeRating(
                    snapshot, selected, selected.Rating - 1));
                Button raise = NativeTheme.SecondaryButton(Text("Raise rating"));
                raise.IsEnabled = selected.Rating < source.MaximumRating;
                raise.Clicked += async (_, _) => await RunAsync(() => ChangeRating(
                    snapshot, selected, selected.Rating + 1));
                Button remove = NativeTheme.SecondaryButton(Text("Remove"));
                remove.Clicked += async (_, _) => await RunAsync(() =>
                {
                    Coordinator.UpdateCareerVehicleWorkshopSelection(snapshot.Selection with
                    {
                        Modifications = snapshot.Selection.Modifications
                            .Where(candidate => candidate.InstanceId != selected.InstanceId).ToArray()
                    });
                    return Task.CompletedTask;
                });
                actions.Add(lower);
                actions.Add(raise);
                actions.Add(remove);
                card.Add(actions);
            }
            _body.Add(NativeTheme.Card(card));
        }
    }

    private Task ChangeRating(
        Sr5CareerVehicleWorkshopSnapshot snapshot,
        CharacterVehicleWorkshopModificationSelection selected,
        int rating)
    {
        Coordinator.UpdateCareerVehicleWorkshopSelection(snapshot.Selection with
        {
            Modifications = snapshot.Selection.Modifications.Select(candidate =>
                candidate.InstanceId == selected.InstanceId
                    ? candidate with { Rating = rating }
                    : candidate).ToArray()
        });
        return Task.CompletedTask;
    }
}
