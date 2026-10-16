using ValveResourceFormat.IO.Fgd;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.Renderer.Editor.Helpers;

/// <summary>
/// A rect light, which shows its luminaire and the lobes of what it emits while its entity is selected.
/// </summary>
public sealed class RectLightHelper : LightHelperBase
{
    private const int DiscShape = 1;

    private static readonly Color32 ShapeColor = new(192, 255, 255, 32);
    private static readonly Color32 ShapeBoundsColor = new(255, 255, 0, 16);

    /// <summary>Gets the shape of the luminaire: 0 for a rectangle, 1 for a disc.</summary>
    public int Shape { get; }

    /// <summary>Initializes the light of an entity.</summary>
    /// <param name="owner">The entity.</param>
    /// <param name="declaration">The helper as the class of the entity declares it.</param>
    public RectLightHelper(IHelperOwner owner, FgdHelper declaration) : base(owner, declaration)
    {
        Shape = (int)GetFloatKey("shape");
    }

    internal override void AddSelectionShapes(in SelectionShapes shapes)
    {
        var halfSize = new Vector2(SizeParams.X, SizeParams.Y);

        // The luminaire is an ellipse or a rectangle, whatever the shape between them is
        shapes.Oriented(HelperPasses.Both).AddSuperellipse(Vector3.Zero, halfSize, Shape == DiscShape ? 1f : 0f, ShapeColor);

        // The lobes are only drawn over the scene, in the light's color at full brightness
        var hue = GetHue();
        var lobes = shapes.Oriented(HelperPasses.Overlay);

        lobes.AddEmissionCurves(Range, 0f, lobe: true, hue with { A = 64 });
        lobes.AddEmissionCurves(Range, 22.5f, lobe: false, hue with { A = 32 });
        lobes.AddEmissionCurves(Range * (1f - Skirt), 0f, lobe: true, hue with { A = 16 });

        // A disc also shows the rectangle it fits in
        if (Shape == DiscShape)
        {
            lobes.AddSuperellipse(Vector3.Zero, halfSize, 0f, ShapeBoundsColor);
        }
    }
}
