namespace Chummer.Android;

public partial class App : Microsoft.Maui.Controls.Application
{
    private readonly Func<MainShell> _createMainShell;

    public App(Func<MainShell> createMainShell)
    {
        ArgumentNullException.ThrowIfNull(createMainShell);
        InitializeComponent();
        _createMainShell = createMainShell;
    }

    // Android may recreate its Activity (for example after a font-scale change).
    // A previous Shell contains handlers tied to the disposed window scope.
    // Keep the runner coordinator in DI, but create a fresh visual tree per window.
    protected override Window CreateWindow(IActivationState? activationState)
        => new(_createMainShell());
}
