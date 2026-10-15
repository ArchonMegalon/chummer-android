using System.Globalization;
using Chummer.Contracts.Characters;
using Chummer.Presentation.Creation;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

/// <summary>Read-only starter advice followed by one explicit, Core-reviewed atomic Apply.</summary>
public sealed class CreationCompletionPage : NativePageBase
{
    private readonly VerticalStackLayout _body = new() { Padding = new Thickness(20, 18), Spacing = 12 };
    private readonly VerticalStackLayout _footer = new() { Padding = new Thickness(20, 10, 20, 16), Spacing = 8 };
    private CharacterOverviewState? _display;
    private CharacterCreationCompletionReviewInputs? _inputs;
    private CharacterCreationPrerequisiteState? _missingPrerequisite;
    private CharacterCreationStartingNuyenSource[] _cashSources = [];
    private CompletionArchetypeSuggestion? _archetype;
    private CompletionAssistantProposal? _proposal;
    private CharacterCreationCompletionApplyReview? _review;
    private CharacterCreationFoundationResult<CharacterCreationFinalizationReceipt>? _completion;
    private CancellationTokenSource? _reads;
    private string _archetypeId = string.Empty, _roll = string.Empty;
    private string? _cashDigest, _nativeLanguageId, _idempotencyKey;
    private IReadOnlyList<string> _blockers = [];
    private bool _busy, _visible, _applyStarted, _outcomeLookupAllowed, _leaving;
    private long _render, _inputVersion;

    public CreationCompletionPage(RunnerSessionCoordinator coordinator) : base(coordinator)
    {
        Title = Copy("Title", "Finish this character");
        AutomationId = "creation-completion-page";
        Grid layout = new()
        {
            RowDefinitions = { new RowDefinition(GridLength.Star), new RowDefinition(GridLength.Auto) }
        };
        layout.Add(new ScrollView { Content = _body });
        layout.Add(_footer, 0, 1);
        Content = layout;
        _body.Add(NativeTheme.Body(CreationKarmaCopy.Loading, NativeTheme.Muted));
    }

    internal static bool Supports(CharacterOverviewState state)
        => state.Profile?.Created == false && state.CreationWizard is { RulesetId: "sr5" } wizard
           && wizard.BuildMethod is CharacterCreationBuildMethods.Priority or CharacterCreationBuildMethods.SumToTen;

    protected override void OnAppearing()
    {
        _visible = true;
        _leaving = false;
        _reads = new CancellationTokenSource();
        base.OnAppearing();
    }

    protected override void OnDisappearing()
    {
        _visible = false;
        _render++;
        _inputVersion++;
        _reads?.Cancel();
        _reads?.Dispose();
        _reads = null;
        _body.IsEnabled = false;
        _footer.IsEnabled = false;
        base.OnDisappearing();
    }

