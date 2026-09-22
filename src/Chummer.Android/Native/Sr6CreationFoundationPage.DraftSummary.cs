using Chummer.Contracts.Characters;

namespace Chummer.Android.Native;

internal sealed partial class Sr6CreationFoundationPage
{
    private void BuildDraftSummary(Sr6CreationFoundationState state, Func<bool> current)
    {
        if (state.DraftSummary is not { } summary || summary.Binding != state.Binding)
        { _body.Add(NativeTheme.Body(Sr6CreationCopy.Text("Stale"))); return; }
        _body.Add(NativeTheme.Body(Sr6CreationCopy.Text("DraftHelp"), NativeTheme.Muted));
        var finalization = NativeTheme.Body(Sr6CreationCopy.Text("FinishHelp"), NativeTheme.Muted);
        finalization.AutomationId = "sr6-draft-finalization-help";
        _body.Add(finalization);
        var finish = NativeTheme.PrimaryButton(Sr6CreationCopy.Text("FinishReview"));
        finish.AutomationId = "sr6-draft-review-finalization";
        finish.Clicked += async (_, _) => await RunAsync(async () =>
        {
            if (!current()) return;
            finish.IsEnabled = false;
            try
            {
                var result = await Coordinator.ReviewSr6FinalizationAsync(state, current);
                if (!current()) return;
                if (result.Value is { } review) await Navigation.PushAsync(new Sr6CreationFinalizationPage(Coordinator, review));
                else finalization.Text = string.Join("\n", result.Blockers.Select(Sr6CreationCopy.Blocker));
            }
            finally { if (current()) finish.IsEnabled = true; }
        });
        _body.Add(finish);
        if (summary.Balances is { } balances)
        {
            var cash = NativeTheme.Body(Sr6CreationCopy.DraftBalances(balances));
            cash.AutomationId = "sr6-draft-balances";
            _body.Add(cash);
        }
        if (summary.NaturalValues is { } natural) BuildNaturalValues(natural, current);
        if (summary.PassiveValues is { } passive) BuildPassiveValues(passive, current);
        foreach (var step in summary.Steps)
        {
            var status = NativeTheme.Body(Sr6CreationCopy.Text("DraftStatus." + step.Status));
            status.AutomationId = "sr6-draft-status-" + step.Id;
            var open = NativeTheme.PrimaryButton(Sr6CreationCopy.DraftStepTitle(step.Id));
            open.AutomationId = "sr6-draft-open-" + step.Id;
            open.IsEnabled = current() && step.CanOpen;
            open.Clicked += async (_, _) =>
            {
                if (!current() || !step.CanOpen) return;
                await RunAsync(async () =>
                {
                    if (!current()) return;
                    Sr6CreationFoundationPage? page = step.Id switch
                    {
                        "foundation" => new(Coordinator),
                        "qualities" => new(Coordinator, qualitiesMode: true),
                        "attributes" => new(Coordinator, attributesMode: true),
                        "skills" => new(Coordinator, skillsMode: true),
                        "karma" => new(Coordinator, karmaMode: true),
                        "knowledge" => new(Coordinator, knowledgeMode: true),
                        "contacts" => new(Coordinator, contactsMode: true),
                        "talent" => new(Coordinator, talentMode: true),
                        "forms" => new(Coordinator, formsMode: true),
                        "spells" => new(Coordinator, spellsMode: true),
                        "powers" => new(Coordinator, powersMode: true),
                        "gear" => new(Coordinator, gearMode: true),
                        "lifestyle" => new(Coordinator, lifestyleMode: true),
                        _ => null
                    };
                    if (page is not null) await Navigation.PushAsync(page);
                });
            };
            _body.Add(open);
            _body.Add(status);
            foreach (var remainder in step.Remainders)
                _body.Add(NativeTheme.Body(Sr6CreationCopy.DraftRemainder(remainder), NativeTheme.Muted));
        }
        _body.Add(NativeTheme.Body(string.Join(" · ", summary.SourceAnchorIds), NativeTheme.Muted));
    }

