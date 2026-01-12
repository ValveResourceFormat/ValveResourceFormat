using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.Choreo
{
    /// <summary>
    /// Represents an actor in a choreography scene.
    /// </summary>
    public class ChoreoActor
    {
        /// <summary>
        /// Gets the name of the actor.
        /// </summary>
        public string Name { get; private set; }

        /// <summary>
        /// Gets the channels associated with this actor.
        /// </summary>
        public ChoreoChannel[] Channels { get; private set; }

        /// <summary>
        /// Gets a value indicating whether the actor is active.
        /// </summary>
        public bool IsActive { get; private set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ChoreoActor"/> class.
        /// </summary>
        /// <param name="name">The name of the actor.</param>
        /// <param name="channels">The channels associated with this actor.</param>
        /// <param name="isActive">Whether the actor is active.</param>
        public ChoreoActor(string name, ChoreoChannel[] channels, bool isActive)
        {
            Name = name;
            Channels = channels;
            IsActive = isActive;
        }

        /// <summary>
        /// Converts this actor to a <see cref="KVObject"/>.
        /// </summary>
        /// <returns>A <see cref="KVObject"/> representing this actor.</returns>
        public KVObject ToKeyValues()
        {
            var kv = new KVObject(null);
            kv.AddProperty("name", Name);

            var channels = new KVObject(null, isArray: true);
            foreach (var channel in Channels)
            {
                channels.AddItem(channel.ToKeyValues());
            }

            kv.AddProperty("channels", channels);
            kv.AddProperty("active", IsActive);

            return kv;
        }
    }
}
