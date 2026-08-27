using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

/// <summary>
/// Compatibility route for older callers. The supplied projection is deliberately ignored:
/// the governed page reloads it under an exact saved-document double read before enabling UI.
/// </summary>
public sealed class CareerCalendarPage : Sr5DowntimeCalendarWizardPage
{
    public CareerCalendarPage(
        RunnerSessionCoordinator coordinator,
        CareerCalendarEditorState editor) : base(coordinator)
    {
        ArgumentNullException.ThrowIfNull(editor);
    }
}
