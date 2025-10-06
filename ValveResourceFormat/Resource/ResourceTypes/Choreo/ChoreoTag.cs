namespace ValveResourceFormat.ResourceTypes.Choreo
{
    /// <summary>
    /// Represents a tag in a choreography scene.
    /// </summary>
    public class ChoreoTag
    {
        /// <summary>
        /// Gets the name of the tag.
        /// </summary>
        public string Name { get; private set; }

        /// <summary>
        /// Gets the fraction/time position of the tag.
        /// </summary>
        public float Fraction { get; private set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ChoreoTag"/> class.
        /// </summary>
        /// <param name="name">The name of the tag.</param>
        /// <param name="fraction">The fraction/time position of the tag.</param>
        public ChoreoTag(string name, float fraction)
        {
            Name = name;
            Fraction = fraction;
        }
    }
}
