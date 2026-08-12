namespace Chummer.Android;

public partial class App : Microsoft.Maui.Controls.Application
{
    private readonly MainShell _mainShell;

    public App(MainShell mainShell)
    {
        InitializeComponent();
        _mainShell = mainShell;
    }

    protected override Window CreateWindow(IActivationState? activationState)
        => new(_mainShell);
}