    private void BuildNaturalValues(Sr6CreationNaturalValues values, Func<bool> current)
    {
        var toggle = NativeTheme.PrimaryButton(Sr6CreationCopy.Text("NaturalTitle"));
        toggle.AutomationId = "sr6-draft-natural-toggle";
        var details = new VerticalStackLayout { Spacing = 10, IsVisible = false, AutomationId = "sr6-draft-natural-values" };
        details.Add(NativeTheme.Body(Sr6CreationCopy.Text("NaturalHelp"), NativeTheme.Muted));
        if (values.Attributes is null)
            details.Add(NativeTheme.Body(Sr6CreationCopy.NaturalMissing("AttributeTitle"), NativeTheme.Muted));
        else foreach (var attribute in values.Attributes)
        {
            var label = NativeTheme.Body(Sr6CreationCopy.NaturalAttribute(attribute));
            label.AutomationId = "sr6-draft-natural-attribute-" + attribute.AttributeId;
            details.Add(label);
        }
        if (values.Skills is null)
            details.Add(NativeTheme.Body(Sr6CreationCopy.NaturalMissing("SkillTitle"), NativeTheme.Muted));
        else foreach (var skill in values.Skills.Where(row => row.Available || row.Rating > 0))
        {
            var label = NativeTheme.Body(Sr6CreationCopy.NaturalSkill(skill));
            label.AutomationId = "sr6-draft-natural-skill-" + skill.SkillId;
            details.Add(label);
        }
        if (values.Knowledge is not { } knowledge)
            details.Add(NativeTheme.Body(Sr6CreationCopy.NaturalMissing("KnowledgeTitle"), NativeTheme.Muted));
        else
        {
            var native = NativeTheme.Body(Sr6CreationCopy.NaturalNative(knowledge.NativeLanguage));
            native.AutomationId = "sr6-draft-natural-native-language";
            details.Add(native);
            foreach (var topic in knowledge.KnowledgeSkills)
            {
                var label = NativeTheme.Body(Sr6CreationCopy.NaturalKnowledge(topic.Name));
                label.AutomationId = "sr6-draft-natural-knowledge-" + topic.Id.ToString("N");
                details.Add(label);
            }
            foreach (var language in knowledge.Languages)
            {
                var label = NativeTheme.Body(Sr6CreationCopy.NaturalLanguage(language));
                label.AutomationId = "sr6-draft-natural-language-" + language.Id.ToString("N");
                details.Add(label);
            }
        }
        toggle.Clicked += (_, _) =>
        {
            if (!current()) return;
            details.IsVisible = !details.IsVisible;
            toggle.Text = Sr6CreationCopy.Text(details.IsVisible ? "NaturalHide" : "NaturalTitle");
        };
        _body.Add(toggle);
        _body.Add(details);
    }

    private void BuildPassiveValues(Sr6CreationPassiveValues values, Func<bool> current)
    {
        var toggle = NativeTheme.PrimaryButton(Sr6CreationCopy.Text("PassiveTitle"));
        toggle.AutomationId = "sr6-draft-passive-toggle";
        var details = new VerticalStackLayout { Spacing = 10, IsVisible = false, AutomationId = "sr6-draft-passive-values" };
        details.Add(NativeTheme.Body(Sr6CreationCopy.Text("PassiveHelp"), NativeTheme.Muted));
        foreach (string warning in values.WarningIds)
        {
            var label = NativeTheme.Body(Sr6CreationCopy.Text("PassiveWarning." + warning));
            label.AutomationId = "sr6-draft-passive-warning-" + warning;
            details.Add(label);
        }
        foreach (var row in values.Attributes.Where(row => row.PermanentBonus != 0))
        {
            var label = NativeTheme.Body(Sr6CreationCopy.PassiveAttribute(row));
            label.AutomationId = "sr6-draft-passive-attribute-" + row.AttributeId;
            details.Add(label);
        }
        foreach (var row in values.Skills?.Where(row => row.AlwaysBonus != 0 || row.NoncombatOnlyBonus != 0) ?? [])
        {
            var label = NativeTheme.Body(Sr6CreationCopy.PassiveSkill(row));
            label.AutomationId = "sr6-draft-passive-skill-" + row.SkillId;
            details.Add(label);
        }
        foreach (var row in values.Derived)
        {
            var label = NativeTheme.Body(Sr6CreationCopy.PassiveDerived(row));
            label.AutomationId = "sr6-draft-passive-stat-" + row.Id;
            details.Add(label);
        }
        details.Add(NativeTheme.Body(string.Join(" · ", values.SourceAnchorIds), NativeTheme.Muted));
        toggle.Clicked += (_, _) =>
        {
            if (!current()) return;
            details.IsVisible = !details.IsVisible;
            toggle.Text = Sr6CreationCopy.Text(details.IsVisible ? "PassiveHide" : "PassiveTitle");
        };
        _body.Add(toggle);
        _body.Add(details);
    }
}

