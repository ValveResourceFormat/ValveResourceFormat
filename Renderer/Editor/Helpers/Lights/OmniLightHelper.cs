using ValveResourceFormat.IO.Fgd;
using ValveResourceFormat.Renderer.Editor.ToolRender;
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
    private const float HandleSize = 6f;

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

    internal override void Render(ToolRenderContext context, in HelperRenderState state)
    {
        if (!state.IsSelected)
        {
            return;
        }

        context.PushTransform(Owner.RigidTransform);

        // The luminaire is solid where seen, and faint over the scene
        foreach (var mode in (ReadOnlySpan<ToolRenderMode>)[ToolRenderMode.Wireframe, ToolRenderMode.WireframeTranslucentNoZ])
        {
            context.PushRenderMode(mode);
            context.DrawColor = ShapeColor;

            if (Shape is SphereShape or PointShape)
            {
                context.DrawWireSphere(Vector3.Zero, SizeParams.X, 12, 12);
            }
            else
            {
                DrawTube(context);
            }

            context.PopRenderMode();
        }

        var outer = float.DegreesToRadians(Math.Clamp(OuterAngle, 1f, 180f));
        var inner = MathF.Min(float.DegreesToRadians(MathF.Max(InnerAngle, 0f)), outer);

        context.PushRenderMode(ToolRenderMode.WireframeTranslucent);

        context.PushTransform(CapToLight);
        context.DrawWireSphere(Vector3.Zero, Range, 16, GetCapRows(inner), MathF.PI - inner, MathF.PI, InnerConeColor);

        if (outer > inner)
        {
            context.DrawWireSphere(Vector3.Zero, Range, 16, GetCapRows(outer - inner), MathF.PI - outer, MathF.PI - inner, OuterConeColor);
        }

        context.PopTransform();

        if (inner > 0f && inner < MathF.PI)
        {
            DrawConeLines(context, inner, InnerConeColor, InnerConeNearColor);
        }

        if (outer > inner && outer < MathF.PI)
        {
            DrawConeLines(context, outer, OuterConeColor, OuterConeNearColor);
        }

        context.PopRenderMode();

        DrawHandles(context, inner, outer);

        context.PopTransform();
    }

    // Handles on the sides of the luminaire, the range and where the light starts fading out, and for each
    // cone one above the light that moves along X with the cosine of its angle
    private void DrawHandles(ToolRenderContext context, float inner, float outer)
    {
        var side = Shape is SphereShape or PointShape ? SizeParams.X : SizeParams.Y;

        ReadOnlySpan<Vector3> positions =
        [
            new(0f, side, 0f),
            new(0f, -side, 0f),
            new(0f, 0f, SizeParams.X),
            new(0f, 0f, -SizeParams.X),
            new(-SizeParams.X, 0f, 0f),
            new(SizeParams.X, 0f, 0f),
            new(Range, 0f, 0f),
            new((1f - Skirt) * Range, 0f, 16f),
            new(MathF.Cos(outer) * 100f - 15f, 0f, 100f),
            new(MathF.Cos(inner) * 95f + 15f, 0f, 95f),
        ];

        foreach (var position in positions)
        {
            context.DrawHandle(HandleSize, ToolHandleType.Square, Color32.White, position, noZ: true, border: true, translucent: false);
        }
    }

    private static int GetCapRows(float angle) => Math.Max((int)MathF.Ceiling(angle / CapRowAngle), 2);

    // The tube runs along Y, with spokes across its ends when it is capped
    private void DrawTube(ToolRenderContext context)
    {
        var radius = SizeParams.X;
        var halfLength = SizeParams.Y;

        // Twelve points, the last on the first
        const float Step = MathF.Tau / (TubeSegments - 1);

        context.DrawCircle(Vector3.UnitY * halfLength, -Vector3.UnitZ, Vector3.UnitX, radius, TubeSegments - 1);
        context.DrawCircle(-Vector3.UnitY * halfLength, -Vector3.UnitZ, Vector3.UnitX, radius, TubeSegments - 1);

        for (var i = 0; i < TubeSegments; i++)
        {
            var (sin, cos) = MathF.SinCos(i * Step);
            var rim = new Vector3(sin * radius, 0f, cos * radius);

            context.DrawLine(rim - Vector3.UnitY * halfLength, rim + Vector3.UnitY * halfLength);
        }

        if (Shape != CappedTubeShape)
        {
            return;
        }

        for (var i = 0; i < TubeSegments / 2; i++)
        {
            var (sin, cos) = MathF.SinCos(i * Step);
            var rim = new Vector3(sin * radius, 0f, cos * radius);

            context.DrawLine(rim - Vector3.UnitY * halfLength, -rim - Vector3.UnitY * halfLength);
            context.DrawLine(rim + Vector3.UnitY * halfLength, -rim + Vector3.UnitY * halfLength);
        }
    }

    // Lines along a cone out to the range, split where the light starts fading out
    private void DrawConeLines(ToolRenderContext context, float angle, Color32 color, Color32 nearColor)
    {
        var (sinAngle, cosAngle) = MathF.SinCos(angle);

        for (var i = 0; i < ConeLines; i++)
        {
            var (sin, cos) = MathF.SinCos(i * MathF.Tau / ConeLines);
            var end = new Vector3(cosAngle, sinAngle * sin, sinAngle * cos) * Range;

            context.DrawLine(end * Skirt, end, color);
            context.DrawLine(Vector3.Zero, end * Skirt, nearColor);
        }
    }
}
