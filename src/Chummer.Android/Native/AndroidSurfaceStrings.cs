using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Chummer.Android.Native;

/// <summary>
/// Immutable UI-copy pack for the first localized creation and public Stories
/// surfaces. A pack is selected as a whole so a missing translation can never
/// produce a silently mixed-language screen.
/// </summary>
public sealed class AndroidSurfaceCopy
{
    private readonly IReadOnlyDictionary<string, string> _values;

    internal AndroidSurfaceCopy(
        CultureInfo displayCulture,
        string resourceLanguage,
        bool usesEnglishFallback,
        IReadOnlyDictionary<string, string> values)
    {
        DisplayCulture = displayCulture;
        ResourceLanguage = resourceLanguage;
        UsesEnglishFallback = usesEnglishFallback;
        _values = values;
    }

    public CultureInfo DisplayCulture { get; }
    public string ResourceLanguage { get; }
    public bool UsesEnglishFallback { get; }
    public string this[string key] => _values.TryGetValue(key, out string? value)
        ? value
        : throw new KeyNotFoundException($"Missing Android surface resource key: {key}");

    public string Format(string key, params object?[] arguments) =>
        string.Format(DisplayCulture, this[key], arguments);
}

public static class AndroidSurfaceStrings
{
    private static readonly IReadOnlyDictionary<string, string> English = Catalog(
        ("Common.Ok", "OK"),
        ("Common.Yes", "yes"),
        ("Common.No", "no"),
        ("Common.Unavailable", "unavailable"),
        ("Common.Retry", "Retry"),
        ("Common.Previous", "Previous"),
        ("Common.Next", "Next"),
        ("Common.Rules", "Rules"),
        ("Common.Runtime", "Runtime"),
        ("Common.Profile", "Profile"),
        ("Common.Remaining", "Remaining"),
        ("Common.Lines", "Lines"),
        ("Common.Receipt", "Receipt"),
        ("Common.WorkspaceRevision", "Workspace revision"),
        ("Common.DraftRevision", "Draft revision"),
        ("Resources.PageTitle", "Creation resources"),
        ("Resources.Eyebrow", "Character creation · SR5"),
        ("Resources.Title", "Resources"),
        ("Resources.Intro", "Choose how much creation Karma to convert into nuyen. Core owns the exact priority grant, conversion, carryover, blockers, and persisted draft."),
        ("Resources.AuthorityUnavailable", "Resources authority unavailable"),
        ("Resources.AuthorityBlocked", "Resources authority blocked"),
        ("Resources.CurrentBudget", "Current exact budget"),
        ("Resources.SavedDraft", "Saved Resources draft"),
        ("Resources.Option", "Option"),
        ("Resources.KarmaInvested", "Karma invested"),
        ("Resources.Draft", "Draft {0}"),
        ("Resources.ConversionOptions", "Karma conversion options"),
        ("Resources.OptionDetail", "{0} Karma → {1} nuyen · {2} total"),
        ("Resources.KeepAllKarma", "Keep all creation Karma"),
        ("Resources.ConvertKarma", "Convert {0} Karma"),
        ("Resources.Purchases", "Creation purchases"),
        ("Resources.ConfirmBeforeGear", "Confirm an exact Resources draft before selecting Gear."),
        ("Resources.GearUnavailable", "The typed Gear presenter is unavailable."),
        ("Resources.OpenGearDetail", "Open the active-source catalog with {0} nuyen remaining."),
        ("Resources.ChooseGear", "Choose Gear"),
        ("Resources.PreviewUnavailable", "Resources preview unavailable"),
        ("Resources.Binding", "Revision {0} · saved {1} · snapshot {2} · source {3}"),
        ("Resources.PriorityNuyen", "Priority nuyen"),
        ("Resources.StartingNuyen", "Starting nuyen"),
        ("Resources.KnownPurchases", "Known purchases"),
        ("Resources.CarryoverLimit", "Carryover limit"),
        ("Resources.ExactBudget", "Exact Core budget"),
        ("Resources.IncompleteBudget", "Purchase-cost authority is incomplete"),
        ("Resources.BudgetSemantic", "Starting nuyen {0}. Remaining {1}."),
        ("Resources.CoreAuthority", "Core authority"),
        ("Resources.BuildMethod", "Build method"),
        ("Resources.ResourceRank", "Resource rank"),
        ("Resources.MaximumConversion", "Maximum conversion"),
        ("Resources.MaximumAvailability", "Maximum availability"),
        ("ResourcesPreview.PageTitle", "Review resources"),
        ("ResourcesPreview.Eyebrow", "Explicit review"),
        ("ResourcesPreview.Title", "Resources preview"),
        ("ResourcesPreview.Saved", "Saved"),
        ("ResourcesPreview.Back", "Back to Resources"),
        ("ResourcesPreview.Reopen", "Reopen the persisted Core draft"),
        ("ResourcesPreview.Confirm", "Confirm Resources draft"),
        ("ResourcesPreview.ConfirmExact", "This explicit confirmation persists only the typed Resources draft. It does not write character XML."),
        ("ResourcesPreview.ConfirmStale", "The workspace or authority changed. This preview cannot be confirmed."),
        ("ResourcesPreview.ExactDelta", "Exact delta"),
        ("ResourcesPreview.KarmaBefore", "Karma before"),
        ("ResourcesPreview.KarmaAfter", "Karma after"),
        ("ResourcesPreview.StartingBefore", "Starting nuyen before"),
        ("ResourcesPreview.StartingAfter", "Starting nuyen after"),
        ("ResourcesPreview.RemainingAfter", "Remaining after purchases"),
        ("ResourcesPreview.CarryoverExcess", "Carryover excess"),
        ("ResourcesPreview.Finalization", "Finalization contribution"),
        ("ResourcesPreview.PriorityRank", "Priority rank"),
        ("ResourcesPreview.PriorityGrant", "Priority grant"),
        ("ResourcesPreview.KarmaConverted", "Karma converted"),
        ("ResourcesPreview.Preview", "Preview {0}"),
        ("ResourcesPreview.ReceiptDigest", "Receipt {0}"),
        ("Gear.PageTitle", "Creation gear"),
        ("Gear.Eyebrow", "Character creation · SR5 Resources"),
        ("Gear.Title", "Gear"),
        ("Gear.Intro", "Build a draft basket from the active-source Core catalog. Unsupported rows remain visible but disabled; exact costs and legality are never inferred by this screen."),
        ("Gear.AuthorityUnavailable", "Gear authority unavailable"),
        ("Gear.AuthorityBlocked", "Gear authority blocked"),
        ("Gear.Binding", "Revision {0} · saved {1} · snapshot {2} · source {3}"),
        ("Gear.PersistedDraft", "Persisted exact Gear draft"),
        ("Gear.StartingNuyen", "Starting nuyen"),
        ("Gear.BasketCost", "Basket cost"),
        ("Gear.NoConfirmedBasket", "No Gear basket has been confirmed yet."),
        ("Gear.Draft", "Draft {0} · {1}"),
        ("Gear.DraftBasket", "Draft basket"),
        ("Gear.NoCatalogLines", "No catalog lines selected."),
        ("Gear.BasketAuthorityChanged", "Basket authority changed"),
        ("Gear.ReviewBasket", "Review exact Gear basket"),
        ("Gear.CoreWillCalculate", "Core will calculate the exact basket and reject stale, illegal, unsupported, or unaffordable selections."),
        ("Gear.ChangeBasket", "Change the basket before requesting another preview."),
        ("Gear.PackageDetail", "{0} · {1} ¥ per {2} · {3}"),
        ("Gear.Quantity", "Quantity"),
        ("Gear.Remove", "Remove"),
        ("Gear.ActiveCatalog", "Active-source catalog"),
        ("Gear.Search", "Search name, category, source, or legality"),
        ("Gear.NoMatches", "No catalog rows match this search."),
        ("Gear.Showing", "Showing {0}–{1} of {2} rows."),
        ("Gear.CatalogDetail", "{0} · {1} ¥ / {2} · Avail {3} {4} · {5} {6}"),
        ("Gear.CatalogUnavailable", "Unavailable · {0}"),
        ("Gear.PreviewUnavailable", "Gear preview unavailable"),
        ("Gear.CoreAuthority", "Core authority"),
        ("Gear.CatalogRows", "Catalog rows"),
        ("Gear.MaximumAvailability", "Maximum availability"),
        ("Gear.MaximumBasketLines", "Maximum basket lines"),
        ("Gear.MaximumQuantity", "Maximum quantity"),
        ("GearPreview.PageTitle", "Review Gear"),
        ("GearPreview.Eyebrow", "Explicit review"),
        ("GearPreview.Title", "Gear basket preview"),
        ("GearPreview.Back", "Back to Gear"),
        ("GearPreview.Reopen", "Reopen the persisted Core Gear draft"),
        ("GearPreview.Confirm", "Confirm Gear draft"),
        ("GearPreview.ConfirmExact", "This stores only the typed Gear draft; the raw character XML remains byte-identical."),
        ("GearPreview.ConfirmStale", "The workspace, Resources draft, or Gear authority changed. Reopen Gear before confirming."),
        ("GearPreview.ExactProjection", "Exact Core projection"),
        ("GearPreview.BasketBefore", "Basket before"),
        ("GearPreview.BasketAfter", "Basket after"),
        ("GearPreview.LineDetail", "{0} · quantity {1} · {2} · Avail {3} {4} · {5} {6}"),
        ("GearPreview.Persisted", "Persisted and rebound"),
        ("Origin.PageTitle", "Life Modules"),
        ("Origin.StageTurn", "Stage {0} · turn {1}"),
        ("Origin.LocaleFallback", "Story language · English fallback · formatting {0}"),
        ("Origin.Locale", "Story language · {0} · formatting {1}"),
        ("Origin.LocaleSemantic", "Origin story resource language {0}; formatting locale {1}; English fallback {2}"),
        ("Origin.StorySemantic", "Origin story in {0}, through the current decision point"),
        ("Origin.Budget", "Life Modules budget"),
        ("Origin.BudgetTotal", "Total"),
        ("Origin.BudgetUsed", "Used"),
        ("Origin.BudgetRemaining", "Remaining"),
        ("Origin.BudgetSemantic", "Life Modules budget: {0} total, {1} used, {2} remaining, unit {3}"),
        ("Origin.SourceAnchors", "Source anchors · {0}"),
        ("Origin.Karma", "Karma"),
        ("Origin.Effect", "{0} · {1}: {2} → {3}"),
        ("Origin.NarrativeOnly", "{0} · narrative only; mechanics unchanged"),
        ("Origin.Review", "Review the exact source and effects above before confirming."),
        ("Origin.Confirm", "Confirm this decision"),
        ("Origin.UnavailableTitle", "Life Modules unavailable"),
        ("Origin.UnavailableDetail", "The source-bound SR5 Life Module authority is unavailable."),
        ("Origin.PreviewUnavailableTitle", "Preview unavailable"),
        ("Origin.PreviewUnavailableDetail", "The decision changed. Reopen this step."),
        ("Origin.DecisionNotSavedTitle", "Decision not saved"),
        ("Origin.DecisionNotSavedDetail", "The authority rejected the decision."),
        ("Stories.Title", "Stories"),
        ("Stories.LoadingCatalog", "Loading public stories"),
        ("Stories.Public", "Public runner stories"),
        ("Stories.Intro", "Read and download without an account. Signals are shown here, but voting appears only after the final chapter."),
        ("Stories.EmptyTitle", "No public stories yet"),
        ("Stories.EmptyDetail", "Published Origin Stories will appear here when Stories returns them."),
        ("Stories.LanguageEdition", "Language edition"),
        ("Stories.AllLanguages", "All languages"),
        ("Stories.LanguageFilterSemantic", "Filter Stories by authoritative publication language edition"),
        ("Stories.Archetype", "Archetype"),
        ("Stories.AllArchetypes", "All archetypes"),
        ("Stories.ArchetypeFilterSemantic", "Filter Stories by source-bound archetype for its rules edition"),
        ("Stories.FilterEmptyTitle", "No stories match these filters"),
        ("Stories.FilterEmptyDetail", "Change one or both authoritative filters to see other public stories."),
        ("Stories.FilterEmptySemantic", "No Stories match the selected language and archetype filters"),
        ("Stories.Runner", "Runner"),
        ("Stories.MetadataSemantic", "Publication language {0}; archetypes {1}"),
        ("Stories.Owner", "Story owner: {0}"),
        ("Stories.Signals", "Signals: {0}"),
        ("Stories.Read", "Read story"),
        ("Stories.LoadingStory", "Loading story"),
        ("Stories.Chapter", "Chapter {0} of {1}"),
        ("Stories.NextChapter", "Next chapter"),
        ("Stories.LastChapter", "Last chapter"),
        ("Stories.PublicDownloads", "Public downloads"),
        ("Stories.NoDownloads", "No downloads are available for this revision."),
        ("Stories.DownloadUnavailable", "Download unavailable"),
        ("Stories.DownloadOpenFailed", "Android could not open this public download."),
        ("Stories.NoAccount", "No account is required to read or download."),
        ("Stories.LoadingSignal", "Loading Signal status…"),
        ("Stories.Signal", "Signal"),
        ("Stories.End", "You reached the end"),
        ("Stories.SignIn", "Sign in to Signal"),
        ("Stories.SignInDetail", "Link your account from More. This story and its downloads remain public."),
        ("Stories.RetryStory", "Retry story"),
        ("Stories.CatalogInvalid", "Stories returned invalid language or archetype metadata. No inferred filters were shown."),
        ("Stories.UnavailableCatalog", "Stories is unavailable. No public response was assumed."),
        ("Stories.UnavailableReader", "Stories is unavailable. No public response or Signal change was assumed."),
        ("Stories.OwnerCannotSignal", "Story owners cannot Signal their own story."),
        ("Stories.SignalStatusUnavailable", "Signal status is unavailable. Reading and downloads remain public."),
        ("Stories.AccountToVote", "An account is required only to vote."),
        ("Stories.Retract", "Retract Signal"),
        ("Stories.RetractDetail", "Remove your Signal from this exact story revision."),
        ("Stories.SignalStory", "Signal this story"),
        ("Stories.SignalStoryDetail", "Cast one Signal for this exact story revision."),
        ("Stories.SignalUnavailable", "Signal voting is unavailable for this story."),
        ("Stories.State.SignInDetail", "An account is required only to vote. Reading and downloads stay public."),
        ("Stories.State.OfflineTitle", "You're offline"),
        ("Stories.State.OfflineDetail", "Reconnect to load Stories. No cached response was assumed."),
        ("Stories.State.ChangedTitle", "Story changed"),
        ("Stories.State.ChangedDetail", "Reload the current immutable story revision before continuing."),
        ("Stories.State.ModerationTitle", "Story under review"),
        ("Stories.State.ModerationDetail", "This story is not publicly available while moderation is in progress."),
        ("Stories.State.UnavailableTitle", "Story unavailable"),
        ("Stories.State.UnavailableDetail", "This public story revision could not be found."),
        ("Stories.State.BusyTitle", "Stories is busy"),
        ("Stories.State.BusyDetail", "Too many requests reached Stories. Try again later."),
        ("Stories.State.RetryAfter", "{0} Retry in about {1} seconds."),
        ("Stories.State.PublicAccessTitle", "Public access unavailable"),
        ("Stories.State.LoginUnexpected", "Stories unexpectedly requested a login, so no story was loaded."),
        ("Stories.State.SignalNotAllowed", "Signal not allowed"),
        ("Stories.State.ActionNotAllowed", "This action is not allowed for the current account."),
        ("Stories.State.SignalUnavailable", "Signal unavailable"),
        ("Stories.State.StoriesUnavailable", "Stories unavailable"),
        ("Stories.State.NoResponse", "No public story or Signal response was assumed."));

