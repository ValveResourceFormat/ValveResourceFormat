using ValveResourceFormat.Serialization.KeyValues;

#nullable disable

namespace ValveResourceFormat.ResourceTypes.Choreo
{
    /// <summary>
    /// Represents a channel in a choreography scene.
    /// </summary>
    public class ChoreoChannel
    {
        /// <summary>
        /// Gets the name of the channel.
        /// </summary>
        public string Name { get; private set; }

        /// <summary>
        /// Gets the events in this channel.
        /// </summary>
        public ChoreoEvent[] Events { get; private set; }

        /// <summary>
        /// Gets a value indicating whether the channel is active.
        /// </summary>
        public bool IsActive { get; private set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ChoreoChannel"/> class.
        /// </summary>
        /// <param name="name">The name of the channel.</param>
        /// <param name="events">The events in this channel.</param>
        /// <param name="isActive">Whether the channel is active.</param>
        public ChoreoChannel(string name, ChoreoEvent[] events, bool isActive)
        {
            Name = name;
            Events = events;
            IsActive = isActive;
        }

        /// <summary>
        /// Converts this channel to a <see cref="KVObject"/>.
        /// </summary>
        /// <returns>A <see cref="KVObject"/> representing this channel.</returns>
        public KVObject ToKeyValues()
        {
            var kv = new KVObject(null);
            kv.AddProperty("name", Name);

            var events = new KVObject(null, isArray: true);
            foreach (var choreoEvent in Events)
            {
                events.AddItem(choreoEvent.ToKeyValues());
            }

            kv.AddProperty("events", events);
            kv.AddProperty("active", IsActive);

            return kv;
        }
    }
}
