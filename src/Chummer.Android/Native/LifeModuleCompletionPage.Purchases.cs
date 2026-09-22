using System.Globalization;
using Chummer.Contracts.Characters;

namespace Chummer.Android.Native;

internal sealed partial class LifeModuleCompletionPage
{
    private void Gear()
    {
        if (Quote?.GearAuthority is not { } authority) return;
        if (Input.GearSelection is not { } basket)
        { Button(LifeCopy("StartSelection", "Review this selection (empty is allowed)"), "life-begin-gear", () => ChangeReview(Input with { GearSelection = [] })); return; }
        if (Quote.GearQuote is { } quote)
            Body(CreationKarmaCopy.GearTotals(quote.Budget.TotalStartingNuyen, quote.Budget.BasketCost, quote.Budget.RemainingNuyen, quote.Budget.Overspend));
        foreach (var selected in basket)
        {
            var option = authority.Options.SingleOrDefault(x => x.OptionId == selected.OptionId);
            Number(option?.Name ?? selected.OptionId, "life-gear-quantity-" + selected.OptionId, selected.Quantity, authority.MaximumQuantityPerLine,
                quantity => Change(Input with { GearSelection = Input.GearSelection!.Where(x => x.OptionId != selected.OptionId)
                    .Concat(quantity > 0 ? [new CharacterCreationGearSelection(selected.OptionId, quantity)] : []).ToArray() }));
        }
        Button(LifeCopy("SaveReview", "Save inputs and review"), "life-review-gear", Review);
        Search();
        foreach (var option in Page(authority.Options.Where(x => x.Name.Contains(_search, StringComparison.CurrentCultureIgnoreCase))))
        {
            Body(CreationKarmaCopy.GearLine(option.Name, option.PackageQuantity, option.PackageCost));
            Body(option.SourceBook + " · " + option.Page);
            foreach (string reason in option.Blockers) Body(reason);
            bool chosen = basket.Any(x => x.OptionId == option.OptionId);
            Button(chosen ? CreationKarmaCopy.Selected : CreationKarmaCopy.UseSelection, "life-gear-add-" + option.OptionId,
                () => ChangeReview(Input with { GearSelection = Input.GearSelection!.Append(new(option.OptionId, option.PackageQuantity)).ToArray() }),
                !chosen && option.IsSelectable && option.PricingIsExact && option.AvailabilityIsExact && option.Blockers.Count == 0
                    && basket.Count < authority.MaximumBasketLines);
        }
    }

    private void Lifestyles()
    {
        if (Quote?.LifestylesAuthority is not { } authority) return;
        if (Input.LifestyleSelection is not { } selected)
        { Button(LifeCopy("StartSelection", "Review this selection (empty is allowed)"), "life-begin-lifestyles", () => ChangeReview(Input with { LifestyleSelection = [] })); return; }
        // Explicit starting lifestyle: adding an item never silently changes the cash roll.
        foreach (var row in selected)
        {
            Body(row.Name);
            Number(CreationKarmaCopy.LifestyleIncrements, "life-lifestyle-increments-" + row.LifestyleId, row.Increments, int.MaxValue,
                count => Change(Input with { LifestyleSelection = Input.LifestyleSelection!.Select(x => x.LifestyleId == row.LifestyleId ? x with { Increments = count } : x).ToArray() }));
            Button(Input.StartingLifestyleId == row.LifestyleId ? CreationKarmaCopy.StartingLifestyleSelected : CreationKarmaCopy.UseStartingLifestyle,
                "life-starting-lifestyle-" + row.LifestyleId, () => ChangeReview(Input with { StartingLifestyleId = row.LifestyleId }));
            Button(CreationKarmaCopy.Remove, "life-remove-lifestyle-" + row.LifestyleId,
                () => ChangeReview(Input with { LifestyleSelection = Input.LifestyleSelection!.Where(x => x.LifestyleId != row.LifestyleId).ToArray(),
                    StartingLifestyleId = Input.StartingLifestyleId == row.LifestyleId ? null : Input.StartingLifestyleId }));
        }
        foreach (var line in Quote.LifestylesQuote?.Lines ?? [])
            Body(CreationKarmaCopy.LifestyleLine(line.Configuration.Name, line.BaseLifestyleName, line.Economics.TotalCost));
        Button(LifeCopy("SaveReview", "Save inputs and review"), "life-review-lifestyles", Review);
        Search();
        foreach (var option in Page(authority.LifestyleOptions.Where(x => x.Name.Contains(_search, StringComparison.CurrentCultureIgnoreCase))))
        {
            Body(CreationKarmaCopy.LifestyleSource(option.Name, option.BaseCost, option.DefaultIncrementId));
            Body(option.SourceBook + " · " + option.Page);
            foreach (string reason in option.Blockers) Body(reason);
            Button(option.Name, "life-lifestyle-add-" + option.OptionId, () => ChangeReview(Input with
            {
                LifestyleSelection = Input.LifestyleSelection!.Append(new CharacterCreationLifestyleConfiguration(Guid.NewGuid(), option.OptionId,
                    option.Name, CharacterCreationLifestyleStyleIds.Standard, option.DefaultIncrementId, 1, 100m, 0, false, false,
                    0, 0, 0, 0, "", "", "", [])).ToArray()
            }), option.IsSelectable && option.EligibilityIsExact && option.Blockers.Count == 0);
        }
    }

