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
        /// Derived from <see cref="Frame"/> and the frame rate of the animation the event belongs to.
        /// </remarks>
        public float StartTime { get; init; }

        /// <inheritdoc/>
        /// <remarks>
        /// Always zero, sequence events fire at a single frame.
        /// </remarks>
        public float Duration => 0f;

        /// <summary>
        /// Initializes a new instance of the <see cref="AnimationEvent"/> struct.
        /// </summary>
        /// <param name="data">The event data.</param>
        /// <param name="fps">The frame rate of the animation the event belongs to, used to time the event.</param>
        public AnimationEvent(KVObject data, float fps)
        {
            Name = data.GetStringProperty("m_sEventName");
            Frame = data.GetInt32Property("m_nFrame");
            Cycle = data.GetFloatProperty("m_flCycle");
            EventData = data.GetSubCollection("m_EventData");
            Options = data.GetStringProperty("m_sOptions");
            StartTime = fps > 0f ? Frame / fps : 0f;
        }
    }
}
