namespace ValveResourceFormat.Renderer.Editor.ToolRender;

/// <summary>How the editor draws its tool shapes: lines or faces, opaque or blended, over the scene or not.</summary>
public enum ToolRenderMode
{
    /// <summary>Draws nothing.</summary>
    None,

    /// <summary>The mode a context starts in, flat faces.</summary>
    Default,

    /// <summary>Repeats the current mode.</summary>
    Current,

    /// <summary>Opaque lines where the scene does not hide them.</summary>
    Wireframe,

    /// <summary>Opaque lines over the scene.</summary>
    WireframeNoZ,

    /// <summary>Opaque lines pulled towards the camera, so they show on the surfaces they lie on.</summary>
    WireframeDepthBias,

    /// <summary>Lines blended in their alpha where the scene does not hide them.</summary>
    WireframeTranslucent,

    /// <summary>Lines blended in their alpha over the scene.</summary>
    WireframeTranslucentNoZ,

    /// <summary>Lines blended in their alpha, pulled towards the camera.</summary>
    WireframeTranslucentDepthBias,

    /// <summary>Dashed opaque lines where the scene does not hide them.</summary>
    Dotted,

    /// <summary>Dashed opaque lines over the scene.</summary>
    DottedNoZ,

    /// <summary>Opaque faces in their color.</summary>
    Flat,

    /// <summary>Opaque faces in their color over the scene.</summary>
    FlatNoZ,

    /// <summary>Opaque faces in their color, seen from both sides.</summary>
    FlatNoCull,

    /// <summary>Opaque faces in their color, pulled towards the camera.</summary>
    FlatDepthBias,

    /// <summary>Opaque faces lit from fixed directions.</summary>
    Shaded,

    /// <summary>Opaque lit faces over the scene.</summary>
    ShadedNoZ,

    /// <summary>Opaque lit faces seen from both sides.</summary>
    ShadedNoCull,

    /// <summary>Opaque lit faces pulled towards the camera.</summary>
    ShadedDepthBias,

    /// <summary>Faces blended in their alpha.</summary>
    TranslucentFlat,

    /// <summary>Faces blended in their alpha over the scene.</summary>
    TranslucentFlatNoZ,

    /// <summary>Faces blended in their alpha, seen from both sides.</summary>
    TranslucentFlatNoCull,

    /// <summary>Faces blended in their alpha, pulled towards the camera.</summary>
    TranslucentFlatDepthBias,

    /// <summary>Lit faces blended in their alpha.</summary>
    TranslucentShaded,

    /// <summary>Lit faces blended in their alpha over the scene.</summary>
    TranslucentShadedNoZ,

    /// <summary>Lit faces blended in their alpha, seen from both sides.</summary>
    TranslucentShadedNoCull,

    /// <summary>Lit faces blended in their alpha, pulled towards the camera.</summary>
    TranslucentShadedDepthBias,

    /// <summary>Unused.</summary>
    Unknown27,

    /// <summary>The tint drawn over what is selected.</summary>
    SelectionOverlay,

    /// <summary>The tint drawn over what is selected, on both sides of its faces.</summary>
    SelectionOverlayNoCull,

    /// <summary>The tint drawn over what is selected, with the culling of what it covers.</summary>
    SelectionOverlay30,

    /// <summary>Faces that are only drawn to be picked.</summary>
    PickProxy,
}

/// <summary>What a <see cref="ToolRenderMode"/> draws with.</summary>
[Flags]
public enum ToolRenderModeFlags
{
    /// <summary>Faces, opaque, culled and hidden by the scene.</summary>
    None = 0,

    /// <summary>Lines.</summary>
    Wireframe = 0x1,

    /// <summary>Blended in the alpha of the color.</summary>
    Translucent = 0x2,

    /// <summary>Faces lit from fixed directions.</summary>
    Shaded = 0x4,

    /// <summary>Faces seen from both sides.</summary>
    NoCull = 0x8,

    /// <summary>Drawn over the scene.</summary>
    NoZ = 0x10,

    /// <summary>Pulled towards the camera, so it shows on the surfaces it lies on.</summary>
    DepthBias = 0x20,

    /// <summary>Snapped to the pixel grid, in the 2D views.</summary>
    SnapToScreenPixels = 0x40,

    /// <summary>Lines dashed every 16 pixels.</summary>
    Dashed = 0x80,

    /// <summary>Lines as wide as the line width of the context, in pixels.</summary>
    ThickLines = 0x100,
}

/// <summary>Where the shapes of each mode are drawn, in the order the layers are drawn.</summary>
public enum ToolRenderLayer
{
    /// <summary>Opaque, hidden by the scene.</summary>
    Opaque,

    /// <summary>Blended, hidden by the scene.</summary>
    Translucent,

    /// <summary>The tint over what is selected.</summary>
    PostTranslucent,

    /// <summary>Opaque, over the scene.</summary>
    OpaqueNoZ,

    /// <summary>Blended, over the scene.</summary>
    TranslucentNoZ,

    /// <summary>Before the overlays, for shapes that override the layer of their mode.</summary>
    PreOverlay,

    /// <summary>The first of the overlays drawn over everything else, for shapes that override the layer of their mode.</summary>
    Overlay0,

    /// <summary>The second overlay.</summary>
    Overlay1,