    protected override async Task PrepareForAppearanceRefreshAsync(CancellationToken cancellationToken)
    {
        // A dispatched review is never regenerated, including after navigation.
        if (_applyStarted) return;
        long appearance = CaptureAppearanceGeneration();
        var original = Coordinator.State;
        _display = original;
        _inputs = null;
        _missingPrerequisite = null;
        _proposal = null;
        _review = null;
        _cashSources = [];
        _blockers = [];
        _busy = true;
        if (!Supports(original)) { _busy = false; return; }
        try
        {
            var loaded = await Coordinator.LoadCreationCompletionInputsAsync(cancellationToken);
            if (!CurrentLoad()) return;
            _blockers = loaded.Blockers;
            if (loaded is not { Outcome: CharacterCreationFoundationOutcomes.Success, Value: { } inputs })
            {
                // A downstream input (for example Skills) can be absent because
                // the real priority prerequisite has not been confirmed yet.
                // Only a current, owner-bound Core read may choose that guidance.
                var prerequisite = await Coordinator.LoadCreationPrerequisiteAsync(cancellationToken);
                if (!CurrentLoad()) return;
                if (prerequisite is { Outcome: CharacterCreationFoundationOutcomes.Success, Value: { CanEnterAttributes: false } required }
                    && Coordinator.IsCreationPrerequisiteStateCurrent(required))
                    _missingPrerequisite = required;
                return;
            }
            if (!Coordinator.IsCreationCompletionInputsCurrent(inputs)) return;
            var suggestion = await Task.Run(() => CompletionRecommendationBuilder.SuggestArchetype(
                inputs, cancellationToken: cancellationToken), cancellationToken);
            if (!CurrentLoad() || !Coordinator.IsCreationCompletionInputsCurrent(inputs)) return;
            var cash = await Task.Run(() => Coordinator.LoadCreationFinalizationInBackground(
                original, cancellationToken), cancellationToken);
            if (!CurrentLoad() || !Coordinator.IsCreationCompletionInputsCurrent(inputs)) return;
            _inputs = inputs;
            _archetype = suggestion.Value;
            _archetypeId = suggestion.Value?.ArchetypeId ?? CompletionAssistantRules.GeneralSurvivor;
            _blockers = suggestion.Blockers;
            // CanReview describes the ORIGINAL build, not our proposed one.
            // Only use its exact source terms here, never its admission flag.
            if (cash.Value is { } authority)
                _cashSources = authority.StartingCashSources?.ToArray()
                    ?? (authority.StartingCashSource is { } source ? [source] : []);
            _cashDigest = _cashSources.FirstOrDefault()?.AuthorityDigest;
            _roll = string.Empty;
            _nativeLanguageId = null;
            _inputVersion++;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            if (CurrentLoad()) _blockers = ["completion-ui-load-failed"];
        }
        finally
        {
            if (IsCurrentAppearanceGeneration(appearance)) _busy = false;
        }
        bool CurrentLoad() => IsCurrentAppearanceGeneration(appearance)
            && Coordinator.IsCreationFinalizationDisplayCurrent(original);
    }

    protected override void Refresh()
    {
        long render = ++_render, appearance = CaptureAppearanceGeneration();
        _body.Clear();
        _footer.Clear();
        _body.IsEnabled = !_busy;
        _footer.IsEnabled = true;
        _body.Add(NativeTheme.Title(Title));
        if (_completion is { Outcome: CharacterCreationFoundationOutcomes.Success, Value: { } receipt })
        {
            RenderSaved(receipt, appearance, render);
            return;
        }
        if (_applyStarted)
        {
            if (!_busy && _completion is { Value: null } rejected && rejected.Outcome != "outcome-unknown")
            {
                _body.Add(NativeTheme.Body(Copy("Rejected", "These changes were not applied. Return to creation and review your choices."), NativeTheme.Danger));
                AddBlockers(rejected.Blockers);
                AddCancel(appearance, render, afterRejection: true);
                return;
            }
            _body.Add(NativeTheme.Body(_busy ? Copy("Saving", "Saving and opening Career…")
                : Copy("Unknown", "The save result needs checking. Do not submit another completion."), NativeTheme.Muted));
            if (!_busy && _outcomeLookupAllowed && _completion?.Outcome == "outcome-unknown")
                AddAction(_footer, Copy("CheckResult", "Check saved result"), "creation-completion-check-result",
                    () => ApplyAsync(recoveryOnly: true), appearance, render);
            return;
        }
        AddCancel(appearance, render);
        if (_display is null || !Supports(_display))
        {
            _body.Add(NativeTheme.Body(Copy("Unsupported", "Starter suggestions currently support SR5 Priority and Sum-to-Ten. Continue with this build method’s normal creation steps.")));
            return;
        }
        if (_inputs is null)
        {
            if (_busy) _body.Add(NativeTheme.Body(CreationKarmaCopy.Loading, NativeTheme.Muted));
            else if (_missingPrerequisite is { } prerequisite)
                _body.Add(NativeTheme.Body(Coordinator.IsCreationPrerequisiteStateCurrent(prerequisite)
                    ? Copy("LoadBlocked", "Complete Build method first, then reopen these suggestions. Your existing choices are unchanged.")
                    : Copy("Stale", "Your runner or account changed. Return to creation and open a fresh review."), NativeTheme.Muted));
            else AddBlockers(_blockers.Count == 0 ? ["completion-ui-load-failed"] : _blockers);
            return;
        }
        if (!InputsCurrent())
        {
            _body.Add(NativeTheme.Body(Copy("Stale", "Your runner or account changed. Return to creation and open a fresh review."), NativeTheme.Danger));
            return;
        }
        _body.Add(NativeTheme.Body(Copy("Scope", "Starter attribute and skill suggestions—not a complete loadout. Magic, equipment, contacts and story may still need your choices. Nothing changes until Apply."), NativeTheme.Muted));
        if (_review is not null)
        {
            RenderFinalReview(appearance, render);
            return;
        }
        RenderChoices(appearance, render);
        if (_proposal is null)
        {
            AddAction(_footer, Copy("Preview", "Preview suggestions"), "creation-completion-preview",
                BuildAsync, appearance, render, !_busy && CompletionRecommendationBuilder.GetRecommendationPack(_archetypeId) is not null);
        }
        else
        {
            RenderProposal();
            RenderCashAndReview(appearance, render);
        }
        AddBlockers(_blockers);
    }

