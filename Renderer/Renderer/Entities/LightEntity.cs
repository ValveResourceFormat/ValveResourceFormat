using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// The light entities. Owns the <see cref="SceneLight"/> and steers it live: the real-time set (barn,
/// rect, omni2) re-bins every frame so changes are plain property writes, while the slot-stored set
/// (omni, spot, ortho, environment) re-stores the lighting uniforms on change. Light styles, color
/// temperature and volumetric fog are not simulated.
/// </summary>
public sealed class LightEntity : BaseEntity
{
    /// <summary>Gets whether the light is on. A light turned off costs nothing to render.</summary>
    public bool IsEnabled { get; private set; }

    private SceneLight? light;
    private float brightnessScale = 1f;
    private bool slotStored;

    /// <summary>Initializes a light entity from its keyvalues.</summary>
    public LightEntity(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    // NoShadows like the loader gives light icons, so a light's own icon cannot shadow the light
    /// <inheritdoc/>
    protected override SceneNode? CreateRootNode()
        => World.EditorEntityNode.Create(Scene, KeyValues, Classname, Transform, ObjectTypeFlags.NoShadows);

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

        // On/off is a brightness scale of zero across every store, so the slots stay laid out the same
        // whatever the light's state; Enabled itself would drop a stationary light from the store and
        // leave its old slot data lit
        light.Enabled = true;

        // The light-store sweep after entity load picks the node up from the scene like a loader one
        AddNode(light);

        Apply();
    }

    /// <summary>Moves the light with the entity; a teleport is the only movement a light has.</summary>
    public override void Teleport(Vector3 origin, Vector3? angles)
    {
        base.Teleport(origin, angles);

        if (light != null)
        {
            light.Position = Origin;
            light.Direction = EntityTransformHelper.EulerAnglesToForwardDirection(Angles);
            Changed();
        }
    }

    /// <summary>Turns the light on or off.</summary>
    public void SetEnabled(bool enabled)
    {
        IsEnabled = enabled;
        Apply();
    }

    // A dark light drops out of binning entirely (see SceneLight.IsVisible), the same off-switch the
    // particle light renderers use, leaving the authored scale to come back with
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
        if (light != null)
        {
            light.Brightness = MathF.Max(data.Float(light.Brightness), 0f);
            Changed();
        }
    }

    [EntityInput("SetBrightnessScale")]
    private void InputSetBrightnessScale(EntityInputData data)
    {
        brightnessScale = MathF.Max(data.Float(brightnessScale), 0f);
        Apply();
    }

    // "255 200 100" on the usual 0-255 scale, to the 0-1 sRGB the light holds; an unparseable
    // parameter leaves the color alone rather than setting it black
    [EntityInput("SetColor")]
    private void InputSetColor(EntityInputData data)
    {
        if (light != null && data.Parameter is { } parameter
            && EntityTransformHelper.TryParseVector3(parameter, out var color))
        {
            light.Color = Vector3.Clamp(color / 255f, Vector3.Zero, Vector3.One);
            Changed();
        }
    }
}
