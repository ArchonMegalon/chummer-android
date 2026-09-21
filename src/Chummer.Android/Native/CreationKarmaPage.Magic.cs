using Chummer.Contracts.Characters;

namespace Chummer.Android.Native;

internal sealed partial class CreationKarmaPage
{
    private static string MagicKindLabel(string kind) => kind switch
    {
        CharacterCreationMagicResonanceKinds.Tradition => CreationKarmaCopy.Tradition,
        CharacterCreationMagicResonanceKinds.Stream => CreationKarmaCopy.Stream,
        CharacterCreationMagicResonanceKinds.AdeptPower => CreationKarmaCopy.Powers,
        CharacterCreationMagicResonanceKinds.Spell => CreationKarmaCopy.Spells,
        CharacterCreationMagicResonanceKinds.ComplexForm => CreationKarmaCopy.ComplexForms,
        _ => CreationKarmaCopy.Magic
    };

    // Chooser visibility is issued by Core for the exact talent/skill unlock.
    // This page never infers magical capability from a name or Priority grant.
    private static bool MagicKindAvailable(CharacterCreationKarmaMagicAccess access, string kind) => kind switch
    {
        CharacterCreationMagicResonanceKinds.Tradition => access.RequiresTradition,
        CharacterCreationMagicResonanceKinds.Stream => access.RequiresStream,
        CharacterCreationMagicResonanceKinds.AdeptPower => access.AllowsAdeptPowers,
        CharacterCreationMagicResonanceKinds.Spell => access.AllowsSpells,
        CharacterCreationMagicResonanceKinds.ComplexForm => access.AllowsComplexForms,
        _ => false
    };

    private void AddKarmaMagic()
    {
        _body.Add(NativeTheme.Body(CreationKarmaCopy.MagicHelp, NativeTheme.Muted));
        AddKarmaMagicSummary();
        AddButton(CreationKarmaCopy.Preview, "karma-magic-preview", Preview);
        if (!_session.QuoteCurrent || _session.Quote?.Magic is not { } magic
            || _session.Selection?.MagicSelections is not { } selection || _session.Authority?.MagicCatalog is not { } catalog)
            return;
        foreach (var slice in catalog.Catalogs)
            if (MagicKindAvailable(magic.Access, slice.Kind))
                AddButton(MagicKindLabel(slice.Kind), "karma-magic-open-" + slice.Kind,
                    () => Open(CreationKarmaStep.MagicCatalog, sourceKind: slice.Kind));
        if (magic.Cost.MysticPowerPoints is { } purchase)
        {
            _body.Add(NativeTheme.Body(CreationKarmaCopy.PowerPointPurchase(purchase.PowerPoints, purchase.KarmaCost)));
            AddButton(CreationKarmaCopy.RemovePowerPoint, "karma-magic-pp-decrease",
                () => ChangeMagic(selection with { MysticAdeptPowerPoints = selection.MysticAdeptPowerPoints - 1 }),
                selection.MysticAdeptPowerPoints > 0);
            AddButton(CreationKarmaCopy.AddPowerPoint, "karma-magic-pp-increase",
                () => ChangeMagic(selection with { MysticAdeptPowerPoints = selection.MysticAdeptPowerPoints + 1 }),
                selection.MysticAdeptPowerPoints < purchase.MaximumPowerPoints);
        }
    }

    private void AddKarmaMagicSummary()
    {
        if (!_session.QuoteCurrent || _session.Quote?.Magic is not { } magic) return;
        var totals = NativeTheme.Body(CreationKarmaCopy.MagicTotals(magic.Cost.SpellKarma, magic.Cost.ComplexFormKarma,
            magic.Cost.MysticPowerPoints?.KarmaCost ?? 0, magic.Cost.TotalKarma));
        totals.AutomationId = "karma-magic-totals";
        _body.Add(totals);
        if (magic.Access.AllowsAdeptPowers)
            _body.Add(NativeTheme.Body(CreationKarmaCopy.PowerBudget(magic.PowerPointsUsed, magic.PowerPointsTotal)));
        foreach (var source in magic.Sources)
        {
            _body.Add(NativeTheme.Body(MagicKindLabel(source.Identity.Kind) + " · "
                + CreationKarmaCopy.Levels(source.Name, source.Levels)));
            _body.Add(NativeTheme.Body(source.SourceBook + " · " + source.Page, NativeTheme.Muted));
        }
    }