    private static readonly IReadOnlyDictionary<string, string> German = Translate(English,
        ("Common.Ok", "OK"), ("Common.Yes", "ja"), ("Common.No", "nein"), ("Common.Unavailable", "nicht verfügbar"), ("Common.Retry", "Erneut versuchen"),
        ("Common.Previous", "Zurück"), ("Common.Next", "Weiter"), ("Common.Rules", "Regeln"),
        ("Common.Runtime", "Laufzeit"), ("Common.Profile", "Profil"), ("Common.Remaining", "Verbleibend"),
        ("Common.Lines", "Positionen"), ("Common.Receipt", "Beleg"), ("Common.WorkspaceRevision", "Workspace-Revision"),
        ("Common.DraftRevision", "Entwurfsrevision"),
        ("Resources.PageTitle", "Ressourcen der Charaktererschaffung"), ("Resources.Eyebrow", "Charaktererschaffung · SR5"),
        ("Resources.Title", "Ressourcen"), ("Resources.Intro", "Wähle, wie viel Erschaffungskarma in Nuyen umgewandelt wird. Core verwaltet Prioritätsgutschrift, Umrechnung, Übertrag, Blocker und den gespeicherten Entwurf exakt."),
        ("Resources.AuthorityUnavailable", "Ressourceninstanz nicht verfügbar"), ("Resources.AuthorityBlocked", "Ressourceninstanz blockiert"),
        ("Resources.CurrentBudget", "Aktuelles exaktes Budget"), ("Resources.SavedDraft", "Gespeicherter Ressourcenentwurf"),
        ("Resources.Option", "Option"), ("Resources.KarmaInvested", "Eingesetztes Karma"), ("Resources.Draft", "Entwurf {0}"),
        ("Resources.ConversionOptions", "Optionen zur Karmaumwandlung"), ("Resources.OptionDetail", "{0} Karma → {1} Nuyen · {2} gesamt"),
        ("Resources.KeepAllKarma", "Gesamtes Erschaffungskarma behalten"), ("Resources.ConvertKarma", "{0} Karma umwandeln"),
        ("Resources.Purchases", "Käufe bei der Charaktererschaffung"), ("Resources.ConfirmBeforeGear", "Bestätige zuerst einen exakten Ressourcenentwurf, bevor du Ausrüstung auswählst."),
        ("Resources.GearUnavailable", "Der typisierte Ausrüstungs-Presenter ist nicht verfügbar."), ("Resources.OpenGearDetail", "Aktiven Quellenkatalog mit {0} verbleibenden Nuyen öffnen."),
        ("Resources.ChooseGear", "Ausrüstung wählen"), ("Resources.PreviewUnavailable", "Ressourcenvorschau nicht verfügbar"),
        ("Resources.Binding", "Revision {0} · gespeichert {1} · Snapshot {2} · Quelle {3}"), ("Resources.PriorityNuyen", "Prioritäts-Nuyen"),
        ("Resources.StartingNuyen", "Start-Nuyen"), ("Resources.KnownPurchases", "Bekannte Käufe"), ("Resources.CarryoverLimit", "Übertragsgrenze"),
        ("Resources.ExactBudget", "Exaktes Core-Budget"), ("Resources.IncompleteBudget", "Kosteninstanz für Käufe ist unvollständig"),
        ("Resources.BudgetSemantic", "Start-Nuyen {0}. Verbleibend {1}."), ("Resources.CoreAuthority", "Core-Instanz"),
        ("Resources.BuildMethod", "Erschaffungsmethode"), ("Resources.ResourceRank", "Ressourcenrang"),
        ("Resources.MaximumConversion", "Maximale Umwandlung"), ("Resources.MaximumAvailability", "Maximale Verfügbarkeit"),
        ("ResourcesPreview.PageTitle", "Ressourcen prüfen"), ("ResourcesPreview.Eyebrow", "Explizite Prüfung"),
        ("ResourcesPreview.Title", "Ressourcenvorschau"), ("ResourcesPreview.Saved", "Gespeichert"),
        ("ResourcesPreview.Back", "Zurück zu Ressourcen"), ("ResourcesPreview.Reopen", "Gespeicherten Core-Entwurf erneut öffnen"),
        ("ResourcesPreview.Confirm", "Ressourcenentwurf bestätigen"), ("ResourcesPreview.ConfirmExact", "Diese ausdrückliche Bestätigung speichert nur den typisierten Ressourcenentwurf. Das Charakter-XML wird nicht geschrieben."),
        ("ResourcesPreview.ConfirmStale", "Workspace oder Instanz haben sich geändert. Diese Vorschau kann nicht bestätigt werden."),
        ("ResourcesPreview.ExactDelta", "Exaktes Delta"), ("ResourcesPreview.KarmaBefore", "Karma vorher"),
        ("ResourcesPreview.KarmaAfter", "Karma nachher"), ("ResourcesPreview.StartingBefore", "Start-Nuyen vorher"),
        ("ResourcesPreview.StartingAfter", "Start-Nuyen nachher"), ("ResourcesPreview.RemainingAfter", "Nach Käufen verbleibend"),
        ("ResourcesPreview.CarryoverExcess", "Übertragsüberschuss"), ("ResourcesPreview.Finalization", "Finalisierungsbeitrag"),
        ("ResourcesPreview.PriorityRank", "Prioritätsrang"), ("ResourcesPreview.PriorityGrant", "Prioritätsgutschrift"),
        ("ResourcesPreview.KarmaConverted", "Umgewandeltes Karma"), ("ResourcesPreview.Preview", "Vorschau {0}"),
        ("ResourcesPreview.ReceiptDigest", "Beleg {0}"),
        ("Gear.PageTitle", "Ausrüstung der Charaktererschaffung"), ("Gear.Eyebrow", "Charaktererschaffung · SR5-Ressourcen"),
        ("Gear.Title", "Ausrüstung"), ("Gear.Intro", "Erstelle einen Entwurfskorb aus dem aktiven Core-Quellenkatalog. Nicht unterstützte Einträge bleiben sichtbar, aber deaktiviert; exakte Kosten und Legalität werden auf diesem Bildschirm niemals abgeleitet."),
        ("Gear.AuthorityUnavailable", "Ausrüstungsinstanz nicht verfügbar"), ("Gear.AuthorityBlocked", "Ausrüstungsinstanz blockiert"),
        ("Gear.Binding", "Revision {0} · gespeichert {1} · Snapshot {2} · Quelle {3}"), ("Gear.PersistedDraft", "Gespeicherter exakter Ausrüstungsentwurf"),
        ("Gear.StartingNuyen", "Start-Nuyen"), ("Gear.BasketCost", "Korbsumme"), ("Gear.NoConfirmedBasket", "Noch kein Ausrüstungskorb bestätigt."),
        ("Gear.Draft", "Entwurf {0} · {1}"), ("Gear.DraftBasket", "Entwurfskorb"), ("Gear.NoCatalogLines", "Keine Katalogposition ausgewählt."),
        ("Gear.BasketAuthorityChanged", "Kataloginstanz des Korbs geändert"), ("Gear.ReviewBasket", "Exakten Ausrüstungskorb prüfen"),
        ("Gear.CoreWillCalculate", "Core berechnet den exakten Korb und lehnt veraltete, illegale, nicht unterstützte oder unbezahlbare Auswahl ab."),
        ("Gear.ChangeBasket", "Ändere den Korb, bevor du eine weitere Vorschau anforderst."), ("Gear.PackageDetail", "{0} · {1} ¥ je {2} · {3}"),
        ("Gear.Quantity", "Menge"), ("Gear.Remove", "Entfernen"), ("Gear.ActiveCatalog", "Aktiver Quellenkatalog"),
        ("Gear.Search", "Name, Kategorie, Quelle oder Legalität suchen"), ("Gear.NoMatches", "Keine Katalogzeile entspricht dieser Suche."),
        ("Gear.Showing", "Zeige {0}–{1} von {2} Einträgen."), ("Gear.CatalogDetail", "{0} · {1} ¥ / {2} · Verf. {3} {4} · {5} {6}"),
        ("Gear.CatalogUnavailable", "Nicht verfügbar · {0}"), ("Gear.PreviewUnavailable", "Ausrüstungsvorschau nicht verfügbar"),
        ("Gear.CoreAuthority", "Core-Instanz"), ("Gear.CatalogRows", "Katalogeinträge"), ("Gear.MaximumAvailability", "Maximale Verfügbarkeit"),
        ("Gear.MaximumBasketLines", "Maximale Korbpositionen"), ("Gear.MaximumQuantity", "Maximale Menge"),
        ("GearPreview.PageTitle", "Ausrüstung prüfen"), ("GearPreview.Eyebrow", "Explizite Prüfung"),
        ("GearPreview.Title", "Vorschau des Ausrüstungskorbs"), ("GearPreview.Back", "Zurück zur Ausrüstung"),
        ("GearPreview.Reopen", "Gespeicherten Core-Ausrüstungsentwurf erneut öffnen"), ("GearPreview.Confirm", "Ausrüstungsentwurf bestätigen"),
        ("GearPreview.ConfirmExact", "Dies speichert nur den typisierten Ausrüstungsentwurf; das rohe Charakter-XML bleibt byteidentisch."),
        ("GearPreview.ConfirmStale", "Workspace, Ressourcenentwurf oder Ausrüstungsinstanz haben sich geändert. Öffne Ausrüstung vor der Bestätigung erneut."),
        ("GearPreview.ExactProjection", "Exakte Core-Projektion"), ("GearPreview.BasketBefore", "Korb vorher"),
        ("GearPreview.BasketAfter", "Korb nachher"), ("GearPreview.LineDetail", "{0} · Menge {1} · {2} · Verf. {3} {4} · {5} {6}"),
        ("GearPreview.Persisted", "Gespeichert und neu gebunden"), ("Origin.PageTitle", "Lebensmodule"),
        ("Origin.StageTurn", "Phase {0} · Zug {1}"), ("Origin.LocaleFallback", "Geschichtssprache · englischsprachiger Fallback · Formatierung {0}"),
        ("Origin.Locale", "Geschichtssprache · {0} · Formatierung {1}"), ("Origin.LocaleSemantic", "Ressourcensprache der Vorgeschichte {0}; Formatierungslocale {1}; englischer Fallback {2}"),
        ("Origin.StorySemantic", "Vorgeschichte in {0} bis zum aktuellen Entscheidungspunkt"),
        ("Origin.Budget", "Lebensmodulbudget"), ("Origin.BudgetTotal", "Gesamt"),
        ("Origin.BudgetUsed", "Verwendet"), ("Origin.BudgetRemaining", "Verbleibend"),
        ("Origin.BudgetSemantic", "Lebensmodulbudget: {0} gesamt, {1} verwendet, {2} verbleibend, Einheit {3}"),
        ("Origin.SourceAnchors", "Quellenanker · {0}"), ("Origin.Karma", "Karma"),
        ("Origin.Effect", "{0} · {1}: {2} → {3}"), ("Origin.NarrativeOnly", "{0} · nur Erzählung; Mechanik unverändert"),
        ("Origin.Review", "Prüfe vor der Bestätigung die genaue Quelle und die Effekte oben."), ("Origin.Confirm", "Diese Entscheidung bestätigen"),
        ("Origin.UnavailableTitle", "Lebensmodule nicht verfügbar"), ("Origin.UnavailableDetail", "Die quellgebundene SR5-Lebensmodulinstanz ist nicht verfügbar."),
        ("Origin.PreviewUnavailableTitle", "Vorschau nicht verfügbar"), ("Origin.PreviewUnavailableDetail", "Die Entscheidung hat sich geändert. Öffne diesen Schritt erneut."),
        ("Origin.DecisionNotSavedTitle", "Entscheidung nicht gespeichert"), ("Origin.DecisionNotSavedDetail", "Die Regelinstanz hat die Entscheidung abgelehnt."),
        ("Stories.Title", "Geschichten"), ("Stories.LoadingCatalog", "Öffentliche Geschichten werden geladen"),
        ("Stories.Public", "Öffentliche Runner-Geschichten"), ("Stories.Intro", "Lesen und Herunterladen ohne Konto. Signale werden hier angezeigt; abgestimmt wird erst nach dem letzten Kapitel."),
        ("Stories.EmptyTitle", "Noch keine öffentlichen Geschichten"), ("Stories.EmptyDetail", "Veröffentlichte Vorgeschichten erscheinen hier, sobald Stories sie bereitstellt."),
        ("Stories.LanguageEdition", "Sprachausgabe"), ("Stories.AllLanguages", "Alle Sprachen"),
        ("Stories.LanguageFilterSemantic", "Geschichten nach verbindlicher Veröffentlichungssprache filtern"), ("Stories.Archetype", "Archetyp"),
        ("Stories.AllArchetypes", "Alle Archetypen"), ("Stories.ArchetypeFilterSemantic", "Geschichten nach quellengebundenem Archetyp der Regeledition filtern"),
        ("Stories.FilterEmptyTitle", "Keine Geschichte entspricht diesen Filtern"), ("Stories.FilterEmptyDetail", "Ändere einen oder beide verbindlichen Filter, um andere öffentliche Geschichten zu sehen."),
        ("Stories.FilterEmptySemantic", "Keine Geschichte entspricht den gewählten Sprach- und Archetypfiltern"), ("Stories.Runner", "Runner"),
        ("Stories.MetadataSemantic", "Veröffentlichungssprache {0}; Archetypen {1}"), ("Stories.Owner", "Geschichte von: {0}"),
        ("Stories.Signals", "Signale: {0}"), ("Stories.Read", "Geschichte lesen"), ("Stories.LoadingStory", "Geschichte wird geladen"),
        ("Stories.Chapter", "Kapitel {0} von {1}"), ("Stories.NextChapter", "Nächstes Kapitel"), ("Stories.LastChapter", "Letztes Kapitel"), ("Stories.PublicDownloads", "Öffentliche Downloads"),
        ("Stories.NoDownloads", "Für diese Revision sind keine Downloads verfügbar."), ("Stories.DownloadUnavailable", "Download nicht verfügbar"),
        ("Stories.DownloadOpenFailed", "Android konnte diesen öffentlichen Download nicht öffnen."), ("Stories.NoAccount", "Zum Lesen oder Herunterladen ist kein Konto erforderlich."),
        ("Stories.LoadingSignal", "Signalstatus wird geladen…"), ("Stories.Signal", "Signal"), ("Stories.End", "Du hast das Ende erreicht"),
        ("Stories.SignIn", "Bei Signal anmelden"), ("Stories.SignInDetail", "Verknüpfe dein Konto unter Mehr. Diese Geschichte und ihre Downloads bleiben öffentlich."),
        ("Stories.RetryStory", "Geschichte erneut laden"), ("Stories.CatalogInvalid", "Stories lieferte ungültige Sprach- oder Archetypmetadaten. Es wurden keine Filter abgeleitet."),
        ("Stories.UnavailableCatalog", "Stories ist nicht verfügbar. Es wurde keine öffentliche Antwort angenommen."),
        ("Stories.UnavailableReader", "Stories ist nicht verfügbar. Es wurde keine öffentliche Antwort oder Signaländerung angenommen."),
        ("Stories.OwnerCannotSignal", "Eigentümer können ihrer eigenen Geschichte kein Signal geben."),
        ("Stories.SignalStatusUnavailable", "Der Signalstatus ist nicht verfügbar. Lesen und Downloads bleiben öffentlich."),
        ("Stories.AccountToVote", "Nur zum Abstimmen ist ein Konto erforderlich."), ("Stories.Retract", "Signal zurückziehen"),
        ("Stories.RetractDetail", "Entferne dein Signal von genau dieser Geschichtsrevision."), ("Stories.SignalStory", "Dieser Geschichte ein Signal geben"),
        ("Stories.SignalStoryDetail", "Gib genau dieser Geschichtsrevision ein Signal."), ("Stories.SignalUnavailable", "Die Signalabstimmung ist für diese Geschichte nicht verfügbar."),
        ("Stories.State.SignInDetail", "Nur zum Abstimmen ist ein Konto erforderlich. Lesen und Downloads bleiben öffentlich."),
        ("Stories.State.OfflineTitle", "Du bist offline"), ("Stories.State.OfflineDetail", "Stelle die Verbindung wieder her, um Geschichten zu laden. Es wurde kein Cache angenommen."),
        ("Stories.State.ChangedTitle", "Geschichte geändert"), ("Stories.State.ChangedDetail", "Lade die aktuelle unveränderliche Geschichtsrevision neu."),
        ("Stories.State.ModerationTitle", "Geschichte wird geprüft"), ("Stories.State.ModerationDetail", "Diese Geschichte ist während der Moderation nicht öffentlich verfügbar."),
        ("Stories.State.UnavailableTitle", "Geschichte nicht verfügbar"), ("Stories.State.UnavailableDetail", "Diese öffentliche Geschichtsrevision wurde nicht gefunden."),
        ("Stories.State.BusyTitle", "Stories ist ausgelastet"), ("Stories.State.BusyDetail", "Stories erhielt zu viele Anfragen. Versuche es später erneut."),
        ("Stories.State.RetryAfter", "{0} Erneut versuchen in etwa {1} Sekunden."), ("Stories.State.PublicAccessTitle", "Öffentlicher Zugriff nicht verfügbar"),
        ("Stories.State.LoginUnexpected", "Stories verlangte unerwartet eine Anmeldung; daher wurde keine Geschichte geladen."),
        ("Stories.State.SignalNotAllowed", "Signal nicht erlaubt"), ("Stories.State.ActionNotAllowed", "Diese Aktion ist für das aktuelle Konto nicht erlaubt."),
        ("Stories.State.SignalUnavailable", "Signal nicht verfügbar"), ("Stories.State.StoriesUnavailable", "Stories nicht verfügbar"),
        ("Stories.State.NoResponse", "Es wurde keine öffentliche Geschichte oder Signalantwort angenommen."));

