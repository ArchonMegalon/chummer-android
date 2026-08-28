using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

internal static class TransactionValidationCompileProbe
{
    public static Sr5TableWizardState BindThroughPublicSessionContract(
        Sr5TableWizardSnapshot snapshot)
        => new Sr5TableWizardSession().Bind(snapshot);

    public static Sr5TableWizardRecoveryObservation ObserveThroughTypedPresenter(
        Sr5TableWizardTransactionJournal journal,
        Sr5TableWizardSnapshot snapshot,
        out string observedPostconditionDigest)
        => Sr5TableWizardTypedTransactionPresenter.Observe(
            journal,
            snapshot,
            out observedPostconditionDigest);
}