    /// <summary>The third overlay.</summary>
    Overlay2,

    /// <summary>The last overlay.</summary>
    Overlay3,
}

/// <summary>The flags and layer of each <see cref="ToolRenderMode"/>.</summary>
public static class ToolRenderModes
{
    private static readonly ToolRenderModeFlags[] Flags =
    [
        ToolRenderModeFlags.None, // None
        ToolRenderModeFlags.None, // Default
        ToolRenderModeFlags.None, // Current
        ToolRenderModeFlags.Wireframe,
        ToolRenderModeFlags.Wireframe | ToolRenderModeFlags.NoZ,
        ToolRenderModeFlags.Wireframe | ToolRenderModeFlags.DepthBias,
        ToolRenderModeFlags.Wireframe | ToolRenderModeFlags.Translucent,
        ToolRenderModeFlags.Wireframe | ToolRenderModeFlags.Translucent | ToolRenderModeFlags.NoZ,
        ToolRenderModeFlags.Wireframe | ToolRenderModeFlags.Translucent | ToolRenderModeFlags.DepthBias,
        ToolRenderModeFlags.Wireframe | ToolRenderModeFlags.Dashed,
        ToolRenderModeFlags.Wireframe | ToolRenderModeFlags.Dashed | ToolRenderModeFlags.NoZ,
        ToolRenderModeFlags.None, // Flat
        ToolRenderModeFlags.NoZ,
        ToolRenderModeFlags.NoCull,
        ToolRenderModeFlags.DepthBias,
        ToolRenderModeFlags.Shaded,
        ToolRenderModeFlags.Shaded | ToolRenderModeFlags.NoZ,
        ToolRenderModeFlags.Shaded | ToolRenderModeFlags.NoCull,
        ToolRenderModeFlags.Shaded | ToolRenderModeFlags.DepthBias,
        ToolRenderModeFlags.Translucent,
        ToolRenderModeFlags.Translucent | ToolRenderModeFlags.NoZ,
        ToolRenderModeFlags.Translucent | ToolRenderModeFlags.NoCull,
        ToolRenderModeFlags.Translucent | ToolRenderModeFlags.DepthBias,
        ToolRenderModeFlags.Translucent | ToolRenderModeFlags.Shaded,
        ToolRenderModeFlags.Translucent | ToolRenderModeFlags.Shaded | ToolRenderModeFlags.NoZ,
        ToolRenderModeFlags.Translucent | ToolRenderModeFlags.Shaded | ToolRenderModeFlags.NoCull,
        ToolRenderModeFlags.Translucent | ToolRenderModeFlags.Shaded | ToolRenderModeFlags.DepthBias,
        ToolRenderModeFlags.None, // Unknown27
        ToolRenderModeFlags.None, // SelectionOverlay
        ToolRenderModeFlags.None, // SelectionOverlayNoCull
        ToolRenderModeFlags.None, // SelectionOverlay30
        ToolRenderModeFlags.NoZ, // PickProxy
    ];

    /// <summary>Gets what a mode draws with.</summary>
    /// <param name="mode">The mode.</param>
    /// <returns>The flags of the mode.</returns>
    public static ToolRenderModeFlags GetFlags(ToolRenderMode mode) => Flags[(int)mode];

    /// <summary>Gets whether a mode is one of the tints drawn over what is selected.</summary>
    /// <param name="mode">The mode.</param>
    /// <returns>Whether it is.</returns>
    public static bool IsSelectionOverlay(ToolRenderMode mode)
        => mode is ToolRenderMode.SelectionOverlay or ToolRenderMode.SelectionOverlayNoCull or ToolRenderMode.SelectionOverlay30;

    /// <summary>Gets the layer shapes drawn with some flags go in.</summary>
    /// <param name="flags">The flags.</param>
    /// <returns>The layer.</returns>
    public static ToolRenderLayer GetLayer(ToolRenderModeFlags flags)
    {
        var translucent = (flags & ToolRenderModeFlags.Translucent) != 0;

        return (flags & ToolRenderModeFlags.NoZ) != 0
            ? translucent ? ToolRenderLayer.TranslucentNoZ : ToolRenderLayer.OpaqueNoZ
            : translucent ? ToolRenderLayer.Translucent : ToolRenderLayer.Opaque;
    }
}

/// <summary>Which sides of the faces of a shape are drawn.</summary>
public enum ToolFaceSides
{
    /// <summary>The side the shape faces out of.</summary>
    Front,

    /// <summary>Both sides.</summary>
    Both,

    /// <summary>The side the shape faces into.</summary>
    Back,
}

/// <summary>The shape of a handle, a marker that keeps its size on the screen.</summary>
public enum ToolHandleType
{
    /// <summary>Not drawn.</summary>
    Generic,

    /// <summary>A filled square.</summary>
    Square,

    /// <summary>A filled circle.</summary>
    Circle,

    /// <summary>A cross.</summary>
    Cross,

    /// <summary>The outline of a circle.</summary>
    Ring,

    /// <summary>The outline of a square.</summary>
    SquareRing,

    /// <summary>A filled circle with an edge.</summary>
    EdgedCircle,

    /// <summary>A filled diamond.</summary>
    Diamond,

    /// <summary>A filled diamond with an edge.</summary>
    EdgedDiamond,
}
