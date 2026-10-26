using ValveResourceFormat.IO.ContentFormats.ValveMap;

namespace ValveResourceFormat.IO;

/// <summary>
/// Why a map node is hidden, as <see cref="CVisibilityMgr.HiddenFlags"/> stores it for each node.
/// </summary>
[Flags]
public enum MapNodeHiddenFlags
{
    /// <summary>The node is not hidden.</summary>
    None = 0,

    /// <summary>The node was hidden by hand.</summary>
    Hidden = 1,

    /// <summary>The node is hidden through an automatic visibility group.</summary>
    AutoHidden = 2,

    /// <summary>Does not hide the node. Maps are saved with it, and it is cleared when a map is loaded.</summary>
    Transient = 4,

    /// <summary>The node is hidden by a tool that is working on it, such as what is being clipped.</summary>
    HiddenByTool = 8,
}

/// <summary>
/// What a static overlay projects onto, as <see cref="CMapStaticOverlay.ProjectionMode"/> stores it.
/// </summary>
public enum MapOverlayProjectionMode
{
    /// <summary>World geometry and models.</summary>
    All,

    /// <summary>Only world geometry.</summary>
    World,

    /// <summary>Only models.</summary>
    Models,

    /// <summary>Only its <see cref="CMapStaticOverlay.ProjectionTargets"/>.</summary>
    Targets,
}

/// <summary>
/// How the spline of a path runs between its nodes, as <see cref="CMapPath.InterpolationType"/> stores it.
/// </summary>
public enum MapPathInterpolationType
{
    /// <summary>Straight from node to node.</summary>
    Linear,

    /// <summary>A smooth curve through the nodes.</summary>
    Spline,

    /// <summary>Each node says how its own two handles are derived.</summary>
    Bezier,
}

/// <summary>
/// How a handle of a path node is derived, as <see cref="CMapPathNode.InTangentType"/> and
/// <see cref="CMapPathNode.OutTangentType"/> store it.
/// </summary>
public enum MapPathTangentType
{
    /// <summary>Points at the neighbouring node.</summary>
    Linear,

    /// <summary>Runs along the line between the two neighbouring nodes.</summary>
    Spline,

    /// <summary>Keeps the direction the node stores, at a third of the length of its segment.</summary>
    Direction,

    /// <summary>Kept exactly as the node stores it.</summary>
    Free,
}
