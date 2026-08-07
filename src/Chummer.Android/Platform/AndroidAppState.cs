namespace Chummer.Android.Platform;

public enum AndroidDestination
{
    Home,
    Workbench,
    Campaign,
    More
}

public sealed class AndroidAppState
{
    private AndroidDestination _activeDestination = AndroidDestination.Home;

    public event EventHandler? Changed;

    public AndroidDestination ActiveDestination
    {
        get => _activeDestination;
        set
        {
            if (_activeDestination == value)
            {
                return;
            }

            _activeDestination = value;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public string SyncPosture { get; set; } = "Local";
    public string? LastMessage { get; set; }

    public bool TryNavigateBack()
    {
        if (ActiveDestination == AndroidDestination.Home)
        {
            return false;
        }

        ActiveDestination = AndroidDestination.Home;
        LastMessage = null;
        return true;
    }
}
