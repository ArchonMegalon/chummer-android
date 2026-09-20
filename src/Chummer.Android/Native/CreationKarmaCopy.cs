namespace Chummer.Android.Native;

internal static class CreationKarmaCopy
{
    public static string Title => CreationAllocationStrings.Get("Karma.Title", "Karma foundation");
    public static string Loading => CreationAllocationStrings.Get("Karma.Loading", "Checking rules and saved choices… Your draft is kept while this page loads.");
    public static string Metatype => CreationAllocationStrings.Get("Karma.Metatype", "Metatype");
    public static string Talent => CreationAllocationStrings.Get("Karma.Talent", "Talent");
    public static string Attributes => CreationAllocationStrings.Get("Karma.Attributes", "Attributes");
    public static string Skills => CreationAllocationStrings.Get("Karma.Skills", "Skills");
    public static string Groups => CreationAllocationStrings.Get("Karma.Groups", "Skill groups");
    public static string Resources => CreationAllocationStrings.Get("Karma.Resources", "Resources");
    public static string ResourceInvestment => CreationAllocationStrings.Get("Karma.ResourceInvestment", "Karma to invest");
    public static string ResourceHelp => CreationAllocationStrings.Get("Karma.ResourceHelp", "Preview profile-funded nuyen. Qualities, equipment purchases and final starting cash are not included yet. Use this amount in the draft, then review to save.");
    public static string InvalidNumber => CreationAllocationStrings.Get("Karma.InvalidNumber", "Enter a non-negative number using your language's decimal separator, without thousands separators.");
    public static string ResourceLimit(decimal maximum) => CreationAllocationStrings.Format("Karma.ResourceLimit", "Profile limit: {0} Karma", maximum);
    public static string ResourceFunding(decimal karma, decimal nuyen) => CreationAllocationStrings.Format("Karma.ResourceFunding", "Resources: {0} Karma → {1} ¥ (before qualities and equipment)",
        karma.ToString("G29", System.Globalization.CultureInfo.CurrentCulture),
        nuyen.ToString("N2", System.Globalization.CultureInfo.CurrentCulture));
    public static string Review => CreationAllocationStrings.Get("Karma.Review", "Review draft");
    public static string Scope => CreationAllocationStrings.Get("Karma.Scope", "Experimental Karma foundation: metatype, talent, attributes, skills and resource funding. This saves a pending draft, not a finished runner. Other creation stages and finalization are not ready.");
    public static string Stale => CreationAllocationStrings.Get("Karma.Stale", "This draft is no longer ready. Return to the runner and reopen it; no selection is applied automatically.");
    public static string Saved => CreationAllocationStrings.Get("Karma.Saved", "Pending Karma draft saved. The runner has not been finalized.");
    public static string Choose => CreationAllocationStrings.Get("Karma.Choose", "Choose explicitly");
    public static string Pending => CreationAllocationStrings.Get("Karma.Pending", "Selection changed — refresh the preview for current totals and costs.");
    public static string LevelHelp => CreationAllocationStrings.Get("Karma.LevelHelp", "Adjust purchased levels. Core recalculates final ratings, limits and costs when you refresh the preview.");
    public static string Preview => CreationAllocationStrings.Get("Karma.Preview", "Refresh preview");
    public static string Active => CreationAllocationStrings.Get("Karma.Active", "Active skills");
    public static string Knowledge => CreationAllocationStrings.Get("Karma.Knowledge", "Knowledge points");
    public static string KnowledgeSkills => CreationAllocationStrings.Get("Karma.KnowledgeSkills", "Knowledge skills");
    public static string Search => CreationAllocationStrings.Get("Karma.Search", "Search catalog");
    public static string Previous => CreationAllocationStrings.Get("Karma.Previous", "Previous page");
    public static string Next => CreationAllocationStrings.Get("Karma.Next", "Next page");
    public static string SkillHelp => CreationAllocationStrings.Get("Karma.SkillHelp", "Choose the payment and source option, then use this selection in the draft. Only the final review saves it.");
    public static string KarmaLevels => CreationAllocationStrings.Get("Karma.KarmaLevels", "Karma-paid levels");
    public static string KnowledgeLevels => CreationAllocationStrings.Get("Karma.KnowledgeLevels", "Knowledge-point levels");
    public static string NativeLanguage => CreationAllocationStrings.Get("Karma.NativeLanguage", "Native language");
    public static string Specialization => CreationAllocationStrings.Get("Karma.Specialization", "Specialization / exotic identity");
    public static string None => CreationAllocationStrings.Get("Karma.None", "None");
    public static string Payment => CreationAllocationStrings.Get("Karma.Payment", "Specialization payment");
    public static string Karma => CreationAllocationStrings.Get("Karma.Karma", "Karma");
    public static string UseSelection => CreationAllocationStrings.Get("Karma.UseSelection", "Use selection in draft");
    public static string Remove => CreationAllocationStrings.Get("Karma.Remove", "Remove from draft");
    public static string Confirm => CreationAllocationStrings.Get("Karma.Confirm", "Confirm and save pending draft");
    public static string Binding(long revision, long saved) => CreationAllocationStrings.Format("Karma.Binding", "Revision {0} · saved {1}", revision, saved);
    public static string Budget(decimal spent, decimal total, decimal remaining) => CreationAllocationStrings.Format("Karma.Budget", "Karma: {0} / {1} · remaining {2}", spent, total, remaining);
    public static string Cost(string name, decimal cost) => CreationAllocationStrings.Format("Karma.Cost", "{0} · {1} Karma", name, cost);
    public static string Levels(string name, int levels) => CreationAllocationStrings.Format("Karma.Levels", "{0}: {1}", name, levels);
    public static string KnowledgeBudget(decimal used, decimal total) => CreationAllocationStrings.Format("Karma.KnowledgeBudget", "Knowledge points: {0} / {1}", used, total);
    public static string ValueCost(string name, object rating, decimal cost) => CreationAllocationStrings.Format("Karma.ValueCost", "{0}: {1} · {2} Karma", name, rating, cost);
}
