namespace ValveResourceFormat.Renderer.Input;

/// <summary>
/// Flags for keyboard and mouse input state tracking.
/// </summary>
[Flags]
#pragma warning disable CA2217 // Do not mark enums with FlagsAttribute
public enum TrackedKeys : long
#pragma warning restore CA2217
{
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
    None = 0,

    Shift = 1 << 0,
    Alt = 1 << 1,

    W = 1 << 2,
    A = 1 << 3,
    S = 1 << 4,
    D = 1 << 5,
    Q = 1 << 6,
    Z = 1 << 7,
    X = 1 << 8,
    Control = 1 << 9,
    Space = 1 << 10,
    Escape = 1 << 11,

    MouseWheelUp = 1 << 28,
    MouseWheelDown = 1 << 29,
    MouseLeft = 1 << 30,
    MouseRight = 1 << 31,
    MouseLeftOrRight = MouseLeft | MouseRight,
#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member
}
