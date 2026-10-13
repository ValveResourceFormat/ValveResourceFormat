using System.Globalization;
using ValveKeyValue;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Animation;

partial class Event : IAnimationEvent
{
    private float clipDuration;

    /// <summary>The compiled class of the event, such as <c>CNmSoundEvent</c>.</summary>
    public string ClassName { get; private set; } = string.Empty;

    /// <inheritdoc/>
    public float StartCycle => StartTime.Value;

    /// <summary>When the event starts, in seconds from the start of its clip.</summary>
    public float StartSeconds => StartTime.Value * clipDuration;

    /// <summary>How long the event lasts in seconds, zero for an immediate event.</summary>
    public float DurationSeconds => Duration.Value * clipDuration;

    float IAnimationEvent.StartTime => StartSeconds;

    float IAnimationEvent.Duration => DurationSeconds;

    partial void OnLoaded(KVObject data)
    {
        ClassName = data.GetStringProperty("_class", string.Empty);
    }

    /// <summary>Creates the event for one <c>m_events</c> entry of a clip that lasts <paramref name="clipDuration"/> seconds.</summary>
    public static Event Create(KVObject data, float clipDuration)
    {
        Event created = data.GetStringProperty("_class", string.Empty) switch
        {
            "CNmBodyGroupEvent" => new BodyGroupEvent(data),
            "CNmCameraDOFEvent" => new CameraDOFEvent(data),
            "CNmCameraFOVEvent" => new CameraFOVEvent(data),
            "CNmClothEvent" => new ClothEvent(data),
            "CNmContactEvent" => new ContactEvent(data),
            "CNmEntityAttributeFloatEvent" => new EntityAttributeFloatEvent(data),
            "CNmEntityAttributeIntEvent" => new EntityAttributeIntEvent(data),
            "CNmFloatCurveEvent" => new FloatCurveEvent(data),
            "CNmFootEvent" => new FootEvent(data),
            "CNmFrameSnapEvent" => new FrameSnapEvent(data),
            "CNmIDEvent" => new IDEvent(data),
            "CNmLegacyEvent" => new LegacyEvent(data),
            "CNmMaterialAttributeEvent" => new MaterialAttributeEvent(data),
            "CNmOrientationWarpEvent" => new OrientationWarpEvent(data),
            "CNmParticleEvent" => new ParticleEvent(data),
            "CNmRootMotionEvent" => new RootMotionEvent(data),
            "CNmSoundEvent" => new SoundEvent(data),
            "CNmTargetWarpEvent" => new TargetWarpEvent(data),
            "CNmTransitionEvent" => new TransitionEvent(data),
            _ => new Event(data),
        };

        created.clipDuration = clipDuration;
        return created;
    }

    /// <inheritdoc/>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{ClassName} @ {StartSeconds}s +{DurationSeconds}s");
}
