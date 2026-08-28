using System.Globalization;

namespace Chummer.Android.Native;

internal readonly record struct NativeAuthoritySemanticValue(
    string AutomationId,
    string ExactValue);

/// <summary>
/// Adds machine-readable authority values without adding visible copy.  The
/// transparent one-pixel labels are overlaid on existing content so they do
/// not affect layout, while Android accessibility/UI automation receives the
/// complete value through SemanticProperties.Description.
/// </summary>
internal static class NativeAuthoritySemantics
{
    public static NativeAuthoritySemanticValue Identifier(
        string automationId,
        string? value)
    {
        ValidateAutomationId(automationId);
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"Authority value '{automationId}' is unavailable.");
        return new NativeAuthoritySemanticValue(automationId, value);
    }

    public static NativeAuthoritySemanticValue PositiveRevision(
        string automationId,
        long value)
    {
        ValidateAutomationId(automationId);
        if (value <= 0)
            throw new InvalidOperationException($"Authority revision '{automationId}' is unavailable.");
        return new NativeAuthoritySemanticValue(
            automationId,
            value.ToString(CultureInfo.InvariantCulture));
    }

    public static NativeAuthoritySemanticValue Digest(
        string automationId,
        string? value)
    {
        ValidateAutomationId(automationId);
        string? normalized = CreationPriorityLegalPathProjection
            .NormalizeMachineDigestPayload(value);
        if (normalized is null)
            throw new InvalidOperationException($"Authority digest '{automationId}' is unavailable.");
        // UI automation consumes the normalized SHA-256 payload.  The typed
        // value must still carry the exact algorithm prefix; accepting or
        // guessing another representation would turn this boundary fail-open.
        return new NativeAuthoritySemanticValue(automationId, normalized);
    }

    public static Grid Overlay(
        View visibleContent,
        params NativeAuthoritySemanticValue[] values)
    {
        ArgumentNullException.ThrowIfNull(visibleContent);
        ArgumentNullException.ThrowIfNull(values);
        if (values.Length == 0)
            throw new ArgumentException("At least one authority value is required.", nameof(values));
        if (values.Select(static item => item.AutomationId)
            .Distinct(StringComparer.Ordinal).Count() != values.Length)
        {
            throw new InvalidOperationException("Authority AutomationIds must be unique within an overlay.");
        }

        Grid overlay = new();
        overlay.Add(visibleContent);
        foreach (NativeAuthoritySemanticValue value in values)
        {
            if (string.IsNullOrWhiteSpace(value.AutomationId)
                || string.IsNullOrWhiteSpace(value.ExactValue))
            {
                throw new InvalidOperationException("An authority semantic value is incomplete.");
            }

            Label semantic = new()
            {
                AutomationId = value.AutomationId,
                Text = string.Empty,
                InputTransparent = true,
                IsEnabled = false,
                Opacity = 0.01,
                WidthRequest = 1,
                HeightRequest = 1,
                HorizontalOptions = LayoutOptions.Start,
                VerticalOptions = LayoutOptions.Start
            };
            SemanticProperties.SetDescription(semantic, value.ExactValue);
            overlay.Add(semantic);
        }
        return overlay;
    }

    private static void ValidateAutomationId(string automationId)
    {
        if (string.IsNullOrWhiteSpace(automationId))
            throw new ArgumentException("Authority AutomationId is required.", nameof(automationId));
    }
}
