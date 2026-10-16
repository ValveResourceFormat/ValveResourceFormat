using ValveResourceFormat.IO.Fgd;
using ValveResourceFormat.Renderer.Editor.Entities;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.Renderer.Editor.Helpers;

/// <summary>
/// How the keys of a box helper give the box.
/// </summary>
public enum BoxHelperSize
{
    /// <summary>Two corners.</summary>
    MinsMaxs,

    /// <summary>A size centered on the origin of the entity.</summary>
    Centered,

    /// <summary>A size from the origin of the entity.</summary>
    FromOrigin,
}

/// <summary>
/// The box of a volume an entity has, given by its keys. While the entity is selected its faces are tinted,
/// and where the influence of the volume fades out past its sides the faces fade with it. While the entity is
/// not selected only the edges are drawn.
/// </summary>
public sealed class BoxHelper : EntityHelper
{
    // The faces fade in steps of at most this many units
    private const float FadeStep = 24f;

    /// <summary>Gets how the keys give the box.</summary>
    public BoxHelperSize Size { get; }

    /// <summary>Gets whether the box turns with the entity, rather than staying aligned to the world axes.</summary>
    public bool IsOriented { get; }

    /// <summary>Gets the key of the entity that holds the smallest corner.</summary>
    public string? MinsKey { get; }

    /// <summary>Gets the key of the entity that holds the largest corner.</summary>
    public string? MaxsKey { get; }

    /// <summary>Gets the key of the entity that holds the size of the box, in place of two corners.</summary>
    public string? SizeKey { get; }

    /// <summary>Gets the key of the entity that holds how far past each side its influence fades.</summary>
    public string? EdgeFadesKey { get; }

    /// <summary>Gets the key of the entity that holds one fade distance for every side.</summary>
    public string? EdgeFadeKey { get; }

    /// <summary>Gets whether the edges are drawn while the entity is not selected.</summary>
    public bool DrawUnselected { get; }

