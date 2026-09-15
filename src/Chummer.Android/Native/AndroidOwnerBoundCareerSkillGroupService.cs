using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;

namespace Chummer.Android.Native;

public interface IAndroidOwnerBoundCareerSkillGroupService
{
    CharacterCareerSkillGroupAdvanceResult Advance(
        OwnerContextStamp expectedOwner, CharacterCareerSkillGroupAdvanceCommand command);
}

/// <summary>
/// Holds the real credential-writer lease only during synchronous Core work.
/// The adapter receives a fixed owner partition; neither a background queue nor
/// a same-account relink can replace the stamp admitted by the rendered review.
/// </summary>
public sealed class AndroidOwnerBoundCareerSkillGroupService(
    IWorkspaceStore store,
    ICharacterSourceDataResolver sourceData,
    IAndroidCareerSkillGroupSettingsCatalog settings,
    IOwnerContextLeaseAccessor owners) :
    ICharacterCareerSkillGroupAdvanceService, IAndroidOwnerBoundCareerSkillGroupService
{
    public CharacterCareerSkillGroupQuoteResult Quote(CharacterCareerSkillGroupQuoteRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        OwnerContextStamp expected = owners.Capture();
        using IOwnerContextLease lease = Acquire(expected);
        return CreateCore(expected.Owner).Quote(request);
    }

    // An ambient owner captured after queue admission is never write authority.
    public CharacterCareerSkillGroupAdvanceResult Advance(CharacterCareerSkillGroupAdvanceCommand command)
        => throw new InvalidOperationException("A skill-group mutation requires the original displayed owner stamp.");

    public CharacterCareerSkillGroupAdvanceResult Advance(
        OwnerContextStamp expectedOwner, CharacterCareerSkillGroupAdvanceCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        using IOwnerContextLease lease = Acquire(expectedOwner);
        return CreateCore(expectedOwner.Owner).Advance(command);
    }

    private IOwnerContextLease Acquire(OwnerContextStamp expected)
    {
        if (!expected.IsValid || !owners.TryAcquire(expected, out IOwnerContextLease? lease) || lease is null)
            throw new OperationCanceledException("The reviewed skill-group owner is no longer available.");
        try
        {
            if (lease.Stamp != expected)
                throw new InvalidOperationException("The skill-group owner authority returned another stamp.");
            return lease;
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }

    private CharacterCareerSkillGroupAdvanceService CreateCore(OwnerScope owner)
        => new(new AndroidCharacterCareerSkillGroupAdvanceWorkspace(store, sourceData, settings, owner));
}