    private void AddKarmaMagicCatalog()
    {
        if (!_session.QuoteCurrent || _session.Quote?.Magic is not { } magic
            || _session.Selection?.MagicSelections is not { } selected || _session.Authority?.MagicCatalog is not { } catalog
            || _sourceKind is null || !MagicKindAvailable(magic.Access, _sourceKind)) return;
        var slice = catalog.Catalogs.Single(item => item.Kind == _sourceKind);
        _body.Add(NativeTheme.Title(MagicKindLabel(slice.Kind), 22));
        AddKarmaMagicSummary();
        long render = _render, appearance = CaptureAppearanceGeneration();
        var search = new SearchBar { Placeholder = CreationKarmaCopy.Search, Text = _search, AutomationId = "karma-magic-search" };
        search.TextChanged += (_, args) => { if (Current(render, appearance)) _search = args.NewTextValue ?? string.Empty; };
        _body.Add(search);
        AddButton(CreationKarmaCopy.Search, "karma-magic-search-go", () => { _page = 0; return Task.CompletedTask; });
        var rows = slice.Options.Where(item => item.Name.Contains(_search, StringComparison.CurrentCultureIgnoreCase)).ToArray();
        _page = Math.Min(_page, Math.Max(0, (rows.Length - 1) / PageSize));
        foreach (var option in rows.Skip(_page * PageSize).Take(PageSize))
        {
            _body.Add(NativeTheme.Body(option.Name));
            _body.Add(NativeTheme.Body(option.SourceBook + " · " + option.Page, NativeTheme.Muted));
            if (!option.IsEnabled)
            {
                _body.Add(NativeTheme.Body(CreationKarmaCopy.UnavailableInCurrentRules(option.Name)
                    + " · " + string.Join(", ", option.Blockers), NativeTheme.Muted));
                continue;
            }
            var id = option.Identity;
            bool chosen = selected.Tradition == id || selected.Stream == id || selected.Spells.Contains(id)
                || selected.ComplexForms.Contains(id) || selected.AdeptPowers.Any(item => item.Identity == id);
            if (id.Kind == CharacterCreationMagicResonanceKinds.AdeptPower)
            {
                int levels = selected.AdeptPowers.SingleOrDefault(item => item.Identity == id)?.Levels ?? 0;
                _body.Add(NativeTheme.Body(CreationKarmaCopy.PowerLevel(option.PointCost, levels)));
                AddButton(CreationKarmaCopy.AddPowerLevel, "karma-magic-add-" + id.SourceId,
                    () => ChangeMagic(selected with { AdeptPowers = selected.AdeptPowers.Where(item => item.Identity != id)
                        .Append(new CharacterCreationAdeptPowerAllocation(id, levels + 1)).ToArray() }),
                    levels < CharacterCreationAdeptPowerSourceRules.EffectiveMaximumLevels(option,
                        _session.Quote!.Attributes!.Attributes.Single(item => item.AttributeId == "MAG").Current));
            }
            else
                AddButton(chosen ? CreationKarmaCopy.Selected : CreationKarmaCopy.UseSelection, "karma-magic-add-" + id.SourceId,
                    () => ChangeMagic(id.Kind switch
                    {
                        CharacterCreationMagicResonanceKinds.Tradition => selected with { Tradition = id },
                        CharacterCreationMagicResonanceKinds.Stream => selected with { Stream = id },
                        CharacterCreationMagicResonanceKinds.Spell => selected with { Spells = selected.Spells.Append(id).ToArray() },
                        _ => selected with { ComplexForms = selected.ComplexForms.Append(id).ToArray() }
                    }), !chosen);
            if (chosen)
                AddButton(CreationKarmaCopy.Remove, "karma-magic-remove-" + id.SourceId, () => ChangeMagic(selected with
                {
                    Tradition = selected.Tradition == id ? null : selected.Tradition,
                    Stream = selected.Stream == id ? null : selected.Stream,
                    Spells = selected.Spells.Where(item => item != id).ToArray(),
                    ComplexForms = selected.ComplexForms.Where(item => item != id).ToArray(),
                    AdeptPowers = selected.AdeptPowers.Where(item => item.Identity != id).ToArray()
                }));
        }
        AddButton(CreationKarmaCopy.Previous, "karma-magic-previous", () => { _page--; return Task.CompletedTask; }, _page > 0);
        AddButton(CreationKarmaCopy.Next, "karma-magic-next", () => { _page++; return Task.CompletedTask; }, (_page + 1) * PageSize < rows.Length);
    }

    private async Task ChangeMagic(CharacterCreationMagicResonanceSelections selection)
    {
        if (_session.Selection is not { } current) return;
        Change(current with { MagicSelections = selection });
        await Preview();
    }
}
