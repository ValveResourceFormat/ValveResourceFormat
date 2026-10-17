using ValveResourceFormat.Renderer.Editor.Entities;
using ValveResourceFormat.Renderer.Editor.ToolRender;
using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.Renderer.Editor.Helpers;

/// <summary>
/// How the keys of a box helper give the box.
/// </summary>
internal enum BoxHelperSize
{
    /// <summary>Two corners.</summary>
    MinsMaxs,

    /// <summary>A size centered on the origin of the entity.</summary>
    Centered,
}

/// <summary>
/// The box of a volume an entity has, given by its keys. While the entity is selected its faces are tinted,
/// and where the influence of the volume fades out past its sides the faces fade with it. While the entity is
/// not selected only the edges are drawn.
/// </summary>
internal sealed class BoxHelper : EntityHelper
{
    /// <inheritdoc/>
    public override bool IsToolShape => true;

    /// <inheritdoc/>
    public override bool IsVisual => false;

    /// <inheritdoc/>
    public override bool HiddenInGameObjectsOnly => true;

    // There is no selection tool, so what is only drawn while the entity is highlighted by it is drawn while
    // the entity is selected
    /// <inheritdoc/>
    public override bool RequiresSelection => OnlyVisibleHighlightEnabled;

    // The faces fade in steps of at most this many units
    private const float FadeStep = 24f;

    /// <summary>Gets how the keys give the box.</summary>
    private BoxHelperSize Size { get; }

    /// <summary>Gets whether the box turns with the entity, rather than staying aligned to the world axes.</summary>
    private bool IsOriented { get; }

    /// <summary>Gets the key of the entity that holds the smallest corner.</summary>
    private string? MinsKey { get; }

    /// <summary>Gets the key of the entity that holds the largest corner.</summary>
    private string? MaxsKey { get; }

    /// <summary>Gets the key of the entity that holds the size of the box, in place of two corners.</summary>
    private string? SizeKey { get; }

    /// <summary>Gets the key of the entity that holds how far past each side its influence fades.</summary>
    private string? EdgeFadesKey { get; }

    /// <summary>Gets the key of the entity that holds one fade distance for every side.</summary>
    private string? EdgeFadeKey { get; }

    /// <summary>Gets whether the edges are drawn while the entity is not selected.</summary>
    private bool DrawUnselected { get; }

    /// <summary>Gets whether the box is only drawn while its entity is selected with the selection tool.</summary>
    private bool OnlyVisibleHighlightEnabled { get; }

    /// <summary>
    /// Gets whether the box is a light probe volume, whose faces are not tinted while the sample grids of the
    /// volumes are shown, so the samples can be seen.
    /// </summary>
    private bool IsLightProbeVolume { get; }

    /// <summary>
    /// Gets whether the influence of the volume fades out past its sides, rather than inside them, which the
    /// game decides for all of its volumes.
    /// </summary>
    private bool FadesPastSides { get; }

    private BoxHelper(IHelperOwner owner, HelperDeclaration declaration, BoxHelperSize size, bool isOriented) : base(owner, declaration)
    {
        Size = size;
        IsOriented = isOriented;

        if (size == BoxHelperSize.MinsMaxs)
        {
            MinsKey = (GetMetadata("box_min") ?? GetArgument(0))?.ToLowerInvariant();
            MaxsKey = (GetMetadata("box_max") ?? GetArgument(1))?.ToLowerInvariant();
        }
        else
        {
            SizeKey = (GetMetadata("box_size") ?? GetArgument(0))?.ToLowerInvariant();
        }

        EdgeFadesKey = GetMetadata("edge_fades")?.ToLowerInvariant();
        EdgeFadeKey = GetMetadata("single_edge_fade")?.ToLowerInvariant();
        DrawUnselected = GetMetadataFlag("draw_unselected_box", true);
        OnlyVisibleHighlightEnabled = GetMetadataFlag("only_visible_highlight_enabled", false);
        IsLightProbeVolume = GetMetadataFlag("is_light_probe_volume", false);
        FadesPastSides = owner.Scene.RendererContext.FileLoader.GetGameInfoBool("WorldRenderer/LPVEdgeBlending", true);
    }

