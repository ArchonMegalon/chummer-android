using System.Globalization;
using Chummer.Contracts.Characters;

namespace Chummer.Android.Native;

internal sealed partial class CreationKarmaPage
{
    private CharacterCreationLifestyleConfiguration? _editingLifestyle;

    private void AddKarmaLifestyles()
    {
        if (_session.Authority?.LifestylesAuthority is not { } authority
            || _session.Selection?.LifestyleSelections is not { } selections) return;
        _body.Add(NativeTheme.Body(CreationKarmaCopy.LifestyleHelp, NativeTheme.Muted));
        AddLifestyleTotals();
        AddLifestyleLines();
        foreach (var selected in selections)
        {
            AddButton(selected.Name, "karma-lifestyle-" + selected.LifestyleId.ToString("D"),
                () => Open(CreationKarmaStep.Lifestyle, selected.BaseLifestyleOptionId, selected.LifestyleId.ToString("D")));
            bool isStarting = _session.Selection.StartingLifestyleId == selected.LifestyleId;
            AddButton(isStarting ? CreationKarmaCopy.StartingLifestyleSelected : CreationKarmaCopy.UseStartingLifestyle,
                "karma-starting-lifestyle-" + selected.LifestyleId.ToString("D"), async () =>
                {
                    Change(_session.Selection! with { StartingLifestyleId = selected.LifestyleId });
                    await Preview();
                }, !isStarting);
        }
        AddButton(CreationKarmaCopy.Preview, "karma-preview-lifestyles", Preview);
        var search = new SearchBar { Placeholder = CreationKarmaCopy.Search, Text = _search,
            AutomationId = "karma-lifestyle-search" };
        long render = _render, appearance = CaptureAppearanceGeneration();
        search.TextChanged += (_, args) => { if (Current(render, appearance)) _search = args.NewTextValue ?? string.Empty; };
        _body.Add(search);
        AddButton(CreationKarmaCopy.Search, "karma-lifestyle-search-go", () => { _page = 0; return Task.CompletedTask; });
        var options = authority.LifestyleOptions.Where(row => row.Name.Contains(_search, StringComparison.CurrentCultureIgnoreCase))
            .OrderBy(row => row.BaseCost).ThenBy(row => row.Name, StringComparer.CurrentCulture).ToArray();
        _page = Math.Min(_page, Math.Max(0, (options.Length - 1) / PageSize));
        foreach (var option in options.Skip(_page * PageSize).Take(PageSize))
        {
            _body.Add(NativeTheme.Body(CreationKarmaCopy.LifestyleSource(option.Name, option.BaseCost, option.DefaultIncrementId)));
            _body.Add(NativeTheme.Body(option.SourceBook + " · " + option.Page, NativeTheme.Muted));
            foreach (string blocker in option.Blockers) _body.Add(NativeTheme.Body(blocker, NativeTheme.Danger));
            AddButton(option.Name, "karma-add-lifestyle-" + option.OptionId,
                () => Open(CreationKarmaStep.Lifestyle, option.OptionId),
                option.IsSelectable && option.EligibilityIsExact && option.Blockers.Count == 0
                    && selections.Count < Chummer.Application.Characters.CharacterCreationKarmaLifestylesRules.MaximumSelections);
        }
        AddButton(CreationKarmaCopy.Previous, "karma-lifestyle-previous", () => { _page--; return Task.CompletedTask; }, _page > 0);
        AddButton(CreationKarmaCopy.Next, "karma-lifestyle-next", () => { _page++; return Task.CompletedTask; }, (_page + 1) * PageSize < options.Length);
    }

    private void AddLifestyleTotals()
    {
        if (!_session.QuoteCurrent || _session.Quote?.Lifestyles is not { } lifestyles) return;
        var budget = lifestyles.Budget;
        var totals = NativeTheme.Body(CreationKarmaCopy.LifestyleTotals(budget.Total,
            _session.Quote.Gear!.Budget.BasketCost, lifestyles.LifestyleNuyenUsed, budget.Remaining, budget.Overspend));
        totals.AutomationId = "karma-lifestyle-totals";
        _body.Add(totals);
    }

    private void AddLifestyleLines()
    {
        if (!_session.QuoteCurrent || _session.Quote?.Lifestyles is not { } lifestyles) return;
        foreach (var line in lifestyles.Lines)
        {
            var label = NativeTheme.Body(CreationKarmaCopy.LifestyleLine(line.Configuration.Name,
                line.BaseLifestyleName, line.Economics.TotalCost));
            label.AutomationId = "karma-lifestyle-cost-" + line.Configuration.LifestyleId.ToString("D");
            _body.Add(label);
            if (line.Configuration.LifestyleId == lifestyles.StartingLifestyleId)
                _body.Add(NativeTheme.Body(CreationKarmaCopy.StartingLifestyleSelected, NativeTheme.Muted));
        }
    }

