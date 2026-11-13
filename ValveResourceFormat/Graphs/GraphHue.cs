namespace ValveResourceFormat.Graphs;

/// <summary>
/// Named color slot for graph elements. Frontends bind semantics (pose, flow, bool, ...) to a hue;
/// the host resolves each hue to concrete colors for the theme it is drawing in.
/// </summary>
internal enum GraphHue
{
    /// <summary>No semantic colour; drawn in the theme's plain foreground.</summary>
    Neutral,

    /// <summary>Desaturated blue-grey.</summary>
    Slate,

    /// <summary>Dark red.</summary>
    Maroon,

    Red,

    Orange,

    /// <summary>Yellow-orange.</summary>
    Amber,

    /// <summary>Dark yellow-green.</summary>
    Olive,

    Green,

    /// <summary>Blue-green.</summary>
    Emerald,

    /// <summary>Green-cyan.</summary>
    Teal,

    Cyan,

    Blue,

    /// <summary>Blue-violet.</summary>
    Indigo,

    Purple,

    /// <summary>Purple-pink.</summary>
    Magenta,

    Pink,
}
