namespace GUI.Types.Graphs.Core;

/// <summary>
/// Named color slot for graph elements. Frontends bind semantics (pose, flow, bool, ...) to a hue;
/// <see cref="GraphPalette"/> resolves each hue to concrete colors for the active app theme.
/// </summary>
enum GraphHue
{
    Neutral,
    Slate,
    Maroon,
    Red,
    Orange,
    Amber,
    Olive,
    Green,
    Emerald,
    Teal,
    Cyan,
    Blue,
    Indigo,
    Purple,
    Magenta,
    Pink,
}
