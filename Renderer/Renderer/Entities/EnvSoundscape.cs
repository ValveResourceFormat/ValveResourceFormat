using ValveResourceFormat.Renderer.Audio;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// <c>env_soundscape</c> / <c>snd_soundscape</c>. Registers the region the map's ambient bed plays from:
/// a single sound event ("enablesoundevent") or a classic scripted soundscape. The sound player picks
/// which region is audible each frame; the entity only owns the region and its enabled state.
/// </summary>
public sealed class EnvSoundscape : BaseEntity
{
    private SoundEventPlayer.Soundscape? region;

    /// <summary>Initializes an <c>env_soundscape</c> from its keyvalues.</summary>
    public EnvSoundscape(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    /// <inheritdoc/>
    public override void Spawn()
    {
        var radius = KeyValues.GetFloatProperty("radius");

        region = KeyValues.GetBooleanProperty("enablesoundevent")
            ? Sound.AddSoundscape(Origin, radius, KeyValues.GetStringProperty("soundevent"))
            : Sound.AddScriptedSoundscape(Origin, radius, KeyValues.GetStringProperty("soundscape"));

        region?.Enabled = !KeyValues.GetBooleanProperty("startdisabled");
    }

    /// <summary>The region was registered before the 3D sky placement was known; recenter it.</summary>
    protected override void OnSpawnGroupTransformApplied() => region?.Position = Transform.Translation;

    /// <inheritdoc/>
    protected override void OnRemove()
    {
        region?.Enabled = false;

        base.OnRemove();
    }

    [EntityInput("Enable")] private void InputEnable(EntityInputData data) => region?.Enabled = true;
    [EntityInput("Disable")] private void InputDisable(EntityInputData data) => region?.Enabled = false;

    [EntityInput("ToggleEnabled")]
    private void InputToggleEnabled(EntityInputData data)
    {
        if (region != null)
        {
            region.Enabled = !region.Enabled;
        }
    }
}
