using Chummer.Contracts.Characters;

namespace Chummer.Android.Native;

internal static class Sr6CreationCopy
{
    internal static string Text(string key) => CreationAllocationStrings.Get("Sr6." + key, key);
    internal static string Label(string id) => CreationAllocationStrings.Get("Sr6.Option." + id, id);
    internal static string Budget(Sr6CreationFoundationPreview preview) => CreationAllocationStrings.Format("Sr6.Budget",
        "Attributes: {0} · Skills: {1} · Resources: {2:N0} ¥ · Adjustment: {3} · Base Magic: {4} · Base Resonance: {5}",
        preview.Budget.AttributePoints, preview.Budget.SkillPoints, preview.Budget.ResourcesNuyen,
        preview.Budget.MetatypeAdjustmentPoints, preview.BaseMagic, preview.BaseResonance);
    internal static string Blocker(string code) => code switch
    {
        Sr6CreationPriorityBlockers.CategoriesInvalid => Text("ChooseAll"),
        Sr6CreationPriorityBlockers.UniqueRanksRequired => Text("UniqueRanks"),
        Sr6CreationPriorityBlockers.SumToTenMismatch => Text("SumTen"),
        Sr6CreationFoundationBlockers.MetatypeUnavailable => Text("MetatypeRank"),
        Sr6CreationFoundationBlockers.TalentUnavailable => Text("TalentRank"),
        Sr6CreationFoundationBlockers.StaleBinding => Text("Stale"),
        RunnerSessionCoordinator.Sr6SaveNeedsRefresh => Text("SavedReopen"),
        RunnerSessionCoordinator.Sr6OutcomeUnknown => Text("Unknown"),
        _ => code
    };
}
