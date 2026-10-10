using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// The light entities. Owns the <see cref="SceneLight"/>. Real-time lights (barn, rect, omni2) are
/// re-binned every frame; slot-stored lights (omni, spot, ortho, environment) re-store the lighting
/// uniforms on change. Light styles and volumetric fog are not simulated.
/// </summary>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/server/CLightEntity">CLightEntity</seealso>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/server/CBarnLight">CBarnLight</seealso>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/server/CLightEnvironmentEntity">CLightEnvironmentEntity</seealso>
public sealed class LightEntity : BaseEntity
{
    /// <summary>Gets whether the light is on.</summary>
    public bool IsEnabled { get; private set; }

    private SceneLight? light;
    private float brightnessScale = 1f;

    /// <summary>Gets the light, or <see langword="null"/> for a light class that is not rendered.</summary>
    internal SceneLight? Light => light;
    private bool slotStored;

    /// <summary>Initializes a light entity from its keyvalues.</summary>
    public LightEntity(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    // NoShadows so a light's own icon cannot shadow the light
    /// <inheritdoc/>
    protected override SceneNode? CreateRootNode() => CreateEditorNode(ObjectTypeFlags.NoShadows);

    /// <inheritdoc/>
    public override void Spawn()
    {
        var (accepted, type) = SceneLight.IsAccepted(Classname);

        if (!accepted)
        {
            return;
        }

        light = SceneLight.FromEntityProperties(Scene, type, KeyValues);
        light.Flags |= ObjectTypeFlags.NoShadows;

        IsEnabled = light.Enabled;
        brightnessScale = light.BrightnessScale;
        slotStored = !SceneLight.IsRealTimeLight(light);

        // Off is a brightness scale of zero rather than Enabled = false, which would drop a stationary
        // light from the store and leave its old slot data lit
        light.Enabled = true;

        light.PlaceAt(Transform);

        // The light-store sweep after entity load picks the node up from the scene
        AddNode(light);

        // Rect and omni2 lights can draw their luminaire as geometry
        if (light.UsesOmni2Faces && KeyValues.GetBooleanProperty("showlight"))
        {
            AddNode(new LuminaireSceneNode(Scene, light));
        }

        Apply();
    }

    /// <summary>Moves the light with the entity; lights only move by teleport.</summary>
    public override void Teleport(Vector3 origin, Vector3? angles)
    {
        base.Teleport(origin, angles);

        if (light != null)
        {
            light.PlaceAt(Transform);
            Changed();
        }
    }

    /// <summary>Turns the light on or off.</summary>
    public void SetEnabled(bool enabled)
    {
        IsEnabled = enabled;
        Apply();
    }

    // Zero brightness drops the light out of binning (see SceneLight.IsVisible); the authored scale is kept
    private void Apply()
    {
        if (light == null)
        {
            return;
        }

        light.BrightnessScale = IsEnabled ? brightnessScale : 0f;
        Changed();
    }

    // The legacy lights need their uniforms updated
    private void Changed()
    {
        light!.IsDirty = true;

        if (slotStored)
        {
            Scene.LightingInfo.UpdateGpuLightBuffers();
        }
    }

    [EntityInput("Enable")] private void InputEnable(EntityInputData data) => SetEnabled(true);

    [EntityInput("Disable")] private void InputDisable(EntityInputData data) => SetEnabled(false);

    [EntityInput("Toggle")] private void InputToggle(EntityInputData data) => SetEnabled(!IsEnabled);

    [EntityInput("SetBrightness")]
    private void InputSetBrightness(EntityInputData data)
    {
        if (light == null)
        {
            return;
        }

        // Barn, rect and omni2 lights take an exposure value, which can be negative. The lumens a baked
        // light is stored with scale along with it.
        if (light.IsLight2)
        {
            var linearBrightness = float.Exp2(data.Float(MathF.Log2(light.LinearBrightness)));
            light.Brightness *= linearBrightness / light.LinearBrightness;
            light.LinearBrightness = linearBrightness;
        }
        else
        {
            light.Brightness = MathF.Max(data.Float(light.Brightness), 0f);
        }

        Changed();
    }

    [EntityInput("SetBrightnessScale")]
    private void InputSetBrightnessScale(EntityInputData data)
    {
        brightnessScale = MathF.Max(data.Float(brightnessScale), 0f);
        Apply();
    }

    // "255 200 100" (0-255) to 0-1 sRGB; an unparseable parameter keeps the current color
    [EntityInput("SetColor")]
    private void InputSetColor(EntityInputData data)
    {
        // A light in color temperature mode keeps its color
        if (light is { ColorTemperature: null } && data.Parameter is { } parameter
            && EntityTransformHelper.TryParseVector3(parameter, out var color))
        {
            light.Color = Vector3.Clamp(color / 255f, Vector3.Zero, Vector3.One);
            Changed();
        }
    }

    [EntityInput("SetColorTemperature")]
    private void InputSetColorTemperature(EntityInputData data)
    {
        if (light is { ColorTemperature: { } colorTemperature })
        {
            light.ColorTemperature = data.Float(colorTemperature);
            light.Color = ColorSpace.ColorTemperatureToSrgb(light.ColorTemperature.Value);
            Changed();
        }
    }
}
