using ValveResourceFormat.IO.Fgd;
using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.Renderer.Editor.Helpers;

/// <summary>
/// The light of a barn, rect or omni light, with the keys its shapes are drawn from while its entity is
/// selected.
/// </summary>
public abstract class LightHelperBase : EntityHelper
{
    /// <summary>Gets the light, once it is in the scene.</summary>
    public SceneLight? Light { get; private set; }

    /// <summary>Gets how far the light reaches, at least one unit.</summary>
    public float Range { get; }

    /// <summary>
    /// Gets the exponent the outline of the light is rounded by: zero for a rectangle, one for an ellipse.
    /// </summary>
    public float ShapeExponent { get; }

    /// <summary>Gets the part of the range over which the light fades out, from zero to one.</summary>
    public float Skirt { get; }

    /// <summary>Gets the part of the range over which the light fades in, from zero to one.</summary>
    public float SkirtNear { get; }

    /// <summary>Gets the half width and half height of the luminaire, and how much a barn light widens per unit.</summary>
    public Vector3 SizeParams { get; }

    /// <summary>Gets how far the far end of a barn light is moved sideways and up.</summary>
    public Vector2 Shear { get; }

    /// <summary>Gets whether a texture shapes the light.</summary>
    public bool HasCookie { get; }

    /// <summary>Initializes the light of an entity, reading the keys every one of these lights has.</summary>
    /// <param name="owner">The entity.</param>
    /// <param name="declaration">The helper as the class of the entity declares it.</param>
    protected LightHelperBase(IHelperOwner owner, FgdHelper declaration) : base(owner, declaration)
    {
        Range = MathF.Max(GetFloatKey("range"), 1f);
        ShapeExponent = SceneLight.GetShapeExponent(GetFloatKey("shape"));
        Skirt = MathUtils.Saturate(GetFloatKey("skirt"));
        SkirtNear = MathUtils.Saturate(GetFloatKey("skirt_near"));
        SizeParams = GetVector3Key("size_params");
        Shear = GetVector2Key("shear");
        HasCookie = GetStringKey("lightcookie").EndsWith(".vtex", StringComparison.OrdinalIgnoreCase);
    }

    private LuminaireSceneNode? luminaire;

    /// <inheritdoc/>
    public override void AddToScene()
    {
        Light = LightHelper.AddLight(Owner);

        // The luminaire of a rect or omni2 light is drawn where showlight says so, and while it is selected
        if (Light is { HasLuminaire: true })
        {
            luminaire = new LuminaireSceneNode(Owner.Scene, Light)
            {
                Transform = Light.Transform,
                Visible = ShowsLuminaire,
            };

            Owner.AddNode(luminaire, followsEntity: true);
        }
    }

    private bool ShowsLuminaire => GetBooleanKey("showlight", false);

    /// <inheritdoc/>
    private protected override void OnSelectionChanged(bool isSelected)
    {
        luminaire?.Visible = isSelected || ShowsLuminaire;
    }

    /// <summary>
    /// Gets the color of the light at full brightness: its color normalized in linear space, back in gamma
    /// space and opaque.
    /// </summary>
    /// <returns>The color.</returns>
    protected Color32 GetHue()
    {
        var gamma = GetIntegerKey("colormode") switch
        {
            0 => GetVector3Key("color") / 255f,
            1 => ColorSpace.ColorTemperatureToSrgb(GetFloatKey("colortemperature")),
            _ => Vector3.Zero,
        };

        var linear = ColorSpace.SrgbGammaToLinear(gamma);
        var hue = ColorSpace.SrgbLinearToGamma(Vector3.Clamp(linear / MathF.Max(0.0001f, linear.MaxComponent()), Vector3.Zero, Vector3.One));

        return new Color32((byte)(hue.X * 255f), (byte)(hue.Y * 255f), (byte)(hue.Z * 255f));
    }
}
