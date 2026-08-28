using ValveResourceFormat.Renderer.Audio;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// <c>point_soundevent</c>. Plays a sound event, either the moment the map starts or whenever entity I/O
/// tells it to.
/// </summary>
public sealed class PointSoundEvent : BaseEntity
{
    /// <summary>Gets the sound event this plays.</summary>
    public string? SoundName { get; private set; }

    /// <summary>Gets whether the sound is playing.</summary>
    public bool IsPlaying => playing.Playing;

    private SoundHandle playing;

    /// <summary>Initializes a <c>point_soundevent</c> from its keyvalues.</summary>
    public PointSoundEvent(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    /// <inheritdoc/>
    public override void Spawn()
    {
        SoundName = KeyValues.GetStringProperty("soundname");
    }

    /// <inheritdoc/>
    public override void Activate()
    {
        if (!KeyValues.GetBooleanProperty("startonspawn"))
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

        playing = Sound.Play(SoundName, GetEmitPosition());
    }

    /// <summary>Stops the sound, if it is playing.</summary>
    public void StopSound()
    {
        playing.Stop();
        playing = default;
    }

    /// <inheritdoc/>
    protected override void OnRemove()
    {
        StopSound();

        base.OnRemove();
    }

    [EntityInput("StartSound")]
    private void InputStartSound(EntityInputData data) => StartSound();

    [EntityInput("StopSound")]
    private void InputStopSound(EntityInputData data) => StopSound();

    /// <summary>
    /// Where the sound emits from - the source entity's attachment or origin, falling back to this
    /// entity's own - or null for a "to local player" event played flat on the listener.
    /// </summary>
    private Vector3? GetEmitPosition()
    {
        if (KeyValues.GetBooleanProperty("tolocalplayer"))
        {
            return null;
        }

        var sourceEntityName = KeyValues.GetStringProperty("sourceentityname");

        if (string.IsNullOrEmpty(sourceEntityName)
            || Scene.FindNodeByTargetName(sourceEntityName) is not { } sourceNode)
        {
            return Transform.Translation;
        }

        var attachmentName = KeyValues.GetStringProperty("sourceentityattachment");

        if (!string.IsNullOrEmpty(attachmentName)
            && sourceNode is ModelSceneNode sourceModel
            && sourceModel.Attachments.ContainsKey(attachmentName))
        {
            return sourceModel.GetAttachmentTransform(attachmentName).Translation;
        }

        // The offset is authored relative to the source, and this entity was placed at the result of it
        if (KeyValues.GetBooleanProperty("uselocaloffset"))
        {
            return Transform.Translation;
        }

        return sourceNode.Transform.Translation;
    }
}
