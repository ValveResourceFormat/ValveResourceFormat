using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.ModelAnimation
{
    /// <summary>
    /// Represents an activity associated with an animation.
    /// </summary>
    public readonly struct AnimationActivity
    {
        /// <summary>
        /// Gets or sets the name of the activity.
        /// </summary>
        public string Name { get; init; }

        /// <summary>
        /// Gets or sets the activity identifier.
        /// </summary>
        public int Activity { get; init; }

        /// <summary>
        /// Gets or sets the activity flags.
        /// </summary>
        public int Flags { get; init; }

        /// <summary>
        /// Gets or sets the activity weight.
        /// </summary>
        public int Weight { get; init; }

        /// <summary>
        /// Initializes a new instance of the <see cref="AnimationActivity"/> struct.
        /// </summary>
        public AnimationActivity(KVObject data)
        {
            Name = data.GetStringProperty("m_name");
            Activity = data.GetInt32Property("m_nActivity");
            Flags = data.GetInt32Property("m_nFlags");
            Weight = data.GetInt32Property("m_nWeight");
        }
    }
}
