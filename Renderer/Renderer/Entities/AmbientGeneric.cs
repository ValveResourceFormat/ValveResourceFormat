using Microsoft.Extensions.Logging;
using ValveResourceFormat.Renderer.Audio;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// <c>ambient_generic</c>. Plays one sound, either looping from the moment the map starts or fired by
/// entity I/O. The pitch controls, dynamic presets, and LFO modulation are not simulated.
/// </summary>
public sealed class AmbientGeneric : BaseEntity
{
    /// <summary>What an <c>ambient_generic</c>'s <c>spawnflags</c> mean.</summary>
    [Flags]
    public enum SpawnFlag : uint
    {
        /// <summary>No flags: a looping sound, audible from its own position, playing from the start.</summary>
        None = 0,

        /// <summary>Heard everywhere at full volume, rather than from a point in the world.</summary>
        PlayEverywhere = 1,

        /// <summary>Waits to be told to play instead of starting with the map.</summary>
        StartSilent = 16,

        /// <summary>Plays once per trigger rather than looping.</summary>
        NotLooping = 32,
    }

    /// <summary>Gets the sound event this plays, the <c>message</c> keyvalue.</summary>
    public string? SoundName { get; private set; }

    /// <summary>Gets the volume, 0 to 1. Authored as <c>health</c>, which runs 0 to 10.</summary>
    public float Volume { get; private set; } = 1f;

    /// <summary>Gets whether the sound repeats until stopped.</summary>
    public bool IsLooping => !HasSpawnFlags(SpawnFlag.NotLooping);

    /// <summary>Gets whether the sound is playing.</summary>
    public bool IsPlaying => playing.Playing;

    private SoundHandle playing;
    private SceneNode? soundSource;
    private float fadeInSeconds;
    private float fadeOutSeconds;

    /// <summary>Initializes an <c>ambient_generic</c> from its keyvalues.</summary>
    public AmbientGeneric(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    /// <inheritdoc/>
    public override void Spawn()
    {
        SoundName = KeyValues.GetStringProperty("message");

        // Authored 0 to 10, and the engine emits it as a fraction of full volume
        Volume = Math.Clamp(KeyValues.GetFloatProperty("health", 10f), 0f, 10f) / 10f;

        fadeInSeconds = KeyValues.GetFloatProperty("fadeinsecs");
        fadeOutSeconds = KeyValues.GetFloatProperty("fadeoutsecs");

        if (string.IsNullOrEmpty(SoundName))
        {
            EntitySystem.Logger.LogWarning("ambient_generic '{TargetName}' has no sound to play", TargetName);
        }
        else
        {
            Sound.Cache(SoundName);
        }
    }

    /// <inheritdoc/>
    public override void Activate()
    {
        var sourceName = KeyValues.GetStringProperty("sourceentityname");

        if (!string.IsNullOrEmpty(sourceName))
        {
            soundSource = Scene.FindNodeByTargetName(sourceName);

            if (soundSource == null)
            {
                EntitySystem.Logger.LogWarning("ambient_generic '{TargetName}' source entity '{Source}' was not found", TargetName, sourceName);
            }
        }

        if (HasSpawnFlags(SpawnFlag.StartSilent) || !IsLooping)
        {
            return;
        }

        // Left to the first tick rather than started here: entities activate while the rest of the map is
        // still loading, and the map should not be audible before it is on screen.
        SetNextThink(EntitySystem.CurrentTime + EntitySystem.TickInterval);
    }

    /// <inheritdoc/>
    public override void Think() => StartSound();

    /// <summary>Starts the sound, restarting it if it was already going.</summary>
    public void StartSound()
    {
        if (string.IsNullOrEmpty(SoundName))
        {
            return;
        }

        StopSound();

        // The event keeps its authored volume; the entity's fraction rides on top as the live gain,
        // so the Volume input can move it
        playing = Sound.Play(SoundName, GetEmitPosition());
        playing.Volume = Volume;

        if (fadeInSeconds > 0f)
        {
            playing.FadeIn(fadeInSeconds);
        }
    }

    /// <summary>Stops the sound, fading it out first when the entity was authored to.</summary>
    public void StopSound()
    {
        if (fadeOutSeconds > 0f)
        {
            playing.FadeOutAndStop(fadeOutSeconds);
        }
        else
        {
            playing.Stop();
        }

        playing = default;
    }

    /// <inheritdoc/>
    protected override void OnRemove()
    {
        playing.Stop();
        playing = default;

        base.OnRemove();
    }

    [EntityInput("PlaySound")]
    private void InputPlaySound(EntityInputData data)
    {
        // A looping ambient already playing is left alone; a one-shot fires again
        if (IsLooping && IsPlaying)
        {
            return;
        }

        StartSound();
    }

    [EntityInput("StopSound")]
    private void InputStopSound(EntityInputData data) => StopSound();

    [EntityInput("ToggleSound")]
    private void InputToggleSound(EntityInputData data)
    {
        if (IsPlaying)
        {
            StopSound();
        }
        else
        {
            StartSound();
        }
    }

    [EntityInput("Volume")]
    private void InputVolume(EntityInputData data)
    {
        Volume = Math.Clamp(data.Float(10f), 0f, 10f) / 10f;
        playing.Volume = Volume;
    }

    [EntityInput("FadeIn")]
    private void InputFadeIn(EntityInputData data)
    {
        if (!IsPlaying)
        {
            StartSound();
        }

        playing.FadeIn(MathF.Max(data.Float(), 0f));
    }

    [EntityInput("FadeOut")]
    private void InputFadeOut(EntityInputData data)
    {
        playing.FadeOutAndStop(MathF.Max(data.Float(), 0f));
        playing = default;
    }

    /// <summary>
    /// Where the sound emits from: the source entity if one was named, this entity otherwise, or nowhere
    /// in particular when it is set to play everywhere.
    /// </summary>
    private Vector3? GetEmitPosition()
    {
        if (HasSpawnFlags(SpawnFlag.PlayEverywhere))
        {
            return null;
        }

        return soundSource?.Transform.Translation ?? Transform.Translation;
    }
}