    private void Contacts()
    {
        if (Quote?.ContactsPolicy is null) return;
        if (Input.ContactSelection is not { } contacts)
        { Button(LifeCopy("StartSelection", "Review this selection (empty is allowed)"), "life-begin-contacts", () => ChangeReview(Input with { ContactSelection = [] })); return; }
        foreach (var contact in contacts)
            Button(contact.Identity.Name, "life-contact-" + contact.ContactId, () => Open(LifeCompletionStep.Contact, contact.ContactId.ToString("D")));
        Button(CreationKarmaCopy.AddContact, "life-add-contact", () => Open(LifeCompletionStep.Contact));
    }

    private void Contact()
    {
        if (Input.ContactSelection is not { } contacts) return;
        var original = contacts.SingleOrDefault(x => x.ContactId.ToString("D") == _id);
        if (_id is not null && original is null) return;
        var edit = original ?? new CharacterCreationKarmaContactSelection(Guid.NewGuid(), new("", "", "", "", "", "", "", "", "", "", "", "", ""), 1, 1);
        Text(CreationKarmaCopy.ContactName, "life-contact-name", edit.Identity.Name, value => edit = edit with { Identity = edit.Identity with { Name = value } });
        Text(CreationKarmaCopy.ContactRole, "life-contact-role", edit.Identity.Role, value => edit = edit with { Identity = edit.Identity with { Role = value } });
        Text(CreationKarmaCopy.ContactLocation, "life-contact-location", edit.Identity.Location, value => edit = edit with { Identity = edit.Identity with { Location = value } });
        Text(CreationKarmaCopy.ContactNotes, "life-contact-notes", edit.Identity.Notes, value => edit = edit with { Identity = edit.Identity with { Notes = value } });
        // These are input bounds, not a legality decision: Core checks effective limits and prices.
        Number(CreationKarmaCopy.Connection, "life-contact-connection", edit.Connection, 12, value => edit = edit with { Connection = value });
        Number(CreationKarmaCopy.Loyalty, "life-contact-loyalty", edit.Loyalty, 6, value => edit = edit with { Loyalty = value });
        Flag(CreationKarmaCopy.GroupContact, "life-contact-group", edit.IsGroup, value => edit = edit with { IsGroup = value });
        Flag(CreationKarmaCopy.FamilyContact, "life-contact-family", edit.Family, value => edit = edit with { Family = value });
        Flag(CreationKarmaCopy.BlackmailContact, "life-contact-blackmail", edit.Blackmail, value => edit = edit with { Blackmail = value });
        Button(CreationKarmaCopy.UseSelection, "life-use-contact", async () =>
        { Change(Input with { ContactSelection = contacts.Where(x => x != original).Append(edit).ToArray() }); await Review(); if (_session.Ready) await Navigation.PopAsync(); });
        Button(CreationKarmaCopy.Remove, "life-remove-contact", async () =>
        { Change(Input with { ContactSelection = contacts.Where(x => x != original).ToArray() }); await Review(); if (_session.Ready) await Navigation.PopAsync(); }, original is not null);
    }

