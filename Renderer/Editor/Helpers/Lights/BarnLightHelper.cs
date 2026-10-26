using ValveResourceFormat.IO.Fgd;
using ValveResourceFormat.Renderer.Editor.ToolRender;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.Renderer.Editor.Helpers;

/// <summary>
/// A barn light, which shows the frustum it lights while its entity is selected: its luminaire, its far end
/// at its range, where it fades in and out, and how soft its edges are.
/// </summary>
internal sealed class BarnLightHelper : LightHelperBase
{
    private static readonly Color32 ShapeColor = new(192, 255, 255, 32);
    private static readonly Color32 SoftEdgeColor = new(0, 255, 255, 32);
    private static readonly Color32 SkirtNearColor = new(255, 0, 64, 24);
    private static readonly Color32 SkirtColor = new(255, 64, 0, 24);
    private static readonly Color32 ShapeBoundsColor = new(255, 255, 0, 16);
    private static readonly Color32 LuminaireColor = new(255, 255, 0, 128);
    private static readonly Color32 RectangleLuminaireColor = new(255, 255, 0, 32);

    private const int RectangleLuminaire = 2;
    private const float HandleSize = 6f;

    /// <summary>Gets the part of the width and of the height that is lit at full strength, below one.</summary>
    private Vector2 Hardness { get; }

    /// <summary>Gets the shape of the area light: 1 for a disc, 2 for a rectangle.</summary>
    private int LuminaireShape { get; }

    /// <summary>Gets the angle the area light covers, in degrees.</summary>
    private float LuminaireSize { get; }

    /// <summary>Gets how much narrower the area light is along one axis than the other, from -1 to 1.</summary>
    private float LuminaireAnisotropy { get; }

    public BarnLightHelper(IHelperOwner owner, HelperDeclaration declaration) : base(owner, declaration)
    {
        Hardness = Vector2.Clamp(Vector2.One - new Vector2(GetFloatKey("soft_x"), GetFloatKey("soft_y")), Vector2.Zero, new Vector2(0.9999999f));
        LuminaireShape = (int)GetFloatKey("luminaire_shape", 1f);
        LuminaireSize = GetFloatKey("luminaire_size", 4f);
        LuminaireAnisotropy = GetFloatKey("luminaire_anisotropy");
    }

    internal override void Render(ToolRenderContext context, in HelperRenderState state)
    {
        if (!state.IsSelected)
        {
            return;
        }

        // A texture decides the shape, which is then outlined as a rectangle
        var exponent = HasCookie ? 0f : ShapeExponent;

        var nearHalfSize = new Vector2(SizeParams.X, SizeParams.Y);
        var farCenter = new Vector3(Range, Shear.X, Shear.Y);
        var farHalfSize = GetHalfSizeAt(Range);

        context.PushTransform(Owner.RigidTransform);

        // Solid where seen, and faint over the scene
        foreach (var mode in (ReadOnlySpan<ToolRenderMode>)[ToolRenderMode.Wireframe, ToolRenderMode.WireframeTranslucentNoZ])
        {
            context.PushRenderMode(mode);

            if (!HasCookie)
            {
                context.DrawColor = SoftEdgeColor;
                DrawFrustum(context, nearHalfSize * Hardness, farCenter, farHalfSize * Hardness, exponent);
            }

            context.DrawColor = ShapeColor;
            DrawFrustum(context, nearHalfSize, farCenter, farHalfSize, exponent);

            if (SkirtNear != 0f)
            {
                var center = farCenter * SkirtNear;
                var halfSize = GetHalfSizeAt(Range * SkirtNear);

                context.DrawColor = SkirtNearColor;
                context.DrawSuperellipse(center, halfSize, exponent);
                context.DrawSuperellipseConnectors(Vector3.Zero, nearHalfSize, center, halfSize, exponent, 0f, 20f);
            }

            if (Skirt != 0f)
            {
                var center = farCenter * (1f - Skirt);
                var halfSize = GetHalfSizeAt(Range * (1f - Skirt));

                context.DrawColor = SkirtColor;
                context.DrawSuperellipse(center, halfSize, exponent);
                context.DrawSuperellipseConnectors(farCenter, farHalfSize, center, halfSize, exponent, 10f, 20f);
            }

            context.PopRenderMode();
        }

        context.PushRenderMode(ToolRenderMode.WireframeTranslucentNoZ);

        // A rounded shape also shows the rectangle it fits in
        if (exponent > 0f)
        {
            context.DrawColor = ShapeBoundsColor;
            context.DrawSuperellipse(Vector3.Zero, nearHalfSize, 0f);
            context.DrawSuperellipse(farCenter, farHalfSize, 0f);
        }

        DrawLuminaire(context);

        context.PopRenderMode();

        DrawHandles(context, nearHalfSize, farCenter, farHalfSize);

        context.PopTransform();
    }

