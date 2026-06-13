namespace ValveResourceFormat.ResourceTypes.Choreo
{
    /// <summary>
    /// Represents a relative tag for a choreography event.
    /// </summary>
    public class ChoreoEventRelativeTag
    {
        /// <summary>
        /// Gets the name of the relative tag.
        /// </summary>
        public string Name { get; init; }

        /// <summary>
        /// Gets the sound name associated with this tag.
        /// </summary>
        public string SoundName { get; init; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ChoreoEventRelativeTag"/> class.
        /// </summary>
        /// <param name="name">The name of the tag.</param>
        /// <param name="soundName">The sound name.</param>
        public ChoreoEventRelativeTag(string name, string soundName)
        {
            Name = name;
            SoundName = soundName;
        }
    }
}
