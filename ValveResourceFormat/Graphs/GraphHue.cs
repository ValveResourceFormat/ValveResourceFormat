namespace ValveResourceFormat.Graphs;

/// <summary>
/// Named color slot for graph elements. Frontends bind semantics (pose, flow, bool, ...) to a hue;
/// the host resolves each hue to concrete colors for the theme it is drawing in.
/// </summary>
public enum GraphHue
{
    /// <summary>No semantic colour; drawn in the theme's plain foreground.</summary>
    Neutral,

    /// <summary>Desaturated blue-grey.</summary>
    Slate,

    /// <summary>Dark red.</summary>
    Maroon,

    /// <summary>Red.</summary>
    Red,

    /// <summary>Orange.</summary>
    Orange,

    /// <summary>Yellow-orange.</summary>
    Amber,

    /// <summary>Dark yellow-green.</summary>
    Olive,

    /// <summary>Green.</summary>
    Green,

    /// <summary>Blue-green.</summary>
    Emerald,

    /// <summary>Green-cyan.</summary>
    Teal,

    /// <summary>Cyan.</summary>
    Cyan,

    /// <summary>Blue.</summary>
    Blue,

    /// <summary>Blue-violet.</summary>
    Indigo,

    /// <summary>Purple.</summary>
    Purple,

    /// <summary>Purple-pink.</summary>
    Magenta,

    /// <summary>Pink.</summary>
    Pink,
}