    private void RenderChoices(long appearance, long render)
    {
        string[] archetypes = new[] { _archetypeId, CompletionAssistantRules.GeneralSurvivor,
            CompletionAssistantRules.StreetSamurai, CompletionAssistantRules.Face, CompletionAssistantRules.Mage,
            CompletionAssistantRules.Decker, CompletionAssistantRules.Rigger }.Distinct(StringComparer.Ordinal).ToArray();
        Picker picker = new()
        {
            Title = Copy("Archetype", "Archetype"), AutomationId = "creation-completion-archetype",
            ItemsSource = archetypes.Select(ArchetypeLabel).ToArray(), SelectedIndex = Array.IndexOf(archetypes, _archetypeId),
            TextColor = NativeTheme.Text, TitleColor = NativeTheme.Muted, BackgroundColor = NativeTheme.Surface
        };
        picker.SelectedIndexChanged += (_, _) =>
        {
            if (!Current(appearance, render) || _busy || picker.SelectedIndex < 0) return;
            _archetypeId = archetypes[picker.SelectedIndex];
            InvalidateProposal();
        };
        _body.Add(NativeTheme.FieldLabel(Copy("Archetype", "Archetype")));
        _body.Add(picker);
        _body.Add(NativeTheme.Body(_archetype?.Confidence == CompletionArchetypeConfidence.NoEvidence
            ? Copy("Neutral", "No clear role yet: a general starter is selected. You can change it.")
            : Copy("Prefill", "Suggested from your current choices. You can change the archetype."), NativeTheme.Muted));
        if (CompletionRecommendationBuilder.GetRecommendationPack(_archetypeId) is null)
            _body.Add(NativeTheme.Body(Copy("NoPack", "This archetype has no starter suggestions yet. Keep your talent and continue manually, or choose another suggestion style."), NativeTheme.Danger));
        _body.Add(NativeTheme.Body(Copy("KeepChoices", "Your existing choices stay unchanged. Suggestions only fill choices you have not made yet."), NativeTheme.Muted));
        if (!_inputs!.Skills.Allocations.Any(row => row.IsNativeLanguage))
        {
            var languages = _inputs.Skills.Authority.KnowledgeSkills.Where(row => row.CanBeNativeLanguage).ToArray();
            var language = new Picker
            {
                Title = CreationAllocationStrings.Get("Skills.ChooseNativeLanguage", "Choose native language"),
                AutomationId = "creation-completion-native-language", TextColor = NativeTheme.Text,
                TitleColor = NativeTheme.Danger, BackgroundColor = NativeTheme.Surface,
                ItemsSource = languages.Select(row => SkillCatalogStrings.SkillName(row.Kind, row.SourceSkillId, row.Name)).ToArray(),
                SelectedIndex = Array.FindIndex(languages, row => row.SourceSkillId == _nativeLanguageId)
            };
            language.SelectedIndexChanged += (_, _) =>
            {
                if (!Current(appearance, render) || _busy || language.SelectedIndex < 0) return;
                _nativeLanguageId = languages[language.SelectedIndex].SourceSkillId;
                InvalidateProposal();
            };
            _body.Add(language);
        }
    }

