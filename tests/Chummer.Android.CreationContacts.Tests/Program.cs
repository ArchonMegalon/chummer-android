using Chummer.Android.Native;
using Chummer.Contracts.Characters;

var tests = new (string Name, Action Run)[]
{
    ("Priority begins with fail-closed Contacts authority", PriorityStartsContactsLoading),
    ("Contacts terminal merge preserves prerequisite phases", ContactsMergeIsIndependent),
    ("Contacts authority remains generic across SR5 build methods", ContactsRemainGeneric)
};

foreach ((string name, Action run) in tests)
{
    run();
    Console.WriteLine($"PASS {name}");
}

Console.WriteLine($"Creation Contacts managed tests passed: {tests.Length}");
return;

static void PriorityStartsContactsLoading()
{
    CreationDashboardAuthorityPhaseProgress progress =
        CreationDashboardAuthorityPhaseProgress.ForBuildMethod(
            CharacterCreationBuildMethods.Priority);
    Require(
        progress.Contacts == CreationDashboardAuthorityPhaseState.Loading,
        "Contacts must not appear ready before the exact Core packet arrives.");
}

static void ContactsMergeIsIndependent()
{
    CreationDashboardAuthorityPhaseProgress initial =
        CreationDashboardAuthorityPhaseProgress.ForBuildMethod(
            CharacterCreationBuildMethods.Priority);
    CreationDashboardAuthorityPhaseProgress contactsReady = initial.WithTerminal(
        CreationDashboardAuthorityPhase.Contacts,
        failed: false);
    Require(
        contactsReady.Contacts == CreationDashboardAuthorityPhaseState.Ready,
        "The accepted Contacts packet must become ready.");
    Require(
        contactsReady.Prerequisite == initial.Prerequisite
        && contactsReady.Attributes == initial.Attributes
        && contactsReady.Skills == initial.Skills,
        "Accepting Contacts must not invent or overwrite another phase.");

    CreationDashboardAuthorityPhaseProgress contactsFailed = initial.WithTerminal(
        CreationDashboardAuthorityPhase.Contacts,
        failed: true);
    Require(
        contactsFailed.Contacts == CreationDashboardAuthorityPhaseState.Failed,
        "A loader failure must keep the Contacts route fail-closed.");
}

static void ContactsRemainGeneric()
{
    CreationDashboardAuthorityPhaseProgress sumToTen =
        CreationDashboardAuthorityPhaseProgress.ForBuildMethod(
            CharacterCreationBuildMethods.SumToTen);
    CreationDashboardAuthorityPhaseProgress lifeModules =
        CreationDashboardAuthorityPhaseProgress.ForBuildMethod(
            CharacterCreationBuildMethods.LifeModules);
    Require(
        sumToTen.Contacts == CreationDashboardAuthorityPhaseState.Loading
        && lifeModules.Contacts == CreationDashboardAuthorityPhaseState.Loading,
        "The typed Contacts authority must not be hardcoded to one SR5 build method.");
    Require(
        sumToTen.Skills == CreationDashboardAuthorityPhaseState.NotApplicable
        && lifeModules.Prerequisite == CreationDashboardAuthorityPhaseState.NotApplicable,
        "Contacts must stay independent from method-specific prerequisite phases.");
}

static void Require(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
