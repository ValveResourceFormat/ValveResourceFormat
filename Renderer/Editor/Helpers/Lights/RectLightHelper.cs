using ValveResourceFormat.Renderer.Editor.ToolRender;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.Renderer.Editor.Helpers;

/// <summary>
/// A rect light, which shows its luminaire and the lobes of what it emits while its entity is selected.
/// </summary>
internal sealed class RectLightHelper : LightHelperBase
{
    private const int DiscShape = 1;
    private const float HandleSize = 6f;

    private static readonly Color32 ShapeColor = new(192, 255, 255, 32);
    private static readonly Color32 ShapeBoundsColor = new(255, 255, 0, 16);

    /// <summary>Gets the shape of the luminaire: 0 for a rectangle, 1 for a disc.</summary>
    private int Shape { get; }

    public RectLightHelper(IHelperOwner owner, HelperDeclaration declaration) : base(owner, declaration)
    {
        Shape = (int)GetFloatKey("shape");
    }

    internal override void Render(ToolRenderContext context, in HelperRenderState state)
    {
        if (!state.IsSelected)
        {
            return;
        }

        var halfSize = new Vector2(SizeParams.X, SizeParams.Y);

        context.PushTransform(Owner.RigidTransform);

        // The luminaire is an ellipse or a rectangle, whatever the shape between them is, solid where seen and
        // faint over the scene
        foreach (var mode in (ReadOnlySpan<ToolRenderMode>)[ToolRenderMode.Wireframe, ToolRenderMode.WireframeTranslucentNoZ])
        {
            context.PushRenderMode(mode);
            context.DrawColor = ShapeColor;
            context.DrawSuperellipse(Vector3.Zero, halfSize, Shape == DiscShape ? 1f : 0f);
            context.PopRenderMode();
        }

        // The lobes are only drawn over the scene, in the light's color at full brightness
        var hue = GetHue();

        context.PushRenderMode(ToolRenderMode.WireframeTranslucentNoZ);

        context.DrawEmissionCurves(Range, 0f, lobe: true, hue with { A = 64 });
        context.DrawEmissionCurves(Range, 22.5f, lobe: false, hue with { A = 32 });
        context.DrawEmissionCurves(Range * (1f - Skirt), 0f, lobe: true, hue with { A = 16 });

        // A disc also shows the rectangle it fits in
        if (Shape == DiscShape)
        {
            context.DrawColor = ShapeBoundsColor;
            context.DrawSuperellipse(Vector3.Zero, halfSize, 0f);
        }

        context.PopRenderMode();

        DrawHandles(context, halfSize);

        context.PopTransform();
    }

    // Handles on the edges and corners of the luminaire, the range and where the light starts fading out
    private void DrawHandles(ToolRenderContext context, Vector2 halfSize)
    {
        ReadOnlySpan<Vector3> positions =
        [
            new(0f, halfSize.X, 0f),
            new(0f, -halfSize.X, 0f),
            new(0f, 0f, halfSize.Y),
            new(0f, 0f, -halfSize.Y),
            new(0f, halfSize.X, halfSize.Y),
            new(0f, halfSize.X, -halfSize.Y),
            new(0f, -halfSize.X, -halfSize.Y),
            new(0f, -halfSize.X, halfSize.Y),
            new(Range, 0f, 0f),
            new((1f - Skirt) * Range, 0f, 16f),
        ];

        foreach (var position in positions)
        {
            context.DrawHandle(HandleSize, ToolHandleType.Square, Color32.White, position, noZ: true, border: true, translucent: false);
        }
    }
}
