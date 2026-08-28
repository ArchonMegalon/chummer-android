namespace Chummer.Android.Native;

/// <summary>
/// Renderer-owned description of the bounded SR5 Career and table lanes that
/// this Android build can actually reach. It contains no rule values and does
/// not make unavailable mutations look editable.
/// </summary>
public enum Sr5CareerRunCapabilityStatus
{
    Available,
    ReadOnly,
    Unavailable
}

public sealed record Sr5CareerRunCapability(
    string Id,
    string LabelKey,
    Sr5CareerRunCapabilityStatus Status,
    string AuthorityKey)
{
    public string Label => Sr5CareerFlowStrings.Text(LabelKey);

    public string Authority => Sr5CareerFlowStrings.Text(AuthorityKey);
}

public static class Sr5CareerRunCapabilityCatalog
{
    public const string CyberwareCommerceRoute = "sr5-career/commerce/cyberware";

    public static IReadOnlyList<Sr5CareerRunCapability> BeforeRun { get; } =
    [
        Available(
            "before-run-edge",
            "Edge preparation",
            "typed Career Edge request with revision-bound review and receipt"),
        Unavailable("before-run-loadout", "Loadout selection"),
        Unavailable("before-run-preparation", "Preparation purchases and healing"),
        Unavailable("before-run-contacts", "Contact selection"),
        Unavailable("before-run-commitments", "Run commitments")
    ];

    public static IReadOnlyList<Sr5CareerRunCapability> AfterRun { get; } =
    [
        ReadOnly(
            "after-run-karma",
            "Karma reward",
            "digest-bound completed-run proposal context; this settlement does not award it again"),
        ReadOnly(
            "after-run-nuyen",
            "Nuyen reward",
            "digest-bound completed-run proposal context; this settlement does not award it again"),
        Available(
            "after-run-heat",
            "Heat",
            "typed atomic After Run settlement"),
        Available(
            "after-run-street-cred",
            "Street Cred",
            "typed atomic After Run settlement"),
        Available(
            "after-run-notoriety",
            "Notoriety",
            "typed atomic After Run settlement"),
        Available(
            "after-run-public-awareness",
            "Public Awareness",
            "typed atomic After Run settlement"),
        Available(
            "after-run-contacts",
            "New contacts and contact Karma expense",
            "typed atomic After Run settlement"),
        Unavailable("after-run-injuries", "Injuries"),
        Unavailable("after-run-ammo", "Ammo reconciliation"),
        Unavailable("after-run-loot", "Loot acquisition"),
        Unavailable("after-run-expenses", "Arbitrary expenses"),
        Unavailable("after-run-log", "Run log mutation")
    ];

    public static IReadOnlyList<string> ResourceKeys { get; } = BeforeRun
        .Concat(AfterRun)
        .SelectMany(static capability => new[]
        {
            capability.LabelKey,
            capability.AuthorityKey
        })
        .Distinct(StringComparer.Ordinal)
        .Order(StringComparer.Ordinal)
        .ToArray();

    private static Sr5CareerRunCapability Available(string id, string label, string authority)
        => new(id, label, Sr5CareerRunCapabilityStatus.Available, authority);

    private static Sr5CareerRunCapability ReadOnly(string id, string label, string authority)
        => new(id, label, Sr5CareerRunCapabilityStatus.ReadOnly, authority);

    private static Sr5CareerRunCapability Unavailable(string id, string label)
        => new(
            id,
            label,
            Sr5CareerRunCapabilityStatus.Unavailable,
            "no typed Core/Presentation mutation authority in this build");
}
