using ValveResourceFormat.IO.Fgd;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.Renderer.Editor.Helpers;

/// <summary>
/// An omni light, which shows its luminaire and the cones it lights while its entity is selected: the inner
/// cone at full strength and the outer one it fades out to, each capped by the part of a sphere at its range.
/// </summary>
public sealed class OmniLightHelper : LightHelperBase
{
    private const int SphereShape = 0;
    private const int CappedTubeShape = 2;
    private const int PointShape = 3;
    private const int TubeSegments = 12;
    private const int ConeLines = 16;

    // Rows of the caps of the cones, one per this many radians
    private const float CapRowAngle = MathF.PI / 12f;

    private static readonly Color32 ShapeColor = new(192, 255, 255, 32);
    private static readonly Color32 InnerConeColor = new(255, 128, 0);
    private static readonly Color32 InnerConeNearColor = new(255, 0, 0);
    private static readonly Color32 OuterConeColor = new(0, 255, 255);
    private static readonly Color32 OuterConeNearColor = new(0, 0, 255);

    // The caps are drawn around Z, which this turns onto the forward axis
    private static readonly Matrix4x4 CapToLight = new(
        0f, 0f, 1f, 0f,
        0f, 1f, 0f, 0f,
        -1f, 0f, 0f, 0f,
        0f, 0f, 0f, 1f);

    /// <summary>Gets the shape of the luminaire: a sphere or a point, drawn as a sphere, or a tube along Y, open or capped.</summary>
    public int Shape { get; }

    /// <summary>Gets the angle of the inner cone from the forward axis, in degrees.</summary>
    public float InnerAngle { get; }

    /// <summary>Gets the angle of the outer cone from the forward axis, in degrees.</summary>
    public float OuterAngle { get; }

    /// <summary>Initializes the light of an entity.</summary>
    /// <param name="owner">The entity.</param>
    /// <param name="declaration">The helper as the class of the entity declares it.</param>
    public OmniLightHelper(IHelperOwner owner, FgdHelper declaration) : base(owner, declaration)
    {
        Shape = GetIntegerKey("shape");
        InnerAngle = GetFloatKey("inner_angle");
        OuterAngle = GetFloatKey("outer_angle");
    }

    internal override void AddSelectionShapes(in SelectionShapes shapes)
    {
        var luminaire = shapes.Oriented(HelperPasses.Both);

        if (Shape is SphereShape or PointShape)
        {
            luminaire.AddSphereGrid(Vector3.Zero, SizeParams.X, 12, 12, 0f, MathF.PI, ShapeColor);
        }
        else
        {
            AddTube(luminaire);
        }

        var outer = float.DegreesToRadians(Math.Clamp(OuterAngle, 1f, 180f));
        var inner = MathF.Min(float.DegreesToRadians(MathF.Max(InnerAngle, 0f)), outer);

        var cones = shapes.Oriented(HelperPasses.Visible);
        var caps = cones.Transformed(CapToLight);

        caps.AddSphereGrid(Vector3.Zero, Range, 16, GetCapRows(inner), MathF.PI - inner, MathF.PI, InnerConeColor);

        if (outer > inner)
        {
            caps.AddSphereGrid(Vector3.Zero, Range, 16, GetCapRows(outer - inner), MathF.PI - outer, MathF.PI - inner, OuterConeColor);
        }

        if (inner > 0f && inner < MathF.PI)
        {
            AddConeLines(cones, inner, InnerConeColor, InnerConeNearColor);
        }

        if (outer > inner && outer < MathF.PI)
        {
            AddConeLines(cones, outer, OuterConeColor, OuterConeNearColor);
        }
    }

    private static int GetCapRows(float angle) => Math.Max((int)MathF.Ceiling(angle / CapRowAngle), 2);

    // The tube runs along Y, with spokes across its ends when it is capped
    private void AddTube(EntityHelperLines lines)
    {
        var radius = SizeParams.X;
        var halfLength = SizeParams.Y;

        // Twelve points, the last on the first
        const float Step = MathF.Tau / (TubeSegments - 1);

        lines.AddCircle(Vector3.UnitY * halfLength, Vector3.UnitX * radius, Vector3.UnitZ * radius, TubeSegments - 1, ShapeColor);
        lines.AddCircle(-Vector3.UnitY * halfLength, Vector3.UnitX * radius, Vector3.UnitZ * radius, TubeSegments - 1, ShapeColor);

        for (var i = 0; i < TubeSegments; i++)
        {
            var (sin, cos) = MathF.SinCos(i * Step);
            var rim = new Vector3(sin * radius, 0f, cos * radius);

            lines.AddLine(rim - Vector3.UnitY * halfLength, rim + Vector3.UnitY * halfLength, ShapeColor);
        }

        if (Shape != CappedTubeShape)
        {
            return;
        }

        for (var i = 0; i < TubeSegments / 2; i++)
        {
            var (sin, cos) = MathF.SinCos(i * Step);
            var rim = new Vector3(sin * radius, 0f, cos * radius);

            lines.AddLine(rim - Vector3.UnitY * halfLength, -rim - Vector3.UnitY * halfLength, ShapeColor);
            lines.AddLine(rim + Vector3.UnitY * halfLength, -rim + Vector3.UnitY * halfLength, ShapeColor);
        }
    }

    // Lines along a cone out to the range, split where the light starts fading out
    private void AddConeLines(EntityHelperLines lines, float angle, Color32 color, Color32 nearColor)
    {
        var (sinAngle, cosAngle) = MathF.SinCos(angle);

        for (var i = 0; i < ConeLines; i++)
        {
            var (sin, cos) = MathF.SinCos(i * MathF.Tau / ConeLines);
            var end = new Vector3(cosAngle, sinAngle * sin, sinAngle * cos) * Range;

            lines.AddLine(end * Skirt, end, color);
            lines.AddLine(Vector3.Zero, end * Skirt, nearColor);
        }
    }
}