    private void InvalidateProposal()
    {
        _inputVersion++;
        _proposal = null;
        _review = null;
        _blockers = [];
        Refresh();
    }

    private async Task BuildAsync()
    {
        if (_busy || !InputsCurrent() || _inputs is not { } inputs
            || _display?.DisplayOwnerContext is not { } owner
            || CompletionRecommendationBuilder.GetRecommendationPack(_archetypeId) is null) return;
        long appearance = CaptureAppearanceGeneration(), version = _inputVersion;
        var permissions = new CompletionAdditivePermissions([], []);
        var intent = new CompletionAssistantIntent(owner, inputs.SnapshotDigest, _archetypeId, permissions, new())
        {
            NativeLanguageSourceId = _nativeLanguageId,
            StartingLifestyleId = SelectedLifestyleId(inputs)
        };
        _busy = true;
        Refresh();
        try
        {
            var result = await Coordinator.BuildCreationCompletionProposalAsync(inputs, intent, ReadToken);
            if (!CurrentInput(appearance, version)) return;
            _proposal = result.Value;
            _blockers = result.Blockers;
        }
        catch (OperationCanceledException) { }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            if (CurrentInput(appearance, version)) _blockers = ["completion-ui-load-failed"];
        }
        finally
        {
            if (IsCurrentAppearanceGeneration(appearance))
            {
                _busy = false;
                if (CurrentInput(appearance, version)) Refresh();
            }
        }
    }

    private void RenderProposal()
    {
        _body.Add(NativeTheme.Eyebrow(Copy("Proposal", "Proposed ratings")));
        foreach (var suggestion in _proposal!.Suggestions)
        {
            string label = SuggestionLabel(suggestion.Choice);
            string actual = suggestion.EvaluatedRating?.ToString(CultureInfo.CurrentCulture) ?? "—";
            string state = suggestion.State switch
            {
                CompletionSuggestionState.TargetMet => Copy("Met", "target met"),
                CompletionSuggestionState.Locked or CompletionSuggestionState.Excluded => Copy("Locked", "kept unchanged"),
                CompletionSuggestionState.SourceUnavailable => Copy("Unavailable", "source unavailable"),
                CompletionSuggestionState.TargetUnmet => Copy("Unmet", "target not reached"),
                _ => Copy("NotEvaluated", "not evaluated")
            };
            _body.Add(NativeTheme.Body(CreationFlowStrings.Format("Completion.Rating", "{0}: proposed {1}, target {2} · {3}",
                label, actual, suggestion.TargetRating, state)));
        }
        _body.Add(NativeTheme.Eyebrow(Copy("Remaining", "Still needs attention")));
        foreach (var task in _proposal.RemainingTasks.Where(key => key != "completion.remaining.finalization-review").Distinct())
            _body.Add(NativeTheme.Body(RemainingTask(task), NativeTheme.Muted));
        if (_proposal.Evaluation.Value?.Review.Balances is { } balance)
        {
            _body.Add(NativeTheme.Metric(Copy("KarmaBefore", "Creation Karma remaining"), Number(balance.RemainingCreationKarma)));
            _body.Add(NativeTheme.Metric(Copy("NuyenBefore", "Creation nuyen remaining"), Number(balance.RemainingNuyen)));
        }
    }

    private void RenderCashAndReview(long appearance, long render)
    {
        if (_cashSources.FirstOrDefault(row => row.AuthorityDigest == _cashDigest) is not { } source)
        {
            _body.Add(NativeTheme.Body(Copy("CashUnavailable", "Starting-cash terms are unavailable. Review your lifestyle in creation, then reopen these suggestions."), NativeTheme.Danger));
            return;
        }
        _body.Add(NativeTheme.Eyebrow(CreationKarmaCopy.DiceTotal));
        _body.Add(NativeTheme.Body(CreationKarmaCopy.StartingCash(source.Name, source.Dice, source.Multiplier)));
        if (_cashSources.Length > 1)
        {
            Picker lifestyle = new()
            {
                Title = CreationAllocationStrings.Get("Finalization.StartingLifestyle", "Lifestyle for starting cash"),
                AutomationId = "creation-completion-cash-source", TextColor = NativeTheme.Text,
                BackgroundColor = NativeTheme.Surface,
                ItemsSource = _cashSources.Select(row => CreationKarmaCopy.StartingCash(row.Name, row.Dice, row.Multiplier)).ToArray(),
                SelectedIndex = Array.FindIndex(_cashSources, row => row.AuthorityDigest == _cashDigest)
            };
            lifestyle.SelectedIndexChanged += (_, _) =>
            {
                if (!Current(appearance, render) || _busy || lifestyle.SelectedIndex < 0) return;
                _cashDigest = _cashSources[lifestyle.SelectedIndex].AuthorityDigest;
                _roll = string.Empty;
                InvalidateProposal();
            };
            _body.Add(lifestyle);
        }
        _body.Add(NativeTheme.Body(CreationKarmaCopy.DiceTotalHelp, NativeTheme.Muted));
        Entry dice = new()
        {
            Text = _roll, Keyboard = Keyboard.Numeric, TextColor = NativeTheme.Text,
            BackgroundColor = NativeTheme.Surface, AutomationId = "creation-completion-cash-roll"
        };
        Button review = AddAction(_footer, Copy("Review", "Review and check completion"), "creation-completion-review",
            ReviewAsync, appearance, render, CanReview());
        dice.TextChanged += (_, _) =>
        {
            if (!Current(appearance, render) || _busy) return;
            _roll = dice.Text ?? string.Empty;
            _inputVersion++;
            _review = null;
            review.IsEnabled = CanReview();
        };
        _body.Add(dice);
        bool CanReview() => !_busy && _proposal?.Evaluation.Value is not null
            && int.TryParse(_roll, NumberStyles.None, CultureInfo.InvariantCulture, out _);
    }

    private async Task ReviewAsync()
    {
        if (_busy || !InputsCurrent() || _proposal is not { } proposal
            || !Coordinator.IsCreationCompletionProposalCurrent(proposal)
            || _cashSources.FirstOrDefault(row => row.AuthorityDigest == _cashDigest) is not { } source
            || !int.TryParse(_roll, NumberStyles.None, CultureInfo.InvariantCulture, out int dice)) return;
        long appearance = CaptureAppearanceGeneration(), version = _inputVersion;
        _busy = true;
        Refresh();
        try
        {
            var result = await Coordinator.ReviewCreationCompletionAsync(proposal, new(source.AuthorityDigest, dice), ReadToken);
            if (!CurrentInput(appearance, version)) return;
            _blockers = result.Blockers;
            _review = result is { Outcome: CharacterCreationFoundationOutcomes.Success, Value: { } review, Blockers.Count: 0 }
                && Coordinator.IsCreationCompletionReviewCurrent(review) ? review : null;
            if (_review is not null)
                _idempotencyKey = $"creation-completion:{_review.Projection.Binding.WorkspaceId.Value}:{_review.Projection.Binding.ContentRevision}:{_review.Projection.ProjectionDigest}";
        }
        catch (OperationCanceledException) { }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            if (CurrentInput(appearance, version)) _blockers = ["completion-ui-load-failed"];
        }
        finally
        {
            if (IsCurrentAppearanceGeneration(appearance))
            {
                _busy = false;
                if (CurrentInput(appearance, version)) Refresh();
            }
        }
    }

    private void RenderFinalReview(long appearance, long render)
    {
        var projection = _review!.Projection;
        _body.Add(NativeTheme.Eyebrow(Copy("FinalReview", "Review before Apply")));
        foreach (var delta in projection.OrderedDeltas.OrderBy(row => row.Order))
        {
            string label = !string.IsNullOrWhiteSpace(delta.TargetName) ? delta.TargetName
                : delta.Kind == CharacterCreationFinalizationDeltaKinds.Attribute
                    ? CreationAllocationStrings.AttributeName(delta.TargetId)
                    : Copy("Change", "Character change");
            string change = delta.Kind == CharacterCreationFinalizationDeltaKinds.Lifecycle
                ? Copy("CareerTransition", "Creation → Career")
                : $"{DeltaValue(delta.BeforeValue)} → {DeltaValue(delta.AfterValue)}";
            _body.Add(NativeTheme.Body(label + ": " + change));
        }
        _body.Add(NativeTheme.Metric(Copy("KarmaAfter", "Career Karma"), Number(projection.KarmaRemaining)));
        _body.Add(NativeTheme.Metric(Copy("NuyenAfter", "Career nuyen"), Number(projection.NuyenRemaining)));
        _body.Add(NativeTheme.Body(Copy("Carryover", "Excess creation funds are lost at the Career boundary. Starting cash is added afterward."), NativeTheme.Muted));
        _body.Add(NativeTheme.Body(Copy("ConfirmHelp", "Apply saves these reviewed changes together and enters Career. Cancel returns to creation without changing your runner."), NativeTheme.Muted));
        AddAction(_footer, Copy("Apply", "Apply and enter Career"), "creation-completion-apply",
            () => ApplyAsync(recoveryOnly: false), appearance, render,
            !_busy && Coordinator.IsCreationCompletionReviewCurrent(_review));
    }

    private async Task ApplyAsync(bool recoveryOnly)
    {
        if (_busy || _review is not { } review || _idempotencyKey is not { } key
            || recoveryOnly != _applyStarted || !IsCurrentPage(CaptureAppearanceGeneration())
            || recoveryOnly && !_outcomeLookupAllowed
            || !recoveryOnly && !Coordinator.IsCreationCompletionReviewCurrent(review)) return;
        long appearance = CaptureAppearanceGeneration();
        Page[] stack = Navigation.NavigationStack.ToArray();
        _applyStarted = true;
        _busy = true;
        Refresh();
        try
        {
            // Once dispatched, observe the result even if the page disappears.
            // The coordinator consumes this exact review and owns receipt-only recovery.
            _completion = await Coordinator.ApplyCreationCompletionAsync(review, key, explicitlyConfirmed: true);
            _outcomeLookupAllowed = _completion.Outcome == "outcome-unknown";
            if (!IsCurrentPage(appearance) || !Navigation.NavigationStack.SequenceEqual(stack)) return;
            if (_completion is { Outcome: CharacterCreationFoundationOutcomes.Success, Value: not null })
                await OpenSavedAsync(appearance, stack);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // A navigation failure must never erase a durable receipt. If the
            // call itself unexpectedly throws, do not assume dispatch occurred
            // and offer a recovery call that might start a fresh Apply.
            if (_completion?.Value is null)
            {
                _outcomeLookupAllowed = false;
                _completion = new("outcome-unknown", null, ["creation-completion-apply-outcome-unknown"]);
            }
        }
        finally
        {
            _busy = false;
            if (IsCurrentPage(CaptureAppearanceGeneration())) Refresh();
        }
    }

    private void RenderSaved(CharacterCreationFinalizationReceipt receipt, long appearance, long render)
    {
        if (!Coordinator.CanDisplayCreationFinalizationReceipt(receipt))
        {
            _body.Add(NativeTheme.Body(Copy("ReceiptAccount", "Return to the original account to view this confirmation."), NativeTheme.Danger));
            return;
        }
        _body.Add(NativeTheme.Body(Copy("Saved", "Your changes are saved. Continue to your saved Career runner.")));
        AddAction(_footer, Copy("Continue", "Continue to saved runner"), "creation-completion-continue",
            () => OpenSavedAsync(appearance, Navigation.NavigationStack.ToArray()), appearance, render, !_busy);
    }

    private async Task OpenSavedAsync(long appearance, IReadOnlyList<Page> stack)
    {
        bool Current() => IsCurrentPage(appearance) && Navigation.NavigationStack.SequenceEqual(stack);
        if (!Current() || _completion?.Value is not { } receipt || !Coordinator.CanDisplayCreationFinalizationReceipt(receipt)) return;
        if (_completion.Blockers.Count == 0 && Coordinator.IsCreationFinalizationReceiptCurrent(receipt))
        {
            try { await Navigation.PopToRootAsync(animated: false); return; }
            catch (Exception error) when (error is not OutOfMemoryException) { }
        }
        if (Current() && Coordinator.CanDisplayCreationFinalizationReceipt(receipt))
            await Navigation.PushAsync(new CreationFinalizationReceiptPage(Coordinator, receipt, _completion.Blockers));
    }

    private void AddCancel(long appearance, long render, bool afterRejection = false)
    {
        Button cancel = NativeTheme.SecondaryButton(Copy("Cancel", "Cancel / fine-tune in creation"));
        cancel.AutomationId = "creation-completion-cancel";
        cancel.Clicked += async (_, _) =>
        {
            if (!IsCurrentPage(appearance) || render != _render || _leaving
                || _applyStarted && !afterRejection) return;
            _leaving = true;
            cancel.IsEnabled = false;
            _reads?.Cancel();
            try { await Navigation.PopAsync(); }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                if (IsCurrentPage(appearance))
                    _body.Add(NativeTheme.Body(Copy("BackFailed", "Could not return to creation. Please try Back again."), NativeTheme.Danger));
            }
            finally
            {
                _leaving = false;
                if (IsCurrentPage(appearance)) cancel.IsEnabled = true;
            }
        };
        _footer.Add(cancel);
    }

    private Button AddAction(VerticalStackLayout parent, string title, string id, Func<Task> action,
        long appearance, long render, bool enabled = true)
    {
        Button button = NativeTheme.PrimaryButton(title);
        button.AutomationId = id;
        button.IsEnabled = enabled;
        button.HeightRequest = -1;
        button.MinimumHeightRequest = 50;
        button.Clicked += async (_, _) =>
        {
            if (IsCurrentPage(appearance) && render == _render && ReferenceEquals(button.Parent, parent) && button.IsEnabled)
                await RunAsync(action);
        };
        parent.Add(button);
        return button;
    }

    private void AddBlockers(IEnumerable<string> blockers)
    {
        foreach (string message in blockers.Select(code => code switch
        {
            CharacterCreationFinalizationBlockers.StartingCashChoiceInvalid => CreationAllocationStrings.Get("Finalization.InvalidDiceTotal", "That total does not match these dice. Check your roll and try again."),
            _ => CreationFlowStrings.FinalizationBlocker(code) is var known && known != code ? known
                : Copy("Blocked", "Some choices could not be verified. Return to creation, review the required steps, then try a fresh proposal.")
        }).Distinct(StringComparer.Ordinal)) _body.Add(NativeTheme.Body(message, NativeTheme.Danger));
    }

    private string SuggestionLabel(CompletionChoiceIdentity choice) => choice.Domain switch
    {
        CompletionChoiceDomain.Attribute => CreationAllocationStrings.AttributeName(choice.SourceId),
        CompletionChoiceDomain.ActiveSkill => _inputs!.Skills.Authority.ActiveSkills.FirstOrDefault(row => row.SourceSkillId == choice.SourceId) is { } skill
            ? SkillCatalogStrings.SkillName(skill.Kind, skill.SourceSkillId, skill.Name) : Copy("UnknownSkill", "Unavailable skill"),
        CompletionChoiceDomain.KnowledgeSkill => _inputs!.Skills.Authority.KnowledgeSkills.FirstOrDefault(row => row.SourceSkillId == choice.SourceId) is { } knowledge
            ? SkillCatalogStrings.SkillName(knowledge.Kind, knowledge.SourceSkillId, knowledge.Name) : Copy("UnknownSkill", "Unavailable skill"),
        _ => Copy("SkillGroup", "Skill group")
    };

    private static string RemainingTask(string key) => key switch
    {
        "completion.remaining.practical-loadout" => Copy("Loadout", "Equipment is not automatically selected. Review your practical loadout in creation."),
        "completion.remaining.identity-review" => Copy("Identity", "Review your name, identity and story yourself."),
        "completion.remaining.starter-targets" => Copy("Targets", "Some starter targets are locked, unavailable or unmet. They are advice, not automatic permission to change your choices."),
        "completion.remaining.magic-choices" or "completion.remaining.magic-evaluation" => Copy("Magic", "Review the magic choices required by your talent."),
        "completion.remaining.skills-choices" or "completion.remaining.skills-evaluation" => Copy("SkillsNeeded", "Review skills, native language and any unallocated skill points."),
        "completion.remaining.attributes-choices" or "completion.remaining.attributes-evaluation" => Copy("AttributesNeeded", "Review your attribute allocations."),
        "completion.remaining.attribute-choice-intent" => Copy("AttributeChoiceIntent", "Your saved attribute allocation stays unchanged. The assistant cannot tell which values you deliberately kept, so finish any remaining attribute points in creation."),
        "completion.remaining.lifestyle-choices" or "completion.remaining.lifestyle-evaluation" => Copy("LifestyleNeeded", "Review your lifestyle and starting-cash selection."),
        "completion.remaining.contacts-choices" or "completion.remaining.contacts-evaluation" => Copy("ContactsNeeded", "Review your contacts."),
        "completion.remaining.resources-choices" or "completion.remaining.resources-evaluation" => Copy("ResourcesNeeded", "Review your creation funding."),
        "completion.remaining.gear-choices" or "completion.remaining.gear-evaluation" => Copy("GearNeeded", "Review your equipment choices."),
        "completion.remaining.qualities-choices" or "completion.remaining.qualities-evaluation" => Copy("QualitiesNeeded", "Review your qualities."),
        _ => Copy("ManualNeeded", "This part still needs manual review in creation.")
    };

    private static string ArchetypeLabel(string id) => id switch
    {
        CompletionAssistantRules.GeneralSurvivor => Copy("Archetype.General", "General survivor"),
        CompletionAssistantRules.StreetSamurai => Copy("Archetype.Samurai", "Street samurai"),
        CompletionAssistantRules.Face => Copy("Archetype.Face", "Face"),
        CompletionAssistantRules.Mage => Copy("Archetype.Mage", "Mage"),
        CompletionAssistantRules.Decker => Copy("Archetype.Decker", "Decker"),
        CompletionAssistantRules.Rigger => Copy("Archetype.Rigger", "Rigger"),
        _ => MagicCatalogStrings.TalentName(id)
    };

    private static string DeltaValue(string? value) => value is null ? "—"
        : value == "native" ? CreationKarmaCopy.NativeLanguage
        : Guid.TryParse(value, out _) ? Copy("Selected", "selected choice") : value;
    private Guid? SelectedLifestyleId(CharacterCreationCompletionReviewInputs inputs)
    {
        var source = _cashSources.FirstOrDefault(row => row.AuthorityDigest == _cashDigest);
        var option = inputs.Lifestyles.Authority.LifestyleOptions.FirstOrDefault(row => row.SourceId.ToString("D") == source?.SourceId);
        return option is null ? null : inputs.Lifestyles.Lifestyles.FirstOrDefault(row => row.BaseLifestyleOptionId == option.OptionId)?.LifestyleId;
    }
    private bool InputsCurrent() => _inputs is not null && Coordinator.IsCreationCompletionInputsCurrent(_inputs);
    private bool IsCurrentPage(long appearance) => _visible && IsCurrentAppearanceGeneration(appearance)
        && ReferenceEquals(Navigation.NavigationStack.LastOrDefault(), this);
    private bool Current(long appearance, long render) => IsCurrentPage(appearance) && render == _render && InputsCurrent();
    private bool CurrentInput(long appearance, long version) => IsCurrentPage(appearance) && version == _inputVersion && InputsCurrent();
    private CancellationToken ReadToken => _reads?.Token ?? new CancellationToken(canceled: true);
    private static string Number(decimal? value) => value?.ToString("0.##", CultureInfo.CurrentCulture) ?? "—";
    internal static string Copy(string key, string fallback) => CreationFlowStrings.Get("Completion." + key, fallback);
}
