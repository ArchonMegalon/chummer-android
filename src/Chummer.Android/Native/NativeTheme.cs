using Microsoft.Maui.Controls.Shapes;

namespace Chummer.Android.Native;

internal static class NativeTheme
{
    public static readonly Color Ink = Color.FromArgb("#102426");
    public static readonly Color InkRaised = Color.FromArgb("#173234");
    public static readonly Color Signal = Color.FromArgb("#54D6B3");
    public static readonly Color SignalSoft = Color.FromArgb("#D9F7EE");
    public static readonly Color Paper = Color.FromArgb("#F4F7F5");
    public static readonly Color Surface = Colors.White;
    public static readonly Color Text = Color.FromArgb("#142222");
    public static readonly Color Muted = Color.FromArgb("#61706E");
    public static readonly Color Line = Color.FromArgb("#DCE5E1");
    public static readonly Color Danger = Color.FromArgb("#B3261E");

    public static Label Eyebrow(string text) => new()
    {
        Text = text.ToUpperInvariant(),
        FontSize = 11,
        CharacterSpacing = 1.5,
        FontAttributes = FontAttributes.Bold,
        TextColor = Color.FromArgb("#27715E")
    };

    public static Label Title(string text, double size = 26) => new()
    {
        Text = text,
        FontSize = size,
        FontAttributes = FontAttributes.Bold,
        TextColor = Text,
        LineBreakMode = LineBreakMode.WordWrap
    };

    public static Label Body(string text, Color? color = null) => new()
    {
        Text = text,
        FontSize = 15,
        TextColor = color ?? Text,
        LineBreakMode = LineBreakMode.WordWrap
    };

    public static Button PrimaryButton(string text) => new()
    {
        Text = text,
        BackgroundColor = Ink,
        TextColor = Colors.White,
        CornerRadius = 14,
        HeightRequest = 50,
        Padding = new Thickness(18, 10),
        FontAttributes = FontAttributes.Bold
    };

    public static Button SecondaryButton(string text) => new()
    {
        Text = text,
        BackgroundColor = Colors.Transparent,
        TextColor = Ink,
        BorderColor = Line,
        BorderWidth = 1,
        CornerRadius = 14,
        HeightRequest = 50,
        Padding = new Thickness(16, 10),
        FontAttributes = FontAttributes.Bold
    };

    public static Border Card(View content, Thickness? padding = null) => new()
    {
        BackgroundColor = Surface,
        Stroke = Line,
        StrokeThickness = 1,
        StrokeShape = new RoundRectangle { CornerRadius = 20 },
        Padding = padding ?? new Thickness(18),
        Content = content
    };

    public static Border NavigationRow(
        string title,
        string? detail,
        Func<Task> selected,
        bool enabled = true,
        string? automationId = null)
    {
        Grid row = new()
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            ColumnSpacing = 16,
            MinimumHeightRequest = 58
        };
        VerticalStackLayout copy = new() { Spacing = 3, VerticalOptions = LayoutOptions.Center };
        Label titleLabel = Body(title);
        titleLabel.FontAttributes = FontAttributes.Bold;
        copy.Add(titleLabel);
        if (!string.IsNullOrWhiteSpace(detail))
        {
            Label detailLabel = Body(detail, Muted);
            detailLabel.FontSize = 13;
            copy.Add(detailLabel);
        }

        row.Add(copy);
        Label chevron = Body("›", enabled ? Muted : Line);
        chevron.FontSize = 28;
        chevron.VerticalOptions = LayoutOptions.Center;
        row.Add(chevron, 1);

        Border card = Card(row, new Thickness(16, 12));
        card.AutomationId = automationId;
        card.Opacity = enabled ? 1 : 0.55;
        SemanticProperties.SetDescription(card, string.IsNullOrWhiteSpace(detail) ? title : $"{title}. {detail}");
        if (enabled)
        {
            TapGestureRecognizer tap = new();
            tap.Tapped += async (_, _) => await selected();
            card.GestureRecognizers.Add(tap);
        }

        return card;
    }

    public static Label FieldLabel(string text)
    {
        Label label = Body(text, Muted);
        label.FontSize = 13;
        label.FontAttributes = FontAttributes.Bold;
        return label;
    }

    public static Entry TextField(string automationId, string? value, string placeholder = "") => new()
    {
        AutomationId = automationId,
        Text = value ?? string.Empty,
        Placeholder = placeholder,
        BackgroundColor = Surface,
        TextColor = Text,
        PlaceholderColor = Muted,
        ClearButtonVisibility = ClearButtonVisibility.WhileEditing
    };

    public static Editor TextArea(string automationId, string? value, string placeholder = "") => new()
    {
        AutomationId = automationId,
        Text = value ?? string.Empty,
        Placeholder = placeholder,
        BackgroundColor = Surface,
        TextColor = Text,
        PlaceholderColor = Muted,
        AutoSize = EditorAutoSizeOption.TextChanges,
        MinimumHeightRequest = 112
    };

    public static Grid Metric(string label, string value)
    {
        Grid grid = new()
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            ColumnSpacing = 12
        };
        grid.Add(Body(label, Muted));
        Label valueLabel = Body(string.IsNullOrWhiteSpace(value) ? "—" : value);
        valueLabel.FontAttributes = FontAttributes.Bold;
        grid.Add(valueLabel, 1);
        return grid;
    }
}