    private void AddKarmaLifestyleEditor()
    {
        if (_session.Selection?.LifestyleSelections is not { } selections
            || _session.Authority?.LifestylesAuthority is not { } authority) return;
        var original = selections.SingleOrDefault(row => row.LifestyleId.ToString("D") == _instanceId);
        if (_instanceId is not null && original is null) return;
        var option = authority.LifestyleOptions.SingleOrDefault(row => row.OptionId == _sourceId);
        if (option is null) return;
        _editingLifestyle ??= original ?? new(Guid.NewGuid(), option.OptionId, option.Name,
            CharacterCreationLifestyleStyleIds.Standard, option.DefaultIncrementId, 1, 100m, 0, false, false,
            0, 0, 0, 0, string.Empty, string.Empty, string.Empty, []);
        _body.Add(NativeTheme.Body(CreationKarmaCopy.LifestyleSource(option.Name, option.BaseCost, option.DefaultIncrementId)));
        _body.Add(NativeTheme.Body(CreationKarmaCopy.LifestyleHelp, NativeTheme.Muted));
        long render = _render, appearance = CaptureAppearanceGeneration();
        var invalid = new HashSet<string>(StringComparer.Ordinal);
        var use = NativeTheme.SecondaryButton(CreationKarmaCopy.UseSelection);
        use.AutomationId = "karma-use-lifestyle";
        var invalidLabel = NativeTheme.Body(CreationKarmaCopy.InvalidNumber, NativeTheme.Danger);
        invalidLabel.AutomationId = "karma-lifestyle-invalid";
        invalidLabel.IsVisible = false;
        AddText(CreationKarmaCopy.ContactName, "name", _editingLifestyle.Name,
            value => _editingLifestyle = _editingLifestyle! with { Name = value });
        AddNumber(CreationKarmaCopy.LifestyleIncrements, "increments", _editingLifestyle.Increments, true,
            value => _editingLifestyle = _editingLifestyle! with { Increments = (int)value });
        AddNumber(CreationKarmaCopy.LifestylePercentage, "percentage", _editingLifestyle.Percentage, false,
            value => _editingLifestyle = _editingLifestyle! with { Percentage = value });
        AddNumber(CreationKarmaCopy.LifestyleRoommates, "roommates", _editingLifestyle.Roommates, true,
            value => _editingLifestyle = _editingLifestyle! with { Roommates = (int)value });
        AddFlag(CreationKarmaCopy.LifestyleSplit, "split", _editingLifestyle.SplitCostWithRoommates,
            value => _editingLifestyle = _editingLifestyle! with { SplitCostWithRoommates = value });
        AddFlag(CreationKarmaCopy.LifestyleTrustFund, "trust-fund", _editingLifestyle.TrustFund,
            value => _editingLifestyle = _editingLifestyle! with { TrustFund = value });
        AddText(CreationKarmaCopy.LifestyleCity, "city", _editingLifestyle.City,
            value => _editingLifestyle = _editingLifestyle! with { City = value });
        AddText(CreationKarmaCopy.LifestyleDistrict, "district", _editingLifestyle.District,
            value => _editingLifestyle = _editingLifestyle! with { District = value });
        AddText(CreationKarmaCopy.LifestyleBorough, "borough", _editingLifestyle.Borough,
            value => _editingLifestyle = _editingLifestyle! with { Borough = value });
        use.Clicked += async (_, _) => await RunAsync(async () =>
        {
            await Task.Yield();
            if (!Current(render, appearance) || invalid.Count != 0) return;
            Change(_session.Selection! with { LifestyleSelections = selections.Where(row => row != original)
                .Append(_editingLifestyle!).ToArray() });
            await Navigation.PopAsync();
        });
        _body.Add(invalidLabel); _body.Add(use);
        AddButton(CreationKarmaCopy.Remove, "karma-remove-lifestyle", async () =>
        {
            Change(_session.Selection! with
            {
                LifestyleSelections = selections.Where(row => row != original).ToArray(),
                StartingLifestyleId = _session.Selection.StartingLifestyleId == original!.LifestyleId
                    ? null : _session.Selection.StartingLifestyleId
            });
            await Navigation.PopAsync();
        }, original is not null);

        void AddText(string label, string id, string value, Action<string> changed)
        {
            _body.Add(NativeTheme.Body(label));
            var entry = new Entry { Text = value, MaxLength = 32767, AutomationId = "karma-lifestyle-" + id };
            entry.TextChanged += (_, args) =>
            { if (Current(render, appearance)) changed((args.NewTextValue ?? string.Empty).Trim()); };
            _body.Add(entry);
        }
        void AddNumber(string label, string id, decimal value, bool integer, Action<decimal> changed)
        {
            _body.Add(NativeTheme.Body(label));
            var entry = new Entry { Text = value.ToString(CultureInfo.CurrentCulture), Keyboard = Keyboard.Numeric,
                AutomationId = "karma-lifestyle-" + id };
            entry.TextChanged += (_, args) =>
            {
                if (!Current(render, appearance)) return;
                if (decimal.TryParse(args.NewTextValue,
                    NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite | NumberStyles.AllowDecimalPoint,
                    CultureInfo.CurrentCulture, out decimal parsed)
                    && (!integer || parsed <= int.MaxValue && decimal.Truncate(parsed) == parsed))
                { invalid.Remove(id); changed(parsed); }
                else invalid.Add(id);
                use.IsEnabled = invalid.Count == 0;
                invalidLabel.IsVisible = invalid.Count != 0;
            };
            _body.Add(entry);
        }
        void AddFlag(string label, string id, bool value, Action<bool> changed)
        {
            _body.Add(NativeTheme.Body(label));
            var toggle = new Switch { IsToggled = value, AutomationId = "karma-lifestyle-" + id };
            toggle.Toggled += (_, args) => { if (Current(render, appearance)) changed(args.Value); };
            _body.Add(toggle);
        }
    }
}
