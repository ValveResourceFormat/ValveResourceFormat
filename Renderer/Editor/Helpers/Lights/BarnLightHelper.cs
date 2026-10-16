using ValveResourceFormat.IO.Fgd;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.Renderer.Editor.Helpers;

/// <summary>
/// A barn light, which shows the frustum it lights while its entity is selected: its luminaire, its far end
/// at its range, where it fades in and out, and how soft its edges are.
/// </summary>
public sealed class BarnLightHelper : LightHelperBase
{
    // Solid where seen, and faint over the scene
    private static readonly Color32 ShapeColor = new(192, 255, 255, 32);
    private static readonly Color32 SoftEdgeColor = new(0, 255, 255, 32);
    private static readonly Color32 SkirtNearColor = new(255, 0, 64, 24);
    private static readonly Color32 SkirtColor = new(255, 64, 0, 24);
    private static readonly Color32 ShapeBoundsColor = new(255, 255, 0, 16);
    private static readonly Color32 LuminaireColor = new(255, 255, 0, 128);
    private static readonly Color32 RectangleLuminaireColor = new(255, 255, 0, 32);

    private const int RectangleLuminaire = 2;

    /// <summary>Gets the part of the width and of the height that is lit at full strength, below one.</summary>
    public Vector2 Hardness { get; }

    /// <summary>Gets the shape of the area light: 1 for a disc, 2 for a rectangle.</summary>
    public int LuminaireShape { get; }

    /// <summary>Gets the angle the area light covers, in degrees.</summary>
    public float LuminaireSize { get; }

    /// <summary>Gets how much narrower the area light is along one axis than the other, from -1 to 1.</summary>
    public float LuminaireAnisotropy { get; }

    /// <summary>Initializes the light of an entity.</summary>
    /// <param name="owner">The entity.</param>
    /// <param name="declaration">The helper as the class of the entity declares it.</param>
    public BarnLightHelper(IHelperOwner owner, FgdHelper declaration) : base(owner, declaration)
    {
        Hardness = Vector2.Clamp(Vector2.One - new Vector2(GetFloatKey("soft_x"), GetFloatKey("soft_y")), Vector2.Zero, new Vector2(0.9999999f));
        LuminaireShape = (int)GetFloatKey("luminaire_shape", 1f);
        LuminaireSize = GetFloatKey("luminaire_size", 4f);
        LuminaireAnisotropy = GetFloatKey("luminaire_anisotropy");
    }

    internal override void AddSelectionShapes(in SelectionShapes shapes)
    {
        // A texture decides the shape, which is then outlined as a rectangle
        var exponent = HasCookie ? 0f : ShapeExponent;

        var nearHalfSize = new Vector2(SizeParams.X, SizeParams.Y);
        var farCenter = new Vector3(Range, Shear.X, Shear.Y);
        var farHalfSize = GetHalfSizeAt(Range);

        var lines = shapes.Oriented(HelperPasses.Both);

        if (!HasCookie)
        {
            AddFrustum(lines, nearHalfSize * Hardness, farCenter, farHalfSize * Hardness, exponent, SoftEdgeColor);
        }

        AddFrustum(lines, nearHalfSize, farCenter, farHalfSize, exponent, ShapeColor);

        if (SkirtNear != 0f)
        {
            var center = farCenter * SkirtNear;
            var halfSize = GetHalfSizeAt(Range * SkirtNear);

            lines.AddSuperellipse(center, halfSize, exponent, SkirtNearColor);
            lines.AddSuperellipseConnectors(Vector3.Zero, nearHalfSize, center, halfSize, exponent, 0f, 20f, SkirtNearColor);
        }

        if (Skirt != 0f)
        {
            var center = farCenter * (1f - Skirt);
            var halfSize = GetHalfSizeAt(Range * (1f - Skirt));

            lines.AddSuperellipse(center, halfSize, exponent, SkirtColor);
            lines.AddSuperellipseConnectors(farCenter, farHalfSize, center, halfSize, exponent, 10f, 20f, SkirtColor);
        }

        // A rounded shape also shows the rectangle it fits in
        if (exponent > 0f)
        {
            var bounds = shapes.Oriented(HelperPasses.Overlay);

            bounds.AddSuperellipse(Vector3.Zero, nearHalfSize, 0f, ShapeBoundsColor);
            bounds.AddSuperellipse(farCenter, farHalfSize, 0f, ShapeBoundsColor);
        }

        AddLuminaire(shapes.Oriented(HelperPasses.Overlay));
    }

    // The area light sits where the sides of a widening frustum meet behind the entity, as large as the angle
    // it covers is there, plus some
    private void AddLuminaire(EntityHelperLines lines)
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
                lines.AddCircle(apex, Vector3.UnitY * halfSize.X, Vector3.UnitZ * halfSize.Y, 32, LuminaireColor);
                break;
            case RectangleLuminaire:
                lines.AddSuperellipse(apex, halfSize, 0f, RectangleLuminaireColor);
                break;
            default:
                lines.AddSphereGrid(apex, 3f, 6, 6, 0f, MathF.PI, LuminaireColor);
                break;
        }
    }

    // The light widens by size_params.z per unit of distance from its luminaire
    private Vector2 GetHalfSizeAt(float distance)
        => new Vector2(SizeParams.X, SizeParams.Y) * (distance * SizeParams.Z + 1f);

    private static void AddFrustum(EntityHelperLines lines, Vector2 nearHalfSize, Vector3 farCenter, Vector2 farHalfSize, float exponent, Color32 color)
    {
        lines.AddSuperellipse(Vector3.Zero, nearHalfSize, exponent, color);
        lines.AddSuperellipse(farCenter, farHalfSize, exponent, color);
        lines.AddSuperellipseConnectors(Vector3.Zero, nearHalfSize, farCenter, farHalfSize, exponent, 45f, 90f, color);
    }
}