    /// <summary>Creates the box a class declares.</summary>
    /// <returns>The box, or <see langword="null"/> when the declaration does not name the keys it is given by.</returns>
    public static BoxHelper? Create(IHelperOwner owner, HelperDeclaration declaration)
    {
        ArgumentNullException.ThrowIfNull(declaration);

        var name = declaration.Name.ToLowerInvariant();
        var size = name.StartsWith("centered_", StringComparison.Ordinal) ? BoxHelperSize.Centered : BoxHelperSize.MinsMaxs;
        var box = new BoxHelper(owner, declaration, size, name.EndsWith("_oriented", StringComparison.Ordinal));

        return box.SizeKey != null || (box.MinsKey != null && box.MaxsKey != null) ? box : null;
    }

    /// <inheritdoc/>
    public override void AddToScene()
    {
        if (DrawUnselected)
        {
            AddShapes(layerName: HammerEntityVisuals.HelpersLayerName);
        }
    }

    internal override void Render(ToolRenderContext context, in HelperRenderState state)
    {
        if (!state.IsSelected && !DrawUnselected)
        {
            return;
        }

        var bounds = GetBounds();
        var color = GetDrawColor(state) with { A = 255 };

        context.PushTransform(GetPlacement());

        if (!state.IsSelected)
        {
            context.PushRenderMode(ToolRenderMode.Wireframe);
            context.DrawColor = color;
            context.DrawWireBox(bounds.Min, bounds.Max);
            context.PopRenderMode();
            context.PopTransform();
            return;
        }

        // Some volumes only tint their faces while the selection overlay of the editor is on, which it is
        // until it is turned off. A light probe volume leaves them out while the samples of the volumes show.
        if (!IsLightProbeVolume || Owner.Scene.LightingDebug.LightProbeGrid == LightProbeDebugGridMode.Off)
        {
            DrawFaces(context, bounds, color);
        }

        context.PushRenderMode(ToolRenderMode.WireframeDepthBias);
        context.LineWidth = 2f;
        context.DrawColor = color;
        context.DrawWireBox(bounds.Min, bounds.Max);
        context.LineWidth = 0f;
        context.PopRenderMode();

        context.PushRenderMode(ToolRenderMode.WireframeTranslucentNoZ);
        context.DrawColor = color with { A = 64 };
        context.DrawWireBox(bounds.Min, bounds.Max);
        context.PopRenderMode();

        context.PopTransform();
    }

    // Tinted faces, which fade with the influence of the volume where it fades out past its sides
    private void DrawFaces(ToolRenderContext context, in AABB bounds, Color32 color)
    {
        var edgeFade = GetEdgeFade();

        if (edgeFade != Vector3.Zero)
        {
            DrawFadingFaces(context, bounds, edgeFade, FadesPastSides, color);
            return;
        }

        context.PushRenderMode(ToolRenderMode.TranslucentFlatNoZ);
        context.DrawColor = color with { A = 8 };
        context.DrawSolidBox(bounds.Min, bounds.Max, doubleSided: true);
        context.PopRenderMode();

        context.PushRenderMode(ToolRenderMode.TranslucentFlatDepthBias);
        context.DrawColor = color with { A = 32 };
        context.DrawSolidBox(bounds.Min, bounds.Max, doubleSided: true);
        context.PopRenderMode();
    }

    private AABB GetBounds() => Size == BoxHelperSize.MinsMaxs
        ? new AABB(GetVector3Key(MinsKey!), GetVector3Key(MaxsKey!))
        : AABB.FromCenteredSize(GetVector3Key(SizeKey!));

    private Matrix4x4 GetPlacement()
        => IsOriented ? Owner.RigidTransform : Matrix4x4.CreateTranslation(Owner.RigidTransform.Translation);

    // How far past each side the influence of the volume fades out
    private Vector3 GetEdgeFade()
    {
        var edgeFade = EdgeFadesKey != null ? GetVector3Key(EdgeFadesKey) : Vector3.Zero;

        if (edgeFade == Vector3.Zero && EdgeFadeKey != null)
        {
            edgeFade = new Vector3(GetFloatKey(EdgeFadeKey));
        }

        return edgeFade;
    }

