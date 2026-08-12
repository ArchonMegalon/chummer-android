namespace Chummer.Android.Platform;

public static class ChummerWebRoutes
{
    private static readonly Uri PublicOrigin = new("https://chummer.run");

    public const string AccountAccess = "/account/access";
    public const string AccountDeletion = "/account/delete";
    public const string CampaignRoster = "/account/roster";
    public const string ChronicleStudio = "/groups?focus=chronicles";
    public const string GmCommand = "/gm";
    public const string Groups = "/groups";
    public const string OrganizerCommand = "/organizers";
    public const string Play = "/play";
    public const string RulesetStudio = "/account/edition-studio/sr5";
    public const string Support = "/support";

    public static Uri Resolve(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!path.StartsWith("/", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal))
        {
            throw new ArgumentException("Chummer web routes must be root-relative paths.", nameof(path));
        }

        return new Uri(PublicOrigin, path);
    }
}
