using ValveResourceFormat.IO.Fgd;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.Renderer.Editor.Helpers;

/// <summary>
/// A line from an entity to a point one of its keys holds, in world space or in the space of the entity. It
/// is drawn while the entity is selected, and faintly while it is not where the class asks for that.
/// </summary>
public sealed class VecLineHelper : EntityHelper
{
    private const string DefaultPointKey = "origin";

    /// <summary>Gets the key of the entity that holds the point.</summary>
    public string PointKey { get; }

    /// <summary>Gets the color of the line.</summary>
    public Color32 Color { get; }

    /// <summary>Gets whether the point is in the space of the entity, rather than in world space.</summary>
    public bool IsLocal { get; }

    /// <summary>Gets whether the line is drawn while the entity is not selected too.</summary>
    public bool DrawUnselected { get; }

    /// <summary>Gets whether no line is drawn while a point in the space of the entity is its origin.</summary>
    public bool HideIfZero { get; }

    /// <summary>Initializes the line a class declares.</summary>
    /// <param name="owner">The entity.</param>
    /// <param name="declaration">The helper as the class of the entity declares it.</param>
    public VecLineHelper(IHelperOwner owner, FgdHelper declaration) : base(owner, declaration)
    {
        PointKey = GetArgument(0)?.ToLowerInvariant() ?? DefaultPointKey;
        Color = declaration.Arguments.Count >= 4 ? GetColorArguments(1) : Color32.White;
        IsLocal = declaration.Name.Equals("vecline_local", StringComparison.OrdinalIgnoreCase);
        DrawUnselected = HasArgument("draw_unselected");
        HideIfZero = HasArgument("hideifzero");
    }

    /// <inheritdoc/>
    public override void AddToScene()
    {
        if (DrawUnselected && GetPoint() is { } point)
        {
            AddLines(Owner.RigidTransform, lines => lines.AddLine(point, Vector3.Zero, Color with { A = 128 }), translucent: true);
        }
    }

    internal override void AddSelectionShapes(in SelectionShapes shapes)
    {
        if (GetPoint() is { } point)
        {
            shapes.Oriented(HelperPasses.Both).AddLine(point, Vector3.Zero, Color);
        }
    }

    // The point in the space of the entity, which is its origin until the key is set
    private Vector3? GetPoint()
    {
        if (!Owner.KeyValues.ContainsKey(PointKey))
        {
            return HideIfZero && IsLocal ? null : Vector3.Zero;
        }

        var point = GetVector3Key(PointKey);

        if (!IsLocal)
        {
            Matrix4x4.Invert(Owner.RigidTransform, out var worldToEntity);
            point = Vector3.Transform(point, worldToEntity);
        }
        else if (HideIfZero && point == Vector3.Zero)
        {
            return null;
        }

        return point;
    }
}