    /// <summary>
    /// Faces that show where the influence of the volume fades: on each side, the part where it is whole is
    /// tinted, and the band around it fades out in steps towards the edge, with the outline of the whole part
    /// drawn on it. The fade runs out from the sides of the box when it is past them, and in from them otherwise.
    /// </summary>
    private static void DrawFadingFaces(ToolRenderContext context, in AABB bounds, Vector3 edgeFade, bool fadesPastSides, Color32 color)
    {
        var center = bounds.Center;
        var half = bounds.Size / 2f;
        var (inner, outer) = fadesPastSides ? (half, half + edgeFade) : (half - Vector3.Min(half, edgeFade), half);
        var steps = (int)MathF.Ceiling(MathF.Max(MathF.Abs(edgeFade.X), MathF.Max(MathF.Abs(edgeFade.Y), MathF.Abs(edgeFade.Z))) / FadeStep);

        var outlines = new List<(Vector3 Start, Vector3 End)>();

        context.PushRenderMode(ToolRenderMode.TranslucentFlatDepthBias);

        for (var axis = 0; axis < 3; axis++)
        {
            var (u, v) = ((axis + 1) % 3, (axis + 2) % 3);

            foreach (var side in (ReadOnlySpan<float>)[-1f, 1f])
            {
                var plane = center;
                plane[axis] += side * outer[axis];

                // A point on this side, at extents along its two in-plane axes
                Vector3 GetPoint(float alongU, float alongV)
                {
                    var point = plane;
                    point[u] += alongU;
                    point[v] += alongV;
                    return point;
                }

                AddRing(context, GetPoint, inner[u], inner[v], 0f, 0f, color with { A = 32 }, color with { A = 32 });

                for (var step = 0; step < steps; step++)
                {
                    var from = step / (float)steps;
                    var to = (step + 1) / (float)steps;

                    AddRing(context, GetPoint,
                        float.Lerp(inner[u], outer[u], to), float.Lerp(inner[v], outer[v], to),
                        float.Lerp(inner[u], outer[u], from), float.Lerp(inner[v], outer[v], from),
                        color with { A = (byte)(int)(32f - 28f * to) }, color with { A = (byte)(int)(32f - 28f * from) });
                }

                ReadOnlySpan<Vector3> corners = [GetPoint(-inner[u], -inner[v]), GetPoint(inner[u], -inner[v]), GetPoint(inner[u], inner[v]), GetPoint(-inner[u], inner[v])];

                for (var i = 0; i < corners.Length; i++)
                {
                    outlines.Add((corners[i], corners[(i + 1) % corners.Length]));
                }
            }
        }

        context.PopRenderMode();

        // The outline of each side, on the faded faces
        context.PushRenderMode(ToolRenderMode.WireframeTranslucentDepthBias);

        foreach (var (start, end) in outlines)
        {
            context.DrawLine(start, end, color with { A = 48 });
        }

        context.PopRenderMode();
    }

    /// <summary>
    /// The ring between a rectangle and one inside it, or the outer rectangle itself where the inner one has
    /// no size: a strip along each side, fading from the inner color to the outer one, and a square at each
    /// corner that fades out from the corner of the inner rectangle alone.
    /// </summary>
    private static void AddRing(ToolRenderContext context, Func<float, float, Vector3> getPoint, float outerU, float outerV, float innerU, float innerV, Color32 outerColor, Color32 innerColor)
    {
        foreach (var side in (ReadOnlySpan<float>)[-1f, 1f])
        {
            AddQuad(context,
                getPoint(side * innerU, -innerV), getPoint(side * outerU, -innerV), getPoint(side * outerU, innerV), getPoint(side * innerU, innerV),
                innerColor, outerColor, outerColor, innerColor);

            AddQuad(context,
                getPoint(-innerU, side * innerV), getPoint(innerU, side * innerV), getPoint(innerU, side * outerV), getPoint(-innerU, side * outerV),
                innerColor, innerColor, outerColor, outerColor);

            foreach (var otherSide in (ReadOnlySpan<float>)[-1f, 1f])
            {
                AddQuad(context,
                    getPoint(side * innerU, otherSide * innerV), getPoint(side * outerU, otherSide * innerV),
                    getPoint(side * outerU, otherSide * outerV), getPoint(side * innerU, otherSide * outerV),
                    innerColor, outerColor, outerColor, outerColor);
            }
        }
    }

    // Seen from both sides
    private static void AddQuad(ToolRenderContext context, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color32 colorA, Color32 colorB, Color32 colorC, Color32 colorD)
    {
        context.DrawTriangle(a, b, c, colorA, colorB, colorC);
        context.DrawTriangle(a, c, d, colorA, colorC, colorD);
        context.DrawTriangle(c, b, a, colorC, colorB, colorA);
        context.DrawTriangle(d, c, a, colorD, colorC, colorA);
    }
}
