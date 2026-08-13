using ValveResourceFormat.Renderer.Input;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// One tick's worth of buttons, what is down now, and what changed since the tick before.
/// </summary>
public readonly record struct PlayerButtonState(TrackedKeys HeldButtons, TrackedKeys PressedButtons, TrackedKeys ReleasedButtons)
{
    /// <summary>Whether any of <paramref name="buttons"/> is down.</summary>
    public bool Holding(TrackedKeys buttons) => (HeldButtons & buttons) != 0;

    /// <summary>Whether any of <paramref name="buttons"/> went down on this tick.</summary>
    public bool Pressed(TrackedKeys buttons) => (PressedButtons & buttons) != 0;

    /// <summary>Whether any of <paramref name="buttons"/> came up on this tick.</summary>
    public bool Released(TrackedKeys buttons) => (ReleasedButtons & buttons) != 0;
}
