using Microsoft.Extensions.Logging;
using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// The light entities. Owns the <see cref="SceneLight"/>. Real-time lights (barn, rect, omni2) are
/// re-binned every frame; slot-stored lights (omni, spot, ortho, environment) re-store the lighting
/// uniforms on change. Volumetric fog is not simulated.
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

    private static readonly string[] StyleEventOutputs = ["OnStyleEvent0", "OnStyleEvent1", "OnStyleEvent2", "OnStyleEvent3"];
    private static readonly string[] StyleEventKeys = ["light_style_output_event0", "light_style_output_event1", "light_style_output_event2", "light_style_output_event3"];

    // Each plays once, the last loops unless enableAfterStyles is set
    private LightStyle[] styles = [];
    private int styleIndex;
    private float styleStartTime;
    private float styleTime;
    private bool? enableAfterStyles;

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
        base.Spawn();

        var (accepted, type) = SceneLight.IsAccepted(Classname);

        if (!accepted)
        {
            return;
        }

        var name = string.IsNullOrEmpty(TargetName) ? Classname : TargetName;
        light = SceneLight.FromEntityProperties(Scene, type, KeyValues, name);
        light.Flags |= ObjectTypeFlags.NoShadows;

        IsEnabled = light.Enabled;
        brightnessScale = light.BrightnessScale;
        slotStored = !SceneLight.IsRealTimeLight(light);

        // Off is a brightness scale of zero rather than Enabled = false, which would drop a stationary
        // light from the store and leave its old slot data lit
        light.Enabled = true;

        light.PlaceAt(Transform);

        if (!light.IsLight2)
        {
            PlayStyle(KeyValues.GetStringProperty("pattern") is { Length: > 0 } pattern
                ? new PatternLightStyle(pattern)
                : PatternLightStyle.FromAppearance(KeyValues.GetInt32Property("style")));
        }
        else if (KeyValues.GetStringProperty("light_style") is { Length: > 0 } lightStyle)
        {
            PlayStyles(lightStyle, randomStart: true);
        }

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
        StoredLightChanged();
    }

    private void StoredLightChanged()
    {
        if (slotStored)
        {
            Scene.LightingInfo.MarkStoredLightsChanged();
        }
    }

    private void PlayStyle(LightStyle? style)
    {
        styles = style != null ? [style] : [];
        enableAfterStyles = null;
        StartStyles(randomStart: false);
    }

    // Comma separated, a trailing "on" or "off" switches the light after the rest played once
    private void PlayStyles(string sequence, bool randomStart)
    {
        var names = sequence.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        enableAfterStyles = names is [.., var last] ? last.ToLowerInvariant() switch
        {
            "on" => true,
            "off" => false,
            _ => null,
        } : null;

        if (enableAfterStyles != null)
        {
            names = names[..^1];
        }

        var playing = new List<LightStyle>(names.Length);

        foreach (var name in names)
        {
            if (EntitySystem.LightStyles.TryGetValue(name, out var style))
            {
                playing.Add(style);
            }
            else
            {
                EntitySystem.Logger.LogWarning("{Classname} '{TargetName}' has unknown light style \"{Style}\"", Classname, TargetName, name);
            }
        }

        styles = [.. playing];
        StartStyles(randomStart);
    }

    private void StartStyles(bool randomStart)
    {
        if (light == null)
        {
            return;
        }

        styleIndex = 0;
        styleTime = 0f;
        styleStartTime = EntitySystem.CurrentTime;

        if (styles.Length == 0 || light.Cost == SceneLight.LightCost.Static)
        {
            styles = [];
            FinishStyles();
            return;
        }

        if (randomStart && styles.Length == 1 && enableAfterStyles == null)
        {
            styleTime = Random.Shared.NextSingle() * styles[0].Duration;
            styleStartTime -= styleTime;
        }

        SetStyleBrightness(styles[0].GetBrightness(styleTime));
        SetNextThink(EntitySystem.CurrentTime + EntitySystem.TickInterval);
    }

    private void FinishStyles()
    {
        SetNextThink(-1f);
        SetStyleBrightness(1f);

        if (enableAfterStyles is { } enable)
        {
            enableAfterStyles = null;
            SetEnabled(enable);
        }
    }

    /// <inheritdoc/>
    public override void Think()
    {
        if (styles.Length == 0)
        {
            return;
        }

        var style = styles[styleIndex];
        var time = EntitySystem.CurrentTime - styleStartTime;

        while (style.Duration > 0f && time >= style.Duration)
        {
            FireStyleEvents(style, styleTime, style.Duration);

            time -= style.Duration;
            styleStartTime += style.Duration;
            styleTime = 0f;

            if (styleIndex < styles.Length - 1)
            {
                style = styles[++styleIndex];
            }
            else if (enableAfterStyles != null)
            {
                styles = [];
                FinishStyles();
                return;
            }
        }

        FireStyleEvents(style, styleTime, time);
        styleTime = time;

        SetStyleBrightness(style.GetBrightness(time));
        SetNextThink(EntitySystem.CurrentTime + EntitySystem.TickInterval);
    }

    private void FireStyleEvents(LightStyle style, float from, float to)
    {
        foreach (var styleEvent in style.Events)
        {
            if (styleEvent.Time < from || styleEvent.Time >= to)
            {
                continue;
            }

            for (var i = 0; i < StyleEventOutputs.Length; i++)
            {
                if (string.Equals(KeyValues.GetStringProperty(StyleEventKeys[i]), styleEvent.Name, StringComparison.OrdinalIgnoreCase))
                {
                    EntitySystem.TriggerOutput(this, StyleEventOutputs[i]);
                }
            }
        }
    }

    private void SetStyleBrightness(float brightness)
    {
        if (light == null || light.StyleBrightness == brightness)
        {
            return;
        }

        light.StyleBrightness = brightness;
        StoredLightChanged();
    }

    [EntityInput("Enable")] private void InputEnable(EntityInputData data) => SetEnabled(true);

    [EntityInput("Disable")] private void InputDisable(EntityInputData data) => SetEnabled(false);

    [EntityInput("Toggle")] private void InputToggle(EntityInputData data) => SetEnabled(!IsEnabled);

    [EntityInput("SetStyle")] private void InputSetStyle(EntityInputData data) => PlayStyles(data.Parameter ?? string.Empty, randomStart: false);

    [EntityInput("SetAppearance")] private void InputSetAppearance(EntityInputData data) => PlayStyle(PatternLightStyle.FromAppearance(data.Int()));

    [EntityInput("SetCustomAppearance")]
    private void InputSetCustomAppearance(EntityInputData data)
        => PlayStyle(data.Parameter is { Length: > 0 } pattern ? new PatternLightStyle(pattern) : null);

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
