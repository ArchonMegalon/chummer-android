namespace Chummer.Android.Native;

/// <summary>Integer-only local draft input. Programmatic updates never dispatch another edit.</summary>
internal sealed class CreationIntegerSlider : Slider
{
    private bool _updating;
    private bool _handlingInput;
    private bool _projectionQueued;
    private readonly Func<int> _current;
    private readonly Func<(int Minimum, int Maximum, bool Enabled)> _range;

    public CreationIntegerSlider(string automationId, string label, Func<int> current,
        Func<(int Minimum, int Maximum, bool Enabled)> range, Func<int, Task> change)
    {
        AutomationId = automationId;
        MinimumTrackColor = NativeTheme.Success;
        MaximumTrackColor = NativeTheme.Muted;
        ThumbColor = NativeTheme.Text;
        HeightRequest = 48;
        HorizontalOptions = LayoutOptions.Fill;
        _current = current;
        _range = range;
        SemanticProperties.SetDescription(this, label);
        UpdateProjection();
        ValueChanged += async (_, args) =>
        {
            if (_updating || !IsEnabled) return;
            int target = (int)Math.Round(args.NewValue, MidpointRounding.AwayFromZero);
            if (target == _current()) { QueueProjection(); return; }
            _handlingInput = true;
            try { await change(target); }
            finally
            {
                _handlingInput = false;
                QueueProjection();
            }
        };
    }

    private void QueueProjection()
    {
        if (_projectionQueued) return;
        _projectionQueued = true;
        // MAUI defers writes made inside the same BindableProperty callback.
        // Snapping Value during ValueChanged can therefore replay indefinitely
        // after a simple bool guard has been cleared. Snap after that setter exits.
        if (!Dispatcher.Dispatch(() =>
        {
            _projectionQueued = false;
            UpdateProjection();
        })) _projectionQueued = false;
    }

    public void UpdateProjection()
    {
        if (_handlingInput) return;
        _updating = true;
        try
        {
            var (min, max, enabled) = _range();
            // MAUI requires Maximum > Minimum even for a fixed, disabled value.
            double upper = Math.Max(max, min + 1);
            if (min >= Maximum) Maximum = upper;
            if (upper <= Minimum) Minimum = min;
            Minimum = min;
            Maximum = upper;
            Value = Math.Clamp(_current(), min, upper);
            IsEnabled = enabled && max > min;
        }
        finally { _updating = false; }
    }
}
