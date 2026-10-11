namespace Chummer.Android.Native;

/// <summary>Integer-only local draft input. Programmatic updates never dispatch another edit.</summary>
internal sealed class CreationIntegerSlider : Slider
{
    private bool _updating;
    private bool _handlingInput;
    private bool _projectionQueued;
    private readonly Func<int> _current;
    private readonly Func<(int Minimum, int Maximum, bool Enabled)> _range;
    private readonly Func<int, bool>? _canSelect;
    private readonly Func<object?>? _selectionVersion;
    private object? _scaleVersion;
    private (int Minimum, int Maximum, bool Enabled)? _scaleRange;
    private readonly Dictionary<int, bool> _available = [];
    private readonly Dictionary<int, Label> _ticks = [];

    public Grid RatingScale { get; } = new()
    {
        ColumnSpacing = 0, Margin = new Thickness(12, -4, 12, 0),
        InputTransparent = true
    };

    public CreationIntegerSlider(string automationId, string label, Func<int> current,
        Func<(int Minimum, int Maximum, bool Enabled)> range, Func<int, Task> change,
        Func<int, bool>? canSelect = null, Func<object?>? selectionVersion = null)
    {
        AutomationId = automationId;
        MinimumTrackColor = NativeTheme.Success;
        MaximumTrackColor = NativeTheme.Muted;
        ThumbColor = NativeTheme.Text;
        HeightRequest = 48;
        HorizontalOptions = LayoutOptions.Fill;
        _current = current;
        _range = range;
        _canSelect = canSelect;
        _selectionVersion = selectionVersion;
        RatingScale.AutomationId = automationId + "-scale";
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
            UpdateScale(min, max, enabled);
        }
        finally { _updating = false; }
    }

    private void UpdateScale(int min, int max, bool enabled)
    {
        object? version = _selectionVersion?.Invoke();
        var range = (min, max, enabled);
        if (_scaleRange != range || !ReferenceEquals(_scaleVersion, version))
        {
            // Reuse affordances for the same draft. Budget/other-rating changes
            // produce a new projection identity and invalidate every affected scale.
            _scaleVersion = version;
            _scaleRange = range;
            _available.Clear();
            int intervals = (int)Math.Min(12, Math.Max(0L, (long)max - min));
            var ratings = intervals == 0 ? new[] { min }
                : Enumerable.Range(0, intervals + 1)
                    .Select(i => (int)Math.Round(min + ((double)max - min) * i / intervals)).ToArray();
            if (!_ticks.Keys.SequenceEqual(ratings))
            {
                RatingScale.Clear(); RatingScale.ColumnDefinitions.Clear(); _ticks.Clear();
                for (int i = 0; i < ratings.Length; i++)
                {
                    int rating = ratings[i];
                    RatingScale.ColumnDefinitions.Add(new(i == 0 || i == ratings.Length - 1
                        ? new GridLength(0.5, GridUnitType.Star) : GridLength.Star));
                    var tick = NativeTheme.Body(rating.ToString(System.Globalization.CultureInfo.CurrentCulture));
                    tick.AutomationId = AutomationId + "-value-" + rating;
                    tick.FontSize = 14;
                    tick.HorizontalTextAlignment = i == 0 ? TextAlignment.Start
                        : i == ratings.Length - 1 ? TextAlignment.End : TextAlignment.Center;
                    _ticks.Add(rating, tick);
                    RatingScale.Add(tick, i);
                }
            }
            foreach (int rating in ratings)
                _available[rating] = enabled && (_canSelect?.Invoke(rating) ?? true);
        }
        foreach (var (rating, tick) in _ticks)
        {
            bool available = _available[rating];
            tick.TextColor = available ? NativeTheme.Text : Color.FromArgb("#919896");
            tick.FontAttributes = rating == _current() ? FontAttributes.Bold : FontAttributes.None;
            tick.TextDecorations = available ? TextDecorations.None : TextDecorations.Strikethrough;
            SemanticProperties.SetDescription(tick, tick.Text + (available ? string.Empty : ". " +
                PhoneStrings.Get("CreationRatingUnavailable", "Not available with the current allocation")));
        }
    }
}
