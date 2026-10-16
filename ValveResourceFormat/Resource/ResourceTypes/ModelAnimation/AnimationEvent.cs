using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.ModelAnimation
{
    /// <summary>
    /// Represents an event that occurs at a specific frame in an animation.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/animationsystem/CAnimEventDefinition">CAnimEventDefinition</seealso>
    public readonly struct AnimationEvent : IAnimationEvent
    {
        /// <summary>
        /// Gets the name of the event.
        /// </summary>
        public string Name { get; init; }

        /// <summary>
        /// Gets the frame at which the event occurs.
        /// </summary>
        public int Frame { get; init; }

        /// <summary>
        /// Gets the frame at which the event window ends, or -1 for an event with no window.
        /// </summary>
        public int EndFrame { get; init; }

        /// <summary>
        /// Gets the normalized cycle time of the event.
        /// </summary>
        public float Cycle { get; init; }

        /// <summary>
        /// Gets the event data.
        /// </summary>
        public KVObject EventData { get; init; }

        /// <summary>
        /// Gets the event options.
        /// </summary>
        public string Options { get; init; }

        /// <inheritdoc/>
        /// <remarks>
        /// The authored <see cref="Cycle"/>, sequence events carry one alongside their <see cref="Frame"/>.
        /// </remarks>
        public float StartCycle => Cycle;

        /// <inheritdoc/>
        /// <remarks>
        /// Always zero, sequence events are authored on a <see cref="Frame"/> rather than in seconds.
        /// </remarks>
        public float StartTime => 0f;

        /// <inheritdoc/>
        public float Duration { get; init; }

        /// <summary>
        /// Initializes a new instance of the <see cref="AnimationEvent"/> struct.
        /// </summary>
        public AnimationEvent(KVObject data)
        {
            Name = data.GetStringProperty("m_sEventName");
            Frame = data.GetInt32Property("m_nFrame");
            EndFrame = data.ContainsKey("m_nEndFrame") ? data.GetInt32Property("m_nEndFrame") : -1;
            Cycle = data.GetFloatProperty("m_flCycle");
            Duration = data.GetFloatProperty("m_flDuration");
            EventData = data.GetSubCollection("m_EventData");
            Options = data.GetStringProperty("m_sOptions");
        }
    }
}
