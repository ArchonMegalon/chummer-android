using Chummer.Contracts.Characters;

namespace Chummer.Android.Native;

internal sealed partial class Sr6CreationFoundationPage
{
    private void BuildDraftSummary(Sr6CreationFoundationState state, Func<bool> current)
    {
        if (state.DraftSummary is not { } summary || summary.Binding != state.Binding)
        { _body.Add(NativeTheme.Body(Sr6CreationCopy.Text("Stale"))); return; }
        _body.Add(NativeTheme.Body(Sr6CreationCopy.Text("DraftHelp"), NativeTheme.Muted));
        var finalization = NativeTheme.Body(Sr6CreationCopy.Text("DraftNotFinalized"), NativeTheme.Muted);
        finalization.AutomationId = "sr6-draft-finalization-unavailable";
        _body.Add(finalization);
        if (summary.Balances is { } balances)
        {
            var cash = NativeTheme.Body(Sr6CreationCopy.DraftBalances(balances));
            cash.AutomationId = "sr6-draft-balances";
            _body.Add(cash);
        }
        if (summary.NaturalValues is { } natural) BuildNaturalValues(natural, current);
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
}
