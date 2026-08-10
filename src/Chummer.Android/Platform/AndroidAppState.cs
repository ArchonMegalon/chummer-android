namespace Chummer.Android.Platform;

public enum AndroidDestination
{
    Home,
    Workbench,
    Campaign,
    Account,
    More
}

public enum AndroidNoticeKind
{
    Info,
    Success,
    Error
}

public sealed class AndroidAppState
{
    private const string DestinationPreferenceKey = "chummer.android.active-destination";
    private AndroidDestination _activeDestination = AndroidDestination.Home;
    private string _syncPosture = "Local";
    private string? _lastMessage;
    private AndroidNoticeKind _noticeKind = AndroidNoticeKind.Info;
    private bool _isBusy;

    public AndroidAppState()
    {
        string savedDestination = Preferences.Default.Get(DestinationPreferenceKey, AndroidDestination.Home.ToString());
        if (Enum.TryParse(savedDestination, ignoreCase: true, out AndroidDestination destination))
        {
            _activeDestination = destination;
        }
    }

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
            Preferences.Default.Set(DestinationPreferenceKey, value.ToString());
            NotifyChanged();
        }
    }

    public string SyncPosture
    {
        get => _syncPosture;
        set => SetField(ref _syncPosture, value);
    }

    public string? LastMessage => _lastMessage;

    public AndroidNoticeKind NoticeKind => _noticeKind;

    public bool IsBusy
    {
        get => _isBusy;
        set => SetField(ref _isBusy, value);
    }

    public void SetMessage(string message, AndroidNoticeKind kind = AndroidNoticeKind.Info)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        if (string.Equals(_lastMessage, message, StringComparison.Ordinal) && _noticeKind == kind)
        {
            return;
        }

        _lastMessage = message;
        _noticeKind = kind;
        NotifyChanged();
    }

    public void ClearMessage()
    {
        if (_lastMessage is null)
        {
            return;
        }

        _lastMessage = null;
        _noticeKind = AndroidNoticeKind.Info;
        NotifyChanged();
    }

    public bool TryNavigateBack()
    {
        if (ActiveDestination == AndroidDestination.Home)
        {
            return false;
        }

        ClearMessage();
        ActiveDestination = AndroidDestination.Home;
        return true;
    }

    private void SetField<T>(ref T field, T value)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        NotifyChanged();
    }

    private void NotifyChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