    private static readonly IReadOnlyDictionary<string, string> Spanish = Translate(English,
        ("Common.Ok", "Aceptar"), ("Common.Yes", "sí"), ("Common.No", "no"), ("Common.Unavailable", "no disponible"), ("Common.Retry", "Reintentar"),
        ("Common.Previous", "Anterior"), ("Common.Next", "Siguiente"), ("Common.Rules", "Reglas"),
        ("Common.Runtime", "Ejecución"), ("Common.Profile", "Perfil"), ("Common.Remaining", "Restante"),
        ("Common.Lines", "Líneas"), ("Common.Receipt", "Recibo"), ("Common.WorkspaceRevision", "Revisión del espacio"),
        ("Common.DraftRevision", "Revisión del borrador"),
        ("Resources.PageTitle", "Recursos de creación"), ("Resources.Eyebrow", "Creación de personaje · SR5"),
        ("Resources.Title", "Recursos"), ("Resources.Intro", "Elige cuánto Karma de creación convertir en nuyen. Core controla de forma exacta la concesión de prioridad, la conversión, el remanente, los bloqueos y el borrador guardado."),
        ("Resources.AuthorityUnavailable", "Autoridad de Recursos no disponible"), ("Resources.AuthorityBlocked", "Autoridad de Recursos bloqueada"),
        ("Resources.CurrentBudget", "Presupuesto exacto actual"), ("Resources.SavedDraft", "Borrador de Recursos guardado"),
        ("Resources.Option", "Opción"), ("Resources.KarmaInvested", "Karma invertido"), ("Resources.Draft", "Borrador {0}"),
        ("Resources.ConversionOptions", "Opciones de conversión de Karma"), ("Resources.OptionDetail", "{0} Karma → {1} nuyen · {2} total"),
        ("Resources.KeepAllKarma", "Conservar todo el Karma de creación"), ("Resources.ConvertKarma", "Convertir {0} Karma"),
        ("Resources.Purchases", "Compras de creación"), ("Resources.ConfirmBeforeGear", "Confirma un borrador exacto de Recursos antes de elegir Equipo."),
        ("Resources.GearUnavailable", "El presentador tipado de Equipo no está disponible."), ("Resources.OpenGearDetail", "Abrir el catálogo de fuentes activas con {0} nuyen restantes."),
        ("Resources.ChooseGear", "Elegir Equipo"), ("Resources.PreviewUnavailable", "Vista previa de Recursos no disponible"),
        ("Resources.Binding", "Revisión {0} · guardada {1} · instantánea {2} · fuente {3}"), ("Resources.PriorityNuyen", "Nuyen de prioridad"),
        ("Resources.StartingNuyen", "Nuyen inicial"), ("Resources.KnownPurchases", "Compras conocidas"), ("Resources.CarryoverLimit", "Límite de remanente"),
        ("Resources.ExactBudget", "Presupuesto exacto de Core"), ("Resources.IncompleteBudget", "La autoridad de costes de compra está incompleta"),
        ("Resources.BudgetSemantic", "Nuyen inicial {0}. Restante {1}."), ("Resources.CoreAuthority", "Autoridad de Core"),
        ("Resources.BuildMethod", "Método de creación"), ("Resources.ResourceRank", "Rango de Recursos"),
        ("Resources.MaximumConversion", "Conversión máxima"), ("Resources.MaximumAvailability", "Disponibilidad máxima"),
        ("ResourcesPreview.PageTitle", "Revisar Recursos"), ("ResourcesPreview.Eyebrow", "Revisión explícita"),
        ("ResourcesPreview.Title", "Vista previa de Recursos"), ("ResourcesPreview.Saved", "Guardado"),
        ("ResourcesPreview.Back", "Volver a Recursos"), ("ResourcesPreview.Reopen", "Reabrir el borrador persistido de Core"),
        ("ResourcesPreview.Confirm", "Confirmar borrador de Recursos"), ("ResourcesPreview.ConfirmExact", "Esta confirmación explícita solo guarda el borrador tipado de Recursos. No escribe el XML del personaje."),
        ("ResourcesPreview.ConfirmStale", "El espacio de trabajo o la autoridad cambiaron. No se puede confirmar esta vista previa."),
        ("ResourcesPreview.ExactDelta", "Delta exacto"), ("ResourcesPreview.KarmaBefore", "Karma anterior"),
        ("ResourcesPreview.KarmaAfter", "Karma posterior"), ("ResourcesPreview.StartingBefore", "Nuyen inicial anterior"),
        ("ResourcesPreview.StartingAfter", "Nuyen inicial posterior"), ("ResourcesPreview.RemainingAfter", "Restante tras las compras"),
        ("ResourcesPreview.CarryoverExcess", "Exceso de remanente"), ("ResourcesPreview.Finalization", "Contribución de finalización"),
        ("ResourcesPreview.PriorityRank", "Rango de prioridad"), ("ResourcesPreview.PriorityGrant", "Concesión de prioridad"),
        ("ResourcesPreview.KarmaConverted", "Karma convertido"), ("ResourcesPreview.Preview", "Vista previa {0}"),
        ("ResourcesPreview.ReceiptDigest", "Recibo {0}"),
        ("Gear.PageTitle", "Equipo de creación"), ("Gear.Eyebrow", "Creación de personaje · Recursos SR5"),
        ("Gear.Title", "Equipo"), ("Gear.Intro", "Crea una cesta de borrador desde el catálogo de fuentes activas de Core. Las filas no compatibles siguen visibles, pero desactivadas; esta pantalla nunca infiere costes ni legalidad."),
        ("Gear.AuthorityUnavailable", "Autoridad de Equipo no disponible"), ("Gear.AuthorityBlocked", "Autoridad de Equipo bloqueada"),
        ("Gear.Binding", "Revisión {0} · guardada {1} · instantánea {2} · fuente {3}"), ("Gear.PersistedDraft", "Borrador exacto de Equipo persistido"),
        ("Gear.StartingNuyen", "Nuyen inicial"), ("Gear.BasketCost", "Coste de la cesta"), ("Gear.NoConfirmedBasket", "Aún no se confirmó una cesta de Equipo."),
        ("Gear.Draft", "Borrador {0} · {1}"), ("Gear.DraftBasket", "Cesta de borrador"), ("Gear.NoCatalogLines", "No hay líneas del catálogo seleccionadas."),
        ("Gear.BasketAuthorityChanged", "Cambió la autoridad de la cesta"), ("Gear.ReviewBasket", "Revisar la cesta exacta de Equipo"),
        ("Gear.CoreWillCalculate", "Core calculará la cesta exacta y rechazará selecciones obsoletas, ilegales, no compatibles o inasequibles."),
        ("Gear.ChangeBasket", "Cambia la cesta antes de solicitar otra vista previa."), ("Gear.PackageDetail", "{0} · {1} ¥ por {2} · {3}"),
        ("Gear.Quantity", "Cantidad"), ("Gear.Remove", "Eliminar"), ("Gear.ActiveCatalog", "Catálogo de fuentes activas"),
        ("Gear.Search", "Buscar nombre, categoría, fuente o legalidad"), ("Gear.NoMatches", "Ninguna fila del catálogo coincide con esta búsqueda."),
        ("Gear.Showing", "Mostrando {0}–{1} de {2} filas."), ("Gear.CatalogDetail", "{0} · {1} ¥ / {2} · Disp. {3} {4} · {5} {6}"),
        ("Gear.CatalogUnavailable", "No disponible · {0}"), ("Gear.PreviewUnavailable", "Vista previa de Equipo no disponible"),
        ("Gear.CoreAuthority", "Autoridad de Core"), ("Gear.CatalogRows", "Filas del catálogo"), ("Gear.MaximumAvailability", "Disponibilidad máxima"),
        ("Gear.MaximumBasketLines", "Máximo de líneas de cesta"), ("Gear.MaximumQuantity", "Cantidad máxima"),
        ("GearPreview.PageTitle", "Revisar Equipo"), ("GearPreview.Eyebrow", "Revisión explícita"),
        ("GearPreview.Title", "Vista previa de la cesta de Equipo"), ("GearPreview.Back", "Volver a Equipo"),
        ("GearPreview.Reopen", "Reabrir el borrador persistido de Equipo de Core"), ("GearPreview.Confirm", "Confirmar borrador de Equipo"),
        ("GearPreview.ConfirmExact", "Esto solo guarda el borrador tipado de Equipo; el XML bruto del personaje permanece idéntico byte a byte."),
        ("GearPreview.ConfirmStale", "El espacio, el borrador de Recursos o la autoridad de Equipo cambiaron. Reabre Equipo antes de confirmar."),
        ("GearPreview.ExactProjection", "Proyección exacta de Core"), ("GearPreview.BasketBefore", "Cesta anterior"),
        ("GearPreview.BasketAfter", "Cesta posterior"), ("GearPreview.LineDetail", "{0} · cantidad {1} · {2} · Disp. {3} {4} · {5} {6}"),
        ("GearPreview.Persisted", "Persistido y revinculado"), ("Origin.PageTitle", "Módulos de vida"),
        ("Origin.StageTurn", "Etapa {0} · turno {1}"), ("Origin.LocaleFallback", "Idioma de la historia · alternativa en inglés · formato {0}"),
        ("Origin.Locale", "Idioma de la historia · {0} · formato {1}"), ("Origin.LocaleSemantic", "Idioma del recurso de la historia de origen {0}; configuración regional {1}; alternativa en inglés {2}"),
        ("Origin.StorySemantic", "Historia de origen en {0}, hasta el punto de decisión actual"),
        ("Origin.Budget", "Presupuesto de Módulos de vida"), ("Origin.BudgetTotal", "Total"),
        ("Origin.BudgetUsed", "Usado"), ("Origin.BudgetRemaining", "Restante"),
        ("Origin.BudgetSemantic", "Presupuesto de Módulos de vida: {0} total, {1} usado, {2} restante, unidad {3}"),
        ("Origin.SourceAnchors", "Anclajes de fuente · {0}"), ("Origin.Karma", "Karma"),
        ("Origin.Effect", "{0} · {1}: {2} → {3}"), ("Origin.NarrativeOnly", "{0} · solo narración; mecánicas sin cambios"),
        ("Origin.Review", "Revisa la fuente exacta y los efectos anteriores antes de confirmar."), ("Origin.Confirm", "Confirmar esta decisión"),
        ("Origin.UnavailableTitle", "Módulos de vida no disponibles"), ("Origin.UnavailableDetail", "La autoridad de Módulos de vida de SR5 vinculada a la fuente no está disponible."),
        ("Origin.PreviewUnavailableTitle", "Vista previa no disponible"), ("Origin.PreviewUnavailableDetail", "La decisión cambió. Vuelve a abrir este paso."),
        ("Origin.DecisionNotSavedTitle", "Decisión no guardada"), ("Origin.DecisionNotSavedDetail", "La autoridad rechazó la decisión."),
        ("Stories.Title", "Historias"), ("Stories.LoadingCatalog", "Cargando historias públicas"),
        ("Stories.Public", "Historias públicas de runners"), ("Stories.Intro", "Lee y descarga sin cuenta. Aquí se muestran las señales, pero la votación aparece solo después del último capítulo."),
        ("Stories.EmptyTitle", "Aún no hay historias públicas"), ("Stories.EmptyDetail", "Las historias de origen publicadas aparecerán aquí cuando Stories las entregue."),
        ("Stories.LanguageEdition", "Edición de idioma"), ("Stories.AllLanguages", "Todos los idiomas"),
        ("Stories.LanguageFilterSemantic", "Filtrar Historias por la edición de idioma de publicación autorizada"), ("Stories.Archetype", "Arquetipo"),
        ("Stories.AllArchetypes", "Todos los arquetipos"), ("Stories.ArchetypeFilterSemantic", "Filtrar Historias por el arquetipo vinculado a la fuente de su edición de reglas"),
        ("Stories.FilterEmptyTitle", "Ninguna historia coincide con estos filtros"), ("Stories.FilterEmptyDetail", "Cambia uno o ambos filtros autorizados para ver otras historias públicas."),
        ("Stories.FilterEmptySemantic", "Ninguna Historia coincide con los filtros de idioma y arquetipo"), ("Stories.Runner", "Runner"),
        ("Stories.MetadataSemantic", "Idioma de publicación {0}; arquetipos {1}"), ("Stories.Owner", "Historia de: {0}"),
        ("Stories.Signals", "Señales: {0}"), ("Stories.Read", "Leer historia"), ("Stories.LoadingStory", "Cargando historia"),
        ("Stories.Chapter", "Capítulo {0} de {1}"), ("Stories.NextChapter", "Capítulo siguiente"), ("Stories.LastChapter", "Último capítulo"), ("Stories.PublicDownloads", "Descargas públicas"),
        ("Stories.NoDownloads", "No hay descargas disponibles para esta revisión."), ("Stories.DownloadUnavailable", "Descarga no disponible"),
        ("Stories.DownloadOpenFailed", "Android no pudo abrir esta descarga pública."), ("Stories.NoAccount", "No se requiere una cuenta para leer o descargar."),
        ("Stories.LoadingSignal", "Cargando estado de Signal…"), ("Stories.Signal", "Signal"), ("Stories.End", "Has llegado al final"),
        ("Stories.SignIn", "Iniciar sesión para Signal"), ("Stories.SignInDetail", "Vincula tu cuenta desde Más. Esta historia y sus descargas seguirán siendo públicas."),
        ("Stories.RetryStory", "Reintentar historia"), ("Stories.CatalogInvalid", "Stories devolvió metadatos de idioma o arquetipo no válidos. No se mostraron filtros inferidos."),
        ("Stories.UnavailableCatalog", "Stories no está disponible. No se supuso ninguna respuesta pública."),
        ("Stories.UnavailableReader", "Stories no está disponible. No se supuso ninguna respuesta pública ni cambio de Signal."),
        ("Stories.OwnerCannotSignal", "Quienes poseen una historia no pueden dar Signal a su propia historia."),
        ("Stories.SignalStatusUnavailable", "El estado de Signal no está disponible. La lectura y las descargas siguen siendo públicas."),
        ("Stories.AccountToVote", "Solo se requiere una cuenta para votar."), ("Stories.Retract", "Retirar Signal"),
        ("Stories.RetractDetail", "Elimina tu Signal de esta revisión exacta de la historia."), ("Stories.SignalStory", "Dar Signal a esta historia"),
        ("Stories.SignalStoryDetail", "Emite un Signal para esta revisión exacta de la historia."), ("Stories.SignalUnavailable", "La votación de Signal no está disponible para esta historia."),
        ("Stories.State.SignInDetail", "Solo se requiere una cuenta para votar. La lectura y las descargas siguen siendo públicas."),
        ("Stories.State.OfflineTitle", "Estás sin conexión"), ("Stories.State.OfflineDetail", "Vuelve a conectarte para cargar Historias. No se supuso una respuesta en caché."),
        ("Stories.State.ChangedTitle", "La historia cambió"), ("Stories.State.ChangedDetail", "Recarga la revisión inmutable actual de la historia antes de continuar."),
        ("Stories.State.ModerationTitle", "Historia en revisión"), ("Stories.State.ModerationDetail", "Esta historia no está disponible públicamente mientras se modera."),
        ("Stories.State.UnavailableTitle", "Historia no disponible"), ("Stories.State.UnavailableDetail", "No se encontró esta revisión pública de la historia."),
        ("Stories.State.BusyTitle", "Stories está ocupado"), ("Stories.State.BusyDetail", "Llegaron demasiadas solicitudes a Stories. Inténtalo más tarde."),
        ("Stories.State.RetryAfter", "{0} Reintenta en unos {1} segundos."), ("Stories.State.PublicAccessTitle", "Acceso público no disponible"),
        ("Stories.State.LoginUnexpected", "Stories solicitó un inicio de sesión inesperado, así que no se cargó ninguna historia."),
        ("Stories.State.SignalNotAllowed", "Signal no permitido"), ("Stories.State.ActionNotAllowed", "Esta acción no está permitida para la cuenta actual."),
        ("Stories.State.SignalUnavailable", "Signal no disponible"), ("Stories.State.StoriesUnavailable", "Stories no disponible"),
        ("Stories.State.NoResponse", "No se supuso ninguna respuesta de historia pública ni de Signal."));