    private BoxHelper(IHelperOwner owner, FgdHelper declaration, BoxHelperSize size, bool isOriented) : base(owner, declaration)
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
    }

    /// <summary>Creates the box a class declares.</summary>
    /// <param name="owner">The entity.</param>
    /// <param name="declaration">The helper as the class of the entity declares it.</param>
    /// <returns>The box, or <see langword="null"/> when the declaration does not name the keys it is given by.</returns>
    public static BoxHelper? Create(IHelperOwner owner, FgdHelper declaration)
    {
        ArgumentNullException.ThrowIfNull(declaration);

        var name = declaration.Name.ToLowerInvariant();
        var size = name switch
        {
            "box_world_aligned" or "box_oriented" => BoxHelperSize.MinsMaxs,
            "centered_box_world_aligned" or "centered_box_oriented" => BoxHelperSize.Centered,
            _ => BoxHelperSize.FromOrigin,
        };

        var box = new BoxHelper(owner, declaration, size, name.EndsWith("_oriented", StringComparison.Ordinal));

        return box.SizeKey != null || (box.MinsKey != null && box.MaxsKey != null) ? box : null;
    }

    /// <inheritdoc/>
    public override void AddToScene()
    {
        if (!DrawUnselected)
        {
            return;
        }

        var bounds = GetBounds();
        var color = ClassColor;

        AddLines(GetPlacement(), lines => lines.AddBox(bounds, color), HammerEntityVisuals.HelpersLayerName);
    }

    internal override void AddSelectionShapes(in SelectionShapes shapes)
    {
        var bounds = GetBounds();
        var lines = shapes.World(HelperPasses.Visible).Transformed(GetPlacement());
        var color = EntityHelperLines.SelectionColor;

        // Some volumes only tint their faces while the selection overlay of the editor is on, which it is
        // until it is turned off
        var edgeFade = GetEdgeFade();

        if (edgeFade == Vector3.Zero)
        {
            lines.AddBoxFaces(bounds, color);
        }
        else
        {
            AddFadingFaces(lines.With(HelperPasses.Translucent), bounds, edgeFade, color);
        }

        lines.AddBox(bounds, color);
        lines.With(HelperPasses.Overlay).AddBox(bounds, color with { A = 64 });
    }

    private AABB GetBounds() => Size switch
    {
        BoxHelperSize.MinsMaxs => new AABB(GetVector3Key(MinsKey!), GetVector3Key(MaxsKey!)),
        BoxHelperSize.Centered => AABB.FromCenteredSize(GetVector3Key(SizeKey!)),
        _ => new AABB(Vector3.Zero, GetVector3Key(SizeKey!)),
    };

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
    /// Faces that fade out past the sides of the box: on each side of the box grown by the fade, the part over
    /// the side itself is tinted, and the band around it fades out in steps towards the edge, with the outline
    /// of the side drawn on it.
    /// </summary>
    private static void AddFadingFaces(EntityHelperLines lines, in AABB bounds, Vector3 edgeFade, Color32 color)
    {
        var center = bounds.Center;
        var inner = bounds.Size / 2f;
        var outer = inner + edgeFade;
        var steps = (int)MathF.Ceiling(MathF.Max(MathF.Abs(edgeFade.X), MathF.Max(MathF.Abs(edgeFade.Y), MathF.Abs(edgeFade.Z))) / FadeStep);

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

                AddRing(lines, GetPoint, inner[u], inner[v], 0f, 0f, color with { A = 32 }, color with { A = 32 });

                for (var step = 0; step < steps; step++)
                {
                    var from = step / (float)steps;
                    var to = (step + 1) / (float)steps;

                    AddRing(lines, GetPoint,
                        float.Lerp(inner[u], outer[u], to), float.Lerp(inner[v], outer[v], to),
                        float.Lerp(inner[u], outer[u], from), float.Lerp(inner[v], outer[v], from),
                        color with { A = (byte)(int)(32f - 28f * to) }, color with { A = (byte)(int)(32f - 28f * from) });
                }

                ReadOnlySpan<Vector3> corners = [GetPoint(-inner[u], -inner[v]), GetPoint(inner[u], -inner[v]), GetPoint(inner[u], inner[v]), GetPoint(-inner[u], inner[v])];

                for (var i = 0; i < corners.Length; i++)
                {
                    lines.AddLine(corners[i], corners[(i + 1) % corners.Length], color with { A = 48 });
                }
            }
        }
    }

    /// <summary>
    /// The ring between a rectangle and one inside it, or the outer rectangle itself where the inner one has
    /// no size: a strip along each side, fading from the inner color to the outer one, and a square at each
    /// corner that fades out from the corner of the inner rectangle alone.
    /// </summary>
    private static void AddRing(EntityHelperLines lines, Func<float, float, Vector3> getPoint, float outerU, float outerV, float innerU, float innerV, Color32 outerColor, Color32 innerColor)
    {
        foreach (var side in (ReadOnlySpan<float>)[-1f, 1f])
        {
            AddQuad(lines,
                getPoint(side * innerU, -innerV), getPoint(side * outerU, -innerV), getPoint(side * outerU, innerV), getPoint(side * innerU, innerV),
                innerColor, outerColor, outerColor, innerColor);

            AddQuad(lines,
                getPoint(-innerU, side * innerV), getPoint(innerU, side * innerV), getPoint(innerU, side * outerV), getPoint(-innerU, side * outerV),
                innerColor, innerColor, outerColor, outerColor);

            foreach (var otherSide in (ReadOnlySpan<float>)[-1f, 1f])
            {
                AddQuad(lines,
                    getPoint(side * innerU, otherSide * innerV), getPoint(side * outerU, otherSide * innerV),
                    getPoint(side * outerU, otherSide * outerV), getPoint(side * innerU, otherSide * outerV),
                    innerColor, outerColor, outerColor, outerColor);
            }
        }
    }

    private static void AddQuad(EntityHelperLines lines, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color32 colorA, Color32 colorB, Color32 colorC, Color32 colorD)
    {
        lines.AddFace(a, b, c, colorA, colorB, colorC);
        lines.AddFace(a, c, d, colorA, colorC, colorD);
    }
}
