using Chummer.Contracts.Characters;

namespace Chummer.Android.Native;

public enum Sr5VehicleWorkshopFactoryModificationPosture
{
    Included,
    Unsupported
}

public sealed record Sr5VehicleWorkshopFactoryModificationRow(
    CharacterVehicleFactoryModificationInstructionId InstructionId,
    CharacterVehicleFactoryModificationSourceId SourceId,
    CharacterVehicleFactoryModificationInstanceId InstanceId,
    int Ordinal,
    string Name,
    string Category,
    int Rating,
    string MaximumRating,
    string Slots,
    string Capacity,
    string Cost,
    string SourceBook,
    string Page,
    string SourceNodeDigest,
    string InstructionNodeDigest,
    Sr5VehicleWorkshopFactoryModificationPosture Posture,
    bool Included,
    bool Removable,
    string BlockReason);

public sealed record Sr5VehicleWorkshopFactoryModificationProjection(
    IReadOnlyList<Sr5VehicleWorkshopFactoryModificationRow> Rows,
    bool CanContinue,
    IReadOnlyList<string> Blockers);

/// <summary>
/// Native read-only projection of Core-owned factory vehicle modifications. It
/// never adds factory children to the editable draft: their deterministic saved
/// identities are re-derived from the durable vehicle identity and the catalog-
/// bound factory instruction after every reopen.
/// </summary>
public static class Sr5VehicleWorkshopFactoryModificationConsumer
{
    public static Sr5VehicleWorkshopFactoryModificationProjection Project(
        CharacterVehicleWorkshopChassisEntry chassis,
        CharacterVehicleInstanceId vehicleInstanceId)
    {
        ArgumentNullException.ThrowIfNull(chassis);
        var blockers = new List<string>();
        var rows = new List<Sr5VehicleWorkshopFactoryModificationRow>();
        IReadOnlyList<CharacterVehicleWorkshopFactoryModificationEntry>? factoryModifications =
            chassis.FactoryModifications;
        if (vehicleInstanceId.Value == Guid.Empty)
            blockers.Add("The factory modification preview has no durable vehicle instance identity.");
        if (factoryModifications is null)
        {
            blockers.Add("The factory modification catalog collection is unavailable.");
            return new Sr5VehicleWorkshopFactoryModificationProjection([], false,
                Normalize(blockers));
        }

        int[] ordinals = factoryModifications
            .Where(item => item is not null)
            .Select(item => item.Ordinal)
            .Order()
            .ToArray();
        if (factoryModifications.Any(item => item is null)
            || ordinals.Where((ordinal, index) => ordinal != index).Any())
        {
            blockers.Add("The factory modification instruction order is incomplete or ambiguous.");
        }

        foreach (CharacterVehicleWorkshopFactoryModificationEntry? entry in factoryModifications
                     .OrderBy(item => item?.Ordinal ?? int.MinValue))
        {
            if (entry is null)
                continue;
            CharacterVehicleFactoryModificationInstanceId instanceId =
                vehicleInstanceId.Value == Guid.Empty || entry.InstructionId.Value == Guid.Empty
                    ? default
                    : CharacterVehicleWorkshopRules.DeriveFactoryModificationInstanceId(
                        vehicleInstanceId,
                        entry.InstructionId);
            CharacterVehicleFactoryModificationInstructionId expectedInstructionId =
                CharacterVehicleWorkshopRules.DeriveFactoryModificationInstructionId(
                    chassis.SourceId,
                    entry.SourceId,
                    entry.Ordinal,
                    entry.InstructionNodeDigest);
            bool structurallyExact = entry.ProjectionStatus
                                     == CharacterVehicleWorkshopProjectionStatus.Exact
                                     && entry.UnsupportedReason.Length == 0
                                     && entry.ChassisSourceId == chassis.SourceId
                                     && entry.InstructionId.Value != Guid.Empty
                                     && entry.SourceId.Value != Guid.Empty
                                     && entry.InstructionId == expectedInstructionId
                                     && instanceId.Value != Guid.Empty
                                     && CharacterVehicleWorkshopRules.IsCanonicalDigest(
                                         entry.SourceNodeDigest)
                                     && CharacterVehicleWorkshopRules.IsCanonicalDigest(
                                         entry.InstructionNodeDigest);
            string reason = structurallyExact
                ? string.Empty
                : string.IsNullOrWhiteSpace(entry.UnsupportedReason)
                    ? CharacterVehicleWorkshopBlockers.UnsupportedSelection
                    : entry.UnsupportedReason;
            if (!structurallyExact)
                blockers.Add(reason);
            rows.Add(new Sr5VehicleWorkshopFactoryModificationRow(
                entry.InstructionId,
                entry.SourceId,
                instanceId,
                entry.Ordinal,
                entry.Name,
                entry.Category,
                entry.Rating,
                entry.MaximumRating,
                entry.Slots,
                entry.Capacity,
                entry.Cost,
                entry.SourceBook,
                entry.Page,
                entry.SourceNodeDigest,
                entry.InstructionNodeDigest,
                structurallyExact
                    ? Sr5VehicleWorkshopFactoryModificationPosture.Included
                    : Sr5VehicleWorkshopFactoryModificationPosture.Unsupported,
                Included: structurallyExact,
                Removable: false,
                reason));
        }

        if (chassis.ProjectionStatus != CharacterVehicleWorkshopProjectionStatus.Exact)
        {
            blockers.Add(string.IsNullOrWhiteSpace(chassis.UnsupportedReason)
                ? CharacterVehicleWorkshopBlockers.UnsupportedSelection
                : chassis.UnsupportedReason);
        }
        else if (!string.IsNullOrEmpty(chassis.UnsupportedReason))
        {
            blockers.Add(CharacterVehicleWorkshopBlockers.UnsupportedSelection);
        }

        string[] normalized = Normalize(blockers);
        return new Sr5VehicleWorkshopFactoryModificationProjection(
            rows,
            chassis.ProjectionStatus == CharacterVehicleWorkshopProjectionStatus.Exact
            && normalized.Length == 0
            && rows.All(row => row.Posture == Sr5VehicleWorkshopFactoryModificationPosture.Included),
            normalized);
    }

    private static string[] Normalize(IEnumerable<string> blockers)
        => blockers.Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
}
