using Chummer.Contracts.Characters;
using System.Text;

namespace Chummer.Android.Native;

internal sealed partial class Sr6CreationFoundationPage
{
    private void BuildKnowledgeEditor(int budget, Func<bool> current, Action<Sr6CreationFoundationSelection> change)
    {
        _selection = _selection! with { Knowledge = _selection!.Knowledge ?? new("", [], []) };
        _body.Add(NativeTheme.Body(Sr6CreationCopy.KnowledgePool(budget)));
        _body.Add(NativeTheme.Body(Sr6CreationCopy.Text("KnowledgeHelp"), NativeTheme.Muted));
        AddName("knowledge-native", "KnowledgeNative", _selection.Knowledge.NativeLanguage,
            name => Edit(knowledge => knowledge with { NativeLanguage = name }));

        foreach (var topic in _selection.Knowledge.KnowledgeSkills)
        {
            Guid id = topic.Id;
            string suffix = id.ToString("N");
            AddName("knowledge-topic-" + suffix, "KnowledgeTopic", topic.Name,
                name => Edit(knowledge => knowledge with { KnowledgeSkills = knowledge.KnowledgeSkills
                    .Select(row => row.Id == id ? row with { Name = name } : row).ToArray() }));
            AddButton("knowledge-remove-topic-" + suffix, "KnowledgeRemove", () =>
                Edit(knowledge => knowledge with { KnowledgeSkills = knowledge.KnowledgeSkills.Where(row => row.Id != id).ToArray() }));
        }
        foreach (var language in _selection.Knowledge.Languages)
        {
            Guid id = language.Id;
            string suffix = id.ToString("N");
            AddName("knowledge-language-" + suffix, "KnowledgeLanguage", language.Name,
                name => Edit(knowledge => knowledge with { Languages = knowledge.Languages
                    .Select(row => row.Id == id ? row with { Name = name } : row).ToArray() }));
            _body.Add(NativeTheme.Body(Sr6CreationCopy.Text("KnowledgeLevel")));
            string[] levels = Sr6CreationLanguageLevels.Ordered.ToArray();
            var picker = new Picker { Title = Sr6CreationCopy.Text("KnowledgeLevel"),
                AutomationId = "sr6-foundation-knowledge-level-" + suffix };
            foreach (string level in levels) picker.Items.Add(Sr6CreationCopy.LanguageLevel(level));
            picker.SelectedIndex = Array.IndexOf(levels, language.Level);
            picker.SelectedIndexChanged += (_, _) =>
            {
                if (!current() || picker.SelectedIndex < 0 || picker.SelectedIndex >= levels.Length) return;
                string level = levels[picker.SelectedIndex];
                Edit(knowledge => knowledge with { Languages = knowledge.Languages
                    .Select(row => row.Id == id ? row with { Level = level } : row).ToArray() });
            };
            _body.Add(picker);
            AddButton("knowledge-remove-language-" + suffix, "KnowledgeRemove", () =>
                Edit(knowledge => knowledge with { Languages = knowledge.Languages.Where(row => row.Id != id).ToArray() }));
        }
        if (_selection.Knowledge.KnowledgeSkills.Count + _selection.Knowledge.Languages.Count < 32)
        {
            AddButton("knowledge-add-topic", "KnowledgeAddTopic", () => Edit(knowledge => knowledge with
                { KnowledgeSkills = [.. knowledge.KnowledgeSkills, new(Guid.NewGuid(), "")] }));
            AddButton("knowledge-add-language", "KnowledgeAddLanguage", () => Edit(knowledge => knowledge with
                { Languages = [.. knowledge.Languages, new(Guid.NewGuid(), "", Sr6CreationLanguageLevels.Basic)] }));
        }

        void Edit(Func<Sr6CreationKnowledgeSelection, Sr6CreationKnowledgeSelection> update)
        {
            if (current() && _selection.Knowledge is { } knowledge)
                change(_selection with { Knowledge = update(knowledge) });
        }
        void AddName(string id, string label, string value, Action<string> update)
        {
            _body.Add(NativeTheme.Body(Sr6CreationCopy.Text(label)));
            var entry = new Entry { AutomationId = "sr6-foundation-" + id, Text = value, MaxLength = 80 };
            entry.TextChanged += (_, _) =>
            {
                if (!current()) return;
                string name = (entry.Text ?? "").Trim();
                try { name = name.Normalize(NormalizationForm.FormC); }
                catch (ArgumentException) { /* Core rejects malformed Unicode at review. */ }
                update(name);
            };
            _body.Add(entry);
        }
        void AddButton(string id, string label, Action update)
        {
            var button = NativeTheme.PrimaryButton(Sr6CreationCopy.Text(label));
            button.AutomationId = "sr6-foundation-" + id;
            button.Clicked += (_, _) =>
            {
                if (!current()) return;
                update();
                Refresh(); // Row topology only; entry and picker changes never rebuild their own controls.
            };
            _body.Add(button);
        }
    }
}
