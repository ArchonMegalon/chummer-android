using Chummer.Application.Characters;

namespace Chummer.Android.Native;

public sealed partial class RunnerSessionCoordinator
{
    private ICharacterCareerReputationService? _careerReputationService;
    private Sr5CareerReputationJournal? _careerReputationJournal;

    private void InitializeCareerReputationHost(ICharacterCareerReputationService? service, Sr5CareerReputationJournal? journal)
        => (_careerReputationService, _careerReputationJournal) = (service, journal);

    public bool SupportsCareerReputationEntry
        => !_disposed && _careerReputationService is not null && _careerReputationJournal is not null;

    internal Sr5CareerReputationPhoneModel CreateCareerReputationModel(ISr5CareerCheckpointOwnerAuthority? owner = null)
    {
        if (!SupportsCareerReputationEntry)
            throw new InvalidOperationException(PhoneStrings.Get("ReputationWizardUnavailable", "The local reputation authority is unavailable."));
        // Reuse the immutable Career selection generation, local owner and real
        // presenter reload adapter. No second store or account is introduced.
        var host = new RunnerSessionSr5AfterRunRewardHost(this, owner ?? new PreferencesSr5CareerCheckpointOwnerAuthority());
        if (!host.Current.IsCleanSavedSr5())
            throw new InvalidOperationException(PhoneStrings.Get("ReputationWizardRunnerChanged", "Open a clean saved SR5 Career runner."));
        return new(new Sr5CareerReputationCoordinator(_careerReputationService!, host, _careerReputationJournal!), host, host);
    }

    internal async Task<Sr5CareerReputationPhoneModel> PrepareCareerReputationEntryAsync(
        CancellationToken token = default, ISr5CareerCheckpointOwnerAuthority? owner = null)
    {
        token.ThrowIfCancellationRequested();
        var model = CreateCareerReputationModel(owner);
        await model.InitializeAsync(token);
        token.ThrowIfCancellationRequested();
        if (!model.HasCurrentSelection)
            throw new InvalidOperationException(PhoneStrings.Get("ReputationWizardRunnerChanged", "The selected runner changed."));
        // Pending/corrupt state remains visible as recovery, never falls through
        // to the old generic edit route or fabricates a new operation.
        return model;
    }
}
