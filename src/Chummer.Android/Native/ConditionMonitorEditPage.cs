using System.Globalization;
using Chummer.Presentation.Overview;

namespace Chummer.Android.Native;

public sealed class ConditionMonitorEditPage : NativePageBase
{
    private readonly WorkspaceConditionMonitorTrack _track;
    private readonly VerticalStackLayout _body = new()
    {
        Padding = new Thickness(20, 18, 20, 40),
        Spacing = 14
    };

    public ConditionMonitorEditPage(
        RunnerSessionCoordinator coordinator,
        WorkspaceConditionMonitorTrack track) : base(coordinator)
    {
        _track = track;
        Title = $"{track} damage";
        AutomationId = $"condition-monitor-editor-{Token(track)}";
        Content = new ScrollView { Content = _body };
    }

    protected override void Refresh()
    {
        _body.Clear();
        ConditionMonitorEditorState? editor = Coordinator.State.ActiveConditionMonitor;
        ConditionMonitorTrackState? track = editor?.Tracks.FirstOrDefault(candidate => candidate.Track == _track);
        if (track is null)
        {
            _body.Add(NativeTheme.Title("Damage track unavailable"));
            _body.Add(NativeTheme.Body("Reload the condition monitor before editing.", NativeTheme.Muted));
            return;
        }

        _body.Add(NativeTheme.Eyebrow(editor!.CareerEditable ? "Career condition" : "Creation preview"));
        _body.Add(NativeTheme.Title(track.Label));
        VerticalStackLayout summary = new() { Spacing = 9 };
        summary.Add(NativeTheme.Metric("Filled", $"{track.Filled} / {track.EditableMaximum}"));
        summary.Add(NativeTheme.Metric("Base track", track.TrackMaximum.ToString(CultureInfo.InvariantCulture)));
        if (track.Overflow > 0)
        {
            summary.Add(NativeTheme.Metric("Overflow", track.Overflow.ToString(CultureInfo.InvariantCulture)));
        }
        summary.Add(NativeTheme.Metric("Threshold offset", track.ThresholdOffset.ToString(CultureInfo.InvariantCulture)));
        if (!string.IsNullOrWhiteSpace(track.NaturalRecovery))
        {
            summary.Add(NativeTheme.Metric("Natural recovery", track.NaturalRecovery));
        }
        _body.Add(NativeTheme.Card(summary));

        if (!editor.CareerEditable)
        {
            _body.Add(NativeTheme.Body(
                "Damage becomes editable after the runner enters career mode.",
                NativeTheme.Muted));
            return;
        }

        _body.Add(NativeTheme.FieldLabel("Filled boxes"));
        Picker filled = NumberPicker(track);
        _body.Add(filled);

        Button apply = NativeTheme.PrimaryButton("Apply damage track");
        apply.AutomationId = $"condition-monitor-save-{Token(track.Track)}";
        apply.Clicked += async (_, _) => await RunAsync(() => Coordinator.ApplyConditionMonitorEditAsync(
            new ConditionMonitorEditRequest(track.Track, SelectedNumber(filled, track.Filled))));
        _body.Add(apply);

        Button clear = NativeTheme.SecondaryButton("Clear damage");
        clear.AutomationId = $"condition-monitor-clear-{Token(track.Track)}";
        clear.IsEnabled = track.Filled > 0;
        clear.Clicked += async (_, _) => await RunAsync(() => Coordinator.ApplyConditionMonitorEditAsync(
            new ConditionMonitorEditRequest(track.Track, 0)));
        _body.Add(clear);

        if (!string.IsNullOrWhiteSpace(Coordinator.State.Error))
        {
            _body.Add(NativeTheme.Body(Coordinator.State.Error!, NativeTheme.Danger));
        }
    }

    private static Picker NumberPicker(ConditionMonitorTrackState track)
    {
        string[] values = Enumerable.Range(0, track.EditableMaximum + 1)
            .Select(value => value.ToString(CultureInfo.InvariantCulture))
            .ToArray();
        return new Picker
        {
            AutomationId = $"condition-monitor-filled-{Token(track.Track)}",
            Title = "Filled boxes",
            ItemsSource = values,
            SelectedIndex = Math.Clamp(track.Filled, 0, values.Length - 1),
            BackgroundColor = NativeTheme.Surface,
            TextColor = NativeTheme.Text
        };
    }

    private static int SelectedNumber(Picker picker, int fallback)
        => picker.SelectedItem is string selected
            && int.TryParse(selected, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
                ? value
                : fallback;

    internal static string Token(WorkspaceConditionMonitorTrack track)
        => track.ToString().ToLowerInvariant();
}
