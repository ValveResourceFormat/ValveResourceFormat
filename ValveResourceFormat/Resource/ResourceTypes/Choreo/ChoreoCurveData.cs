using ValveResourceFormat.Serialization.KeyValues;

#nullable disable

namespace ValveResourceFormat.ResourceTypes.Choreo
{
    /// <summary>
    /// Represents curve data in a choreography scene.
    /// </summary>
    public class ChoreoCurveData
    {
        /// <summary>
        /// Gets the samples in this curve.
        /// </summary>
        public ChoreoSample[] Samples { get; private set; }

        /// <summary>
        /// Gets the left edge of the curve.
        /// </summary>
        public ChoreoEdge LeftEdge { get; private set; }

        /// <summary>
        /// Gets the right edge of the curve.
        /// </summary>
        public ChoreoEdge RightEdge { get; private set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ChoreoCurveData"/> class.
        /// </summary>
        /// <param name="samples">The samples in this curve.</param>
        /// <param name="leftEdge">The left edge of the curve.</param>
        /// <param name="rightEdge">The right edge of the curve.</param>
        public ChoreoCurveData(ChoreoSample[] samples, ChoreoEdge leftEdge, ChoreoEdge rightEdge)
        {
            Samples = samples;
            LeftEdge = leftEdge;
            RightEdge = rightEdge;
        }

        /// <summary>
        /// Converts this curve data to a <see cref="KVObject"/>.
        /// </summary>
        /// <returns>A <see cref="KVObject"/> representing this curve data.</returns>
        //TODO: This doesn't print edges, though they're not part of the ramp object in .vcds either.
        //Maybe return an array of kvobjects instead? That'd be different from the other choreo classes though.
        public KVObject ToKeyValues()
        {
            var kv = new KVObject(null, true, Samples.Length);

            foreach (var sample in Samples)
            {
                kv.AddItem(sample.ToKeyValues());
            }

            return kv;
        }
    }
}