    private static string MagicCaption(string kind) => kind switch
    {
        CharacterCreationMagicResonanceKinds.Tradition => CreationKarmaCopy.Tradition,
        CharacterCreationMagicResonanceKinds.Stream => CreationKarmaCopy.Stream,
        CharacterCreationMagicResonanceKinds.AdeptPower => CreationKarmaCopy.Powers,
        CharacterCreationMagicResonanceKinds.Spell => CreationKarmaCopy.Spells,
        CharacterCreationMagicResonanceKinds.ComplexForm => CreationKarmaCopy.ComplexForms,
        _ => kind
    };
    private static bool CanChooseMagic(CharacterCreationKarmaMagicAccess access, string kind) => kind switch
    {
        CharacterCreationMagicResonanceKinds.Tradition => access.RequiresTradition,
        CharacterCreationMagicResonanceKinds.Stream => access.RequiresStream,
        CharacterCreationMagicResonanceKinds.AdeptPower => access.AllowsAdeptPowers,
        CharacterCreationMagicResonanceKinds.Spell => access.AllowsSpells,
        CharacterCreationMagicResonanceKinds.ComplexForm => access.AllowsComplexForms,
        _ => false
    };
    private void Magic()
    {
        if (Quote?.MagicCatalog is not { } catalog) return;
        if (Input.MagicSelection is not { } selected)
        { Button(LifeCopy("StartSelection", "Review this selection (empty is allowed)"), "life-begin-magic", () => ChangeReview(Input with { MagicSelection = new(null, null, [], [], []) })); return; }
        if (Quote.MagicQuote is not { } magic) return;
        Body(CreationKarmaCopy.MagicTotals(magic.Cost.SpellKarma, magic.Cost.ComplexFormKarma, magic.Cost.MysticPowerPoints?.KarmaCost ?? 0, magic.Cost.TotalKarma));
        foreach (var source in magic.Sources) Body(source.Name + " · " + source.Levels + " · " + source.SourceBook + " " + source.Page);
        foreach (var catalogSlice in catalog.Catalogs)
            if (CanChooseMagic(magic.Access, catalogSlice.Kind)) Button(MagicCaption(catalogSlice.Kind), "life-magic-open-" + catalogSlice.Kind,
                () => Open(LifeCompletionStep.MagicCatalog, kind: catalogSlice.Kind));
        if (magic.Cost.MysticPowerPoints is { } points)
            Number(CreationKarmaCopy.Powers, "life-mystic-points", selected.MysticAdeptPowerPoints, points.MaximumPowerPoints,
                value => Change(Input with { MagicSelection = Input.MagicSelection! with { MysticAdeptPowerPoints = value } }));
        Button(LifeCopy("SaveReview", "Save inputs and review"), "life-review-magic", Review);
    }
    private void MagicCatalog()
    {
        if (Input.MagicSelection is not { } selected || Quote?.MagicQuote is not { } magic || _kind is null
            || !CanChooseMagic(magic.Access, _kind) || Quote.MagicCatalog?.Catalogs.SingleOrDefault(x => x.Kind == _kind) is not { } catalog) return;
        Search();
        foreach (var option in Page(catalog.Options.Where(x => x.Name.Contains(_search, StringComparison.CurrentCultureIgnoreCase))))
        {
            var id = option.Identity;
            Body(option.Name + " · " + option.SourceBook + " " + option.Page);
            foreach (string reason in option.Blockers) Body(reason);
            bool chosen = selected.Tradition == id || selected.Stream == id || selected.Spells.Contains(id)
                || selected.ComplexForms.Contains(id) || selected.AdeptPowers.Any(x => x.Identity == id);
            if (_kind == CharacterCreationMagicResonanceKinds.AdeptPower)
            {
                int levels = selected.AdeptPowers.SingleOrDefault(x => x.Identity == id)?.Levels ?? 0;
                Body(CreationKarmaCopy.PowerLevel(option.PointCost, levels));
                int max = CharacterCreationAdeptPowerSourceRules.EffectiveMaximumLevels(option,
                    Quote.AttributeQuote!.Attributes.Single(x => x.AttributeId == "MAG").Current);
                Button(CreationKarmaCopy.AddPowerLevel, "life-magic-add-" + id.SourceId, () => ChangeReview(Input with
                { MagicSelection = selected with { AdeptPowers = selected.AdeptPowers.Where(x => x.Identity != id).Append(new(id, levels + 1)).ToArray() } }), option.IsEnabled && levels < max);
            }
            else Button(chosen ? CreationKarmaCopy.Selected : CreationKarmaCopy.UseSelection, "life-magic-add-" + id.SourceId,
                () => ChangeReview(Input with { MagicSelection = id.Kind switch
                {
                    CharacterCreationMagicResonanceKinds.Tradition => selected with { Tradition = id },
                    CharacterCreationMagicResonanceKinds.Stream => selected with { Stream = id },
                    CharacterCreationMagicResonanceKinds.Spell => selected with { Spells = selected.Spells.Append(id).ToArray() },
                    _ => selected with { ComplexForms = selected.ComplexForms.Append(id).ToArray() }
                } }), option.IsEnabled && !chosen);
            if (chosen) Button(CreationKarmaCopy.Remove, "life-magic-remove-" + id.SourceId, () => ChangeReview(Input with
            { MagicSelection = selected with { Tradition = selected.Tradition == id ? null : selected.Tradition, Stream = selected.Stream == id ? null : selected.Stream,
                Spells = selected.Spells.Where(x => x != id).ToArray(), ComplexForms = selected.ComplexForms.Where(x => x != id).ToArray(),
                AdeptPowers = selected.AdeptPowers.Where(x => x.Identity != id).ToArray() } }));
        }
    }
}
