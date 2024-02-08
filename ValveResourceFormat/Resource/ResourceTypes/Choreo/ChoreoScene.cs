using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.Choreo
{
    public class ChoreoScene
    {
        //These come from outside of the bvcd data
        public string Name { get; set; }
        public byte Version { get; set; }
        public int Duration { get; set; }
        public int SoundDuration { get; set; }
        public int Unk1 { get; set; } //todo: what's this

        public ChoreoEvent[] Events { get; private set; }
        public ChoreoActor[] Actors { get; private set; }
        public ChoreoCurveData Ramp { get; private set; }
        public bool IgnorePhonemes { get; private set; }

        public ChoreoScene(ChoreoEvent[] events, ChoreoActor[] actors, ChoreoCurveData ramp, bool ignorePhonemes)
        {
            Events = events;
            Actors = actors;
            Ramp = ramp;
            IgnorePhonemes = ignorePhonemes;
        }

        public KVObject ToKeyValues()
        {
            var kv = new KVObject(null);


            var actors = new KVObject(null, isArray: true);
            foreach (var actor in Actors)
            {
                actors.AddProperty(null, new KVValue(KVType.OBJECT, actor.ToKeyValues()));
            }
            kv.AddProperty("actors", new KVValue(KVType.ARRAY, actors));


            var events = new KVObject(null, isArray: true);
            foreach (var choreoEvent in Events)
            {
                events.AddProperty(null, new KVValue(KVType.OBJECT, choreoEvent.ToKeyValues()));
            }
            kv.AddProperty("events", new KVValue(KVType.ARRAY, events));


            if (Ramp.Samples.Length > 0)
            {
                kv.AddProperty("scene_ramp", new KVValue(KVType.OBJECT, Ramp.ToKeyValues()));
            }

            kv.AddProperty("ignorePhonemes", new KVValue(KVType.BOOLEAN, IgnorePhonemes));

            return kv;
        }
    }
}