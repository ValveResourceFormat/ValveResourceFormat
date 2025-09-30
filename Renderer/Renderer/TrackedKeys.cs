namespace ValveResourceFormat.Renderer;

/// <summary>
/// Flags for keyboard and mouse input state tracking.
/// </summary>
[Flags]
#pragma warning disable CA2217 // Do not mark enums with FlagsAttribute
public enum TrackedKeys
#pragma warning restore CA2217
{
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
    None = 0,

    Shift = 1 << 0,
    Alt = 1 << 1,

    Forward = 1 << 2,
    Left = 1 << 3,
    Back = 1 << 4,
    Right = 1 << 5,
    Up = 1 << 6,
    Down = 1 << 7,
    Control = 1 << 8,
    Space = 1 << 9,
    X = 1 << 10,

    MouseLeft = 1 << 30,
    MouseRight = 1 << 31,
    MouseLeftOrRight = MouseLeft | MouseRight,
#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member
}
