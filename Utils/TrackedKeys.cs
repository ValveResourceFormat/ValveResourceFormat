namespace GUI.Utils;

[Flags]
enum TrackedKeys
{
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

    MouseLeft = 1 << 30,
    MouseRight = 1 << 31,
    MouseLeftOrRight = MouseLeft | MouseRight,
}