    // Handles on the far end, its edges, its corners and a quarter of its smaller size in from its center, on
    // the origin, and on the edges and corners of the near end
    private void DrawHandles(ToolRenderContext context, Vector2 nearHalfSize, Vector3 farCenter, Vector2 farHalfSize)
    {
        var quarter = MathF.Min(SizeParams.X, SizeParams.Y) * (Range * SizeParams.Z + 1f) / 4f;

        ReadOnlySpan<Vector3> positions =
        [
            farCenter,
            farCenter + new Vector3(0f, farHalfSize.X, 0f),
            farCenter - new Vector3(0f, farHalfSize.X, 0f),
            farCenter + new Vector3(0f, 0f, farHalfSize.Y),
            farCenter - new Vector3(0f, 0f, farHalfSize.Y),
            farCenter + new Vector3(0f, quarter, 0f),
            farCenter - new Vector3(0f, quarter, 0f),
            farCenter + new Vector3(0f, 0f, quarter),
            farCenter - new Vector3(0f, 0f, quarter),
            farCenter + new Vector3(0f, farHalfSize.X, farHalfSize.Y),
            farCenter + new Vector3(0f, farHalfSize.X, -farHalfSize.Y),
            farCenter + new Vector3(0f, -farHalfSize.X, -farHalfSize.Y),
            farCenter + new Vector3(0f, -farHalfSize.X, farHalfSize.Y),
            Vector3.Zero,
            new(0f, nearHalfSize.X, 0f),
            new(0f, -nearHalfSize.X, 0f),
            new(0f, 0f, nearHalfSize.Y),
            new(0f, 0f, -nearHalfSize.Y),
            new(0f, nearHalfSize.X, nearHalfSize.Y),
            new(0f, nearHalfSize.X, -nearHalfSize.Y),
            new(0f, -nearHalfSize.X, -nearHalfSize.Y),
            new(0f, -nearHalfSize.X, nearHalfSize.Y),
        ];

        foreach (var position in positions)
        {
            context.DrawHandle(HandleSize, ToolHandleType.Square, Color32.White, position, noZ: true, border: true, translucent: false);
        }
    }

    // The area light sits where the sides of a widening frustum meet behind the entity, as large as the angle
    // it covers is there, plus some
    private void DrawLuminaire(ToolRenderContext context)
    {
        if (SizeParams.Z <= 0f)
        {
            return;
        }

        var apex = new Vector3(-1f, -Shear.X / Range, -Shear.Y / Range) / SizeParams.Z;

        var size = Math.Clamp(LuminaireSize, 1f, 90f);
        var narrowed = size * MathF.Max(0.01f, 1f - MathF.Abs(LuminaireAnisotropy));
        var (width, height) = LuminaireAnisotropy > 0f ? (size, narrowed) : (narrowed, size);
        var halfSize = new Vector2(MathF.Tan(float.DegreesToRadians(width) / 2f), MathF.Tan(float.DegreesToRadians(height) / 2f)) * (apex.Length() + 100f);

        switch (LuminaireShape)
        {
            case <= 1:
                context.DrawColor = LuminaireColor;
                context.DrawCircle(apex, -Vector3.UnitZ * halfSize.Y, Vector3.UnitY * halfSize.X, 1f, 32);
                break;
            case RectangleLuminaire:
                context.DrawColor = RectangleLuminaireColor;
                context.DrawSuperellipse(apex, halfSize, 0f);
                break;
            default:
                context.DrawColor = LuminaireColor;
                context.DrawWireSphere(apex, 3f, 6, 6);
                break;
        }
    }

    // The light widens by size_params.z per unit of distance from its luminaire
    private Vector2 GetHalfSizeAt(float distance)
        => new Vector2(SizeParams.X, SizeParams.Y) * (distance * SizeParams.Z + 1f);

    private static void DrawFrustum(ToolRenderContext context, Vector2 nearHalfSize, Vector3 farCenter, Vector2 farHalfSize, float exponent)
    {
        context.DrawSuperellipse(Vector3.Zero, nearHalfSize, exponent);
        context.DrawSuperellipse(farCenter, farHalfSize, exponent);
        context.DrawSuperellipseConnectors(Vector3.Zero, nearHalfSize, farCenter, farHalfSize, exponent, 45f, 90f);
    }
}
