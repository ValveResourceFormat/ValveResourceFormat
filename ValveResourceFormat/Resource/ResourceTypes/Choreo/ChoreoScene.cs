using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.Choreo
{
    public class ChoreoScene
    {
        //These come from outside of the bvcd data
        public string Name { get; set; }
        public int Duration { get; set; }
        public int SoundDuration { get; set; }
        public bool HasSounds { get; set; }

        public byte Version { get; private set; }
        public ChoreoEvent[] Events { get; private set; }
        public ChoreoActor[] Actors { get; private set; }
        public ChoreoCurveData Ramp { get; private set; }
        public bool IgnorePhonemes { get; private set; }

        public ChoreoScene(byte version, ChoreoEvent[] events, ChoreoActor[] actors, ChoreoCurveData ramp, bool ignorePhonemes)
        {
            Version = version;
            Events = events;
            Actors = actors;
            Ramp = ramp;
            IgnorePhonemes = ignorePhonemes;
        }

        public KVObject ToKeyValues()
        {
            var kv = new KVObject(null);

            if (Events.Length > 0)
            {
                var events = new KVObject(null, isArray: true);
                foreach (var choreoEvent in Events)
                {
                    events.AddProperty(null, new KVValue(KVType.OBJECT, choreoEvent.ToKeyValues()));
                }
                kv.AddProperty("events", new KVValue(KVType.ARRAY, events));
            }

            if (Actors.Length > 0)
            {
                var actors = new KVObject(null, isArray: true);
                foreach (var actor in Actors)
                {
                    actors.AddProperty(null, new KVValue(KVType.OBJECT, actor.ToKeyValues()));
                }
                kv.AddProperty("actors", new KVValue(KVType.ARRAY, actors));
            }

            if (Ramp?.LeftEdge != null)
            {
                kv.AddProperty("left_edge", new KVValue(KVType.OBJECT, Ramp.LeftEdge.ToKeyValues()));
            }
            if (Ramp?.RightEdge != null)
            {
                kv.AddProperty("right_edge", new KVValue(KVType.OBJECT, Ramp.RightEdge.ToKeyValues()));
            }
            if (Ramp.Samples.Length > 0)
            {
                kv.AddProperty("scene_ramp", new KVValue(KVType.OBJECT, Ramp.ToKeyValues()));
            }

            kv.AddProperty("ignorePhonemes", new KVValue(KVType.BOOLEAN, IgnorePhonemes));

            return kv;
        }
    }
}
