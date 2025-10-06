using ValveResourceFormat.Serialization.KeyValues;

#nullable disable

namespace ValveResourceFormat.ResourceTypes.Choreo
{
    /// <summary>
    /// Represents flex animation data for a choreography event.
    /// </summary>
    public class ChoreoEventFlex
    {
        /// <summary>
        /// Gets the flex animation tracks.
        /// </summary>
        public ChoreoFlexAnimationTrack[] Tracks { get; private set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ChoreoEventFlex"/> class.
        /// </summary>
        /// <param name="tracks">The flex animation tracks.</param>
        public ChoreoEventFlex(ChoreoFlexAnimationTrack[] tracks)
        {
            Tracks = tracks;
        }

        /// <summary>
        /// Converts this event flex to a KeyValues object.
        /// </summary>
        /// <returns>A KeyValues object representing this event flex.</returns>
        public KVObject ToKeyValues()
        {
            var kv = new KVObject(null, true, Tracks.Length);

            foreach (var track in Tracks)
            {
                kv.AddProperty(null, track.ToKeyValues());
            }

            return kv;
        }
    }
}
