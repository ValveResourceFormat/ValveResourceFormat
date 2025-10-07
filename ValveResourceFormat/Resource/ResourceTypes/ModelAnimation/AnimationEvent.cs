using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.ModelAnimation
{
    /// <summary>
    /// Represents an event that occurs at a specific frame in an animation.
    /// </summary>
    public readonly struct AnimationEvent
    {
        /// <summary>
        /// Gets or sets the name of the event.
        /// </summary>
        public string Name { get; init; }

        /// <summary>
        /// Gets or sets the frame at which the event occurs.
        /// </summary>
        public int Frame { get; init; }

        /// <summary>
        /// Gets or sets the normalized cycle time of the event.
        /// </summary>
        public float Cycle { get; init; }

        /// <summary>
        /// Gets or sets the event data.
        /// </summary>
        public KVObject EventData { get; init; }

        /// <summary>
        /// Gets or sets the event options.
        /// </summary>
        public string Options { get; init; }

        /// <summary>
        /// Initializes a new instance of the <see cref="AnimationEvent"/> struct.
        /// </summary>
        public AnimationEvent(KVObject data)
        {
            Name = data.GetStringProperty("m_sEventName");
            Frame = data.GetInt32Property("m_nFrame");
            Cycle = data.GetFloatProperty("m_flCycle");
            EventData = data.GetSubCollection("m_EventData");
            Options = data.GetStringProperty("m_sOptions");
        }
    }
}