    static AndroidSurfaceStrings()
    {
        AssertExactKeyParity();
    }

    public static AndroidSurfaceCopy Resolve(CultureInfo? culture = null)
    {
        CultureInfo requested = culture ?? CultureInfo.CurrentUICulture;
        string language = requested.TwoLetterISOLanguageName.ToLowerInvariant();
        return language switch
        {
            "de" => new(requested, "de", false, German),
            "es" => new(requested, "es", false, Spanish),
            "en" => new(requested, "en", false, English),
            _ => new(CultureInfo.GetCultureInfo("en-US"), "en", true, English)
        };
    }

    public static AndroidSurfaceCopy Resolve(string? cultureName)
    {
        if (string.IsNullOrWhiteSpace(cultureName))
            return Resolve(CultureInfo.CurrentUICulture);
        try
        {
            return Resolve(CultureInfo.GetCultureInfo(cultureName));
        }
        catch (CultureNotFoundException)
        {
            return new AndroidSurfaceCopy(
                CultureInfo.GetCultureInfo("en-US"),
                "en",
                true,
                English);
        }
    }

    public static IReadOnlyCollection<string> Keys => English.Keys.ToArray();

    public static void AssertExactKeyParity()
    {
        string[] canonical = English.Keys.OrderBy(key => key, StringComparer.Ordinal).ToArray();
        foreach (IReadOnlyDictionary<string, string> catalog in new[] { German, Spanish })
        {
            string[] candidate = catalog.Keys.OrderBy(key => key, StringComparer.Ordinal).ToArray();
            if (!canonical.SequenceEqual(candidate, StringComparer.Ordinal)
                || catalog.Values.Any(string.IsNullOrWhiteSpace))
                throw new InvalidOperationException("Android surface localization catalogs must have exact, non-empty key parity.");
            foreach (string key in canonical)
            {
                string[] expected = PlaceholderIndexes(English[key]);
                string[] actual = PlaceholderIndexes(catalog[key]);
                if (!expected.SequenceEqual(actual, StringComparer.Ordinal))
                    throw new InvalidOperationException($"Android surface localization placeholder mismatch: {key}");
            }
        }
    }

    private static IReadOnlyDictionary<string, string> Catalog(
        params (string Key, string Value)[] values) => new ReadOnlyDictionary<string, string>(
        values.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal));

    private static IReadOnlyDictionary<string, string> Translate(
        IReadOnlyDictionary<string, string> source,
        params (string Key, string Value)[] translations)
    {
        if (translations.Length != source.Count)
            throw new InvalidOperationException("Every Android surface resource needs an explicit translation.");
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach ((string key, string value) in translations)
        {
            if (!source.ContainsKey(key) || string.IsNullOrWhiteSpace(value) || !result.TryAdd(key, value))
                throw new InvalidOperationException($"Invalid Android surface translation key: {key}");
        }
        return new ReadOnlyDictionary<string, string>(result);
    }

    private static string[] PlaceholderIndexes(string value) => Regex.Matches(value, @"\{(\d+)(?:[^}]*)\}")
        .Select(match => match.Groups[1].Value)
        .OrderBy(index => index, StringComparer.Ordinal)
        .ToArray();
}