/// <summary>Only submits the reviewed Core command; never writes XML or calculates rules.</summary>
internal sealed class Sr6CreationFinalizationPage : NativePageBase
{
    private readonly Sr6CreationFinalizationReview _review;
    private readonly VerticalStackLayout _body = new() { Padding = new Thickness(20, 18, 20, 40), Spacing = 14 };
    private Sr6FinalizationPhoneCommit? _result;
    private bool _busy, _lossAccepted;
    private long _render;

    internal Sr6CreationFinalizationPage(RunnerSessionCoordinator coordinator, Sr6CreationFinalizationReview review) : base(coordinator)
    {
        _review = review;
        Title = Sr6CreationCopy.Text("FinishReview");
        AutomationId = "sr6-finalization-page";
        Content = new ScrollView { Content = _body };
    }

    protected override void OnDisappearing() { _render++; base.OnDisappearing(); }

    protected override void Refresh()
    {
        if (_busy) return;
        long render = ++_render, appearance = CaptureAppearanceGeneration();
        bool Visible() => render == _render && IsCurrentAppearanceGeneration(appearance);
        bool Current() => Visible() && Coordinator.IsSr6FinalizationReviewCurrent(_review);
        _body.Clear();
        if (_result?.Receipt is { } receipt && Coordinator.CanDisplaySr6FinalizationReceipt(receipt))
        {
            var saved = NativeTheme.Body(Sr6CreationCopy.Text("FinishSaved"));
            saved.AutomationId = "sr6-finalization-saved";
            _body.Add(saved);
            _body.Add(NativeTheme.Body(receipt.ReceiptDigest, NativeTheme.Muted));
            foreach (string blocker in _result.Blockers) _body.Add(NativeTheme.Body(Sr6CreationCopy.Blocker(blocker)));
            var done = NativeTheme.PrimaryButton(Sr6CreationCopy.Text("FinishDone"));
            done.AutomationId = "sr6-finalization-done";
            done.Clicked += async (_, _) => await RunAsync(async () =>
            { if (Visible() && Coordinator.CanDisplaySr6FinalizationReceipt(receipt)) await Navigation.PopToRootAsync(); });
            _body.Add(done);
            return;
        }
        if (!Current())
        {
            _body.Add(NativeTheme.Body(_result is not null ? Sr6CreationCopy.Text("FinishReopen") : Sr6CreationCopy.Text("Stale")));
            return;
        }
        _body.Add(NativeTheme.Body(Sr6CreationCopy.Text("FinishHelp")));
        _body.Add(NativeTheme.Body(Sr6CreationCopy.DraftBalances(_review.Balances)));
        foreach (var loss in _review.Losses)
            _body.Add(NativeTheme.Body(Sr6CreationCopy.DraftStepTitle(loss.DomainId) + ": "
                + Sr6CreationCopy.DraftRemainder(new(loss.PoolId, loss.Amount))));
        foreach (string blocker in _review.Blockers)
            _body.Add(NativeTheme.Body(Sr6CreationCopy.Text("FinishMissing") + " " + Sr6CreationCopy.FinishDomain(blocker), NativeTheme.Danger));
        _body.Add(NativeTheme.Body(string.Join(" · ", _review.SourceAnchorIds), NativeTheme.Muted));
        if (!_review.CanFinalize) return;
        var confirm = NativeTheme.PrimaryButton(Sr6CreationCopy.Text("FinishConfirm"));
        confirm.AutomationId = "sr6-finalization-confirm";
        confirm.IsEnabled = _review.Losses.Count == 0 || _lossAccepted;
        if (_review.Losses.Count > 0)
        {
            _body.Add(NativeTheme.Body(Sr6CreationCopy.Text("FinishAcceptLoss")));
            var consent = new CheckBox { AutomationId = "sr6-finalization-accept-loss", IsChecked = _lossAccepted };
            consent.CheckedChanged += (_, args) => { if (Current()) { _lossAccepted = args.Value; confirm.IsEnabled = args.Value; } };
            _body.Add(consent);
        }
        confirm.Clicked += async (_, _) => await RunAsync(async () =>
        {
            if (!Current() || _busy || _review.Losses.Count > 0 && !_lossAccepted) return;
            _busy = true; _body.IsEnabled = false;
            try { _result = await Coordinator.ConfirmSr6FinalizationAsync(_review, true, _lossAccepted, Visible); }
            finally { _busy = false; if (Visible()) { _body.IsEnabled = true; Refresh(); } }
        });
        _body.Add(confirm);
    }
}
