using ValveResourceFormat.ResourceTypes.Choreo.Enums;
using ValveResourceFormat.Serialization.KeyValues;

#nullable disable

namespace ValveResourceFormat.ResourceTypes.Choreo
{
    /// <summary>
    /// Represents a flex animation track in a choreography scene.
    /// </summary>
    public class ChoreoFlexAnimationTrack
    {
        /// <summary>
        /// Gets the name of the track.
        /// </summary>
        public string Name { get; private set; }

        /// <summary>
        /// Gets the flags for the track.
        /// </summary>
        public ChoreoTrackFlags TrackFlags { get; private set; }

        /// <summary>
        /// Gets the minimum range value.
        /// </summary>
        public float MinRange { get; private set; }

        /// <summary>
        /// Gets the maximum range value.
        /// </summary>
        public float MaxRange { get; private set; } = 1f;

        /// <summary>
        /// Gets the ramp curve data.
        /// </summary>
        public ChoreoCurveData Ramp { get; private set; }

        /// <summary>
        /// Gets the combo ramp curve data.
        /// </summary>
        public ChoreoCurveData ComboRamp { get; private set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ChoreoFlexAnimationTrack"/> class.
        /// </summary>
        /// <param name="name">The name of the track.</param>
        /// <param name="trackFlags">The flags for the track.</param>
        /// <param name="minRange">The minimum range value.</param>
        /// <param name="maxRange">The maximum range value.</param>
        /// <param name="samples">The ramp curve data.</param>
        /// <param name="comboSamples">The combo ramp curve data.</param>
        public ChoreoFlexAnimationTrack(string name, ChoreoTrackFlags trackFlags, float minRange, float maxRange, ChoreoCurveData samples, ChoreoCurveData comboSamples)
        {
            Name = name;
            TrackFlags = trackFlags;
            MinRange = minRange;
            MaxRange = maxRange;
            Ramp = samples;
            ComboRamp = comboSamples;
        }

        /// <summary>
        /// Converts this track to a KeyValues object.
        /// </summary>
        /// <returns>A KeyValues object representing this track.</returns>
        public KVObject ToKeyValues()
        {
            var kv = new KVObject(null);

            var isDisabled = !TrackFlags.HasFlag(ChoreoTrackFlags.Enabled);
            var isCombo = TrackFlags.HasFlag(ChoreoTrackFlags.Combo);

            kv.AddProperty("name", Name);
            if (isDisabled)
            {
                kv.AddProperty("disabled", true);
            }
            if (isCombo)
            {
                kv.AddProperty("combo", true);
            }
            kv.AddProperty("min", MinRange);
            kv.AddProperty("max", MaxRange);

            //Edges are the same for both curves
            if (Ramp?.LeftEdge != null)
            {
                kv.AddProperty("left_edge", Ramp.LeftEdge.ToKeyValues());
            }
            if (Ramp?.RightEdge != null)
            {
                kv.AddProperty("right_edge", Ramp.RightEdge.ToKeyValues());
            }

            if (Ramp?.Samples.Length > 0)
            {
                kv.AddProperty("samples", Ramp.ToKeyValues());
            }
            if (isCombo && ComboRamp?.Samples.Length > 0)
            {
                kv.AddProperty("stereo", ComboRamp.ToKeyValues());
            }

            return kv;
        }
    }
}
