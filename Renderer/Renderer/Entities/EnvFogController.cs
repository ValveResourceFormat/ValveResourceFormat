using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary><c>env_fog_controller</c>. Sets the linear fog of the scene.</summary>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/server/CFogController">CFogController</seealso>
public sealed class EnvFogController : BaseEntity
{
    // The values a transition blends, with colors in sRGB
    private readonly record struct FogValues(Vector3 Color, Vector3 SecondaryColor, float Start, float End, float MaxDensity)
    {
        public static FogValues Lerp(FogValues from, FogValues to, float t) => new(
            Vector3.Lerp(from.Color, to.Color, t),
            Vector3.Lerp(from.SecondaryColor, to.SecondaryColor, t),
            float.Lerp(from.Start, to.Start, t),
            float.Lerp(from.End, to.End, t),
            float.Lerp(from.MaxDensity, to.MaxDensity, t));
    }

    private FogValues current;
    private FogValues target;
    private FogValues transitionFrom;
    private float transitionStartTime;
    private float transitionDuration;
    private bool enabled;
    private float farZ;
    private float exponent;
    private float hdrColorScale;
    private Vector3? blendDirection;

    /// <summary>Initializes an <c>env_fog_controller</c> from its keyvalues.</summary>
    /// <param name="system">The entity system the fog controller belongs to.</param>
    /// <param name="spawnInfo">The fog controller's keyvalues and placement.</param>
    public EnvFogController(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    /// <inheritdoc/>
    public override void Spawn()
    {
        base.Spawn();

        current = new FogValues(
            KeyValues.GetColor32Property("fogcolor"),
            KeyValues.GetColor32Property("fogcolor2"),
            KeyValues.GetFloatProperty("fogstart"),
            KeyValues.GetFloatProperty("fogend"),
            KeyValues.GetFloatProperty("fogmaxdensity", 1f));
        target = current;

        enabled = KeyValues.GetBooleanProperty("fogenable");
        farZ = KeyValues.GetFloatProperty("farz", -1f);
        exponent = KeyValues.GetFloatProperty("fogexponent", 1f);
        hdrColorScale = KeyValues.GetFloatProperty("hdrcolorscale", 1f);
        transitionDuration = KeyValues.GetFloatProperty("foglerptime");

        if (KeyValues.GetBooleanProperty("fogblend"))
        {
            blendDirection = KeyValues.GetBooleanProperty("use_angles")
                ? EntityTransformHelper.EulerAnglesToForwardDirection(WorldAngles)
                : MathUtils.SafeNormalize(KeyValues.GetVector3Property("fogdir", Vector3.UnitX), Vector3.UnitX);
        }

        UpdateFog();
    }

    /// <inheritdoc/>
    public override void Think()
    {
        var t = transitionDuration > 0f ? (EntitySystem.CurrentTime - transitionStartTime) / transitionDuration : 1f;

        current = FogValues.Lerp(transitionFrom, target, MathUtils.Saturate(t));
        UpdateFog();

        if (t < 1f)
        {
            SetNextThink(EntitySystem.CurrentTime + EntitySystem.TickInterval);
        }
    }

    private void UpdateFog()
    {
        // A fog that is off covers nothing, though it keeps its color
        var maxDensity = enabled ? Math.Clamp(current.MaxDensity, 0f, 1f) : 0f;

        Scene.FogInfo.LinearFog = new LinearFog(
            ColorSpace.SrgbGammaToLinear(current.Color) * hdrColorScale,
            current.Start,
            current.End,
            maxDensity,
            exponent)
        {
            SecondaryColor = ColorSpace.SrgbGammaToLinear(current.SecondaryColor) * hdrColorScale,
            BlendDirection = blendDirection,
            FarZ = farZ,
        };
    }

    // Setting a value right away ends any transition
    private void Set(FogValues values)
    {
        SetNextThink(-1f);
        current = target = values;
        UpdateFog();
    }

    // "255 200 100" (0-255) to 0-1 sRGB; an unparseable parameter keeps the current color
    private static Vector3 ParseColor(EntityInputData data, Vector3 color) => Vector3.Clamp(data.Vector(color * 255f) / 255f, Vector3.Zero, Vector3.One);

    [EntityInput("TurnOn")]
    private void InputTurnOn(EntityInputData data)
    {
        enabled = true;
        UpdateFog();
    }

    [EntityInput("TurnOff")]
    private void InputTurnOff(EntityInputData data)
    {
        enabled = false;
        UpdateFog();
    }

    [EntityInput("SetColor")] private void InputSetColor(EntityInputData data) => Set(current with { Color = ParseColor(data, current.Color) });

    [EntityInput("SetColorSecondary")] private void InputSetColorSecondary(EntityInputData data) => Set(current with { SecondaryColor = ParseColor(data, current.SecondaryColor) });

    [EntityInput("SetStartDist")] private void InputSetStartDist(EntityInputData data) => Set(current with { Start = data.Float(current.Start) });

    [EntityInput("SetEndDist")] private void InputSetEndDist(EntityInputData data) => Set(current with { End = data.Float(current.End) });

    [EntityInput("SetMaxDensity")] private void InputSetMaxDensity(EntityInputData data) => Set(current with { MaxDensity = data.Float(current.MaxDensity) });

    [EntityInput("SetFarZ")]
    private void InputSetFarZ(EntityInputData data)
    {
        farZ = data.Float(farZ);
        UpdateFog();
    }

    [EntityInput("SetColorLerpTo")] private void InputSetColorLerpTo(EntityInputData data) => target = target with { Color = ParseColor(data, target.Color) };

    [EntityInput("SetColorSecondaryLerpTo")] private void InputSetColorSecondaryLerpTo(EntityInputData data) => target = target with { SecondaryColor = ParseColor(data, target.SecondaryColor) };

    [EntityInput("SetStartDistLerpTo")] private void InputSetStartDistLerpTo(EntityInputData data) => target = target with { Start = data.Float(target.Start) };

    [EntityInput("SetEndDistLerpTo")] private void InputSetEndDistLerpTo(EntityInputData data) => target = target with { End = data.Float(target.End) };

    [EntityInput("SetMaxDensityLerpTo")] private void InputSetMaxDensityLerpTo(EntityInputData data) => target = target with { MaxDensity = data.Float(target.MaxDensity) };

    [EntityInput("StartFogTransition")]
    private void InputStartFogTransition(EntityInputData data)
    {
        transitionFrom = current;
        transitionStartTime = EntitySystem.CurrentTime;
        Think();
    }
}
