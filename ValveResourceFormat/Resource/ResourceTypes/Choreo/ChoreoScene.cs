using ValveResourceFormat.Serialization.KeyValues;

#nullable disable

namespace ValveResourceFormat.ResourceTypes.Choreo
{
    /// <summary>
    /// Represents a choreography scene.
    /// </summary>
    public class ChoreoScene
    {
        /// <summary>
        /// Gets or sets the name of the scene. This comes from outside of the BVCD data.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets the duration of the scene. This comes from outside of the BVCD data.
        /// </summary>
        public int Duration { get; set; }

        /// <summary>
        /// Gets or sets the sound duration. This comes from outside of the BVCD data.
        /// </summary>
        public int SoundDuration { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the scene has sounds. This comes from outside of the BVCD data.
        /// </summary>
        public bool HasSounds { get; set; }

        /// <summary>
        /// Gets the version of the choreography format.
        /// </summary>
        public byte Version { get; private set; }

        /// <summary>
        /// Gets the events in this scene.
        /// </summary>
        public ChoreoEvent[] Events { get; private set; }

        /// <summary>
        /// Gets the actors in this scene.
        /// </summary>
        public ChoreoActor[] Actors { get; private set; }

        /// <summary>
        /// Gets the scene ramp curve data.
        /// </summary>
        public ChoreoCurveData Ramp { get; private set; }

        /// <summary>
        /// Gets a value indicating whether phonemes should be ignored.
        /// </summary>
        public bool IgnorePhonemes { get; private set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ChoreoScene"/> class.
        /// </summary>
        /// <param name="version">The choreography format version.</param>
        /// <param name="events">The events in this scene.</param>
        /// <param name="actors">The actors in this scene.</param>
        /// <param name="ramp">The scene ramp curve data.</param>
        /// <param name="ignorePhonemes">Whether to ignore phonemes.</param>
        public ChoreoScene(byte version, ChoreoEvent[] events, ChoreoActor[] actors, ChoreoCurveData ramp, bool ignorePhonemes)
        {
            Version = version;
            Events = events;
            Actors = actors;
            Ramp = ramp;
            IgnorePhonemes = ignorePhonemes;
        }

        /// <summary>
        /// Converts this scene to a <see cref="KVObject"/>.
        /// </summary>
        /// <returns>A <see cref="KVObject"/> representing this scene.</returns>
        public KVObject ToKeyValues()
        {
            var kv = new KVObject(null);

            if (Events.Length > 0)
            {
                var events = new KVObject(null, isArray: true);
                foreach (var choreoEvent in Events)
                {
                    events.AddProperty(null, choreoEvent.ToKeyValues());
                }

                kv.AddProperty("events", events);
            }

            if (Actors.Length > 0)
            {
                var actors = new KVObject(null, isArray: true);
                foreach (var actor in Actors)
                {
                    actors.AddItem(actor.ToKeyValues());
                }

                kv.AddProperty("actors", actors);
            }

            if (Ramp?.LeftEdge != null)
            {
                kv.AddProperty("left_edge", Ramp.LeftEdge.ToKeyValues());
            }
            if (Ramp?.RightEdge != null)
            {
                kv.AddProperty("right_edge", Ramp.RightEdge.ToKeyValues());
            }
            if (Ramp.Samples.Length > 0)
            {
                kv.AddProperty("scene_ramp", Ramp.ToKeyValues());
            }

            kv.AddProperty("ignorePhonemes", IgnorePhonemes);

            return kv;
        }
    }
}
