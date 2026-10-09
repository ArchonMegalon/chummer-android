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
    public static readonly Color Success = Color.FromArgb("#27715E");

    public static Label Eyebrow(string text) => new()
    {
        Text = text.ToUpperInvariant(),
        FontSize = 11,
        CharacterSpacing = 1.5,
        FontAttributes = FontAttributes.Bold,
        TextColor = Success
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

    public static Label BookProse(string text)
    {
        var label = Body(text);
        label.FontSize = 18;
        label.LineHeight = 1.4;
        return label;
    }

    public static Button ReadingButton(string text)
    {
        var button = SecondaryButton(text);
        button.HeightRequest = -1;
        button.MinimumHeightRequest = 50;
        button.LineBreakMode = LineBreakMode.WordWrap;
        return button;
    }

    public static Switch ReadableSwitch()
    {
        var toggle = new Switch { OnColor = Success, ThumbColor = Ink };
#if ANDROID
        // Android's default light off-track disappears against our white cards.
        // Keep an explicit off state; do not rely on OnColor (which only sets on).
        void ApplyContrast()
        {
            if (toggle.Handler?.PlatformView is not global::AndroidX.AppCompat.Widget.SwitchCompat view) return;
            view.TrackTintList = new global::Android.Content.Res.ColorStateList(
                new[] { new[] { global::Android.Resource.Attribute.StateChecked }, System.Array.Empty<int>() },
                new[] { global::Android.Graphics.Color.ParseColor("#27715E").ToArgb(),
                    global::Android.Graphics.Color.ParseColor("#61706E").ToArgb() });
        }
        toggle.HandlerChanged += (_, _) => ApplyContrast();
        toggle.Toggled += (_, _) => ApplyContrast();
#endif
        return toggle;
    }

    public static Button PrimaryButton(string text) => WithAvailabilityStates(new Button
    {
        Text = text,
        BackgroundColor = Ink,
        TextColor = Colors.White,
        CornerRadius = 10,
        HeightRequest = 50,
        Padding = new Thickness(18, 10),
        FontAttributes = FontAttributes.Bold
    });

    public static Button SecondaryButton(string text) => WithAvailabilityStates(new Button
    {
        Text = text,
        BackgroundColor = Colors.Transparent,
        TextColor = Ink,
        BorderColor = Line,
        BorderWidth = 1,
        CornerRadius = 10,
        HeightRequest = 50,
        Padding = new Thickness(16, 10),
        FontAttributes = FontAttributes.Bold
    });

    private static Button WithAvailabilityStates(Button button)
    {
        // Do not fade the whole control: inactive actions still need readable
        // text. Leaving Disabled restores the caller's ordinary local colors.
        VisualStateManager.SetVisualStateGroups(button, new VisualStateGroupList
        {
            new VisualStateGroup
            {
                Name = "CommonStates",
                States =
                {
                    new VisualState { Name = "Normal" },
                    new VisualState
                    {
                        Name = "Disabled",
                        Setters =
                        {
                            new Setter { Property = Button.BackgroundColorProperty, Value = Line },
                            new Setter { Property = Button.TextColorProperty, Value = Text },
                            new Setter { Property = Button.BorderColorProperty, Value = Muted },
                            new Setter { Property = Button.BorderWidthProperty, Value = 1d }
                        }
                    }
                }
            }
        });
        return button;
    }

    public static Border Card(View content, Thickness? padding = null) => new()
    {
        BackgroundColor = Surface,
        Stroke = Line,
        StrokeThickness = 1,
        StrokeShape = new RoundRectangle { CornerRadius = 12 },
        Padding = padding ?? new Thickness(16),
        Content = content
    };

    // Exact machine values remain available for troubleshooting, but do not
    // compete with player choices or get read aloud by default. Never alter
    // the underlying values (or filter arbitrary player names/book prose).
    public static VerticalStackLayout TechnicalDetails(View content, string automationId, Func<bool>? canToggle = null)
    {
        VerticalStackLayout panel = new() { Spacing = 8, AutomationId = automationId };
        content.IsVisible = false;
#if CHUMMER_API36_PROOF_INSTRUMENTATION
        content.IsVisible = true;
#endif
        string Copy() => CreationFlowStrings.Get(
            content.IsVisible ? "Qualities.HideDetails" : "Qualities.ShowDetails",
            content.IsVisible ? "Hide technical details" : "Show technical details");
        Button toggle = ReadingButton(Copy());
        toggle.AutomationId = automationId + "-toggle";
        toggle.Clicked += (_, _) =>
        {
            if (panel.Parent is null || !ReferenceEquals(toggle.Parent, panel)
                || canToggle is not null && !canToggle()) return;
            content.IsVisible = !content.IsVisible;
            toggle.Text = Copy();
        };
        panel.Add(toggle);
        panel.Add(content);
        return panel;
    }

    public static Border NavigationRow(
        string title,
        string? detail,
        Func<Task> selected,
        bool enabled = true,
        string? automationId = null,
        Action? pressed = null,
        Action? released = null,
        string? value = null)
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
        Label chevron = value is null ? Body("›", enabled ? Muted : Line) : Title(value, 28);
        if (value is not null && automationId is not null)
            chevron.AutomationId = automationId + "-value";
        chevron.FontSize = 28;
        chevron.VerticalOptions = LayoutOptions.Center;
        row.Add(chevron, 1);

        Border card = Card(row, new Thickness(16, 12));
        card.Opacity = enabled ? 1 : 0.55;
        SemanticProperties.SetDescription(card, string.IsNullOrWhiteSpace(detail) ? title : $"{title}. {detail}");

        Button interaction = new()
        {
            AutomationId = automationId,
            Text = string.Empty,
            BackgroundColor = Colors.Transparent,
            BorderWidth = 0,
            Padding = 0,
            IsEnabled = enabled,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            ZIndex = 1
        };
        SemanticProperties.SetDescription(
            interaction,
            string.IsNullOrWhiteSpace(detail) ? title : $"{title}. {detail}");
        if (pressed is not null)
            interaction.Pressed += (_, _) => pressed();
        if (released is not null)
            interaction.Released += (_, _) => released();
        interaction.Clicked += async (_, _) => await selected();
        row.Add(interaction);
        Grid.SetColumnSpan(interaction, 2);

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

    public static SearchBar SearchField(string automationId, string? value, string placeholder = "") => new()
    {
        AutomationId = automationId,
        Text = value ?? string.Empty,
        Placeholder = placeholder,
        BackgroundColor = Surface,
        TextColor = Text,
        PlaceholderColor = Muted,
        CancelButtonColor = Text
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
                // Reserve space for both sides. An Auto value column can
                // measure a GUID at full width and squeeze the caption away.
                new ColumnDefinition(GridLength.Star)
            },
            ColumnSpacing = 12
        };
        grid.Add(Body(label, Muted));
        Label valueLabel = Body(string.IsNullOrWhiteSpace(value) ? "—" : value);
        valueLabel.FontAttributes = FontAttributes.Bold;
        valueLabel.LineBreakMode = LineBreakMode.CharacterWrap;
        valueLabel.HorizontalTextAlignment = TextAlignment.End;
        grid.Add(valueLabel, 1);
        return grid;
    }
}
